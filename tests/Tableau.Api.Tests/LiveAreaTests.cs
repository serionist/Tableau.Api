using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Tableau.Api.Rest.Models;

namespace Tableau.Api.Tests;

/// <summary>
/// Projects, groups, users, permissions and the Metadata API in the run's site (<see cref="LiveSite"/> has the
/// settings): a project made in the run's project, found by a filter whose value Tableau must read as written, updated
/// and deleted; a local group made, found, renamed, given the suite's own user and then without them, and deleted; users
/// read a page of one at a time, never changed; a capability granted to a run's group on the run's project, read back and
/// deleted; and a read-only query of the Metadata API, once it lists the run's samples, paged by its cursor. Nothing is
/// tagged, no data quality warning is set, and no database, table or column is changed. They run one at a time, with the
/// session tests: a personal access token allows one session, so another test's sign-in would end this one's.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class LiveAreaTests
{
    /// <summary>A name with an ampersand, pluses, a hash, a percent and a space, which a filter carries as written.</summary>
    private const string NestedName = "R&D C++ #1 100%";

    [SiteTest(TableauSite.Live, [LiveSite.ServerUrl], SignsIn = LiveSignIn.Token), TestCategory(TableauSite.Live)]
    public async Task AProjectIsMadeFoundByAFilterUpdatedAndDeletedInTheRunsProject()
    {
        await using var client = await LiveSite.RunClientAsync();
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

    [SiteTest(TableauSite.Live, [LiveSite.ServerUrl], SignsIn = LiveSignIn.Token), TestCategory(TableauSite.Live)]
    public async Task ALocalGroupIsMadeFoundRenamedGivenTheSuitesUserAndDeleted()
    {
        await using var client = await LiveSite.RunClientAsync();
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

        Assert.AreEqual("local", listed.Domain?.Name, "a group the suite makes is local");
        Assert.AreEqual(renamed, updated.Name);
        CollectionAssert.AreEqual(new[] { self }, members.Select(member => member.Id).ToArray());
        Assert.IsTrue(selfsGroups.Any(found => Named(found, renamed)), "the user's groups hold the group, by its new name");
        Assert.IsEmpty(left);
        await ClientFixtures.FailsAsync(TableauErrorKind.NotFound, () => AreaFixtures.AllAsync(client.Groups.ListUsersAsync(group.Id!)));
    }

    [SiteTest(TableauSite.Live, [LiveSite.ServerUrl], SignsIn = LiveSignIn.Token), TestCategory(TableauSite.Live)]
    public async Task UsersAreListedPageByPageAndTheSuitesOwnIsReadByIdAndByFilter()
    {
        await using var client = await LiveSite.RunClientAsync();

        // Pages of one, since the run's site holds few users; six at most, should it hold more.
        var some = new List<User>();
        await foreach (var user in client.Users.ListAsync(new UserListOptions { PageSize = 1 }))
        {
            some.Add(user);
            if (some.Count == 6) break;
        }
        var self = LiveSite.Saw(client.Pipeline.State.Sessions.Current!).UserId;
        var read = await client.Users.GetAsync(self);
        var filtered = await AreaFixtures.AllAsync(client.Users.ListAsync(new UserListOptions { Filter = TableauFilter.Eq(UserField.Luid, self) }));
        var groups = await AreaFixtures.AllAsync(client.Users.ListGroupsAsync(self));
        var firstPage = await client.Users.ListPageAsync(1, new UserListOptions { PageSize = 1 });

        Assert.HasCount(some.Count, some.Select(user => user.Id).Distinct(), "no user twice across pages");
        Assert.AreEqual(Math.Min(6, firstPage.TotalAvailable ?? 0), some.Count);
        Assert.AreEqual(self, read.Id);
        Assert.IsNotNull(read.SiteRole);
        Assert.AreEqual(self, filtered.Single().Id);
        Assert.IsTrue(groups.Any(group => group.Name == "All Users"), "every user is in All Users");
    }

    [SiteTest(TableauSite.Live, [LiveSite.ServerUrl], SignsIn = LiveSignIn.Token), TestCategory(TableauSite.Live)]
    public async Task ACapabilityIsGrantedToARunsGroupOnTheRunsProjectReadBackAndDeleted()
    {
        await using var client = await LiveSite.RunClientAsync();
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

        Assert.IsTrue(Grants(added, group.Id!), "the add answers with the group's Read");
        Assert.IsTrue(Grants(read, group.Id!), "the list holds the group's Read");
        Assert.AreEqual(project.Id, read.Project?.Id);
        Assert.IsFalse(Grants(after, group.Id!), "the delete took the group's Read away");
    }

    [SiteTest(TableauSite.Live, [LiveSite.ServerUrl], SignsIn = LiveSignIn.Token), TestCategory(TableauSite.Live)]
    public async Task AMetadataQueryReadsTheWorkbooksPageByPage()
    {
        await using var client = await LiveSite.RunClientAsync();
        var samples = await LiveSite.SamplesAsync(client);
        const string Workbooks = """
            query workbooks($first: Int, $after: String, $project: String) { workbooksConnection(first: $first, after: $after, filter: { projectName: $project }) { nodes { luid name } pageInfo { hasNextPage endCursor } totalCount } }
            """;
        const string Count = """
            query count($project: String) { workbooksConnection(first: 1, filter: { projectName: $project }) { totalCount } }
            """;
        static System.Text.Json.Nodes.JsonObject InProject() => new() { ["project"] = LiveSite.ProjectName };

        // The Metadata API lists a new site's samples once it has taken them in; the counts come from the run's project
        // alone, which the projects other tests make for their own samples don't change.
        var total = (await LiveSite.EventuallyAsync(async () =>
            (await client.Metadata.QueryAsync(Count, InProject())).GetProperty("workbooksConnection").GetProperty("totalCount").GetInt32() is var count
                && count >= LiveSite.Samples ? Tuple.Create(count) : null, "The run's samples, in the Metadata API", TimeSpan.FromMinutes(5))).Item1;
        // Pages of one, so the list follows a cursor.
        var all = await AreaFixtures.AllAsync(client.Metadata.ListNodesAsync(Workbooks, "workbooksConnection", LiveJson.Default.LiveWorkbookNode,
            InProject(), pageSize: 1));
        var variables = InProject();
        variables["first"] = 1;
        var first = await client.Metadata.QueryAsync(Workbooks, variables, LiveJson.Default.LiveWorkbooksData);

        Assert.AreEqual(total, all.Count);
        Assert.HasCount(all.Count, all.Select(workbook => workbook.Luid).Distinct(), "no workbook twice across pages");
        CollectionAssert.AreEquivalent(samples.Select(workbook => workbook.Id).ToList(), all.Select(workbook => workbook.Luid).ToList(),
            "the run's project holds the samples alone");
        Assert.AreEqual(total, first.WorkbooksConnection.TotalCount);
        CollectionAssert.AreEqual(all.Take(1).Select(workbook => workbook.Luid).ToArray(), first.WorkbooksConnection.Nodes.Select(workbook => workbook.Luid).ToArray());
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
