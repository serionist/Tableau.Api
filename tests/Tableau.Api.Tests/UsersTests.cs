using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Tableau.Api.Rest.Models;
using Tableau.Api.Tests.Fakes;

namespace Tableau.Api.Tests;

/// <summary>
/// <see cref="UsersClient"/>: each call's method, path, query and body as the reference gives them, and its answer read
/// from a fixture; Update User sends every attribute the reference lists.
/// </summary>
[TestClass]
public sealed class UsersTests
{
    private const string Users = AreaFixtures.SitePath + "/users";

    [TestMethod]
    public async Task GetQueriesTheUserByTheirLuid()
    {
        using var server = AreaFixtures.Server();
        server.Map("GET", $"{Users}/{Fixtures.UserId}", FakeResponse.Fixture(200, "user.json"));
        await using var client = AreaFixtures.Client(server, TableauDeployment.Cloud);

        var user = await client.Users.GetAsync(Fixtures.UserId);

        AreaFixtures.Sent(server, $"GET {Users}/{Fixtures.UserId}");
        Assert.AreEqual(("ada", SiteRole.SiteAdministratorCreator, "local"), (user.Name, user.SiteRole, user.Domain?.Name));
        Assert.AreEqual(new DateTimeOffset(2026, 10, 30, 7, 45, 12, TimeSpan.Zero), user.LastLogin);
    }

    [TestMethod]
    public async Task ListPageSortsByNameThenDomainOnServerUnlessToldAndReadsTheUsers()
    {
        using var server = AreaFixtures.Server();
        server.Map("GET", Users, FakeResponse.Fixture(200, "users-page.json"));
        await using var client = AreaFixtures.Client(server);

        var page = await client.Users.ListPageAsync(2, new UserListOptions { PageSize = 1 });

        AreaFixtures.Sent(server, $"GET {Users}?pageSize=1&pageNumber=2&sort=name:asc,domainName:asc");
        Assert.AreEqual((2, 1, 2), (page.PageNumber, page.PageSize, page.TotalAvailable));
        Assert.AreEqual("ada", page.Items.Single().Name);
    }

    [TestMethod]
    public async Task ListSendsItsFilterAndSortAndReadsEveryPage()
    {
        using var server = AreaFixtures.Server();
        server.Map("GET", Users, FakeResponse.Fixture(200, "users-in-group.json"));
        await using var client = AreaFixtures.Client(server);
        var since = new DateTimeOffset(2026, 1, 1, 1, 0, 0, TimeSpan.FromHours(1));

        var users = await AreaFixtures.AllAsync(client.Users.ListAsync(new UserListOptions
        {
            Filter = TableauFilter.And(TableauFilter.In(UserField.SiteRole, SiteRole.Viewer, SiteRole.Creator),
                TableauFilter.Gte(UserField.LastLogin, since), TableauFilter.CiEq(UserField.Name, "ADA")),
            Sort = TableauSort.Asc(UserField.SiteRole).Then(TableauSort.Desc(UserField.Name)),
        }));

        AreaFixtures.Sent(server, $"GET {Users}?pageSize=100&pageNumber=1"
            + "&filter=siteRole:in:%5BViewer,Creator%5D,lastLogin:gte:2026-01-01T00:00:00Z,name:cieq:ADA&sort=siteRole:asc,name:desc,domainName:asc");
        CollectionAssert.AreEqual(new[] { "ada", "grace" }, users.Select(user => user.Name).ToArray());
        Assert.IsNull(users[1].LastLogin, "a user who never signed in has no lastLogin");
    }

    [TestMethod]
    public async Task AddSendsTheNameTheSiteRoleAndTheOptionsGivenOnce()
    {
        using var server = AreaFixtures.Server();
        server.Map("POST", Users, FakeResponse.Fixture(201, "user.json"));
        await using var client = AreaFixtures.Client(server);

        var user = await client.Users.AddAsync("ada", SiteRole.Explorer, new UserAddOptions
        {
            AuthSetting = SiteUserAuthSetting.Saml,
            IdentityPoolName = "Invented pool",
        });

        AreaFixtures.Sent(server, $"POST {Users}",
            """{"user":{"authSetting":"SAML","identityPoolName":"Invented pool","name":"ada","siteRole":"Explorer"}}""");
        Assert.AreEqual(Fixtures.UserId, user.Id);
    }

    [TestMethod]
    public async Task AddSendsTheDetailsTableauCloudTakes()
    {
        using var server = AreaFixtures.Server();
        server.Map("POST", Users, FakeResponse.Fixture(201, "user.json"));
        await using var client = AreaFixtures.Client(server, TableauDeployment.Cloud);

        await client.Users.AddAsync("ada@example.org", SiteRole.Viewer, new UserAddOptions
        {
            IdpConfigurationId = "9f8e7d6c-5b4a-4392-8170-6e5d4c3b2a19",
            Email = "notices@example.org",
            Language = "en_GB",
            Locale = "en_GB",
        });
        await client.Users.AddAsync("grace@example.org", SiteRole.Unlicensed);

        CollectionAssert.AreEqual(new[]
        {
            """{"user":{"email":"notices@example.org","idpConfigurationId":"9f8e7d6c-5b4a-4392-8170-6e5d4c3b2a19","language":"en_GB","locale":"en_GB","name":"ada@example.org","siteRole":"Viewer"}}""",
            """{"user":{"name":"grace@example.org","siteRole":"Unlicensed"}}""",
        }.Select(AreaFixtures.Json).ToArray(), AreaFixtures.Bodies(server));
    }

    [TestMethod]
    public async Task UpdateSendsEveryAttributeTheReferenceListsThatIsSet()
    {
        using var server = AreaFixtures.Server();
        server.Map("PUT", $"{Users}/{Fixtures.UserId}", FakeResponse.Fixture(200, "user.json"));
        await using var client = AreaFixtures.Client(server);
        var changes = ModelFixtures.Request("update-user-request.json").User!;

        var user = await client.Users.UpdateAsync(Fixtures.UserId, changes);

        var call = AreaFixtures.Sent(server, $"PUT {Users}/{Fixtures.UserId}", ModelFixtures.Write(new TsRequest { User = changes }));
        ModelFixtures.AssertSameJson(ModelFixtures.Text("update-user-request.json"), call.BodyText);
        foreach (var attribute in new[] { "email", "idpConfigurationId", "language", "locale", "fullName", "siteRole", "authSetting", "identityPoolName" })
            Assert.Contains($"\"{attribute}\":", call.BodyText);
        Assert.AreEqual("Ada Lovelace", user.FullName);
    }

    [TestMethod]
    public async Task UpdateSendsAPasswordAndNothingUnset()
    {
        using var server = AreaFixtures.Server();
        server.Map("PUT", $"{Users}/{Fixtures.UserId}", FakeResponse.Fixture(200, "user.json"));
        await using var client = AreaFixtures.Client(server);

        await client.Users.UpdateAsync(Fixtures.UserId, new User { Password = Fixtures.Password });

        AreaFixtures.Sent(server, $"PUT {Users}/{Fixtures.UserId}", $$$"""{"user":{"password":"{{{Fixtures.Password}}}"}}""");
    }

    [TestMethod]
    public async Task RemoveSendsADeleteAndTheUserWhoTakesTheirContent()
    {
        using var server = AreaFixtures.Server();
        server.Map("DELETE", $"{Users}/{Fixtures.UserId}", FakeResponse.Empty(204));
        await using var client = AreaFixtures.Client(server);

        await client.Users.RemoveAsync(Fixtures.UserId);
        await client.Users.RemoveAsync(Fixtures.UserId, AreaFixtures.OtherUserId);

        CollectionAssert.AreEqual(new[] { $"DELETE {Users}/{Fixtures.UserId}", $"DELETE {Users}/{Fixtures.UserId}?mapAssetsTo={AreaFixtures.OtherUserId}" },
            AreaFixtures.Calls(server).Select(call => $"{call.Method} {call.Target}").ToArray());
    }

    [TestMethod]
    public async Task ListGroupsReadsTheUsersGroupsPageByPage()
    {
        using var server = AreaFixtures.Server();
        server.Map("GET", $"{Users}/{Fixtures.UserId}/groups", request => AreaFixtures.PageNumber(request) == 1
            ? AreaFixtures.Page("groups", "group", 1, 100, 103, 100)
            : AreaFixtures.Page("groups", "group", 2, 100, 103, 3));
        await using var client = AreaFixtures.Client(server);

        var groups = await AreaFixtures.AllAsync(client.Users.ListGroupsAsync(Fixtures.UserId));

        CollectionAssert.AreEqual(new[] { $"{Users}/{Fixtures.UserId}/groups?pageSize=100&pageNumber=1", $"{Users}/{Fixtures.UserId}/groups?pageSize=100&pageNumber=2" },
            AreaFixtures.Calls(server).Select(call => call.Target).ToArray());
        CollectionAssert.AreEqual(Enumerable.Range(0, 103).Select(i => $"n{i}").ToArray(), groups.Select(group => group.Name).ToArray());
    }

    [TestMethod]
    public async Task ARefusedArgumentSendsNothing()
    {
        using var server = AreaFixtures.Server();
        await using var client = AreaFixtures.Client(server);

        Assert.AreEqual("id", (await AreaFixtures.RefusedAsync<ArgumentException>(server, () => client.Users.GetAsync("ada"))).ParamName);
        await AreaFixtures.RefusedAsync<ArgumentException>(server, () => client.Users.AddAsync("", SiteRole.Viewer));
        Assert.AreEqual("siteRole", (await AreaFixtures.RefusedAsync<ArgumentException>(server, () => client.Users.AddAsync("ada", default))).ParamName);
        Assert.AreEqual("id", (await AreaFixtures.RefusedAsync<ArgumentException>(server, () => client.Users.UpdateAsync("ada", new User()))).ParamName);
        await AreaFixtures.RefusedAsync<ArgumentNullException>(server, () => client.Users.UpdateAsync(Fixtures.UserId, null!));
        Assert.AreEqual("id", (await AreaFixtures.RefusedAsync<ArgumentException>(server, () => client.Users.RemoveAsync("ada"))).ParamName);
        Assert.AreEqual("mapAssetsToUserId",
            (await AreaFixtures.RefusedAsync<ArgumentException>(server, () => client.Users.RemoveAsync(Fixtures.UserId, "grace"))).ParamName);
        Assert.AreEqual("userId",
            (await AreaFixtures.RefusedAsync<ArgumentException>(server, () => AreaFixtures.AllAsync(client.Users.ListGroupsAsync("ada")))).ParamName);
    }
}
