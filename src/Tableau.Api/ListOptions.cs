namespace Tableau.Api;

/// <summary>What <see cref="ProjectsClient.ListAsync"/> and <see cref="ProjectsClient.ListPageAsync"/> ask for.</summary>
public sealed record ProjectListOptions
{
    /// <summary>Which projects, over <see cref="ProjectField"/>'s fields; null for all the user can see.</summary>
    public TableauFilter? Filter { get; init; }

    /// <summary>
    /// Their order, over <see cref="ProjectField"/>'s fields; null for the oldest first, then by name, an order that
    /// doesn't change from page to page. A sort given is followed by those two fields where it doesn't name them, to
    /// break its ties.
    /// </summary>
    public TableauSort? Sort { get; init; }

    /// <summary>How many projects a page holds: 1 to 1,000, 100 by default.</summary>
    public int PageSize { get; init; } = 100;
}

/// <summary>What <see cref="UsersClient.ListAsync"/> and <see cref="UsersClient.ListPageAsync"/> ask for.</summary>
public sealed record UserListOptions
{
    /// <summary>Which users, over <see cref="UserField"/>'s fields; null for every user of the site.</summary>
    public TableauFilter? Filter { get; init; }

    /// <summary>
    /// Their order, over <see cref="UserField"/>'s fields; null for by name, then on Tableau Server by domain, an order
    /// that doesn't change from page to page. A sort given is followed by those fields where it doesn't name them, to
    /// break its ties.
    /// </summary>
    public TableauSort? Sort { get; init; }

    /// <summary>How many users a page holds: 1 to 1,000, 100 by default.</summary>
    public int PageSize { get; init; } = 100;
}

/// <summary>What <see cref="GroupsClient.ListAsync"/> and <see cref="GroupsClient.ListPageAsync"/> ask for.</summary>
public sealed record GroupListOptions
{
    /// <summary>Which groups, over <see cref="GroupField"/>'s fields; null for every group of the site.</summary>
    public TableauFilter? Filter { get; init; }

    /// <summary>
    /// Their order, over <see cref="GroupField"/>'s fields; null for by name, an order that doesn't change from page to
    /// page. A sort given is followed by the name where it doesn't name it, to break its ties.
    /// </summary>
    public TableauSort? Sort { get; init; }

    /// <summary>How many groups a page holds: 1 to 1,000, 100 by default.</summary>
    public int PageSize { get; init; } = 100;
}

/// <summary>What <see cref="MetadataClient.ListDatabasesAsync"/> and <see cref="MetadataClient.ListDatabasesPageAsync"/> ask for.</summary>
/// <remarks>A page size alone: Tableau's reference lists no filter or sort for Query Databases.</remarks>
public sealed record DatabaseListOptions
{
    /// <summary>How many databases a page holds: 1 to 1,000, 100 by default.</summary>
    public int PageSize { get; init; } = 100;
}

/// <summary>What <see cref="MetadataClient.ListTablesAsync"/> and <see cref="MetadataClient.ListTablesPageAsync"/> ask for.</summary>
/// <remarks>A page size alone: Tableau's reference lists no filter or sort for Query Tables.</remarks>
public sealed record TableListOptions
{
    /// <summary>How many tables a page holds: 1 to 1,000, 100 by default.</summary>
    public int PageSize { get; init; } = 100;
}

/// <summary>What <see cref="SitesClient.ListAsync"/> and <see cref="SitesClient.ListPageAsync"/> ask for.</summary>
/// <remarks>
/// No filter or sort: Tableau Server 2025.1 refuses the one filter Tableau's reference lists for Query Sites, on the
/// site's id, as it does any other, and leaves the sites in its own order whatever the sort.
/// </remarks>
public sealed record SiteListOptions
{
    /// <summary>How many sites a page holds: 1 to 1,000, 100 by default.</summary>
    public int PageSize { get; init; } = 100;
}
