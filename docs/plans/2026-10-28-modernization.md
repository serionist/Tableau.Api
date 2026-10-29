---
title: Modernise Tableau.Api into the 1.2.6 security patch and the 2.0.0 release
issue: "#2"
status: draft
approved_by:
approved_at:
---

# Modernise Tableau.Api into the 1.2.6 security patch and the 2.0.0 release

This plan is the record of the work up to 2.0.0: the decisions, the design, the order of work, the step that closes each
review finding, and a progress log. Read it first when picking the work up. Update its progress log and its trackers
at the end of every step.

**In short:**

- **1.2.6 ships first** (S2), for the users of 1.2.5: the security fixes and the cheap fixes that change no signature,
  built on `release/1.x` from 1.2.5's own commit, with offline tests, released from a tag.
- **main gets CI, the open-source files and its own security fixes next** (S1, S3), then the review goes public (S4).
- **2.0.0 is a rebuild** (S5 to S19): net10.0 only, JSON with source-generated models and native AOT, a client bound to
  its session with every current sign-in, safe HTTP defaults, paging, gates that work, Vizportal kept and fixed, offline
  tests on every PR and a live suite every night.
- **Every finding F1 to F50 has exactly one step** (Finding tracker). None is "won't fix". Every research decision and
  feature has a step or a follow-up issue (Research tracker).
- **Six questions** wait for the maintainer (Open questions).

**Sources:** the codebase review (findings F1 to F50) and the research (its decisions, called RD1 to RD9 here, and its
22 ranked features), both of 2026-10-28. Until 1.2.6 is on nuget.org both stay in `.local/review/`, which git ignores.
S4 publishes them as `docs/reviews/2026-10-28-codebase.md` and `docs/reviews/2026-10-28-research.md`.

**Approval:** draft. @serionist took D1 to D10 on 2026-10-28. The plan's own choices (Design) and the open questions
wait for their yes on #2.

## Goal

When this is done:

- **1.2.6 is on nuget.org** with the 1.2.5 API on netstandard2.0, the fixes listed under Design, an MIT licence and true
  package metadata. 1.2.x then takes security fixes only.
- **2.0.0 is on nuget.org** as `Tableau.Api`:
  - net10.0, `IsAotCompatible`, JSON on the wire through source-generated models;
  - a client from DI or a factory, bound to a session it signs in itself, with PAT, connected-app JWT, unified access
    token and password sign-in;
  - every 1.2.5 endpoint ported, plus the high-value new ones, Vizportal included;
  - tests offline on every PR with a coverage floor, and live every night;
  - a migration guide that lists every break against 1.2.5.

**In scope:**

- **The 1.x line:** `release/1.x`, 1.2.6, its tests and its release workflow.
- **main, before 2.0:** CI, the open-source files, the Vizportal TLS fix and S2's security fixes ported to main.
- **2.0:** layout and build, the reformat, the test platform, the model generator, the client core, resilience,
  sessions, the port of all 142 REST methods and the 41 Vizportal operations, publishing, the research's P1 features,
  the four metadata methods of the NuGet_Prod line, docs, the release candidate and the release.

**Out of scope** (under Later): the research's P2 and P3 features, each as a follow-up issue; Tableau Server in CI; a
year-numbered version line; any package id other than `Tableau.Api`.

## Decisions

Taken by @serionist on 2026-10-28.

| # | Decision | Detail | Steps |
|---|----------|--------|-------|
| D1 | A 1.2.6 security patch ships first | For the users of 1.2.5. Built on `release/1.x` from the commit 1.2.5 was built from: `b23503e` on today's `main` (2021-10-13, "Updated Tableau.Api.csproj"; `b11a150` before the history was rewritten). It keeps netstandard2.0 and the 1.2.5 signatures. It holds the fixes for F2 and F3, and the cheap fixes that change no signature and exist in the 1.2.5 code, each checked against that code. F1 is not in 1.2.5 (Vizportal came later) and is fixed on main. It has offline tests with a fake server. It is released from a tag by a `release.yml` workflow through the `nuget` environment. nuget.org trusted publishing is set up for workflow `release.yml` and environment `nuget` on serionist/Tableau.Api, so both names are fixed; the environment allows `v*` tags only and needs a maintainer's approval. | S2 |
| D2 | 2.0.0, SemVer, MinVer | Prereleases are `-rc.N`. Package validation runs against the 1.2.5 baseline; every break is suppressed deliberately and listed in the migration guide. | S5, S18, S19 |
| D3 | Vizportal stays in the main package, fixed | The research's "drop it" is overruled. The fix covers F1 (TLS), F29, F41, F49, cancellation, paging, the shared HTTP core and the same exception and logging rules. The docs say it wraps Tableau's undocumented internal API, may break with any Tableau release, and name the Tableau versions it was tested on. | S3, S16 |
| D4 | MIT | "the Tableau.Api contributors". The LICENSE lands with the harness PR (#1). | S0 |
| D5 | The package id stays `Tableau.Api` | With a "not affiliated with or endorsed by Tableau or Salesforce" disclaimer in the README and the package description. No new `Tableau.*` ids. | S1, S2, S5 |
| D6 | net10.0 only | .NET Framework and older users stay on 1.2.x, which gets security fixes only. | S1, S5 |
| D7 | JSON on the wire | System.Text.Json with source-generated models; `IsAotCompatible`, with an AOT smoke-test project in CI like WriteableFileProviders' `tests/WriteableFileProviders.Aot`. XML stays only where Tableau requires it, such as the multipart publish payload, and is written with `XmlWriter`. The models are generated from the newest published xsd by a repeatable generator in `eng/`, never hand-patched, and a CI check proves the generated file is current. How the generator works is under Design. | S8, S17 |
| D8 | A modern client | DI registration with options and IHttpClientFactory typed clients. One session manager for every sign-in Tableau has now: PAT, connected-app JWT (direct trust and OAuth 2.0 trust), unified access tokens (Cloud), password with impersonation (Server); one sign-in at a time, sign in again on expiry, thread-safe. The client is bound to its session, with an explicit session API kept for multi-site work. `IAsyncEnumerable` paging and a typed filter and sort builder. A `CancellationToken` last on every method, `ConfigureAwait(false)`, nullable annotations, consistent `...Async` names and return shapes. Resilience: retry only idempotent calls, honour 429 and `Retry-After`, long timeouts for downloads and publishing, streamed downloads. No cross-host redirects with credentials; TLS validation always on unless explicitly turned off. One exception type with the details. Server-vs-Cloud and API-version gates that work, with the version read from Server Info. The Cloud schedule model from API 3.20. The supported floor: the maintained Server versions (about 2024.2, REST 3.23) and Cloud, older versions on opt-in (RD8). | S9 to S15 |
| D9 | Tests | Offline tests with a fake server or `HttpMessageHandler` on every PR, with a coverage floor enforced in CI (WriteableFileProviders' `eng/coverage-gate.cs` is the model). A live suite, nightly and on dispatch, against a free Tableau Developer Program site, signed in through a direct-trust connected app from a reviewed GitHub environment; never on PRs from forks. Server-only features are tested by hand, and the PR says so. The old live tests' coverage is kept or replaced. | S2, S7, S11, S12 to S17 |
| D10 | New endpoints | The high-value ones from the research's ranked list in 2.0.0, the rest as follow-up issues. The four NuGet_Prod metadata methods (F44) are in 2.0.0. | S11 to S17, Research tracker |

## How the work runs

**Already done, 2026-10-28:** the repo is public; the rulesets are set; the `nuget` environment exists (tags `v*` only,
a maintainer's approval); the nuget.org trusted publishing policy names `release.yml` and `nuget`.

**S0, done 2026-10-29:** the harness and licence, PR #3 (squash `262223d`), closing #1. Its review took eight rounds,
recorded in `docs/reviews/2026-10-28-dev-harness-code.md`. #4 keeps the permission-rule derivation and its engine check
in the repo.

**Every later step:**

- One issue and one PR per step, on a branch `<issue>-<slug>`. The issue numbers go into the Steps table as they are
  opened.
- Each PR updates this plan: its step's status, the trackers and the progress log.
- `review-code` runs in fresh agents, round after round, until no blocker or major is open. Each round is recorded in
  `docs/reviews/`, except where the security rule below says `.local/review/`.
- CI is green, then the session squash-merges. The session has standing approval to merge.
- **One-way doors need the maintainer's yes on the issue** before merge: a name, the public API shape, the version, the
  supported Tableau floor. Each step lists its doors.

**Order:**

- **CI and the open-source files come early.** S1 lands right after S0, because 1.2.6 needs CI: S2 reuses S1's
  workflows. S1 and S2 may be built side by side. Once S1 merges, `CI` and `PR text` become required checks on the
  `main` ruleset, and on `release/1.x` before S2's PR.
- **The reformat lands alone** (S6), so `.git-blame-ignore-revs` can list its commit. The next PR (S7) adds that file.
- **The 2.0 steps leave `main` building:** each port step deletes the old methods it replaces, and an old file when it
  empties.

**Security text:** until 1.2.6 is on nuget.org, nothing public carries exploit detail.

- This plan is public. It says what a security fix changes ("escape every path segment", "never send credentials to
  another host"), never how to attack the old code.
- The same holds for S2's and S3's issues, PRs, commits, CHANGELOG and release notes.
- The review, the research and the review-code records of S2 and S3 stay in `.local/review/` until 1.2.6 ships; S4
  publishes them.
- SECURITY.md (S1) makes this the standing rule for every later fix.

**Ids:** no plan or review id (F, D, RD, P, Q, R, S numbers) appears in code, comments or contributor docs. Only
`docs/plans/` and `docs/reviews/` cite them. A PR names its step and links this plan.

**Writing:** plain words, no em dashes, British spelling, the conclusion first, tables and short bullets.

## Design

### Two lines

| Line | Branch | Target | Takes | Version | Released by |
|---|---|---|---|---|---|
| 1.x | `release/1.x`, from `b23503e` | netstandard2.0 | 1.2.6's fixes, then security fixes only (D6) | `<Version>` in the project, tags `v1.*` | the branch's `release.yml`, environment `nuget` |
| 2.x | `main` | net10.0 | everything else | MinVer from `v*` tags, minimum 2.0, `-rc.N` before 2.0.0 | main's `release.yml` (S18), environment `nuget` |

- **`b23503e` is 1.2.5's commit.** Its project says `<Version>1.2.5</Version>` and netstandard2.0. The merge after it,
  `dcc50a1`, has the same tree.
- **`release/1.x` is never merged into `main`.** A fix both lines need is written for each, as S3 does for S2. So
  MinVer on `main` never sees a `v1` tag.

### The 1.2.6 patch (S2)

**The rule:** a fix goes into 1.2.6 when it exists in 1.2.5's code, changes no public signature, and is small. Every
candidate the review named was checked against `b23503e`, and all of them are there.

| Finding | What 1.2.6 changes | What checking 1.2.5's code showed |
|---|---|---|
| F2 | Every path segment (ids, tag names, site names and content URLs) is validated and escaped: no empty or dot-only segment, and `/`, `?`, `#` and `%` always escaped. `:` and `@` stay as they are, since upload session ids contain `:` and are not LUIDs. | Present; every area file builds its paths. |
| F3 | Redirects are not followed: a 3xx fails with the status and the host it pointed to. The token goes on each request message. | Present, in the shared request code. |
| F5 | View filters get the `vf_` prefix on view data and view image, as on PDF; one helper builds them; ordinal comparison. | 1.2.5 strips the prefix on both data and image (main fixed image in 2024). The same helper stops `QueryViewPDF` failing when no filters are passed, a 1.2.5-only defect. |
| F6 | Every query key and value is escaped. In `filter` and `sort` the parts between `:` and `,` are escaped and the separators kept. | Filter and sort values, and every key, are sent raw. |
| F7 | Revision lists send `pageSize` and `pageNumber`. | Validated, never sent. |
| F10 | Schedule create accepts 201 (through F15). The start and end times go as `HH:mm:ss` with no offset, and a time outside `[00:00, 24:00)` throws. Monthly schedules send `monthDay` from the existing `dayToRun` parameter. | 1.2.5 still has `dayToRun` (main removed it), and its schema has `monthDay`, so this needs no new signature. |
| F11 | Extract refresh task lists select the task items by type and treat an empty list as empty. | The cast bug and the null list are both there. |
| F12 | Get Extract Refresh Task calls `tasks/extractRefreshes/{id}`. | Calls `tasks/{id}`. |
| F14 | Enum values added: `OpenID` and `TableauIDWithMFA` for the user auth setting, and every value the 3.29 xsd lists that 1.2.5's enums lack. | 1.2.5 knows only `ServerDefault` and `SAML`. The values are added to the generated file by hand, which is acceptable on a branch that only takes fixes. |
| F15 | Any 2xx is success. | One exact status per call. |
| F27 | `TableauSession.UserName` holds what each sign-in meant it to: the user name, the token name for a PAT, the old name after Switch Site. | Always null. |
| F39 | The base URL's path is kept, so a server behind a path prefix is reachable. | Rebuilt from scheme, host and port. |

- **How F2 is built:** an interpolated-string handler for paths escapes and validates every hole, so each call site
  changes only by its wrapper. The compiler attribute it needs is declared internally for netstandard2.0.
- **What stays as in 1.2.5,** because fixing it changes a signature or makes working calls throw: the dead version
  gates (F4), null lists (F20), buffered downloads (F17), the nullable token (F21), and the rest.
- **Considered and left out** (Q5): F17's Content-Type null check, F28 and F32 also qualify. They stay for 2.0 to keep
  the patch small.
- **The package:**
  - `<Version>1.2.6</Version>`, a true description ending in the disclaimer (D5), MIT, the GitHub URLs, a README packed
    into the package, `snupkg`, no `IncludeSource`, `ContinuousIntegrationBuild` in CI;
  - the same single dependency, `Microsoft.Extensions.Logging` 2.1.1.
- **The branch also gets** what its PRs and its release need, taken from `main`: `global.json`, `nuget.config`, LICENSE,
  `.gitleaks.toml`, `eng/gate.cs` (its `text` lane; the harness checks do not apply there), `ci.yml`, `pr-text.yml`,
  `codeql.yml`, `release.yml`, a short AGENTS.md for the branch's limits, README, SECURITY.md and CHANGELOG.md. No
  Dependabot version updates: the line changes only for security fixes.
- **The old live test project is deleted** on the branch: it cannot build (netcoreapp2.1) and needs a server. The
  offline suite replaces it; a live smoke run by hand checks the release.
- **CHANGELOG, 1.2.6** (the ids in brackets are for this plan; the CHANGELOG carries none):
  - Security: path segments are validated and escaped; redirects are not followed, so credentials and request bodies
    never go to another address.
  - Fixed: the rest of the table.
  - Changed: inputs once sent raw are escaped or refused; a filter that was already URL-encoded is now encoded twice;
    a redirect fails; any 2xx succeeds; a schedule time outside a day throws.
  - Known issues, fixed in 2.0: `UpdateTo*Schedule` creates a new schedule (F9); the Active Directory group updates miss
    the group id (F13); `AddWorkbookToSchedule` returns null (F31); `QueryDatabases` and `QueryTables` return the first
    page (F8).

### Security on main before 2.0 (S3)

main is unreleased, but public. S3 makes it safe before the rebuild replaces the code:

- **Vizportal (F1):** certificates are validated as on REST. One helper builds the handler for both clients, and it
  turns validation off only when `IgnoreSSLErrors` is set. No automatic redirects.
- **REST (F2, F3, F6, F39):** main's `buildUri`, `buildClient` and `apiRequest` are still 1.2.5's, so S2's versions and
  its path handler are ported as they are, with main's extra call sites (`QueryView`, the crosstab export, view
  permissions).
- **Tests:** S2's request-building tests, adapted to main's signatures, start `tests/Tableau.Api.Tests`, plus an F1 test
  against a TLS fake with an untrusted certificate.

### Build and layout for 2.0 (S5)

```
Tableau.Api.slnx
Directory.Build.props        from S0: net10.0, LangVersion, Nullable, strictness, analyzers, docs, audit, lock files
Directory.Packages.props     central package versions, lock files
global.json                  from S0: SDK 10.0, test runner Microsoft.Testing.Platform
eng/gate.cs                  from S0; gains the generator check in S8
eng/coverage-gate.cs         S7: fails when the assembly falls below its floor in eng/coverage.json
eng/generate-models.cs       S8: the model generator; eng/schema/ holds the xsd
src/Directory.Build.props    packaging: MinVer, README, licence, SourceLink, snupkg, package validation (baseline 1.2.5)
src/Tableau.Api/
tests/Directory.Build.props  MSTest 4 on Microsoft.Testing.Platform, coverage, no packing
tests/Tableau.Api.Tests/     offline and live tests, fakes, fixtures
tests/Tableau.Api.Aot/       S8: published with native AOT in CI
```

- **Strictness:**
  - `TreatWarningsAsErrors`, `EnforceCodeStyleInBuild`, `AnalysisLevel` latest-recommended, `GenerateDocumentationFile`,
    `Nullable`, no blanket `NoWarn`.
  - These are errors: CA1068 and CA2016 (tokens), CA2007 (`ConfigureAwait`), CA1305, CA1309 and CA1310 (culture),
    CA2000 (disposal), CA2021 (casts).
- **The legacy ratchet:** old files compile under a `TableauLegacy` section of `.editorconfig` that relaxes those rules
  and, from S8, the trim and AOT warnings. Each port step removes its files from it. S18 checks that it is empty.
- **Package validation:**
  - `EnablePackageValidation` with `PackageValidationBaselineVersion` 1.2.5 (D2);
  - `CompatibilitySuppressions.xml` regenerated with `GenerateCompatibilitySuppressionFile` whenever a step breaks
    something more, and reviewed in that PR as the list of breaks;
  - the migration guide groups the same list.

### The 2.0 client (S9, S10, S11)

```csharp
// One named client per Tableau connection.
services.AddTableauClient("prod", o =>
    {
        o.ServerUrl = new Uri("https://tableau.example.com/tableau/"); // its path is kept
        o.Site = "finance";                                          // content URL; "" is the default site
        o.ApiVersion = TableauApiVersion.Negotiate;                  // or pinned, such as new(3, 27)
        o.Deployment = TableauDeployment.Auto;                       // Server or Cloud, or detected
    })
    .UseConnectedAppDirectTrust(sp => sp.GetRequiredService<MySecrets>().TableauDirectTrust);

// The client signs itself in, lazily, once, and again when the session ends.
try
{
    await foreach (var workbook in tableau.Workbooks.ListAsync(
        new() { Filter = TableauFilter.Eq(WorkbookField.Tags, "finance") }, cancellationToken))
    {
        await using var download = await tableau.Workbooks.DownloadAsync(workbook.Id!, cancellationToken: cancellationToken);
        await download.SaveToFileAsync(Path.Combine("out", download.FileName), cancellationToken);
    }
}
catch (TableauException e) when (e.Kind == TableauErrorKind.RateLimited)
{
    // e.RetryAfter, e.StatusCode, e.ErrorCode, e.Summary, e.Detail, e.RequestPath
}
```

**Shape** (the maintainer's yes on S9's issue; S11 and S12 add their parts):

- **`TableauClient`** with one sub-client per area: `Server`, `Sessions`, `Sites`, `Projects`, `Workbooks`, `Views`,
  `DataSources`, `Users`, `Groups`, `Permissions`, `Tags`, `Favorites`, `Jobs`, `Tasks`, `Schedules`, `Subscriptions`,
  `Metadata`, `VizPortal`. Sub-clients are created once per client (F46).
- **Verbs:** `GetAsync`, `ListAsync` (`IAsyncEnumerable<T>`), `ListPageAsync` (`Page<T>`), `CreateAsync`, `UpdateAsync`,
  `DeleteAsync`, `DownloadAsync`, `PublishAsync`, `RunAsync`, `CancelAsync`, `AddAsync`, `RemoveAsync`. An options
  record where a method has more than three optional parameters. String LUIDs throughout. The token last, defaulted.
- **Mocking:** sub-clients are classes with virtual methods and a protected constructor (the Azure SDK pattern), not
  interfaces, so adding a method in a minor breaks no implementer.
- **Options:** `TableauClientOptions` (above, plus `AllowUnsupportedServerVersion`, `DangerousAcceptAnyServerCertificate`,
  `AllowInsecureHttp`, timeouts and resilience), immutable once built, bound from configuration by the binder source
  generator and validated at start-up by the options validation source generator. Both stay AOT safe.
- **DI and typed clients:**
  - `AddTableauClient(name, ...)` registers a typed client over a named `HttpClient`, keyed by name, and returns a
    builder for credentials and resilience. An unnamed overload registers the default client.
  - Typed clients are transient, so the long-lived state of each named client (session, negotiated version,
    deployment, rate-limit back-off) lives in a singleton keyed by the name.
- **Without DI:** `TableauClient.Create(options, credentials, loggerFactory)` builds the same pipeline over one
  `SocketsHttpHandler` with `PooledConnectionLifetime`. A constructor takes a caller's `HttpClient`, which the client
  never disposes. The client is `IAsyncDisposable` and signs out on dispose (F18).

**Endpoint descriptors and gates (F4, F16):**

```csharp
internal sealed record Endpoint(string Name, HttpMethod Method, string Template, TableauApiVersion MinVersion,
    TableauDeployments Deployments, bool Idempotent, TransferKind Transfer);
```

- Each REST call has one descriptor in an internal `Endpoints` registry. The pipeline reads it for the path, the
  gates, the retry rule and the timeout. No reflection, no static constructor.
- **Version:** with `Negotiate`, the client reads `restApiVersion` from Server Info once (whether Server Info needs no
  sign-in is to verify in S9) and caps it at the newest version it knows, 3.30 today. A pinned version is parsed as
  major.minor only (F47).
- **Floor (D8):** below the floor, the first call throws unless `AllowUnsupportedServerVersion` is set, which logs a
  warning once. The exact floor is confirmed in S9 (Q4).
- **Deployment:** `Auto` takes Cloud for the Tableau Cloud host names (the list is to verify in S9; Server Info is
  checked for a better signal), Server otherwise; or set it.
- **Before any request:** a call below its descriptor's version, or on a deployment it does not run on, throws
  `TableauException` (`UnsupportedApiVersion` or `UnsupportedDeployment`) and sends nothing. Impersonation and Switch
  Site are Server only. `DeleteGroup` is gated at 2.1 on both deployments.
- **URL shapes:** the pipeline builds `/api/{version}/...`, `/api/-/...` (resource-versioned endpoints, for later
  minors) and `/api/metadata/graphql`.

**HTTP rules:**

- **Paths:** built from segments by S3's path handler; a descriptor may mark a segment as a LUID, checked strictly
  (F2).
- **Queries:** every key and value escaped; `filter` and `sort` come from the builder (F6). The base URL's path is
  kept (F39).
- **Headers:** `Accept` and `Content-Type` are `application/json`. `X-Tableau-Auth` goes on each request message (F3).
- **Redirects:**
  - the handler's automatic redirects are off;
  - the client follows at most five redirects, and only to the same scheme, host and port;
  - 307 and 308 resend the method and a body held in memory; 301, 302 and 303 are followed for GET and HEAD only;
  - anything else, and any move from https to http, fails with `TableauException` (`Redirect`) naming the target
    host, and nothing is sent there.
- **TLS:**
  - validation is always on;
  - `DangerousAcceptAnyServerCertificate` turns it off and logs a warning at start-up (F38; the old name goes);
  - revocation: a certificate known to be revoked is refused, and an unknown revocation status is tolerated (an
    `X509ChainPolicy` on the handler; to verify on Windows and Linux in S9).
- **Plain http** is refused unless `AllowInsecureHttp` is set (F38).
- **Status:** any 2xx is success (F15). An expected element missing from a 2xx body throws `TableauException`
  (`InvalidResponse`) naming the endpoint (F20).
- **Async:** every await in the library has `ConfigureAwait(false)` (F37).

**One exception type (D8, F35):**

- `TableauException` is the only exception type the library defines. It carries:
  - `Kind`: Http, Authentication, NotFound, RateLimited, Timeout, Redirect, UnsupportedApiVersion,
    UnsupportedDeployment, InvalidResponse, VizPortal; the list may grow in minors;
  - `StatusCode`, Tableau's `ErrorCode`, `Summary` and `Detail`, `Method`, `RequestPath` with query values redacted,
    the request id when the server sends one, `RetryAfter`, and the response body cut to 4 KB;
  - public constructors, for consumers' tests.
- Argument errors stay `ArgumentException`. The caller's cancellation stays `OperationCanceledException`. A timeout is
  `TableauException` (`Timeout`) with the cause as `InnerException`, never the caller's cancellation.
- The 1.x exception types and attributes go in 2.0 (migration guide).

**Logging (F36):**

- `Microsoft.Extensions.Logging.Abstractions` only, with `LoggerMessage` source-generated methods.
- Request lines at Debug, with query values redacted. A failure the caller receives is not logged above Debug.
  Cancellation is never an error.
- Never logged: tokens, PATs, JWTs, passwords, cookies, `X-Tableau-Auth`, bodies. A test reads every log line the
  suite writes and fails on any secret.

### Resilience and downloads (S10)

- **Pipeline:** `Microsoft.Extensions.Http.Resilience`, built the same way with and without DI. The caller can
  configure it or replace it.
- **Retries:** only for descriptors marked idempotent (GET, DELETE, PUT on a resource, sign-in). Never publish, create
  or run-now. 408, 429, 5xx and connection failures, three times, with jittered back-off.
- **429:** honours `Retry-After`, or the wait in Tableau's 429 body when the header is missing (the body's wording is to
  verify). Above a cap (two minutes by default) it does not wait: it throws `RateLimited` with `RetryAfter`, since
  Cloud's hourly limits are not worth waiting for in the call.
- **Timeouts:** per attempt for ordinary calls; downloads, exports and uploads (`TransferKind`) use a separate transfer
  timeout with no total cap by default. Neither looks like the caller's cancellation.
- **No circuit breaker or concurrency limiter** by default: one client talks to one server, and a breaker opening on a
  burst of 5xx surprises batch jobs.
- **Downloads (F17, F42):** sent with `ResponseHeadersRead`. `TableauDownload` (`IAsyncDisposable`) owns the response
  and has:
  - a forward-only `Content` stream that honours the token;
  - `FileName` from `Content-Disposition`, passed through `Path.GetFileName` with invalid characters removed;
  - `ContentType`, `Length` when known, and a file kind from the name's extension or the zip signature;
  - `SaveToFileAsync`.

  A response without Content-Type works.

### Sessions and sign-in (S11)

- **Credentials** (`TableauCredentials`), each with a delegate overload so a secret can come from a vault:
  - `PersonalAccessToken(name, secret)`;
  - `ConnectedAppDirectTrust(clientId, secretId, secretValue, userName, scopes)`: the library mints an HS256 JWT at
    each sign-in (header `kid` and `iss`; claims `iss`, `exp` at most 10 minutes ahead, a fresh `jti`, `aud` "tableau",
    `sub`, `scp` as an array) with the BCL, no JWT package;
  - `Jwt(Func<CancellationToken, ValueTask<string>>)` for OAuth 2.0 trust and any JWT minted elsewhere;
  - `UnifiedAccessToken(...)`: Cloud only, 3.27, signs in with `isUat`;
  - `Password(name, password)`, with `ImpersonateUserId` (Server only, also with a PAT where Server allows it).
- **The session manager,** one per named client:
  - signs in lazily on the first call, one sign-in at a time (a `SemaphoreSlim`; one PAT allows one session);
  - holds an immutable `TableauSession` swapped atomically;
  - signs in again before the known expiry (`estimatedTimeToExpiration`, else 240 minutes on Server and 120 on Cloud)
    and once after a 401, resending the call when its body can be resent;
  - signs out on dispose, without throwing.
- **`TableauSession`** is a record with `SiteId`, `SiteContentUrl`, `UserId`, `UserName`, `ExpiresAt`, `Deployment` and
  `ApiVersion`. Its `ToString` and `PrintMembers` leave the token out (F27, F34).
- **The explicit API:** `client.Sessions.SignInAsync(credentials, site)`, `SwitchSiteAsync` (Server only) and
  `SignOutAsync`. `client.WithSession(session)` gives a client bound to that session, which never signs in by itself:
  a 401 throws `Authentication`. One bound client per site serves multi-site work.

### Paging and filters (S12)

- `ListAsync(options, cancellationToken)` pages lazily with `[EnumeratorCancellation]`. `ListPageAsync` returns
  `Page<T>(IReadOnlyList<T> Items, int PageNumber, int PageSize, int? TotalAvailable)`.
- Paging stops at `TotalAvailable` or on a short page. The default page size is 100, at most 1,000 where the docs say
  so. Lists are never null (F20).
- Results follow Tableau's internal order unless sorted. The list sorts by a stable field where the resource has one,
  and the docs state the caveat.
- **Filters and sorts:** `TableauFilter.Eq`, `In`, `Gt`, `Gte`, `Lt`, `Lte`, `Has` and `CiEq`, combined with `And`,
  over typed fields (`WorkbookField.Tags`); `TableauSort.Asc` and `Desc`. Values are escaped and the syntax kept. A raw
  string overload escapes the parts between separators.
- One enumerable model also serves Vizportal's page tokens and GraphQL cursors.

### Models and the generator (S8)

**What the research found:**

- The schema grew from 191 named types in 3.16 to 296 in 3.29: 105 added, none removed.
- `ts-api_3_29.xsd` is the newest file published; `ts-api_3_30.xsd` answered 404 on 2026-10-28.
- Tableau's JSON is its XML converted mechanically, and no schema describes it:
  - attributes become properties;
  - lists keep their wrapper objects (`{"users":{"user":[...]}}`);
  - numbers may arrive as strings (both to verify);
  - the xsd is the only contract, so recorded fixtures pin the JSON.
- `XmlSerializer` cannot be trim or AOT safe, and `sgen` does not help under AOT.
- The System.Text.Json source generator needs public members, and metadata mode to deserialise.
- `xsd.exe` is Windows only and emits `XmlSerializer` types; XmlSchemaClassGenerator emits `XmlSerializer` classes as
  well.

**How the generator works:**

- **Input:** `eng/schema/ts-api_3_29.xsd`, checked in. `eng/schema/README.md` records its URL, download date and SHA-256.
- **Tool:** `eng/generate-models.cs`, a .NET 10 file-based app, like the coverage gate.
  - `dotnet run eng/generate-models.cs` writes `src/Tableau.Api/Models/Generated/`.
  - `-- --check` exits non-zero when the committed output differs. The gate's quick lane runs it, so CI proves the
    generated file is current (D7).
  - It compiles the xsd with `System.Xml.Schema.XmlSchemaSet` and walks the compiled types. No third-party generator.
- **Output:**
  - deterministic, ordered by name, with an `<auto-generated/>` header naming the xsd and its hash;
  - never edited by hand;
  - hand additions live in partial declarations in other files.
- **Mapping:**

  | xsd | C# |
  |---|---|
  | named complex type `workbookType` | `public sealed partial record Workbook`, `init` properties |
  | attribute or element `contentUrl` | `string? ContentUrl` with `[JsonPropertyName("contentUrl")]` |
  | `xs:int`, `xs:long`, `xs:boolean`, `xs:dateTime`, `xs:time` | `int?`, `long?`, `bool?`, `DateTimeOffset?`, `TimeOnly?` written as `HH:mm:ss` |
  | optional or required | nullable in both cases, no `required`: a missing value never fails a response |
  | enumeration | an extensible string enum: a struct with known values as statics and the raw text kept, with a generated converter, so an unknown value parses (F14) |
  | wrapper list type (one repeated child) | `IReadOnlyList<T>` on the public model, read by a generated converter that also takes a single object; empty means `[]` |
  | `xs:choice` | one nullable property per alternative |
  | `xs:extension` | the base's properties flattened in |
  | `tsRequest` and `tsResponse` | internal envelopes; whether JSON wraps a body in them is pinned by fixtures |

- **Unsupported constructs** stop the generator with an error, so a new schema cannot drop types silently.
- **The context:** one `TableauJsonContext` lists every root. Its options:
  - the xsd's names, set on every property;
  - `AllowReadingFromString` for numbers;
  - `WhenWritingNull`, so a request carries only what the caller set (F32).
- **Requests and responses share the model types,** as the xsd does. Each area builds its requests from options
  records.
- **AOT:** `IsAotCompatible` and `VerifyReferenceAotCompatibility` are on from S8. Old files are exempt through the
  legacy section until they are ported.
  - `tests/Tableau.Api.Aot` is published with native AOT in CI, with every member of the library kept. Any warning
    fails it.
  - It then runs against an injected fake handler serving fixtures. S8 starts it with parsing; each later step adds
    its path (sign-in, paging, a download, a publish, an error, a Vizportal call).
- **XML stays** for the multipart publish payload, written with `XmlWriter` (S17). If Tableau will not answer a
  multipart request in JSON (to verify in S17), the four publish response types are read with `XmlReader`, with both
  namespaces understood and no text rewritten (F48).
- **Fixtures:**
  - S8 writes synthetic fixtures from the docs' XML examples, converted by the same rules;
  - from S11 the live suite records real ones with `TABLEAU_RECORD=1` through a handler that scrubs tokens, LUIDs,
    names, emails and host names before writing;
  - a fixture that only a Server can produce stays synthetic until the maintainer records it by hand, and the fixture
    manifest says so.

### Publishing (S17)

- `Workbooks.PublishAsync` and `DataSources.PublishAsync` take a stream, a file name, options and an
  `IProgress<long>`.
  - Up to 64 MB the file goes in one request; above it, in chunks of 50 MB by default.
  - Options: `asJob`, overwrite or append, skip the connection check, description (3.20), hidden views, connection
    credentials.
  - Any readable stream works, seekable or not. Each chunk is a bounded read with async reads (F30).
- Cancellation stops the upload and abandons the session.
- Whether the low-level file-upload calls stay public is settled on S17's issue.
- S17 deletes the last old REST code: `TableauApiClient`, `PartialStream`, `XmlSchema.cs`, `SchemaExtensions` and the
  1.x enums, exceptions and attributes (F43).

### Vizportal (S16)

- **The client:** `client.VizPortal`, on the shared pipeline, so it keeps the same TLS, redirect, logging and exception
  rules (D3). Vizportal errors are `TableauException` (`VizPortal`) with Vizportal's code.
- **Serialisation:** System.Text.Json source generation replaces Newtonsoft.Json, which leaves the package. Requests
  are camelCase everywhere, compact JSON, with tags quoted by the serialiser (F29, F49).
- **Calls:** a token on every method. Paging advances by the items returned, never changes the caller's query object,
  and has `[EnumeratorCancellation]` (F41). Model enums are tolerant. Ids are `int` everywhere. The paging helpers
  become internal.
- **Cookies:** the session cookie is `Secure` exactly when the base URL is https.
- **Cloud:** S16's live tests check whether a REST token opens a Vizportal session on Tableau Cloud (unverified). If it
  does not, every Vizportal descriptor is Server only, and the docs say so.
- **Docs:** it wraps Tableau's undocumented internal API, may break with any Tableau release, and was tested on the
  versions listed. The REST replacements table of the research goes into the guide page.

### Tests and coverage

- **Fakes:**
  - `FakeTableauHandler`, an `HttpMessageHandler` with a route table, which records method, path, query, headers and
    body, for unit tests;
  - `FakeTableauServer`, a small HTTP/1.1 server on a loopback port that records the raw request line, with an
    optional TLS mode and an untrusted generated certificate, for the pipeline (redirects, TLS, resilience,
    streaming);
  - no WireMock.Net: escaping tests need the raw request line, and the 1.x suite runs on net48.
- **Categories:**
  - `Live` and `ServerOnly` are inconclusive unless `TABLEAU_LIVE=1` or `TABLEAU_SERVER_ONLY=1`;
  - with the variable set and a setting missing, they fail and name what is missing.
- **Coverage:**
  - `Microsoft.Testing.Extensions.CodeCoverage` with Cobertura output; generated models excluded;
  - `eng/coverage-gate.cs`, after WriteableFileProviders', with a line and a branch floor for `Tableau.Api` in
    `eng/coverage.json`;
  - the floors start at S7's first green run and only rise, and the gate refuses a floor lower than `origin/main`'s;
  - at 2.0.0: at least 90% of lines and 80% of branches.
- **Legs:** Linux and Windows; on Linux, one run under sv-SE, one under tr-TR, and a time zone that is not UTC (F10,
  F28).
- **Live suite** (S11): nightly and on dispatch, on `main` only, in the `tableau-live` environment, serial.
  - It signs in with the direct-trust app.
  - Every object is made in a project named after the run and deleted in cleanup; leftovers older than a day with the
    prefix are swept.
  - It never runs on PRs, so forks never see its secrets.

### CI and release

- **Every workflow:** actions pinned by SHA with a version comment; `permissions: contents: read`, raised per job with a
  reason; `persist-credentials: false`; `timeout-minutes`; only PR runs cancelled by a newer one.
- **`ci.yml`** on PRs, `main`, daily (auto-merged Dependabot PRs start no push run) and dispatch:
  - `Checks (Linux)`: the gate's quick lane, gitleaks installed against its SHA-256;
  - `Build and test (Linux)` and `Build and test (Windows)`: locked restore, build, tests with coverage and the gate, pack with
    package validation;
  - from S8, `Native AOT (Linux)`;
  - `CI`, the required check, fails when any job above failed.
- **`pr-text.yml`:** `PR text` runs the gate's `text` lane on the title and description; it passes on Dependabot's PRs.
- **`codeql.yml`:** csharp and actions, `security-extended`, on PRs, `main` and weekly.
- **`dependabot.yml`:** nuget, github-actions and dotnet-sdk; weekly; a 7-day cooldown; minor and patch grouped.
  `dependabot-auto-merge.yml` turns on auto-merge for minor and patch once `CI` and `PR text` pass; a major waits for
  the maintainer.
- **`live.yml`:** S11.
- **`release.yml`,** on a `v*` tag, one per branch:
  1. **Verify:** the tag is on the line's branch (`release/1.x` for `v1.*`, `main` otherwise); the version equals the
     tag (the project's `<Version>` on 1.x, MinVer on main); CHANGELOG has the dated section.
  2. **CI:** a green `ci.yml` run on the tagged commit.
  3. **Pack:** nupkg and snupkg, package validation.
  4. **Publish:** environment `nuget`, trusted publishing through `NuGet/login`, push both.
  5. **GitHub release:** the CHANGELOG section as the notes, the packages attached.

### Not chosen

| Option | Why not |
|---|---|
| Keep `XmlSerializer` and pass IL2026 and IL3050 to callers | It can never be AOT safe (D7) |
| Internal generated DTOs behind hand-written public models (F23's first option) | 296 types by hand, and they drift; generated public records, reviewed as diffs, stay current |
| xsd.exe or XmlSchemaClassGenerator | Both emit `XmlSerializer` classes; xsd.exe is Windows only |
| A Roslyn source generator over the xsd | Its output is not committed, so a schema change is not reviewed in a diff |
| An exception hierarchy (F35) | D8: one type, with `Kind` |
| Interfaces for the sub-clients | Adding a member in a minor breaks every implementer |
| PublicAPI analyzer files | Package validation, plus the committed generated file, show every change; WriteableFileProviders dropped them too |
| Strict LUID checks in 1.2.6 | Upload session ids are not LUIDs |
| Following same-origin redirects in 1.2.6 | More code in a patch; following none is safe and clear |
| A temporary private fork for 1.2.6 | Actions do not run there (to verify); the public text carries no walkthrough, and the window between PR and package is kept short |
| Vizportal as a separate package | D3, and D5 rules out new `Tableau.*` ids |
| WireMock.Net | Hand-written fakes give raw request lines and run on net48 |
| Multi-targeting netstandard2.0 in 2.0 | D6 |

## Compatibility

**1.2.6** keeps every 1.2.5 signature and the target. What changes for a caller is in its CHANGELOG (above): escaped or
refused inputs, double encoding of pre-encoded filters, failed redirects, any 2xx accepted, a time outside a day
refused.

**2.0.0** breaks every call site. The ledger, kept in `docs/migration-2.0.md` from S5:

| Area | Change | How to move over |
|---|---|---|
| Target | net10.0 only (was netstandard2.0) | .NET Framework and older stay on 1.2.x |
| Client | `TableauApiClient` and its 142 methods become `TableauClient` and sub-clients; the session is held by the client | The name map (one row per 1.x method), built by each port step |
| Sessions | `TableauSession` becomes an immutable record; sign-in through credentials | Configure credentials, or use `client.Sessions` |
| Models | The xsd types (`workbookType`) become generated records (`Workbook`) with nullable, typed members | The name map; `*Specified` flags become nullable values |
| Exceptions | `TableauRequestException`, `TableauApiVersionException` and `TableauOnlineNotSupportedException` become `TableauException` with `Kind` | Catch `TableauException` and switch on `Kind` |
| Tokens | `CancellationToken?` becomes `CancellationToken cancellationToken = default`, last | Pass `default`, not `null` |
| Lists | Never null; `ListAsync` and `Page<T>` replace the tuples | `await foreach`, or `ListPageAsync` |
| Downloads | `TableauDownload`, forward-only | Copy to a file or stream; do not seek |
| Behaviour | Gates throw before sending; redirects only to the same origin; plain http refused; any 2xx succeeds | Set `AllowInsecureHttp` or `AllowUnsupportedServerVersion` where meant |
| TLS switch | `IgnoreSSLErrors` becomes `DangerousAcceptAnyServerCertificate` | Rename |
| Vizportal | Never shipped in 1.x. On main it moves to `client.VizPortal`, with tokens and `int` ids | The guide's Vizportal page |
| Dependencies | Newtonsoft.Json and `Microsoft.Extensions.Logging` leave. Added: `Microsoft.Extensions.Logging.Abstractions`, `.Http`, `.Http.Resilience`, `.Options` and `.DependencyInjection.Abstractions` (10.x) | None |

**One-way doors from 2.0.0** (AGENTS.md's list, from S5 and finished in S18):

- the package id;
- the target framework policy;
- the version scheme and the 1.x support statement;
- the public API: types, members, option names, configuration keys, DI methods;
- `TableauException` and its kinds;
- the model naming rules, since a regeneration changes the public API;
- the Vizportal surface;
- the supported Tableau floor.

## Steps

| Step | Line | What | Closes on main | Issue | PR | Status |
|---|---|---|---|---|---|---|
| S0 | main | Harness and licence | (the licence part of F25) | #1 | #3 | done |
| S1 | main | CI and open-source files | F25 | | | todo |
| S2 | 1.x | 1.2.6 security patch | none on main; 1.2.6 column of the tracker | | | todo |
| S3 | main | Security on main | F1 F2 F3 F6 F39 | | | todo |
| S4 | main | Publish the review, after 1.2.6 | none | | | todo |
| S5 | main | Build and layout for 2.0 | F24 F40 | | | todo |
| S6 | main | Reformat, alone | none | | | todo |
| S7 | main | Test platform and coverage | F26 F28 | | | todo |
| S8 | main | Models, generator, AOT | F14 F23 F50 | | | todo |
| S9 | main | Client core | F4 F16 F18 F20 F21 F22 F35 F36 F37 F38 F46 F47 F48 | | | todo |
| S10 | main | Resilience and downloads | F17 F19 F42 | | | todo |
| S11 | main | Sessions and the live suite | F27 F34 | | | todo |
| S12 | main | Port: server, sites, projects, users, groups, favorites; paging and filters | F13 F33 | | | todo |
| S13 | main | Port: permissions, tags, metadata (REST and GraphQL) | F8 F44 | | | todo |
| S14 | main | Port: workbooks, views, data sources, revisions | F5 F7 F32 | | | todo |
| S15 | main | Port: jobs, tasks, schedules, subscriptions, Server and Cloud | F9 F10 F11 F12 F15 F31 | | | todo |
| S16 | main | Vizportal | F29 F41 F49 | | | todo |
| S17 | main | Publishing, and the last old code | F30 F43 F45 | | | todo |
| S18 | main | Launch prep and 2.0.0-rc.1 | none | | | todo |
| S19 | main | 2.0.0 | none | | | todo |

### S0. Harness and licence (#1, done in #3)

- **Goal:** the repo has the rules, the gate and the licence that every later step works under.
- **Scope:**
  - AGENTS.md, REVIEW.md, the skills, `.claude/` settings;
  - `eng/gate.cs` with the lanes quick, full, text, selftest and hooks, and `.githooks/`;
  - `.gitleaks.toml`, `.editorconfig`, `Directory.Build.props`, `global.json`, `nuget.config`;
  - CODEOWNERS, the PR template, `docs/plans/README.md` (the plan template);
  - LICENSE (MIT, "the Tableau.Api contributors", D4);
  - `.local/` in `.gitignore`, where the review waits (S1 adds it if #1 has merged).
- **Findings:** none closed; it delivers F25's licence.
- **Tests:** the gate's selftest; gate quick and full pass on the branch; the hooks refuse what AGENTS.md forbids.
- **One-way doors:** the licence (D4, decided).
- **Done when:** #1's PR is merged with its review rounds clean, and gate quick passes on `main`.

### S1. CI and open-source files

- **Goal:** every PR on `main` is checked by CI and by the PR-text check. The repo has what users and contributors
  expect, and the package metadata is true.
- **Scope:**
  - **Workflows:** `ci.yml` (Design; until S3 adds tests it builds and packs the library only, since the old test
    project cannot build), `pr-text.yml`, `codeql.yml`, `dependabot.yml`, `dependabot-auto-merge.yml`.
  - **Issue forms:** bug (Tableau.Api version, Server or Cloud, Tableau and REST versions), feature (endpoint, docs
    link), and a `config.yml` that sends security reports to private reporting.
  - **SECURITY.md:**
    - supported versions: 2.x all fixes once released; 1.2.x security fixes only (Q1); older none;
    - report through private vulnerability reporting;
    - fixes are described by what they change until the fixed package ships.
  - **The other files:** CONTRIBUTING.md (a short path to AGENTS.md; contributions under MIT); CHANGELOG.md (Keep a
    Changelog, `Unreleased`); README.md (the disclaimer first; status: 1.2.x on nuget.org, 2.0 in progress on `main`,
    not for use yet; licence; "Tableau is a trademark of Salesforce, Inc.").
  - **Package metadata** in `Tableau.Api.csproj`: a true description ending in the disclaimer, MIT, GitHub URLs, the
    packed README, `snupkg`, no `IncludeSource`, `ContinuousIntegrationBuild` in CI. S5 moves it to
    `src/Directory.Build.props`.
  - **Old tests:** the profane project description and "Preparing Azure VM" replaced in the old test project.
- **Findings:** F25.
- **Tests:**
  - CI green on its own PR;
  - the gate's selftest covers the text lane on a clean and on a refused description;
  - `dotnet pack` holds the licence expression, the README and a snupkg.
- **One-way doors:** the package description's wording (D5) and SECURITY.md's support statement (D6, Q1).
- **Done when:**
  - merged;
  - `CI` and `PR text` are required checks on `main`'s ruleset;
  - private vulnerability reporting is on (the maintainer's).

### S2. Security patch 1.2.6 (release/1.x)

- **Goal:** Tableau.Api 1.2.6 on nuget.org: 1.2.5's API on netstandard2.0, with the fixes of the 1.2.6 table.
- **Scope:**
  - **The branch:** `release/1.x` pushed at `b23503e`; this step's branch is cut from it, and its PR targets it.
  - **Library:**
    - `TableauApiClient.cs`: the path handler, query building, redirects, the token per request, any 2xx, the base
      path;
    - the area files' path and query call sites;
    - `TableauApiClient_WorkbooksAndViews.cs` (filters), `_Revisions.cs`, `_JobsTasksSchedules.cs`,
      `_Authentication.cs` and `Models/TableauSession.cs`;
    - `Models/XmlSchema.cs` (enum values);
    - `Tableau.Api.csproj` (version and metadata).
  - **New:** `Tableau.Api.Tests/` replaced by the offline suite (MSTest 4 on Microsoft.Testing.Platform, net10.0 and
    net48), the solution updated, and the branch files listed under Design.
- **Findings (on 1.x):** F2, F3, F5 (data, image, PDF without filters), F6, F7, F10, F11, F12, F14, F15, F27, F39.
  None closes on `main`.
- **Tests,** offline, the library driven against `FakeTableauServer` on Linux (net10.0) and Windows (net10.0 and net48):
  - **Paths:** data rows for every method that takes an id or a name, with dot segments, escaped dots, `#`, `?`, `/`,
    `%`, spaces and an upload id with `:`. Each either throws `ArgumentException` before any request, or sends exactly
    the escaped request line. No request line ever has a dot segment.
  - **Redirects:** 301, 302, 307 and 308 to a second fake server on another port. The call fails and that server
    records nothing, sign-in included.
  - **Queries:** `R&D`, `C++`, `#` and `%` in filter, sort and view-filter names, escaped.
  - **View filters:** `vf_` on data and image; PDF without filters works.
  - **The other fixes:**
    - revision paging sent;
    - schedule create on 201, its time as `18:30:00` under a non-UTC time zone, `monthDay` sent, 24:00 refused;
    - task lists with tasks and with `<tasks/>`; the task path;
    - a users page with `OpenID` parses;
    - subscription create on 200 and on 201;
    - `UserName` after each sign-in;
    - a base URL with `/tableau/`.
  - **Logs:** the suite's log lines hold no password, token or PAT secret.
- **One-way doors:** the version 1.2.6 (D1, decided). The behaviour changes are written in the CHANGELOG.
- **Done when:**
  - merged into `release/1.x` with CI green and `review-code` clean, its records in `.local/review/`;
  - a live smoke run by hand passes and is recorded in the PR. With a PAT on the Developer Program site: sign in, list
    projects with a filter containing `&`, chunked publish of a small data source, download, delete;
  - `v1.2.6` is tagged on `release/1.x`;
  - the maintainer approves the `nuget` deployment;
  - nuget.org lists 1.2.6 with MIT, the README and the disclaimer;
  - the GitHub release says what changed.

### S3. Security on main

- **Goal:** `main` no longer accepts any certificate on Vizportal, and has 1.2.6's path, query and redirect rules,
  before the rebuild replaces that code.
- **Scope:** `Tableau.Api/VizPortal/VizPortalClient.cs`, `Tableau.Api/TableauApiClient.cs` and the area files' call
  sites; a new `tests/Tableau.Api.Tests/` (net10.0) with S2's request-building tests adapted to main; `ci.yml` runs it.
- **Findings:** F1, F2, F3, F6, F39.
- **Tests:**
  - S2's path, query, redirect and base-path rows, on main's methods;
  - Vizportal against a TLS fake with an untrusted certificate fails, and with `IgnoreSSLErrors` set it connects;
  - no Vizportal or REST request follows a redirect to another port.
- **One-way doors:** none (`main` is unreleased).
- **Done when:** merged with CI running the new tests. It may merge while S2's release waits for its approval; until
  1.2.6 ships, its review records stay in `.local/review/`.

### S4. Publish the review

- **Goal:** the review, the research and the security review records become public, once 1.2.6 is on nuget.org.
- **Scope:**
  - `.local/review/` becomes `docs/reviews/2026-10-28-codebase.md`, `docs/reviews/2026-10-28-research.md` and S2's and
    S3's review-code records, with local paths replaced by a note;
  - this plan's source links;
  - main's CHANGELOG gets the 1.2.6 entry.
- **Findings:** none.
- **Tests:** gate quick; gitleaks over the new files.
- **One-way doors:** none.
- **Done when:** merged after 1.2.6 is listed on nuget.org. The maintainer publishes the advisory (For the
  maintainers).

### S5. Build and layout for 2.0

- **Goal:** `main` builds a net10.0 2.0 package under strict settings, versioned from tags, with package validation
  listing every break against 1.2.5.
- **Scope:**
  - the layout under Design: `.slnx`, `src/Tableau.Api/`, `tests/`, `Directory.Packages.props` with lock files,
    `src/Directory.Build.props`;
  - the strictness and the `TableauLegacy` ratchet; the `NoWarn` list goes;
  - `XmlSchema.cs` marked generated in `.editorconfig`;
  - MinVer (prefix `v`, minimum 2.0);
  - package validation with `CompatibilitySuppressions.xml`;
  - `docs/migration-2.0.md` started with the six signatures `main` already broke (F24) and the target change;
  - AGENTS.md's one-way doors.
- **Findings:** F24, F40.
- **Tests:**
  - CI builds and packs `Tableau.Api.2.0.0-alpha.0.N` for net10.0;
  - package validation runs, and only the reviewed suppressions pass;
  - S3's tests pass on net10.0;
  - a deliberate CA2021 cast fails the build outside the legacy section (gate selftest).
- **One-way doors:** target framework (D6), version scheme (D2), package id (D5), all decided; AGENTS.md records them.
- **Done when:** merged, with the suppression file reviewed.

### S6. Reformat, alone

- **Goal:** the hand-written code follows `.editorconfig`, in one commit that blame can skip.
- **Scope:** `dotnet format` (whitespace and style) over `src/` and `tests/`, generated code excluded. Nothing else.
- **Findings:** none.
- **Tests:**
  - build and tests unchanged;
  - the public API dump unchanged;
  - `dotnet format --verify-no-changes` passes after it;
  - `git diff -w` shows only style edits.
- **One-way doors:** none.
- **Done when:** merged as one squash commit, its SHA in the progress log.

### S7. Test platform and coverage

- **Goal:** a fast offline suite on every PR, a coverage floor that only rises, culture and time-zone legs, and the old
  live tests replaced.
- **Scope:**
  - `tests/Directory.Build.props`;
  - `tests/Tableau.Api.Tests/`: the fakes, the fixtures folder and manifest, the log capture, the `Live` and
    `ServerOnly` categories;
  - `eng/coverage-gate.cs` and `eng/coverage.json`;
  - the CI test jobs and the culture and time-zone legs;
  - the old `Tableau.Api.Tests/` deleted, with the map below;
  - `.git-blame-ignore-revs` with S6's commit;
  - the PR template's question on Server-only tests.
- **Findings:** F26, F28.
- **The old live tests' coverage:**

  | Old calls | Replaced by |
  |---|---|
  | `SignIn` (password), `SwitchSite` | offline (S11) and `ServerOnly` by hand |
  | `SignOut` | offline and live (S11) |
  | `CreateProject`, `QueryProjects` | offline and live (S12) |
  | `QueryDataSources`, `QueryDataSource` | offline and live (S14) |
  | `InitiateFileUpload`, `AppendToFileUpload`, `PublishDatasource` | offline and live (S17) |

- **Tests:**
  - the suite under all legs;
  - the coverage gate fails a run below the floor (its selftest);
  - a canary test on each leg asserts that its culture and time zone are in effect.
- **One-way doors:** none.
- **Done when:** merged; CI runs the suite and the gate; the floors are set from the first green run.

### S8. Models, generator, AOT

- **Goal:** the wire models are generated from `ts-api_3_29.xsd` as source-generated JSON, tolerate values Tableau
  adds, and are AOT safe.
- **Scope:**
  - `eng/schema/` and `eng/generate-models.cs`;
  - `src/Tableau.Api/Models/Generated/`, `TableauJsonContext` and the generated converters;
  - `IsAotCompatible` and `VerifyReferenceAotCompatibility`;
  - `tests/Tableau.Api.Aot/` and its CI job;
  - the gate's generator check;
  - synthetic fixtures for credentials, server info, error, pagination, site, user, project and workbook.
- **Findings:** F14, F23, F50.
- **Tests:**
  - two generator runs give identical bytes, and check mode fails on an edited file;
  - every fixture round-trips;
  - an unknown enum value parses and keeps its text;
  - empty, single-item and many-item lists;
  - numbers sent as strings;
  - the AOT job publishes with no warning and its checks pass.
- **One-way doors:** the public model shape (names, nullability, list types, the extensible enum pattern): the
  maintainer's yes on the issue.
- **Done when:** merged with that yes; the AOT job and the generator check run in CI.

### S9. Client core

- **Goal:** the 2.0 client exists: options, DI and typed clients, one pipeline with the HTTP rules, one exception type,
  safe logging, and version and deployment gates that work.
- **Scope:**
  - `TableauClient`, `TableauClientOptions` and their validation;
  - `AddTableauClient` and `TableauClient.Create`;
  - the pipeline (S3's path handler, the query builder, redirects, TLS, auth header);
  - `Endpoint` descriptors and the registry; version negotiation and deployment detection;
  - `TableauException`; the logging;
  - `client.Server.GetInfoAsync` as the first ported call, with its old method deleted;
  - the dependency changes;
  - a conventions test over every public method: the `Async` suffix, the token last, the return shapes.
- **Findings:** F4, F16, F18, F20, F21, F22, F35, F36, F37, F38, F46, F47, F48. S3's tests for F2, F3, F6 and F39 move
  onto the new pipeline.
- **Tests:**
  - **Gates:** one row per descriptor, below its version and on the wrong deployment, throwing and sending nothing; the
    floor, with and without the opt-in.
  - **Redirects:** a same-origin redirect followed; another origin, and https to http, refused, with nothing reaching
    the target.
  - **TLS and http:** an untrusted certificate refused; the opt-out connects and logs its warning; http refused
    without `AllowInsecureHttp`.
  - **Request shape:** escaping rows, the base path, any 2xx.
  - **Errors:** a Tableau error body, an HTML body cut to 4 KB, an empty body, a missing element.
  - **Logs:** no secret, and no query value at Debug.
  - **DI:** keyed and default registration; options refused at start-up.
  - **Conventions:** the test above.
  - **Live:** Server Info on the Developer Program site.
- **One-way doors:** the client API shape (sub-clients, verbs, options, DI methods, `TableauException`, the mocking
  pattern) and the supported floor (D8, Q4): the maintainer's yes on the issue.
- **Done when:** merged with that yes.

### S10. Resilience and downloads

- **Goal:** retries that are safe, 429 honoured, long transfers that do not time out, and downloads that stream.
- **Scope:** the resilience pipeline for DI and the factory; its options; the descriptor in the request options;
  `TableauDownload`; the 429 body parser.
- **Findings:** F17, F19, F42.
- **Tests:**
  - **Retries:** a GET retried after 503 succeeds; a POST is not retried.
  - **429:** with `Retry-After` it waits on a fake `TimeProvider`; over the cap it throws `RateLimited` with
    `RetryAfter`; the body hint without the header.
  - **Timeouts:** the transfer timeout is separate from the attempt timeout; a timeout is not the caller's
    cancellation.
  - **Downloads:** 5 MB arrives as a stream without buffering and stops on the token; a missing Content-Type works; a
    `..\` file name is cleaned; the zip signature is detected.
- **One-way doors:** `TableauDownload` and the resilience option names: the maintainer's yes on the issue.
- **Done when:** merged with that yes.

### S11. Sessions and the live suite

- **Goal:** one session manager for every sign-in, the explicit session API, and a nightly live suite.
- **Scope:**
  - `TableauCredentials` and the DI builder methods;
  - the session manager and `TableauSession`;
  - `client.Sessions` and `client.WithSession`; the old authentication methods deleted;
  - `live.yml`, the live settings (`TABLEAU_LIVE_*` variables), the run project and its sweep;
  - the recording handler.
- **Findings:** F27, F34.
- **Tests:**
  - **Offline:**
    - the sign-in body for each credential kind;
    - the direct-trust JWT decoded and checked: header, claims, `exp` within 10 minutes, a new `jti` each time;
    - the UAT flag; impersonation refused on Cloud before sending;
    - 20 concurrent first calls make one sign-in;
    - a 401 signs in again once and resends;
    - expiry signs in again first;
    - dispose signs out;
    - `ToString` has no token.
  - **Live:** sign in with direct trust, call, sign out; a bound session used across two sub-clients.
  - **ServerOnly, by hand:** password with impersonation, Switch Site.
- **One-way doors:** the credentials and session API: the maintainer's yes on the issue.
- **Done when:**
  - merged with that yes;
  - the maintainer has set up the site, the app and `tableau-live` (Q2);
  - the first nightly run is green.

### S12. Port: server, sites, projects, users, groups, favorites

- **Goal:** these areas on the 2.0 client, with paging and the filter and sort builder.
- **Scope:**
  - their sub-clients; `Page<T>`, `ListAsync`, `ListPageAsync`, `TableauFilter`, `TableauSort` and typed fields;
  - the old Sites, Projects, UsersAndGroups and Favorites partials deleted, with `DeleteProject2`;
  - live fixtures; the migration name map's rows.
- **Findings:** F13, F33. Update User also gains the documented `email`, `idpConfigurationId`, `language` and `locale`.
- **Tests:**
  - **Every method:** verb, path, query, golden body and status; fixture parse; an empty list gives `[]`; the gate
    rows.
  - **Paging:** stops at `TotalAvailable` and on a short page; cancellation stops it.
  - **Filters:** escaping rows (`R&D`, `C++`, `#`, `%`, in-lists).
  - **Live:** create, list, update and delete a project, a group and a user in the run's scope.
  - **ServerOnly, by hand:** sites create and delete; Active Directory groups.
- **One-way doors:** the paging and filter API, the first use of the patterns later steps copy: the maintainer's yes.
- **Done when:** merged with that yes; the live suite covers the area.

### S13. Port: permissions, tags, metadata

- **Goal:** one permissions API and one tags API over every content type, the REST metadata calls with paging, and the
  Metadata API (GraphQL).
- **Scope:**
  - **`client.Permissions`:** get, add and delete for projects, workbooks, views, data sources, flows, virtual
    connections, collections, databases and tables, each gated by its version, plus the default permissions.
  - **`client.Tags`:** add and delete on content, tables and columns; batch add and batch delete.
  - **`client.Metadata`:** databases, tables and columns (list with paging, update); data quality warnings (add,
    update, query, delete by content, delete by id); `QueryAsync` for GraphQL with a `JsonTypeInfo<T>` overload, a raw
    `JsonElement` overload, and a cursor-paging helper.
  - The old Permissions and Metadata partials and the tag methods deleted, except `AddWorkbookToSchedule` (S15).
- **Findings:** F8, F44 (the four NuGet_Prod methods, with escaped tags, a request type outside the generated file, and
  their siblings).
- **Tests:**
  - request shape for every content kind, from one table, with gates;
  - the batch tag bodies against the documented ones;
  - a DELETE with a body is sent as documented;
  - database and table paging;
  - GraphQL paging over a cursor fixture;
  - live: permissions and tags on a run project, a GraphQL query.
- **One-way doors:** the generic content reference and the metadata API: the maintainer's yes.
- **Done when:** merged with that yes.

### S14. Port: workbooks, views, data sources, revisions

- **Goal:** these areas on the 2.0 client, with one view-filter builder, revision paging and streamed downloads.
- **Scope:**
  - their sub-clients;
  - exports (data, image, PDF, crosstab) through one options type with view filters, `maxAge`, size and orientation
    (one orientation enum);
  - downloads through `TableauDownload`;
  - updates from options records;
  - the old WorkbooksAndViews, Datasources and Revisions partials deleted.
- **Findings:** F5, F7, F32.
- **Tests:**
  - `vf_` on every export from one helper;
  - revision paging;
  - an update sends no empty nested object (golden bodies);
  - downloads stream;
  - live: list, get and download a published sample; export view data, an image and a PDF.
- **One-way doors:** the export options type: the maintainer's yes.
- **Done when:** merged with that yes.

### S15. Port: jobs, tasks, schedules, subscriptions

- **Goal:** the job and task calls on both deployments, the Cloud schedule model from 3.20, and Server schedules that
  work.
- **Scope:**
  - **Jobs:** get, list with filters, cancel, `WaitForJobAsync` with back-off and a timeout.
  - **Extract refresh tasks:** get, list, run, delete, on both payloads; Create and Update Cloud Extract Refresh Task
    (Cloud, 3.20).
  - **Server schedules** (Server only): create, update with PUT, delete, list, the tasks in a schedule; Add Workbook and
    Add Data Source to Server Schedule (2.8).
  - **Subscriptions:** the Server form (schedule id) and the Cloud form (frequency).
  - **`CloudSchedule`:** the frequency model shared by tasks and subscriptions.
  - The old JobsTasksSchedules and Subscriptions partials and `AddWorkbookToSchedule` deleted.
- **Findings:** F9, F10, F11, F12, F15, F31.
- **Tests:**
  - golden bodies for both forms;
  - `HH:mm:ss` on the non-UTC leg; `monthDay`; 201 and 200;
  - Server-only calls refused on Cloud and Cloud-only calls on Server, before sending;
  - `WaitForJobAsync` on a fake `TimeProvider`;
  - **Live (Cloud):** create, update and delete a Cloud extract refresh task and a subscription. Whether the
    Developer Program site allows them is to verify.
  - **ServerOnly, by hand:** schedules, and adding content to a schedule.
- **One-way doors:** `CloudSchedule` and the schedule API: the maintainer's yes.
- **Done when:** merged with that yes.

### S16. Vizportal

- **Goal:** Vizportal on the shared core, fixed, documented as unofficial.
- **Scope:** `client.VizPortal` and its models (Design); Newtonsoft.Json removed; the old `VizPortal/` folder replaced;
  the guide page; `[Experimental]` if Q3 says so.
- **Findings:** F29, F41, F49.
- **Tests:**
  - request bodies camelCase and compact, tags quoted by the serialiser;
  - paging by items returned, with the caller's query unchanged and two concurrent enumerations independent;
  - the token stops paging; unknown enum values parse;
  - the cookie is `Secure` on https only;
  - TLS and redirect rows on the Vizportal path;
  - live on Cloud if the token opens a session there, else `ServerOnly` by hand. The versions tested go into the docs.
- **One-way doors:** the Vizportal surface: the maintainer's yes.
- **Done when:** merged with that yes and the tested versions written down.

### S17. Publishing, and the last old code

- **Goal:** publishing by size, as a job, with progress and cancellation; the old client gone.
- **Scope:**
  - `PublishAsync` on workbooks and data sources;
  - the `XmlWriter` payload, and the response reader if needed;
  - the bounded chunk reader;
  - the old Publishing partial, `TableauApiClient`, `PartialStream`, `XmlSchema.cs`, `SchemaExtensions` and the 1.x
    enums, exceptions and attributes deleted;
  - the AOT app's publish path.
- **Findings:** F30, F43, F45 (its endpoint list goes to the steps and follow-ups in the Research tracker).
- **Tests:**
  - one request at 64 MB and below, chunks above (set small in tests);
  - a non-seekable source;
  - progress reports;
  - cancellation abandons the upload;
  - `asJob` returns a job;
  - golden payloads;
  - live: publish and delete a small workbook and data source, and one over a chunk.
- **One-way doors:** the publish API: the maintainer's yes.
- **Done when:** merged with that yes; no file of the 1.x client remains.

### S18. Launch prep and 2.0.0-rc.1

- **Goal:** 2.0.0-rc.1 on nuget.org with complete docs, after a final whole-repo review.
- **Scope:**
  - **Docs:** the README rewritten (disclaimer first, install, quick start, Server and Cloud, Vizportal caveat,
    versioning and support); `docs/guide/` (sign-in and sessions, paging and filters, downloads and publishing,
    errors and resilience, DI and options, AOT, Vizportal); the migration guide's final pass; CHANGELOG; AGENTS.md's
    one-way doors final.
  - **Release:** main's `release.yml`; `docs/releasing.md`.
  - **Review:** a deep `review-code` of the whole repo (`docs/reviews/`) and its fixes.
  - **Checks:** the legacy section empty, every floor at its target.
  - **Tag:** `v2.0.0-rc.1`.
- **Findings:** none new; the final review's go into this plan.
- **Tests:**
  - every suite;
  - the AOT job;
  - package validation with every suppression in the guide;
  - the nightly live suite green on the rc's commit;
  - the `ServerOnly` suite run by the maintainer on a Server.
- **One-way doors:** the version `2.0.0-rc.1` (D2) and the 1.x end date (Q1): the maintainer's yes.
- **Done when:** the rc is on nuget.org after the maintainer approves the deployment.

### S19. 2.0.0

- **Goal:** 2.0.0 on nuget.org.
- **Scope:** CHANGELOG dated; SECURITY.md's table for 2.x; the tag `v2.0.0`; the follow-up issues checked against the
  Research tracker.
- **Findings:** none.
- **Tests:** the same as S18, on the tagged commit.
- **One-way doors:** the version 2.0.0: the maintainer's yes.
- **Done when:**
  - 2.0.0 is on nuget.org after the approval;
  - the GitHub release is out;
  - the Later items are opened as issues.

## Tests

| Suite | Runs | Where |
|---|---|---|
| 1.x offline | PRs to `release/1.x` | Linux (net10.0), Windows (net10.0 and net48) |
| Offline unit (`FakeTableauHandler`) | every PR, `main`, daily | Linux and Windows |
| Pipeline (`FakeTableauServer`, with TLS) | every PR | Linux and Windows |
| Culture and time zone | every PR | Linux: sv-SE, tr-TR, a non-UTC zone |
| Native AOT | every PR, from S8 | Linux |
| Coverage gate | every PR, from S7 | Linux and Windows |
| Live (`Live`) | nightly and on dispatch, `main` only, from S11 | the Developer Program site (Cloud) |
| Server only (`ServerOnly`) | by hand; each PR that touches a Server-only call says whether it ran | a Tableau Server the maintainer has |

## Docs

- **Repo files:** README, SECURITY.md, CONTRIBUTING.md and CHANGELOG.md (S1, updated in S2, S4, S18 and S19); LICENSE
  (S0).
- **Guides:** `docs/migration-2.0.md` (from S5, a section per step); `docs/guide/` (S18); `docs/releasing.md` (S18).
- **XML docs:** on every public member; CS1591 is an error outside the legacy section from S5.
- **Harness:** AGENTS.md's one-way doors (S5, S18), its tests section (S7, S11), the layout (S5).
- **The 1.x line:** its own README and CHANGELOG (S2).

## Risks

| # | Risk | Status |
|---|---|---|
| R1 | Tableau's JSON shape is undocumented: wrappers, single items, numbers as strings, envelopes | Mitigated: tolerant converters, fixtures recorded live per area; Server-only fixtures stay synthetic until recorded by hand |
| R2 | A multipart publish may not answer in JSON | Mitigated: `XmlReader` for the four publish responses, decided in S17 on the live site |
| R3 | The Developer Program site can be wiped, can run a pre-release Cloud, and lapses unless renewed | Accepted: a red night is triaged as Tableau's or ours; the run prefix and sweep; the maintainer renews |
| R4 | A REST token may not open a Vizportal session on Cloud | Checked in S16; if not, Vizportal is Server only and the docs say so |
| R5 | 1.2.6's escaping breaks callers who pre-encoded filters or relied on raw paths | Accepted: written in the CHANGELOG; the new behaviour is what the docs describe |
| R6 | 1.2.6's code is public on the PR before the package is on nuget.org | Accepted: PR, review, merge, tag and approval kept close together; the text says what changes, nothing more |
| R7 | Upload session ids are not LUIDs and contain `:` | Resolved: the path rule keeps `:`; strict LUID checks only where a descriptor says so |
| R8 | Server-only features never run in CI | Accepted (D9): by hand, and the PR says so |
| R9 | MIT needs the agreement of everyone whose code it covers | Q6 |
| R10 | The trademark guidelines discourage "Tableau" in a product name; the id has been used since 2018 | Accepted (D5): the disclaimer; a fallback id is ready if Salesforce objects |
| R11 | One PAT allows one session, so two sign-ins end each other | Mitigated: one sign-in at a time per client, direct trust in the live suite, and a docs warning against sharing a PAT between processes |
| R12 | The suppression file lists most of 1.2.5's API | Accepted: reviewed per PR, grouped in the guide, deleted after 2.0.0 (Later) |
| R13 | Cloud rate limits on the nightly run | Accepted: a small serial suite, far under 40,000 calls an hour |
| R14 | Versions below the floor are allowed on opt-in but never tested | Accepted: the docs say so |
| R15 | The work spans many sessions, and context is lost between them | Mitigated: this file is the record, updated by every step |

## Open questions

| # | Question | Recommendation |
|---|---|---|
| Q1 | Until when does 1.2.x get security fixes? SECURITY.md needs a date | 12 months after 2.0.0 ships, written as a date in S19; until then "security fixes only" |
| Q2 | "A reviewed environment": should `tableau-live` require reviewers? Then every nightly run waits for an approval | Deployment branch `main` only and no required reviewers, as WriteableFileProviders' `live-cloud`; dispatch needs write access anyway |
| Q3 | Mark the Vizportal API `[Experimental]`, so callers acknowledge it with a diagnostic id? | Yes: it states the risk in the compiler and costs callers one `NoWarn` |
| Q4 | The exact floor: the research's support dates are unverified | S9 checks Tableau's published support dates; expected REST 3.23 (Server 2024.2) and Cloud; your yes on S9's issue |
| Q5 | 1.2.6 scope. Beyond the review's list, 1.2.6 also takes F10's monthly day (the parameter exists in 1.2.5), every 3.29 enum value for F14, and the fix for `QueryViewPDF` without filters. F17's Content-Type check, F28 and F32 also qualify, and are left out | Keep those three for 2.0, so the patch stays small and ships sooner |
| Q6 | Starschema (named as Company in 1.2.5, co-owner on nuget.org) and László Nemes (most of Vizportal) hold rights in the code now licensed MIT | Answered 2026-10-28: László Nemes agreed to MIT (recorded on #1); serionist confirmed the code is cleared for release as open source when the repo went public |

## Later

- **The research's P2 and P3 features,** one follow-up issue each (Research tracker), opened once this plan is approved
  and labelled for after 2.0.
- **`ts-api_3_30.xsd`** once published: one generator run, reviewed as a diff, in a minor.
- **Package validation baseline** moved to 2.0.0 after it ships, and `CompatibilitySuppressions.xml` deleted.
- **netstandard2.0 for 2.x** only if users ask; adding it later is a minor (D6).
- **Telemetry:** an `ActivitySource` and a `Meter` (P3.21).
- **A fallback package id,** ready in case Salesforce objects (RD6).

## Finding tracker

Status: `todo`, `done` (with the PR) or `wontfix` (with the reason). No finding is `wontfix`.

- **Step:** the one step that closes the finding on `main`. Its fix and the test or analyzer that guards it land there;
  later steps are built to that guard.
- **1.2.6:** whether S2 fixes it on the 1.x line too; n/a when 1.2.5 does not have it. A "no" is final: 1.x takes no
  signature change (D1), then security fixes only (D6).

| F | What closes it | Step | 1.2.6 | Status |
|---|---|---|---|---|
| F1 | Vizportal validates TLS certificates, with the same opt-out as REST | S3 | n/a | todo |
| F2 | Every path segment validated and escaped: ids, tag names, site names | S3 | yes | todo |
| F3 | Credentials and bodies never go to another host on a redirect; auth on each request | S3 | yes | todo |
| F4 | Version and deployment gates that run, from descriptors, without reflection | S9 | no | todo |
| F5 | View filters with `vf_` on every export, from one helper; `maxAge` | S14 | yes: data, image, and PDF without filters | todo |
| F6 | Every query key and value escaped; filter and sort escaped between separators | S3 | yes | todo |
| F7 | Revision lists send their paging | S14 | yes | todo |
| F8 | Database and table lists paged | S13 | no | todo |
| F9 | Update Schedule with PUT on the schedule's id | S15 | no; a known issue in the CHANGELOG | todo |
| F10 | Schedule create: 201, `HH:mm:ss`, a checked time, `monthDay` | S15 | yes | todo |
| F11 | Extract refresh task lists by item type, empty when empty | S15 | yes | todo |
| F12 | Get Extract Refresh Task on its documented path | S15 | yes | todo |
| F13 | Active Directory group updates carry the group id | S12 | no | todo |
| F14 | An unknown enum value never fails a response | S8 | yes: the missing values added | todo |
| F15 | Any 2xx is success; the Cloud subscription form | S15 | yes: any 2xx | todo |
| F16 | Server and Cloud modelled; no silent version cap; Cloud and Server gates | S9 | no | todo |
| F17 | Downloads streamed, under their own timeout, without a Content-Type crash, the response disposed | S10 | no | todo |
| F18 | One `HttpClient` per client, injectable, from `IHttpClientFactory`; disposable | S9 | no | todo |
| F19 | Retries for idempotent calls only; 429 and `Retry-After` honoured and exposed | S10 | no | todo |
| F20 | Lists never null; a missing element names the endpoint | S9 | no | todo |
| F21 | A `CancellationToken` last on every method, Vizportal included | S9 | no | todo |
| F22 | One naming scheme and return shape, guarded by the conventions test; the name map | S9 | no | todo |
| F23 | Models generated from a checked-in xsd by a repeatable generator; no hand edits | S8 | no | todo |
| F24 | Target, version and package validation set once; the breaks listed | S5 | no | todo |
| F25 | Licence, README, CONTRIBUTING, SECURITY, true package metadata | S1 | the 1.2.6 package gets the metadata | todo |
| F26 | Tests that build, run offline and cover the API | S7 | 1.x gets its own offline suite | todo |
| F27 | The session's user name set after sign-in | S11 | yes | todo |
| F28 | Invariant culture and ordinal comparisons, tested under sv-SE and tr-TR | S7 | no (Q5) | todo |
| F29 | The Vizportal schedule check sends camelCase | S16 | n/a | todo |
| F30 | Chunks read from any stream, asynchronously | S17 | no | todo |
| F31 | Add Workbook to Schedule returns the task; Add Data Source to Schedule | S15 | no; a known issue | todo |
| F32 | Updates omit nested elements the caller did not set | S14 | no (Q5) | todo |
| F33 | Update User sends the documented attributes | S12 | no | todo |
| F34 | A session lifecycle: expiry, signing in again, no token in `ToString` | S11 | no | todo |
| F35 | One exception type with the details (D8) | S9 | no | todo |
| F36 | Logging without secrets or noise; Abstractions only | S9 | no | todo |
| F37 | `ConfigureAwait(false)` on every await | S9 | no | todo |
| F38 | The accept-any-certificate switch renamed and warned about; plain http refused unless allowed | S9 | no | todo |
| F39 | The base URL's path kept | S3 | yes | todo |
| F40 | A strict build with no blanket `NoWarn`; nullable; the analyzers that matter as errors | S5 | no | todo |
| F41 | Vizportal pages by items returned and leaves the caller's query alone; tolerant models | S16 | n/a | todo |
| F42 | Download file name and kind from the header or the content, cleaned | S10 | no | todo |
| F43 | Dead code and duplicates gone with the old client | S17 | no | todo |
| F44 | The four NuGet_Prod metadata methods, escaped, with their siblings | S13 | no | todo |
| F45 | Publishing by size, as a job, with options; the listed gaps mapped in the Research tracker | S17 | no | todo |
| F46 | Immutable options and session; sub-clients made once | S9 | no | todo |
| F47 | Versions parsed as major.minor and compared whole; true messages | S9 | no | todo |
| F48 | No rewriting of response bodies | S9 | no | todo |
| F49 | Vizportal tags quoted by the serialiser, the cookie `Secure` by scheme, compact JSON | S16 | n/a | todo |
| F50 | Source-generated serialisation; `IsAotCompatible` with the AOT smoke test | S8 | no | todo |

## Research tracker

**Decisions.** The research numbered its own D1 to D9; here they are RD1 to RD9.

| # | The research recommended | Outcome | Step |
|---|---|---|---|
| RD1 | JSON through source generation, models from the xsd, AOT | Taken (D7) | S8 |
| RD2 | A client bound to its session, with an explicit session API | Taken (D8) | S9, S11 |
| RD3 | Drop Vizportal from 2.0.0 | Overruled (D3): kept and fixed; its REST replacements table goes into the Vizportal guide page | S3, S16 |
| RD4 | net10.0 only; 1.x security fixes until a date; 1.x marked Legacy once 2.0.0 ships | Taken (D6). The date is Q1. The nuget.org deprecation is the maintainer's: after 1.2.6, versions up to 1.2.5 point to 1.2.6; 1.2.6 stays undeprecated as the .NET Framework line | S1, S5, S19 |
| RD5 | 2.0.0, SemVer, MinVer | Taken (D2) | S5, S19 |
| RD6 | Keep the id; disclaimer and attribution; no new `Tableau.*` ids; a fallback id; optionally ask Tableau for permission | Taken (D5). The fallback and the request are the maintainer's, under Later and For the maintainers | S1, S2 |
| RD7 | MIT, agreed in writing with Starschema and László Nemes | MIT taken (D4); the agreement is Q6, answered | S0 |
| RD8 | Test against maintained Server versions (about 2024.2, REST 3.23) and Cloud; older on opt-in | Taken (D8); the exact floor is Q4 | S9 |
| RD9 | The Developer Program site, a direct-trust app, a reviewed environment; Server-only by hand | Taken (D9); the environment's protection is Q2 | S11 |

**Features,** from the research's ranked list.

| Feature | Where |
|---|---|
| P1.1 Modern sign-in and a session manager | S11 |
| P1.2 JSON models generated from the xsd | S8 |
| P1.3 Transport: IHttpClientFactory, DI, resilience tuned for Tableau, typed errors | S9 (DI, transport, errors), S10 (resilience) |
| P1.4 Version negotiation and capability checks; `/api/{v}/` and `/api/-/` | S9 |
| P1.5 Paging and filters | S12 |
| P1.6 The Cloud task model; Delete Extract Refresh Task; Add Data Source to Server Schedule; Query Jobs; `WaitForJobAsync` | S15 |
| P1.7 Publishing by size, `asJob`, progress, cancellation | S17 |
| P1.8 Generic permissions and tags | S13 |
| P1.9 Metadata API (GraphQL) | S13 |
| P2.10 Custom views, Get View by Path, PowerPoint download, view filters on exports | follow-up issue (S14 brings the view filters, `maxAge` and size on exports) |
| P2.11 Flows and Cloud flow tasks | follow-up issue |
| P2.12 Webhooks | follow-up issue |
| P2.13 Update Data in Hyper Data Source; create and delete extracts | follow-up issue |
| P2.14 Users at scale: bulk group membership, CSV import and delete | follow-up issue (S12 brings `email`, `idpConfigurationId`, language and locale on Update User) |
| P2.15 Replace permissions, Get Server Schedule, server sessions, project owner | follow-up issue |
| P2.16 Metadata REST gaps: single gets, DQW batch, certifications | follow-up issue (S13 brings DQW delete and the tag siblings) |
| P3.17 Labels, virtual connections, collections, Recycle Bin, content search, usage statistics | follow-up issue |
| P3.18 Administration: identity pools, OpenID Connect, connected apps and EAS, SCIM, PAT list and revoke | follow-up issue |
| P3.19 Pulse | follow-up issue |
| P3.20 VizQL Data Service and the Tableau Cloud Manager API | follow-up issue |
| P3.21 Telemetry | follow-up issue (Later) |
| P3.22 Encrypted keychains and user credentials | follow-up issue |

**F45's list of gaps:**

| Gap | Where |
|---|---|
| Connected-app sign-in | S11 |
| Update Schedule, Add Data Source to Schedule, Cloud tasks, Cloud subscriptions | S15 |
| Permissions on views and flows | S13 |
| Publish options and a whole-file helper | S17 |
| `maxAge` and size on view exports | S14 |
| Custom views | P2.10 |
| Flows | P2.11 |
| Webhooks | P2.12 |
| Data-driven alerts | follow-up issue, with P2.12 |
| PAT management | P3.18 |
| Metrics | none: Tableau retired them in 3.22 |

**Engineering items:**

| Item | Where |
|---|---|
| `.slnx`, `src/` and `tests/`, central packages, strictness | S5 |
| MSTest 4 on Microsoft.Testing.Platform | S7 |
| Fixture recorder and scrubber | S11 |
| AOT smoke test | S8 |
| CodeQL and Dependabot | S1 |
| Release through trusted publishing | S2 (1.x), S18 (2.x). The environment is `nuget`, not the research's `release` (D1) |
| Launch docs and the migration guide | S1, S5, S18 |

## Plan review

None yet. `review-plan` arrives with S0's skills. Round 1 runs on this file once #1 merges, and its findings and their
status are recorded here.

## Progress log

| Date | Step | What happened |
|---|---|---|
| 2026-10-28 | before S0 | Done: the repo made public; the rulesets; the `nuget` environment (tags `v*` only, a maintainer's approval); the nuget.org trusted publishing policy for `release.yml` and `nuget`. |
| 2026-10-28 | before S0 | The codebase review (F1 to F50, at `4267562`) and the research (RD1 to RD9, 22 ranked features) written. Both go into `.local/review/` and stay out of the repo until 1.2.6 ships. |
| 2026-10-28 | before S0 | @serionist took D1 to D10. |
| 2026-10-28 | S0 | The PR for #1 is in progress. |
| 2026-10-29 | S0 | Done: PR #3 merged as `262223d`, closing #1. Eight review rounds. Rounds 3 to 8 ran Claude Code's permission engine headless and hardened `.claude/settings.json`: denies for the file-reading flags of its built-in read-only commands, asks for bundles, quotes, backslashes and leading redirections. Round 8 swept 780 generated rewrites with 0 leaks. The licence agreement (Q6) is recorded on #1. Follow-up #4 keeps the derivation and the engine check in the repo. |
| 2026-10-29 | S0 | Follow-up #7 (squash `1889a04`, closing #5 and #6): at the maintainer's request the permission rules ask less. The allows cover the normal git, gh and dotnet workflow in both shells, every deny stays, and the one ask is a live test run. Also a gate fix: gitleaks in a worktree's pre-push no longer inherits the hook's `GIT_DIR`. NemesLaszlo accepted his invite and approves `nuget` deployments with serionist. |
| 2026-10-29 | S0 | Follow-up #9 (squash `b6c9d84`, closing #8): at the maintainer's request `.claude/settings.json` sets no permission rules at all, only its no-attribution setting. #4 is closed as not needed. |
| 2026-10-29 | plan | The plan committed as the first commit of `2-modernization-plan`, with S0 marked done; the review and research are in `.local/review/codebase/`. |
| 2026-10-28 | plan | This plan written as a draft for #2, to be committed as `docs/plans/2026-10-28-modernization.md`. 1.2.5's commit on today's `main` is `b23503e`, and every 1.2.6 candidate was checked against it. It also showed: `QueryViewImage` strips the filter prefix too; `CreateMonthlySchedule` still has its `dayToRun` parameter and the schema has `monthDay`; the user auth-setting enum lacks `OpenID` and `TableauIDWithMFA`; `QueryViewPDF` fails when no filters are passed. |

## For the maintainers

What the session cannot do, first what closes a security exposure. Kept current as the work goes on.

**Now:**

- [ ] Turn on private vulnerability reporting (Settings, Code security). SECURITY.md (S1) sends reporters there.
- [x] Q6: László Nemes agreed to MIT, recorded on #1 (2026-10-28); serionist confirmed the clearance when the repo went public.
- [ ] Answer Q1 to Q5 on #2.

**For S1 and S2:**

- [ ] After S1: make `CI` and `PR text` required on the `main` ruleset, or tell the session to apply it through the
  API. Before S2's PR: the same rules for `release/1.x` (PRs only, squash, no force push, the two checks).
- [ ] Check the `nuget` environment has what `NuGet/login` needs, such as a `NUGET_USER` secret with the nuget.org
  profile name of the owning account (`serionist`).
- [ ] Approve the `nuget` deployment for `v1.2.6`.

**After 1.2.6 is on nuget.org:**

- [ ] Publish a GitHub security advisory for 1.2.5 and older, fixed in 1.2.6, describing what changed; request a CVE if
  you want one.
- [ ] Deprecate 1.0.6 to 1.2.5 on nuget.org with "Critical bugs" and Tableau.Api 1.2.6 as the alternative. Leave 1.2.6
  undeprecated.
- [ ] Tell Starschema, the co-owner.

**Before S11:**

- [ ] Join the Tableau Developer Program and get its site; note its renewal date.
- [ ] On the site, a direct-trust connected app: enabled, one secret, the scopes the suite needs.
- [ ] The `tableau-live` environment, protected as Q2 decides, with the secrets `TABLEAU_LIVE_SERVER_URL`,
  `TABLEAU_LIVE_SITE`, `TABLEAU_LIVE_CLIENT_ID`, `TABLEAU_LIVE_SECRET_ID`, `TABLEAU_LIVE_SECRET_VALUE` and
  `TABLEAU_LIVE_USER`.

**Settings:**

- [ ] Dependabot alerts, Dependabot security updates and the dependency graph on; keep secret scanning and push
  protection on.
- [ ] Keep CodeQL's default setup off: `codeql.yml` is the advanced setup, and the two conflict.
- [ ] Check that a tag ruleset restricts who can create `v*` tags.

**Before 2.0.0:**

- [ ] Run the `ServerOnly` suite on a Tableau Server you have (S18).
- [ ] Approve the `nuget` deployments for `v2.0.0-rc.1` and `v2.0.0`.
- [ ] Optional: ask Tableau's developer relations for written permission for the name (RD6).
