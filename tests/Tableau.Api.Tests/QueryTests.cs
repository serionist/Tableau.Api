using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Tableau.Api.Enums;
using Tableau.Api.Tests.Fakes;

namespace Tableau.Api.Tests;

/// <summary>
/// The old client's query strings: every value escaped, one already escaped escaped again. Its filter and sort
/// expressions and its view filters went with its lists and exports to the 2.0 client, whose rules the filter and the
/// export tests hold.
/// </summary>
[TestClass]
public sealed class QueryTests
{
    private const string SitePath = "/api/" + Fixtures.ApiVersion + "/sites/" + Fixtures.SiteId;

    [TestMethod]
    public async Task AValueAlreadyEscapedIsEscapedAgain()
    {
        using var server = FakeTableauServer.Start();
        var client = Fixtures.Client(server);
        server.Map("POST", SitePath + "/datasources", FakeResponse.Xml(201, "<datasource id=\"d1\" name=\"Sales\"/>"));

        await client.PublishDatasource(Fixtures.Session(), "R%26D 1:0", "Sales", DatasourceFileType.tdsx, "p1");

        Assert.AreEqual("uploadSessionId=R%2526D%201%3A0&datasourceType=tdsx&overwrite=False&append=False", server.Requests.Single().Query);
    }
}
