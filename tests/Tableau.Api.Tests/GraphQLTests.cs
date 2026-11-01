using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Tableau.Api.Tests.Fakes;

namespace Tableau.Api.Tests;

/// <summary>
/// The Metadata API through <see cref="MetadataClient"/>: a query's body on <c>api/metadata/graphql</c>, its data read as
/// JSON or through the caller's own source-generated type, a connection paged by its cursors over the fixtures in
/// <c>Fixtures/GraphQL</c>, and an answer with <c>errors</c>, or one that isn't GraphQL's, made a
/// <see cref="TableauException"/>. The fixtures are invented, in the shapes the Metadata API's reference gives.
/// </summary>
[TestClass]
public sealed class GraphQLTests
{
    private const string Endpoint = "/api/metadata/graphql";

    private const string Paged = """
        query databases($first: Int, $after: String) { databasesConnection(first: $first, after: $after) { nodes { id name connectionType } pageInfo { hasNextPage endCursor } totalCount } }
        """;

    [TestMethod]
    public async Task AQueryIsPostedWithItsVariablesInTheSessionAndItsDataReturned()
    {
        using var server = AreaFixtures.Server();
        server.Map("POST", Endpoint, FakeResponse.Json(200, GraphQL("databases-page-1.json")));
        await using var client = AreaFixtures.Client(server);

        var data = await client.Metadata.QueryAsync("query q($name: String) { databasesConnection(filter: {name: $name}) { totalCount } }",
            new JsonObject { ["name"] = "invented \"airlines\"" });

        var call = AreaFixtures.Sent(server, $"POST {Endpoint}", """
            {"query":"query q($name: String) { databasesConnection(filter: {name: $name}) { totalCount } }","variables":{"name":"invented \"airlines\""}}
            """);
        Assert.AreEqual("application/json", call.Header("Accept"));
        Assert.AreEqual(3, data.GetProperty("databasesConnection").GetProperty("totalCount").GetInt32());
    }

    [TestMethod]
    public async Task AQueryWithoutVariablesSendsNone()
    {
        using var server = AreaFixtures.Server();
        server.Map("POST", Endpoint, FakeResponse.Json(200, """{"data":{"databases":[]}}"""));
        await using var client = AreaFixtures.Client(server, TableauDeployment.Cloud);

        var data = await client.Metadata.QueryAsync("{ databases { id } }");

        AreaFixtures.Sent(server, $"POST {Endpoint}", """{"query":"{ databases { id } }"}""");
        Assert.AreEqual(0, data.GetProperty("databases").GetArrayLength());
    }

    [TestMethod]
    public async Task ATypedQueryReadsItsDataThroughTheCallersJsonTypeInfo()
    {
        using var server = AreaFixtures.Server();
        server.Map("POST", Endpoint, FakeResponse.Json(200, GraphQL("databases-page-1.json")));
        await using var client = AreaFixtures.Client(server);

        var data = await client.Metadata.QueryAsync(Paged, null, GraphQLJson.Default.DatabasesData);

        Assert.AreEqual(3, data.DatabasesConnection.TotalCount);
        CollectionAssert.AreEqual(new[] { "invented-airlines", "invented.xlsx" }, data.DatabasesConnection.Nodes.Select(node => node.Name).ToArray());
    }

    [TestMethod]
    public async Task AConnectionIsReadPageByPageAfterEachEndCursorUntilTheLast()
    {
        using var server = AreaFixtures.Server();
        server.Map("POST", Endpoint, request => FakeResponse.Json(200,
            GraphQL(JsonNode.Parse(request.BodyText)!["variables"]!["after"] is null ? "databases-page-1.json" : "databases-page-2.json")));
        await using var client = AreaFixtures.Client(server);

        var nodes = await AreaFixtures.AllAsync(client.Metadata.ListNodesAsync(Paged, "databasesConnection", GraphQLJson.Default.DatabaseNode,
            new JsonObject { ["first"] = 7, ["unrelated"] = "kept" }, pageSize: 2));

        CollectionAssert.AreEqual(new[] { "invented-airlines", "invented.xlsx", "invented.json" }, nodes.Select(node => node.Name).ToArray());
        var variables = AreaFixtures.Calls(server).Select(call => JsonNode.Parse(call.BodyText)!["variables"]!.ToJsonString()).ToArray();
        CollectionAssert.AreEqual(new[]
        {
            """{"first":2,"unrelated":"kept","after":null}""",
            """{"first":2,"unrelated":"kept","after":"aW52ZW50ZWQtY3Vyc29yLTI="}""",
        }, variables);
    }

    [TestMethod]
    public async Task APageNamesWhereTheNextStartsAndANestedConnectionIsFoundByItsPath()
    {
        using var server = AreaFixtures.Server();
        server.Map("POST", Endpoint, FakeResponse.Json(200, """
            {"data":{"database":{"tablesConnection":{"nodes":[{"id":"t1","name":"Orders"},null],"pageInfo":{"hasNextPage":true,"endCursor":"c1"}}}}}
            """));
        await using var client = AreaFixtures.Client(server);

        var page = await client.Metadata.QueryPageAsync("query($first: Int, $after: String) { ... }", "database.tablesConnection",
            GraphQLJson.Default.DatabaseNode, after: "c0");

        Assert.AreEqual(("c1", true), (page.EndCursor, page.HasNextPage));
        Assert.AreEqual("Orders", page.Items.Single().Name);
        Assert.AreEqual("""{"first":100,"after":"c0"}""", JsonNode.Parse(AreaFixtures.Calls(server)[0].BodyText)!["variables"]!.ToJsonString());
    }

    [TestMethod]
    public async Task AnAnswerWithErrorsIsATableauExceptionWithItsCodesAndMessages()
    {
        using var server = AreaFixtures.Server();
        server.Map("POST", Endpoint, FakeResponse.Json(200, GraphQL("errors.json")));
        await using var client = AreaFixtures.Client(server);

        var thrown = await ClientFixtures.FailsAsync(TableauErrorKind.Http, () => client.Metadata.QueryAsync(Paged));

        Assert.AreEqual(HttpStatusCode.OK, thrown.StatusCode);
        // The warning comes first; the code and the summary are the error's, which failed the call, and the warning's
        // message is in Detail.
        Assert.IsNull(thrown.ErrorCode);
        Assert.AreEqual("Invented: a second error, its code left out.", thrown.Summary);
        Assert.StartsWith("Showing partial results.", thrown.Detail);
        Assert.AreEqual(Endpoint, thrown.RequestPath);
        Assert.Contains("Metadata API query (POST /api/metadata/graphql) answered with errors: NODE_LIMIT_EXCEEDED: Showing partial results.", thrown.Message);
        Assert.Contains("NODE_LIMIT_EXCEEDED", thrown.ResponseBody!);
        await ClientFixtures.FailsAsync(TableauErrorKind.Http,
            () => AreaFixtures.AllAsync(client.Metadata.ListNodesAsync(Paged, "databasesConnection", GraphQLJson.Default.DatabaseNode)));
    }

    [TestMethod]
    public async Task ARateLimitIsRateLimited()
    {
        using var server = AreaFixtures.Server();
        server.Map("POST", Endpoint, FakeResponse.Json(200, """
            {"errors":[{"message":"The rate of API requests exceeded what's allowed.","extensions":{"severity":"ERROR","code":"RATE_EXCEEDED"}}]}
            """));
        await using var client = AreaFixtures.Client(server);

        var thrown = await ClientFixtures.FailsAsync(TableauErrorKind.RateLimited, () => client.Metadata.QueryAsync("{ databases { id } }"));

        Assert.AreEqual("RATE_EXCEEDED", thrown.ErrorCode);
        Assert.IsNull(thrown.Detail);
    }

    [TestMethod]
    [DataRow("not json", "a body that isn't JSON")]
    [DataRow("[1]", "a body that isn't GraphQL's answer")]
    [DataRow("{}", "no data")]
    [DataRow("""{"data":null}""", "no data")]
    [DataRow("""{"data":{"databasesConnection":null}}""", "its databasesConnection is no connection object")]
    [DataRow("""{"data":{"other":{}}}""", "its data holds no databasesConnection")]
    [DataRow("""{"data":{"databasesConnection":{"pageInfo":{"hasNextPage":false}}}}""", "has no nodes")]
    [DataRow("""{"data":{"databasesConnection":{"nodes":[]}}}""", "has no pageInfo with hasNextPage")]
    [DataRow("""{"data":{"databasesConnection":{"nodes":[],"pageInfo":{"hasNextPage":"no"}}}}""", "has no pageInfo with hasNextPage")]
    [DataRow("""{"data":{"databasesConnection":{"nodes":[],"pageInfo":{"hasNextPage":true}}}}""", "has no endCursor, so the next page can't be asked for: the query asks for pageInfo { hasNextPage endCursor }")]
    [DataRow("""{"data":{"databasesConnection":{"nodes":[{"id":1}],"pageInfo":{"hasNextPage":false}}}}""", "data it couldn't read")]
    [DataRow("""{"errors":"none","data":{"databasesConnection":{"nodes":[{"id":1}],"pageInfo":{"hasNextPage":false}}}}""", "data it couldn't read")]
    public async Task AnAnswerThatIsntAConnectionPageIsAnInvalidResponse(string body, string what)
    {
        using var server = AreaFixtures.Server();
        server.Map("POST", Endpoint, FakeResponse.Json(200, body));
        await using var client = AreaFixtures.Client(server);

        var thrown = await ClientFixtures.FailsAsync(TableauErrorKind.InvalidResponse,
            () => client.Metadata.QueryPageAsync(Paged, "databasesConnection", GraphQLJson.Default.DatabaseNode));

        Assert.Contains(what, thrown.Message);
        Assert.AreEqual(body, thrown.ResponseBody);
        Assert.AreEqual(Endpoint, thrown.RequestPath);
    }

    [TestMethod]
    public async Task DataThatDoesntReadAsTheCallersTypeIsAnInvalidResponse()
    {
        using var server = AreaFixtures.Server();
        server.Map("POST", Endpoint, FakeResponse.Json(200, """{"data":{"databasesConnection":"none"}}"""));
        await using var client = AreaFixtures.Client(server);

        var thrown = await ClientFixtures.FailsAsync(TableauErrorKind.InvalidResponse,
            () => client.Metadata.QueryAsync(Paged, null, GraphQLJson.Default.DatabasesData));

        Assert.IsInstanceOfType<JsonException>(thrown.InnerException);
    }

    [TestMethod]
    [DataRow("""{"data":{"c":{"nodes":[],"pageInfo":{"hasNextPage":true,"endCursor":null}}}}""", "with no cursor")]
    [DataRow("""{"data":{"c":{"nodes":[],"pageInfo":{"hasNextPage":true,"endCursor":"same"}}}}""", "with a cursor it gave before")]
    public async Task APageThatSaysMoreFollowWithoutANewCursorEndsTheList(string body, string what)
    {
        using var server = AreaFixtures.Server();
        server.Map("POST", Endpoint, FakeResponse.Json(200, body));
        await using var client = AreaFixtures.Client(server);

        var thrown = await ClientFixtures.FailsAsync(TableauErrorKind.InvalidResponse,
            () => AreaFixtures.AllAsync(client.Metadata.ListNodesAsync(Paged, "c", GraphQLJson.Default.DatabaseNode)));

        Assert.Contains($"Metadata API query answered that more pages follow, {what}", thrown.Message);
        Assert.HasCount(what == "with no cursor" ? 1 : 2, AreaFixtures.Calls(server));
    }

    [TestMethod]
    public async Task AForbiddenQuerySaysTheMetadataApiMayBeOff()
    {
        using var server = AreaFixtures.Server();
        // As a Tableau Server 2025.1 answered every query, from its metadata service.
        server.Map("POST", Endpoint, FakeResponse.Json(403, """
            {"timestamp":"2026-11-01T10:00:00.000+00:00","status":403,"error":"Forbidden","path":"/relationship-service-war/graphql"}
            """));
        await using var client = AreaFixtures.Client(server);

        var thrown = await ClientFixtures.FailsAsync(TableauErrorKind.Http, () => client.Metadata.QueryAsync("{ databases { id } }"));

        Assert.AreEqual(HttpStatusCode.Forbidden, thrown.StatusCode);
        Assert.Contains("tsm maintenance metadata-services enable", thrown.Message);
    }

    [TestMethod]
    public async Task AnErrorStatusWithGraphQLErrorsNamesTheFirst()
    {
        using var server = AreaFixtures.Server();
        server.Map("POST", Endpoint, FakeResponse.Json(400, """
            {"errors":[{"message":"Invalid Syntax : offending token '<EOF>'","extensions":{"code":"INVALID_ARGUMENT"}},{"message":"Invented: a second."}]}
            """).WithHeader("X-Request-Id", "invented-request-400"));
        await using var client = AreaFixtures.Client(server);

        var thrown = await ClientFixtures.FailsAsync(TableauErrorKind.Http, () => client.Metadata.QueryAsync("{ databases"));

        // As a 200's errors read: the first's code and message, the others' messages.
        Assert.AreEqual(("INVALID_ARGUMENT", "Invalid Syntax : offending token '<EOF>'", "Invented: a second."), (thrown.ErrorCode, thrown.Summary, thrown.Detail));
        Assert.AreEqual("invented-request-400", thrown.RequestId);
        Assert.DoesNotContain("metadata-services", thrown.Message);
    }

    [TestMethod]
    [DataRow(200)]
    [DataRow(400)]
    [DataRow(503)]
    public async Task RateExceededIsRateLimitedWhateverTheStatus(int status)
    {
        using var server = AreaFixtures.Server();
        server.Map("POST", Endpoint, FakeResponse.Json(status, """
            {"errors":[{"message":"The rate of API requests exceeded what's allowed.","extensions":{"severity":"ERROR","code":"RATE_EXCEEDED"}}]}
            """).WithHeader("X-Request-Id", "invented-request-rate"));
        await using var client = AreaFixtures.Client(server);

        var thrown = await ClientFixtures.FailsAsync(TableauErrorKind.RateLimited, () => client.Metadata.QueryAsync("{ databases { id } }"));

        Assert.AreEqual(("RATE_EXCEEDED", status), (thrown.ErrorCode, (int?)thrown.StatusCode));
        Assert.AreEqual("invented-request-rate", thrown.RequestId);
    }

    [TestMethod]
    public async Task AnotherEndpointsErrorsNamingRateExceededStayHttp()
    {
        using var server = AreaFixtures.Server();
        server.Map("GET", $"{AreaFixtures.SitePath}/databases/{AreaFixtures.ItemId}", FakeResponse.Json(400, """
            {"errors":[{"message":"Invented.","extensions":{"code":"RATE_EXCEEDED"}}]}
            """));
        await using var client = AreaFixtures.Client(server);

        var thrown = await ClientFixtures.FailsAsync(TableauErrorKind.Http, () => client.Metadata.GetDatabaseAsync(AreaFixtures.ItemId));

        Assert.AreEqual("RATE_EXCEEDED", thrown.ErrorCode);
    }

    [TestMethod]
    public async Task ARefusedArgumentSendsNothing()
    {
        using var server = AreaFixtures.Server();
        await using var client = AreaFixtures.Client(server);
        var node = GraphQLJson.Default.DatabaseNode;

        Assert.AreEqual("query", (await AreaFixtures.RefusedAsync<ArgumentException>(server, () => client.Metadata.QueryAsync(" "))).ParamName);
        Assert.AreEqual("query", (await AreaFixtures.RefusedAsync<ArgumentException>(server,
            () => client.Metadata.QueryAsync("", null, GraphQLJson.Default.DatabasesData))).ParamName);
        await AreaFixtures.RefusedAsync<ArgumentNullException>(server, () => client.Metadata.QueryAsync<GraphQLDatabasesData>(Paged, null, null!));
        Assert.AreEqual("connection", (await AreaFixtures.RefusedAsync<ArgumentException>(server, () => client.Metadata.QueryPageAsync(Paged, "", node))).ParamName);
        Assert.AreEqual("connection", (await AreaFixtures.RefusedAsync<ArgumentException>(server,
            () => client.Metadata.QueryPageAsync(Paged, "database..tablesConnection", node))).ParamName);
        await AreaFixtures.RefusedAsync<ArgumentNullException>(server, () => client.Metadata.QueryPageAsync<GraphQLDatabaseNode>(Paged, "c", null!));
        Assert.AreEqual("pageSize", (await AreaFixtures.RefusedAsync<ArgumentOutOfRangeException>(server,
            () => client.Metadata.QueryPageAsync(Paged, "c", node, pageSize: 0))).ParamName);
        Assert.AreEqual("pageSize", (await AreaFixtures.RefusedAsync<ArgumentOutOfRangeException>(server,
            () => AreaFixtures.AllAsync(client.Metadata.ListNodesAsync(Paged, "c", node, pageSize: 1001)))).ParamName);
    }

    [TestMethod]
    public Task AQueryIsGatedAtTheMetadataApisFirstVersion() =>
        PermissionsTests.AssertGatedAsync("3.5", "Metadata API query", client => client.Metadata.QueryAsync("{ databases { id } }"));

    [TestMethod]
    public async Task AMockThatOverridesThePageServesTheList()
    {
        var mock = new PagedGraph();

        var nodes = await AreaFixtures.AllAsync(mock.ListNodesAsync(Paged, "databasesConnection", GraphQLJson.Default.DatabaseNode));

        CollectionAssert.AreEqual(new[] { "n-", "n-c1" }, nodes.Select(node => node.Name).ToArray());
    }

    [TestMethod]
    public async Task CancellationStopsTheListBetweenNodes()
    {
        using var server = AreaFixtures.Server();
        server.Map("POST", Endpoint, FakeResponse.Json(200, GraphQL("databases-page-1.json")));
        await using var client = AreaFixtures.Client(server);
        using var cancel = new CancellationTokenSource();
        var read = 0;

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            await foreach (var node in client.Metadata.ListNodesAsync(Paged, "databasesConnection", GraphQLJson.Default.DatabaseNode,
                               cancellationToken: cancel.Token))
            {
                read++;
                await cancel.CancelAsync();
            }
        });

        // The page holds two nodes: the second is never read.
        Assert.AreEqual(1, read);
        Assert.HasCount(1, AreaFixtures.Calls(server));
    }

    [TestMethod]
    public async Task AnAnswerWhoseErrorsAreAllWarningsReturnsItsDataAndLogsEachAtWarning()
    {
        using var server = AreaFixtures.Server();
        server.Map("POST", Endpoint, FakeResponse.Json(200, """
            {"data":{"databasesConnection":{"nodes":[{"id":"d1","name":"Partial"}],"pageInfo":{"hasNextPage":false,"endCursor":"c1"},"totalCount":1}},
             "errors":[{"message":"Still creating the Metadata API Store. Results from the query may be incomplete at this time.","extensions":{"severity":"WARNING","code":"BACKFILL_RUNNING"}},
                       {"message":"Invented: a second warning.","extensions":{"severity":"warning","code":"TIME_LIMIT_EXCEEDED"}}]}
            """));
        var log = new RecordingLogger();
        await using var client = Logged(server, log);

        var data = await client.Metadata.QueryAsync(Paged, null, GraphQLJson.Default.DatabasesData);
        var nodes = await AreaFixtures.AllAsync(client.Metadata.ListNodesAsync(Paged, "databasesConnection", GraphQLJson.Default.DatabaseNode));

        Assert.AreEqual("Partial", data.DatabasesConnection.Nodes.Single().Name);
        Assert.AreEqual("Partial", nodes.Single().Name);
        var warnings = Partial(log);
        Assert.HasCount(4, warnings, string.Join("\n", warnings));
        Assert.Contains("POST /api/metadata/graphql answered with a warning, and its data, which may be partial, is returned: BACKFILL_RUNNING: "
            + "Still creating the Metadata API Store.", warnings[0]);
        Assert.Contains("TIME_LIMIT_EXCEEDED: Invented: a second warning.", warnings[1]);
    }

    [TestMethod]
    public async Task AWarningBesideAnErrorFailsTheCallAndLogsNoWarning()
    {
        using var server = AreaFixtures.Server();
        server.Map("POST", Endpoint, FakeResponse.Json(200, """
            {"data":{"databasesConnection":null},
             "errors":[{"message":"Still creating the Metadata API Store.","extensions":{"severity":"WARNING","code":"BACKFILL_RUNNING"}},
                       {"message":"Invented: an error.","extensions":{"severity":"ERROR","code":"INVALID_ARGUMENT"}}]}
            """));
        var log = new RecordingLogger();
        await using var client = Logged(server, log);

        var thrown = await ClientFixtures.FailsAsync(TableauErrorKind.Http, () => client.Metadata.QueryAsync(Paged));

        // The error, not the warning before it, names why the call failed; the warning is in Detail and the message.
        Assert.AreEqual(("INVALID_ARGUMENT", "Invented: an error.", "Still creating the Metadata API Store."),
            (thrown.ErrorCode, thrown.Summary, thrown.Detail));
        Assert.Contains("BACKFILL_RUNNING: Still creating the Metadata API Store.; INVALID_ARGUMENT: Invented: an error.", thrown.Message);
        Assert.IsEmpty(Partial(log));
    }

    [TestMethod]
    [DataRow("""{"data":null,""")]
    [DataRow("""{""")]
    public async Task WarningsWithNoDataFailUnderTheWarningsCodeAndLogNothing(string start)
    {
        using var server = AreaFixtures.Server();
        server.Map("POST", Endpoint, FakeResponse.Json(200, start + """
             "errors":[{"message":"Can't show all data because the timeout limit has been exceeded.","extensions":{"severity":"WARNING","code":"TIME_LIMIT_EXCEEDED"}}]}
            """));
        var log = new RecordingLogger();
        await using var client = Logged(server, log);

        var thrown = await ClientFixtures.FailsAsync(TableauErrorKind.Http, () => client.Metadata.QueryAsync(Paged));

        Assert.AreEqual(("TIME_LIMIT_EXCEEDED", "Can't show all data because the timeout limit has been exceeded."), (thrown.ErrorCode, thrown.Summary));
        Assert.IsNull(thrown.Detail);
        Assert.IsEmpty(Partial(log), "nothing is returned, so no line says it is");
    }

    [TestMethod]
    public async Task WarningsBesideDataThatDoesntReadLogNothing()
    {
        using var server = AreaFixtures.Server();
        server.Map("POST", Endpoint, FakeResponse.Json(200, """
            {"data":{"databasesConnection":"none"},
             "errors":[{"message":"Still creating the Metadata API Store.","extensions":{"severity":"WARNING","code":"BACKFILL_RUNNING"}}]}
            """));
        var log = new RecordingLogger();
        await using var client = Logged(server, log);

        await ClientFixtures.FailsAsync(TableauErrorKind.InvalidResponse, () => client.Metadata.QueryAsync(Paged, null, GraphQLJson.Default.DatabasesData));
        await ClientFixtures.FailsAsync(TableauErrorKind.InvalidResponse,
            () => client.Metadata.QueryPageAsync(Paged, "databasesConnection", GraphQLJson.Default.DatabaseNode));

        Assert.IsEmpty(Partial(log), "nothing is returned, so no line says it is");
    }

    [TestMethod]
    public async Task AnErrorWithoutASeverityFailsTheCall()
    {
        using var server = AreaFixtures.Server();
        server.Map("POST", Endpoint, FakeResponse.Json(200, """{"data":{},"errors":[{"message":"Invented: no extensions."}]}"""));
        await using var client = AreaFixtures.Client(server);

        var thrown = await ClientFixtures.FailsAsync(TableauErrorKind.Http, () => client.Metadata.QueryAsync("{ databases { id } }"));

        Assert.AreEqual((null, "Invented: no extensions."), (thrown.ErrorCode, thrown.Summary));
    }

    // The warnings of answers whose data may be partial, apart from the fake server's plain http one.
    private static List<string> Partial(RecordingLogger log) =>
        [.. log.At(Microsoft.Extensions.Logging.LogLevel.Warning).Where(line => line.Contains("answered with a warning", StringComparison.Ordinal))];

    // A client of the fake server whose log is its own, so its lines are this test's alone.
    private static TableauClient Logged(FakeTableauServer server, RecordingLogger log)
    {
        var options = SessionFixtures.Options(server);
        options.Deployment = TableauDeployment.Server;
        return TableauClient.CreateWithClock(options, SessionFixtures.Pat(), log.Factory, null);
    }

    private static string GraphQL(string file) => File.ReadAllText(Path.Combine(ModelFixtures.Folder, "GraphQL", file));

    // Pages by cursor: the first after none, the second after c1, the last.
    private sealed class PagedGraph : MetadataClient
    {
        public override Task<CursorPage<T>> QueryPageAsync<T>(string query, string connection, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> nodeType,
            string? after = null, JsonObject? variables = null, int pageSize = 100, CancellationToken cancellationToken = default) =>
            Task.FromResult(new CursorPage<T>([(T)(object)new GraphQLDatabaseNode("id", $"n-{after}", null)], "c1", after is null));
    }
}

/// <summary>A database as the tests' queries ask for it.</summary>
/// <param name="Id">Its LUID.</param>
/// <param name="Name">Its name.</param>
/// <param name="ConnectionType">Its connection's type.</param>
internal sealed record GraphQLDatabaseNode(string Id, string Name, string? ConnectionType);

/// <summary>The data of a databases query.</summary>
/// <param name="DatabasesConnection">The connection.</param>
internal sealed record GraphQLDatabasesData(GraphQLDatabasesConnection DatabasesConnection);

/// <summary>A databases connection: its nodes and its total.</summary>
/// <param name="Nodes">The nodes.</param>
/// <param name="TotalCount">How many there are in all.</param>
internal sealed record GraphQLDatabasesConnection(IReadOnlyList<GraphQLDatabaseNode> Nodes, int TotalCount);

/// <summary>The tests' own source-generated JSON, as a caller's would be.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(GraphQLDatabaseNode), TypeInfoPropertyName = "DatabaseNode")]
[JsonSerializable(typeof(GraphQLDatabasesData), TypeInfoPropertyName = "DatabasesData")]
internal sealed partial class GraphQLJson : JsonSerializerContext;
