using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Tableau.Api.Rest.Models;
using Tableau.Api.Tests.Fakes;

namespace Tableau.Api.Tests;

/// <summary>
/// <see cref="WorkbooksClient"/> against the fake server: each call's method, path, query, body and answer; lists paged
/// in a stable order; updates that send what is set and no empty element (F32); revisions paged (F7); the version gates.
/// The files' streaming is <see cref="ExportTests"/>'.
/// </summary>
[TestClass]
public sealed class WorkbooksTests
{
    private const string Site = AreaFixtures.SitePath;
    private const string Id = AreaFixtures.ItemId;
    private const string Workbook = Site + "/workbooks/" + Id;
    private const string ConnectionId = "3c4d5e6f-0001-4a7b-8c9d-0e1f2a3b4c5d";

    /// <summary>Each workbook call above REST API 1.0: the version the reference gives it, its descriptor's name, and the call.</summary>
    public static IEnumerable<object[]> Gated =>
    [
        ["2.3", "Query Workbooks for Site", (Func<TableauClient, Task>)(client => client.Workbooks.ListPageAsync(1))],
        ["2.0", "Get Workbook", (Func<TableauClient, Task>)(client => client.Workbooks.GetAsync(Id))],
        ["2.0", "Query Workbook Connections", (Func<TableauClient, Task>)(client => AreaFixtures.AllAsync(client.Workbooks.ListConnectionsAsync(Id)))],
        ["2.0", "Update Workbook", (Func<TableauClient, Task>)(client => client.Workbooks.UpdateAsync(Id, new WorkbookUpdate()))],
        ["2.0", "Update Workbook Connection",
            (Func<TableauClient, Task>)(client => client.Workbooks.UpdateConnectionAsync(Id, ConnectionId, new ConnectionUpdate()))],
        ["2.8", "Update Workbook Now", (Func<TableauClient, Task>)(client => client.Workbooks.RunExtractRefreshAsync(Id))],
        ["2.0", "Delete Workbook", (Func<TableauClient, Task>)(client => client.Workbooks.DeleteAsync(Id))],
        ["2.0", "Download Workbook", (Func<TableauClient, Task>)(client => client.Workbooks.DownloadAsync(Id))],
        ["3.4", "Download Workbook PDF", (Func<TableauClient, Task>)(client => client.Workbooks.DownloadPdfAsync(Id))],
        ["3.23", "Download Workbook PDF with view filters", (Func<TableauClient, Task>)(client => client.Workbooks.DownloadPdfAsync(Id,
            new ExportOptions { Filters = new Dictionary<string, string> { ["Region"] = "West" } }))],
        ["3.26", "Download Workbook PDF at a size", (Func<TableauClient, Task>)(client => client.Workbooks.DownloadPdfAsync(Id,
            new ExportOptions { Width = 800, Height = 600, Filters = new Dictionary<string, string> { ["Region"] = "West" } }))],
        ["2.3", "Get Workbook Revisions", (Func<TableauClient, Task>)(client => client.Workbooks.ListRevisionsPageAsync(Id, 1))],
        ["2.3", "Download Workbook Revision", (Func<TableauClient, Task>)(client => client.Workbooks.DownloadRevisionAsync(Id, 2))],
        ["2.0", "Remove Workbook Revision", (Func<TableauClient, Task>)(client => client.Workbooks.RemoveRevisionAsync(Id, 2))],
    ];

    [TestMethod]
    public async Task WorkbooksAreListedPageByPageInAStableOrder()
    {
        using var server = AreaFixtures.Server();
        server.Map("GET", $"{Site}/workbooks", request => AreaFixtures.PageNumber(request) == 1
            ? FakeResponse.Fixture(200, "workbooks-page.json")
            : AreaFixtures.Page("workbooks", "workbook", 2, 2, 3, 1));
        await using var client = AreaFixtures.Client(server);

        var workbooks = await AreaFixtures.AllAsync(client.Workbooks.ListAsync(new WorkbookListOptions { PageSize = 2 }));

        CollectionAssert.AreEqual(new[]
        {
            $"GET {Site}/workbooks?pageSize=2&pageNumber=1&sort=createdAt:asc,name:asc",
            $"GET {Site}/workbooks?pageSize=2&pageNumber=2&sort=createdAt:asc,name:asc",
        }, AreaFixtures.Calls(server).Select(call => $"{call.Method} {call.Target}").ToArray());
        Assert.HasCount(3, workbooks);
        var quarterly = workbooks[0];
        Assert.AreEqual(("Quarterly", "true", "Finance", "Yearly Sales"),
            (quarterly.Name, quarterly.ShowTabs, quarterly.Project?.Name, quarterly.Tags![0].Label));
        // Tableau Server 2025.1 sends a workbook's location type as the schema writes it, where a catalog asset's comes as
        // PROJECT.
        Assert.AreEqual(LocationType.Project, quarterly.Location?.Type);
        Assert.IsEmpty(workbooks[1].Tags!);
    }

    [TestMethod]
    public async Task AFilterAndASortAreSentBeforeTheStableOrdersFields()
    {
        using var server = AreaFixtures.Server();
        server.Map("GET", $"{Site}/workbooks", FakeResponse.Fixture(200, "workbooks-page.json"));
        await using var client = AreaFixtures.Client(server);

        var page = await client.Workbooks.ListPageAsync(1, new WorkbookListOptions
        {
            Filter = TableauFilter.And(TableauFilter.Eq(WorkbookField.ProjectName, "R&D C++"), TableauFilter.In(WorkbookField.Tags, "Sales", "Q1")),
            Sort = TableauSort.Desc(WorkbookField.UpdatedAt),
        });

        AreaFixtures.Sent(server, $"GET {Site}/workbooks?pageSize=100&pageNumber=1"
            + "&filter=projectName:eq:R%26D%20C%2B%2B,tags:in:%5BSales,Q1%5D&sort=updatedAt:desc,createdAt:asc,name:asc");
        Assert.AreEqual((1, 2, (int?)3), (page.PageNumber, page.PageSize, page.TotalAvailable));
    }

    [TestMethod]
    public async Task AListRefusesAnotherResourcesFieldBeforeSending()
    {
        using var server = AreaFixtures.Server();
        await using var client = AreaFixtures.Client(server);

        var thrown = await AreaFixtures.RefusedAsync<ArgumentException>(server, () => client.Workbooks.ListPageAsync(1, new WorkbookListOptions
        {
            Filter = TableauFilter.Eq(ViewField.Name, "Overview"),
        }));
        await AreaFixtures.RefusedAsync<ArgumentException>(server, () => client.Workbooks.ListPageAsync(1, new WorkbookListOptions
        {
            Sort = TableauSort.Asc(DataSourceField.Name),
        }));
        await AreaFixtures.RefusedAsync<ArgumentOutOfRangeException>(server, () => client.Workbooks.ListPageAsync(1, new WorkbookListOptions { PageSize = 1001 }));

        Assert.Contains("ViewField.Name is a field of views, and a list of workbooks", thrown.Message);
    }

    [TestMethod]
    public async Task AWorkbookIsGotUpdatedWithWhatIsSetAndDeleted()
    {
        using var server = AreaFixtures.Server();
        server.Map("GET", Workbook, FakeResponse.Fixture(200, "workbook.json"));
        server.Map("PUT", Workbook, FakeResponse.Fixture(200, "workbook.json"));
        server.Map("DELETE", Workbook, FakeResponse.Empty(204));
        await using var client = AreaFixtures.Client(server);

        var got = await client.Workbooks.GetAsync(Id);
        var updated = await client.Workbooks.UpdateAsync(Id, new WorkbookUpdate
        {
            Name = "Quarterly R&D",
            Description = "Invented: the quarter's numbers.",
            ShowTabs = true,
            RecentlyViewed = true,
            EncryptExtracts = false,
            ProjectId = AreaFixtures.ProjectId,
            OwnerId = AreaFixtures.OtherUserId,
            DataFreshnessPolicy = new DataFreshnessPolicy { Option = DataFreshnessPolicyOption.AlwaysLive },
        });
        await client.Workbooks.DeleteAsync(Id);

        var calls = AreaFixtures.Calls(server);
        CollectionAssert.AreEqual(new[] { $"GET {Workbook}", $"PUT {Workbook}", $"DELETE {Workbook}" },
            calls.Select(call => $"{call.Method} {call.Target}").ToArray());
        Assert.AreEqual(AreaFixtures.Json($$"""
            {"workbook":{"dataFreshnessPolicy":{"option":"AlwaysLive"},"owner":{"id":"{{AreaFixtures.OtherUserId}}"},
            "project":{"id":"{{AreaFixtures.ProjectId}}"},"description":"Invented: the quarter's numbers.","encryptExtracts":"false",
            "name":"Quarterly R&D","recentlyViewed":true,"showTabs":"true"} }
            """), AreaFixtures.Json(calls[1].BodyText));
        Assert.AreEqual(("Quarterly", "Finance", 2), (got.Name, got.Project?.Name, got.Views!.Count));
        Assert.AreEqual("Quarterly", updated.Name);
        Assert.IsEmpty(calls[0].Body);
        Assert.IsEmpty(calls[2].Body);
    }

    [TestMethod]
    public async Task AnUpdateSendsOnlyWhatIsSetAndNoEmptyElement()
    {
        using var server = AreaFixtures.Server();
        server.Map("PUT", Workbook, FakeResponse.Fixture(200, "workbook.json"));
        await using var client = AreaFixtures.Client(server);

        await client.Workbooks.UpdateAsync(Id, new WorkbookUpdate { ShowTabs = false });
        await client.Workbooks.UpdateAsync(Id, new WorkbookUpdate { Description = "" });

        CollectionAssert.AreEqual(new[] { AreaFixtures.Json("""{"workbook":{"showTabs":"false"}}"""), AreaFixtures.Json("""{"workbook":{"description":""}}""") },
            AreaFixtures.Bodies(server));
    }

    [TestMethod]
    [DataRow("not-a-luid", null)]
    [DataRow(null, "not-a-luid")]
    public async Task AnUpdatesProjectOrOwnerThatIsNoLuidIsRefusedBeforeSending(string? projectId, string? ownerId)
    {
        using var server = AreaFixtures.Server();
        await using var client = AreaFixtures.Client(server);

        var thrown = await AreaFixtures.RefusedAsync<ArgumentException>(server,
            () => client.Workbooks.UpdateAsync(Id, new WorkbookUpdate { ProjectId = projectId, OwnerId = ownerId }));

        Assert.AreEqual("changes", thrown.ParamName);
        Assert.Contains("'not-a-luid', which is no LUID", thrown.Message);
    }

    [TestMethod]
    public async Task ConnectionsAreListedAndOneIsUpdatedWithWhatIsSet()
    {
        using var server = AreaFixtures.Server();
        server.Map("GET", $"{Workbook}/connections", FakeResponse.Fixture(200, "connections.json"));
        server.Map("PUT", $"{Workbook}/connections/{ConnectionId}", FakeResponse.Fixture(200, "connection.json"));
        await using var client = AreaFixtures.Client(server);

        var connections = await AreaFixtures.AllAsync(client.Workbooks.ListConnectionsAsync(Id));
        var changes = new ConnectionUpdate
        {
            ServerAddress = "sql2.invented.example",
            ServerPort = 1434,
            AuthenticationType = "auth-user-pass",
            UserName = "writer",
            Password = Fixtures.ConnectionPassword,
            EmbedPassword = true,
        };
        var updated = await client.Workbooks.UpdateConnectionAsync(Id, ConnectionId, changes);
        var onlyAddress = await client.Workbooks.UpdateConnectionAsync(Id, ConnectionId, new ConnectionUpdate { ServerAddress = "sql3.invented.example" });

        var calls = AreaFixtures.Calls(server);
        Assert.AreEqual($"GET {Workbook}/connections", $"{calls[0].Method} {calls[0].Target}");
        Assert.AreEqual($"PUT {Workbook}/connections/{ConnectionId}", $"{calls[1].Method} {calls[1].Target}");
        Assert.AreEqual(AreaFixtures.Json($$"""
            {"connection":{"authenticationType":"auth-user-pass","embedPassword":true,"password":"{{Fixtures.ConnectionPassword}}",
            "serverAddress":"sql2.invented.example","serverPort":1434,"userName":"writer"} }
            """), AreaFixtures.Json(calls[1].BodyText));
        Assert.AreEqual(AreaFixtures.Json("""{"connection":{"serverAddress":"sql3.invented.example"}}"""), AreaFixtures.Json(calls[2].BodyText));
        Assert.HasCount(2, connections);
        Assert.AreEqual(("sqlserver", 1433L, "Invented sales"), (connections[0].Type, connections[0].ServerPort, connections[0].Datasource?.Name));
        Assert.AreEqual(("sql2.invented.example", 1434L), (updated.ServerAddress, updated.ServerPort));
        Assert.IsNotNull(onlyAddress);
        Assert.DoesNotContain(Fixtures.ConnectionPassword, changes.ToString());
        Assert.Contains("Password = ***", changes.ToString());
        Assert.Contains("Password = ,", new ConnectionUpdate { UserName = "reader" }.ToString());
    }

    [TestMethod]
    public async Task AnExtractRefreshPostsAnEmptyBodyAndReturnsTheJob()
    {
        using var server = AreaFixtures.Server();
        server.Map("POST", $"{Workbook}/refresh", FakeResponse.Fixture(202, "refresh-job.json"));
        await using var client = AreaFixtures.Client(server);

        var job = await client.Workbooks.RunExtractRefreshAsync(Id);

        AreaFixtures.Sent(server, $"POST {Workbook}/refresh", "{}");
        Assert.AreEqual((JobType.RefreshExtract, "Quarterly"), (job.Type, job.ExtractRefreshJob?.Workbook?.Name));
    }

    [TestMethod]
    public async Task AUsersWorkbooksArePagedAndOwnedOnlySendsOwnedBy()
    {
        using var server = AreaFixtures.Server();
        var path = $"{Site}/users/{AreaFixtures.OtherUserId}/workbooks";
        server.Map("GET", path, request => AreaFixtures.Page("workbooks", "workbook", AreaFixtures.PageNumber(request), 100, 101,
            AreaFixtures.PageNumber(request) == 1 ? 100 : 1));
        await using var client = AreaFixtures.Client(server);

        var all = await AreaFixtures.AllAsync(client.Workbooks.ListForUserAsync(AreaFixtures.OtherUserId));
        var owned = await AreaFixtures.AllAsync(client.Workbooks.ListForUserAsync(AreaFixtures.OtherUserId, ownedOnly: true));

        CollectionAssert.AreEqual(new[]
        {
            $"GET {path}?pageSize=100&pageNumber=1",
            $"GET {path}?pageSize=100&pageNumber=2",
            $"GET {path}?pageSize=100&pageNumber=1&ownedBy=true",
            $"GET {path}?pageSize=100&pageNumber=2&ownedBy=true",
        }, AreaFixtures.Calls(server).Select(call => $"{call.Method} {call.Target}").ToArray());
        Assert.HasCount(101, all);
        Assert.HasCount(101, owned);
        await Assert.ThrowsAsync<ArgumentException>(() => AreaFixtures.AllAsync(client.Workbooks.ListForUserAsync("ada")));
    }

    [TestMethod]
    public async Task RevisionsSendTheirPagingAndArePagedToTheTotal()
    {
        using var server = AreaFixtures.Server();
        server.Map("GET", $"{Workbook}/revisions", request => AreaFixtures.PageNumber(request) == 1
            ? FakeResponse.Fixture(200, "revisions-page.json")
            : FakeResponse.Json(200, """
                {"pagination":{"pageNumber":"2","pageSize":"2","totalAvailable":"3"},"revisions":{"revision":[{"revisionNumber":"3","current":true}]}}
                """));
        await using var client = AreaFixtures.Client(server);

        var revisions = await AreaFixtures.AllAsync(client.Workbooks.ListRevisionsAsync(Id, new RevisionListOptions { PageSize = 2 }));
        var first = await client.Workbooks.ListRevisionsPageAsync(Id, 1);

        CollectionAssert.AreEqual(new[]
        {
            $"GET {Workbook}/revisions?pageSize=2&pageNumber=1",
            $"GET {Workbook}/revisions?pageSize=2&pageNumber=2",
            $"GET {Workbook}/revisions?pageSize=100&pageNumber=1",
        }, AreaFixtures.Calls(server).Select(call => $"{call.Method} {call.Target}").ToArray());
        CollectionAssert.AreEqual(new long?[] { 1, 2, 3 }, revisions.Select(revision => revision.RevisionNumber).ToArray());
        Assert.AreEqual(("ada", true, true), (revisions[0].Publisher?.Name, revisions[1].Deleted, revisions[2].Current));
        Assert.AreEqual((1, 2, (int?)3), (first.PageNumber, first.PageSize, first.TotalAvailable));
    }

    [TestMethod]
    public async Task ARevisionIsRemovedByItsNumberAndANumberBelowOneIsRefused()
    {
        using var server = AreaFixtures.Server();
        server.Map("DELETE", $"{Workbook}/revisions/2", FakeResponse.Empty(204));
        await using var client = AreaFixtures.Client(server);

        await client.Workbooks.RemoveRevisionAsync(Id, 2);

        AreaFixtures.Sent(server, $"DELETE {Workbook}/revisions/2");
        var thrown = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => client.Workbooks.RemoveRevisionAsync(Id, 0));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => client.Workbooks.DownloadRevisionAsync(Id, -1));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => client.Workbooks.ListRevisionsPageAsync(Id, 0));
        Assert.AreEqual("revision", thrown.ParamName);
        Assert.HasCount(1, AreaFixtures.Calls(server));
    }

    [TestMethod]
    public async Task APdfSendsItsPageItsAgeItsSizeAndItsFilters()
    {
        using var server = AreaFixtures.Server();
        server.Map("POST", "/api/3.26/auth/signin", SessionFixtures.SignedIn());
        server.Map("GET", $"/api/3.26/sites/{Fixtures.SiteId}/workbooks/{Id}/pdf", FakeResponse.Content(200, "application/pdf", "%PDF-1.7"));
        var options = SessionFixtures.Options(server, "3.26");
        await using var client = SessionFixtures.Client(options, SessionFixtures.Pat());

        await using var pdf = await client.Workbooks.DownloadPdfAsync(Id, new ExportOptions
        {
            PageType = PageType.A4,
            Orientation = PageOrientation.Landscape,
            MaxAge = 5,
            Width = 800,
            Height = 600,
            Filters = new Dictionary<string, string> { ["Region"] = "West,East", ["vf_Year"] = "2026" },
        });

        var call = server.Requests.Single(request => request.Method == "GET");
        Assert.AreEqual($"/api/3.26/sites/{Fixtures.SiteId}/workbooks/{Id}/pdf?type=A4&orientation=Landscape&maxAge=5&vizWidth=800&vizHeight=600"
            + "&vf_Region=West%2CEast&vf_Year=2026", call.Target);
        Assert.AreEqual(TableauFileKind.Pdf, pdf.Kind);
    }

    [TestMethod]
    [DynamicData(nameof(Gated))]
    public async Task EachCallIsRefusedBelowItsVersionBeforeSending(string from, string name, Func<TableauClient, Task> call) =>
        await PermissionsTests.AssertGatedAsync(from, name, call);

    [TestMethod]
    public async Task APdfRefusesAnImagesResolutionBeforeSending()
    {
        using var server = AreaFixtures.Server();
        await using var client = AreaFixtures.Client(server);

        var thrown = await AreaFixtures.RefusedAsync<ArgumentException>(server,
            () => client.Workbooks.DownloadPdfAsync(Id, new ExportOptions { Resolution = ImageResolution.High }));

        Assert.AreEqual("options", thrown.ParamName);
        Assert.Contains("ExportOptions.Resolution isn't for this export: Download Workbook PDF takes", thrown.Message);
    }
}
