using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Tableau.Api.Models;
using Tableau.Api.Tests.Fakes;

namespace Tableau.Api.Tests;

/// <summary>Responses with the enum values Tableau's 3.29 schema adds parse; a session keeps the name it signed in with.</summary>
[TestClass]
public sealed class ModelTests
{
    private const string ApiPath = "/api/" + Fixtures.ApiVersion;
    private const string SitePath = ApiPath + "/sites/" + Fixtures.SiteId;

    [TestMethod]
    public async Task AUsersPageWithNewAuthSettingsAndRolesParses()
    {
        using var server = FakeTableauServer.Start();
        var client = Fixtures.Client(server);
        server.Map("GET", SitePath + "/users", FakeResponse.Xml(200,
            "<pagination pageNumber=\"1\" pageSize=\"100\" totalAvailable=\"3\"/><users>"
            + "<user id=\"u1\" name=\"ada\" siteRole=\"Creator\" authSetting=\"OpenID\"/>"
            + "<user id=\"u2\" name=\"grace\" siteRole=\"Viewer\" authSetting=\"TableauIDWithMFA\"/>"
            + "<user id=\"u3\" name=\"support\" siteRole=\"SupportUser\" authSetting=\"ServerDefault\"/></users>"));

        var (_, users) = await client.GetUsersOnSite(Fixtures.Session());

        CollectionAssert.AreEqual(
            new[] { siteUserAuthSettingType.OpenID, siteUserAuthSettingType.TableauIDWithMFA, siteUserAuthSettingType.ServerDefault },
            users.Select(user => user.authSetting).ToArray());
        Assert.AreEqual(siteRoleType.SupportUser, users[2].siteRole);
    }

    // Every member 1.2.6 appends to an enum, as its XML value, which the responses below carry.
    private static readonly (Enum Member, string Xml)[] Appended =
    [
        (siteUserAuthSettingType.OpenID, "OpenID"), (siteUserAuthSettingType.TableauIDWithMFA, "TableauIDWithMFA"),
        (siteRoleType.SupportUser, "SupportUser"),
        (jobTypeType.BroadcastPost, "BroadcastPost"), (jobTypeType.CreateSiteUsersCsv, "CreateSiteUsersCsv"),
        (jobTypeType.EmptyRecycleBin, "EmptyRecycleBin"), (jobTypeType.LinkUserIdsUpdate, "LinkUserIdsUpdate"),
        (jobTypeType.RefreshThumbnails, "RefreshThumbnails"), (jobTypeType.ResyncTCMUsersCsv, "ResyncTCMUsersCsv"),
        (projectTypeContentPermissions.LockedToProjectWithoutNested, "LockedToProjectWithoutNested"),
        (scheduleTypeType.DataAcceleration, "DataAcceleration"),
        (intervalTypeHours.Item24, "24"), (intervalTypeMinutes.Item60, "60"),
        (capabilityTypeName.AiAccess, "AiAccess"), (capabilityTypeName.Execute, "Execute"),
        (capabilityTypeName.ExtractRefresh, "ExtractRefresh"), (capabilityTypeName.Overwrite, "Overwrite"),
        (capabilityTypeName.PulseMetricDefine, "PulseMetricDefine"), (capabilityTypeName.RunExplainData, "RunExplainData"),
        (capabilityTypeName.SaveAs, "SaveAs"), (capabilityTypeName.VizqlDataApiAccess, "VizqlDataApiAccess"),
        (capabilityTypeName.WebAuthoringForFlows, "WebAuthoringForFlows"),
    ];

    [TestMethod]
    public async Task EveryAppendedEnumMemberParses()
    {
        using var server = FakeTableauServer.Start();
        var client = Fixtures.Client(server);
        var session = Fixtures.Session();
        string[] Values<T>() => [.. Appended.Where(pair => pair.Member is T).Select(pair => pair.Xml)];
        server.Map("GET", SitePath + "/users", FakeResponse.Xml(200,
            "<pagination pageNumber=\"1\" pageSize=\"100\" totalAvailable=\"3\"/><users>"
            + string.Concat(Values<siteUserAuthSettingType>().Select(v => $"<user id=\"u-{v}\" siteRole=\"Viewer\" authSetting=\"{v}\"/>"))
            + string.Concat(Values<siteRoleType>().Select(v => $"<user id=\"u-{v}\" siteRole=\"{v}\"/>")) + "</users>"));
        var jobs = Values<jobTypeType>();
        foreach (var type in jobs)
            server.Map("GET", SitePath + "/jobs/" + type, FakeResponse.Xml(200, $"<job id=\"{type}\" type=\"{type}\"/>"));
        server.Map("GET", SitePath + "/projects", FakeResponse.Xml(200,
            "<pagination pageNumber=\"1\" pageSize=\"100\" totalAvailable=\"1\"/><projects>"
            + string.Concat(Values<projectTypeContentPermissions>().Select(v => $"<project id=\"p1\" contentPermissions=\"{v}\"/>"))
            + "</projects>"));
        server.Map("GET", ApiPath + "/schedules", FakeResponse.Xml(200,
            "<pagination pageNumber=\"1\" pageSize=\"100\" totalAvailable=\"1\"/><schedules>"
            + string.Concat(Values<scheduleTypeType>().Select(v => $"<schedule id=\"s1\" type=\"{v}\"><frequencyDetails start=\"00:00:00\"><intervals>"
                + string.Concat(Values<intervalTypeHours>().Select(h => $"<interval hours=\"{h}\"/>"))
                + string.Concat(Values<intervalTypeMinutes>().Select(m => $"<interval minutes=\"{m}\"/>"))
                + "</intervals></frequencyDetails></schedule>"))
            + "</schedules>"));
        server.Map("GET", SitePath + "/datasources/d1/permissions", FakeResponse.Xml(200,
            "<permissions><datasource id=\"d1\"/><granteeCapabilities><user id=\"u1\"/><capabilities>"
            + string.Concat(Values<capabilityTypeName>().Select(v => $"<capability name=\"{v}\" mode=\"Allow\"/>"))
            + "</capabilities></granteeCapabilities></permissions>"));

        var parsed = new List<Enum>();
        var (_, users) = await client.GetUsersOnSite(session);
        parsed.AddRange(users.Where(user => user.authSettingSpecified).Select(user => (Enum)user.authSetting));
        parsed.AddRange(users.Where(user => user.siteRole == siteRoleType.SupportUser).Select(user => (Enum)user.siteRole));
        foreach (var type in jobs) parsed.Add((await client.QueryJob(session, type)).type);
        var (_, projects) = await client.QueryProjects(session);
        parsed.AddRange(projects.Select(project => (Enum)project.contentPermissions));
        var (_, schedules) = await client.QuerySchedules(session);
        parsed.AddRange(schedules.Select(schedule => (Enum)schedule.type));
        var intervals = schedules.Single().frequencyDetails.intervals;
        parsed.AddRange(intervals.Where(interval => interval.hoursSpecified).Select(interval => (Enum)interval.hours));
        parsed.AddRange(intervals.Where(interval => interval.minutesSpecified).Select(interval => (Enum)interval.minutes));
        var permissions = await client.QueryDataSourcePermissions(session, "d1");
        parsed.AddRange(permissions.granteeCapabilities.Single().capabilities.Select(capability => (Enum)capability.name));

        Assert.HasCount(22, Appended);
        CollectionAssert.AreEquivalent(Appended.Select(pair => pair.Member).ToArray(), parsed.ToArray());
    }

    [TestMethod]
    public async Task ASessionKeepsTheNameItSignedInWith()
    {
        using var server = FakeTableauServer.Start();
        var client = Fixtures.Client(server);
        server.Map("POST", ApiPath + "/auth/switchsite", Fixtures.SignedIn());

        var signedIn = await Fixtures.SignInAsync(server, client);
        var withToken = await client.SignInWithPAT("nightly-export", Fixtures.PatSecret, Fixtures.SiteContentUrl);
        var switched = await client.SwitchSite(signedIn, "marketing");

        Assert.AreEqual(Fixtures.UserName, signedIn.UserName);
        Assert.AreEqual("nightly-export", withToken.UserName);
        Assert.AreEqual(Fixtures.UserName, switched.UserName);
    }
}
