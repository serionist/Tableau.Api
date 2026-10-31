using System;
using System.Globalization;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using Tableau.Api.Rest.Models;

namespace Tableau.Api.Pipeline;

/// <summary>
/// A named client's own session: signed in on demand with the client's credentials, one sign-in at a time, held as an
/// immutable <see cref="TableauSession"/> swapped whole, and signed in again before it is due to end or once after a
/// 401. Under DI it lives in the name's singleton state, shared by every client of the name; a client built without DI
/// holds its own, and signs it out when it is disposed.
/// </summary>
/// <remarks>
/// One sign-in at a time, since Tableau allows a personal access token one session: two at once would end each other.
/// Callers that wait while a sign-in fails get its failure rather than trying again each, so twenty calls with a wrong
/// password make one failed sign-in, not twenty, which a server's lockout would count.
/// </remarks>
/// <param name="state">The named client's state: its options, its clock, its deployment and its logger.</param>
/// <param name="credentials">What the client signs in with, or null for a client that signs in only when told to.</param>
internal sealed class TableauSessions(TableauClientState state, TableauCredentials? credentials) : IDisposable
{
    /// <summary>How long a session lasts on Tableau Server, unless its administrator changed it.</summary>
    public static readonly TimeSpan ServerLifetime = TimeSpan.FromMinutes(240);

    /// <summary>How long a session lasts on Tableau Cloud.</summary>
    public static readonly TimeSpan CloudLifetime = TimeSpan.FromMinutes(120);

    /// <summary>How long before its end a session is replaced: five minutes, or half its life when that is shorter.</summary>
    public static readonly TimeSpan RenewBefore = TimeSpan.FromMinutes(5);

    /// <summary>
    /// How long signing out may take when a client is disposed, or after a token went to another host: it is never
    /// waited for longer, so a dispose can't hang on an unreachable server.
    /// </summary>
    public static readonly TimeSpan SignOutTimeout = TimeSpan.FromSeconds(10);

    private readonly SemaphoreSlim _signIn = new(1, 1);
    private Held? _held;
    private int _failures;
    private ExceptionDispatchInfo? _failure;

    /// <summary>The client's credentials, or null.</summary>
    public TableauCredentials? Credentials => credentials;

    /// <summary>The session held now, or null before the first sign-in and after a sign-out.</summary>
    public TableauSession? Current => Volatile.Read(ref _held)?.Session;

    /// <summary>
    /// The session a call is made in: the one held while it isn't due for renewal, or a new one. Throws
    /// <see cref="TableauErrorKind.Authentication"/> before sending anything when the client has no credentials.
    /// </summary>
    public ValueTask<TableauSession> SessionAsync(TableauPipeline pipeline, Endpoint endpoint, CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _held) is { } held && state.TimeProvider.GetUtcNow() < held.RenewAt)
            return ValueTask.FromResult(held.Session);
        if (credentials is null)
        {
            throw pipeline.Failed(endpoint, null, new TableauException(TableauErrorKind.Authentication,
                $"{endpoint.Name} needs a session, and the client has no credentials to sign in with, so it was not sent. Give "
                + "the client credentials (TableauClient.Create, its constructor or AddTableauClient(...).UseCredentials), or "
                + "call it on a client from WithSession.")
            { Method = endpoint.Method });
        }
        return SignInAsync(pipeline, failed: null, cancellationToken);
    }

    /// <summary>
    /// A new session for a call whose session the server refused: one another call signed in meanwhile, or a sign-in of
    /// this call's own.
    /// </summary>
    public ValueTask<TableauSession> RenewAsync(TableauPipeline pipeline, TableauSession failed, CancellationToken cancellationToken) =>
        SignInAsync(pipeline, failed, cancellationToken);

    /// <summary>Drops the session held, if its token is the one given: one that went to another host.</summary>
    public void Discard(string token)
    {
        var held = Volatile.Read(ref _held);
        if (held is not null && held.Session.Token == token)
            Interlocked.CompareExchange(ref _held, null, held);
    }

    /// <summary>
    /// Signs the session held out, once, when the client that owns it is disposed: never throwing, and never waiting
    /// longer than <see cref="SignOutTimeout"/>. A failure is logged at Debug; the session then ends on its own.
    /// </summary>
    public async Task SignOutOnDisposeAsync(TableauPipeline pipeline)
    {
        if (Interlocked.Exchange(ref _held, null) is not { } held) return;
        using var timeout = new CancellationTokenSource(SignOutTimeout);
        try
        {
            await SignOutAsync(pipeline, held.Session, timeout.Token).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            // Whatever went wrong, a dispose doesn't throw: the session then ends on its own.
            TableauLog.SignOutFailed(state.Logger, state.DisplayName, e);
        }
    }

    /// <summary>
    /// Ends the one-at-a-time sign-in, with the state that owns it: a call still signing in then fails. It sends nothing;
    /// a client that owns its session signs it out first.
    /// </summary>
    public void Dispose() => _signIn.Dispose();

    /// <summary>Signs in at the client's negotiated version and returns the session, which nothing here holds.</summary>
    public static async Task<TableauSession> SignInAsync(TableauPipeline pipeline, TableauCredentials credentials, string site,
        CancellationToken cancellationToken)
    {
        var state = pipeline.State;
        var endpoint = credentials.Endpoint;
        // The gates first: a kind the server or its deployment lacks is refused before any secret is read.
        var version = await pipeline.GatesAsync(endpoint, cancellationToken).ConfigureAwait(false);
        Credentials body;
        string? userName;
        try
        {
            (body, userName) = await credentials.SignInAsync(site, state.TimeProvider, cancellationToken).ConfigureAwait(false);
        }
        catch (TableauException e)
        {
            throw pipeline.Failed(endpoint, null, e);
        }
        var answer = await pipeline.SendAtAsync(endpoint, version, new TableauRequest(Body: new TsRequest { Credentials = body }),
            static answer => answer.Credentials, "credentials", cancellationToken).ConfigureAwait(false);
        var session = Session(pipeline, endpoint, version, answer, site, userName);
        TableauLog.SignedIn(state.Logger, state.DisplayName, credentials.Kind, session.ExpiresAt);
        return session;
    }

    /// <summary>Switch Site: a session on another site, from the one given, whose token the server then ends.</summary>
    public static async Task<TableauSession> SwitchSiteAsync(TableauPipeline pipeline, TableauSession session, string site,
        CancellationToken cancellationToken)
    {
        var endpoint = Endpoints.SwitchSite;
        var version = await pipeline.GatesAsync(endpoint, cancellationToken).ConfigureAwait(false);
        var answer = await pipeline.SendAtAsync(endpoint, version,
            new TableauRequest(Body: new TsRequest { Site = new Site { ContentUrl = site } }, Token: session.Token),
            static answer => answer.Credentials, "credentials", cancellationToken).ConfigureAwait(false);
        return Session(pipeline, endpoint, version, answer, site, session.UserName);
    }

    /// <summary>Sign Out: ends the session given.</summary>
    public static async Task SignOutAsync(TableauPipeline pipeline, TableauSession session, CancellationToken cancellationToken)
    {
        await pipeline.SendAsync(Endpoints.SignOut, new TableauRequest(Token: session.Token), cancellationToken).ConfigureAwait(false);
        TableauLog.SignedOut(pipeline.State.Logger, pipeline.State.DisplayName);
    }

    /// <summary>
    /// A session's expected end: its deployment's lifetime from now, or sooner when a personal access token's
    /// <c>estimatedTimeToExpiration</c> says the token expires first.
    /// </summary>
    public static DateTimeOffset ExpiresAt(DateTimeOffset now, TableauDeployment deployment, string? estimatedTimeToExpiration)
    {
        var lifetime = deployment == TableauDeployment.Cloud ? CloudLifetime : ServerLifetime;
        if (TokenLifetime(estimatedTimeToExpiration) is { } token && token < lifetime)
            lifetime = token;
        return now + lifetime;
    }

    /// <summary>
    /// A personal access token's <c>estimatedTimeToExpiration</c>, which Tableau writes as <c>DAYS:HH:MM</c>, such as
    /// <c>363:10:35</c>; null for text in another shape.
    /// </summary>
    public static TimeSpan? TokenLifetime(string? text)
    {
        var parts = text?.Split(':');
        if (parts is not { Length: 3 }) return null;
        return int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var days)
            && int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var hours) && hours < 24
            && int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var minutes) && minutes < 60
            && days <= TimeSpan.MaxValue.Days - 1
                ? new TimeSpan(days, hours, minutes, 0)
                : null;
    }

    // One sign-in at a time. A caller that waited while another signed in takes that session; one that arrived before
    // another's sign-in failed, the attempt under way when it came among them, takes that failure; a caller whose session
    // the server refused signs in again, unless another did meanwhile. A cancelled sign-in is no failure: the next caller
    // tries for itself.
    private async ValueTask<TableauSession> SignInAsync(TableauPipeline pipeline, TableauSession? failed, CancellationToken cancellationToken)
    {
        var failuresSeen = Volatile.Read(ref _failures);
        await _signIn.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_held is { } held && !ReferenceEquals(held.Session, failed) && state.TimeProvider.GetUtcNow() < held.RenewAt)
                return held.Session;
            if (Volatile.Read(ref _failures) != failuresSeen && _failure is { } failure)
                failure.Throw();
            if (failed is not null)
                TableauLog.SigningInAgain(state.Logger, state.DisplayName, "the server refused the session with a 401");
            else if (_held is not null)
                TableauLog.SigningInAgain(state.Logger, state.DisplayName, "the session is due to end");
            try
            {
                var session = await SignInAsync(pipeline, credentials!, state.Options.Site, cancellationToken).ConfigureAwait(false);
                _failure = null;
                Volatile.Write(ref _held, new Held(session, RenewAt(session)));
                return session;
            }
            catch (TableauException e)
            {
                _failure = ExceptionDispatchInfo.Capture(e);
                Interlocked.Increment(ref _failures);
                throw;
            }
        }
        finally
        {
            _signIn.Release();
        }
    }

    private DateTimeOffset RenewAt(TableauSession session)
    {
        var left = session.ExpiresAt - state.TimeProvider.GetUtcNow();
        return session.ExpiresAt - (left / 2 < RenewBefore ? left / 2 : RenewBefore);
    }

    private static TableauSession Session(TableauPipeline pipeline, Endpoint endpoint, Version version, Credentials answer, string site,
        string? userName)
    {
        var state = pipeline.State;
        if (string.IsNullOrEmpty(answer.Token) || string.IsNullOrEmpty(answer.Site?.Id) || string.IsNullOrEmpty(answer.User?.Id))
        {
            var missing = string.IsNullOrEmpty(answer.Token) ? "token" : string.IsNullOrEmpty(answer.Site?.Id) ? "site id" : "user id";
            throw pipeline.Failed(endpoint, null, new TableauException(TableauErrorKind.InvalidResponse,
                $"{endpoint.Name} answered credentials with no {missing}, so the client has no session.")
            { Method = endpoint.Method });
        }
        return new TableauSession
        {
            SiteId = answer.Site.Id,
            SiteContentUrl = answer.Site.ContentUrl ?? site,
            UserId = answer.User.Id,
            UserName = userName,
            ExpiresAt = ExpiresAt(state.TimeProvider.GetUtcNow(), state.Deployment, answer.EstimatedTimeToExpiration),
            Deployment = state.Deployment,
            ApiVersion = version,
            Token = answer.Token,
        };
    }

    /// <summary>A session held, with when it is replaced, swapped as one.</summary>
    private sealed record Held(TableauSession Session, DateTimeOffset RenewAt);
}
