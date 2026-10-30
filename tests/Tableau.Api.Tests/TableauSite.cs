using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tableau.Api.Tests;

/// <summary>
/// The tests that need a Tableau site, in two categories. <see cref="Live"/> runs on Tableau Cloud or a Tableau Server;
/// <see cref="ServerOnly"/> shows what only a Tableau Server has, run by hand. Every other test is offline. A site test
/// is marked with its category and starts with <see cref="Require"/>:
/// <code>
/// [TestMethod, TestCategory(TableauSite.Live)]
/// public async Task ServerInfoAnswers()
/// {
///     var site = TableauSite.Require(TableauSite.Live, "TABLEAU_LIVE_SERVER_URL");
///     ...
/// }
/// </code>
/// It is inconclusive unless its category's switch is set to 1, TABLEAU_LIVE or TABLEAU_SERVER_ONLY, so a run with
/// neither, every CI run among them, reaches no site. With the switch set, a setting that is missing or empty fails
/// the test and names it, so a run meant to reach a site never passes having reached none. Settings are environment
/// variables, kept outside the clone with the site's secrets.
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

    /// <summary>The settings named, by name, from the environment; or the test ends, inconclusive or failed.</summary>
    public static IReadOnlyDictionary<string, string> Require(string category, params string[] settings) =>
        Apply(Check(category, settings, Environment.GetEnvironmentVariable));

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
            return SiteCheck.End(SiteOutcome.Inconclusive,
                $"A {category} test: it runs with {name}=1, against a site you may change.");
        }
        if (value != "1")
        {
            return SiteCheck.End(SiteOutcome.Fail,
                $"{name} is '{value}': set it to 1 to run the {category} tests, or unset it.");
        }
        var missing = settings.Where(setting => string.IsNullOrEmpty(environment(setting))).ToList();
        if (missing.Count > 0)
        {
            return SiteCheck.End(SiteOutcome.Fail,
                $"{name}=1, but {string.Join(", ", missing)} {(missing.Count == 1 ? "is" : "are")} not set.");
        }
        return new SiteCheck(SiteOutcome.Run, "",
            settings.ToDictionary(setting => setting, setting => environment(setting)!, StringComparer.Ordinal));
    }

    /// <summary>The settings when the check says run; otherwise the test ends as the check says.</summary>
    public static IReadOnlyDictionary<string, string> Apply(SiteCheck check)
    {
        switch (check.Outcome)
        {
            case SiteOutcome.Inconclusive:
                Assert.Inconclusive(check.Message);
                break;
            case SiteOutcome.Fail:
                Assert.Fail(check.Message);
                break;
        }
        return check.Settings;
    }
}

internal enum SiteOutcome
{
    Run,
    Inconclusive,
    Fail,
}

/// <summary>What <see cref="TableauSite.Check"/> found: run with the settings, or end with the message.</summary>
internal sealed record SiteCheck(SiteOutcome Outcome, string Message, IReadOnlyDictionary<string, string> Settings)
{
    public static SiteCheck End(SiteOutcome outcome, string message) =>
        new(outcome, message, new Dictionary<string, string>());
}
