using System.Collections.Generic;

namespace Tableau.Api;

/// <summary>One page of a list, as a <c>ListPageAsync</c> method returns it.</summary>
/// <typeparam name="T">What the list holds.</typeparam>
/// <param name="Items">The page's items: empty, never null, for a page with none.</param>
/// <param name="PageNumber">The page's number, from 1, as Tableau answered it.</param>
/// <param name="PageSize">How many items a page holds, as Tableau answered it; the last page may hold fewer.</param>
/// <param name="TotalAvailable">How many items the whole list holds, or null when Tableau doesn't say.</param>
public sealed record Page<T>(IReadOnlyList<T> Items, int PageNumber, int PageSize, int? TotalAvailable);
