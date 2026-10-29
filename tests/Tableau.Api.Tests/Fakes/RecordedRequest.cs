using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Tableau.Api.Tests.Fakes;

/// <summary>One request as FakeTableauServer read it off the wire: nothing decoded unless a member says so.</summary>
internal sealed class RecordedRequest
{
    public RecordedRequest(string requestLine, IReadOnlyList<KeyValuePair<string, string>> headers, byte[] body)
    {
        RequestLine = requestLine;
        Headers = headers;
        Body = body;
        var parts = requestLine.Split(' ');
        Method = parts[0];
        Target = parts.Length > 1 ? parts[1] : "";
        var question = Target.IndexOf('?');
        Path = question < 0 ? Target : Target.Substring(0, question);
        Query = question < 0 ? "" : Target.Substring(question + 1);
    }

    /// <summary>The request line as sent: `GET /api/3.4/sites/... HTTP/1.1`.</summary>
    public string RequestLine { get; }

    public string Method { get; }

    /// <summary>The request target as sent: the path and the query, escapes included.</summary>
    public string Target { get; }

    /// <summary>The target before any `?`, as sent.</summary>
    public string Path { get; }

    /// <summary>The target after the `?`, as sent, or empty.</summary>
    public string Query { get; }

    /// <summary>Each header line's name and value, in the order sent.</summary>
    public IReadOnlyList<KeyValuePair<string, string>> Headers { get; }

    public byte[] Body { get; }

    public string BodyText => Encoding.UTF8.GetString(Body);

    /// <summary>The query's parameters, each name and value unescaped, `+` read as a space.</summary>
    public IReadOnlyList<KeyValuePair<string, string>> QueryParameters =>
        Query.Length == 0
            ? []
            : [.. Query.Split('&').Select(pair =>
            {
                var equals = pair.IndexOf('=');
                var name = equals < 0 ? pair : pair.Substring(0, equals);
                var value = equals < 0 ? "" : pair.Substring(equals + 1);
                return new KeyValuePair<string, string>(Unescape(name), Unescape(value));
            })];

    /// <summary>The first value of the header name, which is case-insensitive, or null.</summary>
    public string? Header(string name) => Find(Headers, name);

    public static string? Find(IEnumerable<KeyValuePair<string, string>> headers, string name) =>
        headers.Where(header => string.Equals(header.Key, name, StringComparison.OrdinalIgnoreCase))
            .Select(header => header.Value).FirstOrDefault();

    public override string ToString() => RequestLine;

    private static string Unescape(string text) => Uri.UnescapeDataString(text.Replace('+', ' '));
}
