using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Tableau.Api.Rest.Models;
using Tableau.Api.Tests.Fakes;

namespace Tableau.Api.Tests;

/// <summary>
/// <see cref="ViewsClient"/>'s reads against the fake server: the site's views paged in a stable order, with their usage
/// when asked; a view by its id; a workbook's views. Its exports are <see cref="ExportTests"/>'.
/// </summary>
[TestClass]
public sealed class ViewsTests
{
    private const string Site = AreaFixtures.SitePath;
    private const string Id = AreaFixtures.ItemId;
    private const string WorkbookId = "66666666-7777-4888-8999-aaaaaaaaaaaa";

    /// <summary>Each view call above REST API 1.0: the version the reference gives it, its descriptor's name, and the call.</summary>
    public static IEnumerable<object[]> Gated =>
    [
        ["2.2", "Query Views for Site", (Func<TableauClient, Task>)(client => client.Views.ListPageAsync(1))],
        ["3.0", "Get View", (Func<TableauClient, Task>)(client => client.Views.GetAsync(Id))],
        ["2.8", "Query View Data", (Func<TableauClient, Task>)(client => client.Views.DownloadDataAsync(Id))],
        ["2.5", "Query View Image", (Func<TableauClient, Task>)(client => client.Views.DownloadImageAsync(Id))],
        ["3.24", "Query View Image at a size", (Func<TableauClient, Task>)(client => client.Views.DownloadImageAsync(Id,
            new ExportOptions { Width = 800, Height = 600 }))],
        ["2.8", "Query View PDF", (Func<TableauClient, Task>)(client => client.Views.DownloadPdfAsync(Id))],
        ["3.9", "Download View Crosstab Excel", (Func<TableauClient, Task>)(client => client.Views.DownloadCrosstabAsync(Id))],
    ];

    [TestMethod]
    public async Task ViewsAreListedPageByPageInAStableOrderWithTheirUsageWhenAsked()
    {
        using var server = AreaFixtures.Server();
        server.Map("GET", $"{Site}/views", FakeResponse.Fixture(200, "views-page.json"));
        await using var client = AreaFixtures.Client(server);

        var views = await AreaFixtures.AllAsync(client.Views.ListAsync(new ViewListOptions
        {
            Filter = TableauFilter.Eq(ViewField.WorkbookName, "Quarterly"),
            IncludeUsageStatistics = true,
        }));
        var page = await client.Views.ListPageAsync(1, new ViewListOptions { Sort = TableauSort.Asc(ViewField.Name), PageSize = 2 });

        CollectionAssert.AreEqual(new[]
        {
            $"GET {Site}/views?pageSize=100&pageNumber=1&filter=workbookName:eq:Quarterly&sort=createdAt:asc,workbookName:asc,name:asc&includeUsageStatistics=true",
            $"GET {Site}/views?pageSize=2&pageNumber=1&sort=name:asc,createdAt:asc,workbookName:asc",
        }, AreaFixtures.Calls(server).Select(call => $"{call.Method} {call.Target}").ToArray());
        Assert.HasCount(2, views);
        Assert.AreEqual(("Overview", "dashboard", 42L, WorkbookId), (views[0].Name, views[0].SheetType, views[0].Usage?.TotalViewCount, views[0].Workbook?.Id));
        Assert.AreEqual("draft", views[1].Tags![0].Label);
        Assert.HasCount(2, page.Items);
        await Assert.ThrowsAsync<ArgumentException>(
            () => client.Views.ListPageAsync(1, new ViewListOptions { Filter = TableauFilter.Eq(WorkbookField.Name, "Quarterly") }));
    }

    [TestMethod]
    public async Task AViewIsGotWithItsUsageWhenAsked()
    {
        using var server = AreaFixtures.Server();
        server.Map("GET", $"{Site}/views/{Id}", FakeResponse.Fixture(200, "view.json"));
        await using var client = AreaFixtures.Client(server);

        var view = await client.Views.GetAsync(Id);
        var withUsage = await client.Views.GetAsync(Id, includeUsageStatistics: true);

        CollectionAssert.AreEqual(new[] { $"GET {Site}/views/{Id}", $"GET {Site}/views/{Id}?includeUsageStatistics=true" },
            AreaFixtures.Calls(server).Select(call => $"{call.Method} {call.Target}").ToArray());
        Assert.AreEqual(("Costs by region", WorkbookId), (view.Name, view.Workbook?.Id));
        Assert.AreEqual(42L, withUsage.Usage?.TotalViewCount);
        await Assert.ThrowsAsync<ArgumentException>(() => client.Views.GetAsync("Overview"));
    }

    [TestMethod]
    public async Task AWorkbooksViewsComeFromOneCall()
    {
        using var server = AreaFixtures.Server();
        var path = $"{Site}/workbooks/{WorkbookId}/views";
        server.Map("GET", path, FakeResponse.Json(200, """
            {"views":{"view":[{"id":"44444444-5555-4666-8777-888888888888","name":"Overview"},{"id":"55555555-6666-4777-8888-999999999999","name":"Costs"}]}}
            """));
        await using var client = AreaFixtures.Client(server);

        var views = await AreaFixtures.AllAsync(client.Views.ListForWorkbookAsync(WorkbookId));
        var withUsage = await AreaFixtures.AllAsync(client.Views.ListForWorkbookAsync(WorkbookId, includeUsageStatistics: true));

        CollectionAssert.AreEqual(new[] { $"GET {path}", $"GET {path}?includeUsageStatistics=true" },
            AreaFixtures.Calls(server).Select(call => $"{call.Method} {call.Target}").ToArray());
        CollectionAssert.AreEqual(new[] { "Overview", "Costs" }, views.Select(view => view.Name).ToArray());
        Assert.HasCount(2, withUsage);
        await Assert.ThrowsAsync<ArgumentException>(() => AreaFixtures.AllAsync(client.Views.ListForWorkbookAsync("Quarterly")));
    }

    [TestMethod]
    public async Task AListWithoutItsWrapperOrItsItemsIsEmpty()
    {
        using var server = AreaFixtures.Server();
        server.Map("GET", $"{Site}/workbooks/{WorkbookId}/views", FakeResponse.Json(200, """{"views":{}}"""));
        server.Map("GET", $"{Site}/workbooks/{Id}/views", FakeResponse.Json(200, "{}"));
        server.Map("GET", $"{Site}/workbooks/{Id}/connections", FakeResponse.Json(200, "{}"));
        server.Map("GET", $"{Site}/views", FakeResponse.Json(200, """{"pagination":{"pageNumber":"1","pageSize":"100","totalAvailable":"0"}}"""));
        await using var client = AreaFixtures.Client(server);

        Assert.IsEmpty(await AreaFixtures.AllAsync(client.Views.ListForWorkbookAsync(WorkbookId)));
        Assert.IsEmpty(await AreaFixtures.AllAsync(client.Views.ListForWorkbookAsync(Id)));
        Assert.IsEmpty(await AreaFixtures.AllAsync(client.Workbooks.ListConnectionsAsync(Id)));
        Assert.IsEmpty(await AreaFixtures.AllAsync(client.Views.ListAsync()));
    }

    [TestMethod]
    [DynamicData(nameof(Gated))]
    public async Task EachCallIsRefusedBelowItsVersionBeforeSending(string from, string name, Func<TableauClient, Task> call) =>
        await PermissionsTests.AssertGatedAsync(from, name, call);
}
