using System;
using System.Threading;
using System.Threading.Tasks;
using Tableau.Api.Pipeline;

namespace Tableau.Api;

/// <summary>
/// Sessions you hold yourself: <see cref="TableauClient.Sessions"/>. A client with credentials signs itself in and needs
/// none of this; sign in here for a session of your own, such as one per site, and call in it through
/// <see cref="TableauClient.WithSession"/>.
/// </summary>
/// <remarks>
/// A session signed in here is yours: the client never replaces it, and never signs it out, not even when it is
/// disposed. Sign it out with <see cref="SignOutAsync"/>, or it ends on its own (<see cref="TableauSession.ExpiresAt"/>).
/// </remarks>
public class SessionsClient
{
    private readonly TableauPipeline? _pipeline;

    /// <summary>A sub-client with no connection, for a mock to derive from: its methods throw until overridden.</summary>
    protected SessionsClient()
    {
    }

    internal SessionsClient(TableauPipeline pipeline)
    {
        _pipeline = pipeline;
    }

    private TableauPipeline Pipeline => _pipeline ?? throw TableauClient.ForMocksOnly();

    /// <summary>Sign In: a new session on a site, which you hold and end.</summary>
    /// <remarks>
    /// A personal access token allows one session at a time: signing in with one ends its earlier session, the session a
    /// client with the same token signed itself in with among them. Impersonation is Tableau Server's alone, and a unified
    /// access token Tableau Cloud's from REST API 3.27: each is refused on the other before anything is sent.
    /// </remarks>
    /// <param name="credentials">What to sign in with.</param>
    /// <param name="site">The site's content URL, <c>""</c> for the default site; null takes the options' <see cref="TableauClientOptions.Site"/>.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The session.</returns>
    /// <exception cref="TableauException">
    /// The sign-in failed; <see cref="TableauErrorKind.Authentication"/> when Tableau refused the credentials, or the
    /// delegate that gives a secret failed or gave none. The exception holds no part of Tableau's answer's body.
    /// </exception>
    public virtual Task<TableauSession> SignInAsync(TableauCredentials credentials, string? site = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(credentials);
        var pipeline = Pipeline;
        return TableauSessions.SignInAsync(pipeline, credentials, site ?? pipeline.State.Options.Site, cancellationToken);
    }

    /// <summary>
    /// Switch Site: a session on another site for the same user, from <paramref name="session"/>, which the server ends.
    /// Tableau Server only: refused on Tableau Cloud before anything is sent.
    /// </summary>
    /// <param name="session">The session to switch from; it ends, so use the one returned.</param>
    /// <param name="site">The other site's content URL, <c>""</c> for the default site.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The session on the other site, with the same <see cref="TableauSession.UserName"/>.</returns>
    /// <exception cref="ArgumentException"><paramref name="session"/> holds no token: it wasn't made by a sign-in.</exception>
    /// <exception cref="TableauException">The call failed; <see cref="TableauException.Kind"/> says how.</exception>
    public virtual Task<TableauSession> SwitchSiteAsync(TableauSession session, string site, CancellationToken cancellationToken = default)
    {
        Signed(session);
        ArgumentNullException.ThrowIfNull(site);
        return TableauSessions.SwitchSiteAsync(Pipeline, session, site, cancellationToken);
    }

    /// <summary>Sign Out: ends <paramref name="session"/>, so its token works no more.</summary>
    /// <param name="session">The session to end.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <exception cref="ArgumentException"><paramref name="session"/> holds no token: it wasn't made by a sign-in.</exception>
    /// <exception cref="TableauException">
    /// The call failed; <see cref="TableauErrorKind.Authentication"/> when the session had already ended.
    /// </exception>
    public virtual Task SignOutAsync(TableauSession session, CancellationToken cancellationToken = default)
    {
        Signed(session);
        return TableauSessions.SignOutAsync(Pipeline, session, cancellationToken);
    }

    /// <summary>Refuses a session no sign-in made, which has no token to send.</summary>
    internal static void Signed(TableauSession session, string name = "session")
    {
        ArgumentNullException.ThrowIfNull(session, name);
        if (string.IsNullOrEmpty(session.Token))
            throw new ArgumentException("This session holds no token: it wasn't made by a sign-in, so nothing can call in it.", name);
    }
}
