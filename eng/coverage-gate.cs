#:property RestorePackagesWithLockFile=false
#:property PublishAot=false
// The coverage gate: dotnet run eng/coverage-gate.cs -- [<folder with *.cobertura.xml reports>] [--base <ref>]
//
// Every project under src/ has a floor in eng/coverage.json: the least line and branch coverage the offline tests must
// reach, in whole percent. The gate reads every Cobertura report under the folder (artifacts/TestResults by default),
// keeps the best figures of each assembly across them, and fails when an assembly is below its floor, has no floor, or
// is in no report. The floors only rise: it also fails when a floor is lower than the one eng/coverage.json holds at
// the base, origin/main unless --base names another commit. It prints the floors the figures beat by a whole point, so
// they can be raised.
//
// It runs from anywhere inside the repo, which it finds by Tableau.Api.slnx. CI runs it after the tests on every leg,
// and the gate's full lane after its tests check (AGENTS.md, Build and test).
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml;

const string FloorsPath = "eng/coverage.json";
const string Usage = "Usage: dotnet run eng/coverage-gate.cs -- [<reports folder>] [--base <ref>]";

var reportsFolder = "artifacts/TestResults";
var baseRef = "origin/main";
var positional = 0;
for (var i = 0; i < args.Length; i++)
{
    if (args[i] == "--base" && i + 1 < args.Length) baseRef = args[++i];
    else if (!args[i].StartsWith('-') && positional++ == 0) reportsFolder = args[i];
    else
    {
        Console.Error.WriteLine(Usage);
        return 2;
    }
}

var repoRoot = FindRepoRoot(Directory.GetCurrentDirectory());
var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
var floors = JsonSerializer.Deserialize<Dictionary<string, Floor>>(
        File.ReadAllText(Path.Combine(repoRoot, FloorsPath)), options)
    ?? throw new InvalidDataException($"{FloorsPath} is empty");
var assemblies = Directory.GetDirectories(Path.Combine(repoRoot, "src")).Select(Path.GetFileName).OfType<string>()
    .Where(name => File.Exists(Path.Combine(repoRoot, "src", name, name + ".csproj")))
    .Order(StringComparer.Ordinal).ToList();

var failures = new List<string>();
foreach (var (assembly, floor) in floors)
{
    if (floor.Lines is < 0 or > 100 || floor.Branches is < 0 or > 100)
        failures.Add($"{FloorsPath}: {assembly}'s floors are whole percents from 0 to 100");
}

// The floors at the base. A base without the file has none yet; a base git can't find fails, since a floor could
// otherwise fall without anything noticing.
var baseFloors = new Dictionary<string, Floor>(StringComparer.Ordinal);
if (Git(repoRoot, "rev-parse", "--verify", "--quiet", $"{baseRef}^{{commit}}").ExitCode != 0)
{
    failures.Add($"can't find {baseRef} to compare the floors with: git fetch origin main:refs/remotes/origin/main, "
        + "or name another commit with --base");
}
else if (Git(repoRoot, "cat-file", "-e", $"{baseRef}:{FloorsPath}").ExitCode != 0)
    Console.WriteLine($"{baseRef} has no {FloorsPath}, so no floor there to keep.");
else
{
    var (exitCode, shown) = Git(repoRoot, "show", $"{baseRef}:{FloorsPath}");
    if (exitCode != 0) failures.Add($"can't read {FloorsPath} at {baseRef}");
    else baseFloors = JsonSerializer.Deserialize<Dictionary<string, Floor>>(shown, options) ?? baseFloors;
}
foreach (var (assembly, kept) in baseFloors)
{
    if (!floors.TryGetValue(assembly, out var floor))
    {
        if (assemblies.Contains(assembly, StringComparer.Ordinal))
            failures.Add($"{assembly} has a floor at {baseRef} and none in {FloorsPath}");
        continue;
    }
    if (floor.Lines < kept.Lines)
    {
        failures.Add($"{assembly}: the line floor of {floor.Lines}% is lower than {baseRef}'s {kept.Lines}%; "
            + "floors only rise");
    }
    if (floor.Branches < kept.Branches)
    {
        failures.Add($"{assembly}: the branch floor of {floor.Branches}% is lower than {baseRef}'s {kept.Branches}%; "
            + "floors only rise");
    }
}

var reports = Directory.Exists(reportsFolder)
    ? Directory.GetFiles(reportsFolder, "*.cobertura.xml", SearchOption.AllDirectories)
    : [];
if (reports.Length == 0)
{
    failures.Add($"no *.cobertura.xml report under {reportsFolder}: "
        + "run the tests with --coverage first (AGENTS.md, Build and test)");
    return Report(failures, []);
}

var figures = new Dictionary<string, Figures>(StringComparer.Ordinal);
foreach (var report in reports)
{
    using var reader = XmlReader.Create(report, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit });
    while (reader.Read())
    {
        if (reader.NodeType != XmlNodeType.Element || reader.Name != "package") continue;
        var name = reader.GetAttribute("name") ?? "";
        var read = new Figures(Rate(reader, "line-rate"), Rate(reader, "branch-rate"));
        figures[name] = figures.TryGetValue(name, out var seen)
            ? new Figures(Math.Max(seen.Lines, read.Lines), Math.Max(seen.Branches, read.Branches))
            : read;
    }
}

var raises = new List<string>();
Console.WriteLine($"{"Assembly",-30} {"Lines",9} {"Floor",6} {"Branches",10} {"Floor",6}");
foreach (var assembly in assemblies)
{
    if (!floors.TryGetValue(assembly, out var floor))
    {
        failures.Add($"{assembly} has no floor in {FloorsPath}");
        continue;
    }
    if (!figures.TryGetValue(assembly, out var measured))
    {
        failures.Add($"{assembly} is in no coverage report: no test project measured it");
        continue;
    }
    Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
        $"{assembly,-30} {measured.Lines,8:F2}% {floor.Lines,5}% {measured.Branches,9:F2}% {floor.Branches,5}%"));
    // A rate read back from the report can fall a hair under the whole percent it rounds to.
    if (measured.Lines + 1e-9 < floor.Lines)
    {
        failures.Add(string.Create(CultureInfo.InvariantCulture,
            $"{assembly}: lines {measured.Lines:F2}% is below its floor of {floor.Lines}%"));
    }
    if (measured.Branches + 1e-9 < floor.Branches)
    {
        failures.Add(string.Create(CultureInfo.InvariantCulture,
            $"{assembly}: branches {measured.Branches:F2}% is below its floor of {floor.Branches}%"));
    }
    var (lines, branches) = ((int)(measured.Lines + 1e-9), (int)(measured.Branches + 1e-9));
    if (lines > floor.Lines || branches > floor.Branches)
    {
        raises.Add($"  \"{assembly}\": {{ \"lines\": {Math.Max(floor.Lines, lines)}, "
            + $"\"branches\": {Math.Max(floor.Branches, branches)} }}");
    }
}
foreach (var stale in floors.Keys.Except(assemblies, StringComparer.Ordinal))
    failures.Add($"{FloorsPath} has a floor for {stale}, which is no project under src/");
return Report(failures, raises);

static int Report(List<string> failures, List<string> raises)
{
    if (raises.Count > 0)
    {
        Console.WriteLine();
        Console.WriteLine($"Floors that can rise, to the whole points under the figures, in {FloorsPath}:");
        raises.ForEach(Console.WriteLine);
    }
    if (failures.Count > 0)
    {
        Console.Error.WriteLine();
        failures.ForEach(failure => Console.Error.WriteLine("FAIL " + failure));
        return 1;
    }
    Console.WriteLine();
    Console.WriteLine("Coverage gate passed.");
    return 0;
}

static double Rate(XmlReader reader, string attribute) =>
    double.TryParse(reader.GetAttribute(attribute), NumberStyles.Float, CultureInfo.InvariantCulture, out var rate)
    && rate is >= 0 and <= 1
        ? rate * 100
        : throw new InvalidDataException($"package {reader.GetAttribute("name")} has no {attribute} between 0 and 1");

static string FindRepoRoot(string from)
{
    for (var dir = new DirectoryInfo(from); dir != null; dir = dir.Parent)
    {
        if (File.Exists(Path.Combine(dir.FullName, "Tableau.Api.slnx"))) return dir.FullName;
    }
    throw new DirectoryNotFoundException($"Run the coverage gate inside the repo: no Tableau.Api.slnx above {from}");
}

// git's own messages are dropped: the caller says what a failure means.
static (int ExitCode, string Output) Git(string cwd, params string[] arguments)
{
    var start = new ProcessStartInfo("git")
    {
        WorkingDirectory = cwd,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        StandardOutputEncoding = Encoding.UTF8,
    };
    foreach (var argument in arguments) start.ArgumentList.Add(argument);
    using var git = Process.Start(start) ?? throw new InvalidOperationException("git did not start");
    var output = git.StandardOutput.ReadToEndAsync();
    _ = git.StandardError.ReadToEnd();
    git.WaitForExit();
    return (git.ExitCode, output.Result);
}

internal sealed record Floor([property: JsonRequired] int Lines, [property: JsonRequired] int Branches);

internal sealed record Figures(double Lines, double Branches);
