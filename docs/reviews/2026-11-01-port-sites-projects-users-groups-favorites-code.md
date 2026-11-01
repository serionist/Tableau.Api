---
subject: "Port sites, projects, users, groups and favorites to the 2.0 client, with paging and filters (#46)"
base: main eb79b4b
head: 51dbd2b
date: 2026-11-01
round: 1
tier: three fresh reviewers split by scope (the paging and filter API and the door's shape; each endpoint against Tableau's REST reference; the live suite, the old client's removal, the harness and the docs), reading only; no finding on credentials, so no refuter
verdict: clean
---

# Review: sites, projects, users, groups and favorites (#46)

Three fresh agents read the change at `51dbd2b` and ran nothing. `dotnet run eng/gate.cs -- full` passed on it first:
686 tests, lines 96.04%, branches 94.77%.

- **The paging and filter reviewer:**
  - A filter is escaped once and travels as Tableau's syntax.
  - A comma is refused where it would split a filter.
  - Paging is 1-based, refuses a page size outside 1 to 1,000, and stops at the total or on a short or empty page.
  - Cancellation is checked between pages and items.
  - `ListAsync` reads through the virtual `ListPageAsync`.
  - Every public type is in `roots.xml`, and the door matches #45 with its departures.
- **The endpoints reviewer:**
  - All 37 descriptors match the reference: method, path, query, body, version and deployment, with every id the path names a parameter.
  - F13's group id and F33's attributes hold.
  - Each departure checks out against the reference and Tableau's Python client.
- **The live suite and docs reviewer:**
  - The suite does no more than #45 allows. It creates no user and changes no site setting. It touches only its own project and its own local group, found by an exact name.
  - Each of the 40 deleted methods has its ledger entry and its row in the migration guide. `QueryViewsForSite` moved byte for byte.
  - Nothing in the harness is loosened.

| # | Sev | Where | Finding | Status |
|---|---|---|---|---|
| F1 | minor | `TableauFilter`, `TableauSort` | Both compared by reference, so two list options built alike with a filter or a sort weren't equal, and a mock set up by value never matched | fixed `a9db3f3`: both compare by their expression and fields; `FiltersAndSortsBuiltAlikeAreEqualAndSoAreTheirOptions` |
| F2 | minor | `TableauFilter`, `CultureTests` | No test failed if a date filter dropped the invariant culture: sv-SE and tr-TR write the same separators and calendar | fixed `a9db3f3`: `ADateFilterIsWrittenTheSameInEveryCulture` under th-TH and fi-FI, which fail without the invariant culture |
| F3 | minor | `ProjectField` | Its summary said only the names and the owner's fields take `in`; the reference lists `parentProjectId` too | fixed `a9db3f3`: each field lists its own operators from the reference's table |
| F4 | minor | `ServerOnlyAreaTests` | The Active Directory test could leave the group its import job made, listed late or found as the group it had just deleted; the site test left its site when the sign-in to it failed | fixed `a9db3f3`: the job's group found by name, domain and another id, deleted in a `finally`; the site deleted from the first session when anything after its creation fails, or named |
| F5 | minor | `SitesTests`, `ProjectsTests`, `UsersTests`, `GroupsTests` | No test proved a call sent once is never retried: each "Once" test answered success first, so marking Create Project idempotent failed nothing | fixed `a9db3f3`: `RetryRuleTests` pins all 46 descriptors' rule by name, and each call sent once, answered 503, stays one request |
| F6 | minor | `TableauPaging`, `FavoritesClient` | Favorites carry no pagination, so a server that ignored `pageNumber` would serve the same full page forever | fixed `a9db3f3`: a list fails as `InvalidResponse` when a page comes back earlier than asked, or favorites repeat their first item; a read-only probe on the server showed it pages favorites |

## Refuted

- **A filter is escaped twice:** the expression goes unescaped to `RequestUri`, which escapes each part once.
- **`in:[...]` sent escaped breaks:** the server decodes before it parses, as a live `R&D` match showed.
- **Groups tie on name in the default sort:** group names are unique on a site, ignoring case (409009).
- **The group sweep takes a person's group:** only a local group whose name is a run's exactly, its timestamp more than
  a day past; a malformed or future one is kept.
- **The JWT asks for more than it needs:** the suite uses all four group actions, and the scope names are Tableau's.
- **A number in a request breaks:** `xs:int` values already travel as bare numbers in Tableau's own answers; Update
  Site's `revisionLimit`, an `xs:integer`, is left to the by-hand Server test, which the PR says.
- **`OldPublicTypes` should shrink:** the replaced types live in `XmlSchema.cs`, which stays until the old client goes.

| Improvement | Answer |
|---|---|
| A caller's sort gets the default's fields as tie-breakers | taken `a9db3f3` |
| Users sorted by name and domain on Tableau Server | taken `a9db3f3`; Tableau Cloud keeps the name alone, unverified there |
| `TableauFilter.Eq` and `In` take the generated enumerations | taken `a9db3f3` for `SiteRole`, the one S12's fields hold: one overload for every enumeration would make the models' internal interface public; later areas add theirs |
| The refusal names the C# member | taken `a9db3f3` |
| Paging stops when a server answers an earlier page | taken `a9db3f3`, with F6 |
| Delete Site sent once | taken `a9db3f3` |
| CHANGELOG names every call sent once | taken `a9db3f3` |
| The nightly reads a few pages of users | taken `a9db3f3`: three pages of two |
| The plan's S12 row, F13 and F33, and the live suite's design | taken `a9db3f3`, `4733092` |
| `LiveSiteTests`' summary covers the group sweep | taken `a9db3f3` |
| The migration guide's outdated "so far Server Info" | taken `a9db3f3` |
| `UsersClient.UpdateAsync` names each attribute's version | taken `a9db3f3` |
| A site test's assertion on `Usage` | taken `a9db3f3`: dropped |
| Three test names claim more pages than they serve | taken `a9db3f3`: two serve two pages, one renamed |
| `SitesClient.DeleteAsync`'s doc | taken `a9db3f3` |
| A test's comment | taken `a9db3f3` |
| Organize Favorites | later: #47 |

Only minors, so no second round: the main session read the fixes against each finding.
- **Paging:** the guard compares the page answered with the one asked for. Favorites, which carry no pagination,
  also compare each page's first item with the page before's.
- **Sorting:** the caller's sort is followed only by the default's fields it doesn't name.
- **Equality:** filters and sorts compare by value.
- **Retries:** the retry table fails on a new descriptor until it is added.

## Live run

Tableau Server 2025.1 (REST API 3.25), the default site, with a personal access token mapped into the test process
only.
- **`Live` tests:** all seven passed on `51dbd2b`, twice, and again on `4733092`.
- **Cleanup:** the run's project and group were deleted, and every session was signed out.
- **Recording:** one run wrote 52 scrubbed bodies. Those read matched the synthetic fixtures, and none is committed.
- **Probes:** read-only probes first settled what the departures rely on:
  - a site filter is refused, and a site sort ignored;
  - a page past the end of groups or projects is refused;
  - favorites are paged with no pagination element, shown by the suite's own two favorites, one a page.
- **Not run:**
  - `ServerOnlyAreaTests`, which need a site the maintainer may change: site create and delete, Active Directory
    groups, adding a user;
  - Update Site's `revisionLimit` as a JSON number;
  - the nightly's direct-trust sign-in with the new scopes;
  - Tableau Cloud.

## Checks

- **Gate:** `dotnet run eng/gate.cs -- full` passed on `51dbd2b` (686 tests) and on `4733092` (705 tests; lines
  96.05%, branches 94.81%, floors 96 and 94).
- **Coverage loop:** five runs of the tests with coverage in a row passed on the builder's heads.
- **CI:** passed on `51dbd2b` (run 41257743739) and `4733092` (run 41260655885), Native AOT (Linux) included.
- **ApiCompat:** `dotnet run eng/apicompat.cs` passed with the ledger at 62 breaks, the 40 deleted methods' among them.
