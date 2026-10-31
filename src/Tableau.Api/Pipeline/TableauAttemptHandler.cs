using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Tableau.Api.Pipeline;

/// <summary>
/// The innermost of the library's handlers, inside the retries and the attempt timeout: it reads the answer within its
/// attempt, so the timeout covers the answer as well as its headers, and an answer that breaks off is retried like a
/// connection that failed. A success is read whole, but a download's, which streams to the caller; an error, its first
/// 64 KB, which is all the pipeline keeps, so a proxy's page can't fill memory; a redirect, nothing.
/// </summary>
internal sealed class TableauAttemptHandler : DelegatingHandler
{
    /// <summary>How much of an error body is read: Tableau's error JSON is far below it.</summary>
    public const int ErrorBodyRead = 64 * 1024;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        try
        {
            var status = (int)response.StatusCode;
            if (response.IsSuccessStatusCode)
            {
                if (TableauResilience.EndpointOf(request)?.Transfer != TransferKind.Download)
                    await response.Content.LoadIntoBufferAsync(cancellationToken).ConfigureAwait(false);
            }
            else if (status is < 300 or > 399)
            {
                await KeepStartAsync(response, cancellationToken).ConfigureAwait(false);
            }
            return response;
        }
        catch
        {
            response.Dispose();
            throw;
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
