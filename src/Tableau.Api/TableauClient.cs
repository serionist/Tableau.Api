using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tableau.Api.Pipeline;

namespace Tableau.Api;

/// <summary>
/// A client for Tableau's REST API, on Tableau Server or Tableau Cloud, with one sub-client per area: <see cref="Server"/>
/// first, and the others as the 2.0 client takes them over from <see cref="TableauApiClient"/>.
/// </summary>
/// <remarks>
/// <para>Get one from DI, registered with <c>services.AddTableauClient(name, options => ...)</c>, or build one with
/// <see cref="Create"/>, or over an <see cref="HttpClient"/> of your own.</para>
/// <para>
/// On its first call it asks the server's REST API version through Server Info, unless
/// <see cref="TableauClientOptions.ApiVersion"/> names one, and uses it up to 3.29, the newest it knows. Below REST
/// API 3.23 (Tableau Server 2024.2) every call throws <see cref="TableauErrorKind.UnsupportedApiVersion"/>, unless
/// <see cref="TableauClientOptions.AllowUnsupportedServerVersion"/> is set. A call that doesn't exist at that version,
/// or on that deployment, throws before anything is sent.
/// </para>
/// <para>
/// Every call fails with <see cref="TableauException"/>, but for a refused argument (<see cref="ArgumentException"/>)
/// and the caller's own cancellation (<see cref="OperationCanceledException"/>). The client is safe to share between
/// threads. Its members are virtual, and the protected constructor makes one for a mock.
/// </para>
/// </remarks>
public class TableauClient : IAsyncDisposable
{
    private readonly TableauPipeline? _pipeline;
    private readonly ServerClient? _server;
    private readonly HttpClient? _ownedHttpClient;
    private readonly TableauClientState? _ownedState;
    private int _disposed;

    /// <summary>A client with no connection, for a mock to derive from: its members throw until overridden.</summary>
    protected TableauClient()
    {
    }

    /// <summary>
    /// A client that sends through <paramref name="httpClient"/>, which it never disposes: for a proxy, a handler of your
    /// own, or a test's fake.
    /// </summary>
    /// <remarks>
    /// The client can't change a handler it didn't build, so <paramref name="httpClient"/>'s handler should follow no
    /// redirect (<c>AllowAutoRedirect = false</c>) and keep no cookies (<c>UseCookies = false</c>): the client follows
    /// redirects itself, only to the same scheme, host and port, and throws <see cref="TableauErrorKind.Redirect"/> when
    /// it finds a handler went elsewhere, though by then the request has gone there. Certificates are the handler's to
    /// check, so <see cref="TableauClientOptions.DangerousAcceptAnyServerCertificate"/> is refused here. The HttpClient's
    /// own <see cref="HttpClient.Timeout"/> applies beside the options' timeouts.
    /// </remarks>
    /// <param name="httpClient">The HttpClient to send through.</param>
    /// <param name="options">The server and the rules; checked here, and read-only from here on.</param>
    /// <param name="loggerFactory">Where the client logs, or null for nowhere.</param>
    /// <exception cref="ArgumentException">The options are not valid, or ask for what this constructor can't do.</exception>
    public TableauClient(HttpClient httpClient, TableauClientOptions options, ILoggerFactory? loggerFactory = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);
        if (options.DangerousAcceptAnyServerCertificate)
        {
            throw new ArgumentException("DangerousAcceptAnyServerCertificate can't apply to an HttpClient the client didn't build: "
                + "set the certificate check on its handler.", nameof(options));
        }
        _ownedState = NewState(options, loggerFactory);
        _pipeline = new TableauPipeline(httpClient, _ownedState);
        _server = new ServerClient(_pipeline);
    }

    /// <summary>A client over a named client's state, which DI owns.</summary>
    internal TableauClient(HttpClient httpClient, TableauClientState state)
    {
        _pipeline = new TableauPipeline(httpClient, state);
        _server = new ServerClient(_pipeline);
    }

    /// <summary>A client that owns its HttpClient and its state, as <see cref="Create"/> builds it.</summary>
    private TableauClient(TableauClientState ownedState, HttpClient ownedHttpClient)
        : this(ownedHttpClient, ownedState)
    {
        _ownedHttpClient = ownedHttpClient;
        _ownedState = ownedState;
    }

    /// <summary>Server Info: the versions of the server and of its REST API.</summary>
    public virtual ServerClient Server => _server ?? throw ForMocksOnly();

    /// <summary>The pipeline every sub-client sends through.</summary>
    internal TableauPipeline Pipeline => _pipeline ?? throw ForMocksOnly();

    /// <summary>
    /// A client with an HttpClient of its own, over one connection pool that renews each connection every five
    /// minutes, so DNS changes reach it. It follows no redirect but the client's own, keeps no cookies, and validates
    /// the server's certificate, a revoked one refused, unless the options turn that off. Dispose it when done.
    /// </summary>
    /// <param name="options">The server and the rules; checked here, and read-only from here on.</param>
    /// <param name="loggerFactory">Where the client logs, or null for nowhere.</param>
    /// <exception cref="ArgumentException">The options are not valid.</exception>
    public static TableauClient Create(TableauClientOptions options, ILoggerFactory? loggerFactory = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        var state = NewState(options, loggerFactory);
        SocketsHttpHandler? handler = null;
        HttpClient? httpClient = null;
        var built = false;
        try
        {
            handler = TableauHandler.Create(state.Options);
            httpClient = new HttpClient(handler, disposeHandler: true) { Timeout = Timeout.InfiniteTimeSpan };
            handler = null;
            var client = new TableauClient(state, httpClient);
            httpClient = null;
            built = true;
            return client;
        }
        finally
        {
            // Left set only when building the client failed.
            handler?.Dispose();
            httpClient?.Dispose();
            if (!built) state.Dispose();
        }
    }

    /// <summary>
    /// Ends the client. A client from <see cref="Create"/> closes its connections; one over a caller's HttpClient leaves
    /// it open; one from DI leaves its named client's state to the container. A call after this throws
    /// <see cref="ObjectDisposedException"/>.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        await DisposeAsyncCore().ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }

    /// <summary>Ends what the client owns; a derived client ends its own here too.</summary>
    protected virtual ValueTask DisposeAsyncCore()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            _pipeline?.Dispose();
            _ownedHttpClient?.Dispose();
            _ownedState?.Dispose();
        }
        return ValueTask.CompletedTask;
    }

    internal static InvalidOperationException ForMocksOnly() =>
        new("This instance was made by the protected constructor, for a mock: override the members it uses.");

    private static TableauClientState NewState(TableauClientOptions options, ILoggerFactory? loggerFactory)
    {
        var result = new TableauClientOptionsValidator().Validate(Options.DefaultName, options);
        if (result.Failed)
            throw new ArgumentException($"The TableauClientOptions are not valid: {result.FailureMessage}", nameof(options));
        return new TableauClientState(Options.DefaultName, options, loggerFactory);
    }
}
