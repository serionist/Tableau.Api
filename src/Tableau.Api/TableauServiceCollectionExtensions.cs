using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tableau.Api.Pipeline;

namespace Tableau.Api;

/// <summary>Registers <see cref="TableauClient"/> with a service collection.</summary>
public static class TableauServiceCollectionExtensions
{
    /// <summary>
    /// Registers the default client, which a service takes as a plain <see cref="TableauClient"/> parameter.
    /// </summary>
    /// <inheritdoc cref="AddTableauClient(IServiceCollection, string, Action{TableauClientOptions})" path="/param|/returns|/remarks"/>
    public static TableauClientBuilder AddTableauClient(this IServiceCollection services, Action<TableauClientOptions> configure) =>
        AddTableauClient(services, Options.DefaultName, configure);

    /// <summary>
    /// Registers a client by name, which a service takes as <c>[FromKeyedServices("prod")] TableauClient prod</c>, or
    /// from <c>GetRequiredKeyedService&lt;TableauClient&gt;("prod")</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The client is transient, made over a named <see cref="HttpClient"/> from <see cref="IHttpClientFactory"/>, whose
    /// handler follows no redirect, keeps no cookies and validates certificates, and whose calls are retried and timed
    /// by the client's own handlers (<see cref="TableauClientOptions.MaxRetries"/>); what lasts, the options and the
    /// negotiated version, lives in a singleton for the name. The container disposes each client it makes when its scope
    /// ends, so take one from a scope or inject it: one taken from the root provider is kept until the provider is
    /// disposed, as every disposable transient is.
    /// </para>
    /// <para>
    /// The options are checked when the host starts, or else when the first client is made, and are read-only from then
    /// on: a configuration reloaded later changes nothing for the name. To bind them from configuration, bind in
    /// <paramref name="configure"/>: <c>options => configuration.GetSection("Tableau:Prod").Bind(options)</c>. Calling
    /// this again for the same name adds to its options, and keeps what the builder added before.
    /// </para>
    /// <para>
    /// The named HttpClient takes none of the handlers an app adds to every client with
    /// <c>ConfigureHttpClientDefaults</c>, such as a standard resilience handler: the client keeps its own rules for
    /// redirects, retries, timeouts and errors, and throws <see cref="TableauException"/> alone. Add a handler of your own
    /// to this client with <see cref="TableauClientBuilder.AddHttpMessageHandler"/>. A <see cref="TimeProvider"/> the
    /// container holds is the clock the retries wait on.
    /// </para>
    /// </remarks>
    /// <param name="services">The service collection.</param>
    /// <param name="name">The client's name, its key; <c>""</c> is the default client.</param>
    /// <param name="configure">Sets the client's options.</param>
    /// <returns>A builder for what later configures the client: its primary handler and handlers of your own.</returns>
    public static TableauClientBuilder AddTableauClient(this IServiceCollection services, string name,
        Action<TableauClientOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(configure);

        services.AddOptions<TableauClientOptions>(name).Configure(configure).ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<TableauClientOptions>, TableauClientOptionsValidator>());
        // The HttpClient's handlers are set once per name, by one action that reads everything the builders added: a
        // second registration for the name adds options and handlers, but sets nothing twice, so it drops none.
        if (!services.Any(descriptor => descriptor.IsKeyedService && descriptor.ServiceType == typeof(TableauClientState)
                && Equals(descriptor.ServiceKey, name)))
            Register(services, name);
        return new TableauClientBuilder(name, services);
    }

    /// <summary>The name of the HttpClient a Tableau client of this name sends through.</summary>
    internal static string HttpClientName(string name) => "Tableau.Api.TableauClient:" + name;

    // The named HttpClient, the state and the client, once per name. The primary handler is built from the state's
    // options, read-only since the first client, so a handler the factory makes later, after a configuration reload,
    // still follows them; the caller's settings run on it before the library's rules. Its handlers are the caller's,
    // outermost, then the library's retries and attempt, closest to the connection; those an app gives every HttpClient
    // go, since they would change the client's rules, and they run before any action of a name. The client logs its
    // own request lines, at Debug with query values hidden, so the factory's loggers go.
    private static void Register(IServiceCollection services, string name)
    {
        services.AddHttpClient(HttpClientName(name))
            .ConfigurePrimaryHttpMessageHandler(provider =>
                TableauHandler.Create(State(provider, name).Options, HandlerOptions(provider, name).PrimaryHandler))
            .ConfigureAdditionalHttpMessageHandlers((handlers, provider) =>
            {
                handlers.Clear();
                foreach (var handler in HandlerOptions(provider, name).Handlers)
                    handlers.Add(handler(provider));
                handlers.Add(TableauResilience.Handler(State(provider, name)));
            })
            .ConfigureHttpClient(client => client.Timeout = Timeout.InfiniteTimeSpan)
            .RemoveAllLoggers();

        services.TryAddKeyedSingleton(name, (provider, _) => new TableauClientState(name, OptionsFor(provider, name),
            provider.GetService<ILoggerFactory>(), provider.GetService<TimeProvider>()));
        services.TryAddKeyedTransient(name, (provider, _) => NewClient(provider, name));
        if (name.Length == 0)
            services.TryAddTransient(provider => NewClient(provider, name));
    }

    private static TableauClientState State(IServiceProvider provider, string name) =>
        provider.GetRequiredKeyedService<TableauClientState>(name);

    private static TableauHandlerOptions HandlerOptions(IServiceProvider provider, string name) =>
        provider.GetRequiredService<IOptionsMonitor<TableauHandlerOptions>>().Get(name);

    private static TableauClientOptions OptionsFor(IServiceProvider provider, string name) =>
        provider.GetRequiredService<IOptionsMonitor<TableauClientOptions>>().Get(name);

    private static TableauClient NewClient(IServiceProvider provider, string name)
    {
        var state = State(provider, name);
        var httpClient = provider.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName(name));
        return new TableauClient(httpClient, state);
    }
}

/// <summary>
/// What <see cref="TableauServiceCollectionExtensions.AddTableauClient(IServiceCollection, string, Action{TableauClientOptions})"/>
/// returns, for the methods that configure the client further.
/// </summary>
/// <remarks>
/// <para>The client's HttpClient is built in this order, whatever order the methods were called in:</para>
/// <list type="number">
/// <item><description>
/// The library makes a <see cref="SocketsHttpHandler"/> that renews its connections every five minutes and checks
/// certificates for revocation.
/// </description></item>
/// <item><description>Each <see cref="ConfigurePrimaryHandler"/> action runs on it, in the order they were added.</description></item>
/// <item><description>
/// The library's rules are set again, whatever the actions did: no automatic redirects, no cookies, and the certificate
/// check, with no <c>RemoteCertificateValidationCallback</c> but the one
/// <see cref="TableauClientOptions.DangerousAcceptAnyServerCertificate"/> sets. They are set on the handler's own copy of
/// its <c>SslOptions</c>, so options an action gave it, shared with another client or an app's own handler, stay as
/// they were.
/// </description></item>
/// <item><description>
/// The handlers of <see cref="AddHttpMessageHandler"/> wrap it, the first added outermost; inside them, closest to the
/// connection, the library's handler, with the retries and the attempt timeout. So a handler of yours sees each request
/// once, however many attempts it takes, and its outcome after them; a redirect the client follows is another request.
/// </description></item>
/// </list>
/// </remarks>
public sealed class TableauClientBuilder
{
    internal TableauClientBuilder(string name, IServiceCollection services)
    {
        Name = name;
        Services = services;
    }

    /// <summary>The client's name, its key in the container; <c>""</c> for the default client.</summary>
    public string Name { get; }

    /// <summary>The service collection the client is registered with.</summary>
    public IServiceCollection Services { get; }

    /// <summary>
    /// Sets the client's connection: a proxy (<see cref="SocketsHttpHandler.Proxy"/>), a client certificate
    /// (<c>SslOptions.ClientCertificates</c>), a private certificate authority, or a limit such as
    /// <see cref="SocketsHttpHandler.ConnectTimeout"/>. The library's rules are set again after it: no automatic
    /// redirects, no cookies, and the certificate check.
    /// </summary>
    /// <remarks>
    /// <para>
    /// To trust a private certificate authority, give the handler a chain policy that trusts it:
    /// <c>handler.SslOptions.CertificateChainPolicy = new X509ChainPolicy { TrustMode = X509ChainTrustMode.CustomRootTrust,
    /// CustomTrustStore = { authority } }</c>. A chain policy you set is used as you set it, in place of the library's:
    /// the library's tolerance of a revocation status that can't be learnt, and its 10-second wait for a revocation
    /// answer, go with it, so the policy above fails a certificate whose revocation list can't be reached. Its flags and
    /// its <c>RevocationMode</c> can loosen the check, such as <c>AllowUnknownCertificateAuthority</c>,
    /// <c>IgnoreNotTimeValid</c>, or <c>IgnoreInvalidName</c>, which also turns off the check of the server's name, and
    /// the client gives no warning for them.
    /// </para>
    /// <para>
    /// A <c>RemoteCertificateValidationCallback</c> you set is replaced, so no callback turns the check off: only
    /// <see cref="TableauClientOptions.DangerousAcceptAnyServerCertificate"/> does, with a warning. The client sets its
    /// rules on its own copy of the handler's <c>SslOptions</c>, so options you share with another handler stay as you
    /// set them.
    /// </para>
    /// </remarks>
    /// <param name="configure">Sets the handler; it runs each time the factory makes one, every few minutes.</param>
    /// <returns>This builder.</returns>
    public TableauClientBuilder ConfigurePrimaryHandler(Action<SocketsHttpHandler> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        Services.Configure<TableauHandlerOptions>(Name, options => options.PrimaryHandler.Add(configure));
        return this;
    }

    /// <summary>
    /// Adds a handler of your own around the client's connection, outside its retries: it sees each request once, after
    /// the client's attempts at it. The first added is the outermost. To bring your own resilience, set
    /// <see cref="TableauClientOptions.MaxRetries"/> to 0 and add it here.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An exception your handler throws, a timeout or a cancellation of its own among them, fails the call with
    /// <see cref="TableauException"/> (<see cref="TableauErrorKind.Http"/>), the exception as its cause; the caller's
    /// cancellation stays a cancellation.
    /// </para>
    /// <para>
    /// A handler of yours that retries sees the request but not whether the call is safe to send twice, which the client's
    /// own retries know: retry GET and HEAD, or a call you know is safe to send twice; never a POST, which publishes,
    /// creates or runs, nor Append to File Upload's PUT, which adds its chunk again. The attempt timeout stays the
    /// client's, and runs inside your handler, so a timeout of yours decides only when it is shorter than
    /// <see cref="TableauClientOptions.RequestTimeout"/>: make it shorter, or set that to
    /// <see cref="Timeout.InfiniteTimeSpan"/>.
    /// </para>
    /// </remarks>
    /// <param name="configureHandler">
    /// Makes a new handler each time it is called, from the scoped provider the factory gives the handler's lifetime.
    /// </param>
    /// <returns>This builder.</returns>
    public TableauClientBuilder AddHttpMessageHandler(Func<IServiceProvider, DelegatingHandler> configureHandler)
    {
        ArgumentNullException.ThrowIfNull(configureHandler);
        Services.Configure<TableauHandlerOptions>(Name, options => options.Handlers.Add(configureHandler));
        return this;
    }
}

/// <summary>What the builders of one name added to its HttpClient, read once each time the factory builds its handlers.</summary>
internal sealed class TableauHandlerOptions
{
    /// <summary>The <see cref="TableauClientBuilder.ConfigurePrimaryHandler"/> actions, in order.</summary>
    public List<Action<SocketsHttpHandler>> PrimaryHandler { get; } = [];

    /// <summary>The <see cref="TableauClientBuilder.AddHttpMessageHandler"/> factories, outermost first.</summary>
    public List<Func<IServiceProvider, DelegatingHandler>> Handlers { get; } = [];
}
