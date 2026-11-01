using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Tableau.Api.Rest.Models;

namespace Tableau.Api.Tests;

/// <summary>
/// Projects, groups, users, permissions and the Metadata API on a Tableau site (<see cref="LiveSite"/> has the
/// settings): a project made in the run's project, found by a filter whose value Tableau must read as written, updated
/// and deleted; a local group made, found, renamed, given the suite's own user and then without them, and deleted; users
/// read, never changed; a capability granted to a run's group on the run's project, read back and deleted; and a
/// read-only query of the Metadata API, paged by its cursor. Nothing is tagged, no data quality warning is set, and no
/// database, table or column is changed. They run one at a time, with the session tests: a personal access token allows
/// one session, so another test's sign-in would end this one's.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class LiveAreaTests
{
    /// <summary>A name with an ampersand, pluses, a hash, a percent and a space, which a filter carries as written.</summary>
    private const string NestedName = "R&D C++ #1 100%";

    [SiteTest(TableauSite.Live, [LiveSite.ServerUrl], SignsIn = true), TestCategory(TableauSite.Live)]
    public async Task AProjectIsMadeFoundByAFilterUpdatedAndDeletedInTheRunsProject()
    {
        await using var client = LiveSite.Client(LiveSite.Credentials());
        var parent = await LiveSite.ProjectAsync(client);

        var made = await client.Projects.CreateAsync(new Project
        {
            Name = NestedName,
            Description = LiveSite.ProjectDescription,
            ParentProjectId = parent,
        });
        var nested = new ProjectListOptions
        {
            Filter = TableauFilter.And(TableauFilter.Eq(ProjectField.ParentProjectId, parent), TableauFilter.Eq(ProjectField.Name, NestedName)),
            PageSize = 1,
        };
        var found = await LiveSite.EventuallyAsync(async () => (await AreaFixtures.AllAsync(client.Projects.ListAsync(nested))).SingleOrDefault(),
            "The nested project");
        var updated = await client.Projects.UpdateAsync(made.Id!, new Project { Description = "Updated by Tableau.Api's live suite." });
        await client.Projects.DeleteAsync(made.Id!);

        Assert.AreEqual(made.Id, found.Id);
        Assert.AreEqual((NestedName, parent), (found.Name, found.ParentProjectId));
        Assert.AreEqual(("Updated by Tableau.Api's live suite.", NestedName), (updated.Description, updated.Name));
        var page = await client.Projects.ListPageAsync(1, new ProjectListOptions
        {
            Filter = TableauFilter.Eq(ProjectField.TopLevelProject, true),
            Sort = TableauSort.Desc(ProjectField.CreatedAt),
            PageSize = 1,
        });
        Assert.HasCount(1, page.Items);
        Assert.IsGreaterThanOrEqualTo(1, page.TotalAvailable ?? 0);
    }

    [SiteTest(TableauSite.Live, [LiveSite.ServerUrl], SignsIn = true), TestCategory(TableauSite.Live)]
    public async Task ALocalGroupIsMadeFoundRenamedGivenTheSuitesUserAndDeleted()
    {
        await using var client = LiveSite.Client(LiveSite.Credentials());
        var group = await LiveSite.NewGroupAsync(client);
        var self = LiveSite.Saw(client.Pipeline.State.Sessions.Current!).UserId;
        var renamed = LiveSite.GroupName(DateTimeOffset.UtcNow.AddSeconds(1));
        bool Named(Group listed, string name) => listed.Id == group.Id && listed.Name == name;

        var listed = await LiveSite.EventuallyAsync(async () => (await AreaFixtures.AllAsync(client.Groups.ListAsync(new GroupListOptions
        {
            Filter = TableauFilter.Eq(GroupField.Name, group.Name!),
        }))).SingleOrDefault(found => Named(found, group.Name!)), "The run's group");
        var updated = await client.Groups.UpdateAsync(group.Id!, name: renamed);
        await client.Groups.AddUserAsync(group.Id!, self);
        var members = await AreaFixtures.AllAsync(client.Groups.ListUsersAsync(group.Id!));
        var selfsGroups = await AreaFixtures.AllAsync(client.Users.ListGroupsAsync(self));
        await client.Groups.RemoveUserAsync(group.Id!, self);
        var left = await AreaFixtures.AllAsync(client.Groups.ListUsersAsync(group.Id!));
        await client.Groups.DeleteAsync(group.Id!);
        LiveSite.GroupGone(group.Id!);

        Assert.IsTrue(LiveSite.IsRunGroup(listed), "the sweep knows the run's group");
        Assert.AreEqual(renamed, updated.Name);
        CollectionAssert.AreEqual(new[] { self }, members.Select(member => member.Id).ToArray());
        Assert.IsTrue(selfsGroups.Any(found => Named(found, renamed)), "the user's groups hold the group, by its new name");
        Assert.IsEmpty(left);
        await ClientFixtures.FailsAsync(TableauErrorKind.NotFound, () => AreaFixtures.AllAsync(client.Groups.ListUsersAsync(group.Id!)));
    }

    [SiteTest(TableauSite.Live, [LiveSite.ServerUrl], SignsIn = true), TestCategory(TableauSite.Live)]
    public async Task UsersAreListedPageByPageAndTheSuitesOwnIsReadByIdAndByFilter()
    {
        await using var client = LiveSite.Client(LiveSite.Credentials());

        // Three pages of two at most, so a site with a large directory isn't read whole each night.
        var some = new List<User>();
        await foreach (var user in client.Users.ListAsync(new UserListOptions { PageSize = 2 }))
        {
            some.Add(user);
            if (some.Count == 6) break;
        }
        var self = LiveSite.Saw(client.Pipeline.State.Sessions.Current!).UserId;
        var read = await client.Users.GetAsync(self);
        var filtered = await AreaFixtures.AllAsync(client.Users.ListAsync(new UserListOptions { Filter = TableauFilter.Eq(UserField.Luid, self) }));
        var groups = await AreaFixtures.AllAsync(client.Users.ListGroupsAsync(self));
        var firstPage = await client.Users.ListPageAsync(1, new UserListOptions { PageSize = 2 });

        Assert.HasCount(some.Count, some.Select(user => user.Id).Distinct(), "no user twice across pages");
        Assert.AreEqual(Math.Min(6, firstPage.TotalAvailable ?? 0), some.Count);
        Assert.AreEqual(self, read.Id);
        Assert.IsNotNull(read.SiteRole);
        Assert.AreEqual(self, filtered.Single().Id);
        Assert.IsTrue(groups.Any(group => group.Name == "All Users"), "every user is in All Users");
    }

    [SiteTest(TableauSite.Live, [LiveSite.ServerUrl], SignsIn = true), TestCategory(TableauSite.Live)]
    public async Task ACapabilityIsGrantedToARunsGroupOnTheRunsProjectReadBackAndDeleted()
    {
        await using var client = LiveSite.Client(LiveSite.Credentials());
        var project = TableauContent.Project(await LiveSite.ProjectAsync(client));
        var group = await LiveSite.NewGroupAsync(client);
        static bool Grants(Permissions permissions, string groupId) =>
            permissions.GranteeCapabilities!.Any(rule => rule.Group?.Id == groupId
                && rule.Capabilities!.Any(capability => capability.Name == CapabilityName.Read && capability.Mode == CapabilityMode.Allow));

        var added = await client.Permissions.AddAsync(project,
        [
            new GranteeCapabilities
            {
                Group = new Group { Id = group.Id },
                Capabilities = [new Capability { Name = CapabilityName.Read, Mode = CapabilityMode.Allow }],
            },
        ]);
        var read = await client.Permissions.GetAsync(project);
        await client.Permissions.DeleteAsync(project, PermissionGrantee.Group(group.Id!), CapabilityName.Read, CapabilityMode.Allow);
        var after = await client.Permissions.GetAsync(project);
        await client.Groups.DeleteAsync(group.Id!);
        LiveSite.GroupGone(group.Id!);

        Assert.IsTrue(Grants(added, group.Id!), "the add answers with the group's Read");
        Assert.IsTrue(Grants(read, group.Id!), "the list holds the group's Read");
        Assert.AreEqual(project.Id, read.Project?.Id);
        Assert.IsFalse(Grants(after, group.Id!), "the delete took the group's Read away");
    }

    [SiteTest(TableauSite.Live, [LiveSite.ServerUrl], SignsIn = true), TestCategory(TableauSite.Live)]
    public async Task AMetadataQueryReadsTheWorkbooksPageByPage()
    {
        await using var client = LiveSite.Client(LiveSite.Credentials());
        const string Workbooks = """
            query workbooks($first: Int, $after: String) { workbooksConnection(first: $first, after: $after) { nodes { luid name } pageInfo { hasNextPage endCursor } totalCount } }
            """;

        var total = (await client.Metadata.QueryAsync("{ workbooksConnection(first: 1) { totalCount } }"))
            .GetProperty("workbooksConnection").GetProperty("totalCount").GetInt32();
        // Three pages of two at most, so a site with many workbooks isn't read whole each night.
        var some = new List<LiveWorkbookNode>();
        await foreach (var workbook in client.Metadata.ListNodesAsync(Workbooks, "workbooksConnection", LiveJson.Default.LiveWorkbookNode, pageSize: 2))
        {
            some.Add(workbook);
            if (some.Count == 6) break;
        }
        var first = await client.Metadata.QueryAsync(Workbooks, new System.Text.Json.Nodes.JsonObject { ["first"] = 2 },
            LiveJson.Default.LiveWorkbooksData);

        Assert.AreEqual(Math.Min(6, total), some.Count);
        Assert.HasCount(some.Count, some.Select(workbook => workbook.Luid).Distinct(), "no workbook twice across pages");
        Assert.AreEqual(total, first.WorkbooksConnection.TotalCount);
        CollectionAssert.AreEqual(some.Take(2).Select(workbook => workbook.Luid).ToArray(), first.WorkbooksConnection.Nodes.Select(workbook => workbook.Luid).ToArray());
    }
}

/// <summary>A workbook as the live suite's Metadata API query asks for it.</summary>
/// <param name="Luid">Its LUID.</param>
/// <param name="Name">Its name.</param>
internal sealed record LiveWorkbookNode(string Luid, string Name);

/// <summary>The data of the live suite's workbooks query.</summary>
/// <param name="WorkbooksConnection">The connection.</param>
internal sealed record LiveWorkbooksData(LiveWorkbooksConnection WorkbooksConnection);

/// <summary>A workbooks connection: its nodes and its total.</summary>
/// <param name="Nodes">The nodes.</param>
/// <param name="TotalCount">How many there are in all.</param>
internal sealed record LiveWorkbooksConnection(IReadOnlyList<LiveWorkbookNode> Nodes, int TotalCount);

/// <summary>The live suite's own source-generated JSON, for its Metadata API query.</summary>
[System.Text.Json.Serialization.JsonSourceGenerationOptions(PropertyNamingPolicy = System.Text.Json.Serialization.JsonKnownNamingPolicy.CamelCase)]
[System.Text.Json.Serialization.JsonSerializable(typeof(LiveWorkbookNode))]
[System.Text.Json.Serialization.JsonSerializable(typeof(LiveWorkbooksData))]
internal sealed partial class LiveJson : System.Text.Json.Serialization.JsonSerializerContext;

/// <summary>
/// What only a Tableau Server has, or what changes its users and sites, run by hand with TABLEAU_SERVER_ONLY=1 on a server
/// you may change, signed in as a server administrator (TABLEAU_SERVER_ONLY_SERVER_URL, TABLEAU_SERVER_ONLY_SITE,
/// TABLEAU_SERVER_ONLY_USER and TABLEAU_SERVER_ONLY_PASSWORD, as for the session tests): a site made with a revision limit
/// sent as a JSON number, updated and deleted; a group imported from Active Directory and synchronised, at once and as a
/// job (TABLEAU_SERVER_ONLY_AD_GROUP, a directory group not on the site, and TABLEAU_SERVER_ONLY_AD_DOMAIN, its domain;
/// its users join the site and stay); and a user added, updated and removed (TABLEAU_SERVER_ONLY_NEW_USER, a name not on
/// the site, from the directory where the server uses one). One at a time, as the live ones.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class ServerOnlyAreaTests
{
    private const string ServerUrl = "TABLEAU_SERVER_ONLY_SERVER_URL";
    private const string User = "TABLEAU_SERVER_ONLY_USER";
    private const string Password = "TABLEAU_SERVER_ONLY_PASSWORD";
    private const string DirectoryGroup = "TABLEAU_SERVER_ONLY_AD_GROUP";
    private const string DirectoryDomain = "TABLEAU_SERVER_ONLY_AD_DOMAIN";
    private const string NewUser = "TABLEAU_SERVER_ONLY_NEW_USER";

    [SiteTest(TableauSite.ServerOnly, [ServerUrl, User, Password]), TestCategory(TableauSite.ServerOnly)]
    public async Task ASiteIsMadeWithARevisionLimitUpdatedOnItAndDeleted()
    {
        await using var client = Client();
        var first = await SignInAsync(client);
        var contentUrl = "tableau-api-server-only-" + LiveSite.Run;
        Site? made = null;
        Site updated;
        Site read;
        try
        {
            made = await client.WithSession(first).Sites.CreateAsync(new Site
            {
                Name = "Tableau.Api ServerOnly " + LiveSite.Run,
                ContentUrl = contentUrl,
                RevisionHistoryEnabled = true,
                RevisionLimit = 25,
            });
            // Tableau updates only the site a session is on, so the update, the read and the delete are made from one.
            var onIt = LiveSite.Saw(await client.Sessions.SignInAsync(TableauCredentials.Password(TableauSite.Setting(User),
                LiveSite.Secret(Password)), contentUrl));
            var there = client.WithSession(onIt);
            updated = await there.Sites.UpdateAsync(made.Id!, new Site { RevisionLimit = 30 });
            read = await there.Sites.GetByContentUrlAsync(contentUrl);
            await there.Sites.DeleteAsync(made.Id!);
        }
        catch (Exception failed) when (made?.Id is { } id)
        {
            // The site the test made goes whatever failed; one that can't is named, so it can be deleted by hand.
            try
            {
                await client.WithSession(first).Sites.DeleteAsync(id);
            }
            catch (TableauException cleanup)
            {
                throw new AggregateException($"The site '{contentUrl}' ({id}) is left on the server: delete it by hand.", failed, cleanup);
            }
            throw;
        }
        finally
        {
            await client.Sessions.SignOutAsync(first);
        }

        Assert.AreEqual((contentUrl, 25L), (made.ContentUrl, made.RevisionLimit));
        Assert.AreEqual(30L, updated.RevisionLimit);
        Assert.AreEqual((made.Id, 30L), (read.Id, read.RevisionLimit));
    }

    [SiteTest(TableauSite.ServerOnly, [ServerUrl, User, Password, DirectoryGroup, DirectoryDomain]), TestCategory(TableauSite.ServerOnly)]
    public async Task AGroupIsImportedFromActiveDirectoryAndSynchronisedAtOnceAndAsAJob()
    {
        await using var client = Client();
        var session = await SignInAsync(client);
        var bound = client.WithSession(session);
        var name = TableauSite.Setting(DirectoryGroup);
        var domain = TableauSite.Setting(DirectoryDomain);
        try
        {
            var imported = await bound.Groups.ImportFromActiveDirectoryAsync(name, domain, SiteRole.Unlicensed);
            Group synchronised;
            Job job;
            try
            {
                synchronised = await bound.Groups.UpdateFromActiveDirectoryAsync(imported.Id!, name, domain, SiteRole.Unlicensed,
                    ImportDirectiveGrantLicenseMode.OnLogin);
                job = await bound.Groups.UpdateFromActiveDirectoryAsJobAsync(imported.Id!, name, domain, SiteRole.Unlicensed);
            }
            finally
            {
                await bound.Groups.DeleteAsync(imported.Id!);
            }
            var importJob = await bound.Groups.ImportFromActiveDirectoryAsJobAsync(name, domain, SiteRole.Unlicensed);
            // The job makes the group in its own time, and the list may still show the one just deleted: the new one has
            // the directory group's name and domain and another id. The test deletes it once it is there, or names it.
            Group? byJob = null;
            try
            {
                byJob = await LiveSite.EventuallyAsync(async () => (await AreaFixtures.AllAsync(bound.Groups.ListAsync(new GroupListOptions
                {
                    Filter = TableauFilter.Eq(GroupField.Name, name),
                }))).FirstOrDefault(group => group.Id != imported.Id && string.Equals(group.Domain?.Name, domain, StringComparison.OrdinalIgnoreCase)),
                    $"The group the import job made, '{name}' in {domain}, which is left to delete by hand should it show later,",
                    TimeSpan.FromMinutes(2));
            }
            finally
            {
                if (byJob?.Id is { } id) await bound.Groups.DeleteAsync(id);
            }

            Assert.AreEqual((name, domain), (imported.Name, imported.Import?.DomainName));
            Assert.AreEqual(imported.Id, synchronised.Id);
            Assert.AreEqual(JobType.GroupSync, job.Type);
            Assert.AreEqual("GroupImport", importJob.Type?.Value);
        }
        finally
        {
            await client.Sessions.SignOutAsync(session);
        }
    }

    [SiteTest(TableauSite.ServerOnly, [ServerUrl, User, Password, NewUser]), TestCategory(TableauSite.ServerOnly)]
    public async Task AUserIsAddedUpdatedAndRemoved()
    {
        await using var client = Client();
        var session = await SignInAsync(client);
        var bound = client.WithSession(session);
        try
        {
            var added = await bound.Users.AddAsync(TableauSite.Setting(NewUser), SiteRole.Unlicensed);
            User updated;
            User read;
            try
            {
                updated = await bound.Users.UpdateAsync(added.Id!, new User { SiteRole = SiteRole.Viewer });
                read = await bound.Users.GetAsync(added.Id!);
            }
            finally
            {
                await bound.Users.RemoveAsync(added.Id!, mapAssetsToUserId: session.UserId);
            }

            Assert.AreEqual(SiteRole.Unlicensed, added.SiteRole);
            Assert.AreEqual(SiteRole.Viewer, updated.SiteRole);
            Assert.AreEqual((added.Id, SiteRole.Viewer), (read.Id, read.SiteRole));
            await ClientFixtures.FailsAsync(TableauErrorKind.NotFound, () => bound.Users.GetAsync(added.Id!));
        }
        finally
        {
            await client.Sessions.SignOutAsync(session);
        }
    }

    private static async Task<TableauSession> SignInAsync(TableauClient client) =>
        LiveSite.Saw(await client.Sessions.SignInAsync(TableauCredentials.Password(TableauSite.Setting(User), LiveSite.Secret(Password))));

    // Over the suite's HttpClient, so every token it carries is kept for the log check.
    private static TableauClient Client() => LiveSite.Client(null, new TableauClientOptions
    {
        ServerUrl = new Uri(TableauSite.Setting(ServerUrl)),
        Site = Environment.GetEnvironmentVariable("TABLEAU_SERVER_ONLY_SITE") ?? "",
        Deployment = TableauDeployment.Server,
    });
}
