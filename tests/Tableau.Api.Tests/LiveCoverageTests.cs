using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Tableau.Api.Tests.Fakes;

namespace Tableau.Api.Tests;

/// <summary>
/// The live-coverage matrix, <c>docs/live-coverage.md</c>: its table names every public method of the 2.0 client and
/// every call of <c>Endpoints.All</c>, and only <c>Live</c> site tests; the checker that says so fails on each fault of an
/// invented table, so the real table can't pass by reading nothing; the nightly's check fails on each fault of an
/// invented record; and the record keeps each call Tableau answered, by the test that made it.
/// </summary>
[TestClass]
public sealed class LiveCoverageTests
{
    private static readonly string[] _methods = ["ProjectsClient.ListAsync", "ProjectsClient.DeleteAsync", "MetadataClient.QueryAsync<T>"];

    private static readonly string[] _calls = ["Query Projects", "Delete Project", "Metadata API query"];

    private static readonly string[] _tests = ["LiveAreaTests.AProjectIsMade", "LiveAreaTests.AQuery"];

    [TestMethod]
    public void TheTableHoldsEveryMethodAndCallOfTheClientAndNamesOnlyLiveTests()
    {
        var text = LiveCoverage.Text();
        var (rows, unread) = LiveCoverage.Parse(text, LiveCoverage.ClientSection);
        var (vizportal, vizportalUnread) = LiveCoverage.Parse(text, LiveCoverage.VizPortalSection);

        var problems = LiveCoverage.Problems(rows, LiveCoverage.Methods(), LiveCoverage.Calls(), LiveCoverage.LiveTests());

        Assert.IsEmpty(unread, string.Join("\n", unread));
        Assert.IsEmpty(problems, string.Join("\n", problems));
        Assert.IsGreaterThanOrEqualTo(LiveCoverage.Methods().Count, rows.Count, "a row for each method at least");
        Assert.IsEmpty(vizportalUnread, "the Vizportal client's table, which S16 fills, is there");
        Assert.IsEmpty(vizportal);
        Assert.Contains("Tableau Server 2025.1.5", text.Split('\n')[2], "the lead line names the server the nightly runs on");
    }

    [TestMethod]
    public void TheMethodsAreTheSubClientsAndTheCredentialsFactoriesAndTheCallsTheRegistrys()
    {
        var methods = LiveCoverage.Methods();

        CollectionAssert.IsSubsetOf(new[]
        {
            "ServerClient.GetInfoAsync", "SessionsClient.SwitchSiteAsync", "MetadataClient.QueryAsync", "MetadataClient.QueryAsync<T>",
            "MetadataClient.ListNodesAsync<T>", "TableauCredentials.PersonalAccessToken", "TableauCredentials.UnifiedAccessToken",
        }, methods.ToList());
        Assert.IsFalse(methods.Any(method => method.StartsWith("TableauClient.", StringComparison.Ordinal)), "the client's own members send no call of their own");
        Assert.HasCount(5, methods.Where(method => method.StartsWith("TableauCredentials.", StringComparison.Ordinal)), "overloads share a row");
        Assert.HasCount(Pipeline.Endpoints.All.Count, LiveCoverage.Calls(), "each call's name is its own");
        Assert.IsTrue(LiveCoverage.LiveTests().Contains("ServerInfoSiteTests.ServerInfoAnswersWithoutASignInAtTheNegotiatedVersion"));
        Assert.IsFalse(LiveCoverage.LiveTests().Contains("ServerOnlySessionTests.APasswordSignInImpersonatesAUser"), "a ServerOnly test is no Live one");
    }

    [TestMethod]
    public void AnInventedTableThatHoldsEachMethodAndCallOnceWithItsTestsOrReasonsPasses()
    {
        Assert.IsEmpty(Problems(Table(
            "| `ProjectsClient.ListAsync` | Query Projects | yes | `LiveAreaTests.AProjectIsMade`, `LiveSite` |",
            "| `ProjectsClient.DeleteAsync` | Delete Project | refused 403004 | `LiveAreaTests.AProjectIsMade`, `SuiteCleanup` |",
            "| `MetadataClient.QueryAsync<T>` | Metadata API query | no | the Metadata API is off |")));
    }

    [TestMethod]
    public void AMethodWithNoRowAndARowNamingNoMethodFail()
    {
        var problems = Problems(Table(
            "| `ProjectsClient.ListAsync` | Query Projects | yes | `LiveAreaTests.AProjectIsMade` |",
            "| `ProjectsClient.Delete` | Delete Project | yes | `LiveAreaTests.AProjectIsMade` |",
            "| `MetadataClient.QueryAsync` | Metadata API query | no | the Metadata API is off |"));

        CollectionAssert.AreEquivalent(new[]
        {
            "line 8: ProjectsClient.Delete is no public method of the client's",
            "line 9: MetadataClient.QueryAsync is no public method of the client's",
            "ProjectsClient.DeleteAsync has no row",
            "MetadataClient.QueryAsync<T> has no row",
        }, problems);
    }

    [TestMethod]
    public void ACallWithNoRowAndARowNamingNoCallFail()
    {
        var problems = Problems(Table(
            "| `ProjectsClient.ListAsync` | Query Projects | yes | `LiveAreaTests.AProjectIsMade` |",
            "| `ProjectsClient.DeleteAsync` | Delete Projects | yes | `LiveAreaTests.AProjectIsMade` |",
            "| `MetadataClient.QueryAsync<T>` | Query Projects | no | the Metadata API is off |"));

        CollectionAssert.AreEquivalent(new[]
        {
            "line 8: 'Delete Projects' is no call of Endpoints.All",
            "'Delete Project' has no row",
            "'Metadata API query' has no row",
        }, problems);
    }

    [TestMethod]
    public void AYesOrRefusedRowNamingNoTestOrOneThatIsNoLiveSiteTestFails()
    {
        var problems = Problems(Table(
            "| `ProjectsClient.ListAsync` | Query Projects | yes | `ProjectsTests.ListPageSortsByAStableOrderUnlessToldAndReadsTheProjects` |",
            "| `ProjectsClient.DeleteAsync` | Delete Project | refused 403004 | `LiveAreaTests.AProjectIsMade`, `ServerOnlyAreaTests.AUserIsAddedUpdatedAndRemoved` |",
            "| `MetadataClient.QueryAsync<T>` | Metadata API query | yes |  |"));

        CollectionAssert.AreEquivalent(new[]
        {
            "line 7, ProjectsClient.ListAsync and Query Projects: ProjectsTests.ListPageSortsByAStableOrderUnlessToldAndReadsTheProjects is no Live site "
                + "test, nor LiveSite or SuiteCleanup",
            "line 8, ProjectsClient.DeleteAsync and Delete Project: ServerOnlyAreaTests.AUserIsAddedUpdatedAndRemoved is no Live site test, nor LiveSite "
                + "or SuiteCleanup",
            "line 9, MetadataClient.QueryAsync<T> and Metadata API query: a yes row names the tests that make the call",
        }, problems);
    }

    [TestMethod]
    public void ANoRowWithoutItsReasonFails()
    {
        var problems = Problems(Table(
            "| `ProjectsClient.ListAsync` | Query Projects | yes | `LiveAreaTests.AProjectIsMade` |",
            "| `ProjectsClient.DeleteAsync` | Delete Project | yes | `LiveAreaTests.AProjectIsMade` |",
            "| `MetadataClient.QueryAsync<T>` | Metadata API query | no |   |"));

        CollectionAssert.AreEqual(new[] { "line 9, MetadataClient.QueryAsync<T> and Metadata API query: a no row says why not" }, problems);
    }

    [TestMethod]
    public void AMethodAndACallWithTwoRowsFail()
    {
        var problems = Problems(Table(
            "| `ProjectsClient.ListAsync` | Query Projects | yes | `LiveAreaTests.AProjectIsMade` |",
            "| `ProjectsClient.DeleteAsync` | Delete Project | yes | `LiveAreaTests.AProjectIsMade` |",
            "| `MetadataClient.QueryAsync<T>` | Metadata API query | yes | `LiveAreaTests.AQuery` |",
            "| `ProjectsClient.ListAsync` | Query Projects | no | another reading |"));

        CollectionAssert.AreEqual(new[] { "line 10, ProjectsClient.ListAsync and Query Projects: a second row for them, after line 7's" }, problems);
    }

    [TestMethod]
    [DataRow("Yes")]
    [DataRow("refused")]
    [DataRow("refused 4031")]
    [DataRow("refused NotFound")]
    [DataRow("no: not yet")]
    public void ALiveCellThatIsNotYesRefusedWithACodeOrNoFails(string live)
    {
        var problems = Problems(Table(
            "| `ProjectsClient.ListAsync` | Query Projects | yes | `LiveAreaTests.AProjectIsMade` |",
            $"| `ProjectsClient.DeleteAsync` | Delete Project | {live} | `LiveAreaTests.AProjectIsMade` |",
            "| `MetadataClient.QueryAsync<T>` | Metadata API query | yes | `LiveAreaTests.AQuery` |"));

        CollectionAssert.AreEqual(new[]
        {
            $"line 8, ProjectsClient.DeleteAsync and Delete Project: Live is '{live}', where it is yes, refused and Tableau's six-digit code, or no",
        }, problems);
    }

    [TestMethod]
    public void ATableThatCantBeReadSaysWhy()
    {
        var (_, cells) = LiveCoverage.Parse(Table("| `ProjectsClient.ListAsync` | Query Projects | yes |"), "TableauClient");
        var (_, pipe) = LiveCoverage.Parse(Table("| `ProjectsClient.ListAsync` | Query Projects | no | a | b |"), "TableauClient");
        var (_, section) = LiveCoverage.Parse(Table(), "Other");
        var (_, header) = LiveCoverage.Parse("# Live coverage\n\n## TableauClient\n\n| Method | Call | Live |\n|---|---|---|\n", "TableauClient");
        var (rows, none) = LiveCoverage.Parse("## TableauClient\n\nNo table yet.\n\n## VizPortalClient\n\n| Method | Call | Live | Test, or why not |\n|---|---|---|---|\n",
            "TableauClient");

        CollectionAssert.AreEqual(new[] { "line 7: a row has four cells, between pipes" }, cells);
        CollectionAssert.AreEqual(new[] { "line 7: a row has four cells, between pipes" }, pipe, "a reason holds no pipe");
        CollectionAssert.AreEqual(new[] { "no section '## Other'" }, section);
        CollectionAssert.AreEqual(new[] { "the section '## TableauClient' has no table headed '| Method | Call | Live | Test, or why not |'" }, header);
        CollectionAssert.AreEqual(new[] { "the section '## TableauClient' has no table headed '| Method | Call | Live | Test, or why not |'" }, none,
            "the next section's table is not this one's");
        Assert.IsEmpty(rows);
    }

    [TestMethod]
    public void ARecordThatBearsOutEveryRowPasses()
    {
        var rows = Rows();

        Assert.IsEmpty(LiveCoverage.NightlyProblems(rows,
        [
            Answer("LiveAreaTests.AProjectIsMade", "Query Projects"),
            Answer("LiveAreaTests.AProjectIsMade", "Query Projects", 404, "404000"),
            Refusal("LiveAreaTests.AProjectIsMade", "Delete Project", 403, "403004"),
            Answer("LiveAreaTests.Other", "Create Group"),
        ], []));
    }

    [TestMethod]
    public void AYesRowWhoseCallNoTestOfItsRowHadAnsweredWithA2xxFails()
    {
        var rows = Rows();

        var elsewhere = LiveCoverage.NightlyProblems(rows, [Answer("LiveAreaTests.Other", "Query Projects"), DeleteRefused()], []);
        var refused = LiveCoverage.NightlyProblems(rows, [Refusal("LiveAreaTests.AProjectIsMade", "Query Projects", 400, "400000"), DeleteRefused()], []);
        var none = LiveCoverage.NightlyProblems(rows, [DeleteRefused()], []);

        CollectionAssert.AreEqual(new[]
        {
            "line 7, ProjectsClient.ListAsync and Query Projects: yes, but no test of the row had it answered with a 2xx; its tests didn't have it "
                + "answered at all",
        }, elsewhere);
        CollectionAssert.AreEqual(new[]
        {
            "line 7, ProjectsClient.ListAsync and Query Projects: yes, but no test of the row had it answered with a 2xx; they had it answered "
                + "400 400000 in LiveAreaTests.AProjectIsMade",
        }, refused);
        CollectionAssert.AreEqual(elsewhere, none);
    }

    [TestMethod]
    public void ARefusedRowRefusedWithAnotherCodeOrNotAtAllFails()
    {
        var rows = Rows();

        var other = LiveCoverage.NightlyProblems(rows, [ListAnswered(), Refusal("LiveAreaTests.AProjectIsMade", "Delete Project", 404, "404005")], []);
        var none = LiveCoverage.NightlyProblems(rows, [ListAnswered()], []);

        CollectionAssert.AreEqual(new[]
        {
            "line 8, ProjectsClient.DeleteAsync and Delete Project: refused 403004, but no test of the row had it refused with 403004; they had it "
                + "answered 404 404005 in LiveAreaTests.AProjectIsMade",
        }, other);
        CollectionAssert.AreEqual(new[]
        {
            "line 8, ProjectsClient.DeleteAsync and Delete Project: refused 403004, but no test of the row had it refused with 403004; its tests "
                + "didn't have it answered at all",
        }, none);
    }

    [TestMethod]
    public void ARefusedRowAnsweredWithA2xxFailsSayingTheRowShouldSayYes()
    {
        var problems = LiveCoverage.NightlyProblems(Rows(),
            [ListAnswered(), DeleteRefused(), Answer("LiveAreaTests.AProjectIsMade", "Delete Project", 204)], ["LiveAreaTests.AProjectIsMade"]);

        CollectionAssert.AreEqual(new[]
        {
            "line 8, ProjectsClient.DeleteAsync and Delete Project: refused 403004, but LiveAreaTests.AProjectIsMade had it answered 204: the row "
                + "should say yes",
        }, problems, "a success says so whatever else failed");
    }

    [TestMethod]
    public void ANoRowWhoseCallWasAnsweredFailsUnlessAnotherRowHoldsTheCallLive()
    {
        var rows = Rows();
        var shared = Rows("| `ProjectsClient.ListPageAsync` | Query Projects | no | not live yet |");

        var answered = LiveCoverage.NightlyProblems(rows, [ListAnswered(), DeleteRefused(), Refusal("SuiteCleanup", "Metadata API query", 403, "403000")], []);
        var held = LiveCoverage.NightlyProblems(shared, [ListAnswered(), DeleteRefused()], []);

        CollectionAssert.AreEqual(new[]
        {
            "line 9, MetadataClient.QueryAsync<T> and Metadata API query: no, but SuiteCleanup had it answered 403 403000: the row should change",
        }, answered);
        Assert.IsEmpty(held, "the record is per call, and the list's row holds Query Projects live");
    }

    [TestMethod]
    public void ARowWhoseTestFailedIsLeftOutAndACallTheTableLacksIsPassedOver()
    {
        var rows = Rows();

        var failed = LiveCoverage.NightlyProblems(rows, [], ["LiveAreaTests.AProjectIsMade"]);
        var setupFailed = LiveCoverage.NightlyProblems(Rows("| `GroupsClient.CreateAsync` | Create Group | yes | `LiveSite` |"),
            [ListAnswered(), DeleteRefused()], ["LiveSite"]);
        var unknown = LiveCoverage.NightlyProblems(rows, [ListAnswered(), DeleteRefused(), Answer("LiveAccessTests.AGroupSet", "Create Group Set")], []);

        Assert.IsEmpty(failed);
        Assert.IsEmpty(setupFailed, "the suite's setup fails as a test does");
        Assert.IsEmpty(unknown, "the test-side Create Group Set has no row");
    }

    [TestMethod]
    public async Task TheRecordKeepsEachCallTableauAnsweredByTheTestThatMadeItAndPassesEveryLineOn()
    {
        using var server = AreaFixtures.Server();
        server.Map("GET", AreaFixtures.SitePath + "/projects", FakeResponse.Fixture(200, "projects-page.json"));
        server.Map("DELETE", AreaFixtures.SitePath + "/projects/" + AreaFixtures.ProjectId,
            FakeResponse.Json(404, """{"error":{"code":"404005","summary":"Resource Not Found"}}"""));
        var inner = new RecordingLogger();
        var record = new CallRecord(inner);
        await using (var client = TableauClient.CreateWithClock(SessionFixtures.Options(server), SessionFixtures.Pat(), record.Factory, null))
        {
            using (record.As("LiveAreaTests.AProjectIsMade"))
            {
                await client.Projects.ListPageAsync(1);
                await ClientFixtures.FailsAsync(TableauErrorKind.NotFound, () => client.Projects.DeleteAsync(AreaFixtures.ProjectId));
                await ClientFixtures.FailsAsync(TableauErrorKind.UnsupportedApiVersion,
                    () => client.Workbooks.DownloadPdfAsync(AreaFixtures.ItemId, new ExportOptions { Width = 1200, Height = 900 }));
                using (record.As(LiveSite.SetupName))
                    await client.Projects.ListPageAsync(1);
                Assert.AreEqual("LiveAreaTests.AProjectIsMade", record.Current, "a scope gives the name before it back");
            }
            Assert.IsNull(record.Current);
            await client.Projects.ListPageAsync(1);
        }

        CollectionAssert.AreEqual(new[]
        {
            new AnsweredCall("LiveAreaTests.AProjectIsMade", "Sign In with a personal access token", 200, null, true),
            new AnsweredCall("LiveAreaTests.AProjectIsMade", "Query Projects", 200, null, true),
            new AnsweredCall("LiveAreaTests.AProjectIsMade", "Delete Project", 404, "404005", false),
            new AnsweredCall(LiveSite.SetupName, "Query Projects", 200, null, true),
            new AnsweredCall(CallRecord.NoTest, "Query Projects", 200, null, true),
        }, record.Answered.Where(call => call.Call != "Sign Out").ToArray(), "a call refused before sending wasn't answered");
        Assert.IsTrue(inner.Lines.Any(line => line.Contains("Download Workbook PDF at a size: GET ", StringComparison.Ordinal)
            && line.Contains("failed: UnsupportedApiVersion", StringComparison.Ordinal)), "every line goes on to the logger given");
        Assert.HasCount(inner.Lines.Count, inner.Entries);
    }

    [TestMethod]
    public async Task AFailureStaysAndTheSuitesOwnNameEndsFailedWhenItsCallThrows()
    {
        var record = new CallRecord(new RecordingLogger());

        record.Ended("LiveAreaTests.A", passed: false);
        record.Ended("LiveAreaTests.A", passed: true);
        record.Ended("LiveAreaTests.B", passed: true);
        Assert.AreEqual(7, await record.AsAsync(SuiteCleanup.Name, () => Task.FromResult(7)));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => record.AsAsync<int>(LiveSite.SetupName,
            () => throw new InvalidOperationException("Create Site failed.")));

        CollectionAssert.AreEquivalent(new[] { "LiveAreaTests.A", LiveSite.SetupName }, record.Failed.ToList());
        Assert.IsNull(record.Current);
    }

    // An invented table: the title and the section, then its rows from line 7.
    private static string Table(params string[] rows) =>
        "# Live coverage\n\n## TableauClient\n\n| Method | Call | Live | Test, or why not |\n|---|---|---|---|\n" + string.Join("\n", rows)
        + "\n\n## VizPortalClient\n";

    private static List<string> Problems(string table)
    {
        var (rows, unread) = LiveCoverage.Parse(table, "TableauClient");
        Assert.IsEmpty(unread, string.Join("\n", unread));
        return LiveCoverage.Problems(rows, _methods, _calls, _tests);
    }

    // The invented table's rows the nightly's check reads, at lines 7 to 9, and any more after them.
    private static List<CoverageRow> Rows(params string[] more) => LiveCoverage.Parse(Table(
    [
        "| `ProjectsClient.ListAsync` | Query Projects | yes | `LiveAreaTests.AProjectIsMade` |",
        "| `ProjectsClient.DeleteAsync` | Delete Project | refused 403004 | `LiveAreaTests.AProjectIsMade` |",
        "| `MetadataClient.QueryAsync<T>` | Metadata API query | no | the Metadata API is off |",
        .. more,
    ]), "TableauClient").Rows;

    private static AnsweredCall Answer(string test, string call, int status = 200, string? code = null) =>
        new(test, call, status, code, code is null);

    private static AnsweredCall Refusal(string test, string call, int status, string code) => new(test, call, status, code, false);

    private static AnsweredCall ListAnswered() => Answer("LiveAreaTests.AProjectIsMade", "Query Projects");

    private static AnsweredCall DeleteRefused() => Refusal("LiveAreaTests.AProjectIsMade", "Delete Project", 403, "403004");
}
