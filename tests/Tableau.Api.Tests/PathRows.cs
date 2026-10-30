using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Tableau.Api.Tests;

/// <summary>
/// The values the path tests put in a request path, and what each must become there: its UTF-8 bytes, each one raw
/// when it is an unreserved character or one of : @ ! ' ( ) * and ,, and escaped in upper-case hex otherwise. Written
/// apart from the library's escaping, from the rule alone.
/// </summary>
internal static class PathRows
{
    private const string Raw = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-._~:@!'(),*";

    /// <summary>The eight characters outside the unreserved set that a path segment keeps raw.</summary>
    public const string SubDelimiters = ":@!'()*,";

    /// <summary>Unicode's bidirectional formatting characters.</summary>
    public static readonly char[] BidiControls =
        ['؜', '‎', '‏', '‪', '‫', '‬', '‭', '‮', '⁦', '⁧', '⁨', '⁩'];

    /// <summary>Every ASCII character alone, inside a value, at its start and its end, and beside a non-ASCII one.</summary>
    public static IEnumerable<string> Ascii()
    {
        for (var code = 0; code < 0x80; code++)
        {
            var c = ((char)code).ToString();
            yield return c;
            yield return "a" + c + "b";
            yield return c + "ab";
            yield return "ab" + c;
            yield return "é" + c;
            yield return c + "é";
        }
    }

    /// <summary>Values that are empty or only dots, which name no resource.</summary>
    public static IEnumerable<string> DotOnly() => ["", ".", "..", "...", "...."];

    /// <summary>Each of the eight raw characters beside non-ASCII ones, and names as people write them.</summary>
    public static IEnumerable<string> SubDelimitersBesideNonAscii()
    {
        yield return "Équipe d'analyse";
        yield return "coût (estimé)";
        yield return "Ventes, Q1 : réel*";
        yield return "équipe@siège!";
        foreach (var c in SubDelimiters)
        {
            yield return "é" + c;
            yield return c + "é";
            yield return "é" + c + "é";
        }
    }

    /// <summary>Each bidi control alone, and inside values.</summary>
    public static IEnumerable<string> Bidi()
    {
        foreach (var c in BidiControls)
        {
            yield return c.ToString();
            yield return "a" + c + "b";
            yield return "." + c + ".";
        }
    }

    public static IEnumerable<string> All() =>
        Ascii().Concat(DotOnly()).Concat(SubDelimitersBesideNonAscii()).Concat(Bidi()).Distinct(StringComparer.Ordinal);

    public static bool IsSubDelimiterRow(string value) =>
        value.Any(c => c > 0x7F) && value.Any(c => SubDelimiters.IndexOf(c) >= 0) && !value.Any(IsBidiControl);

    public static bool IsRefused(string value) => value.All(c => c == '.');

    public static bool IsBidiControl(char c) => Array.IndexOf(BidiControls, c) >= 0;

    /// <summary>The value as one escaped path segment.</summary>
    public static string Escaped(string value)
    {
        var escaped = new StringBuilder();
        foreach (var b in Encoding.UTF8.GetBytes(value))
        {
            if (b < 0x80 && Raw.IndexOf((char)b) >= 0) escaped.Append((char)b);
            else escaped.Append('%').Append(b.ToString("X2", CultureInfo.InvariantCulture));
        }
        return escaped.ToString();
    }

    /// <summary>The segments of a path as sent, each unescaped, as a server reads them.</summary>
    public static string[] Segments(string path) => [.. path.Split('/').Select(Uri.UnescapeDataString)];

    /// <summary>The value with each control or non-ASCII character as \uXXXX, so a failure shows what it was.</summary>
    public static string Show(string value)
    {
        var shown = new StringBuilder("\"");
        foreach (var c in value)
        {
            if (c < 0x20 || c > 0x7E) shown.Append("\\u").Append(((int)c).ToString("X4", CultureInfo.InvariantCulture));
            else shown.Append(c);
        }
        return shown.Append('"').ToString();
    }
}
