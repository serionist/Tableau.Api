# Moving from 1.2.x to 2.0

2.0 is a rebuild of Tableau.Api for .NET 10, in progress on `main` and not on nuget.org yet. This guide lists every
change from 1.2.5 that a caller has to act on, and grows with each change that makes one. Stay on 1.2.x until 2.0.0
is released: it takes security fixes ([SECURITY.md](../SECURITY.md)).

**Where the list comes from:** `eng/apicompat/1.2.5.xml`, the ledger of every break in the public API, which CI
compares on every pull request: the ApiCompat tool reads 1.2.5's DLL from nuget.org and the library's build, and fails
on a break the ledger doesn't list. Each section below groups the ledger's entries, with how to move over.

| Area | Change | Ledger entries |
|---|---|---|
| [Target](#target-net100-only) | net10.0 only, where 1.2.x is netstandard2.0 | none: package validation's one suppression, PKV006 |
| [Version](#version) | 2.0.0, from git tags | none |
| [Signatures](#six-methods-whose-signature-changed) | six methods whose parameters or return type changed | 6, `CP0002` |
| [Permission capabilities](#permission-capabilities-renumbered) | `capabilityTypeName` members renumbered | 12, `CP0011` |
| [Dependencies](#dependencies) | Microsoft.Extensions.Logging 2.2.0, and Newtonsoft.Json added | none |

## Target: net10.0 only

1.2.x targets netstandard2.0, which .NET Framework 4.6.1 and later and .NET Core 2.0 and later load. 2.0 targets
net10.0 alone.

- **On .NET 10 or later:** take 2.0 when it is released.
- **On .NET Framework, or on .NET before 10:** stay on 1.2.x, with a version range that keeps you there, such as
  `<PackageReference Include="Tableau.Api" Version="[1.2.6,2.0.0)" />`. 1.2.x takes security fixes only.

## Version

2.0 follows Semantic Versioning: 2.0.0, with release candidates `2.0.0-rc.N` before it. Builds of `main` are
`2.0.0-alpha.0.<n>`, made by CI and never published.

## Six methods whose signature changed

`main` changed these 1.2.5 methods of `TableauApiClient` before the rebuild started. Code compiled against 1.2.5 that
calls one fails with `MissingMethodException`; recompiled, a call that passes its arguments by position may not
compile, or may bind a value to the new parameter.

| 1.2.5 | Now | How to move over |
|---|---|---|
| `CreateMonthlySchedule(session, name, priority, type, executionOrder, startTime, dayToRun, cancelToken)` | `dayToRun` is gone | Drop the argument |
| `UpdateToMonthlySchedule(session, newName, newPriority, type, executionOrder, startTime, dayToRun, cancelToken)` | `dayToRun` is gone | Drop the argument |
| `DownloadDataSource(session, datasourceId, includeExtract, cancelToken)` returns `Task<Stream>` | It returns `Task<(Stream stream, DatasourceFileType type)>` | Take `.stream` from the result; `.type` is the file's kind |
| `SignInWithPAT(tokenName, token, siteContentUrl, cancelToken)` | `userIdToImpersonate` comes before `cancelToken` | Pass the cancellation token by name, `cancelToken: cancellationToken` |
| `UpdateDataSource(session, datasourceId, newProjectId, newOwnerId, newCertificationStatus, newCertificationNote, cancelToken)` | `newName` comes before `cancelToken` | Pass the token by name |
| `UpdateWorkbook(session, workbookId, newShowTabs, newProjectId, newOwnerId, cancelToken)` | `newName` comes before `cancelToken` | Pass the token by name |

## Permission capabilities renumbered

`Tableau.Api.Models.capabilityTypeName` gained the capabilities newer Tableau versions grant: `CreateRefreshMetrics`,
`RunExplainData`, `Execute` and `WebAuthoringForFlows` went in among the existing members, and `SaveAs` at the end. So
every member from `Delete` on has another number: `Delete`, `ExportData`, `ExportImage`, `ExportXml`, `Filter`,
`ProjectLeader`, `Read`, `ShareView`, `ViewComments`, `ViewUnderlyingData`, `WebAuthoring` and `Write`.

- **Code compiled against 1.2.5** holds the old numbers, so it sends another capability than it names: recompile it.
- **A capability stored as a number,** in a database or a file, reads as another one: store and read it by name.

## Dependencies

| Package | 1.2.5 | Now |
|---|---|---|
| Microsoft.Extensions.Logging | 2.1.1 | 2.2.0 |
| Newtonsoft.Json | none | 13.0.4, for the Vizportal client |
