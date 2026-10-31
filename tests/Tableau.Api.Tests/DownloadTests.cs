using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Tableau.Api.Pipeline;
using Tableau.Api.Tests.Fakes;

namespace Tableau.Api.Tests;

/// <summary>
/// Downloads: streamed as they come, never buffered; the token and the transfer timeout honoured while the file is read;
/// the file name cleaned and the kind told from the name, the media type or the first bytes; saved to a file without
/// replacing one unasked, and without leaving part of one.
/// </summary>
[TestClass]
public sealed class DownloadTests : IDisposable
{
    private const string ContentPath = "/api/3.25/workbooks/w1/content";
    private const int FiveMegabytes = 5 * 1024 * 1024;
    private const int OneMegabyte = 1024 * 1024;

    private readonly CancellationTokenSource _end = new();
    private readonly System.Collections.Generic.List<string> _folders = [];

    public TestContext TestContext { get; set; } = null!;

    public void Dispose()
    {
        _end.Cancel();
        _end.Dispose();
        foreach (var folder in _folders)
            Directory.Delete(folder, recursive: true);
    }

    [TestMethod]
    public async Task FiveMegabytesArriveAsAStreamBeforeTheServerHasSentThem()
    {
        using var server = FakeTableauServer.Start();
        var rest = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        // Chunked, with no length, as Tableau Server 2025.1 sends a workbook.
        server.Map("GET", ContentPath, Gated(FiveMegabytes, OneMegabyte, rest.Task, sendLength: false,
            ("Content-Disposition", "name=\"tableau_workbook\"; filename=\"Superstore.twbx\"")));
        await using var client = ClientFixtures.Client(server);

        // The server holds back four of the five megabytes until the first has been read, so a client that waited for the
        // whole body would wait here for ever.
        await using var download = await ClientFixtures.DownloadAsync(client, Content()).WaitAsync(TimeSpan.FromSeconds(10), TestContext.CancellationToken);
        var first = new byte[OneMegabyte];
        await download.Content.ReadExactlyAsync(first, TestContext.CancellationToken).AsTask().WaitAsync(TimeSpan.FromSeconds(10), TestContext.CancellationToken);
        rest.SetResult();
        using var all = new MemoryStream();
        await all.WriteAsync(first, TestContext.CancellationToken);
        await download.Content.CopyToAsync(all, TestContext.CancellationToken);

        Assert.AreEqual("Superstore.twbx", download.FileName);
        Assert.AreEqual(TableauFileKind.PackagedWorkbook, download.Kind);
        Assert.AreEqual("application/octet-stream", download.ContentType);
        Assert.IsNull(download.Length, "no Content-Length came");
        CollectionAssert.AreEqual(Pattern(FiveMegabytes), all.ToArray());
    }

    [TestMethod]
    public async Task AReadStopsOnItsToken()
    {
        using var server = FakeTableauServer.Start();
        server.Map("GET", ContentPath, Gated(FiveMegabytes, OneMegabyte, Never()));
        await using var client = ClientFixtures.Client(server);
        await using var download = await ClientFixtures.DownloadAsync(client, Content());
        await download.Content.ReadExactlyAsync(new byte[OneMegabyte], TestContext.CancellationToken);
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        var thrown = await Assert.ThrowsAsync<OperationCanceledException>(
            async () => await download.Content.ReadExactlyAsync(new byte[1024], cancel.Token));

        Assert.AreEqual(cancel.Token, thrown.CancellationToken);
    }

    [TestMethod]
    public async Task AReadStopsOnTheCallsToken()
    {
        using var server = FakeTableauServer.Start();
        server.Map("GET", ContentPath, Gated(FiveMegabytes, OneMegabyte, Never()));
        await using var client = ClientFixtures.Client(server);
        using var call = new CancellationTokenSource();
        await using var download = await ClientFixtures.DownloadAsync(client, Content(), call.Token);
        await download.Content.ReadExactlyAsync(new byte[OneMegabyte], TestContext.CancellationToken);
        call.CancelAfter(TimeSpan.FromMilliseconds(200));

        var thrown = await Assert.ThrowsAsync<OperationCanceledException>(
            async () => await download.Content.ReadExactlyAsync(new byte[1024], TestContext.CancellationToken));

        Assert.AreEqual(call.Token, thrown.CancellationToken);
    }

    [TestMethod]
    public async Task ASynchronousReadStopsOnTheTransferTimeout()
    {
        using var server = FakeTableauServer.Start();
        server.Map("GET", ContentPath, Gated(FiveMegabytes, 1024, Never()));
        var options = ClientFixtures.Options(server);
        options.TransferTimeout = TimeSpan.FromSeconds(2);
        await using var client = ClientFixtures.Client(options);
        await using var download = await ClientFixtures.DownloadAsync(client, Content());
        download.Content.ReadExactly(new byte[1024]);

        var thrown = Assert.ThrowsExactly<TableauException>(() => download.Content.ReadExactly(new byte[1024]));

        Assert.AreEqual(TableauErrorKind.Timeout, thrown.Kind);
        Assert.Contains("took longer than its TransferTimeout allows, after 1024 bytes of its file", thrown.Message);
    }

    [TestMethod]
    public async Task TheTransferTimeoutIsApartFromTheAttemptTimeout()
    {
        using var server = FakeTableauServer.Start();
        server.Map("GET", ContentPath, _ =>
        {
            // Longer than the request timeout, before the headers, as a server preparing an export takes.
            Thread.Sleep(800);
            return Gated(FiveMegabytes, OneMegabyte, Never())(_);
        });
        var options = ClientFixtures.Options(server, maxRetries: 3);
        options.RequestTimeout = TimeSpan.FromMilliseconds(200);
        options.TransferTimeout = TimeSpan.FromSeconds(3);
        await using var client = ClientFixtures.Client(options);

        await using var download = await ClientFixtures.DownloadAsync(client, Content());
        await download.Content.ReadExactlyAsync(new byte[OneMegabyte], TestContext.CancellationToken);
        var thrown = await ClientFixtures.FailsAsync(TableauErrorKind.Timeout,
            async () => await download.Content.ReadExactlyAsync(new byte[1024], TestContext.CancellationToken));

        Assert.HasCount(1, server.Requests);
        Assert.Contains($"took longer than its TransferTimeout allows, after {OneMegabyte} bytes of its file", thrown.Message);
        Assert.IsInstanceOfType<OperationCanceledException>(thrown.InnerException);
    }

    [TestMethod]
    public async Task TheTransferTimeoutCoversTheWaitForTheHeaders()
    {
        using var server = FakeTableauServer.Start();
        server.Map("GET", ContentPath, _ =>
        {
            Thread.Sleep(2000);
            return FakeResponse.File([1, 2, 3], null);
        });
        var options = ClientFixtures.Options(server);
        options.TransferTimeout = TimeSpan.FromMilliseconds(300);
        await using var client = ClientFixtures.Client(options);

        var thrown = await ClientFixtures.FailsAsync(TableauErrorKind.Timeout, () => ClientFixtures.DownloadAsync(client, Content()));

        Assert.Contains("took longer than its TransferTimeout allows", thrown.Message);
    }

    [TestMethod]
    public async Task AnAnswerWithoutContentTypeOrNameIsKnownByItsZipSignature()
    {
        using var server = FakeTableauServer.Start();
        byte[] zip = [(byte)'P', (byte)'K', 3, 4, .. Pattern(1000)];
        server.Map("GET", ContentPath, FakeResponse.File(zip, contentType: null));
        await using var client = ClientFixtures.Client(server);

        await using var download = await ClientFixtures.DownloadAsync(client, Content());
        using var all = new MemoryStream();
        await download.Content.CopyToAsync(all, TestContext.CancellationToken);

        Assert.IsNull(download.ContentType);
        Assert.IsNull(download.FileName);
        Assert.AreEqual(zip.Length, download.Length);
        Assert.AreEqual(TableauFileKind.Zip, download.Kind);
        CollectionAssert.AreEqual(zip, all.ToArray());
    }

    [TestMethod]
    [DataRow("application/pdf", "", TableauFileKind.Pdf)]
    [DataRow("image/png", "", TableauFileKind.Png)]
    [DataRow("text/csv; charset=utf-8", "", TableauFileKind.Csv)]
    [DataRow("application/zip", "", TableauFileKind.Zip)]
    [DataRow("application/octet-stream", "%PDF-1.7", TableauFileKind.Pdf)]
    [DataRow(null, "\u0089PNG\r\n\u001a\n", TableauFileKind.Png)]
    [DataRow("application/xml", "<?xml", TableauFileKind.Unknown)]
    [DataRow(null, "PK", TableauFileKind.Unknown)]
    [DataRow(null, "", TableauFileKind.Unknown)]
    public async Task AnAnswerWithoutANameIsKnownByItsMediaTypeOrFirstBytes(string? contentType, string start, TableauFileKind kind)
    {
        using var server = FakeTableauServer.Start();
        var body = Encoding.Latin1.GetBytes(start);
        server.Map("GET", ContentPath, FakeResponse.File(body, contentType));
        await using var client = ClientFixtures.Client(server);

        await using var download = await ClientFixtures.DownloadAsync(client, Content());
        using var all = new MemoryStream();
        await download.Content.CopyToAsync(all, TestContext.CancellationToken);

        Assert.AreEqual(kind, download.Kind);
        CollectionAssert.AreEqual(body, all.ToArray());
    }

    [TestMethod]
    [DataRow(@"name=""tableau_workbook""; filename=""..\..\evil.twbx""", "evil.twbx", TableauFileKind.PackagedWorkbook)]
    [DataRow(@"attachment; filename=""../../etc/passwd""", "passwd", TableauFileKind.Unknown)]
    [DataRow(@"attachment; filename=""C:\Windows\System32\x.tds""", "x.tds", TableauFileKind.DataSource)]
    [DataRow("attachment; filename*=UTF-8''..%2F..%2Fe%C3%A9.tdsx", "eé.tdsx", TableauFileKind.PackagedDataSource)]
    [DataRow("attachment; filename*=UTF-8''%E2%80%AEtxt.hyper", "txt.hyper", TableauFileKind.Hyper)]
    [DataRow("attachment; filename*=iso-8859-1'en'caf%E9.tfl", "café.tfl", TableauFileKind.Flow)]
    [DataRow("attachment; filename*=UTF-16''x.tflx; filename=\"y.tflx\"", "y.tflx", TableauFileKind.PackagedFlow)]
    [DataRow("attachment; filename=\"plain.twb\"; filename*=UTF-8''star.pdf", "star.pdf", TableauFileKind.Pdf)]
    [DataRow(@"attachment; filename=""a<b>:c|d?*.twb""", "abcd.twb", TableauFileKind.Workbook)]
    [DataRow(@"attachment; filename=""say \""hi\"".csv""", "say hi.csv", TableauFileKind.Csv)]
    [DataRow(@"attachment; filename=""CON.png""", "_CON.png", TableauFileKind.Png)]
    [DataRow(@"attachment; filename=""lpt1""", "_lpt1", TableauFileKind.Unknown)]
    [DataRow(@"attachment; filename=""report.zip. . """, "report.zip", TableauFileKind.Zip)]
    [DataRow("attachment; filename=plain.csv", "plain.csv", TableauFileKind.Csv)]
    [DataRow(@"attachment; filename=""...""", null, TableauFileKind.Unknown)]
    [DataRow(@"attachment; filename=""..\""", null, TableauFileKind.Unknown)]
    [DataRow("attachment", null, TableauFileKind.Unknown)]
    [DataRow("attachment; =x; filename", null, TableauFileKind.Unknown)]
    [DataRow("attachment; filename*=bad.pdf", null, TableauFileKind.Unknown)]
    [DataRow("attachment; filename*=UTF-8''; filename=\"good.twb\"", "good.twb", TableauFileKind.Workbook)]
    [DataRow("attachment; filename=\"good.twb\"; filename*=UTF-8''..%2F..%2F", "good.twb", TableauFileKind.Workbook)]
    [DataRow("attachment; filename*=UTF-8''%EE%80%80%E2%80%AE; filename=\"good.twb\"", "good.twb", TableauFileKind.Workbook)]
    [DataRow("attachment; filename*=UTF-8''100%25%zz.pdf", "100%%zz.pdf", TableauFileKind.Pdf)]
    public async Task TheFileNameIsANameAloneCleaned(string disposition, string? fileName, TableauFileKind kind)
    {
        using var server = FakeTableauServer.Start();
        server.Map("GET", ContentPath, FakeResponse.File(Encoding.ASCII.GetBytes("invented"), "application/octet-stream",
            ("Content-Disposition", disposition)));
        await using var client = ClientFixtures.Client(server);

        await using var download = await ClientFixtures.DownloadAsync(client, Content());

        Assert.AreEqual(fileName, download.FileName);
        Assert.AreEqual(kind, download.Kind);
    }

    [TestMethod]
    [DataRow(new byte[] { (byte)'P', (byte)'K', 3, 4 }, TableauFileKind.Zip)]
    [DataRow(new byte[] { (byte)'P', (byte)'K', 5, 6, 0, 0, 0, 0 }, TableauFileKind.Zip)]
    [DataRow(new byte[] { (byte)'P', (byte)'K', 7, 8 }, TableauFileKind.Zip)]
    [DataRow(new byte[] { (byte)'P', (byte)'K', 3 }, TableauFileKind.Unknown)]
    [DataRow(new byte[] { (byte)'P', (byte)'K', 1, 2 }, TableauFileKind.Unknown)]
    [DataRow(new byte[] { (byte)'%', (byte)'P', (byte)'D', (byte)'F', (byte)'-' }, TableauFileKind.Pdf)]
    [DataRow(new byte[] { (byte)'%', (byte)'P', (byte)'D', (byte)'F' }, TableauFileKind.Unknown)]
    [DataRow(new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A }, TableauFileKind.Png)]
    [DataRow(new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A }, TableauFileKind.Unknown)]
    [DataRow(new byte[0], TableauFileKind.Unknown)]
    public void TheFirstBytesTellAZipAPdfOrAPng(byte[] start, TableauFileKind kind) =>
        Assert.AreEqual(kind, DownloadFiles.KindOf(start));

    [TestMethod]
    public async Task AFileThatBreaksOffInItsFirstBytesFailsTheDownload()
    {
        using var server = FakeTableauServer.Start();
        server.Map("GET", ContentPath, FakeResponse.Streamed(200, null, 100, async stream =>
        {
            await stream.WriteAsync("PK"u8.ToArray());
            await stream.FlushAsync();
            throw new IOException("Invented: the server stops mid-file.");
        }));
        await using var client = ClientFixtures.Client(server);

        var thrown = await ClientFixtures.FailsAsync(TableauErrorKind.Http, () => ClientFixtures.DownloadAsync(client, Content()));

        Assert.Contains("broke off after 2 bytes of its file", thrown.Message);
    }

    [TestMethod]
    public void ALongNameIsCutWithItsExtensionKept()
    {
        var name = new string('a', 300) + ".twbx";
        var surrogates = new string('b', 248) + "\U0001F600" + "x.hyper";
        var noExtension = new string('c', 300) + "." + new string('d', 20);

        Assert.AreEqual(new string('a', 250) + ".twbx", DownloadFiles.Clean(name));
        Assert.AreEqual(new string('b', 248) + ".hyper", DownloadFiles.Clean(surrogates));
        Assert.AreEqual(DownloadFiles.LongestName, DownloadFiles.Clean(noExtension)!.Length);
        Assert.IsNull(DownloadFiles.Clean(null));
        Assert.IsNull(DownloadFiles.Clean("\u0001\u0002"));
    }

    // The cut comes before the trim and the device rule, so what it leaves is trimmed and checked too.
    [TestMethod]
    public void WhatTheCutLeavesIsTrimmedAndChecked()
    {
        var device = DownloadFiles.Clean("NUL" + new string(' ', 250) + "x.twbx");
        var spaces = DownloadFiles.Clean("a" + new string(' ', 300) + "b");

        Assert.AreEqual("_NUL" + new string(' ', 246) + ".twbx", device);
        Assert.AreEqual(DownloadFiles.LongestName, Encoding.UTF8.GetByteCount(device!));
        Assert.AreEqual("a", spaces);
        Assert.AreEqual("_COM1.csv", DownloadFiles.Clean(" COM1.csv "));
        Assert.AreEqual("report", DownloadFiles.Clean("report .\u00A0"));
    }

    // Linux and macOS take 255 bytes of UTF-8 in a name, which 100 characters of Japanese pass.
    [TestMethod]
    public void ALongNameIsCutByItsBytesOfUtf8()
    {
        var name = DownloadFiles.Clean(new string('日', 100) + ".twbx")!;

        Assert.AreEqual(new string('日', 83) + ".twbx", name);
        Assert.IsLessThanOrEqualTo(DownloadFiles.LongestName, Encoding.UTF8.GetByteCount(name));
    }

    [TestMethod]
    public async Task SavingToAFolderIsRefusedBeforeAnythingIsRead()
    {
        using var server = FakeTableauServer.Start();
        server.Map("GET", ContentPath, FakeResponse.File(Pattern(100), null));
        await using var client = ClientFixtures.Client(server);
        var folder = Folder();
        Directory.CreateDirectory(Path.Combine(folder, "reports"));

        await using var download = await ClientFixtures.DownloadAsync(client, Content());
        var thrown = await Assert.ThrowsExactlyAsync<ArgumentException>(() => download.SaveToFileAsync(Path.Combine(folder, "reports"),
            cancellationToken: TestContext.CancellationToken));
        await download.SaveToFileAsync(Path.Combine(folder, "saved.bin"), cancellationToken: TestContext.CancellationToken);

        Assert.Contains("names a folder", thrown.Message);
        CollectionAssert.AreEqual(Pattern(100), await File.ReadAllBytesAsync(Path.Combine(folder, "saved.bin"), TestContext.CancellationToken));
    }

    [TestMethod]
    public async Task ASaveThatCantOpenItsFileMayBeTriedAgain()
    {
        using var server = FakeTableauServer.Start();
        server.Map("GET", ContentPath, FakeResponse.File(Pattern(100), null));
        await using var client = ClientFixtures.Client(server);
        var missing = Path.Combine(Folder(), "not yet");

        await using var download = await ClientFixtures.DownloadAsync(client, Content());
        await Assert.ThrowsExactlyAsync<DirectoryNotFoundException>(() => download.SaveToFileAsync(Path.Combine(missing, "saved.bin"),
            cancellationToken: TestContext.CancellationToken));
        Directory.CreateDirectory(missing);
        await download.SaveToFileAsync(Path.Combine(missing, "saved.bin"), cancellationToken: TestContext.CancellationToken);

        CollectionAssert.AreEqual(Pattern(100), await File.ReadAllBytesAsync(Path.Combine(missing, "saved.bin"), TestContext.CancellationToken));
    }

    [TestMethod]
    public async Task TheTransferTimeoutStopsTheReadAhead()
    {
        using var server = FakeTableauServer.Start();
        server.Map("GET", ContentPath, FakeResponse.Streamed(200, null, 100, async stream =>
        {
            await stream.FlushAsync();
            await Never();
        }));
        var options = ClientFixtures.Options(server);
        options.TransferTimeout = TimeSpan.FromMilliseconds(500);
        await using var client = ClientFixtures.Client(options);

        var thrown = await ClientFixtures.FailsAsync(TableauErrorKind.Timeout, () => ClientFixtures.DownloadAsync(client, Content()));

        Assert.Contains("took longer than its TransferTimeout allows", thrown.Message);
    }
    // A caller's HttpClient may hand back content whose stream fails as an HttpRequestException, not an IOException.
    [TestMethod]
    public async Task AContentStreamThatFailsAsARequestBreaksOffTheDownload()
    {
        using var server = FakeTableauServer.Start();
        using var answering = new FailingContent();
        using var http = new System.Net.Http.HttpClient(answering, disposeHandler: false);
        await using var client = new TableauClient(http, ClientFixtures.Options(server), Fixtures.Log.Factory);

        var thrown = await ClientFixtures.FailsAsync(TableauErrorKind.Http, () => ClientFixtures.DownloadAsync(client, Content()));

        Assert.Contains("broke off after 0 bytes of its file: Invented: the content failed.", thrown.Message);
        Assert.IsInstanceOfType<System.Net.Http.HttpRequestException>(thrown.InnerException);
    }
    [TestMethod]
    public async Task ACallThatIsNoDownloadIsRefusedBeforeSending()
    {
        using var server = FakeTableauServer.Start();
        await using var client = ClientFixtures.Client(server);

        var thrown = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => ClientFixtures.DownloadAsync(client, ClientFixtures.Endpoint("{version}/workbooks/w1/content")));

        Assert.Contains("is no download", thrown.Message);
        Assert.IsEmpty(server.Requests);
    }
    [TestMethod]
    public async Task ADownloadSavesToAFile()
    {
        using var server = FakeTableauServer.Start();
        server.Map("GET", ContentPath, Gated(FiveMegabytes, OneMegabyte, Task.CompletedTask));
        await using var client = ClientFixtures.Client(server);
        var folder = Folder();

        await using var download = await ClientFixtures.DownloadAsync(client, Content());
        var path = Path.Combine(folder, "saved.twbx");
        await download.SaveToFileAsync(path, cancellationToken: TestContext.CancellationToken);

        CollectionAssert.AreEqual(Pattern(FiveMegabytes), await File.ReadAllBytesAsync(path, TestContext.CancellationToken));
        CollectionAssert.AreEqual(new[] { path }, Directory.GetFiles(folder));
        Assert.AreEqual(FiveMegabytes, download.Length);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => download.SaveToFileAsync(path, overwrite: true,
            cancellationToken: TestContext.CancellationToken));
    }

    [TestMethod]
    public async Task SavingRefusesToReplaceAFileUnlessAsked()
    {
        using var server = FakeTableauServer.Start();
        server.Map("GET", ContentPath, FakeResponse.File(Encoding.ASCII.GetBytes("new"), null));
        await using var client = ClientFixtures.Client(server);
        var folder = Folder();
        var path = Path.Combine(folder, "kept.csv");
        await File.WriteAllTextAsync(path, "old", TestContext.CancellationToken);

        await using var download = await ClientFixtures.DownloadAsync(client, Content());
        var thrown = await Assert.ThrowsExactlyAsync<IOException>(() => download.SaveToFileAsync(path, cancellationToken: TestContext.CancellationToken));
        var kept = await File.ReadAllTextAsync(path, TestContext.CancellationToken);
        await download.SaveToFileAsync(path, overwrite: true, TestContext.CancellationToken);

        Assert.Contains("exists already: pass overwrite: true to replace it", thrown.Message);
        Assert.AreEqual("old", kept);

        Assert.AreEqual("new", await File.ReadAllTextAsync(path, TestContext.CancellationToken));
        CollectionAssert.AreEqual(new[] { path }, Directory.GetFiles(folder));
    }

    [TestMethod]
    public async Task ADownloadThatBreaksOffLeavesNoFile()
    {
        using var server = FakeTableauServer.Start();
        // Says a megabyte, sends half, and closes the connection.
        server.Map("GET", ContentPath, FakeResponse.Streamed(200, null, OneMegabyte, async stream =>
        {
            await stream.WriteAsync(Pattern(OneMegabyte / 2));
            await stream.FlushAsync();
            throw new IOException("Invented: the server stops mid-file.");
        }));
        await using var client = ClientFixtures.Client(server);
        var folder = Folder();

        await using var download = await ClientFixtures.DownloadAsync(client, Content());
        var thrown = await ClientFixtures.FailsAsync(TableauErrorKind.Http,
            () => download.SaveToFileAsync(Path.Combine(folder, "broken.twbx"), cancellationToken: TestContext.CancellationToken));

        Assert.Contains("broke off after", thrown.Message);
        Assert.IsEmpty(Directory.GetFiles(folder));
    }

    [TestMethod]
    public async Task SavingContentAlreadyReadIsRefused()
    {
        using var server = FakeTableauServer.Start();
        server.Map("GET", ContentPath, FakeResponse.File(Pattern(100), null));
        await using var client = ClientFixtures.Client(server);

        await using var download = await ClientFixtures.DownloadAsync(client, Content());
        _ = download.Content.ReadByte();

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => SaveAsync(download, overwrite: true));
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => download.SaveToFileAsync(" ", cancellationToken: TestContext.CancellationToken));
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => download.SaveToFileAsync(Folder() + Path.DirectorySeparatorChar,
            cancellationToken: TestContext.CancellationToken));
    }

    [TestMethod]
    public async Task AnErrorAnswerFailsTheDownloadWithTableausError()
    {
        using var server = FakeTableauServer.Start();
        server.Map("GET", ContentPath, FakeResponse.Json(404, """{"error":{"code":"404006","summary":"Resource Not Found","detail":"Invented: no workbook."}}"""));
        await using var client = ClientFixtures.Client(server);

        var thrown = await ClientFixtures.FailsAsync(TableauErrorKind.NotFound, () => ClientFixtures.DownloadAsync(client, Content()));

        Assert.AreEqual("404006", thrown.ErrorCode);
        Assert.AreEqual(ContentPath, thrown.RequestPath);
    }

    [TestMethod]
    public async Task ADownloadIsRetriedBeforeItsFileBegins()
    {
        using var server = FakeTableauServer.Start();
        server.Map("GET", ContentPath, ResilienceTests.InTurn(FakeResponse.Empty(503).WithHeader("Retry-After", "0"),
            FakeResponse.File(Pattern(10), "application/pdf")));
        await using var client = ClientFixtures.Client(ClientFixtures.Options(server, maxRetries: 3));

        await using var download = await ClientFixtures.DownloadAsync(client, Content());

        Assert.AreEqual(TableauFileKind.Pdf, download.Kind);
        Assert.HasCount(2, server.Requests);
    }

    [TestMethod]
    public async Task ADisposedDownloadReadsNoMore()
    {
        using var server = FakeTableauServer.Start();
        server.Map("GET", ContentPath, Gated(FiveMegabytes, OneMegabyte, Never()));
        await using var client = ClientFixtures.Client(server);
        var download = await ClientFixtures.DownloadAsync(client, Content());
        var content = download.Content;

        await download.DisposeAsync();
        await download.DisposeAsync();
        download.Dispose();

        Assert.IsFalse(content.CanRead);
        await Assert.ThrowsExactlyAsync<ObjectDisposedException>(async () => await content.ReadExactlyAsync(new byte[10], TestContext.CancellationToken));
        Assert.ThrowsExactly<ObjectDisposedException>(() => content.ReadByte());
        await Assert.ThrowsExactlyAsync<ObjectDisposedException>(() => SaveAsync(download, overwrite: true));
    }

    [TestMethod]
    public async Task TheContentIsReadForwardOnly()
    {
        using var server = FakeTableauServer.Start();
        server.Map("GET", ContentPath, FakeResponse.File(Pattern(100), null));
        await using var client = ClientFixtures.Client(server);
        using var download = await ClientFixtures.DownloadAsync(client, Content());
        var content = download.Content;

        await content.ReadExactlyAsync(new byte[2]);
        // The array overload, as an older caller reads.
        Func<byte[], int, int, CancellationToken, Task<int>> readArray = content.ReadAsync;
        var read = await readArray(new byte[3], 0, 3, TestContext.CancellationToken);
        content.Flush();

        Assert.AreEqual(3, read);
        Assert.AreEqual(5, content.Position);
        Assert.IsTrue(content.CanRead);
        Assert.IsFalse(content.CanSeek);
        Assert.IsFalse(content.CanWrite);
        Assert.ThrowsExactly<NotSupportedException>(() => content.Length);
        Assert.ThrowsExactly<NotSupportedException>(() => content.Position = 0);
        Assert.ThrowsExactly<NotSupportedException>(() => content.Seek(0, SeekOrigin.Begin));
        Assert.ThrowsExactly<NotSupportedException>(() => content.SetLength(1));
        Assert.ThrowsExactly<NotSupportedException>(() => content.Write([1], 0, 1));
    }

    [TestMethod]
    public async Task ADownloadOfYourOwnIsCleanedAndSaved()
    {
        var folder = Folder();
        var path = Path.Combine(folder, "mine.pdf");
        var stream = new MemoryStream(Pattern(64));

        await using (var download = new TableauDownload(stream, "../reports/q3.pdf", "application/pdf", 64))
        {
            Assert.AreEqual("q3.pdf", download.FileName);
            Assert.AreEqual(TableauFileKind.Pdf, download.Kind);
            Assert.AreEqual("application/pdf", download.ContentType);
            Assert.AreEqual(64, download.Length);
            await download.SaveToFileAsync(path, cancellationToken: TestContext.CancellationToken);
        }
        using var empty = new TableauDownload(new MemoryStream());
        using var csv = new TableauDownload(new MemoryStream(), contentType: "text/csv; charset=utf-8");

        CollectionAssert.AreEqual(Pattern(64), await File.ReadAllBytesAsync(path, TestContext.CancellationToken));
        Assert.ThrowsExactly<ObjectDisposedException>(() => stream.ReadByte());
        Assert.AreEqual(TableauFileKind.Unknown, empty.Kind);
        Assert.AreEqual(TableauFileKind.Csv, csv.Kind);
        Assert.AreEqual(-1, csv.Content.ReadByte());
        Assert.AreEqual("text/csv", csv.ContentType);
        Assert.IsNull(empty.FileName);
        Assert.AreEqual(0, await empty.Content.ReadAtLeastAsync(new byte[1], 1, throwOnEndOfStream: false));
        Assert.ThrowsExactly<ArgumentNullException>(() => new TableauDownload(null!));
        using var unreadable = new MemoryStream();
        unreadable.Dispose();
        Assert.ThrowsExactly<ArgumentException>(() => new TableauDownload(unreadable));
        var negative = Assert.ThrowsExactly<ArgumentException>(() => new TableauDownload(new MemoryStream(), length: -1));
        Assert.Contains("The length is -1: give 0 or more, or null.", negative.Message);
    }

    private static Endpoint Content() => ClientFixtures.Download("{version}/workbooks/w1/content");

    private Task SaveAsync(TableauDownload download, bool overwrite) =>
        download.SaveToFileAsync(Path.Combine(Folder(), "any.twbx"), overwrite, TestContext.CancellationToken);

    /// <summary>A handler that answers 200 with content whose every read fails as an HttpRequestException.</summary>
    private sealed class FailingContent : System.Net.Http.HttpMessageHandler
    {
        protected override Task<System.Net.Http.HttpResponseMessage> SendAsync(System.Net.Http.HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = new System.Net.Http.StreamContent(new FailingStream()),
            });
    }

    /// <summary>A stream whose every read fails as an HttpRequestException.</summary>
    private sealed class FailingStream : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new System.Net.Http.HttpRequestException("Invented: the content failed.");

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            throw new System.Net.Http.HttpRequestException("Invented: the content failed.");

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
    /// <summary>A new, empty folder of this test's own.</summary>
    private string Folder()
    {
        var folder = Path.Combine(Path.GetTempPath(), "Tableau.Api.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        _folders.Add(folder);
        return folder;
    }

    /// <summary>A wait the server's writer never ends, but the test's end does.</summary>
    private Task Never() => Task.Delay(Timeout.Infinite, _end.Token);

    /// <summary>Bytes that differ from one place to the next, so a byte out of order shows.</summary>
    private static byte[] Pattern(int length) => [.. Enumerable.Range(0, length).Select(i => (byte)(i % 251))];

    /// <summary>
    /// A file of <paramref name="length"/> bytes of <see cref="Pattern"/>, of which the server sends the first
    /// <paramref name="first"/> at once and the rest once <paramref name="rest"/> completes.
    /// </summary>
    private static Func<RecordedRequest, FakeResponse> Gated(int length, int first, Task rest, params (string Name, string Value)[] headers) =>
        Gated(length, first, rest, sendLength: true, headers);

    /// <summary>The same, sent with its length or, as Tableau Server sends a workbook, chunked, with none.</summary>
    private static Func<RecordedRequest, FakeResponse> Gated(int length, int first, Task rest, bool sendLength,
        params (string Name, string Value)[] headers)
    {
        var body = Pattern(length);
        return _ => FakeResponse.Streamed(200, "application/octet-stream", sendLength ? length : null, async stream =>
        {
            await stream.WriteAsync(body.AsMemory(0, first));
            await stream.FlushAsync();
            await rest;
            await stream.WriteAsync(body.AsMemory(first));
        }, headers);
    }
}
