using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Tableau.Api.Pipeline;
using Tableau.Api.Rest.Models;

namespace Tableau.Api;

/// <summary>The site's users: <see cref="TableauClient.Users"/>.</summary>
/// <remarks>
/// Tableau answers these, but Query User On Site for the user's own record, only to a server or site administrator.
/// </remarks>
public class UsersClient
{
    private const string Resource = "users";

    // Made once and never changed: a user's name is the site's own within a domain, so pages don't shift between requests.
    private static readonly TableauSort _stable = TableauSort.Asc(UserField.Name).Then(TableauSort.Asc(UserField.DomainName));

    private readonly TableauPipeline? _pipeline;

    /// <summary>A sub-client with no connection, for a mock to derive from: its methods throw until overridden.</summary>
    protected UsersClient()
    {
    }

    internal UsersClient(TableauPipeline pipeline)
    {
        _pipeline = pipeline;
    }

    private TableauPipeline Pipeline => _pipeline ?? throw TableauClient.ForMocksOnly();

    /// <summary>Query User On Site: one user, by their LUID, such as <see cref="TableauSession.UserId"/>.</summary>
    /// <param name="id">The user's LUID.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The user.</returns>
    /// <exception cref="ArgumentException"><paramref name="id"/> is no LUID.</exception>
    /// <exception cref="TableauException">The call failed; <see cref="TableauException.Kind"/> says how.</exception>
    public virtual Task<User> GetAsync(string id, CancellationToken cancellationToken = default) =>
        Pipeline.SendAsync(Endpoints.QueryUserOnSite, new TableauRequest([RequestUri.Luid(id, nameof(id))]), static body => body.User, "user",
            cancellationToken);

    /// <summary>
    /// Get Users on Site, every page: the site's users, read a page at a time as you go, until the list ends. It reads its
    /// pages through <see cref="ListPageAsync"/>, so a mock that overrides that serves this too.
    /// </summary>
    /// <remarks>
    /// Without a sort they come by name, then by domain, so the pages of a list that doesn't change neither skip nor repeat
    /// a user; one added, removed or renamed while you read can still be missed or met twice.
    /// </remarks>
    /// <param name="options">The filter, the sort and the page size; null for every user, 100 a page.</param>
    /// <param name="cancellationToken">Stops the list, between users and between pages.</param>
    /// <returns>The users, as you read them.</returns>
    /// <exception cref="ArgumentException">The filter or the sort names another resource's field, or the page size is outside 1 to 1,000.</exception>
    /// <exception cref="TableauException">A page's call failed; <see cref="TableauException.Kind"/> says how.</exception>
    public virtual async IAsyncEnumerable<User> ListAsync(UserListOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var user in TableauPaging.ListAsync((number, token) => ListPageAsync(number, options, token), cancellationToken)
                           .ConfigureAwait(false))
            yield return user;
    }

    /// <summary>Get Users on Site, one page.</summary>
    /// <param name="pageNumber">The page's number, from 1.</param>
    /// <param name="options">The filter, the sort and the page size; null for every user, 100 a page.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The page, its users empty, never null, when it has none.</returns>
    /// <exception cref="ArgumentException">
    /// The page number is below 1, the page size outside 1 to 1,000, or the filter or the sort names another resource's
    /// field.
    /// </exception>
    /// <exception cref="TableauException">The call failed; <see cref="TableauException.Kind"/> says how.</exception>
    public virtual Task<Page<User>> ListPageAsync(int pageNumber, UserListOptions? options = null, CancellationToken cancellationToken = default)
    {
        options ??= new UserListOptions();
        return TableauPaging.PageAsync(Pipeline, Endpoints.GetUsersOnSite, null, pageNumber, options.PageSize, options.Filter, options.Sort,
            _stable, Resource, static body => body.Users, cancellationToken);
    }

    /// <summary>
    /// Add User to Site: a new user in the site, or on a Tableau Server with Active Directory, a directory user imported
    /// into it. Sent once, never again after a failure, since a second would fail on the name.
    /// </summary>
    /// <remarks>
    /// On Tableau Cloud the name is the email address the user signs in with, and Tableau sends them an invitation. A
    /// site with no licence left for <paramref name="siteRole"/> adds the user as <c>Unlicensed</c>, which the answer
    /// shows.
    /// </remarks>
    /// <param name="name">The user's name.</param>
    /// <param name="siteRole">The user's site role; not <see cref="SiteRole.ServerAdministrator"/>, which the REST API can't give.</param>
    /// <param name="options">How the user signs in, and the details only Tableau Cloud takes; null for the site's defaults.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The user as Tableau added them, with their id.</returns>
    /// <exception cref="ArgumentException"><paramref name="name"/> is empty.</exception>
    /// <exception cref="TableauException">The call failed; <see cref="TableauException.Kind"/> says how.</exception>
    public virtual Task<User> AddAsync(string name, SiteRole siteRole, UserAddOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        Role(siteRole, nameof(siteRole));
        var user = new User
        {
            Name = name,
            SiteRole = siteRole,
            AuthSetting = options?.AuthSetting,
            IdentityPoolName = options?.IdentityPoolName,
            IdpConfigurationId = options?.IdpConfigurationId,
            Email = options?.Email,
            Language = options?.Language,
            Locale = options?.Locale,
        };
        return Pipeline.SendAsync(Endpoints.AddUserToSite, new TableauRequest(Body: new TsRequest { User = user }), static body => body.User,
            "user", cancellationToken);
    }

    /// <summary>
    /// Update User: changes the members of <paramref name="changes"/> that are set, and leaves the others as they are.
    /// Tableau's reference lists <see cref="User.FullName"/>, <see cref="User.Email"/>, <see cref="User.Password"/>,
    /// <see cref="User.SiteRole"/>, <see cref="User.AuthSetting"/>, <see cref="User.IdentityPoolName"/>,
    /// <see cref="User.IdpConfigurationId"/>, <see cref="User.Language"/> and <see cref="User.Locale"/>.
    /// </summary>
    /// <remarks>
    /// The full name and the password are Tableau Server's; the identity pool is Tableau Server's, and the identity
    /// provider's configuration, the language and the locale Tableau Cloud's. A user Active Directory holds gets their
    /// full name and email back from it at the next synchronisation.
    /// </remarks>
    /// <param name="id">The user's LUID.</param>
    /// <param name="changes">What to change: set only those members, such as <c>new User { SiteRole = SiteRole.Viewer }</c>.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The user as Tableau holds them now.</returns>
    /// <exception cref="ArgumentException"><paramref name="id"/> is no LUID.</exception>
    /// <exception cref="TableauException">The call failed; <see cref="TableauException.Kind"/> says how.</exception>
    public virtual Task<User> UpdateAsync(string id, User changes, CancellationToken cancellationToken = default)
    {
        RequestUri.Luid(id, nameof(id));
        ArgumentNullException.ThrowIfNull(changes);
        return Pipeline.SendAsync(Endpoints.UpdateUser, new TableauRequest([id], Body: new TsRequest { User = changes }),
            static body => body.User, "user", cancellationToken);
    }

    /// <summary>
    /// Remove User from Site: the user leaves the site, and is deleted once in no site. A user who owns content can be
    /// removed only when <paramref name="mapAssetsToUserId"/> gives it to another, which only a server administrator can do.
    /// </summary>
    /// <param name="id">The user's LUID.</param>
    /// <param name="mapAssetsToUserId">The LUID of the user who takes over what the user owns, or null for none.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <exception cref="ArgumentException"><paramref name="id"/> or <paramref name="mapAssetsToUserId"/> is no LUID.</exception>
    /// <exception cref="TableauException">The call failed; <see cref="TableauException.Kind"/> says how.</exception>
    public virtual Task RemoveAsync(string id, string? mapAssetsToUserId = null, CancellationToken cancellationToken = default)
    {
        RequestUri.Luid(id, nameof(id));
        if (mapAssetsToUserId is not null)
            RequestUri.Luid(mapAssetsToUserId, nameof(mapAssetsToUserId));
        return Pipeline.SendAsync(Endpoints.RemoveUserFromSite, new TableauRequest([id], [new("mapAssetsTo", mapAssetsToUserId)]),
            cancellationToken);
    }

    /// <summary>
    /// Refuses a default site role, <c>default(SiteRole)</c>, which holds no text to send, where a call needs one; returns
    /// the role.
    /// </summary>
    internal static SiteRole Role(SiteRole siteRole, string parameter) =>
        siteRole.Value is null
            ? throw new ArgumentException("A site role is needed, such as SiteRole.Viewer; a default one names none.", parameter)
            : siteRole;

    /// <summary>Get Groups for a User, every page: the groups the user is in, read a page at a time as you go.</summary>
    /// <param name="userId">The user's LUID.</param>
    /// <param name="cancellationToken">Stops the list, between groups and between pages.</param>
    /// <returns>The groups, as you read them, in Tableau's order.</returns>
    /// <exception cref="ArgumentException"><paramref name="userId"/> is no LUID, which the first read throws.</exception>
    /// <exception cref="TableauException">A page's call failed; <see cref="TableauException.Kind"/> says how.</exception>
    public virtual async IAsyncEnumerable<Group> ListGroupsAsync(string userId, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        RequestUri.Luid(userId, nameof(userId));
        await foreach (var group in TableauPaging.ListAsync((number, token) => TableauPaging.PageAsync(Pipeline, Endpoints.GetGroupsForUser,
                               [userId], number, TableauPaging.DefaultPageSize, null, null, null, "groups", static body => body.Groups, token),
                           cancellationToken).ConfigureAwait(false))
            yield return group;
    }
}

/// <summary>How a user added to a site signs in, and the details only Tableau Cloud takes: <see cref="UsersClient.AddAsync"/>.</summary>
public sealed record UserAddOptions
{
    /// <summary>
    /// How the user signs in, such as <see cref="SiteUserAuthSetting.Saml"/>; null for the site's default. On Tableau
    /// Cloud, give this or <see cref="IdpConfigurationId"/>, not both.
    /// </summary>
    public SiteUserAuthSetting? AuthSetting { get; init; }

    /// <summary>The identity pool the user joins, where pools are set up; Tableau Server only, from REST API 3.19.</summary>
    public string? IdentityPoolName { get; init; }

    /// <summary>The authentication configuration the user signs in with; Tableau Cloud only, from REST API 3.25.</summary>
    public string? IdpConfigurationId { get; init; }

    /// <summary>Where the user gets notifications, when not at their name; Tableau Cloud only, from REST API 3.26.</summary>
    public string? Email { get; init; }

    /// <summary>The user's language, such as <c>en_GB</c>; Tableau Cloud only, from REST API 3.28.</summary>
    public string? Language { get; init; }

    /// <summary>The user's locale, such as <c>en_GB</c>; Tableau Cloud only, from REST API 3.28.</summary>
    public string? Locale { get; init; }
}
