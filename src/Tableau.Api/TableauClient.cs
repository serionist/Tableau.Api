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
/// A call that is safe to send twice, such as a read, is sent again after a 408, a 429, a 5xx, a connection that failed
/// or an attempt longer than <see cref="TableauClientOptions.RequestTimeout"/>, up to
/// <see cref="TableauClientOptions.MaxRetries"/> times, waiting as the server asks up to
/// <see cref="TableauClientOptions.MaxRetryAfter"/>. A file the server sends comes back as a <see cref="TableauDownload"/>,
/// streamed.
/// </para>
/// <para>
/// Every call fails with <see cref="TableauException"/>, but for a refused argument (<see cref="ArgumentException"/>)
/// and the caller's own cancellation (<see cref="OperationCanceledException"/>). The client is safe to share between
/// threads. Its members are virtual, and the protected constructor makes one for a mock.
/// </para>
/// </remarks>
public class TableauClient : IDisposable, IAsyncDisposable
{
    private readonly TableauPipeline? _pipeline;
    private readonly ServerClient? _server;
    private readonly HttpMessageInvoker? _owned;
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
    /// <para>
    /// The client's retries and timeouts wrap <paramref name="httpClient"/>, as they wrap the connection of a client
    /// <see cref="Create"/> builds: each attempt of a call is one <see cref="HttpClient.SendAsync(HttpRequestMessage, HttpCompletionOption, System.Threading.CancellationToken)"/>
    /// of a copy of the request, so the HttpClient's handlers run within each attempt, and its own
    /// <see cref="HttpClient.Timeout"/> applies to each, beside the options' timeouts. If its handlers retry too, set
    /// <see cref="TableauClientOptions.MaxRetries"/> to 0, or each of their attempts is repeated by the client's.
    /// </para>
    /// <para>
    /// The client can't change a handler it didn't build, so <paramref name="httpClient"/>'s handler should follow no
    /// redirect (<c>AllowAutoRedirect = false</c>) and keep no cookies (<c>UseCookies = false</c>): the client follows
    /// redirects itself, only to the same scheme, host and port, and throws <see cref="TableauErrorKind.Redirect"/> when
    /// it finds a handler went elsewhere, though by then the request has gone there. Certificates are the handler's to
    /// check, so <see cref="TableauClientOptions.DangerousAcceptAnyServerCertificate"/> is refused here.
    /// </para>
    /// </remarks>
    /// <param name="httpClient">The HttpClient to send through.</param>
    /// <param name="options">The server and the rules; checked here, and read-only from here on.</param>
    /// <param name="loggerFactory">Where the client logs, or null for nowhere.</param>
    /// <exception cref="ArgumentException">The options are not valid, or ask for what this constructor can't do.</exception>
    public TableauClient(HttpClient httpClient, TableauClientOptions options, ILoggerFactory? loggerFactory = null)
        : this(httpClient, options, loggerFactory, timeProvider: null)
    {
    }

    /// <summary>The same, with the clock the retries wait on, for the tests.</summary>
    internal TableauClient(HttpClient httpClient, TableauClientOptions options, ILoggerFactory? loggerFactory, TimeProvider? timeProvider)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);
        if (options.DangerousAcceptAnyServerCertificate)
        {
            throw new ArgumentException("DangerousAcceptAnyServerCertificate can't apply to an HttpClient the client didn't build: "
                + "set the certificate check on its handler.", nameof(options));
        }
        var state = NewState(options, loggerFactory, timeProvider);
        HttpMessageHandler? chain = null;
        try
        {
            chain = TableauResilience.Chain(state, new CallerClientHandler(httpClient));
            _owned = new HttpMessageInvoker(chain, disposeHandler: true);
            chain = null;
        }
        finally
        {
            // Left set only when building the invoker failed.
            chain?.Dispose();
        }
        _pipeline = new TableauPipeline(_owned, state);
        _server = new ServerClient(_pipeline);
    }

    /// <summary>A client over a named client's state, which DI owns, and the named HttpClient, whose chain the library built.</summary>
    internal TableauClient(HttpClient httpClient, TableauClientState state)
    {
        _pipeline = new TableauPipeline(httpClient, state);
        _server = new ServerClient(_pipeline);
    }

    /// <summary>A client that owns its HttpClient, as <see cref="Create"/> builds it.</summary>
    private TableauClient(TableauClientState state, HttpClient ownedHttpClient)
        : this(ownedHttpClient, state)
    {
        _owned = ownedHttpClient;
    }

    /// <summary>Server Info: the versions of the server and of its REST API.</summary>
    public virtual ServerClient Server => _server ?? throw ForMocksOnly();

    /// <summary>The pipeline every sub-client sends through.</summary>
    internal TableauPipeline Pipeline => _pipeline ?? throw ForMocksOnly();

    /// <summary>
    /// A client with an HttpClient of its own, over one connection pool that renews each connection every five
    /// minutes, so DNS changes reach it. It follows no redirect but the client's own, keeps no cookies, and validates
    /// the server's certificate, a revoked one refused, unless the options turn that off. Its calls are retried and
    /// timed as a client from <c>AddTableauClient</c>'s are, by the same handlers. Dispose it when done.
    /// </summary>
    /// <param name="options">The server and the rules; checked here, and read-only from here on.</param>
    /// <param name="loggerFactory">Where the client logs, or null for nowhere.</param>
    /// <exception cref="ArgumentException">The options are not valid.</exception>
    public static TableauClient Create(TableauClientOptions options, ILoggerFactory? loggerFactory = null) =>
        CreateWithClock(options, loggerFactory, timeProvider: null);

    /// <summary>The same, with the clock the retries wait on, for the tests.</summary>
    internal static TableauClient CreateWithClock(TableauClientOptions options, ILoggerFactory? loggerFactory, TimeProvider? timeProvider)
    {
        ArgumentNullException.ThrowIfNull(options);
        var state = NewState(options, loggerFactory, timeProvider);
        SocketsHttpHandler? primary = null;
        HttpMessageHandler? chain = null;
        HttpClient? httpClient = null;
        try
        {
            primary = TableauHandler.Create(state.Options);
            chain = TableauResilience.Chain(state, primary);
            primary = null;
            httpClient = new HttpClient(chain, disposeHandler: true) { Timeout = Timeout.InfiniteTimeSpan };
            chain = null;
            var client = new TableauClient(state, httpClient);
            httpClient = null;
            return client;
        }
        finally
        {
            // Left set only when building the client failed.
            primary?.Dispose();
            chain?.Dispose();
            httpClient?.Dispose();
        }
    }

    /// <summary>
    /// Ends the client. A client from <see cref="Create"/> closes its connections; one over a caller's HttpClient leaves
    /// it open; one from DI leaves its named client's state to the container. A call after this throws
    /// <see cref="ObjectDisposedException"/>. Disposing twice does nothing more.
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Ends the client as <see cref="Dispose()"/> does: nothing it ends waits on I/O, so a container or a caller may end
    /// it either way.
    /// </summary>
    public ValueTask DisposeAsync()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }

    /// <summary>Ends what the client owns; a derived client ends its own here too, and calls this.</summary>
    /// <param name="disposing">True from <see cref="Dispose()"/>; false from a finalizer, which ends nothing managed.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (disposing && Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            _pipeline?.Dispose();
            _owned?.Dispose();
        }
    }

    internal static InvalidOperationException ForMocksOnly() =>
        new("This instance was made by the protected constructor, for a mock: override the members it uses.");

    private static TableauClientState NewState(TableauClientOptions options, ILoggerFactory? loggerFactory, TimeProvider? timeProvider)
    {
        var result = new TableauClientOptionsValidator().Validate(Options.DefaultName, options);
        if (result.Failed)
            throw new ArgumentException($"The TableauClientOptions are not valid: {result.FailureMessage}", nameof(options));
        return new TableauClientState(Options.DefaultName, options, loggerFactory, timeProvider);
    }
}
