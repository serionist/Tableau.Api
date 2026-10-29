#:property RestorePackagesWithLockFile=false
#:property PublishAot=false
// The package check: dotnet run eng/package-check.cs -- <folder with the packed package> [<version>]
//
// What a pack of the library must hold before it may go to nuget.org. CI runs it on every pack; a release runs it on
// what it is about to push, with the tag's version. It fails unless the folder holds one Tableau.Api .nupkg and its
// .snupkg, nothing else, at <version> when one is given and at a version nuget.org accepts, and the package has:
// - its licence as the expression MIT;
// - a readme, packed into it;
// - its icon packed into it, when it names one;
// - the repository's URL, and the commit this clone has checked out as the commit it was built from;
// - a description that ends with the disclaimer;
// - symbols for each assembly in the .snupkg, and no source files in either package (Source Link points the symbols
//   at GitHub instead).
using System.Diagnostics;
using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Xml.Linq;

const string Id = "Tableau.Api";
const string Repository = "https://github.com/serionist/Tableau.Api";
const string Disclaimer = "Not affiliated with or endorsed by Tableau or Salesforce.";

if (args.Length is < 1 or > 2)
{
    Console.Error.WriteLine("Usage: dotnet run eng/package-check.cs -- <packages folder> [<version>]");
    return 2;
}
var folder = args[0];
var expectedVersion = args.Length == 2 ? args[1] : null;
if (!Directory.Exists(folder))
{
    Console.Error.WriteLine($"{folder} is not a folder.");
    return 2;
}

var failures = new List<string>();
var packages = Directory.GetFiles(folder, "*.nupkg").Order(StringComparer.Ordinal).ToList();
var symbols = Directory.GetFiles(folder, "*.snupkg").Order(StringComparer.Ordinal).ToList();
if (packages.Count != 1 || symbols.Count != 1)
{
    Console.Error.WriteLine($"FAIL {folder} holds {packages.Count} .nupkg and {symbols.Count} .snupkg files, not one of each: "
        + string.Join(", ", packages.Concat(symbols).Select(Path.GetFileName)));
    return 1;
}

using var package = ZipFile.OpenRead(packages[0]);
var metadata = Metadata(package, packages[0]);
var id = Value(metadata, "id") ?? "";
var version = Value(metadata, "version") ?? "";
Console.WriteLine($"{Path.GetFileName(packages[0])}: {id} {version}");

if (id != Id) failures.Add($"the package is {id}, not {Id}");
if (expectedVersion is not null && !string.Equals(version, expectedVersion, StringComparison.OrdinalIgnoreCase))
    failures.Add($"the package is at {version}, not {expectedVersion}");
if (GalleryRefusal(version) is { } refusal) failures.Add($"nuget.org refuses the version {version}: {refusal}");

var license = metadata.Elements().FirstOrDefault(e => e.Name.LocalName == "license");
if (license is null || (string?)license.Attribute("type") != "expression" || license.Value != "MIT")
    failures.Add($"the licence is not the expression MIT: {license?.ToString() ?? "none"}");

if (Value(metadata, "readme") is not { Length: > 0 } readme) failures.Add("the nuspec names no readme");
else if (Entry(package, readme) is not { Length: > 0 }) failures.Add($"the readme {readme} is not in the package, or is empty");

if (Value(metadata, "icon") is { Length: > 0 } icon && Entry(package, icon) is not { Length: > 0 })
    failures.Add($"the icon {icon} is not in the package, or is empty");

var repository = metadata.Elements().FirstOrDefault(e => e.Name.LocalName == "repository");
var url = (string?)repository?.Attribute("url");
var commit = (string?)repository?.Attribute("commit") ?? "";
if (url != Repository) failures.Add($"the repository URL is {url ?? "missing"}, not {Repository}");
var head = Head();
if (head is null) failures.Add("git can't say which commit is checked out here: run the check in the clone that packed it");
else if (commit != head) failures.Add($"the package names the commit {(commit.Length > 0 ? commit : "none")}, but {head} is checked out");

var description = (Value(metadata, "description") ?? "").Trim();
if (!description.EndsWith(Disclaimer, StringComparison.Ordinal))
    failures.Add($"the description does not end with \"{Disclaimer}\": {description}");

using var symbolPackage = ZipFile.OpenRead(symbols[0]);
var symbolMetadata = Metadata(symbolPackage, symbols[0]);
if (Path.GetFileName(symbols[0]) != $"{id}.{version}.snupkg"
    || Value(symbolMetadata, "id") != id || Value(symbolMetadata, "version") != version)
    failures.Add($"{Path.GetFileName(symbols[0])} is not the symbol package of {id} {version}");
foreach (var assembly in package.Entries.Where(e => e.FullName.StartsWith("lib/", StringComparison.Ordinal)
             && e.FullName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)))
{
    var pdb = Path.ChangeExtension(assembly.FullName, ".pdb");
    if (Entry(symbolPackage, pdb) is not { Length: > 0 }) failures.Add($"{assembly.FullName} has no {pdb} in the symbol package");
}

foreach (var (zip, name) in new[] { (package, packages[0]), (symbolPackage, symbols[0]) })
{
    var sources = zip.Entries.Where(e => e.FullName.StartsWith("src/", StringComparison.OrdinalIgnoreCase)
        || e.FullName.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)).Select(e => e.FullName).ToList();
    if (sources.Count > 0)
        failures.Add($"{Path.GetFileName(name)} carries source files (IncludeSource): {string.Join(", ", sources.Take(5))}");
}

if (failures.Count > 0)
{
    Console.Error.WriteLine();
    failures.ForEach(f => Console.Error.WriteLine("FAIL " + f));
    return 1;
}
Console.WriteLine($"Package check passed: {id} {version}, MIT, readme, repository {url} at {commit[..10]}, symbols, no sources.");
return 0;

static XElement Metadata(ZipArchive zip, string file)
{
    var entry = zip.Entries.SingleOrDefault(e => !e.FullName.Contains('/', StringComparison.Ordinal)
        && e.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidDataException($"{file} has no nuspec");
    using var stream = entry.Open();
    return XDocument.Load(stream).Root?.Elements().FirstOrDefault(e => e.Name.LocalName == "metadata")
        ?? throw new InvalidDataException($"{file} has no nuspec metadata");
}

static string? Value(XElement metadata, string name) =>
    metadata.Elements().FirstOrDefault(e => e.Name.LocalName == name)?.Value;

// A file in a package, by the path its nuspec gives (either slash), or null.
static ZipArchiveEntry? Entry(ZipArchive zip, string path)
{
    var wanted = path.Replace('\\', '/').TrimStart('/');
    return zip.Entries.FirstOrDefault(e => string.Equals(e.FullName, wanted, StringComparison.OrdinalIgnoreCase));
}

// The commit checked out where the check runs, which is the one the pack read, or null when git can't say.
static string? Head()
{
    try
    {
        using var git = Process.Start(new ProcessStartInfo("git", ["rev-parse", "--verify", "HEAD"])
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        })!;
        var output = git.StandardOutput.ReadToEnd().Trim();
        git.StandardError.ReadToEnd();
        git.WaitForExit();
        return git.ExitCode == 0 && output.Length is 40 or 64 ? output : null;
    }
    catch (System.ComponentModel.Win32Exception)
    {
        return null;
    }
}

// Why nuget.org would refuse a version, or null. Its ManifestValidator takes a SemVer 2 version (a dotted prerelease
// label, or build metadata), and any other only if NuGet 2.x clients can read it: up to four numbers and at most one
// prerelease label that starts with a letter (NuGetGallery's NuGetVersionExtensions.IsValidVersionForLegacyClients).
static string? GalleryRefusal(string version)
{
    var dash = version.IndexOf('-', StringComparison.Ordinal);
    var label = dash < 0 ? "" : version[(dash + 1)..];
    if (label.Contains('.', StringComparison.Ordinal) || version.Contains('+', StringComparison.Ordinal))
        return null;
    return Gallery.LegacyVersion().IsMatch(version)
        ? null
        : "neither SemVer 2 nor readable by NuGet 2.x clients (a single prerelease label must start with a letter)";
}

// NuGetGallery's SemanticVersionRegex: the versions NuGet 2.x clients read.
internal static partial class Gallery
{
    [GeneratedRegex(@"^\d+(\s*\.\s*\d+){0,3}(-[a-z][0-9a-z-]*)?$", RegexOptions.IgnoreCase)]
    public static partial Regex LegacyVersion();
}
