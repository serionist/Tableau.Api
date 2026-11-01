using System;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Tableau.Api.Rest.Models;

namespace Tableau.Api.Tests;

/// <summary>
/// The live suite's sweep deletes only its own leftovers: a top-level project with the suite's description and a run's
/// exact name, made more than a day ago; and a local group named exactly after a run and the second it was made, more
/// than a day ago. Every other project and group stays, whatever its name. And a live session Tableau refuses fails its
/// test with what the code may mean: 401001 a sign-in refused, 401002 a call the session may not make, which for the
/// connected app names its JWT's scopes.
/// </summary>
[TestClass]
public sealed class LiveSiteTests
{
    private static readonly DateTimeOffset _now = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);

    [TestMethod]
    [DataRow("Tableau.Api live run 41248323953-1")]
    [DataRow("Tableau.Api live run local-20261031-221500-0f3a")]
    public void ADayOldProjectOfTheSuitesIsSwept(string name) =>
        Assert.IsTrue(LiveSite.IsLeftover(Project(name), _now));

    [TestMethod]
    [DataRow("Tableau.Api live run notes")]
    [DataRow("Tableau.Api live run 41248323953")]
    [DataRow("Tableau.Api live run 41248323953-1 copy")]
    [DataRow("Tableau.Api live run local-20261031-221500-0F3A")]
    [DataRow("Tableau.Api live run local-2026-10-31")]
    [DataRow("tableau.api live run 41248323953-1")]
    [DataRow("Tableau.Api live run ")]
    public void AProjectWithAnotherNameIsKept(string name) =>
        Assert.IsFalse(LiveSite.IsLeftover(Project(name), _now));

    [TestMethod]
    public void AProjectThatIsNestedOrDescribedOtherwiseOrRecentIsKept()
    {
        var name = "Tableau.Api live run 41248323953-1";

        Assert.IsFalse(LiveSite.IsLeftover(Project(name) with { ParentProjectId = "9a8b7c6d-5e4f-4a3b-8c2d-1e0f9a8b7c6d" }, _now), "nested");
        Assert.IsFalse(LiveSite.IsLeftover(Project(name) with { Description = "A user's notes" }, _now), "described otherwise");
        Assert.IsFalse(LiveSite.IsLeftover(Project(name) with { Description = null }, _now), "no description");
        Assert.IsFalse(LiveSite.IsLeftover(Project(name) with { CreatedAt = _now.AddHours(-23) }, _now), "made today, by a run still going");
        Assert.IsFalse(LiveSite.IsLeftover(Project(name) with { CreatedAt = null }, _now), "no date");
        Assert.IsFalse(LiveSite.IsLeftover(Project(name) with { Id = null }, _now), "no id");
        Assert.IsTrue(LiveSite.IsRunProject(Project(name) with { CreatedAt = _now }), "the run's own, listed but not swept");
    }

    [TestMethod]
    public void TheRunsOwnNameIsARunsName() =>
        Assert.IsTrue(LiveSite.IsRunProject(Project(LiveSite.ProjectName)));

    [TestMethod]
    [DataRow("Tableau.Api live run 41248323953-1 20261030T115959Z")]
    [DataRow("Tableau.Api live run local-20261029-221500-0f3a 20261029T221501Z")]
    public void ALocalGroupOfTheSuitesNamedMoreThanADayAgoIsSwept(string name) =>
        Assert.IsTrue(LiveSite.IsLeftover(Group(name), _now));

    [TestMethod]
    [DataRow("Tableau.Api live run 41248323953-1")]
    [DataRow("Tableau.Api live run 41248323953-1 20261030T115959")]
    [DataRow("Tableau.Api live run 41248323953-1 20261030T115959Z copy")]
    [DataRow("Tableau.Api live run notes 20261030T115959Z")]
    [DataRow("tableau.api live run 41248323953-1 20261030T115959Z")]
    [DataRow("Tableau.Api live run 41248323953-1 20261399T999999Z")]
    public void AGroupWithAnotherNameIsKept(string name) =>
        Assert.IsFalse(LiveSite.IsLeftover(Group(name), _now));

    [TestMethod]
    public void AGroupFromADirectoryOrNamedTodayIsKept()
    {
        Assert.IsFalse(LiveSite.IsLeftover(Group("Tableau.Api live run 41248323953-1 20261030T115959Z") with
        {
            Domain = new DomainDirective { Name = "example.org" },
        }, _now), "from a directory");
        Assert.IsFalse(LiveSite.IsLeftover(Group("Tableau.Api live run 41248323953-1 20261030T115959Z") with { Domain = null }, _now), "no domain");
        Assert.IsFalse(LiveSite.IsLeftover(Group("Tableau.Api live run 41248323953-1 20261031T120001Z"), _now), "made today, by a run still going");
        Assert.IsFalse(LiveSite.IsLeftover(Group("Tableau.Api live run 41248323953-1 20261030T115959Z") with { Id = null }, _now), "no id");
        Assert.IsTrue(LiveSite.IsRunGroup(Group(LiveSite.GroupName(_now))), "the run's own, listed but not swept");
        Assert.IsFalse(LiveSite.IsLeftover(Group(LiveSite.GroupName(_now)), _now));
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

    private static Group Group(string name) => new()
    {
        Id = "4a1b2c3d-0009-4e5f-8a9b-0c1d2e3f4a5b",
        Name = name,
        Domain = new DomainDirective { Name = "local" },
    };

    private static Project Project(string name) => new()
    {
        Id = "1d0e8a7b-0001-4c2d-9e3f-5a6b7c8d9e0f",
        Name = name,
        Description = LiveSite.ProjectDescription,
        CreatedAt = _now.AddDays(-2),
    };
}
