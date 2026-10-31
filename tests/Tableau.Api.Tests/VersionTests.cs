using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Tableau.Api.Pipeline;
using Tableau.Api.Tests.Fakes;

namespace Tableau.Api.Tests;

/// <summary>
/// The REST API version a 2.0 client calls: the server's, asked once through Server Info and capped at 3.29, or the one
/// the options pin; refused below 3.23 unless the options allow it, with one warning then; and each call refused
/// before sending when it needs a newer version or another deployment.
/// </summary>
[TestClass]
public sealed class VersionTests
{
    [TestMethod]
    public async Task TheFirstCallAsksTheServersVersionOnceAndUsesIt()
    {
        using var server = FakeTableauServer.Start();
        server.Map("GET", ClientFixtures.NegotiationPath, ClientFixtures.ServerInfo("3.25"));
        server.Map("GET", ClientFixtures.ServerInfoPath, ClientFixtures.ServerInfo("3.25"));
        await using var client = ClientFixtures.Client(server, version: null);

        var info = await client.Server.GetInfoAsync();
        await client.Server.GetInfoAsync();

        Assert.AreEqual("3.25", info.RestApiVersion);
        CollectionAssert.AreEqual(
            new[] { $"GET {ClientFixtures.NegotiationPath} HTTP/1.1", $"GET {ClientFixtures.ServerInfoPath} HTTP/1.1", $"GET {ClientFixtures.ServerInfoPath} HTTP/1.1" },
            ClientFixtures.Lines(server));
    }

    [TestMethod]
    [DataRow("3.30", "3.29")]
    [DataRow("4.2", "3.29")]
    [DataRow("3.29", "3.29")]
    [DataRow("3.23", "3.23")]
    [DataRow("3.25.1", "3.25")]
    public async Task ANewerServerIsCalledAtTheNewestVersionTheClientKnows(string offered, string used)
    {
        using var server = FakeTableauServer.Start();
        server.Map("GET", ClientFixtures.NegotiationPath, ClientFixtures.ServerInfo(offered));
        server.Map("GET", $"/api/{used}/serverinfo", ClientFixtures.ServerInfo(offered));
        var log = new RecordingLogger();
        await using var client = TableauClient.Create(ClientFixtures.Options(server, version: null), log.Factory);

        await client.Server.GetInfoAsync();

        Assert.AreEqual($"GET /api/{used}/serverinfo HTTP/1.1", ClientFixtures.Lines(server)[^1]);
        Assert.IsTrue(log.At(LogLevel.Debug).Any(line => line.Contains($"offers REST API {ApiVersions.Parse(offered)}; the client uses {used}", StringComparison.Ordinal)),
            string.Join("\n", log.Lines));
    }

    [TestMethod]
    public async Task AServerBelowTheFloorIsRefusedNamingItsVersionAndNothingElseIsSent()
    {
        using var server = OldServer();
        await using var client = ClientFixtures.Client(server, version: null);

        var thrown = await ClientFixtures.FailsAsync(TableauErrorKind.UnsupportedApiVersion, () => client.Server.GetInfoAsync());
        await ClientFixtures.FailsAsync(TableauErrorKind.UnsupportedApiVersion, () => client.Server.GetInfoAsync());

        Assert.Contains("REST API 3.21 (Tableau 2023.3.4) is below 3.23", thrown.Message);
        Assert.Contains("AllowUnsupportedServerVersion", thrown.Message);
        CollectionAssert.AreEqual(new[] { $"GET {ClientFixtures.NegotiationPath} HTTP/1.1", "GET /api/2.4/serverinfo HTTP/1.1" },
            ClientFixtures.Lines(server));
    }

    [TestMethod]
    public async Task AServerBelowTheFloorWithoutAProductVersionIsNamedByItsApiVersion()
    {
        using var server = FakeTableauServer.Start();
        server.Map("GET", ClientFixtures.NegotiationPath, FakeResponse.Json(200, """{"serverInfo":{"restApiVersion":"3.19"}}"""));
        await using var client = ClientFixtures.Client(server, version: null);

        var thrown = await ClientFixtures.FailsAsync(TableauErrorKind.UnsupportedApiVersion, () => client.Server.GetInfoAsync());

        Assert.StartsWith("REST API 3.19 is below 3.23 (Tableau Server 2024.2)", thrown.Message);
    }

    [TestMethod]
    public async Task AServerBelowTheFloorIsCalledWhenAllowedWithOneWarning()
    {
        using var server = OldServer();
        server.Map("GET", "/api/3.21/serverinfo", ClientFixtures.ServerInfo("3.21", "2023.3.4"));
        var log = new RecordingLogger();
        var options = ClientFixtures.Options(server, version: null);
        options.AllowUnsupportedServerVersion = true;
        await using var client = TableauClient.Create(options, log.Factory);

        await client.Server.GetInfoAsync();
        var info = await client.Server.GetInfoAsync();

        Assert.AreEqual("3.21", info.RestApiVersion);
        Assert.AreEqual("GET /api/3.21/serverinfo HTTP/1.1", ClientFixtures.Lines(server)[^1]);
        var warning = log.At(LogLevel.Warning).Single(line => line.Contains("below", StringComparison.Ordinal));
        Assert.Contains("REST API 3.21 is below 3.23", warning);
    }

    [TestMethod]
    public async Task APinnedVersionIsCalledWithoutAskingTheServer()
    {
        using var server = FakeTableauServer.Start();
        server.Map("GET", "/api/3.27/serverinfo", ClientFixtures.ServerInfo());
        await using var client = ClientFixtures.Client(server, version: "3.27");

        await client.Server.GetInfoAsync();

        CollectionAssert.AreEqual(new[] { "GET /api/3.27/serverinfo HTTP/1.1" }, ClientFixtures.Lines(server));
    }

    [TestMethod]
    public async Task APinnedVersionAboveTheNewestIsCalledAsGiven()
    {
        using var server = FakeTableauServer.Start();
        server.Map("GET", "/api/3.31/serverinfo", ClientFixtures.ServerInfo("3.31"));
        await using var client = ClientFixtures.Client(server, version: "3.31");

        await client.Server.GetInfoAsync();

        CollectionAssert.AreEqual(new[] { "GET /api/3.31/serverinfo HTTP/1.1" }, ClientFixtures.Lines(server));
    }

    [TestMethod]
    public async Task APinnedVersionBelowTheFloorIsRefusedBeforeSendingUnlessAllowed()
    {
        using var server = FakeTableauServer.Start();
        server.Map("GET", "/api/3.22/serverinfo", ClientFixtures.ServerInfo("3.22"));
        await using var refusing = ClientFixtures.Client(server, version: "3.22");
        var options = ClientFixtures.Options(server, version: "3.22");
        options.AllowUnsupportedServerVersion = true;
        var log = new RecordingLogger();
        await using var allowing = TableauClient.Create(options, log.Factory);

        var thrown = await ClientFixtures.FailsAsync(TableauErrorKind.UnsupportedApiVersion, () => refusing.Server.GetInfoAsync());
        Assert.IsEmpty(server.Requests);
        await allowing.Server.GetInfoAsync();
        await allowing.Server.GetInfoAsync();

        Assert.Contains("REST API 3.22 is below 3.23", thrown.Message);
        Assert.HasCount(2, server.Requests);
        Assert.HasCount(1, log.At(LogLevel.Warning).Where(line => line.Contains("below", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task AFailedNegotiationIsAskedAgainOnTheNextCall()
    {
        using var server = FakeTableauServer.Start();
        var answers = 0;
        server.Map("GET", ClientFixtures.NegotiationPath, _ =>
            ++answers == 1 ? FakeResponse.Text(500, "invented outage") : ClientFixtures.ServerInfo("3.25"));
        server.Map("GET", ClientFixtures.ServerInfoPath, ClientFixtures.ServerInfo());
        await using var client = ClientFixtures.Client(server, version: null);

        await ClientFixtures.FailsAsync(TableauErrorKind.Http, () => client.Server.GetInfoAsync());
        await client.Server.GetInfoAsync();

        Assert.AreEqual(2, server.Requests.Count(request => request.Path == ClientFixtures.NegotiationPath));
    }

    [TestMethod]
    public async Task ConcurrentFirstCallsAskTheServersVersionOnce()
    {
        using var server = FakeTableauServer.Start();
        server.Map("GET", ClientFixtures.NegotiationPath, _ =>
        {
            System.Threading.Thread.Sleep(200);
            return ClientFixtures.ServerInfo("3.25");
        });
        server.Map("GET", ClientFixtures.ServerInfoPath, ClientFixtures.ServerInfo());
        await using var client = ClientFixtures.Client(server, version: null);

        await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => client.Server.GetInfoAsync()));

        Assert.AreEqual(1, server.Requests.Count(request => request.Path == ClientFixtures.NegotiationPath));
        Assert.AreEqual(10, server.Requests.Count(request => request.Path == ClientFixtures.ServerInfoPath));
    }

    [TestMethod]
    public async Task AServerInfoWithoutAVersionIsAnInvalidResponse()
    {
        using var server = FakeTableauServer.Start();
        server.Map("GET", ClientFixtures.NegotiationPath, FakeResponse.Json(200, """{"serverInfo":{"restApiVersion":"three"}}"""));
        await using var client = ClientFixtures.Client(server, version: null);

        var thrown = await ClientFixtures.FailsAsync(TableauErrorKind.InvalidResponse, () => client.Server.GetInfoAsync());

        Assert.Contains("'three'", thrown.Message);
    }

    // Every descriptor below its version is refused before sending. A version the floor allows only with the opt-in
    // reaches the descriptor's own gate, which no opt-in passes.
    [TestMethod]
    public async Task EveryEndpointBelowItsVersionIsRefusedBeforeSending()
    {
        foreach (var endpoint in Endpoints.All)
        {
            using var server = FakeTableauServer.Start();
            var options = ClientFixtures.Options(server, version: null);
            options.ApiVersion = endpoint.MinVersion.Minor > 0
                ? new Version(endpoint.MinVersion.Major, endpoint.MinVersion.Minor - 1)
                : new Version(endpoint.MinVersion.Major - 1, 99);
            options.AllowUnsupportedServerVersion = true;
            await using var client = ClientFixtures.Client(options);

            var thrown = await ClientFixtures.FailsAsync(TableauErrorKind.UnsupportedApiVersion,
                () => client.Pipeline.SendAsync(endpoint, default, body => body.ServerInfo, "serverInfo", default));

            Assert.Contains($"{endpoint.Name} needs REST API {ApiVersions.Format(endpoint.MinVersion)}", thrown.Message);
            Assert.IsEmpty(server.Requests, endpoint.Name);
        }
    }

    [TestMethod]
    public async Task EveryEndpointOnADeploymentItDoesntRunOnIsRefusedBeforeSending()
    {
        var endpoints = Endpoints.All.Concat([
            ClientFixtures.Endpoint("{version}/schedules", deployments: TableauDeployments.Server),
            ClientFixtures.Endpoint("{version}/cloud-only", deployments: TableauDeployments.Cloud),
        ]);
        foreach (var endpoint in endpoints)
        {
            foreach (var deployment in new[] { TableauDeployment.Server, TableauDeployment.Cloud })
            {
                var runs = (endpoint.Deployments & (deployment == TableauDeployment.Cloud ? TableauDeployments.Cloud : TableauDeployments.Server)) != 0;
                if (runs) continue;
                using var server = FakeTableauServer.Start();
                var options = ClientFixtures.Options(server, version: null);
                options.Deployment = deployment;
                await using var client = ClientFixtures.Client(options);

                var thrown = await ClientFixtures.FailsAsync(TableauErrorKind.UnsupportedDeployment,
                    () => ClientFixtures.SendAsync(client, endpoint));

                Assert.Contains($"doesn't exist on Tableau {deployment}", thrown.Message);
                Assert.IsEmpty(server.Requests, $"{endpoint.Name} on {deployment}");
            }
        }
    }

    [TestMethod]
    public async Task AnEndpointAtItsVersionAndDeploymentIsSent()
    {
        using var server = FakeTableauServer.Start();
        server.Map("GET", "/api/3.25/schedules", ClientFixtures.ServerInfo());
        await using var client = ClientFixtures.Client(server);

        await ClientFixtures.SendAsync(client,
            ClientFixtures.Endpoint("{version}/schedules", deployments: TableauDeployments.Server, minVersion: "3.25"));

        Assert.HasCount(1, server.Requests);
    }

    [TestMethod]
    public void EveryEndpointsTemplateReadsAndItsVersionIsMajorMinor()
    {
        var fields = typeof(Endpoints).GetFields().Where(field => field.FieldType == typeof(Endpoint)).Select(field => (Endpoint)field.GetValue(null)!);

        CollectionAssert.AreEquivalent(fields.ToList(), Endpoints.All.ToList(), "Endpoints.All lists every descriptor once.");
        foreach (var endpoint in Endpoints.All)
        {
            Assert.IsTrue(ApiVersions.IsMajorMinor(endpoint.MinVersion), endpoint.Name);
            Assert.IsNotEmpty(endpoint.Segments, endpoint.Name);
        }
    }

    [TestMethod]
    [DataRow("3.25", 3, 25)]
    [DataRow(" 3.9 ", 3, 9)]
    [DataRow("3.10.2", 3, 10)]
    [DataRow("2.4", 2, 4)]
    public void AVersionReadsAsMajorAndMinor(string text, int major, int minor) =>
        Assert.AreEqual(new Version(major, minor), ApiVersions.Parse(text));

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("3")]
    [DataRow("three.two")]
    [DataRow("-3.2")]
    [DataRow("3.x")]
    public void TextThatIsNoVersionReadsAsNull(string? text) => Assert.IsNull(ApiVersions.Parse(text));

    [TestMethod]
    public void VersionsCompareWhole()
    {
        Assert.IsLessThan(new Version(3, 10), new Version(3, 9));
        Assert.IsLessThan(ApiVersions.Floor, new Version(3, 9));
        Assert.AreEqual("3.29", ApiVersions.Format(ApiVersions.Newest));
    }

    // A Tableau Server 2023.3: REST API 3.21, which answers no Server Info at 3.23.
    private static FakeTableauServer OldServer()
    {
        var server = FakeTableauServer.Start();
        server.Map("GET", ClientFixtures.NegotiationPath, FakeResponse.Json(404,
            """{"error":{"summary":"Resource Not Found","detail":"Invented: no version 3.23 here.","code":"404001"}}"""));
        server.Map("GET", "/api/2.4/serverinfo", ClientFixtures.ServerInfo("3.21", "2023.3.4"));
        return server;
    }
}
