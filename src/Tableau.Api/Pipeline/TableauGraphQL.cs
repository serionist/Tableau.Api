using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
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

    /// <summary>One entry of a GraphQL answer's <c>errors</c>.</summary>
    /// <param name="Code">Tableau's code, from the entry's <c>extensions</c>, such as <c>FEATURE_DISABLED</c>.</param>
    /// <param name="Message">The entry's message.</param>
    public readonly record struct Error(string? Code, string? Message);

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
            var code = error.TryGetProperty("extensions", out var extensions) && extensions.ValueKind == JsonValueKind.Object
                ? Text(extensions, "code")
                : null;
            found.Add(new Error(code, Text(error, "message")));
        }
        return found;
    }

    /// <summary>
    /// What <paramref name="select"/> makes of a query's <c>data</c>; a <see cref="TableauException"/> for an answer
    /// with <c>errors</c>, partial data and warnings among them, or one that isn't GraphQL's JSON.
    /// </summary>
    /// <param name="endpoint">The query's descriptor, which a message names.</param>
    /// <param name="bytes">The answer.</param>
    /// <param name="path">The request's path, as a message names it.</param>
    /// <param name="select">What the call returns from <c>data</c>; it throws <see cref="JsonException"/> or
    /// <see cref="InvalidDataException"/> for data it can't read.</param>
    public static T Read<T>(Endpoint endpoint, byte[] bytes, string path, Func<JsonElement, T> select)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(bytes);
        }
        catch (JsonException e)
        {
            throw Invalid(endpoint, path, "a body that isn't JSON", bytes, e);
        }
        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw Invalid(endpoint, path, "a body that isn't GraphQL's answer", bytes, null);
            var errors = Errors(root);
            if (errors.Count > 0)
                throw Failed(endpoint, path, errors, bytes);
            if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
                throw Invalid(endpoint, path, "no data", bytes, null);
            try
            {
                return select(data);
            }
            catch (Exception e) when (e is JsonException or InvalidDataException)
            {
                throw Invalid(endpoint, path, $"data it couldn't read: {e.Message}", bytes, e);
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
    /// <exception cref="InvalidDataException">The data holds no such connection, or it lacks its nodes or its page info.</exception>
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
        var items = new List<T>(nodes.GetArrayLength());
        foreach (var node in nodes.EnumerateArray())
        {
            if (node.ValueKind == JsonValueKind.Null) continue;
            if (node.Deserialize(nodeType) is { } item) items.Add(item);
        }
        var endCursor = pageInfo.TryGetProperty("endCursor", out var cursor) && cursor.ValueKind == JsonValueKind.String ? cursor.GetString() : null;
        return new CursorPage<T>(items.AsReadOnly(), endCursor, hasNextPage.GetBoolean());
    }

    // An answer with errors: Tableau's own code and message for each, the first as the code and summary. Tableau's
    // RATE_EXCEEDED is a rate limit, which RateLimited names; any other, an error or a warning that the data is
    // partial, is Http, as Tableau's other error answers are.
    private static TableauException Failed(Endpoint endpoint, string path, IReadOnlyList<Error> errors, byte[] bytes)
    {
        var listed = string.Join("; ", errors.Select(error => error.Code is null ? error.Message : $"{error.Code}: {error.Message}"));
        var limited = errors.Any(error => string.Equals(error.Code, "RATE_EXCEEDED", StringComparison.Ordinal));
        return new TableauException(limited ? TableauErrorKind.RateLimited : TableauErrorKind.Http,
            $"{endpoint.Name} ({endpoint.Method} {path}) answered with errors: {listed}{(listed.EndsWith('.') ? "" : ".")}")
        {
            StatusCode = HttpStatusCode.OK,
            ErrorCode = errors[0].Code,
            Summary = errors[0].Message,
            Detail = errors.Count > 1 ? string.Join("; ", errors.Skip(1).Select(error => error.Message)) : null,
            Method = endpoint.Method,
            RequestPath = path,
            ResponseBody = TableauPipeline.Kept(bytes, bytes.Length),
        };
    }

    private static TableauException Invalid(Endpoint endpoint, string path, string what, byte[] bytes, Exception? inner) =>
        new(TableauErrorKind.InvalidResponse,
            $"{endpoint.Name} ({endpoint.Method} {path}) answered {what}, where it returns the Metadata API's data.", inner)
        {
            StatusCode = HttpStatusCode.OK,
            Method = endpoint.Method,
            RequestPath = path,
            ResponseBody = TableauPipeline.Kept(bytes, bytes.Length),
        };

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
