using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Tableau.Api.Exceptions;
using Tableau.Api.Tests.Fakes;
using Tableau.Api.VizPortal.Models;

namespace Tableau.Api.Tests;

/// <summary>
/// Vizportal's paging, by start index and by token, reads every page; and each way Vizportal reports a failure reaches
/// the caller as a TableauRequestException, with Vizportal's code when it sent one.
/// </summary>
[TestClass]
public sealed class VizportalRequestTests
{
    [TestMethod]
    public async Task PagesByStartIndexAreReadToTheLast()
    {
        using var server = FakeTableauServer.Start();
        var client = Fixtures.Client(server);
        // A full first page, so the next start index is the number of items read, as it must be whatever the page size.
        var projectsOnFirstPage = string.Join(",",
            Enumerable.Range(1, 100).Select(id => $$"""{"id":"{{id}}","name":"Project {{id}}"}"""));
        var firstPage = $$$"""{"result":{"projects":[{{{projectsOnFirstPage}}}],"moreItems":true,"totalCount":101}}""";
        server.Map("POST", Fixtures.VizportalPath + "getProjects", request =>
            (long?)Params(request)["page"]?["startIndex"] == 0
                ? FakeResponse.Json(200, firstPage)
                : FakeResponse.Json(200, """{"result":{"projects":[{"id":"101","name":"Last"}],"moreItems":false}}"""));

        var projects = new List<Project>();
        await foreach (var project in client.VizPortal.QueryProjects(Fixtures.Session()))
            projects.Add(project);

        Assert.HasCount(101, projects);
        Assert.AreEqual("Last", projects[^1].Name);
        CollectionAssert.AreEqual(new long?[] { 0, 100 },
            server.Requests.Select(request => (long?)Params(request)["page"]?["startIndex"]).ToArray());
        Assert.IsTrue(server.Requests.All(request => (long?)Params(request)["page"]?["maxItems"] == 100));
    }

    [TestMethod]
    public async Task PagesByTokenAreReadToTheLast()
    {
        using var server = FakeTableauServer.Start();
        var client = Fixtures.Client(server);
        server.Map("POST", Fixtures.VizportalPath + "getSiteUsersWithToken", request =>
            (string?)Params(request)["pageWithToken"]?["token"] is null
                ? FakeResponse.Json(200, """
                    {"result":{"users":[{"id":"1","userName":"ada"}],"moreItems":true,
                     "nextPageToken":"invented-page-2"}}
                    """)
                : FakeResponse.Json(200, """{"result":{"users":[{"id":"2","userName":"grace"}],"moreItems":false}}"""));

        var users = new List<User>();
        await foreach (var user in client.VizPortal.QueryUsers(Fixtures.Session()))
            users.Add(user);

        CollectionAssert.AreEqual(new[] { "ada", "grace" }, users.Select(user => user.UserName).ToArray());
        CollectionAssert.AreEqual(new[] { null, "invented-page-2" },
            server.Requests.Select(request => (string?)Params(request)["pageWithToken"]?["token"]).ToArray());
    }

    [TestMethod]
    [DataRow(200, """{"result":{"errors":[{"code":403}]}}""", "403", "Tableau VizPortal API threw code 403",
        DisplayName = "an error in a 200")]
    [DataRow(200, """{"result":{"errors":[{"code":-1}]}}""", "-1", "Tableau VizPortal API threw code -1",
        DisplayName = "a negative code, the same in every culture")]
    [DataRow(500, """{"result":{"errors":[{"code":1}]}}""", "1", "Tableau VizPortal API threw code 1",
        DisplayName = "an error in a 500")]
    [DataRow(200, """{"result":{"errors":{}}}""", "100", "Tableau VizPortal API threw an unknown error",
        DisplayName = "errors that are no list")]
    [DataRow(200, """{"answer":{}}""", "100", "VizPortal response invalid", DisplayName = "no result")]
    [DataRow(500, "<html>Proxy error</html>", null, null, DisplayName = "a 500 that is no JSON")]
    public async Task AFailureIsATableauRequestException(int status, string body, string? code, string? summary)
    {
        using var server = FakeTableauServer.Start();
        var client = Fixtures.Client(server);
        server.Map("POST", Fixtures.VizportalPath + "deleteUsers",
            FakeResponse.Content(status, "application/json", body));

        var thrown = await Assert.ThrowsExactlyAsync<TableauRequestException>(
            () => client.VizPortal.DeleteUser(Fixtures.Session(), 42));

        Assert.AreEqual((HttpStatusCode)status, thrown.StatusCode);
        Assert.AreEqual(code, thrown.Details?.code);
        Assert.AreEqual(summary, thrown.Details?.summary);
        Assert.AreEqual(code is not null, thrown.HasDetails);
    }

    private static JsonObject Params(RecordedRequest request) =>
        JsonNode.Parse(request.BodyText)!["params"]!.AsObject();
}
