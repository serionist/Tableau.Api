using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Tableau.Api.Tests.Fakes;

namespace Tableau.Api.Tests;

/// <summary>Extract refresh tasks read by their kind and on their documented path; revision lists sent with paging.</summary>
[TestClass]
public sealed class TaskAndRevisionTests
{
    private const string SitePath = "/api/" + Fixtures.ApiVersion + "/sites/" + Fixtures.SiteId;
    private const string TasksPath = SitePath + "/tasks/extractRefreshes";

    [TestMethod]
    public async Task ExtractRefreshTasksAreReadByTheKindOfItemEachHolds()
    {
        using var server = FakeTableauServer.Start();
        var client = Fixtures.Client(server);
        server.Map("GET", TasksPath, FakeResponse.Xml(200,
            "<tasks><task><extractRefresh id=\"t1\"/></task><task><flowRun id=\"f1\"/></task>"
            + "<task><extractRefresh id=\"t2\"/></task></tasks>"));

        var (refreshes, flows, schedules) = await client.GetExtractRefreshTasks(Fixtures.Session());

        CollectionAssert.AreEqual(new[] { "t1", "t2" }, refreshes.Select(task => task.id).ToArray());
        CollectionAssert.AreEqual(new[] { "f1" }, flows.Select(task => task.id).ToArray());
        Assert.IsEmpty(schedules);
    }

    [TestMethod]
    public async Task AnEmptyTaskListIsEmpty()
    {
        using var server = FakeTableauServer.Start();
        var client = Fixtures.Client(server);
        server.Map("GET", TasksPath, FakeResponse.Xml(200, "<tasks/>"));

        var (refreshes, flows, schedules) = await client.GetExtractRefreshTasks(Fixtures.Session());

        Assert.IsEmpty(refreshes);
        Assert.IsEmpty(flows);
        Assert.IsEmpty(schedules);
    }

    [TestMethod]
    public async Task OneExtractRefreshTaskIsReadFromItsDocumentedPath()
    {
        using var server = FakeTableauServer.Start();
        var client = Fixtures.Client(server);
        server.Map("GET", TasksPath + "/t1", FakeResponse.Xml(200, "<task><extractRefresh id=\"t1\" priority=\"50\"/></task>"));

        var task = await client.GetExtractRefreshTask(Fixtures.Session(), "t1");

        Assert.AreEqual($"GET {TasksPath}/t1 HTTP/1.1", server.Requests.Single().RequestLine);
        Assert.AreEqual("t1", task.id);
    }

    [TestMethod]
    public async Task RevisionListsSendTheirPaging()
    {
        using var server = FakeTableauServer.Start();
        var client = Fixtures.Client(server);
        var session = Fixtures.Session();
        const string revisions = "<pagination pageNumber=\"3\" pageSize=\"10\" totalAvailable=\"25\"/>"
            + "<revisions><revision revisionNumber=\"21\"/></revisions>";
        server.Map("GET", SitePath + "/datasources/d1/revisions", FakeResponse.Xml(200, revisions));
        server.Map("GET", SitePath + "/workbooks/w1/revisions", FakeResponse.Xml(200, revisions));

        var (pagination, dataSourceRevisions) = await client.GetDataSourceRevisions(session, "d1", 10, 3);
        await client.GetWorkbookRevisions(session, "w1", 5, 2);

        CollectionAssert.AreEqual(new[] { "pageSize=10&pageNumber=3", "pageSize=5&pageNumber=2" },
            server.Requests.Select(request => request.Query).ToArray());
        Assert.AreEqual("3", pagination.pageNumber);
        Assert.AreEqual("21", dataSourceRevisions.Single().revisionNumber);
    }
}
