using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tableau.Api.Tests;

/// <summary>
/// The tests that need a Tableau site, in two categories. <see cref="Live"/> runs on Tableau Cloud or a Tableau Server;
/// <see cref="ServerOnly"/> shows what only a Tableau Server has, run by hand. Every other test is offline. A site test
/// is marked with <see cref="SiteTestAttribute"/> in place of [TestMethod], and with its category:
/// <code>
/// [SiteTest(TableauSite.Live, ["TABLEAU_LIVE_SERVER_URL"]), TestCategory(TableauSite.Live)]
/// public async Task ServerInfoAnswers()
/// {
///     var url = TableauSite.Setting("TABLEAU_LIVE_SERVER_URL");
///     ...
/// }
/// </code>
/// It runs only when its category's switch, TABLEAU_LIVE or TABLEAU_SERVER_ONLY, is 1. When the switch is unset or
/// empty the test is inconclusive, so a run without it, every CI run among them, reaches no site; any value but 1 fails
/// the test, so a typo can't skip a run meant for a site. With the switch at 1, a setting that is missing or empty
/// fails the test and names it. A test that signs in names its sign-in, <c>SignsIn = LiveSignIn.Token</c> for the
/// content work or <c>SignsIn = LiveSignIn.ConnectedApp</c> for the connected app's own tests, and needs that sign-in's
/// settings too (<see cref="SettingsOf"/>): the other sign-in's settings never stand in for them. Settings are environment
/// variables, kept outside the clone with the site's secrets; <see cref="LiveSite"/> names the live suite's.
/// <see cref="TableauSiteTests"/> fails on a site test without its category, or a category without the attribute.
/// </summary>
internal static class TableauSite
{
    public const string Live = "Live";
    public const string ServerOnly = "ServerOnly";

    private static readonly Dictionary<string, string> _switches = new(StringComparer.Ordinal)
    {
        [Live] = "TABLEAU_LIVE",
        [ServerOnly] = "TABLEAU_SERVER_ONLY",
    };

    /// <summary>The settings of the personal access token every content test signs in with.</summary>
    public static readonly string[] TokenSettings = ["TABLEAU_LIVE_PAT_NAME", "TABLEAU_LIVE_PAT_SECRET"];

    /// <summary>The settings of the connected app its own tests sign in with by direct trust: the user is the JWT's sub.</summary>
    public static readonly string[] ConnectedAppSettings =
        ["TABLEAU_LIVE_CLIENT_ID", "TABLEAU_LIVE_SECRET_ID", "TABLEAU_LIVE_SECRET_VALUE", "TABLEAU_LIVE_USER"];

    /// <summary>The site categories.</summary>
    public static IReadOnlyCollection<string> Categories => _switches.Keys;

    /// <summary>The settings a sign-in needs: none for <see cref="LiveSignIn.None"/>.</summary>
    public static IReadOnlyList<string> SettingsOf(LiveSignIn signIn) => signIn switch
    {
        LiveSignIn.None => [],
        LiveSignIn.Token => TokenSettings,
        LiveSignIn.ConnectedApp => ConnectedAppSettings,
        _ => throw new ArgumentOutOfRangeException(nameof(signIn), signIn, "No such sign-in."),
    };

    /// <summary>A setting the test's <see cref="SiteTestAttribute"/> named, which it found set before the test.</summary>
    public static string Setting(string name) =>
        Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException($"{name} is not set; name it in the test's SiteTest attribute.");

    /// <summary>
    /// Whether a test of category runs, with the environment environment gives. A test that signs in also needs the
    /// settings of its sign-in (<see cref="SettingsOf"/>), each named when missing, whatever the other sign-in has set.
    /// </summary>
    public static SiteCheck Check(string category, IReadOnlyList<string> settings, Func<string, string?> environment,
        LiveSignIn signIn = LiveSignIn.None)
    {
        if (!_switches.TryGetValue(category, out var name))
        {
            throw new ArgumentException($"{category} is no site category: {string.Join(" or ", _switches.Keys)}",
                nameof(category));
        }
        var value = environment(name);
        if (string.IsNullOrEmpty(value))
        {
            return new SiteCheck(SiteOutcome.Inconclusive,
                $"A {category} test: it runs with {name}=1, against a site you may change.");
        }
        if (value != "1")
        {
            return new SiteCheck(SiteOutcome.Fail,
                $"{name} is '{value}': set it to 1 to run the {category} tests, or unset it.");
        }
        var signInSettings = SettingsOf(signIn);
        var missing = settings.Concat(signInSettings).Distinct(StringComparer.Ordinal)
            .Where(setting => string.IsNullOrEmpty(environment(setting))).ToList();
        if (missing.Count > 0)
        {
            // Said when the sign-in lacks a setting, so a run with only the other sign-in's settings says why it fails.
            var why = signInSettings.Any(missing.Contains) ? $": the test signs in with {Described(signIn)}" : "";
            return new SiteCheck(SiteOutcome.Fail,
                $"{name}=1, but {string.Join(", ", missing)} {(missing.Count == 1 ? "is" : "are")} not set{why}.");
        }
        return new SiteCheck(SiteOutcome.Run, "");
    }

    private static string Described(LiveSignIn signIn) =>
        signIn == LiveSignIn.Token ? "the personal access token" : "the connected app";

    /// <summary>
    /// What is wrong with a test class's order: one with a site test runs its tests one at a time, as every other site
    /// test does, so a token's sign-in never ends another test's session and each call answered is its own test's.
    /// </summary>
    public static string? OrderProblem(string type, bool hasSiteTest, bool doNotParallelize) =>
        hasSiteTest && !doNotParallelize ? $"{type} has a SiteTest and no [DoNotParallelize]: a site test runs one at a time" : null;

    /// <summary>What is wrong with a test's marks: a site test without its category, or the reverse.</summary>
    public static IEnumerable<string> MarkProblems(string test, SiteTestAttribute? site, IEnumerable<string> categories)
    {
        var siteCategories = categories.Intersect(Categories, StringComparer.Ordinal).ToList();
        if (site is not null && !siteCategories.Contains(site.Category, StringComparer.Ordinal))
            yield return $"{test} is a {site.Category} SiteTest without [TestCategory({site.Category})]";
        foreach (var category in siteCategories.Where(category => site?.Category != category))
        {
            yield return $"{test} is in the {category} category without [SiteTest({category}, ...)], "
                + "which checks its switch";
        }
    }
}

/// <summary>
/// [TestMethod] for a test on a Tableau site: before the test runs, it checks the category's switch and the settings
/// named, and ends the test inconclusive or failed as <see cref="TableauSite.Check"/> says. While the test runs, it is
/// the one that calls in <see cref="LiveSite.Record"/>, which keeps how it ended too. A result that didn't pass comes
/// back with every known value and live secret as <c>***</c> in its message, its stack trace and its output
/// (<see cref="Scrubbed"/>), since a failure's message carries Tableau's detail, which can name the site or a
/// directory account, into the public log, where GitHub masks only a secret's exact value.
/// </summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
internal sealed class SiteTestAttribute(
    string category,
    string[] settings,
    [CallerFilePath] string callerFilePath = "",
    [CallerLineNumber] int callerLineNumber = -1) : TestMethodAttribute(callerFilePath, callerLineNumber)
{
    public string Category { get; } = category;

    public IReadOnlyList<string> Settings { get; } = settings;

    /// <summary>The sign-in the test needs, whose settings it needs besides its own; none by default.</summary>
    public LiveSignIn SignsIn { get; set; }

    public override async Task<TestResult[]> ExecuteAsync(ITestMethod testMethod)
    {
        var name = $"{testMethod.MethodInfo.DeclaringType!.Name}.{testMethod.TestMethodName}";
        var check = TableauSite.Check(Category, Settings, Environment.GetEnvironmentVariable, SignsIn);
        if (Ended(check) is { } ended)
        {
            if (check.Outcome == SiteOutcome.Fail) LiveSite.Record.Ended(name, passed: false);
            return [ended];
        }
        TestResult[] results;
        using (LiveSite.Record.As(name))
            results = await base.ExecuteAsync(testMethod);
        LiveSite.Record.Ended(name, results.All(result => result.Outcome == UnitTestOutcome.Passed));
        return [.. results.Select(result => result.Outcome == UnitTestOutcome.Passed ? result : Scrubbed(result, LiveSite.Scrubbed))];
    }

    /// <summary>
    /// A copy of the result with <paramref name="scrub"/> applied to its failure's message and stack trace, and to its
    /// output. A copy, since MSTest's <see cref="TestResult.TestFailureException"/> adds an exception to the one it holds.
    /// </summary>
    public static TestResult Scrubbed(TestResult result, Func<string, string> scrub)
    {
        string[] scrubbed = [nameof(TestResult.TestFailureException), nameof(TestResult.LogOutput), nameof(TestResult.LogError), nameof(TestResult.DebugTrace),
            nameof(TestResult.TestContextMessages)];
        var copy = new TestResult();
        foreach (var property in typeof(TestResult).GetProperties().Where(property => property.CanRead && property.CanWrite && !scrubbed.Contains(property.Name)))
            property.SetValue(copy, property.GetValue(result));
        if (result.TestFailureException is { } failure)
            copy.TestFailureException = new ScrubbedFailure(scrub(failure.Message), scrub(StackTraceOf(failure) ?? ""));
        copy.LogOutput = result.LogOutput is { } output ? scrub(output) : null;
        copy.LogError = result.LogError is { } error ? scrub(error) : null;
        copy.DebugTrace = result.DebugTrace is { } trace ? scrub(trace) : null;
        copy.TestContextMessages = result.TestContextMessages is { } messages ? scrub(messages) : null;
        return copy;
    }

    // MSTest keeps a failed test's own stack trace beside the exception it reports, in StackTraceInformation.
    private static string? StackTraceOf(Exception failure) =>
        failure.GetType().GetProperty("StackTraceInformation")?.GetValue(failure) is { } information
            && information.GetType().GetProperty("ErrorStackTrace")?.GetValue(information) is string stack
            ? stack
            : failure.StackTrace;

    /// <summary>The result a check ends the test with, or null when it runs.</summary>
    public static TestResult? Ended(SiteCheck check) => check.Outcome switch
    {
        SiteOutcome.Inconclusive => new TestResult
        {
            Outcome = UnitTestOutcome.Inconclusive,
            TestFailureException = new AssertInconclusiveException(check.Message),
        },
        SiteOutcome.Fail => new TestResult
        {
            Outcome = UnitTestOutcome.Failed,
            TestFailureException = new AssertFailedException(check.Message),
        },
        _ => null,
    };
}

/// <summary>
/// The sign-in a live test needs, each with its own job: the personal access token does the content work, and the
/// connected app signs in for its own tests alone, since on Tableau Server 2025.1 a connected app's session can't call a
/// method whose scope came in REST API 3.27.
/// </summary>
internal enum LiveSignIn
{
    /// <summary>No sign-in of the live suite's: the test signs in with its own settings, or not at all, as Server Info's.</summary>
    None,

    /// <summary>The personal access token: TABLEAU_LIVE_PAT_NAME and TABLEAU_LIVE_PAT_SECRET.</summary>
    Token,

    /// <summary>
    /// The connected app's direct trust: TABLEAU_LIVE_CLIENT_ID, TABLEAU_LIVE_SECRET_ID, TABLEAU_LIVE_SECRET_VALUE and
    /// TABLEAU_LIVE_USER.
    /// </summary>
    ConnectedApp,
}

/// <summary>A site test's failure as it is reported, its message and stack trace scrubbed.</summary>
internal sealed class ScrubbedFailure(string message, string stackTrace) : Exception(message)
{
    public override string StackTrace => stackTrace;

    public override string ToString() => $"{Message}\n{StackTrace}";
}

internal enum SiteOutcome
{
    Run,
    Inconclusive,
    Fail,
}

/// <summary>What <see cref="TableauSite.Check"/> found: run, or end the test with the message.</summary>
internal sealed record SiteCheck(SiteOutcome Outcome, string Message);
