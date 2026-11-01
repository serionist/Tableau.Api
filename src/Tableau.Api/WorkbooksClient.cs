using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Tableau.Api.Pipeline;
using Tableau.Api.Rest.Models;

namespace Tableau.Api;

/// <summary>The site's workbooks, their connections, files and revisions: <see cref="TableauClient.Workbooks"/>.</summary>
/// <remarks>
/// <para>
/// Tableau lists a workbook from its search index, so one just published may be missing from a list for a moment. A
/// workbook's file, its preview image and its PDF come as a <see cref="TableauDownload"/>, which streams from the server
/// as it is read: dispose it.
/// </para>
/// <para>
/// A workbook's location type comes as <see cref="LocationType.Project"/> from Tableau Server 2025.1, the schema's own
/// text, where a database's or a table's comes as <c>PROJECT</c>; a published data source's came with no location.
/// </para>
/// </remarks>
public class WorkbooksClient
{
    private const string Resource = "workbooks";

    // Made once and never changed: the oldest first, then by name, so pages don't shift between requests. Tableau Server
    // 2025.1 refuses a sort on the content URL, which no two workbooks of a site share, and on the project's name.
    private static readonly TableauSort _stable = TableauSort.Asc(WorkbookField.CreatedAt).Then(TableauSort.Asc(WorkbookField.Name));

    // The query that lists only the workbooks a user owns.
    private static readonly IReadOnlyList<KeyValuePair<string, string?>> _ownedOnly = [new("ownedBy", "true")];

    private readonly TableauPipeline? _pipeline;

    /// <summary>A sub-client with no connection, for a mock to derive from: its methods throw until overridden.</summary>
    protected WorkbooksClient()
    {
    }

    internal WorkbooksClient(TableauPipeline pipeline)
    {
        _pipeline = pipeline;
    }

    private TableauPipeline Pipeline => _pipeline ?? throw TableauClient.ForMocksOnly();

    /// <summary>
    /// Query Workbooks for Site, every page: the workbooks of the site the user can see, read a page at a time as you go,
    /// until the list ends. It reads its pages through <see cref="ListPageAsync"/>, so a mock that overrides that serves
    /// this too.
    /// </summary>
    /// <remarks>
    /// Without a sort the oldest come first, then by name, so the pages of a list that doesn't change neither skip nor
    /// repeat a workbook, but for two of one name in two projects published in the same second; a sort you give is
    /// followed by those of the two it doesn't name. One published, deleted or renamed while you read can still be missed
    /// or met twice.
    /// </remarks>
    /// <param name="options">The filter, the sort and the page size; null for every workbook, 100 a page.</param>
    /// <param name="cancellationToken">Stops the list, between workbooks and between pages.</param>
    /// <returns>The workbooks, as you read them.</returns>
    /// <exception cref="ArgumentException">The filter or the sort names another resource's field, or the page size is outside 1 to 1,000.</exception>
    /// <exception cref="TableauException">A page's call failed; <see cref="TableauException.Kind"/> says how.</exception>
    public virtual async IAsyncEnumerable<Workbook> ListAsync(WorkbookListOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var workbook in TableauPaging.ListAsync((number, token) => ListPageAsync(number, options, token),
                               Endpoints.QueryWorkbooksForSite.Name, null, cancellationToken)
                           .ConfigureAwait(false))
            yield return workbook;
    }

    /// <summary>Query Workbooks for Site, one page.</summary>
    /// <param name="pageNumber">The page's number, from 1.</param>
    /// <param name="options">The filter, the sort and the page size; null for every workbook, 100 a page.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The page, its workbooks empty, never null, when it has none.</returns>
    /// <exception cref="ArgumentException">
    /// The page number is below 1, the page size outside 1 to 1,000, or the filter or the sort names another resource's
    /// field.
    /// </exception>
    /// <exception cref="TableauException">The call failed; <see cref="TableauException.Kind"/> says how.</exception>
    public virtual Task<Page<Workbook>> ListPageAsync(int pageNumber, WorkbookListOptions? options = null, CancellationToken cancellationToken = default)
    {
        options ??= new WorkbookListOptions();
        return TableauPaging.PageAsync(Pipeline, Endpoints.QueryWorkbooksForSite, null, pageNumber, options.PageSize, options.Filter, options.Sort,
            _stable, Resource, static body => body.Workbooks, cancellationToken);
    }

    /// <summary>Get Workbook: the workbook, with its project, owner, tags and views.</summary>
    /// <param name="id">The workbook's LUID.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The workbook.</returns>
    /// <exception cref="ArgumentException"><paramref name="id"/> is no LUID.</exception>
    /// <exception cref="TableauException">The call failed; <see cref="TableauException.Kind"/> says how.</exception>
    public virtual Task<Workbook> GetAsync(string id, CancellationToken cancellationToken = default) =>
        Pipeline.SendAsync(Endpoints.GetWorkbook, new TableauRequest([RequestUri.Luid(id, nameof(id))]), static body => body.Workbook, "workbook",
            cancellationToken);

    /// <summary>
    /// Query Workbooks for User, every page: the workbooks a user owns and those they may read, or only those they own,
    /// in Tableau's order, 100 a page.
    /// </summary>
    /// <param name="userId">The user's LUID.</param>
    /// <param name="ownedOnly">True for the workbooks the user owns alone; false, the default, for every one they may read too.</param>
    /// <param name="cancellationToken">Stops the list, between workbooks and between pages.</param>
    /// <returns>The workbooks, as you read them.</returns>
    /// <exception cref="ArgumentException"><paramref name="userId"/> is no LUID, which the first read throws.</exception>
    /// <exception cref="TableauException">A page's call failed; <see cref="TableauException.Kind"/> says how.</exception>
    public virtual async IAsyncEnumerable<Workbook> ListForUserAsync(string userId, bool ownedOnly = false,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        RequestUri.Luid(userId, nameof(userId));
        await foreach (var workbook in TableauPaging.ListAsync((number, token) => TableauPaging.PageAsync(Pipeline, Endpoints.QueryWorkbooksForUser,
                               [userId], number, TableauPaging.DefaultPageSize, null, null, null, Resource, static body => body.Workbooks,
                               ownedOnly ? _ownedOnly : null, token),
                           Endpoints.QueryWorkbooksForUser.Name, null, cancellationToken).ConfigureAwait(false))
            yield return workbook;
    }

    /// <summary>Query Workbook Connections: the workbook's data connections, from one call.</summary>
    /// <param name="id">The workbook's LUID.</param>
    /// <param name="cancellationToken">Stops the list, and cancels its call.</param>
    /// <returns>The connections, as you read them.</returns>
    /// <exception cref="ArgumentException"><paramref name="id"/> is no LUID, which the first read throws.</exception>
    /// <exception cref="TableauException">The call failed; <see cref="TableauException.Kind"/> says how.</exception>
    public virtual async IAsyncEnumerable<Connection> ListConnectionsAsync(string id, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        RequestUri.Luid(id, nameof(id));
        await foreach (var connection in TableauContentCalls.ConnectionsAsync(Pipeline, Endpoints.QueryWorkbookConnections, id, cancellationToken)
                           .ConfigureAwait(false))
            yield return connection;
    }

    /// <summary>
    /// Update Workbook: changes the members of <paramref name="changes"/> that are set, and leaves the others as they are;
    /// a project or an owner is sent by its LUID alone, and nothing for one not set.
    /// </summary>
    /// <param name="id">The workbook's LUID.</param>
    /// <param name="changes">What to change, such as <c>new WorkbookUpdate { ShowTabs = true }</c>.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The workbook as Tableau holds it now.</returns>
    /// <exception cref="ArgumentException"><paramref name="id"/>, or a project's or an owner's id in <paramref name="changes"/>, is no LUID.</exception>
    /// <exception cref="TableauException">The call failed; <see cref="TableauException.Kind"/> says how.</exception>
    public virtual Task<Workbook> UpdateAsync(string id, WorkbookUpdate changes, CancellationToken cancellationToken = default)
    {
        RequestUri.Luid(id, nameof(id));
        ArgumentNullException.ThrowIfNull(changes);
        return Pipeline.SendAsync(Endpoints.UpdateWorkbook, new TableauRequest([id], Body: new TsRequest { Workbook = changes.Sent(nameof(changes)) }),
            static body => body.Workbook, "workbook", cancellationToken);
    }

    /// <summary>
    /// Update Workbook Connection: changes the members of <paramref name="changes"/> that are set on one of the
    /// workbook's connections.
    /// </summary>
    /// <param name="id">The workbook's LUID.</param>
    /// <param name="connectionId">The connection's LUID, from <see cref="ListConnectionsAsync"/>.</param>
    /// <param name="changes">What to change, such as <c>new ConnectionUpdate { ServerAddress = "db.example.com" }</c>.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The connection as Tableau holds it now.</returns>
    /// <exception cref="ArgumentException"><paramref name="id"/> or <paramref name="connectionId"/> is no LUID.</exception>
    /// <exception cref="TableauException">
    /// The call failed; <see cref="TableauException.Kind"/> says how, <see cref="TableauErrorKind.UnsupportedApiVersion"/>
    /// for an authentication type below REST API 3.27, before anything is sent.
    /// </exception>
    public virtual Task<Connection> UpdateConnectionAsync(string id, string connectionId, ConnectionUpdate changes,
        CancellationToken cancellationToken = default) =>
        TableauContentCalls.UpdateConnectionAsync(Pipeline, Endpoints.UpdateWorkbookConnection, Endpoints.UpdateWorkbookConnectionAuthentication, id,
            connectionId, changes, cancellationToken);

    /// <summary>
    /// Update Workbook Now: refreshes the workbook's extracts, as a job, at once and outside any schedule. Sent once, never
    /// again after a failure, since a second would queue a second refresh.
    /// </summary>
    /// <param name="id">The workbook's LUID.</param>
    /// <param name="cancellationToken">Cancels the call, not the job.</param>
    /// <returns>The job, its <see cref="Job.Type"/> <c>RefreshExtract</c>.</returns>
    /// <exception cref="ArgumentException"><paramref name="id"/> is no LUID.</exception>
    /// <exception cref="TableauException">The call failed; <see cref="TableauException.Kind"/> says how.</exception>
    public virtual Task<Job> RunExtractRefreshAsync(string id, CancellationToken cancellationToken = default) =>
        TableauContentCalls.RunExtractRefreshAsync(Pipeline, Endpoints.UpdateWorkbookNow, id, cancellationToken);

    /// <summary>Delete Workbook: the workbook, its views and its connections.</summary>
    /// <param name="id">The workbook's LUID.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <exception cref="ArgumentException"><paramref name="id"/> is no LUID.</exception>
    /// <exception cref="TableauException">The call failed; <see cref="TableauException.Kind"/> says how.</exception>
    public virtual Task DeleteAsync(string id, CancellationToken cancellationToken = default) =>
        Pipeline.SendAsync(Endpoints.DeleteWorkbook, new TableauRequest([RequestUri.Luid(id, nameof(id))]), cancellationToken);

    /// <summary>
    /// Download Workbook: the workbook's file, a <c>.twbx</c> or a <c>.twb</c>, streamed as it is read; its
    /// <see cref="TableauDownload.Kind"/> says which.
    /// </summary>
    /// <param name="id">The workbook's LUID.</param>
    /// <param name="includeExtract">False to leave the workbook's extracts out, for a smaller file; true, the default, keeps them.</param>
    /// <param name="cancellationToken">Cancels the call, and stops reading the file.</param>
    /// <returns>The file: dispose it once read or saved.</returns>
    /// <exception cref="ArgumentException"><paramref name="id"/> is no LUID.</exception>
    /// <exception cref="TableauException">The call failed; <see cref="TableauException.Kind"/> says how.</exception>
    public virtual Task<TableauDownload> DownloadAsync(string id, bool includeExtract = true, CancellationToken cancellationToken = default) =>
        TableauContentCalls.DownloadAsync(Pipeline, Endpoints.DownloadWorkbook, id, includeExtract, cancellationToken);

    /// <summary>Query Workbook Preview Image: the workbook's thumbnail, a PNG, usually of its first sheet.</summary>
    /// <param name="id">The workbook's LUID.</param>
    /// <param name="cancellationToken">Cancels the call, and stops reading the image.</param>
    /// <returns>The image: dispose it once read or saved.</returns>
    /// <exception cref="ArgumentException"><paramref name="id"/> is no LUID.</exception>
    /// <exception cref="TableauException">The call failed; <see cref="TableauException.Kind"/> says how.</exception>
    public virtual Task<TableauDownload> DownloadPreviewImageAsync(string id, CancellationToken cancellationToken = default) =>
        Pipeline.DownloadAsync(Endpoints.QueryWorkbookPreviewImage, new TableauRequest([RequestUri.Luid(id, nameof(id))]), cancellationToken);

    /// <summary>
    /// Download Workbook PDF: a PDF of the workbook's sheets the user may see; without tabs shown, of its default sheet
    /// alone. Tableau may answer from its cache: <see cref="ExportOptions.MaxAge"/> bounds its age.
    /// </summary>
    /// <param name="id">The workbook's LUID.</param>
    /// <param name="options">
    /// Its <see cref="ExportOptions.Filters"/> (from REST API 3.24), <see cref="ExportOptions.MaxAge"/>,
    /// <see cref="ExportOptions.Width"/> and <see cref="ExportOptions.Height"/> (from 3.26),
    /// <see cref="ExportOptions.PageType"/> and <see cref="ExportOptions.Orientation"/>; null for Tableau's defaults,
    /// Legal and portrait.
    /// </param>
    /// <param name="cancellationToken">Cancels the call, and stops reading the file.</param>
    /// <returns>The PDF: dispose it once read or saved.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="id"/> is no LUID, or <paramref name="options"/> sets <see cref="ExportOptions.Resolution"/>, or a
    /// value it sets is out of range: nothing is sent.
    /// </exception>
    /// <exception cref="TableauException">
    /// The call failed; <see cref="TableauException.Kind"/> says how, <see cref="TableauErrorKind.UnsupportedApiVersion"/>
    /// for filters below REST API 3.24 or a size below 3.26, before anything is sent.
    /// </exception>
    public virtual Task<TableauDownload> DownloadPdfAsync(string id, ExportOptions? options = null, CancellationToken cancellationToken = default)
    {
        RequestUri.Luid(id, nameof(id));
        var query = ExportQuery.For(ExportKind.WorkbookPdf, options, nameof(options));
        var endpoint = options?.Width is not null ? Endpoints.DownloadWorkbookPdfSized
            : options?.Filters is { Count: > 0 } ? Endpoints.DownloadWorkbookPdfFiltered
            : Endpoints.DownloadWorkbookPdf;
        return Pipeline.DownloadAsync(endpoint, new TableauRequest([id], query), cancellationToken);
    }

    /// <summary>
    /// Get Workbook Revisions, every page: the workbook's revisions, read a page at a time as you go. It reads its pages
    /// through <see cref="ListRevisionsPageAsync"/>, so a mock that overrides that serves this too.
    /// </summary>
    /// <remarks>Tableau answers only when the site keeps revision history, and with a 403 otherwise.</remarks>
    /// <param name="id">The workbook's LUID.</param>
    /// <param name="options">The page size; null for 100 a page.</param>
    /// <param name="cancellationToken">Stops the list, between revisions and between pages.</param>
    /// <returns>The revisions, as you read them, the current one marked <see cref="Revision.Current"/>.</returns>
    /// <exception cref="ArgumentException"><paramref name="id"/> is no LUID, or the page size is outside 1 to 1,000, which the first read throws.</exception>
    /// <exception cref="TableauException">A page's call failed; <see cref="TableauException.Kind"/> says how.</exception>
    public virtual async IAsyncEnumerable<Revision> ListRevisionsAsync(string id, RevisionListOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        RequestUri.Luid(id, nameof(id));
        await foreach (var revision in TableauPaging.ListAsync((number, token) => ListRevisionsPageAsync(id, number, options, token),
                               Endpoints.WorkbookRevisions.List.Name, null, cancellationToken)
                           .ConfigureAwait(false))
            yield return revision;
    }

    /// <summary>Get Workbook Revisions, one page.</summary>
    /// <param name="id">The workbook's LUID.</param>
    /// <param name="pageNumber">The page's number, from 1.</param>
    /// <param name="options">The page size; null for 100 a page.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The page, its revisions empty, never null, when it has none.</returns>
    /// <exception cref="ArgumentException"><paramref name="id"/> is no LUID, the page number is below 1, or the page size outside 1 to 1,000.</exception>
    /// <exception cref="TableauException">The call failed; <see cref="TableauException.Kind"/> says how.</exception>
    public virtual Task<Page<Revision>> ListRevisionsPageAsync(string id, int pageNumber, RevisionListOptions? options = null,
        CancellationToken cancellationToken = default) =>
        TableauContentCalls.RevisionPageAsync(Pipeline, Endpoints.WorkbookRevisions, id, pageNumber, options, cancellationToken);

    /// <summary>
    /// Download Workbook Revision: an earlier revision's file; the current revision is <see cref="DownloadAsync"/>'s,
    /// which Tableau doesn't serve here, refusing it with 400057.
    /// </summary>
    /// <param name="id">The workbook's LUID.</param>
    /// <param name="revision">The revision's number, from 1, as <see cref="ListRevisionsAsync"/> gives it.</param>
    /// <param name="includeExtract">False to leave the revision's extracts out, for a smaller file; true, the default, keeps them.</param>
    /// <param name="cancellationToken">Cancels the call, and stops reading the file.</param>
    /// <returns>The file: dispose it once read or saved.</returns>
    /// <exception cref="ArgumentException"><paramref name="id"/> is no LUID, or <paramref name="revision"/> is below 1.</exception>
    /// <exception cref="TableauException">The call failed; <see cref="TableauException.Kind"/> says how.</exception>
    public virtual Task<TableauDownload> DownloadRevisionAsync(string id, int revision, bool includeExtract = true,
        CancellationToken cancellationToken = default) =>
        TableauContentCalls.DownloadRevisionAsync(Pipeline, Endpoints.WorkbookRevisions, id, revision, includeExtract, cancellationToken);

    /// <summary>
    /// Remove Workbook Revision: removes one revision's file for good; the list then shows it deleted. The first revision
    /// can't be removed: delete the workbook instead.
    /// </summary>
    /// <param name="id">The workbook's LUID.</param>
    /// <param name="revision">The revision's number, from 1.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <exception cref="ArgumentException"><paramref name="id"/> is no LUID, or <paramref name="revision"/> is below 1.</exception>
    /// <exception cref="TableauException">The call failed; <see cref="TableauException.Kind"/> says how.</exception>
    public virtual Task RemoveRevisionAsync(string id, int revision, CancellationToken cancellationToken = default) =>
        TableauContentCalls.RemoveRevisionAsync(Pipeline, Endpoints.WorkbookRevisions, id, revision, cancellationToken);
}
