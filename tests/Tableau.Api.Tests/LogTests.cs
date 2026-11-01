using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Tableau.Api.Exceptions;
using Tableau.Api.Pipeline;
using Tableau.Api.Tests.Fakes;

namespace Tableau.Api.Tests;

/// <summary>The library's log lines hold no password, session token or personal access token secret.</summary>
[TestClass]
public sealed partial class LogTests
{
    private const string ApiPath = "/api/" + Fixtures.ApiVersion;

    [TestMethod]
    public async Task TheOldClientsCallsAndFailuresLogNoSecret()
    {
        using var server = FakeTableauServer.Start();
        var log = new RecordingLogger();
        var client = new TableauApiClient(server.BaseUrl, log, Fixtures.ApiVersion);
        server.Map("GET", ApiPath + "/sites/" + Fixtures.SiteId + "/datasources",
            FakeResponse.Redirect(302, "https://tableau.example.com/next"));
        var session = Fixtures.Session();

        await Assert.ThrowsExactlyAsync<TableauRequestException>(() => client.QueryDataSources(session));
        await Assert.ThrowsExactlyAsync<TableauRequestException>(() => client.QueryDataSource(session, "d1"));
        await Assert.ThrowsExactlyAsync<TableauRequestException>(() => client.DeleteDataSource(session, "d1"));

        Assert.IsGreaterThanOrEqualTo(6, log.Lines.Count, "the calls logged too little to check");
        Assert.IsEmpty(log.LinesHolding(Fixtures.Secrets).ToList());
    }

    [TestMethod]
    public async Task SignInsSignOutsAndTheirFailuresLogNoSecretAndNoUser()
    {
        using var server = FakeTableauServer.Start();
        server.Map("POST", SessionFixtures.SignInPath, SessionFixtures.SignedIn());
        server.Map("GET", SessionFixtures.ItemsPath, ClientFixtures.ServerInfo());
        server.Map("POST", SessionFixtures.SignOutPath, FakeResponse.Json(500, """{"error":{"code":"500000","summary":"Invented"}}"""));
        var log = new RecordingLogger();
        using var refused = FakeTableauServer.Start();
        refused.Map("POST", SessionFixtures.SignInPath, SessionFixtures.Refused());

        await using (var client = TableauClient.Create(SessionFixtures.Options(server), SessionFixtures.DirectTrust(), log.Factory))
            await ClientFixtures.SendAsync(client, SessionFixtures.Items);
        await using (var client = TableauClient.Create(SessionFixtures.Options(server), SessionFixtures.Pat(), log.Factory))
            await ClientFixtures.SendAsync(client, SessionFixtures.Items);
        await using (var client = TableauClient.Create(SessionFixtures.Options(refused), SessionFixtures.Password(), log.Factory))
            await ClientFixtures.FailsAsync(TableauErrorKind.Authentication, () => ClientFixtures.SendAsync(client, SessionFixtures.Items));

        var debug = log.At(Microsoft.Extensions.Logging.LogLevel.Debug);
        Assert.IsTrue(debug.Any(line => line.Contains("signed in with ConnectedAppDirectTrust", StringComparison.Ordinal)), string.Join("\n", log.Lines));
        Assert.IsTrue(debug.Any(line => line.Contains("signing out failed", StringComparison.Ordinal)), string.Join("\n", log.Lines));
        Assert.IsEmpty(log.LinesHolding([.. Fixtures.Secrets, Fixtures.UserName, SessionFixtures.PatName, SessionFixtures.AppUser]).ToList());
    }

    [TestMethod]
    public async Task TheTwoPointZeroClientLogsRequestsAtDebugWithoutQueryValuesOrTheToken()
    {
        using var server = FakeTableauServer.Start();
        server.Map("GET", "/api/3.25/items", ClientFixtures.ServerInfo());
        server.Map("GET", "/api/3.25/missing", FakeResponse.Json(404, """{"error":{"code":"404000","summary":"Not found"}}"""));
        var log = new RecordingLogger();
        await using var client = TableauClient.Create(ClientFixtures.Options(server), loggerFactory: log.Factory);
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

    [TestMethod]
    public async Task ARedirectIsLoggedWithItsTargetsQueryValuesHidden()
    {
        using var server = FakeTableauServer.Start();
        server.Map("GET", "/api/3.25/hop", FakeResponse.Redirect(307, "/api/3.25/items?filter=email:eq:ada@invented.example&flag"));
        server.Map("GET", "/api/3.25/items", ClientFixtures.ServerInfo());
        var log = new RecordingLogger();
        await using var client = TableauClient.Create(ClientFixtures.Options(server), loggerFactory: log.Factory);

        await ClientFixtures.SendAsync(client, ClientFixtures.Endpoint("{version}/hop"), new TableauRequest(
            Query: ClientFixtures.Query(("filter", "name:eq:ada")), Token: Fixtures.SessionToken));

        var debug = log.At(Microsoft.Extensions.Logging.LogLevel.Debug);
        Assert.IsTrue(debug.Any(line => line.Contains(
            "GET /api/3.25/hop?filter=*** answered 307, a redirect to /api/3.25/items?filter=***&flag=***, which it follows", StringComparison.Ordinal)),
            string.Join("\n", log.Lines));
        Assert.IsTrue(debug.Any(line => line.EndsWith(": GET /api/3.25/items?filter=***&flag=***", StringComparison.Ordinal)), string.Join("\n", log.Lines));
        Assert.IsEmpty(log.LinesHolding([.. Fixtures.Secrets, "ada@invented.example", "name:eq"]).ToList());
        Assert.IsEmpty(QueryValuesShown(log.Lines).ToList());
    }

    [TestMethod]
    public void AfterARunThatReachedASiteTheCheckCountsLinesAndShowsNone()
    {
        string[] live = ["live-token-value"];
        var jwt = $"Debug: Tableau client prod: {Fixtures.JwtStart}.minted.jwt";
        var query = "Debug: Tableau client prod: GET /api/3.25/items?filter=email:eq:ada@invented.example";

        var offline = Assert.ThrowsExactly<InvalidOperationException>(() => CheckLines([jwt], [], reachedSite: false));
        var quiet = Assert.ThrowsExactly<InvalidOperationException>(() => CheckLines([jwt], live, reachedSite: true));
        var quietQuery = Assert.ThrowsExactly<InvalidOperationException>(() => CheckLines([query], live, reachedSite: true));
        var leak = Assert.ThrowsExactly<InvalidOperationException>(() => CheckLines(["Debug: live-token-value"], live, reachedSite: true));

        Assert.Contains(jwt, offline.Message, "offline, the line is shown");
        Assert.DoesNotContain(Fixtures.JwtStart, quiet.Message);
        Assert.DoesNotContain("ada@invented.example", quietQuery.Message);
        Assert.DoesNotContain("live-token-value", leak.Message);
        CheckLines(["Debug: Tableau client prod: GET /api/3.25/items?filter=*** answered 200 in 3 ms"], live, reachedSite: true);
    }

    [TestMethod]
    public void TheQueryCheckFindsAValueShown()
    {
        string[] lines =
        [
            "Debug: Tableau client prod: GET /api/3.25/items?filter=*** answered 200 in 3 ms",
            "Debug: Tableau client prod: Test call (GET /api/3.25/items?filter=***&flag=***) failed.",
            "Debug: Tableau client prod: GET /api/3.25/items?filter=***&pageSize=100",
            "Information: Sending request to Tableau Server. Method: GET, Url: 'https://x.example/api/3.4/items?filter=a'",
        ];

        CollectionAssert.AreEqual(new[] { lines[2] }, QueryValuesShown(lines).ToArray());
    }

    /// <summary>
    /// The same for every line any logger in the suite took, once every test has run: no secret in any, and no query
    /// value but <c>***</c> in a line of the 2.0 client. A site's own secrets and tokens, from the live suite, are checked
    /// too. In a run that reached a site, every failure gives a count and shows no line: a line there may hold a live
    /// value no list names, such as a JWT the client minted, and the run's log is public.
    /// </summary>
    /// <param name="live">The live suite's secrets: its settings' and every session token its clients carried.</param>
    /// <param name="reachedSite">Whether the run reached a site: a live setting was read, or a site test's switch is set.</param>
    internal static void NoLogLineInTheSuiteHoldsASecretOrAQueryValue(IReadOnlyCollection<string> live, bool reachedSite) =>
        CheckLines([.. RecordingLogger.All.Append(Fixtures.Log).Distinct().SelectMany(log => log.Lines)], live, reachedSite);

    /// <summary>The check over the lines given.</summary>
    internal static void CheckLines(List<string> lines, IReadOnlyCollection<string> live, bool reachedSite)
    {
        var leaking = lines.Count(line => live.Any(secret => line.Contains(secret, StringComparison.Ordinal)));
        if (leaking > 0)
            throw new InvalidOperationException($"{leaking} log line(s) hold a secret or a token of the live site; they are not shown here.");
        var holding = lines.Where(line => Fixtures.Secrets.Any(secret => line.Contains(secret, StringComparison.Ordinal))).ToList();
        if (holding.Count > 0)
            throw new InvalidOperationException($"{holding.Count} log line(s) hold a secret" + Shown(holding, reachedSite));
        var shown = QueryValuesShown(lines).ToList();
        if (shown.Count > 0)
            throw new InvalidOperationException($"{shown.Count} log line(s) of the 2.0 client show a query value" + Shown(shown, reachedSite));
    }

    // The lines themselves, offline; nothing of them in a run that reached a site.
    private static string Shown(List<string> lines, bool reachedSite) =>
        reachedSite ? "; this run reached a site, so they are not shown here." : ":\n" + string.Join("\n", lines);

    [System.Text.RegularExpressions.GeneratedRegex(@"/[^\s?()']*\?(?<query>[^\s)']+)", System.Text.RegularExpressions.RegexOptions.CultureInvariant)]
    private static partial System.Text.RegularExpressions.Regex QueryPattern();

    // A 2.0 line names its client first; the old client's lines show values, and go with it.
    private static IEnumerable<string> QueryValuesShown(IEnumerable<string> lines) =>
        lines.Where(line => line.Contains(": Tableau client ", StringComparison.Ordinal)
            && QueryPattern().Matches(line).Any(match => match.Groups["query"].Value.TrimEnd('.', ',').Split('&')
                .Any(pair => !pair.EndsWith("=***", StringComparison.Ordinal))));
}
