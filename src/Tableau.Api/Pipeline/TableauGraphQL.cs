using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;

namespace Tableau.Api.Pipeline;

/// <summary>
/// The Metadata API's GraphQL: a query's body, and its answer read into the data the caller asked for, or into the
/// <see cref="TableauException"/> its errors make. JSON through <see cref="JsonDocument"/> and the caller's own
/// <see cref="JsonTypeInfo{T}"/>, so nothing is read by reflection.
/// </summary>
internal static class TableauGraphQL
{
    /// <summary>The variable the cursor helper sets to the page's size.</summary>
    public const string First = "first";

    /// <summary>The variable the cursor helper sets to the cursor the page starts after.</summary>
    public const string After = "after";

    /// <summary>Tableau's code for a rate limit, which <see cref="TableauErrorKind.RateLimited"/> names.</summary>
    private const string RateExceeded = "RATE_EXCEEDED";

    /// <summary>One entry of a GraphQL answer's <c>errors</c>.</summary>
    /// <param name="Code">Tableau's code, from the entry's <c>extensions</c>, such as <c>FEATURE_DISABLED</c>.</param>
    /// <param name="Message">The entry's message.</param>
    /// <param name="Severity">Its <c>extensions</c>' <c>severity</c>, <c>WARNING</c> for partial data, or null.</param>
    public readonly record struct Error(string? Code, string? Message, string? Severity)
    {
        /// <summary>
        /// Whether Tableau marks the entry a warning, which comes with data that may be partial, such as
        /// <c>BACKFILL_RUNNING</c>, <c>NODE_LIMIT_EXCEEDED</c> or <c>TIME_LIMIT_EXCEEDED</c>.
        /// </summary>
        public bool IsWarning => string.Equals(Severity, "WARNING", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>What an exception carries of an answer's errors: the first's code and message, the others' messages.</summary>
    /// <param name="Code">The first entry's code.</param>
    /// <param name="Summary">The first entry's message.</param>
    /// <param name="Detail">The other entries' messages, joined, or null when there is one.</param>
    /// <param name="RateLimited">Whether an entry is <c>RATE_EXCEEDED</c>.</param>
    public readonly record struct Described(string? Code, string? Summary, string? Detail, bool RateLimited);

    /// <summary>A query's body: <c>{"query":...,"variables":...}</c>, the variables left out when there are none.</summary>
    public static byte[] Body(string query, JsonObject? variables)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("query", query);
            if (variables is not null)
            {
                writer.WritePropertyName("variables");
                variables.WriteTo(writer);
            }
            writer.WriteEndObject();
        }
        return buffer.ToArray();
    }

    /// <summary>The variables of one page: the caller's, with <c>first</c> and <c>after</c> set.</summary>
    public static JsonObject PageVariables(JsonObject? variables, int pageSize, string? after)
    {
        var page = variables?.DeepClone().AsObject() ?? [];
        page[First] = pageSize;
        page[After] = after;
        return page;
    }

    /// <summary>The entries of an answer's <c>errors</c>, or none.</summary>
    public static IReadOnlyList<Error> Errors(JsonElement root)
    {
        if (!root.TryGetProperty("errors", out var errors) || errors.ValueKind != JsonValueKind.Array) return [];
        var found = new List<Error>();
        foreach (var error in errors.EnumerateArray())
        {
            if (error.ValueKind != JsonValueKind.Object) continue;
            var extensions = error.TryGetProperty("extensions", out var value) && value.ValueKind == JsonValueKind.Object ? value : default;
            found.Add(extensions.ValueKind == JsonValueKind.Object
                ? new Error(Text(extensions, "code"), Text(error, "message"), Text(extensions, "severity"))
                : new Error(null, Text(error, "message"), null));
        }
        return found;
    }

    /// <summary>The errors as an exception carries them, whatever the answer's status.</summary>
    public static Described Describe(IReadOnlyList<Error> errors) =>
        new(errors[0].Code, errors[0].Message, errors.Count > 1 ? string.Join("; ", errors.Skip(1).Select(error => error.Message)) : null,
            errors.Any(error => string.Equals(error.Code, RateExceeded, StringComparison.Ordinal)));

    /// <summary>
    /// What <paramref name="select"/> makes of a query's <c>data</c>. An answer whose <c>errors</c> are all warnings,
    /// which come with data that may be partial, logs each at Warning and returns the data; any other entry, or an
    /// answer that isn't GraphQL's JSON, throws <see cref="TableauException"/>.
    /// </summary>
    /// <param name="pipeline">The pipeline that logs a warning.</param>
    /// <param name="endpoint">The query's descriptor, which a message names.</param>
    /// <param name="answer">The answer.</param>
    /// <param name="select">What the call returns from <c>data</c>; it throws <see cref="JsonException"/> or
    /// <see cref="InvalidDataException"/> for data it can't read.</param>
    public static T Read<T>(TableauPipeline pipeline, Endpoint endpoint, JsonAnswer answer, Func<JsonElement, T> select)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(answer.Bytes);
        }
        catch (JsonException e)
        {
            throw Invalid(endpoint, answer, "a body that isn't JSON", e);
        }
        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw Invalid(endpoint, answer, "a body that isn't GraphQL's answer", null);
            var errors = Errors(root);
            if (errors.Any(error => !error.IsWarning))
                throw Failed(endpoint, answer, errors);
            foreach (var warning in errors)
                pipeline.Warned(endpoint, answer.Path, warning.Code, warning.Message);
            if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
                throw Invalid(endpoint, answer, "no data", null);
            try
            {
                return select(data);
            }
            catch (Exception e) when (e is JsonException or InvalidDataException)
            {
                throw Invalid(endpoint, answer, $"data it couldn't read: {e.Message}", e);
            }
        }
    }

    /// <summary>
    /// One page of a connection: its <c>nodes</c>, each read through <paramref name="nodeType"/>, and its
    /// <c>pageInfo</c>'s <c>endCursor</c> and <c>hasNextPage</c>.
    /// </summary>
    /// <param name="data">The answer's <c>data</c>.</param>
    /// <param name="connection">Where the connection is in <c>data</c>: its field's name, or the names of the fields to it, joined with dots.</param>
    /// <param name="nodeType">How a node reads.</param>
    /// <exception cref="InvalidDataException">
    /// The data holds no such connection, or it lacks its nodes or its page info, or, when more pages follow, its
    /// <c>endCursor</c>.
    /// </exception>
    public static CursorPage<T> Page<T>(JsonElement data, string connection, JsonTypeInfo<T> nodeType)
    {
        var found = data;
        foreach (var field in connection.Split('.'))
        {
            if (found.ValueKind != JsonValueKind.Object || !found.TryGetProperty(field, out found))
                throw new InvalidDataException($"its data holds no {connection}.");
        }
        if (found.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException($"its {connection} is no connection object.");
        if (!found.TryGetProperty("nodes", out var nodes) || nodes.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException($"its {connection} has no nodes: the query asks for them in nodes {{ ... }}.");
        if (!found.TryGetProperty("pageInfo", out var pageInfo) || pageInfo.ValueKind != JsonValueKind.Object
            || !pageInfo.TryGetProperty("hasNextPage", out var hasNextPage) || hasNextPage.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            throw new InvalidDataException($"its {connection} has no pageInfo with hasNextPage: the query asks for "
                + "pageInfo { hasNextPage endCursor }.");
        }
        // A cursor the query didn't ask for is the query's to mend; one Tableau sends as null is Tableau's, which the list
        // reports when it can't go on.
        var asked = pageInfo.TryGetProperty("endCursor", out var cursor);
        if (!asked && hasNextPage.GetBoolean())
        {
            throw new InvalidDataException($"its {connection}'s pageInfo has no endCursor, so the next page can't be asked for: the query "
                + "asks for pageInfo { hasNextPage endCursor }.");
        }
        var items = new List<T>(nodes.GetArrayLength());
        foreach (var node in nodes.EnumerateArray())
        {
            if (node.ValueKind == JsonValueKind.Null) continue;
            if (node.Deserialize(nodeType) is { } item) items.Add(item);
        }
        var endCursor = asked && cursor.ValueKind == JsonValueKind.String ? cursor.GetString() : null;
        return new CursorPage<T>(items.AsReadOnly(), endCursor, hasNextPage.GetBoolean());
    }

    // An answer with an error that is no warning: Tableau's own code and message for each, the warnings among them, the
    // first as the code and summary. RATE_EXCEEDED is a rate limit, which RateLimited names; any other is Http, as
    // Tableau's other error answers are.
    private static TableauException Failed(Endpoint endpoint, JsonAnswer answer, IReadOnlyList<Error> errors)
    {
        var listed = string.Join("; ", errors.Select(error => error.Code is null ? error.Message : $"{error.Code}: {error.Message}"));
        var described = Describe(errors);
        return new TableauException(described.RateLimited ? TableauErrorKind.RateLimited : TableauErrorKind.Http,
            $"{endpoint.Name} ({endpoint.Method} {answer.Path}) answered with errors: {listed}{(listed.EndsWith('.') ? "" : ".")}")
        {
            StatusCode = answer.StatusCode,
            ErrorCode = described.Code,
            Summary = described.Summary,
            Detail = described.Detail,
            Method = endpoint.Method,
            RequestPath = answer.Path,
            RequestId = answer.RequestId,
            ResponseBody = TableauPipeline.Kept(answer.Bytes, answer.Bytes.Length),
        };
    }

    private static TableauException Invalid(Endpoint endpoint, JsonAnswer answer, string what, Exception? inner) =>
        new(TableauErrorKind.InvalidResponse,
            $"{endpoint.Name} ({endpoint.Method} {answer.Path}) answered {what}, where it returns the Metadata API's data.", inner)
        {
            StatusCode = answer.StatusCode,
            Method = endpoint.Method,
            RequestPath = answer.Path,
            RequestId = answer.RequestId,
            ResponseBody = TableauPipeline.Kept(answer.Bytes, answer.Bytes.Length),
        };

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
