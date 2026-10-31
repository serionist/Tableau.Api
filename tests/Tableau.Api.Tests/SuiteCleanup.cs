using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tableau.Api.Tests;

/// <summary>What runs once every test has: the suite's one assembly cleanup, since a test assembly has one.</summary>
[TestClass]
public sealed class SuiteCleanup
{
    /// <summary>
    /// The live run's project deleted, if a live test made it; then every log line the suite took checked, the
    /// cleanup's own among them.
    /// </summary>
    [AssemblyCleanup]
    public static async Task AfterEveryTestAsync()
    {
        try
        {
            await LiveSite.CleanupAsync();
        }
        finally
        {
            LogTests.NoLogLineInTheSuiteHoldsASecretOrAQueryValue(LiveSite.Secrets);
        }
    }
}
