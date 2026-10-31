using System;
using System.Globalization;
using System.Text;

namespace Tableau.Api;

/// <summary>
/// A signed-in session on one site, as <see cref="SessionsClient.SignInAsync"/> and
/// <see cref="SessionsClient.SwitchSiteAsync"/> return it. Give it to <see cref="TableauClient.WithSession"/> for a client
/// that calls in it, and end it with <see cref="SessionsClient.SignOutAsync"/>.
/// </summary>
/// <remarks>
/// <para>
/// Its token goes only in each request's <c>X-Tableau-Auth</c> header: no member shows it, and <see cref="ToString"/>
/// leaves it out. A session is immutable; signing in again makes another.
/// </para>
/// <para>
/// A test can make one for a mock of <see cref="SessionsClient"/>. One made so holds no token, so
/// <see cref="TableauClient.WithSession"/> refuses it.
/// </para>
/// </remarks>
public sealed record TableauSession
{
    /// <summary>The site's LUID, which the calls made in the session name in their paths.</summary>
    public required string SiteId { get; init; }

    /// <summary>The site's content URL, as its address shows it after <c>/site/</c>; <c>""</c> for the default site.</summary>
    public required string SiteContentUrl { get; init; }

    /// <summary>The LUID of the user the session acts as: the impersonated user's, when the sign-in impersonated one.</summary>
    public required string UserId { get; init; }

    /// <summary>
    /// The name the session signed in with: the user's for a password or a connected app, the token's name for a
    /// personal access token, a JWT's <c>sub</c> claim when it has one, and the name of the session it came from after
    /// <see cref="SessionsClient.SwitchSiteAsync"/>. Null when the sign-in named none.
    /// </summary>
    public string? UserName { get; init; }

    /// <summary>
    /// When the session is expected to end: 240 minutes after it began on Tableau Server, 120 on Tableau Cloud, or when
    /// the personal access token it signed in with expires, if that is sooner. A server's administrator can make a
    /// session shorter, so a call can still find it ended. A client renews its own session five minutes before the
    /// deployment's lifetime is up, not by a token's sooner end, which comes as a 401 that signs in again.
    /// </summary>
    public required DateTimeOffset ExpiresAt { get; init; }

    /// <summary>Tableau Server or Tableau Cloud.</summary>
    public required TableauDeployment Deployment { get; init; }

    /// <summary>The REST API version the session was signed in at, major.minor.</summary>
    public required Version ApiVersion { get; init; }

    /// <summary>The session's token, for the requests made in it; never printed.</summary>
    internal string? Token { get; init; }

    // Every public member, never the token, which a record would leave out too, being internal; written out so no later
    // change prints it by accident.
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("SiteId = ").Append(SiteId)
            .Append(", SiteContentUrl = ").Append(SiteContentUrl)
            .Append(", UserId = ").Append(UserId)
            .Append(", UserName = ").Append(UserName)
            .Append(", ExpiresAt = ").Append(ExpiresAt.ToString("O", CultureInfo.InvariantCulture))
            .Append(", Deployment = ").Append(Deployment)
            .Append(", ApiVersion = ").Append(ApiVersion);
        return true;
    }
}
