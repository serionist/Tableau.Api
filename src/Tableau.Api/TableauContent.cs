using System;
using Tableau.Api.Pipeline;

namespace Tableau.Api;

/// <summary>What kind of content a <see cref="TableauContent"/> names.</summary>
public enum TableauContentKind
{
    /// <summary>A project.</summary>
    Project,

    /// <summary>A workbook.</summary>
    Workbook,

    /// <summary>A view, a sheet of a workbook.</summary>
    View,

    /// <summary>A published data source.</summary>
    DataSource,

    /// <summary>A flow, Tableau Prep's.</summary>
    Flow,

    /// <summary>A virtual connection.</summary>
    VirtualConnection,

    /// <summary>A collection.</summary>
    Collection,

    /// <summary>A database, an external asset Tableau Catalog lists.</summary>
    Database,

    /// <summary>A table, an external asset Tableau Catalog lists.</summary>
    Table,

    /// <summary>A column of a table, an external asset Tableau Catalog lists.</summary>
    Column,
}

/// <summary>
/// One piece of content on the site, by its kind and its LUID, such as <c>TableauContent.Workbook(id)</c>: what
/// permissions, tags, favorites and data quality warnings are about.
/// </summary>
/// <remarks>
/// <para>
/// Each area takes the kinds Tableau's reference gives it, and refuses another before it sends anything, such as a
/// favorite of a table or a tag on a project. Two values are equal when they name the same content: the same kind and
/// the same LUIDs, case aside.
/// </para>
/// <para>The value is immutable, and safe to share.</para>
/// </remarks>
public sealed class TableauContent : IEquatable<TableauContent>
{
    private TableauContent(TableauContentKind kind, string id, string? tableId = null)
    {
        Kind = kind;
        Id = id;
        TableId = tableId;
    }

    /// <summary>What kind of content it is.</summary>
    public TableauContentKind Kind { get; }

    /// <summary>The content's LUID; a column's own, for a column.</summary>
    public string Id { get; }

    /// <summary>For a column, the LUID of its table; null for any other kind.</summary>
    public string? TableId { get; }

    /// <summary>A project, by its LUID.</summary>
    /// <param name="id">The project's LUID.</param>
    /// <exception cref="ArgumentException"><paramref name="id"/> is no LUID.</exception>
    public static TableauContent Project(string id) => new(TableauContentKind.Project, RequestUri.Luid(id, nameof(id)));

    /// <summary>A workbook, by its LUID.</summary>
    /// <param name="id">The workbook's LUID.</param>
    /// <exception cref="ArgumentException"><paramref name="id"/> is no LUID.</exception>
    public static TableauContent Workbook(string id) => new(TableauContentKind.Workbook, RequestUri.Luid(id, nameof(id)));

    /// <summary>A view, by its LUID.</summary>
    /// <param name="id">The view's LUID.</param>
    /// <exception cref="ArgumentException"><paramref name="id"/> is no LUID.</exception>
    public static TableauContent View(string id) => new(TableauContentKind.View, RequestUri.Luid(id, nameof(id)));

    /// <summary>A published data source, by its LUID.</summary>
    /// <param name="id">The data source's LUID.</param>
    /// <exception cref="ArgumentException"><paramref name="id"/> is no LUID.</exception>
    public static TableauContent DataSource(string id) => new(TableauContentKind.DataSource, RequestUri.Luid(id, nameof(id)));

    /// <summary>A flow, by its LUID.</summary>
    /// <param name="id">The flow's LUID.</param>
    /// <exception cref="ArgumentException"><paramref name="id"/> is no LUID.</exception>
    public static TableauContent Flow(string id) => new(TableauContentKind.Flow, RequestUri.Luid(id, nameof(id)));

    /// <summary>A virtual connection, by its LUID.</summary>
    /// <param name="id">The virtual connection's LUID.</param>
    /// <exception cref="ArgumentException"><paramref name="id"/> is no LUID.</exception>
    public static TableauContent VirtualConnection(string id) => new(TableauContentKind.VirtualConnection, RequestUri.Luid(id, nameof(id)));

    /// <summary>A collection, by its LUID.</summary>
    /// <param name="id">The collection's LUID.</param>
    /// <exception cref="ArgumentException"><paramref name="id"/> is no LUID.</exception>
    public static TableauContent Collection(string id) => new(TableauContentKind.Collection, RequestUri.Luid(id, nameof(id)));

    /// <summary>A database asset, by its LUID.</summary>
    /// <param name="id">The database's LUID.</param>
    /// <exception cref="ArgumentException"><paramref name="id"/> is no LUID.</exception>
    public static TableauContent Database(string id) => new(TableauContentKind.Database, RequestUri.Luid(id, nameof(id)));

    /// <summary>A table asset, by its LUID.</summary>
    /// <param name="id">The table's LUID.</param>
    /// <exception cref="ArgumentException"><paramref name="id"/> is no LUID.</exception>
    public static TableauContent Table(string id) => new(TableauContentKind.Table, RequestUri.Luid(id, nameof(id)));

    /// <summary>A column asset, by its table's LUID and its own.</summary>
    /// <param name="tableId">The LUID of the column's table.</param>
    /// <param name="columnId">The column's LUID.</param>
    /// <exception cref="ArgumentException"><paramref name="tableId"/> or <paramref name="columnId"/> is no LUID.</exception>
    public static TableauContent Column(string tableId, string columnId)
    {
        RequestUri.Luid(tableId, nameof(tableId));
        return new(TableauContentKind.Column, RequestUri.Luid(columnId, nameof(columnId)), tableId);
    }

    /// <summary>Whether <paramref name="other"/> names the same content: the same kind and LUIDs, case aside.</summary>
    /// <param name="other">The other value.</param>
    public bool Equals(TableauContent? other) =>
        other is not null && Kind == other.Kind && string.Equals(Id, other.Id, StringComparison.OrdinalIgnoreCase)
        && string.Equals(TableId, other.TableId, StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => Equals(obj as TableauContent);

    /// <inheritdoc/>
    public override int GetHashCode() =>
        HashCode.Combine(Kind, StringComparer.OrdinalIgnoreCase.GetHashCode(Id), TableId is null ? 0 : StringComparer.OrdinalIgnoreCase.GetHashCode(TableId));

    /// <summary>The kind and the LUID, such as <c>Workbook 1f2f3e4e-...</c>; a column's table after it.</summary>
    public override string ToString() => TableId is null ? $"{Kind} {Id}" : $"{Kind} {Id} of table {TableId}";

    /// <summary>
    /// <see cref="ArgumentException"/> naming <paramref name="parameter"/>, for a kind the call can't take: what it takes,
    /// so the caller sees why before anything is sent.
    /// </summary>
    internal ArgumentException Refused(string call, string takes, string parameter) =>
        new($"{call} takes {takes}, not a {Words(Kind)}.", parameter);

    /// <summary>The kind in words, as a message names it: <c>data source</c>.</summary>
    internal static string Words(TableauContentKind kind) => kind switch
    {
        TableauContentKind.Project => "project",
        TableauContentKind.Workbook => "workbook",
        TableauContentKind.View => "view",
        TableauContentKind.DataSource => "data source",
        TableauContentKind.Flow => "flow",
        TableauContentKind.VirtualConnection => "virtual connection",
        TableauContentKind.Collection => "collection",
        TableauContentKind.Database => "database",
        TableauContentKind.Table => "table",
        TableauContentKind.Column => "column",
        _ => kind.ToString(),
    };
}
