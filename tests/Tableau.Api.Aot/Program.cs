// The library in a native AOT app. CI publishes it with native AOT, which fails on any trim or AOT warning in the 2.0
// types roots.xml keeps (Tableau.Api.Aot.csproj), then runs the binary, which reads and writes Tableau's JSON through
// the generated models as the client will:
//
// - every fixture of the offline tests (tests/Tableau.Api.Tests/Fixtures, inside the binary) is read and written back,
//   and says the same to Tableau;
// - the shapes Tableau's JSON takes are read for real: a token, numbers sent as strings, a list empty, of one and of
//   many, a value this version doesn't know, text beside an attribute, a time;
// - the client calls through its pipeline, over a handler that answers from the fixtures: the version negotiated
//   through Server Info, Server Info itself, and Tableau's error body read into a TableauException;
// - the client from DI, its options bound from configuration by the binder's source generator, and bad options
//   refused at start-up by the options validation source generator's code.
//
// In the native binary reflection-based JSON is off, so a converter that reached for it would fail here. Later steps
// add their paths: a sign-in, a page, a download, a publish, a Vizportal call. It prints a line per check and exits
// with 1 on the first failure.
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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Tableau.Api;
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
    }

    var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["Tableau:ServerUrl"] = "https://tableau.example.com/tableau/",
        ["Tableau:ApiVersion"] = "3.27",
        ["Tableau:Deployment"] = "Cloud",
        ["Tableau:RequestTimeout"] = "00:00:45",
    }).Build();
    var services = new ServiceCollection();
    services.AddTableauClient("prod", options => configuration.GetSection("Tableau").Bind(options));
    services.AddTableauClient("bad", options => options.ServerUrl = new Uri("http://tableau.example.com/"));
    await using (var provider = services.BuildServiceProvider())
    {
        var bound = provider.GetRequiredService<IOptionsMonitor<TableauClientOptions>>().Get("prod");
        Check("the options bind from configuration", bound is { ApiVersion: { Major: 3, Minor: 27 }, Deployment: TableauDeployment.Cloud }
            && bound.RequestTimeout == TimeSpan.FromSeconds(45) && bound.ServerUrl?.AbsolutePath == "/tableau/");
        await using var scope = provider.CreateAsyncScope();
        Check("the keyed client resolves", scope.ServiceProvider.GetRequiredKeyedService<TableauClient>("prod") is not null);
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

/// <summary>Tableau, as the fixtures answer it: Server Info at 3.23 and 3.25, the error fixture for anything else.</summary>
internal sealed class FixtureHandler : HttpMessageHandler
{
    public static List<string> Paths { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri!.AbsolutePath;
        Paths.Add(path);
        var found = path is "/api/3.23/serverinfo" or "/api/3.25/serverinfo";
        return Task.FromResult(new HttpResponseMessage(found ? HttpStatusCode.OK : HttpStatusCode.NotFound)
        {
            RequestMessage = request,
            Content = new StringContent(Fixture(found ? "server-info.json" : "error.json"), Encoding.UTF8, "application/json"),
        });
    }

    private static string Fixture(string name)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"Fixtures/{name}")!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
