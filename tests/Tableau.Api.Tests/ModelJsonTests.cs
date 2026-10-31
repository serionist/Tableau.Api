using System;
using System.Linq;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Tableau.Api.Rest.Models;

namespace Tableau.Api.Tests;

/// <summary>
/// How the models read and write the shapes Tableau's JSON takes: lists in their wrappers, empty, of one or of many;
/// numbers as strings; enumeration values this version doesn't know; times, instants, and a type JSON can't tell from
/// the types that extend it.
/// </summary>
[TestClass]
public sealed class ModelJsonTests
{
    private static T Read<T>(string json) =>
        (T)JsonSerializer.Deserialize(json, TableauJsonContext.Default.GetTypeInfo(typeof(T))!)!;

    private static string Write<T>(T value) => JsonSerializer.Serialize(value, TableauJsonContext.Default.GetTypeInfo(typeof(T))!);

    [TestMethod]
    [DataRow("""{"tags":{"tag":[{"label":"a"},{"label":"b"},{"label":"c"}]}}""", "a,b,c")]
    [DataRow("""{"tags":{"tag":[{"label":"a"}]}}""", "a")]
    [DataRow("""{"tags":{"tag":{"label":"a"}}}""", "a")]
    [DataRow("""{"tags":{}}""", "")]
    [DataRow("""{"tags":{"tag":[]}}""", "")]
    [DataRow("""{"tags":{"tag":null}}""", "")]
    [DataRow("""{"tags":{"tag":[{"label":"a"},null]}}""", "a")]
    [DataRow("""{"tags":{"count":"1","tag":[{"label":"a"}],"more":{"x":[1]}}}""", "a")]
    public void AWrappedListReadsManyOneOrNone(string json, string labels)
    {
        var tags = Read<Workbook>(json).Tags;

        Assert.IsNotNull(tags);
        Assert.AreEqual(labels, string.Join(',', tags.Select(tag => tag.Label)));
    }

    [TestMethod]
    [DataRow("""{}""")]
    [DataRow("""{"tags":null}""")]
    public void AWrappedListThatIsNotThereIsNull(string json) => Assert.IsNull(Read<Workbook>(json).Tags);

    [TestMethod]
    [DataRow("""{"tags":[{"label":"a"}]}""")]
    [DataRow("""{"tags":"a"}""")]
    public void AWrappedListWithoutItsWrapperFails(string json) =>
        Assert.ThrowsExactly<JsonException>(() => Read<Workbook>(json));

    [TestMethod]
    public void AWrappedListIsWrittenInItsWrapperAsAnArray()
    {
        Assert.AreEqual("""{"tags":{}}""", Write(new Workbook { Tags = [] }));
        Assert.AreEqual("""{"tags":{"tag":[{"label":"a"}]}}""", Write(new Workbook { Tags = [new Tag { Label = "a" }] }));
        Assert.AreEqual("""{"tags":{"tag":[{"label":"a"},{"label":"b"}]}}""",
            Write(new Workbook { Tags = [new Tag { Label = "a" }, new Tag { Label = "b" }] }));
    }

    [TestMethod]
    public void AWrappedListOfStringsReadsAndWrites()
    {
        var request = Read<TsRequest>("""{"connectionLuids":{"connectionLuid":["a","b"]},"scheduleLuids":{"scheduleLuid":"c"}}""");

        CollectionAssert.AreEqual(new[] { "a", "b" }, request.ConnectionLuids!.ToArray());
        CollectionAssert.AreEqual(new[] { "c" }, request.ScheduleLuids!.ToArray());
        Assert.AreEqual("""{"connectionLuids":{"connectionLuid":["a","b"]},"scheduleLuids":{"scheduleLuid":["c"]}}""", Write(request));
    }

    [TestMethod]
    [DataRow("""{"granteeCapabilities":[{"user":{"id":"u1"}},{"group":{"id":"g1"}}]}""", 2,
        """{"granteeCapabilities":[{"user":{"id":"u1"}},{"group":{"id":"g1"}}]}""")]
    [DataRow("""{"granteeCapabilities":[{"user":{"id":"u1"}}]}""", 1, """{"granteeCapabilities":[{"user":{"id":"u1"}}]}""")]
    [DataRow("""{"granteeCapabilities":{"user":{"id":"u1"}}}""", 1, """{"granteeCapabilities":[{"user":{"id":"u1"}}]}""")]
    [DataRow("""{"granteeCapabilities":[]}""", 0, """{"granteeCapabilities":[]}""")]
    public void ARepeatedElementReadsAnArrayOrOneObjectAndWritesAnArray(string json, int count, string written)
    {
        var permissions = Read<Permissions>(json);

        Assert.HasCount(count, permissions.GranteeCapabilities!);
        Assert.AreEqual(written, Write(permissions));
    }
    [TestMethod]
    public void ARepeatedElementThatIsNotThereIsNull() =>
        Assert.IsNull(Read<Permissions>("""{"granteeCapabilities":null}""").GranteeCapabilities);

    [TestMethod]
    public void RepeatedStringsReadAnArrayOrOneString()
    {
        CollectionAssert.AreEqual(new[] { "a", "b" }, Read<DataSource>("""{"parentDataSourceUrls":["a","b"]}""").ParentDataSourceUrls!.ToArray());
        CollectionAssert.AreEqual(new[] { "a" }, Read<DataSource>("""{"parentDataSourceUrls":"a"}""").ParentDataSourceUrls!.ToArray());
    }

    [TestMethod]
    public void NumbersReadFromStringsOrNumbersAndWriteAsNumbers()
    {
        var pagination = Read<Pagination>("""{"pageNumber":"2","pageSize":100,"totalAvailable":"9007199254740993"}""");

        Assert.AreEqual(new Pagination { PageNumber = 2, PageSize = 100, TotalAvailable = 9007199254740993 }, pagination);
        Assert.AreEqual("""{"pageNumber":2,"pageSize":100,"totalAvailable":9007199254740993}""", Write(pagination));
        Assert.AreEqual(0.75m, Read<Recommendation>("""{"score":"0.75"}""").Score);
        Assert.AreEqual(-1L, Read<Site>("""{"revisionLimit":"-1"}""").RevisionLimit);
    }

    [TestMethod]
    public void ANumberThatIsNoNumberFails() =>
        Assert.ThrowsExactly<JsonException>(() => Read<Pagination>("""{"pageNumber":"one"}"""));

    [TestMethod]
    public void AnUnknownEnumerationValueReadsAndKeepsItsText()
    {
        var user = Read<User>("""{"siteRole":"InventedFutureRole","authSetting":"SAML"}""");

        Assert.AreEqual(new SiteRole("InventedFutureRole"), user.SiteRole);
        Assert.AreNotEqual(SiteRole.Creator, user.SiteRole);
        Assert.AreEqual("InventedFutureRole", user.SiteRole.ToString());
        Assert.AreEqual(SiteUserAuthSetting.Saml, user.AuthSetting);
        Assert.AreEqual("""{"authSetting":"SAML","siteRole":"InventedFutureRole"}""", Write(user));
    }

    [TestMethod]
    public void AnEnumerationValueEqualsItsKnownValueByItsExactText()
    {
        Assert.AreEqual(SiteRole.Viewer, new SiteRole("Viewer"));
        Assert.IsTrue(SiteRole.Viewer == new SiteRole("Viewer"));
        Assert.AreNotEqual(SiteRole.Viewer, new SiteRole("viewer"));
        Assert.AreEqual("", default(SiteRole).ToString());
        Assert.IsNull(Read<User>("""{"siteRole":null}""").SiteRole);
    }

    [TestMethod]
    [DataRow("""{"siteRole":1}""")]
    [DataRow("""{"siteRole":true}""")]
    [DataRow("""{"siteRole":{}}""")]
    public void AnEnumerationValueThatIsNoStringFails(string json) =>
        Assert.ThrowsExactly<JsonException>(() => Read<User>(json));

    [TestMethod]
    public void ATimeIsWrittenAsHoursMinutesAndSeconds()
    {
        var details = new FrequencyDetails { Start = new TimeOnly(18, 30), End = new TimeOnly(6, 5, 9) };

        Assert.AreEqual("""{"end":"06:05:09","start":"18:30:00"}""", Write(details));
        Assert.AreEqual(details, Read<FrequencyDetails>("""{"start":"18:30:00","end":"06:05:09"}"""));
    }

    [TestMethod]
    public void AnInstantReadsInUtc()
    {
        var at = Read<Project>("""{"createdAt":"2026-10-30T07:45:12Z"}""").CreatedAt;

        Assert.AreEqual(new DateTimeOffset(2026, 10, 30, 7, 45, 12, TimeSpan.Zero), at);
        Assert.AreEqual(TimeSpan.Zero, at!.Value.Offset);
    }

    [TestMethod]
    public void NothingSetWritesNothing()
    {
        Assert.AreEqual("{}", Write(new Workbook()));
        Assert.AreEqual("""{"name":"a"}""", Write(new Project { Name = "a" }));
    }

    [TestMethod]
    public void AMemberTheSchemaDoesNotNameIsSkipped() =>
        Assert.AreEqual(new Tag { Label = "a" }, Read<Tag>("""{"label":"a","addedIn":"3.30","more":{"x":[1,2]}}"""));

    [TestMethod]
    public void ATypeThatOthersExtendHoldsTheirMembers()
    {
        var list = Read<FlowParameter>("""{"domain":{"domainType":"list","values":{"value":["a","b"]}}}""").Domain!;
        var range = Read<FlowParameter>("""{"domain":{"domainType":"range","minValue":"1","maxValue":"9","stepSize":"2"}}""").Domain!;

        CollectionAssert.AreEqual(new[] { "a", "b" }, list.Values!.ToArray());
        Assert.IsNull(list.MinValue);
        Assert.AreEqual(new FlowParameterDomain { DomainType = "range", MinValue = "1", MaxValue = "9", StepSize = "2" }, range);
    }

    [TestMethod]
    public void AUnionOfANumberAndATextReadsAsText()
    {
        Assert.AreEqual("LastDay", Read<Interval>("""{"monthDay":"LastDay"}""").MonthDay);
        Assert.AreEqual("15", Read<Interval>("""{"monthDay":"15"}""").MonthDay);
        Assert.AreEqual(24L, Read<Interval>("""{"hours":"24"}""").Hours);
    }

    [TestMethod]
    public void ARecursiveTypeReadsItsNesting()
    {
        var action = Read<DataUpdateAction>("""
            {"action":"update","target-table":"t","condition":{"op":"and","args":[
              {"op":"eq","source-col":"a","target-col":"b"},{"op":"gt","const":{"type":"integer","v":"5"},"target-col":"c"}]},
             "actions":{"action":"insert"}}
            """);

        Assert.AreEqual("t", action.TargetTable);
        var args = action.Condition!.Args!;
        Assert.HasCount(2, args);
        Assert.AreEqual(DataUpdateConstConditionType.Integer, args[1].Const!.Type);
        Assert.AreEqual("insert", action.Actions!.Action);
    }
}
