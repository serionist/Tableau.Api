---
title: Live-test almost every 2.0 method, with a site per run and a live-coverage matrix
issue: "#57"
status: draft
approved_by:
approved_at:
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
    live too.
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

- **The site:** named `Tableau.Api live run <run> <made>`, content URL `tableau-api-live-run-<run>-<made>`, where
  `<run>` is `LiveSite.Run` (CI's run and attempt, or a local run's time and random part) and `<made>` the second it was
  made, in UTC (`yyyyMMddTHHmmssZ`): a site has no creation date in the REST API, so its name carries one, as a run's
  groups' names do today. The content URL uses only letters, digits and hyphens, which Create Site keeps as given.
- **Made once a run,** by `LiveSite.RunSiteAsync`, under a lock, from a client of the token on `TABLEAU_LIVE_SITE`,
  after the sweep; that client is disposed before anything signs in to the run site.
- **Worked in:** every content test gets its client from `LiveSite.RunClientAsync()`, whose options name the run site,
  so the client signs in there with the token, and signs in there again on renewal or after a 401. Switch Site is not
  how the content clients get there: a client that owns its session signs in again to its options' site, so one moved by
  Switch Site would land on the default site after a renewal, and a bound client never renews. Switch Site gets its own
  test.
- **Inside it,** as today: the run's project made with `publishSamples=true`, so Tableau publishes two sample workbooks
  and their data source into it; the run's local groups. Every list in the site holds only the run's content, so Query
  Workbooks for User and the Metadata API read nothing else.
- **Deleted at the end** by `LiveSite.CleanupAsync`: Delete Site from a session on the run site, as the reference asks
  ("you must include the authentication token that you got back when you signed into the site"). The cleanup's client
  has a `RequestTimeout` of ten minutes, since a site with content can take long to delete and Delete Site is never sent
  twice. The site's projects, groups, content, catalog assets, group set and users' memberships go with it.
- **The sweep,** before the run site is made: Query Sites, and for each site whose name and content URL are a run's
  exactly and whose `<made>` is more than a day ago, a sign-in to it and Delete Site. A site already gone is passed
  over. Any other site stays, whatever its name.
- **What goes:** the project and group sweeps on the default site, with `IsRunProject`, `IsRunGroup` and their rows in
  `LiveSiteTests`; the site sweep's rule takes their place there.

### The directory accounts

The maintainer made technical Active Directory accounts on the server's domain: a group with one member, and a user on
its own; none can sign in. Their names never enter the repo; they reach the suite as settings, `tableau-live`
environment secrets in CI (so GitHub masks them too), and join `LiveSite`'s known settings, which every test's output,
every recorded fixture and the end-of-run scan scrub.

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

### The backfilled live tests

Everything happens in the run site unless a row says otherwise. A call Tableau refuses for a reason of the server's
(no extract, a licence, a retired feature) is called anyway and its refusal pinned by its code, as S14's tests do.

| Area | Live calls, new in this plan | Where |
|---|---|---|
| Sites | the run site made and deleted (setup and cleanup); read by id, content URL and name; listed by `ListAsync` and `ListPageAsync`; updated with a revision limit, which shows Tableau reads a JSON number there (S12's open item) | `LiveSiteAdminTests`, `LiveSite`, `SuiteCleanup` |
| Sessions | Switch Site from a session on `TABLEAU_LIVE_SITE` into the run site, the first token refused after; a token sign-in impersonating the directory user (Q5) | `LiveSessionTests` |
| Users | the directory user added as `Unlicensed`, updated to `Viewer`, read, impersonated, removed with its assets mapped to the suite's user | `LiveDirectoryTests` |
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
  escapes and logs as any call does; the site's deletion takes the group set with it (Q2).
- **Impersonation:** the token's owner is a server administrator and `tsm authentication pat-impersonation enable` is
  on, which lets any server administrator's token impersonate, older tokens included. The directory user is a `Viewer`
  on the run site while the test signs in as it, since an unlicensed user has no session to act in (Q5). Whether
  Tableau lets a token act as a directory account that can't sign in itself is open (O4).

### The ServerOnly tests

| Test | After |
|---|---|
| `ServerOnlyAreaTests.ASiteIsMadeWithARevisionLimitUpdatedOnItAndDeleted` | deleted: the run site and `LiveSiteAdminTests` cover it nightly |
| `ServerOnlyAreaTests.AGroupIsImportedFromActiveDirectoryAndSynchronisedAtOnceAndAsAJob` | deleted: `LiveDirectoryTests` |
| `ServerOnlyAreaTests.AUserIsAddedUpdatedAndRemoved` | deleted: `LiveDirectoryTests` |
| `ServerOnlySessionTests.SwitchSiteGivesASessionOnTheOtherSiteAndEndsTheFirst` | deleted: `LiveSessionTests` |
| `ServerOnlySessionTests.APasswordSignInImpersonatesAUser` | kept, by hand: a password's sign-in has no automated run |

Their settings `TABLEAU_SERVER_ONLY_AD_GROUP`, `_AD_DOMAIN`, `_NEW_USER` and `_OTHER_SITE` go, from the tests and from
`LiveSite`'s known settings; `ServerOnly` stays the category for the password sign-ins.

### The live-coverage matrix

`docs/live-coverage.md`: a lead line, then one table with a row for each method and each REST call it sends.

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
    test. `LiveSite`'s setup and `SuiteCleanup` set their own names while they call.
  - The live clients log through a logger of the suite's that records each answered call's name and status beside the
    test, and passes every line on to the suite's log check as now.
  - The call's name comes from the client: its three Debug lines for a request (sending, answered, failed) gain a `Call`
    property, the descriptor's name. Matching a path to a descriptor in the tests would repeat the client's own choice
    (Sign In by its body, an export by its query, Query Site by `key`) (Q3).
- **The nightly's check,** in `SuiteCleanup` after the cleanup, when `TABLEAU_LIVE_MATRIX=1`, which `live.yml` sets,
  so a local run of one class isn't failed for the rest; it fails the run, naming each row, when:
  - a `yes` row's call wasn't answered with a 2xx in one of its tests;
  - a `refused` row's call wasn't answered with an error in one of its tests, or was answered with a 2xx: the row
    should say `yes`;
  - a `no` row's call was answered: after a server upgrade, the row should change.

  A row whose test failed is left out, since that failure already fails the run.
- **What it can't show:** two methods that send one call (a list and its page) in one test are told apart by the test's
  code, which review reads, not by the record; a `no` row's call is checked to exist, not to be its method's.

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
- **Tableau versions:** none for users. The suite needs a Tableau Server (Create Site and Switch Site are Server only),
  a server administrator's token, PAT impersonation on, and the directory accounts; Tableau Cloud stays checked by hand.
- **The harness:** `live.yml` and the site tests' checks get stricter (a matrix on every PR and each night); nothing is
  loosened. The `ServerOnly` tests the nightly now runs are deleted, not skipped.

## Steps

Two PRs (Q1): the first is step 1, so `main`'s nightly turns green; the second is steps 2 to 9. Each step is a commit
that builds and passes `dotnet run eng/gate.cs -- full`, and each from step 3 ends with a local run of every `Live`
test on Tableau Server 2025.1.5 with `TABLEAU_LIVE_MATRIX=1`, the settings mapped from outside the clone.

1. **The credential split** (Design, Two sign-ins):
   - `live.yml` maps `TABLEAU_LIVE_PAT_NAME` and `TABLEAU_LIVE_PAT_SECRET`, its header says which sign-in does what, and
     its scopes line names `tableau:content:read` alone;
   - `TableauSite` and `SiteTestAttribute` take the sign-in a test needs; `LiveSite` gives the token to every content
     test and the app's settings to its own; `LiveConnectedAppTests` with direct trust and `Jwt`.
   - **Proves it:** `TableauSiteTests` rows: each sign-in's missing settings fail by name, and a test needing one isn't
     satisfied by the other's; a local run, the token and the app's settings mapped from outside the clone: the 13
     `Live` tests and the two new ones pass. After the merge, `live.yml` by hand on `main` is green.
2. **The call's name in the log:** `TableauLog`'s three request lines take the endpoint's name; their callers pass it.
   - **Proves it:** `LogTests` rows: each line names its call, Query Projects among them; the log check still finds no
     secret.
3. **The matrix and its checks:** `docs/live-coverage.md` with every row as step 1 leaves it, the rows steps 4 to 8 make
   live saying `no: not live yet (#57)`; the table embedded in the test project; `LiveCoverageTests`; the record and the
   nightly's check; `live.yml` sets `TABLEAU_LIVE_MATRIX=1`.
   - **Proves it:** the checker's rows on invented tables, one per failure above, and on an invented record, one per
     failure of the nightly's check; `LiveCoverageTests` passes on the real table; a local full `Live` run with
     `TABLEAU_LIVE_MATRIX=1` passes, and fails when a `yes` row is pointed at a test that doesn't make its call.
4. **A site per run** (Design, A site per run): `RunSiteAsync`, `RunClientAsync`, the cleanup's Delete Site, the site
   sweep; every content test on the run site; the project and group sweeps and their rows go.
   - **Proves it:** `LiveSiteTests` rows for the site sweep: a day-old run site swept; a recent one, one named
     otherwise, one whose content URL or name differs by a character or by case, and the default site kept; the `Live`
     tests pass on the run site; after the run, a read-only Query Sites lists no run site. Rows: Create Site, Delete
     Site, Query Sites.
5. **Sites and Switch Site:** `LiveSiteAdminTests`; Switch Site in `LiveSessionTests`; `ServerOnlyAreaTests`' site test
   and `ServerOnlySessionTests`' Switch Site test deleted.
   - **Proves it:** the new tests pass; the revision limit read back as sent. Rows: the sites' reads, lists and update,
     Switch Site.
6. **Directory users and groups, and impersonation:** the forms the import takes settled first, in the run site (O1);
   the four settings in `live.yml` and `LiveSite`; `LiveDirectoryTests`; impersonation in `LiveSessionTests`;
   `ServerOnlyAreaTests` deleted.
   - **Proves it:** the import lists the one member named by `TABLEAU_LIVE_AD_GROUP_MEMBER`; the synchronisations answer
     the group and a `GroupSync` job; the import job's group is found and deleted; the user's role reads back
     `Viewer`; the impersonating session's user is the directory user's LUID; the test output holds none of the four
     settings' values. Rows: Add User to Site, Update User, Remove User from Site, Create Group from Active Directory,
     Update Group from Active Directory, Sign In with a personal access token, impersonating a user.
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
   - **Proves it:** `LiveCoverageTests`; `dotnet run eng/gate.cs -- quick` (AGENTS.md's length, links, skills); after
     the merge, `live.yml` by hand on `main`, then a night.

## Tests

| Suite | What is new or changed | Where it runs |
|---|---|---|
| Offline | `TableauSiteTests` (each sign-in's settings); `LiveSiteTests` (the site sweep's rule, the project and group rules gone); `LiveCoverageTests` (the table against the code, and its checker on invented tables and records); `LogTests` (the call's name in three lines) | every PR, Linux and Windows, under the gate's coverage |
| `Live` | `LiveConnectedAppTests`, `LiveSiteAdminTests`, `LiveDirectoryTests`, `LiveAccessTests`, `LiveCatalogTests`; Switch Site and impersonation in `LiveSessionTests`; more tags in `LiveContentTests`; every content test on the run site | nightly and by hand from `main` (`live.yml`), and locally, on the maintainers' Tableau Server 2025.1.5, REST API 3.25 |
| `ServerOnly` | the password sign-ins alone | by hand, on a Tableau Server |

- **The gate:** `dotnet run eng/gate.cs -- full` on each commit; coverage of the library can only rise, since only
  `TableauLog`'s three lines change there.
- **What none of them shows:** Tableau Cloud; calls above REST API 3.25 (a sized workbook PDF, a connection's
  authentication type); flows, virtual connections and collections; a password's sign-in; unified access tokens;
  Add Data Quality Warning and the catalog's updates if the server lacks Data Management; a server whose user licences
  are all taken.

## Docs

- `docs/live-coverage.md`, new.
- `AGENTS.md`, rows edited in place (it holds 148 of its 150 lines): the `docs/` row names `live-coverage.md`; the
  `tests/` row's `LiveSite.cs` says run site, not run project and groups, and names `LiveCoverageTests`; the `.github/`
  row's `live.yml` says it signs in with the CI token in a site of the run's own; Build and test's site-test bullet
  says a new public method adds its rows to `docs/live-coverage.md`.
- `.claude/skills/finish-work/SKILL.md`, step 3: a live test works in the run's own site, which the suite deletes, with
  the token's settings; the connected app's settings drive its own tests.
- `.github/workflows/live.yml`'s header; the summaries in `LiveSite.cs`, `TableauSite.cs`, `SuiteCleanup.cs` and the
  live test classes.
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
| Delete Site outlasts the cleanup's ten minutes, or the run breaks off, and a run site stays | Mitigated: the next run's sweep deletes it after a day; a failed cleanup fails the run and names the site |
| One token allows one session: Switch Site and the impersonating sign-in end the token's other session | Mitigated: the site tests run one at a time, and each client is disposed before the next signs in; local runs use another token |
| The catalog takes longer than five minutes to list a new site's assets | Accepted: the test fails naming the wait; a rerun shows whether it was the index |
| The server isn't licensed for Data Management | Accepted: those calls are pinned as refused, and the rows say so (O2) |
| The directory user as a `Viewer` takes a user licence; with none free, Tableau adds it `Unlicensed` and answers 201 | Accepted: the test fails naming the role it got; the maintainer frees a licence or the test drops the impersonation |
| Tableau won't let a token impersonate a directory account that can't sign in | Open (O4): the row is pinned as refused with Tableau's code, or the maintainer names another user |
| The import takes another form of the group's name or domain than the settings give | Open (O1): settled before the settings are set |
| A setting's value reaches the public Actions log | Mitigated: the four directory settings are environment secrets, which GitHub masks, and known settings, which the suite scrubs from its output, fixtures and log check |
| The run gets longer: a site made and deleted, imports, the catalog's wait | Mitigated: `live.yml`'s `timeout-minutes` goes from 20 to 45 |
| The matrix check fails on a night when a test failed early | Mitigated: a row whose test failed is left out |
| A user's tooling matches the client's Debug lines by their exact text | Accepted: a property is added, nothing removed; CHANGELOG says so |
| The directory users stay on the server once the run site goes | Accepted: they are the maintainer's technical accounts, added again each night |

## Owner calls

| # | Question | Recommendation | Answer |
|---|---|---|---|
| Q1 | One PR, or the credential split first on its own? | Two: step 1 first, so `main`'s nightly is green within a day; steps 2 to 9 on a second branch from `main` | |
| Q2 | A group set for the permission test: through a test-side descriptor on the client's pipeline, in the run site; Create and Delete Group Set in the 2.0 client (a door); or group sets not live | The test-side descriptor: no public API, and the run site takes the group set with it | |
| Q3 | The record needs each call's name: add it to the client's Debug lines (users see it), or match paths in the tests | The Debug lines: one place, and a log reader learns the call too | |
| Q4 | `ServerOnly` keeps only the password sign-ins, the rest folded into the nightly and deleted | Yes | |
| Q5 | Impersonation acts as the directory user, made a `Viewer` for the test's length (a user licence, briefly); or as the token's own user, which proves less | The directory user | |
| Q6 | The nightly's matrix check runs only with `TABLEAU_LIVE_MATRIX=1`, set by `live.yml`, so a local run of one class isn't failed | Yes | |

## Open items

What the build settles, on the server, before the row or the setting it decides:

| # | Item | How the build settles it |
|---|---|---|
| O1 | Which form of the group's name and domain Create Group's import takes | an import into the run site with the group's `sAMAccountName` and the domain's full name, then its short name; the settings get what worked |
| O2 | Whether the server is licensed for Data Management | Add Data Quality Warning on the run's data source: answered or refused, and each row follows |
| O3 | The data roles' default permissions answer 405000 on Tableau Server 2025.1 (a read-only probe) | pinned as refused; whether the 2.0 client keeps `DefaultPermissionsKind.DataRoles` is a follow-up issue |
| O4 | Whether a token can impersonate a directory account that can't sign in | the impersonating sign-in, answered or refused |

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

## Plan review

Not reviewed yet: `review-plan` writes each round here.
