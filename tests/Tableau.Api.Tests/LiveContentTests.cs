using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Tableau.Api.Rest.Models;

namespace Tableau.Api.Tests;

/// <summary>
/// Workbooks, views, data sources and tags on a Tableau site (<see cref="LiveSite"/> has the settings), on the sample
/// workbooks and the data source Tableau publishes into the run's project alone: listed, got, downloaded, updated, a
/// connection of each updated, refreshed, their revisions listed, downloaded and removed, as Tableau allows a sample's
/// one revision; the workbooks' views listed and exported as data, an image, a PDF and a crosstab, with a view filter; a
/// workbook tagged and untagged with a label holding a space and one holding a comma, one at a time and in a batch; a
/// workbook and the data source deleted. A list is filtered by the run's project's name, so the suite reads no content
/// it didn't make, but the user's own workbooks, which Query Workbooks for User can't filter, read only until a sample
/// comes. What Tableau sends for each file's Content-Type and Content-Disposition, and each outcome Tableau refuses, the
/// test output records. One at a time, as the other live tests.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed partial class LiveContentTests
{
    private static readonly string[] _labels = ["Yearly Sales", "Sales,Marketing"];

    public TestContext TestContext { get; set; } = null!;

    [SiteTest(TableauSite.Live, [LiveSite.ServerUrl], SignsIn = true), TestCategory(TableauSite.Live)]
    public async Task SampleWorkbooksAreListedGotDownloadedAndUpdated()
    {
        await using var client = LiveSite.Client(LiveSite.Credentials());
        var project = await LiveSite.ProjectAsync(client);
        var samples = await LiveSite.SamplesAsync(client);
        var sample = samples[0];
        var inProject = TableauFilter.Eq(WorkbookField.ProjectName, LiveSite.ProjectName);

        var byOne = await AreaFixtures.AllAsync(client.Workbooks.ListAsync(new WorkbookListOptions { Filter = inProject, PageSize = 1 }));
        var page = await client.Workbooks.ListPageAsync(1, new WorkbookListOptions { Filter = inProject, Sort = TableauSort.Desc(WorkbookField.Name) });
        var got = await client.Workbooks.GetAsync(sample.Id!);
        var connections = await AreaFixtures.AllAsync(client.Workbooks.ListConnectionsAsync(sample.Id!));
        var folder = Directory.CreateTempSubdirectory("Tableau.Api.Live-").FullName;
        long withoutExtract;
        long whole;
        try
        {
            await using (var file = await client.Workbooks.DownloadAsync(sample.Id!, includeExtract: false))
            {
                var path = Path.Combine(folder, file.FileName ?? "sample.twbx");
                await file.SaveToFileAsync(path);
                withoutExtract = new FileInfo(path).Length;
                Assert.IsTrue(file.Kind is TableauFileKind.Workbook or TableauFileKind.PackagedWorkbook, $"a workbook's file is {file.Kind}");
            }
            await using (var file = await client.Workbooks.DownloadAsync(sample.Id!))
                whole = await LengthAsync(file);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
        long previewLength;
        await using (var preview = await client.Workbooks.DownloadPreviewImageAsync(sample.Id!))
        {
            // A workbook just published may have no thumbnail yet, which Tableau sends as an empty PNG.
            Assert.AreEqual(TableauFileKind.Png, preview.Kind);
            previewLength = await LengthAsync(preview);
        }
        await using (var pdf = await client.Workbooks.DownloadPdfAsync(sample.Id!, new ExportOptions
        {
            PageType = PageType.A4,
            Orientation = PageOrientation.Landscape,
            MaxAge = 1,
        }))
            Assert.AreEqual((TableauFileKind.Pdf, "%PDF-"), (pdf.Kind, await StartAsync(pdf, 5)));
        await using (var pdf = await client.Workbooks.DownloadPdfAsync(sample.Id!, new ExportOptions
        {
            Filters = new Dictionary<string, string> { ["Country/Region"] = "Canada" },
        }))
            Assert.AreEqual((TableauFileKind.Pdf, "%PDF-"), (pdf.Kind, await StartAsync(pdf, 5)));
        var sized = await OutcomeAsync("a workbook's PDF at a size", async () =>
        {
            await using var pdf = await client.Workbooks.DownloadPdfAsync(sample.Id!, new ExportOptions { Width = 1200, Height = 900 });
            return $"{pdf.Kind}, {await StartAsync(pdf, 5)}";
        });
        var revisions = await AreaFixtures.AllAsync(client.Workbooks.ListRevisionsAsync(sample.Id!, new RevisionListOptions { PageSize = 1 }));
        var revisionPage = await client.Workbooks.ListRevisionsPageAsync(sample.Id!, 1);
        // A sample has one revision, the current one, which Tableau serves through Download Workbook alone and never
        // removes: the calls reach it on their paths, and it refuses each for that.
        var head = (int)(revisions[^1].RevisionNumber ?? 1);
        var downloadedRevision = await OutcomeAsync("a workbook's current revision downloaded", async () =>
        {
            await using var file = await client.Workbooks.DownloadRevisionAsync(sample.Id!, head, includeExtract: false);
            return $"{file.Kind}, {await LengthAsync(file)} bytes";
        });
        var removedRevision = await OutcomeAsync("a workbook's only revision removed", async () =>
        {
            await client.Workbooks.RemoveRevisionAsync(sample.Id!, head);
            return "removed";
        });
        var showTabs = got.ShowTabs != "true";
        var updated = await client.Workbooks.UpdateAsync(sample.Id!, new WorkbookUpdate
        {
            Description = "Updated by Tableau.Api's live suite.",
            ShowTabs = showTabs,
        });
        // The sample's connections reach files packaged with it, which Tableau Server 2025.1 doesn't let a workbook's
        // update change (400039, its class not editable), once it has read the body's attributes: with embedPassword
        // alone it answers that there are none to change.
        var connectionUpdate = await OutcomeAsync("a workbook's connection updated with a port", async () =>
        {
            var updatedConnection = await client.Workbooks.UpdateConnectionAsync(sample.Id!, connections[0].Id!,
                new ConnectionUpdate { ServerPort = 8443, EmbedPassword = false });
            return $"connection {(updatedConnection.Id == connections[0].Id ? "answered" : "of another id")}";
        });
        // The samples hold no extract, so Tableau refuses their refresh (403180) once it has read the call; one with an
        // extract answers with the job.
        var refresh = await OutcomeAsync("a workbook's extract refresh", async () =>
        {
            var job = await client.Workbooks.RunExtractRefreshAsync(sample.Id!);
            return $"job {job.Type?.Value} {job.Mode?.Value}";
        });
        // Query Workbooks for User takes no filter: the user's own workbooks are read only until the run's samples come.
        var self = LiveSite.Saw(client.Pipeline.State.Sessions.Current!).UserId;
        Workbook? owned = null;
        await foreach (var workbook in client.Workbooks.ListForUserAsync(self, ownedOnly: true))
        {
            if (workbook.Project?.Id != project) continue;
            owned = workbook;
            break;
        }

        Record($"samples: {samples.Count}, listed {LiveSite.SamplesTook?.TotalSeconds.ToString("F0", CultureInfo.InvariantCulture)} s after the project was made");
        Record($"a workbook's location type: {got.Location?.Type?.Value ?? "(none)"}; its project's id {(got.Location?.Id == project ? "is" : "isn't")} the location's");
        Record($"a workbook's revisions: {revisions.Count}, the last current: {revisions.LastOrDefault()?.Current}");
        Record($"a workbook's download: {withoutExtract} bytes without its extract, {whole} with it; its preview image {previewLength} bytes");
        // Another test of the run deletes the last sample, before or after this one.
        Assert.HasCount(byOne.Count, byOne.Select(workbook => workbook.Id).Distinct(), "no workbook twice across pages of one");
        CollectionAssert.IsSubsetOf(byOne.Select(workbook => workbook.Id).ToList(), samples.Select(workbook => workbook.Id).ToList());
        CollectionAssert.Contains(byOne.Select(workbook => workbook.Id).ToList(), sample.Id);
        CollectionAssert.AreEqual(byOne.Select(workbook => workbook.Name).OrderByDescending(name => name, StringComparer.Ordinal).ToArray(),
            page.Items.Select(workbook => workbook.Name).ToArray());
        Assert.AreEqual((sample.Id, project), (got.Id, got.Project?.Id));
        Assert.IsNotEmpty(got.Views!);
        Assert.IsNotEmpty(connections);
        Assert.IsGreaterThan(0L, withoutExtract);
        Assert.IsGreaterThanOrEqualTo(withoutExtract, whole);
        Assert.IsNotEmpty(revisions, "the site keeps revision history");
        Assert.AreEqual((revisions.Count, (int?)revisions.Count), (revisionPage.Items.Count, revisionPage.TotalAvailable));
        Assert.AreEqual(("Updated by Tableau.Api's live suite.", showTabs ? "true" : "false"), (updated.Description, updated.ShowTabs));
        Assert.IsNotNull(owned, "the user's own workbooks hold a sample of the run");
        Assert.IsTrue(connectionUpdate == "connection answered" || connectionUpdate.Contains(" 400039 ", StringComparison.Ordinal), connectionUpdate);
        Assert.IsTrue(sized.StartsWith("Pdf, %PDF-", StringComparison.Ordinal) || sized.StartsWith("refused: UnsupportedApiVersion", StringComparison.Ordinal),
            sized);
        Assert.Contains(" 400057 ", downloadedRevision);
        Assert.Contains(" 403055 ", removedRevision);
        Assert.IsTrue(refresh.StartsWith("job RefreshExtract", StringComparison.Ordinal) || refresh.Contains(" 403180 ", StringComparison.Ordinal), refresh);
        RecordFiles();
    }

    [SiteTest(TableauSite.Live, [LiveSite.ServerUrl], SignsIn = true), TestCategory(TableauSite.Live)]
    public async Task ThePublishedSampleDataSourceIsReadDownloadedUpdatedAndDeleted()
    {
        await using var client = LiveSite.Client(LiveSite.Credentials());
        var project = await LiveSite.ProjectAsync(client);
        await LiveSite.SamplesAsync(client);
        var inProject = new DataSourceListOptions { Filter = TableauFilter.Eq(DataSourceField.ProjectName, LiveSite.ProjectName), PageSize = 1 };

        // Tableau Server 2025.1 publishes a sample's data source beside the samples: the suite's own too.
        var listed = await LiveSite.EventuallyAsync(async () => (await AreaFixtures.AllAsync(client.DataSources.ListAsync(inProject)))
            .FirstOrDefault(found => found.Project?.Id == project), "The sample's data source");
        var page = await client.DataSources.ListPageAsync(1, inProject);
        var dataSource = await client.DataSources.GetAsync(listed.Id!);
        var connections = await AreaFixtures.AllAsync(client.DataSources.ListConnectionsAsync(listed.Id!));
        long withoutExtract;
        await using (var file = await client.DataSources.DownloadAsync(listed.Id!, includeExtract: false))
        {
            Assert.IsTrue(file.Kind is TableauFileKind.DataSource or TableauFileKind.PackagedDataSource, $"a data source's file is {file.Kind}");
            withoutExtract = await LengthAsync(file);
        }
        long whole;
        await using (var file = await client.DataSources.DownloadAsync(listed.Id!))
            whole = await LengthAsync(file);
        var updated = await client.DataSources.UpdateAsync(listed.Id!, new DataSourceUpdate
        {
            IsCertified = true,
            CertificationNote = "Certified by Tableau.Api's live suite.",
        });
        // A port on a connection to a file Tableau packaged: harmless here, and it shows Tableau reads a JSON number.
        var connection = await client.DataSources.UpdateConnectionAsync(listed.Id!, connections[0].Id!,
            new ConnectionUpdate { ServerPort = 8443, EmbedPassword = false });
        var revisions = await AreaFixtures.AllAsync(client.DataSources.ListRevisionsAsync(listed.Id!));
        var revisionPage = await client.DataSources.ListRevisionsPageAsync(listed.Id!, 1, new RevisionListOptions { PageSize = 1 });
        // One revision, the current one, as a sample workbook's: Tableau refuses to serve or remove it here.
        var head = (int)(revisions[^1].RevisionNumber ?? 1);
        var downloadedRevision = await OutcomeAsync("a data source's current revision downloaded", async () =>
        {
            await using var file = await client.DataSources.DownloadRevisionAsync(listed.Id!, head);
            return $"{file.Kind}, {await LengthAsync(file)} bytes";
        });
        var removedRevision = await OutcomeAsync("a data source's only revision removed", async () =>
        {
            await client.DataSources.RemoveRevisionAsync(listed.Id!, head);
            return "removed";
        });
        var refresh = await OutcomeAsync("a data source's extract refresh", async () =>
        {
            var job = await client.DataSources.RunExtractRefreshAsync(listed.Id!);
            return $"job {job.Type?.Value} {job.Mode?.Value}";
        });
        // A batch names a data source Datasource, the least obvious of the types: tagged and untagged in one, each call's
        // refusal, if any, scrubbed before it reaches the log.
        var content = TableauContent.DataSource(listed.Id!);
        var batchAdded = await OutcomeAsync("a data source's batch tags added", async () =>
        {
            await client.Tags.AddBatchAsync(_labels, [content]);
            return "added";
        });
        var tagged = (await client.DataSources.GetAsync(listed.Id!)).Tags?.Select(tag => tag.Label ?? "").ToArray() ?? [];
        var batchDeleted = await OutcomeAsync("a data source's batch tags deleted", async () =>
        {
            await client.Tags.DeleteBatchAsync(_labels, [content]);
            return "deleted";
        });
        var untagged = (await client.DataSources.GetAsync(listed.Id!)).Tags?.Select(tag => tag.Label ?? "").ToArray() ?? [];
        await client.DataSources.DeleteAsync(listed.Id!);

        Record($"a data source's location type: {dataSource.Location?.Type?.Value ?? "(none)"}; its connections {connections.Count}, "
            + $"its revisions {revisions.Count}; its download {withoutExtract} bytes without its extract, {whole} with it; its connection "
            + $"updated with a port answered {connection.Type ?? "(no type)"}, port {connection.ServerPort?.ToString(CultureInfo.InvariantCulture) ?? "(none)"}");
        Record($"a data source after the batch add: [{string.Join(" | ", tagged)}], after the batch delete: [{string.Join(" | ", untagged)}]");
        Assert.AreEqual((listed.Id, project), (dataSource.Id, dataSource.Project?.Id));
        Assert.HasCount(1, page.Items);
        Assert.IsNotEmpty(connections);
        Assert.IsGreaterThan(0L, withoutExtract);
        Assert.AreEqual((true, "Certified by Tableau.Api's live suite."), (updated.IsCertified, updated.CertificationNote));
        Assert.IsNotEmpty(revisions, "the site keeps revision history");
        Assert.AreEqual((int?)revisions.Count, revisionPage.TotalAvailable);
        Assert.AreEqual(connections[0].Id, connection.Id);
        Assert.Contains(" 400057 ", downloadedRevision);
        Assert.Contains(" 403055 ", removedRevision);
        Assert.IsTrue(refresh.StartsWith("job RefreshExtract", StringComparison.Ordinal) || refresh.Contains(" 403180 ", StringComparison.Ordinal), refresh);
        Assert.AreEqual(("added", "deleted"), (batchAdded, batchDeleted));
        foreach (var label in _labels)
            Assert.AreEqual(1, tagged.Count(tag => tag == label), $"the batch tagged the data source with '{label}' once: [{string.Join(" | ", tagged)}]");
        Assert.IsFalse(untagged.Intersect(_labels).Any(), $"the batch took the labels off the data source: [{string.Join(" | ", untagged)}]");
        await ClientFixtures.FailsAsync(TableauErrorKind.NotFound, () => client.DataSources.GetAsync(listed.Id!));
        RecordFiles();
    }

    // What a call did, for the test's output: its answer, or Tableau's refusal by status and code.
    private async Task<string> OutcomeAsync(string what, Func<Task<string>> call)
    {
        string outcome;
        try
        {
            outcome = await call();
        }
        catch (TableauException e)
        {
            outcome = Scrubbed($"refused: {e.Kind} {(int?)e.StatusCode} {e.ErrorCode} {e.Summary}: {Luids().Replace(e.Detail ?? "", "{id}")}");
        }
        Record($"{what}: {outcome}");
        return outcome;
    }

    [SiteTest(TableauSite.Live, [LiveSite.ServerUrl], SignsIn = true), TestCategory(TableauSite.Live)]
    public async Task ASampleViewIsListedGotAndExportedWithAViewFilter()
    {
        await using var client = LiveSite.Client(LiveSite.Credentials());
        var samples = await LiveSite.SamplesAsync(client);
        var sample = samples[0];

        var views = await AreaFixtures.AllAsync(client.Views.ListForWorkbookAsync(sample.Id!, includeUsageStatistics: true));
        // Tableau lists a view from its search index, which may take a moment after the sample is published.
        var listed = await LiveSite.EventuallyAsync(async () =>
        {
            var found = await AreaFixtures.AllAsync(client.Views.ListAsync(new ViewListOptions
            {
                Filter = TableauFilter.And(TableauFilter.Eq(ViewField.ProjectName, LiveSite.ProjectName), TableauFilter.Eq(ViewField.WorkbookName, sample.Name!)),
                PageSize = 2,
            }));
            return found.Count >= views.Count ? found : null;
        }, "The sample's views", TimeSpan.FromMinutes(2));
        var worksheet = listed.FirstOrDefault(found => string.Equals(found.SheetType, "worksheet", StringComparison.OrdinalIgnoreCase)) ?? listed[0];
        var view = await client.Views.GetAsync(worksheet.Id!, includeUsageStatistics: true);
        string csv;
        await using (var data = await client.Views.DownloadDataAsync(view.Id!))
        {
            Assert.AreEqual(TableauFileKind.Csv, data.Kind);
            csv = await TextAsync(data);
        }
        var rows = Csv(csv);
        var (column, value) = FilterOf(rows);
        Dictionary<string, string> filters = [];
        if (column >= 0) filters[rows[0][column]] = value;
        string filtered;
        await using (var data = await client.Views.DownloadDataAsync(view.Id!, new ExportOptions { Filters = filters }))
            filtered = await TextAsync(data);
        await using (var image = await client.Views.DownloadImageAsync(view.Id!, new ExportOptions
        {
            Resolution = ImageResolution.High,
            MaxAge = 1,
            Width = 800,
            Height = 600,
            Filters = filters,
        }))
            Assert.AreEqual((TableauFileKind.Png, "\u0089PNG"), (image.Kind, await StartAsync(image, 4)));
        await using (var pdf = await client.Views.DownloadPdfAsync(view.Id!, new ExportOptions
        {
            PageType = PageType.Letter,
            Orientation = PageOrientation.Landscape,
            Filters = filters,
        }))
            Assert.AreEqual((TableauFileKind.Pdf, "%PDF-"), (pdf.Kind, await StartAsync(pdf, 5)));
        await using (var crosstab = await client.Views.DownloadCrosstabAsync(view.Id!, new ExportOptions { MaxAge = 1, Filters = filters }))
            Assert.AreEqual((TableauFileKind.Zip, "PK"), (crosstab.Kind, await StartAsync(crosstab, 2)));
        await using (var preview = await client.Views.DownloadPreviewImageAsync(sample.Id!, view.Id!))
            Assert.AreEqual(TableauFileKind.Png, preview.Kind);

        Record($"views of a sample: {views.Count}, listed two a page: {listed.Count}; their sheet types: "
            + string.Join(", ", listed.Select(found => found.SheetType ?? "(none)").Distinct(StringComparer.Ordinal)));
        Record(column < 0 ? "no column to filter on" : $"a view filter on column {column + 1} of {rows[0].Count}: {Csv(filtered).Count - 1} of {rows.Count - 1} rows");
        Assert.IsNotEmpty(views);
        Assert.IsTrue(views.All(found => found.Usage is not null), "each view comes with its usage");
        Assert.IsTrue(listed.All(found => found.Workbook?.Id == sample.Id), "the filter keeps the sample's views");
        Assert.HasCount(listed.Count, listed.Select(found => found.Id).Distinct(), "no view twice across pages of two");
        Assert.AreEqual(worksheet.Id, view.Id);
        Assert.IsGreaterThan(1, rows.Count, "the data has a header and a row at least");
        if (column >= 0)
        {
            var kept = Csv(filtered);
            Assert.IsGreaterThan(1, kept.Count, "the filtered data keeps the row it was taken from");
            Assert.IsTrue(kept.Skip(1).All(row => row[column] == value), "every row the view filter kept has its value");
            Assert.IsLessThanOrEqualTo(rows.Count, kept.Count);
        }
        RecordFiles();
    }

    [SiteTest(TableauSite.Live, [LiveSite.ServerUrl], SignsIn = true), TestCategory(TableauSite.Live)]
    public async Task SamplesAreTaggedWithASpaceAndACommaOneAtATimeAndInABatchAndOneIsDeleted()
    {
        await using var client = LiveSite.Client(LiveSite.Credentials());
        var samples = await LiveSite.SamplesAsync(client);
        // The last two by name, so the tests that read the first never meet the one deleted here.
        var (first, second) = (TableauContent.Workbook(samples[^2].Id!), TableauContent.Workbook(samples[^1].Id!));
        async Task<string[]> LabelsAsync(TableauContent workbook) =>
            [.. (await client.Workbooks.GetAsync(workbook.Id)).Tags?.Select(tag => tag.Label ?? "") ?? []];

        var added = (await client.Tags.AddAsync(first, _labels)).Select(tag => tag.Label ?? "").ToArray();
        var afterAdd = await LabelsAsync(first);
        foreach (var label in _labels)
            await client.Tags.DeleteAsync(first, label);
        var afterDelete = await LabelsAsync(first);
        await client.Tags.AddBatchAsync(_labels, [first, second]);
        var batchFirst = await LabelsAsync(first);
        var batchSecond = await LabelsAsync(second);
        await client.Tags.DeleteBatchAsync(_labels, [first, second]);
        var clearedFirst = await LabelsAsync(first);
        var clearedSecond = await LabelsAsync(second);
        await client.Workbooks.DeleteAsync(second.Id);

        Record($"Add Tags answered: [{string.Join(" | ", added)}]");
        Record($"after the batch add: [{string.Join(" | ", batchFirst)}] and [{string.Join(" | ", batchSecond)}]");
        foreach (var label in _labels)
        {
            Assert.AreEqual(1, added.Count(tag => tag == label), $"Add Tags answers '{label}' once: [{string.Join(" | ", added)}]");
            Assert.AreEqual(1, afterAdd.Count(tag => tag == label), $"the workbook holds '{label}' once: [{string.Join(" | ", afterAdd)}]");
            Assert.AreEqual(1, batchFirst.Count(tag => tag == label), $"the batch tagged the first with '{label}' once");
            Assert.AreEqual(1, batchSecond.Count(tag => tag == label), $"the batch tagged the second with '{label}' once");
        }
        foreach (var split in new[] { "Yearly", "Sales", "Marketing" })
            Assert.DoesNotContain(split, afterAdd, $"no label was split at its space or comma: [{string.Join(" | ", afterAdd)}]");
        foreach (var labels in new[] { afterDelete, clearedFirst, clearedSecond })
            Assert.IsFalse(labels.Intersect(_labels).Any(), $"the labels are gone: [{string.Join(" | ", labels)}]");
        await ClientFixtures.FailsAsync(TableauErrorKind.NotFound, () => client.Workbooks.GetAsync(second.Id));
    }

    // A line for the test's output, which the PR quotes and CI's public log shows: shapes only.
    private void Record(string line) => TestContext.WriteLine(Scrubbed(line));

    // The text with every setting that names the site, its server or the sign-in, and every live secret, as ***: a
    // refusal's detail can name the site or the user, and an assertion's message carries it into the log. Case aside,
    // since a server may echo a host or a content URL in another case.
    private static string Scrubbed(string text)
    {
        foreach (var value in LiveSite.Known.Concat(LiveSite.Secrets).Where(value => value.Length > 0).OrderByDescending(value => value.Length))
            text = text.Replace(value, "***", StringComparison.OrdinalIgnoreCase);
        return text;
    }

    private void RecordFiles()
    {
        foreach (var line in LiveSite.FileHeaders.Distinct(StringComparer.Ordinal))
            Record(line);
    }

    [System.Text.RegularExpressions.GeneratedRegex("[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}",
        System.Text.RegularExpressions.RegexOptions.CultureInvariant)]
    private static partial System.Text.RegularExpressions.Regex Luids();

    private static async Task<long> LengthAsync(TableauDownload file)
    {
        var buffer = new byte[81920];
        long length = 0;
        int read;
        while ((read = await file.Content.ReadAsync(buffer)) > 0)
            length += read;
        return length;
    }

    // The file's first bytes, as Latin-1 text, so a signature's high byte reads as itself.
    private static async Task<string> StartAsync(TableauDownload file, int count)
    {
        var start = new byte[count];
        await file.Content.ReadExactlyAsync(start);
        return Encoding.Latin1.GetString(start);
    }

    private static async Task<string> TextAsync(TableauDownload file)
    {
        using var reader = new StreamReader(file.Content, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return await reader.ReadToEndAsync();
    }

    // The CSV's rows, each its fields, a quoted field's quotes taken off and its doubled quotes made one.
    private static List<List<string>> Csv(string text)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"')
                {
                    field.Append('"');
                    i++;
                }
                else if (c == '"')
                {
                    quoted = false;
                }
                else
                {
                    field.Append(c);
                }
            }
            else if (c == '"')
            {
                quoted = true;
            }
            else if (c == ',')
            {
                row.Add(field.ToString());
                field.Clear();
            }
            else if (c is '\r' or '\n')
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                row.Add(field.ToString());
                field.Clear();
                rows.Add(row);
                row = [];
            }
            else
            {
                field.Append(c);
            }
        }
        if (field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString());
            rows.Add(row);
        }
        return rows;
    }

    // A column of text to filter the view on, and the first row's value in it: no number, no comma or backslash, which a
    // view filter reads as a list or an escape; or -1 when the data has none.
    private static (int Column, string Value) FilterOf(List<List<string>> rows)
    {
        if (rows.Count < 2) return (-1, "");
        for (var column = 0; column < rows[0].Count && column < rows[1].Count; column++)
        {
            var value = rows[1][column];
            if (value.Length > 0 && !double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out _)
                && value.IndexOfAny([',', '\\']) < 0 && rows[0][column].Length > 0)
                return (column, value);
        }
        return (-1, "");
    }
}
