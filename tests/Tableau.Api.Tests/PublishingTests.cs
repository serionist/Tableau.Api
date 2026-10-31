using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Tableau.Api.Enums;
using Tableau.Api.Tests.Fakes;

namespace Tableau.Api.Tests;

[TestClass]
public sealed class PublishingTests
{
    private const string SitePath = "/api/" + Fixtures.ApiVersion + "/sites/" + Fixtures.SiteId;
    // Tableau's upload session ids hold colons.
    private const string UploadId = "6417:4CD3F1AD7BC54D5A8D3F0A1E3E2C1B9A-0:0";
    private const string UploadPath = SitePath + "/fileUploads/" + UploadId;
    private const string ProjectId = "1d0e8a7b-0001-4c2d-9e3f-5a6b7c8d9e0f";
    private const string DatasourceId = "5e6f7a8b-0003-4c2d-9e3f-5a6b7c8d9e0f";

    [TestMethod]
    public async Task AChunkedUploadSendsEachChunkThenPublishesIt()
    {
        using var server = FakeTableauServer.Start();
        var client = Fixtures.Client(server);
        var session = Fixtures.Session();
        server.Map("POST", SitePath + "/fileUploads",
            FakeResponse.Xml(201, $"<fileUpload uploadSessionId=\"{UploadId}\" fileSize=\"0\"/>"));
        server.Map("PUT", UploadPath,
            FakeResponse.Xml(200, $"<fileUpload uploadSessionId=\"{UploadId}\" fileSize=\"1\"/>"));
        server.Map("POST", SitePath + "/datasources", FakeResponse.Xml(201,
            $"<datasource id=\"{DatasourceId}\" name=\"Sales extract\"><project id=\"{ProjectId}\"/></datasource>"));
        var file = Encoding.ASCII.GetBytes("0123456789");

        var upload = await client.InitiateFileUpload(session);
        using (var source = new MemoryStream(file))
        {
            // Ten bytes in reads of four take three appends; a fixed count fails, rather than hangs, if an append stops
            // moving the stream on.
            for (var i = 0; i < 3; i++)
                await client.AppendToFileUpload(session, upload.uploadSessionId, source, maxBytesToRead: 4);
            Assert.AreEqual(source.Length, source.Position, "three appends read the whole file");
        }
        var datasource = await client.PublishDatasource(session, upload.uploadSessionId, "Sales extract",
            DatasourceFileType.tde, ProjectId);

        var requests = server.Requests.ToList();
        CollectionAssert.AreEqual(
            new[]
            {
                "POST " + SitePath + "/fileUploads",
                "PUT " + UploadPath,
                "PUT " + UploadPath,
                "PUT " + UploadPath,
                "POST " + SitePath + "/datasources",
            },
            requests.Select(request => request.Method + " " + request.Path).ToArray());
        Assert.IsTrue(requests.All(request => request.Header("X-Tableau-Auth") == Fixtures.SessionToken));

        var chunks = requests.Where(request => request.Method == "PUT").Select(MultipartBody.Parse).ToList();
        foreach (var parts in chunks)
        {
            Assert.HasCount(2, parts);
            Assert.AreEqual("name=\"request_payload\"", parts[0].Header("Content-Disposition"));
            Assert.IsEmpty(parts[0].Body);
            Assert.AreEqual("name=\"tableau_file\"; filename=\"FILE-NAME\"", parts[1].Header("Content-Disposition"));
            Assert.AreEqual("application/octet-stream", parts[1].Header("Content-Type"));
        }
        CollectionAssert.AreEqual(new[] { "0123", "4567", "89" }, chunks.Select(parts => parts[1].BodyText).ToArray());

        var publish = requests.Last();
        CollectionAssert.AreEquivalent(
            new[]
            {
                new KeyValuePair<string, string>("uploadSessionId", UploadId),
                new KeyValuePair<string, string>("datasourceType", "tde"),
                new KeyValuePair<string, string>("overwrite", "False"),
                new KeyValuePair<string, string>("append", "False"),
            },
            publish.QueryParameters.ToArray());
        var payload = MultipartBody.Parse(publish).Single();
        Assert.AreEqual("name=\"request_payload\"", payload.Header("Content-Disposition"));
        var described = XElement.Parse(payload.BodyText).Elements()
            .Single(element => element.Name.LocalName == "datasource");
        Assert.AreEqual("Sales extract", (string?)described.Attribute("name"));
        var project = described.Elements().Single(element => element.Name.LocalName == "project");
        Assert.AreEqual(ProjectId, (string?)project.Attribute("id"));

        Assert.AreEqual(DatasourceId, datasource.id);
    }
}
