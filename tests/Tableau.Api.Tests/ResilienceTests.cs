using System;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Tableau.Api.Pipeline;
using Tableau.Api.Tests.Fakes;

namespace Tableau.Api.Tests;

/// <summary>
/// The retries: only for a call that is safe to send twice; for a 408, a 429, a 5xx, a connection that failed or an
/// attempt that took longer than RequestTimeout; the server's wait honoured from Retry-After or a 429's body, up to
/// MaxRetryAfter, and a longer one thrown as RateLimited at once. The waits run on a fake clock.
/// </summary>
[TestClass]
public sealed class ResilienceTests
{
    private const string ItemsPath = "/api/3.25/items";
    private static readonly TimeSpan _step = TimeSpan.FromSeconds(1);

    [TestMethod]
    public async Task AGetRetriedAfterA503Succeeds()
    {
        using var server = FakeTableauServer.Start();
        server.Map("GET", ItemsPath, InTurn(FakeResponse.Empty(503), ClientFixtures.ServerInfo()));
        var time = new FakeTimeProvider();
        var log = new RecordingLogger();
        await using var client = TableauClient.CreateWithClock(ClientFixtures.Options(server, maxRetries: 3), log.Factory, time);

        var call = ClientFixtures.SendAsync(client, Items(), new TableauRequest(Query: ClientFixtures.Query(("filter", "name:eq:ada"))));
        await ClientFixtures.WaitUntilAsync(() => server.Requests.Count == 1);
        await ClientFixtures.AdvanceUntilAsync(time, () => server.Requests.Count == 2, _step, TimeSpan.FromMinutes(1));
        var info = await call;

        Assert.AreEqual("2025.1.0", info.ProductVersion?.Value);
        Assert.IsTrue(log.At(LogLevel.Debug).Any(line => line.Contains($"GET {ItemsPath}?filter=*** failed (503); retry 1 of 3 in ",
            StringComparison.Ordinal)), string.Join("\n", log.Lines));
        Assert.IsEmpty(log.LinesHolding(["name:eq:ada"]).ToList());
    }

    [TestMethod]
    [DataRow(408)]
    [DataRow(429)]
    [DataRow(500)]
    [DataRow(502)]
    [DataRow(503)]
    [DataRow(504)]
    public async Task ATransientStatusIsRetried(int status)
    {
        using var server = FakeTableauServer.Start();
        server.Map("GET", ItemsPath, InTurn(FakeResponse.Empty(status).WithHeader("Retry-After", "0"), ClientFixtures.ServerInfo()));
        await using var client = ClientFixtures.Client(ClientFixtures.Options(server, maxRetries: 3));

        await ClientFixtures.SendAsync(client, Items());

        Assert.HasCount(2, server.Requests);
    }

    [TestMethod]
    [DataRow(400)]
    [DataRow(401)]
    [DataRow(403)]
    [DataRow(404)]
    [DataRow(409)]
    public async Task AnotherStatusIsNotRetried(int status)
    {
        using var server = FakeTableauServer.Start();
        server.Map("GET", ItemsPath, InTurn(FakeResponse.Empty(status).WithHeader("Retry-After", "0"), ClientFixtures.ServerInfo()));
        await using var client = ClientFixtures.Client(ClientFixtures.Options(server, maxRetries: 3));

        var thrown = await Assert.ThrowsExactlyAsync<TableauException>(() => ClientFixtures.SendAsync(client, Items()));

        Assert.AreEqual((HttpStatusCode)status, thrown.StatusCode);
        Assert.HasCount(1, server.Requests);
    }

    [TestMethod]
    [DataRow("POST")]
    [DataRow("PATCH")]
    public async Task ACallThatIsntSafeToSendTwiceIsNotRetried(string method)
    {
        using var server = FakeTableauServer.Start();
        server.Map(method, ItemsPath, InTurn(FakeResponse.Empty(503).WithHeader("Retry-After", "0"), ClientFixtures.ServerInfo()));
        await using var client = ClientFixtures.Client(ClientFixtures.Options(server, maxRetries: 3));

        var thrown = await ClientFixtures.FailsAsync(TableauErrorKind.Http,
            () => ClientFixtures.SendAsync(client, ClientFixtures.Endpoint("{version}/items", method)));

        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, thrown.StatusCode);
        Assert.HasCount(1, server.Requests);
    }

    [TestMethod]
    [DataRow(0, 1)]
    [DataRow(1, 2)]
    [DataRow(3, 4)]
    public async Task MaxRetriesBoundsTheAttempts(int maxRetries, int attempts)
    {
        using var server = FakeTableauServer.Start();
        server.Map("GET", ItemsPath, FakeResponse.Empty(503).WithHeader("Retry-After", "0"));
        await using var client = ClientFixtures.Client(ClientFixtures.Options(server, maxRetries: maxRetries));

        var thrown = await ClientFixtures.FailsAsync(TableauErrorKind.Http, () => ClientFixtures.SendAsync(client, Items()));

        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, thrown.StatusCode);
        Assert.HasCount(attempts, server.Requests);
    }

    [TestMethod]
    public async Task AConnectionThatFailedIsRetried()
    {
        using var server = FakeTableauServer.Start();
        server.Map("GET", ItemsPath, ClientFixtures.ServerInfo());
        var time = new FakeTimeProvider();
        using var primary = new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false };
        using var failing = new FailingFirst(new HttpRequestException(HttpRequestError.ConnectionError, "Invented: the connection was reset."), primary);
        using var http = new HttpClient(failing, disposeHandler: false);
        await using var client = new TableauClient(http, ClientFixtures.Options(server, maxRetries: 3), Fixtures.Log.Factory, time);

        var call = ClientFixtures.SendAsync(client, Items());
        await ClientFixtures.WaitUntilAsync(() => failing.Attempts == 1);
        await ClientFixtures.AdvanceUntilAsync(time, () => server.Requests.Count == 1, _step, TimeSpan.FromMinutes(1));
        await call;

        Assert.AreEqual(2, failing.Attempts);
    }

    [TestMethod]
    public async Task AnAnswerThatBreaksOffIsRetried()
    {
        using var server = FakeTableauServer.Start();
        var broken = FakeResponse.Streamed(200, "application/json", 1000, async stream =>
        {
            await stream.WriteAsync("""{"serverInfo":"""u8.ToArray());
            await stream.FlushAsync();
            throw new System.IO.IOException("Invented: the server stops mid-answer.");
        });
        server.Map("GET", ItemsPath, InTurn(broken, ClientFixtures.ServerInfo()));
        var time = new FakeTimeProvider();
        await using var client = ClientFixtures.Client(ClientFixtures.Options(server, maxRetries: 3), time);

        var call = ClientFixtures.SendAsync(client, Items());
        await ClientFixtures.WaitUntilAsync(() => server.Requests.Count == 1);
        await ClientFixtures.AdvanceUntilAsync(time, () => server.Requests.Count == 2, _step, TimeSpan.FromMinutes(1));
        var info = await call;

        Assert.AreEqual("2025.1.0", info.ProductVersion?.Value);
    }

    [TestMethod]
    public async Task ACallersHttpClientTimeoutIsATimeoutAndNotRetried()
    {
        using var server = FakeTableauServer.Start();
        server.Map("GET", ItemsPath, _ =>
        {
            Thread.Sleep(2000);
            return ClientFixtures.ServerInfo();
        });
        using var primary = new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false };
        using var http = new HttpClient(primary, disposeHandler: false) { Timeout = TimeSpan.FromMilliseconds(300) };
        await using var client = new TableauClient(http, ClientFixtures.Options(server, maxRetries: 3), Fixtures.Log.Factory);

        var thrown = await ClientFixtures.FailsAsync(TableauErrorKind.Timeout, () => ClientFixtures.SendAsync(client, Items()));

        Assert.Contains("took longer than the HttpClient's Timeout allows", thrown.Message);
        Assert.HasCount(1, server.Requests);
    }

    [TestMethod]
    public async Task ACertificateRefusedIsNotRetried()
    {
        using var server = FakeTableauServer.StartTls();
        server.Map("GET", ClientFixtures.ServerInfoPath, ClientFixtures.ServerInfo());
        using var primary = TableauHandler.Create(ClientFixtures.Options(server));
        using var counting = new CountingHandler(primary);
        using var http = new HttpClient(counting, disposeHandler: false);
        await using var client = new TableauClient(http, ClientFixtures.Options(server, maxRetries: 3), Fixtures.Log.Factory);

        var thrown = await ClientFixtures.FailsAsync(TableauErrorKind.Http, () => client.Server.GetInfoAsync());

        Assert.AreEqual(HttpRequestError.SecureConnectionError, ((HttpRequestException)thrown.InnerException!).HttpRequestError);
        Assert.AreEqual(1, counting.Attempts);
    }

    [TestMethod]
    public async Task AnAttemptThatTakesTooLongIsRetried()
    {
        using var server = FakeTableauServer.Start();
        var first = 0;
        server.Map("GET", ItemsPath, _ =>
        {
            if (Interlocked.Increment(ref first) == 1) Thread.Sleep(3000);
            return ClientFixtures.ServerInfo();
        });
        var time = new FakeTimeProvider();
        var options = ClientFixtures.Options(server, maxRetries: 3);
        options.RequestTimeout = TimeSpan.FromSeconds(10);
        await using var client = ClientFixtures.Client(options, time);

        var call = ClientFixtures.SendAsync(client, Items());
        await ClientFixtures.WaitUntilAsync(() => server.Requests.Count == 1);
        var moved = await ClientFixtures.AdvanceUntilAsync(time, () => server.Requests.Count == 2, _step, TimeSpan.FromMinutes(1));
        await call;

        Assert.IsGreaterThanOrEqualTo(options.RequestTimeout, moved);
    }

    [TestMethod]
    public async Task TheLastAttemptThatTakesTooLongIsATimeout()
    {
        using var server = FakeTableauServer.Start();
        server.Map("GET", ItemsPath, _ =>
        {
            Thread.Sleep(3000);
            return ClientFixtures.ServerInfo();
        });
        var time = new FakeTimeProvider();
        var options = ClientFixtures.Options(server, maxRetries: 1);
        options.RequestTimeout = TimeSpan.FromSeconds(10);
        await using var client = ClientFixtures.Client(options, time);

        var call = ClientFixtures.SendAsync(client, Items());
        await ClientFixtures.WaitUntilAsync(() => server.Requests.Count == 1);
        await ClientFixtures.AdvanceUntilAsync(time, () => call.IsCompleted, _step, TimeSpan.FromMinutes(1));
        var thrown = await ClientFixtures.FailsAsync(TableauErrorKind.Timeout, () => call);

        Assert.HasCount(2, server.Requests);
        Assert.Contains("its last attempt took longer than its RequestTimeout allows", thrown.Message);
        Assert.IsInstanceOfType<OperationCanceledException>(thrown.InnerException, "the attempt's cancellation, not Polly's exception");
    }

    [TestMethod]
    public async Task TheCallersCancellationDuringAWaitStaysACancellation()
    {
        using var server = FakeTableauServer.Start();
        server.Map("GET", ItemsPath, FakeResponse.Empty(429).WithHeader("Retry-After", "60"));
        var time = new FakeTimeProvider();
        await using var client = ClientFixtures.Client(ClientFixtures.Options(server, maxRetries: 3), time);
        using var cancel = new CancellationTokenSource();

        var call = ClientFixtures.SendAsync(client, Items(), default, cancel.Token);
        await ClientFixtures.WaitUntilAsync(() => server.Requests.Count == 1);
        await cancel.CancelAsync();
        var thrown = await Assert.ThrowsAsync<OperationCanceledException>(() => call);

        Assert.AreEqual(cancel.Token, thrown.CancellationToken);
        Assert.HasCount(1, server.Requests);
    }

    [TestMethod]
    public async Task A429WithRetryAfterWaitsThatLongOnTheClock()
    {
        using var server = FakeTableauServer.Start();
        server.Map("GET", ItemsPath, InTurn(Limited().WithHeader("Retry-After", "30"), ClientFixtures.ServerInfo()));
        var time = new FakeTimeProvider();
        await using var client = ClientFixtures.Client(ClientFixtures.Options(server, maxRetries: 3), time);

        var call = ClientFixtures.SendAsync(client, Items());
        await ClientFixtures.WaitUntilAsync(() => server.Requests.Count == 1);
        var waited = await ClientFixtures.AdvanceUntilAsync(time, () => server.Requests.Count == 2, _step, TimeSpan.FromMinutes(2));
        await call;

        Assert.IsGreaterThanOrEqualTo(TimeSpan.FromSeconds(30), waited);
        Assert.IsLessThan(TimeSpan.FromSeconds(35), waited);
    }

    [TestMethod]
    public async Task A429WithARetryAfterDateWaitsUntilThen()
    {
        using var server = FakeTableauServer.Start();
        var time = new FakeTimeProvider();
        var until = time.GetUtcNow().AddSeconds(40).ToString("r", CultureInfo.InvariantCulture);
        server.Map("GET", ItemsPath, InTurn(Limited().WithHeader("Retry-After", until), ClientFixtures.ServerInfo()));
        await using var client = ClientFixtures.Client(ClientFixtures.Options(server, maxRetries: 3), time);

        var call = ClientFixtures.SendAsync(client, Items());
        await ClientFixtures.WaitUntilAsync(() => server.Requests.Count == 1);
        var waited = await ClientFixtures.AdvanceUntilAsync(time, () => server.Requests.Count == 2, _step, TimeSpan.FromMinutes(2));
        await call;

        Assert.IsGreaterThanOrEqualTo(TimeSpan.FromSeconds(40), waited);
        Assert.IsLessThan(TimeSpan.FromSeconds(45), waited);
    }

    [TestMethod]
    public async Task A429OverTheCapThrowsRateLimitedAtOnce()
    {
        using var server = FakeTableauServer.Start();
        server.Map("GET", ItemsPath, Limited().WithHeader("Retry-After", "300"));
        var time = new FakeTimeProvider();
        await using var client = ClientFixtures.Client(ClientFixtures.Options(server, maxRetries: 3), time);

        var thrown = await ClientFixtures.FailsAsync(TableauErrorKind.RateLimited, () => ClientFixtures.SendAsync(client, Items()));

        Assert.AreEqual(TimeSpan.FromMinutes(5), thrown.RetryAfter);
        Assert.AreEqual("429000", thrown.ErrorCode);
        Assert.HasCount(1, server.Requests);
    }

    [TestMethod]
    public async Task MaxRetryAfterSetsTheCap()
    {
        using var server = FakeTableauServer.Start();
        server.Map("GET", ItemsPath, Limited().WithHeader("Retry-After", "30"));
        var options = ClientFixtures.Options(server, maxRetries: 3);
        options.MaxRetryAfter = TimeSpan.FromSeconds(10);
        await using var client = ClientFixtures.Client(options, new FakeTimeProvider());

        var thrown = await ClientFixtures.FailsAsync(TableauErrorKind.RateLimited, () => ClientFixtures.SendAsync(client, Items()));

        Assert.AreEqual(TimeSpan.FromSeconds(30), thrown.RetryAfter);
        Assert.HasCount(1, server.Requests);
    }

    // Tableau Cloud's 429, as a caller of Tableau's own Python client reported it (tableau/server-client-python#952):
    // code 429000, no Retry-After, the wait in the detail.
    [TestMethod]
    public async Task A429BodysWaitOverTheCapThrowsRateLimitedWithIt()
    {
        using var server = FakeTableauServer.Start();
        server.Map("GET", ItemsPath, Limited("Too many requests for 'api.rest.refresh_extracts'. Please retry after 29 minutes."));
        await using var client = ClientFixtures.Client(ClientFixtures.Options(server, maxRetries: 3), new FakeTimeProvider());

        var thrown = await ClientFixtures.FailsAsync(TableauErrorKind.RateLimited, () => ClientFixtures.SendAsync(client, Items()));

        Assert.AreEqual(TimeSpan.FromMinutes(29), thrown.RetryAfter);
        Assert.AreEqual("Too Many Requests", thrown.Summary);
        Assert.HasCount(1, server.Requests);
    }

    [TestMethod]
    public async Task A429BodysWaitUnderTheCapIsWaited()
    {
        using var server = FakeTableauServer.Start();
        server.Map("GET", ItemsPath, InTurn(Limited("Too many requests. Please retry after 20 seconds."), ClientFixtures.ServerInfo()));
        var time = new FakeTimeProvider();
        await using var client = ClientFixtures.Client(ClientFixtures.Options(server, maxRetries: 3), time);

        var call = ClientFixtures.SendAsync(client, Items());
        await ClientFixtures.WaitUntilAsync(() => server.Requests.Count == 1);
        var waited = await ClientFixtures.AdvanceUntilAsync(time, () => server.Requests.Count == 2, _step, TimeSpan.FromMinutes(2));
        await call;

        Assert.IsGreaterThanOrEqualTo(TimeSpan.FromSeconds(20), waited);
        Assert.IsLessThan(TimeSpan.FromSeconds(25), waited);
    }

    [TestMethod]
    public async Task A429ToACallThatIsntSafeToSendTwiceThrowsRateLimitedWithItsWait()
    {
        using var server = FakeTableauServer.Start();
        server.Map("POST", ItemsPath, Limited().WithHeader("Retry-After", "5"));
        await using var client = ClientFixtures.Client(ClientFixtures.Options(server, maxRetries: 3), new FakeTimeProvider());

        var thrown = await ClientFixtures.FailsAsync(TableauErrorKind.RateLimited,
            () => ClientFixtures.SendAsync(client, ClientFixtures.Endpoint("{version}/items", "POST")));

        Assert.AreEqual(TimeSpan.FromSeconds(5), thrown.RetryAfter);
        Assert.HasCount(1, server.Requests);
    }

    [TestMethod]
    public async Task A429OnEveryAttemptThrowsRateLimitedOnceTheRetriesRunOut()
    {
        using var server = FakeTableauServer.Start();
        server.Map("GET", ItemsPath, Limited().WithHeader("Retry-After", "0"));
        await using var client = ClientFixtures.Client(ClientFixtures.Options(server, maxRetries: 2));

        var thrown = await ClientFixtures.FailsAsync(TableauErrorKind.RateLimited, () => ClientFixtures.SendAsync(client, Items()));

        Assert.AreEqual(TimeSpan.Zero, thrown.RetryAfter);
        Assert.HasCount(3, server.Requests);
    }

    [TestMethod]
    public async Task A503AskingForMoreThanTheCapIsNotWaitedFor()
    {
        using var server = FakeTableauServer.Start();
        server.Map("GET", ItemsPath, FakeResponse.Empty(503).WithHeader("Retry-After", "3600"));
        await using var client = ClientFixtures.Client(ClientFixtures.Options(server, maxRetries: 3), new FakeTimeProvider());

        var thrown = await ClientFixtures.FailsAsync(TableauErrorKind.Http, () => ClientFixtures.SendAsync(client, Items()));

        Assert.AreEqual(TimeSpan.FromHours(1), thrown.RetryAfter);
        Assert.HasCount(1, server.Requests);
    }

    [TestMethod]
    [DataRow("Too many requests for 'api.rest.refresh_extracts'. Please retry after 29 minutes.", 29 * 60)]
    [DataRow("Please RETRY AFTER 1 minute", 60)]
    [DataRow("retry after 45 secs", 45)]
    [DataRow("retry  after\n2 hours.", 2 * 3600)]
    [DataRow("retry after 3hrs", 3 * 3600)]
    [DataRow("retry after 999999 seconds", 999999)]
    [DataRow("retry after 9999999 seconds", null)]
    [DataRow("retry after a while", null)]
    [DataRow("retry after 5 days", null)]
    [DataRow("retry later", null)]
    [DataRow("", null)]
    [DataRow(null, null)]
    public void TheBodysWaitIsReadLoosely(string? body, int? seconds) =>
        Assert.AreEqual(seconds is null ? null : TimeSpan.FromSeconds(seconds.Value), TableauResilience.WaitInBody(body));

    [TestMethod]
    public void ARequestThePipelineDidntSendCarriesNoEndpoint()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("https://tableau.example.com/"));

        Assert.IsNull(TableauResilience.EndpointOf(null));
        Assert.IsNull(TableauResilience.EndpointOf(request));
    }

    [TestMethod]
    public async Task AnExceptionTheCallItselfThrowsReachesTheCallerAsItIs()
    {
        using var server = FakeTableauServer.Start();
        server.Map("GET", ItemsPath, ClientFixtures.ServerInfo());
        await using var client = ClientFixtures.Client(server);

        var thrown = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => client.Pipeline.SendAsync<string>(Items(), default,
            _ => throw new InvalidOperationException("Invented: the answer's element can't be taken."), "items", CancellationToken.None));

        Assert.AreEqual("Invented: the answer's element can't be taken.", thrown.Message);
    }

    [TestMethod]
    public void AReadsFailureIsTheCallsAndAnotherIsLeftAsItIs()
    {
        using var server = FakeTableauServer.Start();
        using var client = ClientFixtures.Client(server);
        var call = new DownloadCall(client.Pipeline, ClientFixtures.Download("{version}/items"), ItemsPath, CancellationToken.None,
            CancellationToken.None);
        var other = new InvalidOperationException("Invented: not the connection's.");

        var broke = call.Failure(new HttpRequestException("Invented: the connection broke."), 10, CancellationToken.None);

        Assert.AreEqual(TableauErrorKind.Http, ((TableauException)broke).Kind);
        Assert.Contains("broke off after 10 bytes of its file", broke.Message);
        Assert.AreSame(other, call.Failure(other, 10, CancellationToken.None));
    }

    [TestMethod]
    public void AnEndpointsAttemptTimeoutIsRequestTimeoutAndATransfersNone()
    {
        var options = new TableauClientOptions { RequestTimeout = TimeSpan.FromSeconds(7) };

        Assert.AreEqual(TimeSpan.FromSeconds(7), TableauResilience.AttemptTimeout(Items(), options));
        Assert.AreEqual(TimeSpan.FromSeconds(7), TableauResilience.AttemptTimeout(null, options));
        Assert.AreEqual(Timeout.InfiniteTimeSpan, TableauResilience.AttemptTimeout(ClientFixtures.Download("{version}/items"), options));
        Assert.AreEqual(Timeout.InfiniteTimeSpan, TableauResilience.AttemptTimeout(
            ClientFixtures.Endpoint("{version}/items", "POST", transfer: TransferKind.Upload), options));
    }

    private static Endpoint Items() => ClientFixtures.Endpoint("{version}/items");

    /// <summary>Tableau's 429 error body, with the detail given.</summary>
    private static FakeResponse Limited(string detail = "Too many requests.") =>
        FakeResponse.Json(429, $$$"""{"error":{"code":"429000","summary":"Too Many Requests","detail":"{{{detail}}}"}}""");

    /// <summary>A route that answers each request with the next response in turn, and every later one with the last.</summary>
    internal static Func<RecordedRequest, FakeResponse> InTurn(params FakeResponse[] responses)
    {
        var next = -1;
        return _ => responses[Math.Min(Interlocked.Increment(ref next), responses.Length - 1)];
    }

    /// <summary>A handler that fails its first request with the exception given, and sends the rest.</summary>
    private sealed class FailingFirst(Exception exception, HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        private int _attempts;

        public int Attempts => Volatile.Read(ref _attempts);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Interlocked.Increment(ref _attempts) == 1 ? throw exception : base.SendAsync(request, cancellationToken);
    }

    /// <summary>A handler that counts what passes through it to the handler given.</summary>
    private sealed class CountingHandler(HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        private int _attempts;

        public int Attempts => Volatile.Read(ref _attempts);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _attempts);
            return base.SendAsync(request, cancellationToken);
        }
    }
}
