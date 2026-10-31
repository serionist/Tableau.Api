using System;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Tableau.Api.Tests.Fakes;

namespace Tableau.Api.Tests;

/// <summary>
/// Sessions the caller holds: <c>client.Sessions</c> signs in, switches site and signs out; a client from
/// <c>WithSession</c> calls in one and never signs in; and a session whose token a caller's handler sent elsewhere is
/// signed out.
/// </summary>
[TestClass]
public sealed class SessionApiTests
{
    private const string Other = "other";

    [TestMethod]
    public async Task SignInTakesTheOptionsSiteOrTheOneGiven()
    {
        using var server = FakeTableauServer.Start();
        server.Map("POST", SessionFixtures.SignInPath, request => request.BodyText.Contains("\"other\"", StringComparison.Ordinal)
            ? FakeResponse.Json(200, $$$"""{"credentials":{"site":{"id":"{{{Fixtures.SiteId}}}","contentUrl":"other"},"user":{"id":"{{{Fixtures.UserId}}}"},"token":"t"}}""")
            : SessionFixtures.SignedIn());
        await using var client = SessionFixtures.Client(SessionFixtures.Options(server), null);

        var finance = await client.Sessions.SignInAsync(SessionFixtures.Pat());
        var other = await client.Sessions.SignInAsync(SessionFixtures.Pat(), Other);
        var fallback = await client.Sessions.SignInAsync(SessionFixtures.Pat(), "");

        Assert.AreEqual(Fixtures.SiteContentUrl, finance.SiteContentUrl);
        Assert.AreEqual(Other, other.SiteContentUrl);
        Assert.Contains("\"contentUrl\":\"\"", server.Requests[2].BodyText, "\"\" is the default site");
        Assert.AreEqual(Fixtures.SiteContentUrl, fallback.SiteContentUrl, "the site the answer names");
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(() => client.Sessions.SignInAsync(null!));
    }

    [TestMethod]
    public async Task SwitchSiteSendsTheSessionsTokenAndTheSiteAndKeepsTheName()
    {
        using var server = FakeTableauServer.Start();
        server.Map("POST", SessionFixtures.SignInPath, SessionFixtures.SignedIn());
        server.Map("POST", SessionFixtures.SwitchSitePath, FakeResponse.Json(200,
            $$$"""{"credentials":{"site":{"id":"9a8b7c6d-5e4f-4a3b-8c2d-1e0f9a8b7c6d","contentUrl":"other"},"user":{"id":"{{{Fixtures.UserId}}}"},"token":"{{{Fixtures.OtherSessionToken}}}"}}"""));
        await using var client = SessionFixtures.Client(SessionFixtures.Options(server), null);
        var session = await client.Sessions.SignInAsync(SessionFixtures.Pat());

        var switched = await client.Sessions.SwitchSiteAsync(session, Other);

        var request = server.Requests[^1];
        Assert.AreEqual($"POST {SessionFixtures.SwitchSitePath} HTTP/1.1", request.RequestLine);
        Assert.AreEqual(Fixtures.SessionToken, request.Header("X-Tableau-Auth"));
        Assert.IsNull(JsonEquivalence.Difference("""{"site":{"contentUrl":"other"}}""", request.BodyText));
        Assert.AreEqual("9a8b7c6d-5e4f-4a3b-8c2d-1e0f9a8b7c6d", switched.SiteId);
        Assert.AreEqual(Other, switched.SiteContentUrl);
        Assert.AreEqual(SessionFixtures.PatName, switched.UserName, "the name the session came from signed in with");
        Assert.AreEqual(Fixtures.OtherSessionToken, switched.Token);
    }

    [TestMethod]
    public async Task SwitchSiteIsRefusedOnTableauCloudBeforeSending()
    {
        using var server = FakeTableauServer.Start();
        var options = SessionFixtures.Options(server);
        options.Deployment = TableauDeployment.Cloud;
        await using var client = SessionFixtures.Client(options, null);

        var thrown = await ClientFixtures.FailsAsync(TableauErrorKind.UnsupportedDeployment,
            () => client.Sessions.SwitchSiteAsync(Signed(TableauDeployment.Cloud), Other));

        Assert.Contains("Switch Site doesn't exist on Tableau Cloud", thrown.Message);
        Assert.IsEmpty(server.Requests);
    }

    [TestMethod]
    public async Task SignOutEndsTheSessionGivenAndAnEndedOneIsAuthentication()
    {
        using var server = FakeTableauServer.Start();
        server.Map("POST", SessionFixtures.SignOutPath, request =>
            request.Header("X-Tableau-Auth") == Fixtures.SessionToken ? FakeResponse.Empty(204) : SessionFixtures.Unauthorized());
        await using var client = SessionFixtures.Client(SessionFixtures.Options(server), null);

        await client.Sessions.SignOutAsync(Signed());
        await ClientFixtures.FailsAsync(TableauErrorKind.Authentication,
            () => client.Sessions.SignOutAsync(Signed() with { Token = Fixtures.OtherSessionToken }));

        Assert.AreEqual($"POST {SessionFixtures.SignOutPath} HTTP/1.1", server.Requests[0].RequestLine);
        Assert.IsEmpty(server.Requests[0].Body);
    }

    [TestMethod]
    public async Task ASessionNoSignInMadeIsRefused()
    {
        await using var client = SessionFixtures.Client(new TableauClientOptions { ServerUrl = new Uri("https://tableau.example.com/") }, null);
        var made = Signed() with { Token = null };

        Assert.ThrowsExactly<ArgumentException>(() => client.WithSession(made));
        Assert.ThrowsExactly<ArgumentNullException>(() => client.WithSession(null!));
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => client.Sessions.SignOutAsync(made));
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => client.Sessions.SwitchSiteAsync(made, Other));
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(() => client.Sessions.SwitchSiteAsync(Signed(), null!));
    }

    [TestMethod]
    public async Task ABoundClientCallsInItsSessionAcrossItsSubClientsAndNeverSignsIn()
    {
        using var server = FakeTableauServer.Start();
        var signIns = SessionFixtures.MapSignIns(server, Fixtures.SessionToken);
        server.Map("GET", SessionFixtures.ItemsPath, ClientFixtures.ServerInfo());
        server.Map("GET", ClientFixtures.ServerInfoPath, ClientFixtures.ServerInfo());
        server.Map("POST", SessionFixtures.SignOutPath, FakeResponse.Empty(204));
        await using var client = SessionFixtures.Client(SessionFixtures.Options(server), SessionFixtures.Password());
        var session = await client.Sessions.SignInAsync(SessionFixtures.Pat());

        await using (var bound = client.WithSession(session))
        {
            await ClientFixtures.SendAsync(bound, SessionFixtures.Items);
            await bound.Server.GetInfoAsync();
            await bound.Sessions.SignOutAsync(session);
        }

        Assert.AreEqual(1, signIns.Count, "the explicit sign-in alone");
        CollectionAssert.AreEqual(new[] { Fixtures.SessionToken, null, Fixtures.SessionToken },
            server.Requests.Skip(1).Select(request => request.Header("X-Tableau-Auth")).ToArray(), "Server Info needs no token");
        Assert.AreEqual(SessionFixtures.ItemsPath, server.Requests[1].Path, "the session's site fills the path");
        // The client still works, and has its own session to sign in.
        await ClientFixtures.SendAsync(client, SessionFixtures.Items);
        Assert.AreEqual(2, signIns.Count);
    }

    [TestMethod]
    public async Task ABoundClientsRefusedSessionIsAuthenticationAndNothingSignsIn()
    {
        using var server = FakeTableauServer.Start();
        var signIns = SessionFixtures.MapSignIns(server, Fixtures.SessionToken);
        server.Map("GET", SessionFixtures.ItemsPath, SessionFixtures.Unauthorized());
        await using var client = SessionFixtures.Client(SessionFixtures.Options(server), SessionFixtures.Pat());
        await using var bound = client.WithSession(Signed());

        var thrown = await ClientFixtures.FailsAsync(TableauErrorKind.Authentication, () => ClientFixtures.SendAsync(bound, SessionFixtures.Items));

        Assert.AreEqual(0, signIns.Count);
        Assert.HasCount(1, server.Requests);
        Assert.Contains("The session given to WithSession has ended or was signed out", thrown.Message);
    }

    [TestMethod]
    public async Task DisposingTheClientEndsItsBoundClientsAndNotTheReverse()
    {
        using var server = FakeTableauServer.Start();
        server.Map("GET", SessionFixtures.ItemsPath, ClientFixtures.ServerInfo());
        var client = SessionFixtures.Client(SessionFixtures.Options(server), null);
        var bound = client.WithSession(Signed());
        var again = bound.WithSession(Signed() with { Token = Fixtures.OtherSessionToken });

        bound.Dispose();
        await Assert.ThrowsExactlyAsync<ObjectDisposedException>(() => ClientFixtures.SendAsync(bound, SessionFixtures.Items));
        await ClientFixtures.SendAsync(again, SessionFixtures.Items);
        await client.DisposeAsync();
        await Assert.ThrowsExactlyAsync<ObjectDisposedException>(() => ClientFixtures.SendAsync(again, SessionFixtures.Items));

        Assert.AreEqual(Fixtures.OtherSessionToken, server.Requests.Single().Header("X-Tableau-Auth"));
    }

    [TestMethod]
    public async Task ATokenACallersHandlerSentElsewhereIsSignedOutAndTheClientSignsInAgain()
    {
        using var server = FakeTableauServer.Start();
        using var elsewhere = FakeTableauServer.Start();
        var signIns = SessionFixtures.MapSignIns(server, Fixtures.SessionToken, Fixtures.OtherSessionToken);
        server.Map("GET", SessionFixtures.ItemsPath, request => request.Header("X-Tableau-Auth") == Fixtures.SessionToken
            ? FakeResponse.Redirect(302, new Uri(elsewhere.BaseUrl, SessionFixtures.ItemsPath).ToString())
            : ClientFixtures.ServerInfo());
        server.Map("POST", SessionFixtures.SignOutPath, FakeResponse.Empty(204));
        elsewhere.Map("GET", SessionFixtures.ItemsPath, ClientFixtures.ServerInfo());
        using var following = new SocketsHttpHandler { AllowAutoRedirect = true, UseCookies = false };
        using var http = new HttpClient(following, disposeHandler: false);
        await using var client = new TableauClient(http, SessionFixtures.Options(server), SessionFixtures.Pat(), Fixtures.Log.Factory);

        var thrown = await ClientFixtures.FailsAsync(TableauErrorKind.Redirect, () => ClientFixtures.SendAsync(client, SessionFixtures.Items));
        await ClientFixtures.SendAsync(client, SessionFixtures.Items);

        Assert.Contains("The session whose token it carried is signed out.", thrown.Message);
        Assert.AreEqual(Fixtures.SessionToken, elsewhere.Requests.Single().Header("X-Tableau-Auth"), "the handler sent it there");
        Assert.AreEqual(Fixtures.SessionToken, SessionFixtures.To(server, SessionFixtures.SignOutPath).First().Header("X-Tableau-Auth"));
        Assert.AreEqual(2, signIns.Count, "the client signs in again");
    }

    [TestMethod]
    public async Task ASignOutOfASentTokenThatFailsSaysSo()
    {
        using var server = FakeTableauServer.Start();
        using var elsewhere = FakeTableauServer.Start();
        server.Map("GET", SessionFixtures.ItemsPath, FakeResponse.Redirect(302, new Uri(elsewhere.BaseUrl, SessionFixtures.ItemsPath).ToString()));
        server.Map("POST", SessionFixtures.SignOutPath, FakeResponse.Redirect(307, new Uri(elsewhere.BaseUrl, SessionFixtures.SignOutPath).ToString()));
        elsewhere.Map("GET", SessionFixtures.ItemsPath, ClientFixtures.ServerInfo());
        elsewhere.Map("POST", SessionFixtures.SignOutPath, FakeResponse.Empty(204));
        using var following = new SocketsHttpHandler { AllowAutoRedirect = true, UseCookies = false };
        using var http = new HttpClient(following, disposeHandler: false);
        await using var client = new TableauClient(http, SessionFixtures.Options(server), loggerFactory: Fixtures.Log.Factory);

        var thrown = await ClientFixtures.FailsAsync(TableauErrorKind.Redirect,
            () => ClientFixtures.SendAsync(client, SessionFixtures.Items, new Pipeline.TableauRequest(Token: Fixtures.SessionToken, SiteId: Fixtures.SiteId)));

        Assert.Contains("couldn't be signed out", thrown.Message);
        Assert.HasCount(1, SessionFixtures.To(elsewhere, SessionFixtures.SignOutPath), "one sign-out, not one for each redirect");
    }

    [TestMethod]
    public async Task AMockMadeByTheProtectedConstructorsThrowsUntilOverridden()
    {
        await using var client = new MockClient();
        var sessions = new MockSessions();

        Assert.ThrowsExactly<InvalidOperationException>(() => client.Sessions);
        Assert.ThrowsExactly<InvalidOperationException>(() => client.WithSession(Signed()));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => sessions.SignInAsync(SessionFixtures.Pat()));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => sessions.SignOutAsync(Signed()));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => sessions.SwitchSiteAsync(Signed(), Other));
    }

    [TestMethod]
    public async Task ASignInWhoseAnswerNamesNoContentUrlKeepsTheSiteAskedFor()
    {
        using var server = FakeTableauServer.Start();
        server.Map("POST", SessionFixtures.SignInPath, FakeResponse.Json(200,
            $$$"""{"credentials":{"site":{"id":"{{{Fixtures.SiteId}}}"},"user":{"id":"{{{Fixtures.UserId}}}"},"token":"t"}}"""));
        await using var client = SessionFixtures.Client(SessionFixtures.Options(server), null);

        var session = await client.Sessions.SignInAsync(SessionFixtures.Pat(), Other);

        Assert.AreEqual(Other, session.SiteContentUrl);
    }

    /// <summary>A session as a sign-in in the invented site gives it.</summary>
    private static TableauSession Signed(TableauDeployment deployment = TableauDeployment.Server) => new()
    {
        SiteId = Fixtures.SiteId,
        SiteContentUrl = Fixtures.SiteContentUrl,
        UserId = Fixtures.UserId,
        UserName = SessionFixtures.PatName,
        ExpiresAt = DateTimeOffset.UtcNow.AddHours(4),
        Deployment = deployment,
        ApiVersion = new Version(3, 25),
        Token = Fixtures.SessionToken,
    };

    private sealed class MockClient : TableauClient;

    private sealed class MockSessions : SessionsClient;
}
