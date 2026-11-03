using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Tableau.Api.Pipeline;
using Tableau.Api.Rest.Models;

namespace Tableau.Api.Tests;

/// <summary>
/// Favorites and permissions in the run's site (<see cref="LiveSite"/> has the settings), on the shared samples and
/// the run's project, each change taken back in the test that made it: a sample workbook, one of its views, the samples'
/// data source and the run's project added to the suite's user's favorites, listed and removed; each kind of default
/// permission granted to a run's group on the run's project, read back and deleted, the kinds Tableau refuses pinned by
/// their code; a capability granted to a run's group on a sample workbook, a view and the data source, read back and
/// deleted; and a group set of the run's own, made by a test-side call since the 2.0 client has no Create Group Set,
/// granted a capability on the run's project, which Tableau Server 2025.1 answers and drops, read back, and deleted by its
/// <c>groupSets</c> path, which it refuses.
/// The run's site's deletion takes the groups and the group set. One at a time, as the other live tests.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class LiveAccessTests
{
    // Create Group Set (REST API 3.22), sent through the client's own pipeline, so it signs in, escapes and logs as any
    // call does; the matrix has no row for it, as Endpoints doesn't.
    private static readonly Endpoint _createGroupSet = new("Create Group Set", HttpMethod.Post, "{version}/sites/{siteId}/groupsets",
        new Version(3, 22), TableauDeployments.Both, Idempotent: false, TransferKind.None);

    private static readonly Capability[] _read = [new Capability { Name = CapabilityName.Read, Mode = CapabilityMode.Allow }];

    public TestContext TestContext { get; set; } = null!;

    [SiteTest(TableauSite.Live, [LiveSite.ServerUrl], SignsIn = LiveSignIn.Token), TestCategory(TableauSite.Live)]
    public async Task ASampleWorkbookViewAndDataSourceAndTheRunsProjectAreFavoritedListedAndRemoved()
    {
        await using var client = await LiveSite.RunClientAsync();
        var project = await LiveSite.ProjectAsync(client);
        var workbook = (await LiveSite.SamplesAsync(client))[0];
        var view = (await AreaFixtures.AllAsync(client.Views.ListForWorkbookAsync(workbook.Id!)))[0];
        var dataSource = await SampleDataSourceAsync(client, project);
        var self = LiveSite.Saw(client.Pipeline.State.Sessions.Current!).UserId;
        TableauContent[] contents =
        [
            TableauContent.Workbook(workbook.Id!), TableauContent.View(view.Id!), TableauContent.DataSource(dataSource.Id!), TableauContent.Project(project),
        ];

        var answered = new List<IReadOnlyList<Favorite>>();
        foreach (var content in contents)
            answered.Add(await client.Favorites.AddAsync(self, $"{LiveSite.ProjectName} {content.Kind}", content));
        var listed = await AreaFixtures.AllAsync(client.Favorites.ListAsync(self));
        foreach (var content in contents)
            await client.Favorites.RemoveAsync(self, content);
        var after = await AreaFixtures.AllAsync(client.Favorites.ListAsync(self));
        foreach (var favorite in listed)
            Record($"a favorite listed: workbook {favorite.Workbook?.Id is not null}, view {favorite.View?.Id is not null}, data source {favorite.Datasource?.Id is not null}, project {favorite.Project?.Id is not null}, label '{favorite.Label}'");

        for (var i = 0; i < contents.Length; i++)
        {
            Assert.IsTrue(answered[i].Any(favorite => Holds(favorite, contents[i])), $"Add {contents[i].Kind} to Favorites answers the favorite");
            // Tableau Server 2025.1 lists a favorite under its content's name, whatever label Add was given.
            Assert.IsTrue(listed.Any(favorite => Holds(favorite, contents[i])), $"the list holds the {contents[i].Kind}");
            Assert.IsFalse(after.Any(favorite => Holds(favorite, contents[i])), $"the {contents[i].Kind} is gone after its remove");
        }
    }

    [SiteTest(TableauSite.Live, [LiveSite.ServerUrl], SignsIn = LiveSignIn.Token), TestCategory(TableauSite.Live)]
    public async Task EachKindOfDefaultPermissionIsGrantedToARunsGroupOnTheRunsProjectReadBackAndDeleted()
    {
        await using var client = await LiveSite.RunClientAsync();
        var project = await LiveSite.ProjectAsync(client);
        var group = await LiveSite.NewGroupAsync(client);
        var grantee = PermissionGrantee.Group(group.Id!);

        var outcomes = new Dictionary<DefaultPermissionsKind, string>();
        foreach (var kind in Enum.GetValues<DefaultPermissionsKind>())
        {
            // Each call on its own, so a kind Tableau refuses shows each of its calls' code.
            var added = await OutcomeAsync(async () => Grants(await client.Permissions.AddDefaultAsync(project, kind, [Rule(group.Id!)]), group.Id!));
            var read = await OutcomeAsync(async () => Grants(await client.Permissions.GetDefaultAsync(project, kind), group.Id!));
            var deleted = await OutcomeAsync(async () =>
            {
                await client.Permissions.DeleteDefaultAsync(project, kind, grantee, CapabilityName.Read, CapabilityMode.Allow);
                return "deleted";
            });
            var after = await OutcomeAsync(async () => Grants(await client.Permissions.GetDefaultAsync(project, kind), group.Id!));
            outcomes[kind] = $"added: {added}; read: {read}; deleted: {deleted}; after: {after}";
            Record($"default permissions for {kind}: {outcomes[kind]}");
        }
        await client.Groups.DeleteAsync(group.Id!);

        foreach (var (kind, outcome) in outcomes)
        {
            // Tableau Server 2025.1 answers 405000 to a project's default permissions for data roles, listed or added, and
            // 404003 to a delete, an unknown resource.
            var expected = kind == DefaultPermissionsKind.DataRoles
                ? "added: refused 405000; read: refused 405000; deleted: refused 404003; after: refused 405000"
                : "added: granted; read: granted; deleted: deleted; after: not granted";
            Assert.AreEqual(expected, outcome, $"default permissions for {kind}");
        }
    }

    [SiteTest(TableauSite.Live, [LiveSite.ServerUrl], SignsIn = LiveSignIn.Token), TestCategory(TableauSite.Live)]
    public async Task ACapabilityIsGrantedToARunsGroupOnASampleWorkbookViewAndDataSourceReadBackAndDeleted()
    {
        await using var client = await LiveSite.RunClientAsync();
        var project = await LiveSite.ProjectAsync(client);
        var workbook = (await LiveSite.SamplesAsync(client))[0];
        var view = (await AreaFixtures.AllAsync(client.Views.ListForWorkbookAsync(workbook.Id!)))[0];
        var dataSource = await SampleDataSourceAsync(client, project);
        var group = await LiveSite.NewGroupAsync(client);
        TableauContent[] contents = [TableauContent.Workbook(workbook.Id!), TableauContent.View(view.Id!), TableauContent.DataSource(dataSource.Id!)];
        // A view's permissions are its workbook's while the workbook shows its sheets as tabs: the shared sample hides them
        // for the test, and shows them again after.
        var showTabs = (await client.Workbooks.GetAsync(workbook.Id!)).ShowTabs;
        await client.Workbooks.UpdateAsync(workbook.Id!, new WorkbookUpdate { ShowTabs = false });

        var outcomes = new List<string>();
        foreach (var content in contents)
        {
            var added = await OutcomeAsync(async () => Grants(await client.Permissions.AddAsync(content, [Rule(group.Id!)]), group.Id!));
            var read = await OutcomeAsync(async () => Grants(await client.Permissions.GetAsync(content), group.Id!));
            var deleted = await OutcomeAsync(async () =>
            {
                await client.Permissions.DeleteAsync(content, PermissionGrantee.Group(group.Id!), CapabilityName.Read, CapabilityMode.Allow);
                return "deleted";
            });
            var after = await OutcomeAsync(async () => Grants(await client.Permissions.GetAsync(content), group.Id!));
            outcomes.Add($"{content.Kind}: added: {added}; read: {read}; deleted: {deleted}; after: {after}");
            Record($"permissions on a sample {outcomes[^1]}");
        }
        var restored = await client.Workbooks.UpdateAsync(workbook.Id!, new WorkbookUpdate { ShowTabs = showTabs == "true" });
        await client.Groups.DeleteAsync(group.Id!);

        CollectionAssert.AreEqual(contents.Select(content => $"{content.Kind}: added: granted; read: granted; deleted: deleted; after: not granted").ToArray(),
            outcomes.ToArray());
        Assert.AreEqual(showTabs, restored.ShowTabs, "the sample shows its tabs as it did");
    }

    [SiteTest(TableauSite.Live, [LiveSite.ServerUrl], SignsIn = LiveSignIn.Token), TestCategory(TableauSite.Live)]
    public async Task AGroupSetOfTheRunsIsGrantedACapabilityOnTheRunsProjectReadBackAndDeletedByItsPath()
    {
        await using var client = await LiveSite.RunClientAsync();
        var project = TableauContent.Project(await LiveSite.ProjectAsync(client));
        var set = await client.Pipeline.SendAsync(_createGroupSet,
            new TableauRequest(Body: new TsRequest { GroupSet = new GroupSet { Name = LiveSite.GroupName(DateTimeOffset.UtcNow) } }),
            static body => body.GroupSet, "groupSet", default);
        bool Grants(Permissions permissions) => permissions.GranteeCapabilities?.Any(rule => rule.GroupSet?.Id == set.Id
            && rule.Capabilities!.Any(capability => capability.Name == CapabilityName.Read && capability.Mode == CapabilityMode.Allow)) == true;

        var added = await client.Permissions.AddAsync(project, [new GranteeCapabilities { GroupSet = new GroupSet { Id = set.Id }, Capabilities = _read }]);
        var read = await client.Permissions.GetAsync(project);
        var deleted = await OutcomeAsync(async () =>
        {
            await client.Permissions.DeleteAsync(project, PermissionGrantee.GroupSet(set.Id!), CapabilityName.Read, CapabilityMode.Allow);
            return "deleted";
        });
        var after = await client.Permissions.GetAsync(project);

        var outcome = $"the add answered it: {Grants(added)}; listed: {Grants(read)}; the delete: {deleted}; listed after: {Grants(after)}";
        Record($"a group set's Read on the run's project: {outcome}");
        Assert.IsTrue(Guid.TryParse(set.Id, out _), "Create Group Set answers the set's LUID");
        // Tableau Server 2025.1 answers a group set's grant with a 200 and drops it, its answer and its list holding no
        // such rule, and refuses the delete under permissions/groupSets/ with 400000, "the grantee type must be a group,
        // groupset or user". Either that, or the rule granted, read back and gone after the delete.
        Assert.IsTrue(outcome is "the add answered it: True; listed: True; the delete: deleted; listed after: False"
            or "the add answered it: False; listed: False; the delete: refused 400000; listed after: False", outcome);
    }

    // The samples' data source in the run's project, which Tableau Server 2025.1 publishes beside the samples.
    private static Task<DataSource> SampleDataSourceAsync(TableauClient client, string project) =>
        LiveSite.EventuallyAsync(async () => (await AreaFixtures.AllAsync(client.DataSources.ListAsync(new DataSourceListOptions
        {
            Filter = TableauFilter.Eq(DataSourceField.ProjectName, LiveSite.ProjectName),
        }))).FirstOrDefault(found => found.Project?.Id == project), "The samples' data source");

    private static GranteeCapabilities Rule(string groupId) => new() { Group = new Group { Id = groupId }, Capabilities = _read };

    private static string Grants(Permissions permissions, string groupId) =>
        permissions.GranteeCapabilities?.Any(rule => rule.Group?.Id == groupId
            && rule.Capabilities!.Any(capability => capability.Name == CapabilityName.Read && capability.Mode == CapabilityMode.Allow)) == true
            ? "granted"
            : "not granted";

    private static bool Holds(Favorite favorite, TableauContent content) => content.Kind switch
    {
        TableauContentKind.Workbook => favorite.Workbook?.Id == content.Id,
        TableauContentKind.View => favorite.View?.Id == content.Id,
        TableauContentKind.DataSource => favorite.Datasource?.Id == content.Id,
        TableauContentKind.Project => favorite.Project?.Id == content.Id,
        _ => false,
    };

    // What a call gave, or Tableau's refusal by its code, its summary and detail in the test's output.
    private async Task<string> OutcomeAsync(Func<Task<string>> call)
    {
        try
        {
            return await call();
        }
        catch (TableauException e) when (e.StatusCode is not null)
        {
            var refused = $"refused {e.ErrorCode ?? ((int)e.StatusCode).ToString(System.Globalization.CultureInfo.InvariantCulture)}";
            Record($"{refused}: {e.Summary}: {e.Detail}");
            return refused;
        }
    }

    private void Record(string line) => TestContext.WriteLine(LiveSite.Scrubbed(line));
}
