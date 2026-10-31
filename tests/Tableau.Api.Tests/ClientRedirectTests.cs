using System;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Tableau.Api.Pipeline;
using Tableau.Api.Rest.Models;
using Tableau.Api.Tests.Fakes;

namespace Tableau.Api.Tests;

/// <summary>
/// The 2.0 client follows a redirect only to the same scheme, host and port, at most five in a row: 307 and 308 with the
/// method and body, 301, 302 and 303 for GET alone. Any other redirect fails with <see cref="TableauErrorKind.Redirect"/>,
/// naming where it pointed, and nothing reaches its target.
/// </summary>
[TestClass]
public sealed class ClientRedirectTests
{
    private const string ItemsPath = "/api/3.25/items";
    private const string MovedPath = "/api/3.25/moved";

    [TestMethod]
    [DataRow(301)]
    [DataRow(302)]
    [DataRow(303)]
    [DataRow(307)]
    [DataRow(308)]
    public async Task AGetIsFollowedToTheSameOriginWithItsToken(int status)
    {
        using var server = FakeTableauServer.Start();
        server.Map("GET", ItemsPath, FakeResponse.Redirect(status, MovedPath + "?k=v&flag"));
        server.Map("GET", MovedPath, ClientFixtures.ServerInfo());
        await using var client = ClientFixtures.Client(server);

        await ClientFixtures.SendAsync(client, ClientFixtures.Endpoint("{version}/items"), new TableauRequest(Token: Fixtures.SessionToken));

        CollectionAssert.AreEqual(new[] { $"GET {ItemsPath} HTTP/1.1", $"GET {MovedPath}?k=v&flag HTTP/1.1" }, ClientFixtures.Lines(server));
        Assert.IsTrue(server.Requests.All(request => request.Header("X-Tableau-Auth") == Fixtures.SessionToken));
    }

    [TestMethod]
    [DataRow(307)]
    [DataRow(308)]
    public async Task A307Or308ResendsTheMethodAndBody(int status)
    {
        using var server = FakeTableauServer.Start();
        server.Map("POST", ItemsPath, FakeResponse.Redirect(status, new Uri(server.BaseUrl, MovedPath).ToString()));
        server.Map("POST", MovedPath, ClientFixtures.ServerInfo());
        await using var client = ClientFixtures.Client(server);
        var body = new TsRequest { Project = new Project { Name = "Invented project" } };

        await ClientFixtures.SendAsync(client, ClientFixtures.Endpoint("{version}/items", "POST"), new TableauRequest(Body: body));

        CollectionAssert.AreEqual(new[] { $"POST {ItemsPath} HTTP/1.1", $"POST {MovedPath} HTTP/1.1" }, ClientFixtures.Lines(server));
        Assert.AreEqual(server.Requests[0].BodyText, server.Requests[1].BodyText);
        Assert.AreEqual("""{"project":{"name":"Invented project"}}""", server.Requests[1].BodyText);
    }

    [TestMethod]
    [DataRow(301)]
    [DataRow(302)]
    [DataRow(303)]
    public async Task A301To303OfAPostIsRefusedSinceItWouldLoseItsBody(int status)
    {
        using var server = FakeTableauServer.Start();
        server.Map("POST", ItemsPath, FakeResponse.Redirect(status, MovedPath));
        server.Map("GET", MovedPath, ClientFixtures.ServerInfo());
        await using var client = ClientFixtures.Client(server);

        var thrown = await ClientFixtures.FailsAsync(TableauErrorKind.Redirect,
            () => ClientFixtures.SendAsync(client, ClientFixtures.Endpoint("{version}/items", "POST")));

        Assert.Contains($"a {status} redirect of a POST, which would lose its body", thrown.Message);
        Assert.AreEqual(status, (int)thrown.StatusCode!);
        Assert.HasCount(1, server.Requests);
    }

    [TestMethod]
    [DataRow(301)]
    [DataRow(302)]
    [DataRow(307)]
    [DataRow(308)]
    public async Task ARedirectToAnotherPortIsRefusedAndNothingReachesIt(int status)
    {
        using var server = FakeTableauServer.Start();
        using var other = FakeTableauServer.Start();
        server.Map("GET", ItemsPath, FakeResponse.Redirect(status, new Uri(other.BaseUrl, ItemsPath).ToString()));
        other.Map("GET", ItemsPath, ClientFixtures.ServerInfo());
        await using var client = ClientFixtures.Client(server);

        var thrown = await ClientFixtures.FailsAsync(TableauErrorKind.Redirect,
            () => ClientFixtures.SendAsync(client, ClientFixtures.Endpoint("{version}/items"), new TableauRequest(Token: Fixtures.SessionToken)));

        Assert.Contains($"a redirect to http://127.0.0.1:{other.BaseUrl.Port}, another origin", thrown.Message);
        Assert.HasCount(1, server.Requests);
        Assert.IsEmpty(other.Requests);
    }

    [TestMethod]
    public async Task ARedirectFromHttpsToHttpIsRefused()
    {
        using var server = FakeTableauServer.StartTls();
        server.Map("GET", ItemsPath, FakeResponse.Redirect(302, $"http://127.0.0.1:{server.BaseUrl.Port}{MovedPath}"));
        var options = ClientFixtures.Options(server);
        options.DangerousAcceptAnyServerCertificate = true;
        await using var client = ClientFixtures.Client(options);

        var thrown = await ClientFixtures.FailsAsync(TableauErrorKind.Redirect,
            () => ClientFixtures.SendAsync(client, ClientFixtures.Endpoint("{version}/items")));

        Assert.Contains($"a redirect to http://127.0.0.1:{server.BaseUrl.Port}, another origin", thrown.Message);
        Assert.HasCount(1, server.Requests);
    }

    [TestMethod]
    public async Task TheMessageNamesOnlyTheTargetsSchemeHostAndPort()
    {
        using var server = FakeTableauServer.Start();
        server.Map("GET", ItemsPath, FakeResponse.Redirect(302, "https://someone:invented-secret@tableau.example.com:8443/next?k=v"));
        await using var client = ClientFixtures.Client(server);

        var thrown = await ClientFixtures.FailsAsync(TableauErrorKind.Redirect,
            () => ClientFixtures.SendAsync(client, ClientFixtures.Endpoint("{version}/items")));

        Assert.Contains("a redirect to https://tableau.example.com:8443, another origin", thrown.Message);
        foreach (var part in new[] { "someone", "invented-secret", "/next", "k=v" })
            Assert.DoesNotContain(part, thrown.Message);
    }

    [TestMethod]
    public async Task FiveRedirectsAreFollowedAndASixthIsRefused()
    {
        using var server = FakeTableauServer.Start();
        for (var i = 0; i < 6; i++)
            server.Map("GET", $"/api/3.25/hop{i}", FakeResponse.Redirect(307, $"/api/3.25/hop{i + 1}"));
        server.Map("GET", "/api/3.25/hop5", ClientFixtures.ServerInfo());
        await using var client = ClientFixtures.Client(server);

        await ClientFixtures.SendAsync(client, ClientFixtures.Endpoint("{version}/hop0"));
        server.Map("GET", "/api/3.25/hop5", FakeResponse.Redirect(307, "/api/3.25/hop6"));
        var thrown = await ClientFixtures.FailsAsync(TableauErrorKind.Redirect,
            () => ClientFixtures.SendAsync(client, ClientFixtures.Endpoint("{version}/hop0")));

        Assert.Contains("more than 5 redirects in a row", thrown.Message);
        Assert.HasCount(12, server.Requests);
    }

    [TestMethod]
    [DataRow(300, "/elsewhere")]
    [DataRow(302, null)]
    public async Task ARedirectWithoutATargetOrOfAnotherKindIsRefused(int status, string? location)
    {
        using var server = FakeTableauServer.Start();
        server.Map("GET", ItemsPath, location is null ? FakeResponse.Empty(status) : FakeResponse.Redirect(status, location));
        await using var client = ClientFixtures.Client(server);

        var thrown = await ClientFixtures.FailsAsync(TableauErrorKind.Redirect,
            () => ClientFixtures.SendAsync(client, ClientFixtures.Endpoint("{version}/items")));

        Assert.Contains(location is null ? "a redirect that names no address" : "a 300 answer, which is no redirect", thrown.Message);
        Assert.HasCount(1, server.Requests);
    }

    [TestMethod]
    public async Task AnHttpClientWhoseHandlerFollowedARedirectElsewhereIsCaught()
    {
        using var server = FakeTableauServer.Start();
        using var other = FakeTableauServer.Start();
        server.Map("GET", ClientFixtures.ServerInfoPath,
            FakeResponse.Redirect(302, new Uri(other.BaseUrl, ClientFixtures.ServerInfoPath).ToString()));
        other.Map("GET", ClientFixtures.ServerInfoPath, ClientFixtures.ServerInfo());
        using var following = new HttpClient();
        await using var client = new TableauClient(following, ClientFixtures.Options(server), Fixtures.Log.Factory);

        var thrown = await ClientFixtures.FailsAsync(TableauErrorKind.Redirect, () => client.Server.GetInfoAsync());

        Assert.Contains($"its HttpClient's handler followed a redirect to http://127.0.0.1:{other.BaseUrl.Port}", thrown.Message);
    }
}
