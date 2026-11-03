using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;

namespace Tableau.Api.Tests.Fakes;

/// <summary>
/// An ILogger that keeps every line the library logs, its level and its exception's text included, with a logger
/// factory that hands it out, for the 2.0 client, which takes a factory; and each line's event and properties, as a
/// structured reader would see them. Every one made is kept in <see cref="All"/>, which the suite's cleanup reads, so no
/// line any test logs escapes the check for secrets.
/// </summary>
internal sealed class RecordingLogger : ILogger
{
    private static readonly ConcurrentQueue<RecordingLogger> _all = new();
    private readonly ConcurrentQueue<string> _lines = new();
    private readonly ConcurrentQueue<LogEntry> _entries = new();

    public RecordingLogger()
    {
        Factory = new LoggerFactory(this);
        _all.Enqueue(this);
    }

    /// <summary>Every logger the suite has made so far.</summary>
    public static IReadOnlyList<RecordingLogger> All => [.. _all];

    /// <summary>A logger factory whose every logger is this one.</summary>
    public ILoggerFactory Factory { get; }

    public IReadOnlyList<string> Lines => [.. _lines];

    /// <summary>Each line's level, event id and properties, in the order logged.</summary>
    public IReadOnlyList<LogEntry> Entries => [.. _entries];

    public IDisposable BeginScope<TState>(TState state)
        where TState : notnull => NoScope.Instance;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        _lines.Enqueue($"{logLevel}: {formatter(state, exception)}{(exception is null ? "" : "\n" + exception)}");
        _entries.Enqueue(new LogEntry(logLevel, eventId.Id, Properties(state), exception));
    }

    /// <summary>A line's properties by name, as a structured logger reads them; none for a state that has no list of them.</summary>
    public static IReadOnlyDictionary<string, object?> Properties<TState>(TState state) =>
        state is IReadOnlyList<KeyValuePair<string, object?>> pairs
            ? pairs.GroupBy(pair => pair.Key, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.First().Value, StringComparer.Ordinal)
            : new Dictionary<string, object?>(StringComparer.Ordinal);

    /// <summary>The lines that hold any of secrets.</summary>
    public IEnumerable<string> LinesHolding(IEnumerable<string> secrets) =>
        Lines.Where(line => secrets.Any(secret => line.Contains(secret, StringComparison.Ordinal)));

    /// <summary>The lines logged at level.</summary>
    public IReadOnlyList<string> At(LogLevel level) =>
        [.. Lines.Where(line => line.StartsWith(level + ": ", StringComparison.Ordinal))];

    private sealed class LoggerFactory(RecordingLogger logger) : ILoggerFactory
    {
        public ILogger CreateLogger(string categoryName) => logger;

        public void AddProvider(ILoggerProvider provider)
        {
        }

        public void Dispose()
        {
        }
    }

    private sealed class NoScope : IDisposable
    {
        public static readonly NoScope Instance = new();

        public void Dispose()
        {
        }
    }
}

/// <summary>One line as a structured logger receives it.</summary>
/// <param name="Level">Its level.</param>
/// <param name="EventId">Its event's id.</param>
/// <param name="Properties">Its properties by name, the message template's <c>{OriginalFormat}</c> among them.</param>
/// <param name="Exception">The exception it was logged with, or null.</param>
internal sealed record LogEntry(LogLevel Level, int EventId, IReadOnlyDictionary<string, object?> Properties, Exception? Exception);
