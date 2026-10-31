using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Tableau.Api.Rest.Models;

namespace Tableau.Api.Tests;

/// <summary>
/// The models read Tableau's JSON bodies, the fixtures in Fixtures/, and write them back: each member kept, each value
/// the same to Tableau, a request carrying only what was set.
/// </summary>
[TestClass]
public sealed class ModelFixtureTests
{
    public static IEnumerable<object[]> Bodies => ModelFixtures.Manifest().Select(fixture => new object[] { fixture.File, fixture.Body });

    [TestMethod]
    [DynamicData(nameof(Bodies))]
    public void EveryFixtureRoundTrips(string file, string body)
    {
        var written = body switch
        {
            "request" => ModelFixtures.Write(ModelFixtures.Request(file)),
            "response" => ModelFixtures.Write(ModelFixtures.Response(file)),
            _ => throw new InvalidDataException($"{file}: body is {body}, not request or response"),
        };

        ModelFixtures.AssertSameJson(ModelFixtures.Text(file), written);
    }

    [TestMethod]
    public void TheManifestListsEveryFixtureOnce()
    {
        var listed = ModelFixtures.Manifest().Select(fixture => fixture.File).ToList();
        var present = Directory.GetFiles(ModelFixtures.Folder, "*.json").Select(Path.GetFileName)
            .Where(name => name != "manifest.json").Order(StringComparer.Ordinal).ToList();

        CollectionAssert.AreEqual(present, listed.Order(StringComparer.Ordinal).ToList());
        Assert.HasCount(listed.Distinct(StringComparer.Ordinal).Count(), listed);
    }

    [TestMethod]
    public void ASignInReadsTheTokenAndTheIds()
    {
        var credentials = ModelFixtures.Response("sign-in.json").Credentials!;

        Assert.AreEqual("invented-session-token", credentials.Token);
        Assert.AreEqual("239:59:59", credentials.EstimatedTimeToExpiration);
        Assert.AreEqual(Fixtures.SiteId, credentials.Site!.Id);
        Assert.AreEqual(Fixtures.SiteContentUrl, credentials.Site.ContentUrl);
        Assert.AreEqual(Fixtures.UserId, credentials.User!.Id);
    }

    [TestMethod]
    public void ARequestCarriesOnlyWhatItsCallerSet()
    {
        var request = new TsRequest
        {
            Credentials = new Credentials
            {
                PersonalAccessTokenName = "invented-pat-name",
                PersonalAccessTokenSecret = Fixtures.PatSecret,
                Site = new Site { ContentUrl = Fixtures.SiteContentUrl },
            },
        };

        Assert.AreEqual(
            """{"credentials":{"site":{"contentUrl":"finance"},"personalAccessTokenName":"invented-pat-name","personalAccessTokenSecret":"invented-pat-secret"}}""",
            ModelFixtures.Write(request));
        ModelFixtures.AssertSameJson(ModelFixtures.Text("sign-in-request.json"), ModelFixtures.Write(request));
    }

    [TestMethod]
    public void ServerInfoReadsTheVersionTextBesideItsBuild()
    {
        var info = ModelFixtures.Response("server-info.json").ServerInfo!;

        Assert.AreEqual("2025.1.0", info.ProductVersion!.Value);
        Assert.AreEqual("20251.25.0101.0000", info.ProductVersion.Build);
        Assert.AreEqual("3.25", info.RestApiVersion);
    }

    [TestMethod]
    public void AnErrorReadsItsCodeFromAString()
    {
        var error = ModelFixtures.Response("error.json").Error!;

        Assert.AreEqual(404006L, error.Code);
        Assert.AreEqual("Resource Not Found", error.Summary);
        Assert.StartsWith("Workbook '", error.Detail);
    }

    [TestMethod]
    public void APageReadsItsNumbersFromStringsAndItsItems()
    {
        var response = ModelFixtures.Response("projects-page.json");

        Assert.AreEqual(new Pagination { PageNumber = 1, PageSize = 3, TotalAvailable = 5 }, response.Pagination);
        var projects = response.Projects!;
        Assert.HasCount(3, projects);
        Assert.AreEqual(ProjectContentPermissions.LockedToProject, projects[1].ContentPermissions);
        Assert.IsFalse(projects[1].Writeable);
        Assert.AreEqual(projects[1].Id, projects[2].ParentProjectId);
        Assert.AreEqual(new DateTimeOffset(2025, 4, 2, 10, 15, 0, TimeSpan.Zero), projects[2].UpdatedAt);
    }

    [TestMethod]
    public void AnEmptyWrapperIsAnEmptyList()
    {
        var response = ModelFixtures.Response("projects-empty.json");

        Assert.IsNotNull(response.Projects);
        Assert.IsEmpty(response.Projects);
        Assert.AreEqual(0L, response.Pagination!.TotalAvailable);
        Assert.IsNull(response.Users);
    }

    [TestMethod]
    public void APageOfOneIsAListOfOne()
    {
        var user = ModelFixtures.Response("users-page.json").Users!.Single();

        Assert.AreEqual("ada", user.Name);
        Assert.AreEqual(SiteRole.SiteAdministratorCreator, user.SiteRole);
        Assert.AreEqual(SiteUserAuthSetting.OpenId, user.AuthSetting);
        Assert.AreEqual("local", user.Domain!.Name);
        Assert.AreEqual("", user.ExternalAuthUserId);
    }

    [TestMethod]
    public void ValuesTableauAddsLaterReadAndKeepTheirText()
    {
        var user = ModelFixtures.Response("user-unknown-values.json").User!;

        Assert.AreEqual("InventedFutureRole", user.SiteRole!.Value.Value);
        Assert.AreEqual("InventedFutureAuthSetting", user.AuthSetting!.Value.ToString());
        Assert.AreEqual(LicensingRole.Interactor, user.Role);
        Assert.Contains("\"siteRole\":\"InventedFutureRole\"", ModelFixtures.Write(new TsResponse { User = user }));
    }

    [TestMethod]
    public void ASiteReadsBothKindsOfNumberAndItsUsage()
    {
        var site = ModelFixtures.Response("site.json").Site!;

        Assert.AreEqual(25L, site.RevisionLimit);
        Assert.AreEqual(30, site.AutoSuspendRefreshInactivityWindow);
        Assert.AreEqual(5L, site.TierCreatorCapacity);
        Assert.AreEqual("-1", site.UserQuota);
        Assert.AreEqual(SiteAdminMode.ContentAndUsers, site.AdminMode);
        Assert.AreEqual(SiteAskDataMode.DisabledByDefault, site.AskDataMode);
        Assert.AreEqual(new SiteUsage { NumUsers = 12, Storage = 2048 }, site.Usage);
    }

    [TestMethod]
    public void AWorkbookReadsItsListsAndItsEmptyObjects()
    {
        var workbook = ModelFixtures.Response("workbook.json").Workbook!;

        CollectionAssert.AreEqual(new[] { "finance", "R&D" }, workbook.Tags!.Select(tag => tag.Label).ToArray());
        var views = workbook.Views!;
        Assert.HasCount(2, views);
        Assert.IsEmpty(views[0].Tags!);
        Assert.AreEqual("draft", views[1].Tags!.Single().Label);
        Assert.AreEqual(new DataAccelerationInfo(), workbook.DataAccelerationConfig);
        Assert.AreEqual(DataFreshnessPolicyOption.AlwaysLive, workbook.DataFreshnessPolicy!.Option);
        Assert.AreEqual("true", workbook.ShowTabs);
        Assert.AreEqual(1L, workbook.Size);
        Assert.AreEqual(2L, workbook.SheetCount);
        Assert.IsFalse(workbook.HasExtracts);
        Assert.AreEqual("Project", workbook.Location!.Type!.Value.Value);
    }

    [TestMethod]
    public void AProjectRequestAndItsResponseShareTheModel()
    {
        var request = ModelFixtures.Request("create-project-request.json").Project!;
        var response = ModelFixtures.Response("create-project.json").Project!;

        Assert.AreEqual(request.Name, response.Name);
        Assert.AreEqual(request.ParentProjectId, response.ParentProjectId);
        Assert.AreEqual(request.Owner, response.Owner);
        Assert.IsNull(request.Id);
        Assert.AreEqual(response.Id, response.ControllingPermissionsProjectId);
    }
}
