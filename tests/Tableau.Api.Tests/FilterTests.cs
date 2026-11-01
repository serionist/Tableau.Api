using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Tableau.Api.Rest.Models;
using Tableau.Api.Tests.Fakes;

namespace Tableau.Api.Tests;

/// <summary>
/// <see cref="TableauFilter"/> and <see cref="TableauSort"/>: Tableau's syntax written over typed fields, each value
/// escaped between the separators on the wire, a comma refused, and a field of another resource refused before anything
/// is sent.
/// </summary>
[TestClass]
public sealed class FilterTests
{
    private const string Projects = AreaFixtures.SitePath + "/projects";

    [TestMethod]
    public void EachOperatorIsWrittenInTableausSyntax()
    {
        var when = new DateTimeOffset(2026, 1, 2, 4, 5, 6, TimeSpan.FromHours(3));

        CollectionAssert.AreEqual(new[]
        {
            "name:eq:Finance", "topLevelProject:eq:true", "topLevelProject:eq:false", "userCount:eq:-1", "createdAt:eq:2026-01-02T01:05:06Z",
            "name:cieq:finance", "siteRole:in:[Viewer,Creator]", "name:has:fin",
            "userCount:gt:2", "createdAt:gt:2026-01-02T01:05:06Z", "userCount:gte:2", "createdAt:gte:2026-01-02T01:05:06Z",
            "userCount:lt:2", "createdAt:lt:2026-01-02T01:05:06Z", "userCount:lte:2", "createdAt:lte:2026-01-02T01:05:06Z",
        }, new[]
        {
            TableauFilter.Eq(ProjectField.Name, "Finance"), TableauFilter.Eq(ProjectField.TopLevelProject, true),
            TableauFilter.Eq(ProjectField.TopLevelProject, false), TableauFilter.Eq(GroupField.UserCount, -1), TableauFilter.Eq(ProjectField.CreatedAt, when),
            TableauFilter.CiEq(ProjectField.Name, "finance"), TableauFilter.In(UserField.SiteRole, "Viewer", "Creator"), TableauFilter.Has(ProjectField.Name, "fin"),
            TableauFilter.Gt(GroupField.UserCount, 2), TableauFilter.Gt(ProjectField.CreatedAt, when), TableauFilter.Gte(GroupField.UserCount, 2),
            TableauFilter.Gte(ProjectField.CreatedAt, when), TableauFilter.Lt(GroupField.UserCount, 2), TableauFilter.Lt(ProjectField.CreatedAt, when),
            TableauFilter.Lte(GroupField.UserCount, 2), TableauFilter.Lte(ProjectField.CreatedAt, when),
        }.Select(filter => filter.ToString()).ToArray());
    }

    [TestMethod]
    public void AndJoinsFiltersWithCommasAndASortChainsItsLevels()
    {
        var filter = TableauFilter.And(TableauFilter.Eq(UserField.SiteRole, "Viewer"),
            TableauFilter.And(TableauFilter.Eq(UserField.DomainName, "local"), TableauFilter.FromString("name:like:ad*")));
        var sort = TableauSort.Asc(UserField.SiteRole).Then(TableauSort.Desc(UserField.Name).Then(TableauSort.Asc(UserField.LastLogin)));

        Assert.AreEqual("siteRole:eq:Viewer,domainName:eq:local,name:like:ad*", filter.ToString());
        Assert.AreEqual("siteRole:asc,name:desc,lastLogin:asc", sort.ToString());
        Assert.AreEqual("siteRole", UserField.SiteRole.ToString());
        Assert.AreEqual("siteRole", UserField.SiteRole.Name);
    }

    // The values the filter must carry as written: an ampersand, pluses, a hash, a percent, spaces, a colon, and in an
    // in-list, brackets around values that hold them.
    [TestMethod]
    [DataRow("R&D", "name:eq:R%26D")]
    [DataRow("C++", "name:eq:C%2B%2B")]
    [DataRow("#1", "name:eq:%231")]
    [DataRow("100%", "name:eq:100%25")]
    [DataRow("Sales / EMEA?", "name:eq:Sales%20%2F%20EMEA%3F")]
    [DataRow("Région Nord", "name:eq:R%C3%A9gion%20Nord")]
    [DataRow("a:b", "name:eq:a:b")]
    public async Task AValueIsEscapedBetweenTheSeparatorsOnTheWire(string value, string query)
    {
        using var server = AreaFixtures.Server();
        server.Map("GET", Projects, FakeResponse.Fixture(200, "projects-empty.json"));
        await using var client = AreaFixtures.Client(server);

        await client.Projects.ListPageAsync(1, new ProjectListOptions { Filter = TableauFilter.Eq(ProjectField.Name, value) });

        var sent = AreaFixtures.Calls(server).Single();
        Assert.AreEqual($"pageSize=100&pageNumber=1&filter={query}&sort=createdAt:asc,name:asc", sent.Query);
        Assert.AreEqual($"name:eq:{value}", sent.QueryParameters.Single(pair => pair.Key == "filter").Value);
    }

    [TestMethod]
    public async Task AnInListKeepsItsBracketsAndCommasAndEscapesEachValue()
    {
        using var server = AreaFixtures.Server();
        server.Map("GET", Projects, FakeResponse.Fixture(200, "projects-empty.json"));
        await using var client = AreaFixtures.Client(server);

        await client.Projects.ListPageAsync(1, new ProjectListOptions
        {
            Filter = TableauFilter.And(TableauFilter.In(ProjectField.Name, "R&D", "C++", "#1 100%"), TableauFilter.Eq(ProjectField.TopLevelProject, true)),
        });

        Assert.AreEqual("pageSize=100&pageNumber=1&filter=name:in:%5BR%26D,C%2B%2B,%231%20100%25%5D,topLevelProject:eq:true"
            + "&sort=createdAt:asc,name:asc", AreaFixtures.Calls(server).Single().Query);
    }

    [TestMethod]
    public async Task AFilterFromAStringIsEscapedBetweenItsSeparatorsAndNotChecked()
    {
        using var server = AreaFixtures.Server();
        server.Map("GET", Projects, FakeResponse.Fixture(200, "projects-empty.json"));
        await using var client = AreaFixtures.Client(server);

        await client.Projects.ListPageAsync(1, new ProjectListOptions
        {
            Filter = TableauFilter.FromString("name:in:[R&D,C++],siteRole:eq:Viewer"),
            Sort = TableauSort.Asc(ProjectField.Name),
        });

        Assert.AreEqual("pageSize=100&pageNumber=1&filter=name:in:%5BR%26D,C%2B%2B%5D,siteRole:eq:Viewer&sort=name:asc,createdAt:asc",
            AreaFixtures.Calls(server).Single().Query);
    }

    [TestMethod]
    public void AValueWithACommaIsRefusedAsTableauCantTakeIt()
    {
        var single = Assert.ThrowsExactly<ArgumentException>(() => TableauFilter.Eq(ProjectField.Name, "Sales, EMEA"));
        var inList = Assert.ThrowsExactly<ArgumentException>(() => TableauFilter.In(ProjectField.Name, "Sales", "R, D"));
        Assert.ThrowsExactly<ArgumentException>(() => TableauFilter.CiEq(UserField.Name, "a,b"));
        Assert.ThrowsExactly<ArgumentException>(() => TableauFilter.Has(ProjectField.Name, ","));

        Assert.Contains("comma", single.Message);
        Assert.Contains("'R, D'", inList.Message);
    }

    [TestMethod]
    public void AnEmptyOrNullArgumentIsRefused()
    {
        Assert.ThrowsExactly<ArgumentException>(() => TableauFilter.In(ProjectField.Name, Array.Empty<string>()));
        Assert.ThrowsExactly<ArgumentException>(() => TableauFilter.In(UserField.SiteRole, Array.Empty<SiteRole>()));
        Assert.AreEqual("value", Assert.ThrowsExactly<ArgumentException>(() => TableauFilter.Eq(UserField.SiteRole, default(SiteRole))).ParamName);
        Assert.ThrowsExactly<ArgumentException>(() => TableauFilter.In(UserField.SiteRole, SiteRole.Viewer, default));
        Assert.ThrowsExactly<ArgumentException>(() => TableauFilter.And());
        Assert.ThrowsExactly<ArgumentNullException>(() => TableauFilter.And(TableauFilter.Eq(ProjectField.Name, "a"), null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => TableauFilter.And(null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => TableauFilter.In(ProjectField.Name, (IEnumerable<string>)null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => TableauFilter.In(UserField.SiteRole, (IEnumerable<SiteRole>)null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => TableauFilter.Eq(null!, "a"));
        Assert.ThrowsExactly<ArgumentNullException>(() => TableauFilter.Eq(null!, SiteRole.Viewer));
        Assert.ThrowsExactly<ArgumentNullException>(() => TableauFilter.Eq(ProjectField.Name, null!));
        Assert.ThrowsExactly<ArgumentException>(() => TableauFilter.FromString(" "));
        Assert.ThrowsExactly<ArgumentNullException>(() => TableauSort.Asc(null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => TableauSort.Asc(ProjectField.Name).Then(null!));
    }

    [TestMethod]
    public async Task AFieldOfAnotherResourceIsRefusedBeforeAnythingIsSent()
    {
        using var server = AreaFixtures.Server();
        await using var client = AreaFixtures.Client(server);

        var filtered = await AreaFixtures.RefusedAsync<ArgumentException>(server, () => client.Projects.ListPageAsync(1,
            new ProjectListOptions { Filter = TableauFilter.And(TableauFilter.Eq(ProjectField.Name, "a"), TableauFilter.Eq(UserField.SiteRole, SiteRole.Viewer)) }));
        var sorted = await AreaFixtures.RefusedAsync<ArgumentException>(server, () => AreaFixtures.AllAsync(client.Groups.ListAsync(
            new GroupListOptions { Sort = TableauSort.Asc(GroupField.Name).Then(TableauSort.Asc(ProjectField.CreatedAt)) })));
        await AreaFixtures.RefusedAsync<ArgumentException>(server, () => client.Users.ListPageAsync(1,
            new UserListOptions { Filter = TableauFilter.Eq(GroupField.Name, "Finance") }));

        Assert.AreEqual("options", filtered.ParamName);
        Assert.Contains("UserField.SiteRole is a field of users, and a list of projects", filtered.Message);
        Assert.Contains("ProjectField.CreatedAt is a field of projects, and a list of groups", sorted.Message);
    }

    [TestMethod]
    public void ASiteRoleIsWrittenByItsText()
    {
        Assert.AreEqual("siteRole:eq:Viewer", TableauFilter.Eq(UserField.SiteRole, SiteRole.Viewer).ToString());
        Assert.AreEqual("minimumSiteRole:in:[Explorer,Creator]", TableauFilter.In(GroupField.MinimumSiteRole, SiteRole.Explorer, SiteRole.Creator).ToString());
        Assert.AreEqual(TableauFilter.Eq(UserField.SiteRole, "Viewer"), TableauFilter.Eq(UserField.SiteRole, SiteRole.Viewer));
        Assert.AreEqual("siteRole:eq:InventedFutureRole", TableauFilter.Eq(UserField.SiteRole, new SiteRole("InventedFutureRole")).ToString());
    }

    [TestMethod]
    public void FiltersAndSortsBuiltAlikeAreEqualAndSoAreTheirOptions()
    {
        static TableauFilter Filter() => TableauFilter.And(TableauFilter.Eq(UserField.SiteRole, SiteRole.Viewer), TableauFilter.Gt(UserField.LastLogin,
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)));
        static TableauSort Sort() => TableauSort.Asc(UserField.SiteRole).Then(TableauSort.Desc(UserField.Name));

        Assert.AreEqual(Filter(), Filter());
        Assert.AreEqual(Filter().GetHashCode(), Filter().GetHashCode());
        Assert.AreEqual(Sort(), Sort());
        Assert.AreEqual(Sort().GetHashCode(), Sort().GetHashCode());
        Assert.AreEqual(new UserListOptions { Filter = Filter() }, new UserListOptions { Filter = Filter() });
        Assert.AreEqual(new UserListOptions { Sort = Sort(), PageSize = 5 }, new UserListOptions { Sort = Sort(), PageSize = 5 });
        Assert.AreEqual(TableauFilter.FromString("name:like:a*"), TableauFilter.FromString("name:like:a*"));

        // The same text over another resource's field, or with no field, filters otherwise: refused, or not checked.
        Assert.AreNotEqual(TableauFilter.Eq(UserField.Name, "ada"), TableauFilter.Eq(GroupField.Name, "ada"));
        Assert.AreNotEqual(TableauFilter.Eq(UserField.Name, "ada"), TableauFilter.FromString("name:eq:ada"));
        Assert.AreNotEqual(TableauSort.Asc(UserField.Name), TableauSort.Asc(GroupField.Name));
        Assert.AreNotEqual(TableauSort.Asc(UserField.Name), TableauSort.Desc(UserField.Name));
        Assert.AreNotEqual(new UserListOptions { Filter = Filter() }, new UserListOptions { Filter = TableauFilter.Eq(UserField.Name, "ada") });
        Assert.IsFalse(Filter().Equals(null));
        Assert.IsFalse(Sort().Equals((object)"siteRole:asc,name:desc"));
    }

    [TestMethod]
    public async Task ASortGivenIsFollowedByTheDefaultFieldsItDoesntName()
    {
        using var server = AreaFixtures.Server();
        server.MapPrefix("GET", AreaFixtures.SitePath, FakeResponse.Json(200, "{}"));
        await using var serverClient = AreaFixtures.Client(server);
        await using var cloudClient = AreaFixtures.Client(server, TableauDeployment.Cloud);

        await serverClient.Users.ListPageAsync(1, new UserListOptions { Sort = TableauSort.Asc(UserField.SiteRole) });
        await serverClient.Users.ListPageAsync(1, new UserListOptions { Sort = TableauSort.Desc(UserField.DomainName) });
        await cloudClient.Users.ListPageAsync(1, new UserListOptions { Sort = TableauSort.Asc(UserField.SiteRole) });
        await cloudClient.Users.ListPageAsync(1);
        await serverClient.Groups.ListPageAsync(1, new GroupListOptions { Sort = TableauSort.Desc(GroupField.Name) });
        await serverClient.Projects.ListPageAsync(1, new ProjectListOptions { Sort = TableauSort.Desc(ProjectField.Name) });

        CollectionAssert.AreEqual(new[]
        {
            "siteRole:asc,name:asc,domainName:asc", "domainName:desc,name:asc", "siteRole:asc,name:asc", "name:asc", "name:desc",
            "name:desc,createdAt:asc",
        }, AreaFixtures.Calls(server).Select(call => call.QueryParameters.Single(pair => pair.Key == "sort").Value).ToArray());
    }

    [TestMethod]
    public void EachFieldClassListsTheReferencesFields()
    {
        static string[] Names(Type type) =>
            [.. type.GetProperties().Select(property => ((TableauField)property.GetValue(null)!).Name).Order(StringComparer.Ordinal)];

        CollectionAssert.AreEqual(new[] { "createdAt", "name", "ownerDomain", "ownerEmail", "ownerName", "parentProjectId", "topLevelProject", "updatedAt" },
            Names(typeof(ProjectField)));
        CollectionAssert.AreEqual(new[] { "domainName", "friendlyName", "isLocal", "lastLogin", "luid", "name", "siteRole" }, Names(typeof(UserField)));
        CollectionAssert.AreEqual(new[] { "domainName", "domainNickname", "isExternalUserEnabled", "isLocal", "luid", "minimumSiteRole", "name", "userCount" },
            Names(typeof(GroupField)));
    }
}
