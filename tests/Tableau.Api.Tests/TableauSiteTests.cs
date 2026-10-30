using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tableau.Api.Tests;

/// <summary>
/// A Live or ServerOnly test runs only when its switch is set to 1, and then fails, naming each setting it misses,
/// rather than pass having reached no site. The environment here is a dictionary, so no test changes the process's.
/// </summary>
[TestClass]
public sealed class TableauSiteTests
{
    private static readonly string[] _liveSettings = ["TABLEAU_LIVE_SERVER_URL", "TABLEAU_LIVE_SITE"];

    [TestMethod]
    [DataRow(TableauSite.Live, "TABLEAU_LIVE")]
    [DataRow(TableauSite.ServerOnly, "TABLEAU_SERVER_ONLY")]
    public void WithoutItsSwitchATestIsInconclusive(string category, string name)
    {
        foreach (var value in new string?[] { null, "" })
        {
            var check = TableauSite.Check(category, _liveSettings, Variables(new() { [name] = value }));

            Assert.AreEqual(SiteOutcome.Inconclusive, check.Outcome);
            Assert.Contains($"{name}=1", check.Message);
            Assert.ThrowsExactly<AssertInconclusiveException>(() => TableauSite.Apply(check));
        }
    }

    [TestMethod]
    public void OneCategorysSwitchRunsNoTestOfTheOther()
    {
        var variables = Variables(new() { ["TABLEAU_LIVE"] = "1", ["TABLEAU_LIVE_SERVER_URL"] = "https://x.example" });

        Assert.AreEqual(SiteOutcome.Inconclusive, TableauSite.Check(TableauSite.ServerOnly, [], variables).Outcome);
        Assert.AreEqual(SiteOutcome.Run, TableauSite.Check(TableauSite.Live, [], variables).Outcome);
    }

    [TestMethod]
    public void WithItsSwitchAMissingSettingFailsTheTestAndIsNamed()
    {
        var check = TableauSite.Check(TableauSite.Live, _liveSettings,
            Variables(new() { ["TABLEAU_LIVE"] = "1", ["TABLEAU_LIVE_SITE"] = "" }));

        Assert.AreEqual(SiteOutcome.Fail, check.Outcome);
        Assert.AreEqual("TABLEAU_LIVE=1, but TABLEAU_LIVE_SERVER_URL, TABLEAU_LIVE_SITE are not set.", check.Message);
        Assert.ThrowsExactly<AssertFailedException>(() => TableauSite.Apply(check));

        var one = TableauSite.Check(TableauSite.ServerOnly, ["TABLEAU_SERVER_ONLY_SERVER_URL"],
            Variables(new() { ["TABLEAU_SERVER_ONLY"] = "1" }));
        Assert.AreEqual("TABLEAU_SERVER_ONLY=1, but TABLEAU_SERVER_ONLY_SERVER_URL is not set.", one.Message);
    }

    [TestMethod]
    [DataRow("true")]
    [DataRow("yes")]
    [DataRow("0")]
    public void ASwitchSetToAnythingButOneFailsTheTest(string value)
    {
        var check = TableauSite.Check(TableauSite.Live, _liveSettings, Variables(new() { ["TABLEAU_LIVE"] = value }));

        Assert.AreEqual(SiteOutcome.Fail, check.Outcome);
        Assert.AreEqual($"TABLEAU_LIVE is '{value}': set it to 1 to run the Live tests, or unset it.", check.Message);
    }

    [TestMethod]
    public void WithItsSwitchAndEverySettingATestRunsWithThem()
    {
        var check = TableauSite.Check(TableauSite.Live, _liveSettings, Variables(new()
        {
            ["TABLEAU_LIVE"] = "1",
            ["TABLEAU_LIVE_SERVER_URL"] = "https://tableau.example.com/",
            ["TABLEAU_LIVE_SITE"] = "finance",
        }));

        var settings = TableauSite.Apply(check);

        Assert.AreEqual(SiteOutcome.Run, check.Outcome);
        Assert.AreEqual("https://tableau.example.com/", settings["TABLEAU_LIVE_SERVER_URL"]);
        Assert.AreEqual("finance", settings["TABLEAU_LIVE_SITE"]);
    }

    [TestMethod]
    public void AnUnknownCategoryIsRefused() =>
        Assert.ThrowsExactly<ArgumentException>(() => TableauSite.Check("Nightly", [], _ => "1"));

    private static Func<string, string?> Variables(Dictionary<string, string?> variables) =>
        name => variables.GetValueOrDefault(name);
}
