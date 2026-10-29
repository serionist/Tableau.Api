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

    [TestMethod]
    public async Task AJobAProjectAScheduleAndCapabilitiesWithNewValuesParse()
    {
        using var server = FakeTableauServer.Start();
        var client = Fixtures.Client(server);
        var session = Fixtures.Session();
        server.Map("GET", SitePath + "/jobs/j1", FakeResponse.Xml(200, "<job id=\"j1\" type=\"RefreshThumbnails\"/>"));
        server.Map("GET", SitePath + "/projects", FakeResponse.Xml(200,
            "<pagination pageNumber=\"1\" pageSize=\"100\" totalAvailable=\"1\"/><projects>"
            + "<project id=\"p1\" name=\"Sales\" contentPermissions=\"LockedToProjectWithoutNested\"/></projects>"));
        server.Map("GET", ApiPath + "/schedules", FakeResponse.Xml(200,
            "<pagination pageNumber=\"1\" pageSize=\"100\" totalAvailable=\"1\"/><schedules>"
            + "<schedule id=\"s1\" name=\"Acceleration\" type=\"DataAcceleration\"/></schedules>"));
        server.Map("GET", SitePath + "/datasources/d1/permissions", FakeResponse.Xml(200,
            "<permissions><datasource id=\"d1\"/><granteeCapabilities><user id=\"u1\"/><capabilities>"
            + "<capability name=\"Read\" mode=\"Allow\"/><capability name=\"SaveAs\" mode=\"Allow\"/>"
            + "<capability name=\"VizqlDataApiAccess\" mode=\"Deny\"/></capabilities></granteeCapabilities></permissions>"));

        var job = await client.QueryJob(session, "j1");
        var (_, projects) = await client.QueryProjects(session);
        var (_, schedules) = await client.QuerySchedules(session);
        var permissions = await client.QueryDataSourcePermissions(session, "d1");

        Assert.AreEqual(jobTypeType.RefreshThumbnails, job.type);
        Assert.AreEqual(projectTypeContentPermissions.LockedToProjectWithoutNested, projects[0].contentPermissions);
        Assert.AreEqual(scheduleTypeType.DataAcceleration, schedules[0].type);
        CollectionAssert.AreEqual(
            new[] { capabilityTypeName.Read, capabilityTypeName.SaveAs, capabilityTypeName.VizqlDataApiAccess },
            permissions.granteeCapabilities[0].capabilities.Select(capability => capability.name).ToArray());
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
