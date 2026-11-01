using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tableau.Api.Tests;

/// <summary>What runs once every test has: the suite's one assembly cleanup, since a test assembly has one.</summary>
[TestClass]
public sealed class SuiteCleanup
{
    /// <summary>The name the cleanup calls under in <see cref="LiveSite.Record"/>.</summary>
    public const string Name = nameof(SuiteCleanup);

    /// <summary>
    /// The live run's project deleted, if a live test made it, and the suite's HttpClient disposed once that client has
    /// signed out through it. With TABLEAU_LIVE_MATRIX=1, the live-coverage matrix held to what the run's calls answered
    /// (<see cref="LiveCoverage.NightlyProblems"/>), each row it fails named. Then the fixtures a recording wrote scanned
    /// for every live secret and setting, a file holding one deleted and named, never the value. Then every log line the
    /// suite took checked, the cleanup's own among them; in a run that reached a site, a failure counts the lines and
    /// shows none. Each runs whatever the one before did.
    /// </summary>
    [AssemblyCleanup]
    public static async Task AfterEveryTestAsync()
    {
        var failures = new List<Exception>();
        try
        {
            await LiveSite.Record.AsAsync(Name, async () =>
            {
                await LiveSite.CleanupAsync();
                return true;
            });
        }
        catch (Exception e)
        {
            failures.Add(e);
        }
        LiveSite.DisposeHttp();
        if (Environment.GetEnvironmentVariable("TABLEAU_LIVE_MATRIX") == "1"
            && MatrixProblems() is { Count: > 0 } problems)
        {
            failures.Add(new InvalidOperationException($"The live run doesn't bear out {problems.Count} row(s) of docs/live-coverage.md:\n"
                + string.Join("\n", problems)));
        }
        if (LiveSite.Recorded is { } folder
            && FixtureRecorder.DeleteHolding(folder, LiveSite.Secrets, LiveSite.Known) is { Count: > 0 } holding)
        {
            failures.Add(new InvalidOperationException($"{holding.Count} recorded file(s) held a live secret or setting, and were "
                + "deleted: " + string.Join(", ", holding)));
        }
        try
        {
            var reachedSite = LiveSite.Secrets.Count > 0
                || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("TABLEAU_LIVE"))
                || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("TABLEAU_SERVER_ONLY"));
            LogTests.NoLogLineInTheSuiteHoldsASecretOrAQueryValue(LiveSite.Secrets, reachedSite);
        }
        catch (InvalidOperationException e)
        {
            failures.Add(e);
        }
        if (failures.Count == 1) throw failures[0];
        if (failures.Count > 1) throw new AggregateException("The suite's cleanup failed more than once.", failures);
    }

    // The table's rows against the run's record; a table that can't be read is a problem of its own.
    private static List<string> MatrixProblems()
    {
        var (rows, problems) = LiveCoverage.Parse(LiveCoverage.Text(), LiveCoverage.ClientSection);
        problems.AddRange(LiveCoverage.NightlyProblems(rows, LiveSite.Record.Answered, LiveSite.Record.Failed));
        return problems;
    }
}
