using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Tableau.Api.Tests.Fakes;

namespace Tableau.Api.Tests;

/// <summary>
/// The live-coverage matrix, <c>docs/live-coverage.md</c>, embedded in the test assembly: its table read, checked against
/// the code (<see cref="Problems"/>, on every PR) and against what the live suite's calls answered
/// (<see cref="NightlyProblems"/>, after a live run with TABLEAU_LIVE_MATRIX=1).
/// </summary>
internal static partial class LiveCoverage
{
    /// <summary>The table's resource in the test assembly.</summary>
    public const string ResourceName = "live-coverage.md";

    /// <summary>The section of the 2.0 client's table.</summary>
    public const string ClientSection = "TableauClient";

    /// <summary>The section of the Vizportal client's table.</summary>
    public const string VizPortalSection = "VizPortalClient";

    /// <summary>The names a row may give for the suite's own setup and cleanup, which no test method makes.</summary>
    public static readonly IReadOnlyList<string> SuiteNames = ["LiveSite", "SuiteCleanup"];

    private const string Header = "| Method | Call | Live | Test, or why not |";

    /// <summary>The matrix as the test assembly holds it.</summary>
    public static string Text()
    {
        using var stream = typeof(LiveCoverage).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"The test assembly holds no {ResourceName}.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// The rows of the table under <c>## section</c>, and what can't be read: no such section, no table there, or a row
    /// without its four cells.
    /// </summary>
    public static (List<CoverageRow> Rows, List<string> Problems) Parse(string markdown, string section)
    {
        var rows = new List<CoverageRow>();
        var problems = new List<string>();
        var lines = markdown.ReplaceLineEndings("\n").Split('\n');
        var start = Array.FindIndex(lines, line => line.TrimEnd() == "## " + section);
        if (start < 0)
        {
            problems.Add($"no section '## {section}'");
            return (rows, problems);
        }
        var header = Array.FindIndex(lines, start + 1, line => line.StartsWith('|') || line.StartsWith("## ", StringComparison.Ordinal));
        if (header < 0 || lines[header].TrimEnd() != Header || header + 1 >= lines.Length || !Delimiter().IsMatch(lines[header + 1]))
        {
            problems.Add($"the section '## {section}' has no table headed '{Header}'");
            return (rows, problems);
        }
        for (var index = header + 2; index < lines.Length && lines[index].StartsWith('|'); index++)
        {
            var number = index + 1;
            var cells = lines[index].Trim().Split('|');
            if (cells.Length != 6 || cells[0].Length > 0 || cells[^1].Length > 0)
            {
                problems.Add($"line {number}: a row has four cells, between pipes");
                continue;
            }
            var (method, call, live, last) = (Code(cells[1]), cells[2].Trim(), cells[3].Trim(), cells[4].Trim());
            var tests = live == "no" ? [] : last.Split(',').Select(Code).Where(test => test.Length > 0).ToList();
            rows.Add(new CoverageRow(number, method, call, live, tests, live == "no" ? last : ""));
        }
        return (rows, problems);
    }

    /// <summary>
    /// What is wrong with the rows, against the code: a method with no row, or a row naming none; a call of
    /// <c>Endpoints.All</c> with no row, or a row naming none; a Live cell that isn't <c>yes</c>, <c>refused</c> and a
    /// code, or <c>no</c>; a <c>yes</c> or <c>refused</c> row naming no test, or one that isn't a <c>Live</c> site test,
    /// nor <see cref="SuiteNames"/>; a <c>no</c> row without its reason; a method and a call with two rows.
    /// </summary>
    /// <param name="rows">The table's rows.</param>
    /// <param name="methods">Every method a row may name, as <c>ClassName.MethodName</c>, <c>&lt;T&gt;</c> after a generic one.</param>
    /// <param name="calls">Every call's name.</param>
    /// <param name="liveTests">Every <c>Live</c> site test, as <c>ClassName.TestName</c>.</param>
    public static List<string> Problems(IReadOnlyList<CoverageRow> rows, IReadOnlyCollection<string> methods, IReadOnlyCollection<string> calls,
        IReadOnlyCollection<string> liveTests)
    {
        var problems = new List<string>();
        var seen = new Dictionary<(string, string), int>();
        foreach (var row in rows)
        {
            var at = $"line {row.Line}, {row.Method} and {row.Call}";
            if (!methods.Contains(row.Method)) problems.Add($"line {row.Line}: {row.Method} is no public method of the client's");
            if (!calls.Contains(row.Call)) problems.Add($"line {row.Line}: '{row.Call}' is no call of Endpoints.All");
            if (seen.TryGetValue((row.Method, row.Call), out var first))
                problems.Add($"{at}: a second row for them, after line {first}'s");
            else
                seen[(row.Method, row.Call)] = row.Line;
            if (row.IsNo)
            {
                if (row.Reason.Length == 0) problems.Add($"{at}: a no row says why not");
                continue;
            }
            if (!row.IsYes && row.RefusedCode is null)
            {
                problems.Add($"{at}: Live is '{row.Live}', where it is yes, refused and Tableau's six-digit code, or no");
                continue;
            }
            if (row.Tests.Count == 0) problems.Add($"{at}: a {row.Live} row names the tests that make the call");
            foreach (var test in row.Tests.Where(test => !liveTests.Contains(test) && !SuiteNames.Contains(test)))
                problems.Add($"{at}: {test} is no Live site test, nor {string.Join(" or ", SuiteNames)}");
        }
        foreach (var method in methods.Where(method => rows.All(row => row.Method != method)).Order(StringComparer.Ordinal))
            problems.Add($"{method} has no row");
        foreach (var call in calls.Where(call => rows.All(row => row.Call != call)).Order(StringComparer.Ordinal))
            problems.Add($"'{call}' has no row");
        return problems;
    }

    /// <summary>
    /// What the calls a live run answered say against the rows: a <c>yes</c> row's call not answered with a 2xx in one of
    /// its tests; a <c>refused</c> row's not answered with its code in one of them, or answered with a 2xx there, which
    /// the row should say; a <c>no</c> row's call answered, unless a <c>yes</c> or <c>refused</c> row holds the call
    /// live, since the record is per call. A row whose call wasn't answered as it says, and one of whose tests failed, is
    /// left out: that failure fails the run already, and may be why. A call the table has no row for is passed over.
    /// </summary>
    /// <param name="rows">The table's rows.</param>
    /// <param name="answered">Every call Tableau answered in the run, by the test that made it.</param>
    /// <param name="failed">The site tests, and the suite's own names, that failed in the run.</param>
    public static List<string> NightlyProblems(IReadOnlyList<CoverageRow> rows, IReadOnlyCollection<AnsweredCall> answered,
        IReadOnlyCollection<string> failed)
    {
        var problems = new List<string>();
        var live = rows.Where(row => !row.IsNo).Select(row => row.Call).ToHashSet(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            var at = $"line {row.Line}, {row.Method} and {row.Call}";
            var mine = answered.Where(call => call.Call == row.Call && row.Tests.Contains(call.Test)).ToList();
            var excused = row.Tests.Any(failed.Contains);
            if (row.IsYes)
            {
                if (!mine.Any(call => call.Succeeded) && !excused)
                    problems.Add($"{at}: yes, but no test of the row had it answered with a 2xx; {Seen(mine)}");
            }
            else if (row.RefusedCode is { } code)
            {
                if (mine.FirstOrDefault(call => call.Succeeded) is { } success)
                    problems.Add($"{at}: refused {code}, but {success.Test} had it answered {success.Status}: the row should say yes");
                else if (!mine.Any(call => call.ErrorCode == code) && !excused)
                    problems.Add($"{at}: refused {code}, but no test of the row had it refused with {code}; {Seen(mine)}");
            }
            else if (row.IsNo && !live.Contains(row.Call) && answered.FirstOrDefault(call => call.Call == row.Call) is { } made)
            {
                problems.Add($"{at}: no, but {made.Test} had it answered {made.Status}{(made.ErrorCode is null ? "" : " " + made.ErrorCode)}: "
                    + "the row should change");
            }
        }
        return problems;

        static string Seen(List<AnsweredCall> calls) => calls.Count == 0
            ? "its tests didn't have it answered at all"
            : "they had it answered " + string.Join(", ", calls.Select(call => $"{call.Status}{(call.ErrorCode is null ? "" : " " + call.ErrorCode)} in {call.Test}")
                .Distinct(StringComparer.Ordinal));
    }

    /// <summary>
    /// The methods a row may name: each public method of the sub-clients, the types of <see cref="TableauClient"/>'s
    /// public properties, and each <see cref="TableauCredentials"/> factory, whose kind of sign-in is its own call.
    /// </summary>
    public static IReadOnlyCollection<string> Methods()
    {
        var subClients = typeof(TableauClient).GetProperties(BindingFlags.Instance | BindingFlags.Public).Select(property => property.PropertyType);
        var methods = subClients.SelectMany(type => type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
            .Where(method => !method.IsSpecialName)
            .Select(method => $"{method.DeclaringType!.Name}.{method.Name}"
                + (method.IsGenericMethodDefinition ? $"<{string.Join(", ", method.GetGenericArguments().Select(type => type.Name))}>" : ""));
        var factories = typeof(TableauCredentials).GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Where(method => method.ReturnType == typeof(TableauCredentials))
            .Select(method => $"{nameof(TableauCredentials)}.{method.Name}");
        return methods.Concat(factories).ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>Every call's name, from the client's registry.</summary>
    public static IReadOnlyCollection<string> Calls() => Pipeline.Endpoints.All.Select(endpoint => endpoint.Name).ToHashSet(StringComparer.Ordinal);

    /// <summary>Every <c>Live</c> site test in the assembly, as <c>ClassName.TestName</c>.</summary>
    public static IReadOnlyCollection<string> LiveTests() =>
        typeof(LiveCoverage).Assembly.GetTypes()
            .SelectMany(type => type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
            .Where(method => method.GetCustomAttribute<SiteTestAttribute>()?.Category == TableauSite.Live)
            .Select(method => $"{method.DeclaringType!.Name}.{method.Name}")
            .ToHashSet(StringComparer.Ordinal);

    // Inline code, its backticks taken off.
    private static string Code(string cell) => cell.Trim().Trim('`');

    [GeneratedRegex(@"^\|(\s*:?-+:?\s*\|){4}\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex Delimiter();
}

/// <summary>A row of the live-coverage matrix.</summary>
/// <param name="Line">Its line in the file, from 1.</param>
/// <param name="Method">The method, <c>ClassName.MethodName</c>.</param>
/// <param name="Call">The call's name, as <c>Endpoints</c> gives it.</param>
/// <param name="Live">The Live cell: <c>yes</c>, <c>refused</c> and a code, or <c>no</c>.</param>
/// <param name="Tests">For <c>yes</c> and <c>refused</c>, the tests that make the call.</param>
/// <param name="Reason">For <c>no</c>, why not.</param>
internal sealed partial record CoverageRow(int Line, string Method, string Call, string Live, IReadOnlyList<string> Tests, string Reason)
{
    /// <summary>Whether the row says Tableau answers the call with a 2xx.</summary>
    public bool IsYes => Live == "yes";

    /// <summary>Whether the row says the call isn't made live.</summary>
    public bool IsNo => Live == "no";

    /// <summary>The code a <c>refused</c> row pins, Tableau's six digits, or null.</summary>
    public string? RefusedCode => RefusedLive().Match(Live) is { Success: true } match ? match.Groups["code"].Value : null;

    [GeneratedRegex(@"^refused (?<code>[0-9]{6})$", RegexOptions.CultureInvariant)]
    private static partial Regex RefusedLive();
}

/// <summary>A call Tableau answered in a live run.</summary>
/// <param name="Test">The site test that made it, or the suite's own name for its setup and cleanup.</param>
/// <param name="Call">The call's name, from the client's log line.</param>
/// <param name="Status">The answer's status.</param>
/// <param name="ErrorCode">Tableau's error code, for an answer the client failed with; null for a success.</param>
/// <param name="Succeeded">Whether the call succeeded: the client logged it answered, not failed.</param>
internal sealed record AnsweredCall(string Test, string Call, int Status, string? ErrorCode, bool Succeeded);

/// <summary>
/// What a live run's calls answered, by the site test that made each: a logger factory for the live clients that keeps
/// each call Tableau answered and passes every line on to another logger, the suite's log check's; the test that runs
/// now; and how each test ended. Site tests that sign in run one at a time (<c>[DoNotParallelize]</c>), so the test that
/// runs now made the call. Answered is a request's answered line (event 2), or its failed line (event 3) whose
/// <see cref="TableauException"/> has a status: a call the client refused before sending has none, and wasn't answered.
/// </summary>
internal sealed class CallRecord
{
    /// <summary>The name a call made outside any site test is kept under.</summary>
    public const string NoTest = "(no test)";

    private readonly ILogger _inner;
    private readonly ConcurrentQueue<AnsweredCall> _answered = new();
    private readonly ConcurrentDictionary<string, bool> _passed = new(StringComparer.Ordinal);
    private string? _current;

    public CallRecord(ILogger inner)
    {
        _inner = inner;
        Factory = new RecordFactory(this);
    }

    /// <summary>The factory the live clients log through.</summary>
    public ILoggerFactory Factory { get; }

    /// <summary>The site test, or the suite's own name, that calls now; null between them.</summary>
    public string? Current => Volatile.Read(ref _current);

    /// <summary>Every answered call so far, in order.</summary>
    public IReadOnlyList<AnsweredCall> Answered => [.. _answered];

    /// <summary>The names that ended failed, at least once.</summary>
    public IReadOnlyCollection<string> Failed => [.. _passed.Where(pair => !pair.Value).Select(pair => pair.Key)];

    /// <summary>Makes <paramref name="name"/> the one that calls, until the scope is disposed, which gives the name back.</summary>
    public IDisposable As(string name) => new Scope(this, Interlocked.Exchange(ref _current, name));

    /// <summary>Says how a site test, or the suite's own name, ended; a failure stays.</summary>
    public void Ended(string name, bool passed) => _passed.AddOrUpdate(name, passed, (_, before) => before && passed);

    /// <summary>
    /// What <paramref name="call"/> gives, called as <paramref name="name"/>, such as the suite's setup within a test, which
    /// ends failed if it throws.
    /// </summary>
    public async Task<T> AsAsync<T>(string name, Func<Task<T>> call)
    {
        using (As(name))
        {
            try
            {
                var result = await call();
                Ended(name, passed: true);
                return result;
            }
            catch
            {
                Ended(name, passed: false);
                throw;
            }
        }
    }

    private void Log<TState>(LogLevel level, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        _inner.Log(level, eventId, state, exception, formatter);
        if (eventId.Id is not (2 or 3)) return;
        var properties = RecordingLogger.Properties(state);
        if (!properties.TryGetValue("Call", out var value) || value is not string call) return;
        var test = Current ?? NoTest;
        if (eventId.Id == 2 && properties.TryGetValue("StatusCode", out var status) && status is int code)
            _answered.Enqueue(new AnsweredCall(test, call, code, null, Succeeded: true));
        else if (eventId.Id == 3 && exception is TableauException { StatusCode: { } failed } refused)
            _answered.Enqueue(new AnsweredCall(test, call, (int)failed, refused.ErrorCode, Succeeded: false));
    }

    private sealed class Scope(CallRecord record, string? before) : IDisposable
    {
        public void Dispose() => Volatile.Write(ref record._current, before);
    }

    private sealed class RecordFactory(CallRecord record) : ILoggerFactory
    {
        private readonly Logger _logger = new(record);

        public ILogger CreateLogger(string categoryName) => _logger;

        public void AddProvider(ILoggerProvider provider)
        {
        }

        public void Dispose()
        {
        }
    }

    private sealed class Logger(CallRecord record) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            record.Log(logLevel, eventId, state, exception, formatter);
    }
}
