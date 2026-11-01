using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Tableau.Api.Rest.Models;
using Tableau.Api.Tests.Fakes;

namespace Tableau.Api.Tests;

/// <summary>
/// <see cref="MetadataClient"/>'s REST calls: databases and tables listed page by page (F8), got, updated and deleted;
/// a table's columns read in one call, updated and deleted; and data quality warnings added, updated, listed and deleted
/// for each kind of content that takes one, a column's from REST API 3.16, from one table.
/// </summary>
[TestClass]
public sealed class MetadataTests
{
    private const string Site = AreaFixtures.SitePath;
    private const string TableId = "6a9b6058-671d-44bc-ad6d-c58b225f5d4c";
    private const string ColumnId = "c6dcdb0e-9b2a-4224-bf3b-58662fe72b09";
    private const string WarningId = "1c8ebdfc-270a-4414-812c-3306a1c95e07";

    /// <summary>Each kind a data quality warning takes: its path's content type, and the version a column's calls need.</summary>
    public static IEnumerable<object[]> Warned =>
    [
        [TableauContentKind.Database, "database", "3.5"],
        [TableauContentKind.Table, "table", "3.5"],
        [TableauContentKind.Column, "column", "3.16"],
        [TableauContentKind.DataSource, "datasource", "3.5"],
        [TableauContentKind.Flow, "flow", "3.5"],
        [TableauContentKind.VirtualConnection, "virtualconnection", "3.5"],
    ];

    [TestMethod]
    public async Task DatabasesAreListedPageByPageUntilTheTotal()
    {
        using var server = AreaFixtures.Server();
        server.Map("GET", $"{Site}/databases", request => AreaFixtures.PageNumber(request) == 1
            ? FakeResponse.Fixture(200, "databases-page.json")
            : AreaFixtures.Page("databases", "database", 2, 2, 3, 1));
        await using var client = AreaFixtures.Client(server);

        var databases = await AreaFixtures.AllAsync(client.Metadata.ListDatabasesAsync(new DatabaseListOptions { PageSize = 2 }));

        CollectionAssert.AreEqual(new[] { $"GET {Site}/databases?pageSize=2&pageNumber=1", $"GET {Site}/databases?pageSize=2&pageNumber=2" },
            AreaFixtures.Calls(server).Select(call => $"{call.Method} {call.Target}").ToArray());
        Assert.HasCount(3, databases);
        var airlines = databases[0];
        Assert.AreEqual(("invented-airlines", DatabaseType.DatabaseServer, DatabaseContentPermissions.ManagedByOwner),
            (airlines.Name, airlines.Type, airlines.ContentPermissions));
        // Tableau Server 2025.1 sends PROJECT, where the schema's enumeration has Project: the value keeps the server's text.
        Assert.AreEqual(("ada", "Finance", "PROJECT"), (airlines.Contact?.Name, airlines.Tags![0].Label, airlines.Location?.Type?.Value));
        Assert.AreNotEqual(LocationType.Project, airlines.Location?.Type);
        Assert.IsEmpty(databases[1].Tags!);
    }

    [TestMethod]
    public async Task TablesAreListedPageByPageAndAPageIsReadWithItsNumbers()
    {
        using var server = AreaFixtures.Server();
        server.Map("GET", $"{Site}/tables", FakeResponse.Fixture(200, "tables-page.json"));
        await using var client = AreaFixtures.Client(server);

        var tables = await AreaFixtures.AllAsync(client.Metadata.ListTablesAsync());
        var page = await client.Metadata.ListTablesPageAsync(1, new TableListOptions { PageSize = 100 });

        var first = AreaFixtures.Calls(server)[0];
        Assert.AreEqual($"GET {Site}/tables?pageSize=100&pageNumber=1", $"{first.Method} {first.Target}");
        Assert.HasCount(2, tables);
        Assert.AreEqual(("[Orders$]", "PII", true), (tables[1].Name, tables[1].Tags![0].Label, tables[1].IsCertified));
        Assert.AreEqual((1, 100, (int?)2), (page.PageNumber, page.PageSize, page.TotalAvailable));
        Assert.HasCount(2, page.Items);
    }

    [TestMethod]
    public async Task AListWithoutItsWrapperIsEmptyAndAPageSizeOutsideItsRangeIsRefused()
    {
        using var server = AreaFixtures.Server();
        server.Map("GET", $"{Site}/databases", FakeResponse.Json(200, """{"pagination":{"pageNumber":"1","pageSize":"100","totalAvailable":"0"}}"""));
        server.Map("GET", $"{Site}/tables", FakeResponse.Json(200, """{"pagination":{"pageNumber":"1","pageSize":"100","totalAvailable":"0"},"tables":{}}"""));
        await using var client = AreaFixtures.Client(server);

        Assert.IsEmpty(await AreaFixtures.AllAsync(client.Metadata.ListDatabasesAsync()));
        Assert.IsEmpty((await client.Metadata.ListTablesPageAsync(1)).Items);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => client.Metadata.ListDatabasesPageAsync(1, new DatabaseListOptions { PageSize = 1001 }));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => client.Metadata.ListTablesPageAsync(0));
        Assert.HasCount(2, AreaFixtures.Calls(server));
    }

    [TestMethod]
    public async Task ADatabaseIsGotUpdatedAndDeleted()
    {
        using var server = AreaFixtures.Server();
        var database = $"{Site}/databases/{AreaFixtures.ItemId}";
        server.Map("GET", database, FakeResponse.Fixture(200, "database.json"));
        server.Map("PUT", database, FakeResponse.Fixture(200, "database.json"));
        server.Map("DELETE", database, FakeResponse.Empty(204));
        await using var client = AreaFixtures.Client(server);

        var got = await client.Metadata.GetDatabaseAsync(AreaFixtures.ItemId);
        var updated = await client.Metadata.UpdateDatabaseAsync(AreaFixtures.ItemId, new Database
        {
            IsCertified = true,
            CertificationNote = "Checked",
            Description = "Invented: ask the data team before changing it.",
            ContentPermissions = DatabaseContentPermissions.LockedToDatabase,
            Contact = new User { Id = AreaFixtures.OtherUserId, Name = "ada" },
            // Not the reference's: left out of the body, as a database read and changed with `with` holds them.
            Id = AreaFixtures.ItemId,
            Name = "invented-airlines",
            HostName = "sql.invented.example",
            Tags = [new Tag { Label = "Finance" }],
        });
        await client.Metadata.DeleteDatabaseAsync(AreaFixtures.ItemId);

        var calls = AreaFixtures.Calls(server);
        CollectionAssert.AreEqual(new[] { $"GET {database}", $"PUT {database}", $"DELETE {database}" }, calls.Select(call => $"{call.Method} {call.Target}").ToArray());
        Assert.AreEqual(AreaFixtures.Json($$$"""
            {"database":{"contact":{"id":"{{{AreaFixtures.OtherUserId}}}"},"certificationNote":"Checked","contentPermissions":"LockedToDatabase","description":"Invented: ask the data team before changing it.","isCertified":true}}
            """), AreaFixtures.Json(calls[1].BodyText));
        Assert.AreEqual(("invented-airlines", "grace"), (got.Name, got.Certifier?.Name));
        Assert.AreEqual("Invented: ask the data team before changing it.", updated.Description);
    }

    [TestMethod]
    public async Task ATableIsGotUpdatedAndDeleted()
    {
        using var server = AreaFixtures.Server();
        var table = $"{Site}/tables/{AreaFixtures.ItemId}";
        server.Map("GET", table, FakeResponse.Fixture(200, "table.json"));
        server.Map("PUT", table, FakeResponse.Fixture(200, "table.json"));
        server.Map("DELETE", table, FakeResponse.Empty(204));
        await using var client = AreaFixtures.Client(server);

        var got = await client.Metadata.GetTableAsync(AreaFixtures.ItemId);
        var updated = await client.Metadata.UpdateTableAsync(AreaFixtures.ItemId,
            new Table { IsCertified = true, Description = "Invented cultures", Name = "Culture", Schema = "invented", IsEmbedded = false });
        await client.Metadata.DeleteTableAsync(AreaFixtures.ItemId);

        var calls = AreaFixtures.Calls(server);
        CollectionAssert.AreEqual(new[] { $"GET {table}", $"PUT {table}", $"DELETE {table}" }, calls.Select(call => $"{call.Method} {call.Target}").ToArray());
        Assert.AreEqual(AreaFixtures.Json("""{"table":{"description":"Invented cultures","isCertified":true}}"""), AreaFixtures.Json(calls[1].BodyText));
        Assert.AreEqual(("Culture", "invented"), (got.Name, got.Schema));
        Assert.IsTrue(updated.IsCertified);
    }

    [TestMethod]
    public async Task ATablesColumnsAreReadInOneCallOfTheMostAPageHolds()
    {
        using var server = AreaFixtures.Server();
        server.Map("GET", $"{Site}/tables/{TableId}/columns", FakeResponse.Fixture(200, "columns.json"));
        await using var client = AreaFixtures.Client(server);

        var columns = await AreaFixtures.AllAsync(client.Metadata.ListColumnsAsync(TableId));

        // Tableau Server 2025.1 refuses the second page of a table's columns, whatever their number, so there is one call.
        AreaFixtures.Sent(server, $"GET {Site}/tables/{TableId}/columns?pageSize=1000");
        Assert.HasCount(2, columns);
        Assert.AreEqual(("AddressLine1", "WSTR", TableId, true), (columns[1].Name, columns[1].RemoteType, columns[1].ParentTableId, columns[1].Nullable));
        Assert.AreEqual(("PII", "Invented street address"), (columns[1].Tags![0].Label, columns[1].Description));
    }

    [TestMethod]
    public async Task AnAnswerThatCountsMoreColumnsThanItHoldsFailsRatherThanStopShort()
    {
        using var server = AreaFixtures.Server();
        server.Map("GET", $"{Site}/tables/{TableId}/columns", FakeResponse.Json(200, """
            {"pagination":{"pageNumber":"1","pageSize":"1000","totalAvailable":"1500"},"columns":{"column":[{"id":"c1","name":"A"},{"id":"c2","name":"B"}]}}
            """));
        server.Map("GET", $"{Site}/tables/{AreaFixtures.ItemId}/columns", FakeResponse.Json(200, """
            {"pagination":{"pageNumber":"1","pageSize":"1000","totalAvailable":"2"},"columns":{"column":[{"id":"c1","name":"A"},{"id":"c2","name":"B"}]}}
            """));
        await using var client = AreaFixtures.Client(server);

        var thrown = await ClientFixtures.FailsAsync(TableauErrorKind.InvalidResponse, () => AreaFixtures.AllAsync(client.Metadata.ListColumnsAsync(TableId)));
        var whole = await AreaFixtures.AllAsync(client.Metadata.ListColumnsAsync(AreaFixtures.ItemId));

        Assert.StartsWith("Query Columns in a Table answered 2 of the table's 1500 columns", thrown.Message);
        Assert.HasCount(2, whole);
    }

    [TestMethod]
    public async Task AContactWithoutItsLuidIsRefusedBeforeSending()
    {
        using var server = AreaFixtures.Server();
        await using var client = AreaFixtures.Client(server);

        Assert.AreEqual("changes", (await AreaFixtures.RefusedAsync<ArgumentException>(server,
            () => client.Metadata.UpdateDatabaseAsync(AreaFixtures.ItemId, new Database { Contact = new User { Name = "ada" } }))).ParamName);
        Assert.AreEqual("changes", (await AreaFixtures.RefusedAsync<ArgumentException>(server,
            () => client.Metadata.UpdateTableAsync(AreaFixtures.ItemId, new Table { Contact = new User { Id = "ada" } }))).ParamName);
    }

    [TestMethod]
    public async Task AColumnIsUpdatedAndDeletedUnderItsTable()
    {
        using var server = AreaFixtures.Server();
        var column = $"{Site}/tables/{TableId}/columns/{ColumnId}";
        server.Map("PUT", column, FakeResponse.Fixture(200, "column.json"));
        server.Map("DELETE", column, FakeResponse.Empty(204));
        server.Map("GET", $"{Site}/tables/{AreaFixtures.ItemId}/columns", FakeResponse.Json(200, "{}"));
        await using var client = AreaFixtures.Client(server);

        var updated = await client.Metadata.UpdateColumnAsync(TableId, ColumnId,
            new Column { Description = "Validated against invented maps", Name = "AddressLine1", RemoteType = "WSTR", Nullable = true });
        await client.Metadata.DeleteColumnAsync(TableId, ColumnId);
        var none = await AreaFixtures.AllAsync(client.Metadata.ListColumnsAsync(AreaFixtures.ItemId));

        var calls = AreaFixtures.Calls(server);
        Assert.AreEqual($"PUT {column}", $"{calls[0].Method} {calls[0].Target}");
        Assert.AreEqual(AreaFixtures.Json("""{"column":{"description":"Validated against invented maps"}}"""), AreaFixtures.Json(calls[0].BodyText));
        Assert.AreEqual($"DELETE {column}", $"{calls[1].Method} {calls[1].Target}");
        Assert.AreEqual("Validated against invented maps", updated.Description);
        Assert.IsEmpty(none);
    }

    [TestMethod]
    [DynamicData(nameof(Warned))]
    public async Task EachKindsWarningsAreAddedListedAndDeletedByContent(TableauContentKind kind, string type, string from)
    {
        using var server = AreaFixtures.Server();
        var warnings = $"{Site}/dataQualityWarnings/{type}/{AreaFixtures.ItemId}";
        server.Map("POST", warnings, FakeResponse.Fixture(201, "data-quality-warning.json"));
        server.Map("GET", warnings, FakeResponse.Fixture(200, "data-quality-warnings.json"));
        server.Map("DELETE", warnings, FakeResponse.Empty(204));
        await using var client = AreaFixtures.Client(server);
        var content = Content(kind);

        var added = await client.Metadata.AddDataQualityWarningAsync(content, new DataQualityWarning
        {
            Type = "WARNING",
            Message = "Invented: delayed",
            IsSevere = false,
            IsActive = true,
            // Not the request's: left out of its body.
            Id = WarningId,
            ContentId = AreaFixtures.ItemId,
        });
        var listed = await AreaFixtures.AllAsync(client.Metadata.ListDataQualityWarningsAsync(content));
        await client.Metadata.DeleteDataQualityWarningsAsync(content);

        var calls = AreaFixtures.Calls(server);
        CollectionAssert.AreEqual(new[] { $"POST {warnings}", $"GET {warnings}", $"DELETE {warnings}" }, calls.Select(call => $"{call.Method} {call.Target}").ToArray());
        Assert.AreEqual(AreaFixtures.Json("""{"dataQualityWarning":{"isActive":true,"isSevere":false,"message":"Invented: delayed","type":"WARNING"}}"""),
            AreaFixtures.Json(calls[0].BodyText));
        Assert.AreEqual((WarningId, "TABLE", "Ada Lovelace"), (added.Id, added.ContentType, added.UserDisplayName));
        Assert.AreEqual(("MAINTENANCE", true), (listed.Single().Type, listed.Single().IsSevere));

        var column = kind == TableauContentKind.Column ? ", of a column" : "";
        await PermissionsTests.AssertGatedAsync(from, kind == TableauContentKind.Column ? "Add Data Quality Warning to a column" : "Add Data Quality Warning",
            client => client.Metadata.AddDataQualityWarningAsync(content, new DataQualityWarning { Type = "WARNING" }));
        await PermissionsTests.AssertGatedAsync(from, $"Query Data Quality Warning by Content{column}",
            client => AreaFixtures.AllAsync(client.Metadata.ListDataQualityWarningsAsync(content)));
        await PermissionsTests.AssertGatedAsync(from, $"Delete Data Quality Warning by Content{column}",
            client => client.Metadata.DeleteDataQualityWarningsAsync(content));
    }

    [TestMethod]
    public async Task AWarningIsUpdatedAndDeletedByItsId()
    {
        using var server = AreaFixtures.Server();
        var warning = $"{Site}/dataQualityWarnings/{WarningId}";
        server.Map("PUT", warning, FakeResponse.Fixture(200, "data-quality-warning.json"));
        server.Map("DELETE", warning, FakeResponse.Empty(204));
        server.Map("GET", $"{Site}/dataQualityWarnings/table/{AreaFixtures.ItemId}", FakeResponse.Json(200, """{"dataQualityWarningList":{}}"""));
        await using var client = AreaFixtures.Client(server);

        var updated = await client.Metadata.UpdateDataQualityWarningAsync(WarningId, new DataQualityWarning { Type = "STALE", IsActive = false });
        await client.Metadata.DeleteDataQualityWarningAsync(WarningId);
        var none = await AreaFixtures.AllAsync(client.Metadata.ListDataQualityWarningsAsync(TableauContent.Table(AreaFixtures.ItemId)));

        var calls = AreaFixtures.Calls(server);
        CollectionAssert.AreEqual(new[] { $"PUT {warning}", $"DELETE {warning}", $"GET {Site}/dataQualityWarnings/table/{AreaFixtures.ItemId}" },
            calls.Select(call => $"{call.Method} {call.Target}").ToArray());
        Assert.AreEqual(AreaFixtures.Json("""{"dataQualityWarning":{"isActive":false,"type":"STALE"}}"""), AreaFixtures.Json(calls[0].BodyText));
        Assert.AreEqual(WarningId, updated.Id);
        Assert.IsEmpty(none);
    }

    [TestMethod]
    public async Task AKindThatTakesNoWarningIsRefusedBeforeSending()
    {
        using var server = AreaFixtures.Server();
        await using var client = AreaFixtures.Client(server);
        var warning = new DataQualityWarning { Type = "WARNING" };

        foreach (var content in new[]
                 {
                     TableauContent.Project(AreaFixtures.ItemId), TableauContent.Workbook(AreaFixtures.ItemId), TableauContent.View(AreaFixtures.ItemId),
                     TableauContent.Collection(AreaFixtures.ItemId),
                 })
        {
            var added = await AreaFixtures.RefusedAsync<ArgumentException>(server, () => client.Metadata.AddDataQualityWarningAsync(content, warning));
            await AreaFixtures.RefusedAsync<ArgumentException>(server, () => AreaFixtures.AllAsync(client.Metadata.ListDataQualityWarningsAsync(content)));
            await AreaFixtures.RefusedAsync<ArgumentException>(server, () => client.Metadata.DeleteDataQualityWarningsAsync(content));
            Assert.AreEqual("content", added.ParamName);
            Assert.StartsWith("A data quality warning takes a database, a table, a column, a data source, a flow or a virtual connection, not a ", added.Message);
        }
    }

    [TestMethod]
    public async Task ARefusedArgumentSendsNothing()
    {
        using var server = AreaFixtures.Server();
        await using var client = AreaFixtures.Client(server);
        var table = TableauContent.Table(AreaFixtures.ItemId);

        Assert.AreEqual("warning", (await AreaFixtures.RefusedAsync<ArgumentException>(server,
            () => client.Metadata.AddDataQualityWarningAsync(table, new DataQualityWarning { Message = "No type" }))).ParamName);
        Assert.AreEqual("warning", (await AreaFixtures.RefusedAsync<ArgumentNullException>(server,
            () => client.Metadata.AddDataQualityWarningAsync(table, null!))).ParamName);
        Assert.AreEqual("changes", (await AreaFixtures.RefusedAsync<ArgumentException>(server,
            () => client.Metadata.UpdateDataQualityWarningAsync(WarningId, new DataQualityWarning()))).ParamName);
        Assert.AreEqual("id", (await AreaFixtures.RefusedAsync<ArgumentException>(server,
            () => client.Metadata.UpdateDataQualityWarningAsync("w1", new DataQualityWarning { Type = "WARNING" }))).ParamName);
        Assert.AreEqual("id", (await AreaFixtures.RefusedAsync<ArgumentException>(server, () => client.Metadata.DeleteDataQualityWarningAsync("w1"))).ParamName);
        Assert.AreEqual("id", (await AreaFixtures.RefusedAsync<ArgumentException>(server, () => client.Metadata.GetDatabaseAsync("airlines"))).ParamName);
        Assert.AreEqual("id", (await AreaFixtures.RefusedAsync<ArgumentException>(server, () => client.Metadata.GetTableAsync("orders"))).ParamName);
        Assert.AreEqual("id", (await AreaFixtures.RefusedAsync<ArgumentException>(server, () => client.Metadata.DeleteDatabaseAsync("airlines"))).ParamName);
        Assert.AreEqual("id", (await AreaFixtures.RefusedAsync<ArgumentException>(server, () => client.Metadata.DeleteTableAsync("orders"))).ParamName);
        await AreaFixtures.RefusedAsync<ArgumentNullException>(server, () => client.Metadata.UpdateDatabaseAsync(AreaFixtures.ItemId, null!));
        await AreaFixtures.RefusedAsync<ArgumentNullException>(server, () => client.Metadata.UpdateTableAsync(AreaFixtures.ItemId, null!));
        await AreaFixtures.RefusedAsync<ArgumentNullException>(server, () => client.Metadata.UpdateColumnAsync(TableId, ColumnId, null!));
        Assert.AreEqual("columnId", (await AreaFixtures.RefusedAsync<ArgumentException>(server,
            () => client.Metadata.UpdateColumnAsync(TableId, "c1", new Column()))).ParamName);
        Assert.AreEqual("tableId", (await AreaFixtures.RefusedAsync<ArgumentException>(server, () => client.Metadata.DeleteColumnAsync("t1", ColumnId))).ParamName);
        Assert.AreEqual("tableId", (await AreaFixtures.RefusedAsync<ArgumentException>(server,
            () => AreaFixtures.AllAsync(client.Metadata.ListColumnsAsync("orders")))).ParamName);
        await AreaFixtures.RefusedAsync<ArgumentNullException>(server, () => client.Metadata.DeleteDataQualityWarningsAsync(null!));
    }

    [TestMethod]
    public async Task AMockThatOverridesAPageServesItsList()
    {
        var mock = new PagedMetadata();

        var databases = await AreaFixtures.AllAsync(mock.ListDatabasesAsync(new DatabaseListOptions { PageSize = 1 }));
        var tables = await AreaFixtures.AllAsync(mock.ListTablesAsync(new TableListOptions { PageSize = 1 }));

        CollectionAssert.AreEqual(new[] { "d1", "d2" }, databases.Select(database => database.Name).ToArray());
        CollectionAssert.AreEqual(new[] { "t1", "t2" }, tables.Select(table => table.Name).ToArray());
    }

    private static TableauContent Content(TableauContentKind kind) => kind switch
    {
        TableauContentKind.Database => TableauContent.Database(AreaFixtures.ItemId),
        TableauContentKind.Table => TableauContent.Table(AreaFixtures.ItemId),
        TableauContentKind.Column => TableauContent.Column(TableId, AreaFixtures.ItemId),
        TableauContentKind.DataSource => TableauContent.DataSource(AreaFixtures.ItemId),
        TableauContentKind.Flow => TableauContent.Flow(AreaFixtures.ItemId),
        _ => TableauContent.VirtualConnection(AreaFixtures.ItemId),
    };

    // Two pages of one, as a mock of the page methods serves them.
    private sealed class PagedMetadata : MetadataClient
    {
        public override Task<Page<Database>> ListDatabasesPageAsync(int pageNumber, DatabaseListOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new Page<Database>([new Database { Name = $"d{pageNumber}" }], pageNumber, 1, 2));

        public override Task<Page<Table>> ListTablesPageAsync(int pageNumber, TableListOptions? options = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new Page<Table>([new Table { Name = $"t{pageNumber}" }], pageNumber, 1, 2));
    }
}
