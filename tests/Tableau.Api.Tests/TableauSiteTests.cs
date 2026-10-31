using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tableau.Api.Tests;

/// <summary>
/// A Live or ServerOnly test runs only when its switch is 1: inconclusive when the switch is unset or empty, failed for
/// any other value, and failed, naming each setting it misses, when the switch is 1 and a setting is not set. The
/// environment here is a dictionary, so no test changes the process's. And every test in the suite carries its site
/// marks in pairs: a SiteTest with its category, a site category with a SiteTest.
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
            var ended = SiteTestAttribute.Ended(check);
            Assert.AreEqual(UnitTestOutcome.Inconclusive, ended?.Outcome);
            Assert.IsInstanceOfType<AssertInconclusiveException>(ended?.TestFailureException);
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
        var ended = SiteTestAttribute.Ended(check);
        Assert.AreEqual(UnitTestOutcome.Failed, ended?.Outcome);
        Assert.IsInstanceOfType<AssertFailedException>(ended?.TestFailureException);

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
    public void WithItsSwitchAndEverySettingATestRuns()
    {
        var check = TableauSite.Check(TableauSite.Live, _liveSettings, Variables(new()
        {
            ["TABLEAU_LIVE"] = "1",
            ["TABLEAU_LIVE_SERVER_URL"] = "https://tableau.example.com/",
            ["TABLEAU_LIVE_SITE"] = "finance",
        }));

        Assert.AreEqual(SiteOutcome.Run, check.Outcome);
        Assert.IsNull(SiteTestAttribute.Ended(check));
    }

    [TestMethod]
    public void ASignInTakesTheConnectedAppWhenAnyOfItsSettingsIsSetElseAToken()
    {
        var app = new Dictionary<string, string?>
        {
            ["TABLEAU_LIVE"] = "1",
            ["TABLEAU_LIVE_CLIENT_ID"] = "c",
            ["TABLEAU_LIVE_SECRET_ID"] = "k",
            ["TABLEAU_LIVE_SECRET_VALUE"] = "v",
            ["TABLEAU_LIVE_USER"] = "u",
            ["TABLEAU_LIVE_PAT_NAME"] = "n",
        };
        var token = new Dictionary<string, string?> { ["TABLEAU_LIVE"] = "1", ["TABLEAU_LIVE_PAT_NAME"] = "n", ["TABLEAU_LIVE_PAT_SECRET"] = "s" };

        Assert.AreSame(TableauSite.DirectTrustSettings, TableauSite.SignInSettings(Variables(app)));
        Assert.AreSame(TableauSite.PersonalAccessTokenSettings, TableauSite.SignInSettings(Variables(token)));
        Assert.IsNull(TableauSite.SignInSettings(Variables(new() { ["TABLEAU_LIVE"] = "1" })));
        Assert.AreEqual(SiteOutcome.Run, TableauSite.Check(TableauSite.Live, [], Variables(app), signsIn: true).Outcome);
        Assert.AreEqual(SiteOutcome.Run, TableauSite.Check(TableauSite.Live, [], Variables(token), signsIn: true).Outcome);
    }

    [TestMethod]
    public void ASignInsMissingSettingsFailTheTestAndAreNamed()
    {
        var halfAnApp = TableauSite.Check(TableauSite.Live, ["TABLEAU_LIVE_SERVER_URL"],
            Variables(new()
            {
                ["TABLEAU_LIVE"] = "1",
                ["TABLEAU_LIVE_SERVER_URL"] = "https://x.example",
                ["TABLEAU_LIVE_CLIENT_ID"] = "c",
                ["TABLEAU_LIVE_USER"] = "u",
                ["TABLEAU_LIVE_PAT_NAME"] = "n",
                ["TABLEAU_LIVE_PAT_SECRET"] = "s"
            }), signsIn: true);
        var halfAToken = TableauSite.Check(TableauSite.Live, [],
            Variables(new() { ["TABLEAU_LIVE"] = "1", ["TABLEAU_LIVE_PAT_SECRET"] = "s" }), signsIn: true);
        var none = TableauSite.Check(TableauSite.Live, [], Variables(new() { ["TABLEAU_LIVE"] = "1" }), signsIn: true);

        Assert.AreEqual("TABLEAU_LIVE=1, but TABLEAU_LIVE_SECRET_ID, TABLEAU_LIVE_SECRET_VALUE are not set.", halfAnApp.Message);
        Assert.AreEqual("TABLEAU_LIVE=1, but TABLEAU_LIVE_PAT_NAME is not set.", halfAToken.Message);
        Assert.AreEqual(SiteOutcome.Fail, none.Outcome);
        Assert.AreEqual("TABLEAU_LIVE=1, but no sign-in is set: a connected app's TABLEAU_LIVE_CLIENT_ID, TABLEAU_LIVE_SECRET_ID, "
            + "TABLEAU_LIVE_SECRET_VALUE, TABLEAU_LIVE_USER, or a personal access token's TABLEAU_LIVE_PAT_NAME and TABLEAU_LIVE_PAT_SECRET.",
            none.Message);
        Assert.AreEqual(SiteOutcome.Inconclusive, TableauSite.Check(TableauSite.Live, [], Variables([]), signsIn: true).Outcome);
    }

    [TestMethod]
    public void AnUnknownCategoryIsRefused() =>
        Assert.ThrowsExactly<ArgumentException>(() => TableauSite.Check("Nightly", [], _ => "1"));

    [TestMethod]
    public void ASiteTestAndItsCategoryComeTogether()
    {
        var live = new SiteTestAttribute(TableauSite.Live, []);

        Assert.IsEmpty(TableauSite.MarkProblems("T", live, [TableauSite.Live, "Slow"]));
        Assert.IsEmpty(TableauSite.MarkProblems("T", null, ["Slow"]));
        CollectionAssert.AreEqual(new[] { "T is a Live SiteTest without [TestCategory(Live)]" },
            TableauSite.MarkProblems("T", live, []).ToArray());
        CollectionAssert.AreEqual(
            new[] { "T is in the ServerOnly category without [SiteTest(ServerOnly, ...)], which checks its switch" },
            TableauSite.MarkProblems("T", null, [TableauSite.ServerOnly]).ToArray());
        CollectionAssert.AreEqual(
            new[]
            {
                "T is a Live SiteTest without [TestCategory(Live)]",
                "T is in the ServerOnly category without [SiteTest(ServerOnly, ...)], which checks its switch",
            },
            TableauSite.MarkProblems("T", live, [TableauSite.ServerOnly]).ToArray());
    }

    [TestMethod]
    public void EveryTestInTheSuiteCarriesItsSiteMarksInPairs()
    {
        var problems = typeof(TableauSiteTests).Assembly.GetTypes()
            .Where(type => type.GetCustomAttribute<TestClassAttribute>() is not null)
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(method => method.GetCustomAttribute<TestMethodAttribute>() is not null)
                .SelectMany(method => TableauSite.MarkProblems($"{type.Name}.{method.Name}",
                    method.GetCustomAttribute<SiteTestAttribute>(),
                    method.GetCustomAttributes<TestCategoryBaseAttribute>()
                        .Concat(type.GetCustomAttributes<TestCategoryBaseAttribute>())
                        .SelectMany(category => category.TestCategories))))
            .ToList();

        Assert.IsEmpty(problems, string.Join("\n", problems));
    }

    private static Func<string, string?> Variables(Dictionary<string, string?> variables) =>
        name => variables.GetValueOrDefault(name);
}
