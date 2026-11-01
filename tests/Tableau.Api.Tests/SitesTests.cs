using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Tableau.Api.Rest.Models;
using Tableau.Api.Tests.Fakes;

namespace Tableau.Api.Tests;

/// <summary>
/// <see cref="SitesClient"/>: each call's method, path, query and body as the reference gives them, its answer read
/// from a fixture, and the calls Tableau Cloud lacks refused there before anything is sent.
/// </summary>
[TestClass]
public sealed class SitesTests
{
    private const string Sites = AreaFixtures.SitesPath;
    private const string OtherSiteId = "9e8d7c6b-5a49-4382-9170-6f5e4d3c2b1a";

    [TestMethod]
    public async Task GetQueriesTheSiteByItsLuid()
    {
        using var server = AreaFixtures.Server();
        server.Map("GET", $"{Sites}/{OtherSiteId}", FakeResponse.Fixture(200, "site.json"));
        await using var client = AreaFixtures.Client(server, TableauDeployment.Cloud);

        var site = await client.Sites.GetAsync(OtherSiteId);

        AreaFixtures.Sent(server, $"GET {Sites}/{OtherSiteId}");
        Assert.AreEqual("finance", site.ContentUrl);
        Assert.AreEqual(25L, site.RevisionLimit);
    }

    [TestMethod]
    public async Task GetByContentUrlAndByNameSendTheirKeyAndTheValueAsOneSegment()
    {
        using var server = AreaFixtures.Server();
        server.Map("GET", $"{Sites}/finance", FakeResponse.Fixture(200, "site.json"));
        server.Map("GET", $"{Sites}/Sales%2FEMEA%3F%20%26%20R%26D", FakeResponse.Fixture(200, "site.json"));
        await using var client = AreaFixtures.Client(server, TableauDeployment.Cloud);

        var byUrl = await client.Sites.GetByContentUrlAsync("finance");
        var byName = await client.Sites.GetByNameAsync("Sales/EMEA? & R&D");

        CollectionAssert.AreEqual(new[] { $"GET {Sites}/finance?key=contentUrl", $"GET {Sites}/Sales%2FEMEA%3F%20%26%20R%26D?key=name" },
            AreaFixtures.Calls(server).Select(call => $"{call.Method} {call.Target}").ToArray());
        Assert.AreEqual(Fixtures.SiteId, byUrl.Id);
        Assert.AreEqual("Finance", byName.Name);
    }

    [TestMethod]
    public async Task AnIdOrKeyThatNamesNoSiteIsRefusedBeforeAnythingIsSent()
    {
        using var server = AreaFixtures.Server();
        await using var client = AreaFixtures.Client(server);

        Assert.AreEqual("id", (await AreaFixtures.RefusedAsync<ArgumentException>(server, () => client.Sites.GetAsync("finance"))).ParamName);
        Assert.AreEqual("contentUrl", (await AreaFixtures.RefusedAsync<ArgumentException>(server, () => client.Sites.GetByContentUrlAsync(""))).ParamName);
        Assert.AreEqual("name", (await AreaFixtures.RefusedAsync<ArgumentException>(server, () => client.Sites.GetByNameAsync(".."))).ParamName);
        await AreaFixtures.RefusedAsync<ArgumentNullException>(server, () => client.Sites.GetByNameAsync(null!));
        Assert.AreEqual("id", (await AreaFixtures.RefusedAsync<ArgumentException>(server, () => client.Sites.UpdateAsync("x", new Site()))).ParamName);
        Assert.AreEqual("id", (await AreaFixtures.RefusedAsync<ArgumentException>(server, () => client.Sites.DeleteAsync("x"))).ParamName);
        await AreaFixtures.RefusedAsync<ArgumentNullException>(server, () => client.Sites.UpdateAsync(Fixtures.SiteId, null!));
    }

    [TestMethod]
    public async Task ListPageAsksForItsPageAndReadsTheSites()
    {
        using var server = AreaFixtures.Server();
        server.Map("GET", Sites, FakeResponse.Fixture(200, "sites-page.json"));
        await using var client = AreaFixtures.Client(server);

        var page = await client.Sites.ListPageAsync(1);

        AreaFixtures.Sent(server, $"GET {Sites}?pageSize=100&pageNumber=1");
        Assert.AreEqual((1, 100, 2), (page.PageNumber, page.PageSize, page.TotalAvailable));
        CollectionAssert.AreEqual(new[] { "", "finance" }, page.Items.Select(site => site.ContentUrl).ToArray());
        Assert.AreEqual(SiteAdminMode.ContentOnly, page.Items[1].AdminMode);
        Assert.AreEqual(-1L, page.Items[1].RevisionLimit);
    }

    [TestMethod]
    public async Task ListReadsEveryPageAndTakesAPageSize()
    {
        using var server = AreaFixtures.Server();
        server.Map("GET", Sites, request => AreaFixtures.Page("sites", "site", AreaFixtures.PageNumber(request), 1, 2, 1));
        await using var client = AreaFixtures.Client(server);

        var sites = await AreaFixtures.AllAsync(client.Sites.ListAsync(new SiteListOptions { PageSize = 1 }));

        CollectionAssert.AreEqual(new[] { $"{Sites}?pageSize=1&pageNumber=1", $"{Sites}?pageSize=1&pageNumber=2" },
            AreaFixtures.Calls(server).Select(call => call.Target).ToArray());
        CollectionAssert.AreEqual(new[] { "n0", "n1" }, sites.Select(site => site.Name).ToArray());
    }

    [TestMethod]
    public async Task CreateSendsTheSiteOnceAndReadsIt()
    {
        using var server = AreaFixtures.Server();
        server.Map("POST", Sites, FakeResponse.Fixture(201, "site.json"));
        await using var client = AreaFixtures.Client(server);

        var site = await client.Sites.CreateAsync(new Site
        {
            Name = "Finance",
            ContentUrl = "finance",
            AdminMode = SiteAdminMode.ContentOnly,
            RevisionLimit = 25,
            TierCreatorCapacity = 5,
        });

        AreaFixtures.Sent(server, $"POST {Sites}",
            """{"site":{"adminMode":"ContentOnly","contentUrl":"finance","name":"Finance","revisionLimit":25,"tierCreatorCapacity":5}}""");
        Assert.AreEqual(Fixtures.SiteId, site.Id);
    }

    [TestMethod]
    public async Task CreateNeedsANameAndAContentUrl()
    {
        using var server = AreaFixtures.Server();
        await using var client = AreaFixtures.Client(server);

        await AreaFixtures.RefusedAsync<ArgumentNullException>(server, () => client.Sites.CreateAsync(null!));
        await AreaFixtures.RefusedAsync<ArgumentException>(server, () => client.Sites.CreateAsync(new Site { Name = "Finance" }));
        await AreaFixtures.RefusedAsync<ArgumentException>(server, () => client.Sites.CreateAsync(new Site { ContentUrl = "finance" }));
    }

    [TestMethod]
    public async Task UpdateSendsOnlyWhatIsSetAndARevisionLimitAsANumber()
    {
        using var server = AreaFixtures.Server();
        server.Map("PUT", AreaFixtures.SitePath, FakeResponse.Fixture(200, "site.json"));
        await using var client = AreaFixtures.Client(server, TableauDeployment.Cloud);

        var site = await client.Sites.UpdateAsync(Fixtures.SiteId, new Site { RevisionLimit = 25, State = "Active" });

        AreaFixtures.Sent(server, $"PUT {AreaFixtures.SitePath}", """{"site":{"revisionLimit":25,"state":"Active"}}""");
        Assert.AreEqual("Finance", site.Name);
    }

    [TestMethod]
    public async Task DeleteSendsADeleteToTheSite()
    {
        using var server = AreaFixtures.Server();
        server.Map("DELETE", $"{Sites}/{OtherSiteId}", FakeResponse.Empty(204));
        await using var client = AreaFixtures.Client(server);

        await client.Sites.DeleteAsync(OtherSiteId);

        AreaFixtures.Sent(server, $"DELETE {Sites}/{OtherSiteId}");
    }

    [TestMethod]
    public async Task WhatTableauCloudLacksIsRefusedThereBeforeAnythingIsSent()
    {
        using var server = AreaFixtures.Server();
        await using var client = AreaFixtures.Client(server, TableauDeployment.Cloud);

        foreach (var call in new Func<Task>[]
                 {
                     () => client.Sites.ListPageAsync(1),
                     () => AreaFixtures.AllAsync(client.Sites.ListAsync()),
                     () => client.Sites.CreateAsync(new Site { Name = "Finance", ContentUrl = "finance" }),
                     () => client.Sites.DeleteAsync(OtherSiteId),
                 })
        {
            var thrown = await ClientFixtures.FailsAsync(TableauErrorKind.UnsupportedDeployment, call);
            Assert.Contains("doesn't exist on Tableau Cloud", thrown.Message);
        }
        Assert.IsEmpty(server.Requests);
    }
}
