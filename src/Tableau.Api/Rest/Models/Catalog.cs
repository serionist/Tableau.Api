using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Tableau.Api.Rest.Models;

// Tableau Catalog's answers that Tableau's schema, ts-api_3_29.xsd, leaves out: the column, and the lists of databases,
// tables and columns, which Query Databases, Query Tables and Query Columns in a Table answer. Written here by hand,
// from the reference and from what Tableau Server 2025.1 answered, in the generator's shapes and names. Should a schema
// bring any of them, the generator writes it and its counterpart here goes. The JSON context lists the column through
// the envelopes' members, which its source generator follows: a second list of types on another part of the context
// would make it generate each type twice.

/// <summary>
/// A column of a table, an external asset Tableau Catalog lists, as Query Columns in a Table and Update Column answer it.
/// Tableau's schema has no type for it, so this one is written by hand, in the generated models' shape.
/// </summary>
public sealed partial record Column
{
    /// <summary>The <c>site</c> element.</summary>
    [JsonPropertyName("site")]
    public Site? Site { get; init; }

    /// <summary>The <c>tags</c> element: its <c>tag</c> elements, as a list.</summary>
    [JsonPropertyName("tags")]
    [JsonConverter(typeof(TagListConverter))]
    public IReadOnlyList<Tag>? Tags { get; init; }

    /// <summary>The <c>description</c> attribute, which Update Column sets.</summary>
    [JsonPropertyName("description")]
    public string? Description { get; init; }

    /// <summary>The <c>id</c> attribute.</summary>
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    /// <summary>The <c>name</c> attribute.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    /// <summary>The <c>nullable</c> attribute: whether the column may hold no value.</summary>
    [JsonPropertyName("nullable")]
    public bool? Nullable { get; init; }

    /// <summary>The <c>parentTableId</c> attribute: the LUID of the column's table.</summary>
    [JsonPropertyName("parentTableId")]
    public string? ParentTableId { get; init; }

    /// <summary>The <c>remoteType</c> attribute: the column's type in its database, such as <c>I4</c> or <c>STR</c>.</summary>
    [JsonPropertyName("remoteType")]
    public string? RemoteType { get; init; }
}

/// <summary>The catalog's answers, beside the schema's.</summary>
internal sealed partial record TsResponse
{
    /// <summary>The <c>column</c> element: Update Column's answer.</summary>
    [JsonPropertyName("column")]
    public Column? Column { get; init; }

    /// <summary>The <c>columns</c> element: its <c>column</c> elements, as a list.</summary>
    [JsonPropertyName("columns")]
    [JsonConverter(typeof(ColumnListConverter))]
    public IReadOnlyList<Column>? Columns { get; init; }

    /// <summary>The <c>databases</c> element: its <c>database</c> elements, as a list.</summary>
    [JsonPropertyName("databases")]
    [JsonConverter(typeof(DatabaseListConverter))]
    public IReadOnlyList<Database>? Databases { get; init; }

    /// <summary>The <c>tables</c> element: its <c>table</c> elements, as a list.</summary>
    [JsonPropertyName("tables")]
    [JsonConverter(typeof(TableListConverter))]
    public IReadOnlyList<Table>? Tables { get; init; }
}

/// <summary>The catalog's request, beside the schema's.</summary>
internal sealed partial record TsRequest
{
    /// <summary>The <c>column</c> element: Update Column's body.</summary>
    [JsonPropertyName("column")]
    public Column? Column { get; init; }
}

/// <summary>Reads and writes Query Columns in a Table's <c>columns</c>: its <c>column</c> elements, as a list, inside the wrapper object.</summary>
internal sealed class ColumnListConverter() : TableauWrappedListConverter<Column>("column");

/// <summary>Reads and writes Query Databases' <c>databases</c>: its <c>database</c> elements, as a list, inside the wrapper object.</summary>
internal sealed class DatabaseListConverter() : TableauWrappedListConverter<Database>("database");

/// <summary>Reads and writes Query Tables' <c>tables</c>: its <c>table</c> elements, as a list, inside the wrapper object.</summary>
internal sealed class TableListConverter() : TableauWrappedListConverter<Table>("table");
