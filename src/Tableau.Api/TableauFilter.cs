using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Tableau.Api;

/// <summary>
/// A filter expression for a list, built over a resource's fields, such as
/// <c>TableauFilter.And(TableauFilter.Eq(UserField.SiteRole, "Viewer"), TableauFilter.Gte(UserField.LastLogin, since))</c>,
/// which Tableau's syntax writes <c>siteRole:eq:Viewer,lastLogin:gte:2026-01-01T00:00:00Z</c>.
/// </summary>
/// <remarks>
/// <para>
/// A value is written as it is, not URL-encoded: the client escapes each part between the separators, <c>:</c> and
/// <c>,</c>, when it sends the filter, so <c>R&amp;D</c>, <c>C++</c>, <c>#</c> and <c>%</c> reach Tableau as written. A
/// value can't hold a comma: Tableau splits a filter at each one, escaped or not, and refuses the rest.
/// </para>
/// <para>
/// Tableau's reference lists which operators each field takes, as each field class says; the server refuses another.
/// Values are compared as Tableau stores them, case included, but by <see cref="CiEq"/>. A list refuses a field of another
/// resource before it sends anything. The filter is immutable, and safe to share.
/// </para>
/// </remarks>
public sealed class TableauFilter
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
        var list = values.Select(Value).ToList();
        if (list.Count == 0)
            throw new ArgumentException("An in filter takes one value at least.", nameof(values));
        return Term(field, "in", "[" + string.Join(',', list) + "]");
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

    private static TableauFilter Term(TableauField field, string op, string value)
    {
        ArgumentNullException.ThrowIfNull(field);
        return new TableauFilter($"{field.Name}:{op}:{value}", [field]);
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

    private static string Number(long value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Date(DateTimeOffset value) =>
        value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
}
