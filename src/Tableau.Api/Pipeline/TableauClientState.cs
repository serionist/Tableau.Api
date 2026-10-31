using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Tableau.Api.Rest.Models;

namespace Tableau.Api.Pipeline;

/// <summary>
/// What a named client keeps for as long as it lives, however many <see cref="TableauClient"/> objects a container
/// makes over it: its options, read-only from here; its deployment; the REST API version it negotiated. Under DI it is
/// a singleton keyed by the client's name; a client built without DI owns its own.
/// </summary>
internal sealed class TableauClientState : IDisposable
{
    private readonly SemaphoreSlim _negotiating = new(1, 1);
    private Version? _negotiated;
    private string? _productVersion;
    private int _warnedBelowFloor;

    public TableauClientState(string name, TableauClientOptions options, ILoggerFactory? loggerFactory)
    {
        options.MakeReadOnly();
        Name = name;
        DisplayName = name.Length == 0 ? "(default)" : name;
        Options = options;
        ServerUrl = options.ServerUrl ?? throw new ArgumentException("ServerUrl is not set.", nameof(options));
        Logger = (loggerFactory ?? NullLoggerFactory.Instance).CreateLogger<TableauClient>();
        Deployment = options.Deployment switch
        {
            TableauDeployment.Auto => IsTableauCloud(ServerUrl) ? TableauDeployment.Cloud : TableauDeployment.Server,
            var deployment => deployment,
        };
        if (options.DangerousAcceptAnyServerCertificate)
            TableauLog.AcceptsAnyCertificate(Logger, DisplayName, ServerUrl);
        if (ServerUrl.Scheme == Uri.UriSchemeHttp)
            TableauLog.InsecureHttp(Logger, DisplayName, ServerUrl);
    }

    /// <summary>The client's name: the key it is registered under, <c>""</c> for the default client.</summary>
    public string Name { get; }

    /// <summary>The name as log lines give it.</summary>
    public string DisplayName { get; }

    public TableauClientOptions Options { get; }

    public Uri ServerUrl { get; }

    public ILogger Logger { get; }

    /// <summary>Tableau Server or Tableau Cloud, never <see cref="TableauDeployment.Auto"/>.</summary>
    public TableauDeployment Deployment { get; }

    /// <summary>
    /// Tableau Cloud's hosts: <c>online.tableau.com</c>, where a sign-in starts, and its pods under it, such as
    /// <c>10ax.online.tableau.com</c> and <c>prod-useast-a.online.tableau.com</c>.
    /// </summary>
    public static bool IsTableauCloud(Uri serverUrl) =>
        serverUrl.IdnHost.Equals("online.tableau.com", StringComparison.OrdinalIgnoreCase)
        || serverUrl.IdnHost.EndsWith(".online.tableau.com", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The REST API version calls use: the pinned one, or the server's, read from Server Info once and capped at the
    /// newest the client knows. Below the floor it throws <see cref="TableauErrorKind.UnsupportedApiVersion"/> on every
    /// call, unless the options allow it, and then warns once.
    /// </summary>
    public async ValueTask<Version> ApiVersionAsync(TableauPipeline pipeline, CancellationToken cancellationToken)
    {
        if (Options.ApiVersion is { } pinned) return Supported(ApiVersions.MajorMinor(pinned), null);
        if (Volatile.Read(ref _negotiated) is { } known) return Supported(known, _productVersion);

        await _negotiating.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_negotiated is null)
            {
                var info = await ServerInfoAsync(pipeline, cancellationToken).ConfigureAwait(false);
                var offered = ApiVersions.Parse(info.RestApiVersion) ?? throw new TableauException(TableauErrorKind.InvalidResponse,
                    $"Server Info gave no REST API version the client can read: '{info.RestApiVersion}'.");
                var used = offered > ApiVersions.Newest ? ApiVersions.Newest : offered;
                TableauLog.Negotiated(Logger, DisplayName, offered, used);
                _productVersion = info.ProductVersion?.Value;
                Volatile.Write(ref _negotiated, used);
            }
        }
        finally
        {
            _negotiating.Release();
        }
        return Supported(_negotiated!, _productVersion);
    }

    public void Dispose() => _negotiating.Dispose();

    // Asked at the floor, which every supported server answers; a server too old for it answers 404, and is asked
    // again at the version Server Info came in, so the error can name its version.
    private static async Task<ServerInfo> ServerInfoAsync(TableauPipeline pipeline, CancellationToken cancellationToken)
    {
        try
        {
            return await pipeline.SendAtAsync(Endpoints.ServerInfo, ApiVersions.Floor, default, static body => body.ServerInfo,
                "serverInfo", cancellationToken).ConfigureAwait(false);
        }
        catch (TableauException e) when (e.Kind == TableauErrorKind.NotFound)
        {
            return await pipeline.SendAtAsync(Endpoints.ServerInfo, ApiVersions.ServerInfoOldest, default,
                static body => body.ServerInfo, "serverInfo", cancellationToken).ConfigureAwait(false);
        }
    }

    private Version Supported(Version version, string? productVersion)
    {
        if (version >= ApiVersions.Floor) return version;
        if (!Options.AllowUnsupportedServerVersion)
        {
            var server = productVersion is null ? "" : $" (Tableau {productVersion})";
            throw new TableauException(TableauErrorKind.UnsupportedApiVersion,
                $"REST API {ApiVersions.Format(version)}{server} is below {ApiVersions.Format(ApiVersions.Floor)} (Tableau Server "
                + "2024.2), the oldest this client supports. Upgrade the server, or set AllowUnsupportedServerVersion to call it "
                + "anyway, untested.");
        }
        if (Interlocked.Exchange(ref _warnedBelowFloor, 1) == 0)
            TableauLog.UnsupportedVersion(Logger, DisplayName, version, ApiVersions.Floor);
        return version;
    }
}
