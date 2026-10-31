using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Tableau.Api.Tests.Fakes;

namespace Tableau.Api.Tests;

/// <summary>
/// <c>AddTableauClient</c>: a default client and named ones, each a transient over a named HttpClient, keyed by its
/// name, with what lasts in a singleton per name; options bound from configuration, checked at start-up and read-only
/// once a client is made.
/// </summary>
[TestClass]
public sealed class DependencyInjectionTests
{
    [TestMethod]
    public async Task ADefaultAndANamedClientEachCallTheirOwnServer()
    {
        using var first = FakeTableauServer.Start();
        using var second = FakeTableauServer.Start();
        first.Map("GET", ClientFixtures.ServerInfoPath, ClientFixtures.ServerInfo("3.25", "2025.1.0"));
        second.Map("GET", ClientFixtures.ServerInfoPath, ClientFixtures.ServerInfo("3.25", "2025.3.0"));
        var services = Services();
        services.AddTableauClient(options => Copy(ClientFixtures.Options(first), options));
        services.AddTableauClient("prod", options => Copy(ClientFixtures.Options(second), options));
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        await using var scope = provider.CreateAsyncScope();

        var plain = scope.ServiceProvider.GetRequiredService<TableauClient>();
        var keyedDefault = scope.ServiceProvider.GetRequiredKeyedService<TableauClient>("");
        var prod = scope.ServiceProvider.GetRequiredKeyedService<TableauClient>("prod");

        Assert.AreEqual("2025.1.0", (await plain.Server.GetInfoAsync()).ProductVersion?.Value);
        Assert.AreEqual("2025.1.0", (await keyedDefault.Server.GetInfoAsync()).ProductVersion?.Value);
        Assert.AreEqual("2025.3.0", (await prod.Server.GetInfoAsync()).ProductVersion?.Value);
        Assert.AreNotSame(plain, keyedDefault);
        Assert.HasCount(2, first.Requests);
        Assert.HasCount(1, second.Requests);
    }

    [TestMethod]
    public async Task ClientsOfOneNameShareTheirNegotiatedVersion()
    {
        using var server = FakeTableauServer.Start();
        server.Map("GET", ClientFixtures.NegotiationPath, ClientFixtures.ServerInfo());
        server.Map("GET", ClientFixtures.ServerInfoPath, ClientFixtures.ServerInfo());
        var services = Services();
        services.AddTableauClient("prod", options => Copy(ClientFixtures.Options(server, version: null), options));
        await using var provider = services.BuildServiceProvider();

        for (var i = 0; i < 3; i++)
        {
            await using var scope = provider.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredKeyedService<TableauClient>("prod").Server.GetInfoAsync();
        }

        Assert.AreEqual(1, server.Requests.Count(request => request.Path == ClientFixtures.NegotiationPath));
        Assert.AreEqual(3, server.Requests.Count(request => request.Path == ClientFixtures.ServerInfoPath));
    }

    [TestMethod]
    public async Task ADisposedClientLeavesTheNamedClientWorking()
    {
        using var server = FakeTableauServer.Start();
        server.Map("GET", ClientFixtures.ServerInfoPath, ClientFixtures.ServerInfo());
        var services = Services();
        services.AddTableauClient("prod", options => Copy(ClientFixtures.Options(server), options));
        await using var provider = services.BuildServiceProvider();

        await using (var scope = provider.CreateAsyncScope())
        {
            var disposed = scope.ServiceProvider.GetRequiredKeyedService<TableauClient>("prod");
            await disposed.DisposeAsync();
            await Assert.ThrowsExactlyAsync<ObjectDisposedException>(() => disposed.Server.GetInfoAsync());
        }
        await using (var scope = provider.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredKeyedService<TableauClient>("prod").Server.GetInfoAsync();

        Assert.HasCount(1, server.Requests);
    }

    [TestMethod]
    public async Task TheNamedHttpClientsHandlerFollowsNoRedirectAndKeepsNoCookies()
    {
        using var server = FakeTableauServer.Start();
        using var other = FakeTableauServer.Start();
        server.Map("GET", ClientFixtures.ServerInfoPath,
            ClientFixtures.ServerInfo().WithHeader("Set-Cookie", "workgroup_session_id=invented-cookie; Path=/"));
        server.Map("GET", "/api/3.25/elsewhere", FakeResponse.Redirect(302, new Uri(other.BaseUrl, "/api/3.25/elsewhere").ToString()));
        var services = Services();
        services.AddTableauClient("prod", options => Copy(ClientFixtures.Options(server), options));
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var client = scope.ServiceProvider.GetRequiredKeyedService<TableauClient>("prod");

        await client.Server.GetInfoAsync();
        await client.Server.GetInfoAsync();
        await ClientFixtures.FailsAsync(TableauErrorKind.Redirect,
            () => ClientFixtures.SendAsync(client, ClientFixtures.Endpoint("{version}/elsewhere")));

        Assert.IsTrue(server.Requests.All(request => request.Header("Cookie") is null));
        Assert.IsEmpty(other.Requests);
    }

    [TestMethod]
    public async Task OptionsBindFromConfiguration()
    {
        using var server = FakeTableauServer.Start();
        server.Map("GET", "/tableau/api/3.27/serverinfo", ClientFixtures.ServerInfo("3.27"));
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Tableau:Prod:ServerUrl"] = new Uri(server.BaseUrl, "tableau/").ToString(),
            ["Tableau:Prod:Site"] = "finance",
            ["Tableau:Prod:ApiVersion"] = "3.27",
            ["Tableau:Prod:Deployment"] = "Server",
            ["Tableau:Prod:AllowInsecureHttp"] = "true",
            ["Tableau:Prod:AllowUnsupportedServerVersion"] = "true",
            ["Tableau:Prod:RequestTimeout"] = "00:00:45",
            ["Tableau:Prod:TransferTimeout"] = "01:00:00",
        }).Build();
        var services = Services();
        services.AddTableauClient("prod", options => configuration.GetSection("Tableau:Prod").Bind(options));
        await using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptionsMonitor<TableauClientOptions>>().Get("prod");
        await using var scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredKeyedService<TableauClient>("prod").Server.GetInfoAsync();

        Assert.AreEqual("finance", options.Site);
        Assert.AreEqual(new Version(3, 27), options.ApiVersion);
        Assert.AreEqual(TableauDeployment.Server, options.Deployment);
        Assert.IsTrue(options.AllowUnsupportedServerVersion);
        Assert.AreEqual(TimeSpan.FromSeconds(45), options.RequestTimeout);
        Assert.AreEqual(TimeSpan.FromHours(1), options.TransferTimeout);
        Assert.AreEqual("GET /tableau/api/3.27/serverinfo HTTP/1.1", server.Requests.Single().RequestLine);
        Assert.ThrowsExactly<InvalidOperationException>(() => options.Site = "other");
    }

    [TestMethod]
    public void BadOptionsAreRefusedAtStartUp()
    {
        var services = Services();
        services.AddTableauClient("prod", options => options.ServerUrl = new Uri("http://tableau.example.com/"));
        services.AddTableauClient("none", _ => { });
        using var provider = services.BuildServiceProvider();

        var thrown = Assert.ThrowsExactly<AggregateException>(() => provider.GetRequiredService<IStartupValidator>().Validate());

        var failures = thrown.InnerExceptions.Cast<OptionsValidationException>().ToDictionary(inner => inner.OptionsName, inner => inner.Message);
        Assert.Contains("ServerUrl is plain http", failures["prod"]);
        Assert.Contains("The none.ServerUrl field is required", failures["none"]);
    }

    [TestMethod]
    public void BadOptionsAreRefusedWhenAClientIsMadeWithoutAHost()
    {
        var services = Services();
        services.AddTableauClient("prod", options => options.ServerUrl = new Uri("tableau", UriKind.Relative));
        using var provider = services.BuildServiceProvider();

        var thrown = Assert.ThrowsExactly<OptionsValidationException>(() => provider.GetRequiredKeyedService<TableauClient>("prod"));

        Assert.Contains("ServerUrl 'tableau' is relative", thrown.Message);
    }

    [TestMethod]
    public void TheBuilderNamesTheClientAndItsServices()
    {
        var services = Services();

        var named = services.AddTableauClient("prod", _ => { });
        var unnamed = services.AddTableauClient(_ => { });

        Assert.AreEqual("prod", named.Name);
        Assert.AreEqual("", unnamed.Name);
        Assert.AreSame(services, named.Services);
        Assert.ThrowsExactly<ArgumentNullException>(() => services.AddTableauClient(null!, _ => { }));
        Assert.ThrowsExactly<ArgumentNullException>(() => services.AddTableauClient("prod", null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => ((IServiceCollection)null!).AddTableauClient(_ => { }));
    }

    [TestMethod]
    public async Task TheClientLogsThroughTheContainersLoggerFactory()
    {
        using var server = FakeTableauServer.Start();
        server.Map("GET", ClientFixtures.ServerInfoPath, ClientFixtures.ServerInfo());
        var log = new RecordingLogger();
        var services = new ServiceCollection();
        services.AddSingleton(log.Factory);
        services.AddTableauClient("prod", options => Copy(ClientFixtures.Options(server), options));
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        await scope.ServiceProvider.GetRequiredKeyedService<TableauClient>("prod").Server.GetInfoAsync();

        Assert.IsTrue(log.At(LogLevel.Debug).Any(line => line.Contains($"Tableau client prod: GET {ClientFixtures.ServerInfoPath} answered 200", StringComparison.Ordinal)),
            string.Join("\n", log.Lines));
        Assert.IsFalse(log.Lines.Any(line => line.Contains("Start processing HTTP request", StringComparison.Ordinal)),
            "the HttpClient factory's own request lines are off");
    }

    private static ServiceCollection Services()
    {
        var services = new ServiceCollection();
        services.AddSingleton(Fixtures.Log.Factory);
        return services;
    }

    private static void Copy(TableauClientOptions from, TableauClientOptions to)
    {
        to.ServerUrl = from.ServerUrl;
        to.Site = from.Site;
        to.ApiVersion = from.ApiVersion;
        to.Deployment = from.Deployment;
        to.AllowInsecureHttp = from.AllowInsecureHttp;
        to.AllowUnsupportedServerVersion = from.AllowUnsupportedServerVersion;
        to.DangerousAcceptAnyServerCertificate = from.DangerousAcceptAnyServerCertificate;
        to.RequestTimeout = from.RequestTimeout;
        to.TransferTimeout = from.TransferTimeout;
    }
}
