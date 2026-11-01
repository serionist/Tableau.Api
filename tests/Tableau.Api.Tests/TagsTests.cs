using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Tableau.Api.Tests.Fakes;

namespace Tableau.Api.Tests;

/// <summary>
/// <see cref="TagsClient"/>: each kind of content's add and delete from one table, with the path, the body and the
/// version the reference gives, a tag escaped as one path segment; the batch calls' bodies against the reference's, each
/// content with its type as Tableau Server 2025.1 asked, Batch Delete Tags a DELETE with that body; and a kind a call
/// doesn't take refused before sending.
/// </summary>
[TestClass]
public sealed class TagsTests
{
    private const string Site = AreaFixtures.SitePath;

    /// <summary>A tag with a space, an ampersand, a slash, a hash and a percent, which a path carries as one segment.</summary>
    private const string Awkward = "C# & R/D 100%";

    private const string AwkwardEscaped = "C%23%20%26%20R%2FD%20100%25";

    /// <summary>Each kind tagged one at a time: its path's segment, its version, and its name in the descriptors'.</summary>
    public static IEnumerable<object[]> Kinds =>
    [
        [TableauContentKind.Workbook, "workbooks", "1.0", "Workbook"],
        [TableauContentKind.View, "views", "1.0", "View"],
        [TableauContentKind.DataSource, "datasources", "1.0", "Data Source"],
        [TableauContentKind.VirtualConnection, "virtualconnections", "3.23", "Virtual Connection"],
        [TableauContentKind.Database, "databases", "3.9", "Database"],
        [TableauContentKind.Table, "tables", "3.9", "Table"],
        [TableauContentKind.Column, "columns", "3.9", "Column"],
    ];

    /// <summary>The batch's content, one of each kind it takes, as the reference's example lists them.</summary>
    private static readonly TableauContent[] _batch =
    [
        TableauContent.Database("21cade8c-d4eb-4fef-bd4a-bdb9d8b09b7d"),
        TableauContent.Table("a0314be1-afd4-4a4f-ab85-d75dac661c41"),
        TableauContent.Column("a0314be1-afd4-4a4f-ab85-d75dac661c41", "8fec4046-e054-476d-a6d0-e808b0d5448b"),
        TableauContent.DataSource("c8c3a0be-3ac8-4de7-8bd6-f912900a1ccd"),
        TableauContent.Workbook("ec2b7845-d627-430e-b99c-8543ed50b25c"),
        TableauContent.Flow("c4a560eb-c8a6-48c6-86f4-c9d5e518d027"),
    ];

    [TestMethod]
    [DynamicData(nameof(Kinds))]
    public async Task EachKindIsTaggedAndUntaggedOnItsOwnPath(TableauContentKind kind, string segment, string from, string name)
    {
        using var server = AreaFixtures.Server();
        var tags = $"{Site}/{segment}/{AreaFixtures.ItemId}/tags";
        server.Map("PUT", tags, FakeResponse.Fixture(200, "tags.json"));
        server.Map("DELETE", $"{tags}/{AwkwardEscaped}", FakeResponse.Empty(204));
        await using var client = AreaFixtures.Client(server);
        var content = Content(kind);

        var added = await client.Tags.AddAsync(content, ["Finance", Awkward]);
        await client.Tags.DeleteAsync(content, Awkward);

        var calls = AreaFixtures.Calls(server);
        CollectionAssert.AreEqual(new[] { $"PUT {tags}", $"DELETE {tags}/{AwkwardEscaped}" }, calls.Select(call => $"{call.Method} {call.Target}").ToArray());
        // A tag with a space goes in double quotes in the body, so Tableau keeps it one tag; the path names it as stored.
        Assert.AreEqual(AreaFixtures.Json($$$"""{"tags":{"tag":[{"label":"Finance"},{"label":"\"{{{Awkward}}}\""}]}}"""), AreaFixtures.Json(calls[0].BodyText));
        Assert.IsEmpty(calls[1].Body);
        CollectionAssert.AreEqual(new[] { "Noteworthy", "PII" }, added.Select(tag => tag.Label).ToArray());

        await PermissionsTests.AssertGatedAsync(from, $"Add Tags to {name}", client => client.Tags.AddAsync(content, ["Finance"]));
        await PermissionsTests.AssertGatedAsync(from, $"Delete Tag from {name}", client => client.Tags.DeleteAsync(content, "Finance"));
    }

    [TestMethod]
    [DataRow("Finance", "Finance")]
    [DataRow("Yearly Sales", "\"Yearly Sales\"")]
    [DataRow("Sales,Marketing", "\"Sales,Marketing\"")]
    [DataRow("Q1\tSales", "\"Q1\tSales\"")]
    [DataRow("Q1 Sales", "\"Q1 Sales\"")]
    [DataRow("\"Yearly Sales\"", "\"Yearly Sales\"")]
    [DataRow("C++#1", "C++#1")]
    [DataRow("\"", "\"")]
    public async Task ABodysLabelIsQuotedWhenItHoldsWhitespaceOrACommaAndIsntQuotedAlready(string tag, string sent)
    {
        using var server = AreaFixtures.Server();
        server.Map("PUT", $"{Site}/workbooks/{AreaFixtures.ItemId}/tags", FakeResponse.Fixture(200, "tags.json"));
        await using var client = AreaFixtures.Client(server);

        await client.Tags.AddAsync(TableauContent.Workbook(AreaFixtures.ItemId), [tag]);

        Assert.AreEqual(sent, System.Text.Json.Nodes.JsonNode.Parse(AreaFixtures.Calls(server).Single().BodyText)!["tags"]!["tag"]![0]!["label"]!.GetValue<string>());
        Assert.AreEqual(sent, TagsClient.Label(tag));
    }

    [TestMethod]
    public async Task BatchAddPutsEachContentByItsTypeAndItsId()
    {
        using var server = AreaFixtures.Server();
        server.Map("PUT", $"{Site}/tags:batchCreate", FakeResponse.Json(200, "{}"));
        await using var client = AreaFixtures.Client(server);

        await client.Tags.AddBatchAsync(["PII", "Yearly Sales"], _batch);

        AssertBatch(server, $"PUT {Site}/tags:batchCreate");
        await PermissionsTests.AssertGatedAsync("3.9", "Batch Add Tags", client => client.Tags.AddBatchAsync(["PII"], _batch));
    }

    [TestMethod]
    public async Task BatchDeleteIsADeleteWithTheBatchAddsBody()
    {
        using var server = AreaFixtures.Server();
        server.Map("DELETE", $"{Site}/tags:BatchDelete", FakeResponse.Empty(204));
        await using var client = AreaFixtures.Client(server);

        await client.Tags.DeleteBatchAsync(["PII", "Yearly Sales"], _batch);

        var call = AssertBatch(server, $"DELETE {Site}/tags:BatchDelete");
        Assert.AreEqual(call.Body.Length.ToString(System.Globalization.CultureInfo.InvariantCulture), call.Header("Content-Length"));
        await PermissionsTests.AssertGatedAsync("3.9", "Batch Delete Tags", client => client.Tags.DeleteBatchAsync(["PII"], _batch));
    }

    // The one call after the sign-in: its line, its token, and the batch body, its members in any order.
    private static RecordedRequest AssertBatch(FakeTableauServer server, string line)
    {
        var call = AreaFixtures.Calls(server).Single();
        Assert.AreEqual(line, $"{call.Method} {call.Target}");
        Assert.AreEqual(Fixtures.SessionToken, call.Header("X-Tableau-Auth"));
        Assert.AreEqual("application/json", call.Header("Content-Type"));
        ModelFixtures.AssertSameJson(ModelFixtures.Text("tag-batch-request.json"), call.BodyText);
        return call;
    }

    [TestMethod]
    public async Task AKindATagCallDoesntTakeIsRefusedBeforeSending()
    {
        using var server = AreaFixtures.Server();
        await using var client = AreaFixtures.Client(server);

        foreach (var content in new[] { TableauContent.Project(AreaFixtures.ItemId), TableauContent.Flow(AreaFixtures.ItemId), TableauContent.Collection(AreaFixtures.ItemId) })
        {
            var added = await AreaFixtures.RefusedAsync<ArgumentException>(server, () => client.Tags.AddAsync(content, ["Finance"]));
            var deleted = await AreaFixtures.RefusedAsync<ArgumentException>(server, () => client.Tags.DeleteAsync(content, "Finance"));
            Assert.AreEqual(("content", "content"), (added.ParamName, deleted.ParamName));
            Assert.StartsWith("Tags take a workbook, a view, a data source, a virtual connection, a database, a table or a column, not a ", added.Message);
        }
        foreach (var content in new[]
                 {
                     TableauContent.Project(AreaFixtures.ItemId), TableauContent.View(AreaFixtures.ItemId), TableauContent.VirtualConnection(AreaFixtures.ItemId),
                     TableauContent.Collection(AreaFixtures.ItemId),
                 })
        {
            var added = await AreaFixtures.RefusedAsync<ArgumentException>(server, () => client.Tags.AddBatchAsync(["PII"], [.. _batch, content]));
            var deleted = await AreaFixtures.RefusedAsync<ArgumentException>(server, () => client.Tags.DeleteBatchAsync(["PII"], [content]));
            Assert.AreEqual(("contents", "contents"), (added.ParamName, deleted.ParamName));
            Assert.StartsWith("A batch of tags takes databases, tables, columns, data sources, workbooks and flows, not a ", added.Message);
        }
    }

    [TestMethod]
    public async Task ARefusedArgumentSendsNothing()
    {
        using var server = AreaFixtures.Server();
        await using var client = AreaFixtures.Client(server);
        var workbook = TableauContent.Workbook(AreaFixtures.ItemId);

        Assert.AreEqual("tag", (await AreaFixtures.RefusedAsync<ArgumentException>(server, () => client.Tags.DeleteAsync(workbook, ".."))).ParamName);
        Assert.AreEqual("tag", (await AreaFixtures.RefusedAsync<ArgumentException>(server, () => client.Tags.DeleteAsync(workbook, ""))).ParamName);
        Assert.AreEqual("tag", (await AreaFixtures.RefusedAsync<ArgumentNullException>(server, () => client.Tags.DeleteAsync(workbook, null!))).ParamName);
        Assert.AreEqual("tags", (await AreaFixtures.RefusedAsync<ArgumentException>(server, () => client.Tags.AddAsync(workbook, []))).ParamName);
        Assert.AreEqual("tags", (await AreaFixtures.RefusedAsync<ArgumentException>(server, () => client.Tags.AddAsync(workbook, ["Finance", ""]))).ParamName);
        Assert.AreEqual("tags", (await AreaFixtures.RefusedAsync<ArgumentNullException>(server, () => client.Tags.AddAsync(workbook, null!))).ParamName);
        await AreaFixtures.RefusedAsync<ArgumentNullException>(server, () => client.Tags.AddAsync(null!, ["Finance"]));
        await AreaFixtures.RefusedAsync<ArgumentNullException>(server, () => client.Tags.DeleteAsync(null!, "Finance"));
        Assert.AreEqual("tags", (await AreaFixtures.RefusedAsync<ArgumentException>(server, () => client.Tags.AddBatchAsync([], _batch))).ParamName);
        Assert.AreEqual("contents", (await AreaFixtures.RefusedAsync<ArgumentException>(server, () => client.Tags.AddBatchAsync(["PII"], []))).ParamName);
        Assert.AreEqual("contents", (await AreaFixtures.RefusedAsync<ArgumentException>(server, () => client.Tags.DeleteBatchAsync(["PII"], [null!]))).ParamName);
        Assert.AreEqual("contents", (await AreaFixtures.RefusedAsync<ArgumentNullException>(server, () => client.Tags.DeleteBatchAsync(["PII"], null!))).ParamName);
    }

    [TestMethod]
    public async Task AnAnswerWithoutTagsIsAnEmptyList()
    {
        using var server = AreaFixtures.Server();
        server.Map("PUT", $"{Site}/workbooks/{AreaFixtures.ItemId}/tags", FakeResponse.Json(200, """{"tags":{}}"""));
        server.Map("PUT", $"{Site}/views/{AreaFixtures.ItemId}/tags", FakeResponse.Json(200, "{}"));
        await using var client = AreaFixtures.Client(server);

        var tags = await client.Tags.AddAsync(TableauContent.Workbook(AreaFixtures.ItemId), ["Finance"]);
        var none = await client.Tags.AddAsync(TableauContent.View(AreaFixtures.ItemId), ["Finance"]);

        Assert.IsNotNull(tags);
        Assert.IsEmpty(tags);
        Assert.IsEmpty(none);
    }

    private static TableauContent Content(TableauContentKind kind) => kind switch
    {
        TableauContentKind.Workbook => TableauContent.Workbook(AreaFixtures.ItemId),
        TableauContentKind.View => TableauContent.View(AreaFixtures.ItemId),
        TableauContentKind.DataSource => TableauContent.DataSource(AreaFixtures.ItemId),
        TableauContentKind.VirtualConnection => TableauContent.VirtualConnection(AreaFixtures.ItemId),
        TableauContentKind.Database => TableauContent.Database(AreaFixtures.ItemId),
        TableauContentKind.Table => TableauContent.Table(AreaFixtures.ItemId),
        _ => TableauContent.Column(AreaFixtures.ProjectId, AreaFixtures.ItemId),
    };
}
