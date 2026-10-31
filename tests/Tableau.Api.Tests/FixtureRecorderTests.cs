using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Tableau.Api.Tests.Fakes;

namespace Tableau.Api.Tests;

/// <summary>
/// The live suite's recorder writes no secret, token, LUID, name, email address, URL, host name or setting of the run
/// into a fixture: names fail closed, a LUID is the same invented one wherever it recurs, and what says nothing of the
/// site, a date, a number, a version, an enumeration, stays. At the suite's end a file still holding a live value is
/// deleted and named.
/// </summary>
[TestClass]
public sealed class FixtureRecorderTests
{
    private const string Host = "tableau.invented-company.example";
    private const string SiteLuid = "1a2b3c4d-5e6f-4a7b-8c9d-0e1f2a3b4c5d";
    private const string UserLuid = "6f5e4d3c-2b1a-4f9e-8d7c-6b5a4f3e2d1c";

    [TestMethod]
    public void TheScrubberTakesOutWhatNamesTheSiteAndKeepsTheShape()
    {
        var scrubber = new FixtureScrubber([Host]);
        var answer = $$$"""
            {"credentials":{"site":{"id":"{{{SiteLuid}}}","contentUrl":"finance"},"user":{"id":"{{{UserLuid}}}"},
            "token":"live-token-value","estimatedTimeToExpiration":"363:10:35"},
            "projects":{"project":[{"id":"{{{SiteLuid}}}","name":"Quarterly Sales","description":"Owned by ada@company.example",
            "owner":{"id":"{{{UserLuid}}}","email":"ada@company.example"},"webpageUrl":"https://{{{Host}}}:8443/#/projects/1",
            "contentPermissions":"ManagedByOwner","topLevelProject":true,"size":"42","createdAt":"2026-10-31T12:00:00Z"}]},
            "serverInfo":{"productVersion":{"value":"2025.1.5","build":"20251.25.0618.1347"},"restApiVersion":"3.25"},
            "error":{"summary":"Resource Not Found","detail":"Project 'Quarterly Sales' could not be found."}}
            """;

        var scrubbed = scrubber.Scrub(answer)!;

        foreach (var real in new[] { SiteLuid, UserLuid, "live-token-value", "finance", "Quarterly Sales", "ada@company.example", Host, "company.example", "8443" })
            Assert.DoesNotContain(real, scrubbed);
        using var json = JsonDocument.Parse(scrubbed);
        var root = json.RootElement;
        var credentials = root.GetProperty("credentials");
        var project = root.GetProperty("projects").GetProperty("project")[0];
        Assert.AreEqual("invented-token", credentials.GetProperty("token").GetString());
        Assert.AreEqual(credentials.GetProperty("site").GetProperty("id").GetString(), project.GetProperty("id").GetString(),
            "a LUID that recurs gets the same invented one");
        Assert.AreEqual(credentials.GetProperty("user").GetProperty("id").GetString(), project.GetProperty("owner").GetProperty("id").GetString());
        Assert.IsTrue(Guid.TryParse(project.GetProperty("id").GetString(), out _));
        Assert.StartsWith("invented-", project.GetProperty("webpageUrl").GetString()!, "a URL is a name, and fails closed");
        Assert.StartsWith("invented-", project.GetProperty("owner").GetProperty("email").GetString()!);
        Assert.StartsWith("invented-", root.GetProperty("error").GetProperty("detail").GetString()!);
        Assert.AreEqual("363:10:35", credentials.GetProperty("estimatedTimeToExpiration").GetString(), "what names nothing stays");
        Assert.AreEqual("ManagedByOwner", project.GetProperty("contentPermissions").GetString());
        Assert.AreEqual("2026-10-31T12:00:00Z", project.GetProperty("createdAt").GetString());
        Assert.AreEqual("42", project.GetProperty("size").GetString());
        Assert.IsTrue(project.GetProperty("topLevelProject").GetBoolean());
        Assert.AreEqual("20251.25.0618.1347", root.GetProperty("serverInfo").GetProperty("productVersion").GetProperty("build").GetString());
        Assert.AreEqual("3.25", root.GetProperty("serverInfo").GetProperty("restApiVersion").GetString());
        Assert.AreEqual("Resource Not Found", root.GetProperty("error").GetProperty("summary").GetString());
    }

    [TestMethod]
    public void EverySecretTheSchemaHasAndWhatAKeychainOrAWebhookHoldsIsTakenOut()
    {
        var scrubber = new FixtureScrubber([Host]);
        var body = """
            {"apiToken":{"secret":"live-1"},"connectedApplicationSecret":{"id":"live-id","value":"live-2"},
            "connectionCredentials":{"name":"live-name","password":"live-3"},"connection":{"password":"live-4"},
            "personalAccessToken":{"secret":"live-5"},"serverApiToken":{"apiToken":"live-6"},"serviceToken":{"tokenSecret":"live-7"},
            "siteOIDCConfiguration":{"clientSecret":"live-8"},
            "credentials":{"jwt":"live-9","password":"live-10","personalAccessTokenSecret":"live-11","token":"live-12"},
            "user":{"password":"live-13"},"encryptedKeychain":"live-14","keychains":{"encryptedKeychain":["live-15"],"other":{"x":"live-16"}},
            "webhook":{"webhook-destination":{"webhook-destination-http":{"method":"POST","url":"https://hooks.live.example/live-17"}}}}
            """;

        var scrubbed = scrubber.Scrub(body)!;

        Assert.DoesNotContain("live", scrubbed);
        using var json = JsonDocument.Parse(scrubbed);
        Assert.AreEqual("invented-value", json.RootElement.GetProperty("connectedApplicationSecret").GetProperty("value").GetString(),
            "under a secret's key, every string goes");
        Assert.AreEqual("POST", json.RootElement.GetProperty("webhook").GetProperty("webhook-destination")
            .GetProperty("webhook-destination-http").GetProperty("method").GetString());
    }

    [TestMethod]
    public void TheRunsKnownValuesAreTakenOutAsWordsBodyAndPathAlike()
    {
        var scrubber = new FixtureScrubber([Host], ["svc-reports", "export", "nightly-site"]);

        var body = scrubber.Scrub("""{"error":{"summary":"Signed in as svc-reports to nightly-site with export, an exporter"}}""")!;
        var slug = scrubber.Slug("/api/3.25/sites/nightly-site/export");

        Assert.Contains("Signed in as invented to invented with invented, an exporter", body, "a word, not part of one");
        Assert.AreEqual("api-3.25-sites-invented-invented", slug);
    }

    [TestMethod]
    public void TextThatIsNoJsonGivesNothing()
    {
        var scrubber = new FixtureScrubber([Host]);

        Assert.IsNull(scrubber.Scrub($"<html>{Host} {SiteLuid}</html>"));
        Assert.IsNull(scrubber.Scrub("{\"token\":"));
    }

    [TestMethod]
    public void ARequestsSecretsAndAPathsLuidsAreTakenOutToo()
    {
        var scrubber = new FixtureScrubber([Host]);

        var request = scrubber.Scrub("""{"credentials":{"personalAccessTokenName":"nightly","personalAccessTokenSecret":"live-secret","jwt":"a.b.c","site":{"contentUrl":""}}}""")!;
        var slug = scrubber.Slug($"/api/3.25/sites/{SiteLuid}/projects/{UserLuid}");

        Assert.DoesNotContain("live-secret", request);
        Assert.DoesNotContain("nightly", request);
        Assert.DoesNotContain("a.b.c", request);
        Assert.Contains("\"contentUrl\": \"\"", request, "the default site's empty content URL stays");
        Assert.AreEqual("api-3.25-sites-00000000-0000-4000-8000-000000000001-projects-00000000-0000-4000-8000-000000000002", slug);
    }

    [TestMethod]
    public async Task TheRecorderWritesEachJsonBodyScrubbedAndNothingForOneThatIsNoJson()
    {
        using var server = FakeTableauServer.Start();
        server.Map("POST", SessionFixtures.SignInPath, SessionFixtures.SignedIn());
        server.Map("GET", ClientFixtures.ServerInfoPath, FakeResponse.Content(200, "application/json", "<html>not json</html>"));
        var folder = Path.Combine(Path.GetTempPath(), "Tableau.Api.Tests-recorded-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var recorder = new FixtureRecorder(folder, new FixtureScrubber(["127.0.0.1"], [SessionFixtures.PatName]))
            {
                InnerHandler = new SocketsHttpHandler { AllowAutoRedirect = false },
            };
            using var http = new HttpClient(recorder);
            await using var client = new TableauClient(http, SessionFixtures.Options(server), loggerFactory: Fixtures.Log.Factory);

            var session = await client.Sessions.SignInAsync(SessionFixtures.Pat());
            await ClientFixtures.FailsAsync(TableauErrorKind.InvalidResponse, () => client.Server.GetInfoAsync());

            Assert.AreEqual(Fixtures.SessionToken, session.Token, "the client read the answer as sent");
            var files = Directory.GetFiles(folder).Select(Path.GetFileName).Order(StringComparer.Ordinal).ToArray();
            CollectionAssert.AreEqual(new[] { "001-post-api-3.25-auth-signin-200.json", "001-post-api-3.25-auth-signin-request.json" }, files);
            var written = string.Concat(Directory.GetFiles(folder).Select(File.ReadAllText));
            foreach (var real in new[] { Fixtures.SessionToken, Fixtures.PatSecret, Fixtures.SiteId, Fixtures.UserId, SessionFixtures.PatName })
                Assert.DoesNotContain(real, written);
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
    }

    [TestMethod]
    public void TheEndOfSuiteScanDeletesAFileHoldingALiveValueAndNamesOnlyTheFile()
    {
        var folder = Path.Combine(Path.GetTempPath(), "Tableau.Api.Tests-scan-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            File.WriteAllText(Path.Combine(folder, "clean.json"), """{"name":"invented-name-1","summary":"an exporter"}""");
            File.WriteAllText(Path.Combine(folder, "secret.json"), """{"x":"prefix-live-token-value-suffix"}""");
            File.WriteAllText(Path.Combine(folder, "known.json"), """{"x":"by export"}""");

            var holding = FixtureRecorder.DeleteHolding(folder, ["live-token-value"], ["export"]);

            CollectionAssert.AreEquivalent(new[] { "secret.json", "known.json" }, holding.ToArray());
            CollectionAssert.AreEqual(new[] { "clean.json" }, Directory.GetFiles(folder).Select(Path.GetFileName).ToArray());
            Assert.IsEmpty(FixtureRecorder.DeleteHolding(Path.Combine(folder, "none"), ["x"], []));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
