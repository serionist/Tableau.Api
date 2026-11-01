using System;
using System.Collections.Generic;
using System.Globalization;

namespace Tableau.Api;

/// <summary>
/// What an export of a view or a workbook asks for: view filters, how old a cached export may be, an image's
/// resolution and size, and a PDF's pages. Each export takes some of them, as each member says, and refuses one it
/// doesn't take with <see cref="ArgumentException"/> before anything is sent.
/// </summary>
/// <remarks>
/// <para>Which export takes which, as Tableau's reference lists their query parameters:</para>
/// <list type="table">
/// <listheader><term>Export</term><description>Takes</description></listheader>
/// <item><term><see cref="ViewsClient.DownloadDataAsync"/>, CSV</term><description><see cref="Filters"/> alone</description></item>
/// <item><term><see cref="ViewsClient.DownloadImageAsync"/>, PNG</term><description><see cref="Filters"/>, <see cref="MaxAge"/>, <see cref="Resolution"/>, <see cref="Width"/> and <see cref="Height"/> (REST API 3.24)</description></item>
/// <item><term><see cref="ViewsClient.DownloadPdfAsync"/></term><description><see cref="Filters"/>, <see cref="MaxAge"/>, <see cref="Width"/> and <see cref="Height"/>, <see cref="PageType"/>, <see cref="Orientation"/></description></item>
/// <item><term><see cref="ViewsClient.DownloadCrosstabAsync"/>, .xlsx</term><description><see cref="Filters"/>, <see cref="MaxAge"/></description></item>
/// <item><term><see cref="WorkbooksClient.DownloadPdfAsync"/></term><description><see cref="Filters"/> (REST API 3.24), <see cref="MaxAge"/>, <see cref="Width"/> and <see cref="Height"/> (3.26), <see cref="PageType"/>, <see cref="Orientation"/></description></item>
/// </list>
/// <para>
/// An option below its version, such as a workbook PDF's size on REST API 3.25, is refused with
/// <see cref="TableauException"/> (<see cref="TableauErrorKind.UnsupportedApiVersion"/>) before anything is sent. A
/// member left null sends nothing, and Tableau uses its default.
/// </para>
/// </remarks>
public sealed record ExportOptions
{
    /// <summary>
    /// View filters, by field name: each is sent as <c>vf_</c> and the name, such as <c>vf_Region=West</c>, so the export
    /// holds only the marks whose field has that value. Every export takes them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Write a name and its value as the view's data holds them, case included, not URL-encoded: the client escapes both.
    /// A name that starts with <c>vf_</c> is sent as it is. A date is <c>yyyy-mm-dd hh:mm:ss</c>. An empty dictionary
    /// sends no filter.
    /// </para>
    /// <para>
    /// A value of several, comma-separated, such as <c>2025,2026</c>, keeps any of them. With more than one field,
    /// Tableau's reference links the values by position: <c>Year</c> <c>2025,2026</c> and <c>Region</c> <c>West,East</c>
    /// keep 2025 in the west and 2026 in the east, so every field must hold as many values, and filters that don't are
    /// refused before anything is sent. A comma a value holds as data is written <c>\,</c>, and a backslash <c>\\</c>, as
    /// Tableau's Python client found the server reads them; the reference names neither.
    /// </para>
    /// <para>
    /// A workbook parameter is <c>Parameters.</c> and its name, such as <c>Parameters.Top N</c>, sent as
    /// <c>vf_Parameters.Top N</c>, as Tableau's Python client sends one; the reference doesn't document parameters.
    /// </para>
    /// </remarks>
    public IReadOnlyDictionary<string, string>? Filters { get; init; }

    /// <summary>
    /// How many minutes old a cached export may be before Tableau makes it again, one at least; null for Tableau's own
    /// cache. Images, PDFs and crosstabs take it, not a view's data.
    /// </summary>
    public int? MaxAge { get; init; }

    /// <summary>A view image's resolution; <see cref="ImageResolution.High"/> for the most pixels. Images alone take it.</summary>
    public ImageResolution? Resolution { get; init; }

    /// <summary>
    /// The width the view is drawn at, in pixels, which with <see cref="Height"/> sets the export's resolution and aspect
    /// ratio; the two go together. A view's image takes it from REST API 3.24, a view's PDF, and a workbook's PDF from
    /// REST API 3.26.
    /// </summary>
    public int? Width { get; init; }

    /// <summary>The height the view is drawn at, in pixels; it goes with <see cref="Width"/>, which says what takes it.</summary>
    public int? Height { get; init; }

    /// <summary>A PDF's page size; Tableau's default is <see cref="Tableau.Api.PageType.Legal"/>. PDFs alone take it.</summary>
    public PageType? PageType { get; init; }

    /// <summary>A PDF's page orientation; Tableau's default is <see cref="PageOrientation.Portrait"/>. PDFs alone take it.</summary>
    public PageOrientation? Orientation { get; init; }
}

/// <summary>A view image's resolution, for <see cref="ExportOptions.Resolution"/>.</summary>
public enum ImageResolution
{
    /// <summary>Tableau's default, which sends nothing.</summary>
    Standard,

    /// <summary><c>resolution=high</c>: the most pixels Tableau draws the view at.</summary>
    High,
}

/// <summary>A PDF's page size, for <see cref="ExportOptions.PageType"/>, sent as Tableau's reference writes it.</summary>
public enum PageType
{
    /// <summary>A3.</summary>
    A3,

    /// <summary>A4.</summary>
    A4,

    /// <summary>A5.</summary>
    A5,

    /// <summary>B5.</summary>
    B5,

    /// <summary>Executive.</summary>
    Executive,

    /// <summary>Folio.</summary>
    Folio,

    /// <summary>Ledger.</summary>
    Ledger,

    /// <summary>Legal, Tableau's default.</summary>
    Legal,

    /// <summary>Letter.</summary>
    Letter,

    /// <summary>Note.</summary>
    Note,

    /// <summary>Quarto.</summary>
    Quarto,

    /// <summary>Tabloid.</summary>
    Tabloid,
}

/// <summary>A PDF's page orientation, for <see cref="ExportOptions.Orientation"/>.</summary>
public enum PageOrientation
{
    /// <summary>Portrait, Tableau's default.</summary>
    Portrait,

    /// <summary>Landscape.</summary>
    Landscape,
}

/// <summary>The exports <see cref="ExportOptions"/> serves, each taking the options its reference lists.</summary>
internal enum ExportKind
{
    /// <summary>Query View Data.</summary>
    ViewData,

    /// <summary>Query View Image.</summary>
    ViewImage,

    /// <summary>Query View PDF.</summary>
    ViewPdf,

    /// <summary>Download View Crosstab Excel.</summary>
    ViewCrosstab,

    /// <summary>Download Workbook PDF.</summary>
    WorkbookPdf,
}

/// <summary>
/// The one place an export's options become its query: each option checked against the export, then written in one
/// order, the view filters last, each with the <c>vf_</c> prefix <see cref="FilterName"/> gives it.
/// </summary>
internal static class ExportQuery
{
    /// <summary>
    /// The query of an export, or <see cref="ArgumentException"/> naming <paramref name="parameter"/> for an option it
    /// doesn't take.
    /// </summary>
    /// <param name="export">The export.</param>
    /// <param name="options">Its options, or null for none.</param>
    /// <param name="parameter">The parameter the options came in.</param>
    public static List<KeyValuePair<string, string?>> For(ExportKind export, ExportOptions? options, string parameter)
    {
        var query = new List<KeyValuePair<string, string?>>();
        if (options is null) return query;
        var image = export == ExportKind.ViewImage;
        var pdf = export is ExportKind.ViewPdf or ExportKind.WorkbookPdf;
        Take(export, options.PageType is not null, pdf, nameof(ExportOptions.PageType), parameter);
        Take(export, options.Orientation is not null, pdf, nameof(ExportOptions.Orientation), parameter);
        Take(export, options.Resolution is not null, image, nameof(ExportOptions.Resolution), parameter);
        Take(export, options.MaxAge is not null, export != ExportKind.ViewData, nameof(ExportOptions.MaxAge), parameter);
        Take(export, options.Width is not null || options.Height is not null, image || pdf, "Width and Height", parameter);

        if (options.PageType is { } pageType) query.Add(new("type", PageTypeText(pageType, parameter)));
        if (options.Orientation is { } orientation) query.Add(new("orientation", OrientationText(orientation, parameter)));
        if (options.Resolution is { } resolution && ResolutionSent(resolution, parameter)) query.Add(new("resolution", "high"));
        if (options.MaxAge is { } maxAge)
        {
            if (maxAge < 1)
                throw new ArgumentException($"ExportOptions.MaxAge is {Number(maxAge)}: a cached export's age is a minute at least.", parameter);
            query.Add(new("maxAge", Number(maxAge)));
        }
        if (options.Width is not null || options.Height is not null)
        {
            if (options.Width is not { } width || options.Height is not { } height)
                throw new ArgumentException("ExportOptions.Width and Height go together: Tableau sizes the view by both.", parameter);
            if (width < 1 || height < 1)
                throw new ArgumentException($"ExportOptions.Width and Height are {Number(width)} and {Number(height)}: each is a pixel at least.", parameter);
            query.Add(new("vizWidth", Number(width)));
            query.Add(new("vizHeight", Number(height)));
        }
        int? values = null;
        foreach (var (name, value) in options.Filters ?? new Dictionary<string, string>())
        {
            if (value is null) throw new ArgumentException($"The view filter '{name}' has no value.", parameter);
            // Tableau links the values of several fields by position, and its reference asks every field for as many.
            var count = ValueCount(value);
            if (values is { } first && first != count)
            {
                throw new ArgumentException(string.Create(CultureInfo.InvariantCulture,
                    $"The view filters hold {first} and {count} values: with more than one field, Tableau links their values by position, ")
                    + "so every field must hold as many.", parameter);
            }
            values = count;
            query.Add(new(FilterName(name, parameter), value));
        }
        return query;
    }

    // How many comma-separated values a view filter holds; an escaped comma, \,, is one value's data.
    private static int ValueCount(string value)
    {
        var count = 1;
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] == '\\') i++;
            else if (value[i] == ',') count++;
        }
        return count;
    }

    /// <summary>
    /// A view filter's query key: <c>vf_</c> and the field's name, unless the name starts with <c>vf_</c> already, by an
    /// ordinal comparison, case included, as Tableau compares a field's name.
    /// </summary>
    /// <exception cref="ArgumentException">The name is empty, or <c>vf_</c> alone.</exception>
    public static string FilterName(string name, string parameter)
    {
        var key = name.StartsWith("vf_", StringComparison.Ordinal) ? name : "vf_" + name;
        return key.Length > 3 ? key : throw new ArgumentException("A view filter names no field.", parameter);
    }

    /// <summary>What an export takes, in words, for a refusal.</summary>
    private static string Takes(ExportKind export) => export switch
    {
        ExportKind.ViewData => "Query View Data takes Filters alone",
        ExportKind.ViewImage => "Query View Image takes Filters, MaxAge, Resolution, and Width and Height",
        ExportKind.ViewPdf => "Query View PDF takes Filters, MaxAge, Width and Height, PageType and Orientation",
        ExportKind.ViewCrosstab => "Download View Crosstab Excel takes Filters and MaxAge",
        _ => "Download Workbook PDF takes Filters, MaxAge, Width and Height, PageType and Orientation",
    };

    private static void Take(ExportKind export, bool set, bool takes, string option, string parameter)
    {
        if (set && !takes)
            throw new ArgumentException($"ExportOptions.{option} isn't for this export: {Takes(export)}, as Tableau's reference lists them.", parameter);
    }

    private static string PageTypeText(PageType value, string parameter) => value switch
    {
        PageType.A3 => "A3",
        PageType.A4 => "A4",
        PageType.A5 => "A5",
        PageType.B5 => "B5",
        PageType.Executive => "Executive",
        PageType.Folio => "Folio",
        PageType.Ledger => "Ledger",
        PageType.Legal => "Legal",
        PageType.Letter => "Letter",
        PageType.Note => "Note",
        PageType.Quarto => "Quarto",
        PageType.Tabloid => "Tabloid",
        _ => throw new ArgumentException($"ExportOptions.PageType is {Number((int)value)}, which names no page type.", parameter),
    };

    private static string OrientationText(PageOrientation value, string parameter) => value switch
    {
        PageOrientation.Portrait => "Portrait",
        PageOrientation.Landscape => "Landscape",
        _ => throw new ArgumentException($"ExportOptions.Orientation is {Number((int)value)}, which names no orientation.", parameter),
    };

    // Standard is Tableau's default, which no parameter names.
    private static bool ResolutionSent(ImageResolution value, string parameter) => value switch
    {
        ImageResolution.Standard => false,
        ImageResolution.High => true,
        _ => throw new ArgumentException($"ExportOptions.Resolution is {Number((int)value)}, which names no resolution.", parameter),
    };

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
}
