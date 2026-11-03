using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Tableau.Api.Rest.Models;

namespace Tableau.Api.Tests;

/// <summary>
/// The live suite's sweep deletes only its own leftovers: a site whose name is a run's and the second it was made,
/// exactly, and whose content URL is the one that name gives, exactly, made more than a day ago. Every other site stays,
/// whatever its name, the default site among them; and the run's own site is made only when Create Site's answer passes
/// that rule. And a live session Tableau refuses fails its test with what the code may mean: 401001 a sign-in refused,
/// 401002 a call the session may not make, which for the connected app names its JWT's scopes.
/// </summary>
[TestClass]
public sealed class LiveSiteTests
{
    private static readonly DateTimeOffset _now = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);

    [TestMethod]
    [DataRow("Tableau.Api live run 41248323953-1 20261030T115959Z", "tableau-api-live-run-41248323953-1-20261030115959")]
    [DataRow("Tableau.Api live run local-20261029-221500-0f3a 20261029T221501Z", "tableau-api-live-run-local-20261029-221500-0f3a-20261029221501")]
    public void ADayOldRunSiteIsSwept(string name, string contentUrl)
    {
        Assert.IsTrue(LiveSite.IsRunSite(Site(name, contentUrl)));
        Assert.IsTrue(LiveSite.IsLeftover(Site(name, contentUrl), _now));
    }

    [TestMethod]
    public void TheRunsOwnSiteIsARunsAndARecentOneIsKept()
    {
        var name = LiveSite.RunSiteName(LiveSite.Run, _now);
        var contentUrl = LiveSite.RunSiteContentUrl(LiveSite.Run, _now);

        Assert.IsTrue(LiveSite.IsRunSite(Site(name, contentUrl)), "the run's own, which the sweep would take once a day old");
        Assert.IsFalse(LiveSite.IsLeftover(Site(name, contentUrl), _now), "made now, by a run still going");
        Assert.IsFalse(LiveSite.IsLeftover(Site("Tableau.Api live run 41248323953-1 20261031T120001Z",
            "tableau-api-live-run-41248323953-1-20261031120001"), _now), "made a second less than a day ago");
        Assert.IsTrue(LiveSite.IsLeftover(Site("Tableau.Api live run 41248323953-1 20261031T115959Z",
            "tableau-api-live-run-41248323953-1-20261031115959"), _now), "made a second more than a day ago");
        Assert.IsTrue(contentUrl.All(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-'), "lower-case letters, digits and hyphens");
        Assert.AreEqual(("Tableau.Api live run 41248323953-1 20261101T120000Z", "tableau-api-live-run-41248323953-1-20261101120000"),
            (LiveSite.RunSiteName("41248323953-1", _now), LiveSite.RunSiteContentUrl("41248323953-1", _now)));
    }

    [TestMethod]
    [DataRow("Default", "", "the default site")]
    [DataRow("Finance", "finance", "a site of its own")]
    [DataRow("Tableau.Api live run notes 20261030T115959Z", "tableau-api-live-run-notes-20261030115959", "no run")]
    [DataRow("Tableau.Api live run 41248323953-1", "tableau-api-live-run-41248323953-1", "no second")]
    [DataRow("Tableau.Api live run 41248323953-1 20261030T115959Z copy", "tableau-api-live-run-41248323953-1-20261030115959", "more after the name")]
    [DataRow("Tableau.Api live run 41248323953-1 20261399T999999Z", "tableau-api-live-run-41248323953-1-20261399999999", "no such second")]
    [DataRow("tableau.api live run 41248323953-1 20261030T115959Z", "tableau-api-live-run-41248323953-1-20261030115959", "the name in another case")]
    [DataRow("Tableau.Api live run 41248323953-1 20261030t115959z", "tableau-api-live-run-41248323953-1-20261030115959", "the second in another case")]
    [DataRow("Tableau.Api live run local-20261029-221500-0F3A 20261029T221501Z", "tableau-api-live-run-local-20261029-221500-0f3a-20261029221501",
        "the run's random part in another case")]
    [DataRow("Tableau.Api live run 41248323953-1 20261030T115959Z", "Tableau-api-live-run-41248323953-1-20261030115959", "the content URL in another case")]
    [DataRow("Tableau.Api live run 41248323953-1 20261030T115959Z", "tableau-api-live-run-41248323953-1-20261030115958", "the content URL a character off")]
    [DataRow("Tableau.Api live run 41248323953-1 20261030T115959Z", "tableau-api-live-run-41248323953-2-20261030115959", "another run's content URL")]
    [DataRow("Tableau.Api live run 41248323953-1 20261030T115959Z", "tableau-api-live-run-41248323953-1-20261030T115959Z", "the second as the name has it")]
    [DataRow("Tableau.Api live run 41248323953-1 20261030T115959Z", null, "no content URL")]
    [DataRow(null, "tableau-api-live-run-41248323953-1-20261030115959", "no name")]
    public void ASiteThatIsNoRunsExactlyIsKept(string? name, string? contentUrl, string why)
    {
        Assert.IsFalse(LiveSite.IsRunSite(Site(name, contentUrl)), why);
        Assert.IsFalse(LiveSite.IsLeftover(Site(name, contentUrl), _now), why);
    }

    [TestMethod]
    public void ARunSiteWithoutAnIdIsKept() =>
        Assert.IsFalse(LiveSite.IsLeftover(Site("Tableau.Api live run 41248323953-1 20261030T115959Z", "tableau-api-live-run-41248323953-1-20261030115959")
            with
        { Id = null }, _now));

    [TestMethod]
    public void TheRunsSiteIsMadeOnlyWhenCreateSitesAnswerIsOneTheSweepWouldTake()
    {
        var name = LiveSite.RunSiteName(LiveSite.Run, _now);
        var contentUrl = LiveSite.RunSiteContentUrl(LiveSite.Run, _now);
        var made = Site(name, contentUrl);

        Assert.AreSame(made, LiveSite.Checked(made));
        var changed = Assert.ThrowsExactly<InvalidOperationException>(() => LiveSite.Checked(made with { ContentUrl = contentUrl.ToUpperInvariant() }));
        var renamed = Assert.ThrowsExactly<InvalidOperationException>(() => LiveSite.Checked(made with { Name = name + " (2)" }));
        Assert.ThrowsExactly<InvalidOperationException>(() => LiveSite.Checked(made with { Id = null }));

        Assert.StartsWith($"Create Site answered the site '{name}' ({contentUrl.ToUpperInvariant()}), which the sweep wouldn't take", changed.Message);
        Assert.Contains("delete it by hand", renamed.Message);
    }

    [TestMethod]
    [DataRow(nameof(LiveSignIn.Token), "the personal access token was refused: check that it is current and not revoked")]
    [DataRow(nameof(LiveSignIn.ConnectedApp), "the connected app refused the JWT: check that it is enabled")]
    public async Task A401001IsASignInTableauRefusedWithWhatToCheckForItsSignIn(string name, string hint)
    {
        var refused = await RefusedAsync(Enum.Parse<LiveSignIn>(name), "401001", "Signin Error");

        Assert.StartsWith("The sign-in failed (401001: Signin Error): " + hint, refused.Message);
        Assert.AreEqual("401001", ((TableauException)refused.InnerException!).ErrorCode, "the refusal is kept as the inner exception");
    }

    [TestMethod]
    public async Task A401002InTheConnectedAppsSessionNamesTheJwtsScopesAndNoSignInFailure()
    {
        var refused = await RefusedAsync(LiveSignIn.ConnectedApp, "401002", "Unauthorized Access");

        Assert.StartsWith("Tableau refused a call in the connected app's session (401002: Unauthorized Access): its JWT asks for the "
            + "scopes tableau:content:read (LiveSite.Scopes)", refused.Message);
        Assert.Contains("one this server's version lacks", refused.Message);
        Assert.DoesNotContain("sign-in failed", refused.Message);
        Assert.DoesNotContain("refused the JWT", refused.Message);
    }

    [TestMethod]
    public async Task A401002InTheTokensSessionIsNoSignInFailure()
    {
        var refused = await RefusedAsync(LiveSignIn.Token, "401002", "Unauthorized Access");

        Assert.StartsWith("Tableau refused a call in the token's session (401002: Unauthorized Access): a sign-in elsewhere with the same "
            + "token ends its session", refused.Message);
        Assert.DoesNotContain("sign-in failed", refused.Message);
    }

    [TestMethod]
    public async Task AnotherRefusalIsNamedByItsCodeOrItsLackAndHintsAtTheSignIn()
    {
        var other = await RefusedAsync(LiveSignIn.ConnectedApp, "401003", "Switch Site Error");
        var none = await RefusedAsync(LiveSignIn.Token, null, null);

        Assert.StartsWith("Tableau or the client refused the session (401003: Switch Site Error); if it was the sign-in, the connected app "
            + "refused the JWT", other.Message);
        Assert.StartsWith("Tableau or the client refused the session (no error code: ); if it was the sign-in, the personal access token",
            none.Message);
    }

    [TestMethod]
    public async Task ACallThatAnswersOrFailsOtherwiseIsLeftAsItIs()
    {
        var notFound = new TableauException(TableauErrorKind.NotFound, "Not here.") { ErrorCode = "404000" };

        Assert.AreEqual(7, await LiveSite.SignedInAsync(LiveSignIn.Token, () => Task.FromResult(7)));
        Assert.AreSame(notFound, await Assert.ThrowsExactlyAsync<TableauException>(
            () => LiveSite.SignedInAsync<int>(LiveSignIn.ConnectedApp, () => throw notFound)));
    }

    // What a live test's sign-in fails with when Tableau refuses its session with the code given.
    private static Task<AssertFailedException> RefusedAsync(LiveSignIn signIn, string? errorCode, string? summary) =>
        Assert.ThrowsExactlyAsync<AssertFailedException>(() => LiveSite.SignedInAsync<int>(signIn,
            () => throw new TableauException(TableauErrorKind.Authentication, "Refused.") { ErrorCode = errorCode, Summary = summary }));

    private static Site Site(string? name, string? contentUrl) => new()
    {
        Id = "5b2c3d4e-0009-4f5a-8b9c-0d1e2f3a4b5c",
        Name = name,
        ContentUrl = contentUrl,
    };
}
