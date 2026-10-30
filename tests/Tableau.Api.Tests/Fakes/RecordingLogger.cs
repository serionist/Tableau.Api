using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;

namespace Tableau.Api.Tests.Fakes;

/// <summary>An ILogger that keeps every line the library logs, its exception's text included.</summary>
internal sealed class RecordingLogger : ILogger
{
    private readonly ConcurrentQueue<string> _lines = new();

    public IReadOnlyList<string> Lines => [.. _lines];

    public IDisposable BeginScope<TState>(TState state) => NoScope.Instance;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception,
        Func<TState, Exception, string> formatter)
    {
        _lines.Enqueue($"{logLevel}: {formatter(state, exception)}{(exception is null ? "" : "\n" + exception)}");
    }

    /// <summary>The lines that hold any of secrets.</summary>
    public IEnumerable<string> LinesHolding(IEnumerable<string> secrets) =>
        Lines.Where(line => secrets.Any(secret => line.IndexOf(secret, StringComparison.Ordinal) >= 0));

    private sealed class NoScope : IDisposable
    {
        public static readonly NoScope Instance = new();

        public void Dispose()
        {
        }
    }
}
