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
/// The one path every REST call of the 2.0 client takes: the gates, the request URI, the headers, redirects, timeouts,
/// errors and the answer's JSON. It sends through the client's HttpClient and reads the named client's state.
/// </summary>
internal sealed class TableauPipeline(HttpClient http, TableauClientState state)
{
    /// <summary>The most redirects one call follows.</summary>
    public const int MostRedirects = 5;

    /// <summary>How much of a body an error keeps.</summary>
    public const int BodyKept = 4096;

    // An error body is read up to this much, which Tableau's error JSON is far below, so a proxy's page can't fill memory.
    private const int ErrorBodyRead = 64 * 1024;

    private static readonly MediaTypeWithQualityHeaderValue _acceptJson = new("application/json");
    private int _disposed;

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
        return await SendAtAsync(endpoint, version, request, select, element, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The same at the version given, without the gates: what version negotiation asks Server Info with.</summary>
    public async Task<T> SendAtAsync<T>(Endpoint endpoint, Version version, TableauRequest request, Func<TsResponse, T?> select,
        string element, CancellationToken cancellationToken)
        where T : class
    {
        var uri = RequestUri.Build(state.ServerUrl, endpoint, version, request.PathValues, request.Query);
        var path = RequestUri.Redacted(uri);
        var body = request.Body is null ? null : JsonSerializer.SerializeToUtf8Bytes(request.Body, TableauJsonContext.Default.TsRequest);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(endpoint.Transfer == TransferKind.None ? state.Options.RequestTimeout : state.Options.TransferTimeout);
        var started = Stopwatch.GetTimestamp();
        try
        {
            using var response = await SendFollowingRedirectsAsync(endpoint, uri, path, body, request.Token, timeout.Token)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                throw await ErrorAsync(endpoint, response, path, timeout.Token).ConfigureAwait(false);
            var result = await ReadAsync(endpoint, response, path, select, element, timeout.Token).ConfigureAwait(false);
            var elapsed = Stopwatch.GetElapsedTime(started);
            TableauLog.Answered(state.Logger, state.DisplayName, endpoint.Method, path, (int)response.StatusCode, elapsed.TotalMilliseconds);
            return result;
        }
        catch (OperationCanceledException e) when (cancellationToken.IsCancellationRequested)
        {
            // Thrown again with the caller's token, which the HttpClient only saw linked to the timeout's.
            TableauLog.Cancelled(state.Logger, state.DisplayName, endpoint.Method, path);
            throw new OperationCanceledException(e.Message, e, cancellationToken);
        }
        catch (OperationCanceledException e)
        {
            // The client's timeout, or the HttpClient's own: either way not the caller's cancellation.
            var limit = endpoint.Transfer == TransferKind.None ? "RequestTimeout" : "TransferTimeout";
            throw Failed(endpoint, path, new TableauException(TableauErrorKind.Timeout,
                $"{endpoint.Name} ({endpoint.Method} {path}) took longer than its {limit}, or the HttpClient's Timeout, allows.", e)
            { Method = endpoint.Method, RequestPath = path });
        }
        catch (Exception e) when (e is HttpRequestException or IOException)
        {
            throw Failed(endpoint, path, new TableauException(TableauErrorKind.Http,
                $"{endpoint.Name} ({endpoint.Method} {path}) got no answer: {e.Message}", e)
            { Method = endpoint.Method, RequestPath = path, StatusCode = (e as HttpRequestException)?.StatusCode });
        }
        catch (TableauException e)
        {
            throw Failed(endpoint, path, e);
        }
    }

    private TableauException Failed(Endpoint endpoint, string? path, TableauException exception)
    {
        TableauLog.Failed(state.Logger, state.DisplayName, endpoint.Method, path ?? endpoint.Template, exception.Kind, exception);
        return exception;
    }

    private async Task<HttpResponseMessage> SendFollowingRedirectsAsync(Endpoint endpoint, Uri uri, string path, byte[]? body,
        string? token, CancellationToken cancellationToken)
    {
        var method = endpoint.Method;
        for (var redirects = 0; ; redirects++)
        {
            var response = await SendOnceAsync(method, uri, path, body, token, cancellationToken).ConfigureAwait(false);
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

    private async Task<HttpResponseMessage> SendOnceAsync(HttpMethod method, Uri uri, string path, byte[]? body, string? token,
        CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(method, uri);
        message.Headers.Accept.Add(_acceptJson);
        // On the request, not the client's default headers, so the token goes with this request only.
        if (token is not null)
            message.Headers.TryAddWithoutValidation("X-Tableau-Auth", token);
        if (body is not null)
            message.Content = new ByteArrayContent(body) { Headers = { ContentType = new MediaTypeHeaderValue("application/json") } };
        TableauLog.Sending(state.Logger, state.DisplayName, method, path);
        return await http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
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

    private static async Task<TableauException> ErrorAsync(Endpoint endpoint, HttpResponseMessage response, string path,
        CancellationToken cancellationToken)
    {
        var (bytes, length) = await ReadSomeAsync(response.Content, ErrorBodyRead, cancellationToken).ConfigureAwait(false);
        var (code, summary, detail) = TableauError(bytes, length);
        var status = response.StatusCode;
        var kind = status switch
        {
            HttpStatusCode.Unauthorized => TableauErrorKind.Authentication,
            HttpStatusCode.NotFound => TableauErrorKind.NotFound,
            HttpStatusCode.TooManyRequests => TableauErrorKind.RateLimited,
            _ => TableauErrorKind.Http,
        };
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
            RetryAfter = RetryAfter(response.Headers.RetryAfter),
            ResponseBody = length == 0 ? null : Kept(bytes, length),
        };
    }

    private static TimeSpan? RetryAfter(RetryConditionHeaderValue? header) => header switch
    {
        { Delta: { } delta } => delta,
        { Date: { } date } => date > DateTimeOffset.UtcNow ? date - DateTimeOffset.UtcNow : TimeSpan.Zero,
        _ => null,
    };

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
