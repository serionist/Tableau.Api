using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Tableau.Api.Pipeline;
using Tableau.Api.Rest.Models;
using Tableau.Api.Tests.Fakes;

namespace Tableau.Api.Tests;

/// <summary>The 2.0 client the tests drive against the fake server, and what they send through its pipeline.</summary>
internal static class ClientFixtures
{
    /// <summary>The version the tests pin, so a call sends no Server Info first; the fixture's server answers it.</summary>
    public const string Version = "3.25";

    public const string ServerInfoPath = "/api/" + Version + "/serverinfo";

    /// <summary>Where a client asks for the server's version: Server Info at the client's floor.</summary>
    public const string NegotiationPath = "/api/3.23/serverinfo";

    /// <summary>
    /// Options for the fake server: its plain http allowed, the version pinned unless a test passes null, a short
    /// timeout, so a request nobody answers fails fast, and no retries, so a test of another behaviour sends each call
    /// once and waits for no back-off. The retries' own tests set <see cref="TableauClientOptions.MaxRetries"/>.
    /// </summary>
    public static TableauClientOptions Options(FakeTableauServer server, string? version = Version, int maxRetries = 0) => new()
    {
        ServerUrl = server.BaseUrl,
        AllowInsecureHttp = server.BaseUrl.Scheme == Uri.UriSchemeHttp,
        ApiVersion = version is null ? null : System.Version.Parse(version),
        RequestTimeout = TimeSpan.FromSeconds(30),
        MaxRetries = maxRetries,
    };

    /// <summary>A client of its own over the fake server, logging to <see cref="Fixtures.Log"/>.</summary>
    public static TableauClient Client(FakeTableauServer server, string? version = Version) =>
        TableauClient.Create(Options(server, version), Fixtures.Log.Factory);

    /// <summary>A client from options, logging to <see cref="Fixtures.Log"/>, its retries waiting on the clock given.</summary>
    public static TableauClient Client(TableauClientOptions options, TimeProvider? time = null) =>
        TableauClient.CreateWithClock(options, Fixtures.Log.Factory, time);

    /// <summary>
    /// Moves a fake clock on a step at a time, giving the client a moment after each, until <paramref name="done"/> holds,
    /// and returns how far the clock moved. A wait of the client's ends within a step of the time it asked for; a step
    /// shorter than the attempt timeout leaves an attempt that started meanwhile alone.
    /// </summary>
    public static async Task<TimeSpan> AdvanceUntilAsync(FakeTimeProvider time, Func<bool> done, TimeSpan step, TimeSpan most)
    {
        var moved = TimeSpan.Zero;
        while (!done())
        {
            if (moved >= most) throw new AssertFailedException($"Nothing happened in {most} of the fake clock.");
            time.Advance(step);
            moved += step;
            await Task.Delay(20);
        }
        return moved;
    }

    /// <summary>Waits, in real time, until <paramref name="done"/> holds, or fails after ten seconds.</summary>
    public static async Task WaitUntilAsync(Func<bool> done)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!done())
        {
            if (DateTime.UtcNow > deadline) throw new AssertFailedException("Nothing happened in ten seconds.");
            await Task.Delay(10);
        }
    }

    /// <summary>Server Info's answer, with the REST API version given.</summary>
    public static FakeResponse ServerInfo(string restApiVersion = Version, string productVersion = "2025.1.0") =>
        FakeResponse.Json(200, $$$"""{"serverInfo":{"productVersion":{"value":"{{{productVersion}}}","build":"20251.25.0101.0000"},"restApiVersion":"{{{restApiVersion}}}"}}""");

    /// <summary>A descriptor for a test of the pipeline, which no real call has.</summary>
    public static Endpoint Endpoint(string template, string method = "GET", TableauDeployments deployments = TableauDeployments.Both,
        string minVersion = "2.0", TransferKind transfer = TransferKind.None) =>
        new("Test call", new HttpMethod(method), template, System.Version.Parse(minVersion), deployments, Idempotent: method == "GET",
            transfer);

    /// <summary>Sends a test call and returns Server Info from its answer, as a call that returns an element does.</summary>
    public static Task<ServerInfo> SendAsync(TableauClient client, Endpoint endpoint, TableauRequest request = default,
        CancellationToken cancellationToken = default) =>
        client.Pipeline.SendAsync(endpoint, request, static body => body.ServerInfo, "serverInfo", cancellationToken);

    /// <summary>A descriptor for a download of the pipeline's tests: a GET, safe to retry, that answers with a file.</summary>
    public static Endpoint Download(string template) => Endpoint(template, transfer: TransferKind.Download);

    /// <summary>Sends a test download.</summary>
    public static Task<TableauDownload> DownloadAsync(TableauClient client, Endpoint endpoint, CancellationToken cancellationToken = default) =>
        client.Pipeline.DownloadAsync(endpoint, default, cancellationToken);

    /// <summary>A loopback port nothing listens on: one a listener just gave back.</summary>
    public static int ClosedPort()
    {
        using var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        return ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
    }

    /// <summary>The request lines the server received.</summary>
    public static string[] Lines(FakeTableauServer server) => [.. server.Requests.Select(request => request.RequestLine)];

    /// <summary>The query's pairs, for a request.</summary>
    public static IReadOnlyList<KeyValuePair<string, string?>> Query(params (string Key, string? Value)[] pairs) =>
        [.. pairs.Select(pair => new KeyValuePair<string, string?>(pair.Key, pair.Value))];

    /// <summary>Asserts a call failed with the kind given, and returns the exception.</summary>
    public static async Task<TableauException> FailsAsync(TableauErrorKind kind, Func<Task> call)
    {
        var thrown = await Assert.ThrowsExactlyAsync<TableauException>(call);
        Assert.AreEqual(kind, thrown.Kind, thrown.ToString());
        return thrown;
    }
}
