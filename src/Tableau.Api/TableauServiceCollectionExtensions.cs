using System;
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
    /// The client is transient, made over a named <see cref="System.Net.Http.HttpClient"/> from
    /// <see cref="System.Net.Http.IHttpClientFactory"/>, whose handler follows no redirect, keeps no cookies and
    /// validates certificates; what lasts, the options and the negotiated version, lives in a singleton for the name.
    /// The container disposes each client it makes when its scope ends, so take one from a scope or inject it: one
    /// taken from the root provider is kept until the provider is disposed, as every disposable transient is.
    /// </para>
    /// <para>
    /// The options are checked when the host starts, or else when the first client is made, and are read-only from then
    /// on. To bind them from configuration, bind in <paramref name="configure"/>:
    /// <c>options => configuration.GetSection("Tableau:Prod").Bind(options)</c>.
    /// </para>
    /// </remarks>
    /// <param name="services">The service collection.</param>
    /// <param name="name">The client's name, its key; <c>""</c> is the default client.</param>
    /// <param name="configure">Sets the client's options.</param>
    /// <returns>A builder for what later configures the client.</returns>
    public static TableauClientBuilder AddTableauClient(this IServiceCollection services, string name,
        Action<TableauClientOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(configure);

        services.AddOptions<TableauClientOptions>(name).Configure(configure).ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<TableauClientOptions>, TableauClientOptionsValidator>());

        // The client logs its own request lines, at Debug and with query values hidden, so the factory's loggers go.
        var httpClientName = HttpClientName(name);
        services.AddHttpClient(httpClientName)
            .ConfigurePrimaryHttpMessageHandler(provider => TableauHandler.Create(OptionsFor(provider, name)))
            .ConfigureHttpClient(client => client.Timeout = Timeout.InfiniteTimeSpan)
            .RemoveAllLoggers();

        services.TryAddKeyedSingleton(name, (provider, _) =>
            new TableauClientState(name, OptionsFor(provider, name), provider.GetService<ILoggerFactory>()));
        services.TryAddKeyedTransient(name, (provider, _) => NewClient(provider, name));
        if (name.Length == 0)
            services.TryAddTransient(provider => NewClient(provider, name));
        return new TableauClientBuilder(name, services);
    }

    /// <summary>The name of the HttpClient a Tableau client of this name sends through.</summary>
    internal static string HttpClientName(string name) => "Tableau.Api.TableauClient:" + name;

    private static TableauClientOptions OptionsFor(IServiceProvider provider, string name) =>
        provider.GetRequiredService<IOptionsMonitor<TableauClientOptions>>().Get(name);

    private static TableauClient NewClient(IServiceProvider provider, string name)
    {
        var state = provider.GetRequiredKeyedService<TableauClientState>(name);
        var httpClient = provider.GetRequiredService<System.Net.Http.IHttpClientFactory>().CreateClient(HttpClientName(name));
        return new TableauClient(httpClient, state);
    }
}

/// <summary>
/// What <see cref="TableauServiceCollectionExtensions.AddTableauClient(IServiceCollection, string, Action{TableauClientOptions})"/>
/// returns, for the methods that configure the client further.
/// </summary>
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
}
