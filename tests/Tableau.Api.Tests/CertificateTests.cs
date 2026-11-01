using System;
using System.Linq;
using System.Net.Http;
using System.Security.Authentication;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Tableau.Api.Tests.Fakes;

namespace Tableau.Api.Tests;

/// <summary>
/// Both clients validate the server's certificate, and accept one they can't validate only when IgnoreSSLErrors is set.
/// The server is a TLS fake whose certificate is self-signed and trusted nowhere.
/// </summary>
[TestClass]
public sealed class CertificateTests
{
    private const string DataSourcesPath = "/api/" + Fixtures.ApiVersion + "/sites/" + Fixtures.SiteId + "/datasources";
    private const string NoDataSources = "<pagination pageNumber=\"1\" pageSize=\"100\" totalAvailable=\"0\"/><datasources/>";

    [TestMethod]
    public async Task VizportalRefusesAnUntrustedCertificate()
    {
        using var server = FakeTableauServer.StartTls();
        var client = Fixtures.Client(server);
        server.Map("POST", Fixtures.VizportalPath + "updateUsersSiteRole", Fixtures.VizportalResult());

        var thrown = await Assert.ThrowsAsync<HttpRequestException>(
            () => client.VizPortal.ChangeUserRole(Fixtures.Session(), 42, "Viewer"));

        Assert.IsInstanceOfType<AuthenticationException>(thrown.InnerException);
        Assert.IsEmpty(server.Requests);
    }

    [TestMethod]
    public async Task VizportalConnectsToAnUntrustedCertificateWhenIgnoreSslErrorsIsSet()
    {
        using var server = FakeTableauServer.StartTls();
        var client = Fixtures.Client(server);
        client.IgnoreSSLErrors = true;
        server.Map("POST", Fixtures.VizportalPath + "updateUsersSiteRole", Fixtures.VizportalResult());

        await client.VizPortal.ChangeUserRole(Fixtures.Session(), 42, "Viewer");

        var request = server.Requests.Single();
        Assert.AreEqual($"POST {Fixtures.VizportalPath}updateUsersSiteRole HTTP/1.1", request.RequestLine);
        Assert.Contains($"workgroup_session_id={Fixtures.SessionToken}", request.Header("Cookie") ?? "");
        // Vizportal checks that the XSRF header repeats the XSRF cookie.
        var cookie = (request.Header("Cookie") ?? "").Split(';').Select(part => part.Trim())
            .Single(part => part.StartsWith("XSRF-TOKEN=", StringComparison.Ordinal))["XSRF-TOKEN=".Length..];
        Assert.AreEqual(cookie, request.Header("X-Xsrf-Token"));
    }

    [TestMethod]
    public async Task RestRefusesAnUntrustedCertificate()
    {
        using var server = FakeTableauServer.StartTls();
        var client = Fixtures.Client(server);
        server.Map("GET", DataSourcesPath, FakeResponse.Xml(200, NoDataSources));

        var thrown = await Assert.ThrowsAsync<HttpRequestException>(() => client.QueryDataSources(Fixtures.Session()));

        Assert.IsInstanceOfType<AuthenticationException>(thrown.InnerException);
        Assert.IsEmpty(server.Requests);
    }

    [TestMethod]
    public async Task RestConnectsToAnUntrustedCertificateWhenIgnoreSslErrorsIsSet()
    {
        using var server = FakeTableauServer.StartTls();
        var client = Fixtures.Client(server);
        client.IgnoreSSLErrors = true;
        server.Map("GET", DataSourcesPath, FakeResponse.Xml(200, NoDataSources));

        var (pagination, _) = await client.QueryDataSources(Fixtures.Session());

        Assert.AreEqual("0", pagination.totalAvailable);
        Assert.AreEqual(Uri.UriSchemeHttps, server.BaseUrl.Scheme);
    }
}
