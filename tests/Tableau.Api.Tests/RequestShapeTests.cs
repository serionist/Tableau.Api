using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Tableau.Api.Pipeline;
using Tableau.Api.Rest.Models;
using Tableau.Api.Tests.Fakes;

namespace Tableau.Api.Tests;

/// <summary>
/// What the 2.0 client's pipeline puts on the wire: each value escaped as one path segment by 1.2.6's rule, a value
/// that is empty or only dots refused before sending, a LUID checked where the template says so, every query key and
/// value escaped, the base URL's path kept, JSON both ways, the session token on its own request, and any 2xx a success.
/// </summary>
[TestClass]
public sealed class RequestShapeTests
{
    private const string ItemsPath = "/api/" + ClientFixtures.Version + "/items/";

    [TestMethod]
    public async Task EveryAsciiCharacterIsSentEscaped() => await AssertRowsAsync(PathRows.Ascii());

    [TestMethod]
    public async Task ValuesThatAreEmptyOrOnlyDotsAreRefusedBeforeSending() => await AssertRowsAsync(PathRows.DotOnly());

    [TestMethod]
    public async Task SubDelimitersBesideNonAsciiCharactersAreSentRaw() => await AssertRowsAsync(PathRows.SubDelimitersBesideNonAscii());

    [TestMethod]
    public async Task BidiControlsAreSentEscaped() => await AssertRowsAsync(PathRows.Bidi());

    [TestMethod]
    public async Task ALuidSegmentTakesOnlyALuid()
    {
        using var server = FakeTableauServer.Start();
        server.MapPrefix("GET", ItemsPath, ClientFixtures.ServerInfo());
        await using var client = ClientFixtures.Client(server);
        var endpoint = ClientFixtures.Endpoint("{version}/items/{itemId:luid}");

        await ClientFixtures.SendAsync(client, endpoint, new TableauRequest([Fixtures.SiteId]));
        await ClientFixtures.SendAsync(client, endpoint, new TableauRequest([Fixtures.SiteId.ToUpperInvariant()]));
        foreach (var notALuid in new[]
                 {
                     "finance", "0F4B9E1C3A524D6E9C8B1A2B3C4D5E6F", "{" + Fixtures.SiteId + "}", "(" + Fixtures.SiteId + ")",
                     Fixtures.SiteId + "/x", " " + Fixtures.SiteId, Fixtures.SiteId + " ", "\t" + Fixtures.SiteId,
                     Fixtures.SiteId + "\n", Fixtures.SiteId + " ", Fixtures.SiteId[..^1] + "g", "",
                 })
        {
            var thrown = await Assert.ThrowsExactlyAsync<ArgumentException>(
                () => ClientFixtures.SendAsync(client, endpoint, new TableauRequest([notALuid])));
            Assert.AreEqual("itemId", thrown.ParamName);
        }

        CollectionAssert.AreEqual(new[] { $"GET {ItemsPath}{Fixtures.SiteId} HTTP/1.1", $"GET {ItemsPath}{Fixtures.SiteId.ToUpperInvariant()} HTTP/1.1" },
            ClientFixtures.Lines(server));
    }

    [TestMethod]
    public async Task TheBaseUrlsPathIsKept()
    {
        using var server = FakeTableauServer.Start();
        server.Map("GET", "/tableau/api/3.25/serverinfo", ClientFixtures.ServerInfo());
        var options = ClientFixtures.Options(server);
        options.ServerUrl = new Uri(server.BaseUrl, "tableau");
        await using var client = ClientFixtures.Client(options);

        await client.Server.GetInfoAsync();

        Assert.AreEqual("GET /tableau/api/3.25/serverinfo HTTP/1.1", server.Requests.Single().RequestLine);
    }

    [TestMethod]
    [DataRow("{version}/serverinfo", "/api/3.25/serverinfo")]
    [DataRow("-/pulse/definitions", "/api/-/pulse/definitions")]
    [DataRow("metadata/graphql", "/api/metadata/graphql")]
    public async Task TheThreeShapesOfAPathAreBuilt(string template, string path)
    {
        using var server = FakeTableauServer.Start();
        server.Map("GET", path, ClientFixtures.ServerInfo());
        await using var client = ClientFixtures.Client(server);

        await ClientFixtures.SendAsync(client, ClientFixtures.Endpoint(template));

        Assert.AreEqual($"GET {path} HTTP/1.1", server.Requests.Single().RequestLine);
    }

    [TestMethod]
    [DataRow("{version}/Items/{a}/{b}", "a value and b value: the count of values and segments")]
    [DataRow("{version}/{}", "no parameter")]
    [DataRow("{version}/items?x", "no literal segment")]
    [DataRow("{version}//items", "no literal segment")]
    public void ATemplateThatCantBeReadFailsWhenItsDescriptorIsMade(string template, string why)
    {
        if (why.StartsWith("a value", StringComparison.Ordinal))
        {
            var endpoint = ClientFixtures.Endpoint(template);
            var url = new Uri("https://tableau.example.com/");
            Assert.ThrowsExactly<InvalidOperationException>(() => RequestUri.Build(url, endpoint, new Version(3, 25), ["only one"], null));
            Assert.ThrowsExactly<InvalidOperationException>(() => RequestUri.Build(url, endpoint, new Version(3, 25), ["a", "b", "c"], null));
            return;
        }
        var thrown = Assert.ThrowsExactly<FormatException>(() => ClientFixtures.Endpoint(template));
        Assert.Contains(why, thrown.Message);
    }

    [TestMethod]
    public async Task EveryQueryKeyAndValueIsEscapedAndFilterAndSortKeepTheirSeparators()
    {
        using var server = FakeTableauServer.Start();
        server.Map("GET", "/api/3.25/items", ClientFixtures.ServerInfo());
        await using var client = ClientFixtures.Client(server);

        await ClientFixtures.SendAsync(client, ClientFixtures.Endpoint("{version}/items"), new TableauRequest(Query: ClientFixtures.Query(
            ("fields", "_default_,owner.name"), ("filter", "name:eq:R&D,tags:in:[C++,#1 100%]"), ("sort", "name:asc"),
            ("vf_R&D", "a b"), ("left out", null), ("pageSize", "100"))));

        Assert.AreEqual(
            "fields=_default_%2Cowner.name&filter=name:eq:R%26D,tags:in:%5BC%2B%2B,%231%20100%25%5D&sort=name:asc"
            + "&vf_R%26D=a%20b&pageSize=100",
            server.Requests.Single().Query);
    }

    [TestMethod]
    public async Task ARequestAsksForJsonAndSendsItsBodyAsJson()
    {
        using var server = FakeTableauServer.Start();
        server.Map("POST", "/api/3.25/items", ClientFixtures.ServerInfo());
        await using var client = ClientFixtures.Client(server);
        var body = new TsRequest { Project = new Project { Name = "Invented project", Description = "R&D" } };

        await ClientFixtures.SendAsync(client, ClientFixtures.Endpoint("{version}/items", "POST"), new TableauRequest(Body: body));

        var request = server.Requests.Single();
        Assert.AreEqual("application/json", request.Header("Accept"));
        Assert.AreEqual("application/json", request.Header("Content-Type"));
        Assert.AreEqual("{\"project\":{\"description\":\"R\\u0026D\",\"name\":\"Invented project\"}}", request.BodyText);
    }

    [TestMethod]
    public async Task TheSessionTokenGoesOnTheRequestThatCarriesIt()
    {
        using var server = FakeTableauServer.Start();
        server.Map("GET", "/api/3.25/items", ClientFixtures.ServerInfo());
        await using var client = ClientFixtures.Client(server);
        var endpoint = ClientFixtures.Endpoint("{version}/items");

        await ClientFixtures.SendAsync(client, endpoint, new TableauRequest(Token: Fixtures.SessionToken));
        await ClientFixtures.SendAsync(client, endpoint, new TableauRequest(Token: Fixtures.OtherSessionToken));
        await ClientFixtures.SendAsync(client, endpoint);

        CollectionAssert.AreEqual(new[] { Fixtures.SessionToken, Fixtures.OtherSessionToken, null },
            server.Requests.Select(request => request.Header("X-Tableau-Auth")).ToArray());
    }

    [TestMethod]
    [DataRow(200)]
    [DataRow(201)]
    [DataRow(202)]
    [DataRow(203)]
    public async Task Any2xxIsASuccess(int status)
    {
        using var server = FakeTableauServer.Start();
        server.Map("POST", "/api/3.25/items", FakeResponse.Json(status, ModelFixtures.Text("server-info.json")));
        await using var client = ClientFixtures.Client(server);

        var info = await ClientFixtures.SendAsync(client, ClientFixtures.Endpoint("{version}/items", "POST"));

        Assert.AreEqual("3.25", info.RestApiVersion);
    }

    [TestMethod]
    public async Task ServerInfoReadsTheServersVersions()
    {
        using var server = FakeTableauServer.Start();
        server.Map("GET", ClientFixtures.ServerInfoPath, FakeResponse.Fixture(200, "server-info.json"));
        await using var client = ClientFixtures.Client(server);

        var info = await client.Server.GetInfoAsync();

        Assert.AreEqual(new ProductVersion { Value = "2025.1.0", Build = "20251.25.0101.0000" }, info.ProductVersion);
        Assert.AreEqual("3.25", info.RestApiVersion);
        var request = server.Requests.Single();
        Assert.AreEqual($"GET {ClientFixtures.ServerInfoPath} HTTP/1.1", request.RequestLine);
        Assert.IsNull(request.Header("X-Tableau-Auth"));
    }

    [TestMethod]
    public async Task ACancelledCallSendsNothing()
    {
        using var server = FakeTableauServer.Start();
        await using var client = ClientFixtures.Client(server, version: null);
        using var cancelled = new System.Threading.CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(() => client.Server.GetInfoAsync(cancelled.Token));

        Assert.IsEmpty(server.Requests);
    }

    private static async Task AssertRowsAsync(IEnumerable<string> values)
    {
        using var server = FakeTableauServer.Start();
        server.MapPrefix("GET", ItemsPath, ClientFixtures.ServerInfo());
        await using var client = ClientFixtures.Client(server);
        var endpoint = ClientFixtures.Endpoint("{version}/items/{itemId}/tail");
        var failures = new List<string>();
        foreach (var value in values)
        {
            var before = server.Requests.Count;
            try
            {
                await ClientFixtures.SendAsync(client, endpoint, new TableauRequest([value]));
                var path = server.Requests[before].Path;
                if (PathRows.IsRefused(value)) failures.Add($"{PathRows.Show(value)} was sent as {path}");
                else if (path != ItemsPath + PathRows.Escaped(value) + "/tail") failures.Add($"{PathRows.Show(value)} was sent as {path}");
                else if (PathRows.Segments(path)[4] != value) failures.Add($"{PathRows.Show(value)} reads back otherwise");
            }
            catch (ArgumentException e) when (PathRows.IsRefused(value))
            {
                if (e.ParamName != "itemId" || server.Requests.Count != before)
                    failures.Add($"{PathRows.Show(value)} was refused as {e.ParamName}, or sent");
            }
        }
        Assert.IsEmpty(failures, string.Join("\n", failures));
    }
}
