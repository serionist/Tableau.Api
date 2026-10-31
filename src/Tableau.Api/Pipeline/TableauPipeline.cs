using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Polly.Timeout;
using Tableau.Api.Rest.Models;

namespace Tableau.Api.Pipeline;

/// <summary>What a call sends besides its endpoint: the values of its path, its query, its body and the session's token.</summary>
/// <param name="PathValues">A value for each <c>{name}</c> of the endpoint's template, in order.</param>
/// <param name="Query">The query's keys and values; a null value is left out.</param>
/// <param name="Body">The request's body.</param>
/// <param name="Token">The session token, sent in <c>X-Tableau-Auth</c> on this request alone.</param>
internal readonly record struct TableauRequest(
    IReadOnlyList<string>? PathValues = null,
    IReadOnlyList<KeyValuePair<string, string?>>? Query = null,
    TsRequest? Body = null,
    string? Token = null);

/// <summary>
/// The one path every REST call of the 2.0 client takes: the gates, the request URI, the headers, redirects, the
/// transfer timeout, errors and the answer's JSON or file. It sends through the client's chain, whose handlers retry
/// and time each attempt (<see cref="TableauResilience"/>), and reads the named client's state.
/// </summary>
/// <param name="http">
/// What sends: the client's HttpClient, from DI or <c>TableauClient.Create</c>, whose chain holds the library's handlers;
/// or an invoker over those handlers and a caller's HttpClient.
/// </param>
/// <param name="state">The named client's state.</param>
internal sealed class TableauPipeline(HttpMessageInvoker http, TableauClientState state)
{
    /// <summary>The most redirects one call follows.</summary>
    public const int MostRedirects = 5;

    /// <summary>How much of a body an error keeps.</summary>
    public const int BodyKept = 4096;

    private static readonly MediaTypeWithQualityHeaderValue _acceptJson = new("application/json");
    private int _disposed;

    /// <summary>The named client's state, for the tests of its negotiation.</summary>
    internal TableauClientState State => state;

    /// <summary>Marks the pipeline's client disposed, so a later call throws <see cref="ObjectDisposedException"/>.</summary>
    public void Dispose() => Volatile.Write(ref _disposed, 1);

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

    /// <summary>The same at the version given, without the gates: what version negotiation asks Server Info with.</summary>
    public async Task<T> SendAtAsync<T>(Endpoint endpoint, Version version, TableauRequest request, Func<TsResponse, T?> select,
        string element, CancellationToken cancellationToken)
        where T : class
    {
        var (uri, path, body) = Prepare(endpoint, version, request);
        using var transfer = TransferTimer(endpoint, cancellationToken);
        var token = transfer?.Token ?? cancellationToken;
        var started = Stopwatch.GetTimestamp();
        try
        {
            using var response = await SendFollowingRedirectsAsync(endpoint, uri, path, body, request.Token, token)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                throw await ErrorAsync(endpoint, response, path, token).ConfigureAwait(false);
            var result = await ReadAsync(endpoint, response, path, select, element, token).ConfigureAwait(false);
            var elapsed = Stopwatch.GetElapsedTime(started);
            TableauLog.Answered(state.Logger, state.DisplayName, endpoint.Method, path, (int)response.StatusCode, elapsed.TotalMilliseconds);
            return result;
        }
        catch (Exception e)
        {
            throw Failure(endpoint, path, e, cancellationToken);
        }
    }

    /// <summary>
    /// Sends a call whose answer is a file, and returns it as soon as its headers have come, its content still on the
    /// wire, for the caller to read or save. The download owns the answer and the call's <see cref="TableauClientOptions.TransferTimeout"/>,
    /// which runs until it is disposed. The gates are <see cref="SendAsync"/>'s.
    /// </summary>
    /// <param name="endpoint">The call's descriptor.</param>
    /// <param name="request">What it sends.</param>
    /// <param name="cancellationToken">The caller's token, which also stops reading the file.</param>
    public async Task<TableauDownload> DownloadAsync(Endpoint endpoint, TableauRequest request, CancellationToken cancellationToken)
    {
        var version = await GatesAsync(endpoint, cancellationToken).ConfigureAwait(false);
        var (uri, path, body) = Prepare(endpoint, version, request);
        // A download is a transfer, whatever its descriptor says: TransferTimeout runs until the file is read and disposed.
        var transfer = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        transfer.CancelAfter(state.Options.TransferTimeout);
        HttpResponseMessage? response = null;
        var started = Stopwatch.GetTimestamp();
        try
        {
            response = await SendFollowingRedirectsAsync(endpoint, uri, path, body, request.Token, transfer.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                throw await ErrorAsync(endpoint, response, path, transfer.Token).ConfigureAwait(false);
            var call = new DownloadCall(this, endpoint, path, cancellationToken, transfer.Token);
            var download = await TableauDownload.OpenAsync(response, transfer, call).ConfigureAwait(false);
            var elapsed = Stopwatch.GetElapsedTime(started);
            TableauLog.Answered(state.Logger, state.DisplayName, endpoint.Method, path, (int)response.StatusCode, elapsed.TotalMilliseconds);
            response = null;
            transfer = null;
            return download;
        }
        catch (Exception e)
        {
            throw Failure(endpoint, path, e, cancellationToken);
        }
        finally
        {
            response?.Dispose();
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

    // The deployment gate before anything is sent, then the version gates once the version is known.
    private async ValueTask<Version> GatesAsync(Endpoint endpoint, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, typeof(TableauClient));
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

    private (Uri Uri, string Path, byte[]? Body) Prepare(Endpoint endpoint, Version version, TableauRequest request)
    {
        var uri = RequestUri.Build(state.ServerUrl, endpoint, version, request.PathValues, request.Query);
        var body = request.Body is null ? null : JsonSerializer.SerializeToUtf8Bytes(request.Body, TableauJsonContext.Default.TsRequest);
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

    // What the caller receives for a failure: the caller's cancellation as itself, a timeout or a failed connection as
    // TableauException, and the pipeline's own exceptions as they are, each logged at Debug.
    private Exception Failure(Endpoint endpoint, string path, Exception exception, CancellationToken cancellationToken)
    {
        switch (exception)
        {
            case OperationCanceledException when cancellationToken.IsCancellationRequested:
                // Thrown again with the caller's token, which the HttpClient only saw linked to the transfer's.
                return Cancelled(endpoint, path, exception, cancellationToken);
            case TimeoutRejectedException rejected:
                // The cancellation the attempt timeout caused, as other timeouts give theirs: Polly stays inside the client.
                return Failed(endpoint, path, new TableauException(TableauErrorKind.Timeout,
                    $"{endpoint.Name} ({endpoint.Method} {path}): its last attempt took longer than its RequestTimeout allows.",
                    rejected.InnerException ?? rejected)
                { Method = endpoint.Method, RequestPath = path });
            case OperationCanceledException:
                // The transfer timeout, or the HttpClient's own: either way not the caller's cancellation.
                var limit = endpoint.Transfer == TransferKind.None ? "the HttpClient's Timeout" : "its TransferTimeout, or the HttpClient's Timeout,";
                return Failed(endpoint, path, new TableauException(TableauErrorKind.Timeout,
                    $"{endpoint.Name} ({endpoint.Method} {path}) took longer than {limit} allows.", exception)
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

    private async Task<HttpResponseMessage> SendFollowingRedirectsAsync(Endpoint endpoint, Uri uri, string path, byte[]? body,
        string? token, CancellationToken cancellationToken)
    {
        var method = endpoint.Method;
        for (var redirects = 0; ; redirects++)
        {
            var response = await SendOnceAsync(endpoint, method, uri, path, body, token, cancellationToken).ConfigureAwait(false);
            try
            {
                // A caller's HttpClient may follow redirects itself; then the answer came from wherever it went, and the
                // request, its headers and its body went there too, which this can only report.
                if (response.RequestMessage?.RequestUri is { } answered && !RequestUri.SameOrigin(answered, uri))
                {
                    throw new TableauException(TableauErrorKind.Redirect,
                        $"{endpoint.Name} ({endpoint.Method} {path}) was redirected by the HttpClient's own handler, which sent "
                        + $"the request, its headers and its body on to {RequestUri.Origin(answered)}. Give the client an "
                        + "HttpClient whose handler follows no redirect (AllowAutoRedirect = false).")
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
        catch (Exception e) when (e is not (OperationCanceledException or HttpRequestException or IOException or TimeoutRejectedException))
        {
            // A handler of the caller's threw something of its own: the request got no answer, and the call fails as one
            // that got none, with the handler's exception as the cause.
            throw new HttpRequestException($"A handler failed the request: {e.Message}", e);
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
            ResponseBody = kept,
        };

    private async Task<TableauException> ErrorAsync(Endpoint endpoint, HttpResponseMessage response, string path,
        CancellationToken cancellationToken)
    {
        // The chain kept at most the body's first 64 KB; this reads no more, whatever sent it.
        var (bytes, length) = await ReadSomeAsync(response.Content, TableauAttemptHandler.ErrorBodyRead, cancellationToken)
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
        return new TableauException(kind, message.Append('.').ToString())
        {
            StatusCode = status,
            ErrorCode = code,
            Summary = summary,
            Detail = detail,
            Method = endpoint.Method,
            RequestPath = path,
            RequestId = response.Headers.TryGetValues("X-Request-Id", out var ids) ? ids.FirstOrDefault() : null,
            RetryAfter = retryAfter,
            ResponseBody = length == 0 ? null : Kept(bytes, length),
        };
    }

    // Tableau's error JSON: {"error":{"code":"404004","summary":...,"detail":...}}, the code a string or a number; the
    // resource-versioned endpoints under api/- answer {"httpErrorCode":404,"message":...}.
    private static (string? Code, string? Summary, string? Detail) TableauError(byte[] bytes, int length)
    {
        try
        {
            using var document = JsonDocument.Parse(bytes.AsMemory(0, length));
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return default;
            if (root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object)
                return (Text(error, "code"), Text(error, "summary"), Text(error, "detail"));
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

    // The first 4 KB of a body as text, cut where a character starts, so no half of one is kept.
    private static string Kept(byte[] bytes, int length)
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
                $"{endpoint.Name} ({endpoint.Method} {path}) took longer than its TransferTimeout allows, after {read} bytes of its "
                + "file.", exception)
            { Method = endpoint.Method, RequestPath = path });
        }
        if (exception is IOException or HttpRequestException)
        {
            return pipeline.Failed(endpoint, path, new TableauException(TableauErrorKind.Http,
                $"{endpoint.Name} ({endpoint.Method} {path}) broke off after {read} bytes of its file: {exception.Message}", exception)
            { Method = endpoint.Method, RequestPath = path });
        }
        return exception;
    }
}
