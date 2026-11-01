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
/// In the bodies of Add Tags, Batch Add Tags and Batch Delete Tags, a tag that holds whitespace or a comma is sent in
/// double quotes, <c>"Yearly Sales"</c>: Tableau splits an unquoted label at whitespace and commas into several tags, so
/// <c>Yearly Sales</c> as written would add <c>Yearly</c> and <c>Sales</c>. Tableau's own Python client quotes such a
/// tag the same way, and this library's Vizportal client quotes every tag. A tag already in double quotes is sent as it
/// is, and any other as written. A <c>"</c> inside a tag is undefined: Tableau documents nothing for it.
/// </para>
/// <para>
/// Delete Tag names the tag in its path, unquoted, as Tableau stores it and <see cref="AddAsync"/> returns it.
/// </para>
/// <para>
/// A batch names each content by its type and LUID: <c>Database</c>, <c>Table</c>, <c>Column</c>, <c>Datasource</c>,
/// <c>Workbook</c> or <c>Flow</c>, as the reference's Batch Add Tags answer writes them in the schema's
/// <c>contentType</c>, and as Tableau's Python client sends them. The reference's request names a content by its LUID
/// alone, which Tableau Server 2025.1 refuses (409004). Batch Delete Tags is the reference's DELETE with that body, which
/// the same server took.
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
    /// <param name="tags">The tags, one at least; one with whitespace or a comma is sent in double quotes, so it stays one tag.</param>
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
    /// <param name="tag">
    /// The tag as Tableau stores it, as <see cref="AddAsync"/> returns it: unquoted, such as <c>Yearly Sales</c>. It is
    /// escaped as one segment of the request's path, and never quoted, since a quoted one names a tag with the quotes in it.
    /// </param>
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

    // The batch's body: the tags by their labels, each content by its type and LUID. The reference's request names the
    // content by its LUID alone, which Tableau Server 2025.1 refuses as a content type not allowed (409004); the types
    // are the ones the reference's answer writes and Tableau's Python client sends.
    private static TsRequest Batch(IEnumerable<string> tags, IEnumerable<TableauContent> contents)
    {
        var labels = Labels(tags, nameof(tags));
        ArgumentNullException.ThrowIfNull(contents);
        var ids = new List<ContentTypeAndId>();
        foreach (var content in contents)
        {
            if (content is null) throw new ArgumentException("A content is null.", nameof(contents));
            var type = content.Kind switch
            {
                TableauContentKind.Database => "Database",
                TableauContentKind.Table => "Table",
                TableauContentKind.Column => "Column",
                TableauContentKind.DataSource => "Datasource",
                TableauContentKind.Workbook => "Workbook",
                TableauContentKind.Flow => "Flow",
                _ => throw content.Refused("A batch of tags takes", TakesBatch, nameof(contents)),
            };
            ids.Add(new ContentTypeAndId { ContentType = type, Id = content.Id });
        }
        if (ids.Count == 0) throw new ArgumentException("A batch takes one content at least.", nameof(contents));
        return new TsRequest { TagBatch = new TagBatch { Tags = labels, Contents = ids } };
    }

    // The labels a body sends, each as Label writes it.
    private static List<Tag> Labels(IEnumerable<string> tags, string parameter)
    {
        ArgumentNullException.ThrowIfNull(tags, parameter);
        var labels = tags.Select(tag => string.IsNullOrEmpty(tag) ? throw new ArgumentException("A tag is empty.", parameter) : new Tag { Label = Label(tag) })
            .ToList();
        return labels.Count > 0 ? labels : throw new ArgumentException("It takes one tag at least.", parameter);
    }

    /// <summary>
    /// A tag as a body sends it: in double quotes when it holds whitespace or a comma, at which Tableau splits an unquoted
    /// label into several tags, unless it is in them already; as written otherwise.
    /// </summary>
    internal static string Label(string tag)
    {
        var quoted = tag.Length >= 2 && tag[0] == '"' && tag[^1] == '"';
        return !quoted && tag.Any(character => char.IsWhiteSpace(character) || character == ',') ? $"\"{tag}\"" : tag;
    }
}
