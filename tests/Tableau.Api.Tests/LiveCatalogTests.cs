using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Tableau.Api.Rest.Models;

namespace Tableau.Api.Tests;

/// <summary>
/// Tableau Catalog in the run's site (<see cref="LiveSite"/> has the settings), on samples of the test's own, since it
/// deletes their assets: the samples' database, a table of it and that table's columns, once the catalog lists them,
/// listed whole and a page of one, got, described, tagged and untagged, and a capability granted to a run's group and
/// deleted, which Tableau refuses for an asset embedded in a workbook or a data source, as every sample's is; data quality warnings on the samples' data source and on a column added, listed, updated and deleted, by id
/// and by content; then the column, the table and the database deleted, each not found after. An update or a warning
/// that needs Data Management, which a read can't show the server has, is called anyway and its outcome pinned: answered,
/// or refused with Tableau's code. One at a time, as the other live tests.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class LiveCatalogTests
{
    private const string Label = "Yearly Sales";

    private const string Described = "Described by Tableau.Api's live suite.";

    private static readonly Capability[] _read = [new Capability { Name = CapabilityName.Read, Mode = CapabilityMode.Allow }];

    public TestContext TestContext { get; set; } = null!;

    [SiteTest(TableauSite.Live, [LiveSite.ServerUrl], SignsIn = LiveSignIn.Token), TestCategory(TableauSite.Live)]
    public async Task TheSamplesCatalogAssetsAreListedGotDescribedTaggedPermittedWarnedAndDeleted()
    {
        await using var client = await LiveSite.RunClientAsync();
        var own = await LiveSite.OwnSamplesAsync(client, "catalog");
        var dataSource = await LiveSite.EventuallyAsync(async () => (await AreaFixtures.AllAsync(client.DataSources.ListAsync(new DataSourceListOptions
        {
            Filter = TableauFilter.Eq(DataSourceField.ProjectName, own.ProjectName),
        }))).FirstOrDefault(found => found.Project?.Id == own.ProjectId), "The samples' data source");
        // The catalog lists a new site's assets once the Metadata API has taken in its content.
        var (databaseId, tableId, embedded) = await LiveSite.EventuallyAsync(() => AssetsAsync(client, own.ProjectName), "The samples' database and a table of it",
            TimeSpan.FromMinutes(5));
        var group = await LiveSite.NewGroupAsync(client);
        var (database, table) = (TableauContent.Database(databaseId), TableauContent.Table(tableId));

        var databases = await AreaFixtures.AllAsync(client.Metadata.ListDatabasesAsync());
        var databasePage = await client.Metadata.ListDatabasesPageAsync(1, new DatabaseListOptions { PageSize = 1 });
        var gotDatabase = await client.Metadata.GetDatabaseAsync(databaseId);
        var databaseUpdate = await OutcomeAsync(async () => (await client.Metadata.UpdateDatabaseAsync(databaseId, new Database { Description = Described })).Description);
        var databaseTags = await TagsAsync(client, database, async () => (await client.Metadata.GetDatabaseAsync(databaseId)).Tags);
        var databasePermissions = await PermissionsAsync(client, database, group.Id!);

        var tables = await AreaFixtures.AllAsync(client.Metadata.ListTablesAsync());
        var tablePage = await client.Metadata.ListTablesPageAsync(1, new TableListOptions { PageSize = 1 });
        var gotTable = await client.Metadata.GetTableAsync(tableId);
        var tableUpdate = await OutcomeAsync(async () => (await client.Metadata.UpdateTableAsync(tableId, new Table { Description = Described })).Description);
        var tableTags = await TagsAsync(client, table, async () => (await client.Metadata.GetTableAsync(tableId)).Tags);
        var tablePermissions = await PermissionsAsync(client, table, group.Id!);

        var columns = await AreaFixtures.AllAsync(client.Metadata.ListColumnsAsync(tableId));
        var columnId = columns[0].Id!;
        var column = TableauContent.Column(tableId, columnId);
        var columnUpdate = await OutcomeAsync(async () => (await client.Metadata.UpdateColumnAsync(tableId, columnId, new Column { Description = Described })).Description);
        var columnTags = await TagsAsync(client, column, async () =>
            (await AreaFixtures.AllAsync(client.Metadata.ListColumnsAsync(tableId))).Single(found => found.Id == columnId).Tags);

        var dataSourceWarnings = await WarningsAsync(client, TableauContent.DataSource(dataSource.Id!), updates: true);
        var columnWarnings = await WarningsAsync(client, column, updates: false);

        await client.Metadata.DeleteColumnAsync(tableId, columnId);
        var columnsAfter = await AreaFixtures.AllAsync(client.Metadata.ListColumnsAsync(tableId));
        await client.Metadata.DeleteTableAsync(tableId);
        await client.Metadata.DeleteDatabaseAsync(databaseId);
        await client.Groups.DeleteAsync(group.Id!);

        Record($"the catalog: {databases.Count} database(s), {tables.Count} table(s), {columns.Count} column(s) of the table; the database embedded: {embedded}");
        Record($"updates: the database {databaseUpdate}, the table {tableUpdate}, the column {columnUpdate}");
        Record($"tags: the database {databaseTags}, the table {tableTags}, the column {columnTags}");
        Record($"permissions: the database {databasePermissions}, the table {tablePermissions}");
        Record($"warnings: on the data source {dataSourceWarnings}; on the column {columnWarnings}");
        while (_details.TryDequeue(out var detail))
            Record($"refused {detail}");
        Assert.IsTrue(databases.Any(found => found.Id == databaseId), "the list holds the samples' database");
        Assert.HasCount(databases.Count, databases.Select(found => found.Id).Distinct(), "no database twice across pages");
        Assert.AreEqual((1, databases.Count), (databasePage.Items.Count, databasePage.TotalAvailable));
        Assert.AreEqual(databaseId, gotDatabase.Id);
        Assert.IsTrue(tables.Any(found => found.Id == tableId), "the list holds the table");
        Assert.AreEqual((1, tables.Count), (tablePage.Items.Count, tablePage.TotalAvailable));
        Assert.AreEqual(tableId, gotTable.Id);
        Assert.IsNotEmpty(columns);
        foreach (var update in new[] { databaseUpdate, tableUpdate, columnUpdate })
            Assert.AreEqual(Described, update, "an update answers the description");
        foreach (var tags in new[] { databaseTags, tableTags, columnTags })
            Assert.AreEqual("tagged once, then untagged", tags);
        // The samples' databases are embedded in their workbooks and data sources, and Tableau Server 2025.1 refuses
        // explicit permissions on such a database or its tables, each call refused 400120, the list too.
        foreach (var permissions in new[] { databasePermissions, tablePermissions })
        {
            Assert.AreEqual(embedded
                ? "added: refused 400120; read: refused 400120; deleted: refused 400120; after: refused 400120"
                : "added: granted; read: granted; deleted: deleted; after: not granted", permissions);
        }
        Assert.AreEqual("added, listed, updated, deleted by id and gone; added again, deleted by content and gone", dataSourceWarnings);
        Assert.AreEqual("added, listed, deleted by content and gone", columnWarnings);
        Assert.IsFalse(columnsAfter.Any(found => found.Id == columnId), "the column is gone after its delete");
        await ClientFixtures.FailsAsync(TableauErrorKind.NotFound, () => client.Metadata.GetTableAsync(tableId));
        await ClientFixtures.FailsAsync(TableauErrorKind.NotFound, () => client.Metadata.GetDatabaseAsync(databaseId));
    }

    // The samples' database and a table of it, by their LUIDs, once the Metadata API lists them upstream of the
    // project's published data source or its workbooks and Query Database answers for the database; one that isn't
    // embedded in a workbook first, since an embedded one takes no permissions of its own; null until then.
    private static async Task<Tuple<string, string, bool>?> AssetsAsync(TableauClient client, string project)
    {
        const string Upstream = """
            query upstream($project: String) {
              publishedDatasourcesConnection(filter: { projectName: $project }) { nodes { upstreamTables { luid database { luid } } } }
              workbooksConnection(filter: { projectName: $project }) { nodes { upstreamTables { luid database { luid } } } }
            }
            """;
        var data = await client.Metadata.QueryAsync(Upstream, new JsonObject { ["project"] = project });
        var found = new[] { "publishedDatasourcesConnection", "workbooksConnection" }
            .SelectMany(connection => data.GetProperty(connection).GetProperty("nodes").EnumerateArray())
            .SelectMany(content => content.GetProperty("upstreamTables").EnumerateArray())
            .Select(table => (Table: Text(table, "luid"), Database: table.TryGetProperty("database", out var database) && database.ValueKind == JsonValueKind.Object
                ? Text(database, "luid") : null))
            .Where(pair => pair.Table is not null && pair.Database is not null)
            .Distinct()
            .ToList();
        Tuple<string, string, bool>? embedded = null;
        foreach (var (table, database) in found)
        {
            try
            {
                var got = await client.Metadata.GetDatabaseAsync(database!);
                await client.Metadata.GetTableAsync(table!);
                if (got.IsEmbedded != true) return Tuple.Create(database!, table!, false);
                embedded ??= Tuple.Create(database!, table!, true);
            }
            catch (TableauException e) when (e.Kind == TableauErrorKind.NotFound)
            {
                // Not in the catalog yet.
            }
        }
        return embedded;

        static string? Text(JsonElement element, string name) =>
            element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && value.GetString() is { Length: > 0 } text ? text : null;
    }

    // A label added and deleted: what the asset held after each, or Tableau's refusal.
    private static Task<string> TagsAsync(TableauClient client, TableauContent content, Func<Task<IReadOnlyList<Tag>?>> tags) => OutcomeAsync(async () =>
    {
        await client.Tags.AddAsync(content, [Label]);
        var tagged = (await tags())?.Count(tag => tag.Label == Label) ?? 0;
        await client.Tags.DeleteAsync(content, Label);
        var untagged = (await tags())?.Count(tag => tag.Label == Label) ?? 0;
        return tagged == 1 && untagged == 0 ? "tagged once, then untagged" : $"held {tagged}, then {untagged}";
    });

    // A run's group granted Read on the asset, the permissions read, the grant deleted and read again: each call's
    // outcome by itself, so an asset that takes no permissions shows each call's answer.
    private static async Task<string> PermissionsAsync(TableauClient client, TableauContent content, string groupId)
    {
        string Grants(Permissions permissions) => permissions.GranteeCapabilities?.Any(rule => rule.Group?.Id == groupId
            && rule.Capabilities!.Any(capability => capability.Name == CapabilityName.Read && capability.Mode == CapabilityMode.Allow)) == true ? "granted" : "not granted";
        var added = await OutcomeAsync(async () =>
            Grants(await client.Permissions.AddAsync(content, [new GranteeCapabilities { Group = new Group { Id = groupId }, Capabilities = _read }])));
        var read = await OutcomeAsync(async () => Grants(await client.Permissions.GetAsync(content)));
        var deleted = await OutcomeAsync(async () =>
        {
            await client.Permissions.DeleteAsync(content, PermissionGrantee.Group(groupId), CapabilityName.Read, CapabilityMode.Allow);
            return "deleted";
        });
        var after = await OutcomeAsync(async () => Grants(await client.Permissions.GetAsync(content)));
        return $"added: {added}; read: {read}; deleted: {deleted}; after: {after}";
    }

    // A warning added, listed, updated where asked, deleted by id; then added again and deleted by content; each step's
    // outcome, a refusal by its code.
    private static async Task<string> WarningsAsync(TableauClient client, TableauContent content, bool updates)
    {
        async Task<List<DataQualityWarning>> ListAsync() => await AreaFixtures.AllAsync(client.Metadata.ListDataQualityWarningsAsync(content));
        var steps = new List<string>();
        DataQualityWarning added;
        try
        {
            added = await client.Metadata.AddDataQualityWarningAsync(content, new DataQualityWarning { Type = "WARNING", Message = Described, IsActive = true });
        }
        catch (TableauException e) when (e.StatusCode is not null)
        {
            // The list and the delete by content still answer, with no warning to show.
            var listed = await OutcomeAsync(async () => $"{(await ListAsync()).Count} listed");
            var deleted = await OutcomeAsync(async () =>
            {
                await client.Metadata.DeleteDataQualityWarningsAsync(content);
                return "deleted by content";
            });
            return $"add refused {e.ErrorCode}; {listed}; {deleted}";
        }
        steps.Add((await ListAsync()).Any(warning => warning.Id == added.Id) ? "added, listed" : "added, not listed");
        if (updates)
        {
            var updated = await client.Metadata.UpdateDataQualityWarningAsync(added.Id!, new DataQualityWarning { Type = "DEPRECATED", Message = Described + " Again.", IsSevere = true });
            steps.Add(updated is { Type: "DEPRECATED", IsSevere: true } && updated.Message == Described + " Again." ? "updated" : "updated otherwise");
            await client.Metadata.DeleteDataQualityWarningAsync(added.Id!);
            steps.Add((await ListAsync()).Count == 0 ? "deleted by id and gone; added again" : "deleted by id, still listed");
            await client.Metadata.AddDataQualityWarningAsync(content, new DataQualityWarning { Type = "WARNING", Message = Described, IsActive = true });
        }
        await client.Metadata.DeleteDataQualityWarningsAsync(content);
        steps.Add((await ListAsync()).Count == 0 ? "deleted by content and gone" : "deleted by content, still listed");
        return string.Join(", ", steps);
    }

    // What a call gave, or Tableau's refusal by its code.
    private static async Task<string> OutcomeAsync(Func<Task<string?>> call)
    {
        try
        {
            return await call() ?? "(nothing)";
        }
        catch (TableauException e) when (e.StatusCode is not null)
        {
            _details.Enqueue($"{e.ErrorCode}: {e.Method} {e.RequestPath}: {e.Summary}: {e.Detail}");
            return $"refused {e.ErrorCode ?? ((int)e.StatusCode).ToString(System.Globalization.CultureInfo.InvariantCulture)}";
        }
    }

    // Each refusal's detail, for the test's output.
    private static readonly System.Collections.Concurrent.ConcurrentQueue<string> _details = new();

    private void Record(string line) => TestContext.WriteLine(LiveSite.Scrubbed(line));
}
