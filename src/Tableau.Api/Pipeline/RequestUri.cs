using System;
using System.Collections.Generic;
using System.Text;

namespace Tableau.Api.Pipeline;

/// <summary>
/// The request URI of a call: the base URL with its path, <c>api/</c>, the endpoint's template with the version and
/// each value escaped as one segment, and the query with each key and value escaped. 1.2.6's rules, which the old
/// client follows too.
/// </summary>
internal static class RequestUri
{
    /// <summary>
    /// The URI, or <see cref="ArgumentException"/> before anything is sent: for a value that is empty or only dots, a
    /// LUID that isn't one, or a path this platform's URI parsing would send otherwise than written.
    /// </summary>
    /// <param name="serverUrl">The base URL.</param>
    /// <param name="endpoint">The call's descriptor.</param>
    /// <param name="version">The version in use.</param>
    /// <param name="values">A value for each of the template's parameters, in order.</param>
    /// <param name="query">The query's keys and values.</param>
    /// <param name="siteId">The session's site, for a template that names <c>{siteId}</c>.</param>
    public static Uri Build(Uri serverUrl, Endpoint endpoint, Version version, IReadOnlyList<string>? values,
        IReadOnlyList<KeyValuePair<string, string?>>? query, string? siteId = null)
    {
        var path = new StringBuilder(BasePath(serverUrl)).Append("api");
        var next = 0;
        foreach (var segment in endpoint.Segments)
        {
            path.Append('/');
            switch (segment.Kind)
            {
                case PathSegmentKind.Literal:
                    path.Append(segment.Text);
                    break;
                case PathSegmentKind.Version:
                    path.Append(ApiVersions.Format(version));
                    break;
                case PathSegmentKind.SiteId:
                    UriEscaping.AppendPathSegment(path, Checked(new PathSegment("siteId", PathSegmentKind.Luid),
                        siteId ?? throw new InvalidOperationException($"{endpoint.Name} names the session's site, and was given none.")));
                    break;
                default:
                    if (values is null || next >= values.Count)
                        throw new InvalidOperationException($"{endpoint.Name} was given no value for {segment.Text}.");
                    UriEscaping.AppendPathSegment(path, Checked(segment, values[next++]));
                    break;
            }
        }
        if (next != (values?.Count ?? 0))
            throw new InvalidOperationException($"{endpoint.Name} was given {values!.Count} values for {next} segments.");

        var written = path.ToString();
        var url = new StringBuilder(serverUrl.GetComponents(UriComponents.SchemeAndServer, UriFormat.UriEscaped))
            .Append(written);
        AppendQuery(url, query);
        var uri = new Uri(url.ToString());
        // Uri may rewrite a path after it is escaped, so the path it would send is checked against the one written.
        if (!string.Equals(uri.AbsolutePath, written, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"The request path '{written}' would be sent as '{uri.AbsolutePath}' by this platform's URI parsing, so it "
                + "is not sent. A value or the base URL's path holds characters that it rewrites.");
        }
        return uri;
    }

    /// <summary>The base URL's path, ending in /, so a server behind a path prefix is reached under it.</summary>
    public static string BasePath(Uri serverUrl) =>
        serverUrl.AbsolutePath.EndsWith('/') ? serverUrl.AbsolutePath : serverUrl.AbsolutePath + "/";

    /// <summary>
    /// The path and query a log line or an exception names: each query value as <c>***</c>, since a filter can hold a
    /// name or an email address, and never the host.
    /// </summary>
    public static string Redacted(Uri uri)
    {
        var text = new StringBuilder(uri.AbsolutePath);
        var query = uri.Query;
        if (query.Length > 1)
        {
            var first = true;
            foreach (var pair in query[1..].Split('&'))
            {
                text.Append(first ? '?' : '&');
                first = false;
                var equals = pair.IndexOf('=', StringComparison.Ordinal);
                text.Append(equals < 0 ? pair : pair[..equals]).Append("=***");
            }
        }
        return text.ToString();
    }

    /// <summary>Whether two URIs share their scheme, host and port: where a request, its token and its body may go.</summary>
    public static bool SameOrigin(Uri a, Uri b) =>
        string.Equals(a.Scheme, b.Scheme, StringComparison.OrdinalIgnoreCase)
        && string.Equals(a.IdnHost, b.IdnHost, StringComparison.OrdinalIgnoreCase)
        && a.Port == b.Port;

    /// <summary>The scheme, host and port of a URI, without its user info, path or query.</summary>
    public static string Origin(Uri uri) => uri.GetComponents(UriComponents.SchemeAndServer, UriFormat.UriEscaped);

    private static string Checked(PathSegment segment, string? value)
    {
        ArgumentNullException.ThrowIfNull(value, segment.Text);
        if (value.AsSpan().IndexOfAnyExcept('.') < 0)
        {
            throw new ArgumentException(
                $"{segment.Text} is '{value}', but a value in a request path can't be empty or only dots: it would name "
                + "no resource.", segment.Text);
        }
        // TryParseExact forgives whitespace around the value, which would then be sent, so the value must be the LUID
        // itself, in either case.
        if (segment.Kind == PathSegmentKind.Luid
            && !(Guid.TryParseExact(value, "D", out var luid) && string.Equals(luid.ToString("D"), value, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException($"{segment.Text} is '{value}', which is no LUID.", segment.Text);
        return value;
    }

    private static void AppendQuery(StringBuilder url, IReadOnlyList<KeyValuePair<string, string?>>? query)
    {
        if (query is null) return;
        var first = true;
        foreach (var (key, value) in query)
        {
            if (value is null) continue;
            url.Append(first ? '?' : '&');
            first = false;
            UriEscaping.AppendQueryPart(url, key);
            url.Append('=');
            AppendQueryValue(url, key, value);
        }
    }

    // A filter or sort expression keeps its separators, : and ,, and the parts between them are escaped.
    private static void AppendQueryValue(StringBuilder url, string key, string value)
    {
        if (key is not ("filter" or "sort"))
        {
            UriEscaping.AppendQueryPart(url, value);
            return;
        }
        var start = 0;
        for (var i = 0; i <= value.Length; i++)
        {
            if (i < value.Length && value[i] != ':' && value[i] != ',') continue;
            UriEscaping.AppendQueryPart(url, value[start..i]);
            if (i < value.Length) url.Append(value[i]);
            start = i + 1;
        }
    }
}
