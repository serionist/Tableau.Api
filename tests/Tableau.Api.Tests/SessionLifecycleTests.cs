using System;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Tableau.Api.Tests.Fakes;

namespace Tableau.Api.Tests;

/// <summary>
/// A client's own session: signed in by the first call that needs one, once however many calls ask at once; signed in
/// again before it is due to end, and once after a 401, the refused call sent again; signed out when the client that
/// owns it is disposed, and never by a client from DI.
/// </summary>
[TestClass]
public sealed class SessionLifecycleTests
{
    private const string First = "invented-first-session-token";
    private const string Second = "invented-second-session-token";

    [TestMethod]
    public async Task TwentyConcurrentFirstCallsMakeOneSignIn()
    {
        using var server = FakeTableauServer.Start();
        var signIns = new SessionFixtures.Counter();
        server.Map("POST", SessionFixtures.SignInPath, _ =>
        {
            signIns.Next();
            // Slow, so every call is waiting while it runs.
            Thread.Sleep(200);
            return SessionFixtures.SignedIn();
        });
        server.Map("GET", SessionFixtures.ItemsPath, ClientFixtures.ServerInfo());
        await using var client = SessionFixtures.Client(SessionFixtures.Options(server), SessionFixtures.Pat());

        await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => ClientFixtures.SendAsync(client, SessionFixtures.Items)));

        Assert.AreEqual(1, signIns.Count);
        var calls = SessionFixtures.To(server, SessionFixtures.ItemsPath);
        Assert.HasCount(20, calls);
        Assert.IsTrue(calls.All(call => call.Header("X-Tableau-Auth") == Fixtures.SessionToken));
    }

    [TestMethod]
    public async Task TwentyConcurrentCallsShareOneRefusedSignIn()
    {
        using var server = FakeTableauServer.Start();
        var signIns = new SessionFixtures.Counter();
        server.Map("POST", SessionFixtures.SignInPath, _ =>
        {
            signIns.Next();
            Thread.Sleep(200);
            return SessionFixtures.Refused();
        });
        await using var client = SessionFixtures.Client(SessionFixtures.Options(server), SessionFixtures.Password());

        var calls = Enumerable.Range(0, 20).Select(_ => ClientFixtures.FailsAsync(TableauErrorKind.Authentication,
            () => ClientFixtures.SendAsync(client, SessionFixtures.Items))).ToList();
        await Task.WhenAll(calls);

        Assert.AreEqual(1, signIns.Count, "a wrong password is tried once, not once a call, which a lockout would count");
        Assert.IsEmpty(SessionFixtures.To(server, SessionFixtures.ItemsPath));

        // A call made after the failure tries again.
        await ClientFixtures.FailsAsync(TableauErrorKind.Authentication, () => ClientFixtures.SendAsync(client, SessionFixtures.Items));
        Assert.AreEqual(2, signIns.Count);
    }

    [TestMethod]
    public async Task A401SignsInAgainOnceAndSendsTheCallAgain()
    {
        using var server = FakeTableauServer.Start();
        var signIns = SessionFixtures.MapSignIns(server, First, Second);
        server.Map("GET", SessionFixtures.ItemsPath, request =>
            request.Header("X-Tableau-Auth") == First ? SessionFixtures.Unauthorized() : ClientFixtures.ServerInfo());
        await using var client = SessionFixtures.Client(SessionFixtures.Options(server), SessionFixtures.Pat());

        await ClientFixtures.SendAsync(client, SessionFixtures.Items);
        await ClientFixtures.SendAsync(client, SessionFixtures.Items);

        Assert.AreEqual(2, signIns.Count);
        CollectionAssert.AreEqual(new[] { First, Second, Second },
            SessionFixtures.To(server, SessionFixtures.ItemsPath).Select(call => call.Header("X-Tableau-Auth")).ToArray());
    }

    [TestMethod]
    public async Task ACallWithABodyIsSentAgainWithIt()
    {
        using var server = FakeTableauServer.Start();
        SessionFixtures.MapSignIns(server, First, Second);
        server.Map("PUT", SessionFixtures.ItemsPath, request =>
            request.Header("X-Tableau-Auth") == First ? SessionFixtures.Unauthorized() : ClientFixtures.ServerInfo());
        await using var client = SessionFixtures.Client(SessionFixtures.Options(server), SessionFixtures.Pat());
        var update = SessionFixtures.Items with { Method = HttpMethod.Put };
        var body = new Rest.Models.TsRequest { Project = new Rest.Models.Project { Name = "Invented project" } };

        await ClientFixtures.SendAsync(client, update, new Pipeline.TableauRequest(Body: body));

        var calls = SessionFixtures.To(server, SessionFixtures.ItemsPath);
        Assert.HasCount(2, calls);
        Assert.IsTrue(calls.All(call => call.BodyText.Contains("Invented project", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task A401AfterSigningInAgainIsAuthentication()
    {
        using var server = FakeTableauServer.Start();
        var signIns = SessionFixtures.MapSignIns(server, First, Second);
        server.Map("GET", SessionFixtures.ItemsPath, SessionFixtures.Unauthorized());
        await using var client = SessionFixtures.Client(SessionFixtures.Options(server), SessionFixtures.Pat());

        var thrown = await ClientFixtures.FailsAsync(TableauErrorKind.Authentication, () => ClientFixtures.SendAsync(client, SessionFixtures.Items));

        Assert.AreEqual(2, signIns.Count);
        Assert.HasCount(2, SessionFixtures.To(server, SessionFixtures.ItemsPath));
        Assert.Contains("signed in again and the server refused the new session too", thrown.Message);
        Assert.AreEqual("401002", thrown.ErrorCode);
    }

    [TestMethod]
    public async Task TwoCallsRefusedTogetherSignInAgainOnce()
    {
        using var server = FakeTableauServer.Start();
        var signIns = SessionFixtures.MapSignIns(server, First, Second);
        server.Map("GET", SessionFixtures.ItemsPath, request =>
            request.Header("X-Tableau-Auth") == First ? SessionFixtures.Unauthorized() : ClientFixtures.ServerInfo());
        await using var client = SessionFixtures.Client(SessionFixtures.Options(server), SessionFixtures.Pat());
        await client.Pipeline.State.Sessions.SessionAsync(client.Pipeline, SessionFixtures.Items, default);

        await Task.WhenAll(ClientFixtures.SendAsync(client, SessionFixtures.Items), ClientFixtures.SendAsync(client, SessionFixtures.Items));

        Assert.AreEqual(2, signIns.Count, "the second refused call takes the session the first signed in");
    }

    [TestMethod]
    public async Task ASessionDueToEndIsReplacedBeforeTheCall()
    {
        using var server = FakeTableauServer.Start();
        var signIns = SessionFixtures.MapSignIns(server, First, Second);
        server.Map("GET", SessionFixtures.ItemsPath, ClientFixtures.ServerInfo());
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 31, 12, 0, 0, TimeSpan.Zero));
        await using var client = SessionFixtures.Client(SessionFixtures.Options(server), SessionFixtures.Pat(), time);

        await ClientFixtures.SendAsync(client, SessionFixtures.Items);
        time.Advance(TimeSpan.FromMinutes(234));
        await ClientFixtures.SendAsync(client, SessionFixtures.Items);
        time.Advance(TimeSpan.FromMinutes(2));
        await ClientFixtures.SendAsync(client, SessionFixtures.Items);

        Assert.AreEqual(2, signIns.Count, "renewed five minutes before its 240 are up");
        CollectionAssert.AreEqual(new[] { First, First, Second },
            SessionFixtures.To(server, SessionFixtures.ItemsPath).Select(call => call.Header("X-Tableau-Auth")).ToArray());
        CollectionAssert.AreEqual(new[] { "POST", "GET", "GET", "POST", "GET" }, server.Requests.Select(request => request.Method).ToArray(),
            "the sign-in comes before the call");
    }

    [TestMethod]
    public async Task ASessionWhoseTokenExpiresSoonerIsReplacedSooner()
    {
        using var server = FakeTableauServer.Start();
        var signIns = new SessionFixtures.Counter();
        server.Map("POST", SessionFixtures.SignInPath, _ => SessionFixtures.SignedIn(signIns.Next() == 1 ? First : Second, "0:01:00"));
        server.Map("GET", SessionFixtures.ItemsPath, ClientFixtures.ServerInfo());
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 31, 12, 0, 0, TimeSpan.Zero));
        await using var client = SessionFixtures.Client(SessionFixtures.Options(server), SessionFixtures.Pat(), time);

        await ClientFixtures.SendAsync(client, SessionFixtures.Items);
        time.Advance(TimeSpan.FromMinutes(56));
        await ClientFixtures.SendAsync(client, SessionFixtures.Items);

        Assert.AreEqual(2, signIns.Count);
    }

    [TestMethod]
    public async Task ASessionWhoseTokenIsAboutToExpireIsReplacedAtHalfItsLife()
    {
        using var server = FakeTableauServer.Start();
        var signIns = new SessionFixtures.Counter();
        server.Map("POST", SessionFixtures.SignInPath, _ => SessionFixtures.SignedIn(signIns.Next() == 1 ? First : Second, "0:00:08"));
        server.Map("GET", SessionFixtures.ItemsPath, ClientFixtures.ServerInfo());
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 31, 12, 0, 0, TimeSpan.Zero));
        await using var client = SessionFixtures.Client(SessionFixtures.Options(server), SessionFixtures.Pat(), time);

        await ClientFixtures.SendAsync(client, SessionFixtures.Items);
        time.Advance(TimeSpan.FromMinutes(3));
        await ClientFixtures.SendAsync(client, SessionFixtures.Items);
        time.Advance(TimeSpan.FromMinutes(2));
        await ClientFixtures.SendAsync(client, SessionFixtures.Items);

        Assert.AreEqual(2, signIns.Count, "an eight-minute session is renewed after four, not five before its end");
    }

    [TestMethod]
    public async Task ADownloadInASessionSignsInAgainAfterA401()
    {
        using var server = FakeTableauServer.Start();
        var signIns = SessionFixtures.MapSignIns(server, First, Second);
        const string ContentPath = "/api/" + ClientFixtures.Version + "/sites/" + Fixtures.SiteId + "/content";
        server.Map("GET", ContentPath, request => request.Header("X-Tableau-Auth") == First
            ? SessionFixtures.Unauthorized()
            : FakeResponse.File([(byte)'P', (byte)'K', 3, 4], "application/octet-stream", ("Content-Disposition", "name=\"tableau_workbook\"; filename=\"Sales.twbx\"")));
        await using var client = SessionFixtures.Client(SessionFixtures.Options(server), SessionFixtures.Pat());
        var content = ClientFixtures.Endpoint("{version}/sites/{siteId}/content", transfer: Pipeline.TransferKind.Download, needsSession: true);

        await using var download = await ClientFixtures.DownloadAsync(client, content);

        Assert.AreEqual("Sales.twbx", download.FileName);
        Assert.AreEqual(2, signIns.Count);
        CollectionAssert.AreEqual(new[] { First, Second }, SessionFixtures.To(server, ContentPath).Select(call => call.Header("X-Tableau-Auth")).ToArray());
    }

    [TestMethod]
    public async Task ADownloadRefusedAgainAfterSigningInAgainIsAuthentication()
    {
        using var server = FakeTableauServer.Start();
        var signIns = SessionFixtures.MapSignIns(server, First, Second);
        server.Map("GET", "/api/" + ClientFixtures.Version + "/sites/" + Fixtures.SiteId + "/content", SessionFixtures.Unauthorized());
        await using var client = SessionFixtures.Client(SessionFixtures.Options(server), SessionFixtures.Pat());
        var content = ClientFixtures.Endpoint("{version}/sites/{siteId}/content", transfer: Pipeline.TransferKind.Download, needsSession: true);

        var thrown = await ClientFixtures.FailsAsync(TableauErrorKind.Authentication, () => ClientFixtures.DownloadAsync(client, content));

        Assert.AreEqual(2, signIns.Count);
        Assert.Contains("refused the new session too", thrown.Message);
    }

    [TestMethod]
    public async Task AnUploadsRefusedSessionIsNotSentAgain()
    {
        using var server = FakeTableauServer.Start();
        var signIns = SessionFixtures.MapSignIns(server, First, Second);
        const string UploadPath = "/api/" + ClientFixtures.Version + "/sites/" + Fixtures.SiteId + "/uploads";
        server.Map("POST", UploadPath, SessionFixtures.Unauthorized());
        await using var client = SessionFixtures.Client(SessionFixtures.Options(server), SessionFixtures.Pat());
        var upload = ClientFixtures.Endpoint("{version}/sites/{siteId}/uploads", "POST", transfer: Pipeline.TransferKind.Upload, needsSession: true);

        var thrown = await ClientFixtures.FailsAsync(TableauErrorKind.Authentication, () => ClientFixtures.SendAsync(client, upload));

        Assert.AreEqual(1, signIns.Count, "an upload's body may not be readable twice");
        Assert.HasCount(1, SessionFixtures.To(server, UploadPath));
        Assert.DoesNotContain("signed in again", thrown.Message);
    }

    [TestMethod]
    public async Task DisposingAClientSignsItsSessionOut()
    {
        using var server = FakeTableauServer.Start();
        SessionFixtures.MapSignIns(server, First);
        server.Map("GET", SessionFixtures.ItemsPath, ClientFixtures.ServerInfo());
        server.Map("POST", SessionFixtures.SignOutPath, FakeResponse.Empty(204));
        var asynchronous = SessionFixtures.Client(SessionFixtures.Options(server), SessionFixtures.Pat());
        var synchronous = SessionFixtures.Client(SessionFixtures.Options(server), SessionFixtures.Password());
        await ClientFixtures.SendAsync(asynchronous, SessionFixtures.Items);
        await ClientFixtures.SendAsync(synchronous, SessionFixtures.Items);

        await asynchronous.DisposeAsync();
        synchronous.Dispose();
        await asynchronous.DisposeAsync();
        synchronous.Dispose();

        var signOuts = SessionFixtures.To(server, SessionFixtures.SignOutPath);
        Assert.HasCount(2, signOuts, "each once");
        Assert.IsTrue(signOuts.All(signOut => signOut.Header("X-Tableau-Auth") == First));
        await Assert.ThrowsExactlyAsync<ObjectDisposedException>(() => ClientFixtures.SendAsync(asynchronous, SessionFixtures.Items));
        await Assert.ThrowsExactlyAsync<ObjectDisposedException>(() => ClientFixtures.SendAsync(synchronous, SessionFixtures.Items));
    }

    [TestMethod]
    public async Task AClientOverACallersHttpClientSignsItsSessionOutAndLeavesTheHttpClientOpen()
    {
        using var server = FakeTableauServer.Start();
        SessionFixtures.MapSignIns(server, First);
        server.Map("GET", SessionFixtures.ItemsPath, ClientFixtures.ServerInfo());
        server.Map("POST", SessionFixtures.SignOutPath, FakeResponse.Empty(204));
        using var handler = new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false };
        using var http = new HttpClient(handler, disposeHandler: false);

        await using (var client = new TableauClient(http, SessionFixtures.Options(server), SessionFixtures.Pat(), Fixtures.Log.Factory))
            await ClientFixtures.SendAsync(client, SessionFixtures.Items);

        Assert.HasCount(1, SessionFixtures.To(server, SessionFixtures.SignOutPath));
        using var after = await http.GetAsync(new Uri(server.BaseUrl, SessionFixtures.ItemsPath));
    }

    [TestMethod]
    public async Task ASignOutThatFailsOnDisposeThrowsNothing()
    {
        using var server = FakeTableauServer.Start();
        SessionFixtures.MapSignIns(server, First);
        server.Map("GET", SessionFixtures.ItemsPath, ClientFixtures.ServerInfo());
        server.Map("POST", SessionFixtures.SignOutPath, FakeResponse.Json(500, """{"error":{"code":"500000","summary":"Invented"}}"""));
        var refused = SessionFixtures.Client(SessionFixtures.Options(server), SessionFixtures.Pat());
        var unreachable = SessionFixtures.Client(SessionFixtures.Options(server), SessionFixtures.Pat());
        await ClientFixtures.SendAsync(refused, SessionFixtures.Items);
        await ClientFixtures.SendAsync(unreachable, SessionFixtures.Items);

        await refused.DisposeAsync();
        server.Dispose();
        unreachable.Dispose();

        Assert.HasCount(1, SessionFixtures.To(server, SessionFixtures.SignOutPath));
    }

    [TestMethod]
    public async Task AClientThatNeverSignedInOrSignedInOnlyWhenToldSignsNothingOut()
    {
        using var server = FakeTableauServer.Start();
        SessionFixtures.MapSignIns(server, First);
        server.Map("GET", ClientFixtures.ServerInfoPath, ClientFixtures.ServerInfo());

        await using (var client = SessionFixtures.Client(SessionFixtures.Options(server), SessionFixtures.Pat()))
            await client.Server.GetInfoAsync();
        await using (var client = SessionFixtures.Client(SessionFixtures.Options(server), null))
            await client.Sessions.SignInAsync(SessionFixtures.Pat());

        CollectionAssert.AreEqual(new[] { ClientFixtures.ServerInfoPath, SessionFixtures.SignInPath },
            server.Requests.Select(request => request.Path).ToArray(), "Server Info needs no session, and an explicit session is the caller's");
    }

    [TestMethod]
    public async Task AClientWithoutCredentialsRefusesACallThatNeedsASessionBeforeSending()
    {
        using var server = FakeTableauServer.Start();
        await using var client = SessionFixtures.Client(SessionFixtures.Options(server), null);

        var thrown = await ClientFixtures.FailsAsync(TableauErrorKind.Authentication, () => ClientFixtures.SendAsync(client, SessionFixtures.Items));

        Assert.Contains("needs a session, and the client has no credentials", thrown.Message);
        Assert.IsEmpty(server.Requests);
    }

    [TestMethod]
    public async Task ACallRefusedForItsArgumentsIsRefusedBeforeASignIn()
    {
        using var server = FakeTableauServer.Start();
        await using var client = SessionFixtures.Client(SessionFixtures.Options(server), SessionFixtures.Pat());
        var item = ClientFixtures.Endpoint("{version}/sites/{siteId}/items/{itemId:luid}", needsSession: true);

        await Assert.ThrowsExactlyAsync<ArgumentException>(() => ClientFixtures.SendAsync(client, item, new Pipeline.TableauRequest(PathValues: ["not-a-luid"])));

        Assert.IsEmpty(server.Requests);
    }

    [TestMethod]
    public async Task ATokenOfTheCallsOwnFillsTheSiteItIsGivenOrNone()
    {
        using var server = FakeTableauServer.Start();
        server.Map("GET", SessionFixtures.ItemsPath, ClientFixtures.ServerInfo());
        await using var client = SessionFixtures.Client(SessionFixtures.Options(server), null);

        await ClientFixtures.SendAsync(client, SessionFixtures.Items, new Pipeline.TableauRequest(Token: First, SiteId: Fixtures.SiteId));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            ClientFixtures.SendAsync(client, SessionFixtures.Items, new Pipeline.TableauRequest(Token: First)));

        Assert.AreEqual(First, server.Requests.Single().Header("X-Tableau-Auth"));
    }

    [TestMethod]
    public async Task ClientsOfOneNameShareOneSessionAndNoneSignsItOut()
    {
        using var server = FakeTableauServer.Start();
        var signIns = SessionFixtures.MapSignIns(server, First);
        server.Map("GET", SessionFixtures.ItemsPath, ClientFixtures.ServerInfo());
        server.Map("POST", SessionFixtures.SignOutPath, FakeResponse.Empty(204));
        var services = new ServiceCollection();
        services.AddSingleton(Fixtures.Log.Factory);
        services.AddTableauClient("prod", options => Copy(SessionFixtures.Options(server), options)).UseCredentials(SessionFixtures.Pat());

        await using (var provider = services.BuildServiceProvider())
        {
            for (var i = 0; i < 3; i++)
            {
                await using var scope = provider.CreateAsyncScope();
                await ClientFixtures.SendAsync(scope.ServiceProvider.GetRequiredKeyedService<TableauClient>("prod"), SessionFixtures.Items);
            }
            var root = provider.GetRequiredKeyedService<TableauClient>("prod");
            await ClientFixtures.SendAsync(root, SessionFixtures.Items);
            root.Dispose();
        }

        Assert.AreEqual(1, signIns.Count);
        Assert.HasCount(4, SessionFixtures.To(server, SessionFixtures.ItemsPath));
        Assert.IsEmpty(SessionFixtures.To(server, SessionFixtures.SignOutPath), "neither a transient nor the container signs the name's session out");
    }

    [TestMethod]
    public async Task CredentialsFromTheContainerAreMadeOnceAndTheLastGivenWin()
    {
        using var server = FakeTableauServer.Start();
        SessionFixtures.MapSignIns(server, First);
        server.Map("GET", SessionFixtures.ItemsPath, ClientFixtures.ServerInfo());
        var made = new SessionFixtures.Counter();
        var services = new ServiceCollection();
        services.AddSingleton(new InventedVault(Fixtures.Password));
        services.AddTableauClient("prod", options => Copy(SessionFixtures.Options(server), options))
            .UseCredentials(SessionFixtures.Pat())
            .UseCredentials(provider =>
            {
                made.Next();
                return TableauCredentials.Password(Fixtures.UserName, provider.GetRequiredService<InventedVault>().SecretAsync);
            });
        services.AddTableauClient("broken", options => Copy(SessionFixtures.Options(server), options)).UseCredentials(_ => null!);
        services.AddTableauClient(options => Copy(SessionFixtures.Options(server), options)).UseCredentials(_ => null!);
        await using var provider = services.BuildServiceProvider();

        await ClientFixtures.SendAsync(provider.GetRequiredKeyedService<TableauClient>("prod"), SessionFixtures.Items);
        await ClientFixtures.SendAsync(provider.GetRequiredKeyedService<TableauClient>("prod"), SessionFixtures.Items);
        var thrown = Assert.ThrowsExactly<InvalidOperationException>(() => provider.GetRequiredKeyedService<TableauClient>("broken"));
        var unnamed = Assert.ThrowsExactly<InvalidOperationException>(() => provider.GetRequiredService<TableauClient>());

        Assert.AreEqual(1, made.Count);
        Assert.Contains("\"name\":\"ada\"", SessionFixtures.To(server, SessionFixtures.SignInPath).Single().BodyText);
        Assert.Contains("'broken'", thrown.Message);
        Assert.Contains("'(default)'", unnamed.Message);
        Assert.ThrowsExactly<ArgumentNullException>(() => services.AddTableauClient("x", _ => { }).UseCredentials((TableauCredentials)null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => services.AddTableauClient("x", _ => { }).UseCredentials((Func<IServiceProvider, TableauCredentials>)null!));
    }

    private static void Copy(TableauClientOptions from, TableauClientOptions to)
    {
        to.ServerUrl = from.ServerUrl;
        to.Site = from.Site;
        to.ApiVersion = from.ApiVersion;
        to.AllowInsecureHttp = from.AllowInsecureHttp;
        to.RequestTimeout = from.RequestTimeout;
        to.MaxRetries = from.MaxRetries;
    }

    /// <summary>A vault of the caller's, which gives a secret when asked.</summary>
    private sealed class InventedVault(string secret)
    {
        public ValueTask<string> SecretAsync(CancellationToken cancellationToken) =>
            cancellationToken.IsCancellationRequested ? ValueTask.FromCanceled<string>(cancellationToken) : ValueTask.FromResult(secret);
    }
}
