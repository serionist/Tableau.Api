using System;
using System.Globalization;

namespace Tableau.Api.Pipeline;

/// <summary>
/// The REST API versions the client knows. A version is major.minor only: the URI takes no more, and two versions
/// compare whole, so 3.9 is below 3.10.
/// </summary>
internal static class ApiVersions
{
    /// <summary>
    /// The oldest version the client supports: Tableau Server 2024.2's, the oldest release Tableau still supports. A
    /// server below it is refused unless the caller sets <see cref="TableauClientOptions.AllowUnsupportedServerVersion"/>.
    /// </summary>
    public static readonly Version Floor = new(3, 23);

    /// <summary>The newest version the client knows, its schema's: a server that offers more is spoken to at this one.</summary>
    public static readonly Version Newest = new(3, 29);

    /// <summary>The oldest version Server Info answers at, for a server too old to answer at <see cref="Floor"/>.</summary>
    public static readonly Version ServerInfoOldest = new(2, 4);

    /// <summary>The version as the URI writes it: 3.25.</summary>
    public static string Format(Version version) =>
        string.Create(CultureInfo.InvariantCulture, $"{version.Major}.{version.Minor}");

    /// <summary>Whether the version names a major and a minor and nothing more.</summary>
    public static bool IsMajorMinor(Version version) => version.Build < 0 && version.Revision < 0;

    /// <summary>
    /// The major and minor of a version Tableau sends, such as 3.25, or null when the text is no version. A schema's
    /// update number after them is dropped, as the URI takes none.
    /// </summary>
    public static Version? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var parts = text.Trim().Split('.');
        return parts.Length >= 2
            && int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var major)
            && int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var minor)
                ? new Version(major, minor)
                : null;
    }

    /// <summary>The version as major.minor, whatever more it names.</summary>
    public static Version MajorMinor(Version version) => new(version.Major, version.Minor);
}
