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
/// into Fixtures/ with its manifest entry. It writes nothing it hasn't scrubbed, nothing for a body labelled JSON that
/// doesn't parse, and the bodies it passes on are as sent. At the suite's end <see cref="DeleteHolding"/> scans what it
/// wrote for every live secret and setting.
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

    /// <summary>
    /// Deletes each file in <paramref name="folder"/> that holds one of <paramref name="secrets"/> anywhere, or one of
    /// <paramref name="known"/> as a whole word, and returns their names, never the values.
    /// </summary>
    public static IReadOnlyList<string> DeleteHolding(string folder, IEnumerable<string> secrets, IEnumerable<string> known)
    {
        if (!Directory.Exists(folder)) return [];
        var secretList = secrets.Where(secret => secret.Length > 0).ToList();
        var patterns = known.Where(value => value.Length > 0).Select(FixtureScrubber.Word).ToList();
        var holding = new List<string>();
        foreach (var file in Directory.GetFiles(folder))
        {
            var text = File.ReadAllText(file);
            if (!secretList.Any(secret => text.Contains(secret, StringComparison.Ordinal)) && !patterns.Any(pattern => pattern.IsMatch(text)))
                continue;
            File.Delete(file);
            holding.Add(Path.GetFileName(file));
        }
        return holding;
    }

    private static bool IsJson(HttpContent content) =>
        content.Headers.ContentType?.MediaType?.EndsWith("json", StringComparison.OrdinalIgnoreCase) == true;

    private async Task WriteAsync(string file, string body)
    {
        if (scrubber.Scrub(body) is not { } scrubbed) return;
        Directory.CreateDirectory(folder);
        await File.WriteAllTextAsync(Path.Combine(folder, file), scrubbed, new UTF8Encoding(false)).ConfigureAwait(false);
    }
}

/// <summary>
/// Takes out of Tableau's JSON what a fixture must not hold, failing closed: every string is invented but a LUID, mapped
/// to the same invented LUID wherever it recurs so a fixture's ids still meet; a date, a number, a version, a time span
/// or a boolean; and the value of a key on a short list of enumerations and messages, in which host names, emails,
/// LUIDs and the run's known values are still replaced. A key that names a token, a secret, a password, a JWT or a
/// keychain gives its value away as <c>invented-</c> and the key, and every string under a key that names a secret or a
/// keychain goes too.
/// </summary>
/// <param name="hosts">The host names to take out wherever they appear, such as the server's.</param>
/// <param name="known">
/// The run's values that name the site or the sign-in without being secrets, taken out wherever they appear as a word:
/// the site, the user, a token's name, a connected app's client id and secret id.
/// </param>
internal sealed partial class FixtureScrubber(IEnumerable<string> hosts, IEnumerable<string>? known = null)
{
    // Enumerations and messages that say what happened, not to whom: kept, scrubbed within.
    private static readonly HashSet<string> _keep = new(StringComparer.OrdinalIgnoreCase)
    {
        "summary", "contentPermissions", "siteRole", "authSetting", "state", "status", "type", "mode", "adminMode", "role",
        "licensingRole", "frequency", "executionOrder", "jobType", "method", "priority",
    };

    private readonly string[] _hosts = [.. hosts.Where(host => host.Length > 0)];
    private readonly Regex[] _known = [.. (known ?? []).Where(value => value.Length > 0).Distinct(StringComparer.Ordinal).Select(Word)];
    private readonly Dictionary<string, string> _luids = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _emails = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _lock = new();


    /// <summary>A value as a whole word: not inside a longer run of letters and digits.</summary>
    public static Regex Word(string value) =>
        new($"(?<![A-Za-z0-9]){Regex.Escape(value)}(?![A-Za-z0-9])", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    /// <summary>The JSON, scrubbed and indented; null for text that isn't JSON, so nothing is written for it.</summary>
    public string? Scrub(string json)
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
                return null;
            }
            return Node(node, null, secret: false)?.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) ?? "null";
        }
    }

    /// <summary>
    /// A request path as part of a file's name: its LUIDs, host names and the run's known values replaced as in a body,
    /// then every run of other characters than letters, digits and dots as one dash.
    /// </summary>
    public string Slug(string path)
    {
        lock (_lock)
        {
            var scrubbed = Text(path).Trim('/');
            return SlugPattern().Replace(scrubbed, "-").Trim('-');
        }
    }

    private JsonNode? Node(JsonNode? node, string? key, bool secret) => node switch
    {
        JsonObject obj => new JsonObject(obj.Select(pair =>
            KeyValuePair.Create(pair.Key, Node(pair.Value, pair.Key, secret || SecretTree().IsMatch(pair.Key))))),
        JsonArray array => new JsonArray([.. array.Select(item => Node(item, key, secret))]),
        JsonValue value when value.TryGetValue<string>(out var text) => JsonValue.Create(Value(key, text, secret)),
        _ => node?.DeepClone(),
    };

    private string Value(string? key, string text, bool secret)
    {
        if (text.Length == 0) return text;
        if (secret || (key is not null && SecretKey().IsMatch(key))) return "invented-" + (key ?? "value");
        if (LuidPattern().IsMatch(text) && LuidPattern().Match(text).Length == text.Length) return Luid(text);
        if (SaysNothing().IsMatch(text)) return text;
        if (key is not null && _keep.Contains(key)) return Text(text);
        if (!_values.TryGetValue(text, out var invented))
            _values[text] = invented = string.Create(CultureInfo.InvariantCulture, $"invented-{key ?? "value"}-{_values.Count + 1}");
        return invented;
    }

    private string Luid(string luid)
    {
        if (!_luids.TryGetValue(luid, out var invented))
            _luids[luid] = invented = string.Create(CultureInfo.InvariantCulture, $"00000000-0000-4000-8000-{_luids.Count + 1:x12}");
        return invented;
    }

    // LUIDs, emails, URLs' hosts, the site's hosts and the run's known values, wherever they are in a text.
    private string Text(string text)
    {
        text = LuidPattern().Replace(text, match => Luid(match.Value));
        text = EmailPattern().Replace(text, match =>
        {
            if (!_emails.TryGetValue(match.Value, out var invented))
                _emails[match.Value] = invented = string.Create(CultureInfo.InvariantCulture, $"user-{_emails.Count + 1}@invented.example");
            return invented;
        });
        text = UrlHostPattern().Replace(text, match => match.Groups["scheme"].Value + "tableau.example.com");
        foreach (var host in _hosts)
            text = text.Replace(host, "tableau.example.com", StringComparison.OrdinalIgnoreCase);
        foreach (var known in _known)
            text = known.Replace(text, "invented");
        return text;
    }

    [GeneratedRegex("token|secret|password|jwt|keychain", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SecretKey();

    [GeneratedRegex("secret|keychain", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SecretTree();

    // A date, a number, a version, a time span or a boolean, which name no one.
    [GeneratedRegex(@"^(?:\d{4}-\d{2}-\d{2}(?:T\d{2}:\d{2}(?::\d{2}(?:\.\d+)?)?(?:Z|[+-]\d{2}:\d{2})?)?|-?\d+(?:\.\d+)*|\d+:\d{2}(?::\d{2})?|true|false)$",
        RegexOptions.CultureInvariant)]
    private static partial Regex SaysNothing();

    [GeneratedRegex("[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}", RegexOptions.CultureInvariant)]
    private static partial Regex LuidPattern();

    [GeneratedRegex(@"[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}", RegexOptions.CultureInvariant)]
    private static partial Regex EmailPattern();

    [GeneratedRegex(@"(?<scheme>[a-zA-Z][a-zA-Z0-9+.\-]*://)(?:[^/\s""@]*@)?[^/\s"":?#]+", RegexOptions.CultureInvariant)]
    private static partial Regex UrlHostPattern();

    [GeneratedRegex("[^A-Za-z0-9.]+", RegexOptions.CultureInvariant)]
    private static partial Regex SlugPattern();
}
