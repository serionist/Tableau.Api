using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tableau.Api.Pipeline;

namespace Tableau.Api;

/// <summary>
/// A client for Tableau's REST API, on Tableau Server or Tableau Cloud, with one sub-client per area: <see cref="Server"/>,
/// <see cref="Sessions"/>, <see cref="Sites"/>, <see cref="Projects"/>, <see cref="Users"/>, <see cref="Groups"/>,
/// <see cref="Favorites"/>, <see cref="Permissions"/>, <see cref="Tags"/>, <see cref="Metadata"/>, <see cref="Workbooks"/>,
/// <see cref="Views"/> and <see cref="DataSources"/> so far, and the others as the 2.0 client takes them over from
/// <see cref="TableauApiClient"/>.
/// </summary>
/// <remarks>
/// <para>Get one from DI, registered with <c>services.AddTableauClient(name, options => ...)</c>, or build one with
/// <see cref="Create"/>, or over an <see cref="HttpClient"/> of your own.</para>
/// <para>
/// A list comes two ways: <c>ListAsync</c> reads every item, a page at a time as you go
/// (<c>await foreach (var project in client.Projects.ListAsync(options, cancellationToken))</c>); <c>ListPageAsync</c>
/// returns one <see cref="Page{T}"/>. A list's options filter and sort it over its resource's fields
/// (<see cref="TableauFilter"/>, <see cref="TableauSort"/>, <see cref="ProjectField"/>), and a list is never null.
/// </para>
/// <para>
/// Given credentials, it signs itself in on the first call that needs a session, one sign-in at a time, and signs in
/// again shortly before the session is due to end and once when the server refuses it with a 401, sending the refused
/// call again. Without credentials it makes only the calls that need no session, such as Server Info, or calls in a
/// session you signed in yourself through a client from <see cref="WithSession"/>.
/// </para>
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
    private readonly SessionsClient? _sessions;
    private readonly SitesClient? _sites;
    private readonly ProjectsClient? _projects;
    private readonly UsersClient? _users;
    private readonly GroupsClient? _groups;
    private readonly FavoritesClient? _favorites;
    private readonly PermissionsClient? _permissions;
    private readonly TagsClient? _tags;
    private readonly MetadataClient? _metadata;
    private readonly WorkbooksClient? _workbooks;
    private readonly ViewsClient? _views;
    private readonly DataSourcesClient? _dataSources;
    private readonly HttpMessageInvoker? _owned;
    private readonly bool _ownsState;
    private int _disposed;
    private int _signedOut;

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
    /// The client can't change a handler it didn't build, so <paramref name="httpClient"/>'s handler must follow no
    /// redirect (<c>AllowAutoRedirect = false</c>) and keep no cookies (<c>UseCookies = false</c>). The client follows
    /// redirects itself, only to the same scheme, host and port. A handler that follows one elsewhere sends the request
    /// there, its session token in <c>X-Tableau-Auth</c> and its body, a sign-in's password among them, before the client
    /// can see it: the client then throws <see cref="TableauErrorKind.Redirect"/> and signs that session out, so the token
    /// stops working, but a password has gone. Certificates are the handler's to check, so
    /// <see cref="TableauClientOptions.DangerousAcceptAnyServerCertificate"/> is refused here.
    /// </para>
    /// <para>
    /// The client owns its session: disposing it signs the session out, as <see cref="DisposeAsync"/> describes.
    /// </para>
    /// </remarks>
    /// <param name="httpClient">The HttpClient to send through.</param>
    /// <param name="options">The server and the rules; checked here, and read-only from here on.</param>
    /// <param name="credentials">What the client signs itself in with, or null for a client that signs in only when told to.</param>
    /// <param name="loggerFactory">Where the client logs, or null for nowhere.</param>
    /// <exception cref="ArgumentException">The options are not valid, or ask for what this constructor can't do.</exception>
    public TableauClient(HttpClient httpClient, TableauClientOptions options, TableauCredentials? credentials = null,
        ILoggerFactory? loggerFactory = null)
        : this(httpClient, options, credentials, loggerFactory, timeProvider: null)
    {
    }

    /// <summary>The same, with the clock the retries wait on and sessions end by, for the tests.</summary>
    internal TableauClient(HttpClient httpClient, TableauClientOptions options, TableauCredentials? credentials,
        ILoggerFactory? loggerFactory, TimeProvider? timeProvider)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);
        if (options.DangerousAcceptAnyServerCertificate)
        {
            throw new ArgumentException("DangerousAcceptAnyServerCertificate can't apply to an HttpClient the client didn't build: "
                + "set the certificate check on its handler.", nameof(options));
        }
        TableauClientState? state = null;
        HttpMessageHandler? caller = null;
        HttpMessageHandler? chain = null;
        try
        {
            state = NewState(options, credentials, loggerFactory, timeProvider);
            caller = new CallerClientHandler(httpClient);
            chain = TableauResilience.Chain(state, caller);
            caller = null;
            _owned = new HttpMessageInvoker(chain, disposeHandler: true);
            chain = null;
            _pipeline = new TableauPipeline(_owned, state);
            state = null;
        }
        finally
        {
            // Left set only when building the client failed.
            caller?.Dispose();
            chain?.Dispose();
            state?.Dispose();
        }
        (_server, _sessions, _sites, _projects, _users, _groups, _favorites, _permissions, _tags, _metadata, _workbooks, _views, _dataSources) =
            SubClients(_pipeline);
        _ownsState = true;
    }

    /// <summary>
    /// A client over a named client's state, which DI owns, and the named HttpClient, whose chain the library built. The
    /// session is the name's, which no single client signs out.
    /// </summary>
    internal TableauClient(HttpClient httpClient, TableauClientState state)
        : this(new TableauPipeline(httpClient, state))
    {
    }

    /// <summary>A client that owns its HttpClient and its session, as <see cref="Create"/> builds it.</summary>
    private TableauClient(TableauClientState state, HttpClient ownedHttpClient)
        : this(new TableauPipeline(ownedHttpClient, state))
    {
        _owned = ownedHttpClient;
        _ownsState = true;
    }

    /// <summary>A client over a pipeline: a named client's, or one bound to a session by <see cref="WithSession"/>.</summary>
    private TableauClient(TableauPipeline pipeline)
    {
        _pipeline = pipeline;
        (_server, _sessions, _sites, _projects, _users, _groups, _favorites, _permissions, _tags, _metadata, _workbooks, _views, _dataSources) =
            SubClients(_pipeline);
    }

    /// <summary>Server Info: the versions of the server and of its REST API.</summary>
    public virtual ServerClient Server => _server ?? throw ForMocksOnly();

    /// <summary>Sessions you hold yourself: sign in, switch site, sign out.</summary>
    public virtual SessionsClient Sessions => _sessions ?? throw ForMocksOnly();

    /// <summary>Sites: get one, list them, create, update and delete one.</summary>
    public virtual SitesClient Sites => _sites ?? throw ForMocksOnly();

    /// <summary>The site's projects: list them, create, update and delete one.</summary>
    public virtual ProjectsClient Projects => _projects ?? throw ForMocksOnly();

    /// <summary>The site's users: get one, list them, add, update and remove one, list a user's groups.</summary>
    public virtual UsersClient Users => _users ?? throw ForMocksOnly();

    /// <summary>The site's groups: list them, create, import, update and delete one, add and remove its users, list them.</summary>
    public virtual GroupsClient Groups => _groups ?? throw ForMocksOnly();

    /// <summary>A user's favorites: list them, add and remove one.</summary>
    public virtual FavoritesClient Favorites => _favorites ?? throw ForMocksOnly();

    /// <summary>Permissions on content of every kind, and a project's default permissions: list, add and delete them.</summary>
    public virtual PermissionsClient Permissions => _permissions ?? throw ForMocksOnly();

    /// <summary>Tags on content of every kind: add and delete them, one content at a time or in a batch.</summary>
    public virtual TagsClient Tags => _tags ?? throw ForMocksOnly();

    /// <summary>
    /// Tableau Catalog's databases, tables and columns, data quality warnings, and the Metadata API's GraphQL queries.
    /// </summary>
    public virtual MetadataClient Metadata => _metadata ?? throw ForMocksOnly();

    /// <summary>
    /// The site's workbooks: list them, get, update and delete one, its connections, an extract refresh, its file, preview
    /// image and PDF, and its revisions.
    /// </summary>
    public virtual WorkbooksClient Workbooks => _workbooks ?? throw ForMocksOnly();

    /// <summary>The site's views: list them, get one, and export one as data, an image, a PDF or a crosstab.</summary>
    public virtual ViewsClient Views => _views ?? throw ForMocksOnly();

    /// <summary>
    /// The site's published data sources: list them, get, update and delete one, its connections, an extract refresh, its
    /// file and its revisions.
    /// </summary>
    public virtual DataSourcesClient DataSources => _dataSources ?? throw ForMocksOnly();

    /// <summary>The pipeline every sub-client sends through.</summary>
    internal TableauPipeline Pipeline => _pipeline ?? throw ForMocksOnly();

    /// <summary>
    /// A client that calls in <paramref name="session"/>: over this client's connection and options, with its negotiated
    /// version, but never signing in. One bound client per site serves work across sites.
    /// </summary>
    /// <remarks>
    /// When the server refuses the session, because it ended or was signed out, a call throws
    /// <see cref="TableauErrorKind.Authentication"/>: sign in again and bind the new session. Disposing the bound client
    /// signs nothing out and leaves this client open; disposing this client ends the bound one too.
    /// </remarks>
    /// <param name="session">The session to call in, from <see cref="SessionsClient.SignInAsync"/> or <see cref="SessionsClient.SwitchSiteAsync"/>.</param>
    /// <returns>The bound client.</returns>
    /// <exception cref="ArgumentException"><paramref name="session"/> holds no token: it wasn't made by a sign-in.</exception>
    public virtual TableauClient WithSession(TableauSession session)
    {
        SessionsClient.Signed(session);
        return new TableauClient(Pipeline.Bind(session));
    }

    /// <summary>
    /// A client with an HttpClient of its own, over one connection pool that renews each connection every five
    /// minutes, so DNS changes reach it. It follows no redirect but the client's own, keeps no cookies, and validates
    /// the server's certificate, a revoked one refused, unless the options turn that off. Its calls are retried and
    /// timed as a client from <c>AddTableauClient</c>'s are, by the same handlers. Dispose it when done: that signs its
    /// session out and closes its connections.
    /// </summary>
    /// <param name="options">The server and the rules; checked here, and read-only from here on.</param>
    /// <param name="credentials">What the client signs itself in with, or null for a client that signs in only when told to.</param>
    /// <param name="loggerFactory">Where the client logs, or null for nowhere.</param>
    /// <exception cref="ArgumentException">The options are not valid.</exception>
    public static TableauClient Create(TableauClientOptions options, TableauCredentials? credentials = null,
        ILoggerFactory? loggerFactory = null) =>
        CreateWithClock(options, credentials, loggerFactory, timeProvider: null);

    /// <summary>The same, with the clock the retries wait on and sessions end by, for the tests.</summary>
    internal static TableauClient CreateWithClock(TableauClientOptions options, TableauCredentials? credentials,
        ILoggerFactory? loggerFactory, TimeProvider? timeProvider)
    {
        ArgumentNullException.ThrowIfNull(options);
        TableauClientState? state = null;
        SocketsHttpHandler? primary = null;
        HttpMessageHandler? chain = null;
        HttpClient? httpClient = null;
        try
        {
            state = NewState(options, credentials, loggerFactory, timeProvider);
            primary = TableauHandler.Create(state.Options);
            chain = TableauResilience.Chain(state, primary);
            primary = null;
            httpClient = new HttpClient(chain, disposeHandler: true) { Timeout = Timeout.InfiniteTimeSpan };
            chain = null;
            var client = new TableauClient(state, httpClient);
            httpClient = null;
            state = null;
            return client;
        }
        finally
        {
            // Left set only when building the client failed.
            primary?.Dispose();
            chain?.Dispose();
            httpClient?.Dispose();
            state?.Dispose();
        }
    }

    /// <summary>
    /// Ends the client as <see cref="DisposeAsync"/> does, signing its session out on a thread of the pool and waiting
    /// for that ten seconds at most, so it can't deadlock on the caller's synchronisation context. Prefer
    /// <see cref="DisposeAsync"/>, which blocks no thread.
    /// </summary>
    public void Dispose()
    {
        if (OwnsSessionToSignOut())
        {
            var signingOut = Task.Run(() => _pipeline!.State.Sessions.SignOutOnDisposeAsync(_pipeline));
            // It never throws, and gives up within its own timeout; the wait's own bound is a backstop.
            signingOut.Wait(TableauSessions.SignOutTimeout + TimeSpan.FromSeconds(1));
        }
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Ends the client. A client from <see cref="Create"/> or the constructor over an HttpClient signs out the session it
    /// signed itself in with, never throwing and waiting ten seconds at most, then a client from <see cref="Create"/>
    /// closes its connections; one over a caller's HttpClient leaves that open. A client from DI signs nothing out: its
    /// session is its name's, shared by every client of the name, and ends on its own, or when a personal access token
    /// signs in again. A client from <see cref="WithSession"/> signs nothing out either: its session is yours. Sessions
    /// signed in through <see cref="Sessions"/> are yours too. A call after this throws
    /// <see cref="ObjectDisposedException"/>. Disposing twice does nothing more.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (OwnsSessionToSignOut())
            await _pipeline!.State.Sessions.SignOutOnDisposeAsync(_pipeline).ConfigureAwait(false);
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>Ends what the client owns; a derived client ends its own here too, and calls this.</summary>
    /// <param name="disposing">True from <see cref="Dispose()"/>; false from a finalizer, which ends nothing managed.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (disposing && Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            _pipeline?.Dispose();
            _owned?.Dispose();
            if (_ownsState) _pipeline!.State.Dispose();
        }
    }

    internal static InvalidOperationException ForMocksOnly() =>
        new("This instance was made by the protected constructor, for a mock: override the members it uses.");

    // Each sub-client made once per client, over its pipeline.
    private static (ServerClient, SessionsClient, SitesClient, ProjectsClient, UsersClient, GroupsClient, FavoritesClient, PermissionsClient,
        TagsClient, MetadataClient, WorkbooksClient, ViewsClient, DataSourcesClient) SubClients(TableauPipeline pipeline) =>
        (new ServerClient(pipeline), new SessionsClient(pipeline), new SitesClient(pipeline), new ProjectsClient(pipeline),
            new UsersClient(pipeline), new GroupsClient(pipeline), new FavoritesClient(pipeline), new PermissionsClient(pipeline),
            new TagsClient(pipeline), new MetadataClient(pipeline), new WorkbooksClient(pipeline), new ViewsClient(pipeline),
            new DataSourcesClient(pipeline));

    // Once, and only for a client that owns its session and holds one.
    private bool OwnsSessionToSignOut() =>
        _ownsState && Volatile.Read(ref _disposed) == 0 && _pipeline!.State.Sessions.Current is not null
        && Interlocked.Exchange(ref _signedOut, 1) == 0;

    private static TableauClientState NewState(TableauClientOptions options, TableauCredentials? credentials,
        ILoggerFactory? loggerFactory, TimeProvider? timeProvider)
    {
        var result = new TableauClientOptionsValidator().Validate(Options.DefaultName, options);
        if (result.Failed)
            throw new ArgumentException($"The TableauClientOptions are not valid: {result.FailureMessage}", nameof(options));
        return new TableauClientState(Options.DefaultName, options, loggerFactory, timeProvider, credentials);
    }
}
