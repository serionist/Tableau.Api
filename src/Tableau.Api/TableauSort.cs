using System;
using System.Collections.Generic;
using System.Linq;

namespace Tableau.Api;

/// <summary>
/// A sort expression for a list, by a resource's fields, such as
/// <c>TableauSort.Asc(UserField.SiteRole).Then(TableauSort.Desc(UserField.Name))</c>, which Tableau's syntax writes
/// <c>siteRole:asc,name:desc</c>.
/// </summary>
/// <remarks>
/// <para>
/// Tableau sorts on the fields it filters on, though not on every one. A list refuses a field of another resource
/// before it sends anything, and after the fields given sorts by its own default fields that the sort doesn't name, so
/// items the sort ranks the same keep their order from page to page.
/// </para>
/// <para>
/// The sort is immutable, and safe to share. Two sorts are equal when they write the same expression over the same
/// fields.
/// </para>
/// </remarks>
public sealed class TableauSort : IEquatable<TableauSort>
{
    private readonly IReadOnlyList<(TableauField Field, string Direction)> _terms;

    private TableauSort(IReadOnlyList<(TableauField Field, string Direction)> terms)
    {
        _terms = terms;
    }

    /// <summary>The fields the sort names, which a list checks are its own resource's.</summary>
    internal IEnumerable<TableauField> Fields => _terms.Select(term => term.Field);

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
        return new TableauSort([.. _terms, .. next._terms]);
    }

    /// <summary>The expression in Tableau's syntax.</summary>
    public override string ToString() => string.Join(',', _terms.Select(term => $"{term.Field.Name}:{term.Direction}"));

    /// <summary>Whether <paramref name="other"/> writes the same expression over the same fields.</summary>
    /// <param name="other">The other sort.</param>
    public bool Equals(TableauSort? other) =>
        other is not null && _terms.SequenceEqual(other._terms);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => Equals(obj as TableauSort);

    /// <inheritdoc/>
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(ToString());

    /// <summary>
    /// This sort, then <paramref name="stable"/>'s terms on fields this one doesn't name: a list's default order breaks
    /// the ties of the order its caller asked for.
    /// </summary>
    internal TableauSort ThenStable(TableauSort stable)
    {
        var named = _terms.Select(term => term.Field).ToHashSet();
        var rest = stable._terms.Where(term => !named.Contains(term.Field)).ToList();
        return rest.Count == 0 ? this : new TableauSort([.. _terms, .. rest]);
    }

    private static TableauSort By(TableauField field, string direction)
    {
        ArgumentNullException.ThrowIfNull(field);
        return new TableauSort([(field, direction)]);
    }
}
