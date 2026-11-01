using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Tableau.Api.Rest.Models;
using Tableau.Api.Tests.Fakes;

namespace Tableau.Api.Tests;

/// <summary>
/// <see cref="ProjectsClient"/>: each call's method, path, query and body as the reference gives them, and its answer
/// read from a fixture, on Tableau Server and Tableau Cloud alike.
/// </summary>
[TestClass]
public sealed class ProjectsTests
{
    private const string Projects = AreaFixtures.SitePath + "/projects";

    [TestMethod]
    [DataRow(TableauDeployment.Server)]
    [DataRow(TableauDeployment.Cloud)]
    public async Task ListPageSortsByAStableOrderUnlessToldAndReadsTheProjects(TableauDeployment deployment)
    {
        using var server = AreaFixtures.Server();
        server.Map("GET", Projects, FakeResponse.Fixture(200, "projects-page.json"));
        await using var client = AreaFixtures.Client(server, deployment);

        var page = await client.Projects.ListPageAsync(1);

        AreaFixtures.Sent(server, $"GET {Projects}?pageSize=100&pageNumber=1&sort=createdAt:asc,name:asc");
        Assert.AreEqual((1, 3, 5), (page.PageNumber, page.PageSize, page.TotalAvailable));
        CollectionAssert.AreEqual(new[] { "Default", "Finance", "Quarterly reports" }, page.Items.Select(project => project.Name).ToArray());
        Assert.AreEqual(page.Items[1].Id, page.Items[2].ParentProjectId);
    }

    [TestMethod]
    public async Task ListPageSendsTheFilterTheSortAndThePageGiven()
    {
        using var server = AreaFixtures.Server();
        server.Map("GET", Projects, FakeResponse.Fixture(200, "projects-empty.json"));
        await using var client = AreaFixtures.Client(server);

        var page = await client.Projects.ListPageAsync(3, new ProjectListOptions
        {
            Filter = TableauFilter.And(TableauFilter.Eq(ProjectField.ParentProjectId, AreaFixtures.ProjectId), TableauFilter.Eq(ProjectField.TopLevelProject, false)),
            Sort = TableauSort.Desc(ProjectField.UpdatedAt),
            PageSize = 1000,
        });

        AreaFixtures.Sent(server, $"GET {Projects}?pageSize=1000&pageNumber=3&filter=parentProjectId:eq:{AreaFixtures.ProjectId},topLevelProject:eq:false"
            + "&sort=updatedAt:desc");
        Assert.IsEmpty(page.Items);
        Assert.AreEqual(0, page.TotalAvailable);
    }

    [TestMethod]
    public async Task ListReadsEveryPageUntilTheTotal()
    {
        using var server = AreaFixtures.Server();
        server.Map("GET", Projects, request => FakeResponse.Fixture(200,
            request.Query.Contains("pageNumber=1", StringComparison.Ordinal) ? "projects-page.json" : "projects-last-page.json"));
        await using var client = AreaFixtures.Client(server);

        var projects = await AreaFixtures.AllAsync(client.Projects.ListAsync(new ProjectListOptions { PageSize = 3 }));

        CollectionAssert.AreEqual(new[] { "Default", "Finance", "Quarterly reports", "R&D", "C++ #1 100%" },
            projects.Select(project => project.Name).ToArray());
        CollectionAssert.AreEqual(new[] { "1", "2" },
            AreaFixtures.Calls(server).Select(call => call.QueryParameters.Single(pair => pair.Key == "pageNumber").Value).ToArray());
    }

    [TestMethod]
    public async Task CreateSendsTheProjectOnceAndReadsIt()
    {
        using var server = AreaFixtures.Server();
        server.Map("POST", Projects, FakeResponse.Fixture(201, "create-project.json"));
        await using var client = AreaFixtures.Client(server, TableauDeployment.Cloud);
        var project = ModelFixtures.Request("create-project-request.json").Project!;

        var made = await client.Projects.CreateAsync(project);

        AreaFixtures.Sent(server, $"POST {Projects}", ModelFixtures.Write(new TsRequest { Project = project }));
        ModelFixtures.AssertSameJson(ModelFixtures.Text("create-project-request.json"), AreaFixtures.Calls(server)[0].BodyText);
        Assert.AreEqual(AreaFixtures.ProjectId, made.Id);
    }

    [TestMethod]
    public async Task CreateAsksForTheSamplesOnlyWhenTold()
    {
        using var server = AreaFixtures.Server();
        server.Map("POST", Projects, FakeResponse.Fixture(201, "create-project.json"));
        await using var client = AreaFixtures.Client(server);

        await client.Projects.CreateAsync(new Project { Name = "Samples" }, publishSamples: true);

        AreaFixtures.Sent(server, $"POST {Projects}?publishSamples=true", """{"project":{"name":"Samples"}}""");
    }

    [TestMethod]
    public async Task UpdateSendsOnlyWhatIsSet()
    {
        using var server = AreaFixtures.Server();
        server.Map("PUT", $"{Projects}/{AreaFixtures.ProjectId}", FakeResponse.Fixture(200, "create-project.json"));
        await using var client = AreaFixtures.Client(server);

        var project = await client.Projects.UpdateAsync(AreaFixtures.ProjectId, new Project
        {
            Name = "Quarterly reports",
            Description = "R&D, C++ #1 100%",
            ParentProjectId = "",
        });

        AreaFixtures.Sent(server, $"PUT {Projects}/{AreaFixtures.ProjectId}",
            """{"project":{"description":"R&D, C++ #1 100%","name":"Quarterly reports","parentProjectId":""}}""");
        Assert.AreEqual("Invented nested project", project.Description);
    }

    [TestMethod]
    public async Task DeleteSendsADeleteToTheProject()
    {
        using var server = AreaFixtures.Server();
        server.Map("DELETE", $"{Projects}/{AreaFixtures.ProjectId}", FakeResponse.Empty(204));
        await using var client = AreaFixtures.Client(server);

        await client.Projects.DeleteAsync(AreaFixtures.ProjectId);

        AreaFixtures.Sent(server, $"DELETE {Projects}/{AreaFixtures.ProjectId}");
    }

    [TestMethod]
    public async Task ARefusedArgumentSendsNothing()
    {
        using var server = AreaFixtures.Server();
        await using var client = AreaFixtures.Client(server);

        await AreaFixtures.RefusedAsync<ArgumentNullException>(server, () => client.Projects.CreateAsync(null!));
        Assert.AreEqual("project", (await AreaFixtures.RefusedAsync<ArgumentException>(server, () => client.Projects.CreateAsync(new Project()))).ParamName);
        Assert.AreEqual("id", (await AreaFixtures.RefusedAsync<ArgumentException>(server, () => client.Projects.UpdateAsync("finance", new Project()))).ParamName);
        await AreaFixtures.RefusedAsync<ArgumentNullException>(server, () => client.Projects.UpdateAsync(AreaFixtures.ProjectId, null!));
        Assert.AreEqual("id", (await AreaFixtures.RefusedAsync<ArgumentException>(server, () => client.Projects.DeleteAsync(""))).ParamName);
    }
}
