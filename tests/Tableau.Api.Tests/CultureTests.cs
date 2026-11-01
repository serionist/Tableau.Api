using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Tableau.Api.Enums;
using Tableau.Api.Rest.Models;
using Tableau.Api.Tests.Fakes;

namespace Tableau.Api.Tests;

/// <summary>
/// What goes on the wire, and what a response is read as, is the same in every culture: numbers are written in the
/// invariant culture and names compared ordinally. Each test runs its calls under the culture where the machine's
/// rules differ most, whatever the leg; CI also runs the whole suite under sv-SE and tr-TR in a time zone that is not
/// UTC, and the canary says whether that leg's culture and zone took.
/// </summary>
[TestClass]
public sealed class CultureTests
{
    private const string SitePath = "/api/" + Fixtures.ApiVersion + "/sites";
    private const string SitesPath = "/api/" + ClientFixtures.Version + "/sites";
    private const string Minus = "\u2212";

    // What each culture leg in .github/workflows/ci.yml sets, and a rule of that culture which the invariant culture
    // doesn't have, so the canary sees the culture's own data in effect and not only its name.
    private static readonly Dictionary<string, Func<bool>> _cultureProbes = new(StringComparer.Ordinal)
    {
        ["sv-SE"] = () => (-1).ToString(CultureInfo.CurrentCulture) == Minus + "1",
        ["tr-TR"] = () => CultureInfo.CurrentCulture.TextInfo.ToUpper('i') == '\u0130',
    };

    [TestMethod]
    public void TheLegsCultureAndTimeZoneAreInEffect()
    {
        var culture = Environment.GetEnvironmentVariable("TABLEAU_TEST_CULTURE");
        var zone = Environment.GetEnvironmentVariable("TABLEAU_TEST_TIME_ZONE");
        if (string.IsNullOrEmpty(culture) && string.IsNullOrEmpty(zone))
            Assert.Inconclusive("Not a culture leg: TABLEAU_TEST_CULTURE and TABLEAU_TEST_TIME_ZONE are unset.");
        Assert.IsFalse(string.IsNullOrEmpty(culture), "A culture leg sets TABLEAU_TEST_CULTURE with its time zone.");
        Assert.IsFalse(string.IsNullOrEmpty(zone), "A culture leg sets TABLEAU_TEST_TIME_ZONE with its culture.");

        Assert.AreEqual(culture, CultureInfo.CurrentCulture.Name, "the process's culture");
        Assert.IsTrue(_cultureProbes.TryGetValue(culture, out var probe), $"No probe for {culture}: add one above.");
        Assert.IsTrue(probe(), $"{culture} is named, but its rules are not in effect; is ICU missing?");
        Assert.AreEqual(zone, TimeZoneInfo.Local.Id, "the process's time zone");
        Assert.AreNotEqual(TimeSpan.Zero, TimeZoneInfo.Local.BaseUtcOffset, $"{zone} is UTC");
    }

    [TestMethod]
    public async Task ASiteSendsMinusOneAsAHyphenUnderSwedish()
    {
        using var server = FakeTableauServer.Start();
        SessionFixtures.MapSignIns(server, Fixtures.SessionToken);
        var site = $$$"""{"site":{"id":"{{{Fixtures.SiteId}}}","name":"Finance","contentUrl":"{{{Fixtures.SiteContentUrl}}}"}}""";
        server.Map("POST", SitesPath, FakeResponse.Json(201, site));
        server.Map("PUT", SitesPath + "/" + Fixtures.SiteId, FakeResponse.Json(200, site));
        await using var client = SessionFixtures.Client(SessionFixtures.Options(server), SessionFixtures.Pat());
        var capacities = new Site { TierCreatorCapacity = -1, TierExplorerCapacity = -1, TierViewerCapacity = -1, RevisionLimit = -1 };

        await InCulture("sv-SE", async () =>
        {
            // The precondition: Swedish writes -1 with U+2212, which is no number.
            Assert.AreEqual(Minus + "1", (-1).ToString(CultureInfo.CurrentCulture));
            await client.Sites.CreateAsync(capacities with { Name = "Finance", ContentUrl = Fixtures.SiteContentUrl });
            await client.Sites.UpdateAsync(Fixtures.SiteId, capacities);
        });

        var bodies = server.Requests.Where(request => request.Path.StartsWith(SitesPath, StringComparison.Ordinal))
            .Select(request => request.BodyText).ToList();
        Assert.HasCount(2, bodies);
        foreach (var attribute in new[] { "tierCreatorCapacity", "tierExplorerCapacity", "tierViewerCapacity", "revisionLimit" })
        {
            Assert.Contains($"\"{attribute}\":-1", bodies[0]);
            Assert.Contains($"\"{attribute}\":-1", bodies[1]);
        }
        Assert.IsFalse(bodies.Any(body => body.Contains(Minus, StringComparison.Ordinal)), string.Join("\n", bodies));
    }

    [TestMethod]
    public async Task ADownloadsContentTypeIsReadOrdinallyUnderTurkish()
    {
        using var server = FakeTableauServer.Start();
        var client = Fixtures.Client(server);
        var session = Fixtures.Session();
        server.Map("GET", SitePath + "/" + Fixtures.SiteId + "/datasources/d1/content",
            FakeResponse.Content(200, "APPLICATION/XML", "<datasource/>"));
        server.Map("GET", SitePath + "/" + Fixtures.SiteId + "/workbooks/w1/content",
            FakeResponse.Content(200, "APPLICATION/XML", "<workbook/>"));

        await InCulture("tr-TR", async () =>
        {
            // The precondition: in Turkish, I is not the capital of i, so a culture's comparison misses the type.
            Assert.AreNotEqual(0, CultureInfo.CurrentCulture.CompareInfo.Compare("APPLICATION/XML", "application/xml",
                CompareOptions.IgnoreCase));
            var (dataSource, dataSourceType) = await client.DownloadDataSource(session, "d1");
            var (workbook, workbookType) = await client.DownloadWorkbook(session, "w1");
            await using (dataSource)
            await using (workbook)
            {
                Assert.AreEqual(DatasourceFileType.tds, dataSourceType);
                Assert.AreEqual(WorkbookFileType.twb, workbookType);
                Assert.AreEqual("<workbook/>", await new StreamReader(workbook).ReadToEndAsync());
            }
        });
    }

    /// <summary>
    /// Runs test with the culture set for its calls alone: the culture flows with the calls' awaits, and the test
    /// host's own is back once it ends.
    /// </summary>
    private static async Task InCulture(string name, Func<Task> test)
    {
        var before = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(name);
        try
        {
            await test();
        }
        finally
        {
            CultureInfo.CurrentCulture = before;
        }
    }
}
