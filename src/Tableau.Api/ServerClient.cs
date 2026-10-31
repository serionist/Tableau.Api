using System.Threading;
using System.Threading.Tasks;
using Tableau.Api.Pipeline;
using Tableau.Api.Rest.Models;

namespace Tableau.Api;

/// <summary>The server itself: <see cref="TableauClient.Server"/>.</summary>
public class ServerClient
{
    private readonly TableauPipeline? _pipeline;

    /// <summary>A sub-client with no connection, for a mock to derive from: its methods throw until overridden.</summary>
    protected ServerClient()
    {
    }

    internal ServerClient(TableauPipeline pipeline)
    {
        _pipeline = pipeline;
    }

    private TableauPipeline Pipeline => _pipeline ?? throw TableauClient.ForMocksOnly();

    /// <summary>
    /// Server Info: the Tableau version the server runs (<see cref="ServerInfo.ProductVersion"/>) and the newest REST API
    /// version it answers (<see cref="ServerInfo.RestApiVersion"/>). It needs no sign-in, and runs on Tableau Server and
    /// Tableau Cloud.
    /// </summary>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The server's versions.</returns>
    /// <exception cref="TableauException">The call failed; <see cref="TableauException.Kind"/> says how.</exception>
    public virtual Task<ServerInfo> GetInfoAsync(CancellationToken cancellationToken = default) =>
        Pipeline.SendAsync(Endpoints.ServerInfo, default, static body => body.ServerInfo, "serverInfo", cancellationToken);
}
