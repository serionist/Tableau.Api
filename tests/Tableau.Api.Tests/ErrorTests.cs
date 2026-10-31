using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Tableau.Api.Pipeline;
using Tableau.Api.Tests.Fakes;

namespace Tableau.Api.Tests;

/// <summary>
/// How a 2.0 call fails: one exception type, <see cref="TableauException"/>, its kind from the status, with Tableau's
/// code, summary and detail when it sent them, the request's method and path with query values hidden, the body cut to
/// 4 KB, and a success answer the client can't read as Tableau's JSON. A timeout is never the caller's cancellation.
/// </summary>
[TestClass]
public sealed class ErrorTests
{
    private const string ItemsPath = "/api/3.25/items";

    [TestMethod]
    [DataRow(400, TableauErrorKind.Http)]
    [DataRow(401, TableauErrorKind.Authentication)]
    [DataRow(403, TableauErrorKind.Http)]
    [DataRow(404, TableauErrorKind.NotFound)]
    [DataRow(409, TableauErrorKind.Http)]
    [DataRow(429, TableauErrorKind.RateLimited)]
    [DataRow(500, TableauErrorKind.Http)]
    public async Task ATableauErrorGivesItsKindAndDetails(int status, TableauErrorKind kind)
    {
        using var server = FakeTableauServer.Start();
        server.Map("GET", ItemsPath, FakeResponse.Json(status,
            $$$"""{"error":{"summary":"Invented summary","detail":"Invented: not these.","code":"{{{status}}}004"}}"""));
        await using var client = ClientFixtures.Client(server);

        var thrown = await ClientFixtures.FailsAsync(kind, () => Send(client, ("filter", "name:eq:ada")));

        Assert.AreEqual((HttpStatusCode)status, thrown.StatusCode);
        Assert.AreEqual($"{status}004", thrown.ErrorCode);
        Assert.AreEqual("Invented summary", thrown.Summary);
        Assert.AreEqual("Invented: not these.", thrown.Detail);
        Assert.AreEqual(HttpMethod.Get, thrown.Method);
        Assert.AreEqual(ItemsPath + "?filter=***", thrown.RequestPath);
        Assert.Contains($"Test call (GET {ItemsPath}?filter=***) answered {status} ", thrown.Message);
        Assert.Contains($"Tableau's error {status}004: Invented summary: Invented: not these.", thrown.Message);
        Assert.DoesNotContain("ada", thrown.Message);
    }

    [TestMethod]
    public async Task ANumericCodeAndTheResourceVersionedShapeAreRead()
    {
        using var server = FakeTableauServer.Start();
        server.Map("GET", ItemsPath, FakeResponse.Json(404, """{"error":{"summary":"Gone","code":404001}}"""));
        server.Map("GET", "/api/-/items", FakeResponse.Json(404, """{"httpErrorCode":404,"message":"Invented: no such item"}"""));
        await using var client = ClientFixtures.Client(server);

        var versioned = await ClientFixtures.FailsAsync(TableauErrorKind.NotFound, () => Send(client));
        var resource = await ClientFixtures.FailsAsync(TableauErrorKind.NotFound,
            () => ClientFixtures.SendAsync(client, ClientFixtures.Endpoint("-/items")));

        Assert.AreEqual("404001", versioned.ErrorCode);
        Assert.IsNull(versioned.Detail);
        Assert.AreEqual("404", resource.ErrorCode);
        Assert.AreEqual("Invented: no such item", resource.Summary);
    }

    [TestMethod]
    [DataRow("""{"error":{"summary":"Invented summary"}}""", null, "Invented summary", ". Tableau's error: Invented summary.")]
    [DataRow("""{"error":{"code":null,"summary":true}}""", null, null, " 400 Bad Request.")]
    [DataRow("""[1,2]""", null, null, " 400 Bad Request.")]
    [DataRow("""{"error":"invented"}""", null, null, " 400 Bad Request.")]
    public async Task AnErrorBodyOfAnotherShapeGivesWhatItHolds(string body, string? code, string? summary, string ending)
    {
        using var server = FakeTableauServer.Start();
        server.Map("GET", ItemsPath, FakeResponse.Json(400, body));
        await using var client = ClientFixtures.Client(server);

        var thrown = await ClientFixtures.FailsAsync(TableauErrorKind.Http, () => Send(client));

        Assert.AreEqual(code, thrown.ErrorCode);
        Assert.AreEqual(summary, thrown.Summary);
        Assert.EndsWith(ending, thrown.Message);
        Assert.AreEqual(body, thrown.ResponseBody);
    }

    [TestMethod]
    [DataRow(new byte[] { 0x22, 0x5C, 0x75, 0x64, 0x38, 0x30, 0x30, 0x22 })]
    [DataRow(new byte[] { 0x22, 0xC3, 0x28, 0x22 })]
    public async Task AnErrorBodyWhoseStringsCantBeReadGivesTheStatusAlone(byte[] code)
    {
        // A lone surrogate written as an escape, and two bytes that are no UTF-8, as the error's code.
        using var server = FakeTableauServer.Start();
        byte[] body = [.. Encoding.ASCII.GetBytes("""{"error":{"summary":"Invented","code":"""), .. code, (byte)'}', (byte)'}'];
        server.Map("GET", ItemsPath, FakeResponse.Bytes(400, "application/json", body));
        await using var client = ClientFixtures.Client(server);

        var thrown = await ClientFixtures.FailsAsync(TableauErrorKind.Http, () => Send(client));

        Assert.IsNull(thrown.ErrorCode);
        Assert.IsNull(thrown.Summary);
        Assert.EndsWith(" 400 Bad Request.", thrown.Message);
        Assert.IsNotNull(thrown.ResponseBody);
    }

    [TestMethod]
    public async Task AnHtmlPageIsCutToFourKilobytesAndGivesNoDetails()
    {
        using var server = FakeTableauServer.Start();
        var page = "<html>" + string.Concat(Enumerable.Repeat("Bad gateway, é. ", 1000)) + "</html>";
        server.Map("GET", ItemsPath, FakeResponse.Content(502, "text/html", page));
        await using var client = ClientFixtures.Client(server);

        var thrown = await ClientFixtures.FailsAsync(TableauErrorKind.Http, () => Send(client));

        Assert.AreEqual(HttpStatusCode.BadGateway, thrown.StatusCode);
        Assert.IsNull(thrown.ErrorCode);
        Assert.IsNull(thrown.Summary);
        Assert.IsTrue(page.StartsWith(thrown.ResponseBody!, StringComparison.Ordinal));
        Assert.IsLessThanOrEqualTo(TableauPipeline.BodyKept, Encoding.UTF8.GetByteCount(thrown.ResponseBody!));
        Assert.IsGreaterThan(TableauPipeline.BodyKept - 4, Encoding.UTF8.GetByteCount(thrown.ResponseBody!));
        Assert.EndsWith(" 502 Bad Gateway.", thrown.Message);
    }

    [TestMethod]
    public async Task AnEmptyErrorBodyGivesTheStatusAlone()
    {
        using var server = FakeTableauServer.Start();
        server.Map("GET", ItemsPath, FakeResponse.Empty(503).WithHeader("X-Request-Id", "invented-request-id"));
        await using var client = ClientFixtures.Client(server);

        var thrown = await ClientFixtures.FailsAsync(TableauErrorKind.Http, () => Send(client));

        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, thrown.StatusCode);
        Assert.IsNull(thrown.ResponseBody);
        Assert.AreEqual("invented-request-id", thrown.RequestId);
    }

    [TestMethod]
    public async Task RetryAfterIsReadAsADelayOrADate()
    {
        using var server = FakeTableauServer.Start();
        server.Map("GET", ItemsPath, FakeResponse.Json(429, """{"error":{"code":"429000"}}""").WithHeader("Retry-After", "146"));
        server.Map("GET", "/api/3.25/dated", FakeResponse.Empty(429)
            .WithHeader("Retry-After", DateTimeOffset.UtcNow.AddMinutes(10).ToString("r", System.Globalization.CultureInfo.InvariantCulture)));
        server.Map("GET", "/api/3.25/past", FakeResponse.Empty(429)
            .WithHeader("Retry-After", DateTimeOffset.UtcNow.AddMinutes(-10).ToString("r", System.Globalization.CultureInfo.InvariantCulture)));
        await using var client = ClientFixtures.Client(server);

        var delta = await ClientFixtures.FailsAsync(TableauErrorKind.RateLimited, () => Send(client));
        var dated = await ClientFixtures.FailsAsync(TableauErrorKind.RateLimited,
            () => ClientFixtures.SendAsync(client, ClientFixtures.Endpoint("{version}/dated")));

        var past = await ClientFixtures.FailsAsync(TableauErrorKind.RateLimited,
            () => ClientFixtures.SendAsync(client, ClientFixtures.Endpoint("{version}/past")));

        Assert.AreEqual(TimeSpan.Zero, past.RetryAfter);
        Assert.AreEqual(TimeSpan.FromSeconds(146), delta.RetryAfter);
        Assert.IsTrue(dated.RetryAfter > TimeSpan.FromMinutes(9) && dated.RetryAfter <= TimeSpan.FromMinutes(10), $"{dated.RetryAfter}");
    }

    [TestMethod]
    public async Task ASuccessWithoutTheElementNamesTheEndpointAndTheElement()
    {
        using var server = FakeTableauServer.Start();
        server.Map("GET", ClientFixtures.ServerInfoPath, FakeResponse.Json(200, """{"site":{"id":"x"}}"""));
        await using var client = ClientFixtures.Client(server);

        var thrown = await ClientFixtures.FailsAsync(TableauErrorKind.InvalidResponse, () => client.Server.GetInfoAsync());

        Assert.AreEqual($"Server Info (GET {ClientFixtures.ServerInfoPath}) answered 200 with no serverInfo, where it returns Tableau's JSON.",
            thrown.Message);
    }

    [TestMethod]
    public async Task ASuccessThatIsNoJsonOrEmptyIsAnInvalidResponse()
    {
        using var server = FakeTableauServer.Start();
        server.Map("GET", ItemsPath, FakeResponse.Content(200, "text/html", "<html>Sign in to the proxy</html>"));
        server.Map("GET", "/api/3.25/empty", FakeResponse.Empty(200));
        await using var client = ClientFixtures.Client(server);

        var html = await ClientFixtures.FailsAsync(TableauErrorKind.InvalidResponse, () => Send(client));
        var empty = await ClientFixtures.FailsAsync(TableauErrorKind.InvalidResponse,
            () => ClientFixtures.SendAsync(client, ClientFixtures.Endpoint("{version}/empty")));

        Assert.IsInstanceOfType<System.Text.Json.JsonException>(html.InnerException);
        Assert.AreEqual("<html>Sign in to the proxy</html>", html.ResponseBody);
        Assert.Contains("a body that isn't Tableau's JSON", html.Message);
        Assert.Contains("answered 200 with no body", empty.Message);
    }

    [TestMethod]
    public async Task ATimeoutIsATimeoutNotTheCallersCancellation()
    {
        using var server = FakeTableauServer.Start();
        server.Map("GET", ItemsPath, _ =>
        {
            Thread.Sleep(2000);
            return ClientFixtures.ServerInfo();
        });
        var options = ClientFixtures.Options(server);
        options.RequestTimeout = TimeSpan.FromMilliseconds(200);
        await using var client = ClientFixtures.Client(options);

        var thrown = await ClientFixtures.FailsAsync(TableauErrorKind.Timeout, () => Send(client));

        Assert.IsInstanceOfType<OperationCanceledException>(thrown.InnerException);
        Assert.Contains("took longer than its RequestTimeout", thrown.Message);
    }

    [TestMethod]
    public async Task ATransferRunsUnderTheTransferTimeout()
    {
        using var server = FakeTableauServer.Start();
        server.Map("GET", ItemsPath, _ =>
        {
            Thread.Sleep(600);
            return ClientFixtures.ServerInfo();
        });
        var options = ClientFixtures.Options(server);
        options.RequestTimeout = TimeSpan.FromMilliseconds(100);
        await using var client = ClientFixtures.Client(options);

        await ClientFixtures.SendAsync(client, ClientFixtures.Endpoint("{version}/items", transfer: TransferKind.Download));
        await using var limited = ClientFixtures.Client(new TableauClientOptions
        {
            ServerUrl = server.BaseUrl,
            AllowInsecureHttp = true,
            ApiVersion = new Version(3, 25),
            TransferTimeout = TimeSpan.FromMilliseconds(100),
        });
        var thrown = await ClientFixtures.FailsAsync(TableauErrorKind.Timeout,
            () => ClientFixtures.SendAsync(limited, ClientFixtures.Endpoint("{version}/items", transfer: TransferKind.Upload)));
        Assert.Contains("took longer than its TransferTimeout", thrown.Message);
    }

    [TestMethod]
    public async Task TheCallersCancellationStaysACancellation()
    {
        using var server = FakeTableauServer.Start();
        server.Map("GET", ItemsPath, _ =>
        {
            Thread.Sleep(2000);
            return ClientFixtures.ServerInfo();
        });
        await using var client = ClientFixtures.Client(server);
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        var thrown = await Assert.ThrowsAsync<OperationCanceledException>(
            () => ClientFixtures.SendAsync(client, ClientFixtures.Endpoint("{version}/items"), default, cancel.Token));

        Assert.AreEqual(cancel.Token, thrown.CancellationToken);
    }

    [TestMethod]
    public async Task NoAnswerIsAnHttpFailureWithItsCause()
    {
        await using var client = ClientFixtures.Client(new TableauClientOptions
        {
            ServerUrl = new Uri($"http://127.0.0.1:{ClientFixtures.ClosedPort()}/"),
            AllowInsecureHttp = true,
            ApiVersion = new Version(3, 25),
        });

        var thrown = await ClientFixtures.FailsAsync(TableauErrorKind.Http, () => client.Server.GetInfoAsync());

        Assert.IsInstanceOfType<HttpRequestException>(thrown.InnerException);
        Assert.IsNull(thrown.StatusCode);
        Assert.Contains($"Server Info (GET {ClientFixtures.ServerInfoPath}) got no answer", thrown.Message);
    }

    [TestMethod]
    public void ATestCanMakeTheExceptionACallThrows()
    {
        var plain = new TableauException();
        var message = new TableauException("Invented failure");
        var inner = new TableauException("Invented failure", new TimeoutException());
        var made = new TableauException(TableauErrorKind.RateLimited, "Invented: slow down")
        {
            StatusCode = HttpStatusCode.TooManyRequests,
            ErrorCode = "429000",
            RetryAfter = TimeSpan.FromSeconds(30),
        };

        Assert.AreEqual(TableauErrorKind.Http, plain.Kind);
        Assert.AreEqual("Invented failure", message.Message);
        Assert.IsInstanceOfType<TimeoutException>(inner.InnerException);
        Assert.AreEqual(TableauErrorKind.RateLimited, made.Kind);
        Assert.AreEqual(TimeSpan.FromSeconds(30), made.RetryAfter);
    }

    private static Task Send(TableauClient client, params (string Key, string? Value)[] query) =>
        ClientFixtures.SendAsync(client, ClientFixtures.Endpoint("{version}/items"), new TableauRequest(Query: ClientFixtures.Query(query)));
}
