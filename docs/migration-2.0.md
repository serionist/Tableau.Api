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
| [The 2.0 client](#the-20-client) | `TableauClient` takes over `TableauApiClient`'s calls area by area; so far Server Info | 1, `CP0002` |
| [Dependencies](#dependencies) | Microsoft.Extensions' abstractions in place of Microsoft.Extensions.Logging, and Newtonsoft.Json added | none |

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

## The 2.0 client

`TableauClient` is 2.0's client. It takes `TableauApiClient`'s calls over one area at a time, and each old method goes
when its new one lands. Build it from DI or on its own:

```csharp
// With DI: one named client per Tableau server; a service takes [FromKeyedServices("prod")] TableauClient.
services.AddTableauClient("prod", options => configuration.GetSection("Tableau:Prod").Bind(options));

// Without DI: dispose it when done.
await using var tableau = TableauClient.Create(new TableauClientOptions { ServerUrl = new Uri("https://tableau.example.com/") });
var info = await tableau.Server.GetInfoAsync(cancellationToken);
```

What changes for a caller who moves a call over:

- **Versions:** the client asks the server's REST API version on its first call and uses it, up to 3.29; set
  `ApiVersion` to pin one, major.minor only. A server below REST API 3.23 (Tableau Server 2024.2) is refused unless
  `AllowUnsupportedServerVersion` is set. 1.2.5 used the version it was given, and 3.14 above that.
- **Gates:** a call the server's version or deployment, Tableau Server or Tableau Cloud, lacks throws before it is sent.
- **Errors:** every failure is a `TableauException`, its `Kind` saying which: switch on it where 1.2.5 had
  `TableauRequestException`, `TableauApiVersionException` and `TableauOnlineNotSupportedException`. A refused argument is
  still an `ArgumentException`, and your own cancellation an `OperationCanceledException`; a timeout is
  `TableauErrorKind.Timeout`.
- **Tokens:** `CancellationToken cancellationToken = default`, last, where 1.2.5 took `CancellationToken? cancelToken`.
- **Connections:** plain http is refused unless `AllowInsecureHttp` is set; `DangerousAcceptAnyServerCertificate` is
  `IgnoreSSLErrors`' successor, and logs a warning; a certificate its issuer revoked is refused; a redirect is
  followed only to the same scheme, host and port; no cookie is kept between requests.
- **Options** can't change once a client is built from them.
- **Retries:** 1.2.5 sent each call once. A call that is safe to send twice, such as a read, is now sent again after
  a 408, a 429, a 5xx, a connection that failed or an attempt longer than `RequestTimeout`, three times by default
  (`MaxRetries`; 0 turns them off). A 429 or 503 is waited out as the server asks, up to `MaxRetryAfter`, two minutes
  by default; a longer wait throws `TableauErrorKind.RateLimited` at once, with `RetryAfter`. A publish, a create or a
  run is never sent twice. Retries of your own, such as a resilience handler on every `HttpClient`, don't reach the
  client: set `MaxRetries` to 0 and add yours with `AddTableauClient(...).AddHttpMessageHandler(...)`.
- **Timeouts:** `RequestTimeout` limits each attempt, two minutes by default; a download or an upload has
  `TransferTimeout` instead, for the whole of it, with no limit by default.
- **Downloads,** as their areas move over: a `TableauDownload`, returned once the headers have come, whose `Content`
  streams from the server, where 1.2.5 read the whole file into memory first. Dispose it; `SaveToFileAsync` writes it
  to a file. Its `FileName` is cleaned to a name alone, and `Kind` says what it is, as the `DatasourceFileType` beside
  the stream of the old `DownloadDataSource` did.

| 1.2.5 | 2.0 | How to move over |
|---|---|---|
| `TableauApiClient.ServerInfo(cancelToken)` returns `serverInfo` | `TableauClient.Server.GetInfoAsync(cancellationToken)` returns `Tableau.Api.Rest.Models.ServerInfo` | `info.restApiVersion` is `info.RestApiVersion`, `info.productVersion.Value` is `info.ProductVersion?.Value`. It needs no sign-in |

## Dependencies

| Package | 1.2.5 | Now |
|---|---|---|
| Microsoft.Extensions.Logging | 2.1.1 | 10.0.14, through Microsoft.Extensions.Http; the library itself references Microsoft.Extensions.Logging.Abstractions alone |
| Microsoft.Extensions.Http, .Options, .DependencyInjection.Abstractions | none | 10.0.14, for `AddTableauClient` and the client's options |
| Microsoft.Extensions.Http.Resilience | none | 10.11.0, for the client's retries and attempt timeout, with Polly.Core 8 under it; none of its types is in the library's API, and the library registers none of its services |
| Newtonsoft.Json | none | 13.0.4, for the Vizportal client |
