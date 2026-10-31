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
/// The live suite's recorder writes no token, LUID, name, email address or host name of the site into a fixture: each
/// is replaced, a LUID by the same invented one wherever it recurs.
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
            "contentPermissions":"ManagedByOwner","topLevelProject":true,"size":"42"}]}}
            """;

        var scrubbed = scrubber.Scrub(answer);

        foreach (var real in new[] { SiteLuid, UserLuid, "live-token-value", "finance", "Quarterly Sales", "ada@company.example", Host, "company.example" })
            Assert.DoesNotContain(real, scrubbed);
        using var json = JsonDocument.Parse(scrubbed);
        var credentials = json.RootElement.GetProperty("credentials");
        var project = json.RootElement.GetProperty("projects").GetProperty("project")[0];
        Assert.AreEqual("invented-token", credentials.GetProperty("token").GetString());
        Assert.AreEqual(credentials.GetProperty("site").GetProperty("id").GetString(), project.GetProperty("id").GetString(),
            "a LUID that recurs gets the same invented one");
        Assert.AreEqual(credentials.GetProperty("user").GetProperty("id").GetString(), project.GetProperty("owner").GetProperty("id").GetString());
        Assert.IsTrue(Guid.TryParse(project.GetProperty("id").GetString(), out _));
        Assert.AreEqual("https://tableau.example.com:8443/#/projects/1", project.GetProperty("webpageUrl").GetString());
        Assert.AreEqual("363:10:35", credentials.GetProperty("estimatedTimeToExpiration").GetString(), "what names nothing stays");
        Assert.AreEqual("ManagedByOwner", project.GetProperty("contentPermissions").GetString());
        Assert.IsTrue(project.GetProperty("topLevelProject").GetBoolean());
        Assert.AreEqual("42", project.GetProperty("size").GetString());
        Assert.Contains("@invented.example", project.GetProperty("owner").GetProperty("email").GetString()!);
    }

    [TestMethod]
    public void ARequestsSecretsAndAPathsLuidsAreTakenOutToo()
    {
        var scrubber = new FixtureScrubber([Host]);

        var request = scrubber.Scrub("""{"credentials":{"personalAccessTokenName":"nightly","personalAccessTokenSecret":"live-secret","jwt":"a.b.c","site":{"contentUrl":""}}}""");
        var slug = scrubber.Slug($"/api/3.25/sites/{SiteLuid}/projects/{UserLuid}");
        var text = scrubber.Scrub($"<html>{Host} {SiteLuid}</html>");

        Assert.DoesNotContain("live-secret", request);
        Assert.DoesNotContain("nightly", request);
        Assert.DoesNotContain("a.b.c", request);
        Assert.Contains("\"contentUrl\": \"\"", request, "the default site's empty content URL stays");
        Assert.DoesNotContain(SiteLuid, slug);
        Assert.DoesNotContain("/", slug);
        Assert.AreEqual("<html>tableau.example.com 00000000-0000-4000-8000-000000000001</html>", text);
    }

    [TestMethod]
    public async Task TheRecorderWritesEachJsonBodyScrubbedAndPassesItOnAsSent()
    {
        using var server = FakeTableauServer.Start();
        server.Map("POST", SessionFixtures.SignInPath, SessionFixtures.SignedIn());
        var folder = Path.Combine(Path.GetTempPath(), "Tableau.Api.Tests-recorded-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var recorder = new FixtureRecorder(folder, new FixtureScrubber(["127.0.0.1"])) { InnerHandler = new SocketsHttpHandler { AllowAutoRedirect = false } };
            using var http = new HttpClient(recorder);
            await using var client = new TableauClient(http, SessionFixtures.Options(server), loggerFactory: Fixtures.Log.Factory);

            var session = await client.Sessions.SignInAsync(SessionFixtures.Pat());

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
}
