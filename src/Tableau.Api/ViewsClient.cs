using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Tableau.Api.Pipeline;
using Tableau.Api.Rest.Models;

namespace Tableau.Api;

/// <summary>The site's views and their exports: <see cref="TableauClient.Views"/>.</summary>
/// <remarks>
/// <para>
/// A view exports as its data (CSV), an image (PNG), a PDF or a crosstab (an .xlsx), each a <see cref="TableauDownload"/>
/// that streams from the server as it is read: dispose it. An export of a dashboard holds its first view's data alone,
/// and a story's crosstab Tableau doesn't make. Tableau may answer an image, a PDF or a crosstab from its cache:
/// <see cref="ExportOptions.MaxAge"/> bounds its age.
/// </para>
/// <para>
/// <see cref="ExportOptions"/> holds every export's options; each export takes those its reference lists, and refuses
/// another with <see cref="ArgumentException"/> before anything is sent. View filters go as <c>vf_</c> and the field's
/// name on every export.
/// </para>
/// </remarks>
public class ViewsClient
{
    private const string Resource = "views";

    // Made once and never changed: the oldest first, then by workbook and by name, which no two views of a workbook share,
    // so pages don't shift between requests. Tableau Server 2025.1 refuses a sort on the content URL.
    private static readonly TableauSort _stable = TableauSort.Asc(ViewField.CreatedAt).Then(TableauSort.Asc(ViewField.WorkbookName))
        .Then(TableauSort.Asc(ViewField.Name));

    // The query that asks for each view's usage.
    private static readonly IReadOnlyList<KeyValuePair<string, string?>> _withUsage = [new("includeUsageStatistics", "true")];

    private readonly TableauPipeline? _pipeline;

    /// <summary>A sub-client with no connection, for a mock to derive from: its methods throw until overridden.</summary>
    protected ViewsClient()
    {
    }

    internal ViewsClient(TableauPipeline pipeline)
    {
        _pipeline = pipeline;
    }

    private TableauPipeline Pipeline => _pipeline ?? throw TableauClient.ForMocksOnly();

    /// <summary>
    /// Query Views for Site, every page: the views of the site the user can see, read a page at a time as you go, until
    /// the list ends. It reads its pages through <see cref="ListPageAsync"/>, so a mock that overrides that serves this too.
    /// </summary>
    /// <remarks>
    /// Without a sort the oldest come first, then by their workbook's name and their own, so the pages of a list that
    /// doesn't change neither skip nor repeat a view, but for two workbooks of one name in two projects published in the
    /// same second; a sort you give is followed by those of the three it doesn't name.
    /// </remarks>
    /// <param name="options">The filter, the sort, the page size and whether each view comes with its usage; null for every view, 100 a page.</param>
    /// <param name="cancellationToken">Stops the list, between views and between pages.</param>
    /// <returns>The views, as you read them, each with its workbook's id.</returns>
    /// <exception cref="ArgumentException">The filter or the sort names another resource's field, or the page size is outside 1 to 1,000.</exception>
    /// <exception cref="TableauException">A page's call failed; <see cref="TableauException.Kind"/> says how.</exception>
    public virtual async IAsyncEnumerable<View> ListAsync(ViewListOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var view in TableauPaging.ListAsync((number, token) => ListPageAsync(number, options, token), Endpoints.QueryViewsForSite.Name,
                               null, cancellationToken)
                           .ConfigureAwait(false))
            yield return view;
    }

    /// <summary>Query Views for Site, one page.</summary>
    /// <param name="pageNumber">The page's number, from 1.</param>
    /// <param name="options">The filter, the sort, the page size and whether each view comes with its usage; null for every view, 100 a page.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The page, its views empty, never null, when it has none.</returns>
    /// <exception cref="ArgumentException">
    /// The page number is below 1, the page size outside 1 to 1,000, or the filter or the sort names another resource's
    /// field.
    /// </exception>
    /// <exception cref="TableauException">The call failed; <see cref="TableauException.Kind"/> says how.</exception>
    public virtual Task<Page<View>> ListPageAsync(int pageNumber, ViewListOptions? options = null, CancellationToken cancellationToken = default)
    {
        options ??= new ViewListOptions();
        return TableauPaging.PageAsync(Pipeline, Endpoints.QueryViewsForSite, null, pageNumber, options.PageSize, options.Filter, options.Sort,
            _stable, Resource, static body => body.Views, options.IncludeUsageStatistics ? _withUsage : null, cancellationToken);
    }

    /// <summary>Get View: the view, with its workbook, owner, project and tags.</summary>
    /// <param name="id">The view's LUID.</param>
    /// <param name="includeUsageStatistics">True for its <see cref="View.Usage"/>, how often it was seen.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The view.</returns>
    /// <exception cref="ArgumentException"><paramref name="id"/> is no LUID.</exception>
    /// <exception cref="TableauException">The call failed; <see cref="TableauException.Kind"/> says how.</exception>
    public virtual Task<View> GetAsync(string id, bool includeUsageStatistics = false, CancellationToken cancellationToken = default) =>
        Pipeline.SendAsync(Endpoints.GetView, new TableauRequest([RequestUri.Luid(id, nameof(id))], includeUsageStatistics ? _withUsage : null),
            static body => body.View, "view", cancellationToken);

    /// <summary>Query Views for Workbook: the workbook's views, from one call, in Tableau's order.</summary>
    /// <param name="workbookId">The workbook's LUID.</param>
    /// <param name="includeUsageStatistics">True for each view's <see cref="View.Usage"/>, how often it was seen.</param>
    /// <param name="cancellationToken">Stops the list, and cancels its call.</param>
    /// <returns>The views, as you read them.</returns>
    /// <exception cref="ArgumentException"><paramref name="workbookId"/> is no LUID, which the first read throws.</exception>
    /// <exception cref="TableauException">The call failed; <see cref="TableauException.Kind"/> says how.</exception>
    public virtual async IAsyncEnumerable<View> ListForWorkbookAsync(string workbookId, bool includeUsageStatistics = false,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        RequestUri.Luid(workbookId, nameof(workbookId));
        var body = await Pipeline.SendAsync(Endpoints.QueryViewsForWorkbook, new TableauRequest([workbookId], includeUsageStatistics ? _withUsage : null),
            static body => body, "a body", cancellationToken).ConfigureAwait(false);
        foreach (var view in body.Views ?? [])
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return view;
        }
    }

    /// <summary>
    /// Query View Data: the view's data, summarised as the view shows it, as comma-separated values; a dashboard's first
    /// view's. Detail rows only Tableau's own interface downloads.
    /// </summary>
    /// <param name="id">The view's LUID.</param>
    /// <param name="options">Its <see cref="ExportOptions.Filters"/>, the one option it takes; null for none.</param>
    /// <param name="cancellationToken">Cancels the call, and stops reading the data.</param>
    /// <returns>The CSV: dispose it once read or saved.</returns>
    /// <exception cref="ArgumentException"><paramref name="id"/> is no LUID, or <paramref name="options"/> sets an option other than its filters: nothing is sent.</exception>
    /// <exception cref="TableauException">The call failed; <see cref="TableauException.Kind"/> says how.</exception>
    public virtual Task<TableauDownload> DownloadDataAsync(string id, ExportOptions? options = null, CancellationToken cancellationToken = default) =>
        ExportAsync(Endpoints.QueryViewData, ExportKind.ViewData, id, options, cancellationToken);

    /// <summary>Query View Image: the view drawn as a PNG.</summary>
    /// <param name="id">The view's LUID.</param>
    /// <param name="options">
    /// Its <see cref="ExportOptions.Filters"/>, <see cref="ExportOptions.MaxAge"/>, <see cref="ExportOptions.Resolution"/>,
    /// and <see cref="ExportOptions.Width"/> and <see cref="ExportOptions.Height"/> (from REST API 3.24); null for
    /// Tableau's defaults.
    /// </param>
    /// <param name="cancellationToken">Cancels the call, and stops reading the image.</param>
    /// <returns>The image: dispose it once read or saved.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="id"/> is no LUID, or <paramref name="options"/> sets a PDF's page, or a value it sets is out of
    /// range: nothing is sent.
    /// </exception>
    /// <exception cref="TableauException">
    /// The call failed; <see cref="TableauException.Kind"/> says how, <see cref="TableauErrorKind.UnsupportedApiVersion"/>
    /// for a size below REST API 3.24, before anything is sent.
    /// </exception>
    public virtual Task<TableauDownload> DownloadImageAsync(string id, ExportOptions? options = null, CancellationToken cancellationToken = default) =>
        ExportAsync(options?.Width is null ? Endpoints.QueryViewImage : Endpoints.QueryViewImageSized, ExportKind.ViewImage, id, options, cancellationToken);

    /// <summary>Query View PDF: the view as a PDF.</summary>
    /// <param name="id">The view's LUID.</param>
    /// <param name="options">
    /// Its <see cref="ExportOptions.Filters"/>, <see cref="ExportOptions.MaxAge"/>, <see cref="ExportOptions.Width"/> and
    /// <see cref="ExportOptions.Height"/>, <see cref="ExportOptions.PageType"/> and <see cref="ExportOptions.Orientation"/>;
    /// null for Tableau's defaults, Legal and portrait.
    /// </param>
    /// <param name="cancellationToken">Cancels the call, and stops reading the file.</param>
    /// <returns>The PDF: dispose it once read or saved.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="id"/> is no LUID, or <paramref name="options"/> sets <see cref="ExportOptions.Resolution"/>, or a
    /// value it sets is out of range: nothing is sent.
    /// </exception>
    /// <exception cref="TableauException">The call failed; <see cref="TableauException.Kind"/> says how.</exception>
    public virtual Task<TableauDownload> DownloadPdfAsync(string id, ExportOptions? options = null, CancellationToken cancellationToken = default) =>
        ExportAsync(Endpoints.QueryViewPdf, ExportKind.ViewPdf, id, options, cancellationToken);

    /// <summary>
    /// Download View Crosstab Excel: the view's data as a crosstab in an .xlsx, summarised as the view shows it; a
    /// dashboard's first view's. Its <see cref="TableauDownload.Kind"/> is <see cref="TableauFileKind.Zip"/>, an .xlsx's.
    /// </summary>
    /// <param name="id">The view's LUID.</param>
    /// <param name="options">Its <see cref="ExportOptions.Filters"/> and <see cref="ExportOptions.MaxAge"/>; null for none.</param>
    /// <param name="cancellationToken">Cancels the call, and stops reading the file.</param>
    /// <returns>The workbook of the crosstab: dispose it once read or saved.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="id"/> is no LUID, or <paramref name="options"/> sets an option other than its filters and maximum
    /// age: nothing is sent.
    /// </exception>
    /// <exception cref="TableauException">The call failed; <see cref="TableauException.Kind"/> says how.</exception>
    public virtual Task<TableauDownload> DownloadCrosstabAsync(string id, ExportOptions? options = null, CancellationToken cancellationToken = default) =>
        ExportAsync(Endpoints.DownloadViewCrosstabExcel, ExportKind.ViewCrosstab, id, options, cancellationToken);

    /// <summary>Query View Preview Image: the view's thumbnail, a PNG.</summary>
    /// <param name="workbookId">The LUID of the view's workbook, which the reference's path names.</param>
    /// <param name="id">The view's LUID.</param>
    /// <param name="cancellationToken">Cancels the call, and stops reading the image.</param>
    /// <returns>The image: dispose it once read or saved.</returns>
    /// <exception cref="ArgumentException"><paramref name="workbookId"/> or <paramref name="id"/> is no LUID.</exception>
    /// <exception cref="TableauException">The call failed; <see cref="TableauException.Kind"/> says how.</exception>
    public virtual Task<TableauDownload> DownloadPreviewImageAsync(string workbookId, string id, CancellationToken cancellationToken = default) =>
        Pipeline.DownloadAsync(Endpoints.QueryViewPreviewImage,
            new TableauRequest([RequestUri.Luid(workbookId, nameof(workbookId)), RequestUri.Luid(id, nameof(id))]), cancellationToken);

    // Every export: the view's id and the options' query, each checked before anything is sent.
    private Task<TableauDownload> ExportAsync(Endpoint endpoint, ExportKind export, string id, ExportOptions? options, CancellationToken cancellationToken)
    {
        RequestUri.Luid(id, nameof(id));
        var query = ExportQuery.For(export, options, nameof(options));
        return Pipeline.DownloadAsync(endpoint, new TableauRequest([id], query), cancellationToken);
    }
}
