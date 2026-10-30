using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Tableau.Api.Enums;
using Tableau.Api.Tests.Fakes;
using Tableau.Api.VizPortal.Models;

namespace Tableau.Api.Tests;

/// <summary>
/// Vizportal's query responses deserialise as Tableau Server 2025.1 sends them: the bodies have the keys and JSON types
/// read from a live server, with invented values.
/// </summary>
[TestClass]
public sealed class VizportalResponseTests
{
    private const string DatasourcesPage = """
        {"result":{"datasources":[
          {"id":"101","name":"Sales","hasExtracts":false,"lastRefreshedAt":"","tags":[],"dataQualityIndicators":[]},
          {"id":"102","name":"Orders","hasExtracts":true,"lastRefreshedAt":"2026-10-02T06:00:00Z","tags":["finance"],
           "dataQualityIndicators":[
             {"id":"9001","category":"invented-category","type":"WARNING","message":"Invented: late refresh",
              "isActive":true,"isSevere":true,"updatedAt":"2026-10-01T09:30:00Z",
              "owner":{"displayName":"Ada Lovelace","hasServerDisplay":true,"readOnly":false}},
             {"id":"9002","category":"invented-category","type":"DEPRECATED","message":"Invented: use Orders v2",
              "isActive":false,"isSevere":false,"updatedAt":"2026-09-15T00:00:00Z",
              "owner":{"displayName":"Ada Lovelace","hasServerDisplay":true,"readOnly":false}}]}],
         "users":[],"projects":[],"moreItems":false,"totalCount":2}}
        """;

    private const string SchedulesPage = """
        {"result":{"schedules":[{"id":"7","name":"Nightly","definedBy":"SYSTEM_ADMIN"}],"moreItems":false,"totalCount":1}}
        """;

    private const string SubscriptionsPage = """
        {"result":{"subscriptions":[
          {"id":"31","subject":"Flow run","targetType":"FLOW","targetId":"5","pageOrientation":"PORTRAIT","pageSizeOption":"LETTER"},
          {"id":"32","subject":"Sales view","targetType":"VIEW","targetId":"6","pageOrientation":"PORTRAIT","pageSizeOption":"LETTER"},
          {"id":"33","subject":"Sales workbook","targetType":"WORKBOOK","targetId":"8","pageOrientation":"PORTRAIT","pageSizeOption":"LETTER"}],
         "moreItems":false,"totalCount":3}}
        """;

    [TestMethod]
    public async Task QueryDatasourcesReadsDataQualityIndicatorsAsObjects()
    {
        using var server = FakeTableauServer.Start();
        var client = Fixtures.Client(server);
        server.Map("POST", Fixtures.VizportalPath + "getDatasources", FakeResponse.Json(200, DatasourcesPage));

        var pages = new List<(Datasource[], ContentUser[], ContentProjectDs[])>();
        await foreach (var page in client.VizPortal.QueryDatasources(Fixtures.Session()))
            pages.Add(page);

        Assert.AreEqual($"POST {Fixtures.VizportalPath}getDatasources HTTP/1.1", server.Requests.Single().RequestLine);
        var datasources = pages.Single().Item1;
        Assert.HasCount(2, datasources);
        Assert.IsEmpty(datasources[0].DataQualityIndicators);
        var indicators = datasources[1].DataQualityIndicators;
        CollectionAssert.AreEqual(new[] { "9001", "9002" }, indicators.Select(indicator => indicator.Id).ToArray());
        var warning = indicators[0];
        Assert.AreEqual("invented-category", warning.Category);
        Assert.AreEqual("WARNING", warning.Type);
        Assert.AreEqual("Invented: late refresh", warning.Message);
        Assert.IsTrue(warning.IsActive);
        Assert.IsTrue(warning.IsSevere);
        Assert.AreEqual(new DateTime(2026, 10, 1, 9, 30, 0, DateTimeKind.Utc), warning.UpdatedAt);
        Assert.AreEqual("Ada Lovelace", warning.Owner.DisplayName);
        Assert.IsTrue(warning.Owner.HasServerDisplay);
        Assert.IsFalse(warning.Owner.ReadOnly);
        Assert.IsFalse(indicators[1].IsActive);
        Assert.AreEqual("DEPRECATED", indicators[1].Type);
    }

    [TestMethod]
    public async Task QuerySubscriptionsForScheduleReadsASubscriptionToAFlow()
    {
        using var server = FakeTableauServer.Start();
        var client = Fixtures.Client(server);
        server.Map("POST", Fixtures.VizportalPath + "getSchedules", FakeResponse.Json(200, SchedulesPage));
        server.Map("POST", Fixtures.VizportalPath + "getSubscriptions", FakeResponse.Json(200, SubscriptionsPage));

        var subscriptions = new List<Subscription>();
        await foreach (var subscription in client.VizPortal.QuerySubscriptionsForSchedule(Fixtures.Session(), "1", "7"))
            subscriptions.Add(subscription);

        CollectionAssert.AreEqual(new[] { TargetType.Flow, TargetType.View, TargetType.Workbook },
            subscriptions.Select(subscription => subscription.TargetType).ToArray());
        Assert.AreEqual(PageOrientation.Portrait, subscriptions[0].PageOrientation);
        Assert.AreEqual(PageType.Letter, subscriptions[0].PageSizeOption);
    }
}
