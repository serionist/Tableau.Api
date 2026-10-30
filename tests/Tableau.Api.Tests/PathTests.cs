using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Tableau.Api.Models;
using Tableau.Api.Tests.Fakes;

namespace Tableau.Api.Tests;

/// <summary>
/// Path segments: each value a method puts in a path is escaped and sent as one segment, and a value that is empty or
/// only dots is refused before anything is sent. The rows go through Query Data Source, whose path ends in the value.
/// </summary>
[TestClass]
public sealed class PathTests
{
    private const string DatasourcesPath = "/api/" + Fixtures.ApiVersion + "/sites/" + Fixtures.SiteId + "/datasources/";

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

        var thrown = await Assert.ThrowsAsync<ArgumentException>(() => client.QueryDataSource(session, "d1"));

        Assert.AreEqual("session", thrown.ParamName);
        Assert.IsEmpty(server.Requests);
    }

    [TestMethod]
    public async Task ATagThatIsOnlyDotsIsRefusedBeforeSending()
    {
        using var server = FakeTableauServer.Start();
        var client = Fixtures.Client(server);

        var thrown = await Assert.ThrowsAsync<ArgumentException>(
            () => client.DeleteTagFromDataSource(Fixtures.Session(), "d1", ".."));

        Assert.AreEqual("tag", thrown.ParamName);
        Assert.IsEmpty(server.Requests);
    }

    [TestMethod]
    public async Task ATagAndASiteNameAreEachSentAsOneSegment()
    {
        using var server = FakeTableauServer.Start();
        var client = Fixtures.Client(server);
        var session = Fixtures.Session();
        var tagPath = DatasourcesPath + "d1/tags/C%23%20%26%20R%2FD";
        var sitePath = "/api/" + Fixtures.ApiVersion + "/sites/Sales%2FEMEA%3F";
        server.Map("DELETE", tagPath, FakeResponse.Empty(204));
        server.Map("GET", sitePath, FakeResponse.Xml(200, "<site id=\"s2\" name=\"Sales/EMEA?\" contentUrl=\"emea\"/>"));

        await client.DeleteTagFromDataSource(session, "d1", "C# & R/D");
        var site = await client.QuerySiteByName(session, "Sales/EMEA?");

        CollectionAssert.AreEqual(
            new[] { $"DELETE {tagPath} HTTP/1.1", $"GET {sitePath}?key=name HTTP/1.1" },
            server.Requests.Select(request => request.RequestLine).ToArray());
        Assert.AreEqual("s2", site.id);
    }

    [TestMethod]
    [DataRow("tableau/")]
    [DataRow("tableau")]
    public async Task ABaseUrlsPathIsKept(string basePath)
    {
        using var server = FakeTableauServer.Start();
        var client = new TableauApiClient(new Uri(server.BaseUrl, basePath), Fixtures.ApiVersion);
        server.Map("POST", $"/tableau/api/{Fixtures.ApiVersion}/auth/signin", Fixtures.SignedIn());

        var session = await client.SignIn(Fixtures.UserName, Fixtures.Password, Fixtures.SiteContentUrl);

        Assert.AreEqual($"POST /tableau/api/{Fixtures.ApiVersion}/auth/signin HTTP/1.1", server.Requests.Single().RequestLine);
        Assert.AreEqual(Fixtures.SessionToken, session.Token);
    }

    [TestMethod]
    public async Task AnUnpairedSurrogateIsRefusedBeforeSending()
    {
        using var server = FakeTableauServer.Start();
        var client = Fixtures.Client(server);

        await Assert.ThrowsAsync<ArgumentException>(() => client.QueryDataSource(Fixtures.Session(), "a\uD800b"));

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
        TableauSession session, string value)
    {
        var path = DatasourcesPath + PathRows.Escaped(value);
        server.Map("GET", path, FakeResponse.Xml(200, "<datasource id=\"d1\" name=\"Sales\"/>"));
        var before = server.Requests.Count;
        Exception? thrown = null;
        try
        {
            await client.QueryDataSource(session, value);
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
