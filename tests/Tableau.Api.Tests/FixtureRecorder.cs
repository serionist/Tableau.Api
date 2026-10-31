using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Tableau.Api.Tests;

/// <summary>
/// A handler the live suite sends through when TABLEAU_RECORD is 1: it writes each JSON body, request and answer, to a
/// file in a folder outside Fixtures/, scrubbed first by <see cref="FixtureScrubber"/>, for a maintainer to read and copy
/// into Fixtures/ with its manifest entry. It writes nothing it hasn't scrubbed, and the bodies it passes on are as sent.
/// </summary>
/// <param name="folder">Where the files go: TABLEAU_RECORD_DIR, or recorded/ beside the test assembly, under bin/, which git ignores.</param>
/// <param name="scrubber">The run's scrubber, so a LUID keeps one invented value across files.</param>
internal sealed class FixtureRecorder(string folder, FixtureScrubber scrubber) : DelegatingHandler
{
    private int _count;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var number = Interlocked.Increment(ref _count);
        var name = string.Create(CultureInfo.InvariantCulture,
            $"{number:000}-{request.Method.Method.ToLowerInvariant()}-{scrubber.Slug(request.RequestUri!.AbsolutePath)}");
        if (request.Content is { } content && IsJson(content))
            await WriteAsync(name + "-request.json", await content.ReadAsStringAsync(cancellationToken)).ConfigureAwait(false);
        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (IsJson(response.Content))
        {
            await response.Content.LoadIntoBufferAsync(cancellationToken).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (body.Length > 0) await WriteAsync(string.Create(CultureInfo.InvariantCulture, $"{name}-{(int)response.StatusCode}.json"), body).ConfigureAwait(false);
        }
        return response;
    }

    private static bool IsJson(HttpContent content) =>
        content.Headers.ContentType?.MediaType?.EndsWith("json", StringComparison.OrdinalIgnoreCase) == true;

    private async Task WriteAsync(string file, string body)
    {
        Directory.CreateDirectory(folder);
        await File.WriteAllTextAsync(Path.Combine(folder, file), scrubber.Scrub(body), new UTF8Encoding(false)).ConfigureAwait(false);
    }
}

/// <summary>
/// Takes out of Tableau's JSON what a fixture must not hold: tokens, passwords and secrets; LUIDs, each the same invented
/// LUID wherever it recurs, so the fixture's ids still meet; names, content URLs and descriptions; email addresses; and
/// host names, the site's own and any in a URL. What remains is the shape, and values that say nothing of the site.
/// </summary>
/// <param name="hosts">The host names to take out wherever they appear, such as the server's.</param>
internal sealed partial class FixtureScrubber(IEnumerable<string> hosts)
{
    private static readonly HashSet<string> _secrets = new(StringComparer.OrdinalIgnoreCase)
    {
        "token", "password", "jwt", "personalAccessTokenSecret", "secret", "secretValue", "apiToken", "credentialsToken",
    };

    private static readonly HashSet<string> _names = new(StringComparer.OrdinalIgnoreCase)
    {
        "name", "contentUrl", "fullName", "displayName", "ownerName", "userName", "personalAccessTokenName", "description",
        "domainName", "siteName", "projectName", "workbookName", "datasourceName", "viewName", "serverName", "serverAddress",
        "userId", "sub", "subscriptionSubject", "subject", "message", "tag", "label",
    };

    private readonly string[] _hosts = [.. hosts.Where(host => host.Length > 0)];
    private readonly Dictionary<string, string> _luids = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _emails = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _lock = new();

    /// <summary>The JSON, scrubbed and indented; text that isn't JSON is scrubbed as text.</summary>
    public string Scrub(string json)
    {
        lock (_lock)
        {
            JsonNode? node;
            try
            {
                node = JsonNode.Parse(json);
            }
            catch (JsonException)
            {
                return Text(json);
            }
            return Node(node, null)?.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) ?? "null";
        }
    }

    /// <summary>A request path as a file name's part: its LUIDs and names invented, its separators dashes.</summary>
    public string Slug(string path)
    {
        lock (_lock)
        {
            var scrubbed = Text(path).Trim('/');
            return SlugPattern().Replace(scrubbed, "-").Trim('-');
        }
    }

    private JsonNode? Node(JsonNode? node, string? property) => node switch
    {
        JsonObject obj => new JsonObject(obj.Select(pair => KeyValuePair.Create(pair.Key, Node(pair.Value, pair.Key)))),
        JsonArray array => new JsonArray([.. array.Select(item => Node(item, property))]),
        JsonValue value when value.TryGetValue<string>(out var text) => JsonValue.Create(Value(property, text)),
        _ => node?.DeepClone(),
    };

    private string Value(string? property, string text)
    {
        if (text.Length == 0) return text;
        if (property is not null && _secrets.Contains(property)) return "invented-" + property;
        if (property is not null && _names.Contains(property) && !LuidPattern().IsMatch(text))
        {
            if (!_values.TryGetValue(text, out var invented))
                _values[text] = invented = string.Create(CultureInfo.InvariantCulture, $"invented-{property}-{_values.Count + 1}");
            return invented;
        }
        return Text(text);
    }

    // LUIDs, emails, URLs' hosts and the site's hosts, wherever they are in a text.
    private string Text(string text)
    {
        text = LuidPattern().Replace(text, match =>
        {
            if (!_luids.TryGetValue(match.Value, out var invented))
                _luids[match.Value] = invented = string.Create(CultureInfo.InvariantCulture, $"00000000-0000-4000-8000-{_luids.Count + 1:x12}");
            return invented;
        });
        text = EmailPattern().Replace(text, match =>
        {
            if (!_emails.TryGetValue(match.Value, out var invented))
                _emails[match.Value] = invented = string.Create(CultureInfo.InvariantCulture, $"user-{_emails.Count + 1}@invented.example");
            return invented;
        });
        text = UrlHostPattern().Replace(text, match => match.Groups["scheme"].Value + "tableau.example.com");
        foreach (var host in _hosts)
            text = text.Replace(host, "tableau.example.com", StringComparison.OrdinalIgnoreCase);
        return text;
    }

    [GeneratedRegex("[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}", RegexOptions.CultureInvariant)]
    private static partial Regex LuidPattern();

    [GeneratedRegex(@"[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}", RegexOptions.CultureInvariant)]
    private static partial Regex EmailPattern();

    [GeneratedRegex(@"(?<scheme>[a-zA-Z][a-zA-Z0-9+.\-]*://)(?:[^/\s""@]*@)?[^/\s"":?#]+", RegexOptions.CultureInvariant)]
    private static partial Regex UrlHostPattern();

    [GeneratedRegex("[^A-Za-z0-9.]+", RegexOptions.CultureInvariant)]
    private static partial Regex SlugPattern();
}
