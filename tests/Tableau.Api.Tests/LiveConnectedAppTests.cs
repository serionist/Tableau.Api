using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Tableau.Api.Rest.Models;

namespace Tableau.Api.Tests;

/// <summary>
/// The connected app's own sign-ins on a Tableau site (<see cref="LiveSite"/> has the settings), on TABLEAU_LIVE_SITE,
/// reading a page of one project and changing nothing: a client with the app's direct trust signs itself in for Query
/// Projects and signs out when disposed; a session signed in with a JWT the suite mints from the same settings serves a
/// bound client and is signed out. Each checks the sign-out took: the session's token is refused after it. The JWT asks
/// for <see cref="LiveSite.Scopes"/> alone; the content tests sign in with the token. One at a time, as the other live
/// tests.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class LiveConnectedAppTests
{
    private const string User = "TABLEAU_LIVE_USER";

    private static readonly ProjectListOptions _onePage = new() { PageSize = 1 };

    [SiteTest(TableauSite.Live, [LiveSite.ServerUrl], SignsIn = LiveSignIn.ConnectedApp), TestCategory(TableauSite.Live)]
    public async Task AClientWithTheConnectedAppSignsItselfInForAPageOfProjectsAndSignsOutWhenDisposed()
    {
        TableauSession session;
        Page<Project> page;
        await using (var client = LiveSite.Client(LiveSite.ConnectedApp()))
        {
            page = await LiveSite.SignedInAsync(LiveSignIn.ConnectedApp, () => client.Projects.ListPageAsync(1, _onePage));
            session = LiveSite.Saw(client.Pipeline.State.Sessions.Current!);
        }

        Assert.AreEqual(LiveSite.SiteContentUrl, session.SiteContentUrl);
        Assert.IsTrue(Guid.TryParse(session.UserId, out _), "the user's LUID");
        Assert.HasCount(1, page.Items, "a site has its Default project at least");
        Assert.IsGreaterThanOrEqualTo(1, page.TotalAvailable ?? 0);
        await using var check = LiveSite.Client(null);
        await ClientFixtures.FailsAsync(TableauErrorKind.Authentication, () => check.WithSession(session).Projects.ListPageAsync(1, _onePage));
    }

    [SiteTest(TableauSite.Live, [LiveSite.ServerUrl], SignsIn = LiveSignIn.ConnectedApp), TestCategory(TableauSite.Live)]
    public async Task ASessionSignedInWithAMintedJwtServesABoundClientAndIsSignedOut()
    {
        await using var client = LiveSite.Client(null);
        var session = LiveSite.Saw(await LiveSite.SignedInAsync(LiveSignIn.ConnectedApp, () => client.Sessions.SignInAsync(LiveSite.Jwt())));
        await using var bound = client.WithSession(session);
        Page<Project> page;
        try
        {
            page = await bound.Projects.ListPageAsync(1, _onePage);
        }
        finally
        {
            // A session held by hand is its holder's to end, whatever failed.
            await bound.Sessions.SignOutAsync(session);
        }

        var user = TableauSite.Setting(User);
        // Fixed messages: an assertion that printed the user's name would put it in the public log.
        Assert.IsTrue(string.Equals(user, session.UserName, StringComparison.Ordinal), "the session takes its name from the JWT's sub, TABLEAU_LIVE_USER");
        Assert.IsTrue(LiveSite.Secrets.Any(secret => string.Equals(ConnectedAppJwt.Subject(secret), user, StringComparison.Ordinal)),
            "the JWT the suite minted is kept with the live secrets");
        Assert.AreEqual(LiveSite.SiteContentUrl, session.SiteContentUrl);
        Assert.HasCount(1, page.Items, "a site has its Default project at least");
        var thrown = await ClientFixtures.FailsAsync(TableauErrorKind.Authentication, () => bound.Projects.ListPageAsync(1, _onePage));
        Assert.Contains("WithSession", thrown.Message, "a bound client never signs in");
    }
}
