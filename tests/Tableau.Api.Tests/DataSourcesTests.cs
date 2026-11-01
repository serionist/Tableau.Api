using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Tableau.Api.Rest.Models;
using Tableau.Api.Tests.Fakes;

namespace Tableau.Api.Tests;

/// <summary>
/// <see cref="DataSourcesClient"/> against the fake server: each call's method, path, query, body and answer; the list
/// paged in a stable order; an update that sends what is set and no empty element (F32); revisions paged (F7); the
/// version gates. The files' streaming is <see cref="ExportTests"/>'.
/// </summary>
[TestClass]
public sealed class DataSourcesTests
{
    private const string Site = AreaFixtures.SitePath;
    private const string Id = AreaFixtures.ItemId;
    private const string DataSource = Site + "/datasources/" + Id;
    private const string ConnectionId = "3c4d5e6f-0001-4a7b-8c9d-0e1f2a3b4c5d";

    /// <summary>Each data source call above REST API 1.0: the version the reference gives it, its descriptor's name, and the call.</summary>
    public static IEnumerable<object[]> Gated =>
    [
        ["2.3", "Query Data Source Connections", (Func<TableauClient, Task>)(client => AreaFixtures.AllAsync(client.DataSources.ListConnectionsAsync(Id)))],
        ["2.0", "Update Data Source", (Func<TableauClient, Task>)(client => client.DataSources.UpdateAsync(Id, new DataSourceUpdate()))],
        ["2.3", "Update Data Source Connection",
            (Func<TableauClient, Task>)(client => client.DataSources.UpdateConnectionAsync(Id, ConnectionId, new ConnectionUpdate()))],
        ["2.8", "Update Data Source Now", (Func<TableauClient, Task>)(client => client.DataSources.RunExtractRefreshAsync(Id))],
        ["2.0", "Delete Data Source", (Func<TableauClient, Task>)(client => client.DataSources.DeleteAsync(Id))],
        ["2.0", "Download Data Source", (Func<TableauClient, Task>)(client => client.DataSources.DownloadAsync(Id))],
        ["2.3", "Get Data Source Revisions", (Func<TableauClient, Task>)(client => client.DataSources.ListRevisionsPageAsync(Id, 1))],
        ["2.3", "Download Data Source Revision", (Func<TableauClient, Task>)(client => client.DataSources.DownloadRevisionAsync(Id, 2))],
        ["2.3", "Remove Data Source Revision", (Func<TableauClient, Task>)(client => client.DataSources.RemoveRevisionAsync(Id, 2))],
    ];

    [TestMethod]
    public async Task DataSourcesAreListedInAStableOrderAndAFieldOfAnotherResourceIsRefused()
    {
        using var server = AreaFixtures.Server();
        server.Map("GET", $"{Site}/datasources", FakeResponse.Fixture(200, "datasources-page.json"));
        await using var client = AreaFixtures.Client(server);

        var dataSources = await AreaFixtures.AllAsync(client.DataSources.ListAsync(new DataSourceListOptions
        {
            Filter = TableauFilter.Eq(DataSourceField.IsCertified, true),
        }));

        AreaFixtures.Sent(server, $"GET {Site}/datasources?pageSize=100&pageNumber=1&filter=isCertified:eq:true&sort=createdAt:asc,name:asc");
        Assert.HasCount(2, dataSources);
        Assert.AreEqual(("Invented sales", "hyper", true, true), (dataSources[0].Name, dataSources[0].Type, dataSources[0].HasExtracts, dataSources[0].IsCertified));
        Assert.AreEqual("Sales,Marketing", dataSources[1].Tags![0].Label);
        await Assert.ThrowsAsync<ArgumentException>(
            () => client.DataSources.ListPageAsync(1, new DataSourceListOptions { Sort = TableauSort.Asc(WorkbookField.Name) }));
    }

    [TestMethod]
    public async Task AnEmptyListIsEmpty()
    {
        using var server = AreaFixtures.Server();
        // An empty wrapper, and on the second page none at all.
        server.Map("GET", $"{Site}/datasources", request => AreaFixtures.PageNumber(request) == 1
            ? FakeResponse.Json(200, """{"pagination":{"pageNumber":"1","pageSize":"100","totalAvailable":"0"},"datasources":{}}""")
            : FakeResponse.Json(200, """{"pagination":{"pageNumber":"2","pageSize":"100","totalAvailable":"0"}}"""));
        await using var client = AreaFixtures.Client(server);

        Assert.IsEmpty(await AreaFixtures.AllAsync(client.DataSources.ListAsync()));
        Assert.IsEmpty((await client.DataSources.ListPageAsync(2)).Items);
    }

    [TestMethod]
    public async Task ADataSourceIsGotUpdatedWithWhatIsSetAndDeleted()
    {
        using var server = AreaFixtures.Server();
        server.Map("GET", DataSource, FakeResponse.Fixture(200, "datasource.json"));
        server.Map("PUT", DataSource, FakeResponse.Fixture(200, "datasource.json"));
        server.Map("DELETE", DataSource, FakeResponse.Empty(204));
        await using var client = AreaFixtures.Client(server);

        var got = await client.DataSources.GetAsync(Id);
        var updated = await client.DataSources.UpdateAsync(Id, new DataSourceUpdate
        {
            Name = "Invented sales R&D",
            IsCertified = true,
            CertificationNote = "Checked by the data team.",
            EncryptExtracts = true,
            ProjectId = AreaFixtures.ProjectId,
            OwnerId = AreaFixtures.OtherUserId,
        });
        await client.DataSources.UpdateAsync(Id, new DataSourceUpdate { IsCertified = false });
        await client.DataSources.DeleteAsync(Id);

        var calls = AreaFixtures.Calls(server);
        CollectionAssert.AreEqual(new[] { $"GET {DataSource}", $"PUT {DataSource}", $"PUT {DataSource}", $"DELETE {DataSource}" },
            calls.Select(call => $"{call.Method} {call.Target}").ToArray());
        Assert.AreEqual(AreaFixtures.Json($$"""
            {"datasource":{"owner":{"id":"{{AreaFixtures.OtherUserId}}"},"project":{"id":"{{AreaFixtures.ProjectId}}"},
            "certificationNote":"Checked by the data team.","encryptExtracts":"true","isCertified":true,"name":"Invented sales R&D"} }
            """), AreaFixtures.Json(calls[1].BodyText));
        Assert.AreEqual(AreaFixtures.Json("""{"datasource":{"isCertified":false}}"""), AreaFixtures.Json(calls[2].BodyText));
        Assert.AreEqual(("Invented sales", "Checked by the data team.", "Finance"), (got.Name, got.CertificationNote, got.Project?.Name));
        Assert.AreEqual("Invented sales", updated.Name);
        await Assert.ThrowsAsync<ArgumentException>(
            () => client.DataSources.UpdateAsync(Id, new DataSourceUpdate { OwnerId = "ada" }));
    }

    [TestMethod]
    public async Task ConnectionsAreListedAndOneIsUpdated()
    {
        using var server = AreaFixtures.Server();
        server.Map("GET", $"{DataSource}/connections", FakeResponse.Fixture(200, "connections.json"));
        server.Map("PUT", $"{DataSource}/connections/{ConnectionId}", FakeResponse.Fixture(200, "connection.json"));
        await using var client = AreaFixtures.Client(server);

        var connections = await AreaFixtures.AllAsync(client.DataSources.ListConnectionsAsync(Id));
        var updated = await client.DataSources.UpdateConnectionAsync(Id, ConnectionId,
            new ConnectionUpdate { UserName = "writer", Password = Fixtures.ConnectionPassword, EmbedPassword = false });

        var calls = AreaFixtures.Calls(server);
        Assert.AreEqual($"PUT {DataSource}/connections/{ConnectionId}", $"{calls[1].Method} {calls[1].Target}");
        Assert.AreEqual(AreaFixtures.Json($$"""{"connection":{"embedPassword":false,"password":"{{Fixtures.ConnectionPassword}}","userName":"writer"} }"""),
            AreaFixtures.Json(calls[1].BodyText));
        Assert.HasCount(2, connections);
        Assert.AreEqual("writer", updated.UserName);
        await Assert.ThrowsAsync<ArgumentException>(() => client.DataSources.UpdateConnectionAsync(Id, "c1", new ConnectionUpdate()));
        await Assert.ThrowsAsync<ArgumentNullException>(() => client.DataSources.UpdateConnectionAsync(Id, ConnectionId, null!));
    }

    [TestMethod]
    public async Task AnExtractRefreshPostsAnEmptyBodyAndReturnsTheJob()
    {
        using var server = AreaFixtures.Server();
        server.Map("POST", $"{DataSource}/refresh", FakeResponse.Fixture(202, "refresh-job.json"));
        await using var client = AreaFixtures.Client(server);

        var job = await client.DataSources.RunExtractRefreshAsync(Id);

        AreaFixtures.Sent(server, $"POST {DataSource}/refresh", "{}");
        Assert.AreEqual(JobType.RefreshExtract, job.Type);
    }

    [TestMethod]
    public async Task RevisionsSendTheirPagingAndOneIsRemoved()
    {
        using var server = AreaFixtures.Server();
        server.Map("GET", $"{DataSource}/revisions", FakeResponse.Json(200, """
            {"pagination":{"pageNumber":"1","pageSize":"100","totalAvailable":"1"},"revisions":{"revision":[{"revisionNumber":"1","current":true}]}}
            """));
        server.Map("DELETE", $"{DataSource}/revisions/7", FakeResponse.Empty(204));
        await using var client = AreaFixtures.Client(server);

        var revisions = await AreaFixtures.AllAsync(client.DataSources.ListRevisionsAsync(Id));
        await client.DataSources.RemoveRevisionAsync(Id, 7);

        CollectionAssert.AreEqual(new[] { $"GET {DataSource}/revisions?pageSize=100&pageNumber=1", $"DELETE {DataSource}/revisions/7" },
            AreaFixtures.Calls(server).Select(call => $"{call.Method} {call.Target}").ToArray());
        Assert.AreEqual((1L, true), (revisions.Single().RevisionNumber, revisions.Single().Current));
        await Assert.ThrowsAsync<ArgumentException>(() => AreaFixtures.AllAsync(client.DataSources.ListRevisionsAsync("Invented sales")));
    }

    [TestMethod]
    [DynamicData(nameof(Gated))]
    public async Task EachCallIsRefusedBelowItsVersionBeforeSending(string from, string name, Func<TableauClient, Task> call) =>
        await PermissionsTests.AssertGatedAsync(from, name, call);
}
