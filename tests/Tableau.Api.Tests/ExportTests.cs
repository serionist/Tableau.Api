using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Tableau.Api.Tests.Fakes;

namespace Tableau.Api.Tests;

/// <summary>
/// Every file the workbooks, views and data sources send: each streamed as it comes, chunked as Tableau Server sends a
/// workbook, its kind told; and the exports' options from <see cref="ExportOptions"/>: view filters as <c>vf_</c> on every
/// export from one helper (F5), each option where its export takes it and refused before sending where it doesn't.
/// </summary>
[TestClass]
public sealed class ExportTests : IDisposable
{
    private const string Site = AreaFixtures.SitePath;
    private const string Id = AreaFixtures.ItemId;
    private const string WorkbookId = "66666666-7777-4888-8999-aaaaaaaaaaaa";
    private const int FirstChunk = 64 * 1024;

    /// <summary>
    /// The filters every export sends, one value each, a comma held as data escaped as <c>\,</c>, and how they go on the
    /// wire: names and values escaped, a vf_ name as it is.
    /// </summary>
    private static readonly Dictionary<string, string> _filters = new()
    {
        ["R&D"] = "C++ #1 100%",
        ["Région"] = @"Côte: Nord\, Sud",
        ["vf_Year"] = "2026",
        ["Parameters.Top N"] = "10",
        ["a=b&c"] = "d",
    };

    private const string Filtered = "vf_R%26D=C%2B%2B%20%231%20100%25&vf_R%C3%A9gion=C%C3%B4te%3A%20Nord%5C%2C%20Sud&vf_Year=2026"
        + "&vf_Parameters.Top%20N=10&vf_a%3Db%26c=d";

    private readonly CancellationTokenSource _end = new();

    public TestContext TestContext { get; set; } = null!;

    public void Dispose()
    {
        _end.Cancel();
        _end.Dispose();
    }

    /// <summary>
    /// Every download: its descriptor's name, its path and query, the Content-Type and Content-Disposition Tableau sends,
    /// the kind the client tells, and the call.
    /// </summary>
    public static IEnumerable<object[]> Downloads =>
    [
        Row("Download Workbook", $"/workbooks/{Id}/content", "application/octet-stream", "name=\"tableau_workbook\"; filename=\"Superstore.twbx\"",
            TableauFileKind.PackagedWorkbook, client => client.Workbooks.DownloadAsync(Id)),
        Row("Download Workbook, without its extract", $"/workbooks/{Id}/content?includeExtract=False", "application/octet-stream",
            "name=\"tableau_workbook\"; filename=\"Superstore.twbx\"", TableauFileKind.PackagedWorkbook,
            client => client.Workbooks.DownloadAsync(Id, includeExtract: false)),
        Row("Query Workbook Preview Image", $"/workbooks/{Id}/previewImage", "image/png", null, TableauFileKind.Png,
            client => client.Workbooks.DownloadPreviewImageAsync(Id)),
        Row("Download Workbook PDF", $"/workbooks/{Id}/pdf", "application/pdf", null, TableauFileKind.Pdf, client => client.Workbooks.DownloadPdfAsync(Id)),
        Row("Download Workbook Revision", $"/workbooks/{Id}/revisions/3/content", "application/xml",
            "name=\"tableau_workbook\"; filename=\"Superstore.twb\"", TableauFileKind.Workbook, client => client.Workbooks.DownloadRevisionAsync(Id, 3)),
        Row("Download Workbook Revision, without its extract", $"/workbooks/{Id}/revisions/3/content?includeExtract=False", "application/octet-stream",
            "name=\"tableau_workbook\"; filename=\"Superstore.twbx\"", TableauFileKind.PackagedWorkbook,
            client => client.Workbooks.DownloadRevisionAsync(Id, 3, includeExtract: false)),
        Row("Query View Data", $"/views/{Id}/data", "text/csv; charset=utf-8", null, TableauFileKind.Csv, client => client.Views.DownloadDataAsync(Id)),
        Row("Query View Image", $"/views/{Id}/image", "image/png", null, TableauFileKind.Png, client => client.Views.DownloadImageAsync(Id)),
        Row("Query View PDF", $"/views/{Id}/pdf", "application/pdf", null, TableauFileKind.Pdf, client => client.Views.DownloadPdfAsync(Id)),
        Row("Download View Crosstab Excel", $"/views/{Id}/crosstab/excel", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", null,
            TableauFileKind.Zip, client => client.Views.DownloadCrosstabAsync(Id)),
        Row("Query View Preview Image", $"/workbooks/{WorkbookId}/views/{Id}/previewImage", "image/png", null, TableauFileKind.Png,
            client => client.Views.DownloadPreviewImageAsync(WorkbookId, Id)),
        Row("Download Data Source", $"/datasources/{Id}/content", "application/octet-stream", "name=\"tableau_datasource\"; filename=\"Sales.tdsx\"",
            TableauFileKind.PackagedDataSource, client => client.DataSources.DownloadAsync(Id)),
        Row("Download Data Source, without its extract", $"/datasources/{Id}/content?includeExtract=False", "application/octet-stream",
            "name=\"tableau_datasource\"; filename=\"Sales.tdsx\"", TableauFileKind.PackagedDataSource,
            client => client.DataSources.DownloadAsync(Id, includeExtract: false)),
        Row("Download Data Source Revision", $"/datasources/{Id}/revisions/2/content", "application/octet-stream",
            "name=\"tableau_datasource\"; filename=\"Sales.tdsx\"", TableauFileKind.PackagedDataSource,
            client => client.DataSources.DownloadRevisionAsync(Id, 2)),
    ];

    /// <summary>Every export, how its call sends a set of options, and its path.</summary>
    public static IEnumerable<object[]> Exports =>
    [
        Export("Query View Data", $"/views/{Id}/data", (client, options) => client.Views.DownloadDataAsync(Id, options)),
        Export("Query View Image", $"/views/{Id}/image", (client, options) => client.Views.DownloadImageAsync(Id, options)),
        Export("Query View PDF", $"/views/{Id}/pdf", (client, options) => client.Views.DownloadPdfAsync(Id, options)),
        Export("Download View Crosstab Excel", $"/views/{Id}/crosstab/excel", (client, options) => client.Views.DownloadCrosstabAsync(Id, options)),
        Export("Download Workbook PDF", $"/workbooks/{Id}/pdf", (client, options) => client.Workbooks.DownloadPdfAsync(Id, options)),
    ];

    /// <summary>Each option an export doesn't take, as Tableau's reference lists its query parameters.</summary>
    public static IEnumerable<object[]> Refused =>
    [
        Option("Query View Data", "MaxAge", new ExportOptions { MaxAge = 5 }),
        Option("Query View Data", "Resolution", new ExportOptions { Resolution = ImageResolution.High }),
        Option("Query View Data", "Width and Height", new ExportOptions { Width = 800, Height = 600 }),
        Option("Query View Data", "PageType", new ExportOptions { PageType = PageType.A4 }),
        Option("Query View Data", "Orientation", new ExportOptions { Orientation = PageOrientation.Landscape }),
        Option("Query View Image", "PageType", new ExportOptions { PageType = PageType.Letter }),
        Option("Query View Image", "Orientation", new ExportOptions { Orientation = PageOrientation.Portrait }),
        Option("Query View PDF", "Resolution", new ExportOptions { Resolution = ImageResolution.Standard }),
        Option("Download View Crosstab Excel", "Resolution", new ExportOptions { Resolution = ImageResolution.High }),
        Option("Download View Crosstab Excel", "Width and Height", new ExportOptions { Height = 600 }),
        Option("Download View Crosstab Excel", "PageType", new ExportOptions { PageType = PageType.Legal }),
        Option("Download View Crosstab Excel", "Orientation", new ExportOptions { Orientation = PageOrientation.Landscape }),
        Option("Download Workbook PDF", "Resolution", new ExportOptions { Resolution = ImageResolution.High }),
    ];

    [TestMethod]
    [DynamicData(nameof(Downloads))]
    public async Task EachDownloadStreamsAsItComesAndKnowsItsKind(string name, string target, string contentType, string? disposition,
        TableauFileKind kind, Func<TableauClient, Task<TableauDownload>> call)
    {
        using var server = AreaFixtures.Server();
        var rest = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var body = Body(kind, 3 * FirstChunk);
        var path = Site + (target.Contains('?', StringComparison.Ordinal) ? target[..target.IndexOf('?', StringComparison.Ordinal)] : target);
        // Chunked, with no length, as Tableau Server sends a workbook; the rest only once the first chunk was read.
        server.Map("GET", path, FakeResponse.Streamed(200, contentType, null, async stream =>
        {
            await stream.WriteAsync(body.AsMemory(0, FirstChunk));
            await stream.FlushAsync();
            await rest.Task.WaitAsync(_end.Token);
            await stream.WriteAsync(body.AsMemory(FirstChunk));
        }, disposition is null ? [] : [("Content-Disposition", disposition)]));
        await using var client = AreaFixtures.Client(server);

        await using var download = await call(client).WaitAsync(TimeSpan.FromSeconds(10), TestContext.CancellationToken);
        var first = new byte[FirstChunk];
        await download.Content.ReadExactlyAsync(first, TestContext.CancellationToken).AsTask().WaitAsync(TimeSpan.FromSeconds(10), TestContext.CancellationToken);
        rest.SetResult();
        using var all = new MemoryStream();
        await all.WriteAsync(first, TestContext.CancellationToken);
        await download.Content.CopyToAsync(all, TestContext.CancellationToken);

        AreaFixtures.Sent(server, $"GET {Site}{target}");
        CollectionAssert.AreEqual(body, all.ToArray(), name);
        Assert.AreEqual(kind, download.Kind, name);
        Assert.IsNull(download.Length, "a chunked answer has no length");
    }

    [TestMethod]
    [DynamicData(nameof(Exports))]
    public async Task EveryExportSendsItsViewFiltersAsVfFromOneHelper(string name, string target, Func<TableauClient, ExportOptions?, Task<TableauDownload>> call)
    {
        using var server = AreaFixtures.Server();
        server.Map("GET", Site + target, FakeResponse.Content(200, "application/octet-stream", "invented"));
        await using var client = AreaFixtures.Client(server);

        await using (await call(client, new ExportOptions { Filters = _filters }))
        {
        }
        await using (await call(client, new ExportOptions { Filters = new Dictionary<string, string>() }))
        {
        }
        await using (await call(client, new ExportOptions()))
        {
        }
        await using (await call(client, null))
        {
        }

        CollectionAssert.AreEqual(new[] { $"{Site}{target}?{Filtered}", $"{Site}{target}", $"{Site}{target}", $"{Site}{target}" },
            AreaFixtures.Calls(server).Select(request => request.Target).ToArray(), name);
    }

    [TestMethod]
    [DynamicData(nameof(Refused))]
    public async Task AnExportRefusesAnOptionItDoesntTakeBeforeSending(string export, string option, ExportOptions options)
    {
        using var server = AreaFixtures.Server();
        await using var client = AreaFixtures.Client(server);
        var call = (Func<TableauClient, ExportOptions?, Task<TableauDownload>>)Exports.Single(row => (string)row[0] == export)[2];

        var thrown = await AreaFixtures.RefusedAsync<ArgumentException>(server, () => call(client, options));

        Assert.AreEqual("options", thrown.ParamName);
        Assert.Contains($"ExportOptions.{option} isn't for this export: {export} takes", thrown.Message);
    }

    [TestMethod]
    public async Task EachOptionGoesWhereItsExportTakesIt()
    {
        using var server = AreaFixtures.Server();
        foreach (var target in new[] { "/views/" + Id + "/image", "/views/" + Id + "/pdf", "/views/" + Id + "/crosstab/excel", "/workbooks/" + Id + "/pdf" })
            server.Map("GET", Site + target, FakeResponse.Content(200, "application/octet-stream", "invented"));
        await using var client = AreaFixtures.Client(server);
        var region = new Dictionary<string, string> { ["Region"] = "West" };

        await using (await client.Views.DownloadImageAsync(Id, new ExportOptions
        {
            Resolution = ImageResolution.High,
            MaxAge = 60,
            Width = 1600,
            Height = 900,
            Filters = region,
        }))
        {
        }
        await using (await client.Views.DownloadImageAsync(Id, new ExportOptions { Resolution = ImageResolution.Standard }))
        {
        }
        await using (await client.Views.DownloadPdfAsync(Id, new ExportOptions
        {
            PageType = PageType.Tabloid,
            Orientation = PageOrientation.Portrait,
            MaxAge = 1,
            Width = 800,
            Height = 600,
            Filters = region,
        }))
        {
        }
        await using (await client.Views.DownloadCrosstabAsync(Id, new ExportOptions { MaxAge = 30, Filters = region }))
        {
        }
        await using (await client.Workbooks.DownloadPdfAsync(Id, new ExportOptions { PageType = PageType.B5, MaxAge = 2, Filters = region }))
        {
        }

        CollectionAssert.AreEqual(new[]
        {
            $"{Site}/views/{Id}/image?resolution=high&maxAge=60&vizWidth=1600&vizHeight=900&vf_Region=West",
            $"{Site}/views/{Id}/image",
            $"{Site}/views/{Id}/pdf?type=Tabloid&orientation=Portrait&maxAge=1&vizWidth=800&vizHeight=600&vf_Region=West",
            $"{Site}/views/{Id}/crosstab/excel?maxAge=30&vf_Region=West",
            $"{Site}/workbooks/{Id}/pdf?type=B5&maxAge=2&vf_Region=West",
        }, AreaFixtures.Calls(server).Select(request => request.Target).ToArray());
    }

    [TestMethod]
    [DataRow("a maximum age below a minute", "MaxAge is 0")]
    [DataRow("a width without its height", "Width and Height go together")]
    [DataRow("a height without its width", "Width and Height go together")]
    [DataRow("a width of no pixel", "Width and Height are 0 and 600")]
    [DataRow("a page type that names none", "PageType is 99")]
    [DataRow("an orientation that names none", "Orientation is 7")]
    [DataRow("a resolution that names none", "Resolution is 5")]
    [DataRow("a filter that names no field", "A view filter names no field")]
    [DataRow("a filter that is the prefix alone", "A view filter names no field")]
    [DataRow("a filter with no value", "The view filter 'Region' has no value")]
    [DataRow("filters of unequal counts", "The view filters 'Year' and 'Region' hold 2 and 1 values")]
    [DataRow("an escaped backslash and comma beside two values", "The view filters 'Region' and 'Year' hold 1 and 2 values")]
    public async Task AnOptionOutOfRangeIsRefusedBeforeSending(string what, string message)
    {
        var options = what switch
        {
            "a maximum age below a minute" => new ExportOptions { MaxAge = 0 },
            "a width without its height" => new ExportOptions { Width = 800 },
            "a height without its width" => new ExportOptions { Height = 600 },
            "a width of no pixel" => new ExportOptions { Width = 0, Height = 600 },
            "a page type that names none" => new ExportOptions { PageType = (PageType)99 },
            "an orientation that names none" => new ExportOptions { Orientation = (PageOrientation)7 },
            "a resolution that names none" => new ExportOptions { Resolution = (ImageResolution)5 },
            "a filter that names no field" => new ExportOptions { Filters = new Dictionary<string, string> { [""] = "West" } },
            "a filter with no value" => new ExportOptions { Filters = new Dictionary<string, string> { ["Region"] = null! } },
            "filters of unequal counts" => new ExportOptions
            {
                Filters = new Dictionary<string, string> { ["Year"] = "2025,2026", ["Region"] = @"Nord\, Sud" },
            },
            // \\ is a backslash and \, a comma, both data: one value.
            "an escaped backslash and comma beside two values" => new ExportOptions
            {
                Filters = new Dictionary<string, string> { ["Region"] = @"a\\\,b", ["Year"] = "2025,2026" },
            },
            _ => new ExportOptions { Filters = new Dictionary<string, string> { ["vf_"] = "West" } },
        };
        using var server = AreaFixtures.Server();
        await using var client = AreaFixtures.Client(server);
        Func<Task> call = options.Resolution is not null ? () => client.Views.DownloadImageAsync(Id, options) : () => client.Views.DownloadPdfAsync(Id, options);

        var thrown = await AreaFixtures.RefusedAsync<ArgumentException>(server, call);

        Assert.AreEqual("options", thrown.ParamName, what);
        Assert.Contains(message, thrown.Message, what);
    }

    // A parameter holds one value, and the positional rule is for view filters: one goes beside view filters of any
    // count, written with its prefix or without, before them or after.
    [TestMethod]
    [DataRow("a parameter after a two-value filter", "vf_Region=West%2CEast&vf_Parameters.Top%20N=10")]
    [DataRow("a prefixed parameter before two two-value filters", "vf_Parameters.Top%20N=10&vf_Region=West%2CEast&vf_Year=2025%2C2026")]
    public async Task AParameterGoesBesideViewFiltersOfAnyCount(string what, string query)
    {
        var filters = what switch
        {
            "a parameter after a two-value filter" => new Dictionary<string, string> { ["Region"] = "West,East", ["Parameters.Top N"] = "10" },
            _ => new Dictionary<string, string> { ["vf_Parameters.Top N"] = "10", ["Region"] = "West,East", ["Year"] = "2025,2026" },
        };
        using var server = AreaFixtures.Server();
        server.Map("GET", $"{Site}/workbooks/{Id}/pdf", FakeResponse.Content(200, "application/pdf", "invented"));
        await using var client = AreaFixtures.Client(server);

        await using (await client.Workbooks.DownloadPdfAsync(Id, new ExportOptions { Filters = filters }))
        {
        }

        CollectionAssert.AreEqual(new[] { $"{Site}/workbooks/{Id}/pdf?{query}" }, AreaFixtures.Calls(server).Select(request => request.Target).ToArray(), what);
    }

    // \\ is a backslash held as data, so the comma after it separates two values; \\\, is a backslash and a comma held
    // as data, one value. Each goes beside a field of as many values, and is sent as written.
    [TestMethod]
    [DataRow("an escaped backslash, then a separator", @"a\\,b", "2025,2026", "vf_Region=a%5C%5C%2Cb&vf_Year=2025%2C2026")]
    [DataRow("an escaped backslash, then an escaped comma", @"a\\\,b", "2026", "vf_Region=a%5C%5C%5C%2Cb&vf_Year=2026")]
    public async Task AnEscapedBackslashEndsAValueWhereAnEscapedCommaDoesnt(string what, string region, string year, string query)
    {
        using var server = AreaFixtures.Server();
        server.Map("GET", $"{Site}/workbooks/{Id}/pdf", FakeResponse.Content(200, "application/pdf", "invented"));
        await using var client = AreaFixtures.Client(server);

        await using (await client.Workbooks.DownloadPdfAsync(Id, new ExportOptions
        {
            Filters = new Dictionary<string, string> { ["Region"] = region, ["Year"] = year },
        }))
        {
        }

        CollectionAssert.AreEqual(new[] { $"{Site}/workbooks/{Id}/pdf?{query}" }, AreaFixtures.Calls(server).Select(request => request.Target).ToArray(), what);
    }

    [TestMethod]
    public async Task AnExportIsSentAgainAfterA503BeforeItsContentBegins()
    {
        using var server = AreaFixtures.Server();
        var sent = 0;
        server.Map("GET", $"{Site}/views/{Id}/image", _ => Interlocked.Increment(ref sent) == 1
            ? FakeResponse.Text(503, "Invented: briefly unavailable.").WithHeader("Retry-After", "0")
            : FakeResponse.Content(200, "image/png", "invented"));
        var options = SessionFixtures.Options(server);
        options.Deployment = TableauDeployment.Server;
        options.MaxRetries = 3;
        await using var client = SessionFixtures.Client(options, SessionFixtures.Pat());

        await using var image = await client.Views.DownloadImageAsync(Id);

        Assert.AreEqual(2, sent);
        Assert.AreEqual(TableauFileKind.Png, image.Kind);
    }

    [TestMethod]
    public async Task ADownloadOfAViewThatIsNoLuidIsRefusedBeforeSending()
    {
        using var server = AreaFixtures.Server();
        await using var client = AreaFixtures.Client(server);

        var thrown = await AreaFixtures.RefusedAsync<ArgumentException>(server, () => client.Views.DownloadImageAsync("Overview"));
        await AreaFixtures.RefusedAsync<ArgumentException>(server, () => client.Views.DownloadPreviewImageAsync("Quarterly", Id));
        await AreaFixtures.RefusedAsync<ArgumentException>(server, () => client.Workbooks.DownloadAsync("Quarterly"));
        await AreaFixtures.RefusedAsync<ArgumentException>(server, () => client.DataSources.DownloadRevisionAsync("Sales", 1));

        Assert.AreEqual("id", thrown.ParamName);
    }

    // A file whose first bytes are its kind's signature where the kind is told by them, then a pattern.
    private static byte[] Body(TableauFileKind kind, int length)
    {
        var body = Enumerable.Range(0, length).Select(i => (byte)(i % 251)).ToArray();
        byte[] start = kind switch
        {
            TableauFileKind.Zip or TableauFileKind.PackagedWorkbook or TableauFileKind.PackagedDataSource => [(byte)'P', (byte)'K', 3, 4],
            TableauFileKind.Pdf => "%PDF-1.7"u8.ToArray(),
            TableauFileKind.Png => [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A],
            _ => [],
        };
        start.CopyTo(body, 0);
        return body;
    }

    private static object[] Row(string name, string target, string contentType, string? disposition, TableauFileKind kind,
        Func<TableauClient, Task<TableauDownload>> call) => [name, target, contentType, disposition!, kind, call];

    private static object[] Export(string name, string target, Func<TableauClient, ExportOptions?, Task<TableauDownload>> call) => [name, target, call];

    private static object[] Option(string export, string option, ExportOptions options) => [export, option, options];
}
