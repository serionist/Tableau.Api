using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Tableau.Api.Rest.Models;

namespace Tableau.Api.Pipeline;

/// <summary>What a call sends besides its endpoint: the values of its path, its query, its body and the session's token.</summary>
/// <param name="PathValues">A value for each <c>{name}</c> of the endpoint's template, in order.</param>
/// <param name="Query">The query's keys and values; a null value is left out.</param>
/// <param name="Body">The request's body.</param>
/// <param name="Token">
/// A session token of the call's own, sent in <c>X-Tableau-Auth</c> on this request alone, such as the one Sign Out ends;
/// null for a call made in the client's session, or in none.
/// </param>
/// <param name="SiteId">The site that fills <c>{siteId}</c> with <paramref name="Token"/>; the session's otherwise.</param>
/// <param name="Json">
/// A JSON body of the call's own, sent as it is, for a call outside Tableau's <c>tsRequest</c>, such as a Metadata API
/// query; null for <paramref name="Body"/>'s.
/// </param>
internal readonly record struct TableauRequest(
    IReadOnlyList<string>? PathValues = null,
    IReadOnlyList<KeyValuePair<string, string?>>? Query = null,
    TsRequest? Body = null,
    string? Token = null,
    string? SiteId = null,
    byte[]? Json = null)
{
    // The members a record prints, but the token, as ***: a request in a debugger or an assertion's message shows none.
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("PathValues = ").Append(PathValues)
            .Append(", Query = ").Append(Query)
            .Append(", Body = ").Append(Body)
            .Append(", Token = ").Append(Token is null ? null : "***")
            .Append(", SiteId = ").Append(SiteId)
            .Append(", Json = ").Append(Json is null ? null : $"{Json.Length} bytes");
        return true;
    }
}

/// <summary>
/// The one path every REST call of the 2.0 client takes: the gates, the session, the request URI, the headers,
/// redirects, the transfer timeout, errors and the answer's JSON or file. It sends through the client's chain, whose
/// handlers retry and time each attempt (<see cref="TableauResilience"/>), and reads the named client's state.
/// </summary>
/// <param name="http">
/// What sends: the client's HttpClient, from DI or <c>TableauClient.Create</c>, whose chain holds the library's handlers;
/// or an invoker over those handlers and a caller's HttpClient.
/// </param>
/// <param name="state">The named client's state, its session among it.</param>
/// <param name="bound">
/// The session a client from <c>WithSession</c> calls in, which it never replaces; null for the client's own session.
/// </param>
/// <param name="parent">The pipeline a bound one came from, whose disposal ends it too.</param>
internal sealed class TableauPipeline(HttpMessageInvoker http, TableauClientState state, TableauSession? bound = null,
    TableauPipeline? parent = null)
{
    /// <summary>The most redirects one call follows.</summary>
    public const int MostRedirects = 5;

    /// <summary>How much of a body an error keeps.</summary>
    public const int BodyKept = 4096;

    // A LUID that fills {siteId} while a call's arguments are checked, before it signs in for the real one.
    private const string NoSiteYet = "00000000-0000-0000-0000-000000000000";

    private static readonly MediaTypeWithQualityHeaderValue _acceptJson = new("application/json");
    private int _disposed;

    /// <summary>The named client's state, for the tests of its negotiation.</summary>
    internal TableauClientState State => state;

    /// <summary>A pipeline over the same connection and state, that calls in <paramref name="session"/> and never signs in.</summary>
    public TableauPipeline Bind(TableauSession session) => new(http, state, session, parent ?? this);

    /// <summary>Marks the pipeline's client disposed, so a later call throws <see cref="ObjectDisposedException"/>.</summary>
    public void Dispose() => Volatile.Write(ref _disposed, 1);

    private bool IsDisposed => Volatile.Read(ref _disposed) != 0 || parent?.IsDisposed == true;

    /// <summary>
    /// Sends a call and returns what <paramref name="select"/> takes from its answer. Before anything is sent, a call on a
    /// deployment its endpoint doesn't run on, or below its endpoint's version or the client's floor, throws.
    /// </summary>
    /// <param name="endpoint">The call's descriptor.</param>
    /// <param name="request">What it sends.</param>
    /// <param name="select">The element of the answer the call returns.</param>
    /// <param name="element">That element's JSON name, which an answer without it is said to lack.</param>
    /// <param name="cancellationToken">The caller's token.</param>
    public async Task<T> SendAsync<T>(Endpoint endpoint, TableauRequest request, Func<TsResponse, T?> select, string element,
        CancellationToken cancellationToken)
        where T : class
    {
        var version = await GatesAsync(endpoint, cancellationToken).ConfigureAwait(false);
        return await SendAtAsync(endpoint, version, request, select, element, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The same at the version given, without the gates: what version negotiation asks Server Info with, and what a
    /// sign-in sends once its own gates passed.
    /// </summary>
    public Task<T> SendAtAsync<T>(Endpoint endpoint, Version version, TableauRequest request, Func<TsResponse, T?> select,
        string element, CancellationToken cancellationToken)
        where T : class =>
        CallAsync(endpoint, version, request, (response, path, token) => ReadAsync(endpoint, response, path, select, element, token),
            cancellationToken);

    /// <summary>Sends a call whose answer holds nothing the caller needs, such as Sign Out's 204, after the gates.</summary>
    public async Task SendAsync(Endpoint endpoint, TableauRequest request, CancellationToken cancellationToken)
    {
        var version = await GatesAsync(endpoint, cancellationToken).ConfigureAwait(false);
        await CallAsync(endpoint, version, request, static (_, _, _) => Task.FromResult(true), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Sends a call whose answer is JSON of its own, outside Tableau's <c>tsResponse</c>, such as a Metadata API query's,
    /// after the gates, and returns what <paramref name="read"/> makes of the whole answer. An exception
    /// <paramref name="read"/> throws is the call's failure, logged as the pipeline's own are.
    /// </summary>
    /// <param name="endpoint">The call's descriptor.</param>
    /// <param name="request">What it sends, its body in <see cref="TableauRequest.Json"/>.</param>
    /// <param name="read">What the call returns, from the answer's bytes and the request's path as a message names it.</param>
    /// <param name="cancellationToken">The caller's token.</param>
    public async Task<T> SendForJsonAsync<T>(Endpoint endpoint, TableauRequest request, Func<byte[], string, T> read,
        CancellationToken cancellationToken)
    {
        var version = await GatesAsync(endpoint, cancellationToken).ConfigureAwait(false);
        return await CallAsync(endpoint, version, request, async (response, path, token) =>
            read(await response.Content.ReadAsByteArrayAsync(token).ConfigureAwait(false), path), cancellationToken).ConfigureAwait(false);
    }

    // One call: its session, then its request, sent once more in a new session when the server refused the first with a
    // 401, for a call whose body can be sent again. A sign-in's failure is the call's, logged where it happened.
    private async Task<T> CallAsync<T>(Endpoint endpoint, Version version, TableauRequest request,
        Func<HttpResponseMessage, string, CancellationToken, Task<T>> read, CancellationToken cancellationToken,
        bool signOutWhereSent = true)
    {
        var session = await SessionAsync(endpoint, version, request, cancellationToken).ConfigureAwait(false);
        for (var renewed = false; ; renewed = true)
        {
            var (uri, path, body) = Prepare(endpoint, version, request, request.SiteId ?? session?.SiteId);
            using var transfer = TransferTimer(endpoint, cancellationToken);
            var token = transfer?.Token ?? cancellationToken;
            var started = Stopwatch.GetTimestamp();
            HttpResponseMessage? response = null;
            try
            {
                response = await SendFollowingRedirectsAsync(endpoint, version, uri, path, body, request.Token ?? session?.Token,
                    signOutWhereSent, token).ConfigureAwait(false);
                // Otherwise it is sent again below, in a new session, and this answer has nothing to keep.
                if (renewed || !Renewable(endpoint, request, session, response))
                {
                    if (!response.IsSuccessStatusCode)
                        throw await ErrorAsync(endpoint, response, path, Hint(endpoint, request, session, renewed, response), token).ConfigureAwait(false);
                    var result = await read(response, path, token).ConfigureAwait(false);
                    var elapsed = Stopwatch.GetElapsedTime(started);
                    TableauLog.Answered(state.Logger, state.DisplayName, endpoint.Method, path, (int)response.StatusCode, elapsed.TotalMilliseconds);
                    return result;
                }
            }
            catch (Exception e)
            {
                throw Failure(endpoint, path, e, transfer, cancellationToken);
            }
            finally
            {
                response?.Dispose();
            }
            session = await state.Sessions.RenewAsync(this, session!, cancellationToken).ConfigureAwait(false);
        }
    }

    // The session a call is made in: none for a call that needs none or brings a token of its own; the bound one; or the
    // client's, signed in now if it has none, after the call's arguments were checked, so a call refused for them is
    // refused before anything is sent.
    private async ValueTask<TableauSession?> SessionAsync(Endpoint endpoint, Version version, TableauRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Token is not null || !endpoint.NeedsSession) return null;
        if (bound is not null) return bound;
        _ = Prepare(endpoint, version, request, request.SiteId ?? NoSiteYet);
        return await state.Sessions.SessionAsync(this, endpoint, cancellationToken).ConfigureAwait(false);
    }

    // A 401 to a call made in the client's own session, which it can replace: the session ended, or the server ended it
    // early. A bound session is the caller's to replace, and an upload's body may not be readable twice.
    private bool Renewable(Endpoint endpoint, TableauRequest request, TableauSession? session, HttpResponseMessage response) =>
        response.StatusCode == HttpStatusCode.Unauthorized && request.Token is null && session is not null && bound is null
        && state.Sessions.Credentials is not null && endpoint.Transfer != TransferKind.Upload;

    // What a 401's message adds, for a call made in a session; and a Metadata API query's 403, which a Tableau Server
    // 2025.1 answered to every query, from its metadata service, with no reason given.
    private string? Hint(Endpoint endpoint, TableauRequest request, TableauSession? session, bool renewed, HttpResponseMessage response)
    {
        if (endpoint == Endpoints.MetadataQuery && response.StatusCode == HttpStatusCode.Forbidden)
        {
            return "If the Metadata API is off, a Tableau Server administrator turns it on with tsm maintenance metadata-services enable; "
                + "on Tableau Cloud it needs Data Management.";
        }
        if (response.StatusCode != HttpStatusCode.Unauthorized || request.Token is not null || session is null) return null;
        if (bound is not null)
            return "The session given to WithSession has ended or was signed out; a client bound to one never signs in, so sign in again and bind the new session.";
        return renewed ? "The client signed in again and the server refused the new session too." : null;
    }

    /// <summary>
    /// Sends a call whose answer is a file, and returns it once its headers have come, and, when neither the file's
    /// name nor its media type says what it is, its first bytes, up to 8, its content still on the wire for the caller
    /// to read or save. The download owns the answer and the call's <see cref="TableauClientOptions.TransferTimeout"/>,
    /// which runs until it is disposed. The gates are <see cref="SendAsync"/>'s.
    /// </summary>
    /// <param name="endpoint">The call's descriptor, a <see cref="TransferKind.Download"/>.</param>
    /// <param name="request">What it sends.</param>
    /// <param name="cancellationToken">The caller's token, which also stops reading the file.</param>
    /// <exception cref="InvalidOperationException">The descriptor is no download.</exception>
    public async Task<TableauDownload> DownloadAsync(Endpoint endpoint, TableauRequest request, CancellationToken cancellationToken)
    {
        // The handler times and reads an attempt by the descriptor: a download's answer it leaves on the wire, under no
        // attempt timeout, and TransferTimeout runs here until the file is read and disposed.
        if (endpoint.Transfer != TransferKind.Download)
            throw new InvalidOperationException($"{endpoint.Name} is no download: its descriptor's transfer is {endpoint.Transfer}.");
        var version = await GatesAsync(endpoint, cancellationToken).ConfigureAwait(false);
        var session = await SessionAsync(endpoint, version, request, cancellationToken).ConfigureAwait(false);
        // One transfer timeout for the whole download, a request sent again in a new session included.
        var transfer = TransferTimer(endpoint, cancellationToken);
        try
        {
            for (var renewed = false; ; renewed = true)
            {
                var (uri, path, body) = Prepare(endpoint, version, request, request.SiteId ?? session?.SiteId);
                HttpResponseMessage? response = null;
                var started = Stopwatch.GetTimestamp();
                try
                {
                    response = await SendFollowingRedirectsAsync(endpoint, version, uri, path, body, request.Token ?? session?.Token,
                        signOutWhereSent: true, transfer!.Token).ConfigureAwait(false);
                    if (renewed || !Renewable(endpoint, request, session, response))
                    {
                        if (!response.IsSuccessStatusCode)
                            throw await ErrorAsync(endpoint, response, path, Hint(endpoint, request, session, renewed, response), transfer.Token).ConfigureAwait(false);
                        var call = new DownloadCall(this, endpoint, path, cancellationToken, transfer.Token);
                        var download = await TableauDownload.OpenAsync(response, transfer, call).ConfigureAwait(false);
                        var elapsed = Stopwatch.GetElapsedTime(started);
                        TableauLog.Answered(state.Logger, state.DisplayName, endpoint.Method, path, (int)response.StatusCode, elapsed.TotalMilliseconds);
                        response = null;
                        transfer = null;
                        return download;
                    }
                }
                catch (Exception e)
                {
                    throw Failure(endpoint, path, e, transfer, cancellationToken);
                }
                finally
                {
                    response?.Dispose();
                }
                session = await state.Sessions.RenewAsync(this, session!, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            transfer?.Dispose();
        }
    }

    /// <summary>A failure the pipeline made, logged at Debug, as the caller receives it.</summary>
    internal TableauException Failed(Endpoint endpoint, string? path, TableauException exception)
    {
        TableauLog.Failed(state.Logger, state.DisplayName, endpoint.Method, path ?? endpoint.Template, exception.Kind, exception);
        return exception;
    }

    /// <summary>The caller's cancellation, logged at Debug and thrown with the caller's own token.</summary>
    internal OperationCanceledException Cancelled(Endpoint endpoint, string path, Exception cause, CancellationToken cancellationToken)
    {
        TableauLog.Cancelled(state.Logger, state.DisplayName, endpoint.Method, path);
        return new OperationCanceledException(cause.Message, cause, cancellationToken);
    }

    /// <summary>
    /// The deployment gate before anything is sent, then the version gates once the version is known: the version a call
    /// is made at, or <see cref="TableauException"/> with nothing sent but Server Info.
    /// </summary>
    internal async ValueTask<Version> GatesAsync(Endpoint endpoint, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, typeof(TableauClient));
        cancellationToken.ThrowIfCancellationRequested();
        var deployment = state.Deployment == TableauDeployment.Cloud ? TableauDeployments.Cloud : TableauDeployments.Server;
        if ((endpoint.Deployments & deployment) == 0)
        {
            throw Failed(endpoint, null, new TableauException(TableauErrorKind.UnsupportedDeployment,
                $"{endpoint.Name} doesn't exist on {(deployment == TableauDeployments.Cloud ? "Tableau Cloud" : "Tableau Server")}, "
                + "so it was not sent.")
            { Method = endpoint.Method });
        }
        var version = await state.ApiVersionAsync(this, endpoint, cancellationToken).ConfigureAwait(false);
        if (version < endpoint.MinVersion)
        {
            throw Failed(endpoint, null, new TableauException(TableauErrorKind.UnsupportedApiVersion,
                $"{endpoint.Name} needs REST API {ApiVersions.Format(endpoint.MinVersion)}, and the client calls "
                + $"{ApiVersions.Format(version)}, so it was not sent.")
            { Method = endpoint.Method });
        }
        return version;
    }

    private (Uri Uri, string Path, byte[]? Body) Prepare(Endpoint endpoint, Version version, TableauRequest request, string? siteId)
    {
        var uri = RequestUri.Build(state.ServerUrl, endpoint, version, request.PathValues, request.Query, siteId);
        var body = request.Body is null ? request.Json : JsonSerializer.SerializeToUtf8Bytes(request.Body, TableauJsonContext.Default.TsRequest);
        return (uri, RequestUri.Redacted(uri), body);
    }

    // A transfer runs under TransferTimeout as a whole: its attempts, the waits between them, and reading its file. A call
    // that moves no file has none: each of its attempts has RequestTimeout, in the chain.
    private CancellationTokenSource? TransferTimer(Endpoint endpoint, CancellationToken cancellationToken)
    {
        if (endpoint.Transfer == TransferKind.None) return null;
        var timer = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timer.CancelAfter(state.Options.TransferTimeout);
        return timer;
    }

    // What the caller receives for a failure: the caller's cancellation as itself; the attempt timeout, the transfer
    // timeout or a caller's HttpClient's own as Timeout; a failed connection, or a handler's own exception, timeout or
    // cancellation, as Http; the pipeline's own exceptions as they are; each logged at Debug.
    private Exception Failure(Endpoint endpoint, string path, Exception exception, CancellationTokenSource? transfer,
        CancellationToken cancellationToken)
    {
        switch (exception)
        {
            case OperationCanceledException when cancellationToken.IsCancellationRequested:
                // Thrown again with the caller's token, which the HttpClient only saw linked to the transfer's.
                return Cancelled(endpoint, path, exception, cancellationToken);
            case TableauAttemptTimeoutException attempt:
                return Failed(endpoint, path, new TableauException(TableauErrorKind.Timeout,
                    $"{endpoint.Name} ({endpoint.Method} {path}): its last attempt took longer than its RequestTimeout allows.",
                    attempt.InnerException)
                { Method = endpoint.Method, RequestPath = path });
            case OperationCanceledException when transfer is { IsCancellationRequested: true }:
                return Failed(endpoint, path, new TableauException(TableauErrorKind.Timeout,
                    $"{endpoint.Name} ({endpoint.Method} {path}) took longer than its TransferTimeout allows.", exception)
                { Method = endpoint.Method, RequestPath = path });
            case OperationCanceledException { InnerException: TimeoutException } when http is not HttpClient:
                // How an HttpClient reports its own Timeout. Only a caller's HttpClient has one, sent through by an invoker;
                // the library's own are infinite, so under DI or Create this is a handler's own, which is Http below.
                return Failed(endpoint, path, new TableauException(TableauErrorKind.Timeout,
                    $"{endpoint.Name} ({endpoint.Method} {path}) took longer than the HttpClient's Timeout allows.", exception)
                { Method = endpoint.Method, RequestPath = path });
            case OperationCanceledException:
                return Failed(endpoint, path, new TableauException(TableauErrorKind.Http,
                    $"{endpoint.Name} ({endpoint.Method} {path}) got no answer: a handler cancelled it: {exception.Message}", exception)
                { Method = endpoint.Method, RequestPath = path });
            case HttpRequestException or IOException:
                return Failed(endpoint, path, new TableauException(TableauErrorKind.Http,
                    $"{endpoint.Name} ({endpoint.Method} {path}) got no answer: {exception.Message}", exception)
                { Method = endpoint.Method, RequestPath = path, StatusCode = (exception as HttpRequestException)?.StatusCode });
            case TableauException tableau:
                return Failed(endpoint, path, tableau);
            default:
                return exception;
        }
    }

    private async Task<HttpResponseMessage> SendFollowingRedirectsAsync(Endpoint endpoint, Version version, Uri uri, string path,
        byte[]? body, string? token, bool signOutWhereSent, CancellationToken cancellationToken)
    {
        var method = endpoint.Method;
        for (var redirects = 0; ; redirects++)
        {
            var response = await SendOnceAsync(endpoint, method, uri, path, body, token, cancellationToken).ConfigureAwait(false);
            try
            {
                // A caller's HttpClient may follow redirects itself; then the answer came from wherever it went, and the
                // request, its headers and its body went there too, which this can only report. A session token among
                // them can be used there, so its session is signed out, and the client's own signs in again.
                if (response.RequestMessage?.RequestUri is { } answered && !RequestUri.SameOrigin(answered, uri))
                {
                    var origin = RequestUri.Origin(answered);
                    var signedOut = Endpoints.IsSignIn(endpoint)
                        ? " The credentials it carried went there too: rotate them, a password or a personal access token's secret."
                        : "";
                    if (token is not null && signOutWhereSent)
                    {
                        TableauLog.TokenSentElsewhere(state.Logger, state.DisplayName, origin);
                        state.Sessions.Discard(token);
                        signedOut = await SignOutSentAsync(version, token, cancellationToken).ConfigureAwait(false)
                            ? " The session whose token it carried is signed out."
                            : " The session whose token it carried couldn't be signed out, and ends on its own.";
                    }
                    throw new TableauException(TableauErrorKind.Redirect,
                        $"{endpoint.Name} ({endpoint.Method} {path}) was redirected by the HttpClient's own handler, which sent "
                        + $"the request, its headers and its body on to {origin}. Give the client an "
                        + $"HttpClient whose handler follows no redirect (AllowAutoRedirect = false).{signedOut}")
                    {
                        StatusCode = response.StatusCode,
                        Method = endpoint.Method,
                        RequestPath = path,
                    };
                }
                var status = (int)response.StatusCode;
                if (status is < 300 or > 399) return response;

                var target = response.Headers.Location is { } location
                    ? new Uri(uri, location)
                    : throw RedirectRefused(endpoint, path, response, "a redirect that names no address");
                if (!RequestUri.SameOrigin(target, uri))
                    throw RedirectRefused(endpoint, path, response, $"a redirect to {RequestUri.Origin(target)}, another origin");
                if (status is not (301 or 302 or 303 or 307 or 308))
                    throw RedirectRefused(endpoint, path, response, $"a {status} answer, which is no redirect the client follows");
                if (redirects == MostRedirects)
                    throw RedirectRefused(endpoint, path, response, $"more than {MostRedirects} redirects in a row");
                if (status is 301 or 302 or 303 && method != HttpMethod.Get && method != HttpMethod.Head)
                    throw RedirectRefused(endpoint, path, response, $"a {status} redirect of a {method}, which would lose its body");

                var targetPath = RequestUri.Redacted(target);
                TableauLog.Redirected(state.Logger, state.DisplayName, method, path, status, targetPath);
                uri = target;
                path = targetPath;
            }
            catch
            {
                response.Dispose();
                throw;
            }
            response.Dispose();
        }
    }

    private async Task<HttpResponseMessage> SendOnceAsync(Endpoint endpoint, HttpMethod method, Uri uri, string path, byte[]? body,
        string? token, CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(method, uri);
        message.Options.Set(TableauResilience.EndpointKey, endpoint);
        message.Headers.Accept.Add(_acceptJson);
        // On the request, not the client's default headers, so the token goes with this request only.
        if (token is not null)
            message.Headers.TryAddWithoutValidation("X-Tableau-Auth", token);
        if (body is not null)
            message.Content = new ByteArrayContent(body) { Headers = { ContentType = new MediaTypeHeaderValue("application/json") } };
        TableauLog.Sending(state.Logger, state.DisplayName, method, path);
        try
        {
            return http is HttpClient client
                ? await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false)
                : await http.SendAsync(message, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not (OperationCanceledException or HttpRequestException or IOException or TableauAttemptTimeoutException))
        {
            // A handler of the caller's threw something of its own, a timeout of its own among them: the request got no
            // answer, and the call fails as one that got none, with the handler's exception as the cause.
            throw new TableauException(TableauErrorKind.Http, $"{endpoint.Name} ({method} {path}) got no answer: a handler failed it: {e.Message}", e)
            { Method = method, RequestPath = path };
        }
    }

    // Signs out the session of a token a caller's handler sent to another host, so it stops working there: through the
    // same HttpClient, which may send this elsewhere too, so a redirect here signs nothing more out. Never throws, and
    // waits no longer than a dispose's sign-out would, nor than the call's own time, so a dispose that sent the token
    // keeps its bound.
    private async Task<bool> SignOutSentAsync(Version version, string token, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TableauSessions.SignOutTimeout);
        try
        {
            await CallAsync(Endpoints.SignOut, version, new TableauRequest(Token: token), static (_, _, _) => Task.FromResult(true),
                timeout.Token, signOutWhereSent: false).ConfigureAwait(false);
            TableauLog.SignedOut(state.Logger, state.DisplayName);
            return true;
        }
        catch (Exception e)
        {
            TableauLog.SignOutFailed(state.Logger, state.DisplayName, e);
            return false;
        }
    }

    private static TableauException RedirectRefused(Endpoint endpoint, string path, HttpResponseMessage response, string what) =>
        new(TableauErrorKind.Redirect, $"{endpoint.Name} ({endpoint.Method} {path}) answered {what}, which the client doesn't "
            + "follow, so nothing was sent there.")
        {
            StatusCode = response.StatusCode,
            Method = endpoint.Method,
            RequestPath = path,
        };

    private static async Task<T> ReadAsync<T>(Endpoint endpoint, HttpResponseMessage response, string path,
        Func<TsResponse, T?> select, string element, CancellationToken cancellationToken)
        where T : class
    {
        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        TsResponse? body;
        try
        {
            body = bytes.Length == 0 ? null : JsonSerializer.Deserialize(bytes, TableauJsonContext.Default.TsResponse);
        }
        catch (JsonException e)
        {
            throw Invalid(endpoint, response, path, $"a body that isn't Tableau's JSON", Kept(bytes, bytes.Length), e);
        }
        return (body is null ? null : select(body))
            ?? throw Invalid(endpoint, response, path, bytes.Length == 0 ? "no body" : $"no {element}", Kept(bytes, bytes.Length), null);
    }

    private static TableauException Invalid(Endpoint endpoint, HttpResponseMessage response, string path, string what,
        string? kept, Exception? inner) =>
        new(TableauErrorKind.InvalidResponse,
            $"{endpoint.Name} ({endpoint.Method} {path}) answered {(int)response.StatusCode} with {what}, where it returns "
            + "Tableau's JSON.", inner)
        {
            StatusCode = response.StatusCode,
            Method = endpoint.Method,
            RequestPath = path,
            // A sign-in's answer holds its token, which an exception would carry into a log.
            ResponseBody = endpoint.KeepsBody ? kept : null,
        };

    private async Task<TableauException> ErrorAsync(Endpoint endpoint, HttpResponseMessage response, string path, string? hint,
        CancellationToken cancellationToken)
    {
        // The chain kept at most the body's first 64 KB; this reads no more, whatever sent it.
        var (bytes, length) = await ReadSomeAsync(response.Content, TableauResilienceHandler.ErrorBodyRead, cancellationToken)
            .ConfigureAwait(false);
        var (code, summary, detail) = TableauError(bytes, length);
        var status = response.StatusCode;
        var kind = status switch
        {
            HttpStatusCode.Unauthorized => TableauErrorKind.Authentication,
            HttpStatusCode.NotFound => TableauErrorKind.NotFound,
            HttpStatusCode.TooManyRequests => TableauErrorKind.RateLimited,
            _ => TableauErrorKind.Http,
        };
        var retryAfter = TableauResilience.RetryAfter(response.Headers.RetryAfter, state.TimeProvider)
            ?? (kind == TableauErrorKind.RateLimited ? TableauResilience.WaitInBody(Encoding.UTF8.GetString(bytes, 0, length)) : null);
        var message = new StringBuilder($"{endpoint.Name} ({endpoint.Method} {path}) answered {(int)status} {response.ReasonPhrase}");
        if (code is not null || summary is not null)
            message.Append(". Tableau's error").Append(code is null ? "" : " " + code).Append(": ").Append(summary);
        if (detail is not null)
            message.Append(": ").Append(detail);
        // Tableau's detail is often a sentence of its own, with its full stop.
        if (message[^1] != '.')
            message.Append('.');
        if (hint is not null)
            message.Append(' ').Append(hint);
        return new TableauException(kind, message.ToString())
        {
            StatusCode = status,
            ErrorCode = code,
            Summary = summary,
            Detail = detail,
            Method = endpoint.Method,
            RequestPath = path,
            RequestId = response.Headers.TryGetValues("X-Request-Id", out var ids) ? ids.FirstOrDefault() : null,
            RetryAfter = retryAfter,
            ResponseBody = length == 0 || !endpoint.KeepsBody ? null : Kept(bytes, length),
        };
    }

    // Tableau's error JSON: {"error":{"code":"404004","summary":...,"detail":...}}, the code a string or a number; the
    // resource-versioned endpoints under api/- answer {"httpErrorCode":404,"message":...}; GraphQL's
    // {"errors":[{"message":...,"extensions":{"code":...}}]}.
    private static (string? Code, string? Summary, string? Detail) TableauError(byte[] bytes, int length)
    {
        try
        {
            using var document = JsonDocument.Parse(bytes.AsMemory(0, length));
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return default;
            if (root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object)
                return (Text(error, "code"), Text(error, "summary"), Text(error, "detail"));
            if (TableauGraphQL.Errors(root) is [var first, ..])
                return (first.Code, first.Message, null);
            return (Text(root, "httpErrorCode"), Text(root, "message"), null);
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException)
        {
            // Not JSON, or JSON whose strings hold what no string can, such as a lone surrogate or bytes that aren't
            // UTF-8, which GetString refuses: the error is the status alone, with the body kept.
            return default;
        }

        static string? Text(JsonElement element, string name) =>
            !element.TryGetProperty(name, out var value) ? null : value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                JsonValueKind.Number => value.GetRawText(),
                _ => null,
            };
    }

    private static async Task<(byte[] Bytes, int Length)> ReadSomeAsync(HttpContent content, int most,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[most];
        var length = 0;
        var stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using (stream.ConfigureAwait(false))
        {
            int read;
            while (length < most && (read = await stream.ReadAsync(buffer.AsMemory(length), cancellationToken).ConfigureAwait(false)) > 0)
                length += read;
        }
        return (buffer, length);
    }

    /// <summary>The first 4 KB of a body as text, cut where a character starts, so no half of one is kept.</summary>
    internal static string Kept(byte[] bytes, int length)
    {
        var cut = Math.Min(length, BodyKept);
        while (cut < length && cut > 0 && (bytes[cut] & 0xC0) == 0x80) cut--;
        return Encoding.UTF8.GetString(bytes, 0, cut);
    }
}

/// <summary>
/// What a download's content stream needs to fail as its call would: the pipeline that logs, the call's descriptor and
/// path, the caller's token, and the token that also carries <see cref="TableauClientOptions.TransferTimeout"/>.
/// </summary>
internal sealed class DownloadCall(TableauPipeline pipeline, Endpoint endpoint, string path, CancellationToken callerToken,
    CancellationToken transferToken)
{
    /// <summary>The token every read honours: the caller's, linked with the transfer timeout.</summary>
    public CancellationToken Token => transferToken;

    /// <summary>
    /// What a read's failure becomes: a read's own cancellation and the caller's stay cancellations, the transfer timeout
    /// is <see cref="TableauErrorKind.Timeout"/>, a connection that broke off is <see cref="TableauErrorKind.Http"/>.
    /// </summary>
    public Exception Failure(Exception exception, long read, CancellationToken readToken)
    {
        if (exception is OperationCanceledException && readToken.IsCancellationRequested)
            return new OperationCanceledException(exception.Message, exception, readToken);
        if (callerToken.IsCancellationRequested)
            return pipeline.Cancelled(endpoint, path, exception, callerToken);
        if (transferToken.IsCancellationRequested)
        {
            return pipeline.Failed(endpoint, path, new TableauException(TableauErrorKind.Timeout,
                $"{endpoint.Name} ({endpoint.Method} {path}) took longer than its TransferTimeout allows, after {Bytes(read)} bytes of its "
                + "file.", exception)
            { Method = endpoint.Method, RequestPath = path });
        }
        return exception is IOException or HttpRequestException ? pipeline.Failed(endpoint, path, BrokeOff(exception, read)) : exception;
    }

    /// <summary>
    /// A connection that broke off after <paramref name="read"/> bytes of the file, not logged yet: a read logs it
    /// through <see cref="Failure"/>, and reading ahead to tell the kind leaves it to the pipeline.
    /// </summary>
    public TableauException BrokeOff(Exception exception, long read) =>
        new(TableauErrorKind.Http,
            $"{endpoint.Name} ({endpoint.Method} {path}) broke off after {Bytes(read)} bytes of its file: {exception.Message}", exception)
        { Method = endpoint.Method, RequestPath = path };

    // A count as every culture reads it.
    private static string Bytes(long read) => read.ToString(CultureInfo.InvariantCulture);
}
