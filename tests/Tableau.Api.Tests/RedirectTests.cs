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
    private const string SubscriptionsPath = "/api/" + Fixtures.ApiVersion + "/sites/" + Fixtures.SiteId + "/subscriptions";
    private const string FileUploadsPath = "/api/" + Fixtures.ApiVersion + "/sites/" + Fixtures.SiteId + "/fileUploads";
    private const string NoSubscriptions = "<pagination pageNumber=\"1\" pageSize=\"100\" totalAvailable=\"0\"/><subscriptions/>";

    [TestMethod]
    [DataRow(301)]
    [DataRow(302)]
    [DataRow(307)]
    [DataRow(308)]
    public async Task APostIsNotRedirected(int status)
    {
        using var server = FakeTableauServer.Start();
        using var other = FakeTableauServer.Start();
        var client = Fixtures.Client(server);
        server.Map("POST", FileUploadsPath, FakeResponse.Redirect(status, new Uri(other.BaseUrl, FileUploadsPath).ToString()));
        other.Map("POST", FileUploadsPath, FakeResponse.Xml(201, "<fileUpload uploadSessionId=\"invented-upload\" fileSize=\"0\"/>"));

        var thrown = await Assert.ThrowsExactlyAsync<TableauRequestException>(
            () => client.InitiateFileUpload(Fixtures.Session()));

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
        server.Map("GET", SubscriptionsPath, FakeResponse.Redirect(status, new Uri(other.BaseUrl, SubscriptionsPath).ToString()));
        other.Map("GET", SubscriptionsPath, FakeResponse.Xml(200, NoSubscriptions));

        var thrown = await Assert.ThrowsExactlyAsync<TableauRequestException>(() => client.QuerySubscriptions(Fixtures.Session()));

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
        server.Map("GET", SubscriptionsPath, FakeResponse.Redirect(302, "/elsewhere"));
        server.Map("GET", "/elsewhere", FakeResponse.Xml(200, NoSubscriptions));

        var thrown = await Assert.ThrowsExactlyAsync<TableauRequestException>(() => client.QuerySubscriptions(Fixtures.Session()));

        Assert.Contains($"a redirect to http://127.0.0.1:{server.BaseUrl.Port}, which is not followed", thrown.Message);
        Assert.HasCount(1, server.Requests);
    }

    [TestMethod]
    public async Task TheMessageNamesOnlyTheSchemeHostAndPortARedirectPointsTo()
    {
        using var server = FakeTableauServer.Start();
        var client = Fixtures.Client(server);
        server.Map("GET", SubscriptionsPath, FakeResponse.Redirect(302, "https://someone:invented-secret@tableau.example.com:8443/next?k=v"));

        var thrown = await Assert.ThrowsExactlyAsync<TableauRequestException>(() => client.QuerySubscriptions(Fixtures.Session()));

        Assert.Contains("a redirect to https://tableau.example.com:8443, which is not followed", thrown.Message);
        foreach (var part in new[] { "someone", "invented-secret", "/next", "k=v" })
            Assert.DoesNotContain(part, thrown.Message);
    }

    [TestMethod]
    public async Task EachRequestCarriesItsOwnSessionsToken()
    {
        using var server = FakeTableauServer.Start();
        var client = Fixtures.Client(server);
        server.Map("GET", SubscriptionsPath, FakeResponse.Xml(200, NoSubscriptions));
        var other = Fixtures.Session();
        other.Token = Fixtures.OtherSessionToken;

        await client.QuerySubscriptions(Fixtures.Session());
        await client.QuerySubscriptions(other);
        await client.QuerySubscriptions(Fixtures.Session());

        CollectionAssert.AreEqual(new[] { Fixtures.SessionToken, Fixtures.OtherSessionToken, Fixtures.SessionToken },
            server.Requests.Select(request => request.Header("X-Tableau-Auth")).ToArray());
    }
}
