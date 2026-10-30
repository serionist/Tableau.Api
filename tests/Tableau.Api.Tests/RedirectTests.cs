using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Tableau.Api.Exceptions;
using Tableau.Api.Tests.Fakes;

namespace Tableau.Api.Tests;

/// <summary>Redirects are not followed: a 3xx fails with its status and where it pointed, and nothing goes there.</summary>
[TestClass]
public sealed class RedirectTests
{
    private const string SignInPath = "/api/" + Fixtures.ApiVersion + "/auth/signin";
    private const string ProjectsPath = "/api/" + Fixtures.ApiVersion + "/sites/" + Fixtures.SiteId + "/projects";
    private const string NoProjects = "<pagination pageNumber=\"1\" pageSize=\"100\" totalAvailable=\"0\"/><projects/>";

    [TestMethod]
    [DataRow(301)]
    [DataRow(302)]
    [DataRow(307)]
    [DataRow(308)]
    public async Task ASignInIsNotRedirected(int status)
    {
        using var server = FakeTableauServer.Start();
        using var other = FakeTableauServer.Start();
        var client = Fixtures.Client(server);
        server.Map("POST", SignInPath, FakeResponse.Redirect(status, new Uri(other.BaseUrl, SignInPath).ToString()));
        other.Map("POST", SignInPath, Fixtures.SignedIn());

        var thrown = await Assert.ThrowsExactlyAsync<TableauRequestException>(
            () => client.SignIn(Fixtures.UserName, Fixtures.Password, Fixtures.SiteContentUrl));

        Assert.AreEqual(status, (int)thrown.StatusCode);
        Assert.Contains($"a redirect to http://127.0.0.1:{other.BaseUrl.Port}, which is not followed", thrown.Message);
        Assert.HasCount(1, server.Requests);
        Assert.IsEmpty(other.Requests);
    }

    [TestMethod]
    [DataRow(301)]
    [DataRow(302)]
    [DataRow(307)]
    [DataRow(308)]
    public async Task ACallWithTheSessionTokenIsNotRedirected(int status)
    {
        using var server = FakeTableauServer.Start();
        using var other = FakeTableauServer.Start();
        var client = Fixtures.Client(server);
        server.Map("GET", ProjectsPath, FakeResponse.Redirect(status, new Uri(other.BaseUrl, ProjectsPath).ToString()));
        other.Map("GET", ProjectsPath, FakeResponse.Xml(200, NoProjects));

        var thrown = await Assert.ThrowsExactlyAsync<TableauRequestException>(() => client.QueryProjects(Fixtures.Session()));

        Assert.AreEqual(status, (int)thrown.StatusCode);
        Assert.AreEqual(Fixtures.SessionToken, server.Requests.Single().Header("X-Tableau-Auth"));
        Assert.IsEmpty(other.Requests);
    }

    [TestMethod]
    [DataRow(301)]
    [DataRow(302)]
    [DataRow(307)]
    [DataRow(308)]
    public async Task AVizportalCallIsNotRedirected(int status)
    {
        using var server = FakeTableauServer.Start();
        using var other = FakeTableauServer.Start();
        var client = Fixtures.Client(server);
        const string Path = Fixtures.VizportalPath + "updateUsersSiteRole";
        server.Map("POST", Path, FakeResponse.Redirect(status, new Uri(other.BaseUrl, Path).ToString()));
        other.Map("POST", Path, Fixtures.VizportalResult());

        var thrown = await Assert.ThrowsExactlyAsync<TableauRequestException>(
            () => client.VizPortal.ChangeUserRole(Fixtures.Session(), 42, "Viewer"));

        Assert.AreEqual(status, (int)thrown.StatusCode);
        Assert.Contains($"a redirect to http://127.0.0.1:{other.BaseUrl.Port}, which is not followed", thrown.Message);
        Assert.HasCount(1, server.Requests);
        Assert.IsEmpty(other.Requests);
    }

    [TestMethod]
    public async Task ARedirectToTheSameHostIsNotFollowedEither()
    {
        using var server = FakeTableauServer.Start();
        var client = Fixtures.Client(server);
        server.Map("GET", ProjectsPath, FakeResponse.Redirect(302, "/elsewhere"));
        server.Map("GET", "/elsewhere", FakeResponse.Xml(200, NoProjects));

        var thrown = await Assert.ThrowsExactlyAsync<TableauRequestException>(() => client.QueryProjects(Fixtures.Session()));

        Assert.Contains($"a redirect to http://127.0.0.1:{server.BaseUrl.Port}, which is not followed", thrown.Message);
        Assert.HasCount(1, server.Requests);
    }

    [TestMethod]
    public async Task TheMessageNamesOnlyTheSchemeHostAndPortARedirectPointsTo()
    {
        using var server = FakeTableauServer.Start();
        var client = Fixtures.Client(server);
        server.Map("GET", ProjectsPath, FakeResponse.Redirect(302, "https://someone:invented-secret@tableau.example.com:8443/next?k=v"));

        var thrown = await Assert.ThrowsExactlyAsync<TableauRequestException>(() => client.QueryProjects(Fixtures.Session()));

        Assert.Contains("a redirect to https://tableau.example.com:8443, which is not followed", thrown.Message);
        foreach (var part in new[] { "someone", "invented-secret", "/next", "k=v" })
            Assert.DoesNotContain(part, thrown.Message);
    }

    [TestMethod]
    public async Task EachRequestCarriesItsOwnSessionsToken()
    {
        using var server = FakeTableauServer.Start();
        var client = Fixtures.Client(server);
        server.Map("GET", ProjectsPath, FakeResponse.Xml(200, NoProjects));
        server.Map("POST", SignInPath, Fixtures.SignedIn());
        var other = Fixtures.Session();
        other.Token = Fixtures.OtherSessionToken;

        await client.QueryProjects(Fixtures.Session());
        await client.QueryProjects(other);
        await client.SignIn(Fixtures.UserName, Fixtures.Password, Fixtures.SiteContentUrl);

        CollectionAssert.AreEqual(new[] { Fixtures.SessionToken, Fixtures.OtherSessionToken, null },
            server.Requests.Select(request => request.Header("X-Tableau-Auth")).ToArray());
    }
}
