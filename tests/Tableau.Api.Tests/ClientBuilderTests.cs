using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Security;
using System.Reflection;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Microsoft.VisualStudio.TestTools.UnitTesting;
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

        Assert.IsInstanceOfType<InvalidOperationException>(thrown.InnerException);
        Assert.Contains("got no answer: a handler failed it: Invented: the handler refuses.", thrown.Message);
        Assert.IsEmpty(server.Requests);
    }

    // A handler's own timeout or cancellation is no timeout of the client's: the call fails as Http, with it as the cause,
    // even one signalled as an HttpClient signals its Timeout, which the named HttpClient, its Timeout infinite, never does.
    [TestMethod]
    [DataRow("Polly")]
    [DataRow("cancellation")]
    [DataRow("HttpClient")]
    public async Task AHandlersOwnTimeoutOrCancellationIsHttpWithItsCause(string kind)
    {
        using var server = FakeTableauServer.Start();
        Exception own = kind switch
        {
            "Polly" => new Polly.Timeout.TimeoutRejectedException("Invented: the caller's own timeout."),
            "cancellation" => new OperationCanceledException("Invented: the caller's own cancellation."),
            _ => new TaskCanceledException("Invented: the caller's own timeout, as HttpClient says it.", new TimeoutException()),
        };
        var services = Services();
        services.AddTableauClient("prod", options => Copy(ClientFixtures.Options(server, maxRetries: 3), options))
            .AddHttpMessageHandler(_ => new Throwing(own));
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        var thrown = await ClientFixtures.FailsAsync(TableauErrorKind.Http,
            () => scope.ServiceProvider.GetRequiredKeyedService<TableauClient>("prod").Server.GetInfoAsync());

        Assert.AreSame(own, thrown.InnerException);
        Assert.Contains($"Server Info (GET {ClientFixtures.ServerInfoPath}) got no answer: a handler", thrown.Message);
        Assert.Contains(own.Message, thrown.Message);
        Assert.IsEmpty(server.Requests);
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

        // Read whole by the library's handler, though the HttpClient was asked for the headers alone: a buffered answer
        // reads as a stream that can seek, one from the wire as one that can't.
        await using var content = await response.Content.ReadAsStreamAsync();
        Assert.IsTrue(response.IsSuccessStatusCode);
        Assert.IsTrue(content.CanSeek, "the answer was read whole within its attempt");
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
        await using var client = new TableauClient(http, ClientFixtures.Options(server, maxRetries: 3), loggerFactory: Fixtures.Log.Factory);

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
        SocketsHttpHandler? made = null;

        var thrown = Assert.ThrowsExactly<InvalidOperationException>(() => Pipeline.TableauHandler.Create(options,
        [
            handler =>
            {
                made = handler;
                throw new InvalidOperationException("Invented: a setting that fails.");
            },
        ]));

        Assert.AreEqual("Invented: a setting that fails.", thrown.Message);
        Assert.ThrowsExactly<ObjectDisposedException>(() => made!.ConnectTimeout = TimeSpan.FromSeconds(1), "the handler was disposed");
    }

    // Each attempt sends a copy of the request through the caller's HttpClient, whose own handler may send it again: the
    // body is the original's, shared by every copy, and must reach the server each time.
    [TestMethod]
    public async Task ACallersHttpClientThatRetriesItselfIsRetriedAroundIt()
    {
        using var server = FakeTableauServer.Start();
        var unavailable = FakeResponse.Empty(503).WithHeader("Retry-After", "0");
        server.Map("PUT", ItemsPath, ResilienceTests.InTurn(unavailable, unavailable, unavailable, ClientFixtures.ServerInfo()));
        using var primary = new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false };
        using var retrying = new RetryingOnce { InnerHandler = primary };
        using var http = new HttpClient(retrying, disposeHandler: false);
        await using var client = new TableauClient(http, ClientFixtures.Options(server, maxRetries: 3), loggerFactory: Fixtures.Log.Factory);

        // An update of a resource, safe to send twice, with a body every attempt must carry.
        var update = new Pipeline.Endpoint("Test update", HttpMethod.Put, "{version}/items", new Version(2, 0), Pipeline.TableauDeployments.Both,
            Idempotent: true, Pipeline.TransferKind.None)
        { NeedsSession = false };
        var body = new Rest.Models.TsRequest { Project = new Rest.Models.Project { Name = "Invented project" } };

        var info = await ClientFixtures.SendAsync(client, update, new Pipeline.TableauRequest(Body: body));

        Assert.AreEqual("2025.1.0", info.ProductVersion?.Value);
        Assert.HasCount(4, server.Requests);
        Assert.IsTrue(server.Requests.All(request => request.BodyText.Contains("Invented project", StringComparison.Ordinal)),
            "every attempt sent the body");
    }

    // TLS options one action gives two clients' handlers: setting the rules on them in place made the client resolved
    // last decide both, so a client without DangerousAcceptAnyServerCertificate could accept any certificate.
    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task TlsOptionsTwoClientsShareKeepEachClientsCheck(bool testFirst)
    {
        using var server = FakeTableauServer.StartTls();
        server.Map("GET", ClientFixtures.ServerInfoPath, ClientFixtures.ServerInfo());
        var shared = new SslClientAuthenticationOptions();
        var services = Services();
        services.AddTableauClient("test", options => Copy(ClientFixtures.Options(server), options))
            .ConfigurePrimaryHandler(handler => handler.SslOptions = shared);
        services.AddTableauClient("prod", options =>
            {
                Copy(ClientFixtures.Options(server), options);
                options.DangerousAcceptAnyServerCertificate = true;
            })
            .ConfigurePrimaryHandler(handler => handler.SslOptions = shared);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var first = scope.ServiceProvider.GetRequiredKeyedService<TableauClient>(testFirst ? "test" : "prod");
        var second = scope.ServiceProvider.GetRequiredKeyedService<TableauClient>(testFirst ? "prod" : "test");
        var (test, prod) = testFirst ? (first, second) : (second, first);

        var refused = await ClientFixtures.FailsAsync(TableauErrorKind.Http, () => test.Server.GetInfoAsync());
        await prod.Server.GetInfoAsync();

        Assert.IsInstanceOfType<AuthenticationException>(refused.InnerException?.InnerException);
        Assert.HasCount(1, server.Requests);
        Assert.IsNull(shared.RemoteCertificateValidationCallback, "the shared options stay as the caller set them");
        Assert.IsNull(shared.CertificateChainPolicy, "the shared options stay as the caller set them");
    }

    // The copy names each member; a .NET release that adds one fails the count, and the copy is given it.
    [TestMethod]
    public void TheHandlersCopyOfTheTlsOptionsCarriesEveryMember()
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=Tableau.Api tests client", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddHours(1));
        var source = new SslClientAuthenticationOptions();
        var members = typeof(SslClientAuthenticationOptions).GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(property => property.SetMethod is { IsPublic: true })
            .ToList();
        var set = new List<string>();
        foreach (var member in members)
        {
            if (Changed(member.PropertyType, member.GetValue(source), certificate) is not { } value) continue;
            try
            {
                member.SetValue(source, value);
                set.Add(member.Name);
            }
            catch (TargetInvocationException e) when (e.InnerException is PlatformNotSupportedException)
            {
                // A member this platform can't take, such as an RSA padding of another system's.
            }
        }

        var copy = Pipeline.TableauHandler.Copy(source);

        Assert.HasCount(15, members, string.Join(", ", members.Select(member => member.Name)));
        Assert.IsGreaterThanOrEqualTo(12, set.Count, string.Join(", ", set));
        foreach (var member in members)
        {
            var expected = member.GetValue(source);
            var actual = member.GetValue(copy);
            if (member.Name == nameof(SslClientAuthenticationOptions.ApplicationProtocols) && expected is System.Collections.IEnumerable list)
            {
                Assert.AreNotSame(list, actual, member.Name);
                CollectionAssert.AreEqual(list.Cast<object>().ToList(), ((System.Collections.IEnumerable)actual!).Cast<object>().ToList(), member.Name);
            }
            else
            {
                Assert.AreEqual(expected, actual, member.Name);
            }
        }
        Assert.AreSame(source.ClientCertificates, copy.ClientCertificates);
    }

    // A value of the member's type other than the one it holds, or null for a type this platform can't make.
    private static object? Changed(Type type, object? current, X509Certificate2 certificate)
    {
        if (type == typeof(bool)) return !(bool)current!;
        if (type.IsEnum) return Enum.GetValues(type).Cast<object>().First(value => !value.Equals(current));
        if (type == typeof(string)) return "invented.example";
        if (type == typeof(List<SslApplicationProtocol>)) return new List<SslApplicationProtocol> { SslApplicationProtocol.Http2 };
        if (type == typeof(X509ChainPolicy)) return new X509ChainPolicy();
        if (type == typeof(X509CertificateCollection)) return new X509CertificateCollection { certificate };
        if (type == typeof(SslStreamCertificateContext)) return SslStreamCertificateContext.Create(certificate, null, offline: true);
        if (type == typeof(LocalCertificateSelectionCallback)) return new LocalCertificateSelectionCallback((_, _, _, _, _) => certificate);
        if (type == typeof(RemoteCertificateValidationCallback)) return new RemoteCertificateValidationCallback((_, _, _, errors) => errors == SslPolicyErrors.None);
        if (type == typeof(CipherSuitesPolicy))
        {
            try
            {
                return Activator.CreateInstance(type, [new[] { TlsCipherSuite.TLS_AES_128_GCM_SHA256 }]);
            }
            catch (TargetInvocationException e) when (e.InnerException is PlatformNotSupportedException)
            {
                return null;
            }
        }
        throw new AssertFailedException($"The test makes no value of {type}: a member .NET added, which the copy and this test must take.");
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
