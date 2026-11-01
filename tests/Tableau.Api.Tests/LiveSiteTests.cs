using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Tableau.Api.Rest.Models;

namespace Tableau.Api.Tests;

/// <summary>
/// The live suite's sweep deletes only its own leftovers: a top-level project with the suite's description and a run's
/// exact name, made more than a day ago. Every other project stays, whatever its name.
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
