using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Tableau.Api.Enums;
using Tableau.Api.Tests.Fakes;

namespace Tableau.Api.Tests;

/// <summary>Query strings: every key and value escaped, filter and sort expressions keeping their separators.</summary>
[TestClass]
public sealed class QueryTests
{
    private const string SitePath = "/api/" + Fixtures.ApiVersion + "/sites/" + Fixtures.SiteId;
    private const string ViewPath = SitePath + "/views/v1";
    private const string DataSourceList = "<pagination pageNumber=\"1\" pageSize=\"100\" totalAvailable=\"0\"/><datasources/>";

    [TestMethod]
    public async Task FilterAndSortExpressionsAreEscapedBetweenTheirSeparators()
    {
        using var server = FakeTableauServer.Start();
        var client = Fixtures.Client(server);
        server.Map("GET", SitePath + "/datasources", FakeResponse.Xml(200, DataSourceList));

        await client.QueryDataSources(Fixtures.Session(), fieldsExpression: "_default_,owner.name",
            filterExpression: "name:eq:R&D,tags:in:[C++,#1 100%]", sortExpression: "name:asc,createdAt:desc");

        Assert.AreEqual(
            "pageSize=100&pageNumber=1&fields=_default_%2Cowner.name"
            + "&filter=name:eq:R%26D,tags:in:%5BC%2B%2B,%231%20100%25%5D&sort=name:asc,createdAt:desc",
            server.Requests.Single().Query);
    }

    [TestMethod]
    public async Task AValueAlreadyEscapedIsEscapedAgain()
    {
        using var server = FakeTableauServer.Start();
        var client = Fixtures.Client(server);
        server.Map("GET", SitePath + "/datasources", FakeResponse.Xml(200, DataSourceList));

        await client.QueryDataSources(Fixtures.Session(), filterExpression: "name:eq:R%26D");

        Assert.AreEqual("pageSize=100&pageNumber=1&filter=name:eq:R%2526D", server.Requests.Single().Query);
    }

    [TestMethod]
    public async Task ViewFilterNamesAndValuesAreEscaped()
    {
        using var server = FakeTableauServer.Start();
        var client = Fixtures.Client(server);
        server.Map("GET", ViewPath + "/data", FakeResponse.Text(200, "Region,Sales\n"));

        using var data = await client.QueryViewData(Fixtures.Session(), "v1", null, ("R&D", "C++ #1 100%"), ("Région", "Côte: Nord, Sud"));

        Assert.AreEqual("vf_R%26D=C%2B%2B%20%231%20100%25&vf_R%C3%A9gion=C%C3%B4te%3A%20Nord%2C%20Sud",
            server.Requests.Single().Query);
    }

    [TestMethod]
    public async Task ViewDataAndImageFiltersGetTheirPrefix()
    {
        using var server = FakeTableauServer.Start();
        var client = Fixtures.Client(server);
        var session = Fixtures.Session();
        server.Map("GET", ViewPath + "/data", FakeResponse.Text(200, "Region,Sales\n"));
        server.Map("GET", ViewPath + "/image", FakeResponse.Text(200, "png"));

        using (await client.QueryViewData(session, "v1", null, ("Region", "West"), ("vf_Year", "2017"), ("VF_Month", "May")))
        {
        }
        using (await client.QueryViewImage(session, "v1", TableauImageWidth.High_1568, null, ("Region", "West"), ("vf_Year", "2017")))
        {
        }

        CollectionAssert.AreEqual(
            new[] { "vf_Region=West&vf_Year=2017&VF_Month=May", "resolution=high&vf_Region=West&vf_Year=2017" },
            server.Requests.Select(request => request.Query).ToArray());
    }

    [TestMethod]
    public async Task AViewPdfWithoutFiltersIsSent()
    {
        using var server = FakeTableauServer.Start();
        var client = Fixtures.Client(server);
        var session = Fixtures.Session();
        server.Map("GET", ViewPath + "/pdf", FakeResponse.Text(200, "%PDF-1.7"));

        using var pdf = await client.QueryViewPDF(session, "v1");
        using (await client.QueryViewPDF(session, "v1", null, PageType.A4, Orientation.Landscape, [("Region", "West")]))
        {
        }

        Assert.AreEqual("%PDF-1.7", await new StreamReader(pdf).ReadToEndAsync());
        CollectionAssert.AreEqual(
            new[] { "type=Legal&orientation=Portrait", "type=A4&orientation=Landscape&vf_Region=West" },
            server.Requests.Select(request => request.Query).ToArray());
    }
}
