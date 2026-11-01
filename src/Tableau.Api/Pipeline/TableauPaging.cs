using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Tableau.Api.Rest.Models;

namespace Tableau.Api.Pipeline;

/// <summary>
/// The paging every list shares: one page's request, with its size, number, filter and sort, read into a
/// <see cref="Page{T}"/>; and the pages one after another, lazily, until the list ends.
/// </summary>
internal static class TableauPaging
{
    /// <summary>The page size a list asks for unless told otherwise: Tableau's own default.</summary>
    public const int DefaultPageSize = 100;

    /// <summary>The largest page Tableau's reference allows, which it refuses above with 403014.</summary>
    public const int MostPageSize = 1000;

    /// <summary>
    /// One page: its number and size checked, the filter's and the sort's fields checked as <paramref name="resource"/>'s,
    /// all before anything is sent; the list never null, empty when Tableau's answer holds none.
    /// </summary>
    /// <param name="pipeline">The pipeline to send through.</param>
    /// <param name="endpoint">The list's descriptor.</param>
    /// <param name="pathValues">The values of its template's parameters, or null for none.</param>
    /// <param name="pageNumber">The page's number, from 1.</param>
    /// <param name="pageSize">The page's size, 1 to 1,000.</param>
    /// <param name="filter">The filter, or null.</param>
    /// <param name="sort">The caller's sort, then <paramref name="stableSort"/>'s fields it doesn't name; or null for <paramref name="stableSort"/>.</param>
    /// <param name="stableSort">The sort that keeps a list's pages apart, or null for Tableau's own order.</param>
    /// <param name="resource">What the list holds, which its fields must belong to: <c>projects</c>.</param>
    /// <param name="select">The list in Tableau's answer.</param>
    /// <param name="cancellationToken">The caller's token.</param>
    public static Task<Page<T>> PageAsync<T>(TableauPipeline pipeline, Endpoint endpoint, IReadOnlyList<string>? pathValues, int pageNumber,
        int pageSize, TableauFilter? filter, TableauSort? sort, TableauSort? stableSort, string resource,
        Func<TsResponse, IReadOnlyList<T>?> select, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(pageNumber, 1);
        Check(pageSize, filter, sort, resource, OptionsParameter);
        var order = sort is null ? stableSort : stableSort is null ? sort : sort.ThenStable(stableSort);
        return PageAsync(pipeline, endpoint, new TableauRequest(pathValues, [
            new("pageSize", pageSize.ToString(CultureInfo.InvariantCulture)),
            new("pageNumber", pageNumber.ToString(CultureInfo.InvariantCulture)),
            new("filter", filter?.ToString()),
            new("sort", order?.ToString()),
        ]), pageNumber, pageSize, select, cancellationToken);
    }

    /// <summary>
    /// Every item of a list, a page at a time as the caller reads on: it stops after the page that reaches Tableau's
    /// <c>totalAvailable</c>, or after a page shorter than its size, so it never asks for a page past the end, which
    /// Tableau answers with an error for some lists. A server that answers another page than the one asked for, which
    /// would serve the first page again and again, fails the list instead.
    /// </summary>
    /// <param name="page">A page by its number.</param>
    /// <param name="call">The list's call, which the failure names: <c>Get Favorites for User</c>.</param>
    /// <param name="identity">
    /// For a list whose answer has no pagination: an item as text, so a page whose first item is the page before's shows
    /// the server ignored the page's number; null for a list that has it.
    /// </param>
    /// <param name="cancellationToken">Stops the list between items and between pages.</param>
    public static async IAsyncEnumerable<T> ListAsync<T>(Func<int, CancellationToken, Task<Page<T>>> page, string call,
        Func<T, string>? identity, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        long seen = 0;
        string? firstBefore = null;
        for (var number = 1; ; number++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = await page(number, cancellationToken).ConfigureAwait(false);
            var first = identity is not null && current.TotalAvailable is null && current.Items.Count > 0 ? identity(current.Items[0]) : null;
            if (current.PageNumber < number || (first is not null && string.Equals(first, firstBefore, StringComparison.Ordinal)))
                throw Repeated(call, number, current.PageNumber < number ? current.PageNumber : number - 1);
            firstBefore = first;
            foreach (var item in current.Items)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return item;
            }
            seen += current.Items.Count;
            if (current.Items.Count == 0 || current.Items.Count < current.PageSize || seen >= current.TotalAvailable)
                yield break;
        }
    }

    /// <summary>
    /// Every item of a list paged by cursors, a page at a time as the caller reads on: each page after the cursor the one
    /// before ended with, until a page says it is the last. A page that says more follow and gives no cursor, or a
    /// cursor already met, fails the list, where reading on would repeat pages without end.
    /// </summary>
    /// <param name="page">A page by the cursor it starts after, null for the first.</param>
    /// <param name="call">The list's call, which the failure names.</param>
    /// <param name="cancellationToken">Stops the list between items and between pages.</param>
    public static async IAsyncEnumerable<T> CursorListAsync<T>(Func<string?, CancellationToken, Task<CursorPage<T>>> page, string call,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var met = new HashSet<string>(StringComparer.Ordinal);
        string? cursor = null;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = await page(cursor, cancellationToken).ConfigureAwait(false);
            foreach (var item in current.Items)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return item;
            }
            if (!current.HasNextPage) yield break;
            if (current.EndCursor is not { } next || !met.Add(next))
            {
                throw new TableauException(TableauErrorKind.InvalidResponse,
                    $"{call} answered that more pages follow, {(current.EndCursor is null ? "with no cursor" : "with a cursor it gave before")}, "
                    + "so the list can't go on; it stops here rather than read the same page again.");
            }
            cursor = next;
        }
    }

    // A list whose server answered an earlier page than the one asked for: reading on would repeat it without end.
    private static TableauException Repeated(string call, int asked, int answered) =>
        new(TableauErrorKind.InvalidResponse,
            string.Create(CultureInfo.InvariantCulture, $"{call} answered page {answered} when asked for page {asked}, so the server doesn't page this list")
            + "; it stops here rather than read the same page again.")
        { Method = System.Net.Http.HttpMethod.Get };

    /// <summary>The name a list method gives its options, which an exception for one of them names.</summary>
    private const string OptionsParameter = "options";

    private static void Check(int pageSize, TableauFilter? filter, TableauSort? sort, string resource, string parameter)
    {
        if (pageSize is < 1 or > MostPageSize)
            throw new ArgumentOutOfRangeException(parameter, pageSize, $"A page holds 1 to {MostPageSize} items; Tableau refuses more.");
        foreach (var field in filter?.Fields ?? [])
            TableauField.Of(field, resource, parameter);
        foreach (var field in sort?.Fields ?? [])
            TableauField.Of(field, resource, parameter);
    }

    private static async Task<Page<T>> PageAsync<T>(TableauPipeline pipeline, Endpoint endpoint, TableauRequest request, int pageNumber,
        int pageSize, Func<TsResponse, IReadOnlyList<T>?> select, CancellationToken cancellationToken)
    {
        var body = await pipeline.SendAsync(endpoint, request, static body => body, "a body", cancellationToken).ConfigureAwait(false);
        var pagination = body.Pagination;
        return new Page<T>(select(body) ?? [], Count(pagination?.PageNumber) ?? pageNumber, Count(pagination?.PageSize) ?? pageSize,
            Count(pagination?.TotalAvailable));
    }

    // Tableau's numbers are xs:integer; a count past int's range is past any list's.
    private static int? Count(long? value) => value is { } number ? (int)Math.Clamp(number, 0, int.MaxValue) : null;
}
