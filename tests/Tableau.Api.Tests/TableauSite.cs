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
/// fails the test and names it. Settings are environment variables, kept outside the clone with the site's secrets.
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

    /// <summary>The site categories.</summary>
    public static IReadOnlyCollection<string> Categories => _switches.Keys;

    /// <summary>A setting the test's <see cref="SiteTestAttribute"/> named, which it found set before the test.</summary>
    public static string Setting(string name) =>
        Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException($"{name} is not set; name it in the test's SiteTest attribute.");

    /// <summary>Whether a test of category runs, with the environment environment gives.</summary>
    public static SiteCheck Check(string category, IReadOnlyList<string> settings, Func<string, string?> environment)
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
        var missing = settings.Where(setting => string.IsNullOrEmpty(environment(setting))).ToList();
        if (missing.Count > 0)
        {
            return new SiteCheck(SiteOutcome.Fail,
                $"{name}=1, but {string.Join(", ", missing)} {(missing.Count == 1 ? "is" : "are")} not set.");
        }
        return new SiteCheck(SiteOutcome.Run, "");
    }

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
/// named, and ends the test inconclusive or failed as <see cref="TableauSite.Check"/> says.
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

    public override Task<TestResult[]> ExecuteAsync(ITestMethod testMethod) =>
        Ended(TableauSite.Check(Category, Settings, Environment.GetEnvironmentVariable)) is { } ended
            ? Task.FromResult<TestResult[]>([ended])
            : base.ExecuteAsync(testMethod);

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

internal enum SiteOutcome
{
    Run,
    Inconclusive,
    Fail,
}

/// <summary>What <see cref="TableauSite.Check"/> found: run, or end the test with the message.</summary>
internal sealed record SiteCheck(SiteOutcome Outcome, string Message);
