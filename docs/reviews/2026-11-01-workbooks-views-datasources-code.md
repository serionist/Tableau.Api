---
subject: "Port workbooks, views and data sources to the 2.0 client, with streamed exports (#58)"
base: main 4cd20b8
head: 00dc95a
date: 2026-11-01
round: 1
tier: three fresh reviewers split by scope (the door's shape, the exports, downloads and updates; each REST endpoint against Tableau's reference; the live suite, the old client's removal, the harness and the docs), reading only; the major checked by a fresh refuter
verdict: needs-fixes
---

# Review: workbooks, views and data sources (#58)

Three fresh agents read the change at `00dc95a` and ran nothing. `dotnet run eng/gate.cs -- full` passed on it first:
957 tests, lines 97.60%, branches 95.04%.

- **The shape and exports reviewer:**
  - `vf_` comes from one helper and is compared ordinally. Its values are escaped once.
  - Every download is a streamed transfer, disposed when it fails.
  - An update sends nothing the caller didn't set.
  - The door matches #56 with its departures.
- **The endpoints reviewer:**
  - Every path, method, body and response element matches the reference.
  - Revision lists send their paging (F7).
  - Batch tags now name each content's type, the text the reference's own response shows.
- **The live suite and docs reviewer:**
  - Everything the suite changes is in its own run project.
  - Its one download is saved to a temporary folder that it deletes.
  - The 33 deleted methods have their ledger entries and migration rows.
  - Nothing in the harness is loosened.
  - It found two live calls past #56's words, a read of the user's own workbook list and the extract refreshes. The maintainer agreed to both on #56.

A fresh refuter confirmed F1 from Tableau's change log and versions table:
- Both features are 3.24.
- The method pages' "3.23" contradicts their own release names (Tableau Server 2024.3, which is 3.24).
- Get Custom View PDF, under the same note, is new in 3.24.
- At 3.23 the gates never fire, since 3.23 is the client's floor.

| # | Sev | Where | Finding | Status |
|---|---|---|---|---|
| F1 | major | `Endpoints.DownloadWorkbookPdfFiltered`, `QueryViewImageSized` | Workbook PDF filters and view image size were gated at 3.23, the floor, where the evidence puts both at 3.24: on Tableau Server 2024.2 a filtered PDF went out and could come back unfiltered with no error | fixed `565d392`: both at 3.24, each summary naming the reference's two versions; the gate rows in `WorkbooksTests` and `ViewsTests` refuse a 3.23 client before sending |
| F2 | minor | `ConnectionUpdate.AuthenticationType` | A REST API 3.27 member, sent through connection updates gated at 2.0 and 2.3 | fixed `565d392`: `UpdateWorkbookConnectionAuthentication` and `UpdateDataSourceConnectionAuthentication` at 3.27, chosen when it is set; gate rows, `RetryRuleTests`, `AConnectionsAuthenticationTypeIsSentFromRestApi327` |
| F3 | minor | `ExportOptions.Filters` | The doc left out that several keys' values pair by position and must be as many; a test sent unequal counts | fixed `565d392`: the doc states the rule, fields of unequal counts are refused before sending, `\,` and `\\` counted as escapes; the PDF test sends two values in each of two fields, and the `ExportTests` row "filters of unequal counts" |
| F4 | minor | `WorkbooksClient.DownloadRevisionAsync` | It had no `includeExtract`, which 1.2.5 had and the reference documents; adding it after the release would break the door | fixed `565d392` (agreed on #56): `includeExtract = true` before the token; the `ExportTests` row "Download Workbook Revision, without its extract" expects `?includeExtract=False`, and the live test downloads a revision without its extract |
| F5 | minor | AGENTS.md | The `vf_` trap's guard named `QueryViewData`, which this change deletes | fixed `565d392`: it names 2.0's `ExportQuery` |
| F6 | minor | the plan, the PR | Neither named PR #58 | fixed `565d392` and the PR body |
| F7 | minor | `LiveContentTests` | The suite read the maintainer's own workbook list and ran extract refreshes, beyond #56's words | fixed: the maintainer agreed to both on #56, as part of #57's widening |

## Refuted

- **`vf_` inverted again:** the one place it is added keeps a name that already starts with it.
- **Filters escaped twice:** the names and values go raw to `RequestUri`, which escapes once.
- **View data should take `maxAge`:** the reference lists none for it.
- **A width alone picks the wrong descriptor:** the options are refused before anything is chosen.
- **The old `Enums` sharing names breaks callers silently:** a file importing both namespaces fails with CS0104,
  and S17 deletes the old ones before the release.
- **Samples left behind on a failed run:** the cleanup deletes the run's project, its samples with it, and the sweep
  takes a crashed run's.
- **The JWT asks for more than it uses:** each scope serves a call the suite makes.

| Improvement | Answer |
|---|---|
| Remove Workbook Revision from 2.3, as the change log says | taken `565d392`, with a gate row |
| The batch tags' type text cited from the reference | taken `565d392`: the reference's Batch Add Tags response, the xsd's `contentType` and Tableau's Python client |
| A sample data source batch-tagged live | taken `565d392`: no new scope needed |
| The source of the workbook `Parameters.` rule | taken `565d392`: Tableau's Python client, since the reference documents none |
| Comments naming both versions where the reference gives two | taken `565d392` |
| The run's settings kept out of the live tests' output | taken `565d392`: each `LiveSite.Known` and `LiveSite.Secrets` value becomes `***` |
| The migration rows show 1.2.5's signatures | taken `565d392`, from `v1.2.6` |
| The PR says five files left TableauLegacy | taken `565d392` |
| The maintainer item on the 3.27 scope names on Tableau Server 2025.1 | taken `565d392`: what a failed first direct-trust sign-in means, and how to drop the tag scopes |
| An older revision downloaded and removed live | later: S17, which can republish |

## Round 2

**Where:** PR #58 at `565d392`, the fixes since `00dc95a`, read by one fresh agent that ran nothing. **Verdict:**
clean after F8, a minor, fixed in `68d9408` and checked in the main session.

Every round 1 fix holds, each code fix with a test that fails on a revert:
- **F1:** both descriptors at 3.24, and the gate rows call at 3.23 and expect "needs REST API 3.24". No "3.23" for
  either is left in the code, the changelog, the migration guide, the README or the native AOT app.
- **F2:** the one place that builds a connection body takes the 3.27 descriptor for any `AuthenticationType` that
  isn't null, an empty one included.
- **F3:** the reference's filtering page says every key must hold as many values; Tableau's Python client documents
  `\,` and `\\`. Dropping either the count or the escapes fails a test.
- **F4:** the signature is the one agreed on #56. The ledger needs nothing: the method is new in 2.0, and 1.2.5's
  `DownloadWorkbookRevision` is already in it.

| # | Sev | Where | Finding | Status |
|---|---|---|---|---|
| F8 | minor | `ExportQuery.For` | A workbook parameter counted in F3's rule: `Region` `West,East` beside `Parameters.Top N` `10` was refused before sending, where `00dc95a` and 1.2.5 sent it, so a multi-value filter could never sit beside a parameter | fixed `68d9408`: a key starting `vf_Parameters.` is sent but not counted, the doc says the rule is for view filters; `AParameterGoesBesideViewFiltersOfAnyCount`, two rows with the exact query, both refused before the fix |

## Refuted (round 2)

- **A lone backslash at a value's end reads past it:** the loop's own bound ends it, and the value counts one.
- **`\\,` miscounted:** the escaped backslash is skipped, so the comma separates: `a\\,b` is two values, `a\\\,b` one.
- **A trailing comma:** `West,` counts two, the split the reference's list gives.
- **An empty value against a multi-value field:** the reference counts it as one value; widening it is undocumented,
  in Tableau's Python client's words "not officially documented".
- **`authenticationType` below 3.27 by another path:** one place builds a connection body.
- **The scrubbing throws on null or empty:** both `Known` and `Scrubbed` drop empty values, and a dictionary's keys
  can't be null.
- **The scrubbing breaks the error code assertions:** only a setting that is a substring of a status text would.

| Improvement | Answer |
|---|---|
| The unequal-count error names the fields | taken `f406890`: "The view filters 'Year' and 'Region' hold 2 and 1 values", in the refusal row |
| A test that reaches `\\,` | taken `f406890`: `AnEscapedBackslashEndsAValueWhereAnEscapedCommaDoesnt`, `a\\,b` two values and `a\\\,b` one, each with its exact query, and a refusal row |
| The data source batch tags' refusals scrubbed in the live suite | taken `f406890`: both go through `OutcomeAsync` |
| The scrubbing ignores case, for a host or content URL echoed in another case | taken `f406890` |

The main session read F8's fix and the four improvements:
- **F8:** the key is built before the count, so `Parameters.Top N` and `vf_Parameters.Top N` both skip it, and every
  view filter is still compared with the first.
- **The improvements:** the refusal names the first field and the one that differs; the two live batch calls assert
  `added` and `deleted` through the scrubbing helper.

## Live run

Tableau Server 2025.1 (REST API 3.25), the default site, with a personal access token read from outside the clone into
the test process only. All 13 `Live` tests passed on `565d392` (57 s) and `f406890` (50 s), as on `a799bcf`'s code.
- **The run's own content:** its project is made with `publishSamples=true`, so Tableau publishes two sample workbooks
  and their data source into it for the suite alone. The suite updates, exports, downloads, tags and deletes those.
- **Every S14 call ran:** most passed, and the rest reached Tableau and were refused with a pinned code, since the
  samples hold no extract, one revision only and no editable connection:
  - extract refreshes, 403180;
  - a revision downloaded without its extract, 400057, and removed, 403055;
  - a workbook connection's update, 400039.
- **What Tableau sends:**
  - a workbook's and a data source's file as `application/octet-stream` with a `Content-Disposition` naming it;
  - each export with no `Content-Disposition`;
  - a workbook's and a view's location type as `Project`, unlike a catalog asset's `PROJECT`.
- **Tags:** labels with a space and a comma were added and deleted, each held once and none split. Batch Add Tags
  failed with 409004 on the reference's id-only body and passed once each content named its type. The batch calls took
  both labels on and off two workbooks and the data source, whose type text is `Datasource`.
- **Cleanup:** each run's project, samples and groups were deleted, and every session signed out. A read-only check
  found nothing left from these runs.
- **Not run:**
  - Tableau Cloud;
  - a sized workbook PDF, which needs REST API 3.26;
  - a `Job`, a second revision and an editable connection, which need content the 2.0 client can't make before S17;
  - the connected app's JWT with S14's new scopes, which `live.yml` runs first after the merge.

## Checks

- **`dotnet run eng/gate.cs -- full`:**

  | Commit | Tests | Lines | Branches |
  |---|---|---|---|
  | `00dc95a` | 957 | 97.60% | 95.04% |
  | `565d392` (the main session's run too) | 962 | 97.61% | 95.07% |
  | `68d9408` | 964 | | |
  | `f406890` | 967 | 97.61% | 95.08% |

  The line floor rose from 96 to 97, as the coverage gate allowed.
- **CI:** passed on `00dc95a` (run 41300905456), `565d392` (41307070288), `68d9408` (41308594141) and `f406890`
  (41309880189 and the PR's 41309884448), Native AOT included.
- **ApiCompat:** `dotnet run eng/apicompat.cs` passed with the ledger at 127 breaks.
- **Native AOT:** the app's 74 checks passed on win-x64.
