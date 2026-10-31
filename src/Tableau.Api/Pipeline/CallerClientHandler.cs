using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Tableau.Api.Pipeline;

/// <summary>
/// The end of the chain for a client over a caller's HttpClient: each attempt goes out through that HttpClient, which
/// the chain never disposes. An HttpClient refuses to send one request twice, so each attempt sends a copy: the same
/// method, address, version, headers and body. Not its options, which hold only the client's own descriptor of the call:
/// the caller's handlers have no use for it, and a copy that carries nothing of the client's leaves them nothing to
/// change.
/// </summary>
internal sealed class CallerClientHandler(HttpClient httpClient) : HttpMessageHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using var copy = Copy(request);
        try
        {
            return await httpClient.SendAsync(copy, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            // The body is the original's, which the next attempt sends again and the pipeline disposes with the original,
            // so disposing the copy leaves it alone. The answer keeps the copy as its RequestMessage, for its address.
            copy.Content = null;
        }
    }

    private static HttpRequestMessage Copy(HttpRequestMessage request)
    {
        var copy = new HttpRequestMessage(request.Method, request.RequestUri)
        {
            Version = request.Version,
            VersionPolicy = request.VersionPolicy,
            Content = request.Content,
        };
        foreach (var header in request.Headers)
            copy.Headers.TryAddWithoutValidation(header.Key, header.Value);
        return copy;
    }
}
