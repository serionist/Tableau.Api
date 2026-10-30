using System;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;

namespace Tableau.Api;

/// <summary>
/// A request path under the API version, written as an interpolated string. The literal text is the path as
/// written; each hole is the value of one path segment, escaped by <see cref="UriEscaping"/>. A segment that holds
/// a value and is empty or only dots is refused, since it names no resource. buildUri takes only this type, so
/// every request path is built here.
/// </summary>
[InterpolatedStringHandler]
internal sealed class RequestPath
{
    private static readonly Regex MemberAccess = new(@"^([A-Za-z_][A-Za-z0-9_]*)(\.[A-Za-z_][A-Za-z0-9_]*)?$",
        RegexOptions.CultureInvariant);

    private readonly StringBuilder _escaped;
    // The current segment as the server will read it, to check it once the segment ends.
    private readonly StringBuilder _segment = new();
    private string _segmentValue;

    public RequestPath(int literalLength, int formattedCount)
    {
        _escaped = new StringBuilder(literalLength + 40 * formattedCount);
    }

    public void AppendLiteral(string value)
    {
        foreach (var c in value)
        {
            if (c == '/') EndSegment();
            else _segment.Append(c);
        }
        _escaped.Append(value);
    }

    public void AppendFormatted<T>(T value, [CallerArgumentExpression("value")] string expression = null)
    {
        var text = (value is IFormattable formattable
            ? formattable.ToString(null, CultureInfo.InvariantCulture)
            : value?.ToString()) ?? "";
        _segment.Append(text);
        _segmentValue = _segmentValue ?? expression ?? "";
        UriEscaping.AppendPathSegment(_escaped, text);
    }

    /// <summary>The path with every value escaped, once its last segment is checked.</summary>
    public string ToEscapedPath()
    {
        EndSegment();
        return _escaped.ToString();
    }

    private void EndSegment()
    {
        if (_segmentValue != null && IsEmptyOrDots(_segment))
        {
            var match = MemberAccess.Match(_segmentValue);
            throw new ArgumentException(
                $"{(_segmentValue.Length > 0 ? _segmentValue : "A value")} is '{_segment}', but a value in a request path "
                + "can't be empty or only dots: it would name no resource.",
                match.Success ? match.Groups[1].Value : null);
        }
        _segment.Clear();
        _segmentValue = null;
    }

    private static bool IsEmptyOrDots(StringBuilder segment)
    {
        for (var i = 0; i < segment.Length; i++)
            if (segment[i] != '.') return false;
        return true;
    }
}

/// <summary>Percent-encoding for request URIs: UTF-8, upper-case hex, the form <see cref="Uri"/> writes.</summary>
internal static class UriEscaping
{
    private const string Hex = "0123456789ABCDEF";
    // Throws on an unpaired surrogate, which UTF-8 can't encode, rather than sending U+FFFD in its place.
    private static readonly Encoding StrictUtf8 = new UTF8Encoding(false, true);

    /// <summary>
    /// One path segment: every character outside RFC 3986's unreserved set is escaped, except : @ ! ' ( ) * and ,.
    /// Those eight are allowed raw in a segment and separate nothing in it; 1.2.5 sent them raw, as 1.2.6 does, and
    /// Tableau's upload session ids hold colons.
    /// </summary>
    public static void AppendPathSegment(StringBuilder into, string value) => Append(into, value, true);

    /// <summary>One query key or value: every character outside RFC 3986's unreserved set is escaped.</summary>
    public static void AppendQueryPart(StringBuilder into, string value) => Append(into, value, false);

    private static void Append(StringBuilder into, string value, bool pathSegment)
    {
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (IsUnreserved(c) || (pathSegment && IsRawInSegment(c)))
            {
                into.Append(c);
            }
            else if (c < 0x80)
            {
                AppendByte(into, (byte)c);
            }
            else
            {
                var start = i;
                while (i + 1 < value.Length && value[i + 1] >= 0x80) i++;
                foreach (var b in StrictUtf8.GetBytes(value.Substring(start, i - start + 1))) AppendByte(into, b);
            }
        }
    }

    private static void AppendByte(StringBuilder into, byte b) => into.Append('%').Append(Hex[b >> 4]).Append(Hex[b & 0xF]);

    private static bool IsUnreserved(char c) =>
        (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '-' || c == '.' || c == '_'
        || c == '~';

    private static bool IsRawInSegment(char c) =>
        c == ':' || c == '@' || c == '!' || c == '\'' || c == '(' || c == ')' || c == '*' || c == ',';
}
