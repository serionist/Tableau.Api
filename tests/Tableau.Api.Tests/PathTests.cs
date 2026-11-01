using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Tableau.Api.Models;
using Tableau.Api.Tests.Fakes;
using Tableau.Api.VizPortal.Models;

namespace Tableau.Api.Tests;

/// <summary>
/// Path segments: each value a method puts in a path is escaped and sent as one segment, and a value that is empty or
/// only dots is refused before anything is sent. The rows go through Query Subscription, whose path ends in the value.
/// </summary>
[TestClass]
public sealed class PathTests
{
    private const string SubscriptionsPath = "/api/" + Fixtures.ApiVersion + "/sites/" + Fixtures.SiteId + "/subscriptions/";

    [TestMethod]
    public async Task EveryAsciiCharacterIsSentEscaped() => await AssertRowsAsync(PathRows.Ascii());

    [TestMethod]
    public async Task ValuesThatAreEmptyOrOnlyDotsAreRefusedBeforeSending() => await AssertRowsAsync(PathRows.DotOnly());

    [TestMethod]
    public async Task SubDelimitersBesideNonAsciiCharactersAreSentRaw() =>
        await AssertRowsAsync(PathRows.SubDelimitersBesideNonAscii());

    [TestMethod]
    public async Task BidiControlsAreSentEscaped() => await AssertRowsAsync(PathRows.Bidi());

    [TestMethod]
    public async Task ASiteIdThatIsOnlyDotsIsRefusedBeforeSending()
    {
        using var server = FakeTableauServer.Start();
        var client = Fixtures.Client(server);
        var session = Fixtures.Session();
        session.SiteId = "..";

        var thrown = await Assert.ThrowsAsync<ArgumentException>(() => client.QuerySubscription(session, "s1"));

        Assert.AreEqual("session", thrown.ParamName);
        Assert.IsEmpty(server.Requests);
    }

    [TestMethod]
    [DataRow("tableau/")]
    [DataRow("tableau")]
    public async Task ABaseUrlsPathIsKept(string basePath)
    {
        using var server = FakeTableauServer.Start();
        var client = new TableauApiClient(new Uri(server.BaseUrl, basePath), Fixtures.ApiVersion);
        var subscriptions = $"/tableau/api/{Fixtures.ApiVersion}/sites/{Fixtures.SiteId}/subscriptions";
        server.Map("GET", subscriptions, FakeResponse.Xml(200, "<pagination pageNumber=\"1\" pageSize=\"100\" totalAvailable=\"0\"/><subscriptions/>"));

        await client.QuerySubscriptions(Fixtures.Session());

        Assert.AreEqual($"GET {subscriptions}?pageSize=100&pageNumber=1 HTTP/1.1", server.Requests.Single().RequestLine);
    }

    [TestMethod]
    [DataRow("tableau/")]
    [DataRow("tableau")]
    public async Task VizportalKeepsTheBaseUrlsPath(string basePath)
    {
        using var server = FakeTableauServer.Start();
        var client = new TableauApiClient(new Uri(server.BaseUrl, basePath), Fixtures.ApiVersion);
        const string Path = "/tableau" + Fixtures.VizportalPath + "updateUsersSiteRole";
        server.Map("POST", Path, Fixtures.VizportalResult());

        await client.VizPortal.ChangeUserRole(Fixtures.Session(), 42, "Viewer");

        Assert.AreEqual($"POST {Path} HTTP/1.1", server.Requests.Single().RequestLine);
    }

    // The public paging methods take the Vizportal method's name from the caller, and it ends the path.
    [TestMethod]
    [DataRow("get/Projects? #1", "get%2FProjects%3F%20%231")]
    [DataRow("http://127.0.0.1:1/elsewhere", "http:%2F%2F127.0.0.1:1%2Felsewhere")]
    public async Task AVizportalMethodNameIsSentAsOneSegment(string name, string escaped)
    {
        using var server = FakeTableauServer.Start();
        var client = Fixtures.Client(server);
        var path = Fixtures.VizportalPath + escaped;
        server.Map("POST", path, NoProjects());

        await foreach (var _ in client.VizPortal.PaginateRequest(Fixtures.Session(), name, new QueryModel(),
                           (ProjectQueryResponse page) => page.Projects))
        {
        }

        Assert.AreEqual($"POST {path} HTTP/1.1", server.Requests.Single().RequestLine);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("..")]
    public async Task AVizportalMethodNameThatIsEmptyOrOnlyDotsIsRefusedBeforeSending(string name)
    {
        using var server = FakeTableauServer.Start();
        var client = Fixtures.Client(server);

        var thrown = await Assert.ThrowsAsync<ArgumentException>(async () =>
        {
            await foreach (var _ in client.VizPortal.PaginateRequest(Fixtures.Session(), name, new QueryModel(),
                               (ProjectQueryResponse page) => page.Projects))
            {
            }
        });

        Assert.AreEqual("methodName", thrown.ParamName);
        Assert.IsEmpty(server.Requests);
    }

    private static FakeResponse NoProjects() =>
        FakeResponse.Json(200, "{\"result\":{\"moreItems\":false,\"totalCount\":0,\"projects\":[]}}");

    [TestMethod]
    public async Task AnUnpairedSurrogateIsRefusedBeforeSending()
    {
        using var server = FakeTableauServer.Start();
        var client = Fixtures.Client(server);

        await Assert.ThrowsAsync<ArgumentException>(() => client.QuerySubscription(Fixtures.Session(), "a\uD800b"));

        Assert.IsEmpty(server.Requests);
    }

    /// <summary>
    /// Each value is either refused with ArgumentException and nothing sent, or sent as exactly the escaped segment.
    /// The expected path is the only one mapped, so a call that sends another gets a 404 and fails.
    /// </summary>
    private static async Task AssertRowsAsync(IEnumerable<string> values)
    {
        using var server = FakeTableauServer.Start();
        var client = Fixtures.Client(server);
        var session = Fixtures.Session();
        var failures = new List<string>();
        var rows = 0;
        foreach (var value in values)
        {
            rows++;
            if (await CheckAsync(server, client, session, value) is { } failure) failures.Add(failure);
        }
        Assert.IsEmpty(failures, $"{failures.Count} of {rows} rows:\n" + string.Join("\n", failures));
    }

    private static async Task<string?> CheckAsync(FakeTableauServer server, TableauApiClient client,
        Models.TableauSession session, string value)
    {
        var path = SubscriptionsPath + PathRows.Escaped(value);
        server.Map("GET", path, FakeResponse.Xml(200, "<subscription id=\"s1\" subject=\"Sales\"/>"));
        var before = server.Requests.Count;
        Exception? thrown = null;
        try
        {
            await client.QuerySubscription(session, value);
        }
        catch (Exception ex)
        {
            thrown = ex;
        }
        var sent = server.Requests.Skip(before).Select(request => request.RequestLine).ToList();
        var outcome = $"{(thrown is null ? "returned" : thrown.GetType().Name)}, {sent.Count} request(s) {string.Join(" ", sent)}";
        if (PathRows.IsRefused(value))
            return thrown is ArgumentException && sent.Count == 0
                ? null
                : $"{PathRows.Show(value)}: expected ArgumentException and no request; {outcome}";
        return thrown is null && sent.Count == 1 && sent[0] == $"GET {path} HTTP/1.1"
            ? null
            : $"{PathRows.Show(value)}: expected GET {path}; {outcome}";
    }
}
