using System;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Tableau.Api.Exceptions;
using Tableau.Api.Tests.Fakes;

namespace Tableau.Api.Tests;

/// <summary>
/// The REST client's versions, which AGENTS.md lists as a one-way door: 2.0 and up, a version above 3.14 used as 3.14
/// with a warning. And how a call fails: Tableau's error details when it sent them, none when the body is no Tableau
/// error, and a cancelled call sends nothing.
/// </summary>
[TestClass]
public sealed class ClientTests
{
    private const string SubscriptionsPath = "/api/" + Fixtures.ApiVersion + "/sites/" + Fixtures.SiteId + "/subscriptions";
    private static readonly Uri _url = new("https://tableau.example.com/");

    [TestMethod]
    [DataRow("2.0")]
    [DataRow("3.4")]
    [DataRow("3.14")]
    public void AVersionFromTwoToThreeFourteenIsUsedAsGiven(string given)
    {
        var log = new RecordingLogger();

        var client = new TableauApiClient(_url, log, given);

        Assert.AreEqual(Version.Parse(given), client.ApiVersion);
        Assert.IsEmpty(Warnings(log), string.Join("\n", log.Lines));
    }

    [TestMethod]
    [DataRow("3.15")]
    [DataRow("3.25")]
    [DataRow("4.0")]
    public void AVersionAboveThreeFourteenIsUsedAsThreeFourteenWithAWarning(string given)
    {
        var log = new RecordingLogger();

        var client = new TableauApiClient(_url, log, given);
        var withoutLog = new TableauApiClient(_url, given);

        Assert.AreEqual(new Version(3, 14), client.ApiVersion);
        Assert.AreEqual(new Version(3, 14), withoutLog.ApiVersion);
        Assert.Contains(given, Warnings(log).Single());
    }

    [TestMethod]
    public void AVersionBelowTwoOrNoVersionOrNoUrlIsRefused()
    {
        var below = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new TableauApiClient(_url, "1.9"));
        var none = Assert.ThrowsExactly<ArgumentException>(() => new TableauApiClient(_url, "three"));
        var noUrl = Assert.ThrowsExactly<ArgumentException>(() => new TableauApiClient(null!, "3.4"));

        Assert.AreEqual("apiVersion", below.ParamName);
        Assert.AreEqual("apiVersion", none.ParamName);
        Assert.AreEqual("tableauBaseUri", noUrl.ParamName);
    }

    [TestMethod]
    public async Task AnErrorTableauSentGivesItsDetails()
    {
        using var server = FakeTableauServer.Start();
        var client = Fixtures.Client(server);
        server.Map("GET", SubscriptionsPath, FakeResponse.Error(403, "403069", "Forbidden", "Invented: not these."));

        var thrown = await Assert.ThrowsExactlyAsync<TableauRequestException>(
            () => client.QuerySubscriptions(Fixtures.Session()));

        Assert.AreEqual(HttpStatusCode.Forbidden, thrown.StatusCode);
        Assert.AreEqual("403069", thrown.Details.code);
        Assert.AreEqual("Forbidden", thrown.Details.summary);
        Assert.AreEqual("Invented: not these.", thrown.Details.detail);
    }

    [TestMethod]
    public async Task AnErrorThatIsNoTableauErrorGivesNoDetailsAndIsLogged()
    {
        using var server = FakeTableauServer.Start();
        var log = new RecordingLogger();
        var client = new TableauApiClient(server.BaseUrl, log, Fixtures.ApiVersion);
        server.Map("GET", SubscriptionsPath, FakeResponse.Content(502, "text/html", "<html>Bad gateway</html>"));

        var thrown = await Assert.ThrowsExactlyAsync<TableauRequestException>(
            () => client.QuerySubscriptions(Fixtures.Session()));

        Assert.AreEqual(HttpStatusCode.BadGateway, thrown.StatusCode);
        Assert.IsFalse(thrown.HasDetails);
        Assert.Contains("Failed to read Tableau Server error details", Warnings(log).Single());
    }

    [TestMethod]
    public async Task ACancelledCallSendsNothing()
    {
        using var server = FakeTableauServer.Start();
        var client = Fixtures.Client(server);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => client.QuerySubscriptions(Fixtures.Session(), cancelToken: cancelled.Token));

        Assert.IsEmpty(server.Requests);
    }

    private static string[] Warnings(RecordingLogger log) =>
        [.. log.Lines.Where(line => line.StartsWith("Warning: ", StringComparison.Ordinal))];
}
