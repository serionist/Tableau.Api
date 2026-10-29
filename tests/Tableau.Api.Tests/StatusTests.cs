using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Tableau.Api.Models;
using Tableau.Api.Tests.Fakes;

namespace Tableau.Api.Tests;

/// <summary>Any 2xx is a success, whichever one Tableau's reference gives for the call.</summary>
[TestClass]
public sealed class StatusTests
{
    private const string SitePath = "/api/" + Fixtures.ApiVersion + "/sites/" + Fixtures.SiteId;

    // Tableau Server answers Create Subscription with 201, Tableau Cloud with 200.
    [TestMethod]
    [DataRow(200)]
    [DataRow(201)]
    public async Task CreatingASubscriptionSucceedsOn200And201(int status)
    {
        using var server = FakeTableauServer.Start();
        var client = Fixtures.Client(server);
        server.Map("POST", SitePath + "/subscriptions",
            FakeResponse.Xml(status, "<subscription id=\"sub1\" subject=\"Weekly sales\"/>"));

        var subscription = await client.CreateSubscription(Fixtures.Session(), "Weekly sales",
            subscriptionContentTypeType.Workbook, "w1", "sch1", Fixtures.UserId, null);

        Assert.AreEqual("sub1", subscription.id);
    }

    [TestMethod]
    [DataRow(202)]
    [DataRow(203)]
    public async Task AnyOther2xxIsASuccessToo(int status)
    {
        using var server = FakeTableauServer.Start();
        var client = Fixtures.Client(server);
        server.Map("GET", SitePath + "/projects", FakeResponse.Xml(status,
            "<pagination pageNumber=\"1\" pageSize=\"100\" totalAvailable=\"1\"/><projects><project id=\"p1\" name=\"Sales\"/></projects>"));

        var (_, projects) = await client.QueryProjects(Fixtures.Session());

        Assert.AreEqual("Sales", projects[0].name);
    }
}
