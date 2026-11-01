using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Tableau.Api.Pipeline;
using Tableau.Api.Rest.Models;

namespace Tableau.Api;

/// <summary>The site's published data sources, their connections, files and revisions: <see cref="TableauClient.DataSources"/>.</summary>
/// <remarks>
/// Tableau lists a data source from its search index, so one just published may be missing from a list for a moment. A
/// data source's file comes as a <see cref="TableauDownload"/>, which streams from the server as it is read: dispose it.
/// A data source embedded in a workbook is no published one: <see cref="WorkbooksClient.ListConnectionsAsync"/> lists it.
/// </remarks>
public class DataSourcesClient
{
    private const string Resource = "datasources";

    // Made once and never changed: the oldest first, then by name, so pages don't shift between requests. Tableau Server
    // 2025.1 refuses a sort on the content URL, which no two data sources of a site share.
    private static readonly TableauSort _stable = TableauSort.Asc(DataSourceField.CreatedAt).Then(TableauSort.Asc(DataSourceField.Name));

    private readonly TableauPipeline? _pipeline;

    /// <summary>A sub-client with no connection, for a mock to derive from: its methods throw until overridden.</summary>
    protected DataSourcesClient()
    {
    }

    internal DataSourcesClient(TableauPipeline pipeline)
    {
        _pipeline = pipeline;
    }

    private TableauPipeline Pipeline => _pipeline ?? throw TableauClient.ForMocksOnly();

    /// <summary>
    /// Query Data Sources, every page: the published data sources of the site the user may connect to, read a page at a
    /// time as you go, until the list ends. It reads its pages through <see cref="ListPageAsync"/>, so a mock that
    /// overrides that serves this too.
    /// </summary>
    /// <remarks>
    /// Without a sort the oldest come first, then by name, so the pages of a list that doesn't change neither skip nor
    /// repeat a data source, but for two of one name in two projects published in the same second; a sort you give is
    /// followed by those of the two it doesn't name.
    /// </remarks>
    /// <param name="options">The filter, the sort and the page size; null for every data source, 100 a page.</param>
    /// <param name="cancellationToken">Stops the list, between data sources and between pages.</param>
    /// <returns>The data sources, as you read them.</returns>
    /// <exception cref="ArgumentException">The filter or the sort names another resource's field, or the page size is outside 1 to 1,000.</exception>
    /// <exception cref="TableauException">A page's call failed; <see cref="TableauException.Kind"/> says how.</exception>
    public virtual async IAsyncEnumerable<DataSource> ListAsync(DataSourceListOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var dataSource in TableauPaging.ListAsync((number, token) => ListPageAsync(number, options, token),
                               Endpoints.QueryDataSources.Name, null, cancellationToken)
                           .ConfigureAwait(false))
            yield return dataSource;
    }

    /// <summary>Query Data Sources, one page.</summary>
    /// <param name="pageNumber">The page's number, from 1.</param>
    /// <param name="options">The filter, the sort and the page size; null for every data source, 100 a page.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The page, its data sources empty, never null, when it has none.</returns>
    /// <exception cref="ArgumentException">
    /// The page number is below 1, the page size outside 1 to 1,000, or the filter or the sort names another resource's
    /// field.
    /// </exception>
    /// <exception cref="TableauException">The call failed; <see cref="TableauException.Kind"/> says how.</exception>
    public virtual Task<Page<DataSource>> ListPageAsync(int pageNumber, DataSourceListOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new DataSourceListOptions();
        return TableauPaging.PageAsync(Pipeline, Endpoints.QueryDataSources, null, pageNumber, options.PageSize, options.Filter, options.Sort,
            _stable, Resource, static body => body.Datasources?.Datasource, cancellationToken);
    }

    /// <summary>Query Data Source: the data source, with its project, owner and tags.</summary>
    /// <param name="id">The data source's LUID.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The data source.</returns>
    /// <exception cref="ArgumentException"><paramref name="id"/> is no LUID.</exception>
    /// <exception cref="TableauException">The call failed; <see cref="TableauException.Kind"/> says how.</exception>
    public virtual Task<DataSource> GetAsync(string id, CancellationToken cancellationToken = default) =>
        Pipeline.SendAsync(Endpoints.QueryDataSource, new TableauRequest([RequestUri.Luid(id, nameof(id))]), static body => body.Datasource,
            "datasource", cancellationToken);

    /// <summary>Query Data Source Connections: the data source's connections, from one call.</summary>
    /// <param name="id">The data source's LUID.</param>
    /// <param name="cancellationToken">Stops the list, and cancels its call.</param>
    /// <returns>The connections, as you read them.</returns>
    /// <exception cref="ArgumentException"><paramref name="id"/> is no LUID, which the first read throws.</exception>
    /// <exception cref="TableauException">The call failed; <see cref="TableauException.Kind"/> says how.</exception>
    public virtual async IAsyncEnumerable<Connection> ListConnectionsAsync(string id, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        RequestUri.Luid(id, nameof(id));
        await foreach (var connection in TableauContentCalls.ConnectionsAsync(Pipeline, Endpoints.QueryDataSourceConnections, id, cancellationToken)
                           .ConfigureAwait(false))
            yield return connection;
    }

    /// <summary>
    /// Update Data Source: changes the members of <paramref name="changes"/> that are set, and leaves the others as they
    /// are; a project or an owner is sent by its LUID alone, and nothing for one not set.
    /// </summary>
    /// <param name="id">The data source's LUID.</param>
    /// <param name="changes">What to change, such as <c>new DataSourceUpdate { IsCertified = true }</c>.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The data source as Tableau holds it now.</returns>
    /// <exception cref="ArgumentException"><paramref name="id"/>, or a project's or an owner's id in <paramref name="changes"/>, is no LUID.</exception>
    /// <exception cref="TableauException">The call failed; <see cref="TableauException.Kind"/> says how.</exception>
    public virtual Task<DataSource> UpdateAsync(string id, DataSourceUpdate changes, CancellationToken cancellationToken = default)
    {
        RequestUri.Luid(id, nameof(id));
        ArgumentNullException.ThrowIfNull(changes);
        return Pipeline.SendAsync(Endpoints.UpdateDataSource, new TableauRequest([id], Body: new TsRequest { Datasource = changes.Sent(nameof(changes)) }),
            static body => body.Datasource, "datasource", cancellationToken);
    }

    /// <summary>
    /// Update Data Source Connection: changes the members of <paramref name="changes"/> that are set on one of the data
    /// source's connections.
    /// </summary>
    /// <param name="id">The data source's LUID.</param>
    /// <param name="connectionId">The connection's LUID, from <see cref="ListConnectionsAsync"/>.</param>
    /// <param name="changes">What to change, such as <c>new ConnectionUpdate { UserName = "reader", Password = secret, EmbedPassword = true }</c>.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The connection as Tableau holds it now.</returns>
    /// <exception cref="ArgumentException"><paramref name="id"/> or <paramref name="connectionId"/> is no LUID.</exception>
    /// <exception cref="TableauException">
    /// The call failed; <see cref="TableauException.Kind"/> says how, <see cref="TableauErrorKind.UnsupportedApiVersion"/>
    /// for an authentication type below REST API 3.27, before anything is sent.
    /// </exception>
    public virtual Task<Connection> UpdateConnectionAsync(string id, string connectionId, ConnectionUpdate changes,
        CancellationToken cancellationToken = default) =>
        TableauContentCalls.UpdateConnectionAsync(Pipeline, Endpoints.UpdateDataSourceConnection, Endpoints.UpdateDataSourceConnectionAuthentication,
            id, connectionId, changes, cancellationToken);

    /// <summary>
    /// Update Data Source Now: refreshes the data source's extract, as a job, at once and outside any schedule; a full
    /// refresh, whatever the extract's own setting. Sent once, never again after a failure, since a second would queue a
    /// second refresh.
    /// </summary>
    /// <param name="id">The data source's LUID.</param>
    /// <param name="cancellationToken">Cancels the call, not the job.</param>
    /// <returns>The job, its <see cref="Job.Type"/> <c>RefreshExtract</c>.</returns>
    /// <exception cref="ArgumentException"><paramref name="id"/> is no LUID.</exception>
    /// <exception cref="TableauException">The call failed; <see cref="TableauException.Kind"/> says how.</exception>
    public virtual Task<Job> RunExtractRefreshAsync(string id, CancellationToken cancellationToken = default) =>
        TableauContentCalls.RunExtractRefreshAsync(Pipeline, Endpoints.UpdateDataSourceNow, id, cancellationToken);

    /// <summary>Delete Data Source: the data source and its connection; workbooks that use it stay, and no longer work.</summary>
    /// <param name="id">The data source's LUID.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <exception cref="ArgumentException"><paramref name="id"/> is no LUID.</exception>
    /// <exception cref="TableauException">The call failed; <see cref="TableauException.Kind"/> says how.</exception>
    public virtual Task DeleteAsync(string id, CancellationToken cancellationToken = default) =>
        Pipeline.SendAsync(Endpoints.DeleteDataSource, new TableauRequest([RequestUri.Luid(id, nameof(id))]), cancellationToken);

    /// <summary>
    /// Download Data Source: the data source's file, a <c>.tdsx</c> or a <c>.tds</c>, streamed as it is read; its
    /// <see cref="TableauDownload.Kind"/> says which.
    /// </summary>
    /// <param name="id">The data source's LUID.</param>
    /// <param name="includeExtract">False to leave its extract out, for a smaller file; true, the default, keeps it.</param>
    /// <param name="cancellationToken">Cancels the call, and stops reading the file.</param>
    /// <returns>The file: dispose it once read or saved.</returns>
    /// <exception cref="ArgumentException"><paramref name="id"/> is no LUID.</exception>
    /// <exception cref="TableauException">The call failed; <see cref="TableauException.Kind"/> says how.</exception>
    public virtual Task<TableauDownload> DownloadAsync(string id, bool includeExtract = true, CancellationToken cancellationToken = default) =>
        TableauContentCalls.DownloadAsync(Pipeline, Endpoints.DownloadDataSource, id, includeExtract, cancellationToken);

    /// <summary>
    /// Get Data Source Revisions, every page: the data source's revisions, read a page at a time as you go. It reads its
    /// pages through <see cref="ListRevisionsPageAsync"/>, so a mock that overrides that serves this too.
    /// </summary>
    /// <remarks>Tableau answers only when the site keeps revision history, and with a 403 otherwise.</remarks>
    /// <param name="id">The data source's LUID.</param>
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
                               Endpoints.DataSourceRevisions.List.Name, null, cancellationToken)
                           .ConfigureAwait(false))
            yield return revision;
    }

    /// <summary>Get Data Source Revisions, one page.</summary>
    /// <param name="id">The data source's LUID.</param>
    /// <param name="pageNumber">The page's number, from 1.</param>
    /// <param name="options">The page size; null for 100 a page.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The page, its revisions empty, never null, when it has none.</returns>
    /// <exception cref="ArgumentException"><paramref name="id"/> is no LUID, the page number is below 1, or the page size outside 1 to 1,000.</exception>
    /// <exception cref="TableauException">The call failed; <see cref="TableauException.Kind"/> says how.</exception>
    public virtual Task<Page<Revision>> ListRevisionsPageAsync(string id, int pageNumber, RevisionListOptions? options = null,
        CancellationToken cancellationToken = default) =>
        TableauContentCalls.RevisionPageAsync(Pipeline, Endpoints.DataSourceRevisions, id, pageNumber, options, cancellationToken);

    /// <summary>
    /// Download Data Source Revision: an earlier revision's file; the current revision is <see cref="DownloadAsync"/>'s,
    /// which Tableau doesn't serve here.
    /// </summary>
    /// <param name="id">The data source's LUID.</param>
    /// <param name="revision">The revision's number, from 1, as <see cref="ListRevisionsAsync"/> gives it.</param>
    /// <param name="cancellationToken">Cancels the call, and stops reading the file.</param>
    /// <returns>The file: dispose it once read or saved.</returns>
    /// <exception cref="ArgumentException"><paramref name="id"/> is no LUID, or <paramref name="revision"/> is below 1.</exception>
    /// <exception cref="TableauException">The call failed; <see cref="TableauException.Kind"/> says how.</exception>
    public virtual Task<TableauDownload> DownloadRevisionAsync(string id, int revision, CancellationToken cancellationToken = default) =>
        TableauContentCalls.DownloadRevisionAsync(Pipeline, Endpoints.DataSourceRevisions, id, revision, includeExtract: true, cancellationToken);

    /// <summary>
    /// Remove Data Source Revision: removes one revision's file for good; the list then shows it deleted. The first
    /// revision can't be removed: delete the data source instead.
    /// </summary>
    /// <param name="id">The data source's LUID.</param>
    /// <param name="revision">The revision's number, from 1.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <exception cref="ArgumentException"><paramref name="id"/> is no LUID, or <paramref name="revision"/> is below 1.</exception>
    /// <exception cref="TableauException">The call failed; <see cref="TableauException.Kind"/> says how.</exception>
    public virtual Task RemoveRevisionAsync(string id, int revision, CancellationToken cancellationToken = default) =>
        TableauContentCalls.RemoveRevisionAsync(Pipeline, Endpoints.DataSourceRevisions, id, revision, cancellationToken);
}
