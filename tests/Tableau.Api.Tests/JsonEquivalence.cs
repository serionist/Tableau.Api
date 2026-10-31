using System;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Tableau.Api.Tests;

/// <summary>
/// Whether two JSON bodies say the same to Tableau: members in any order, a number equal to a string that holds it, as
/// Tableau sends its xs:integer values as strings, and two date-times equal when they name the same instant. The native
/// AOT app links this file too, so it uses nothing a trimmed app can't.
/// </summary>
internal static class JsonEquivalence
{
    /// <summary>The first place where <paramref name="actual"/> differs, by its JSON path, or null when it doesn't.</summary>
    public static string? Difference(string expected, string actual) =>
        Difference(JsonNode.Parse(expected), JsonNode.Parse(actual), "$");

    private static string? Difference(JsonNode? expected, JsonNode? actual, string path)
    {
        switch (expected, actual)
        {
            case (null, null):
                return null;
            case (JsonObject left, JsonObject right):
                foreach (var (name, value) in left)
                {
                    if (!right.ContainsKey(name)) return $"{path}.{name} is missing";
                    if (Difference(value, right[name], $"{path}.{name}") is { } inner) return inner;
                }
                var added = right.Select(member => member.Key).FirstOrDefault(name => !left.ContainsKey(name));
                return added is null ? null : $"{path}.{added} is new";
            case (JsonArray left, JsonArray right):
                if (left.Count != right.Count) return $"{path} has {right.Count} items, not {left.Count}";
                for (var i = 0; i < left.Count; i++)
                {
                    if (Difference(left[i], right[i], $"{path}[{i}]") is { } inner) return inner;
                }
                return null;
            case (JsonValue left, JsonValue right):
                return SameValue(left, right) ? null : $"{path} is {right.ToJsonString()}, not {left.ToJsonString()}";
            default:
                return $"{path} is {actual?.ToJsonString() ?? "null"}, not {expected?.ToJsonString() ?? "null"}";
        }
    }

    private static bool SameValue(JsonValue left, JsonValue right)
    {
        var (leftKind, rightKind) = (left.GetValueKind(), right.GetValueKind());
        if (leftKind == JsonValueKind.Number || rightKind == JsonValueKind.Number)
            return Number(left, leftKind) is { } leftNumber && Number(right, rightKind) is { } rightNumber && leftNumber == rightNumber;
        if (leftKind != rightKind) return false;
        if (leftKind != JsonValueKind.String) return left.ToJsonString() == right.ToJsonString();
        var (leftText, rightText) = (left.GetValue<string>(), right.GetValue<string>());
        return leftText == rightText
            || (Instant(leftText) is { } leftInstant && Instant(rightText) is { } rightInstant && leftInstant == rightInstant);
    }

    // A number, or a string that is only a number: Tableau writes either for the schema's integers.
    private static decimal? Number(JsonValue value, JsonValueKind kind)
    {
        var text = kind switch
        {
            JsonValueKind.Number => value.ToJsonString(),
            JsonValueKind.String => value.GetValue<string>(),
            _ => null,
        };
        return text is not null && decimal.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint
            | NumberStyles.AllowExponent, CultureInfo.InvariantCulture, out var number) ? number : null;
    }

    private static DateTimeOffset? Instant(string text) =>
        text.Contains('T', StringComparison.Ordinal)
        && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var instant)
            ? instant
            : null;
}
