using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Tableau.Api.Pipeline;
using Tableau.Api.Rest.Models;
using Tableau.Api.Tests.Fakes;

namespace Tableau.Api.Tests;

/// <summary>
/// <see cref="FavoritesClient"/> over <see cref="TableauContent"/>: each kind of content's add and delete as the reference
/// gives them, each from its own version, a kind favorites don't take refused before sending, and the list read from a
/// fixture.
/// </summary>
[TestClass]
public sealed class FavoritesTests
{
    private const string Favorites = AreaFixtures.SitePath + "/favorites/" + Fixtures.UserId;

    public static IEnumerable<object[]> Kinds =>
    [
        ["workbook", "workbooks", "1.0", "2.0"],
        ["view", "views", "1.0", "2.0"],
        ["datasource", "datasources", "2.3", "2.3"],
        ["project", "projects", "3.1", "3.1"],
        ["flow", "flows", "3.3", "3.3"],
    ];

    /// <summary>The kinds favorites don't take, each made from a valid LUID.</summary>
    public static IEnumerable<object[]> Refused =>
    [
        [TableauContent.VirtualConnection(AreaFixtures.ItemId)],
        [TableauContent.Collection(AreaFixtures.ItemId)],
        [TableauContent.Database(AreaFixtures.ItemId)],
        [TableauContent.Table(AreaFixtures.ItemId)],
        [TableauContent.Column(AreaFixtures.ItemId, AreaFixtures.ProjectId)],
    ];

    [TestMethod]
    public async Task ListReadsTheFavoritesAFavoriteOfDeletedContentAmongThem()
    {
        using var server = AreaFixtures.Server();
        server.Map("GET", Favorites, FakeResponse.Fixture(200, "favorites.json"));
        await using var client = AreaFixtures.Client(server, TableauDeployment.Cloud);

        var favorites = await AreaFixtures.AllAsync(client.Favorites.ListAsync(Fixtures.UserId));

        AreaFixtures.Sent(server, $"GET {Favorites}?pageSize=100&pageNumber=1");
        CollectionAssert.AreEqual(new[] { "Global indicators", "Finance", "A workbook since deleted" }, favorites.Select(favorite => favorite.Label).ToArray());
        Assert.AreEqual(("Overview", 1L), (favorites[0].View?.Name, favorites[0].Position));
        Assert.AreEqual("Finance", favorites[1].Project?.Name);
        Assert.IsTrue(favorites[2] is { View: null, Project: null, Workbook: null, Datasource: null, Flow: null });
    }

    [TestMethod]
    public async Task AFullPageWithNoPaginationAsksForTheNextUntilAShortOne()
    {
        using var server = AreaFixtures.Server();
        var full = "{\"favorites\":{\"favorite\":[" + string.Join(',', Enumerable.Range(0, 100).Select(i => $"{{\"label\":\"f{i}\"}}")) + "]}}";
        server.Map("GET", Favorites, request => request.Query.Contains("pageNumber=1", StringComparison.Ordinal)
            ? FakeResponse.Json(200, full)
            : FakeResponse.Json(200, """{"favorites":{}}"""));
        await using var client = AreaFixtures.Client(server);

        var favorites = await AreaFixtures.AllAsync(client.Favorites.ListAsync(Fixtures.UserId));

        Assert.HasCount(100, favorites);
        Assert.HasCount(2, AreaFixtures.Calls(server));
    }

    [TestMethod]
    [DynamicData(nameof(Kinds))]
    public async Task AddPutsTheLabelAndTheContentAndReadsTheFavorites(string kind, string segment, string addedFrom, string deletedFrom)
    {
        using var server = AreaFixtures.Server();
        server.Map("PUT", Favorites, FakeResponse.Fixture(200, "favorites.json"));
        server.Map("DELETE", $"{Favorites}/{segment}/{AreaFixtures.ItemId}", FakeResponse.Empty(204));
        await using var client = AreaFixtures.Client(server);
        var (content, add, delete) = Target(kind, AreaFixtures.ItemId);

        var favorites = await client.Favorites.AddAsync(Fixtures.UserId, "R&D, C++ #1", content);
        await client.Favorites.RemoveAsync(Fixtures.UserId, content);

        var calls = AreaFixtures.Calls(server);
        CollectionAssert.AreEqual(new[] { $"PUT {Favorites}", $"DELETE {Favorites}/{segment}/{AreaFixtures.ItemId}" },
            calls.Select(call => $"{call.Method} {call.Target}").ToArray());
        Assert.AreEqual(AreaFixtures.Json($$$"""{"favorite":{"{{{kind}}}":{"id":"{{{AreaFixtures.ItemId}}}"},"label":"R&D, C++ #1"}}"""), AreaFixtures.Json(calls[0].BodyText));
        Assert.HasCount(3, favorites);
        Assert.AreEqual(Version.Parse(addedFrom), add.MinVersion);
        Assert.AreEqual(Version.Parse(deletedFrom), delete.MinVersion);
        Assert.IsTrue(add.Idempotent && delete.Idempotent);
    }

    [TestMethod]
    [DynamicData(nameof(Refused))]
    public async Task AKindFavoritesDontTakeIsRefusedBeforeSending(TableauContent content)
    {
        using var server = AreaFixtures.Server();
        await using var client = AreaFixtures.Client(server);

        var added = await AreaFixtures.RefusedAsync<ArgumentException>(server, () => client.Favorites.AddAsync(Fixtures.UserId, "Finance", content));
        var removed = await AreaFixtures.RefusedAsync<ArgumentException>(server, () => client.Favorites.RemoveAsync(Fixtures.UserId, content));

        Assert.AreEqual(("content", "content"), (added.ParamName, removed.ParamName));
        Assert.StartsWith("A favorite takes a workbook, a view, a data source, a project or a flow, not a ", added.Message);
    }

    [TestMethod]
    public async Task AnAnswerWithoutFavoritesIsAnEmptyList()
    {
        using var server = AreaFixtures.Server();
        server.Map("PUT", Favorites, FakeResponse.Json(200, "{}"));
        await using var client = AreaFixtures.Client(server);

        var favorites = await client.Favorites.AddAsync(Fixtures.UserId, "Finance", TableauContent.Project(AreaFixtures.ProjectId));

        Assert.IsNotNull(favorites);
        Assert.IsEmpty(favorites);
    }

    [TestMethod]
    public async Task ARefusedArgumentSendsNothing()
    {
        using var server = AreaFixtures.Server();
        await using var client = AreaFixtures.Client(server);
        var content = TableauContent.Workbook(AreaFixtures.ItemId);

        Assert.AreEqual("userId", (await AreaFixtures.RefusedAsync<ArgumentException>(server, () => client.Favorites.AddAsync("ada", "x", content))).ParamName);
        Assert.AreEqual("label", (await AreaFixtures.RefusedAsync<ArgumentException>(server, () => client.Favorites.AddAsync(Fixtures.UserId, "", content))).ParamName);
        await AreaFixtures.RefusedAsync<ArgumentNullException>(server, () => client.Favorites.AddAsync(Fixtures.UserId, "x", null!));
        Assert.AreEqual("userId", (await AreaFixtures.RefusedAsync<ArgumentException>(server, () => client.Favorites.RemoveAsync("ada", content))).ParamName);
        await AreaFixtures.RefusedAsync<ArgumentNullException>(server, () => client.Favorites.RemoveAsync(Fixtures.UserId, null!));
        Assert.AreEqual("userId", (await AreaFixtures.RefusedAsync<ArgumentException>(server, () => AreaFixtures.AllAsync(client.Favorites.ListAsync("ada")))).ParamName);
    }

    [TestMethod]
    public async Task AKindTheServerIsTooOldForIsRefusedBeforeSending()
    {
        using var server = AreaFixtures.Server();
        var options = SessionFixtures.Options(server, version: "3.2");
        options.AllowUnsupportedServerVersion = true;
        await using var client = SessionFixtures.Client(options, SessionFixtures.Pat());

        var thrown = await ClientFixtures.FailsAsync(TableauErrorKind.UnsupportedApiVersion,
            () => client.Favorites.AddAsync(Fixtures.UserId, "Prep", TableauContent.Flow(AreaFixtures.ItemId)));

        Assert.Contains("Add Flow to Favorites needs REST API 3.3", thrown.Message);
        Assert.IsEmpty(server.Requests);
    }

    private static (TableauContent Content, Endpoint Add, Endpoint Delete) Target(string kind, string id) => kind switch
    {
        "workbook" => (TableauContent.Workbook(id), Endpoints.AddWorkbookToFavorites, Endpoints.DeleteWorkbookFromFavorites),
        "view" => (TableauContent.View(id), Endpoints.AddViewToFavorites, Endpoints.DeleteViewFromFavorites),
        "datasource" => (TableauContent.DataSource(id), Endpoints.AddDataSourceToFavorites, Endpoints.DeleteDataSourceFromFavorites),
        "project" => (TableauContent.Project(id), Endpoints.AddProjectToFavorites, Endpoints.DeleteProjectFromFavorites),
        _ => (TableauContent.Flow(id), Endpoints.AddFlowToFavorites, Endpoints.DeleteFlowFromFavorites),
    };
}
