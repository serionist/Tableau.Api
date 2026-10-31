using System.Text;

namespace Tableau.Api.Pipeline;

/// <summary>
/// Percent-encoding for request URIs: UTF-8, upper-case hex, the form <see cref="System.Uri"/> writes. Both clients
/// build their paths and queries with it, the old one through its RequestPath.
/// </summary>
internal static class UriEscaping
{
    private const string Hex = "0123456789ABCDEF";
    // Throws on an unpaired surrogate, which UTF-8 can't encode, rather than sending U+FFFD in its place.
    private static readonly Encoding _strictUtf8 = new UTF8Encoding(false, true);

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
                foreach (var b in _strictUtf8.GetBytes(value[start..(i + 1)])) AppendByte(into, b);
            }
        }
    }

    private static void AppendByte(StringBuilder into, byte b) => into.Append('%').Append(Hex[b >> 4]).Append(Hex[b & 0xF]);

    private static bool IsUnreserved(char c) =>
        c is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-' or '.' or '_' or '~';

    private static bool IsRawInSegment(char c) => c is ':' or '@' or '!' or '\'' or '(' or ')' or '*' or ',';
}
