using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Tableau.Api.Pipeline;
using Tableau.Api.Rest.Models;
using Tableau.Api.Tests.Fakes;

namespace Tableau.Api.Tests;

/// <summary>
/// The client's options, checked when a client is built and read-only from then on; the deployment Auto picks from the
/// host; and the client's own lifetime: Create's owns its connections, a caller's HttpClient is never disposed, and a
/// mock derives from the protected constructors.
/// </summary>
[TestClass]
public sealed class ClientOptionsTests
{
    [TestMethod]
    public void TheDefaultsNegotiateTheVersionAndCheckEverything()
    {
        var options = new TableauClientOptions();

        Assert.IsNull(options.ServerUrl);
        Assert.AreEqual("", options.Site);
        Assert.IsNull(options.ApiVersion);
        Assert.AreEqual(TableauDeployment.Auto, options.Deployment);
        Assert.IsFalse(options.AllowUnsupportedServerVersion);
        Assert.IsFalse(options.DangerousAcceptAnyServerCertificate);
        Assert.IsFalse(options.AllowInsecureHttp);
        Assert.AreEqual(TimeSpan.FromMinutes(2), options.RequestTimeout);
        Assert.AreEqual(Timeout.InfiniteTimeSpan, options.TransferTimeout);
        Assert.AreEqual(3, options.MaxRetries);
        Assert.AreEqual(TimeSpan.FromMinutes(2), options.MaxRetryAfter);
    }

    [TestMethod]
    [DataRow("no url", "ServerUrl field is required")]
    [DataRow("relative", "ServerUrl 'tableau/' is relative")]
    [DataRow("ftp", "ServerUrl's scheme is ftp")]
    [DataRow("http", "ServerUrl is plain http")]
    [DataRow("user info", "ServerUrl holds a user name or a password")]
    [DataRow("query", "ServerUrl has a query or a fragment")]
    [DataRow("fragment", "ServerUrl has a query or a fragment")]
    [DataRow("site", "Site is null")]
    [DataRow("version", "ApiVersion 3.25.1 names more than a major and a minor version")]
    [DataRow("deployment", "Deployment 7 is none of Auto, Server and Cloud")]
    [DataRow("zero timeout", "RequestTimeout is 00:00:00")]
    [DataRow("negative timeout", "TransferTimeout is -00:00:01")]
    [DataRow("long timeout", "RequestTimeout is 30.00:00:00")]
    [DataRow("negative retries", "MaxRetries is -1: give 0, for no retries, or more")]
    [DataRow("negative retry after", "MaxRetryAfter is -00:00:01")]
    [DataRow("long retry after", "MaxRetryAfter is 30.00:00:00")]
    public void BadOptionsAreRefusedWhenAClientIsBuilt(string what, string message)
    {
        var options = new TableauClientOptions { ServerUrl = new Uri("https://tableau.example.com/") };
        switch (what)
        {
            case "no url": options.ServerUrl = null; break;
            case "relative": options.ServerUrl = new Uri("tableau/", UriKind.Relative); break;
            case "ftp": options.ServerUrl = new Uri("ftp://tableau.example.com/"); break;
            case "http": options.ServerUrl = new Uri("http://tableau.example.com/"); break;
            case "user info": options.ServerUrl = new Uri("https://ada:invented-password@tableau.example.com/"); break;
            case "query": options.ServerUrl = new Uri("https://tableau.example.com/?x=1"); break;
            case "fragment": options.ServerUrl = new Uri("https://tableau.example.com/#/site/finance"); break;
            case "site": options.Site = null!; break;
            case "version": options.ApiVersion = new Version(3, 25, 1); break;
            case "deployment": options.Deployment = (TableauDeployment)7; break;
            case "zero timeout": options.RequestTimeout = TimeSpan.Zero; break;
            case "negative timeout": options.TransferTimeout = TimeSpan.FromSeconds(-1); break;
            case "long timeout": options.RequestTimeout = TimeSpan.FromDays(30); break;
            case "negative retries": options.MaxRetries = -1; break;
            case "negative retry after": options.MaxRetryAfter = TimeSpan.FromSeconds(-1); break;
            case "long retry after": options.MaxRetryAfter = TimeSpan.FromDays(30); break;
        }

        var thrown = Assert.ThrowsExactly<ArgumentException>(() => TableauClient.Create(options));

        Assert.AreEqual("options", thrown.ParamName);
        Assert.Contains(message, thrown.Message);
        Assert.DoesNotContain("invented-password", thrown.Message);
    }

    [TestMethod]
    public async Task OptionsAreReadOnlyOnceAClientIsBuiltFromThem()
    {
        var options = new TableauClientOptions { ServerUrl = new Uri("https://tableau.example.com/") };

        await using var client = TableauClient.Create(options);

        Assert.ThrowsExactly<InvalidOperationException>(() => options.ServerUrl = new Uri("https://elsewhere.example.com/"));
        Assert.ThrowsExactly<InvalidOperationException>(() => options.DangerousAcceptAnyServerCertificate = true);
        Assert.ThrowsExactly<InvalidOperationException>(() => options.ApiVersion = new Version(3, 25));
        Assert.ThrowsExactly<InvalidOperationException>(() => options.MaxRetries = 0);
        Assert.ThrowsExactly<InvalidOperationException>(() => options.MaxRetryAfter = TimeSpan.Zero);
        Assert.AreEqual(new Uri("https://tableau.example.com/"), options.ServerUrl);
    }

    [TestMethod]
    [DataRow("https://online.tableau.com/", TableauDeployment.Cloud)]
    [DataRow("https://10ax.online.tableau.com/", TableauDeployment.Cloud)]
    [DataRow("https://PROD-USEAST-A.online.tableau.com/", TableauDeployment.Cloud)]
    [DataRow("https://tableau.example.com/", TableauDeployment.Server)]
    [DataRow("https://notonline.tableau.com/", TableauDeployment.Server)]
    [DataRow("https://online.tableau.com.example.com/", TableauDeployment.Server)]
    public void AutoTakesCloudForTableauCloudsHosts(string url, TableauDeployment expected)
    {
        var state = new TableauClientState("", new TableauClientOptions { ServerUrl = new Uri(url) }, null);

        Assert.AreEqual(expected, state.Deployment);
    }

    [TestMethod]
    [DataRow(TableauDeployment.Server)]
    [DataRow(TableauDeployment.Cloud)]
    public void ADeploymentSetIsKept(TableauDeployment deployment)
    {
        var state = new TableauClientState("", new TableauClientOptions
        {
            ServerUrl = new Uri("https://10ax.online.tableau.com/"),
            Deployment = deployment,
        }, null);

        Assert.AreEqual(deployment, state.Deployment);
    }

    [TestMethod]
    public async Task ADisposedClientRefusesCallsAndACallersHttpClientStaysOpen()
    {
        using var server = FakeTableauServer.Start();
        server.Map("GET", ClientFixtures.ServerInfoPath, ClientFixtures.ServerInfo());
        using var handler = new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false };
        using var http = new HttpClient(handler, disposeHandler: false);
        var created = ClientFixtures.Client(server);
        var overCallers = new TableauClient(http, ClientFixtures.Options(server), Fixtures.Log.Factory);

        var disposedSync = ClientFixtures.Client(server);

        await created.Server.GetInfoAsync();
        await created.DisposeAsync();
        await created.DisposeAsync();
        created.Dispose();
        await overCallers.DisposeAsync();
        disposedSync.Dispose();
        disposedSync.Dispose();

        await Assert.ThrowsExactlyAsync<ObjectDisposedException>(() => created.Server.GetInfoAsync());
        await Assert.ThrowsExactlyAsync<ObjectDisposedException>(() => overCallers.Server.GetInfoAsync());
        await Assert.ThrowsExactlyAsync<ObjectDisposedException>(() => disposedSync.Server.GetInfoAsync());
        Assert.IsTrue((await http.GetAsync(new Uri(server.BaseUrl, ClientFixtures.ServerInfoPath))).IsSuccessStatusCode);
    }

    [TestMethod]
    public void ACallersHttpClientCantTakeTheCertificateOptOut()
    {
        using var http = new HttpClient();
        var options = new TableauClientOptions
        {
            ServerUrl = new Uri("https://tableau.example.com/"),
            DangerousAcceptAnyServerCertificate = true,
        };

        var thrown = Assert.ThrowsExactly<ArgumentException>(() => new TableauClient(http, options));

        Assert.Contains("set the certificate check on its handler", thrown.Message);
        Assert.ThrowsExactly<ArgumentNullException>(() => new TableauClient(null!, options));
        Assert.ThrowsExactly<ArgumentNullException>(() => new TableauClient(http, (TableauClientOptions)null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => TableauClient.Create(null!));
    }

    [TestMethod]
    public async Task AMockDerivesFromTheProtectedConstructors()
    {
        await using var client = new MockClient();

        var info = await client.Server.GetInfoAsync();
        await using var bare = new BareClient();

        Assert.AreEqual("3.99", info.RestApiVersion);
        Assert.ThrowsExactly<InvalidOperationException>(() => bare.Server);
        Assert.ThrowsExactly<InvalidOperationException>(() => bare.Pipeline);
        Assert.ThrowsExactly<ArgumentException>(() => new TableauClientState("", new TableauClientOptions(), null));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => new BareServerClient().GetInfoAsync());
    }

    private sealed class MockClient : TableauClient
    {
        public override ServerClient Server { get; } = new MockServerClient();
    }

    private sealed class MockServerClient : ServerClient
    {
        public override Task<ServerInfo> GetInfoAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new ServerInfo { RestApiVersion = "3.99" });
    }

    private sealed class BareClient : TableauClient;

    private sealed class BareServerClient : ServerClient;
}
