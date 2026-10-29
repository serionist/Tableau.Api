using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Tableau.Api.Exceptions;
using Tableau.Api.Tests.Fakes;

namespace Tableau.Api.Tests;

/// <summary>The library's log lines hold no password, session token or personal access token secret.</summary>
[TestClass]
public sealed class LogTests
{
    private const string ApiPath = "/api/" + Fixtures.ApiVersion;

    [TestMethod]
    public async Task SignInsCallsAndFailuresLogNoSecret()
    {
        using var server = FakeTableauServer.Start();
        var log = new RecordingLogger();
        var client = new TableauApiClient(server.BaseUrl, log, Fixtures.ApiVersion);
        server.Map("POST", ApiPath + "/auth/signin", Fixtures.SignedIn());
        server.Map("GET", ApiPath + "/sites/" + Fixtures.SiteId + "/projects",
            FakeResponse.Redirect(302, "https://tableau.example.com/next"));

        var session = await client.SignIn(Fixtures.UserName, Fixtures.Password, Fixtures.SiteContentUrl);
        await client.SignInWithPAT("nightly-export", Fixtures.PatSecret, Fixtures.SiteContentUrl);
        await Assert.ThrowsExactlyAsync<TableauRequestException>(() => client.QueryProjects(session));
        await Assert.ThrowsExactlyAsync<TableauRequestException>(() => client.QueryDataSource(session, "d1"));
        await Assert.ThrowsExactlyAsync<TableauRequestException>(() => client.SignOut(session));

        Assert.IsGreaterThanOrEqualTo(10, log.Lines.Count, "the calls logged too little to check");
        Assert.IsEmpty(log.LinesHolding(Fixtures.Secrets).ToList());
    }

    /// <summary>The same for every line the suite's clients logged, once every test has run.</summary>
    [AssemblyCleanup]
    public static void NoLogLineInTheSuiteHoldsASecret()
    {
        var holding = Fixtures.Log.LinesHolding(Fixtures.Secrets).ToList();
        if (holding.Count > 0)
            throw new InvalidOperationException($"{holding.Count} log line(s) hold a secret:\n" + string.Join("\n", holding));
    }
}
