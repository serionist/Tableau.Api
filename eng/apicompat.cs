#:property RestorePackagesWithLockFile=false
#:property PublishAot=false
// The API comparison with 1.2.5: dotnet run eng/apicompat.cs -- [--update]
//
// 2.0 breaks 1.2.5's public API on purpose, and every break is listed in eng/apicompat/1.2.5.xml, the ledger that
// docs/migration-2.0.md is written from. Package validation can't list them: across the change of target, from
// netstandard2.0 to net10.0, it only says that netstandard2.0 is gone. So this compares the DLLs themselves, with the
// ApiCompat tool (dotnet-tools.json) in assembly mode:
// - 1.2.5's lib/netstandard2.0/Tableau.Api.dll, from its package on nuget.org, checked against the SHA-512 release/1.x
//   pins for it, with the assemblies it compiles against: netstandard2.0 and Microsoft.Extensions.Logging 2.1.1, as
//   eng/apicompat/Baseline.csproj restores them;
// - the library's Release build, which this builds, with the assemblies it compiles against.
// Without those references the tool can't tell an enum from a class, and misses an enum value that moved (CP0011).
//
// It fails when the build breaks something the ledger doesn't list, when the ledger lists something that is no longer a
// break, and when the ledger is not exactly what the tool writes for the build, such as after an edit by hand. A step
// that breaks more runs it with --update, which rewrites the ledger from the build; the PR reviews what it adds, and
// the migration guide moves with it. Its files go to artifacts/apicompat/, which git ignores.
using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;

const string PackageUrl = "https://api.nuget.org/v3-flatcontainer/tableau.api/1.2.5/tableau.api.1.2.5.nupkg";
const string PackageSha512 = "089f5041c181ff4984f798c817a7bc76a0ab06a05ba13c6cdc178d5c2b3fb0fc"
    + "7bfcbb9ede9a98888a5efe2f195ab7222d79d77e4f6d2a66ead009365258bc50";
// The two sides as the ledger names them: relative to artifacts/apicompat/, where the tool runs, so the ledger holds
// no path of the machine that wrote it.
const string Left = "1.2.5/lib/netstandard2.0/Tableau.Api.dll";
const string Right = "build/lib/net10.0/Tableau.Api.dll";

if (args is not ([] or ["--update"]))
{
    Console.Error.WriteLine("Usage: dotnet run eng/apicompat.cs -- [--update]");
    return 2;
}
var update = args.Length == 1;

var root = Path.GetFullPath(Path.Combine(
    AppContext.GetData("EntryPointFileDirectoryPath") as string ?? Directory.GetCurrentDirectory(), ".."));
var ledger = Path.Combine(root, "eng", "apicompat", "1.2.5.xml");
var work = Path.Combine(root, "artifacts", "apicompat");
Directory.CreateDirectory(work);

// 1.2.5's DLL, from the package checked by its hash every run, whether just downloaded or kept from the last.
var package = Path.Combine(work, "tableau.api.1.2.5.nupkg");
if (!File.Exists(package))
{
    Console.WriteLine($"Downloading {PackageUrl}");
    using var http = new HttpClient();
    await File.WriteAllBytesAsync(package, await http.GetByteArrayAsync(new Uri(PackageUrl)));
}
var hash = Convert.ToHexStringLower(SHA512.HashData(await File.ReadAllBytesAsync(package)));
if (hash != PackageSha512)
{
    File.Delete(package);
    Console.Error.WriteLine($"FAIL 1.2.5's package has the SHA-512 {hash}, not the one nuget.org lists; deleted it.");
    return 1;
}
using (var zip = ZipFile.OpenRead(package))
{
    var entry = zip.GetEntry("lib/netstandard2.0/Tableau.Api.dll")
        ?? throw new InvalidDataException("1.2.5's package has no lib/netstandard2.0/Tableau.Api.dll");
    Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(work, Left))!);
    entry.ExtractToFile(Path.Combine(work, Left), overwrite: true);
}

// The assemblies each side compiles against, as MSBuild resolves them, and the build itself.
var baseline = MsBuild(root, ["eng/apicompat/Baseline.csproj", "-restore", "-t:ResolveAssemblyReferences",
    "-getItem:ReferencePath"]);
var build = MsBuild(root, ["src/Tableau.Api/Tableau.Api.csproj", "-restore", "-t:Build", "-p:Configuration=Release",
    "-getProperty:TargetPath", "-getItem:ReferencePath"]);
if (baseline is null || build is null) return 1;
var targetPath = build.RootElement.GetProperty("Properties").GetProperty("TargetPath").GetString()
    ?? throw new InvalidDataException("MSBuild gave no TargetPath for the library");
Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(work, Right))!);
File.Copy(targetPath, Path.Combine(work, Right), overwrite: true);

if (Run(root, "dotnet", ["tool", "restore"]) is { ExitCode: not 0 } restore)
{
    Console.Error.WriteLine(restore.Output);
    Console.Error.WriteLine("FAIL dotnet tool restore failed");
    return 1;
}
string[] compare =
[
    "tool", "run", "apicompat", "--", "--left", Left, "--right", Right,
    "--left-assembly-references", References(baseline), "--right-assembly-references", References(build),
    "--enable-rule-cannot-change-parameter-name",
];

if (update)
{
    var written = Run(work, "dotnet", [.. compare, "--generate-suppression-file", "--suppression-output-file", ledger]);
    if (written.ExitCode != 0 || Incomplete(written.Output))
    {
        Console.Error.WriteLine(written.Output);
        Console.Error.WriteLine("FAIL the ledger was not written");
        return 1;
    }
    File.WriteAllText(ledger, Normalized(File.ReadAllText(ledger)), new UTF8Encoding(false));
    Console.WriteLine($"Wrote eng/apicompat/1.2.5.xml: {Summary(ledger)}. Review what it adds, and move "
        + "docs/migration-2.0.md with it.");
    return 0;
}

var failures = new List<string>();
// The tool's own verdict on the build, with the ledger as its suppressions: it fails on a break the ledger doesn't
// list, and on an entry that no longer matches a break.
var checkedRun = Run(work, "dotnet", [.. compare, "--suppression-file", ledger]);
Console.WriteLine(checkedRun.Output);
if (checkedRun.ExitCode != 0) failures.Add("the build and the ledger disagree: see the tool's messages above");
if (Incomplete(checkedRun.Output)) failures.Add("the tool couldn't load a reference assembly, so its comparison is incomplete");

// The ledger as the tool writes it for this build, to the byte apart from line endings and a byte order mark.
var fresh = Path.Combine(work, "1.2.5.xml");
var regenerated = Run(work, "dotnet", [.. compare, "--generate-suppression-file", "--suppression-output-file", fresh]);
if (regenerated.ExitCode != 0 || !File.Exists(fresh))
{
    failures.Add("the tool could not write the ledger for this build");
}
else if (Normalized(File.ReadAllText(fresh)) != Normalized(File.ReadAllText(ledger)))
{
    var (wanted, held) = (Entries(fresh), Entries(ledger));
    failures.Add("the ledger is not what the tool writes for this build: run dotnet run eng/apicompat.cs -- --update"
        + string.Concat(wanted.Except(held).Select(entry => $"\n  not in the ledger: {entry}"))
        + string.Concat(held.Except(wanted).Select(entry => $"\n  not a break of this build: {entry}")));
}

if (failures.Count > 0)
{
    failures.ForEach(failure => Console.Error.WriteLine($"FAIL {failure}"));
    return 1;
}
Console.WriteLine($"ApiCompat against 1.2.5 passed: {Summary(ledger)}, each in eng/apicompat/1.2.5.xml.");
return 0;

// MSBuild's JSON for -getProperty and -getItem, or null after printing why it failed.
static JsonDocument? MsBuild(string root, string[] arguments)
{
    var done = Run(root, "dotnet", ["msbuild", .. arguments]);
    var json = done.Stdout.IndexOf('{', StringComparison.Ordinal);
    if (done.ExitCode == 0 && json >= 0)
    {
        try
        {
            return JsonDocument.Parse(done.Stdout[json..]);
        }
        catch (JsonException)
        {
            // Not MSBuild's JSON after all: printed below.
        }
    }
    Console.Error.WriteLine(done.Output);
    Console.Error.WriteLine($"FAIL dotnet msbuild {arguments[0]} failed");
    return null;
}

// The folders of the assemblies a project compiles against, as the tool takes them.
static string References(JsonDocument msbuild) => string.Join(',', msbuild.RootElement.GetProperty("Items")
    .GetProperty("ReferencePath").EnumerateArray()
    .Select(item => Path.GetDirectoryName(item.GetProperty("FullPath").GetString()!)!)
    .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal));

// A reference folder or assembly the tool can't find is only a warning, and the comparison then passes without it.
static bool Incomplete(string output) => output.Contains("Could not", StringComparison.Ordinal);

static string Normalized(string text) => text.TrimStart('﻿').ReplaceLineEndings("\n");

// Each suppression as one line: its diagnostic and its target.
static List<string> Entries(string path) =>
[
    .. XDocument.Load(path).Root!.Elements().Where(e => e.Name.LocalName == "Suppression")
        .Select(e => $"{Value(e, "DiagnosticId")} {Value(e, "Target")}"),
];

static string Value(XElement element, string name) =>
    element.Elements().FirstOrDefault(e => e.Name.LocalName == name)?.Value ?? "";

// How many breaks the ledger lists, by diagnostic.
static string Summary(string path)
{
    var entries = Entries(path);
    var kinds = entries.GroupBy(entry => entry.Split(' ')[0], StringComparer.Ordinal)
        .OrderBy(group => group.Key, StringComparer.Ordinal).Select(group => $"{group.Key} {group.Count()}");
    return $"{entries.Count} breaks ({string.Join(", ", kinds)})";
}

// A process with a fixed argument list and no shell, its output and errors read as UTF-8.
static Done Run(string directory, string file, string[] arguments)
{
    var start = new ProcessStartInfo(file, arguments)
    {
        WorkingDirectory = directory,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        StandardOutputEncoding = Encoding.UTF8,
        StandardErrorEncoding = Encoding.UTF8,
    };
    start.Environment["DOTNET_CLI_UI_LANGUAGE"] = "en";
    using var process = Process.Start(start) ?? throw new InvalidOperationException($"{file} did not start");
    var errors = process.StandardError.ReadToEndAsync();
    var output = process.StandardOutput.ReadToEnd();
    process.WaitForExit();
    return new Done(process.ExitCode, output, errors.Result);
}

internal sealed record Done(int ExitCode, string Stdout, string Stderr)
{
    public string Output => (Stdout + Stderr).Trim();
}
