using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Tableau.Api.Pipeline;
using Tableau.Api.Rest.Models;

namespace Tableau.Api;

/// <summary>Tags on the site's content: <see cref="TableauClient.Tags"/>.</summary>
/// <remarks>
/// <para>
/// One API over every kind of content Tableau's reference tags: workbooks, views and data sources (REST API 1.0),
/// databases, tables and columns (3.9, Tableau Catalog's) and virtual connections (3.23), one at a time; and databases,
/// tables, columns, data sources, workbooks and flows in a batch (3.9). A call below its kind's version, or for a kind
/// it doesn't take, such as a project, is refused before anything is sent.
/// </para>
/// <para>
/// A tag is sent as written. Tableau's own Python client wraps a tag with a space or a comma in double quotes, saying
/// the server splits one that isn't at those characters.
/// </para>
/// </remarks>
public class TagsClient
{
    private const string TakesOne = "a workbook, a view, a data source, a virtual connection, a database, a table or a column";
    private const string TakesBatch = "databases, tables, columns, data sources, workbooks and flows";

    private readonly TableauPipeline? _pipeline;

    /// <summary>A sub-client with no connection, for a mock to derive from: its methods throw until overridden.</summary>
    protected TagsClient()
    {
    }

    internal TagsClient(TableauPipeline pipeline)
    {
        _pipeline = pipeline;
    }

    private TableauPipeline Pipeline => _pipeline ?? throw TableauClient.ForMocksOnly();

    /// <summary>Add Tags: tags the content; a tag it has already stays once.</summary>
    /// <param name="content">The content, such as <c>TableauContent.Workbook(id)</c>.</param>
    /// <param name="tags">The tags, one at least.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The content's tags as Tableau answers them, empty, never null, when it lists none.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="content"/> is of a kind that isn't tagged one at a time, or there is no tag, or one is empty:
    /// nothing is sent.
    /// </exception>
    /// <exception cref="TableauException">The call failed; <see cref="TableauException.Kind"/> says how.</exception>
    public virtual Task<IReadOnlyList<Tag>> AddAsync(TableauContent content, IEnumerable<string> tags, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        var calls = Calls(content);
        var request = new TableauRequest([content.Id], Body: new TsRequest { Tags = Labels(tags, nameof(tags)) });
        return SendAsync(Pipeline, calls.Add, request, cancellationToken);

        static async Task<IReadOnlyList<Tag>> SendAsync(TableauPipeline pipeline, Endpoint endpoint, TableauRequest request,
            CancellationToken cancellationToken)
        {
            var body = await pipeline.SendAsync(endpoint, request, static body => body, "a body", cancellationToken).ConfigureAwait(false);
            return body.Tags ?? [];
        }
    }

    /// <summary>Delete Tag: removes one tag from the content.</summary>
    /// <param name="content">The content, such as <c>TableauContent.Table(id)</c>.</param>
    /// <param name="tag">The tag, as written: it is escaped as one segment of the request's path.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <exception cref="ArgumentException">
    /// <paramref name="content"/> is of a kind that isn't tagged one at a time, or <paramref name="tag"/> is empty or only
    /// dots, which a path can't carry: nothing is sent.
    /// </exception>
    /// <exception cref="TableauException">The call failed; <see cref="TableauException.Kind"/> says how.</exception>
    public virtual Task DeleteAsync(TableauContent content, string tag, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        var calls = Calls(content);
        return Pipeline.SendAsync(calls.Delete, new TableauRequest([content.Id, RequestUri.Segment(tag, nameof(tag))]), cancellationToken);
    }

    /// <summary>Batch Add Tags: tags every content given with every tag given, in one call.</summary>
    /// <param name="tags">The tags, one at least.</param>
    /// <param name="contents">The content: databases, tables, columns, data sources, workbooks and flows, one at least.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <exception cref="ArgumentException">
    /// There is no tag or no content, a tag is empty, or a content is of a kind the batch doesn't take: nothing is sent.
    /// </exception>
    /// <exception cref="TableauException">The call failed; <see cref="TableauException.Kind"/> says how.</exception>
    public virtual Task AddBatchAsync(IEnumerable<string> tags, IEnumerable<TableauContent> contents, CancellationToken cancellationToken = default) =>
        Pipeline.SendAsync(Endpoints.BatchAddTags, new TableauRequest(Body: Batch(tags, contents)), cancellationToken);

    /// <summary>
    /// Batch Delete Tags: removes every tag given from every content given, in one call: a DELETE whose body names them,
    /// as Tableau's reference documents it.
    /// </summary>
    /// <param name="tags">The tags, one at least.</param>
    /// <param name="contents">The content: databases, tables, columns, data sources, workbooks and flows, one at least.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <exception cref="ArgumentException">
    /// There is no tag or no content, a tag is empty, or a content is of a kind the batch doesn't take: nothing is sent.
    /// </exception>
    /// <exception cref="TableauException">The call failed; <see cref="TableauException.Kind"/> says how.</exception>
    public virtual Task DeleteBatchAsync(IEnumerable<string> tags, IEnumerable<TableauContent> contents, CancellationToken cancellationToken = default) =>
        Pipeline.SendAsync(Endpoints.BatchDeleteTags, new TableauRequest(Body: Batch(tags, contents)), cancellationToken);

    // Each kind of content's own calls; a kind the reference doesn't tag one at a time is refused.
    private static TagCalls Calls(TableauContent content) => content.Kind switch
    {
        TableauContentKind.Workbook => Endpoints.WorkbookTags,
        TableauContentKind.View => Endpoints.ViewTags,
        TableauContentKind.DataSource => Endpoints.DataSourceTags,
        TableauContentKind.VirtualConnection => Endpoints.VirtualConnectionTags,
        TableauContentKind.Database => Endpoints.DatabaseTags,
        TableauContentKind.Table => Endpoints.TableTags,
        TableauContentKind.Column => Endpoints.ColumnTags,
        _ => throw content.Refused("Tags take", TakesOne, nameof(content)),
    };

    // The batch's body as the reference writes it: the tags by their labels, the content by its LUID alone.
    private static TsRequest Batch(IEnumerable<string> tags, IEnumerable<TableauContent> contents)
    {
        var labels = Labels(tags, nameof(tags));
        ArgumentNullException.ThrowIfNull(contents);
        var ids = new List<ContentTypeAndId>();
        foreach (var content in contents)
        {
            if (content is null) throw new ArgumentException("A content is null.", nameof(contents));
            if (content.Kind is not (TableauContentKind.Database or TableauContentKind.Table or TableauContentKind.Column
                or TableauContentKind.DataSource or TableauContentKind.Workbook or TableauContentKind.Flow))
                throw content.Refused("A batch of tags takes", TakesBatch, nameof(contents));
            ids.Add(new ContentTypeAndId { Id = content.Id });
        }
        if (ids.Count == 0) throw new ArgumentException("A batch takes one content at least.", nameof(contents));
        return new TsRequest { TagBatch = new TagBatch { Tags = labels, Contents = ids } };
    }

    private static List<Tag> Labels(IEnumerable<string> tags, string parameter)
    {
        ArgumentNullException.ThrowIfNull(tags, parameter);
        var labels = tags.Select(tag => string.IsNullOrEmpty(tag) ? throw new ArgumentException("A tag is empty.", parameter) : new Tag { Label = tag })
            .ToList();
        return labels.Count > 0 ? labels : throw new ArgumentException("It takes one tag at least.", parameter);
    }
}
