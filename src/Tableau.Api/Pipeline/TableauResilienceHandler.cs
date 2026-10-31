using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Polly;

namespace Tableau.Api.Pipeline;

/// <summary>
/// The library's handler, closest to the connection in every client: it runs the client's Polly pipeline, the retries,
/// around each attempt, and times each attempt itself. Within an attempt it reads the answer, so the attempt timeout
/// covers the answer as well as its headers, and an answer that breaks off is retried like a connection that failed: a
/// success is read whole, but a download's, which streams to the caller; an error, its first 64 KB, which is all the
/// pipeline keeps, so a proxy's page can't fill memory; a redirect, nothing.
/// </summary>
/// <param name="state">The client's state: its options, its clock and its pipeline.</param>
internal sealed class TableauResilienceHandler(TableauClientState state) : DelegatingHandler
{
    /// <summary>How much of an error body is read: Tableau's error JSON is far below it.</summary>
    public const int ErrorBodyRead = 64 * 1024;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var context = ResilienceContextPool.Shared.Get(cancellationToken);
        context.Properties.Set(TableauResilience.RequestKey, request);
        try
        {
            var outcome = await state.Resilience.ExecuteOutcomeAsync(
                static (context, attempt) => attempt.Handler.AttemptAsync(attempt.Request, context.CancellationToken),
                context, (Handler: this, Request: request)).ConfigureAwait(false);
            outcome.ThrowIfException();
            return outcome.Result!;
        }
        finally
        {
            ResilienceContextPool.Shared.Return(context);
        }
    }

    // One attempt, under its own timeout: what it sends, and what it reads of the answer. A failure comes back as the
    // outcome, for the retries to judge; the attempt's own timeout as TableauAttemptTimeoutException, so neither the
    // retries nor the pipeline take a handler's cancellation, or a timeout of its own, for it.
    private async ValueTask<Outcome<HttpResponseMessage>> AttemptAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var endpoint = TableauResilience.EndpointOf(request);
        var timeout = TableauResilience.AttemptTimeout(endpoint, state.Options);
        using var timer = timeout == Timeout.InfiniteTimeSpan ? null : new CancellationTokenSource(timeout, state.TimeProvider);
        using var linked = timer is null ? null : CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timer.Token);
        var token = linked?.Token ?? cancellationToken;
        HttpResponseMessage? response = null;
        try
        {
            response = await base.SendAsync(request, token).ConfigureAwait(false);
            await ReadAsync(response, endpoint, token).ConfigureAwait(false);
            return Outcome.FromResult(response);
        }
        catch (Exception e)
        {
            response?.Dispose();
            return Outcome.FromException<HttpResponseMessage>(
                e is OperationCanceledException && timer is { IsCancellationRequested: true } && !cancellationToken.IsCancellationRequested
                    ? new TableauAttemptTimeoutException(timeout, e)
                    : e);
        }
    }

    private static async Task ReadAsync(HttpResponseMessage response, Endpoint? endpoint, CancellationToken cancellationToken)
    {
        var status = (int)response.StatusCode;
        if (response.IsSuccessStatusCode)
        {
            if (endpoint?.Transfer != TransferKind.Download)
                await response.Content.LoadIntoBufferAsync(cancellationToken).ConfigureAwait(false);
        }
        else if (status is < 300 or > 399)
        {
            await KeepStartAsync(response, cancellationToken).ConfigureAwait(false);
        }
    }

    // Swaps the error body for its first 64 KB, held in memory, with the original's headers but its length.
    private static async Task KeepStartAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var original = response.Content;
        var buffer = new byte[ErrorBodyRead];
        var length = 0;
        var stream = await original.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using (stream.ConfigureAwait(false))
        {
            int read;
            while (length < buffer.Length
                   && (read = await stream.ReadAsync(buffer.AsMemory(length), cancellationToken).ConfigureAwait(false)) > 0)
                length += read;
        }
        var kept = new ByteArrayContent(buffer, 0, length);
        foreach (var header in original.Headers)
        {
            if (!header.Key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
                kept.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }
        response.Content = kept;
        original.Dispose();
    }
}

/// <summary>
/// An attempt took longer than <see cref="TableauClientOptions.RequestTimeout"/>: the library's own timeout, told apart
/// from a cancellation or a timeout of any handler's. The retries take it as a failure that may pass; the pipeline makes
/// it <see cref="TableauErrorKind.Timeout"/>, with the cancellation as its cause.
/// </summary>
/// <param name="timeout">The attempt's timeout.</param>
/// <param name="cancellation">The cancellation the timeout caused.</param>
internal sealed class TableauAttemptTimeoutException(TimeSpan timeout, Exception cancellation)
    : Exception($"An attempt took longer than its RequestTimeout, {timeout}.", cancellation);
