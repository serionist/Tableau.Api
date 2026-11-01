using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Tableau.Api.Rest.Models;

namespace Tableau.Api.Tests;

/// <summary>
/// The server's Active Directory in the run's site (<see cref="LiveSite"/> has the settings), with the maintainers'
/// technical accounts, enabled, with passwords nobody holds: a group with one member, and a user on its own. The group
/// is imported at once, its member listed, synchronised at once and as a job, and deleted; then imported as a job, found
/// by its name and domain, and deleted. The user is added unlicensed, made a Viewer, read, impersonated by a sign-in
/// with the token, and removed, its assets mapped to the suite's own user. The site's deletion takes the groups and the
/// users' memberships; the accounts stay in the directory, and on the server. The accounts' names are settings, never in
/// the repo: each name Tableau answers is kept with the known values as it is read, so no output or failed result shows
/// it, and the assertions name the setting, never a value. One at a time, as the other live tests.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class LiveDirectoryTests
{
    // Tableau Server 2025.1 refuses an import without a grant licence mode (400125), and answers one granting an
    // unlicensed role on sign-in with a 500: the groups grant theirs on synchronisation.
    private static readonly ImportDirectiveGrantLicenseMode _onSync = ImportDirectiveGrantLicenseMode.OnSync;

    [SiteTest(TableauSite.Live, [LiveSite.ServerUrl, LiveSite.DirectoryGroup, LiveSite.DirectoryDomain, LiveSite.DirectoryGroupMember],
         SignsIn = LiveSignIn.Token), TestCategory(TableauSite.Live)]
    public async Task ADirectoryGroupIsImportedWithItsMemberSynchronisedAndDeletedThenImportedAsAJobFoundAndDeleted()
    {
        var (name, domain, member) = (TableauSite.Setting(LiveSite.DirectoryGroup), TableauSite.Setting(LiveSite.DirectoryDomain),
            TableauSite.Setting(LiveSite.DirectoryGroupMember));
        await using var client = await LiveSite.RunClientAsync();

        var imported = await client.Groups.ImportFromActiveDirectoryAsync(name, domain, SiteRole.Unlicensed, _onSync);
        // Tableau names the group as the directory shows it, which may be another form than the one imported.
        var named = LiveSite.Learned(imported.Name) ?? throw new InvalidOperationException("Create Group answered no name.");
        LiveSite.Learned(imported.Import?.DomainName);
        LiveSite.Learned(imported.Domain?.Name);
        var members = await AreaFixtures.AllAsync(client.Groups.ListUsersAsync(imported.Id!));
        foreach (var user in members)
            LiveSite.Learned(user.Name);
        // A synchronisation names the group as Tableau holds it.
        var synchronised = await client.Groups.UpdateFromActiveDirectoryAsync(imported.Id!, named, domain, SiteRole.Unlicensed, _onSync);
        var job = await client.Groups.UpdateFromActiveDirectoryAsJobAsync(imported.Id!, named, domain, SiteRole.Unlicensed, _onSync);
        await client.Groups.DeleteAsync(imported.Id!);
        var importJob = await client.Groups.ImportFromActiveDirectoryAsJobAsync(name, domain, SiteRole.Unlicensed, _onSync);
        // The job makes the group in its own time, and the list may still show the one just deleted: the new one has the
        // name and the domain and another id.
        var byName = new GroupListOptions { Filter = TableauFilter.Eq(GroupField.Name, named), PageSize = 10 };
        var byJob = await LiveSite.EventuallyAsync(async () => (await client.Groups.ListPageAsync(1, byName)).Items
            .FirstOrDefault(group => group.Id != imported.Id && string.Equals(group.Domain?.Name, domain, StringComparison.OrdinalIgnoreCase)),
            "The group the import job made", TimeSpan.FromMinutes(3));
        await client.Groups.DeleteAsync(byJob.Id!);

        Assert.IsTrue(string.Equals(imported.Import?.DomainName, domain, StringComparison.OrdinalIgnoreCase), "the import answers TABLEAU_LIVE_AD_DOMAIN");
        Assert.AreEqual((ImportDirectiveGrantLicenseMode.OnSync, SiteRole.Unlicensed), (imported.Import?.GrantLicenseMode, imported.Import?.SiteRole));
        Assert.AreEqual(1, members.Count, "the directory group has one member");
        Assert.IsTrue(string.Equals(members[0].Name, member, StringComparison.OrdinalIgnoreCase), "its member is TABLEAU_LIVE_AD_GROUP_MEMBER");
        Assert.AreEqual(imported.Id, synchronised.Id);
        Assert.AreEqual(JobType.GroupSync, job.Type);
        Assert.AreEqual("GroupImport", importJob.Type?.Value);
        await ClientFixtures.FailsAsync(TableauErrorKind.NotFound, () => AreaFixtures.AllAsync(client.Groups.ListUsersAsync(byJob.Id!)));
    }

    [SiteTest(TableauSite.Live, [LiveSite.ServerUrl, LiveSite.DirectoryUser], SignsIn = LiveSignIn.Token), TestCategory(TableauSite.Live)]
    public async Task ADirectoryUserIsAddedMadeAViewerImpersonatedByTheTokenAndRemoved()
    {
        var name = TableauSite.Setting(LiveSite.DirectoryUser);
        await using var client = await LiveSite.RunClientAsync();

        var added = await client.Users.AddAsync(name, SiteRole.Unlicensed);
        LiveSite.Learned(added.Name);
        var self = LiveSite.Saw(client.Pipeline.State.Sessions.Current!).UserId;
        User updated;
        User read;
        TableauSession? impersonating = null;
        Page<Project>? page = null;
        string? refused = null;
        try
        {
            // An unlicensed user has no session to act in: the user is a Viewer while the test signs in as it.
            updated = await client.Users.UpdateAsync(added.Id!, new User { SiteRole = SiteRole.Viewer });
            read = await client.Users.GetAsync(added.Id!);
            // A sign-in of the token's own, which ends the client's session; the client signs in again for its next call.
            await using var other = LiveSite.Client(null, await LiveSite.RunOptionsAsync());
            try
            {
                impersonating = LiveSite.Saw(await other.Sessions.SignInAsync(LiveSite.Credentials(impersonateUserId: added.Id)));
                page = await other.WithSession(impersonating).Projects.ListPageAsync(1, new ProjectListOptions { PageSize = 1 });
            }
            catch (TableauException e) when (e.Kind == TableauErrorKind.Authentication)
            {
                refused = e.ErrorCode ?? "no code";
            }
            finally
            {
                if (impersonating is not null) await other.Sessions.SignOutAsync(impersonating);
            }
        }
        finally
        {
            await client.Users.RemoveAsync(added.Id!, mapAssetsToUserId: self);
        }

        Assert.AreEqual(SiteRole.Unlicensed, added.SiteRole);
        Assert.IsTrue(string.Equals(added.Name, name, StringComparison.OrdinalIgnoreCase), "the user answers TABLEAU_LIVE_AD_USER");
        Assert.AreEqual((added.Id, SiteRole.Viewer), (updated.Id, updated.SiteRole), "Update User makes the user a Viewer");
        Assert.AreEqual((added.Id, SiteRole.Viewer), (read.Id, read.SiteRole), "the user reads back a Viewer");
        Assert.IsNull(refused, $"Tableau refused the token's sign-in impersonating the user: {refused}");
        Assert.IsTrue(string.Equals(added.Id, impersonating?.UserId, StringComparison.OrdinalIgnoreCase), "the session acts as the user impersonated");
        Assert.IsNotNull(page, "the impersonating session calls");
        await ClientFixtures.FailsAsync(TableauErrorKind.NotFound, () => client.Users.GetAsync(added.Id!));
    }
}
