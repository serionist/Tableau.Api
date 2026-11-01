using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Tableau.Api.Rest.Models;

namespace Tableau.Api.Tests;

/// <summary>
/// The sites calls on the run's site (<see cref="LiveSite"/> has the settings), which the suite makes with revision
/// history and a limit, and deletes: read by its id, its content URL and its name, from a session on it, as Tableau
/// answers Query Site only for the site a session is on, its history and limit read back as made, its name refused for
/// the dot it holds; listed among the sites
/// the token's server administrator reaches, whole and a page of one; and updated with another limit, history kept on,
/// which shows Tableau reads the limit as a JSON number in Update Site as in Create Site. One at a time, as the other live
/// tests.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class LiveSiteAdminTests
{
    [SiteTest(TableauSite.Live, [LiveSite.ServerUrl], SignsIn = LiveSignIn.Token), TestCategory(TableauSite.Live)]
    public async Task TheRunsSiteIsReadByItsIdContentUrlAndNameListedAndUpdatedWithAnotherRevisionLimit()
    {
        var made = await LiveSite.RunSiteAsync();
        await using var client = await LiveSite.RunClientAsync();

        var byId = await client.Sites.GetAsync(made.Id!);
        var byContentUrl = await client.Sites.GetByContentUrlAsync(made.ContentUrl!);
        // Tableau Server 2025.1 reads what follows the last dot of the path's last segment as a format, as in name.json,
        // so a name holding a dot, as the run's site's does, isn't found: it answers 404000 for the part before the dot.
        string byName;
        try
        {
            byName = (await client.Sites.GetByNameAsync(made.Name!)) is { } named && (named.Id, named.Name) == (made.Id, made.Name) ? "found" : "another site";
        }
        catch (TableauException e) when (e.Kind == TableauErrorKind.NotFound)
        {
            byName = $"refused {e.ErrorCode}";
        }
        var all = await AreaFixtures.AllAsync(client.Sites.ListAsync());
        var page = await client.Sites.ListPageAsync(1, new SiteListOptions { PageSize = 1 });
        var updated = await client.Sites.UpdateAsync(made.Id!, new Site { RevisionLimit = LiveSite.RevisionLimit + 5, RevisionHistoryEnabled = true });
        var read = await client.Sites.GetAsync(made.Id!);

        Assert.IsTrue(byName is "found" or "refused 404000", $"Query Site by name: {byName}");
        foreach (var site in new[] { byId, byContentUrl })
        {
            Assert.AreEqual((made.Id, made.Name, made.ContentUrl), (site.Id, site.Name, site.ContentUrl));
            Assert.AreEqual((true, (long?)LiveSite.RevisionLimit), (site.RevisionHistoryEnabled, site.RevisionLimit), "the history and the limit as made");
        }
        Assert.AreEqual((true, (long?)LiveSite.RevisionLimit), (made.RevisionHistoryEnabled, made.RevisionLimit), "Create Site answers them as sent");
        Assert.IsTrue(all.Any(site => site.Id == made.Id), "the run's site is listed");
        Assert.HasCount(all.Count, all.Select(site => site.Id).Distinct(), "no site twice across pages");
        Assert.HasCount(1, page.Items);
        Assert.AreEqual(all.Count, page.TotalAvailable);
        Assert.AreEqual((made.Id, (long?)LiveSite.RevisionLimit + 5, (bool?)true), (updated.Id, updated.RevisionLimit, updated.RevisionHistoryEnabled));
        Assert.AreEqual(((long?)LiveSite.RevisionLimit + 5, (bool?)true), (read.RevisionLimit, read.RevisionHistoryEnabled), "the limit as sent, history still on");
    }
}
