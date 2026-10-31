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
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;
using Polly.Timeout;

namespace Tableau.Api.Pipeline;

/// <summary>
/// The retries and the attempt timeout every request of a client goes through: one Polly pipeline per client, built
/// from its read-only options, and run by Microsoft.Extensions.Http.Resilience's handler. A client from DI, one from
/// <c>TableauClient.Create</c> and one over a caller's HttpClient all send through <see cref="Chain"/>'s handlers, in
/// that order: the retries, then <see cref="TableauAttemptHandler"/>, which reads the answer within its attempt.
/// </summary>
/// <remarks>
/// The pipeline is built here rather than with <c>AddResilienceHandler</c>, which would register services and
/// telemetry in the caller's container and log every retry as a warning; the client logs its retries at Debug.
/// </remarks>
internal static partial class TableauResilience
{
    /// <summary>The key a request carries its endpoint's descriptor under, which the retry rule and the timeout read.</summary>
    public static readonly HttpRequestOptionsKey<Endpoint> EndpointKey = new("Tableau.Api.Endpoint");

    /// <summary>The middle of the first back-off, when the server names no wait; each later one doubles, with jitter.</summary>
    public static readonly TimeSpan FirstBackOff = TimeSpan.FromSeconds(2);

    /// <summary>The longest back-off, however many retries <see cref="TableauClientOptions.MaxRetries"/> allows.</summary>
    public static readonly TimeSpan LongestBackOff = TimeSpan.FromSeconds(30);

    /// <summary>
    /// The client's pipeline: retries (when <see cref="TableauClientOptions.MaxRetries"/> allows any) around an attempt
    /// timeout, on the state's time provider.
    /// </summary>
    public static ResiliencePipeline<HttpResponseMessage> Build(TableauClientState state)
    {
        var options = state.Options;
        var builder = new ResiliencePipelineBuilder<HttpResponseMessage> { TimeProvider = state.TimeProvider };
        if (options.MaxRetries > 0)
        {
            builder.AddRetry(new RetryStrategyOptions<HttpResponseMessage>
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
                    if (state.Logger.IsEnabled(LogLevel.Debug) && args.Context.GetRequestMessage() is { RequestUri: { } uri } request)
                    {
                        var path = RequestUri.Redacted(uri);
                        var reason = Reason(args.Outcome);
                        TableauLog.Retrying(state.Logger, state.DisplayName, request.Method, path, reason, args.AttemptNumber + 1,
                            options.MaxRetries, args.RetryDelay.TotalMilliseconds);
                    }
                    return default;
                },
            });
        }
        builder.AddTimeout(new TimeoutStrategyOptions
        {
            TimeoutGenerator = args => ValueTask.FromResult(AttemptTimeout(EndpointOf(args.Context.GetRequestMessage()), options)),
        });
        return builder.Build();
    }

    /// <summary>The outer of the library's two handlers: the retries and the attempt timeout, with the client's pipeline.</summary>
    public static DelegatingHandler RetryHandler(TableauClientState state) => new ResilienceHandler(state.Resilience);

    /// <summary>The inner of the two: <see cref="TableauAttemptHandler"/>, within each attempt.</summary>
    public static DelegatingHandler AttemptHandler() => new TableauAttemptHandler();

    /// <summary>The same handlers over <paramref name="inner"/>, for a client that builds its own chain.</summary>
    public static HttpMessageHandler Chain(TableauClientState state, HttpMessageHandler inner)
    {
        var attempt = AttemptHandler();
        attempt.InnerHandler = inner;
        var retry = RetryHandler(state);
        retry.InnerHandler = attempt;
        return retry;
    }

    /// <summary>
    /// How long one attempt may take: <see cref="TableauClientOptions.RequestTimeout"/> for a call that moves no file,
    /// and no limit for a transfer, which <see cref="TableauClientOptions.TransferTimeout"/> bounds as a whole.
    /// </summary>
    public static TimeSpan AttemptTimeout(Endpoint? endpoint, TableauClientOptions options) =>
        endpoint is null || endpoint.Transfer == TransferKind.None ? options.RequestTimeout : Timeout.InfiniteTimeSpan;

    /// <summary>
    /// How long the server asked the client to wait before trying again: its <c>Retry-After</c> header, as a delay or
    /// a date, or else, on a 429, the wait its body names. Null when it named none.
    /// </summary>
    public static async ValueTask<TimeSpan?> ServerWaitAsync(HttpResponseMessage response, TimeProvider time,
        CancellationToken cancellationToken)
    {
        if (RetryAfter(response.Headers.RetryAfter, time) is { } header) return header;
        if (response.StatusCode != HttpStatusCode.TooManyRequests) return null;
        // The attempt handler kept at most the error body's first 64 KB, in memory.
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

    // Retried: a call that is safe to send twice, whose attempt failed for a reason that may pass, unless the server
    // asked for a longer wait than the options allow.
    private static async ValueTask<bool> ShouldRetryAsync(Outcome<HttpResponseMessage> outcome, ResilienceContext context,
        TableauClientState state)
    {
        if (EndpointOf(context.GetRequestMessage()) is not { Idempotent: true }) return false;
        switch (outcome)
        {
            case { Exception: HttpRequestException { HttpRequestError: var error } }:
                return !IsPermanent(error);
            case { Exception: IOException or TimeoutRejectedException }:
                return true;
            case { Result: { } response } when HttpClientResiliencePredicates.IsTransient(outcome):
                var wait = await ServerWaitAsync(response, state.TimeProvider, context.CancellationToken).ConfigureAwait(false);
                return wait is null || wait <= state.Options.MaxRetryAfter;
            default:
                return false;
        }
    }

    // Failures that sending again won't mend: a certificate or a handshake refused, credentials a proxy refused, a
    // protocol the server doesn't speak, a limit of the handler's configuration.
    private static bool IsPermanent(HttpRequestError error) => error is HttpRequestError.SecureConnectionError
        or HttpRequestError.UserAuthenticationError or HttpRequestError.VersionNegotiationError
        or HttpRequestError.ExtendedConnectNotSupported or HttpRequestError.ConfigurationLimitExceeded;

    // An outcome holds an answer or an exception.
    private static string Reason(Outcome<HttpResponseMessage> outcome) => outcome switch
    {
        { Result: { } response } => ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture),
        { Exception: TimeoutRejectedException } => "no answer within RequestTimeout",
        _ => outcome.Exception!.GetType().Name,
    };
}
