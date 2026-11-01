using System;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Tableau.Api.Pipeline;

namespace Tableau.Api.Tests;

/// <summary>
/// Server Info on a Tableau site, which needs no sign-in: the client negotiates its version there, reads the answer
/// as Tableau's JSON, and reads Tableau's error body. It changes nothing on the site. TABLEAU_LIVE_SERVER_URL is the
/// server's address, such as https://tableau.example.com/ or a Tableau Cloud pod's. One at a time, as the other site
/// tests, so each call answered
/// is credited to its own test in <see cref="LiveSite.Record"/>, which its clients log through.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class ServerInfoSiteTests
{
    private const string ServerUrl = "TABLEAU_LIVE_SERVER_URL";

    [SiteTest(TableauSite.Live, [ServerUrl]), TestCategory(TableauSite.Live)]
    public async Task ServerInfoAnswersWithoutASignInAtTheNegotiatedVersion()
    {
        await using var client = TableauClient.Create(new TableauClientOptions { ServerUrl = new Uri(TableauSite.Setting(ServerUrl)) },
            loggerFactory: LiveSite.Record.Factory);

        var info = await client.Server.GetInfoAsync();

        var offered = ApiVersions.Parse(info.RestApiVersion);
        Assert.IsNotNull(offered, "Server Info gave no REST API version");
        Assert.IsGreaterThanOrEqualTo(ApiVersions.Floor, offered);
        Assert.IsFalse(string.IsNullOrEmpty(info.ProductVersion?.Value), "Server Info gave no product version");
    }

    [SiteTest(TableauSite.Live, [ServerUrl]), TestCategory(TableauSite.Live)]
    public async Task AVersionTheServerLacksIsNotFoundWithTableausError()
    {
        await using var client = TableauClient.Create(new TableauClientOptions
        {
            ServerUrl = new Uri(TableauSite.Setting(ServerUrl)),
            ApiVersion = new Version(9, 0),
        }, loggerFactory: LiveSite.Record.Factory);

        var thrown = await ClientFixtures.FailsAsync(TableauErrorKind.NotFound, () => client.Server.GetInfoAsync());

        Assert.IsFalse(string.IsNullOrEmpty(thrown.ErrorCode), "no Tableau error code");
        Assert.IsFalse(string.IsNullOrEmpty(thrown.Summary), "no Tableau error summary");
        Assert.EndsWith("/api/9.0/serverinfo", thrown.RequestPath!);
    }
}
