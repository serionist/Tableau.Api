---
title: Modernise Tableau.Api into the 1.2.6 security patch and the 2.0.0 release
issue: "#2"
status: approved
approved_by: serionist
approved_at: 2026-10-29
---

# Modernise Tableau.Api into the 1.2.6 security patch and the 2.0.0 release

This plan is the record of the work up to 2.0.0: the decisions, the design, the order of work, the step that closes each
review finding, and a progress log. Read it first when picking the work up. Update its progress log and its trackers
at the end of every step.

**In short:**

- **1.2.6 ships first** (S2a, S2b), for the users of 1.2.5: the security fixes and the cheap fixes that change no
  signature, built on `release/1.x` from 1.2.5's own commit, with offline tests, released from a tag.
- **main gets CI, the open-source files and its own security fixes next** (S1, S3), then the review goes public (S4).
- **2.0.0 is a rebuild** (S5 to S19): net10.0 only, JSON with source-generated models and native AOT, a client bound to
  its session with every current sign-in, safe HTTP defaults, paging, gates that work, Vizportal kept and fixed, offline
  tests on every PR and a live suite every night.
- **Every finding F1 to F50 has exactly one step** (Finding tracker). None is "won't fix". Every research decision and
  feature has a step or a follow-up issue (Research tracker).
- **Every owner call has its answer** (Owner calls). Plan review round 4 approved the plan, and @serionist approved it.

**Sources:** the codebase review (findings F1 to F50) and the research (its decisions, called RD1 to RD9 here, and its
22 ranked features), both of 2026-10-28: `docs/reviews/2026-10-28-codebase.md` and `docs/reviews/2026-10-28-research.md`, published in S4 once 1.2.6 shipped.
S4 publishes them as `docs/reviews/2026-10-28-codebase.md` and `docs/reviews/2026-10-28-research.md`.

**Approval:** approved by @serionist on 2026-10-29 (Plan review). They took D1 to D10 on 2026-10-28 and D11 on
2026-10-29, and every owner call has its answer. Plan review rounds 1 to 3 came back needs-fixes and were applied;
round 4 came back approved, and its minors are applied.

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
- **main, before 2.0:** CI, the open-source files, the Vizportal TLS fix and S2b's security fixes ported to main.
- **2.0:** layout and build, the reformat, the test platform, the model generator, the client core, resilience,
  sessions, the port of all 142 REST methods and the 41 Vizportal operations, publishing, the research's P1 features,
  the four metadata methods of the NuGet_Prod line, docs, the release candidate and the release.

**Out of scope** (under Later): the research's P2 and P3 features, each as a follow-up issue; Tableau Server in CI; a
year-numbered version line; any package id other than `Tableau.Api`.

## Decisions

Taken by @serionist: D1 to D10 on 2026-10-28, D11 on 2026-10-29.

| # | Decision | Detail | Steps |
|---|----------|--------|-------|
| D1 | A 1.2.6 security patch ships first | For the users of 1.2.5. Built on `release/1.x` from the commit 1.2.5 was built from: `b23503e` on today's `main` (2021-10-13, "Updated Tableau.Api.csproj"; `b11a150` before the history was rewritten). It keeps netstandard2.0 and the 1.2.5 signatures. It holds the fixes for F2 and F3, and the cheap fixes that change no signature and exist in the 1.2.5 code, each checked against that code. F1 is not in 1.2.5 (Vizportal came later) and is fixed on main. It has offline tests with a fake server. It is released from a tag by a `release.yml` workflow through the `nuget` environment. nuget.org trusted publishing is set up for workflow `release.yml` and environment `nuget` on serionist/Tableau.Api, so both names are fixed; the environment allows `v*` tags only and needs a maintainer's approval. | S2a, S2b |
| D2 | 2.0.0, SemVer, MinVer | Prereleases are `-rc.N`. Every break against 1.2.5 is suppressed deliberately and listed in the migration guide. Package validation runs against the 1.2.5 baseline, but across the change of target it reports only the dropped framework, so ApiCompat in assembly mode against 1.2.5's DLL carries the breaks, in its own suppression file (Design, Build and layout). | S5, S18, S19 |
| D3 | Vizportal stays in the main package, fixed | The research's "drop it" is overruled. The fix covers F1 (TLS), F29, F41, F49, cancellation, paging, the shared HTTP core and the same exception and logging rules. The docs say it wraps Tableau's undocumented internal API, may break with any Tableau release, and name the Tableau versions it was tested on. | S3, S16 |
| D4 | MIT | "the Tableau.Api contributors". The LICENSE lands with the harness PR (#1). | S0 |
| D5 | The package id stays `Tableau.Api` | With a "not affiliated with or endorsed by Tableau or Salesforce" disclaimer in the README and the package description. No new `Tableau.*` ids. | S1, S2a, S5 |
| D6 | net10.0 only | .NET Framework and older users stay on 1.2.x, which gets security fixes only. | S1, S5 |
| D7 | JSON on the wire | System.Text.Json with source-generated models; `IsAotCompatible`, with an AOT smoke-test project in CI like WriteableFileProviders' `tests/WriteableFileProviders.Aot`. XML stays only where Tableau requires it, such as the multipart publish payload, and is written with `XmlWriter`. The models are generated from the newest published xsd by a repeatable generator in `eng/`, never hand-patched, and a CI check proves the generated file is current. How the generator works is under Design. | S8, S17 |
| D8 | A modern client | DI registration with options and IHttpClientFactory typed clients. One session manager for every sign-in Tableau has now: PAT, connected-app JWT (direct trust and OAuth 2.0 trust), unified access tokens (Cloud), password with impersonation (Server); one sign-in at a time, sign in again on expiry, thread-safe. The client is bound to its session, with an explicit session API kept for multi-site work. `IAsyncEnumerable` paging and a typed filter and sort builder. A `CancellationToken` last on every method, `ConfigureAwait(false)`, nullable annotations, consistent `...Async` names and return shapes. Resilience: retry only idempotent calls, honour 429 and `Retry-After`, long timeouts for downloads and publishing, streamed downloads. No cross-host redirects with credentials; TLS validation always on unless explicitly turned off. One exception type with the details. Server-vs-Cloud and API-version gates that work, with the version read from Server Info. The Cloud schedule model from API 3.20. The supported floor: the maintained Server versions (about 2024.2, REST 3.23) and Cloud, older versions on opt-in (RD8). | S9 to S15 |
| D9 | Tests | Offline tests with a fake server or `HttpMessageHandler` on every PR, with a coverage floor enforced in CI (WriteableFileProviders' `eng/coverage-gate.cs` is the model). A live suite, nightly and on dispatch, against a free Tableau Developer Program site, signed in through a direct-trust connected app from a reviewed GitHub environment; never on PRs from forks. #43 replaced the Developer Program site with the maintainers' own Tableau Server (S11). Server-only features are tested by hand, and the PR says so. The old live tests' coverage is kept or replaced. | S2a, S2b, S7, S11, S12 to S17 |
| D10 | New endpoints | The high-value ones from the research's ranked list in 2.0.0, the rest as follow-up issues. The four NuGet_Prod metadata methods (F44) are in 2.0.0. | S11 to S17, Research tracker |
| D11 | Standing approval to merge, with limits | The session squash-merges each step's PR once CI is green and `review-code` is clean, without waiting for a maintainer. Three things still need a maintainer: a one-way door follows AGENTS.md's rule, a PR "that names it in its title, after a maintainer agrees in its issue; the other can object on the PR before it merges" (worded so by #11, after Q9), and before merging a door PR the session reads its comments and stops on any maintainer's objection; a release tag's publish is approved by a maintainer in the `nuget` environment; and a PR whose review records a loosened check (REVIEW.md, The harness), a `wontfix` blocker or an added ignore among them, waits for a maintainer's agreement in that PR. This is an exception, for the steps of #2, to AGENTS.md's "a maintainer squash-merges" (Q8, 2026-10-29; the third limit from P20 in plan review round 2). | every step |

## How the work runs

**Already done, 2026-10-28:** the repo is public; the rulesets are set; the `nuget` environment exists (tags `v*` only,
a maintainer's approval); the nuget.org trusted publishing policy names `release.yml` and `nuget`.

**S0, done 2026-10-29:** the harness and licence, PR #3 (squash `262223d`), closing #1. Its review took eight rounds,
recorded in `docs/reviews/2026-10-28-dev-harness-code.md`. At the maintainer's request #9 then removed the permission
rules from `.claude/settings.json`, and #4 was closed as not needed.

**Every later step:**

- One issue and one PR per step, on a branch `<issue>-<slug>`. The issue numbers go into the Steps table as they are
  opened.
- A large step may land as several PRs, each with its own issue, under the step. S2 is the first: S2a and S2b (Q7).
- Each PR updates this plan: its step's status, the trackers and the progress log.
- `review-code` runs in fresh agents, round after round, until no blocker or major is open. Each round is recorded in
  `docs/reviews/`, except where the security rule below says `.local/review/`.
- **Merging (D11):** once CI is green and `review-code` is clean, the session squash-merges, without waiting for a
  maintainer. Three things still wait for one:
  - a one-way door (next bullet);
  - the `nuget` deployment of a release tag, which a maintainer approves;
  - a PR whose review records a loosened check (REVIEW.md, The harness), a `wontfix` blocker or an added ignore among
    them, which waits for a maintainer's agreement in that PR.
- **One-way doors:** AGENTS.md's rule, as #11 worded it after Q9: change one "only in a PR that names it in its title,
  after a maintainer agrees in its issue; the other can object on the PR before it merges". Under D11 the objection
  works so: before merging a door PR, the session reads its comments and stops on any maintainer's objection. For
  this plan the doors are a name, the public API shape, the version and the supported Tableau floor. Each step lists
  its doors; "a maintainer's yes" in a step means this rule.
- **Cloud is not Server:** a PR whose change reaches Server-only behaviour, such as schedules, says what the live site
  (Tableau Cloud) could not show, and whether the `ServerOnly` suite ran.

**Order:**

- **CI and the open-source files come early.** S1 lands right after S0, because 1.2.6 needs CI: S2a reuses S1's
  workflows. S1 and S2a may be built side by side. Once S1 merges, `CI` and `PR text` become required checks on the
  `main` ruleset, and on `release/1.x` before S2a's PR.
- **The reformat lands alone** (S6), so `.git-blame-ignore-revs` can list its commit. The next PR (S7) adds that file.
- **The 2.0 steps leave `main` building:** each port step deletes the old methods it replaces, and an old file when it
  empties.
- **Each step from S8 to S16 adds its new public types** to the AOT app's root list, which S9's conventions test also
  reads (Design, Models, AOT). A test enforces it: it fails on any public type that is in neither the root list nor a
  fixed list of the 1.x types, which only shrinks as the old client goes.

**Security text:** until 1.2.6 is on nuget.org, nothing public carries exploit detail.

- This plan is public. It says what a security fix changes ("escape every path segment", "never send credentials to
  another host"), never how to attack the old code.
- The same holds for S2b's and S3's issues, PRs, commits, CHANGELOG and release notes. S2a reveals nothing: it sets
  the branch up and fixes nothing.
- The review, the research and the review-code records of S2b and S3 stay in `.local/review/` until 1.2.6 ships; S4
  publishes them.
- SECURITY.md (S1) makes this the standing rule for every later fix.

**Rule files move with the code.** A step that outdates a line of a rule file fixes it in the same PR:

| Step | What it updates |
|---|---|
| S1 | AGENTS.md: hosted CI now exists; "Where things are" gets what S1 adds: the top-level files (README.md, SECURITY.md, CONTRIBUTING.md, CHANGELOG.md), the `.github/` workflows and issue forms, `eng/package-check.cs` |
| S3 | AGENTS.md: offline tests exist, in `tests/Tableau.Api.Tests/`; the review-code lens's tests section; start-work's test path; finish-work's gate run with tests |
| S4 | AGENTS.md: "last release 1.2.5" becomes 1.2.6, once 1.2.6 is on nuget.org |
| S5 | AGENTS.md: what this is (net10.0), where things are, the build commands, the one-way doors on the target and the version; REVIEW.md Q7 (the build it asks about); the target-framework lines of both lens files (review-code and review-plan) |
| S7 | AGENTS.md: the test categories and the coverage gate, the old live suite gone, `.git-blame-ignore-revs` in "Where things are"; finish-work's live suite; start-work's test path |
| S8 | AGENTS.md: the generator replaces `how_to_generate_schema.txt` (Where things are, Writing, the hand-added enum value in Traps) |
| S9 | REVIEW.md's line on `CancellationToken?` and `[ApiVersion]`; AGENTS.md's door on the Tableau versions claimed; the lens files' compatibility questions |
| S11 | finish-work's live suite: `live.yml` and the `TABLEAU_LIVE_*` settings |
| S16 | AGENTS.md: Newtonsoft.Json gone (what this is, the Context7 line) |
| S17 | AGENTS.md: the Traps rows whose guards were old code, kept as the lesson they teach |
| S18 | every rule file, a final pass |

AGENTS.md holds 147 of its 150 lines. A step that adds to it removes as much, by moving detail to `docs/guide/` or a
skill. S1, the first to add, keeps it at 147:

- the `LICENSE` row becomes one row for the top-level files: LICENSE, README.md, SECURITY.md, CONTRIBUTING.md and
  CHANGELOG.md;
- the `.github/` row gains the workflows and the issue forms, and the `eng/` row `package-check.cs`, in their own
  cells;
- the "Hosted CI doesn't exist yet" bullet becomes one line on what CI runs.

S7 puts `.git-blame-ignore-revs` into the `.editorconfig`, `.gitattributes`, `.gitignore` row the same way.

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
- **`release/1.x` is never merged into `main`.** A fix both lines need is written for each, as S3 does for S2b. So
  MinVer on `main` never sees a `v1` tag.

### The 1.2.6 patch (S2a, S2b)

**Two PRs (Q7):** S2a sets the branch up (CI, the release workflow, the secret backstops, the hooks, the test harness,
the metadata, package validation) and reveals nothing. S2b adds the fixes and releases them.

**The rule:** a fix goes into 1.2.6 when it exists in 1.2.5's code, changes no public signature, and is small. Every
candidate the review named was checked against `b23503e`, and all of them are there.

| Finding | What 1.2.6 changes | What checking 1.2.5's code showed |
|---|---|---|
| F2 | Every path segment (ids, tag names, site names and content URLs) is validated and escaped. Only an empty or dot-only segment is refused. Every character outside RFC 3986's unreserved set is escaped as UTF-8 in upper-case hex, the form `Uri` writes, except two groups that go raw: `:` and `@` (upload session ids contain `:` and are not LUIDs), and `! ' ( ) * ,`, as 1.2.5 sent them, since they are path sub-delimiters and never delimiters. Then, once the request URI is built, its escaped path must equal the path the handler wrote, or the call throws before anything is sent. | Present; every area file builds its paths. |
| F3 | Redirects are not followed: a 3xx fails with the status and the host it pointed to. The token goes on each request message. | Present, in the shared request code. |
| F5 | View filters get the `vf_` prefix on view data and view image, as on PDF; one helper builds them; ordinal comparison. | 1.2.5 strips the prefix on both data and image (main fixed image in 2024). The same helper stops `QueryViewPDF` failing when no filters are passed, a 1.2.5-only defect. |
| F6 | Every query key and value is escaped. In `filter` and `sort` the parts between `:` and `,` are escaped and the separators kept. | Filter and sort values, and every key, are sent raw. |
| F7 | Revision lists send `pageSize` and `pageNumber`. | Validated, never sent. |
| F9 | `UpdateTo*Schedule` throws `NotSupportedException` before sending anything. With any 2xx now a success (F15), it would otherwise create a second schedule and report success. | It posts a new schedule; updating one needs a schedule id that no 1.2.5 signature has. |
| F10 | Schedule create accepts 201 (through F15). The start and end times go as `HH:mm:ss` with no offset, and a time outside `[00:00, 24:00)` throws. Monthly schedules send `monthDay` from the existing `dayToRun` parameter. Only the request is rewritten: the public `DateTime` members of the schedule model keep their serialiser attributes, with no `[XmlIgnore]` shadow, so a schedules response still fills `start` and `end`. | 1.2.5 still has `dayToRun` (main removed it), and its schema has `monthDay`, so this needs no new signature. |
| F11 | Extract refresh task lists select the task items by type and treat an empty list as empty. | The cast bug and the null list are both there. |
| F12 | Get Extract Refresh Task calls `tasks/extractRefreshes/{id}`. | Calls `tasks/{id}`. |
| F14 | Enum values added: `OpenID` and `TableauIDWithMFA` for the user auth setting, and every value the 3.29 xsd lists that 1.2.5's enums lack, each appended after the existing members, so no existing member's value moves (package validation reports a moved value as CP0011). | 1.2.5 knows only `ServerDefault` and `SAML`. The values are added to the generated file by hand, which is acceptable on a branch that only takes fixes. |
| F15 | Any 2xx is success. | One exact status per call. |
| F27 | `TableauSession.UserName` holds what each sign-in meant it to: the user name, the token name for a PAT, the old name after Switch Site. | Always null. |
| F39 | The base URL's path is kept, so a server behind a path prefix is reachable. | Rebuilt from scheme, host and port. |

- **How F2 is built:** an interpolated-string handler for paths escapes and validates every hole, so each call site
  changes only by its wrapper. It needs C# 10, so the project sets `LangVersion` to 10, not `latest`
  (netstandard2.0 defaults to 7.3), and the compiler attribute it needs is declared internally for netstandard2.0.
- **What stays as in 1.2.5,** because fixing it changes a signature or makes working calls throw: the dead version
  gates (F4), null lists (F20), buffered downloads (F17), the nullable token (F21), and the rest.
- **Considered and left out** (Q5): F17's Content-Type null check, F28 and F32 also qualify. They stay for 2.0 to keep
  the patch small.
- **The package:**
  - in the project: `<Version>1.2.6</Version>`; `AssemblyVersion` and `FileVersion` 1.0.2.6, after 1.2.5's 1.0.2.5;
    `LangVersion` 10; a true description ending in the disclaimer (D5); a README packed into the package; `snupkg`; no
    `IncludeSource`; `ContinuousIntegrationBuild` in CI;
  - the licence expression and the GitHub URLs only in the branch's `Directory.Build.props`, as on `main`;
  - the same single dependency, `Microsoft.Extensions.Logging` 2.1.1.
- **Two checks prove the API is 1.2.5's:**
  - `EnablePackageValidation` with `PackageValidationBaselineVersion` 1.2.5, and no suppression file allowed: CI fails
    when a `CompatibilitySuppressions.xml` exists on the branch;
  - since a `NoWarn` in the project would also silence package validation's errors, CI also runs the ApiCompat tool's
    `package` mode on the packed nupkg against the 1.2.5 nupkg from nuget.org, with no suppression input. The tool
    reads no project property, so nothing in the project can quiet it;
  - enum members appended after the existing ones (F14) are not a break, so both pass.
- **The branch gets main's backstops:**
  - the secrets and local blocks of main's `.gitignore` (`.local/`, `.claude/worktrees/`, `.claude/settings.local.json`,
    `.env`, `testsettings.json`, `*.runsettings`), main's `.gitattributes`, `.githooks/`, `.gitleaks.toml`;
  - `eng/gate.cs`. CI runs `dotnet run eng/gate.cs -- quick --base origin/release/1.x` with `GATE_REQUIRE_GITLEAKS=1`,
    so a secret fails it; the skills checks pass over zero skills. The tests check is in the gate's `full` lane, not
    `quick`: there it runs `dotnet test`, as on main from S3, with the net48 tests on Windows only;
  - `global.json` with the SDK and `"test": {"runner": "Microsoft.Testing.Platform"}`, `nuget.config`, LICENSE, and a
    short AGENTS.md for the branch's limits.
- **Its workflows** (S2a writes them, modelled on WriteableFileProviders' and adapted):
  - `ci.yml` on PRs to `release/1.x` and on every push to it: the gate, build, the tests on Linux (net10.0) and Windows
    (net10.0 and net48), pack with package validation, ApiCompat's `package` mode against the 1.2.5 nupkg, and
    WriteableFileProviders' `eng/package-check.cs`, adapted to one package (a `.nupkg` and a `.snupkg` at the
    version, nothing else, a version nuget.org accepts); `CI` last;
  - `pr-text.yml`, `codeql.yml`;
  - `release.yml`, the line's first: its CI job waits for a green push run of `ci.yml` on `release/1.x` for the tagged
    commit (`gh run list --workflow ci.yml --branch release/1.x --event push --commit <sha>`), never `--branch main`
    (CI and release, below).
- **A weekly NuGet audit of the branch:** GitHub runs scheduled workflows only on the default branch, so the weekly
  job lives in main's `ci.yml` (S3) and checks out `release/1.x`; an advisory fails it.
- **Docs on the branch:** README, SECURITY.md, CHANGELOG.md, and a short `docs/releasing.md` for a 1.x patch (main's
  comes in S18). No Dependabot version updates: the line changes only for security fixes.
- **The old live test project is deleted** on the branch: it cannot build (netcoreapp2.1) and needs a server. The
  offline suite replaces it; a live smoke run by hand checks the release.
- **CHANGELOG, 1.2.6** (the ids in brackets are for this plan; the CHANGELOG carries none):
  - Security: path segments are validated and escaped; redirects are not followed, so credentials and request bodies
    never go to another address.
  - Fixed: the rest of the table.
  - Changed: inputs once sent raw are escaped or refused; a value the caller had already URL-encoded is now encoded
    twice, whether a filter or a path value (a tag, a site name, a content URL); a redirect fails; any 2xx succeeds;
    a schedule time outside a day throws; `UpdateTo*Schedule` throws `NotSupportedException` before sending, since it
    created a second schedule instead of updating one (F9; the update comes in 2.0).
  - Known issues, fixed in 2.0: the Active Directory group updates miss the group id (F13); `AddWorkbookToSchedule`
    returns null (F31); `QueryDatabases` and `QueryTables` return the first page (F8).
  - Known issue on .NET Framework targets older than 4.7.2: when the request URI holds a non-ASCII character, those
    targets' URI parsing can undo the escaping of a comma or a colon in a filter value, so Tableau may read it as a
    separator. 1.2.5 behaved the same. The tests record the outcome on each target (F6; 2.0 runs on net10.0 only).

### Security on main before 2.0 (S3)

main is unreleased, but public. S3 makes it safe before the rebuild replaces the code:

- **Vizportal (F1):** certificates are validated as on REST. One helper builds the handler for both clients, and it
  turns validation off only when `IgnoreSSLErrors` is set. No automatic redirects.
- **REST (F2, F3, F6, F39):** main's `buildUri`, `buildClient` and `apiRequest` are still 1.2.5's, so S2b's versions and
  its path handler are ported as they are, with main's extra call sites (`QueryView`, the crosstab export, view
  permissions).
- **Tests:** S2b's request-building tests, adapted to main's signatures, start `tests/Tableau.Api.Tests`, plus an F1
  test against a TLS fake with an untrusted certificate.
- **The gate runs them:** `CheckTests` in `eng/gate.cs`, which today passes with "no offline tests yet", runs
  `dotnet test` on the new test project by name, since the solution keeps the old netcoreapp2.1 project until S7;
  `global.json` gains `"test": {"runner": "Microsoft.Testing.Platform"}`, which `main` lacks.

### Build and layout for 2.0 (S5)

```
Tableau.Api.slnx
Directory.Build.props        S0: metadata, licence, NuGet audit; S5: net10.0, Nullable, strictness, analyzers, docs
Directory.Packages.props     central package versions, lock files
global.json                  S0: the SDK; S3 adds the test runner, Microsoft.Testing.Platform
eng/gate.cs                  S0; S3 wires the tests, S5 the new library path, S7 coverage, S8 the generator check
eng/apicompat/               S5: the ApiCompat suppression file, the ledger of breaks against 1.2.5
eng/package-check.cs         S1: after WriteableFileProviders', one package at the version, nupkg and snupkg
eng/coverage-gate.cs         S7: fails when the assembly falls below its floor in eng/coverage.json
eng/generate-models.cs       S8: the model generator; eng/schema/ holds the xsd
src/Directory.Build.props    packaging: MinVer, README, SourceLink, snupkg, package validation; licence at the root
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
- **Package validation and ApiCompat (D2):**
  - `EnablePackageValidation` with `PackageValidationBaselineVersion` 1.2.5. Across netstandard2.0 to net10.0 it
    reports only the dropped framework (PKV006), which is the one entry of `CompatibilitySuppressions.xml`. S5's first
    `dotnet pack` confirms that behaviour.
  - So the API comparison is ApiCompat in assembly mode (`Microsoft.DotNet.ApiCompat.Tool`, pinned as a local tool),
    between 1.2.5's netstandard2.0 DLL, taken from the nuget.org package and checked against its hash, and the net10.0
    build, each with its references. It runs in CI on every PR.
  - Its own suppression file, `eng/apicompat/1.2.5.xml`, is the ledger of breaks: regenerated with
    `--generate-suppression-file` whenever a step breaks something more, and reviewed in that PR.
  - The migration guide groups the same list.

### The 2.0 client (S9, S10, S11)

```csharp
// One named client per Tableau connection.
services.AddTableauClient("prod", o =>
    {
        o.ServerUrl = new Uri("https://tableau.example.com/tableau/"); // its path is kept
        o.Site = "finance";                                          // content URL; "" is the default site
        o.ApiVersion = null;                                         // negotiated; or pinned, such as new Version(3, 27)
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
        await download.SaveToFileAsync(Path.Combine("out", download.FileName ?? "workbook.twbx"), cancellationToken: cancellationToken);
    }
}
catch (TableauException e) when (e.Kind == TableauErrorKind.RateLimited)
{
    // e.RetryAfter, e.StatusCode, e.ErrorCode, e.Summary, e.Detail, e.RequestPath
}
```

**Shape** (a maintainer's yes on S9's issue; S11 and S12 add their parts):

- **`TableauClient`** with one sub-client per area: `Server`, `Sessions`, `Sites`, `Projects`, `Workbooks`, `Views`,
  `DataSources`, `Users`, `Groups`, `Permissions`, `Tags`, `Favorites`, `Jobs`, `Tasks`, `Schedules`, `Subscriptions`,
  `Metadata`, `VizPortal`. Sub-clients are created once per client (F46).
- **Verbs:** `GetAsync`, `ListAsync` (`IAsyncEnumerable<T>`), `ListPageAsync` (`Page<T>`), `CreateAsync`, `UpdateAsync`,
  `DeleteAsync`, `DownloadAsync`, `PublishAsync`, `RunAsync`, `CancelAsync`, `AddAsync`, `RemoveAsync`. An options
  record where a method has more than three optional parameters. String LUIDs throughout. The token last, defaulted.
- **Mocking:** sub-clients are classes with virtual methods and a protected constructor (the Azure SDK pattern), not
  interfaces, so adding a method in a minor breaks no implementer.
- **Options:** `TableauClientOptions` (above, plus `AllowUnsupportedServerVersion`, `DangerousAcceptAnyServerCertificate`,
  `AllowInsecureHttp`, `RequestTimeout`, `TransferTimeout` and, from S10, resilience), read-only once a client is built,
  bound from configuration by the binder source generator and validated at start-up by the options validation source
  generator. Both stay AOT safe. As built in S9, `ApiVersion` is a `Version?`, null negotiating: the binder's generator
  reads a struct of the library's as all zeros, so a `TableauApiVersion` type couldn't bind.
- **DI and typed clients:**
  - `AddTableauClient(name, ...)` registers a typed client over a named `HttpClient`, keyed by name, and returns a
    builder for credentials and resilience. An unnamed overload registers the default client.
  - Typed clients are transient, so the long-lived state of each named client (session, negotiated version,
    deployment, rate-limit back-off) lives in a singleton keyed by the name.
- **Without DI:** `TableauClient.Create(options, loggerFactory)` (S9; S11 adds the credentials) builds the same pipeline
  over one `SocketsHttpHandler` with `PooledConnectionLifetime`. A constructor takes a caller's `HttpClient`, which the
  client never disposes. The client is `IDisposable` and `IAsyncDisposable` (F18), so a container can end it either
  way; from S11 one that owns its state signs out on dispose, never a DI transient, whose session is the name's.

**Endpoint descriptors and gates (F4, F16):**

```csharp
internal sealed record Endpoint(string Name, HttpMethod Method, string Template, Version MinVersion,
    TableauDeployments Deployments, bool Idempotent, TransferKind Transfer);
```

- Each REST call has one descriptor in an internal `Endpoints` registry. The pipeline reads it for the path, the
  gates, the retry rule and the timeout. No reflection, no static constructor.
- **Version:** with `ApiVersion` null, the default, the client reads `restApiVersion` from Server Info once (it needs no sign-in, S9
  found) and caps it at the newest version it knows, 3.29, the newest schema Tableau publishes (#39). A pinned version
  is parsed as major.minor only (F47).
- **Floor (D8):** below the floor, the first call throws unless `AllowUnsupportedServerVersion` is set, which logs a
  warning once. The floor is REST API 3.23 (Tableau Server 2024.2) and Tableau Cloud, confirmed in S9 (Q4, #39).
- **Deployment:** `Auto` takes Cloud for the Tableau Cloud host names (the list is to verify in S9; Server Info is
  checked for a better signal), Server otherwise; or set it.
- **Before any request:** a call below its descriptor's version, or on a deployment it does not run on, throws
  `TableauException` (`UnsupportedApiVersion` or `UnsupportedDeployment`) and sends nothing. Impersonation and Switch
  Site are Server only. `DeleteGroup` is gated at 2.1 on both deployments.
- **URL shapes:** the pipeline builds `/api/{version}/...`, `/api/-/...` (resource-versioned endpoints, for later
  minors) and `/api/metadata/graphql`.

**HTTP rules:**

- **Paths:** built from segments by S3's path handler, with 1.2.6's rule and its exact path check (the 1.2.6 table, F2),
  so S9 ports S2b's handler as it is; a descriptor may mark a segment as a LUID, checked strictly (F2).
- **Cookies:** `UseCookies` is off on the primary handler, so no cookie container is shared between requests or
  sessions; a call that needs a cookie sets it on its own request (Vizportal, S16).
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

- `Microsoft.Extensions.Logging.Abstractions` only, with `LoggerMessage` source-generated methods. No dependency may
  change how a caller's app builds: Microsoft.Extensions.Telemetry.Abstractions, under Microsoft.Extensions.Http.Resilience,
  turns off the runtime's `LoggerMessage` generator in every app that takes it, so S10 runs Polly.Core alone (#41).
- Request lines at Debug, with query values redacted. A failure the caller receives is not logged above Debug.
  Cancellation is never an error.
- Never logged: tokens, PATs, JWTs, passwords, cookies, `X-Tableau-Auth`, bodies. A test reads every log line the
  suite writes and fails on any secret.

### Resilience and downloads (S10)

As S10 built it (#41, PR #42):

- **Pipeline:** Polly.Core's retry, built from the client's read-only options (`Pipeline/TableauResilience.cs`) and run
  by a handler of the library's own, `TableauResilienceHandler`, which also times each attempt and reads its answer.
  Not Microsoft.Extensions.Http.Resilience: its `Microsoft.Extensions.Telemetry.Abstractions` turns off the runtime's
  `LoggerMessage` generator in every app that takes the package, and `AddResilienceHandler` would register services
  and telemetry in the caller's container and log each retry as a warning (the maintainer's call on PR #42). The
  handler sits closest to the connection in every client: the named `HttpClient` from DI, the one
  `TableauClient.Create` builds, and an invoker over a caller's `HttpClient`, each attempt a copy of the request sent
  through it, without the request's options. The caller configures it with `MaxRetries` and `MaxRetryAfter`, or turns
  it off and adds their own.
- **Retries:** only for descriptors marked idempotent, which the request carries in its options. Never publish, create
  or run-now. 408, 429, 5xx, a connection that failed or broke off, and the library's own attempt timeout; not a
  refused certificate, a proxy's 407, or another failure sending again won't mend, and not a handler's own exception,
  a timeout or a cancellation of its own among them. Three by default, jittered exponential back-off from about two
  seconds, at most 30. Logged at Debug.
- **429 and 503:** a 429's or a 503's `Retry-After`, as a delay or a date, or a 429 body's wait when the header is
  missing, is waited out; another status's `Retry-After` is left to the back-off. Tableau documents neither; the one
  wording found, in a report against Tableau's own Python client (tableau/server-client-python#952), is a detail "Too
  many requests for 'api.rest.refresh_extracts'. Please retry after 29 minutes." under code 429000, so the body is read
  loosely: "retry after", a number, and seconds, minutes or hours. Above `MaxRetryAfter` (two minutes by default) it
  does not wait: a 429 throws `RateLimited` and a 503 `Http`, each with `RetryAfter`.
- **Timeouts:** `RequestTimeout` per attempt, timed by the handler on the client's clock, covering the answer as well
  as its headers by reading it within the attempt (a success whole, an error's first 64 KB); a transfer
  (`TransferKind`) has no attempt timeout, and `TransferTimeout` covers all of it, its attempts, the waits and, for a
  download, reading the file, with no limit by default. A timeout is `Timeout`, with a cancellation as its cause, never
  the caller's cancellation; a handler's own timeout or cancellation is `Http`, with it as the cause.
- **No circuit breaker or concurrency limiter** by default: one client talks to one server, and a breaker opening on a
  burst of 5xx surprises batch jobs.
- **The builder:** `ConfigurePrimaryHandler(Action<SocketsHttpHandler>)` runs on the library's handler, then the
  library's rules are set again on the handler's own copy of its TLS options, so options shared with another handler
  stay as set: no redirects, no cookies, its certificate callback (a caller's callback replaced; a chain policy the
  caller set used as set, its flags able to loosen the check). `AddHttpMessageHandler(Func<IServiceProvider,
  DelegatingHandler>)` adds the caller's handlers outside the retries, the first outermost. One action per name sets
  them all, registered by the first `AddTableauClient` of the name, so a second keeps what the first added (S9's note).
- **Downloads (F17, F42):** sent with `ResponseHeadersRead` and returned at the headers, after reading up to 8 bytes when
  neither the name nor the media type tells the kind. Only a `TransferKind.Download` descriptor is taken.
  `TableauDownload` (`IDisposable`, `IAsyncDisposable`) owns the response and the transfer timeout, and has:
  - a forward-only `Content` stream that honours its read's token, the call's and `TransferTimeout`;
  - `FileName` from `Content-Disposition`'s raw text, since Tableau's documented `name="tableau_workbook";
    filename="..."` has no disposition type and .NET's typed header refuses it: `filename*` first, then `filename`,
    the part after the last `/` or `\`, without control, format, private-use and separator characters or what Windows
    refuses, cut to 255 bytes of UTF-8 with a short extension kept, then trimmed of leading whitespace and trailing
    whitespace, dots and spaces, and a device name prefixed;
  - `ContentType`, `Length` when known (Tableau Server sends a workbook chunked, with none), and `Kind` from the
    name's extension, else the media type, else the first bytes (a zip's, a PDF's or a PNG's signature, read ahead and
    given back);
  - `SaveToFileAsync`, through a temporary file beside the target, refusing a folder and a file it would replace
    unless asked, and open to a second try when the first couldn't open its file;
  - a public constructor over a stream, for a mock of a download method.

  A response without Content-Type works.
### Sessions and sign-in (S11)

As S11 built it (#43, the door agreed there):

- **Credentials** (`TableauCredentials`, an abstract class only the library derives), each secret also from a
  `Func<CancellationToken, ValueTask<string>>`, called at each sign-in, so a vault can rotate it:
  - `PersonalAccessToken(name, secret, impersonateUserId)` and `Password(name, password, impersonateUserId)`;
    impersonation is Tableau Server only, a LUID checked when the credentials are made;
  - `ConnectedAppDirectTrust(clientId, secretId, secretValue, userName, scopes)`: an HS256 JWT minted at each sign-in
    with `HMACSHA256.HashData` and `Base64Url`, no JWT package: header `alg`, `typ`, `kid` and `iss`; claims `iss`,
    `exp` five minutes ahead (Tableau refuses more than ten, so the clocks may be five minutes apart either way), a new
    `jti`, `aud` "tableau", `sub` and `scp` as an array;
  - `Jwt(token)` for OAuth 2.0 trust and any JWT minted elsewhere; `UnifiedAccessToken(token)`, Cloud only, 3.27,
    signed in with `isUat`.
  - Each kind is its own Sign In descriptor (password 1.0; a PAT 3.6, impersonating 3.11, Tableau Server 2021.1 with
    `tsm authentication pat-impersonation enable`; a JWT 3.17; a UAT 3.27 Cloud; impersonation Server), so the gates
    refuse a kind the server or its deployment lacks before any secret is read. A password's or a PAT's sign-in is
    retried like a read; a JWT's never, since Tableau refuses a `jti` it has seen and a late attempt may pass `exp`.
  - An impersonating sign-in that comes back as another user is signed out and fails as `Authentication`.
- **The session manager** (`Pipeline/TableauSessions.cs`), one per named client, in its state:
  - signs in on the first call that needs a session (a descriptor's `NeedsSession`), one sign-in at a time under a
    `SemaphoreSlim`, and holds the session and when to renew it as one immutable record, swapped whole;
  - callers that arrived before a sign-in failed take its failure rather than trying again each, so 20 calls with a
    wrong password make one refused sign-in, not 20 for a lockout to count;
  - signs in again five minutes before the session's own lifetime is up, 240 minutes on Server and 120 on Cloud;
    `ExpiresAt` shows that, or a PAT's sooner end by its `estimatedTimeToExpiration` (`DAYS:HH:MM`, the token's own
    lifetime), whose end then comes as a 401: renewing by the token's time would sign in on every call in its last
    minutes, each ending the session before. A renewal that fails before `ExpiresAt` leaves the call in the session it
    has, and is tried again a minute later;
  - signs in again once after a 401 to a call in its own session, unless another call already did, and sends the call
    again, an upload's excepted;
  - a client from `Create` or the constructor over an `HttpClient` signs its session out when disposed, never throwing,
    within ten seconds: `DisposeAsync` awaits it; `Dispose` runs it on the thread pool and waits, so no caller's
    synchronisation context can deadlock it. A client from DI never signs out: the session is the name's, and the
    container's disposal of the name's singleton doesn't either, since the factory's handlers may be gone by then and a
    host's shutdown shouldn't wait on Tableau.
- **`TableauSession`**, a sealed record with `required` init members `SiteId`, `SiteContentUrl`, `UserId`, `ExpiresAt`,
  `Deployment` and `ApiVersion`, and `UserName` (F27: the user's, the PAT's name, a JWT's `sub`, the first session's
  after Switch Site). The token is internal, and `PrintMembers` is written out to leave it out (F34).
- **The explicit API:** `client.Sessions.SignInAsync(credentials, site)`, `SwitchSiteAsync` (Server only, the old token
  ended) and `SignOutAsync`, on sessions the caller holds and the client never signs out. `client.WithSession(session)`
  is a client over the same connection and state that calls in that session and never signs in: a 401 throws
  `Authentication`, disposing it signs nothing out, and disposing its client ends it.
- **The pipeline:** `X-Tableau-Auth` on each request alone; `{siteId}` in a template filled from the session; a call
  refused for its arguments is refused before a sign-in. A sign-in's or Switch Site's exception keeps no
  `ResponseBody` (a descriptor's `KeepsBody`). When a caller's `HttpClient` followed a redirect elsewhere with a token,
  the call fails as `Redirect` and that session is signed out; the client's own signs in again on the next call.

### Paging and filters (S12)

As S12 built it (#45, both doors agreed there):

- `ListAsync(options, cancellationToken)` pages lazily with `[EnumeratorCancellation]`, through the virtual
  `ListPageAsync`, so a mock that overrides the one serves the other. `ListPageAsync(pageNumber, options,
  cancellationToken)` returns `Page<T>(IReadOnlyList<T> Items, int PageNumber, int PageSize, int? TotalAvailable)`, the
  numbers as Tableau answered them, the requested ones when it sends no pagination (Get Favorites for User).
- Paging stops after the page that reaches `TotalAvailable`, or after a short or empty page, so it never asks for a
  page past the end: Tableau Server 2025.1 answers one with 400006 for groups, 400117 for projects, and an empty page
  with a total of 0 for users. Page size 1 to 1,000 (403014 above), 100 by default, checked before sending; the page
  number from 1. Lists are never null; a list wrapper missing from a 2xx body is an empty list (F20). A server that
  answers an earlier page than the one asked for, or, for favorites, which carry no pagination, a page that starts with
  the page before's first item, fails the list as `InvalidResponse`, where reading on would repeat that page without
  end.
- **Stable order:** a list the caller doesn't sort sends a sort on fields that keep its pages apart, which Tableau
  Server 2025.1 took: projects `createdAt:asc,name:asc`; users `name:asc,domainName:asc` on Tableau Server and
  `name:asc` on Tableau Cloud, where a user's domain names how they sign in and nothing here shows Cloud sorts by it;
  groups `name:asc`, which a group holds alone in a site. A sort the caller gives is followed by the default's fields
  it doesn't name, to break its ties. The server refused `luid`, `id` and `parentProjectId` in a sort. Query Sites
  ignores any sort, so sites come in Tableau's order. The sub-lists that take no options (a user's groups, a group's
  users, favorites) come in Tableau's order, 100 a page.
- **Options records,** one per resource: `ProjectListOptions`, `UserListOptions`, `GroupListOptions` with `Filter`,
  `Sort` and `PageSize`; `SiteListOptions` with `PageSize` alone (below).
- **Fields:** `TableauField`, made only by the library, in `ProjectField`, `UserField` and `GroupField`, each the
  reference's list for its resource. A list refuses a field of another resource, by its options, before sending.
  Operators aren't checked per field: the reference's tables and the server disagree (`like` works on user names,
  `isLocal` on users fails), and the server's 400065 names the problem.
- **Filters:** `TableauFilter.Eq` (string, bool, long, date), `CiEq`, `In` (`[a,b]`), `Has`, `Gt`, `Gte`, `Lt` and
  `Lte` (long, date), joined with `And`; `FromString` for an expression written by hand, unchecked. A filter renders
  Tableau's syntax with values as written, and the pipeline's rule escapes the parts between `:` and `,` on the wire,
  so `R&D`, `C++`, `#` and `%` arrive as written: the live suite finds a project named `R&D C++ #1 100%` by an `eq`
  filter on Tableau Server 2025.1, though the reference says a filter can't hold `&` even encoded. A comma, which the
  server refuses even as `%2C` (400065), is refused in a value. Dates go in UTC as `yyyy-MM-ddTHH:mm:ssZ`, in the
  invariant culture. `Eq` and `In` also take `SiteRole`, the generated enumeration S12's fields hold (agreed on #45);
  a later area adds the ones its fields hold. Filters and sorts compare by their expression and fields, so list
  options built alike are equal.
- **Sorts:** `TableauSort.Asc` and `Desc`, chained with `Then`.
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
  - `dotnet run eng/generate-models.cs` writes `src/Tableau.Api/Rest/Models/Generated/`, in the namespace
    `Tableau.Api.Rest.Models` (S8 moved it there from `Models/Generated/`: progress log).
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
- **AOT:** `IsAotCompatible` is on from S8. Old files are exempt through the legacy section until they are ported.
  - `tests/Tableau.Api.Aot` is published with native AOT in CI. Any warning fails it.
  - **What it roots until S17:** 2.0 keeps the root namespace `Tableau.Api`, so rooting a namespace would root the old
    client too, whose `XmlSerializer` and Newtonsoft.Json code can never pass. Instead a `TrimmerRootDescriptor`
    (`tests/Tableau.Api.Aot/roots.xml`) lists the 2.0 types by name. S8 starts it, each later step adds the types it
    brings, and S9's conventions test reads the same list, so the two cover the same types.
  - **A forgotten type fails a test:** from S8, a test fails on any public type of the library that is in neither
    `roots.xml` nor a fixed list of the 1.x types. That list only shrinks, as each port step deletes old types.
  - From S17, with the old client gone, the app roots every member of the library and the list goes.
  - `VerifyReferenceAotCompatibility` turns on in S17, the first step after Newtonsoft.Json leaves (S16).
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
- **Cookies:** with `UseCookies` off on the shared handler (S9), each Vizportal request carries its own session's
  `workgroup_session_id` cookie and its XSRF cookie and `X-Xsrf-Token` header, so two sessions on one client never
  mix. A request's `Cookie` header has no `Secure` attribute, so the rule is set by where they go: only to the base
  URL's scheme, host and port, and a Vizportal call follows no redirect.
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
  - `Live` and `ServerOnly` are inconclusive when `TABLEAU_LIVE` or `TABLEAU_SERVER_ONLY` is unset or empty; any
    value but 1 fails, so a typo can't skip a run meant for a site;
  - with the variable set and a setting missing, they fail and name what is missing.
- **Coverage:**
  - `Microsoft.Testing.Extensions.CodeCoverage` with Cobertura output; `XmlSchema.cs` and what source generators
    write into the library's `obj/` excluded, the generated models measured (S8, progress log);
  - `eng/coverage-gate.cs`, after WriteableFileProviders', with a line and a branch floor for `Tableau.Api` in
    `eng/coverage.json`;
  - the floors start at S7's first green run and only rise, and the gate refuses a floor lower than `origin/main`'s;
  - at 2.0.0: at least 90% of lines and 80% of branches.
- **Legs:** Linux and Windows; on Linux, one run under sv-SE, one under tr-TR, and a time zone that is not UTC (F10,
  F28).
- **Live suite** (S11): `live.yml`, by hand and nightly once the repository variable `TABLEAU_LIVE_NIGHTLY` is `true`,
  on `main` only, in the `tableau-live` environment, one run at a time, against the maintainers' own Tableau Server
  (#43; the Developer Program site was the first plan).
  - It signs in with the direct-trust app (`TABLEAU_LIVE_CLIENT_ID`, `TABLEAU_LIVE_SECRET_ID`,
    `TABLEAU_LIVE_SECRET_VALUE`, `TABLEAU_LIVE_USER`), or by hand with a PAT (`TABLEAU_LIVE_PAT_NAME`,
    `TABLEAU_LIVE_PAT_SECRET`); `TABLEAU_LIVE_SITE` unset is the default site. Its session tests run one at a time,
    since one PAT allows one session.
  - Every object is made in a project named after the run and deleted in cleanup; leftovers older than a day are swept,
    and only a top-level project with the suite's description and a run's exact name counts as one. Tableau Server
    lists a project from its index, a second behind a create and minutes behind a delete, so the suite waits for a new
    one and the sweep passes over one already gone.
  - A group can't be in a project, so the run's local groups (S12) are named after the run and the second each was made,
    in UTC, since a group has no description or date; the suite deletes each when done with it or at its end, and the
    sweep takes a local group of that exact name more than a day old.
  - Every live client sends through one `HttpClient` that keeps each session token it carries, renewals included, for
    the log check, and is disposed after the cleanup's client signs out through it.
  - With `TABLEAU_RECORD=1` each JSON body goes through `FixtureRecorder`, which fails closed: every string is invented
    but a LUID (mapped), a date, a number, a version, a time span and a short list of enumerations and messages; a key
    naming a token, a secret, a password, a JWT or a keychain, and everything under a secret's or a keychain's, goes;
    the run's host and settings are replaced; a body that isn't JSON is not written. At the suite's end a file still
    holding a live secret or setting is deleted and named. The folder is outside `Fixtures/`, for a maintainer to read
    and copy in.
  - It never runs on PRs, so forks never see its secrets; in a run that reached a site, the log check counts the lines
    it finds and shows none.

### CI and release

- **Every workflow:** actions pinned by SHA with a version comment; `permissions: contents: read`, raised per job with a
  reason; `persist-credentials: false`; `timeout-minutes`; only PR runs cancelled by a newer one.
- **`ci.yml`** on PRs, `main`, daily (auto-merged Dependabot PRs start no push run) and dispatch:
  - `Checks (Linux)`: the gate's quick lane, gitleaks installed against its SHA-256;
  - `Build and test (Linux)` and `Build and test (Windows)`: locked restore, build, tests with coverage and the gate,
    pack with package validation, `eng/package-check.cs` on the pack, and from S5 ApiCompat against 1.2.5;
  - from S8, `Native AOT (Linux)`;
  - from S3, the weekly package audit of `release/1.x` in `audit-1x.yml`, a workflow of its own on Windows, so it
    covers net48 and a failure leaves main's CI and its badge alone (schedules run only on the default branch);
  - `CI`, the required check, fails when any job above failed.
- **`release/1.x` has its own `ci.yml`** (S2a): on PRs to the branch and on every push to it, so each commit on the
  line, a tagged one included, has a push run of its own.
- **`pr-text.yml`:** `PR text` runs the gate's `text` lane on the title and description; it passes on Dependabot's PRs.
- **`codeql.yml`:** csharp and actions, `security-extended`, on PRs, `main`, weekly and by hand; from S3 the weekly run
  also analyses `release/1.x`, in a job that checks it out and uploads to that branch.
- **`dependabot.yml`:** nuget, github-actions and dotnet-sdk; weekly; a 7-day cooldown; minor and patch grouped.
  `dependabot-auto-merge.yml` turns on auto-merge for minor and patch once `CI` and `PR text` pass; a major waits for
  the maintainer.
- **`live.yml`:** S11.
- **`release.yml`,** on a `v*` tag, one per branch, modelled on WriteableFileProviders' (S2a writes the first, S18
  main's):
  1. **Verify:** the tag is on the line's branch (`release/1.x` for `v1.*`, `main` otherwise); the version equals the
     tag (the project's `<Version>` on 1.x, MinVer on main); CHANGELOG has the dated section.
  2. **CI:** a green push run of `ci.yml` for the tagged commit on that line's branch: `--branch release/1.x` on the
     1.x line, `--branch main` on main. A run still going is waited for; a failed run, or none, stops the release.
  3. **Pack:** nupkg and snupkg, package validation, `eng/package-check.cs`.
  4. **Publish:** environment `nuget`, which a maintainer approves (D11), trusted publishing through `NuGet/login`,
     push both.
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
| PublicAPI analyzer files | Package validation, ApiCompat and the committed generated file show every change; WriteableFileProviders dropped them too |
| Package validation alone for 2.0's breaks | Across the change of target it reports only PKV006, so ApiCompat in assembly mode carries the ledger |
| Strict LUID checks in 1.2.6 | Upload session ids are not LUIDs |
| Following same-origin redirects in 1.2.6 | More code in a patch; following none is safe and clear |
| A temporary private fork for 1.2.6 | Actions do not run there (to verify); the public text carries no walkthrough, and the window between PR and package is kept short |
| Vizportal as a separate package | D3, and D5 rules out new `Tableau.*` ids |
| WireMock.Net | Hand-written fakes give raw request lines and run on net48 |
| Multi-targeting netstandard2.0 in 2.0 | D6 |

## Compatibility

**1.2.6** keeps every 1.2.5 signature and the target, which package validation against 1.2.5 proves with no
suppressions. What changes for a caller is in its CHANGELOG (above): escaped or refused inputs, double encoding of
pre-encoded filters, failed redirects, any 2xx accepted, a time outside a day refused, `UpdateTo*Schedule` refused.

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
| Dependencies | Newtonsoft.Json and `Microsoft.Extensions.Logging` leave. Added: `Microsoft.Extensions.Logging.Abstractions`, `.Http`, `.Options` and `.DependencyInjection.Abstractions` (10.x), and Polly.Core (8.x) for the retries; not `Microsoft.Extensions.Http.Resilience`, whose `Microsoft.Extensions.Telemetry.Abstractions` turns off the runtime's `LoggerMessage` generator in every app that takes it (S10) | None |

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
| S1 | main | CI and open-source files | F25 | #13 | #16 | done |
| S2a | 1.x | The `release/1.x` branch set up: CI, release, backstops, harness, metadata, package validation | none | #14 | #20 | done |
| S2b | 1.x | The 1.2.6 fixes, and the release | none on main; the tracker's 1.2.6 column | #15 | #21 | done: released as `v1.2.6` on 2026-10-30 |
| S3 | main | Security on main | F1 F2 F3 F6 F39 | #23 | #25 | done |
| S4 | main | Publish the review, after 1.2.6 | none | #26 | #27 | done |
| S5 | main | Build and layout for 2.0 | F24 F40 | #30 | #31 | done |
| S6 | main | Reformat, alone | none | #32 | #33 | done: squash `80c76d7` |
| S7 | main | Test platform and coverage | F26 F28 | #34 | #35 | done: squash `4ba9b69` |
| S8 | main | Models, generator, AOT | F14 F23 F50 | #37 | #38 | done: squash `ffc43f5` |
| S9 | main | Client core | F4 F16 F18 F20 F21 F22 F35 F36 F37 F38 F46 F47 F48 | #39 | #40 | done: squash `18c8404` |
| S10 | main | Resilience and downloads | F17 F19 F42 | #41 | #42 | done: squash `54b6764` |
| S11 | main | Sessions and the live suite | F27 F34 | #43 | #44 | done: squash `eb79b4b` |
| S12 | main | Port: server, sites, projects, users, groups, favorites; paging and filters | F13 F33 | #45 | #46 | in review |
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
    project cannot build), `pr-text.yml`, `codeql.yml`, `dependabot.yml`, `dependabot-auto-merge.yml`;
    `eng/package-check.cs`, after WriteableFileProviders', on every pack.
  - **Issue forms:** bug (Tableau.Api version, Server or Cloud, Tableau and REST versions), feature (endpoint, docs
    link), and a `config.yml` that sends security reports to private reporting.
  - **SECURITY.md:**
    - supported versions: 2.x all fixes once released; 1.2.x security fixes only (Q1); older none;
    - report through private vulnerability reporting;
    - fixes are described by what they change until the fixed package ships.
  - **The other files:** CONTRIBUTING.md (a short path to AGENTS.md; contributions under MIT); CHANGELOG.md (Keep a
    Changelog, `Unreleased`); README.md (the disclaimer first; status: 1.2.x on nuget.org, 2.0 in progress on `main`,
    not for use yet; licence; "Tableau is a trademark of Salesforce, Inc.").
  - **Package metadata:** the licence expression and the GitHub URLs stay only in `Directory.Build.props`, where S0 put
    them. `Tableau.Api.csproj` gets a true description ending in the disclaimer, the packed README, `snupkg`, no
    `IncludeSource`, and `ContinuousIntegrationBuild` in CI. S5 moves the packaging to `src/Directory.Build.props`.
  - **Old tests:** the profane project description and "Preparing Azure VM" replaced in the old test project.
  - **Rule files:** AGENTS.md's "Hosted CI doesn't exist yet" line (How the work runs, Rule files).
- **Findings:** F25.
- **Tests:**
  - CI green on its own PR;
  - the gate's selftest covers the text lane on a clean and on a refused description;
  - `dotnet pack` holds the licence expression, the README and a snupkg, and `eng/package-check.cs` passes on it.
- **One-way doors:** the package description's wording (D5) and SECURITY.md's support statement (D6, Q1).
- **Done when:**
  - merged;
  - `CI` and `PR text` are required checks on `main`'s ruleset;
  - private vulnerability reporting is on (the maintainer's).

### S2a. The release/1.x branch (1.x)

- **Goal:** `release/1.x` is ready to take fixes and release them, with CI, the release workflow, main's secret
  backstops, a test harness, true metadata and package validation. Nothing in it reveals what S2b fixes.
- **Scope:**
  - **The branch:** `release/1.x` pushed at `b23503e`; this PR's branch is cut from it, and the PR targets it.
  - **Backstops and harness from `main`:** the `.gitignore` blocks, `.gitattributes`, `.githooks/`, `.gitleaks.toml`,
    `eng/gate.cs` (its `full` lane runs `dotnet test`, the net48 tests on Windows only), `global.json` with the test
    runner, `nuget.config`, LICENSE, a short AGENTS.md (the 1.2.6 patch, under Design).
  - **Workflows:** `ci.yml` on PRs and pushes, `pr-text.yml`, `codeql.yml`, and the line's first `release.yml` (CI and
    release).
  - **Build:** the branch's `Directory.Build.props` with the licence and the URLs; in `Tableau.Api.csproj` the version,
    `AssemblyVersion`, `FileVersion`, `LangVersion` 10 and the package metadata; package validation against 1.2.5 with
    no suppression file; ApiCompat's `package` mode against the 1.2.5 nupkg; `eng/package-check.cs`.
  - **Tests:** the old live test project deleted; `Tableau.Api.Tests/` becomes the offline project (MSTest 4 on
    Microsoft.Testing.Platform, net10.0 and net48) with `FakeTableauServer`, and tests of today's behaviour that fix
    nothing: a sign-in, a list, a chunked publish. The solution updated.
  - **Docs:** README for 1.x, SECURITY.md, CHANGELOG.md (`Unreleased`), a short `docs/releasing.md` for the line.
- **Findings:** none fixed; it carries F25's metadata onto the 1.x package.
- **Tests:**
  - CI green on the PR, and on the push run after the merge;
  - gate quick with `--base origin/release/1.x` and `GATE_REQUIRE_GITLEAKS=1`;
  - package validation against 1.2.5 passes with no suppressions, and CI fails when a suppression file is added;
  - ApiCompat's `package` mode passes against the 1.2.5 nupkg with no suppression input, and fails on a removed
    member even with a `NoWarn` in the project;
  - `eng/package-check.cs` passes on the pack;
  - the harness tests pass on net10.0 and net48.
- **One-way doors:** the version 1.2.6 (D1) and the package description with its disclaimer (D5), both decided. The
  API and the target stay 1.2.5's, which the two API checks prove.
- **Done when:** merged into `release/1.x`, with the push run green and the branch's ruleset requiring `CI` and
  `PR text`.

### S2b. The 1.2.6 fixes and release (1.x)

- **Goal:** Tableau.Api 1.2.6 on nuget.org: 1.2.5's API on netstandard2.0, with the fixes of the 1.2.6 table.
- **Scope:**
  - **Library:**
    - `TableauApiClient.cs`: the path handler, query building, redirects, the token per request, any 2xx, the base
      path;
    - the area files' path and query call sites;
    - `TableauApiClient_WorkbooksAndViews.cs` (filters), `_Revisions.cs`, `_JobsTasksSchedules.cs` (including the
      refused `UpdateTo*Schedule`), `_Authentication.cs` and `Models/TableauSession.cs`;
    - `Models/XmlSchema.cs` (enum values).
  - **Tests:** the fix tests below, in S2a's harness.
  - **Docs:** CHANGELOG's 1.2.6 section.
- **Findings (on 1.x):** F2, F3, F5 (data, image, PDF without filters), F6, F7, F9 (refused before sending), F10, F11,
  F12, F14, F15, F27, F39. None closes on `main`.
- **Tests,** offline, the library driven against `FakeTableauServer` on Linux (net10.0) and Windows (net10.0 and net48):
  - **Paths:** for every method that takes an id or a name, rows generated over every ASCII character: alone, inside a
    segment, at its start and at its end, and beside a non-ASCII character. Also empty and dot-only segments, an
    upload id with `:`, each of `! ' ( ) * ,` beside a non-ASCII character (such as "Équipe d'analyse" and "coût
    (estimé)"), and each Unicode bidi control character, alone and inside a segment. Only the empty and dot-only rows
    throw `ArgumentException` before any request; on net10.0 every other row sends exactly the escaped request line,
    with UTF-8 escapes in upper-case hex.
  - **Paths on .NET Framework:** a net48 test runs every one of those rows in AppDomains that target .NET Framework
    4.0, 4.6.1 and 4.8, the older URI parsing an app gets from its target.
    - Each domain first asserts a documented, harmless `Uri` difference of its target, the way S7's culture legs
      assert their culture: under 4.0, `%2C` is decoded; under 4.6.1, `%28` is decoded beside a non-ASCII character;
      under 4.8, it stays escaped. A domain that did not take its target's parsing fails there.
    - The test works out each row's expected outcome itself, not by hand: it runs 1.2.5's own `buildUri` on the raw
      value in the same domain. A row may throw only where that sends a path other than the one the value names, so
      1.2.5 did not reach it either; every other row must send the exact escaped path. A throw happens before
      anything is sent. So the test shows the patch's rule that no working call starts to throw.
    - Every row with `! ' ( ) * ,` sends, on every target.
    - The bidi rows matter here because targets before 4.7.2 strip bidi controls (Microsoft's 4.7.2 note); they prove
      the exact check catches any rewrite the parsing makes.
  - **Redirects:** 301, 302, 307 and 308 to a second fake server on another port. The call fails and that server
    records nothing, sign-in included.
  - **Queries:** `R&D`, `C++`, `#` and `%` in filter, sort and view-filter names, escaped. The query rows also run in
    the three domains, with a comma and a colon in a filter value beside a non-ASCII character; the test records each
    target's outcome, which the CHANGELOG lists as a known issue for targets older than 4.7.2 (the path check covers
    paths, not queries).
  - **View filters:** `vf_` on data and image; PDF without filters works.
  - **Schedules:**
    - create on 201, its time as `18:30:00` under a non-UTC time zone, `monthDay` sent, 24:00 refused;
    - a schedules response still fills `start` and `end`;
    - `UpdateTo*Schedule` throws `NotSupportedException`, and the fake server records no request.
  - **The other fixes:**
    - revision paging sent;
    - task lists with tasks and with `<tasks/>`; the task path;
    - a users page with `OpenID` parses;
    - subscription create on 200 and on 201;
    - `UserName` after each sign-in;
    - a base URL with `/tableau/`.
  - **Logs:** the suite's log lines hold no password, token or PAT secret.
  - **The API checks:** package validation and ApiCompat's `package` mode still pass against 1.2.5 with no suppressions.
- **One-way doors:** none of its own; S2a holds the version and the description. The behaviour changes are written in
  the CHANGELOG.
- **Done when:**
  - merged into `release/1.x` with CI green and `review-code` clean, its records in `.local/review/`, and the push run
    green;
  - a live smoke run by hand passes and is recorded in the PR: a short program outside the clone, with the PAT in
    environment variables, on the Developer Program site: sign in, list projects with a filter containing `&`, chunked
    publish of a small data source, download, delete. The PR says what Server-only behaviour, such as schedules,
    Cloud could not show;
  - `v1.2.6` is tagged on `release/1.x`, and `release.yml` finds that push run;
  - a maintainer approves the `nuget` deployment (D11);
  - nuget.org lists 1.2.6 with MIT, the README and the disclaimer;
  - the GitHub release says what changed.

### S3. Security on main

- **Goal:** `main` no longer accepts any certificate on Vizportal, and has 1.2.6's path, query and redirect rules,
  before the rebuild replaces that code.
- **Scope:**
  - `Tableau.Api/VizPortal/VizPortalClient.cs`, `Tableau.Api/TableauApiClient.cs` and the area files' call sites;
  - a new `tests/Tableau.Api.Tests/` (net10.0) with S2b's request-building tests adapted to main;
  - the gate: `CheckTests` in `eng/gate.cs` runs `dotnet test` on the new test project by name; `global.json` gains the
    test runner;
  - `ci.yml` runs the tests, and gains the weekly NuGet audit of `release/1.x`;
  - the rule files that offline tests outdate (How the work runs, Rule files).
- **Findings:** F1, F2, F3, F6, F39.
- **Tests:**
  - S2b's path, query, redirect and base-path rows, on main's methods, the path rows generated over all of ASCII;
  - Vizportal against a TLS fake with an untrusted certificate fails, and with `IgnoreSSLErrors` set it connects;
  - no Vizportal or REST request follows a redirect to another port;
  - gate full fails when a test fails (its selftest).
- **One-way doors:** none (`main` is unreleased).
- **Done when:** merged with CI and the gate running the new tests. It may merge while S2b's release waits for its
  approval; until 1.2.6 ships, its review records stay in `.local/review/`.

### S4. Publish the review

- **Goal:** the review, the research and the security review records become public, once 1.2.6 is on nuget.org.
- **Scope:**
  - `.local/review/` becomes `docs/reviews/2026-10-28-codebase.md`, `docs/reviews/2026-10-28-research.md` and S2b's
    and S3's review-code records, with local paths replaced by a note;
  - the private notes of this plan's review rounds 1, 2 and 4 (the scenarios of P3 and P15, and round 4's reason for
    the bidi rows) go with them;
  - AGENTS.md's "last release 1.2.5" becomes 1.2.6;
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
  - package validation with `CompatibilitySuppressions.xml` holding PKV006 alone;
  - ApiCompat in assembly mode against 1.2.5's netstandard2.0 DLL, with `eng/apicompat/1.2.5.xml` as the ledger, in CI;
  - `Repo.Library` in `eng/gate.cs` set to `src/Tableau.Api/Tableau.Api.csproj`;
  - `docs/migration-2.0.md` started with the six signatures `main` already broke (F24) and the target change;
  - the rule files the new layout and target outdate (How the work runs, Rule files), AGENTS.md's one-way doors among
    them.
- **Findings:** F24, F40.
- **Tests:**
  - CI builds and packs `Tableau.Api.2.0.0-alpha.0.N` for net10.0;
  - the first `dotnet pack` shows that package validation reports only PKV006 across the change of target, recorded
    in the PR;
  - ApiCompat runs, and only the reviewed suppressions pass;
  - S3's tests pass on net10.0, and gate quick builds the library at its new path;
  - a deliberate CA2021 cast fails the build outside the legacy section (gate selftest).
- **One-way doors:** target framework (D6), version scheme (D2), package id (D5), all decided; AGENTS.md records them.
- **Done when:** merged, with both suppression files reviewed.

### S6. Reformat, alone

- **Goal:** the hand-written code follows `.editorconfig`, in one commit that blame can skip.
- **Scope:** `dotnet format` (whitespace and style) over `src/` and `tests/`, generated code excluded. Nothing else.
- **Findings:** none.
- **Tests:**
  - build and tests unchanged;
  - the public API unchanged: ApiCompat in strict mode (`--strict-mode`, no suppressions) between the build of the
    PR's base and the build of its head, so an addition fails as well as a removal;
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
  - `eng/coverage-gate.cs` and `eng/coverage.json`, run by CI and by the gate's full lane after the tests;
  - the CI test jobs and the culture and time-zone legs;
  - the old `Tableau.Api.Tests/` deleted, with the map below;
  - `.git-blame-ignore-revs` with S6's commit;
  - the PR template's question on Server-only tests;
  - the rule files on tests (How the work runs, Rule files).
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
  - `src/Tableau.Api/Rest/Models/Generated/`, `TableauJsonContext` and the generated converters;
  - `IsAotCompatible` (`VerifyReferenceAotCompatibility` waits for S17);
  - `tests/Tableau.Api.Aot/` with its `TrimmerRootDescriptor` listing the 2.0 types (Models, AOT), and its CI job;
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
  - the pipeline (S3's path handler, the query builder, redirects, TLS, auth header, `UseCookies` off);
  - `Endpoint` descriptors and the registry; version negotiation and deployment detection;
  - `TableauException`; the logging;
  - `client.Server.GetInfoAsync` as the first ported call, with its old method deleted;
  - the dependency changes;
  - whether callers get `TableauJsonContext` or their own: the public models carry `[JsonConverter]` attributes that
    name internal converters, which a caller's own source-generated context may not be able to use (SYSLIB1220, not yet
    verified);
  - a conventions test over every public method of the 2.0 types, read from the AOT app's root list, since 2.0 shares
    the root namespace with the old client, which keeps its shape until it goes: the `Async` suffix, the token last,
    the return shapes. S8's root-list test keeps that list complete;
  - the rule files on tokens, gates and versions (How the work runs, Rule files).
- **Findings:** F4, F16, F18, F20, F21, F22, F35, F36, F37, F38, F46, F47, F48. S3's tests for F2, F3, F6 and F39 move
  onto the new pipeline.
- **Tests:**
  - **Gates:** one row per descriptor, below its version and on the wrong deployment, throwing and sending nothing; the
    floor, with and without the opt-in.
  - **Redirects:** a same-origin redirect followed; another origin, and https to http, refused, with nothing reaching
    the target.
  - **TLS and http:** an untrusted certificate refused; the opt-out connects and logs its warning; http refused
    without `AllowInsecureHttp`.
  - **Cookies:** a `Set-Cookie` from one response is not sent on the next request.
  - **Request shape:** escaping rows, the base path, any 2xx.
  - **Errors:** a Tableau error body, an HTML body cut to 4 KB, an empty body, a missing element.
  - **Logs:** no secret, and no query value at Debug.
  - **DI:** keyed and default registration; options refused at start-up.
  - **Conventions:** the test above.
  - **Live:** Server Info on the Developer Program site.
- **One-way doors:** the client API shape (sub-clients, verbs, options, DI methods, `TableauException`, the mocking
  pattern) and the supported floor (D8, Q4): a maintainer's yes on the issue.
- **Done when:** merged with that yes.

### S10. Resilience and downloads

- **Goal:** retries that are safe, 429 honoured, long transfers that do not time out, and downloads that stream.
- **Scope:** the resilience pipeline for DI and the factory; its options; the descriptor in the request options; a
  builder method for the caller's own handlers (a proxy, a client certificate, a private certificate authority, a
  delegating handler), an addition, since S9 clears the handlers an app gives every `HttpClient` from the named client.
  That clear is a per-name action that runs where it was registered, so a second `AddTableauClient` for the same name
  would clear the handlers S10's builder added after the first: S10 orders the two, or registers the clear once;
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
- **One-way doors:** `TableauDownload` and the resilience option names: a maintainer's yes on the issue.
- **Done when:** merged with that yes.

### S11. Sessions and the live suite

- **Goal:** one session manager for every sign-in, the explicit session API, and a nightly live suite.
- **Scope:**
  - `TableauCredentials` and the DI builder methods;
  - the session manager and `TableauSession`;
  - `client.Sessions` and `client.WithSession`; the old authentication methods deleted;
  - `live.yml`, the live settings (`TABLEAU_LIVE_*` variables), the run project and its sweep;
  - the recording handler;
  - finish-work's live suite (How the work runs, Rule files);
  - from S9's review: the sign-in descriptor keeps no `ResponseBody` on its exception, or it would hold the token; a
    caller's `HttpClient` whose handler follows redirects sends `X-Tableau-Auth` before the pipeline sees the redirect,
    so S11 decides to refuse such a client or warn; sign-out on dispose only for a client that owns its state, never a
    DI transient.
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
- **One-way doors:** the credentials and session API: a maintainer's yes on the issue (#43).
- **Done when:**
  - merged with that yes;
  - the maintainer has set up the app and `tableau-live` on their Tableau Server (Q2; done before the merge);
  - the first run of `live.yml` is green, and `TABLEAU_LIVE_NIGHTLY` turns the nightly run on.

### S12. Port: server, sites, projects, users, groups, favorites

- **Goal:** these areas on the 2.0 client, with paging and the filter and sort builder.
- **Scope:**
  - their sub-clients; `Page<T>`, `ListAsync`, `ListPageAsync`, `TableauFilter`, `TableauSort` and typed fields;
  - the old Sites, Projects, UsersAndGroups and Favorites partials deleted, with `DeleteProject2`;
  - live fixtures; the migration name map's rows;
  - each descriptor's `Idempotent` set by what the call does, not its method, since the retries read it (S10): a PUT
    that updates a resource is idempotent, but Append to File Upload, a PUT that adds a chunk (S17), is not, and is
    never retried.
- **Findings:** F13, F33. Update User also gains the documented `email`, `idpConfigurationId`, `language` and `locale`.
- **Tests:**
  - **Every method:** verb, path, query, golden body and status; fixture parse; an empty list gives `[]`; the gate
    rows.
  - **Numbers in requests:** the first request with an `xs:integer` value, such as Update Site's `revisionLimit`,
    confirms on a site that Tableau takes a JSON number there, since the models write one where Tableau sends a string.
  - **Paging:** stops at `TotalAvailable` and on a short page; cancellation stops it.
  - **Filters:** escaping rows (`R&D`, `C++`, `#`, `%`, in-lists).
  - **Live:** create, list, update and delete a project and a local group in the run's scope, and add the suite's own
    user to the group and remove it; users read only (#45: the maintainers' server takes its users from a directory,
    so the suite makes none).
  - **ServerOnly, by hand:** sites create and delete; Active Directory groups; a user added, updated and removed.
- **One-way doors:** the paging and filter API, the first use of the patterns later steps copy, and the five
  sub-clients with `FavoriteTarget`: a maintainer's yes on #45.
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
- **One-way doors:** the generic content reference and the metadata API: a maintainer's yes.
- **Done when:** merged with that yes.

### S14. Port: workbooks, views, data sources, revisions

- **Goal:** these areas on the 2.0 client, with one view-filter builder, revision paging and streamed downloads.
- **Scope:**
  - their sub-clients;
  - exports (data, image, PDF, crosstab) through one options type with view filters, `maxAge`, size and orientation
    (one orientation enum);
  - downloads through `TableauDownload`, from the pipeline's `DownloadAsync`, each descriptor `TransferKind.Download`
    (S10); a live run checks what Tableau sends for an export's `Content-Disposition` and `Content-Type`;
  - updates from options records;
  - the old WorkbooksAndViews, Datasources and Revisions partials deleted.
- **Findings:** F5, F7, F32.
- **Tests:**
  - `vf_` on every export from one helper;
  - revision paging;
  - an update sends no empty nested object (golden bodies);
  - downloads stream;
  - live: list, get and download a published sample; export view data, an image and a PDF.
- **One-way doors:** the export options type: a maintainer's yes.
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
- **One-way doors:** `CloudSchedule` and the schedule API: a maintainer's yes.
- **Done when:** merged with that yes.

### S16. Vizportal

- **Goal:** Vizportal on the shared core, fixed, documented as unofficial.
- **Scope:** `client.VizPortal` and its models (Design); the session cookie and XSRF header set on each request;
  Newtonsoft.Json removed; the old `VizPortal/` folder replaced; the guide page; `[Experimental]` with its diagnostic
  id (Q3); the rule files on Newtonsoft.Json (How the work runs, Rule files).
- **Findings:** F29, F41, F49.
- **Tests:**
  - request bodies camelCase and compact, tags quoted by the serialiser;
  - paging by items returned, with the caller's query unchanged and two concurrent enumerations independent;
  - the token stops paging; unknown enum values parse;
  - two sessions calling at once on one client each send their own session token and XSRF value;
  - the session cookie and the `X-Xsrf-Token` header reach only the base URL's scheme, host and port: a redirect to
    another port, or from https to http, is not followed, and the second fake server records nothing;
  - TLS and redirect rows on the Vizportal path;
  - live on Cloud if the token opens a session there, else `ServerOnly` by hand. The versions tested go into the docs.
- **One-way doors:** the Vizportal surface: a maintainer's yes.
- **Done when:** merged with that yes and the tested versions written down.

### S17. Publishing, and the last old code

- **Goal:** publishing by size, as a job, with progress and cancellation; the old client gone.
- **Scope:**
  - `PublishAsync` on workbooks and data sources;
  - the `XmlWriter` payload, and the response reader if needed;
  - the bounded chunk reader;
  - the old Publishing partial, `TableauApiClient`, `PartialStream`, `XmlSchema.cs`, `SchemaExtensions` and the 1.x
    enums, exceptions and attributes deleted;
  - the AOT app's publish path; the app roots every member of the library from here, its root list goes, the
    conventions test covers the whole library, and `VerifyReferenceAotCompatibility` turns on, Newtonsoft.Json having
    left in S16;
  - the AOT app's packages locked: `KnownILCompilerPack` pinned, as `src/Directory.Build.targets` pins the ILLink pack,
    and a lock file beside it;
  - AGENTS.md's Traps rows whose guards were the deleted code (How the work runs, Rule files);
  - the duplicates S9 left beside the old client: `TableauHttp.BasePath` (now `RequestUri.BasePath`) and the old
    `appendQueryValue`, which go with it.
- **Findings:** F30, F43, F45 (its endpoint list goes to the steps and follow-ups in the Research tracker).
- **Tests:**
  - one request at 64 MB and below, chunks above (set small in tests);
  - a non-seekable source;
  - progress reports;
  - cancellation abandons the upload;
  - `asJob` returns a job;
  - golden payloads;
  - live: publish and delete a small workbook and data source, and one over a chunk;
  - the AOT job, rooting the whole library, publishes with no warning.
- **One-way doors:** the publish API: a maintainer's yes.
- **Done when:** merged with that yes; no file of the 1.x client remains.

### S18. Launch prep and 2.0.0-rc.1

- **Goal:** 2.0.0-rc.1 on nuget.org with complete docs, after a final whole-repo review.
- **Scope:**
  - **Docs:** the README rewritten (disclaimer first, install, quick start, Server and Cloud, Vizportal caveat,
    versioning and support); `docs/guide/` (sign-in and sessions, paging and filters, downloads and publishing,
    errors and resilience, DI and options, AOT, Vizportal); the migration guide's final pass; CHANGELOG; AGENTS.md's
    one-way doors final, and every rule file's final pass.
  - **Presentation:** the package icon, the project's own and never a Tableau logo.
  - **Release:** main's `release.yml`; `docs/releasing.md`.
  - **Review:** a deep `review-code` of the whole repo (`docs/reviews/`) and its fixes.
  - **Checks:** the legacy section empty, every floor at its target.
  - **Tag:** `v2.0.0-rc.1`.
- **Findings:** none new; the final review's go into this plan.
- **Tests:**
  - every suite;
  - the AOT job;
  - package validation and ApiCompat, with every suppression in the guide;
  - the nightly live suite green on the rc's commit;
  - the `ServerOnly` suite run by the maintainer on a Server.
- **One-way doors:** the version `2.0.0-rc.1` (D2) and the 1.x end date (Q1): a maintainer's yes.
- **Done when:** the rc is on nuget.org after a maintainer approves the deployment (D11).

### S19. 2.0.0

- **Goal:** 2.0.0 on nuget.org.
- **Scope:** CHANGELOG dated; SECURITY.md's table for 2.x; the tag `v2.0.0`; the follow-up issues checked against the
  Research tracker.
- **Findings:** none.
- **Tests:** the same as S18, on the tagged commit.
- **One-way doors:** the version 2.0.0: a maintainer's yes.
- **Done when:**
  - 2.0.0 is on nuget.org after the approval;
  - the GitHub release is out;
  - the Later items are opened as issues.

## Tests

| Suite | Runs | Where |
|---|---|---|
| 1.x offline | PRs to and pushes on `release/1.x`, from S2a | Linux (net10.0), Windows (net10.0 and net48) |
| 1.x paths and queries on .NET Framework | the same runs, from S2b | Windows, net48, in AppDomains targeting .NET Framework 4.0, 4.6.1 and 4.8, each asserting first that it took its target's URI parsing; each row's expected outcome worked out from 1.2.5's own `buildUri` in the same domain |
| 1.x API checks | the same runs, from S2a | package validation and ApiCompat's `package` mode against 1.2.5, no suppressions |
| 1.x NuGet audit | weekly, from S3, `audit-1x.yml` on `release/1.x` | Windows |
| Offline unit (`FakeTableauHandler`) | every PR, `main`, daily | Linux and Windows |
| Pipeline (`FakeTableauServer`, with TLS) | every PR | Linux and Windows |
| Culture and time zone | every PR | Linux: sv-SE, tr-TR, a non-UTC zone |
| Native AOT | every PR, from S8 | Linux |
| Coverage gate | every PR, from S7 | Linux and Windows |
| Live (`Live`) | nightly once `TABLEAU_LIVE_NIGHTLY` is `true`, and on dispatch, `main` only, from S11 | the maintainers' Tableau Server, by its direct-trust app (#43) |
| Server only (`ServerOnly`) | by hand; each PR that touches a Server-only call says whether it ran | a Tableau Server the maintainer has |

## Docs

- **Repo files:** README, SECURITY.md, CONTRIBUTING.md and CHANGELOG.md (S1, updated in S4, S18 and S19); LICENSE
  (S0).
- **Guides:** `docs/migration-2.0.md` (from S5, a section per step); `docs/guide/` (S18); `docs/releasing.md` (S18).
- **XML docs:** on every public member; CS1591 is an error outside the legacy section from S5.
- **Rule files:** AGENTS.md, REVIEW.md, the lens files and the skills, by the step that outdates them (How the work
  runs, Rule files), within AGENTS.md's 150 lines.
- **The 1.x line:** its own README, SECURITY.md, CHANGELOG, AGENTS.md and `docs/releasing.md` (S2a), and CHANGELOG's
  1.2.6 section (S2b).

## Risks

| # | Risk | Status |
|---|---|---|
| R1 | Tableau's JSON shape is undocumented: wrappers, single items, numbers as strings, envelopes | Mitigated: tolerant converters, fixtures recorded live per area; Server-only fixtures stay synthetic until recorded by hand |
| R2 | A multipart publish may not answer in JSON | Mitigated: `XmlReader` for the four publish responses, decided in S17 on the live site |
| R3 | The Developer Program site can be wiped, can run a pre-release Cloud, and lapses unless renewed | Moot from S11: the suite runs on the maintainers' Tableau Server (#43), so nightly runs show Tableau Server's behaviour, and Tableau Cloud's is checked by hand; a red night is triaged as Tableau's or ours; the run prefix and sweep |
| R4 | A REST token may not open a Vizportal session on Cloud | Checked in S16; if not, Vizportal is Server only and the docs say so |
| R5 | 1.2.6's escaping breaks callers who pre-encoded filters or relied on raw paths | Accepted: written in the CHANGELOG; the new behaviour is what the docs describe |
| R6 | 1.2.6's code is public on the PR before the package is on nuget.org | Mitigated (Q7): S2a sets the branch up and reveals nothing, so only S2b is public before the release; its PR, review, merge, tag and approval are kept close together, and its text says what changes, nothing more |
| R7 | Upload session ids are not LUIDs and contain `:` | Resolved: the path rule keeps `:` and `@`; strict LUID checks only where a descriptor says so |
| R8 | Server-only features never run in CI | Accepted (D9): by hand, and the PR says so |
| R9 | MIT needs the agreement of everyone whose code it covers | Q6 |
| R10 | The trademark guidelines discourage "Tableau" in a product name; the id has been used since 2018 | Accepted (D5): the disclaimer; a fallback id is ready if Salesforce objects |
| R11 | One PAT allows one session, so two sign-ins end each other | Mitigated: one sign-in at a time per client, direct trust in the live suite, its session tests one at a time, and a docs warning against sharing a PAT between processes |
| R12 | ApiCompat's suppression file lists most of 1.2.5's API | Accepted: reviewed per PR, grouped in the guide, deleted after 2.0.0 (Later) |
| R13 | Cloud rate limits on the nightly run | Accepted: a small serial suite, far under 40,000 calls an hour |
| R14 | Versions below the floor are allowed on opt-in but never tested | Accepted: the docs say so |
| R15 | The work spans many sessions, and context is lost between them | Mitigated: this file is the record, updated by every step |

## Owner calls

Q1 to Q6 are the plan's own; Q7 and Q8 came from plan review round 1, and Q9 from round 2. @serionist was told the
recommended answers to Q1 to Q5 on 2026-10-28 and did not object, so each stands as its recommendation. The rounds'
reports were not passed to the plan's author, so for Q7 to Q9 the Recommendation column gives the reason for the
answer, not the reviewer's words.

| # | Question | Recommendation | Answer |
|---|---|---|---|
| Q1 | Until when does 1.2.x get security fixes? SECURITY.md needs a date | 12 months after 2.0.0 ships, written as a date in S19; until then "security fixes only" | As recommended (default, 2026-10-28) |
| Q2 | "A reviewed environment": should `tableau-live` require reviewers? Then every nightly run waits for an approval | Deployment branch `main` only and no required reviewers, as WriteableFileProviders' `live-cloud`; dispatch needs write access anyway | As recommended (default, 2026-10-28) |
| Q3 | Mark the Vizportal API `[Experimental]`, so callers acknowledge it with a diagnostic id? | Yes: it states the risk in the compiler and costs callers one `NoWarn` | Yes (default, 2026-10-28) |
| Q4 | The exact floor: the research's support dates are unverified | S9 checks Tableau's published support dates; expected REST 3.23 (Server 2024.2) and Cloud; your yes on S9's issue | REST API 3.23 (Tableau Server 2024.2, in limited support until 2027-07-01 on tableau.com/support/services, read 2026-10-31) and Tableau Cloud, with `AllowUnsupportedServerVersion` below it and 3.29 the newest used: agreed by @serionist on #39 (2026-10-31) |
| Q5 | 1.2.6 scope. Beyond the review's list, 1.2.6 also takes F10's monthly day (the parameter exists in 1.2.5), every 3.29 enum value for F14, and the fix for `QueryViewPDF` without filters. F17's Content-Type check, F28 and F32 also qualify, and are left out | Keep those three for 2.0, so the patch stays small and ships sooner | As recommended (default, 2026-10-28) |
| Q6 | Starschema (named as Company in 1.2.5, co-owner on nuget.org) and László Nemes (most of Vizportal) hold rights in the code now licensed MIT | Get their agreement in writing, recorded on #1, before 1.2.6 ships under MIT | László Nemes agreed to MIT, recorded on #1; serionist confirmed the code is cleared for release as open source when the repo went public (2026-10-28) |
| Q7 | Split S2 into two PRs: one that sets `release/1.x` up and reveals nothing, and one with the fixes? | Yes: only the fixes' PR is public before the release | Yes (2026-10-29): S2a and S2b |
| Q8 | Does the session's standing approval to merge have limits? | Merge on green CI and a clean review; keep the one-way doors and the release's publish with the maintainers | Yes, with those two limits (2026-10-29): D11. D11 now has a third, a loosened check waiting for a maintainer, from P20 in plan review round 2 |
| Q9 | AGENTS.md had the maintainers agree on a one-way door. For #2, does either maintainer's yes count? | Yes, so one maintainer's answer unblocks a step, with the other free to object on the PR | Yes (2026-10-29). #11 then made it AGENTS.md's rule for every change: "after a maintainer agrees in its issue; the other can object on the PR before it merges". D11 cites it |

## Later

- **The research's P2 and P3 features,** one follow-up issue each (Research tracker), opened once this plan is approved
  and labelled for after 2.0.
- **`ts-api_3_30.xsd`** once published: one generator run, reviewed as a diff, in a minor.
- **Package validation baseline** moved to 2.0.0 after it ships, with ApiCompat's 1.2.5 ledger and
  `CompatibilitySuppressions.xml` deleted.
- **netstandard2.0 for 2.x** only if users ask; adding it later is a minor (D6).
- **Telemetry:** an `ActivitySource` and a `Meter` (P3.21).
- **A fallback package id,** ready in case Salesforce objects (RD6).
- **`TableauJsonContext` public,** if callers ask to serialise the models under native AOT: a caller's own context
  can't list them, since their converters are internal (SYSLIB1220, S9), and the envelopes would move to an internal
  context of their own. Adding it is a minor.

## Finding tracker

Status: `todo`, `done` (with the PR) or `wontfix` (with the reason). No finding is `wontfix`.

- **Step:** the one step that closes the finding on `main`. Its fix and the test or analyzer that guards it land there;
  later steps are built to that guard.
- **1.2.6:** whether S2b fixes it on the 1.x line too; n/a when 1.2.5 does not have it. A "no" is final: 1.x takes no
  signature change (D1), then security fixes only (D6).

| F | What closes it | Step | 1.2.6 | Status |
|---|---|---|---|---|
| F1 | Vizportal validates TLS certificates, with the same opt-out as REST | S3 | n/a | done |
| F2 | Every path segment validated and escaped: ids, tag names, site names | S3 | yes | done |
| F3 | Credentials and bodies never go to another host on a redirect; auth on each request | S3 | yes | done |
| F4 | Version and deployment gates that run, from descriptors, without reflection | S9 | no | done |
| F5 | View filters with `vf_` on every export, from one helper; `maxAge` | S14 | yes: data, image, and PDF without filters | todo |
| F6 | Every query key and value escaped; filter and sort escaped between separators | S3 | yes | done |
| F7 | Revision lists send their paging | S14 | yes | todo |
| F8 | Database and table lists paged | S13 | no | todo |
| F9 | Update Schedule with PUT on the schedule's id | S15 | refused before sending, since it would create a second schedule | todo |
| F10 | Schedule create: 201, `HH:mm:ss`, a checked time, `monthDay` | S15 | yes | todo |
| F11 | Extract refresh task lists by item type, empty when empty | S15 | yes | todo |
| F12 | Get Extract Refresh Task on its documented path | S15 | yes | todo |
| F13 | Active Directory group updates carry the group id | S12 | no | done (#46): `GroupsClient.UpdateFromActiveDirectoryAsync` and its job form PUT to `groups/{id}` |
| F14 | An unknown enum value never fails a response | S8 | yes: the missing values added | done |
| F15 | Any 2xx is success; the Cloud subscription form | S15 | yes: any 2xx | todo |
| F16 | Server and Cloud modelled; no silent version cap; Cloud and Server gates | S9 | no | done |
| F17 | Downloads streamed, under their own timeout, without a Content-Type crash, the response disposed | S10 | no | done: `TableauDownload`; the download methods come with their areas |
| F18 | One `HttpClient` per client, injectable, from `IHttpClientFactory`; disposable | S9 | no | done |
| F19 | Retries for idempotent calls only; 429 and `Retry-After` honoured and exposed | S10 | no | done |
| F20 | Lists never null; a missing element names the endpoint | S9 | no | done: the missing element; the lists with S12's paging, `Page<T>.Items` never null |
| F21 | A `CancellationToken` last on every method, Vizportal included | S9 | no | done: the conventions test; Vizportal built to it in S16 |
| F22 | One naming scheme and return shape, guarded by the conventions test; the name map | S9 | no | done |
| F23 | Models generated from a checked-in xsd by a repeatable generator; no hand edits | S8 | no | done |
| F24 | Target, version and package validation set once; the breaks listed | S5 | no | done |
| F25 | Licence, README, CONTRIBUTING, SECURITY, true package metadata | S1 | the 1.2.6 package gets the metadata | todo |
| F26 | Tests that build, run offline and cover the API | S7 | 1.x gets its own offline suite | done |
| F27 | The session's user name set after sign-in | S11 | yes | done (#44): `TableauSession.UserName` |
| F28 | Invariant culture and ordinal comparisons, tested under sv-SE and tr-TR | S7 | no (Q5) | done |
| F29 | The Vizportal schedule check sends camelCase | S16 | n/a | todo |
| F30 | Chunks read from any stream, asynchronously | S17 | no | todo |
| F31 | Add Workbook to Schedule returns the task; Add Data Source to Schedule | S15 | no; a known issue | todo |
| F32 | Updates omit nested elements the caller did not set | S14 | no (Q5) | todo |
| F33 | Update User sends the documented attributes | S12 | no | done (#46): `UsersClient.UpdateAsync` sends the set members of a `User`, the reference's nine among them |
| F34 | A session lifecycle: expiry, signing in again, no token in `ToString` | S11 | no | done (#44): the session manager |
| F35 | One exception type with the details (D8) | S9 | no | done |
| F36 | Logging without secrets or noise; Abstractions only | S9 | no | done |
| F37 | `ConfigureAwait(false)` on every await | S9 | no | done: CA2007 an error outside TableauLegacy |
| F38 | The accept-any-certificate switch renamed and warned about; plain http refused unless allowed | S9 | no | done |
| F39 | The base URL's path kept | S3 | yes | done |
| F40 | A strict build with no blanket `NoWarn`; nullable; the analyzers that matter as errors | S5 | no | done |
| F41 | Vizportal pages by items returned and leaves the caller's query alone; tolerant models | S16 | n/a | todo |
| F42 | Download file name and kind from the header or the content, cleaned | S10 | no | done |
| F43 | Dead code and duplicates gone with the old client | S17 | no | todo |
| F44 | The four NuGet_Prod metadata methods, escaped, with their siblings | S13 | no | todo |
| F45 | Publishing by size, as a job, with options; the listed gaps mapped in the Research tracker | S17 | no | todo |
| F46 | Immutable options and session; sub-clients made once | S9 | no | done: the options and sub-clients; the session is S11's record |
| F47 | Versions parsed as major.minor and compared whole; true messages | S9 | no | done |
| F48 | No rewriting of response bodies | S9 | no | done: the 2.0 pipeline reads the JSON as sent; the old client's rewrite goes with it in S17 |
| F49 | Vizportal tags quoted by the serialiser, the session cookie sent only to the base URL's origin, compact JSON | S16 | n/a | todo |
| F50 | Source-generated serialisation; `IsAotCompatible` with the AOT smoke test | S8 | no | done |

## Research tracker

**Decisions.** The research numbered its own D1 to D9; here they are RD1 to RD9.

| # | The research recommended | Outcome | Step |
|---|---|---|---|
| RD1 | JSON through source generation, models from the xsd, AOT | Taken (D7) | S8 |
| RD2 | A client bound to its session, with an explicit session API | Taken (D8) | S9, S11 |
| RD3 | Drop Vizportal from 2.0.0 | Overruled (D3): kept and fixed; its REST replacements table goes into the Vizportal guide page | S3, S16 |
| RD4 | net10.0 only; 1.x security fixes until a date; 1.x marked Legacy once 2.0.0 ships | Taken (D6). The date is Q1. The nuget.org deprecation is the maintainer's: after 1.2.6, versions up to 1.2.5 point to 1.2.6; 1.2.6 stays undeprecated as the .NET Framework line | S1, S5, S19 |
| RD5 | 2.0.0, SemVer, MinVer | Taken (D2) | S5, S19 |
| RD6 | Keep the id; disclaimer and attribution; no new `Tableau.*` ids; a fallback id; optionally ask Tableau for permission | Taken (D5). The fallback and the request are the maintainer's, under Later and For the maintainers | S1, S2a |
| RD7 | MIT, agreed in writing with Starschema and László Nemes | MIT taken (D4); the agreement is Q6, answered | S0 |
| RD8 | Test against maintained Server versions (about 2024.2, REST 3.23) and Cloud; older on opt-in | Taken (D8): REST API 3.23 and Cloud (Q4, #39) | S9 |
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
| Release through trusted publishing | S2a (1.x), S18 (2.x). The environment is `nuget`, not the research's `release` (D1) |
| Package validation against 1.2.5 | S2a (1.x, no suppressions), S5 (2.x, with ApiCompat for the ledger) |
| Launch docs and the migration guide | S1, S5, S18 |

## Plan review

Approved by @serionist on 2026-10-29, after round 4 came back approved. The approval rests on their answer of
2026-10-28, "Accept all: I write the plan with these decisions recorded, post it on #2, have a fresh agent review it,
and start the steps", and on the owner calls they have answered since (Q7 to Q9).

### Round 1

- **How:** a fresh reviewer agent (Claude Opus 5.5), with review-plan's method and lenses, on `6fbec9e`, 2026-10-29.
- **Verdict:** needs-fixes: 7 majors, 7 minors and 2 owner calls.
- **Now:** every finding below is fixed in the plan, the owner calls are answered, and every improvement has its
  answer. The round had majors, so round 2 reads these edits.

| # | Sev | Finding | Status |
|---|---|---|---|
| P1 | major | The 1.2.6 release could not find a CI run on its tagged commit: `release/1.x`'s `ci.yml` ran on PRs only, and a release modelled on WriteableFileProviders' looks for a push run on `main` | fixed: the branch's `ci.yml` also runs on every push to it; its `release.yml` waits for a green push run of `ci.yml` on `release/1.x` for the tagged commit; S2a writes that first `release.yml`, modelled on WriteableFileProviders' and adapted, and a short `docs/releasing.md` for the line (Design, CI and release; S2a) |
| P2 | major | `release/1.x` lacked main's secret backstops: 1.2.5's `.gitignore` ignores none of `.local/`, `.env` or `testsettings.json` (it does ignore `*.runsettings`), and its CI ran only the gate's text lane, so a secret could be committed there and pass | fixed: main's `.gitignore` blocks, `.gitattributes` and `.githooks/` on the branch; its CI runs `dotnet run eng/gate.cs -- quick --base origin/release/1.x` with `GATE_REQUIRE_GITLEAKS=1`; the skills checks pass over zero skills, and the tests check runs in the gate's `full` lane, not `quick` (the 1.2.6 patch; S2a) |
| P3 | major | The 1.2.6 path rule needs widening. Its scenario stays private until 1.2.6 is on nuget.org | fixed: every character outside RFC 3986's unreserved set is escaped except `:` and `@`; empty and dot-only segments are refused; the test rows are generated over all of ASCII (the 1.2.6 table; S2b, S3). Round 3: P23 |
| P4 | major | Nothing proved that 1.2.6 keeps every 1.2.5 signature, so a slip would reach .NET Framework users as a missing method at run time | fixed: package validation against 1.2.5 on the branch, and CI fails when a suppression file appears (the 1.2.6 patch; S2a, S2b). Round 2 closed a gap: P16 |
| P5 | major | From S8 the AOT app rooted every member and `VerifyReferenceAotCompatibility` was on, while the old `XmlSerializer` and Newtonsoft.Json code was still in the library, so the AOT job could not pass from S8 to S17 | fixed: the app roots only the 2.0 namespaces until S17 deletes the old client; `VerifyReferenceAotCompatibility` turns on in S17, after Newtonsoft.Json leaves in S16 (Models; S8, S17). Round 2 gave "the 2.0 namespaces" a mechanism: P17 |
| P6 | major | Package validation across netstandard2.0 to net10.0 reports only the dropped framework (PKV006), so the planned ledger of breaks would hold one line | fixed: S5 also runs ApiCompat in assembly mode between 1.2.5's netstandard2.0 DLL and the net10.0 build, with its own suppression file as the ledger; S5's first `dotnet pack` confirms the PKV006 behaviour (Build and layout; S5) |
| P7 | major | The shared primary handler keeps one cookie container, so a cookie one response sets rides on later requests, another session's included | fixed: S9 turns `UseCookies` off on the primary handler; S16 sets Vizportal's session cookie and XSRF header on each request, with a test that two concurrent sessions each send their own token (HTTP rules, Vizportal; S9, S16). Round 2 closed a gap: P18 |
| P8 | minor | Once any 2xx is success, 1.2.6's `UpdateTo*Schedule` would create a second schedule and report success | fixed: refused with `NotSupportedException` before sending, said in the CHANGELOG, with a test that nothing is sent (the 1.2.6 table; S2b) |
| P9 | minor | A shadow property for F10's time on the public model would stop schedules responses filling `start` and `end` | fixed: only the request is rewritten, with no `[XmlIgnore]` shadow on the public `DateTime`; a test that a response still fills both (S2b) |
| P10 | minor | 1.2.6's path handler needs C# 10, and netstandard2.0 defaults to 7.3; the assembly and file versions would stay 1.0.2.5; the layout credited `global.json`'s test runner to S0, which `main` lacks | fixed: S2a sets `LangVersion`, `AssemblyVersion` and `FileVersion`; S2a and S3 add the test runner to `global.json`; the layout says what each step adds |
| P11 | minor | The gate did not move with the code: its tests check passes with "no offline tests yet" whatever exists, its library path breaks with S5's move, and the coverage gate was outside it | fixed: S3 wires `dotnet test` into `CheckTests`, S5 updates `Repo.Library`, S7 adds the coverage gate to the full lane |
| P12 | minor | Steps outdated rule files without updating them: REVIEW.md Q7 and its `CancellationToken` line, both lens files, start-work's test path, finish-work's live suite, AGENTS.md's Traps and doors; and AGENTS.md holds 147 of its 150 lines | fixed: a table under How the work runs names the step that updates each, and a step that adds to AGENTS.md removes as much. Round 2 completed the table: P21 |
| P13 | minor | The plan's one-way-door rule ("the maintainer's yes on the issue") was not AGENTS.md's | fixed: stated as AGENTS.md writes it: a PR that names the door in its title, after the maintainers agree in its issue; either maintainer's yes counts. Round 2 found that is not what AGENTS.md says: P19, Q9 |
| P14 | minor | S2's smoke run needs the Developer Program site, which the plan set up only before S11; where its PAT lives, and what Cloud cannot show, went unsaid | fixed: joining the site moved to "For S1, S2a and S2b"; the smoke program lives outside the clone with the PAT in environment variables; each PR says what Server-only behaviour, such as schedules, Cloud could not show |

**Refuted:**
- *Exploit-level text in the plan:* none. The security rows say what changes, the vulnerable code is already public, and no attack example appears.
- *1.2.6 can't ship before `main` has CI and `release.yml`:* it can, apart from P1.
  - Tag-triggered workflows run from the tagged commit, and `pull_request` workflows from the PR's merge commit, so S2's PRs report both required checks.
  - The trusted-publishing policy has no branch filter, and `v1.2.6` matches the `nuget` environment's `v*` rule.
- *A 1.2.6 fix changes a public signature:* none, checked against `b23503e`. The changed request code is private, `dayToRun` and `intervalType.monthDay` exist, and the auth-setting enum holds only `ServerDefault` and `SAML`. P4 is about the missing check.
- *The mappings:* F1 to F50 each appear exactly once, and RD1 to RD9 and the 22 features are all mapped. Only the package icon was unmapped; it is an improvement now.
- *MinVer on `main` picking up `v1.*` tags:* no. Those tags aren't ancestors of `main`, and the minimum is 2.0.
- *Following no redirects in 1.2.6 breaks users:* it is deliberate, recorded under Not chosen and in the CHANGELOG.
- *Plan text that depends on the removed permission rules:* none, apart from the progress log's history rows.
- *The live suite's secrets reaching forks:* no. It never runs on PRs and is limited to `main`.
- *S5's legacy section loosens a check:* no. Overall it is stricter than today's `NoWarn` list.

**Owner calls:**

| # | Question | Recommendation | Answer |
|---|---|---|---|
| Q7 | Split S2 into two PRs? | Yes: S2a sets `release/1.x` up and reveals nothing; S2b adds the fixes | Yes, by @serionist (2026-10-29) |
| Q8 | Standing approval to merge: with what limits? | Merge on green CI and a clean review; keep the one-way doors and the release's publish with the maintainers | Yes, with those two limits, by @serionist (2026-10-29): D11. Round 2 added a third (P20) |

**Improvements:**

| Improvement | Answer |
|---|---|
| Reuse WriteableFileProviders' `eng/package-check.cs` in both lines' CI and release | taken: S1 on `main`, S2a on `release/1.x`, and both `release.yml` |
| Name the check behind S6's "public API dump" | taken: ApiCompat in strict mode between the builds of the PR's base and head |
| Limit S9's conventions test to the 2.0 namespaces | taken; round 2 made it the 2.0 types of the AOT app's root list (P17) |
| Let a large step land as several PRs, each with its own issue, under the step | taken (How the work runs) |
| A weekly NuGet audit of `release/1.x` | taken: a weekly job in main's `ci.yml` from S3, since GitHub runs schedules only on the default branch |
| The licence and URLs only in `Directory.Build.props` | taken: S1, S2a |
| The package icon | later: S18's presentation |

### Round 2

- **How:** a fresh reviewer agent (Claude Opus 5.5), with review-plan's method and lenses, on `890784c`, 2026-10-29. It
  read round 1's edits and the findings left open.
- **Verdict:** needs-fixes: 1 major, 7 minors and 1 owner call.
- **Round 1's findings:** P1, P2, P3, P6, P8, P9, P10, P11 and P14 are confirmed fixed. P4, P5, P7, P12 and P13 left
  gaps, recorded as P16 to P22. Four slips are corrected: round 1's P2 row named `*.runsettings`, which 1.2.5's
  `.gitignore` does ignore (line 9 at `b23503e`); P14's status named "For S1 and S2"; the 1.2.6 design put the gate's
  tests check in `quick`, where it is in `full`; D2 still had package validation carry the breaks, which ApiCompat
  carries.
- **Now:** every finding below is fixed in the plan, the owner call is answered, and every improvement is taken. The
  round had a major, so round 3 reads these edits.

| # | Sev | Finding | Status |
|---|---|---|---|
| P15 | major | The 1.2.6 escaping alone is not enough on some .NET Framework configurations. The scenario stays private until 1.2.6 is on nuget.org. Not sent to a separate refuter: the fix is cheap and leaves the design as it is | fixed: once the request URI is built, its escaped path must equal the path the handler wrote, or the call throws before sending; a net48 test runs the path rows under the older URI parsing an app gets when it targets .NET Framework 4.0 (the 1.2.6 table; S2b; Tests). S4 publishes this round's private note with round 1's. Round 3: the check was too strict as worded, P23 |
| P16 | minor | From P4: a `NoWarn` in the project also silences package validation's errors, so 1.2.6 could drop a signature and still pass | fixed: 1.x CI also runs ApiCompat's `package` mode on the packed nupkg against the 1.2.5 nupkg, with no suppression input; the tool reads no project property (the 1.2.6 patch; S2a, S2b) |
| P17 | minor | From P5: "the 2.0 namespaces" had no mechanism, and 2.0 keeps the root namespace `Tableau.Api`, so rooting a namespace would root the old client and fail the AOT job | fixed: a `TrimmerRootDescriptor` lists the 2.0 types, each step from S8 adds its own, and S9's conventions test reads the same list; from S17 the whole library is rooted (Models, AOT; S8, S9, S17) |
| P18 | minor | From P7: with `UseCookies` off, "the cookie is `Secure` on https only" cannot be built, since a request's `Cookie` header carries no `Secure` attribute | fixed: the session cookie and the `X-Xsrf-Token` header go only to the base URL's scheme, host and port, with no redirect followed, and a test shows a redirect target records nothing (Vizportal; S16; F49's row) |
| P19 | minor | From P13: "either maintainer's yes counts" is not what AGENTS.md says, which has the maintainers agree | fixed: owner call Q9, answered: for #2 either maintainer's yes counts, recorded in D11 as the maintainers' decision for this plan, with László Nemes free to object on any PR; "as AGENTS.md states the rule" is gone (How the work runs). Round 3: #11 made it AGENTS.md's rule, P24 |
| P20 | minor | D11 let the session merge a PR whose review records a loosened check, such as a `wontfix` blocker or an added ignore, with no maintainer seeing it | fixed: D11's third limit: such a PR waits for a maintainer's agreement in that PR (REVIEW.md, The harness) |
| P21 | minor | From P12: the Rule files table missed AGENTS.md's "Where things are" rows for what S1 adds, S7's `.git-blame-ignore-revs`, and "last release 1.2.5" once 1.2.6 ships; and S1 had no plan to stay within 150 lines | fixed: the rows added to S1, S4 and S7; S1 folds `LICENSE`'s row into one for the top-level files, extends the `.github/` and `eng/` cells, and rewrites the hosted-CI bullet, so AGENTS.md stays at 147 lines (How the work runs, Rule files) |
| P22 | minor | S2a listed no one-way doors, though it sets the version 1.2.6 and the package description | fixed: S2a lists both, decided (D1, D5); S2b no longer lists the version |

**Refuted:**
- *P10's assembly binding risk:* the assembly is not strong-named, so a new `AssemblyVersion` breaks no binding.
- *P2's "the gate cannot run on the branch":* it can.
- *P1's "no push run, or the wrong lookup":* a squash merge starts the push run, and neither the tag rule nor trusted
  publishing checks the branch.
- *F14's enum additions breaking binaries:* a moved value is CP0011, and with no suppressions allowed CI would fail.
- *D11 contradicting AGENTS.md:* it is an exception a maintainer answered, and #2 says "merged once CI is green".
- *ApiCompat across target frameworks:* it works, given the references of both sides.

**Owner calls:**

| # | Question | Recommendation | Answer |
|---|---|---|---|
| Q9 | For #2, does either maintainer's yes count for a one-way door, where AGENTS.md has the maintainers agree? | Yes, so one maintainer's answer unblocks a step, with the other free to object on the PR | Yes, by @serionist (2026-10-29): either maintainer's yes counts for #2, László Nemes can still object on any PR; recorded in D11 |

**Improvements:**

| Improvement | Answer |
|---|---|
| `LangVersion` 10, not `latest` | taken (the 1.2.6 patch; S2a) |
| F14's enum values appended after the existing members, since CP0011 catches a moved value | taken (the 1.2.6 table) |
| S3's `CheckTests` names the new test project, since the solution keeps the old netcoreapp2.1 project until S7 | taken (Security on main; S3) |
| On the branch, gate full runs the net48 tests only on Windows | taken (the 1.2.6 patch; S2a) |

### Round 3

- **How:** a fresh reviewer agent (Claude Opus 5.5), with review-plan's method and lenses, on `f2aba27`, 2026-10-29. It
  read round 2's edits and the findings left open.
- **Verdict:** needs-fixes: 2 majors and 3 minors.
- **Round 2's findings:** P16 to P22 hold, with no blocker or major. P15 is partly fixed: its check closes the private
  scenario, but as worded it was too strict, which P23 corrects.
- **Now:** the branch is rebased onto `main` with #11 (`8d69856`), every finding below is fixed in the plan, and the
  improvement is taken. The round had majors, so round 4 reads these edits.

| # | Sev | Finding | Status |
|---|---|---|---|
| P23 | major | The exact path check, as worded, would make working 1.2.5 calls throw on common .NET Framework targets. Apps that target 4.5 to 4.7.1 decode the escapes of some reserved characters, among them `! ' ( ) *`, when the URI also holds a non-ASCII character (Microsoft's 4.7.2 retargeting note, "Ensure System.Uri uses a consistent reserved character set"). An app built for 4.6.1, or an ASP.NET site with `httpRuntime targetFramework` 4.5, that queries the site "Équipe d'analyse" or deletes the tag "coût (estimé)" would throw in 1.2.6, though 1.2.5 worked, against the patch's own rule that no working call starts to throw | fixed: the handler leaves `! ' ( ) * ,` raw, as 1.2.5 sent them, since they are path sub-delimiters and never delimiters; it writes UTF-8 escapes in upper-case hex, as `Uri` does, so the exact check stays; only empty and dot-only segments throw as arguments; the Paths rows add each of `! ' ( ) * ,` beside a non-ASCII character; the net48 test runs AppDomains that target 4.0, 4.6.1 and 4.8 and writes each row's expected outcome, throw or send; the Tests table names the targets (the 1.2.6 table; S2b; Tests) |
| P24 | major | Q9 rested on one maintainer's word while AGENTS.md said "the maintainers agree", so every door PR would fail REVIEW.md question 9 | fixed by #11 (`8d69856`), which rewords AGENTS.md: a door changes "after a maintainer agrees in its issue; the other can object on the PR before it merges", with REVIEW.md question 9 and the PR template to match. D11, Q9's answer and How the work runs now quote that wording and cite #11; the plan no longer calls it the maintainers' decision for this plan. The third limit stays as it was |
| P25 | minor | The net48 test could pass without proving anything if a domain did not take its target's older parsing | fixed: each domain first asserts a documented, harmless `Uri` difference of its target, as S7's culture legs assert their culture: under 4.0, `%2C` is decoded; under 4.6.1, `%28` is decoded beside a non-ASCII character; under 4.8, it stays escaped (S2b) |
| P26 | minor | The CHANGELOG named only filters as values a caller may have encoded already, so a caller who encoded a tag or a site name would not learn that it is now encoded twice | fixed: the line names path values too: a tag, a site name, a content URL (the 1.2.6 patch, CHANGELOG) |
| P27 | minor | Q8's answer said "with those two limits", but D11 now has three | fixed: Q8's answer, in Owner calls and in round 1's record, notes that the third came from P20 in round 2; D11 says so too |

**Refuted or confirmed:**
- *The check misses a request:* no. `buildUri` is the only `new Uri` in the 1.2.5 library, so it covers every request.
- *The check breaks query strings:* no, as long as it compares the path only.
- *Upload ids with `:` break:* no.
- *F39's kept base path breaks:* no.
- *The AppDomain test cannot be built:* it can, on net48 under MSTest: `AppDomainSetup.TargetFrameworkName`, the test's
  `.exe.config` as `ConfigurationFile`, and a `MarshalByRefObject` runner over loopback.
- *Round 2's edits:* P16 to P22 hold with no blocker or major, and P21's merged rows fit the 150-line cap.
- *Exploit-level detail:* no edit adds any.

**Improvements:**

| Improvement | Answer |
|---|---|
| S8 to S16 must each add their public types to `roots.xml`: make it a check, a test that fails on any public type in neither the root list nor a fixed list of 1.x types that only shrinks | taken (How the work runs, Order; Models, AOT; S9) |

### Round 4

- **How:** a fresh reviewer agent (Claude Opus 5.5), with review-plan's method and lenses, on `f1ce466`, 2026-10-29. It
  read round 3's edits and the findings left open.
- **Verdict:** approved. No blocker or major is open, and Q1 to Q9 are all answered.
- **Round 3's findings:**
  - **P23, fixed.** The reviewer checked Microsoft's retargeting notes for 4.0, 4.5 to 4.7.1, 4.6.x, 4.7.x, 4.7.2 and
    later, and 4.8, with and without non-ASCII characters. No target escapes or rewrites any of the eight characters
    the rule leaves raw. Every other rewrite changes the path, so the check throws, and each such case is one where
    1.2.5 did not reach the path the value names either. The three domains cover the three documented behaviour
    classes.
  - **P24, fixed.** D11, How the work runs and Q9 quote AGENTS.md at `main` word for word; REVIEW.md question 9, the
    PR template and AGENTS.md's Never list agree.
  - **Security text:** clean.
- **Now:** the four minors below are fixed, and every improvement has its answer. The plan is approved.

| # | Sev | Finding | Status |
|---|---|---|---|
| P28 | minor | The 2.0 HTTP rules still gave 1.2.6's path rule in its round 2 form, so S9 could port the wrong rule | fixed: they say "with 1.2.6's rule and its exact path check (the 1.2.6 table, F2)", so S9 ports S2b's handler as it is; P3's row in round 1 points to P23 (Design, HTTP rules) |
| P29 | minor | The net48 test's expected outcomes were written by hand, so a row could be allowed to throw on a target where 1.2.5 worked | fixed: the test works each row's outcome out itself, by running 1.2.5's own `buildUri` on the raw value in the same domain; a row may throw only where that sends a path other than the one the value names; every row with `! ' ( ) * ,` sends on every target. So the test shows that no working call starts to throw (S2b; Tests) |
| P30 | minor | Under D11 nothing made "the other can object on the PR before it merges" happen, since the session merges without waiting | fixed: before merging a door PR, the session reads its comments and stops on any maintainer's objection (D11; How the work runs) |
| P31 | minor | F6's filter escaping can be undone on targets older than 4.7.2: a comma or a colon in a filter value may reach Tableau as a separator when the URI holds a non-ASCII character, and the path check does not cover queries. 1.2.5 behaved the same | fixed: the query rows run in the three domains with a comma and a colon in a filter value beside a non-ASCII character, and the CHANGELOG lists the outcome as a known issue for targets older than 4.7.2 (the 1.2.6 patch, CHANGELOG; S2b) |

**Refuted:**
- *Leaving `! ' ( ) * ,` raw reopens P3 or P15:* no. They are sub-delimiters, no `Uri` mode treats them as delimiters,
  and the check backstops any rewrite.
- *Targets 4.6.x to 4.8 each need their own domain:* no. Their retargeting notes have no `Uri` entries.
- *Upper-case hex and the check disagree after a decode and re-escape:* no. `Uri` re-escapes in upper case.

**Improvements:**

| Improvement | Answer |
|---|---|
| Every ASCII row also runs beside a non-ASCII character, and at the start and end of a segment, in all three domains | taken (S2b) |
| Rows with each Unicode bidi control character, alone and inside a segment, in all three domains | taken (S2b). Targets before 4.7.2 strip bidi controls (Microsoft's 4.7.2 note), so the rows prove the exact check catches any rewrite. The round's private note goes public with the review in S4 |
| A working-day wait on door PRs before merging | left: P30's read-before-merge covers objections, and the maintainers can add a wait later |

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
| 2026-10-29 | plan | Plan review round 1 (a fresh reviewer, on `6fbec9e`): needs-fixes, 7 majors and 7 minors, all applied (Plan review, Round 1). @serionist answered its owner calls: S2 splits into S2a and S2b (Q7), and the standing approval to merge has two limits (Q8, recorded as D11). Q1 to Q5 stand as recommended, unopposed since 2026-10-28. Round 2 is next. |
| 2026-10-29 | plan | Plan review round 2 (Claude Opus 5.5, on `890784c`): needs-fixes, 1 major and 7 minors, all applied, with round 1's four slips corrected (Plan review, Round 2). P15's scenario waits in `.local/review/` until 1.2.6 ships. @serionist answered Q9: for #2 either maintainer's yes counts for a one-way door, and D11 gains a third limit, for a loosened check (P20). Round 3 is next. |
| 2026-10-29 | plan | Rebased onto `main` with #11 (`8d69856`), which made AGENTS.md's door rule "after a maintainer agrees in its issue; the other can object on the PR before it merges". Plan review round 3 (Claude Opus 5.5, on `f2aba27`): needs-fixes, 2 majors and 3 minors, all applied (Plan review, Round 3). The 1.2.6 path rule now leaves `! ' ( ) * ,` raw and the net48 test covers three targets (P23, P25); D11 and Q9 quote #11 (P24). Round 4 is next. |
| 2026-10-29 | plan | Plan review round 4 (Claude Opus 5.5, on `f1ce466`): approved, with 4 minors applied (Plan review, Round 4): the 2.0 path rule points at 1.2.6's (P28), the net48 test works out each row's outcome from 1.2.5's own `buildUri` (P29), the session reads a door PR's comments before merging (P30), and filter values on older .NET Framework targets are a known issue (P31). @serionist approved the plan (status `approved`). S1 is next. |
| 2026-10-29 | S1 | Built on `13-ci-and-oss-files` for #13, with #14 and #15 opened for S2a and S2b: `ci.yml` (`Checks (Linux)`, `Build and pack (Linux)`, `CI`), `pr-text.yml` (`PR text`), `codeql.yml` (C# built by hand, the library alone, and actions), `dependabot.yml`, `dependabot-auto-merge.yml`, `eng/package-check.cs`, the issue forms, SECURITY.md, CONTRIBUTING.md, CHANGELOG.md, README.md, the package metadata, the two strings in the old tests, AGENTS.md. Departures: Dependabot has no `eng/` entry, since its NuGet updater reads no file-based app yet (dependabot/dependabot-core#15187) and eng/'s apps take no packages; the auto-merge job fails unless a ruleset on the base branch requires `CI`, because auto-merge with no required check merges at once; REVIEW.md question 1 and the review-code and finish-work skills drop their "until CI exists" wording, as the rule-file rule asks. The `DocumentationFile` paths that say netstandard2.0 stay: the pack carries the XML docs. Private vulnerability reporting and Dependabot alerts were already on. The PR is next. |
| 2026-10-29 | S1 | review-code round 1 on `daf2486`: clean, six minors, all fixed (`docs/reviews/2026-10-29-ci-and-oss-files-code.md`). Two departures from the Design: a .NET SDK bump in `global.json` doesn't auto-merge, because it raises every developer's SDK floor and CI installs the newest 10.0 SDK either way; and CI is Linux only until S3, whose offline tests bring the Windows leg along with the gate's selftest on Windows. |
| 2026-10-29 | S1 | Done: PR #16 merged as `1da97f3`, closing #13. Dependabot's first updates followed on `main`: the SDK to 10.0.402 (#17) and the NuGet group (#18). |
| 2026-10-29 | S2a | Done: PR #20 merged into `release/1.x` as `41b57e5`, closing #14. The branch starts at `b23503e` and has CI (the gate, the build, the tests on Linux and on Windows with net48, the pack with package validation and ApiCompat against 1.2.5, the package check), `release.yml`, `pr-text.yml`, `codeql.yml`, main's backstops, a short AGENTS.md, and the offline harness: `FakeTableauServer` and five tests of unchanged behaviour. The old live suite is deleted on the line. One review-code round, clean, four minors fixed (`docs/reviews/2026-10-29-release-1x-setup-code.md` on that branch). |
| 2026-10-30 | S2b | Done: PR #21 merged into `release/1.x` as `8635623`, closing #15, with the fixes of the 1.2.6 table and their offline tests (54 on net10.0, 60 on net48, the path rows also in AppDomains targeting .NET Framework 4.0, 4.6.1 and 4.8). Two review-code rounds (`docs/reviews/2026-10-30-1-2-6-fixes-code.md` on that branch): round 1 had one major, Tableau Cloud's 24-hour and 60-minute task intervals, which didn't parse, fixed with five minors; round 2 was clean. The live smoke check ran by hand on Tableau Server 2025.1.5 (REST API 3.25, driven as 3.14), signed in with a personal access token: projects and users listed, extract refresh tasks read, a project created, found by a filter and deleted. It ran on that server, not on the Developer Program site, and without the chunked publish and download; Tableau Cloud's task intervals are covered offline. `v1.2.6` was tagged, `release.yml` found the push run, a maintainer approved the `nuget` deployment, and nuget.org lists 1.2.6, with the GitHub release. #22 follows up a CI improvement. |
| 2026-10-30 | S3 | Built on `23-security-on-main` for #23, from `origin/main` at `4ea869b`. One handler for both clients (`TableauHttp`): certificates validated unless `IgnoreSSLErrors` is set, no redirects. 1.2.6's `RequestPath`, query escaping, redirect exception, per-request token and base path, ported to every call site, main's `QueryView`, `QueryViewCrosstabExcel` and `QueryViewPermissions` included. `tests/Tableau.Api.Tests/` with 46 tests on net10.0: S2b's fake server with a TLS mode, the path rows over all of ASCII, `EveryMethodPathTests` (125 path parameters), queries, redirects, base paths, both clients against an untrusted certificate. The gate's `tests` check and its selftest; in CI the tests on Linux and Windows, and the weekly package audit and CodeQL scan of `release/1.x`; the rule files. Each fix group, broken on purpose in a scratch copy, failed its tests, except two no net10.0 test can show: the per-request token, since each request has its own client, and `buildUri`'s exact path check, since .NET 10 rewrites no escaped path. Departures: the status checks stay exact and the view filter prefix stays as it is, since any 2xx (F15) and `vf_` (F5) are later steps; the Vizportal client also keeps the base URL's path, follows no redirect and escapes the method name its public paging methods take; the test project stays out of the solution, whose old project has its name (MSB5004), so `dependabot.yml` names its folder; `codeql.yml` gains a run by hand; the selftest shows that a failed `dotnet test` fails the tests check and gate full, without running a failing project. F1, F2, F3, F6 and F39 turn done in the tracker when the PR merges. |
| 2026-10-30 | S3 | Done: PR #25 merged as `a29ead8`, closing #23. Vizportal validates certificates as REST does, and main takes 1.2.6's request rules; 46 offline tests run in the gate's full lane and in CI on Linux and Windows. Review: two fresh reviewers, clean, three minors fixed (the release/1.x audit moved to `audit-1x.yml` on Windows; three rule files updated). Live: Vizportal's read calls passed against Tableau Server 2025.1.5 with certificates validated; a pre-existing Vizportal model bug is #24. |
| 2026-10-30 | S4 | 1.2.6 is on nuget.org, advisory GHSA-wqhr-qp8x-c5gm is published, and main has the fixes, so the review (F1 to F50), the research, the review records of S2a, S2b and S3, and the plan review's private notes go into `docs/reviews/` (#26). |
| 2026-10-30 | S4 | Done: PR #27 merged as `e13eabe`, closing #26. Two follow-ups merged the same day: #28 (`fbb10b9`, closing #24) reads Vizportal's data quality warnings as `DataQualityIndicator[]`, and #29 on `release/1.x` (`5fe86d1`, closing #22) gives that branch's ApiCompat step the reference assemblies, without which the tool misses an enum value that moved. |
| 2026-10-30 | S5 | Built on `30-build-and-layout` for #30, from `origin/main` at `fbb10b9`; @serionist agreed to its doors on #30 (net10.0 only, MinVer from 2.0, the id kept). The library moved to `src/Tableau.Api/` and `Tableau.Api.slnx` replaced the `.sln`, with the library and the offline tests; the old live suite is in no solution. Root `Directory.Build.props` (net10.0, Nullable, XML docs, warnings as errors, the code style in the build, latest-recommended, lock files locked in CI), `Directory.Packages.props`, `src/Directory.Build.props` (MinVer, prefix `v`, minimum 2.0; package validation against 1.2.5) and `tests/Directory.Build.props`. The csproj's `NoWarn` list is gone. `.editorconfig`: the plan's CA rules as errors; `XmlSchema.cs` marked generated; the TableauLegacy section lists the library's 57 files and relaxes exactly the 44 rules they break, to `suggestion`. The first `dotnet pack` (`2.0.0-alpha.0.59`) failed on PKV006 alone, which `CompatibilitySuppressions.xml` now holds. `eng/apicompat.cs` compares 1.2.5's DLL, checked by its SHA-512, with the Release build in assembly mode, each with its reference assemblies; its ledger `eng/apicompat/1.2.5.xml` holds 18 breaks: F24's six signatures (CP0002) and 12 `capabilityTypeName` members renumbered (CP0011), which F24 didn't list and which the tool misses without the references. A removed or a hand-added entry fails it, and CI on Linux writes the same ledger as Windows. The gate's selftest builds a copy of the build settings with a CA2021 cast in a new library file, which must fail with CA2021 alone. `docs/migration-2.0.md` started. Departures: the CA and SYSLIB rules are set for `src/` and `tests/` only, and `AnalysisLevel` and `AnalysisMode` apart, so the gate the hooks run keeps its own build; the comparison and its regeneration are a file-based app, run by CI after the pack, so a developer rewrites the ledger with one command on any OS, and 1.2.5's references come from `eng/apicompat/Baseline.csproj` (netstandard2.0, Microsoft.Extensions.Logging 2.1.1, a lock file); the tests relax three test idioms (no Async suffix, CA2007, CA1861) and S3's test code was fixed for the rest; `CompilerServices.cs` is deleted, as net10.0 has those attributes; every CI job that builds the library fetches the whole history for MinVer, and the package job refuses a shallow clone, where MinVer versions every build `2.0.0-alpha.0` without a warning; Dependabot reads the root's projects and `.config/dotnet-tools.json`. The legacy files' members read as never null to a caller until they are ported, since Nullable is on and they are not annotated. |
| 2026-10-30 | S5 | Done: PR #31 merged as `e0b3d06`, closing #30. review-code: two fresh reviewers, clean, two minors fixed (`docs/reviews/2026-10-30-build-and-layout-code.md`): the tool manifest moved to `.config/dotnet-tools.json`, the one place Dependabot reads one, and AGENTS.md packs into an emptied folder. Its improvement for the CA2021 probe went to S7. The two follow-ups before it stay closed: #22 by #29 on `release/1.x`, and #24 by #28 on main (S4's row). |
| 2026-10-30 | S6 | Done: PR #33 merged as `80c76d7`, closing #32. `dotnet format` whitespace and style over `src/`, `XmlSchema.cs` left alone and `tests/` needing nothing: indentation, the byte order mark of 54 files, `this.`, file-scoped namespaces, expression bodies, target-typed `new`. TableauLegacy drops the five IDE rules no old file breaks now (44 to 39). The base and head Release builds hold the same IL in all 2,668 method bodies, and ApiCompat in strict mode finds nothing. One fresh reviewer, clean, no findings; its record, `docs/reviews/2026-10-30-reformat-code.md`, lands with S7 so the squash stays the reformat alone, and its improvement, `dotnet format --verify-no-changes` in CI, is taken there. |
| 2026-10-30 | S7 | Built on `34-tests-and-coverage` for #34, from `origin/main` at `80c76d7`. Coverage: `Microsoft.Testing.Extensions.CodeCoverage` 18.12.0 in `tests/Directory.Build.props`, Cobertura; `tests/coverage.config` measures the library without `XmlSchema.cs`, by file, since three of its types are partial. `eng/coverage-gate.cs`, after WriteableFileProviders', fails a run below `eng/coverage.json`'s floors and a floor lower than `origin/main`'s; the gate's full lane runs the tests with coverage, then it; the selftest runs it on a scratch repo (below fails, at passes, lowered fails), and the CA2021 probe now builds with `src/Directory.Build.props` and the library's project file. Floors 91% of lines and 85% of branches, from the first green run (CI run 41137447526): every leg, Linux Release, sv-SE, tr-TR and Windows, measured 91.14% and 85.89%; a Debug build, the gate's, measures 91.86% and 86.36%. F28: every number written in the invariant culture, the media type, `vf_` and the XML declaration compared ordinally (77 CA1305, 2 CA1309, 3 CA1310 sites and the four `vf_` checks), and the three rules out of TableauLegacy (39 to 36); `CultureTests` fails without it. CI: Tests (Linux, sv-SE) in Asia/Kathmandu and Tests (Linux, tr-TR) in America/St_Johns, each with a canary that passed; the tests with coverage and the coverage gate on every leg; `dotnet format --verify-no-changes` in Checks (Linux), which catches a `this.` the build lets through; `CI` needs the new legs. The `Live` and `ServerOnly` categories in `TableauSite.cs`. The old `Tableau.Api.Tests/` is deleted, its map above as written. Tests 48 to 78: every Vizportal method's request by reflection, Vizportal paging and failures, the REST client's versions, errors and cancellation. Also `.git-blame-ignore-revs` (`80c76d7`), the PR template's Server-only question, S6's review record, and the rule files: AGENTS.md (148 lines), REVIEW.md question 3, finish-work, start-work, both lens files, `docs/plans/README.md`; `dependabot.yml` and the slnx named no old suite. Departures: no `FakeTableauHandler` yet, since the client builds an `HttpClient` per request and a handler has no seam until S9's pipeline; no fixtures folder or manifest, which S8's synthetic fixtures start; log capture is S3's `RecordingLogger`; no `Live` or `ServerOnly` test yet, the first coming with S9 and S11, so the categories are tested with an injected environment; F28's fix in the old files is here, as F28 closes in S7; the floors already pass 2.0.0's 90% and 80%, so every later step keeps at least that. |
| 2026-10-30 | S7 | PR #35, a draft. review-code round 1: two fresh reviewers, clean, with minors fixed on the branch. `EveryVizportalMethodTests` now pins each of the 41 methods' Vizportal requests, method and params, in a table: the reviewers' two mutations, `DeleteWorkbook` sending `deleteDatasources` and `ChangeWorkbookOwner` with its ids swapped, passed every test before and fail it now. The docs say a switch unset or empty is inconclusive and any value but 1 fails, as the code does. The gate's tests run in Release, as CI's do, since Debug measured 91.86% and 86.36% where Release measured 91.14% and 85.89%, and its coverage check repeats the floors the coverage gate says can rise. S5's and S6's records are dated 2026-10-30, the day they merged, and renamed so. Improvements taken: `[SiteTest]`, which checks the switch and the settings before a site test runs, with a test that every site test carries its category and every site category its `[SiteTest]`; `dotnet format --verify-no-changes` in the gate's full lane, as `format`; selftest cases for a lowered branch floor, an unknown base, no report and a floor that can rise, the scratch repo with `global.json`; REVIEW.md's harness list and its rule on loosening name `tests/coverage.config`; the duplicate `StartsWith('/')` gone; Vizportal's error summary uses the invariant code. Floors stay 91% and 85%: Release now measures 91.10% and 85.82%. A comments-only `.git-blame-ignore-revs` for `release/1.x` goes in a PR of its own there. |
| 2026-10-30 | S7 | Done: PR #35 merged as `4ba9b69`, closing #34, with the floors at 91% of lines and 85% of branches. |
| 2026-10-31 | S8 | Built on `37-models-generator-aot` for #37, from `origin/main` at `4ba9b69`; @serionist agreed to its door on #37, the public model shape. **Schema:** `eng/schema/ts-api_3_29.xsd`, downloaded 2026-10-31 (`ts-api_3_30.xsd` still 404), 160,752 bytes, its SHA-256 in `eng/schema/README.md`, stored `-text` so the hash holds on every OS. **Generator:** `eng/generate-models.cs` compiles it with `XmlSchemaSet` and writes 256 files: 202 public records, the internal `TsRequest` and `TsResponse`, 50 enumeration structs, 82 list converters in `ListConverters.g.cs`, and the context's type list; any construct it doesn't map, and any name that would clash, stops it. `-- --check` is the gate's quick-lane `models` check (0.6 s); the selftest runs the generator on a scratch repo: two runs write the bytes committed here, `--check` fails on an edited, a missing and a foreign file, a run deletes a stale one, an `xs:list` stops it. **Hand-written:** `TableauJsonContext`'s options (metadata only, numbers read from strings, nulls not written), `TableauListConverter<T>`, `TableauWrappedListConverter<T>`, and `TableauEnumConverter<T>` with `ITableauEnum<T>`. **AOT:** `IsAotCompatible` on the library; the old client breaks only IL2026 and IL3050 (`XmlSerializer` in `TableauApiClient.cs`, Newtonsoft.Json in three Vizportal files), relaxed in TableauLegacy; `src/Directory.Build.targets` pins the ILLink pack at 10.0.14, as WriteableFileProviders does, so an SDK patch doesn't fail a locked restore. `tests/Tableau.Api.Aot` (roots.xml, embedded fixtures, `JsonEquivalence.cs` linked from the tests) is published with native AOT in the new `Native AOT (Linux)` job, which `CI` needs; a probe proved the compiler analyses the rooted types the app never uses (an unused reflection call failed the publish with IL2026 and IL3050). Native on win-x64 here: 11 MB, every check passed. **Live check,** read-only on Tableau Server 2025.1 (REST API 3.25) with a PAT, shapes only, signed out after: no `tsRequest` or `tsResponse` wrapper, the sign-in accepted unwrapped; lists in their wrappers, as arrays even for one item; an empty list an empty wrapper (`"projects":{}`, a view's `"tags":{}`), an empty element an empty object (`"dataAccelerationConfig":{}`); xs:integer values as strings (pagination, `size`, `revisionLimit`, `serverPort`, the error's `code`), xs:int as numbers, xs:boolean as booleans, dates as ISO strings with `Z`; an element's text beside its attributes as `value` (`productVersion`); the error body `{"error":{...}}` on a 404. **Tests:** 78 to 146: every fixture round-trips; lists empty, of one, of many, a single object, null; numbers as strings; an unknown value kept; times; the extended type; every record and every enumeration value through the context by reflection; `ModelRootsTests` with the 232 old public types in `OldPublicTypes.cs`, which only shrinks. Coverage 92.57% of lines and 89.11% of branches, every new line and branch covered; the floors rise to 92% and 89%. ApiCompat's ledger is unchanged: the models are additions. **Departures:** the models are in `Tableau.Api.Rest.Models`, under `src/Tableau.Api/Rest/Models/`, not `Tableau.Api.Models`: the ten old Vizportal files import both `Tableau.Api.Models` and `Tableau.Api.VizPortal.Models`, whose `Workbook`, `User`, `Project` and `Schedule` would clash, so the REST models sit beside `Tableau.Api.VizPortal.Models` instead; `taskType` is `TableauTask`, since `Task` would hide `System.Threading.Tasks.Task` in every caller's file. A list is null when its wrapper is absent and empty when the wrapper is empty: defaulting it to empty would write an empty wrapper into every request (F32), so S12's client gives the never-null lists (F20). A list type with attributes, such as `dataSourceListType` with `includeAll`, stays a record that holds the list, or the attribute would be lost. The xs:integer family is `long`, xs:decimal `decimal`; an enumeration of numbers is the number (an interval's `hours`, which Tableau Cloud sends as 24 though the schema lists 1, 2, 4, 6, 8 and 12); a union of integers is `long`, any other union `string` (`monthDay`). The abstract `flowParameterDomainType` holds its extensions' members, since JSON has no `xsi:type`. The context is internal, seen by the tests and the AOT app through `InternalsVisibleTo`; whether callers get it is S9's. `roots.xml` roots the models' namespace whole, which only 2.0's types are in, not each type by name. A date is written as `+00:00`, the same instant as Tableau's `Z`. Coverage leaves out what source generators write into `obj/`, System.Text.Json's 43,440 lines of metadata, which would have outweighed the library's 2,623 and lifted branches to 92.55%; the generated models are measured. The generator's tests are in the gate's selftest, as the coverage gate's are. |
| 2026-10-31 | S8 | Draft PR #38. review-code round 1, three fresh reviewers on `6de5397`: no blocker or major, eight minors, all fixed, and every improvement taken; the record is `docs/reviews/2026-10-31-models-and-aot-code.md`. @serionist agreed on #37 to every departure from its words (the namespace, `TableauTask`, `Credentials`, nullable lists, list records, numeric enumerations as `long?`, `monthDay` as `string`, anonymous types named owner and element, the casing, unreachable subtypes left out), and on #38 to the coverage exclusion. **Fixes:** a subtype extended in turn stops the generator, since a base holds only its direct subtypes' members; `--check` fails on a folder in the output; `OldPublicTypes.Most` equals the list's count, and the gate's new quick-lane `old types` check refuses it higher than at the base; AGENTS.md back to 148 lines; the 13 password, token and secret attributes print as `***` in a record's `ToString`, from one list in the generator, with a test that every string member named like a secret is on it or on a list of names that hold none; `tableauCredentialsType` becomes `Credentials`, since S11's `TableauCredentials` lives in `Tableau.Api`, and a test fails on a model that shares a name with another public type outside the old client; a list with attributes of its own (`DataSourceList`, `ConnectorList`, `SalesforceAppList`, `ServiceTokenList`) reads `{}` as a record whose list is null, which its doc comment says and a test pins, since reading `[]` there would write an empty list back, or need a hook that only init-only members' backing fields could take; a default enumeration value fails to write, where it wrote JSON null. **Improvements:** one casing rule, a run of capitals as one word, for types, members and values, which renamed 13 names (`SiteOidcConfiguration`, `LinkUserIdp`, `ResyncTcmUserPayload`, `GroupAssertionsSamlEnabled`, `StorageUsedMb`, `OpenId`, `TableauIdWithMfa` among them) and clashed with nothing; the five flow parameter domain subtypes, which nothing names, are no longer written, so 197 records in 251 files; the generator compares the SHA-256 in `eng/schema/README.md` with the schema's; the selftest refuses `xs:list`, `xs:any`, `xs:all`, mixed content, a complexContent restriction, `xs:anyAttribute`, a repeated sequence, a `foo` beside a `fooType` and a subtype extended in turn (414 cases, 35 s); coverage leaves out only the library's `obj/`; the review lens checks a model change by its fixture, manifest entry, counts and roots; S9, S12 and S17 hold the notes on the context, numbers in requests and the AOT app's lock. Tests 146 to 154; coverage 92.88% of lines and 89.65% of branches, the floors unchanged. |
| 2026-10-31 | S8 | Done: PR #38 merged as `ffc43f5`, closing #37. |
| 2026-10-31 | S9 | Built on `39-client-core` for #39, from `origin/main` at `ffc43f5`; @serionist agreed on #39 to both doors, the client API shape and the supported floor. **The client** (`src/Tableau.Api/`): `TableauClient` with `Server`, its first sub-client, and `GetInfoAsync`; `TableauClientOptions`, read-only once a client is built, checked by the options validation source generator's code, which calls the options' `IValidatableObject` for the rules no attribute states; `AddTableauClient(name, configure)` and its unnamed overload, which register named options validated at start-up, a named `HttpClient` (`Tableau.Api.TableauClient:{name}`) whose primary handler the library builds and whose factory loggers go, a keyed singleton state and a keyed transient client, the unnamed one also unkeyed; `TableauClient.Create(options, loggerFactory)`, over one `SocketsHttpHandler` that renews its connections every five minutes, and a constructor over a caller's `HttpClient`, never disposed; `TableauException` with `TableauErrorKind`. The old `ServerInfo` method and its file are deleted, one more break in the ApiCompat ledger (19). **The pipeline** (`Pipeline/`, internal): `Endpoint` descriptors in `Endpoints`, their templates under `api/` (`{version}/...`, `-/...`, `metadata/graphql`; `{name}`, `{name:luid}`); `RequestUri` with 1.2.6's path and query rules (`UriEscaping` moved out of `RequestPath.cs`) and the exact path check; the deployment gate before anything is sent, the version gates after the version is known; redirects followed in code to the same scheme, host and port only, five at most, 307 and 308 with the body, 301 to 303 for GET and HEAD, and a caller's handler that followed one elsewhere caught; `X-Tableau-Auth` on the request; timeouts as `Timeout`, the caller's cancellation thrown again with its own token; errors read from Tableau's `{"error":{...}}` and `api/-`'s `{"httpErrorCode","message"}`, the body cut to 4 KB; `LoggerMessage` lines at Debug, query values as `***`, warnings only for `DangerousAcceptAnyServerCertificate`, plain http and a server below the floor. **Versions:** Server Info is asked at 3.23 and, on a 404, at 2.4, once per named client, under a lock, and capped at 3.29; below 3.23 every call throws unless allowed, then one warning; a pinned version is used as given, above 3.29 too. **Looked up:** Server Info needs no sign-in, from 2.4 (the reference); Tableau Cloud's hosts are `online.tableau.com` and its pods under it (`10ax`, `prod-useast-a` and the others), no other domain found; keyed typed clients through `IHttpClientFactory` and keyed DI; the configuration binder's generator binds `Version`, `Uri`, `TimeSpan`, enums and booleans, and reads a struct of the library's as all zeros without a word, so `ApiVersion` is a `Version?`, null negotiating; the options validation generator calls `IValidatableObject.Validate`; `CertificateChainPolicy` with `RevocationMode.Online` and the four revocation-unknown flags, which supersedes `CertificateRevocationCheckMode`. **Revocation, verified offline on Windows and Linux:** a certificate authority made per test, its revocation list served by a second fake server: a revoked leaf refused, a listed-good one accepted, one whose list can't be reached accepted. **SYSLIB1220, verified:** a caller's own `JsonSerializerContext` that lists a model warns SYSLIB1220 and SYSLIB1030 at build, since the models' converters are internal, and throws `NotSupportedException` at run time; reflection-based System.Text.Json reads and writes them. `TableauJsonContext` stays internal: a public one would need the envelopes in a context of their own and reshapes with every schema, and adding it later is a minor (Later). **Live check,** Tableau Server 2025.1 (REST API 3.25), read-only, no sign-in, by the new `ServerInfoSiteTests` (`Live`, `TABLEAU_LIVE_SERVER_URL`) and a scratch probe outside the clone that printed shapes only: Server Info answers JSON at 2.4, 3.23 and 3.25 without a token, `{"serverInfo":{"productVersion":{"value","build"},"prepConductorVersion","restApiVersion":"3.25"}}` (no `prepConductorVersion` at 2.4); a version above the server's answers 404 with code `404001`, 2.0 answers `404008`, `api/-` answers `{"httpErrorCode","message"}`; the error code is a string; no request id header, but `X-Tableau: Tableau Server` on every answer, a signal for Tableau Cloud not checked; certificates validated with revocation online, on Windows. Both site tests passed. **Tests:** 154 to 299, every row the plan lists: the gates per descriptor and by deployment, the floor with and without the opt-in, negotiation once under concurrency and again after a failure, redirects, TLS, revocation, cookies, path and query rows over 1.2.6's table, the base path, the three URL shapes, any 2xx, every error shape, timeouts, logs without a secret or a query value, DI keyed and default with options from configuration and refused at start-up, mocking, and `ConventionsTests` over `roots.xml`'s types. S3's tests of F2, F3, F6 and F39 now have 2.0 twins; the old ones stay while the old client does. The native AOT app calls the client over a handler serving fixtures, binds the options from configuration in the binary and refuses bad ones: published natively on win-x64 here with no warning, 15 MB, every check passed. Coverage 93.93% of lines and 92.32% of branches; the floors rise to 93% and 92%. **Departures,** in the PR's door list: `ApiVersion` is a `Version?`, null negotiating, since the binder's generator can't read a struct of the library's; `Create` takes no credentials until S11 brings `TableauCredentials`; the sub-client is `ServerClient`; a caller's `HttpClient` refuses `DangerousAcceptAnyServerCertificate`; a pinned version above 3.29 is used as given; the request id is read from `X-Request-Id`. The one `SuppressMessage` (CA5359) is on the callback `DangerousAcceptAnyServerCertificate` asks for. |
| 2026-10-31 | S9 | Draft PR #40. review-code round 1, three fresh reviewers on `ae98f14`: one major, eleven minors, all fixed on the branch, and every improvement taken; the coordinator keeps the record, `docs/reviews/2026-10-31-client-core-code.md`. **The major:** `TableauClient` was `IAsyncDisposable` alone, so a container's synchronous `Dispose` of a scope or the provider that had made one threw and left the rest undisposed; it is `IDisposable` too, in the standard pattern, with tests of a synchronous scope and provider. **Minors:** the named client's handler is built from the name's read-only state, not the options monitor, so a reload can't reach a later handler; the handlers an app gives every `HttpClient` (`ConfigureHttpClientDefaults`) are cleared from the named client; a redirect a caller's own handler followed says the request went there; an error body whose strings can't be read gives the status alone; concurrent first calls share one negotiation, on no caller's token, and a failure is kept by none; a LUID must be the value itself, not one `Guid` parsing forgives; CHANGELOG's 1.2.6 section is restored and its bullets unglued; the revocation tests name the revoked leaf by its own serial, a first byte from 0x01 to 0xFF; every `RecordingLogger` is checked at the suite's end for secrets and for a 2.0 line showing a query value, a redirect among the logged calls; a redirect to another host on the same port is refused; both lens files name the 2.0 surface under public-api. **Improvements:** the conventions test finds the sub-clients from `TableauClient`'s properties; a revocation answer is waited for 10 seconds at most (`UrlRetrievalTimeout`); the floor's and an unreadable version's exceptions are logged as the pipeline's are and carry the call's method; the design above shows what S9 built; S10, S11 and S17 hold the notes the review left; AGENTS.md keeps the old client's "on Tableau Server and Tableau Cloud". Tests 299 to 313; coverage 93.96% of lines and 92.48% of branches, the floors unchanged. |
| 2026-10-31 | S9 | review-code round 2 on `b8f2232`: every round 1 fix confirmed, each with a test that fails on its revert; one new minor and four improvements, all taken. **The minor:** a negotiation that failed after every caller had stopped waiting stayed in the state, so the next call got its failure without asking the server. The negotiation now lets go of itself before it fails, and a failure nobody awaits is observed; a test cancels the only caller, lets the fake answer 503, and sees the next call ask again, and fails with the fix taken out. **Improvements:** an app's default handlers registered after `AddTableauClient` don't reach the client either (a row); the design says `ApiVersion` null where it said `Negotiate`; AGENTS.md names the packages the library depends on; S10's scope notes that the per-name handler clear runs where it is registered. Tests 313 to 315. |
| 2026-10-31 | S9 | Done: PR #40 merged as `18c8404`, closing #39, with both doors and their departures agreed on #39. |
| 2026-10-31 | S10 | Built on `41-resilience-downloads` for #41, from `origin/main` at `18c8404`; @serionist agreed on #41 to its doors, the resilience option names and the builder's handler hooks, and `TableauDownload`. **Resilience** (`Pipeline/TableauResilience.cs`): Microsoft.Extensions.Http.Resilience's `ResilienceHandler` over a Polly pipeline built from the read-only options, a retry around an attempt timeout, on the state's `TimeProvider` (the container's under DI); not `AddResilienceHandler`, whose registration puts services and telemetry in the caller's container and logs each retry as a warning. `TableauAttemptHandler`, inside the retries, reads a success whole and an error's first 64 KB within the attempt, so `RequestTimeout` covers the answer and an answer that breaks off is retried. Retries only for idempotent descriptors, carried in the request's options: 408, 429, 5xx, a connection that failed (not a refused certificate or another failure sending again won't mend), an attempt that timed out; three by default, jittered back-off from about two seconds, at most 30, logged at Debug. The server's wait, `Retry-After` as a delay or a date or a 429 body's "retry after N minutes", is waited out up to `MaxRetryAfter`; a longer one fails at once, `RateLimited` for a 429 with `RetryAfter`. A transfer has no attempt timeout and `TransferTimeout` covers all of it, a download's reading included. A timeout's cause is a cancellation, never Polly's exception; a caller's handler that throws fails the call as `Http`. **One chain everywhere:** the named `HttpClient`'s handlers are the caller's, outermost, then the library's two; `Create` builds the same two over its `SocketsHttpHandler`; the constructor over a caller's `HttpClient` sends each attempt through it as a copy of the request, so its handlers run inside the retries, which its doc says. One action per name sets the primary handler and the handlers, registered by the first `AddTableauClient` of the name, so a second keeps what the first added; `ConfigurePrimaryHandler` runs on the library's handler before its rules are set again (no redirects, no cookies, its certificate callback; a chain policy the caller set kept). **Downloads:** `TableauDownload`, `TableauFileKind`, the pipeline's `DownloadAsync` (the download methods come with their areas, from S12); `Content-Disposition` read raw, since Tableau's documented `name="tableau_workbook"; filename="..."` has no disposition type and .NET's typed header refuses it. **Looked up:** Learn on Microsoft.Extensions.Http.Resilience (the handler, `AddResilienceHandler`, the standard handler's predicates, Retry-After, the static-client pattern), IHttpClientFactory (`ConfigurePrimaryHttpMessageHandler`, `ConfigureAdditionalHttpMessageHandlers`, the obsolete `ConfigureHttpMessageHandlerBuilder`, handler order), Polly's retry and timeout options and `TimeProvider`; Tableau documents no 429 body or `Retry-After`, and the one wording found is a detail "Please retry after 29 minutes." under code 429000 (tableau/server-client-python#952), so the body is read loosely. **AOT:** a probe published natively with `ResilienceHandler`, a retry, a timeout and the request's options: no warning; the AOT app now retries a 503, downloads and saves a file, and calls through a DI client with `ConfigurePrimaryHandler` and a handler of the caller's, published natively on win-x64 with no warning, 16 MB, every check passed. **Live check,** Tableau Server 2025.1 (REST API 3.25), read-only, by a scratch program outside the clone that printed shapes only: the 2.0 client negotiated through the new chain; two workbooks' downloads answered `Content-Disposition: name="tableau_workbook"; filename="<name>.twbx"`, which .NET's typed header refuses and the library reads, `application/octet-stream`, and no `Content-Length`. A first run signed in with JSON but read XML, so it could not sign out; that session ends on its own. **Tests:** 315 to 438; coverage 94.67% of lines and 93.81% of branches, the floors rise to 94% and 93%. A caller's `HttpClient` with a resilience handler of its own showed that a copy carrying the request's options hands the client's resilience context to it, and the client's next attempt then sends the copy without its body: the copy takes no options, and a test sends a body through both. **Departures,** in the PR's door list, agreed by @serionist on #41: `Kind` also from the media type and from a PDF's or a PNG's signature, since an export sends no file name; a public constructor of `TableauDownload` over a stream, for a mock of a download method; the builder's methods return the builder; `MaxRetryAfter` caps a 503's `Retry-After` too; `ConfigurePrimaryHandler`'s certificate rule replaces a caller's callback and uses a caller's chain policy as set; a read of `Content` that breaks off or times out throws `TableauException`. |
| 2026-10-31 | S10 | Draft PR #42. review-code round 1, by fresh reviewers: one blocker and fourteen minors, all fixed on the branch, with the maintainer's calls. **The blocker:** `TableauHandler.Create` set the library's TLS rules in place on `SslClientAuthenticationOptions` a `ConfigurePrimaryHandler` action gave it, so two clients sharing one, one with `DangerousAcceptAnyServerCertificate`, both took the one resolved last, and an app's own handler sharing it lost its callback; the rules are now set on a copy the handler holds alone, member by member as the runtime's own clone copies them, with a test of two clients in both orders and one that counts the 15 members. **Polly.Core alone,** the maintainer's call: Microsoft.Extensions.Http.Resilience brought `Microsoft.Extensions.Telemetry.Abstractions`, which turns off the runtime's `LoggerMessage` generator in every app that takes it, and 16 packages in all; a handler of the library's own, `TableauResilienceHandler`, now runs Polly.Core 8.9.0's retry, times each attempt on the client's clock and reads its answer, and the library's lock file falls from 33 packages to 17. **Minors:** the docs say a caller's chain policy is used as set, its flags able to loosen the check; `Retry-After` is honoured on a 429 and a 503 only, another status left to the back-off; a handler's own timeout or cancellation fails the call as `Http` with it as the cause, the library's own attempt timeout told apart by an internal exception; two tests now fail without what they claim (the disposed handler, the answer read whole); a file name is cut to 255 bytes of UTF-8 on a character's boundary before it is trimmed and checked for a device name; an empty `filename*` leaves `filename`; a save refuses a folder before reading, documents `UnauthorizedAccessException`, and may be tried again when it couldn't open its file; the pipeline takes only a download descriptor for a download; a 5 MB download is sent chunked, as Tableau Server sends one; the guide and the changelog say a 503 over the cap is `Http`, and which old downloads gave a kind. **Improvements taken:** a proxy's 407 isn't retried; `AddHttpMessageHandler` advises retrying only safe methods; the waits' tests measure from the retry's log line; the public constructor parses its `Content-Type`; a break-off while reading ahead says "broke off after N bytes"; the download docs name the read-ahead and the cleaning rules; `SaveToFileAsync`'s example falls back when `FileName` is null; S12 marks Append to File Upload non-idempotent. Tests 438 to 472; coverage 94.73% of lines and 94.09% of branches on Windows, more on Linux, which also runs the platform's own TLS members; the branch floor rises to 94%. The native AOT app stays warning-free, 15.9 MB. The live check on Tableau Server 2025.1 ran again: negotiation and Server Info through the new handler, one workbook download's headers read as before, signed out. |
| 2026-10-31 | S10 | review-code round 2 on `dd567cf`: every round 1 fix holds; five minors and eight improvements, all taken. **Minors:** two comments outlived the switch to Polly.Core (`CallerClientHandler`'s options, `TableauClientState.Resilience`); the advice to a caller's own retrying handler now says GET and HEAD, or a call known safe to send twice, never a POST nor Append to File Upload's PUT, and that the client's attempt timeout runs inside it; a DI handler's exception shaped as an `HttpClient` timeout is `Http`, since only a caller's `HttpClient` has a timeout of its own; a save cancelled before it starts no longer blocks a later one; `ClientFixtures.ClosedPort()` holds its port with a socket bound and never listening, since a freed port went to a parallel test's server and failed a test once in twelve coverage runs (a connection to it is refused at once on Linux, after Windows' usual two seconds there). **Improvements:** the waits' upper bounds have ten seconds' room; a 503 whose body names a wait is left to the back-off; a dead branch in the name cut goes, and the device rule's second trim is pinned; the guide links `main`'s `DownloadDataSource`; blank lines between members restored. Tests 472 to 475; ten runs with coverage in a row, as the gate runs them, all passed; 94.74% of lines and 94.09% of branches on Windows, the floors unchanged. |
| 2026-10-31 | S10 | Done: PR #42 merged as `54b6764`, closing #41, with its doors and their departures agreed on #41. |
| 2026-10-31 | S11 | Built on `43-sessions-live-suite` for #43, from `origin/main` at `54b6764`; @serionist agreed on #43 to its door, the credentials and session API, and chose his own Tableau Server for the live suite over a Developer Program site. **Sessions** (Design, Sessions and sign-in): `TableauCredentials` with its five kinds and their vault overloads; `TableauSession`; `client.Sessions` (`SessionsClient`) and `client.WithSession`; credentials on `Create`, the `HttpClient` constructor and `UseCredentials`; the session manager, `Pipeline/TableauSessions.cs`; eight descriptors (Sign In by kind, Sign Out, Switch Site), `NeedsSession`, `KeepsBody` and `{siteId}` in the pipeline. `TableauApiClient`'s `SignIn`, `SignInWithPAT`, `SignOut` and `SwitchSite` are deleted: three more breaks in the ledger (22), `SignInWithPAT`'s 1.2.5 signature being listed already; the old files name `Models.TableauSession`, since a `TableauSession` in `Tableau.Api` would otherwise bind every old method to the new record. **Looked up:** the REST reference's Sign In, Sign Out and Switch Site (bodies, `isUat` from 3.27, impersonation Server only, Switch Site 2.6 and Server only, a token valid 240 minutes on Server and 120 on Cloud); `estimatedTimeToExpiration` is a PAT's own lifetime, `DAYS:HH:MM`, returned to a PAT sign-in alone, and the live server sent `363:10:35`; the direct-trust JWT's header and claims, Server and Cloud; connected-app scopes (none for sign-in and sign-out from 2023.2; `tableau:content:read` for reads, `tableau:projects:*` for projects); unified access tokens; `HMACSHA256.HashData`, `Base64Url`, `IHttpClientFactory`'s handler lifetimes, and `ServiceProvider.Dispose` throwing on a service that is `IAsyncDisposable` alone. **The live suite:** `live.yml` (by hand from `main`, nightly once `TABLEAU_LIVE_NIGHTLY` is `true`, in `tableau-live`, one run at a time); `LiveSite.cs` (the settings, the sign-in, the run's project and its sweep); `FixtureRecorder.cs` (`TABLEAU_RECORD=1`); `ServerOnlySessionTests` for impersonation and Switch Site by hand; one `[AssemblyCleanup]`, `SuiteCleanup`, which deletes the run's project, then checks every log line, a live secret's counted and never shown. **Live check,** Tableau Server 2025.1 (REST API 3.25), the default site, a PAT from outside the clone: the four `Live` tests passed, sign-outs checked by a refused token; the run's project made, listed and deleted. It showed that a PAT's two sessions end each other, so the session tests run one at a time, and that the server lists a project a second behind a create and minutes behind a delete. A recording run wrote 19 fixtures, none holding the secret, the token's name, the host, a LUID, an email or a token. Not run: Switch Site (one site) and the password sign-ins (no password); the first direct-trust run comes from `main` after the merge. **Tests:** 475 to 551; coverage 95.01% of lines and 94.56% of branches on Windows; the line floor rises to 95%. The native AOT app signs in through its fixtures, mints a JWT, binds a session and signs out: published natively on win-x64 with no warning, 16 MB. **Departures,** in the PR's door list: `ExpiresAt` takes a PAT's `estimatedTimeToExpiration` only when sooner than the deployment's lifetime; `TableauSession`'s six members are `required` init, so a test can make one, and `WithSession` refuses one without a token; `Jwt` and `UnifiedAccessToken` take only a delegate; `UseCredentials` keeps the last given; a DI name's session is never signed out, the container's disposal included; sign-in descriptors are idempotent. |
| 2026-11-01 | S11 | Draft PR #44. review-code round 1: no blocker or major, eleven minors, all fixed, and the maintainer took all fifteen improvements; @serionist agreed on #43 to the eight departures and to a narrower eighth, a JWT's sign-in never retried. **Fixes:** a JWT's or a unified access token's sign-in is never sent twice, since Tableau refuses a `jti` it has seen; PAT impersonation needs REST API 3.11 (Tableau Server 2021.1, `tsm authentication pat-impersonation enable`), and an impersonating sign-in that comes back as another user is signed out and refused; a session is renewed by its own lifetime, its `ExpiresAt` still showing a PAT's sooner end, which comes as a 401, so a token's last minutes don't sign in on every call; the half-life rule goes with it; a cancelled sign-in is pinned as no failure for the callers waiting; the live suite keeps every token its clients carry, renewals included, through one `HttpClient` disposed after the cleanup signs out; in a run that reached a site the log check counts lines and shows none; the sweep deletes only a top-level project with the suite's description and a run's exact name; the recorder fails closed, takes secret keys by pattern and the run's settings everywhere, writes nothing for a body that isn't JSON, and the suite's end deletes and names a file still holding a live value; the migration guide and CHANGELOG say an area not yet ported can't get a session on `main`. **Improvements:** `TableauRequest` prints no token; a sign-in a caller's handler sent elsewhere says to rotate the credentials; a renewal that fails before `ExpiresAt` leaves the call in its session and is tried again a minute later; the sign-out of a sent token keeps its caller's time, so a dispose stays within ten seconds; tests of `Dispose` under a single-threaded context, of the sweep's rules and of the scrubber over the schema's 13 secrets. Tests 551 to 577. |
| 2026-11-01 | S11 | Done: PR #44 merged as `eb79b4b`, closing #43, with its door and its departures agreed on #43. |
| 2026-11-01 | S12 | Built on `45-port-sites-projects-users-groups-favorites` for #45, from `origin/main` at `eb79b4b`; @serionist agreed on #45 to both doors, the paging and filter API and the five sub-clients with `FavoriteTarget`, and to deleting the old methods. **The client:** `client.Sites`, `.Projects`, `.Users`, `.Groups` and `.Favorites`, classes with virtual methods and protected constructors, string LUIDs checked before anything is sent, the token last; 46 descriptors in `Endpoints`, each method, path, version and deployment from the reference (Create Group and Update Group at 3.9, where their bodies took a minimum site role; Delete Group 2.1; Get Groups for a User 3.7; each kind of favorite from its own version), `Idempotent` by what the call does: creates, an import, Add User to Site and a synchronisation from Active Directory, which can outlast an attempt, are sent once; updates, deletes, Add User to Group (a second answers 409 when the first got through) and a favorite's PUT may be sent again. **Paging and filters** (Design, Paging and filters): `Page<T>`, `ListAsync` through the virtual `ListPageAsync`, the options records, `TableauField` in `ProjectField`, `UserField` and `GroupField`, `TableauFilter` and `TableauSort`. **F13:** the Active Directory updates PUT to `groups/{id}`, at once and as a job. **F33:** Update User sends a `User`'s set members, the reference's nine among them. **The old client:** the Sites, Projects, UsersAndGroups and Favorites partials deleted with `DeleteProject2`, 40 more breaks in the ledger (62), each a row of the migration guide's name map; `QueryViewsForSite` moved to the views partial until S14; `OldPublicTypes` unchanged at 232, since the XML types stay with `XmlSchema.cs`; the four files out of TableauLegacy; the old client's tests that used the deleted methods moved to Query Data Sources and Initiate File Upload. **Looked up:** the reference's Sites, Projects, Users and Groups and Favorites pages and its filtering page; Tableau's Python client for Delete Flow from Favorites, whose reference URI repeats the data source's. **Read-only probes of the live server,** Tableau Server 2025.1 (REST API 3.25), outside the clone, shapes and status codes only: Query Sites refuses every filter, the reference's `id:eq` included (400065), and ignores any sort; projects refuse a sort on `id`, `luid` and `parentProjectId`, users and groups on `luid` (400066), and users refuse `isLocal` in a filter; `createdAt:asc,name:asc` and `name:asc,domainName:asc` sort, though the default for users and groups is `name:asc` alone, since nothing here reaches Tableau Cloud, where a user's domain names how they sign in; a comma escaped as `%2C` in a filter value fails to parse (400065); a page past the end answers 400006 for groups and a user's groups, 400117 for projects, and an empty page with a total of 0 for users; a case-insensitive filter on users answered a total of 1 with no users; Get Favorites for User sends no pagination and an empty page past the end; a user who never signed in has no `lastLogin`; page size 1,001 answers 403014. **Live check,** the same server, the default site, a personal access token from outside the clone, mapped to the suite's settings in the test process alone: the seven `Live` tests passed, the three new ones among them, before and after the default sort of users and groups became their name alone: a nested project named `R&D C++ #1 100%` made in the run's project, found by an `eq` filter on its name and parent, updated and deleted; a local group made, found, renamed, given the suite's own user, listed among the user's groups and its users, emptied and deleted, a 404 after; users read two a page, the suite's own by id and by a `luid` filter, and its groups. The run's project and group were deleted and every session signed out; the first run recorded 52 scrubbed bodies, and those read, the projects', the groups' and the users', matched the synthetic fixtures' shapes; none is committed. Not run: Update Site, which changes the site, so a JSON number for `revisionLimit` is left to the `ServerOnly` site test, by hand; the `ServerOnly` tests (a site made, updated on it and deleted; an Active Directory group imported and synchronised, at once and as a job; a user added, updated and removed). **Tests:** 577 to 686; coverage 96.04% of lines and 94.77% of branches on Windows, five runs in a row the same; the line floor rises to 96%. The native AOT app lists projects page by page with a filter and the stable sort, published natively on win-x64 with no warning, 16.3 MB. **Departures,** in the PR's door list: `SiteListOptions` holds a page size alone and no `SiteField` is made, since the server refuses the reference's one site filter and ignores a sort; Delete Flow from Favorites goes to `flows/`; no metric favorite, retired in REST API 3.22, below the floor; the choices the words left open. |
| 2026-11-01 | S12 | Draft PR #46. review-code round 1: no blocker or major, six minors, all fixed, and the maintainer took every improvement; @serionist agreed on #45 to the five departures and to `Eq` and `In` taking the generated enumerations. **Fixes:** `TableauFilter` and `TableauSort` compare by their expression and fields, so list options built alike are equal; a date filter is tested under th-TH (Buddhist years) and fi-FI (dotted times), and both rows fail without the invariant culture; each field's summary gives the reference's operators for it, `parentProjectId` taking `in`; the `ServerOnly` tests delete the site and the import job's group they made, finding that group by its name, domain and a new id, or name what is left; a table pins every descriptor's retry rule, and each call sent once stays one request after a 503 with no wait, where an update is sent again; a list fails as `InvalidResponse` when the server answers an earlier page, or for favorites, which carry no pagination, a page that starts like the one before, where it would repeat without end. A read-only probe on Tableau Server 2025.1 showed favorites page by `pageNumber`: a page of one, then the second favorite, then none. **Improvements:** a caller's sort is followed by the default's fields it doesn't name; users sort by name and domain on Tableau Server and by name alone on Tableau Cloud, where nothing shows a domain sorts; `Eq` and `In` take `SiteRole`, the enumeration S12's fields hold, a later area adding its own; a refusal names the C# member; Delete Site is sent once; CHANGELOG names what is sent once; the nightly users test reads three pages of two at most; the list tests that claimed pages serve two; the docs of Update User's attributes give their versions, and Delete Site's says a site, as the reference does. Tests 686 to 705. The `Live` tests passed again on Tableau Server 2025.1, the run's project and group deleted and every session signed out. |

## For the maintainers

What the session cannot do, first what closes a security exposure. Kept current as the work goes on.

**Now:**

- [x] Turn on private vulnerability reporting (Settings, Code security). SECURITY.md (S1) sends reporters there. On
  when S1 was built (2026-10-29).
- [x] Q6: László Nemes agreed to MIT, recorded on #1 (2026-10-28); serionist confirmed the clearance when the repo went public.
- [x] Answer Q1 to Q5 on #2: the recommended answers stand (default, 2026-10-28).

**For S1, S2a and S2b:**

- [ ] Join the Tableau Developer Program and get its site; note its renewal date. Not needed for the live suite: #43 runs it on the maintainers' own Tableau Server instead. S2b's live smoke run needs it, with
  a PAT kept in environment variables for a short program outside the clone.
- [ ] After S1: make `CI` and `PR text` required on the `main` ruleset, or tell the session to apply it through the
  API. Until then `dependabot-auto-merge.yml` fails on Dependabot's PRs and merges none. Before S2a's PR: the same
  rules for `release/1.x` (PRs only, squash, no force push, the two checks).
- [ ] Check the `nuget` environment has what `NuGet/login` needs, such as a `NUGET_USER` secret with the nuget.org
  profile name of the owning account (`serionist`).
- [x] Approve the `nuget` deployment for `v1.2.6`. Approved; 1.2.6 is on nuget.org (2026-10-30).

**After 1.2.6 is on nuget.org:**

- [x] Publish a GitHub security advisory for 1.2.5 and older, fixed in 1.2.6, describing what changed: GHSA-wqhr-qp8x-c5gm,
  2026-10-30. No CVE requested.
- [ ] Deprecate 1.0.6 to 1.2.5 on nuget.org with "Critical bugs" and Tableau.Api 1.2.6 as the alternative. Leave 1.2.6
  undeprecated.
- [x] Tell Starschema, the co-owner: not needed, its owner is @serionist.

**Before S11:** the suite runs on the maintainers' own Tableau Server, not a Developer Program site (#43).

- [x] On that Tableau Server, the default site, a direct-trust connected app, "Tableau.Api live suite": enabled, all
  projects, one secret (2026-10-31). The JWT asks for `tableau:content:read` and `tableau:projects:*`; signing in and
  out needs no scope from Tableau 2023.2.
- [x] The `tableau-live` environment, deployment branch `main` only and no required reviewers (Q2), with the secrets
  `TABLEAU_LIVE_SERVER_URL`, `TABLEAU_LIVE_CLIENT_ID`, `TABLEAU_LIVE_SECRET_ID`, `TABLEAU_LIVE_SECRET_VALUE` and
  `TABLEAU_LIVE_USER` (2026-10-31). No `TABLEAU_LIVE_SITE`: GitHub keeps no empty secret, and unset is the default site.

**After S11 merges:**

- [ ] Run `live.yml` by hand on `main`, the first direct-trust run. If the sign-in is refused, check the user's name as
  the server shows it, a domain included for Active Directory or LDAP.
- [ ] Then set the repository variable `TABLEAU_LIVE_NIGHTLY` to `true`, which turns the nightly run on.

**After S12 merges:**

- [ ] Run `live.yml` by hand on `main`: the JWT now asks for `tableau:groups:*` and `tableau:users:read` too, for the
  run's group, the suite's own user in it, and the users it reads. If the connected app refuses them, allow them.
- [ ] Run the new `ServerOnly` tests by hand (`ServerOnlyAreaTests`): a site made with a revision limit, which shows
  Tableau takes the JSON number, updated on it and deleted; an Active Directory group, with
  `TABLEAU_SERVER_ONLY_AD_GROUP` and `TABLEAU_SERVER_ONLY_AD_DOMAIN`, whose users join the site and stay; and a user,
  `TABLEAU_SERVER_ONLY_NEW_USER`, added, updated and removed.

**Settings:**

- [ ] Dependabot alerts, Dependabot security updates and the dependency graph on; keep secret scanning and push
  protection on.
- [ ] Keep CodeQL's default setup off: `codeql.yml` is the advanced setup, and the two conflict.
- [ ] Check that a tag ruleset restricts who can create `v*` tags.

**Before 2.0.0:**

- [ ] Run the `ServerOnly` suite on a Tableau Server you have (S18).
- [ ] Approve the `nuget` deployments for `v2.0.0-rc.1` and `v2.0.0`.
- [ ] Optional: ask Tableau's developer relations for written permission for the name (RD6).
