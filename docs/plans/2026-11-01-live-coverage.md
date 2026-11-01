---
title: Live-test almost every 2.0 method, with a site per run and a live-coverage matrix
issue: "#57"
status: approved
approved_by: serionist
approved_at: 2026-11-01
---

# Live-test almost every 2.0 method, with a site per run and a live-coverage matrix

The nightly live suite moves its content work to a personal access token, works in a Tableau site it makes for each
run, calls nearly every method of the 2.0 client there, and checks a matrix in `docs/` that names every method as live
or says why not.

**In short:**

- **The token does the content work, the connected app keeps its own sign-in tests** (step 1, its own PR): `main`'s
  nightly has failed since #58, because a connected app's session on Tableau Server 2025.1 can't call any method whose
  scope came in REST API 3.27.
- **A site per run:** made by the suite, worked in by every content test, deleted at the end; a sweep takes a leftover
  run site more than a day old.
- **Backfilled live tests:** sites, Switch Site, a directory user, an Active Directory group, token impersonation,
  favorites, default permissions, permissions on samples and a group set, the catalog, data quality warnings and tags.
- **The matrix:** `docs/live-coverage.md`, one row per method and call, checked against the code on every PR and against
  what the nightly really sent.

## Goal

When this is done:

- `main`'s nightly is green again and signs in for its content work with the CI token (`TABLEAU_LIVE_PAT_NAME`,
  `TABLEAU_LIVE_PAT_SECRET`); the connected app's settings drive its own sign-in tests only.
- Each run makes a site named after itself, does every change in it, and deletes it; nothing the suite does lands on a
  site people use.
- Every public method of the 2.0 client is called live by a `Live` test, or `docs/live-coverage.md` says why not. A
  test fails on every PR when a method has neither; the nightly fails when a row it calls live wasn't sent by its test.
- S15 to S17 add their rows as they add methods, and S18 publishes the matrix with the release.

In scope:

- `.github/workflows/live.yml`, `tests/Tableau.Api.Tests/` (the live suite, `TableauSite.cs`, a new offline test of the
  matrix), `docs/live-coverage.md`;
- one change to the library: the client's Debug lines for a call name the call (Design, How the matrix stays honest);
- the `ServerOnly` tests that the nightly now covers, deleted;
- the docs that name the live suite (Docs).

Out of scope:

- new public API in the 2.0 client (a group set is made by the test itself, Q2);
- Tableau Cloud: the suite needs a Tableau Server and a server administrator; it never ran on Cloud, which stays checked
  by hand (the modernization plan's R3);
- calls the 2.0 client doesn't have yet (S15 to S17), and re-recording fixtures;
- the old client (`TableauApiClient`), which S17 deletes.

## Design

### Two sign-ins, each with its own job

The maintainer's decision on #57: the content work signs in with a personal access token made for CI, a server
administrator's on the default site; the connected app (direct trust) keeps its own sign-in tests.

| What | Today | After |
|---|---|---|
| `live.yml` | maps the connected app's four settings | maps those four and `TABLEAU_LIVE_PAT_NAME`, `TABLEAU_LIVE_PAT_SECRET` |
| `TableauSite.SignInSettings` | the app's settings when any is set, else the token's | goes: each test names the sign-in it needs |
| `SiteTestAttribute.SignsIn` | `bool` | `LiveSignIn.Token` or `LiveSignIn.ConnectedApp`, none by default as Server Info's tests need; each missing setting of that sign-in fails the test, named |
| `LiveSite.Credentials()` | one or the other (`UsesDirectTrust`) | the token, always; `ConnectedApp()` and `Jwt()` for the app's tests |
| `LiveSite.Scopes` | 19 scopes for every content call | `tableau:content:read`, what the app's tests call |

- **The connected app's tests** (`LiveConnectedAppTests`, on `TABLEAU_LIVE_SITE`, the default site in CI, read only):
  - a client with `ConnectedAppDirectTrust` signs itself in for Query Projects (a page of one), signs out when disposed,
    and its token is refused after;
  - a session signed in with `TableauCredentials.Jwt`, from a JWT the test mints with the library's
    `ConnectedAppJwt.Mint` and the same settings, serves a bound client and is signed out. That makes the `Jwt` kind
    live too. The minted JWT joins `LiveSite`'s live secrets, so the log check and the scrubbing know it.
- **The token and the local runs:** the CI token is not the one in a maintainer's local settings, so a local run and the
  nightly never sign each other out. One token allows one session: a test's client is disposed before the next signs
  in, as the tests run one at a time today.

### What a connected app's session can call on Tableau Server 2025.1

From the reference's scope line on each method (read 2026-11-01). On 2025.1 (REST API 3.25) a JWT session can call a
method whose scope came in 3.16 or 3.17, and none whose scope came in 3.27 (Tableau Server 2025.3): each 401002 of run
41310695999 came from such a method.

| The session can call | It can't, the scope coming in 3.27 |
|---|---|
| Sign In and Sign Out (no scope since Tableau Server 2023.3) | Switch Site, Query Site, Delete Site |
| Create Site, Query Sites, Update Site | Update User |
| Query, create, update and delete projects | Query Workbooks for User, Query Workbook Connections, Query Views for Workbook |
| Users read, added and removed; groups, Active Directory imports and synchronising included | Update Workbook Connection, Delete Workbook, Delete Data Source, Download View Crosstab Excel |
| Permissions on projects, workbooks, views, data sources and virtual connections, and default permissions | the revision lists, both Remove Revision calls, Download Data Source Revision |
| Query Workbooks for Site, Get Workbook, Get View, Query Views for Site, the data sources' reads and connections | every tag call, for content, the catalog and in a batch |
| Downloads and exports of workbooks and views but the crosstab; Download Workbook Revision | every favorites call |
| Update Workbook, Update Data Source and its connection, both extract refreshes | databases, tables and columns: reads, updates, deletes; every data quality warning call |
| the Metadata API (`tableau:content:read`, Tableau Server 2023.3) | collection permissions; database and table permissions list no scope at all |
| group sets (`tableau:groupsets:*`) | |

So the connected app couldn't carry a site per run on 2025.1 either: Switch Site, Query Site and Delete Site are on the
right. Once the server reaches 2025.3 the app could do more, but the token stays the nightly's sign-in for content.

### A site per run

- **The site:** named `Tableau.Api live run <run> <made>`, content URL `tableau-api-live-run-<run>-<stamp>`, where
  `<run>` is `LiveSite.Run` (CI's run and attempt, or a local run's time and random part), `<made>` the second it was
  made in UTC (`yyyyMMddTHHmmssZ`) and `<stamp>` the same second in digits alone (`yyyyMMddHHmmss`): a site has no
  creation date in the REST API, so its name carries one, as a run's groups' names do today. The content URL holds only
  lower-case letters, digits and hyphens, which Create Site keeps as given.
- **Made once a run,** by `LiveSite.RunSiteAsync`, under a lock, from a client of the token on `TABLEAU_LIVE_SITE`,
  after the sweep; that client is disposed before anything signs in to the run site.
  - It is made with `RevisionHistoryEnabled = true` and a `RevisionLimit`, so S14's revision lists have the history
    they read, and each night shows Tableau takes the limit as a JSON number in Create Site.
  - `RunSiteAsync` fails, naming the site, unless Create Site's answer passes the sweep's own rule (`IsRunSite`): a
    name or content URL Tableau changed would otherwise leave every run site out of the sweep, silently.
  - Each test that needs the run site takes it (`RunSiteAsync`) before its first sign-in, so the setup's sign-in, which
    ends the token's other session, never ends one a test holds, such as Switch Site's.
- **Worked in:** every content test gets its client from `LiveSite.RunClientAsync()`, whose options name the run site,
  so the client signs in there with the token, and signs in there again on renewal or after a 401. Switch Site is not
  how the content clients get there: a client that owns its session signs in again to its options' site, so one moved by
  Switch Site would land on the default site after a renewal, and a bound client never renews. Switch Site gets its own
  test.
- **Inside it,** as today: the run's project made with `publishSamples=true`, so Tableau publishes two sample workbooks
  and their data source into it; the run's local groups. Every list in the site holds only the run's content, so Query
  Workbooks for User and the Metadata API read nothing else.
- **Shared and owned content,** since MSTest promises no order between tests:
  - the run's project and its samples are shared: every test may read them, and may change them only as it changes
    them back in the same test (an update, a tag, a permission, a favorite), never delete them;
  - a test that deletes content owns it: `LiveSite.OwnSamplesAsync` makes it a project of its own with
    `publishSamples=true`, named after the run and the test. S14's data source test (Delete Data Source), its tags test
    (Delete Workbook) and `LiveCatalogTests` (the catalog's deletes) take their own;
  - the directory user is owned by the test that adds it, which impersonates it too.
- **On a fresh site:** the Metadata API test waits, with `LiveSite.EventuallyAsync`, until a query filtered on the run's
  project lists both samples, and pages by one, so it follows a cursor; the counts it compares come from that filter,
  which the owned projects don't change. The users test pages by one, the site holding few users.
- **Deleted at the end** by `LiveSite.CleanupAsync`: Delete Site from a session on the run site, as the reference asks
  ("you must include the authentication token that you got back when you signed into the site"). The cleanup's client
  has a `RequestTimeout` of ten minutes, since a site with content can take long to delete and Delete Site is never sent
  twice. The site's projects, groups, content, catalog assets, group set and users' memberships go with it.
- **The sweep,** before the run site is made: Query Sites, and for each site whose name and content URL are a run's
  exactly and whose `<made>` is more than a day ago, a sign-in to it and Delete Site, from a client with the cleanup's
  options and its ten minutes. Any other site stays, whatever its name.
  - A sign-in to a leftover that fails is told apart by Query Sites again, from the default site: a site no longer
    listed is gone and passed over; one still listed is a failure.
  - A leftover the sweep can't delete never stops the run site's creation, which would fail every content test: it is
    kept, and `SuiteCleanup` fails the run at its end naming the site, so that night is red with the cause and every
    test has still run.
- **What goes:** the project and group sweeps on the default site, with `IsRunProject`, `IsRunGroup` and their rows in
  `LiveSiteTests`; the site sweep's rule takes their place there. Before they go, a read-only list of the default
  site's run projects and run groups by the old rules; step 4's progress row names the count, and a maintainer deletes
  any by hand.

### The directory accounts

The maintainer made technical Active Directory accounts on the server's domain: a group with one member, and a user on
its own; each is enabled, with a random password nobody holds (Q7). Their names never enter the repo; they reach the
suite as settings, `tableau-live` environment secrets in CI (so GitHub masks their exact values), and join `LiveSite`'s
known settings, which every test's output, every recorded fixture and the end-of-run scan scrub.

| Setting | What it holds |
|---|---|
| `TABLEAU_LIVE_AD_DOMAIN` | the domain, in the form Create Group's `domainName` takes |
| `TABLEAU_LIVE_AD_GROUP` | the group, in the form Create Group's `name` takes |
| `TABLEAU_LIVE_AD_GROUP_MEMBER` | the group's member, as Tableau names an imported user (its `sAMAccountName`) |
| `TABLEAU_LIVE_AD_USER` | the user on its own, as Add User to Site takes it |

- **Add User to Site** matches a name against `sAMAccountName`, or the UPN when the name is longer than 20 characters
  or holds an `@`.
- **Create Group** imports with `<import source="ActiveDirectory" domainName grantLicenseMode siteRole>`; with
  `asJob=true` it answers 202 and a `GroupImport` job.
- **Which form of the group's name and of the domain the import takes is open** (O1): the server knows two directory
  domains, each with a full name and a short name (List Server Active Directory Domains). The build tries the group's
  `sAMAccountName` with the domain's full name first, then the short name, and the settings hold what worked.
- **Kept out of the public log,** where a failed test's message goes and GitHub masks only exact values:
  - `SiteTestAttribute.ExecuteAsync` returns a failed result's message, stack trace and output with every known
    setting and live secret as `***`, case aside. `Scrubbed` moves from `LiveContentTests` to `LiveSite` for it. Today
    only `LiveContentTests.Record` scrubs, and a `TableauException`'s message carries Tableau's detail;
  - the directory tests add each directory string Tableau answers (a user's or a group's name, a domain) to the known
    values as they read it, so a form no setting holds is scrubbed too;
  - they assert with fixed messages that name the setting, never a value:
    `Assert.IsTrue(string.Equals(...), "the import answers TABLEAU_LIVE_AD_GROUP")`, not `Assert.AreEqual` on the
    names, which prints both.

### The backfilled live tests

Everything happens in the run site unless a row says otherwise. A call Tableau refuses for a reason of the server's
(no extract, a licence, a retired feature) is called anyway and its refusal pinned by its code, as S14's tests do.

| Area | Live calls, new in this plan | Where |
|---|---|---|
| Sites | the run site made with revision history and a limit, and deleted (setup and cleanup); read by id, content URL and name, its history and limit read back; listed by `ListAsync` and `ListPageAsync`; updated with another limit and history kept on, which shows Tableau reads a JSON number there too (S12's open item) | `LiveSiteAdminTests`, `LiveSite`, `SuiteCleanup` |
| Sessions | Switch Site from a session on `TABLEAU_LIVE_SITE` into the run site, the first token refused after | `LiveSessionTests` |
| Users | the directory user added as `Unlicensed`, updated to `Viewer`, read, impersonated by a token sign-in (Q5), removed with its assets mapped to the suite's user, all in one test | `LiveDirectoryTests` |
| Groups | the directory group imported at once (its one member listed), synchronised at once and as a job, deleted; imported as a job, found by name and domain, deleted | `LiveDirectoryTests` |
| Favorites | a sample workbook, view and data source and the run's project added to the suite's user's favorites, listed, removed | `LiveAccessTests` |
| Default permissions | each kind on the run's project for a run's group: read, added, deleted; data roles pinned (O3) | `LiveAccessTests` |
| Permissions | a capability on a sample workbook, a view and the data source for a run's group: added, read, deleted; on a catalog database and table | `LiveAccessTests`, `LiveCatalogTests` |
| Group sets | a group set of the run's own (Q2) granted a capability on the run's project, read back, deleted by its `permissions/groupSets/...` path | `LiveAccessTests` |
| Catalog | the samples' database, tables and columns, once the catalog lists them: listed (all, and a page), got, updated (a description), tagged and untagged; then a column, a table and the database deleted | `LiveCatalogTests` |
| Data quality warnings | on the data source and on a column: added, listed, updated, deleted by id, deleted by content | `LiveCatalogTests` |
| Tags | a view, the data source alone, a database, a table and a column, besides S14's workbooks and batches | `LiveContentTests`, `LiveCatalogTests` |
| Metadata API | `QueryPageAsync<T>` called by itself, beside the query and the cursor helper | `LiveAreaTests` |

- **The catalog** lists a new site's assets once the Metadata API has taken in its content, so `LiveCatalogTests` waits
  for the samples' database, up to five minutes, with `LiveSite.EventuallyAsync`.
- **Data Management:** Add Data Quality Warning and the catalog's updates need the server licensed with Data Management;
  the reads don't. Whether this server is, a read can't show (O2): each such call's outcome is pinned, answered or
  refused with Tableau's code, and its row says which.
- **A group set** needs Create Group Set (REST API 3.22), which the 2.0 client doesn't have. The test sends it through
  the client's own pipeline with a test-side `Endpoint` (the test project sees the library's internals), so it signs in,
  escapes and logs as any call does; the site's deletion takes the group set with it (Q2). Its call isn't in
  `Endpoints`, so the matrix has no row for it, and the nightly's check passes over recorded calls the table lacks.
- **Impersonation:** the token's owner is a server administrator and `tsm authentication pat-impersonation enable` is
  on, which lets any server administrator's token impersonate, older tokens included. The directory user is a `Viewer`
  on the run site while the test signs in as it, since an unlicensed user has no session to act in (Q5); the account
  is enabled and needs no password to be impersonated (Q7). Should Tableau still refuse, the row is pinned `refused`
  with Tableau's code, never moved to the token's own user (Q5, O4).

### The ServerOnly tests

| Test | After |
|---|---|
| `ServerOnlyAreaTests.ASiteIsMadeWithARevisionLimitUpdatedOnItAndDeleted` | deleted: the run site, made each night with revision history and a limit, and `LiveSiteAdminTests`, which reads both back and updates the limit, cover it |
| `ServerOnlyAreaTests.AGroupIsImportedFromActiveDirectoryAndSynchronisedAtOnceAndAsAJob` | deleted: `LiveDirectoryTests` |
| `ServerOnlyAreaTests.AUserIsAddedUpdatedAndRemoved` | deleted: `LiveDirectoryTests` |
| `ServerOnlySessionTests.SwitchSiteGivesASessionOnTheOtherSiteAndEndsTheFirst` | deleted: `LiveSessionTests` |
| `ServerOnlySessionTests.APasswordSignInImpersonatesAUser` | kept, by hand: a password's sign-in has no automated run |

Their settings `TABLEAU_SERVER_ONLY_AD_GROUP`, `_AD_DOMAIN`, `_NEW_USER` and `_OTHER_SITE` go, from the tests and from
`LiveSite`'s known settings; `ServerOnly` stays the category for the password sign-ins.

### The live-coverage matrix

`docs/live-coverage.md`: a lead line naming the server the nightly runs on, Tableau Server 2025.1.5, and saying Tableau
Cloud isn't run (Q8); then one table for `TableauClient` with a row for each method and each REST call it sends; then a
table for the Vizportal client, which S16 fills (Q9).

| Column | Holds |
|---|---|
| Method | `ClassName.MethodName`, `<T>` added for a generic method (`MetadataClient.QueryAsync<T>`); overloads that differ only in how a secret is given share a row |
| Call | the call's name as the client's `Endpoints` registry gives it, the reference's name: "Query Workbooks for Site" |
| Live | `yes`; `refused <code>`, Tableau answering with that error for a reason of the server's; or `no` |
| Test, or why not | for `yes` and `refused`, the `Live` tests that make the call (`ClassName.TestName`), or `LiveSite` and `SuiteCleanup` for the suite's own setup and cleanup; for `no`, the reason |

- **Which methods:** every public method of the sub-clients (the types of `TableauClient`'s public properties, as
  `ConventionsTests` finds them) and each `TableauCredentials` factory, each kind of sign-in being its own Sign In call:
  109 methods and 5 factories today. The rest of the public API sends no call of its own and is tested offline.
- **Which calls:** each of `Endpoints.All`'s 168; a call two methods share (a list and its page) has a row under each.
- **Not live, with their reasons,** as things stand:

  | Calls | Why not |
  |---|---|
  | Sign In, and Sign In impersonating a user, with a password | an automated run is given no password; `ServerOnlySessionTests` runs it by hand |
  | Sign In with a unified access token | Tableau Cloud only |
  | Add and Delete Flow from Favorites; a flow's permissions | no flow in the run site, and the 2.0 client can't publish one |
  | a virtual connection's permissions and tags | no virtual connection on the server, and the 2.0 client can't make one |
  | a collection's permissions | the 2.0 client can't make a collection |
  | Download Workbook PDF at a size | REST API 3.26; the server has 3.25, so the client refuses it before sending |
  | Update Workbook Connection and Update Data Source Connection with an authentication type | REST API 3.27, the same |

- **Refused, as things stand:** S14's extract refreshes (403180), a sample's revision downloaded (400057) and removed
  (403055), a workbook's packaged connection updated (400039); the data roles' default permissions (405000, O3); and
  whatever Data Management decides (O2).

### How the matrix stays honest

Checked from both sides: against the code on every PR, against what Tableau answered each night.

- **`LiveCoverageTests`, offline,** reading the table from the test assembly (embedded, as `roots.xml` is), fails when:
  - a method has no row, or a row names a method that doesn't exist;
  - a row names a call that `Endpoints.All` lacks, or a call has no row;
  - a `yes` or `refused` row names a test that isn't a `[SiteTest(TableauSite.Live, ...)]`, nor `LiveSite` or
    `SuiteCleanup`;
  - a `no` row has no reason, or a method and call have two rows.
- **The checker** is a function of its own, tested on invented tables for each failure above, so the real table's test
  can't pass by reading nothing.
- **The nightly's record:**
  - `SiteTestAttribute.ExecuteAsync` sets the site test that runs, and records how it ended. Site tests that sign in run
    one at a time (`[DoNotParallelize]`), and `ServerInfoSiteTests` joins them, so no call is credited to the wrong
    test; a `TableauSiteTests` row fails when a class with a `[SiteTest]` lacks `[DoNotParallelize]`. `LiveSite`'s
    setup and `SuiteCleanup` set their own names while they call.
  - The live clients log through a logger of the suite's that records each answered call's name, status and Tableau
    error code beside the test, and passes every line on to the suite's log check as now.
  - **Answered** means Tableau answered: an EventId 2 line (sending's answer, with its status), or an EventId 3 line
    whose `TableauException` has a `StatusCode` (its `ErrorCode` too). A call the client's gates refuse before sending,
    such as a sized workbook PDF at 3.25, logs an EventId 3 line with no status, and is not answered.
  - The call's name comes from the client: its three Debug lines for a request (sending, answered, failed) gain a `Call`
    property, the descriptor's name. Matching a path to a descriptor in the tests would repeat the client's own choice
    (Sign In by its body, an export by its query, Query Site by `key`) (Q3).
- **The nightly's check,** in `SuiteCleanup` after the cleanup, when `TABLEAU_LIVE_MATRIX=1`, which `live.yml` sets,
  so a local run of one class isn't failed for the rest; it fails the run, naming each row, when:
  - a `yes` row's call wasn't answered with a 2xx in one of its tests;
  - a `refused <code>` row's call wasn't answered with an error carrying that `ErrorCode` in one of its tests, or was
    answered with a 2xx: the row should say `yes`;
  - a `no` row's call was answered: after a server upgrade, the row should change.

  A row whose test failed is left out, since that failure already fails the run; a recorded call the table has no row
  for, such as the test-side Create Group Set, is passed over (Q2).
- **What it can't show:** the record is per test and per call, not per method, so
  - two methods that send one call (a list and its page) in one test are told apart by the test's code, which review
    reads;
  - a call of the same name made inside the named test by a helper, such as `LiveSite`'s or the client's own Server
    Info, also satisfies the row;
  - a `no` row's call is checked to exist, not to be its method's.

### Not chosen

| Option | Why not |
|---|---|
| The connected app for everything | 2025.1 lacks scopes for Switch Site, Query Site, Delete Site, Update User, tags, favorites, the catalog, warnings and the revision lists |
| Switch Site to move the content clients into the run site | a client that owns its session signs in again to its options' site, so it would leave the run site at its first renewal |
| The run's project on the default site, as today | directory users, imports and site settings would land on a site people use; a run site goes whole |
| A second site per run for the sites tests | the run site itself is made, read, listed, updated and deleted |
| An attribute on each live test naming its methods | two places to keep in step, the attributes and the table; the table alone, checked from both sides, is one |
| Matching request paths to calls in the tests | repeats the client's choice of descriptor, Sign In by body and exports by query among it |
| Sub-clients wrapped to record each public method | 109 overrides by hand, or a source generator, for what the call names already give |
| Create Group Set and Delete Group Set in the 2.0 client now | new public API, a one-way door, for one test; a follow-up issue can add them |

## Compatibility

- **One-way doors:** none. No public member changes.
- **The client's Debug log:** the lines for a request (EventIds 1, 2 and 3) gain the call's name, as a `Call` property
  and in their text: `Tableau client {Client}: {Call}: {Method} {RequestPath} answered {StatusCode} in ...`. A reader
  that matches those lines' exact text sees a new part; structured readers see one property more. CHANGELOG says so.
- **Tableau versions:** none for users. The live suite becomes Tableau Server only (Q8): it needs Create Site and Switch
  Site, a server administrator's token, PAT impersonation on, and the directory accounts. `TableauSite.cs` and
  `AGENTS.md` say so, and Tableau Cloud stays checked by hand (the modernization plan's R3).
- **The harness:** `live.yml` and the site tests' checks get stricter (a matrix on every PR and each night); nothing is
  loosened. The `ServerOnly` tests the nightly now runs are deleted, not skipped.

## Steps

Two PRs (Q1): the first is step 1, `Refs #57`, so `main`'s nightly turns green; the second is steps 2 to 9,
`Closes #57`, on a branch from `main` once the first has merged. Each step is a commit that builds and passes
`dotnet run eng/gate.cs -- full`, and each from step 3 ends with a local run of every `Live` test on Tableau Server
2025.1.5 with `TABLEAU_LIVE_MATRIX=1`, the settings mapped from outside the clone.

1. **The credential split** (Design, Two sign-ins):
   - `live.yml` maps `TABLEAU_LIVE_PAT_NAME` and `TABLEAU_LIVE_PAT_SECRET`, its header says which sign-in does what, and
     its scopes line names `tableau:content:read` alone;
   - `TableauSite` and `SiteTestAttribute` take the sign-in a test needs; `LiveSite` gives the token to every content
     test and the app's settings to its own; `LiveConnectedAppTests` with direct trust and `Jwt`, the minted JWT kept
     with the live secrets.
   - **Proves it:** `TableauSiteTests` rows: each sign-in's missing settings fail by name, and a test needing one isn't
     satisfied by the other's; a local run, the token and the app's settings mapped from outside the clone: the 13
     `Live` tests and the two new ones pass.
   - **Ends with `finish-work`:** the full gate, a draft PR naming the live run (Tableau Server 2025.1.5, what passed),
     `review-code` until clean, a maintainer's merge; then `live.yml` by hand on `main`, green.
2. **The call's name in the log:** `TableauLog`'s three request lines take the endpoint's name; their callers pass it.
   - **Proves it:** `LogTests` rows: each line names its call, Query Projects among them; the log check still finds no
     secret.
3. **The matrix and its checks:** `docs/live-coverage.md` with every row as step 1 leaves it, the rows steps 4 to 8 make
   live saying `no: not live yet (#57)`; the table embedded in the test project; `LiveCoverageTests`; the record and the
   nightly's check; `live.yml` sets `TABLEAU_LIVE_MATRIX=1`.
   - **Proves it:** the checker's rows on invented tables, one per failure above, and on an invented record, one per
     failure of the nightly's check, a `refused` row with another code and a call the table lacks among them; a
     `TableauSiteTests` row failing for a `[SiteTest]` class without `[DoNotParallelize]`; `LiveCoverageTests` passes
     on the real table; a local full `Live` run with `TABLEAU_LIVE_MATRIX=1` passes, and fails when a `yes` row is
     pointed at a test that doesn't make its call.
4. **A site per run** (Design, A site per run):
   - first, a read-only list of the default site's run projects and run groups by the old rules, its count for this
     step's progress row; a maintainer deletes any by hand;
   - `RunSiteAsync` (revision history and a limit, its answer checked against `IsRunSite`), `RunClientAsync`, the
     cleanup's Delete Site, the site sweep with the cleanup's options and its failures named by `SuiteCleanup`;
   - every content test on the run site, the destructive ones on their own samples (`OwnSamplesAsync`); the Metadata
     API test waiting for the run's samples and paging by one, the users test paging by one;
   - the project and group sweeps and their rows go; `live.yml`'s `timeout-minutes` goes from 20 to 45.
   - **Proves it:** `LiveSiteTests` rows for the site sweep: a day-old run site swept; a recent one, one named
     otherwise, one whose content URL or name differs by a character or by case, and the default site kept; a row that
     `RunSiteAsync`'s check refuses an answer the sweep wouldn't take; the `Live` tests pass on the run site, S14's
     revision lists among them; after the run, a read-only Query Sites lists no run site. Rows: Create Site, Delete
     Site, Query Sites.
5. **Sites and Switch Site:** `LiveSiteAdminTests`; Switch Site in `LiveSessionTests`; `ServerOnlyAreaTests`' site test
   and `ServerOnlySessionTests`' Switch Site test deleted.
   - **Proves it:** the new tests pass; the run site's revision history and limit read back as made, and the updated
     limit as sent, history still on. Rows: the sites' reads, lists and update, Switch Site.
6. **Directory users and groups, and impersonation:** the forms the import takes settled first, in the run site (O1);
   the four settings in `live.yml` and `LiveSite`; the failed results' scrubbing in `SiteTestAttribute.ExecuteAsync`,
   `Scrubbed` moved to `LiveSite`; `LiveDirectoryTests`, its user test impersonating the user it adds;
   `ServerOnlyAreaTests` deleted.
   - **Proves it:** a `TableauSiteTests` row: a failed result whose message, stack trace and output hold a known
     setting in another case comes back with `***`; the import lists the one member named by
     `TABLEAU_LIVE_AD_GROUP_MEMBER`; the synchronisations answer the group and a `GroupSync` job; the import job's group
     is found and deleted; the user's role reads back `Viewer`; the impersonating session's user is the directory
     user's LUID, or the refusal is pinned (O4); the test output holds none of the four settings' values. Rows: Add
     User to Site, Update User, Remove User from Site, Create Group from Active Directory, Update Group from Active
     Directory, Sign In with a personal access token, impersonating a user.
7. **Favorites and permissions:** `LiveAccessTests`, the group set's test-side descriptor.
   - **Proves it:** each favorite listed after its add and gone after its remove; each kind's default rule read back
     and gone after its delete; each sample's capability read back and gone; the group set's capability read back and
     gone. Rows: favorites, default permissions, permissions on workbooks, views and data sources.
8. **The catalog, warnings and tags:** `LiveCatalogTests`; view and data source tags in `LiveContentTests`;
   `QueryPageAsync<T>` in `LiveAreaTests`.
   - **Proves it:** each update read back, or its refusal pinned (O2); each warning read back, changed and gone; each
     tag held once and gone; each deleted asset refused as not found after. Rows: the catalog, the warnings, the tags,
     database and table permissions.
9. **The last rows and the docs:** no row left saying `not live yet`; the docs (Docs); this plan's progress log.
   - **Proves it:** `LiveCoverageTests`; `dotnet run eng/gate.cs -- quick` (AGENTS.md's length, links, skills).
   - **Ends with `finish-work`:** the full gate, a draft PR naming the live run (Tableau Server 2025.1.5, what passed,
     what was refused, what Tableau Cloud couldn't show), `review-code` until clean, a maintainer's merge; then
     `live.yml` by hand on `main`, then a night.

## Tests

| Suite | What is new or changed | Where it runs |
|---|---|---|
| Offline | `TableauSiteTests` (each sign-in's settings, a failed result scrubbed, `[DoNotParallelize]` on every site test class); `LiveSiteTests` (the site sweep's rule and `RunSiteAsync`'s check, the project and group rules gone); `LiveCoverageTests` (the table against the code, and its checker on invented tables and records); `LogTests` (the call's name in three lines) | every PR, Linux and Windows, under the gate's coverage |
| `Live` | `LiveConnectedAppTests`, `LiveSiteAdminTests`, `LiveDirectoryTests` (impersonation in its user test), `LiveAccessTests`, `LiveCatalogTests`; Switch Site in `LiveSessionTests`; more tags in `LiveContentTests`; every content test on the run site, the destructive ones on samples of their own | nightly and by hand from `main` (`live.yml`), and locally, on the maintainers' Tableau Server 2025.1.5, REST API 3.25; never on Tableau Cloud (Q8) |
| `ServerOnly` | the password sign-ins alone | by hand, on a Tableau Server |

- **The gate:** `dotnet run eng/gate.cs -- full` on each commit; coverage of the library can only rise, since only
  `TableauLog`'s three lines change there.
- **What none of them shows:** Tableau Cloud; calls above REST API 3.25 (a sized workbook PDF, a connection's
  authentication type); flows, virtual connections and collections; a password's sign-in; unified access tokens;
  Add Data Quality Warning and the catalog's updates if the server lacks Data Management; a server whose user licences
  are all taken.

## Docs

- `docs/live-coverage.md`, new: its lead line names Tableau Server 2025.1.5 and says Tableau Cloud isn't run (Q8); its
  Vizportal table waits for S16 (Q9).
- `AGENTS.md`, rows edited in place (it holds 148 of its 150 lines): the `docs/` row names `live-coverage.md`; the
  `tests/` row's `LiveSite.cs` says run site, not run project and groups, and names `LiveCoverageTests`; the `.github/`
  row's `live.yml` says it signs in with the CI token in a site of the run's own, on Tableau Server only; Build and
  test's site-test bullet says the `Live` tests run on a Tableau Server alone (Q8) and a new public method adds its
  rows to `docs/live-coverage.md`.
- `TableauSite.cs`: `Live` runs on a Tableau Server alone, as a server administrator, where its summary says Tableau
  Cloud or a Tableau Server (Q8).
- `.claude/skills/finish-work/SKILL.md`, step 3: a live test works in the run's own site, which the suite deletes, with
  the token's settings; the connected app's settings drive its own tests.
- `.github/workflows/live.yml`'s header; the summaries in `LiveSite.cs`, `SuiteCleanup.cs` and the live test classes.
- `CHANGELOG.md`, Unreleased: the Debug lines name the call.
- `docs/plans/2026-10-28-modernization.md`: a progress-log row for #57 (the token, the run site, the matrix that S15 to
  S17 follow and S18 publishes); in For the maintainers, the After S14 item on the JWT's 3.27 scopes closed by it, and
  the two items below added.
- **For the maintainers:** the four directory settings in `tableau-live` once step 6 has found their forms; the CI
  token's expiry, a year after it was made, renewed before then (a token unused for 15 days expires sooner, which the
  nightly's use prevents).

## Risks

| Risk | Status |
|---|---|
| Delete Site outlasts the cleanup's ten minutes, or the run breaks off, and a run site stays | Mitigated: the next run's sweep deletes it after a day, with the same ten minutes; a failed cleanup fails the run and names the site |
| The sweep stops matching run sites, or can't delete one, and leftovers pile up unseen | Mitigated: `RunSiteAsync` fails when Create Site's answer wouldn't pass the sweep's rule; the content URL's stamp is digits alone; a leftover the sweep can't delete fails that night's run by name, after every test has run |
| One token allows one session: Switch Site and the impersonating sign-in end the token's other session | Mitigated: the site tests run one at a time, each client is disposed before the next signs in, and a test takes the run site before its first sign-in; local runs use another token |
| The catalog takes longer than five minutes to list a new site's assets | Accepted: the test fails naming the wait; a rerun shows whether it was the index |
| The server isn't licensed for Data Management | Accepted: those calls are pinned as refused, and the rows say so (O2) |
| The directory user as a `Viewer` takes a user licence; with none free, Tableau adds it `Unlicensed` and answers 201 | Accepted: the test fails naming the role it got; the maintainer frees a licence or the test drops the impersonation |
| Tableau won't let a token impersonate the directory account | Open (O4): the row is pinned `refused` with Tableau's code; it never falls back to the token's own user (Q5) |
| The import takes another form of the group's name or domain than the settings give | Open (O1): settled before the settings are set |
| A directory name reaches the public Actions log, through a failed test's message or Tableau's detail in an exception | Mitigated for the settings' values: environment secrets, which GitHub masks when exact; known settings, which `SiteTestAttribute` scrubs, case aside, from a failed result's message, stack trace and output, and the suite from its own output and fixtures; each directory string Tableau answers added to them at run time; assertions with fixed messages. Accepted for a form no setting holds and no answer the test read gives |
| Tests that share content depend on their order | Mitigated: the shared samples are never deleted, and a test that deletes content owns samples of its own |
| The run gets longer: a site made and deleted, imports, the catalog's wait | Mitigated: `live.yml`'s `timeout-minutes` goes from 20 to 45 |
| The matrix check fails on a night when a test failed early | Mitigated: a row whose test failed is left out |
| A user's tooling matches the client's Debug lines by their exact text | Accepted: a property is added, nothing removed; CHANGELOG says so |
| The directory users stay on the server once the run site goes | Accepted: they are the maintainer's technical accounts, added again each night |

## Owner calls

Q1 to Q9, with the maintainer's answers on #57, are in Plan review, Round 1.

## Open items

What the build settles, on the server, before the row or the setting it decides:

| # | Item | How the build settles it |
|---|---|---|
| O1 | Which form of the group's name and domain Create Group's import takes | an import into the run site with the group's `sAMAccountName` and the domain's full name, then its short name; the settings get what worked |
| O2 | Whether the server is licensed for Data Management | Add Data Quality Warning on the run's data source: answered or refused, and each row follows |
| O3 | The data roles' default permissions answer 405000 on Tableau Server 2025.1 (a read-only probe) | pinned as refused; whether the 2.0 client keeps `DefaultPermissionsKind.DataRoles` is a follow-up issue |
| O4 | Whether a token can impersonate the directory account, enabled with a password nobody holds (Q7) | the impersonating sign-in: answered, or refused and pinned `refused` (Q5) |

**Looked up** (2026-11-01): the reference's Sign In, Switch Site, Create Site, Delete Site, Query Sites, Add User to
Site, Create Group, Update Group, the favorites, the permissions and default permissions, the metadata page (data
quality warnings, databases, tables, columns, tags), Batch Add and Delete Tags, List Group Sets and Create Group Set,
and every scope line above; Tableau Server's personal access token page (impersonation on with
`tsm authentication pat-impersonation enable`, for every server administrator's token, older ones included; one session
per token, at any site; 15 days unused, a year at most). **A read-only probe** with the local token on Tableau Server
2025.1.5 (REST API 3.25), statuses and counts only: the token's user is a server administrator; Query Sites answers,
two sites, none a run's; List Server Active Directory Domains answers two domains, each with a name and a short name;
List Group Sets answers, none on the default site; Query Data Quality Warning by Content answers on a data source; the
default site's catalog lists databases and tables; it has flows and no virtual connection; favorites answer; default
permissions answer for workbooks, data sources, flows, virtual connections, databases and tables, and 405000 for data
roles.

## Progress log

| Date | Step | What happened |
|---|---|---|
| 2026-11-01 | plan | Written on `57-live-coverage` from `origin/main` at `c7ffe6f`, with the maintainer's decisions on #57 and the CI token in `tableau-live`. |
| 2026-11-01 | plan | Plan review round 1 on `993a9e1`: eight minors, all fixed; the maintainer answered Q1 to Q9 on #57 and took every improvement. Awaiting the maintainer's approval. |
| 2026-11-01 | 1 | The credential split: `SignsIn` takes `LiveSignIn.Token` or `LiveSignIn.ConnectedApp`; every content test signs in with the token; `LiveConnectedAppTests` signs in by direct trust and with `Jwt`, the JWT it mints kept with the live secrets; `live.yml` maps the token, and its JWT asks for `tableau:content:read` alone. `gate full` passed: 972 tests, lines 97.61%, branches 95.08%. A local run on Tableau Server 2025.1.5 (REST API 3.25) with the local token, `TABLEAU_LIVE=1` and `TestCategory=Live`: the 13 `Live` tests passed, in 50 s with the setup. The two `LiveConnectedAppTests` failed before any call, naming `TABLEAU_LIVE_CLIENT_ID`, `TABLEAU_LIVE_SECRET_ID`, `TABLEAU_LIVE_SECRET_VALUE` and `TABLEAU_LIVE_USER`, which the local settings don't hold: their first run on a site is `live.yml`'s on `main`. A read-only check after: no run group on the default site, the token's sessions signed out, and one run project, left by a local run of 2026-10-31 and not this one's, which the sweep takes once it is a day old. |
| 2026-11-01 | 2 | On `57-live-coverage-matrix` from `origin/main` at `c608a9c`. The call's name in the log: `TableauLog`'s sending, answered and failed lines take the descriptor's name, as `Call` and in their text; `RecordingLogger` keeps each line's event and properties, and a `LogTests` row reads both for Sign In, Query Projects and Delete Project, with no secret or query value in any line. CHANGELOG's logging line says so. `gate full` passed: 979 tests, lines 97.61%, branches 95.08%. |
| 2026-11-01 | 3 | The matrix and its checks: `docs/live-coverage.md`, 191 rows for 114 methods and 168 calls, 64 `yes`, 7 `refused`, 120 `no`, 98 of them `not live yet (#57)`, and an empty Vizportal table; embedded in the test assembly; `LiveCoverage` reads it and checks it against the code and against a run's `CallRecord`, which every live client logs through and `SiteTestAttribute` names the test for; `ServerInfoSiteTests` runs one at a time and logs through it; `SuiteCleanup` checks the run with `TABLEAU_LIVE_MATRIX=1`, which `live.yml` sets. One reading taken: a `no` row's call answered fails the run unless a `yes` or `refused` row holds that call live, since the record is per call and `QueryPageAsync<T>` was `not live yet` while `QueryAsync` sent its call. Three methods and calls had neither a test nor a step: `GroupsClient.ListPageAsync`, `ViewsClient.ListPageAsync` and Query View Image without a size; steps 6 and 8 call them. `gate full` passed: 1003 tests, lines 97.61%, branches 95.08%. A local run, every `Live` test with `TABLEAU_LIVE_MATRIX=1` on Tableau Server 2025.1.5: 13 passed in 60 s, the two connected app tests failed before any call for their missing settings, as in step 1, and the matrix check passed; with `UsersClient.GetAsync`'s row pointed at the Metadata API test, the cleanup failed the run naming that row. |
| 2026-11-01 | 4 | First, read only, by the old rules: the default site holds 1 run project and 0 run groups, which a maintainer deletes by hand; it has 2 sites, none a run's. A site per run: `RunSiteAsync` makes `Tableau.Api live run <run> <made>` with revision history and a limit of 25 after the sweep, and refuses an answer `IsRunSite` wouldn't take; every content test signs in there through `RunClientAsync`; the data source and tags tests take samples of their own (`OwnSamplesAsync`); the workbook test changes its shared sample back; the Metadata API test waits for the run's samples by a filter on the run's project and pages by one, the users test pages by one; the cleanup deletes the site from a session on it with ten minutes, and fails naming any site left, the sweep's too. The project and group sweeps and their rows went; `live.yml` waits 45 minutes. `gate full` passed: 1003 tests, lines 97.61%, branches 95.08%. A local run, every `Live` test with `TABLEAU_LIVE_MATRIX=1`: 13 passed in 68 s on the run's site, S14's revision lists among them, the connected app's two failed for their settings as before, the matrix check passed; refused as pinned: Download Workbook and Data Source Revision 400057, Remove Workbook and Data Source Revision 403055, Update Workbook Connection 400039, Update Workbook Now and Update Data Source Now 403180; the sized workbook PDF refused before sending. After it, Query Sites lists no run site. Rows: Create Site, Delete Site, Query Sites. |
| 2026-11-01 | 5 | Sites and Switch Site: `LiveSiteAdminTests` reads the run's site by id and content URL, its history on and its limit 25 as made, lists the sites whole and a page of one, and updates the limit to 30, history on, read back as sent; `LiveSessionTests` switches a session on `TABLEAU_LIVE_SITE` to the run's site, the first token refused after; `ServerOnlyAreaTests`' site test, `ServerOnlySessionTests`' Switch Site test and `TABLEAU_SERVER_ONLY_OTHER_SITE` went. A departure: Query Site by name answers 404000, "Site 'Tableau' could not be found", since Tableau Server 2025.1 reads what follows a path segment's last dot as a format, and the run's site's name holds one; a read-only probe on a run's site found the site with a trailing `.json` or `.`, not with the dot as `%2E`. The test calls it and pins the refusal, its row says `refused 404000`, and what the library should do with a name holding a dot is a follow-up issue. `gate full` passed: 1003 tests, lines 97.61%, branches 95.08%. A local run, every `Live` test with `TABLEAU_LIVE_MATRIX=1`: 15 passed in 65 s, the connected app's two failed for their settings, the matrix check passed. Rows: Query Site, by content URL, by name (refused), Query Sites' page, Update Site, Switch Site. |

## Plan review

### Round 1

- **How:** a fresh reviewer agent with review-plan's method and lenses, and a fresh refuter agent for the one finding
  it rated major, P1 (Claude Opus 5.5), on `993a9e1`, 2026-11-01.
- **Verdict:** approved. Eight findings, all minor: the refuter confirmed P1 on its facts and rated it minor, since a
  directory name isn't a credential, the leak needs a test to fail first, and #57's ruling keeps the names out of the
  repo, not the CI log. No blocker or major is open, every owner call has its answer, and the plan names its checks
  and docs and ends each PR with `finish-work` and `review-code`. No round 2.
- **Now:** every finding below is fixed in the plan, the answers to Q1 to Q9 are folded into its design and steps, and
  every improvement is taken, in the commit "Apply plan review round 1". @serionist approved the plan on 2026-11-01.

| # | Sev | Finding | Status |
|---|---|---|---|
| P1 | minor (major, refuted to minor) | Nothing scrubs a failed test's message: only `LiveContentTests.Record` scrubs, `SiteTestAttribute.ExecuteAsync` returns results unchanged, a `TableauException`'s message carries Tableau's detail, the log check looks for secrets only, and GitHub masks exact values only. If the import answers the domain's other form, the ported `Assert.AreEqual((name, domain), ...)` prints it in the public log | fixed: `ExecuteAsync` returns a failed result's message, stack trace and output with every known setting and live secret as `***`, case aside (`Scrubbed` moves to `LiveSite`); the directory tests add each directory string Tableau answers to the known values and assert with fixed messages naming the setting; the Risks row says mitigated for the settings' values and accepted for a form no setting holds, without the log check; step 6 proves it with a `TableauSiteTests` row (The directory accounts, Risks, step 6) |
| P2 | minor | On a fresh site the Metadata API test compares two counts while the index grows, and with two workbooks at a page of two it never follows a cursor | fixed: it waits until a query filtered on the run's project lists both samples, compares counts under that filter, and pages by one; the users test pages by one (A site per run, step 4) |
| P3 | minor | The tests depend on their order: S14's data source test deletes the only sample data source, and `LiveCatalogTests` deletes catalog assets other tests would read; impersonation sat in two classes | fixed: the shared samples are never deleted; a test that deletes content owns samples of its own (`OwnSamplesAsync`), S14's data source and tags tests and `LiveCatalogTests` among them; impersonation stays in the test that adds the user (A site per run, The backfilled live tests, steps 4 and 6) |
| P4 | minor | "Answered" was undefined: a call the client's gates refuse logs an EventId 3 line with no status, such as S14's sized workbook PDF at 3.25 | fixed: answered is an EventId 2 line, or an EventId 3 line whose `TableauException` has a `StatusCode` (How the matrix stays honest) |
| P5 | minor | Create Site leaves revision history off by default, which breaks S14's revision lists on the run site, and deleting the `ServerOnly` site test dropped the live proof that Create Site sends `RevisionLimit` as a JSON number | fixed: the run site is made with `RevisionHistoryEnabled = true` and a `RevisionLimit`, `LiveSiteAdminTests` reads both back, and the update keeps history on (A site per run, The ServerOnly tests, steps 4 and 5) |
| P6 | minor | The sweep could stop silently: a case change by Tableau in the content URL's `T` and `Z` would never match; its Delete Site ran at the default timeout, unretried; a leftover it couldn't delete threw from `RunSiteAsync` and failed every content test, with "gone" not told from a refused sign-in | fixed: the content URL's stamp is digits alone, and `RunSiteAsync` fails unless Create Site's answer passes the sweep's own rule; the sweep deletes with the cleanup's options; a leftover it can't delete is named by `SuiteCleanup` at the run's end, and a failed sign-in is told apart by listing the sites again (A site per run, Risks, step 4) |
| P7 | minor | Removing the default site's project and group sweeps would orphan any old run project or group there | fixed: before they go, a read-only list by the old rules, its count in step 4's progress row, and a maintainer deletes any by hand (A site per run, step 4) |
| P8 | minor | Neither PR's last step ended with `finish-work` and `review-code` | fixed: steps 1 and 9 each end with the full gate, a draft PR naming the live run, `review-code` until clean, and a maintainer's merge (Steps) |

**Refuted:**

- **The `Call` log property as a one-way door:** `TableauLog` is internal, and log text is not among `AGENTS.md`'s
  doors; the plan names the change under Compatibility and CHANGELOG.
- **The sweep deleting a maintainers' site:** it takes only a site whose name and content URL are a run's exactly and
  more than a day old, and the default site can't be deleted.
- **Step 1 alone not turning the nightly green:** the 401002s were the JWT's scope refusals, and the same tests passed
  13 of 13 with a token.
- **The sweep's sign-in to a leftover breaking its own client:** the client signs in again once after a 401.
- **The cleanup's session signing a test out:** `[AssemblyCleanup]` runs after every test.
- **A failed run keeping a directory user's licence:** the role lives on the run site, which the cleanup or the next
  sweep deletes.
- **The group set needing public API:** the test-side descriptor sends it through the client's internal pipeline.
- **The job's timeout too short:** the plan raises it to 45 minutes.
- **Delete Site from a session on the site misreading the reference:** the reference asks for "the authentication token
  that you got back when you signed into the site".
- **Deleting the `ServerOnly` tests loosening the harness:** each assertion moves to a nightly `Live` test; the one that
  didn't, Create Site's revision limit, is P5, now fixed.

**Owner calls** (the maintainer's answers on #57, 2026-11-01):

| # | Question | Recommendation | Answer |
|---|---|---|---|
| Q1 | One PR, or the credential split first on its own? | Two: step 1 first, so `main`'s nightly is green within a day; steps 2 to 9 on a second branch from `main` | As recommended: two PRs, the token split first, `Refs #57`, then `Closes #57` |
| Q2 | A group set for the permission test: through a test-side descriptor on the client's pipeline, in the run site; Create and Delete Group Set in the 2.0 client (a door); or group sets not live | The test-side descriptor: no public API, and the run site takes the group set with it | As recommended; the nightly's check passes over recorded calls the table has no row for |
| Q3 | The record needs each call's name: add it to the client's Debug lines (users see it), or match paths in the tests | The Debug lines: one place, and a log reader learns the call too | As recommended: `Call` in the Debug lines |
| Q4 | `ServerOnly` keeps only the password sign-ins, the rest folded into the nightly and deleted | Yes | As recommended, with P5's fix |
| Q5 | Impersonation acts as the directory user, made a `Viewer` for the test's length (a user licence, briefly); or as the token's own user, which proves less | The directory user | As recommended; if Tableau refuses, the row is pinned `refused`, with no fall back to the token's own user |
| Q6 | The nightly's matrix check runs only with `TABLEAU_LIVE_MATRIX=1`, set by `live.yml`, so a local run of one class isn't failed | Yes | As recommended |
| Q7 | Are the technical directory accounts disabled, or enabled without a way to sign in? Tableau may refuse to add, import or impersonate a disabled account (O1, O4) | Enabled, with no way to sign in | They are enabled, each with a random password nobody holds |
| Q8 | Every content test needs Create Site, which Tableau Cloud doesn't offer: does the live suite become Tableau Server only? | Yes, said in `TableauSite.cs` and `AGENTS.md`, and in the matrix's lead line, which S18 publishes | Tableau Server only: `TableauSite.cs` and `AGENTS.md` say so, the matrix's lead line names Tableau Server 2025.1.5 and says Cloud isn't run, and the modernization plan's R3, Tableau Cloud checked by hand, stands |
| Q9 | The Goal says S15 to S17 add rows: does S16's Vizportal client, not a sub-client, get rows? | Say which: a table of its own, or not covered | A table of its own in `docs/live-coverage.md`, filled by S16 |

**Improvements:**

| Improvement | Answer |
|---|---|
| A `TableauSiteTests` row failing when a `[SiteTest]` class lacks `[DoNotParallelize]` | taken (How the matrix stays honest, step 3) |
| The nightly's check compares a `refused <code>` row's code with the recorded `TableauException.ErrorCode` | taken (How the matrix stays honest, step 3) |
| The JWT the `Jwt` test mints joins `LiveSite`'s live secrets | taken (Two sign-ins, step 1) |
| A test takes the run site before its first sign-in, so `RunSiteAsync`'s sign-in can't end a default-site session the Switch Site test holds | taken (A site per run, Risks) |
| "What it can't show" widened: the record is per test, so a call of the same name made by a helper inside the named test also satisfies a row | taken (How the matrix stays honest) |
| The `timeout-minutes` change named in step 4, not only under Risks | taken (step 4) |
