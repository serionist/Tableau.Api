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
/// How a list pages: <c>ListAsync</c> reads lazily and stops at <c>totalAvailable</c> or on a short page, never asking
/// for a page past the end; cancellation stops it; a page's number and size are checked before sending; a list is
/// never null; and a mock that overrides <c>ListPageAsync</c> serves <c>ListAsync</c> too.
/// </summary>
[TestClass]
public sealed class PagingTests
{
    private const string Users = AreaFixtures.SitePath + "/users";

    [TestMethod]
    public async Task ListStopsAtTheTotalWithoutAskingForAPagePastIt()
    {
        using var server = AreaFixtures.Server();
        server.Map("GET", Users, request => Page(Number(request), size: 2, total: 4, count: 2));
        await using var client = AreaFixtures.Client(server);

        var users = await AreaFixtures.AllAsync(client.Users.ListAsync(new UserListOptions { PageSize = 2 }));

        Assert.HasCount(4, users);
        CollectionAssert.AreEqual(new[] { 1, 2 }, AreaFixtures.Calls(server).Select(Number).ToArray());
    }

    [TestMethod]
    public async Task ListStopsOnAShortPageWhateverTheTotalSays()
    {
        using var server = AreaFixtures.Server();
        // A total above what the list holds, as Tableau Server 2025.1 answered a case-insensitive filter on users with a
        // total of 1 and no users: the short second page ends the list, not the total.
        server.Map("GET", Users, request => Number(request) == 1 ? Page(1, size: 3, total: 9, count: 3) : Page(2, size: 3, total: 9, count: 1));
        await using var client = AreaFixtures.Client(server);

        var users = await AreaFixtures.AllAsync(client.Users.ListAsync(new UserListOptions { PageSize = 3 }));

        Assert.HasCount(4, users);
        Assert.HasCount(2, AreaFixtures.Calls(server));
    }

    [TestMethod]
    public async Task ListStopsOnAnEmptyFirstPageAndAPageWithoutUsersIsEmpty()
    {
        using var server = AreaFixtures.Server();
        server.Map("GET", Users, FakeResponse.Json(200, """{"pagination":{"pageNumber":"1","pageSize":"100","totalAvailable":"1"},"users":{}}"""));
        server.Map("GET", AreaFixtures.SitePath + "/projects", FakeResponse.Json(200, """{"pagination":{"pageNumber":"1","pageSize":"100","totalAvailable":"0"}}"""));
        await using var client = AreaFixtures.Client(server);

        var users = await AreaFixtures.AllAsync(client.Users.ListAsync());
        var page = await client.Projects.ListPageAsync(1);

        Assert.IsEmpty(users);
        Assert.IsNotNull(page.Items);
        Assert.IsEmpty(page.Items);
        Assert.HasCount(2, AreaFixtures.Calls(server));
    }

    [TestMethod]
    public async Task APageWithoutPaginationTakesTheNumberAndSizeAskedFor()
    {
        using var server = AreaFixtures.Server();
        server.Map("GET", Users, FakeResponse.Json(200, """{"users":{"user":[{"name":"ada"}]}}"""));
        await using var client = AreaFixtures.Client(server);

        var page = await client.Users.ListPageAsync(4, new UserListOptions { PageSize = 7 });

        Assert.AreEqual((4, 7, (int?)null), (page.PageNumber, page.PageSize, page.TotalAvailable));
        Assert.AreEqual(new Page<User>(page.Items, 4, 7, null), page);
    }

    [TestMethod]
    public async Task AnAnswerWithoutABodyIsAnInvalidResponse()
    {
        using var server = AreaFixtures.Server();
        server.Map("GET", Users, FakeResponse.Empty(200));
        await using var client = AreaFixtures.Client(server);

        var thrown = await ClientFixtures.FailsAsync(TableauErrorKind.InvalidResponse, () => client.Users.ListPageAsync(1));

        Assert.Contains("Get Users on Site", thrown.Message);
    }

    [TestMethod]
    public async Task CancellingTheListStopsItBetweenPagesAndBetweenItems()
    {
        using var server = AreaFixtures.Server();
        server.Map("GET", Users, request => Page(Number(request), size: 2, total: 10, count: 2));
        await using var client = AreaFixtures.Client(server);
        using var cancel = new CancellationTokenSource();

        var read = new List<User>();
        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            await foreach (var user in client.Users.ListAsync(new UserListOptions { PageSize = 2 }).WithCancellation(cancel.Token))
            {
                read.Add(user);
                if (read.Count == 3) await cancel.CancelAsync();
            }
        });

        Assert.HasCount(3, read);
        Assert.HasCount(2, AreaFixtures.Calls(server), "no page after the cancellation");
    }

    [TestMethod]
    public async Task AServerThatAnswersAnEarlierPageFailsTheListRatherThanRepeatIt()
    {
        using var server = AreaFixtures.Server();
        server.Map("GET", Users, _ => Page(1, size: 2, total: 10, count: 2));
        await using var client = AreaFixtures.Client(server);
        var read = new List<User>();

        var thrown = await ClientFixtures.FailsAsync(TableauErrorKind.InvalidResponse, async () =>
        {
            await foreach (var user in client.Users.ListAsync(new UserListOptions { PageSize = 2 }))
                read.Add(user);
        });

        Assert.HasCount(2, read);
        Assert.HasCount(2, AreaFixtures.Calls(server));
        Assert.Contains("Get Users on Site answered page 1 when asked for page 2", thrown.Message);
    }

    [TestMethod]
    public async Task AFavoritesServerThatIgnoresThePageFailsTheListRatherThanRepeatIt()
    {
        using var server = AreaFixtures.Server();
        var full = "{\"favorites\":{\"favorite\":[" + string.Join(',', Enumerable.Range(0, 100).Select(i => $"{{\"label\":\"f{i}\"}}")) + "]}}";
        server.Map("GET", $"{AreaFixtures.SitePath}/favorites/{Fixtures.UserId}", FakeResponse.Json(200, full));
        await using var client = AreaFixtures.Client(server);
        var read = 0;

        var thrown = await ClientFixtures.FailsAsync(TableauErrorKind.InvalidResponse, async () =>
        {
            await foreach (var _ in client.Favorites.ListAsync(Fixtures.UserId))
                read++;
        });

        Assert.AreEqual(100, read);
        Assert.HasCount(2, AreaFixtures.Calls(server));
        Assert.Contains("Get Favorites for User answered page 1 when asked for page 2", thrown.Message);
    }

    [TestMethod]
    public async Task AListLeftPartWayAsksForNoMorePages()
    {
        using var server = AreaFixtures.Server();
        server.Map("GET", Users, request => Page(Number(request), size: 2, total: 10, count: 2));
        server.Map("GET", $"{Users}/{Fixtures.UserId}/groups", FakeResponse.Fixture(200, "groups-page.json"));
        server.Map("GET", $"{AreaFixtures.SitePath}/groups/{AreaFixtures.GroupId}/users", FakeResponse.Fixture(200, "users-in-group.json"));
        server.Map("GET", $"{AreaFixtures.SitePath}/favorites/{Fixtures.UserId}", FakeResponse.Fixture(200, "favorites.json"));
        server.Map("GET", AreaFixtures.SitePath + "/projects", FakeResponse.Fixture(200, "projects-page.json"));
        server.Map("GET", AreaFixtures.SitePath + "/groups", FakeResponse.Fixture(200, "groups-page.json"));
        server.Map("GET", AreaFixtures.SitesPath, FakeResponse.Fixture(200, "sites-page.json"));
        var workbook = $"{AreaFixtures.SitePath}/workbooks/{AreaFixtures.ItemId}";
        var dataSource = $"{AreaFixtures.SitePath}/datasources/{AreaFixtures.ItemId}";
        server.Map("GET", AreaFixtures.SitePath + "/workbooks", FakeResponse.Fixture(200, "workbooks-page.json"));
        server.Map("GET", $"{Users}/{Fixtures.UserId}/workbooks", FakeResponse.Fixture(200, "workbooks-page.json"));
        server.Map("GET", workbook + "/connections", FakeResponse.Fixture(200, "connections.json"));
        server.Map("GET", workbook + "/revisions", FakeResponse.Fixture(200, "revisions-page.json"));
        server.Map("GET", workbook + "/views", FakeResponse.Fixture(200, "views-page.json"));
        server.Map("GET", AreaFixtures.SitePath + "/views", FakeResponse.Fixture(200, "views-page.json"));
        server.Map("GET", AreaFixtures.SitePath + "/datasources", FakeResponse.Fixture(200, "datasources-page.json"));
        server.Map("GET", dataSource + "/connections", FakeResponse.Fixture(200, "connections.json"));
        server.Map("GET", dataSource + "/revisions", FakeResponse.Fixture(200, "revisions-page.json"));
        await using var client = AreaFixtures.Client(server);

        var first = new object?[]
        {
            await FirstAsync(client.Users.ListAsync(new UserListOptions { PageSize = 2 })),
            await FirstAsync(client.Users.ListGroupsAsync(Fixtures.UserId)),
            await FirstAsync(client.Groups.ListUsersAsync(AreaFixtures.GroupId)),
            await FirstAsync(client.Favorites.ListAsync(Fixtures.UserId)),
            await FirstAsync(client.Projects.ListAsync(new ProjectListOptions { PageSize = 3 })),
            await FirstAsync(client.Groups.ListAsync()),
            await FirstAsync(client.Sites.ListAsync()),
            await FirstAsync(client.Workbooks.ListAsync(new WorkbookListOptions { PageSize = 2 })),
            await FirstAsync(client.Workbooks.ListForUserAsync(Fixtures.UserId)),
            await FirstAsync(client.Workbooks.ListConnectionsAsync(AreaFixtures.ItemId)),
            await FirstAsync(client.Workbooks.ListRevisionsAsync(AreaFixtures.ItemId, new RevisionListOptions { PageSize = 2 })),
            await FirstAsync(client.Views.ListForWorkbookAsync(AreaFixtures.ItemId)),
            await FirstAsync(client.Views.ListAsync()),
            await FirstAsync(client.DataSources.ListAsync()),
            await FirstAsync(client.DataSources.ListConnectionsAsync(AreaFixtures.ItemId)),
            await FirstAsync(client.DataSources.ListRevisionsAsync(AreaFixtures.ItemId, new RevisionListOptions { PageSize = 2 })),
        };

        Assert.IsTrue(first.All(item => item is not null));
        Assert.HasCount(first.Length, AreaFixtures.Calls(server), "one page each");
    }

    [TestMethod]
    public async Task ACancelledListSendsNothing()
    {
        using var server = AreaFixtures.Server();
        await using var client = AreaFixtures.Client(server);
        using var cancel = new CancellationTokenSource();
        await cancel.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(() => AreaFixtures.AllAsync(client.Projects.ListAsync(cancellationToken: cancel.Token)));
        await Assert.ThrowsAsync<OperationCanceledException>(() => AreaFixtures.AllAsync(client.Groups.ListUsersAsync(AreaFixtures.GroupId, cancel.Token)));

        Assert.IsEmpty(server.Requests);
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(1001)]
    [DataRow(-1)]
    public async Task APageSizeOutsideOneToAThousandIsRefusedBeforeSending(int size)
    {
        using var server = AreaFixtures.Server();
        await using var client = AreaFixtures.Client(server);

        foreach (var call in new Func<Task>[]
                 {
                     () => client.Projects.ListPageAsync(1, new ProjectListOptions { PageSize = size }),
                     () => client.Users.ListPageAsync(1, new UserListOptions { PageSize = size }),
                     () => client.Groups.ListPageAsync(1, new GroupListOptions { PageSize = size }),
                     () => client.Sites.ListPageAsync(1, new SiteListOptions { PageSize = size }),
                     () => AreaFixtures.AllAsync(client.Projects.ListAsync(new ProjectListOptions { PageSize = size })),
                 })
        {
            var thrown = await AreaFixtures.RefusedAsync<ArgumentOutOfRangeException>(server, call);
            Assert.AreEqual("options", thrown.ParamName);
        }
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(-5)]
    public async Task APageNumberBelowOneIsRefusedBeforeSending(int number)
    {
        using var server = AreaFixtures.Server();
        await using var client = AreaFixtures.Client(server);

        var thrown = await AreaFixtures.RefusedAsync<ArgumentOutOfRangeException>(server, () => client.Groups.ListPageAsync(number));

        Assert.AreEqual("pageNumber", thrown.ParamName);
    }

    [TestMethod]
    public async Task APageOfAThousandIsAskedFor()
    {
        using var server = AreaFixtures.Server();
        server.Map("GET", Users, FakeResponse.Fixture(200, "users-page.json"));
        await using var client = AreaFixtures.Client(server);

        await client.Users.ListPageAsync(1, new UserListOptions { PageSize = 1000 });

        Assert.AreEqual("1000", AreaFixtures.Calls(server).Single().QueryParameters.Single(pair => pair.Key == "pageSize").Value);
    }

    [TestMethod]
    public async Task AMockThatOverridesListPageServesList()
    {
        var mock = new PagedUsers();

        var users = await AreaFixtures.AllAsync(mock.ListAsync(new UserListOptions { PageSize = 2 }));

        CollectionAssert.AreEqual(new[] { "u1", "u2", "u3" }, users.Select(user => user.Name).ToArray());
        CollectionAssert.AreEqual(new[] { 1, 2 }, mock.Asked);
    }

    [TestMethod]
    public void TheOptionsDefaultToAHundredAPageAndNoFilterOrSort()
    {
        Assert.AreEqual(100, new ProjectListOptions().PageSize);
        Assert.AreEqual(100, new UserListOptions().PageSize);
        Assert.AreEqual(100, new GroupListOptions().PageSize);
        Assert.AreEqual(100, new SiteListOptions().PageSize);
        Assert.IsNull(new ProjectListOptions().Filter);
        Assert.IsNull(new GroupListOptions().Sort);
        Assert.AreEqual(new UserListOptions { PageSize = 5 }, new UserListOptions { PageSize = 5 });
    }

    // The first item, the list left there.
    private static async Task<T?> FirstAsync<T>(IAsyncEnumerable<T> items)
    {
        await foreach (var item in items)
            return item;
        return default;
    }

    private static int Number(RecordedRequest request) =>
        int.Parse(request.QueryParameters.Single(pair => pair.Key == "pageNumber").Value, System.Globalization.CultureInfo.InvariantCulture);

    private static FakeResponse Page(int number, int size, int total, int count)
    {
        var users = string.Join(',', Enumerable.Range(0, count).Select(i => $$"""{"name":"u{{number}}-{{i}}"}"""));
        return FakeResponse.Json(200, $$$"""{"pagination":{"pageNumber":"{{{number}}}","pageSize":"{{{size}}}","totalAvailable":"{{{total}}}"},"users":{"user":[{{{users}}}]}}""");
    }

    [TestMethod]
    public async Task AMockThatOverridesAContentPageServesItsList()
    {
        var revisions = await AreaFixtures.AllAsync(new PagedRevisions().ListRevisionsAsync(AreaFixtures.ItemId, new RevisionListOptions { PageSize = 2 }));
        var views = await AreaFixtures.AllAsync(new PagedViews().ListAsync(new ViewListOptions { PageSize = 2 }));
        var dataSources = await AreaFixtures.AllAsync(new PagedDataSources().ListAsync(new DataSourceListOptions { PageSize = 2 }));

        CollectionAssert.AreEqual(new long?[] { 1, 2, 3 }, revisions.Select(revision => revision.RevisionNumber).ToArray());
        CollectionAssert.AreEqual(new[] { "v1", "v2", "v3" }, views.Select(view => view.Name).ToArray());
        CollectionAssert.AreEqual(new[] { "d1", "d2", "d3" }, dataSources.Select(dataSource => dataSource.Name).ToArray());
    }

    /// <summary>A mock of a workbook's revisions: three, two a page.</summary>
    private sealed class PagedRevisions : WorkbooksClient
    {
        public override Task<Page<Revision>> ListRevisionsPageAsync(string id, int pageNumber, RevisionListOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            IReadOnlyList<Revision> items = pageNumber == 1 ? [new Revision { RevisionNumber = 1 }, new Revision { RevisionNumber = 2 }] : [new Revision { RevisionNumber = 3 }];
            return Task.FromResult(new Page<Revision>(items, pageNumber, options!.PageSize, 3));
        }
    }

    /// <summary>A mock of the views' sub-client: three views, two a page.</summary>
    private sealed class PagedViews : ViewsClient
    {
        public override Task<Page<View>> ListPageAsync(int pageNumber, ViewListOptions? options = null, CancellationToken cancellationToken = default)
        {
            IReadOnlyList<View> items = pageNumber == 1 ? [new View { Name = "v1" }, new View { Name = "v2" }] : [new View { Name = "v3" }];
            return Task.FromResult(new Page<View>(items, pageNumber, options!.PageSize, 3));
        }
    }

    /// <summary>A mock of the data sources' sub-client: three, two a page.</summary>
    private sealed class PagedDataSources : DataSourcesClient
    {
        public override Task<Page<DataSource>> ListPageAsync(int pageNumber, DataSourceListOptions? options = null, CancellationToken cancellationToken = default)
        {
            IReadOnlyList<DataSource> items = pageNumber == 1 ? [new DataSource { Name = "d1" }, new DataSource { Name = "d2" }] : [new DataSource { Name = "d3" }];
            return Task.FromResult(new Page<DataSource>(items, pageNumber, options!.PageSize, 3));
        }
    }

    /// <summary>A mock of the users' sub-client that serves pages from memory: three users, two a page.</summary>
    private sealed class PagedUsers : UsersClient
    {
        public List<int> Asked { get; } = [];

        public override Task<Page<User>> ListPageAsync(int pageNumber, UserListOptions? options = null, CancellationToken cancellationToken = default)
        {
            Asked.Add(pageNumber);
            IReadOnlyList<User> items = pageNumber == 1 ? [new User { Name = "u1" }, new User { Name = "u2" }] : [new User { Name = "u3" }];
            return Task.FromResult(new Page<User>(items, pageNumber, options!.PageSize, 3));
        }
    }
}
