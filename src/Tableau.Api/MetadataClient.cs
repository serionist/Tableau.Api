using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;
using Tableau.Api.Pipeline;
using Tableau.Api.Rest.Models;

namespace Tableau.Api;

/// <summary>
/// Tableau Catalog's assets and the Metadata API: <see cref="TableauClient.Metadata"/>. The REST API's metadata calls
/// (databases, tables and their columns, and data quality warnings, from REST API 3.5) and GraphQL queries of the
/// Metadata API.
/// </summary>
/// <remarks>
/// <para>
/// Updating or deleting an asset, and data quality warnings, need Tableau's Data Management; reading assets doesn't.
/// </para>
/// <para>
/// A GraphQL query goes to <c>api/metadata/graphql</c> in the client's session. On Tableau Server the Metadata API is off
/// until a server administrator runs <c>tsm maintenance metadata-services enable</c>; Tableau Cloud needs Data
/// Management. Its JSON is read through the <see cref="JsonTypeInfo{T}"/> you give, such as one from a
/// <see cref="System.Text.Json.Serialization.JsonSerializerContext"/> of your own, so no reflection reads it and the
/// query works under native AOT.
/// </para>
/// <para>
/// An answer's <c>errors</c> entry that Tableau marks a warning (<c>extensions.severity</c> <c>WARNING</c>), such as
/// <c>BACKFILL_RUNNING</c>, <c>NODE_LIMIT_EXCEEDED</c> or <c>TIME_LIMIT_EXCEEDED</c>, comes with data that may be
/// partial: the client logs it at Warning and returns the data. Any other entry fails the call as a
/// <see cref="TableauException"/>, the warnings beside it named too.
/// </para>
/// </remarks>
public class MetadataClient
{
    private const string WarnedTakes = "a database, a table, a column, a data source, a flow or a virtual connection";

    /// <summary>The most nodes a Metadata API page holds, as its reference gives it.</summary>
    private const int MostNodes = 1000;

    // Query Columns in a Table answers a page past the first with 400006 on Tableau Server 2025.1, whatever it holds, so
    // the columns are read in one page of the most a page holds.
    private static readonly IReadOnlyList<KeyValuePair<string, string?>> _allColumns = [new("pageSize", "1000")];

    private readonly TableauPipeline? _pipeline;

    /// <summary>A sub-client with no connection, for a mock to derive from: its methods throw until overridden.</summary>
    protected MetadataClient()
    {
    }

    internal MetadataClient(TableauPipeline pipeline)
    {
        _pipeline = pipeline;
    }

    private TableauPipeline Pipeline => _pipeline ?? throw TableauClient.ForMocksOnly();

    /// <summary>
    /// Query Databases, every page: the site's database assets, read a page at a time as you go, in Tableau's order. It
    /// reads its pages through <see cref="ListDatabasesPageAsync"/>, so a mock that overrides that serves this too.
    /// </summary>
    /// <param name="options">The page size; null for 100 a page.</param>
    /// <param name="cancellationToken">Stops the list, between databases and between pages.</param>
    /// <returns>The databases, as you read them.</returns>
    /// <exception cref="ArgumentException">The page size is outside 1 to 1,000.</exception>
    /// <exception cref="TableauException">A page's call failed; <see cref="TableauException.Kind"/> says how.</exception>
    public virtual async IAsyncEnumerable<Database> ListDatabasesAsync(DatabaseListOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var database in TableauPaging.ListAsync((number, token) => ListDatabasesPageAsync(number, options, token),
                               Endpoints.QueryDatabases.Name, null, cancellationToken).ConfigureAwait(false))
            yield return database;
    }

    /// <summary>Query Databases, one page.</summary>
    /// <param name="pageNumber">The page's number, from 1.</param>
    /// <param name="options">The page size; null for 100 a page.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The page, its databases empty, never null, when it has none.</returns>
    /// <exception cref="ArgumentException">The page number is below 1, or the page size outside 1 to 1,000.</exception>
    /// <exception cref="TableauException">
    /// The call failed; <see cref="TableauException.Kind"/> says how. Tableau answers a page past the end with an error.
    /// </exception>
    public virtual Task<Page<Database>> ListDatabasesPageAsync(int pageNumber, DatabaseListOptions? options = null,
        CancellationToken cancellationToken = default) =>
        TableauPaging.PageAsync(Pipeline, Endpoints.QueryDatabases, null, pageNumber, (options ?? new DatabaseListOptions()).PageSize, null, null, null,
            "databases", static body => body.Databases, cancellationToken);

    /// <summary>Query Database: one database asset.</summary>
    /// <param name="id">The database's LUID.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The database.</returns>
    /// <exception cref="ArgumentException"><paramref name="id"/> is no LUID.</exception>
    /// <exception cref="TableauException">The call failed; <see cref="TableauException.Kind"/> says how.</exception>
    public virtual Task<Database> GetDatabaseAsync(string id, CancellationToken cancellationToken = default) =>
        Pipeline.SendAsync(Endpoints.QueryDatabase, new TableauRequest([RequestUri.Luid(id, nameof(id))]), static body => body.Database, "database",
            cancellationToken);

    /// <summary>
    /// Update Database: changes the members of <paramref name="changes"/> that are set, of those the reference lists
    /// (<see cref="Database.IsCertified"/>, <see cref="Database.CertificationNote"/>, <see cref="Database.Description"/>,
    /// <see cref="Database.ContentPermissions"/> and <see cref="Database.Contact"/>'s id), and leaves the others. Only
    /// those are sent, so a database read with <see cref="GetDatabaseAsync"/> and changed with <c>with</c> sends nothing else.
    /// </summary>
    /// <param name="id">The database's LUID.</param>
    /// <param name="changes">What to change: set only those members, such as <c>new Database { Description = "..." }</c>.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The database as Tableau holds it now.</returns>
    /// <exception cref="ArgumentException"><paramref name="id"/> is no LUID, or the contact's id is none.</exception>
    /// <exception cref="TableauException">The call failed; <see cref="TableauException.Kind"/> says how.</exception>
    public virtual Task<Database> UpdateDatabaseAsync(string id, Database changes, CancellationToken cancellationToken = default)
    {
        RequestUri.Luid(id, nameof(id));
        ArgumentNullException.ThrowIfNull(changes);
        var sent = new Database
        {
            IsCertified = changes.IsCertified,
            CertificationNote = changes.CertificationNote,
            Description = changes.Description,
            ContentPermissions = changes.ContentPermissions,
            Contact = Contact(changes.Contact, nameof(changes)),
        };
        return Pipeline.SendAsync(Endpoints.UpdateDatabase, new TableauRequest([id], Body: new TsRequest { Database = sent }),
            static body => body.Database, "database", cancellationToken);
    }

    /// <summary>Delete Database: removes the database asset from Tableau Catalog.</summary>
    /// <param name="id">The database's LUID.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <exception cref="ArgumentException"><paramref name="id"/> is no LUID.</exception>
    /// <exception cref="TableauException">The call failed; <see cref="TableauException.Kind"/> says how.</exception>
    public virtual Task DeleteDatabaseAsync(string id, CancellationToken cancellationToken = default) =>
        Pipeline.SendAsync(Endpoints.DeleteDatabase, new TableauRequest([RequestUri.Luid(id, nameof(id))]), cancellationToken);

    /// <summary>
    /// Query Tables, every page: the site's table assets, read a page at a time as you go, in Tableau's order. It reads
    /// its pages through <see cref="ListTablesPageAsync"/>, so a mock that overrides that serves this too.
    /// </summary>
    /// <param name="options">The page size; null for 100 a page.</param>
    /// <param name="cancellationToken">Stops the list, between tables and between pages.</param>
    /// <returns>The tables, as you read them.</returns>
    /// <exception cref="ArgumentException">The page size is outside 1 to 1,000.</exception>
    /// <exception cref="TableauException">A page's call failed; <see cref="TableauException.Kind"/> says how.</exception>
    public virtual async IAsyncEnumerable<Table> ListTablesAsync(TableListOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var table in TableauPaging.ListAsync((number, token) => ListTablesPageAsync(number, options, token),
                               Endpoints.QueryTables.Name, null, cancellationToken).ConfigureAwait(false))
            yield return table;
    }

    /// <summary>Query Tables, one page.</summary>
    /// <param name="pageNumber">The page's number, from 1.</param>
    /// <param name="options">The page size; null for 100 a page.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The page, its tables empty, never null, when it has none.</returns>
    /// <exception cref="ArgumentException">The page number is below 1, or the page size outside 1 to 1,000.</exception>
    /// <exception cref="TableauException">
    /// The call failed; <see cref="TableauException.Kind"/> says how. Tableau answers a page past the end with an error.
    /// </exception>
    public virtual Task<Page<Table>> ListTablesPageAsync(int pageNumber, TableListOptions? options = null, CancellationToken cancellationToken = default) =>
        TableauPaging.PageAsync(Pipeline, Endpoints.QueryTables, null, pageNumber, (options ?? new TableListOptions()).PageSize, null, null, null,
            "tables", static body => body.Tables, cancellationToken);

    /// <summary>Query Table: one table asset.</summary>
    /// <param name="id">The table's LUID.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The table.</returns>
    /// <exception cref="ArgumentException"><paramref name="id"/> is no LUID.</exception>
    /// <exception cref="TableauException">The call failed; <see cref="TableauException.Kind"/> says how.</exception>
    public virtual Task<Table> GetTableAsync(string id, CancellationToken cancellationToken = default) =>
        Pipeline.SendAsync(Endpoints.QueryTable, new TableauRequest([RequestUri.Luid(id, nameof(id))]), static body => body.Table, "table",
            cancellationToken);

    /// <summary>
    /// Update Table: changes the members of <paramref name="changes"/> that are set, of those the reference lists
    /// (<see cref="Table.IsCertified"/>, <see cref="Table.CertificationNote"/>, <see cref="Table.Description"/> and
    /// <see cref="Table.Contact"/>'s id), and leaves the others. Only those are sent.
    /// </summary>
    /// <param name="id">The table's LUID.</param>
    /// <param name="changes">What to change: set only those members, such as <c>new Table { IsCertified = true }</c>.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The table as Tableau holds it now.</returns>
    /// <exception cref="ArgumentException"><paramref name="id"/> is no LUID, or the contact's id is none.</exception>
    /// <exception cref="TableauException">The call failed; <see cref="TableauException.Kind"/> says how.</exception>
    public virtual Task<Table> UpdateTableAsync(string id, Table changes, CancellationToken cancellationToken = default)
    {
        RequestUri.Luid(id, nameof(id));
        ArgumentNullException.ThrowIfNull(changes);
        var sent = new Table
        {
            IsCertified = changes.IsCertified,
            CertificationNote = changes.CertificationNote,
            Description = changes.Description,
            Contact = Contact(changes.Contact, nameof(changes)),
        };
        return Pipeline.SendAsync(Endpoints.UpdateTable, new TableauRequest([id], Body: new TsRequest { Table = sent }),
            static body => body.Table, "table", cancellationToken);
    }

    /// <summary>Delete Table: removes the table asset from Tableau Catalog.</summary>
    /// <param name="id">The table's LUID.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <exception cref="ArgumentException"><paramref name="id"/> is no LUID.</exception>
    /// <exception cref="TableauException">The call failed; <see cref="TableauException.Kind"/> says how.</exception>
    public virtual Task DeleteTableAsync(string id, CancellationToken cancellationToken = default) =>
        Pipeline.SendAsync(Endpoints.DeleteTable, new TableauRequest([RequestUri.Luid(id, nameof(id))]), cancellationToken);

    /// <summary>
    /// Query Columns in a Table: the table's columns, in Tableau's order, from one call that asks for 1,000, the most a
    /// page holds: Tableau Server 2025.1 refuses every page of a table's columns after the first.
    /// </summary>
    /// <remarks>
    /// A table of more than 1,000 columns can't be read whole this way. Tableau Server 2025.1 answers with no pagination,
    /// so a list of exactly 1,000 may stop short without a word; an answer whose pagination counts more columns than it
    /// holds fails as <see cref="TableauErrorKind.InvalidResponse"/>. The Metadata API, through <see cref="ListNodesAsync"/>,
    /// pages a table's columns by cursor.
    /// </remarks>
    /// <param name="tableId">The table's LUID.</param>
    /// <param name="cancellationToken">Stops the list, and cancels its call.</param>
    /// <returns>The columns, as you read them.</returns>
    /// <exception cref="ArgumentException"><paramref name="tableId"/> is no LUID, which the first read throws.</exception>
    /// <exception cref="TableauException">
    /// The call failed; <see cref="TableauException.Kind"/> says how, <see cref="TableauErrorKind.InvalidResponse"/> when
    /// its pagination counts more columns than it holds.
    /// </exception>
    public virtual async IAsyncEnumerable<Column> ListColumnsAsync(string tableId, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        RequestUri.Luid(tableId, nameof(tableId));
        var body = await Pipeline.SendAsync(Endpoints.QueryColumns, new TableauRequest([tableId], _allColumns), static body => body, "a body",
            cancellationToken).ConfigureAwait(false);
        var held = body.Columns?.Count ?? 0;
        if (body.Pagination?.TotalAvailable is { } total && total > held)
        {
            throw new TableauException(TableauErrorKind.InvalidResponse, string.Create(CultureInfo.InvariantCulture,
                $"{Endpoints.QueryColumns.Name} answered {held} of the table's {total} columns, and Tableau refuses a page after the first, ")
                + "so the rest can't be read through the REST API; the Metadata API pages them by cursor.")
            { Method = Endpoints.QueryColumns.Method };
        }
        foreach (var column in body.Columns ?? [])
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return column;
        }
    }

    /// <summary>Update Column: the column's description, the one member the reference lets it change, and the one sent.</summary>
    /// <param name="tableId">The LUID of the column's table.</param>
    /// <param name="columnId">The column's LUID.</param>
    /// <param name="changes">What to change: <c>new Column { Description = "..." }</c>; its other members aren't sent.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The column as Tableau holds it now.</returns>
    /// <exception cref="ArgumentException"><paramref name="tableId"/> or <paramref name="columnId"/> is no LUID.</exception>
    /// <exception cref="TableauException">The call failed; <see cref="TableauException.Kind"/> says how.</exception>
    public virtual Task<Column> UpdateColumnAsync(string tableId, string columnId, Column changes, CancellationToken cancellationToken = default)
    {
        RequestUri.Luid(tableId, nameof(tableId));
        RequestUri.Luid(columnId, nameof(columnId));
        ArgumentNullException.ThrowIfNull(changes);
        return Pipeline.SendAsync(Endpoints.UpdateColumn,
            new TableauRequest([tableId, columnId], Body: new TsRequest { Column = new Column { Description = changes.Description } }),
            static body => body.Column, "column", cancellationToken);
    }

    /// <summary>Delete Column: removes the column asset from Tableau Catalog.</summary>
    /// <param name="tableId">The LUID of the column's table.</param>
    /// <param name="columnId">The column's LUID.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <exception cref="ArgumentException"><paramref name="tableId"/> or <paramref name="columnId"/> is no LUID.</exception>
    /// <exception cref="TableauException">The call failed; <see cref="TableauException.Kind"/> says how.</exception>
    public virtual Task DeleteColumnAsync(string tableId, string columnId, CancellationToken cancellationToken = default) =>
        Pipeline.SendAsync(Endpoints.DeleteColumn,
            new TableauRequest([RequestUri.Luid(tableId, nameof(tableId)), RequestUri.Luid(columnId, nameof(columnId))]), cancellationToken);

    /// <summary>
    /// Add Data Quality Warning: a warning on a database, a table, a column (REST API 3.16), a data source, a flow or a
    /// virtual connection. Sent once, never again after a failure: a second would add a second warning, or, up to Tableau
    /// Server 2024.2 and Tableau Cloud February 2024, where an asset held one warning, fail.
    /// </summary>
    /// <param name="content">The asset, such as <c>TableauContent.Table(id)</c>.</param>
    /// <param name="warning">
    /// The warning: its <see cref="DataQualityWarning.Type"/>, such as <c>WARNING</c>, <c>DEPRECATED</c>, <c>STALE</c> or
    /// <c>MAINTENANCE</c>, and as you choose <see cref="DataQualityWarning.Message"/>, <see cref="DataQualityWarning.IsSevere"/>
    /// and <see cref="DataQualityWarning.IsActive"/>, which Tableau takes as true, the warning shown, when it is left null;
    /// nothing else of it is sent.
    /// </param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The warning as Tableau made it, with its id.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="content"/> is of a kind that takes no warning, or the warning has no type: nothing is sent.
    /// </exception>
    /// <exception cref="TableauException">The call failed; <see cref="TableauException.Kind"/> says how.</exception>
    public virtual Task<DataQualityWarning> AddDataQualityWarningAsync(TableauContent content, DataQualityWarning warning,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        var type = Warned(content);
        return Pipeline.SendAsync(content.Kind == TableauContentKind.Column ? Endpoints.AddColumnDataQualityWarning : Endpoints.AddDataQualityWarning,
            new TableauRequest([type, content.Id], Body: new TsRequest { DataQualityWarning = Sent(warning, nameof(warning)) }),
            static body => body.DataQualityWarning, "dataQualityWarning", cancellationToken);
    }

    /// <summary>Update Data Quality Warning: its type, its message, its severity and whether it shows.</summary>
    /// <param name="id">The warning's LUID.</param>
    /// <param name="changes">
    /// The warning: its <see cref="DataQualityWarning.Type"/>, which the reference requires, and as you choose
    /// <see cref="DataQualityWarning.Message"/>, <see cref="DataQualityWarning.IsSevere"/> and
    /// <see cref="DataQualityWarning.IsActive"/>. Left null, <see cref="DataQualityWarning.IsActive"/> is not kept as it was:
    /// Tableau sets it true, and the warning shows. Nothing else of it is sent.
    /// </param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The warning as Tableau holds it now.</returns>
    /// <exception cref="ArgumentException"><paramref name="id"/> is no LUID, or the warning has no type: nothing is sent.</exception>
    /// <exception cref="TableauException">The call failed; <see cref="TableauException.Kind"/> says how.</exception>
    public virtual Task<DataQualityWarning> UpdateDataQualityWarningAsync(string id, DataQualityWarning changes,
        CancellationToken cancellationToken = default)
    {
        RequestUri.Luid(id, nameof(id));
        return Pipeline.SendAsync(Endpoints.UpdateDataQualityWarning,
            new TableauRequest([id], Body: new TsRequest { DataQualityWarning = Sent(changes, nameof(changes)) }),
            static body => body.DataQualityWarning, "dataQualityWarning", cancellationToken);
    }

    /// <summary>Query Data Quality Warning by Content: the asset's warnings, from one call.</summary>
    /// <param name="content">The asset, such as <c>TableauContent.Database(id)</c>.</param>
    /// <param name="cancellationToken">Stops the list, and cancels its call.</param>
    /// <returns>The warnings, as you read them; none when the asset has none.</returns>
    /// <exception cref="ArgumentException"><paramref name="content"/> is of a kind that takes no warning, which the first read throws.</exception>
    /// <exception cref="TableauException">The call failed; <see cref="TableauException.Kind"/> says how.</exception>
    public virtual async IAsyncEnumerable<DataQualityWarning> ListDataQualityWarningsAsync(TableauContent content,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        var type = Warned(content);
        var body = await Pipeline.SendAsync(
            content.Kind == TableauContentKind.Column ? Endpoints.QueryColumnDataQualityWarnings : Endpoints.QueryDataQualityWarnings,
            new TableauRequest([type, content.Id]), static body => body, "a body", cancellationToken).ConfigureAwait(false);
        foreach (var warning in body.DataQualityWarningList ?? [])
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return warning;
        }
    }

    /// <summary>Delete Data Quality Warning by ID: removes one warning.</summary>
    /// <param name="id">The warning's LUID.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <exception cref="ArgumentException"><paramref name="id"/> is no LUID.</exception>
    /// <exception cref="TableauException">The call failed; <see cref="TableauException.Kind"/> says how.</exception>
    public virtual Task DeleteDataQualityWarningAsync(string id, CancellationToken cancellationToken = default) =>
        Pipeline.SendAsync(Endpoints.DeleteDataQualityWarning, new TableauRequest([RequestUri.Luid(id, nameof(id))]), cancellationToken);

    /// <summary>Delete Data Quality Warning by Content: removes the asset's warnings.</summary>
    /// <param name="content">The asset, such as <c>TableauContent.Flow(id)</c>.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <exception cref="ArgumentException"><paramref name="content"/> is of a kind that takes no warning: nothing is sent.</exception>
    /// <exception cref="TableauException">The call failed; <see cref="TableauException.Kind"/> says how.</exception>
    public virtual Task DeleteDataQualityWarningsAsync(TableauContent content, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        var type = Warned(content);
        return Pipeline.SendAsync(
            content.Kind == TableauContentKind.Column ? Endpoints.DeleteColumnDataQualityWarnings : Endpoints.DeleteDataQualityWarnings,
            new TableauRequest([type, content.Id]), cancellationToken);
    }

    /// <summary>A Metadata API query: its <c>data</c>, as JSON.</summary>
    /// <param name="query">The GraphQL query, such as <c>{ databases { id name } }</c>.</param>
    /// <param name="variables">The query's variables, or null for none.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The answer's <c>data</c>, which outlives the call; it may be partial when the answer carried warnings, which are logged.</returns>
    /// <exception cref="ArgumentException"><paramref name="query"/> is empty.</exception>
    /// <exception cref="TableauException">
    /// The call failed, or its answer carried an <c>errors</c> entry that is no warning:
    /// <see cref="TableauException.ErrorCode"/> is the first entry's code, such as <c>FEATURE_DISABLED</c>,
    /// <see cref="TableauException.Summary"/> its message and <see cref="TableauException.Detail"/> the others';
    /// <see cref="TableauErrorKind.RateLimited"/> for <c>RATE_EXCEEDED</c>, whatever the answer's status.
    /// </exception>
    public virtual Task<JsonElement> QueryAsync(string query, JsonObject? variables = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        var pipeline = Pipeline;
        return pipeline.SendForJsonAsync(Endpoints.MetadataQuery, new TableauRequest(Json: TableauGraphQL.Body(query, variables)),
            answer => TableauGraphQL.Read(pipeline, Endpoints.MetadataQuery, answer, static data => data.Clone()), cancellationToken);
    }

    /// <summary>
    /// A Metadata API query, its <c>data</c> read through <paramref name="resultType"/>, such as
    /// <c>MyJsonContext.Default.DatabasesData</c> for your own <c>record DatabasesData(...)</c>, so no reflection reads it.
    /// </summary>
    /// <typeparam name="T">The type <c>data</c> reads as.</typeparam>
    /// <param name="query">The GraphQL query.</param>
    /// <param name="variables">The query's variables, or null for none.</param>
    /// <param name="resultType">How <c>data</c> reads.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The answer's <c>data</c>; it may be partial when the answer carried warnings, which are logged.</returns>
    /// <exception cref="ArgumentException"><paramref name="query"/> is empty.</exception>
    /// <exception cref="TableauException">
    /// The call failed, its answer carried an <c>errors</c> entry that is no warning, as
    /// <see cref="QueryAsync(string, JsonObject?, CancellationToken)"/> says, or its <c>data</c> doesn't read as
    /// <typeparamref name="T"/> (<see cref="TableauErrorKind.InvalidResponse"/>).
    /// </exception>
    public virtual Task<T> QueryAsync<T>(string query, JsonObject? variables, JsonTypeInfo<T> resultType, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        ArgumentNullException.ThrowIfNull(resultType);
        var pipeline = Pipeline;
        return pipeline.SendForJsonAsync(Endpoints.MetadataQuery, new TableauRequest(Json: TableauGraphQL.Body(query, variables)),
            answer => TableauGraphQL.Read(pipeline, Endpoints.MetadataQuery, answer,
                data => data.Deserialize(resultType) ?? throw new InvalidDataException("its data reads as null.")), cancellationToken);
    }

    /// <summary>
    /// One page of a connection, the Metadata API's paged lists, such as <c>databasesConnection</c>: the query is sent with
    /// its variables <c>first</c>, the page's size, and <c>after</c>, the cursor it starts after, and the page read from
    /// the connection's <c>nodes</c> and <c>pageInfo</c>.
    /// </summary>
    /// <remarks>
    /// The query declares the two variables and pages the connection by them, and asks for its nodes and its page info:
    /// <c>query($first: Int, $after: String) { databasesConnection(first: $first, after: $after) { nodes { id name }
    /// pageInfo { hasNextPage endCursor } } }</c>.
    /// </remarks>
    /// <typeparam name="T">The type a node reads as.</typeparam>
    /// <param name="query">The GraphQL query.</param>
    /// <param name="connection">
    /// Where the connection is in <c>data</c>: its field's name, such as <c>databasesConnection</c>, or the names of the
    /// fields to it, joined with dots.
    /// </param>
    /// <param name="nodeType">How a node reads, such as <c>MyJsonContext.Default.DatabaseNode</c>.</param>
    /// <param name="after">The cursor the page starts after, a page's <see cref="CursorPage{T}.EndCursor"/>; null for the first.</param>
    /// <param name="variables">The query's other variables, or null for none; <c>first</c> and <c>after</c> are set over them.</param>
    /// <param name="pageSize">How many nodes the page holds: 1 to 1,000, the Metadata API's most.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The page: its nodes, empty, never null, when it has none, and where the next starts.</returns>
    /// <exception cref="ArgumentException"><paramref name="query"/> or <paramref name="connection"/> is empty, or the page size is outside 1 to 1,000.</exception>
    /// <exception cref="TableauException">
    /// The call failed, its answer carried an <c>errors</c> entry that is no warning, or it holds no such connection, or the
    /// connection lacks its nodes, its <c>pageInfo</c>, or, when more pages follow, its <c>endCursor</c>
    /// (<see cref="TableauErrorKind.InvalidResponse"/>).
    /// </exception>
    public virtual Task<CursorPage<T>> QueryPageAsync<T>(string query, string connection, JsonTypeInfo<T> nodeType, string? after = null,
        JsonObject? variables = null, int pageSize = 100, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        ArgumentException.ThrowIfNullOrWhiteSpace(connection);
        if (connection.Split('.').Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException($"'{connection}' names an empty field: write the fields' names joined with dots.", nameof(connection));
        ArgumentNullException.ThrowIfNull(nodeType);
        if (pageSize is < 1 or > MostNodes)
            throw new ArgumentOutOfRangeException(nameof(pageSize), pageSize, $"A page holds 1 to {MostNodes} nodes, the Metadata API's most.");
        var body = TableauGraphQL.Body(query, TableauGraphQL.PageVariables(variables, pageSize, after));
        var pipeline = Pipeline;
        return pipeline.SendForJsonAsync(Endpoints.MetadataQuery, new TableauRequest(Json: body),
            answer => TableauGraphQL.Read(pipeline, Endpoints.MetadataQuery, answer, data => TableauGraphQL.Page(data, connection, nodeType)),
            cancellationToken);
    }

    /// <summary>
    /// Every node of a connection, a page at a time as you read on, each page after the last one's <c>endCursor</c> until
    /// its <c>pageInfo</c> says <c>hasNextPage</c> is false. It reads its pages through <see cref="QueryPageAsync"/>,
    /// whose remarks give the query's shape, so a mock that overrides that serves this too.
    /// </summary>
    /// <typeparam name="T">The type a node reads as.</typeparam>
    /// <param name="query">The GraphQL query, paging the connection by the variables <c>first</c> and <c>after</c>.</param>
    /// <param name="connection">Where the connection is in <c>data</c>, such as <c>databasesConnection</c>.</param>
    /// <param name="nodeType">How a node reads.</param>
    /// <param name="variables">The query's other variables, or null for none.</param>
    /// <param name="pageSize">How many nodes a page holds: 1 to 1,000, 100 by default.</param>
    /// <param name="cancellationToken">Stops the list, between nodes and between pages.</param>
    /// <returns>The nodes, as you read them.</returns>
    /// <exception cref="ArgumentException">An argument <see cref="QueryPageAsync"/> refuses, which the first read throws.</exception>
    /// <exception cref="TableauException">
    /// A page's call failed, as <see cref="QueryPageAsync"/> says, or a page said more follow without a new cursor
    /// (<see cref="TableauErrorKind.InvalidResponse"/>), where reading on would repeat pages.
    /// </exception>
    public virtual async IAsyncEnumerable<T> ListNodesAsync<T>(string query, string connection, JsonTypeInfo<T> nodeType, JsonObject? variables = null,
        int pageSize = 100, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var node in TableauPaging.CursorListAsync((after, token) => QueryPageAsync(query, connection, nodeType, after, variables, pageSize, token),
                               Endpoints.MetadataQuery.Name, cancellationToken).ConfigureAwait(false))
            yield return node;
    }

    // The content type a data quality warning's path names; a kind that takes no warning is refused.
    private static string Warned(TableauContent content) => content.Kind switch
    {
        TableauContentKind.Database => "database",
        TableauContentKind.Table => "table",
        TableauContentKind.Column => "column",
        TableauContentKind.DataSource => "datasource",
        TableauContentKind.Flow => "flow",
        TableauContentKind.VirtualConnection => "virtualconnection",
        _ => throw content.Refused("A data quality warning takes", WarnedTakes, nameof(content)),
    };

    // An asset's contact as the reference's update bodies write it: the user by its LUID alone.
    private static User? Contact(User? contact, string parameter) =>
        contact is null ? null : new User { Id = RequestUri.Luid(contact.Id, parameter) };

    // A warning as the reference's bodies write it: its type, required, and its message, severity and state.
    private static DataQualityWarning Sent(DataQualityWarning warning, string parameter)
    {
        ArgumentNullException.ThrowIfNull(warning, parameter);
        if (string.IsNullOrEmpty(warning.Type))
            throw new ArgumentException("A data quality warning needs its Type, such as WARNING; Tableau refuses one without.", parameter);
        return new DataQualityWarning { Type = warning.Type, Message = warning.Message, IsSevere = warning.IsSevere, IsActive = warning.IsActive };
    }
}
