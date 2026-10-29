using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Tableau.Api.Exceptions;
using Tableau.Api.Tests.Fakes;

namespace Tableau.Api.Tests;

[TestClass]
public sealed class ProjectsTests
{
    private const string ProjectsPath = "/api/" + Fixtures.ApiVersion + "/sites/" + Fixtures.SiteId + "/projects";

    [TestMethod]
    public async Task QueryProjectsGetsAPageAndReadsIt()
    {
        using var server = FakeTableauServer.Start();
        var client = Fixtures.Client(server);
        var session = await Fixtures.SignInAsync(server, client);
        server.Map("GET", ProjectsPath, FakeResponse.Xml(200,
            "<pagination pageNumber=\"1\" pageSize=\"100\" totalAvailable=\"2\"/><projects>"
            + "<project id=\"1d0e8a7b-0001-4c2d-9e3f-5a6b7c8d9e0f\" name=\"Sales\" description=\"Quarterly reports\"/>"
            + "<project id=\"1d0e8a7b-0002-4c2d-9e3f-5a6b7c8d9e0f\" name=\"Marketing\"/></projects>"));

        var (pagination, projects) = await client.QueryProjects(session);

        var request = server.Requests.Last();
        Assert.AreEqual($"GET {ProjectsPath}?pageSize=100&pageNumber=1 HTTP/1.1", request.RequestLine);
        Assert.AreEqual(Fixtures.SessionToken, request.Header("X-Tableau-Auth"));
        Assert.AreEqual("application/xml", request.Header("Accept"));
        Assert.AreEqual("2", pagination.totalAvailable);
        CollectionAssert.AreEqual(new[] { "Sales", "Marketing" }, projects.Select(project => project.name).ToArray());
        Assert.AreEqual("Quarterly reports", projects[0].description);
    }

    [TestMethod]
    public async Task AnErrorResponseThrowsWithTableausDetails()
    {
        using var server = FakeTableauServer.Start();
        var client = Fixtures.Client(server);
        var session = await Fixtures.SignInAsync(server, client);
        server.Map("GET", ProjectsPath, FakeResponse.Error(401, "401002", "Unauthorized Access",
            "Invalid authentication credentials were provided."));

        var thrown = await Assert.ThrowsExactlyAsync<TableauRequestException>(() => client.QueryProjects(session));

        Assert.AreEqual(HttpStatusCode.Unauthorized, thrown.StatusCode);
        Assert.IsTrue(thrown.HasDetails);
        Assert.AreEqual("401002", thrown.Details.code);
        Assert.AreEqual("Unauthorized Access", thrown.Details.summary);
    }
}
