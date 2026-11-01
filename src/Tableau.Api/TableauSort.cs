using System;
using System.Collections.Generic;

namespace Tableau.Api;

/// <summary>
/// A sort expression for a list, by a resource's fields, such as
/// <c>TableauSort.Asc(UserField.SiteRole).Then(TableauSort.Desc(UserField.Name))</c>, which Tableau's syntax writes
/// <c>siteRole:asc,name:desc</c>.
/// </summary>
/// <remarks>
/// Tableau sorts on the fields it filters on, though not on every one. A list refuses a field of another resource
/// before it sends anything. The sort is immutable, and safe to share.
/// </remarks>
public sealed class TableauSort
{
    private readonly string _expression;
    private readonly IReadOnlyList<TableauField> _fields;

    private TableauSort(string expression, IReadOnlyList<TableauField> fields)
    {
        _expression = expression;
        _fields = fields;
    }

    /// <summary>The fields the sort names, which a list checks are its own resource's.</summary>
    internal IReadOnlyList<TableauField> Fields => _fields;

    /// <summary><c>field:asc</c>: by the field, smallest or earliest first.</summary>
    /// <param name="field">The field.</param>
    public static TableauSort Asc(TableauField field) => By(field, "asc");

    /// <summary><c>field:desc</c>: by the field, largest or latest first.</summary>
    /// <param name="field">The field.</param>
    public static TableauSort Desc(TableauField field) => By(field, "desc");

    /// <summary>This sort, then <paramref name="next"/> among items this one ranks the same.</summary>
    /// <param name="next">The sort that breaks this one's ties.</param>
    /// <returns>A new sort; this one is unchanged.</returns>
    public TableauSort Then(TableauSort next)
    {
        ArgumentNullException.ThrowIfNull(next);
        return new TableauSort(_expression + "," + next._expression, [.. _fields, .. next._fields]);
    }

    /// <summary>The expression in Tableau's syntax.</summary>
    public override string ToString() => _expression;

    private static TableauSort By(TableauField field, string direction)
    {
        ArgumentNullException.ThrowIfNull(field);
        return new TableauSort($"{field.Name}:{direction}", [field]);
    }
}
