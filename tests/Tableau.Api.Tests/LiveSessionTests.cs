using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Tableau.Api.Pipeline;
using Tableau.Api.Rest.Models;

namespace Tableau.Api.Tests;

/// <summary>
/// Sessions on the run's site, signed in with the live suite's personal access token (<see cref="LiveSite"/> has the
/// settings): a client signs itself in for a call and signs out when disposed; a session signed in by hand serves a
/// bound client through its sub-clients, makes the run's project, and is signed out. Each test checks the sign-out took:
/// the token is refused after it. They run one at a time: a personal access token allows one session, so a sign-in of
/// one test would end the other's. <see cref="LiveConnectedAppTests"/> has the connected app's sessions.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class LiveSessionTests
{
    private static readonly ProjectListOptions _onePage = new() { PageSize = 1 };

    [SiteTest(TableauSite.Live, [LiveSite.ServerUrl], SignsIn = LiveSignIn.Token), TestCategory(TableauSite.Live)]
    public async Task AClientSignsItselfInForACallAndSignsOutWhenDisposed()
    {
        var site = await LiveSite.RunSiteAsync();
        TableauSession session;
        await using (var client = await LiveSite.RunClientAsync())
        {
            await LiveSite.SignedInAsync(LiveSignIn.Token, () => client.Projects.ListPageAsync(1, _onePage));
            session = LiveSite.Saw(client.Pipeline.State.Sessions.Current!);
            Assert.AreEqual((site.ContentUrl, site.Id), (session.SiteContentUrl, session.SiteId), "the run's site");
            Assert.IsTrue(Guid.TryParse(session.UserId, out _), "the user's LUID");
            Assert.IsGreaterThan(DateTimeOffset.UtcNow, session.ExpiresAt);
            Assert.IsGreaterThanOrEqualTo(ApiVersions.Floor, session.ApiVersion);
        }

        await using var check = LiveSite.Client(null);
        await ClientFixtures.FailsAsync(TableauErrorKind.Authentication, () => check.WithSession(session).Projects.ListPageAsync(1, _onePage));
    }

    [SiteTest(TableauSite.Live, [LiveSite.ServerUrl], SignsIn = LiveSignIn.Token), TestCategory(TableauSite.Live)]
    public async Task ASessionSignedInByHandServesABoundClientAcrossItsSubClients()
    {
        await using var client = LiveSite.Client(null, await LiveSite.RunOptionsAsync());
        var session = LiveSite.Saw(await LiveSite.SignedInAsync(LiveSignIn.Token, () => client.Sessions.SignInAsync(LiveSite.Credentials())));
        await using var bound = client.WithSession(session);
        ServerInfo info;
        Project listed;
        try
        {
            info = await bound.Server.GetInfoAsync();
            var project = await LiveSite.ProjectAsync(bound);
            listed = await LiveSite.ListedAsync(bound, project);
        }
        finally
        {
            // A session held by hand is its holder's to end, whatever failed.
            await bound.Sessions.SignOutAsync(session);
        }

        Assert.IsFalse(string.IsNullOrEmpty(info.ProductVersion?.Value));
        Assert.AreEqual((LiveSite.ProjectName, LiveSite.ProjectDescription), (listed.Name, listed.Description));
        var thrown = await ClientFixtures.FailsAsync(TableauErrorKind.Authentication, () => bound.Projects.ListPageAsync(1, _onePage));
        Assert.Contains("WithSession", thrown.Message, "a bound client never signs in");
    }
}

/// <summary>
/// What only a Tableau Server's sign-in has, run by hand with TABLEAU_SERVER_ONLY=1 on a server you may change:
/// impersonation with a server administrator's password, and Switch Site between two sites the user can reach. Settings:
/// TABLEAU_SERVER_ONLY_SERVER_URL; TABLEAU_SERVER_ONLY_SITE, unset or empty for the default site;
/// TABLEAU_SERVER_ONLY_USER and TABLEAU_SERVER_ONLY_PASSWORD, a server administrator's; TABLEAU_SERVER_ONLY_IMPERSONATE,
/// the LUID of a user to act as; TABLEAU_SERVER_ONLY_OTHER_SITE, a second site's content URL. One at a time, as the live ones.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class ServerOnlySessionTests
{
    private const string ServerUrl = "TABLEAU_SERVER_ONLY_SERVER_URL";
    private const string User = "TABLEAU_SERVER_ONLY_USER";
    private const string Password = "TABLEAU_SERVER_ONLY_PASSWORD";
    private const string Impersonate = "TABLEAU_SERVER_ONLY_IMPERSONATE";
    private const string OtherSite = "TABLEAU_SERVER_ONLY_OTHER_SITE";

    [SiteTest(TableauSite.ServerOnly, [ServerUrl, User, Password, Impersonate]), TestCategory(TableauSite.ServerOnly)]
    public async Task APasswordSignInImpersonatesAUser()
    {
        await using var client = Client();
        var impersonated = TableauSite.Setting(Impersonate);

        var session = LiveSite.Saw(await client.Sessions.SignInAsync(TableauCredentials.Password(TableauSite.Setting(User),
            LiveSite.Secret(Password), impersonated)));
        try
        {
            await client.WithSession(session).Projects.ListPageAsync(1, new ProjectListOptions { PageSize = 1 });
        }
        finally
        {
            await client.Sessions.SignOutAsync(session);
        }

        Assert.IsTrue(string.Equals(impersonated, session.UserId, StringComparison.OrdinalIgnoreCase), "the session acts as the user impersonated");
        await ClientFixtures.FailsAsync(TableauErrorKind.Authentication, () => client.WithSession(session).Projects.ListPageAsync(1, new ProjectListOptions { PageSize = 1 }));
    }

    [SiteTest(TableauSite.ServerOnly, [ServerUrl, User, Password, OtherSite]), TestCategory(TableauSite.ServerOnly)]
    public async Task SwitchSiteGivesASessionOnTheOtherSiteAndEndsTheFirst()
    {
        await using var client = Client();
        var first = LiveSite.Saw(await client.Sessions.SignInAsync(TableauCredentials.Password(TableauSite.Setting(User),
            LiveSite.Secret(Password))));

        var other = LiveSite.Saw(await client.Sessions.SwitchSiteAsync(first, TableauSite.Setting(OtherSite)));
        try
        {
            await client.WithSession(other).Projects.ListPageAsync(1, new ProjectListOptions { PageSize = 1 });
        }
        finally
        {
            await client.Sessions.SignOutAsync(other);
        }

        Assert.AreEqual(TableauSite.Setting(OtherSite), other.SiteContentUrl);
        Assert.AreNotEqual(first.SiteId, other.SiteId);
        Assert.AreEqual(first.UserName, other.UserName);
        await ClientFixtures.FailsAsync(TableauErrorKind.Authentication, () => client.WithSession(first).Projects.ListPageAsync(1, new ProjectListOptions { PageSize = 1 }));
        await ClientFixtures.FailsAsync(TableauErrorKind.Authentication, () => client.WithSession(other).Projects.ListPageAsync(1, new ProjectListOptions { PageSize = 1 }));
    }

    // Over the suite's HttpClient, so every token it carries is kept for the log check.
    private static TableauClient Client() => LiveSite.Client(null, new TableauClientOptions
    {
        ServerUrl = new Uri(TableauSite.Setting(ServerUrl)),
        Site = Environment.GetEnvironmentVariable("TABLEAU_SERVER_ONLY_SITE") ?? "",
        Deployment = TableauDeployment.Server,
    });
}
