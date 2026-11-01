using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tableau.Api.Tests;

/// <summary>
/// A Live or ServerOnly test runs only when its switch is 1: inconclusive when the switch is unset or empty, failed for
/// any other value, and failed, naming each setting it misses, when the switch is 1 and a setting is not set. A test
/// that signs in needs its own sign-in's settings, the token's or the connected app's, which the other's never stand in
/// for. The environment here is a dictionary, so no test changes the process's. And every test in the suite carries its
/// site marks in pairs: a SiteTest with its category, a site category with a SiteTest.
/// </summary>
[TestClass]
public sealed class TableauSiteTests
{
    private static readonly string[] _liveSettings = ["TABLEAU_LIVE_SERVER_URL", "TABLEAU_LIVE_SITE"];

    private static readonly Dictionary<string, string?> _server = new() { ["TABLEAU_LIVE_SERVER_URL"] = "https://x.example" };

    private static readonly Dictionary<string, string?> _token = new() { ["TABLEAU_LIVE_PAT_NAME"] = "n", ["TABLEAU_LIVE_PAT_SECRET"] = "s" };

    private static readonly Dictionary<string, string?> _app = new()
    {
        ["TABLEAU_LIVE_CLIENT_ID"] = "c",
        ["TABLEAU_LIVE_SECRET_ID"] = "k",
        ["TABLEAU_LIVE_SECRET_VALUE"] = "v",
        ["TABLEAU_LIVE_USER"] = "u",
    };

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
    public void EachSignInRunsWithItsOwnSettings()
    {
        Assert.AreEqual(SiteOutcome.Run, TableauSite.Check(TableauSite.Live, _liveSettings[..1], Switched(_server, _token), LiveSignIn.Token).Outcome);
        Assert.AreEqual(SiteOutcome.Run, TableauSite.Check(TableauSite.Live, _liveSettings[..1], Switched(_server, _app), LiveSignIn.ConnectedApp).Outcome);
        Assert.AreEqual(SiteOutcome.Run, TableauSite.Check(TableauSite.Live, _liveSettings[..1], Switched(_server, _token, _app), LiveSignIn.Token).Outcome);
        Assert.AreEqual(SiteOutcome.Run, TableauSite.Check(TableauSite.Live, _liveSettings[..1], Switched(_server, _token, _app), LiveSignIn.ConnectedApp).Outcome);
        Assert.AreEqual(SiteOutcome.Run, TableauSite.Check(TableauSite.Live, _liveSettings[..1], Switched(_server)).Outcome,
            "Server Info's tests sign in with neither");
        CollectionAssert.AreEqual(TableauSite.TokenSettings, TableauSite.SettingsOf(LiveSignIn.Token).ToArray());
        CollectionAssert.AreEqual(TableauSite.ConnectedAppSettings, TableauSite.SettingsOf(LiveSignIn.ConnectedApp).ToArray());
        Assert.IsEmpty(TableauSite.SettingsOf(LiveSignIn.None));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => TableauSite.SettingsOf((LiveSignIn)99));
    }

    [TestMethod]
    [DataRow(nameof(LiveSignIn.Token), "TABLEAU_LIVE_PAT_NAME, TABLEAU_LIVE_PAT_SECRET are not set: the test signs in with the personal access token.")]
    [DataRow(nameof(LiveSignIn.ConnectedApp), "TABLEAU_LIVE_CLIENT_ID, TABLEAU_LIVE_SECRET_ID, TABLEAU_LIVE_SECRET_VALUE, TABLEAU_LIVE_USER are not set: "
        + "the test signs in with the connected app.")]
    public void EachSignInsMissingSettingsFailTheTestAndAreNamed(string name, string named)
    {
        var signIn = Enum.Parse<LiveSignIn>(name);
        var none = TableauSite.Check(TableauSite.Live, [], Switched(), signIn);

        Assert.AreEqual(SiteOutcome.Fail, none.Outcome);
        Assert.AreEqual("TABLEAU_LIVE=1, but " + named, none.Message);
        Assert.AreEqual(UnitTestOutcome.Failed, SiteTestAttribute.Ended(none)?.Outcome);
        Assert.AreEqual(SiteOutcome.Inconclusive, TableauSite.Check(TableauSite.Live, [], Variables([]), signIn).Outcome,
            "without its switch, a test that signs in reaches no site");
    }

    [TestMethod]
    public void ASignInsHalfSetSettingsFailTheTestNamedAfterTheTestsOwn()
    {
        var halfAnApp = TableauSite.Check(TableauSite.Live, _liveSettings,
            Switched(_server, new Dictionary<string, string?> { ["TABLEAU_LIVE_CLIENT_ID"] = "c", ["TABLEAU_LIVE_USER"] = "u" }), LiveSignIn.ConnectedApp);
        var halfAToken = TableauSite.Check(TableauSite.Live, [], Switched(new Dictionary<string, string?> { ["TABLEAU_LIVE_PAT_SECRET"] = "s" }), LiveSignIn.Token);
        var ownOnly = TableauSite.Check(TableauSite.Live, _liveSettings, Switched(_token), LiveSignIn.Token);

        Assert.AreEqual("TABLEAU_LIVE=1, but TABLEAU_LIVE_SITE, TABLEAU_LIVE_SECRET_ID, TABLEAU_LIVE_SECRET_VALUE are not set: "
            + "the test signs in with the connected app.", halfAnApp.Message);
        Assert.AreEqual("TABLEAU_LIVE=1, but TABLEAU_LIVE_PAT_NAME is not set: the test signs in with the personal access token.", halfAToken.Message);
        Assert.AreEqual("TABLEAU_LIVE=1, but TABLEAU_LIVE_SERVER_URL, TABLEAU_LIVE_SITE are not set.", ownOnly.Message,
            "a sign-in whose settings are all set isn't named");
    }

    [TestMethod]
    public void ATestNeedingOneSignInIsNotSatisfiedByTheOthersSettings()
    {
        var app = TableauSite.Check(TableauSite.Live, [], Switched(_token), LiveSignIn.ConnectedApp);
        var token = TableauSite.Check(TableauSite.Live, [], Switched(_app), LiveSignIn.Token);

        Assert.AreEqual((SiteOutcome.Fail, "TABLEAU_LIVE=1, but TABLEAU_LIVE_CLIENT_ID, TABLEAU_LIVE_SECRET_ID, TABLEAU_LIVE_SECRET_VALUE, "
            + "TABLEAU_LIVE_USER are not set: the test signs in with the connected app."), (app.Outcome, app.Message));
        Assert.AreEqual((SiteOutcome.Fail, "TABLEAU_LIVE=1, but TABLEAU_LIVE_PAT_NAME, TABLEAU_LIVE_PAT_SECRET are not set: the test signs in "
            + "with the personal access token."), (token.Outcome, token.Message));
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

    [TestMethod]
    public void AFailedResultComesBackWithEveryKnownValueInAnyCaseAsStars()
    {
        string[] known = ["Finance-Site", "invented-directory-group", "invented-live-secret"];
        string Scrub(string text) => LiveSite.Scrubbed(text, known);
        var result = new TestResult
        {
            Outcome = UnitTestOutcome.Failed,
            TestFailureException = new ScrubbedFailure("Query Site (GET /api/3.25/sites/FINANCE-SITE) answered 404: Site 'finance-site' wasn't found.",
                "   at Invented() in INVENTED-DIRECTORY-GROUP.cs:line 1"),
            LogOutput = "imported Invented-Directory-Group",
            LogError = "token invented-live-secret",
            DebugTrace = "Finance-Site",
            TestContextMessages = "a line holding FINANCE-site",
        };

        var scrubbed = SiteTestAttribute.Scrubbed(result, Scrub);

        Assert.AreEqual(UnitTestOutcome.Failed, scrubbed.Outcome);
        Assert.AreEqual("Query Site (GET /api/3.25/sites/***) answered 404: Site '***' wasn't found.", scrubbed.TestFailureException!.Message);
        Assert.AreEqual("   at Invented() in ***.cs:line 1", scrubbed.TestFailureException.StackTrace);
        Assert.AreEqual(("imported ***", "token ***", "***", "a line holding ***"),
            (scrubbed.LogOutput, scrubbed.LogError, scrubbed.DebugTrace, scrubbed.TestContextMessages));
        Assert.IsFalse(known.Any(value => scrubbed.TestFailureException.ToString().Contains(value, StringComparison.OrdinalIgnoreCase)));
        var empty = SiteTestAttribute.Scrubbed(new TestResult { Outcome = UnitTestOutcome.Failed }, Scrub);
        Assert.IsNull(empty.TestFailureException, "a result without a failure stays without one");
        Assert.IsNull(empty.LogOutput);
    }

    [TestMethod]
    public void AThrownFailuresOwnStackTraceIsScrubbed()
    {
        Exception thrown;
        try
        {
            throw new InvalidOperationException("Site 'Finance-Site' wasn't found.");
        }
        catch (InvalidOperationException e)
        {
            thrown = e;
        }

        var scrubbed = SiteTestAttribute.Scrubbed(new TestResult { Outcome = UnitTestOutcome.Failed, TestFailureException = thrown },
            text => LiveSite.Scrubbed(text, ["finance-site", nameof(AThrownFailuresOwnStackTraceIsScrubbed)]));

        Assert.AreEqual("Site '***' wasn't found.", scrubbed.TestFailureException!.Message);
        Assert.Contains("TableauSiteTests.***()", scrubbed.TestFailureException.StackTrace!, "the stack trace it was thrown with, scrubbed");
    }

    [TestMethod]
    public void TheSuitesCleanupFailsWithItsFailuresScrubbedInnerOnesAmongThemAndNoInnerException()
    {
        string[] known = ["tableau.invented.example", "Finance-Site"];
        string Scrub(string text) => LiveSite.Scrubbed(text, known);
        var connection = new System.Net.Http.HttpRequestException("No such host is known. (TABLEAU.INVENTED.EXAMPLE:443)");
        var deleted = new InvalidOperationException("The run's site 'Finance-Site' is left on the server.",
            new TableauException(TableauErrorKind.Http, $"Delete Site got no answer: {connection.Message}", connection));

        var one = SuiteCleanup.Failure([deleted], Scrub)!;
        var two = SuiteCleanup.Failure([deleted, new InvalidOperationException("2 recorded file(s) held a live secret")], Scrub)!;

        Assert.IsNull(SuiteCleanup.Failure([], Scrub), "no failure, nothing thrown");
        Assert.IsNull(one.InnerException, "MSTest would print an inner one");
        Assert.Contains("The run's site '***' is left on the server.", one.Message);
        Assert.Contains("Delete Site got no answer: No such host is known. (***:443)", one.Message, "the inner exception's text, scrubbed");
        Assert.IsFalse(known.Any(value => one.Message.Contains(value, StringComparison.OrdinalIgnoreCase)));
        Assert.StartsWith("The suite's cleanup failed 2 times:", two.Message);
        Assert.Contains("2 recorded file(s) held a live secret", two.Message);
        Assert.IsFalse(known.Any(value => two.Message.Contains(value, StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void AClassWithASiteTestRunsItsTestsOneAtATime()
    {
        Assert.AreEqual("T has a SiteTest and no [DoNotParallelize]: a site test runs one at a time", TableauSite.OrderProblem("T", true, false));
        Assert.IsNull(TableauSite.OrderProblem("T", true, true));
        Assert.IsNull(TableauSite.OrderProblem("T", false, false), "an offline class runs in parallel");
    }

    [TestMethod]
    public void EveryClassWithASiteTestInTheSuiteRunsItsTestsOneAtATime()
    {
        var classes = typeof(TableauSiteTests).Assembly.GetTypes()
            .Where(type => type.GetCustomAttribute<TestClassAttribute>() is not null)
            .ToList();
        var problems = classes
            .Select(type => TableauSite.OrderProblem(type.Name,
                type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).Any(method => method.GetCustomAttribute<SiteTestAttribute>() is not null),
                type.GetCustomAttribute<DoNotParallelizeAttribute>() is not null))
            .OfType<string>()
            .ToList();

        Assert.IsEmpty(problems, string.Join("\n", problems));
        Assert.IsTrue(classes.Contains(typeof(ServerInfoSiteTests)) && classes.Contains(typeof(LiveContentTests)), "the scan finds the site test classes");
    }

    private static Func<string, string?> Variables(Dictionary<string, string?> variables) =>
        name => variables.GetValueOrDefault(name);

    // TABLEAU_LIVE at 1, and the settings given.
    private static Func<string, string?> Switched(params Dictionary<string, string?>[] settings) =>
        Variables(settings.SelectMany(part => part).Append(new("TABLEAU_LIVE", "1")).ToDictionary());
}
