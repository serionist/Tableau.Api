namespace Tableau.Api;

/// <summary>
/// What a <see cref="TableauDownload"/> holds, as its file name's extension, its media type or its first bytes say. A
/// minor version may add kinds.
/// </summary>
public enum TableauFileKind
{
    /// <summary>Nothing said what it is.</summary>
    Unknown,

    /// <summary>A zip archive the other kinds don't name, such as an Excel crosstab, known by its first bytes.</summary>
    Zip,

    /// <summary>A workbook, <c>.twb</c>: its XML alone.</summary>
    Workbook,

    /// <summary>A packaged workbook, <c>.twbx</c>: a zip of the workbook with its extracts and images.</summary>
    PackagedWorkbook,

    /// <summary>A data source, <c>.tds</c>: its XML alone.</summary>
    DataSource,

    /// <summary>A packaged data source, <c>.tdsx</c>: a zip of the data source with its extract.</summary>
    PackagedDataSource,

    /// <summary>An extract, <c>.hyper</c>.</summary>
    Hyper,

    /// <summary>A Tableau Prep flow, <c>.tfl</c>.</summary>
    Flow,

    /// <summary>A packaged Tableau Prep flow, <c>.tflx</c>.</summary>
    PackagedFlow,

    /// <summary>A PDF, such as a view's export.</summary>
    Pdf,

    /// <summary>A PNG image, such as a view's image.</summary>
    Png,

    /// <summary>Comma-separated values, such as a view's data.</summary>
    Csv,
}
