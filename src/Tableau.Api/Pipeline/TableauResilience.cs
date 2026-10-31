using System;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;

namespace Tableau.Api.Pipeline;

/// <summary>
/// The retries every request of a client goes through: one Polly pipeline per client, built from its read-only options
/// and run by <see cref="TableauResilienceHandler"/>, which also times each attempt. A client from DI, one from
/// <c>TableauClient.Create</c> and one over a caller's HttpClient all send through that handler, closest to the
/// connection.
/// </summary>
/// <remarks>
/// Polly.Core alone: a handler of the library's own runs it, so no package of Microsoft's resilience libraries, which
/// register services and telemetry and change how an app's logging source generator runs, reaches a caller's app.
/// </remarks>
internal static partial class TableauResilience
{
    /// <summary>The key a request carries its endpoint's descriptor under, which the retry rule and the timeout read.</summary>
    public static readonly HttpRequestOptionsKey<Endpoint> EndpointKey = new("Tableau.Api.Endpoint");

    /// <summary>The key the pipeline's context carries the request under, for the retry rule and its log line.</summary>
    public static readonly ResiliencePropertyKey<HttpRequestMessage?> RequestKey = new("Tableau.Api.Request");

    /// <summary>The middle of the first back-off, when the server names no wait; each later one doubles, with jitter.</summary>
    public static readonly TimeSpan FirstBackOff = TimeSpan.FromSeconds(2);

    /// <summary>The longest back-off, however many retries <see cref="TableauClientOptions.MaxRetries"/> allows.</summary>
    public static readonly TimeSpan LongestBackOff = TimeSpan.FromSeconds(30);

    /// <summary>The client's pipeline: the retries, when <see cref="TableauClientOptions.MaxRetries"/> allows any, on its clock.</summary>
    public static ResiliencePipeline<HttpResponseMessage> Build(TableauClientState state)
    {
        var options = state.Options;
        if (options.MaxRetries == 0) return ResiliencePipeline<HttpResponseMessage>.Empty;
        return new ResiliencePipelineBuilder<HttpResponseMessage> { TimeProvider = state.TimeProvider }
            .AddRetry(new RetryStrategyOptions<HttpResponseMessage>
            {
                MaxRetryAttempts = options.MaxRetries,
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                Delay = FirstBackOff,
                MaxDelay = LongestBackOff,
                ShouldHandle = args => ShouldRetryAsync(args.Outcome, args.Context, state),
                DelayGenerator = args => args.Outcome.Result is { } response
                    ? ServerWaitAsync(response, state.TimeProvider, args.Context.CancellationToken)
                    : ValueTask.FromResult<TimeSpan?>(null),
                OnRetry = args =>
                {
                    if (state.Logger.IsEnabled(LogLevel.Debug) && RequestOf(args.Context) is { RequestUri: { } uri } request)
                    {
                        var path = RequestUri.Redacted(uri);
                        var reason = Reason(args.Outcome);
                        TableauLog.Retrying(state.Logger, state.DisplayName, request.Method, path, reason, args.AttemptNumber + 1,
                            options.MaxRetries, args.RetryDelay.TotalMilliseconds);
                    }
                    return default;
                },
            })
            .Build();
    }

    /// <summary>The library's handler, for a named HttpClient's chain.</summary>
    public static DelegatingHandler Handler(TableauClientState state) => new TableauResilienceHandler(state);

    /// <summary>The same handler over <paramref name="inner"/>, for a client that builds its own chain.</summary>
    public static HttpMessageHandler Chain(TableauClientState state, HttpMessageHandler inner)
    {
        var handler = Handler(state);
        handler.InnerHandler = inner;
        return handler;
    }

    /// <summary>
    /// How long one attempt may take: <see cref="TableauClientOptions.RequestTimeout"/> for a call that moves no file,
    /// and no limit for a transfer, which <see cref="TableauClientOptions.TransferTimeout"/> bounds as a whole.
    /// </summary>
    public static TimeSpan AttemptTimeout(Endpoint? endpoint, TableauClientOptions options) =>
        endpoint is null || endpoint.Transfer == TransferKind.None ? options.RequestTimeout : Timeout.InfiniteTimeSpan;

    /// <summary>
    /// How long the server asked the client to wait before trying again: a 429's or a 503's <c>Retry-After</c> header, as
    /// a delay or a date, or else the wait a 429's body names. Null when it named none, or for another status, which
    /// the back-off times.
    /// </summary>
    public static async ValueTask<TimeSpan?> ServerWaitAsync(HttpResponseMessage response, TimeProvider time,
        CancellationToken cancellationToken)
    {
        if (response.StatusCode is not (HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable)) return null;
        if (RetryAfter(response.Headers.RetryAfter, time) is { } header) return header;
        if (response.StatusCode != HttpStatusCode.TooManyRequests) return null;
        // The handler kept at most the error body's first 64 KB, in memory.
        var body = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        return WaitInBody(Encoding.UTF8.GetString(body));
    }

    /// <summary>A <c>Retry-After</c> header's wait: its delay, or the time until its date, never below zero.</summary>
    public static TimeSpan? RetryAfter(RetryConditionHeaderValue? header, TimeProvider time) => header switch
    {
        { Delta: { } delta } => delta,
        { Date: { } date } => date > time.GetUtcNow() ? date - time.GetUtcNow() : TimeSpan.Zero,
        _ => null,
    };

    /// <summary>
    /// The wait a 429's body names, such as "Please retry after 29 minutes" in Tableau Cloud's error detail, or null.
    /// Tableau documents neither the header nor this wording, so the text is read loosely: "retry after", a whole
    /// number, and seconds, minutes or hours, in any case, anywhere in the body.
    /// </summary>
    public static TimeSpan? WaitInBody(string? body)
    {
        if (body is null || WaitPattern().Match(body) is not { Success: true } match) return null;
        var amount = int.Parse(match.Groups["amount"].ValueSpan, NumberStyles.None, CultureInfo.InvariantCulture);
        return char.ToLowerInvariant(match.Groups["unit"].ValueSpan[0]) switch
        {
            's' => TimeSpan.FromSeconds(amount),
            'm' => TimeSpan.FromMinutes(amount),
            _ => TimeSpan.FromHours(amount),
        };
    }

    // Six digits at most: a million hours still fits a TimeSpan.
    [GeneratedRegex(@"retry\s+after\s+(?<amount>[0-9]{1,6})\s*(?<unit>seconds?|secs?|minutes?|mins?|hours?|hrs?)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture)]
    private static partial Regex WaitPattern();

    /// <summary>The descriptor a request carries, or null for one the pipeline didn't send.</summary>
    public static Endpoint? EndpointOf(HttpRequestMessage? request) =>
        request is not null && request.Options.TryGetValue(EndpointKey, out var endpoint) ? endpoint : null;

    /// <summary>
    /// Whether an HTTP status may pass if the call is sent again: 408, 429, or a 5xx, as the retry rules of
    /// Microsoft.Extensions.Http.Resilience take them.
    /// </summary>
    public static bool IsTransient(HttpStatusCode status) =>
        status is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests || (int)status >= 500;

    /// <summary>
    /// Failures that sending again won't mend: a certificate or a handshake refused, credentials a server or a proxy
    /// refused, a protocol the server doesn't speak, a limit of the handler's configuration.
    /// </summary>
    public static bool IsPermanent(HttpRequestException exception) => exception.HttpRequestError switch
    {
        HttpRequestError.SecureConnectionError or HttpRequestError.UserAuthenticationError or HttpRequestError.VersionNegotiationError
            or HttpRequestError.ExtendedConnectNotSupported or HttpRequestError.ConfigurationLimitExceeded => true,
        HttpRequestError.ProxyTunnelError => exception.StatusCode == HttpStatusCode.ProxyAuthenticationRequired,
        _ => false,
    };

    private static HttpRequestMessage? RequestOf(ResilienceContext context) => context.Properties.GetValue(RequestKey, null);

    // Retried: a call that is safe to send twice, whose attempt failed for a reason that may pass, unless the server
    // asked for a longer wait than the options allow. A handler's own exception, a cancellation or a timeout of its
    // own among them, isn't retried: the library can't tell whether it may pass.
    private static async ValueTask<bool> ShouldRetryAsync(Outcome<HttpResponseMessage> outcome, ResilienceContext context,
        TableauClientState state)
    {
        if (EndpointOf(RequestOf(context)) is not { Idempotent: true }) return false;
        switch (outcome)
        {
            case { Exception: HttpRequestException failed }:
                return !IsPermanent(failed);
            case { Exception: IOException or TableauAttemptTimeoutException }:
                return true;
            case { Result: { } response } when IsTransient(response.StatusCode):
                var wait = await ServerWaitAsync(response, state.TimeProvider, context.CancellationToken).ConfigureAwait(false);
                return wait is null || wait <= state.Options.MaxRetryAfter;
            default:
                return false;
        }
    }

    // An outcome holds an answer or an exception.
    private static string Reason(Outcome<HttpResponseMessage> outcome) => outcome switch
    {
        { Result: { } response } => ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture),
        { Exception: TableauAttemptTimeoutException } => "no answer within RequestTimeout",
        _ => outcome.Exception!.GetType().Name,
    };
}
