using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Tableau.Api.Rest.Models;
using Tableau.Api.Tests.Fakes;

namespace Tableau.Api.Tests;

/// <summary>
/// <see cref="GroupsClient"/>: each call's method, path, query and body as the reference gives them, and its answer read
/// from a fixture; an Active Directory group's update goes to the group's own path, and Active Directory is refused on
/// Tableau Cloud before anything is sent.
/// </summary>
[TestClass]
public sealed class GroupsTests
{
    private const string Groups = AreaFixtures.SitePath + "/groups";
    private const string Group = Groups + "/" + AreaFixtures.GroupId;

    [TestMethod]
    public async Task ListPageSortsByNameUnlessToldAndReadsTheGroups()
    {
        using var server = AreaFixtures.Server();
        server.Map("GET", Groups, FakeResponse.Fixture(200, "groups-page.json"));
        await using var client = AreaFixtures.Client(server, TableauDeployment.Cloud);

        var page = await client.Groups.ListPageAsync(1);

        AreaFixtures.Sent(server, $"GET {Groups}?pageSize=100&pageNumber=1&sort=name:asc");
        Assert.AreEqual(3, page.TotalAvailable);
        var local = page.Items[1];
        Assert.AreEqual((SiteRole.Explorer, ImportDirectiveGrantLicenseMode.OnLogin), (local.MinimumSiteRole, local.Import?.GrantLicenseMode));
        Assert.AreEqual("example.org", page.Items[2].Domain?.Name);
    }

    [TestMethod]
    public async Task ListSendsItsFilterAndSort()
    {
        using var server = AreaFixtures.Server();
        server.Map("GET", Groups, FakeResponse.Fixture(200, "groups-page.json"));
        await using var client = AreaFixtures.Client(server);

        var groups = await AreaFixtures.AllAsync(client.Groups.ListAsync(new GroupListOptions
        {
            Filter = TableauFilter.And(TableauFilter.Eq(GroupField.IsLocal, true), TableauFilter.Gt(GroupField.UserCount, 0)),
            Sort = TableauSort.Desc(GroupField.UserCount),
        }));

        AreaFixtures.Sent(server, $"GET {Groups}?pageSize=100&pageNumber=1&filter=isLocal:eq:true,userCount:gt:0&sort=userCount:desc,name:asc");
        Assert.HasCount(3, groups);
    }

    [TestMethod]
    public async Task CreateSendsALocalGroupOnce()
    {
        using var server = AreaFixtures.Server();
        server.Map("POST", Groups, FakeResponse.Fixture(201, "group.json"));
        await using var client = AreaFixtures.Client(server, TableauDeployment.Cloud);

        await client.Groups.CreateAsync("Finance analysts");
        var group = await client.Groups.CreateAsync("Finance analysts", SiteRole.Explorer, externalUserEnabled: false);

        CollectionAssert.AreEqual(new[]
        {
            """{"group":{"name":"Finance analysts"}}""",
            """{"group":{"externalUserEnabled":false,"minimumSiteRole":"Explorer","name":"Finance analysts"}}""",
        }.Select(AreaFixtures.Json).ToArray(), AreaFixtures.Bodies(server));
        Assert.IsTrue(AreaFixtures.Calls(server).All(call => call.Target == Groups));
        Assert.AreEqual(AreaFixtures.GroupId, group.Id);
    }

    [TestMethod]
    public async Task ImportFromActiveDirectorySendsTheDirectoryGroupAndReadsTheGroup()
    {
        using var server = AreaFixtures.Server();
        server.Map("POST", Groups, FakeResponse.Fixture(201, "group-imported.json"));
        await using var client = AreaFixtures.Client(server);

        var group = await client.Groups.ImportFromActiveDirectoryAsync("Finance", "example.org", SiteRole.Viewer,
            ImportDirectiveGrantLicenseMode.OnSync);

        var call = AreaFixtures.Sent(server, $"POST {Groups}",
            """{"group":{"import":{"domainName":"example.org","grantLicenseMode":"onSync","siteRole":"Viewer","source":"ActiveDirectory"},"name":"Finance"}}""");
        ModelFixtures.AssertSameJson(ModelFixtures.Text("import-group-request.json"), call.BodyText);
        Assert.AreEqual("example.org", group.Import?.DomainName);
    }

    [TestMethod]
    public async Task ImportFromActiveDirectoryAsAJobAsksForOneAndReadsIt()
    {
        using var server = AreaFixtures.Server();
        server.Map("POST", Groups, FakeResponse.Fixture(202, "group-import-job.json"));
        await using var client = AreaFixtures.Client(server);

        var job = await client.Groups.ImportFromActiveDirectoryAsJobAsync("Finance", "example.org", SiteRole.Viewer);

        AreaFixtures.Sent(server, $"POST {Groups}?asJob=true",
            """{"group":{"import":{"domainName":"example.org","siteRole":"Viewer","source":"ActiveDirectory"},"name":"Finance"}}""");
        Assert.AreEqual(("GroupImport", JobMode.Asynchronous, 0L), (job.Type?.Value, job.Mode, job.Progress));
    }

    [TestMethod]
    public async Task UpdateFromActiveDirectoryGoesToTheGroupsOwnPath()
    {
        using var server = AreaFixtures.Server();
        server.Map("PUT", Group, request => FakeResponse.Fixture(request.Query.Length == 0 ? 200 : 202,
            request.Query.Length == 0 ? "group-imported.json" : "group-sync-job.json"));
        await using var client = AreaFixtures.Client(server);

        var group = await client.Groups.UpdateFromActiveDirectoryAsync(AreaFixtures.GroupId, "Finance", "example.org", SiteRole.Viewer,
            ImportDirectiveGrantLicenseMode.OnSync);
        var job = await client.Groups.UpdateFromActiveDirectoryAsJobAsync(AreaFixtures.GroupId, "Finance", "example.org", SiteRole.Viewer);

        var calls = AreaFixtures.Calls(server);
        CollectionAssert.AreEqual(new[] { $"PUT {Group}", $"PUT {Group}?asJob=true" }, calls.Select(call => $"{call.Method} {call.Target}").ToArray());
        ModelFixtures.AssertSameJson(ModelFixtures.Text("import-group-request.json"), calls[0].BodyText);
        Assert.AreEqual("Finance", group.Name);
        Assert.AreEqual(JobType.GroupSync, job.Type);
    }

    [TestMethod]
    public async Task UpdateSendsOnlyWhatIsSet()
    {
        using var server = AreaFixtures.Server();
        server.Map("PUT", Group, FakeResponse.Fixture(200, "group.json"));
        await using var client = AreaFixtures.Client(server, TableauDeployment.Cloud);

        await client.Groups.UpdateAsync(AreaFixtures.GroupId, name: "Finance analysts");
        await client.Groups.UpdateAsync(AreaFixtures.GroupId, minimumSiteRole: SiteRole.Unlicensed, externalUserEnabled: true);

        CollectionAssert.AreEqual(new[]
        {
            """{"group":{"name":"Finance analysts"}}""",
            """{"group":{"externalUserEnabled":true,"minimumSiteRole":"Unlicensed"}}""",
        }.Select(AreaFixtures.Json).ToArray(), AreaFixtures.Bodies(server));
    }

    [TestMethod]
    public async Task DeleteSendsADeleteToTheGroup()
    {
        using var server = AreaFixtures.Server();
        server.Map("DELETE", Group, FakeResponse.Empty(204));
        await using var client = AreaFixtures.Client(server, TableauDeployment.Cloud);

        await client.Groups.DeleteAsync(AreaFixtures.GroupId);

        AreaFixtures.Sent(server, $"DELETE {Group}");
    }

    [TestMethod]
    public async Task AddUserSendsTheUsersIdAndReadsTheUser()
    {
        using var server = AreaFixtures.Server();
        server.Map("POST", $"{Group}/users", FakeResponse.Fixture(200, "user.json"));
        await using var client = AreaFixtures.Client(server);

        var user = await client.Groups.AddUserAsync(AreaFixtures.GroupId, Fixtures.UserId);

        AreaFixtures.Sent(server, $"POST {Group}/users", $$$"""{"user":{"id":"{{{Fixtures.UserId}}}"}}""");
        Assert.AreEqual("ada", user.Name);
    }

    [TestMethod]
    public async Task RemoveUserSendsADeleteToTheMembership()
    {
        using var server = AreaFixtures.Server();
        server.Map("DELETE", $"{Group}/users/{Fixtures.UserId}", FakeResponse.Empty(204));
        await using var client = AreaFixtures.Client(server);

        await client.Groups.RemoveUserAsync(AreaFixtures.GroupId, Fixtures.UserId);

        AreaFixtures.Sent(server, $"DELETE {Group}/users/{Fixtures.UserId}");
    }

    [TestMethod]
    public async Task ListUsersReadsTheGroupsUsersAndItsFixture()
    {
        using var server = AreaFixtures.Server();
        server.Map("GET", $"{Group}/users", FakeResponse.Fixture(200, "users-in-group.json"));
        await using var client = AreaFixtures.Client(server);

        var users = await AreaFixtures.AllAsync(client.Groups.ListUsersAsync(AreaFixtures.GroupId));

        AreaFixtures.Sent(server, $"GET {Group}/users?pageSize=100&pageNumber=1");
        CollectionAssert.AreEqual(new[] { Fixtures.UserId, AreaFixtures.OtherUserId }, users.Select(user => user.Id).ToArray());
    }

    [TestMethod]
    public async Task ListUsersReadsTheGroupsUsersPageByPage()
    {
        using var server = AreaFixtures.Server();
        server.Map("GET", $"{Group}/users", request => AreaFixtures.PageNumber(request) == 1
            ? AreaFixtures.Page("users", "user", 1, 100, 150, 100)
            : AreaFixtures.Page("users", "user", 2, 100, 150, 50));
        await using var client = AreaFixtures.Client(server);

        var users = await AreaFixtures.AllAsync(client.Groups.ListUsersAsync(AreaFixtures.GroupId));

        CollectionAssert.AreEqual(new[] { 1, 2 }, AreaFixtures.Calls(server).Select(AreaFixtures.PageNumber).ToArray());
        Assert.HasCount(150, users.Select(user => user.Id).Distinct());
    }

    [TestMethod]
    public async Task ActiveDirectoryIsRefusedOnTableauCloudBeforeAnythingIsSent()
    {
        using var server = AreaFixtures.Server();
        await using var client = AreaFixtures.Client(server, TableauDeployment.Cloud);

        foreach (var call in new Func<Task>[]
                 {
                     () => client.Groups.ImportFromActiveDirectoryAsync("Finance", "example.org", SiteRole.Viewer),
                     () => client.Groups.ImportFromActiveDirectoryAsJobAsync("Finance", "example.org", SiteRole.Viewer),
                     () => client.Groups.UpdateFromActiveDirectoryAsync(AreaFixtures.GroupId, "Finance", "example.org", SiteRole.Viewer),
                     () => client.Groups.UpdateFromActiveDirectoryAsJobAsync(AreaFixtures.GroupId, "Finance", "example.org", SiteRole.Viewer),
                 })
            await ClientFixtures.FailsAsync(TableauErrorKind.UnsupportedDeployment, call);
        Assert.IsEmpty(server.Requests);
    }

    [TestMethod]
    public async Task ARefusedArgumentSendsNothing()
    {
        using var server = AreaFixtures.Server();
        await using var client = AreaFixtures.Client(server);

        await AreaFixtures.RefusedAsync<ArgumentException>(server, () => client.Groups.CreateAsync(""));
        Assert.AreEqual("domainName", (await AreaFixtures.RefusedAsync<ArgumentException>(server,
            () => client.Groups.ImportFromActiveDirectoryAsync("Finance", "", SiteRole.Viewer))).ParamName);
        Assert.AreEqual("name", (await AreaFixtures.RefusedAsync<ArgumentException>(server,
            () => client.Groups.ImportFromActiveDirectoryAsJobAsync("", "example.org", SiteRole.Viewer))).ParamName);
        Assert.AreEqual("siteRole", (await AreaFixtures.RefusedAsync<ArgumentException>(server,
            () => client.Groups.ImportFromActiveDirectoryAsync("Finance", "example.org", default))).ParamName);
        Assert.AreEqual("id", (await AreaFixtures.RefusedAsync<ArgumentException>(server,
            () => client.Groups.UpdateFromActiveDirectoryAsync("Finance", "Finance", "example.org", SiteRole.Viewer))).ParamName);
        Assert.AreEqual("id", (await AreaFixtures.RefusedAsync<ArgumentException>(server,
            () => client.Groups.UpdateFromActiveDirectoryAsJobAsync("Finance", "Finance", "example.org", SiteRole.Viewer))).ParamName);
        Assert.AreEqual("id", (await AreaFixtures.RefusedAsync<ArgumentException>(server, () => client.Groups.UpdateAsync("Finance"))).ParamName);
        Assert.AreEqual("id", (await AreaFixtures.RefusedAsync<ArgumentException>(server, () => client.Groups.DeleteAsync("Finance"))).ParamName);
        Assert.AreEqual("userId", (await AreaFixtures.RefusedAsync<ArgumentException>(server,
            () => client.Groups.AddUserAsync(AreaFixtures.GroupId, "ada"))).ParamName);
        Assert.AreEqual("groupId", (await AreaFixtures.RefusedAsync<ArgumentException>(server,
            () => client.Groups.RemoveUserAsync("Finance", Fixtures.UserId))).ParamName);
        Assert.AreEqual("groupId", (await AreaFixtures.RefusedAsync<ArgumentException>(server,
            () => AreaFixtures.AllAsync(client.Groups.ListUsersAsync("Finance")))).ParamName);
    }
}
