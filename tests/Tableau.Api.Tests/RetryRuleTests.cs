using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Tableau.Api.Pipeline;
using Tableau.Api.Rest.Models;
using Tableau.Api.Tests.Fakes;

namespace Tableau.Api.Tests;

/// <summary>
/// Which calls the client may send again after a failure that may not have reached Tableau, by what each does: every
/// descriptor's rule pinned by name, so a change to one fails here; and each call that is sent once, answered 503 with
/// no wait, stays one request, where an update is sent again.
/// </summary>
[TestClass]
public sealed class RetryRuleTests
{
    /// <summary>Every descriptor's retry rule: true for a call sent again after a 408, a 429, a 5xx or a broken connection.</summary>
    private static readonly Dictionary<string, bool> _rules = new(StringComparer.Ordinal)
    {
        ["Server Info"] = true,
        ["Sign In"] = true,
        ["Sign In, impersonating a user"] = true,
        ["Sign In with a personal access token"] = true,
        ["Sign In with a personal access token, impersonating a user"] = true,
        ["Sign In with a JWT"] = false,
        ["Sign In with a unified access token"] = false,
        ["Sign Out"] = true,
        ["Switch Site"] = false,
        ["Query Site"] = true,
        ["Query Site by content URL"] = true,
        ["Query Site by name"] = true,
        ["Query Sites"] = true,
        ["Create Site"] = false,
        ["Update Site"] = true,
        ["Delete Site"] = false,
        ["Query Projects"] = true,
        ["Create Project"] = false,
        ["Update Project"] = true,
        ["Delete Project"] = true,
        ["Query User On Site"] = true,
        ["Get Users on Site"] = true,
        ["Add User to Site"] = false,
        ["Update User"] = true,
        ["Remove User from Site"] = true,
        ["Get Groups for a User"] = true,
        ["Query Groups"] = true,
        ["Create Group"] = false,
        ["Create Group from Active Directory"] = false,
        ["Update Group"] = true,
        ["Update Group from Active Directory"] = false,
        ["Delete Group"] = true,
        ["Add User to Group"] = true,
        ["Remove User from Group"] = true,
        ["Get Users in Group"] = true,
        ["Get Favorites for User"] = true,
        ["Add Workbook to Favorites"] = true,
        ["Add View to Favorites"] = true,
        ["Add Data Source to Favorites"] = true,
        ["Add Project to Favorites"] = true,
        ["Add Flow to Favorites"] = true,
        ["Delete Workbook from Favorites"] = true,
        ["Delete View from Favorites"] = true,
        ["Delete Data Source from Favorites"] = true,
        ["Delete Project from Favorites"] = true,
        ["Delete Flow from Favorites"] = true,
    };

    private const string Sites = AreaFixtures.SitesPath;
    private const string Groups = AreaFixtures.SitePath + "/groups";
    private const string OtherSiteId = "9e8d7c6b-5a49-4382-9170-6f5e4d3c2b1a";

    /// <summary>Each call sent once: its method and path, its answer once it succeeds, and the call.</summary>
    private static readonly Dictionary<string, (string Method, string Path, FakeResponse Answer, Func<TableauClient, Task> Call)> _sentOnce =
        new(StringComparer.Ordinal)
        {
            ["Create Site"] = ("POST", Sites, FakeResponse.Fixture(201, "site.json"),
                client => client.Sites.CreateAsync(new Site { Name = "Finance", ContentUrl = "finance" })),
            ["Delete Site"] = ("DELETE", $"{Sites}/{OtherSiteId}", FakeResponse.Empty(204), client => client.Sites.DeleteAsync(OtherSiteId)),
            ["Create Project"] = ("POST", AreaFixtures.SitePath + "/projects", FakeResponse.Fixture(201, "create-project.json"),
                client => client.Projects.CreateAsync(new Project { Name = "Finance" })),
            ["Add User to Site"] = ("POST", AreaFixtures.SitePath + "/users", FakeResponse.Fixture(201, "user.json"),
                client => client.Users.AddAsync("ada", SiteRole.Viewer)),
            ["Create Group"] = ("POST", Groups, FakeResponse.Fixture(201, "group.json"), client => client.Groups.CreateAsync("Finance analysts")),
            ["Create Group from Active Directory"] = ("POST", Groups, FakeResponse.Fixture(201, "group-imported.json"),
                client => client.Groups.ImportFromActiveDirectoryAsync("Finance", "example.org", SiteRole.Viewer)),
            ["Create Group from Active Directory, as a job"] = ("POST", Groups, FakeResponse.Fixture(202, "group-import-job.json"),
                client => client.Groups.ImportFromActiveDirectoryAsJobAsync("Finance", "example.org", SiteRole.Viewer)),
            ["Update Group from Active Directory"] = ("PUT", $"{Groups}/{AreaFixtures.GroupId}", FakeResponse.Fixture(200, "group-imported.json"),
                client => client.Groups.UpdateFromActiveDirectoryAsync(AreaFixtures.GroupId, "Finance", "example.org", SiteRole.Viewer)),
            ["Update Group from Active Directory, as a job"] = ("PUT", $"{Groups}/{AreaFixtures.GroupId}", FakeResponse.Fixture(202, "group-sync-job.json"),
                client => client.Groups.UpdateFromActiveDirectoryAsJobAsync(AreaFixtures.GroupId, "Finance", "example.org", SiteRole.Viewer)),
        };

    public static IEnumerable<object[]> SentOnce => _sentOnce.Keys.Select(name => new object[] { name });

    [TestMethod]
    public void EachDescriptorsRetryRuleIsTheOneDecided()
    {
        var actual = Endpoints.All.ToDictionary(endpoint => endpoint.Name, endpoint => endpoint.Idempotent, StringComparer.Ordinal);

        CollectionAssert.AreEquivalent(_rules.Keys.ToList(), actual.Keys.ToList(), "a descriptor added, renamed or dropped: decide its rule here");
        var changed = _rules.Where(rule => actual[rule.Key] != rule.Value).Select(rule => $"{rule.Key}: {actual[rule.Key]}").ToList();
        Assert.IsEmpty(changed, string.Join("\n", changed));
    }

    [TestMethod]
    [DynamicData(nameof(SentOnce))]
    public async Task ACallSentOnceIsNotSentAgainAfterA503(string name)
    {
        var (method, path, answer, call) = _sentOnce[name];
        using var server = AreaFixtures.Server();
        var sent = 0;
        server.Map(method, path, _ => Interlocked.Increment(ref sent) == 1 ? Unavailable() : answer);
        await using var client = Retrying(server);

        var thrown = await ClientFixtures.FailsAsync(TableauErrorKind.Http, () => call(client));

        Assert.AreEqual(1, sent, name);
        Assert.AreEqual(System.Net.HttpStatusCode.ServiceUnavailable, thrown.StatusCode);
    }

    [TestMethod]
    public async Task AnUpdateIsSentAgainAfterA503()
    {
        using var server = AreaFixtures.Server();
        var sent = 0;
        server.Map("PUT", $"{AreaFixtures.SitePath}/projects/{AreaFixtures.ProjectId}",
            _ => Interlocked.Increment(ref sent) == 1 ? Unavailable() : FakeResponse.Fixture(200, "create-project.json"));
        await using var client = Retrying(server);

        var project = await client.Projects.UpdateAsync(AreaFixtures.ProjectId, new Project { Description = "Updated" });

        Assert.AreEqual(2, sent);
        Assert.AreEqual(AreaFixtures.ProjectId, project.Id);
    }

    // A 503 that asks for no wait, so a call that is sent again is sent at once.
    private static FakeResponse Unavailable() => FakeResponse.Text(503, "Invented: briefly unavailable.").WithHeader("Retry-After", "0");

    private static TableauClient Retrying(FakeTableauServer server)
    {
        var options = SessionFixtures.Options(server);
        options.Deployment = TableauDeployment.Server;
        options.MaxRetries = 3;
        return SessionFixtures.Client(options, SessionFixtures.Pat());
    }
}
