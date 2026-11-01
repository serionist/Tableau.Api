using System.Collections.Generic;

namespace Tableau.Api;

/// <summary>One page of a list, as a <c>ListPageAsync</c> method returns it.</summary>
/// <typeparam name="T">What the list holds.</typeparam>
/// <param name="Items">The page's items: empty, never null, for a page with none.</param>
/// <param name="PageNumber">The page's number, from 1, as Tableau answered it.</param>
/// <param name="PageSize">How many items a page holds, as Tableau answered it; the last page may hold fewer.</param>
/// <param name="TotalAvailable">How many items the whole list holds, or null when Tableau doesn't say.</param>
public sealed record Page<T>(IReadOnlyList<T> Items, int PageNumber, int PageSize, int? TotalAvailable);

/// <summary>
/// One page of a list paged by cursors, such as a Metadata API connection's, which
/// <see cref="MetadataClient.QueryPageAsync"/> returns: the next page starts after <see cref="EndCursor"/>.
/// </summary>
/// <typeparam name="T">What the list holds.</typeparam>
/// <param name="Items">The page's items: empty, never null, for a page with none.</param>
/// <param name="EndCursor">The cursor the next page starts after, or null when the server gave none.</param>
/// <param name="HasNextPage">Whether another page follows.</param>
public sealed record CursorPage<T>(IReadOnlyList<T> Items, string? EndCursor, bool HasNextPage);
