---
subject: "Port permissions, tags and metadata to the 2.0 client, with GraphQL (#52)"
base: main 3433e9b
head: e73ee48
date: 2026-11-01
round: 1
tier: three fresh reviewers split by scope (the door's shape, TableauContent and GraphQL; each REST endpoint against Tableau's reference; the live suite, the old client's removal, the harness and the docs), reading only; the major checked by a fresh refuter
verdict: needs-fixes
---

# Review: permissions, tags and metadata (#52)

Three fresh agents read the change at `e73ee48` and ran nothing. `dotnet run eng/gate.cs -- full` passed on it first:
818 tests, lines 96.84%, branches 94.86%.

**The shape and GraphQL reviewer:**
- A caller's GraphQL type is read only through the `JsonTypeInfo<T>` given.
- Variables are written AOT-safely.
- A GraphQL `errors` answer with partial data fails the call.
- The cursor helper refuses a cursor it has seen.
- `TableauContent` is equal by value.
- Each API refuses a kind it can't take before sending.
- `FavoriteTarget` is gone everywhere.

**The endpoints reviewer:** all 84 descriptors match the reference on method, path, version, deployment and retry rule:
- permissions for nine kinds, and the defaults;
- tags on six kinds and the batch calls;
- the catalog and the data quality warnings.

**The live suite and docs reviewer:**
- The suite only adds and deletes a permission on its own project, for its own group, and reads GraphQL.
- The 36 deleted methods have 35 ledger entries, since `QueryViewPermissions` was never released, and 36 rows in the migration guide.
- Nothing in the harness is loosened.

A fresh refuter confirmed F1 as a major from Tableau's Python client:
- its issue #1738, on Tableau Server 2025.1;
- PR #1782, checked against Tableau Online;
- its `_encode_tag_label`.

It also found that this repo's own Vizportal client already quotes every tag. It showed that Delete Tag's path must stay unquoted.

| # | Sev | Where | Finding | Status |
|---|---|---|---|---|
| F1 | major | `TagsClient` | A label was sent as written, and Tableau splits an unquoted label at whitespace and commas: "Yearly Sales" became two tags, "Yearly" and "Sales" | fixed `e6e8304`: Add, Batch Add and Batch Delete quote a label holding whitespace or a comma, one already in quotes as it is; Delete Tag's path unquoted; `ABodysLabelIsQuotedWhenItHoldsWhitespaceOrACommaAndIsntQuotedAlready`, the per-kind test and both batch tests |
| F2 | minor | `FavoritesTests` | With `FavoriteTarget` gone, no test checked which descriptor `FavoritesClient` picks per kind: a project mapped to the workbook's passed | fixed `41a1b96`: each kind's failure names its own call, and each kind is gated at its own version |
| F3 | minor | `GraphQLTests` | The cancellation test passed without the check between nodes | fixed `41a1b96`: the test asserts one node of the page's two was read |
| F4 | minor | `TableauPaging.CursorListAsync` | A query that didn't select `endCursor` failed with a message that blamed the server | fixed `41a1b96`: an absent `endCursor` says the query must ask for it; a new row |
| F5 | minor | `MetadataClient.ListColumnsAsync` | It stopped at 1,000 columns without a word, in its doc or at run time | fixed `41a1b96`: the limit documented, and an answer that counts more columns than it holds fails as `InvalidResponse` |
| F6 | minor | `MetadataClient.AddDataQualityWarningAsync` | Its doc said a second warning on an asset fails; that holds only up to Tableau Server 2024.2 and Tableau Cloud February 2024 | fixed `41a1b96` |
| F7 | minor | `MetadataClient.UpdateDataQualityWarningAsync` | Its doc didn't say an unset `IsActive` makes the warning visible again | fixed `41a1b96` |
| F8 | minor | `live.yml` | Its header listed four of the JWT's seven scopes | fixed `a864fb1` |
| F9 | minor | the plan | S13's row named no PR, and F8 and F44 cited the issue | fixed `a864fb1` |

## Refuted

- **Reflection serialisation in GraphQL:** every read goes through a `JsonTypeInfo<T>`, and the variables through
  `JsonObject.WriteTo`.
- **A repeated cursor loops forever:** the helper refuses a cursor it has met.
- **The hand-written `Column` trips the generator's check:** the check reads only `Generated/`.
- **Virtual connections' permissions from 3.18:** the reference says 3.23.
- **A column's tags under its table:** the reference puts them at `columns/{id}/tags`.
- **Batch Delete's body needs `contentType`:** the reference's request carries the id alone.
- **The GraphQL live test should skip on 403:** a bare 403 can't be told from a real refusal, and `TableauSite.cs`
  forbids a typo skipping a run.
- **The 2-in-33 socket errors are the suite's:** about 410 ports in use, and this branch adds no new kind of server
  or client.

| Improvement | Answer |
|---|---|
| `==` and `!=` on `TableauContent` and `PermissionGrantee` | taken `eefe7b8` (agreed on #48) |
| `PermissionGrantee` names its kind: user, group, group set | taken `eefe7b8` (agreed on #48): the schema lists group sets as grantees; their delete path follows Tableau's Python client |
| GraphQL warnings logged and the data returned | taken `41a1b96` (agreed on #48): an entry whose `extensions.severity` is `WARNING` |
| GraphQL failures carry the same details whatever the status | taken `41a1b96` |
| The catalog updates send only the reference's members | taken `41a1b96` |
| `ListColumnsAsync` fails when the answer reports more columns | taken `41a1b96`, with F5 |
| `PermissionsClient.AddAsync`'s comment on which bodies name their content | taken `eefe7b8`: each add's body names its content where the reference's does |
| The databases fixture's location type, as the server sends it | taken `41a1b96`: `PROJECT`, from a read-only probe |
| The plan's maintainer items: `FEATURE_DISABLED`, and the backfill | taken `a864fb1` |
| Batch Delete Tags checked on a site | later: S17, once the suite has content of its own |
| A database's own default permissions | later: #53 |

## Round 2

**Where:** PR #52 at `c923325`, the fixes since `a33d991` (the merge of `main` after `e73ee48`), read by one fresh agent
that ran nothing. **Verdict:** clean after F10 to F13, minors, fixed in `4307190` and `5ea89b4` and checked in the main session.

Every round 1 fix holds, each code fix with a test that fails on a revert:
- **F1's quoting** matches Tableau's Python client in the three bodies and leaves the path unquoted. It quotes on any
  whitespace, which the server strips, and doesn't wrap a label already in quotes again.
- **The agreed changes** hold:
  - the warning rule fails loud if the severity's path were wrong;
  - `==` and `!=` handle null on either side;
  - `RATE_EXCEEDED` is mapped for the Metadata API alone;
  - the catalog bodies match the reference's;
  - the add bodies' content elements are the reference's;
  - `PROJECT` reads through the extensible enumeration.
- **The merge of `main`** brought #50's files byte for byte.

| # | Sev | Where | Finding | Status |
|---|---|---|---|---|
| F10 | minor | `TableauGraphQL.Read` | An answer of warnings alone with no data logged that its data was returned, then failed as `InvalidResponse` without the warning's code | fixed `4307190`: warnings with no data fail as `Http` under the warning's code, and warnings are logged only once the data is read; `WarningsWithNoDataFailUnderTheWarningsCodeAndLogNothing`, `WarningsBesideDataThatDoesntReadLogNothing` |
| F11 | minor | `TableauGraphQL.Describe` | A warning before the real error put the warning's code in `ErrorCode` and `Summary` | fixed `4307190`: the code and summary are the first entry's that isn't a warning, the rest in the detail; the test expects `INVALID_ARGUMENT` |
| F12 | minor | `PermissionGrantee`, the PR | The group set's delete path, from Tableau's Python client and not in the reference, had never run on a site, and the PR didn't say so | fixed `40bb4e2`: the PR names it as not run, and S17's live list holds a group set's permission, with the maintainer's yes it needs |
| F13 | minor | `PermissionsClient`, `docs/migration-2.0.md` | The rules' docs still said a user or a group | fixed `5ea89b4` |

## Refuted (round 2)

- **The warning's severity is read from the wrong place:** Tableau's docs show no JSON; DataHub reads
  `extensions.severity`, and an entry without it fails the call, as before the change.
- **A warning's text holds a secret:** Tableau's documented warnings carry no request value.
- **Group sets aren't gated:** the floor is above their version.
- **The new content elements in add bodies are refused:** each is the reference's.

| Improvement | Answer |
|---|---|
| `LocationType` sent as `PROJECT` named where a location is read, and S14 told | taken `5ea89b4`, `40bb4e2` |
| The backfill confirms the warning's shape on a real server | taken `078a5e2`: confirmed, below |

The main session read F10 and F11's fix in `TableauGraphQL`:
- **Warnings and no data:** the call fails under the warning's own code.
- **Warnings beside readable data:** they are logged after the data is read.
- **Describing a failure:** it takes its code and summary from the first entry that isn't a warning.

The branch floor rose from 94 to 95 in `81ef8b4`, as the coverage gate allowed.

## Live run

Tableau Server 2025.1.5 (REST API 3.25), the default site, with a personal access token mapped into the test process
only.
- **`Live` tests:**
  - before the Metadata API was on: 8 of 9 passed, and the GraphQL query got a bare 403 from the metadata service;
  - after the maintainer turned it on and the server restarted: all 9 passed, the GraphQL query on the backfill's
    partial data.
- **A read-only probe that printed shapes only:** every query answered 200 with `data` and one `errors` entry. That
  entry was `extensions.severity` `WARNING`, `extensions.code` `BACKFILL_RUNNING`, with Tableau's "Still creating the
  Metadata API Store" message, the shape the warning rule reads.
- **A last page:** it sends `endCursor` null.
- **What the suite changed:** it added and deleted a permission on its own project for its own group, tagged nothing,
  set no warning and changed no catalog asset. Each run's project and group were deleted, and every session signed
  out.
- **A read-only probe:** a database's location type is `PROJECT`.
- **Not run:**
  - tags, warnings and catalog changes on a site, until S17 gives the suite content of its own;
  - a group set's delete path;
  - Tableau Cloud.
- **Flakiness:** while the backfill runs, the GraphQL test's two counts can differ, since the index grows between its
  queries.

## Checks

- **`dotnet run eng/gate.cs -- full`:**
  - `e73ee48`: 818 tests;
  - `c923325`: 843 tests;
  - `078a5e2`: 846 tests; lines 96.90%, branches 95.01%, floors 96 and 95.
- **CI:**
  - passed on `e73ee48` (run 41265847487), `c923325` (run 41292321824) and `078a5e2` (runs 41294749664 and
    41294746192), Native AOT (Linux) included;
  - a dispatched run on `c923325` failed once on S10's `ATransferRunsUnderTheTransferTimeout` on Windows, a flake
    that predates this change (#54), and passed when re-run.
- **A local failure:** the main session's first full gate on `078a5e2` failed in one test, which got
  `SocketException` 10055 (WSAENOBUFS) connecting to the fake server. The builder's loops had seen it twice in 33. CI's
  legs never have, and the gate's next run and four runs with coverage passed. It is Windows running out of ephemeral
  ports under back-to-back runs, followed in #55.
- **ApiCompat:** `dotnet run eng/apicompat.cs` passed with the ledger at 97 breaks, the 35 released methods deleted
  here among them.
