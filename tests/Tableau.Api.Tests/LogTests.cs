using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Tableau.Api.Exceptions;
using Tableau.Api.Pipeline;
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

    [TestMethod]
    public async Task TheTwoPointZeroClientLogsRequestsAtDebugWithoutQueryValuesOrTheToken()
    {
        using var server = FakeTableauServer.Start();
        server.Map("GET", "/api/3.25/items", ClientFixtures.ServerInfo());
        server.Map("GET", "/api/3.25/missing", FakeResponse.Json(404, """{"error":{"code":"404000","summary":"Not found"}}"""));
        var log = new RecordingLogger();
        await using var client = TableauClient.Create(ClientFixtures.Options(server), log.Factory);
        var query = ClientFixtures.Query(("filter", "email:eq:ada@invented.example"), ("pageSize", "100"));
        using var cancelled = new System.Threading.CancellationTokenSource();
        await cancelled.CancelAsync();

        await ClientFixtures.SendAsync(client, ClientFixtures.Endpoint("{version}/items"),
            new TableauRequest(Query: query, Token: Fixtures.SessionToken));
        await ClientFixtures.FailsAsync(TableauErrorKind.NotFound, () => ClientFixtures.SendAsync(client,
            ClientFixtures.Endpoint("{version}/missing"), new TableauRequest(Query: query, Token: Fixtures.SessionToken)));
        await Assert.ThrowsAsync<OperationCanceledException>(() => ClientFixtures.SendAsync(client,
            ClientFixtures.Endpoint("{version}/items"), new TableauRequest(Token: Fixtures.SessionToken), cancelled.Token));

        var debug = log.At(Microsoft.Extensions.Logging.LogLevel.Debug);
        Assert.IsTrue(debug.Any(line => line.Contains("GET /api/3.25/items?filter=***&pageSize=*** answered 200", StringComparison.Ordinal)),
            string.Join("\n", log.Lines));
        Assert.IsTrue(debug.Any(line => line.Contains("GET /api/3.25/missing?filter=***&pageSize=*** failed: NotFound", StringComparison.Ordinal)),
            string.Join("\n", log.Lines));
        Assert.IsEmpty(log.LinesHolding([.. Fixtures.Secrets, "ada@invented.example", "email:eq"]).ToList());
        Assert.IsTrue(log.Lines.All(line => line.StartsWith("Debug: ", StringComparison.Ordinal)
            || line.Contains("AllowInsecureHttp is set", StringComparison.Ordinal)), string.Join("\n", log.Lines));
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
