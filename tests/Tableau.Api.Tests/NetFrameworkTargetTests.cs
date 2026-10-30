#if NETFRAMEWORK
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Tableau.Api.Models;
using Tableau.Api.Tests.Fakes;

namespace Tableau.Api.Tests;

/// <summary>
/// The path rows on .NET Framework as apps that target older versions get it. An app's target decides how
/// <see cref="Uri"/> parses, so each row runs in an AppDomain whose target is 4.0, 4.6.1 or 4.8. On each, a row is
/// sent as exactly its escaped path, or refused before anything is sent. A refusal is allowed only where 1.2.5's own
/// buildUri, run on the same value in the same domain, made a path that names another resource, or for a value made
/// only of dots, which is refused on every target. So a call 1.2.5 sent to its resource never starts to throw, except
/// one whose value is only dots: three or more of them, since 1.2.5 sent neither "." nor ".." to its resource.
/// </summary>
[TestClass]
public sealed class NetFrameworkTargetTests
{
    private const string DatasourcesPath = "/api/" + Fixtures.ApiVersion + "/sites/" + Fixtures.SiteId + "/datasources/";

    public TestContext TestContext { get; set; } = null!;

    // Each target's parsing, shown by two harmless differences: whether an escaped unreserved character is unescaped
    // (from 4.5), and whether an escaped parenthesis beside a non-ASCII character is unescaped (4.5 to 4.7.1).
    [TestMethod]
    [DataRow("4.0", "/a%7E", "/%C3%A9%28")]
    [DataRow("4.6.1", "/a~", "/%C3%A9(")]
    [DataRow("4.8", "/a~", "/%C3%A9%28")]
    public void EveryPathRowIsSentExactlyOrRefusedWhere125NamedAnotherPath(string target, string unreserved,
        string parenthesis)
    {
        using var server = FakeTableauServer.Start();
        using var domain = new TargetDomain(target);
        var runner = domain.Runner;
        Assert.AreEqual(".NETFramework,Version=v" + target, runner.TargetFramework);
        Assert.AreEqual(unreserved, runner.PathOf("http://h/a%7E"), "the domain does not parse as its target does");
        Assert.AreEqual(parenthesis, runner.PathOf("http://h/%C3%A9%28"), "the domain does not parse as its target does");
        runner.Start(server.BaseUrl, Fixtures.ApiVersion, Fixtures.SiteId, Fixtures.SessionToken);

        var failures = new List<string>();
        var refusedByCheck = new List<string>();
        var dotsReached125 = new List<string>();
        var rows = 0;
        var sentExactly = 0;
        foreach (var value in PathRows.All())
        {
            rows++;
            var path = DatasourcesPath + PathRows.Escaped(value);
            server.Map("GET", path, FakeResponse.Xml(200, "<datasource id=\"d1\" name=\"Sales\"/>"));
            var before = server.Requests.Count;
            var thrown = runner.QueryDataSource(value);
            var sent = server.Requests.Skip(before).Select(request => request.RequestLine).ToList();
            var path125 = runner.PathFrom125($"sites/{Fixtures.SiteId}/datasources/{value}");
            string[] named = ["", "api", Fixtures.ApiVersion, "sites", Fixtures.SiteId, "datasources", value];
            var reached125 = path125 is not null && PathRows.Segments(path125).SequenceEqual(named);

            if (PathRows.IsRefused(value))
            {
                // Refused on every target, whatever 1.2.5 did. 1.2.5's own argument check refused an empty value before
                // it built a path, so only values of dots count as ones 1.2.5 may have sent to their resource.
                if (thrown != nameof(ArgumentException) || sent.Count != 0)
                    failures.Add($"{PathRows.Show(value)}: expected ArgumentException and no request; {thrown ?? "returned"}, {sent.Count} sent");
                else if (reached125 && value.Length > 0)
                    dotsReached125.Add(value);
            }
            else if (thrown is null && sent.Count == 1 && sent[0] == $"GET {path} HTTP/1.1")
            {
                sentExactly++;
            }
            else if (thrown == nameof(ArgumentException) && sent.Count == 0 && !reached125 && !PathRows.IsSubDelimiterRow(value))
            {
                refusedByCheck.Add($"{PathRows.Show(value)} (1.2.5: {path125 ?? "no URI"})");
            }
            else
            {
                failures.Add($"{PathRows.Show(value)}: {thrown ?? "returned"}, sent {string.Join(" ", sent)}; expected GET {path}; "
                    + $"1.2.5 made {path125 ?? "no URI"}, which {(reached125 ? "names" : "does not name")} the value");
            }
        }

        TestContext.WriteLine($".NET Framework {target}: {rows} rows; {sentExactly} sent exactly; "
            + $"{PathRows.DotOnly().Count()} refused as empty or dots; {refusedByCheck.Count} refused by the path check, "
            + "each where 1.2.5 made a path naming another resource:");
        foreach (var row in refusedByCheck) TestContext.WriteLine("  " + row);
        var dots = string.Join(", ", dotsReached125.Select(PathRows.Show));
        TestContext.WriteLine("Refused though 1.2.5 sent them to their resource: " + dots);
        Assert.IsEmpty(failures, $"{failures.Count} of {rows} rows:\n" + string.Join("\n", failures));
        Assert.IsTrue(dotsReached125.All(value => value.Length >= 3), "1.2.5 sent \".\" or \"..\" to its resource: " + dots);
    }

    // Query values as each target sends them: a view filter value with a colon and a comma, and a filter expression,
    // each beside a non-ASCII character. The path check covers paths, not queries, so each target's outcome is recorded
    // here; the changelog lists what targets before 4.7.2 do as a known issue.
    [TestMethod]
    [DataRow("4.0", "vf_R%C3%A9gion=C%C3%B4te%3A%20Nord%2C%20Sud", "name:eq:%C3%89quipe%20%28Nord%29")]
    [DataRow("4.6.1", "vf_R%C3%A9gion=C%C3%B4te:%20Nord%2C%20Sud", "name:eq:%C3%89quipe%20(Nord)")]
    [DataRow("4.8", "vf_R%C3%A9gion=C%C3%B4te%3A%20Nord%2C%20Sud", "name:eq:%C3%89quipe%20%28Nord%29")]
    public void QueryValuesBesideNonAsciiAreSentAsEachTargetParsesThem(string target, string viewFilter, string filter)
    {
        using var server = FakeTableauServer.Start();
        using var domain = new TargetDomain(target);
        var runner = domain.Runner;
        runner.Start(server.BaseUrl, Fixtures.ApiVersion, Fixtures.SiteId, Fixtures.SessionToken);
        var sitePath = "/api/" + Fixtures.ApiVersion + "/sites/" + Fixtures.SiteId;
        server.Map("GET", sitePath + "/views/v1/data", FakeResponse.Text(200, "Region,Sales\n"));
        server.Map("GET", sitePath + "/datasources", FakeResponse.Xml(200,
            "<pagination pageNumber=\"1\" pageSize=\"100\" totalAvailable=\"0\"/><datasources/>"));

        Assert.IsNull(runner.QueryViewData("v1", "Région", "Côte: Nord, Sud"));
        Assert.IsNull(runner.QueryDataSources("name:eq:Équipe (Nord)"));

        CollectionAssert.AreEqual(new[] { viewFilter, "pageSize=100&pageNumber=1&filter=" + filter },
            server.Requests.Select(request => request.Query).ToArray());
    }

    /// <summary>An AppDomain whose target is the .NET Framework version named, with a runner in it.</summary>
    private sealed class TargetDomain : IDisposable
    {
        private readonly AppDomain _domain;

        public TargetDomain(string version)
        {
            var setup = new AppDomainSetup
            {
                ApplicationBase = AppDomain.CurrentDomain.BaseDirectory,
                ConfigurationFile = AppDomain.CurrentDomain.SetupInformation.ConfigurationFile,
                TargetFrameworkName = ".NETFramework,Version=v" + version,
            };
            _domain = AppDomain.CreateDomain($"Tableau.Api on .NET Framework {version}", null, setup);
            Runner = (TargetRunner)_domain.CreateInstanceAndUnwrap(typeof(TargetRunner).Assembly.FullName,
                typeof(TargetRunner).FullName!);
        }

        public TargetRunner Runner { get; }

        public void Dispose() => AppDomain.Unload(_domain);
    }
}

/// <summary>Runs in a target's AppDomain: the library as built, and 1.2.5's buildUri from its own assembly.</summary>
public sealed class TargetRunner : MarshalByRefObject
{
    private TableauApiClient? _client;
    private TableauSession? _session;
    private object? _client125;
    private MethodInfo? _buildUri125;
    private Array? _noQuery125;

    public string TargetFramework => AppDomain.CurrentDomain.SetupInformation.TargetFrameworkName;

    public override object? InitializeLifetimeService() => null;

    public void Start(Uri baseUrl, string apiVersion, string siteId, string token)
    {
        _client = new TableauApiClient(baseUrl, apiVersion) { Timeout = TimeSpan.FromSeconds(30) };
        _session = new TableauSession { SiteId = siteId, Token = token };
        var assembly = Assembly.LoadFile(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "v1.2.5", "Tableau.Api.dll"));
        var type = assembly.GetType("Tableau.Api.TableauApiClient", throwOnError: true)!;
        Assert.AreEqual(new Version(1, 0, 2, 5), assembly.GetName().Version, "not 1.2.5's assembly");
        _client125 = Activator.CreateInstance(type, baseUrl, apiVersion);
        _buildUri125 = type.GetMethod("buildUri", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(type.FullName, "buildUri");
        _noQuery125 = Array.CreateInstance(_buildUri125.GetParameters()[1].ParameterType.GetElementType()!, 0);
    }

    /// <summary>The escaped path of uri, as this domain parses it.</summary>
    public string PathOf(string uri) => new Uri(uri).AbsolutePath;

    /// <summary>The escaped path of the URI 1.2.5's buildUri makes of relativePath in this domain, or null if it threw.</summary>
    public string? PathFrom125(string relativePath)
    {
        try
        {
            return ((Uri)_buildUri125!.Invoke(_client125, [relativePath, _noQuery125])!).AbsolutePath;
        }
        catch (TargetInvocationException)
        {
            return null;
        }
    }

    /// <summary>Query Data Source with value as its id: null when it returned, else the exception's kind.</summary>
    public string? QueryDataSource(string value) => Outcome(() => _client!.QueryDataSource(_session!, value).GetAwaiter().GetResult());

    /// <summary>Query View Data with one view filter: null when it returned, else the exception's kind.</summary>
    public string? QueryViewData(string viewId, string name, string value) =>
        Outcome(() => _client!.QueryViewData(_session!, viewId, null, (name, value)).GetAwaiter().GetResult().Dispose());

    /// <summary>Query Data Sources with a filter expression: null when it returned, else the exception's kind.</summary>
    public string? QueryDataSources(string filter) =>
        Outcome(() => _client!.QueryDataSources(_session!, filterExpression: filter).GetAwaiter().GetResult());

    private static string? Outcome(Action call)
    {
        try
        {
            call();
            return null;
        }
        catch (ArgumentException)
        {
            return nameof(ArgumentException);
        }
        catch (Exception ex)
        {
            return ex.GetType().FullName + ": " + ex.Message;
        }
    }
}
#endif
