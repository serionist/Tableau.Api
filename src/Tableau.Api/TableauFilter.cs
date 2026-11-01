using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Tableau.Api.Rest.Models;

namespace Tableau.Api;

/// <summary>
/// A filter expression for a list, built over a resource's fields, such as
/// <c>TableauFilter.And(TableauFilter.Eq(UserField.SiteRole, SiteRole.Viewer), TableauFilter.Gte(UserField.LastLogin, since))</c>,
/// which Tableau's syntax writes <c>siteRole:eq:Viewer,lastLogin:gte:2026-01-01T00:00:00Z</c>.
/// </summary>
/// <remarks>
/// <para>
/// A value is written as it is, not URL-encoded: the client escapes each part between the separators, <c>:</c> and
/// <c>,</c>, when it sends the filter, so <c>R&amp;D</c>, <c>C++</c>, <c>#</c> and <c>%</c> reach Tableau as written. A
/// value can't hold a comma: Tableau splits a filter at each one, escaped or not, and refuses the rest.
/// </para>
/// <para>
/// Tableau's reference lists which operators each field takes, as each field says; the server refuses another. Values
/// are compared as Tableau stores them, case included, but by <see cref="CiEq"/>. A list refuses a field of another
/// resource before it sends anything.
/// </para>
/// <para>
/// The filter is immutable, and safe to share. Two filters are equal when they write the same expression over the same
/// fields, so list options built the same way are equal too.
/// </para>
/// </remarks>
public sealed class TableauFilter : IEquatable<TableauFilter>
{
    private readonly string _expression;
    private readonly IReadOnlyList<TableauField> _fields;

    private TableauFilter(string expression, IReadOnlyList<TableauField> fields)
    {
        _expression = expression;
        _fields = fields;
    }

    /// <summary>The fields the filter names, which a list checks are its own resource's; none for one from a string.</summary>
    internal IReadOnlyList<TableauField> Fields => _fields;

    /// <summary><c>field:eq:value</c>: the field equals the value.</summary>
    /// <param name="field">The field.</param>
    /// <param name="value">The value, as written, not URL-encoded.</param>
    /// <exception cref="ArgumentException">The value holds a comma.</exception>
    public static TableauFilter Eq(TableauField field, string value) => Term(field, "eq", Value(value));

    /// <summary><c>field:eq:value</c>: the field, a site role, equals the value, such as <see cref="SiteRole.Viewer"/>.</summary>
    /// <param name="field">The field, such as <see cref="UserField.SiteRole"/> or <see cref="GroupField.MinimumSiteRole"/>.</param>
    /// <param name="value">The site role.</param>
    /// <exception cref="ArgumentException">The site role is a default one, which names none.</exception>
    public static TableauFilter Eq(TableauField field, SiteRole value) => Term(field, "eq", Value(value));

    /// <summary><c>field:eq:true</c> or <c>false</c>: the field, a flag, equals the value.</summary>
    /// <param name="field">The field.</param>
    /// <param name="value">The value.</param>
    public static TableauFilter Eq(TableauField field, bool value) => Term(field, "eq", value ? "true" : "false");

    /// <summary><c>field:eq:value</c>: the field, a number, equals the value.</summary>
    /// <param name="field">The field.</param>
    /// <param name="value">The value.</param>
    public static TableauFilter Eq(TableauField field, long value) => Term(field, "eq", Number(value));

    /// <summary><c>field:eq:value</c>: the field, a date and time, equals the value, written in UTC to the second.</summary>
    /// <param name="field">The field.</param>
    /// <param name="value">The value.</param>
    public static TableauFilter Eq(TableauField field, DateTimeOffset value) => Term(field, "eq", Date(value));

    /// <summary><c>field:cieq:value</c>: the field equals the value, case aside.</summary>
    /// <param name="field">The field.</param>
    /// <param name="value">The value, as written, not URL-encoded.</param>
    /// <exception cref="ArgumentException">The value holds a comma.</exception>
    public static TableauFilter CiEq(TableauField field, string value) => Term(field, "cieq", Value(value));

    /// <summary><c>field:in:[a,b]</c>: the field equals one of the values.</summary>
    /// <param name="field">The field.</param>
    /// <param name="values">The values, as written, not URL-encoded; one at least.</param>
    /// <exception cref="ArgumentException">There is no value, or one holds a comma.</exception>
    public static TableauFilter In(TableauField field, params IEnumerable<string> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        return InList(field, [.. values.Select(Value)]);
    }

    /// <summary><c>field:in:[a,b]</c>: the field, a site role, equals one of the values, such as <see cref="SiteRole.Viewer"/>.</summary>
    /// <param name="field">The field, such as <see cref="UserField.SiteRole"/> or <see cref="GroupField.MinimumSiteRole"/>.</param>
    /// <param name="values">The site roles; one at least.</param>
    /// <exception cref="ArgumentException">There is no value, or one is a default site role, which names none.</exception>
    public static TableauFilter In(TableauField field, params IEnumerable<SiteRole> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        return InList(field, [.. values.Select(Value)]);
    }

    /// <summary><c>field:has:value</c>: the field holds the value. Tableau's reference has it for Query Jobs alone.</summary>
    /// <param name="field">The field.</param>
    /// <param name="value">The value, as written, not URL-encoded.</param>
    /// <exception cref="ArgumentException">The value holds a comma.</exception>
    public static TableauFilter Has(TableauField field, string value) => Term(field, "has", Value(value));

    /// <summary><c>field:gt:value</c>: the field, a number, is greater than the value.</summary>
    /// <param name="field">The field.</param>
    /// <param name="value">The value.</param>
    public static TableauFilter Gt(TableauField field, long value) => Term(field, "gt", Number(value));

    /// <summary><c>field:gt:value</c>: the field, a date and time, is later than the value, written in UTC to the second.</summary>
    /// <param name="field">The field.</param>
    /// <param name="value">The value.</param>
    public static TableauFilter Gt(TableauField field, DateTimeOffset value) => Term(field, "gt", Date(value));

    /// <summary><c>field:gte:value</c>: the field, a number, is at least the value.</summary>
    /// <param name="field">The field.</param>
    /// <param name="value">The value.</param>
    public static TableauFilter Gte(TableauField field, long value) => Term(field, "gte", Number(value));

    /// <summary><c>field:gte:value</c>: the field, a date and time, is the value or later, written in UTC to the second.</summary>
    /// <param name="field">The field.</param>
    /// <param name="value">The value.</param>
    public static TableauFilter Gte(TableauField field, DateTimeOffset value) => Term(field, "gte", Date(value));

    /// <summary><c>field:lt:value</c>: the field, a number, is less than the value.</summary>
    /// <param name="field">The field.</param>
    /// <param name="value">The value.</param>
    public static TableauFilter Lt(TableauField field, long value) => Term(field, "lt", Number(value));

    /// <summary><c>field:lt:value</c>: the field, a date and time, is earlier than the value, written in UTC to the second.</summary>
    /// <param name="field">The field.</param>
    /// <param name="value">The value.</param>
    public static TableauFilter Lt(TableauField field, DateTimeOffset value) => Term(field, "lt", Date(value));

    /// <summary><c>field:lte:value</c>: the field, a number, is at most the value.</summary>
    /// <param name="field">The field.</param>
    /// <param name="value">The value.</param>
    public static TableauFilter Lte(TableauField field, long value) => Term(field, "lte", Number(value));

    /// <summary><c>field:lte:value</c>: the field, a date and time, is the value or earlier, written in UTC to the second.</summary>
    /// <param name="field">The field.</param>
    /// <param name="value">The value.</param>
    public static TableauFilter Lte(TableauField field, DateTimeOffset value) => Term(field, "lte", Date(value));

    /// <summary>
    /// Every filter given, joined with commas: Tableau keeps an item only when each holds. A field named twice counts
    /// once, by its last expression, as Tableau reads it.
    /// </summary>
    /// <param name="filters">The filters; one at least.</param>
    /// <exception cref="ArgumentException">There is no filter.</exception>
    public static TableauFilter And(params IEnumerable<TableauFilter> filters)
    {
        ArgumentNullException.ThrowIfNull(filters);
        var list = filters.ToList();
        if (list.Count == 0)
            throw new ArgumentException("And takes one filter at least.", nameof(filters));
        foreach (var filter in list)
            ArgumentNullException.ThrowIfNull(filter, nameof(filters));
        return new TableauFilter(string.Join(',', list.Select(filter => filter._expression)), [.. list.SelectMany(filter => filter._fields)]);
    }

    /// <summary>
    /// A filter written in Tableau's syntax, such as <c>name:like:Sales*</c>, for a field or an operator the builder lacks.
    /// Write its values as they are: the client escapes the parts between <c>:</c> and <c>,</c> when it sends it, and
    /// checks nothing else, the fields' resource included.
    /// </summary>
    /// <param name="expression">The expression, not URL-encoded.</param>
    /// <exception cref="ArgumentException">The expression is empty.</exception>
    public static TableauFilter FromString(string expression)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expression);
        return new TableauFilter(expression, []);
    }

    /// <summary>The expression in Tableau's syntax, values as written, before the client escapes them to send.</summary>
    public override string ToString() => _expression;

    /// <summary>Whether <paramref name="other"/> writes the same expression over the same fields.</summary>
    /// <param name="other">The other filter.</param>
    public bool Equals(TableauFilter? other) =>
        other is not null && string.Equals(_expression, other._expression, StringComparison.Ordinal) && _fields.SequenceEqual(other._fields);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => Equals(obj as TableauFilter);

    /// <inheritdoc/>
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(_expression);

    private static TableauFilter Term(TableauField field, string op, string value)
    {
        ArgumentNullException.ThrowIfNull(field);
        return new TableauFilter($"{field.Name}:{op}:{value}", [field]);
    }

    private static TableauFilter InList(TableauField field, List<string> values)
    {
        if (values.Count == 0)
            throw new ArgumentException("An in filter takes one value at least.", nameof(values));
        return Term(field, "in", "[" + string.Join(',', values) + "]");
    }

    private static string Value(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Contains(',', StringComparison.Ordinal))
        {
            throw new ArgumentException($"'{value}' holds a comma, which Tableau's filters can't hold: it splits a filter at each one, "
                + "escaped or not, and refuses the rest.", nameof(value));
        }
        return value;
    }

    // A generated enumeration's text, which a default value lacks; the roles hold no comma.
    private static string Value(SiteRole value) =>
        value.Value ?? throw new ArgumentException("A site role is needed, such as SiteRole.Viewer; a default one names none.", nameof(value));

    private static string Number(long value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Date(DateTimeOffset value) =>
        value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
}
