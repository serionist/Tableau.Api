using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Tableau.Api.Pipeline;
using Tableau.Api.Rest.Models;

namespace Tableau.Api;

/// <summary>The site's groups and their users: <see cref="TableauClient.Groups"/>.</summary>
/// <remarks>
/// Tableau answers these only to a server or site administrator. A group from Active Directory is Tableau Server's alone:
/// importing and synchronising one are refused on Tableau Cloud before anything is sent.
/// </remarks>
public class GroupsClient
{
    private const string Resource = "groups";

    // Made once and never changed: Tableau keeps a group's name the site's own, case aside, so pages don't shift between
    // requests.
    private static readonly TableauSort _stable = TableauSort.Asc(GroupField.Name);

    // The query that runs an import or a synchronisation as a job.
    private static readonly IReadOnlyList<KeyValuePair<string, string?>> _asJob = [new("asJob", "true")];

    private readonly TableauPipeline? _pipeline;

    /// <summary>A sub-client with no connection, for a mock to derive from: its methods throw until overridden.</summary>
    protected GroupsClient()
    {
    }

    internal GroupsClient(TableauPipeline pipeline)
    {
        _pipeline = pipeline;
    }

    private TableauPipeline Pipeline => _pipeline ?? throw TableauClient.ForMocksOnly();

    /// <summary>
    /// Query Groups, every page: the site's groups, read a page at a time as you go, until the list ends. It reads its
    /// pages through <see cref="ListPageAsync"/>, so a mock that overrides that serves this too.
    /// </summary>
    /// <remarks>
    /// Without a sort they come by name, which no two groups of a site share, so the pages of a list that doesn't change
    /// neither skip nor repeat a group; one made, deleted or renamed while you read can still be missed or met twice.
    /// </remarks>
    /// <param name="options">The filter, the sort and the page size; null for every group, 100 a page.</param>
    /// <param name="cancellationToken">Stops the list, between groups and between pages.</param>
    /// <returns>The groups, as you read them.</returns>
    /// <exception cref="ArgumentException">The filter or the sort names another resource's field, or the page size is outside 1 to 1,000.</exception>
    /// <exception cref="TableauException">A page's call failed; <see cref="TableauException.Kind"/> says how.</exception>
    public virtual async IAsyncEnumerable<Group> ListAsync(GroupListOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var group in TableauPaging.ListAsync((number, token) => ListPageAsync(number, options, token), cancellationToken)
                           .ConfigureAwait(false))
            yield return group;
    }

    /// <summary>Query Groups, one page.</summary>
    /// <param name="pageNumber">The page's number, from 1.</param>
    /// <param name="options">The filter, the sort and the page size; null for every group, 100 a page.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The page, its groups empty, never null, when it has none.</returns>
    /// <exception cref="ArgumentException">
    /// The page number is below 1, the page size outside 1 to 1,000, or the filter or the sort names another resource's
    /// field.
    /// </exception>
    /// <exception cref="TableauException">
    /// The call failed; <see cref="TableauException.Kind"/> says how. Tableau answers a page past the end with an error.
    /// </exception>
    public virtual Task<Page<Group>> ListPageAsync(int pageNumber, GroupListOptions? options = null, CancellationToken cancellationToken = default)
    {
        options ??= new GroupListOptions();
        return TableauPaging.PageAsync(Pipeline, Endpoints.QueryGroups, null, pageNumber, options.PageSize, options.Filter, options.Sort,
            _stable, Resource, static body => body.Groups, cancellationToken);
    }

    /// <summary>
    /// Create Group: a local group, which Tableau holds itself. Sent once, never again after a failure, since a second
    /// would fail on the name.
    /// </summary>
    /// <param name="name">The group's name, the site's own: Tableau compares it case aside.</param>
    /// <param name="minimumSiteRole">
    /// The site role a member is granted when they sign in, or null for none; it can raise a member's role, never lower it.
    /// </param>
    /// <param name="externalUserEnabled">Whether the group has on-demand access, or null to leave it; Tableau Cloud only, from REST API 3.21.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The group as Tableau made it, with its id.</returns>
    /// <exception cref="ArgumentException"><paramref name="name"/> is empty.</exception>
    /// <exception cref="TableauException">The call failed; <see cref="TableauException.Kind"/> says how.</exception>
    public virtual Task<Group> CreateAsync(string name, SiteRole? minimumSiteRole = null, bool? externalUserEnabled = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        return Pipeline.SendAsync(Endpoints.CreateGroup, new TableauRequest(Body: new TsRequest
        {
            Group = new Group { Name = name, MinimumSiteRole = minimumSiteRole, ExternalUserEnabled = externalUserEnabled },
        }), static body => body.Group, "group", cancellationToken);
    }

    /// <summary>
    /// Create Group from Active Directory: a group made from a directory group, with its users, before this returns;
    /// Tableau Server only. A large directory group may take longer than the call may run: import it with
    /// <see cref="ImportFromActiveDirectoryAsJobAsync"/>. Sent once, never again after a failure.
    /// </summary>
    /// <param name="name">The directory group's name.</param>
    /// <param name="domainName">The directory group's domain.</param>
    /// <param name="siteRole">The site role its users are given when imported or, by <paramref name="grantLicenseMode"/>, granted.</param>
    /// <param name="grantLicenseMode">When users are granted <paramref name="siteRole"/>, or null for Tableau's default.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The group as Tableau made it, with its id.</returns>
    /// <exception cref="ArgumentException"><paramref name="name"/> or <paramref name="domainName"/> is empty.</exception>
    /// <exception cref="TableauException">
    /// The call failed; <see cref="TableauException.Kind"/> says how, <see cref="TableauErrorKind.UnsupportedDeployment"/>
    /// on Tableau Cloud.
    /// </exception>
    public virtual Task<Group> ImportFromActiveDirectoryAsync(string name, string domainName, SiteRole siteRole,
        ImportDirectiveGrantLicenseMode? grantLicenseMode = null, CancellationToken cancellationToken = default) =>
        Pipeline.SendAsync(Endpoints.ImportGroup, new TableauRequest(Body: Directory(name, domainName, siteRole, grantLicenseMode)),
            static body => body.Group, "group", cancellationToken);

    /// <summary>
    /// Create Group from Active Directory, as a job: Tableau answers at once with the job that imports the group, which
    /// Query Job follows; Tableau Server only. Sent once, never again after a failure, since a second would start another
    /// job.
    /// </summary>
    /// <param name="name">The directory group's name.</param>
    /// <param name="domainName">The directory group's domain.</param>
    /// <param name="siteRole">The site role its users are given when imported or, by <paramref name="grantLicenseMode"/>, granted.</param>
    /// <param name="grantLicenseMode">When users are granted <paramref name="siteRole"/>, or null for Tableau's default.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The job, its <see cref="Job.Type"/> <c>GroupImport</c>.</returns>
    /// <exception cref="ArgumentException"><paramref name="name"/> or <paramref name="domainName"/> is empty.</exception>
    /// <exception cref="TableauException">
    /// The call failed; <see cref="TableauException.Kind"/> says how, <see cref="TableauErrorKind.UnsupportedDeployment"/>
    /// on Tableau Cloud.
    /// </exception>
    public virtual Task<Job> ImportFromActiveDirectoryAsJobAsync(string name, string domainName, SiteRole siteRole,
        ImportDirectiveGrantLicenseMode? grantLicenseMode = null, CancellationToken cancellationToken = default) =>
        Pipeline.SendAsync(Endpoints.ImportGroup, new TableauRequest(Query: _asJob, Body: Directory(name, domainName, siteRole, grantLicenseMode)),
            static body => body.Job, "job", cancellationToken);

    /// <summary>
    /// Update Group: a local group's name, the site role it grants or its on-demand access; a member left null stays as
    /// it is.
    /// </summary>
    /// <param name="id">The group's LUID.</param>
    /// <param name="name">The group's new name, or null to keep it.</param>
    /// <param name="minimumSiteRole">
    /// The site role a member is granted when they sign in, or null to keep it; <see cref="SiteRole.Unlicensed"/> grants
    /// none.
    /// </param>
    /// <param name="externalUserEnabled">Whether the group has on-demand access, or null to keep it; Tableau Cloud only, from REST API 3.21.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The group as Tableau holds it now.</returns>
    /// <exception cref="ArgumentException"><paramref name="id"/> is no LUID.</exception>
    /// <exception cref="TableauException">The call failed; <see cref="TableauException.Kind"/> says how.</exception>
    public virtual Task<Group> UpdateAsync(string id, string? name = null, SiteRole? minimumSiteRole = null, bool? externalUserEnabled = null,
        CancellationToken cancellationToken = default) =>
        Pipeline.SendAsync(Endpoints.UpdateGroup, new TableauRequest([RequestUri.Luid(id, nameof(id))], Body: new TsRequest
        {
            Group = new Group { Name = name, MinimumSiteRole = minimumSiteRole, ExternalUserEnabled = externalUserEnabled },
        }), static body => body.Group, "group", cancellationToken);

    /// <summary>
    /// Update Group from Active Directory: the group's users made the directory group's, before this returns; Tableau
    /// Server only. A large directory group may take longer than the call may run: synchronise it with
    /// <see cref="UpdateFromActiveDirectoryAsJobAsync"/>. Not sent again after a timeout, which would run a second
    /// synchronisation beside the first.
    /// </summary>
    /// <param name="id">The group's LUID.</param>
    /// <param name="name">The directory group's name, which must be the group's own.</param>
    /// <param name="domainName">The directory group's domain.</param>
    /// <param name="siteRole">The site role its users are given when imported or, by <paramref name="grantLicenseMode"/>, granted.</param>
    /// <param name="grantLicenseMode">When users are granted <paramref name="siteRole"/>, or null to keep the group's.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The group as Tableau holds it now.</returns>
    /// <exception cref="ArgumentException"><paramref name="id"/> is no LUID, or <paramref name="name"/> or <paramref name="domainName"/> is empty.</exception>
    /// <exception cref="TableauException">
    /// The call failed; <see cref="TableauException.Kind"/> says how, <see cref="TableauErrorKind.UnsupportedDeployment"/>
    /// on Tableau Cloud.
    /// </exception>
    public virtual Task<Group> UpdateFromActiveDirectoryAsync(string id, string name, string domainName, SiteRole siteRole,
        ImportDirectiveGrantLicenseMode? grantLicenseMode = null, CancellationToken cancellationToken = default)
    {
        RequestUri.Luid(id, nameof(id));
        return Pipeline.SendAsync(Endpoints.SynchronizeGroup, new TableauRequest([id], Body: Directory(name, domainName, siteRole, grantLicenseMode)),
            static body => body.Group, "group", cancellationToken);
    }

    /// <summary>
    /// Update Group from Active Directory, as a job: Tableau answers at once with the job that synchronises the group,
    /// which Query Job follows; Tableau Server only. Sent once, never again after a failure, since a second would start
    /// another job.
    /// </summary>
    /// <param name="id">The group's LUID.</param>
    /// <param name="name">The directory group's name, which must be the group's own.</param>
    /// <param name="domainName">The directory group's domain.</param>
    /// <param name="siteRole">The site role its users are given when imported or, by <paramref name="grantLicenseMode"/>, granted.</param>
    /// <param name="grantLicenseMode">When users are granted <paramref name="siteRole"/>, or null to keep the group's.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The job, its <see cref="Job.Type"/> <c>GroupSync</c>.</returns>
    /// <exception cref="ArgumentException"><paramref name="id"/> is no LUID, or <paramref name="name"/> or <paramref name="domainName"/> is empty.</exception>
    /// <exception cref="TableauException">
    /// The call failed; <see cref="TableauException.Kind"/> says how, <see cref="TableauErrorKind.UnsupportedDeployment"/>
    /// on Tableau Cloud.
    /// </exception>
    public virtual Task<Job> UpdateFromActiveDirectoryAsJobAsync(string id, string name, string domainName, SiteRole siteRole,
        ImportDirectiveGrantLicenseMode? grantLicenseMode = null, CancellationToken cancellationToken = default)
    {
        RequestUri.Luid(id, nameof(id));
        return Pipeline.SendAsync(Endpoints.SynchronizeGroup,
            new TableauRequest([id], _asJob, Directory(name, domainName, siteRole, grantLicenseMode)), static body => body.Job, "job", cancellationToken);
    }

    /// <summary>Delete Group: the group goes, its users stay, and what it was granted no longer applies. All Users can't be deleted.</summary>
    /// <param name="id">The group's LUID.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <exception cref="ArgumentException"><paramref name="id"/> is no LUID.</exception>
    /// <exception cref="TableauException">The call failed; <see cref="TableauException.Kind"/> says how.</exception>
    public virtual Task DeleteAsync(string id, CancellationToken cancellationToken = default) =>
        Pipeline.SendAsync(Endpoints.DeleteGroup, new TableauRequest([RequestUri.Luid(id, nameof(id))]), cancellationToken);

    /// <summary>
    /// Add User to Group. Sent again after a failure that may not have reached Tableau; when the first had, the second
    /// fails with a 409, the user already a member.
    /// </summary>
    /// <param name="groupId">The group's LUID.</param>
    /// <param name="userId">The user's LUID.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The user, as Tableau answers.</returns>
    /// <exception cref="ArgumentException"><paramref name="groupId"/> or <paramref name="userId"/> is no LUID.</exception>
    /// <exception cref="TableauException">The call failed; <see cref="TableauException.Kind"/> says how.</exception>
    public virtual Task<User> AddUserAsync(string groupId, string userId, CancellationToken cancellationToken = default)
    {
        RequestUri.Luid(groupId, nameof(groupId));
        RequestUri.Luid(userId, nameof(userId));
        return Pipeline.SendAsync(Endpoints.AddUserToGroup, new TableauRequest([groupId], Body: new TsRequest { User = new User { Id = userId } }),
            static body => body.User, "user", cancellationToken);
    }

    /// <summary>Remove User from Group: the user leaves the group, and stays on the site.</summary>
    /// <param name="groupId">The group's LUID.</param>
    /// <param name="userId">The user's LUID.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <exception cref="ArgumentException"><paramref name="groupId"/> or <paramref name="userId"/> is no LUID.</exception>
    /// <exception cref="TableauException">The call failed; <see cref="TableauException.Kind"/> says how.</exception>
    public virtual Task RemoveUserAsync(string groupId, string userId, CancellationToken cancellationToken = default) =>
        Pipeline.SendAsync(Endpoints.RemoveUserFromGroup,
            new TableauRequest([RequestUri.Luid(groupId, nameof(groupId)), RequestUri.Luid(userId, nameof(userId))]), cancellationToken);

    /// <summary>Get Users in Group, every page: the group's users, read a page at a time as you go.</summary>
    /// <param name="groupId">The group's LUID.</param>
    /// <param name="cancellationToken">Stops the list, between users and between pages.</param>
    /// <returns>The users, as you read them, in Tableau's order.</returns>
    /// <exception cref="ArgumentException"><paramref name="groupId"/> is no LUID, which the first read throws.</exception>
    /// <exception cref="TableauException">A page's call failed; <see cref="TableauException.Kind"/> says how.</exception>
    public virtual async IAsyncEnumerable<User> ListUsersAsync(string groupId, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        RequestUri.Luid(groupId, nameof(groupId));
        await foreach (var user in TableauPaging.ListAsync((number, token) => TableauPaging.PageAsync(Pipeline, Endpoints.GetUsersInGroup,
                               [groupId], number, TableauPaging.DefaultPageSize, null, null, null, "users", static body => body.Users, token),
                           cancellationToken).ConfigureAwait(false))
            yield return user;
    }


    private static TsRequest Directory(string name, string domainName, SiteRole siteRole, ImportDirectiveGrantLicenseMode? grantLicenseMode)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentException.ThrowIfNullOrEmpty(domainName);
        UsersClient.Role(siteRole, nameof(siteRole));
        return new TsRequest
        {
            Group = new Group
            {
                Name = name,
                Import = new ImportDirective
                {
                    Source = ImportSource.ActiveDirectory,
                    DomainName = domainName,
                    SiteRole = siteRole,
                    GrantLicenseMode = grantLicenseMode,
                },
            },
        };
    }
}
