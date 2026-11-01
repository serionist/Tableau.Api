using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Tableau.Api.Rest.Models;

namespace Tableau.Api.Pipeline;

/// <summary>
/// What workbooks and data sources share: their revisions, their connections, an extract refresh and a download that
/// leaves the extract out.
/// </summary>
internal static class TableauContentCalls
{
    /// <summary>A revision list's resource, which takes no field.</summary>
    private const string Revisions = "revisions";

    /// <summary>The query that leaves a download's extract out, as Tableau's reference writes it.</summary>
    private static readonly IReadOnlyList<KeyValuePair<string, string?>> _withoutExtract = [new("includeExtract", "False")];

    /// <summary>A page of the content's revisions, with its paging (F7).</summary>
    public static Task<Page<Revision>> RevisionPageAsync(TableauPipeline pipeline, RevisionCalls calls, string id, int pageNumber,
        RevisionListOptions? options, CancellationToken cancellationToken)
    {
        RequestUri.Luid(id, nameof(id));
        return TableauPaging.PageAsync(pipeline, calls.List, [id], pageNumber, (options ?? new RevisionListOptions()).PageSize, null, null, null,
            Revisions, static body => body.Revisions, cancellationToken);
    }

    /// <summary>One revision's file, its extract left out when asked.</summary>
    public static Task<TableauDownload> DownloadRevisionAsync(TableauPipeline pipeline, RevisionCalls calls, string id, int revision,
        bool includeExtract, CancellationToken cancellationToken) =>
        pipeline.DownloadAsync(calls.Download,
            new TableauRequest([RequestUri.Luid(id, nameof(id)), Number(revision)], includeExtract ? null : _withoutExtract), cancellationToken);

    /// <summary>One revision removed.</summary>
    public static Task RemoveRevisionAsync(TableauPipeline pipeline, RevisionCalls calls, string id, int revision, CancellationToken cancellationToken) =>
        pipeline.SendAsync(calls.Remove, new TableauRequest([RequestUri.Luid(id, nameof(id)), Number(revision)]), cancellationToken);

    /// <summary>The content's connections, from one call: the reference lists no paging for them.</summary>
    public static async IAsyncEnumerable<Connection> ConnectionsAsync(TableauPipeline pipeline, Endpoint endpoint, string id,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var body = await pipeline.SendAsync(endpoint, new TableauRequest([id]), static body => body, "a body", cancellationToken).ConfigureAwait(false);
        foreach (var connection in body.Connections ?? [])
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return connection;
        }
    }

    /// <summary>
    /// One of the content's connections updated, with the members of <paramref name="changes"/> that are set: through
    /// <paramref name="withAuthentication"/>, REST API 3.27's, when they set an authentication type, so an older server
    /// refuses it before anything is sent.
    /// </summary>
    public static Task<Connection> UpdateConnectionAsync(TableauPipeline pipeline, Endpoint endpoint, Endpoint withAuthentication, string id,
        string connectionId, ConnectionUpdate changes, CancellationToken cancellationToken)
    {
        RequestUri.Luid(id, nameof(id));
        RequestUri.Luid(connectionId, nameof(connectionId));
        ArgumentNullException.ThrowIfNull(changes);
        return pipeline.SendAsync(changes.AuthenticationType is null ? endpoint : withAuthentication,
            new TableauRequest([id, connectionId], Body: new TsRequest { Connection = changes.Sent() }), static body => body.Connection, "connection",
            cancellationToken);
    }

    /// <summary>
    /// An extract refresh, the job Tableau answers with: an empty body, as the reference's before REST API 3.28, whose
    /// incremental refresh this doesn't ask for.
    /// </summary>
    public static Task<Job> RunExtractRefreshAsync(TableauPipeline pipeline, Endpoint endpoint, string id, CancellationToken cancellationToken) =>
        pipeline.SendAsync(endpoint, new TableauRequest([RequestUri.Luid(id, nameof(id))], Body: new TsRequest()), static body => body.Job, "job",
            cancellationToken);

    /// <summary>A workbook's or a data source's file, its extract left out when asked.</summary>
    public static Task<TableauDownload> DownloadAsync(TableauPipeline pipeline, Endpoint endpoint, string id, bool includeExtract,
        CancellationToken cancellationToken) =>
        pipeline.DownloadAsync(endpoint, new TableauRequest([RequestUri.Luid(id, nameof(id))], includeExtract ? null : _withoutExtract), cancellationToken);

    // A revision's number, from 1, as a path segment.
    private static string Number(int revision)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(revision, 1);
        return revision.ToString(CultureInfo.InvariantCulture);
    }
}
