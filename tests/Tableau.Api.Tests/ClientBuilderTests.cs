using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net.Http;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Polly;
using Tableau.Api.Tests.Fakes;

namespace Tableau.Api.Tests;

/// <summary>
/// What <c>AddTableauClient</c>'s builder adds: the caller's settings on the primary handler, with the library's rules
/// set after them; the caller's handlers, outside the retries, the first added outermost; both kept by a second
/// registration of the name. And the same retries without DI, over the client's own connection or a caller's HttpClient.
/// </summary>
[TestClass]
public sealed class ClientBuilderTests
{
    private const string ItemsPath = "/api/3.25/items";

    [TestMethod]
    public async Task TheCallersHandlersRunOutsideTheRetriesTheFirstOutermost()
    {
        using var server = FakeTableauServer.Start();
        server.Map("GET", ClientFixtures.ServerInfoPath, ResilienceTests.InTurn(
            FakeResponse.Empty(503).WithHeader("Retry-After", "0"), ClientFixtures.ServerInfo()));
        var order = new ConcurrentQueue<string>();
        var services = Services();
        services.AddTableauClient("prod", options => Copy(ClientFixtures.Options(server, maxRetries: 3), options))
            .AddHttpMessageHandler(_ => new Recording("outer", order))
            .AddHttpMessageHandler(_ => new Recording("inner", order));
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        await scope.ServiceProvider.GetRequiredKeyedService<TableauClient>("prod").Server.GetInfoAsync();

        CollectionAssert.AreEqual(new[] { "outer sends", "inner sends", "inner got 200", "outer got 200" }, order.ToArray());
        Assert.HasCount(2, server.Requests);
    }

    [TestMethod]
    public async Task ASecondRegistrationOfTheNameKeepsWhatTheFirstAdded()
    {
        using var server = FakeTableauServer.Start();
        server.Map("GET", ClientFixtures.ServerInfoPath, ClientFixtures.ServerInfo());
        var order = new ConcurrentQueue<string>();
        var primary = new ConcurrentQueue<string>();
        var services = Services();
        services.AddTableauClient("prod", options => Copy(ClientFixtures.Options(server), options))
            .AddHttpMessageHandler(_ => new Recording("first", order))
            .ConfigurePrimaryHandler(_ => primary.Enqueue("first"));
        services.AddTableauClient("prod", options => options.Site = "")
            .AddHttpMessageHandler(_ => new Recording("second", order))
            .ConfigurePrimaryHandler(_ => primary.Enqueue("second"));
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        await scope.ServiceProvider.GetRequiredKeyedService<TableauClient>("prod").Server.GetInfoAsync();

        CollectionAssert.AreEqual(new[] { "first sends", "second sends", "second got 200", "first got 200" }, order.ToArray());
        CollectionAssert.AreEqual(new[] { "first", "second" }, primary.ToArray());
        Assert.HasCount(1, server.Requests);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void TheLibrarysRulesAreSetAfterTheCallersSettings(bool acceptAny)
    {
        var services = Services();
        services.AddTableauClient("prod", options =>
            {
                options.ServerUrl = new Uri("https://tableau.example.com/");
                options.DangerousAcceptAnyServerCertificate = acceptAny;
            })
            .ConfigurePrimaryHandler(handler =>
            {
                handler.AllowAutoRedirect = true;
                handler.UseCookies = true;
                handler.ConnectTimeout = TimeSpan.FromSeconds(7);
                handler.SslOptions.RemoteCertificateValidationCallback = (_, _, _, _) => !acceptAny;
                handler.SslOptions.CertificateChainPolicy = null;
            });
        using var provider = services.BuildServiceProvider();

        using var handler = PrimaryHandler(provider, "prod");

        Assert.IsFalse(handler.AllowAutoRedirect);
        Assert.IsFalse(handler.UseCookies);
        Assert.AreEqual(TimeSpan.FromSeconds(7), handler.ConnectTimeout);
        Assert.AreEqual(X509RevocationMode.Online, handler.SslOptions.CertificateChainPolicy?.RevocationMode);
        if (acceptAny)
            Assert.IsTrue(handler.SslOptions.RemoteCertificateValidationCallback!(null!, null, null, System.Net.Security.SslPolicyErrors.RemoteCertificateChainErrors));
        else
            Assert.IsNull(handler.SslOptions.RemoteCertificateValidationCallback);
    }

    [TestMethod]
    public async Task APrivateCertificateAuthorityIsTrustedThroughThePrimaryHandler()
    {
        using var authority = new ClientCertificateTests.TestAuthority();
        using var server = FakeTableauServer.StartTls(authority.Leaf($"http://127.0.0.1:{ClientFixtures.ClosedPort()}/{Guid.NewGuid():N}.crl", 0x01));
        server.Map("GET", ClientFixtures.ServerInfoPath, ClientFixtures.ServerInfo());
        var services = Services();
        services.AddTableauClient("trusting", options => Copy(ClientFixtures.Options(server), options))
            .ConfigurePrimaryHandler(handler => handler.SslOptions.CertificateChainPolicy = new X509ChainPolicy
            {
                TrustMode = X509ChainTrustMode.CustomRootTrust,
                CustomTrustStore = { authority.Root },
                RevocationMode = X509RevocationMode.NoCheck,
            });
        services.AddTableauClient("default", options => Copy(ClientFixtures.Options(server), options));
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        await scope.ServiceProvider.GetRequiredKeyedService<TableauClient>("trusting").Server.GetInfoAsync();
        var refused = await ClientFixtures.FailsAsync(TableauErrorKind.Http,
            () => scope.ServiceProvider.GetRequiredKeyedService<TableauClient>("default").Server.GetInfoAsync());

        Assert.HasCount(1, server.Requests);
        Assert.IsInstanceOfType<AuthenticationException>(refused.InnerException?.InnerException);
    }

    [TestMethod]
    public async Task AHandlersOwnExceptionFailsTheCallAsHttpWithItsCause()
    {
        using var server = FakeTableauServer.Start();
        var services = Services();
        services.AddTableauClient("prod", options => Copy(ClientFixtures.Options(server), options))
            .AddHttpMessageHandler(_ => new Throwing(new InvalidOperationException("Invented: the handler refuses.")));
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        var thrown = await ClientFixtures.FailsAsync(TableauErrorKind.Http,
            () => scope.ServiceProvider.GetRequiredKeyedService<TableauClient>("prod").Server.GetInfoAsync());

        Assert.IsInstanceOfType<InvalidOperationException>(thrown.InnerException?.InnerException);
        Assert.Contains("A handler failed the request: Invented: the handler refuses.", thrown.Message);
        Assert.IsEmpty(server.Requests);
    }

    [TestMethod]
    public async Task AHandlersOwnTimeoutIsATimeout()
    {
        using var server = FakeTableauServer.Start();
        var timeout = new Polly.Timeout.TimeoutRejectedException("Invented: the caller's own timeout.");
        var services = Services();
        services.AddTableauClient("prod", options => Copy(ClientFixtures.Options(server), options))
            .AddHttpMessageHandler(_ => new Throwing(timeout));
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        var thrown = await ClientFixtures.FailsAsync(TableauErrorKind.Timeout,
            () => scope.ServiceProvider.GetRequiredKeyedService<TableauClient>("prod").Server.GetInfoAsync());

        Assert.AreSame(timeout, thrown.InnerException);
    }

    [TestMethod]
    public async Task TheNamedHttpClientSentOnItsOwnReadsItsAnswerWhole()
    {
        using var server = FakeTableauServer.Start();
        server.Map("GET", ItemsPath, ClientFixtures.ServerInfo());
        var services = Services();
        services.AddTableauClient("prod", options => Copy(ClientFixtures.Options(server), options));
        await using var provider = services.BuildServiceProvider();
        using var http = provider.GetRequiredService<IHttpClientFactory>().CreateClient(TableauServiceCollectionExtensions.HttpClientName("prod"));

        using var response = await http.GetAsync(new Uri(server.BaseUrl, ItemsPath), HttpCompletionOption.ResponseHeadersRead);

        Assert.IsTrue(response.IsSuccessStatusCode);
        Assert.Contains("2025.1.0", await response.Content.ReadAsStringAsync());
    }

    [TestMethod]
    public async Task AHandlerOfYourOwnRetriesWhenTheClientsRetriesAreOff()
    {
        using var server = FakeTableauServer.Start();
        server.Map("GET", ClientFixtures.ServerInfoPath, ResilienceTests.InTurn(FakeResponse.Empty(503), ClientFixtures.ServerInfo()));
        var services = Services();
        services.AddTableauClient("prod", options => Copy(ClientFixtures.Options(server, maxRetries: 0), options))
            .AddHttpMessageHandler(_ => new RetryingOnce());
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        await scope.ServiceProvider.GetRequiredKeyedService<TableauClient>("prod").Server.GetInfoAsync();

        Assert.HasCount(2, server.Requests);
    }

    [TestMethod]
    public async Task TheContainersClockIsTheOneTheRetriesWaitOn()
    {
        using var server = FakeTableauServer.Start();
        server.Map("GET", ClientFixtures.ServerInfoPath, ResilienceTests.InTurn(
            FakeResponse.Empty(429).WithHeader("Retry-After", "30"), ClientFixtures.ServerInfo()));
        var time = new FakeTimeProvider();
        var services = Services();
        services.AddSingleton<TimeProvider>(time);
        services.AddTableauClient("prod", options => Copy(ClientFixtures.Options(server, maxRetries: 3), options));
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        var call = scope.ServiceProvider.GetRequiredKeyedService<TableauClient>("prod").Server.GetInfoAsync();
        await ClientFixtures.WaitUntilAsync(() => server.Requests.Count == 1);
        var waited = await ClientFixtures.AdvanceUntilAsync(time, () => server.Requests.Count == 2, TimeSpan.FromSeconds(1), TimeSpan.FromMinutes(2));
        await call;

        Assert.IsGreaterThanOrEqualTo(TimeSpan.FromSeconds(30), waited);
    }

    [TestMethod]
    public async Task ACallersHttpClientIsSentThroughOnceAnAttempt()
    {
        using var server = FakeTableauServer.Start();
        server.Map("GET", ItemsPath, ResilienceTests.InTurn(FakeResponse.Empty(503).WithHeader("Retry-After", "0"), ClientFixtures.ServerInfo()));
        var order = new ConcurrentQueue<string>();
        using var primary = new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false };
        using var recording = new Recording("caller's", order, primary);
        using var http = new HttpClient(recording, disposeHandler: false);
        await using var client = new TableauClient(http, ClientFixtures.Options(server, maxRetries: 3), Fixtures.Log.Factory);

        await ClientFixtures.SendAsync(client, ClientFixtures.Endpoint("{version}/items"),
            new Pipeline.TableauRequest(Token: Fixtures.SessionToken));

        CollectionAssert.AreEqual(new[] { "caller's sends", "caller's got 503", "caller's sends", "caller's got 200" }, order.ToArray());
        Assert.IsTrue(server.Requests.All(request => request.Header("X-Tableau-Auth") == Fixtures.SessionToken));
        Assert.IsTrue((await http.GetAsync(new Uri(server.BaseUrl, ItemsPath))).IsSuccessStatusCode, "the caller's HttpClient stays open");
    }

    [TestMethod]
    public void ASettingThatThrowsFailsTheHandlerAndLeavesNoneOpen()
    {
        var options = new TableauClientOptions { ServerUrl = new Uri("https://tableau.example.com/") };

        var thrown = Assert.ThrowsExactly<InvalidOperationException>(() => Pipeline.TableauHandler.Create(options,
            [_ => throw new InvalidOperationException("Invented: a setting that fails.")]));

        Assert.AreEqual("Invented: a setting that fails.", thrown.Message);
    }

    // Each attempt sends a copy of the request through the caller's HttpClient; a copy that carried the client's own
    // resilience context would hand it to the caller's resilience handler, which would point it at that copy, and the
    // client's next attempt would send the copy, disposed.
    [TestMethod]
    public async Task ACallersHttpClientWithItsOwnResilienceHandlerIsRetriedAroundIt()
    {
        using var server = FakeTableauServer.Start();
        var unavailable = FakeResponse.Empty(503).WithHeader("Retry-After", "0");
        server.Map("PUT", ItemsPath, ResilienceTests.InTurn(unavailable, unavailable, unavailable, ClientFixtures.ServerInfo()));
        var theirs = new ResiliencePipelineBuilder<HttpResponseMessage>()
            .AddRetry(new Microsoft.Extensions.Http.Resilience.HttpRetryStrategyOptions { MaxRetryAttempts = 1, Delay = TimeSpan.Zero })
            .Build();
        using var primary = new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false };
        using var resilience = new Microsoft.Extensions.Http.Resilience.ResilienceHandler(theirs) { InnerHandler = primary };
        using var http = new HttpClient(resilience, disposeHandler: false);
        await using var client = new TableauClient(http, ClientFixtures.Options(server, maxRetries: 3), Fixtures.Log.Factory);

        // An update of a resource, safe to send twice, with a body every attempt must carry.
        var update = new Pipeline.Endpoint("Test update", HttpMethod.Put, "{version}/items", new Version(2, 0), Pipeline.TableauDeployments.Both,
            Idempotent: true, Pipeline.TransferKind.None);
        var body = new Rest.Models.TsRequest { Project = new Rest.Models.Project { Name = "Invented project" } };

        var info = await ClientFixtures.SendAsync(client, update, new Pipeline.TableauRequest(Body: body));

        Assert.AreEqual("2025.1.0", info.ProductVersion?.Value);
        Assert.HasCount(4, server.Requests);
        Assert.IsTrue(server.Requests.All(request => request.BodyText.Contains("Invented project", StringComparison.Ordinal)),
            "every attempt sent the body");
    }

    [TestMethod]
    public void TheBuildersMethodsRefuseNull()
    {
        var builder = Services().AddTableauClient("prod", _ => { });

        Assert.ThrowsExactly<ArgumentNullException>(() => builder.ConfigurePrimaryHandler(null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => builder.AddHttpMessageHandler(null!));
        Assert.AreSame(builder, builder.ConfigurePrimaryHandler(_ => { }));
        Assert.AreSame(builder, builder.AddHttpMessageHandler(_ => new Recording("unused", new ConcurrentQueue<string>())));
    }

    /// <summary>The primary handler the factory builds for a client's name, through the actions registered for it.</summary>
    private static SocketsHttpHandler PrimaryHandler(ServiceProvider provider, string name)
    {
        var builder = provider.GetRequiredService<HttpMessageHandlerBuilder>();
        builder.Name = TableauServiceCollectionExtensions.HttpClientName(name);
        foreach (var action in provider.GetRequiredService<IOptionsMonitor<HttpClientFactoryOptions>>().Get(builder.Name).HttpMessageHandlerBuilderActions)
            action(builder);
        foreach (var handler in builder.AdditionalHandlers)
            handler.Dispose();
        return (SocketsHttpHandler)builder.PrimaryHandler;
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
        to.ApiVersion = from.ApiVersion;
        to.AllowInsecureHttp = from.AllowInsecureHttp;
        to.RequestTimeout = from.RequestTimeout;
        to.MaxRetries = from.MaxRetries;
    }

    /// <summary>A handler that records each request it passes on, and the status that comes back, in a shared order.</summary>
    private sealed class Recording : DelegatingHandler
    {
        private readonly string _name;
        private readonly ConcurrentQueue<string> _order;

        public Recording(string name, ConcurrentQueue<string> order)
        {
            _name = name;
            _order = order;
        }

        public Recording(string name, ConcurrentQueue<string> order, HttpMessageHandler inner)
            : base(inner)
        {
            _name = name;
            _order = order;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            _order.Enqueue($"{_name} sends");
            var response = await base.SendAsync(request, cancellationToken);
            _order.Enqueue($"{_name} got {(int)response.StatusCode}");
            return response;
        }
    }

    /// <summary>A handler that fails every request with the exception given.</summary>
    private sealed class Throwing(Exception exception) : DelegatingHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw exception;
    }

    /// <summary>A caller's own resilience: a 503 is sent once more.</summary>
    private sealed class RetryingOnce : DelegatingHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = await base.SendAsync(request, cancellationToken);
            if ((int)response.StatusCode != 503) return response;
            response.Dispose();
            return await base.SendAsync(request, cancellationToken);
        }
    }
}
