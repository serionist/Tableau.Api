using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Http.Headers;
using System.Text;

namespace Tableau.Api.Pipeline;

/// <summary>
/// A download's file name and kind, from what the server sent. The name comes from <c>Content-Disposition</c> as written:
/// Tableau's reference gives <c>name="tableau_workbook"; filename="..."</c>, with no disposition type, which .NET's
/// typed header refuses, so the header's raw text is read.
/// </summary>
internal static class DownloadFiles
{
    /// <summary>The longest name kept, as most file systems allow.</summary>
    public const int LongestName = 255;

    // Characters no file name may hold on Windows, refused on every system so a name means the same everywhere.
    private const string Refused = "\"*/:<>?\\|";

    private static readonly Dictionary<string, TableauFileKind> _extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        [".zip"] = TableauFileKind.Zip,
        [".twb"] = TableauFileKind.Workbook,
        [".twbx"] = TableauFileKind.PackagedWorkbook,
        [".tds"] = TableauFileKind.DataSource,
        [".tdsx"] = TableauFileKind.PackagedDataSource,
        [".hyper"] = TableauFileKind.Hyper,
        [".tfl"] = TableauFileKind.Flow,
        [".tflx"] = TableauFileKind.PackagedFlow,
        [".pdf"] = TableauFileKind.Pdf,
        [".png"] = TableauFileKind.Png,
        [".csv"] = TableauFileKind.Csv,
    };

    private static readonly Dictionary<string, TableauFileKind> _mediaTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["application/zip"] = TableauFileKind.Zip,
        ["application/x-zip-compressed"] = TableauFileKind.Zip,
        ["application/pdf"] = TableauFileKind.Pdf,
        ["image/png"] = TableauFileKind.Png,
        ["text/csv"] = TableauFileKind.Csv,
    };

    private static readonly HashSet<string> _devices = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL", "CONIN$", "CONOUT$",
        "COM0", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "COM¹", "COM²", "COM³",
        "LPT0", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9", "LPT¹", "LPT²", "LPT³",
    };

    /// <summary>The bytes <see cref="KindOf(ReadOnlySpan{byte})"/> looks at.</summary>
    public const int SignatureLength = 8;

    /// <summary>The file name the headers give, cleaned, or null: <c>filename*</c> first, then <c>filename</c>.</summary>
    public static string? FileNameOf(HttpContentHeaders headers)
    {
        if (!headers.NonValidated.TryGetValues("Content-Disposition", out var values)) return null;
        string? plain = null;
        string? extended = null;
        foreach (var value in values)
        {
            foreach (var (name, parameter) in Parameters(value))
            {
                if (name.Equals("filename*", StringComparison.OrdinalIgnoreCase)) extended ??= Extended(parameter);
                else if (name.Equals("filename", StringComparison.OrdinalIgnoreCase)) plain ??= parameter;
            }
        }
        return Clean(extended ?? plain);
    }

    /// <summary>
    /// A name the caller can put in a path safely, or null when nothing is left: the part after the last <c>/</c> or
    /// <c>\</c>, so no folder comes with it; without control and format characters, or what Windows refuses; without
    /// the trailing dots and spaces Windows drops; a device name such as <c>CON</c> prefixed with <c>_</c>; and at most
    /// 255 characters, its extension kept.
    /// </summary>
    public static string? Clean(string? name)
    {
        if (name is null) return null;
        var leaf = name[(name.LastIndexOfAny(['/', '\\']) + 1)..];
        var kept = new StringBuilder(leaf.Length);
        foreach (var rune in leaf.EnumerateRunes())
        {
            var category = Rune.GetUnicodeCategory(rune);
            if (category is UnicodeCategory.Control or UnicodeCategory.Format or UnicodeCategory.LineSeparator
                or UnicodeCategory.ParagraphSeparator or UnicodeCategory.Surrogate or UnicodeCategory.PrivateUse
                || (rune.IsAscii && Refused.Contains((char)rune.Value, StringComparison.Ordinal)))
                continue;
            kept.Append(rune.ToString());
        }
        var cleaned = kept.ToString().Trim().TrimEnd('.', ' ');
        if (cleaned.Length == 0) return null;
        var stem = cleaned.Split('.')[0].TrimEnd(' ');
        if (_devices.Contains(stem)) cleaned = "_" + cleaned;
        if (cleaned.Length > LongestName)
        {
            var extension = Path.GetExtension(cleaned);
            if (extension.Length > 16) extension = "";
            var cut = LongestName - extension.Length;
            if (char.IsHighSurrogate(cleaned[cut - 1])) cut--;
            cleaned = cleaned[..cut] + extension;
        }
        return cleaned;
    }

    /// <summary>The kind a name's extension or a media type says, or <see cref="TableauFileKind.Unknown"/>.</summary>
    public static TableauFileKind KindOf(string? fileName, string? mediaType)
    {
        if (fileName is not null && _extensions.TryGetValue(Path.GetExtension(fileName), out var byName)) return byName;
        if (mediaType is not null && _mediaTypes.TryGetValue(mediaType, out var byType)) return byType;
        return TableauFileKind.Unknown;
    }

    /// <summary>The kind a file's first bytes say: a zip's, a PDF's or a PNG's signature.</summary>
    public static TableauFileKind KindOf(ReadOnlySpan<byte> start) => start switch
    {
        [(byte)'P', (byte)'K', 3, 4, ..] or [(byte)'P', (byte)'K', 5, 6, ..] or [(byte)'P', (byte)'K', 7, 8, ..] => TableauFileKind.Zip,
        [(byte)'%', (byte)'P', (byte)'D', (byte)'F', (byte)'-', ..] => TableauFileKind.Pdf,
        [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A, ..] => TableauFileKind.Png,
        _ => TableauFileKind.Unknown,
    };

    // The header's parameters, split at semicolons outside quotes; a quoted value unquoted, with \" read as a quote and
    // any other backslash kept, as servers write Windows paths unescaped.
    private static IEnumerable<(string Name, string Value)> Parameters(string header)
    {
        var position = 0;
        while (position < header.Length)
        {
            var end = position;
            var quoted = false;
            while (end < header.Length && (quoted || header[end] != ';'))
            {
                if (header[end] == '\\' && quoted && end + 1 < header.Length && header[end + 1] == '"') end++;
                else if (header[end] == '"') quoted = !quoted;
                end++;
            }
            var part = header[position..end];
            position = end + 1;
            var equals = part.IndexOf('=', StringComparison.Ordinal);
            if (equals <= 0) continue;
            var value = part[(equals + 1)..].Trim();
            if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
                value = value[1..^1].Replace("\\\"", "\"", StringComparison.Ordinal);
            yield return (part[..equals].Trim(), value);
        }
    }

    // RFC 8187's charset'language'percent-encoded form, in UTF-8 or ISO-8859-1; null for another charset.
    private static string? Extended(string value)
    {
        var parts = value.Split('\'', 3);
        if (parts.Length != 3) return null;
        var encoding = parts[0].ToUpperInvariant() switch
        {
            "UTF-8" => Encoding.UTF8,
            "ISO-8859-1" => Encoding.Latin1,
            _ => null,
        };
        if (encoding is null) return null;
        var bytes = new List<byte>(parts[2].Length);
        for (var i = 0; i < parts[2].Length; i++)
        {
            if (parts[2][i] == '%' && i + 2 < parts[2].Length
                && byte.TryParse(parts[2].AsSpan(i + 1, 2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var b))
            {
                bytes.Add(b);
                i += 2;
            }
            else
            {
                bytes.AddRange(Encoding.UTF8.GetBytes(parts[2][i].ToString()));
            }
        }
        return encoding.GetString([.. bytes]);
    }
}
