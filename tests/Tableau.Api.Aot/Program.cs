// The library in a native AOT app. CI publishes it with native AOT, which fails on any trim or AOT warning in the 2.0
// types roots.xml keeps (Tableau.Api.Aot.csproj), then runs the binary, which reads and writes Tableau's JSON through
// the generated models as the client will:
//
// - every fixture of the offline tests (tests/Tableau.Api.Tests/Fixtures, inside the binary) is read and written back,
//   and says the same to Tableau;
// - the shapes Tableau's JSON takes are read for real: a token, numbers sent as strings, a list empty, of one and of
//   many, a value this version doesn't know, text beside an attribute, a time.
//
// In the native binary reflection-based JSON is off, so a converter that reached for it would fail here. Later steps
// add their paths: a sign-in, a page, a download, a publish, an error, a Vizportal call. It prints a line per check
// and exits with 1 on the first failure.
using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
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
