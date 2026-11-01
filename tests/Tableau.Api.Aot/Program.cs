// The library in a native AOT app. CI publishes it with native AOT, which fails on any trim or AOT warning in the 2.0
// types roots.xml keeps (Tableau.Api.Aot.csproj), then runs the binary, which reads and writes Tableau's JSON through
// the generated models as the client will:
//
// - every fixture of the offline tests (tests/Tableau.Api.Tests/Fixtures, inside the binary) is read and written back,
//   and says the same to Tableau;
// - the shapes Tableau's JSON takes are read for real: a token, numbers sent as strings, a list empty, of one and of
//   many, a value this version doesn't know, text beside an attribute, a time;
// - the client calls through its pipeline, over a handler that answers from the fixtures: the version negotiated
//   through Server Info, Server Info itself, Tableau's error body read into a TableauException, a call retried after a
//   503 by the resilience handler, and a download whose name is cleaned, whose kind is known and which saves to a file;
// - sessions: a call that needs one signs in with a personal access token, carries its token and its site, and the
//   client signs it out when disposed; a connected app's JWT is minted and signed with HMAC-SHA256; a session signed in
//   by hand prints no token and serves a bound client, which signs it out when told;
// - a list read through its sub-client, page by page until the total, with a filter escaped and the stable sort on the
//   wire;
// - a project's permissions listed and added, over a TableauContent;
// - a view's image exported through ExportOptions, its view filter sent as vf_, and a workbook downloaded without its
//   extract through its sub-client and saved;
// - a Metadata API query, its data read through the caller's own source-generated JsonTypeInfo, and a connection read
//   page by page by its cursor, from the GraphQL fixtures;
// - the client from DI, its options bound from configuration by the binder's source generator, its primary handler
//   configured and a handler of the caller's in its chain, and bad options refused at start-up by the options
//   validation source generator's code.
//
// In the native binary reflection-based JSON is off, so a converter that reached for it would fail here. Later steps
// add their paths: a publish, a Vizportal call. It prints a line per check and exits with 1 on the first failure.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Tableau.Api;
using Tableau.Api.Pipeline;
using Tableau.Api.Rest.Models;
using Tableau.Api.Tests;

try
{
    if (!RuntimeFeature.IsDynamicCodeSupported)
        Check("reflection-based JSON is off in the native binary", !JsonSerializer.IsReflectionEnabledByDefault);

    using (var manifest = JsonDocument.Parse(Fixture("manifest.json")))
    {
        foreach (var fixture in manifest.RootElement.GetProperty("fixtures").EnumerateArray())
        {
            var file = fixture.GetProperty("file").GetString()!;
            var text = Fixture(file);
            var written = fixture.GetProperty("body").GetString() == "request"
                ? JsonSerializer.Serialize(JsonSerializer.Deserialize(text, TableauJsonContext.Default.TsRequest)!, TableauJsonContext.Default.TsRequest)
                : JsonSerializer.Serialize(JsonSerializer.Deserialize(text, TableauJsonContext.Default.TsResponse)!, TableauJsonContext.Default.TsResponse);
            var difference = JsonEquivalence.Difference(text, written);
            Check($"{file} reads and writes back", difference is null, difference);
        }
    }

    var credentials = Response("sign-in.json").Credentials;
    Check("a sign-in's token and site", credentials is { Token: "invented-session-token", Site.ContentUrl: "finance" });
    var page = Response("projects-page.json");
    Check("a page's numbers, sent as strings", page.Pagination == new Pagination { PageNumber = 1, PageSize = 3, TotalAvailable = 5 });
    Check("a list of many", page.Projects is { Count: 3 });
    Check("an empty list", Response("projects-empty.json").Projects is { Count: 0 });
    Check("a list of one", Response("users-page.json").Users is [{ Name: "ada" }]);
    var user = Response("user-unknown-values.json").User;
    Check("a value this version doesn't know keeps its text", user?.SiteRole?.Value == "InventedFutureRole" && user.Role == LicensingRole.Interactor);
    Check("text beside an attribute", Response("server-info.json").ServerInfo?.ProductVersion is { Value: "2025.1.0", Build: "20251.25.0101.0000" });
    Check("a time, written as hours, minutes and seconds",
        JsonSerializer.Serialize(new FrequencyDetails { Start = new TimeOnly(18, 30) }, TableauJsonContext.Default.FrequencyDetails) == """{"start":"18:30:00"}""");

    using (var handler = new FixtureHandler())
    using (var http = new HttpClient(handler, disposeHandler: false))
    {
        await using var client = new TableauClient(http, new TableauClientOptions { ServerUrl = new Uri("https://tableau.example.com/") });
        var info = await client.Server.GetInfoAsync();
        Check("the client negotiates its version and reads Server Info", info.ProductVersion?.Value == "2025.1.0"
            && FixtureHandler.Paths.SequenceEqual(["/api/3.23/serverinfo", "/api/3.25/serverinfo"]));

        await using var pinned = new TableauClient(http, new TableauClientOptions
        {
            ServerUrl = new Uri("https://tableau.example.com/"),
            ApiVersion = new Version(9, 0),
        });
        try
        {
            await pinned.Server.GetInfoAsync();
            Check("an error answer throws", false);
        }
        catch (TableauException e)
        {
            Check("Tableau's error body is read", e is { Kind: TableauErrorKind.NotFound, ErrorCode: "404006", Summary: "Resource Not Found" });
        }

        await using var retrying = new TableauClient(http, new TableauClientOptions
        {
            ServerUrl = new Uri("https://tableau.example.com/"),
            ApiVersion = new Version(3, 25),
        });
        var flaky = new Endpoint("Flaky call", HttpMethod.Get, "{version}/flaky", new Version(2, 0), TableauDeployments.Both,
            Idempotent: true, TransferKind.None)
        { NeedsSession = false };
        var retried = await retrying.Pipeline.SendAsync(flaky, default, static body => body.ServerInfo, "serverInfo", CancellationToken.None);
        Check("a call that answered 503 is retried and succeeds", retried.ProductVersion?.Value == "2025.1.0"
            && FixtureHandler.Paths.Count(path => path == "/api/3.25/flaky") == 2);

        var content = new Endpoint("Download Workbook", HttpMethod.Get, "{version}/workbooks/w1/content", new Version(2, 0),
            TableauDeployments.Both, Idempotent: true, TransferKind.Download)
        { NeedsSession = false };
        await using var download = await retrying.Pipeline.DownloadAsync(content, default, CancellationToken.None);
        Check("a download's name is cleaned and its kind known", download is
        {
            FileName: "Superstore.twbx", Kind: TableauFileKind.PackagedWorkbook, ContentType: "application/octet-stream",
            Length: 1024,
        });
        var saved = Path.Combine(Path.GetTempPath(), $"Tableau.Api.Aot-{Guid.NewGuid():N}.twbx");
        try
        {
            await download.SaveToFileAsync(saved);
            Check("a download streams to a file", File.ReadAllBytes(saved).SequenceEqual(FixtureHandler.Workbook));
        }
        finally
        {
            File.Delete(saved);
        }

        var items = new Endpoint("Items", HttpMethod.Get, "{version}/sites/{siteId}/items", new Version(2, 0), TableauDeployments.Both,
            Idempotent: true, TransferKind.None);
        var options = new TableauClientOptions { ServerUrl = new Uri("https://tableau.example.com/"), ApiVersion = new Version(3, 25), Site = "finance" };
        await using (var signing = new TableauClient(http, options, TableauCredentials.PersonalAccessToken("nightly-export", _ => ValueTask.FromResult("invented-pat-secret"))))
        {
            await signing.Pipeline.SendAsync(items, default, static body => body.ServerInfo, "serverInfo", CancellationToken.None);
            Check("a call that needs a session signs in first and carries its token and site",
                FixtureHandler.Sent[^2] == "POST /api/3.25/auth/signin -"
                && FixtureHandler.Sent[^1] == "GET /api/3.25/sites/0f4b9e1c-3a52-4d6e-9c8b-1a2b3c4d5e6f/items invented-session-token");

            var projects = new List<string>();
            await foreach (var project in signing.Projects.ListAsync(new ProjectListOptions
            {
                Filter = TableauFilter.And(TableauFilter.Eq(ProjectField.TopLevelProject, true), TableauFilter.In(ProjectField.Name, "R&D", "C++")),
                PageSize = 3,
            }))
                projects.Add(project.Name!);
            Check("a list reads its pages until the total, its filter escaped and its sort stable on the wire",
                projects.SequenceEqual(["Default", "Finance", "Quarterly reports", "R&D", "C++ #1 100%"])
                && FixtureHandler.Queries[^2..].SequenceEqual([
                    "?pageSize=3&pageNumber=1&filter=topLevelProject:eq:true,name:in:%5BR%26D,C%2B%2B%5D&sort=createdAt:asc,name:asc",
                    "?pageSize=3&pageNumber=2&filter=topLevelProject:eq:true,name:in:%5BR%26D,C%2B%2B%5D&sort=createdAt:asc,name:asc",
                ]));

            var quarterly = TableauContent.Project("33333333-4444-4555-8666-777777777777");
            var listed = await signing.Permissions.GetAsync(quarterly);
            var granted = await signing.Permissions.AddAsync(quarterly,
            [
                new GranteeCapabilities
                {
                    Group = new Group { Id = "4a1b2c3d-0002-4e5f-8a9b-0c1d2e3f4a5b" },
                    Capabilities = [new Capability { Name = CapabilityName.Read, Mode = CapabilityMode.Allow }],
                },
            ]);
            Check("a project's permissions are listed and added over a TableauContent",
                listed.GranteeCapabilities is [{ Group.Id: "4a1b2c3d-0002-4e5f-8a9b-0c1d2e3f4a5b", Capabilities: [{ Mode.Value: "Allow" }, _] }, _]
                && granted.Project?.Name == "Quarterly reports"
                && FixtureHandler.Sent[^1] == "PUT /api/3.25/sites/0f4b9e1c-3a52-4d6e-9c8b-1a2b3c4d5e6f/projects/33333333-4444-4555-8666-777777777777/permissions invented-session-token");

            await using (var image = await signing.Views.DownloadImageAsync(FixtureHandler.ViewId, new ExportOptions
            {
                Resolution = ImageResolution.High,
                MaxAge = 60,
                Filters = new Dictionary<string, string> { ["Région"] = "Nord, Sud" },
            }))
            {
                using var read = new MemoryStream();
                await image.Content.CopyToAsync(read);
                Check("a view's image exports through ExportOptions, its filter as vf_ and escaped, and streams its PNG",
                    image.Kind == TableauFileKind.Png && read.ToArray().SequenceEqual(FixtureHandler.Png)
                    && FixtureHandler.Queries[^1] == "?resolution=high&maxAge=60&vf_R%C3%A9gion=Nord%2C%20Sud");
            }
            await using (var workbook = await signing.Workbooks.DownloadAsync(FixtureHandler.WorkbookId, includeExtract: false))
            {
                var file = Path.Combine(Path.GetTempPath(), $"Tableau.Api.Aot-{Guid.NewGuid():N}.twbx");
                try
                {
                    await workbook.SaveToFileAsync(file);
                    Check("a workbook downloads through its sub-client without its extract, and saves to a file",
                        workbook is { FileName: "Superstore.twbx", Kind: TableauFileKind.PackagedWorkbook }
                        && File.ReadAllBytes(file).SequenceEqual(FixtureHandler.Workbook) && FixtureHandler.Queries[^1] == "?includeExtract=False");
                }
                finally
                {
                    File.Delete(file);
                }
            }

            const string Databases = "query($first: Int, $after: String) { databasesConnection(first: $first, after: $after) { nodes { id name } "
                + "pageInfo { hasNextPage endCursor } totalCount } }";
            var data = await signing.Metadata.QueryAsync(Databases, null, AotJson.Default.AotDatabasesData);
            var names = new List<string>();
            await foreach (var database in signing.Metadata.ListNodesAsync(Databases, "databasesConnection", AotJson.Default.AotDatabaseNode, pageSize: 2))
                names.Add(database.Name);
            Check("a Metadata API query reads its data through the caller's JsonTypeInfo, and a connection pages by its cursor",
                data.DatabasesConnection is { TotalCount: 3, Nodes.Count: 2 }
                && names.SequenceEqual(["invented-airlines", "invented.xlsx", "invented.json"])
                && FixtureHandler.Paths.Count(path => path == "/api/metadata/graphql") == 3);
        }
        Check("disposing the client signs its session out", FixtureHandler.Sent[^1] == "POST /api/3.25/auth/signout invented-session-token");

        await using var explicitly = new TableauClient(http, new TableauClientOptions
        {
            ServerUrl = new Uri("https://tableau.example.com/"),
            ApiVersion = new Version(3, 25),
        });
        var session = await explicitly.Sessions.SignInAsync(TableauCredentials.ConnectedAppDirectTrust("1c9a5c7e-0a1b-4c2d-8e3f-4a5b6c7d8e9f",
            "invented-secret-id", "invented-connected-app-secret-value", "grace", ["tableau:content:read"]));
        Check("a connected app's JWT is minted, HS256 over its header and claims", FixtureHandler.SignedJwt);
        Check("the session names its site and user, and prints no token", session is { SiteContentUrl: "finance", UserName: "grace" }
            && !session.ToString().Contains("invented-session-token", StringComparison.Ordinal));
        await using (var bound = explicitly.WithSession(session))
        {
            await bound.Pipeline.SendAsync(items, default, static body => body.ServerInfo, "serverInfo", CancellationToken.None);
            await bound.Sessions.SignOutAsync(session);
        }
        Check("a bound client calls in its session and signs it out when told",
            FixtureHandler.Sent[^2].EndsWith("/items invented-session-token", StringComparison.Ordinal)
            && FixtureHandler.Sent[^1] == "POST /api/3.25/auth/signout invented-session-token");
    }

    var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["Tableau:ServerUrl"] = "https://tableau.example.com/tableau/",
        ["Tableau:ApiVersion"] = "3.27",
        ["Tableau:Deployment"] = "Cloud",
        ["Tableau:RequestTimeout"] = "00:00:45",
    }).Build();
    var services = new ServiceCollection();
    var primaryConfigured = false;
    services.AddTableauClient("prod", options => configuration.GetSection("Tableau").Bind(options))
        .ConfigurePrimaryHandler(handler =>
        {
            handler.ConnectTimeout = TimeSpan.FromSeconds(30);
            primaryConfigured = true;
        })
        .AddHttpMessageHandler(_ => new FixtureAnswers());
    services.AddTableauClient("bad", options => options.ServerUrl = new Uri("http://tableau.example.com/"));
    await using (var provider = services.BuildServiceProvider())
    {
        var bound = provider.GetRequiredService<IOptionsMonitor<TableauClientOptions>>().Get("prod");
        Check("the options bind from configuration", bound is { ApiVersion: { Major: 3, Minor: 27 }, Deployment: TableauDeployment.Cloud }
            && bound.RequestTimeout == TimeSpan.FromSeconds(45) && bound.ServerUrl?.AbsolutePath == "/tableau/");
        await using var scope = provider.CreateAsyncScope();
        var prod = scope.ServiceProvider.GetRequiredKeyedService<TableauClient>("prod");
        Check("the keyed client resolves, its primary handler configured", prod is not null && primaryConfigured);
        var answered = await prod!.Server.GetInfoAsync();
        Check("the keyed client calls through a handler of the caller's", answered.ProductVersion?.Value == "2025.1.0"
            && FixtureHandler.Paths[^1] == "/tableau/api/3.27/serverinfo");
        try
        {
            provider.GetRequiredService<IStartupValidator>().Validate();
            Check("bad options are refused at start-up", false);
        }
        catch (OptionsValidationException e)
        {
            Check("bad options are refused at start-up", e.OptionsName == "bad" && e.Message.Contains("plain http", StringComparison.Ordinal));
        }
    }

    Console.WriteLine("All checks passed.");
    return 0;
}
catch (Exception e)
{
    Console.Error.WriteLine($"FAILED: {e}");
    return 1;
}

static string Fixture(string name)
{
    using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"Fixtures/{name}")
        ?? throw new InvalidOperationException($"The fixture {name} is not in the binary.");
    using var reader = new StreamReader(stream);
    return reader.ReadToEnd();
}

static TsResponse Response(string name) =>
    JsonSerializer.Deserialize(Fixture(name), TableauJsonContext.Default.TsResponse)
    ?? throw new InvalidOperationException($"The fixture {name} is null.");

static void Check(string what, bool passed, string? detail = null)
{
    Console.WriteLine($"{(passed ? "ok  " : "FAIL")}  {what}{(passed || detail is null ? "" : $": {detail}")}");
    if (!passed)
        throw new InvalidOperationException($"{what} failed.");
}

/// <summary>
/// Tableau, as the fixtures answer it: Server Info at 3.23, 3.25 and, under a path prefix, 3.27; a call that answers 503
/// once, then Server Info; a workbook's file, its name as Tableau's reference writes it; the error fixture for anything
/// else.
/// </summary>
internal sealed class FixtureHandler : HttpMessageHandler
{
    private static int _flaky;

    public static List<string> Paths { get; } = [];

    /// <summary>Each request's query, as sent.</summary>
    public static List<string> Queries { get; } = [];

    /// <summary>Each request as its method, its path and its session token, or "-" for none.</summary>
    public static List<string> Sent { get; } = [];

    /// <summary>Whether the last sign-in with a JWT sent one signed with the invented connected app's secret.</summary>
    public static bool SignedJwt { get; private set; }

    /// <summary>The workbook's bytes: a zip's signature, then a pattern.</summary>
    public static byte[] Workbook { get; } = [(byte)'P', (byte)'K', 3, 4, .. Enumerable.Range(0, 1020).Select(i => (byte)(i % 251))];

    /// <summary>A view's image: a PNG's signature, then a pattern.</summary>
    public static byte[] Png { get; } = [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A, .. Enumerable.Range(0, 504).Select(i => (byte)(i % 13))];

    /// <summary>The invented view the app exports, and the workbook it downloads.</summary>
    public const string ViewId = "44444444-5555-4666-8777-888888888888";

    public const string WorkbookId = "66666666-7777-4888-8999-aaaaaaaaaaaa";

    private const string Site = "/api/3.25/sites/0f4b9e1c-3a52-4d6e-9c8b-1a2b3c4d5e6f";

    public static HttpResponseMessage Answer(HttpRequestMessage request)
    {
        var path = request.RequestUri!.AbsolutePath;
        Paths.Add(path);
        Queries.Add(request.RequestUri.Query);
        Sent.Add($"{request.Method} {path} {(request.Headers.TryGetValues("X-Tableau-Auth", out var tokens) ? tokens.Single() : "-")}");
        if (path == "/api/3.25/auth/signin")
        {
            using var credentials = JsonDocument.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
            if (credentials.RootElement.GetProperty("credentials").TryGetProperty("jwt", out var jwt))
            {
                var parts = jwt.GetString()!.Split('.');
                var signature = System.Security.Cryptography.HMACSHA256.HashData(Encoding.UTF8.GetBytes("invented-connected-app-secret-value"),
                    Encoding.ASCII.GetBytes(parts[0] + "." + parts[1]));
                SignedJwt = System.Buffers.Text.Base64Url.DecodeFromChars(parts[2]).SequenceEqual(signature);
            }
            return Json(request, HttpStatusCode.OK, Fixture("sign-in.json"));
        }
        if (path == "/api/3.25/auth/signout")
            return new HttpResponseMessage(HttpStatusCode.NoContent) { RequestMessage = request };
        if (path == "/api/3.25/sites/0f4b9e1c-3a52-4d6e-9c8b-1a2b3c4d5e6f/items")
            return Json(request, HttpStatusCode.OK, Fixture("server-info.json"));
        if (path == "/api/3.25/sites/0f4b9e1c-3a52-4d6e-9c8b-1a2b3c4d5e6f/projects/33333333-4444-4555-8666-777777777777/permissions")
            return Json(request, HttpStatusCode.OK, Fixture("permissions.json"));
        if (path == "/api/metadata/graphql")
        {
            // The first page for a query with no cursor, the second after the first page's.
            using var query = JsonDocument.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
            var after = query.RootElement.TryGetProperty("variables", out var variables) && variables.TryGetProperty("after", out var cursor)
                ? cursor.GetString()
                : null;
            return Json(request, HttpStatusCode.OK, Fixture(after is null ? "GraphQL/databases-page-1.json" : "GraphQL/databases-page-2.json"));
        }
        if (path == "/api/3.25/sites/0f4b9e1c-3a52-4d6e-9c8b-1a2b3c4d5e6f/projects")
        {
            return Json(request, HttpStatusCode.OK,
                Fixture(request.RequestUri.Query.Contains("pageNumber=1", StringComparison.Ordinal) ? "projects-page.json" : "projects-last-page.json"));
        }
        if (path == $"{Site}/views/{ViewId}/image")
        {
            var image = new ByteArrayContent(Png);
            image.Headers.TryAddWithoutValidation("Content-Type", "image/png");
            return new HttpResponseMessage(HttpStatusCode.OK) { RequestMessage = request, Content = image };
        }
        if (path is "/api/3.25/workbooks/w1/content" || path == $"{Site}/workbooks/{WorkbookId}/content")
        {
            var file = new ByteArrayContent(Workbook);
            file.Headers.TryAddWithoutValidation("Content-Type", "application/octet-stream");
            file.Headers.TryAddWithoutValidation("Content-Disposition", "name=\"tableau_workbook\"; filename=\"..\\Superstore.twbx\"");
            return new HttpResponseMessage(HttpStatusCode.OK) { RequestMessage = request, Content = file };
        }
        if (path == "/api/3.25/flaky" && Interlocked.Increment(ref _flaky) == 1)
            return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { RequestMessage = request, Headers = { RetryAfter = new(TimeSpan.Zero) } };
        var found = path is "/api/3.23/serverinfo" or "/api/3.25/serverinfo" or "/tableau/api/3.27/serverinfo" or "/api/3.25/flaky";
        return new HttpResponseMessage(found ? HttpStatusCode.OK : HttpStatusCode.NotFound)
        {
            RequestMessage = request,
            Content = new StringContent(Fixture(found ? "server-info.json" : "error.json"), Encoding.UTF8, "application/json"),
        };
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(Answer(request));

    private static HttpResponseMessage Json(HttpRequestMessage request, HttpStatusCode status, string body) =>
        new(status) { RequestMessage = request, Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static string Fixture(string name)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"Fixtures/{name}")!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}

/// <summary>A handler of the caller's, added with AddHttpMessageHandler, that answers as the fixtures do and sends nothing on.</summary>
internal sealed class FixtureAnswers : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(FixtureHandler.Answer(request));
}

/// <summary>A database as the app's Metadata API query asks for it.</summary>
/// <param name="Id">Its LUID.</param>
/// <param name="Name">Its name.</param>
internal sealed record AotDatabaseNode(string Id, string Name);

/// <summary>The data of the app's databases query.</summary>
/// <param name="DatabasesConnection">The connection.</param>
internal sealed record AotDatabasesData(AotDatabasesConnection DatabasesConnection);

/// <summary>A databases connection: its nodes and its total.</summary>
/// <param name="Nodes">The nodes.</param>
/// <param name="TotalCount">How many there are in all.</param>
internal sealed record AotDatabasesConnection(IReadOnlyList<AotDatabaseNode> Nodes, int TotalCount);

/// <summary>The app's own source-generated JSON, as a caller's AOT app would have for its queries.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(AotDatabaseNode))]
[JsonSerializable(typeof(AotDatabasesData))]
internal sealed partial class AotJson : JsonSerializerContext;
