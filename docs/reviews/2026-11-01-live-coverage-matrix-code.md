---
subject: "Live-test almost every 2.0 method in a site of its own, with a live-coverage matrix (#63)"
base: main c608a9c
head: b49f5ac
date: 2026-11-01
round: 1
tier: two fresh reviewers, reading only, the lenses split between them (plan-and-intent, tests, docs-freshness and harness-integrity; implementation, rest-contract, credentials, public-api, simplicity, compatibility and vizportal); a fresh refuter each for the findings on credentials and the harness (F3, F4, F6)
verdict: clean
---

# Review: the live suite's run site and its live-coverage matrix (#63)

Two fresh agents read the change at `b49f5ac` and ran nothing, the 29 files and 3,829 lines of diff split by lens. Both
returned clean: no blocker or major, six minors, four improvements. Three refuters, one for the credentials finding and
one for the two harness findings, confirmed each as a minor. `dotnet run eng/gate.cs -- full` passed on the tree
before it, and CI passed on `b49f5ac`.

- **The library change:** each request line carries `endpoint.Name`, a constant of the descriptor's; nothing new is
  sent, and `TableauLog` is internal.
- **The sweep:** it takes a site only when its name is a run's and the second it was made, its content URL the one that
  name gives, both exactly, and more than a day old; `LiveSiteTests` pins case, a character off, another run's URL and
  the default site.
- **The record:** site test classes run one at a time after every parallel test, a `TableauSiteTests` row fails one that
  doesn't, and the offline tests build records of their own.
- **The harness:** nothing is loosened; the `ServerOnly` tests that went have their assertions in `Live` tests (Q4).

| # | Sev | Where | Finding | Status |
|---|---|---|---|---|
| F1 | minor | `docs/live-coverage.md`, the rows of `LiveDirectoryTests`' user test | Five rows said `yes`, "Tableau answered the call with a 2xx", for Add User to Site, Update User, Remove User from Site and both impersonating sign-ins, which no run had answered: Add User to Site answers 404002 for the directory user, and the test sends nothing after. The nightly excuses a failed test's rows, so the matrix claimed more than any run showed | fixed `8757b1f`: Add User to Site `refused 404002`; the four calls behind it `no`, waiting on a directory user the server finds; the run after holds both |
| F2 | minor | `docs/live-coverage.md`, `ProjectsClient.ListAsync`'s row | The row named `LiveSessionTests.AClientSignsItselfInForACallAndSignsOutWhenDisposed`, which since step 4 calls `ListPageAsync` alone: the record is per call, so the row stayed satisfied by a test that no longer calls its method | fixed `8757b1f`: it names `ASessionSignedInByHandServesABoundClientAcrossItsSubClients`, whose `LiveSite.ListedAsync` calls `ListAsync` |
| F3 | minor | `.github/pull_request_template.md`, Server only; `.claude/skills/review-code/lenses.md`, tests | Both sent a Server-only change, such as site management or Active Directory, to the `ServerOnly` tests, which now hold the password sign-in alone | fixed `8757b1f`: they name the `Live` tests that reach the change, and `ServerOnly` for a password's sign-in |
| F4 | minor | `.claude/skills/finish-work/SKILL.md`, step 3 | Step 3 began "when the developer has a Tableau Server", so a developer with a Tableau Cloud site was given no path, the scratch program included, though Q8 keeps Tableau Cloud checked by hand | fixed `8757b1f`: on a Tableau Server the `Live` tests; on Tableau Cloud, or where no `Live` test reaches the change, a scratch program |
| F5 | minor | `docs/live-coverage.md`, the lead | The rules `LiveCoverageTests` holds left out step 9's: a `no` row that says it isn't live yet fails | fixed `8757b1f`: the lead says so |
| F6 | minor | `SuiteCleanup.AfterEveryTestAsync`, with `LiveSite.CleanupAsync` and its sweep | The cleanup rethrew its failures unscrubbed. With the server unreachable at the run's end, Delete Site's `TableauException` carries .NET's "(host:443)", kept as the inner exception or in the sweep's message, and MSTest prints a cleanup's exception and every inner one: the server's host, a known value, reached the public log, where GitHub masks only the whole secret URL | fixed `8757b1f`: `SuiteCleanup.Failure` throws one exception with each failure's whole text scrubbed and no inner exception; a `TableauSiteTests` row checks an inner host in another case comes back `***` |

## Refuted

- **The sweep deleting a site that isn't the suite's:** `IsLeftover` needs the name's exact form, the content URL that
  name gives by ordinal comparison, and a day's age; the default site's content URL is empty.
- **The static record racing the parallel offline tests:** MSTest runs the `[DoNotParallelize]` tests after the others,
  one at a time; only `LiveSite.Client` and `ServerInfoSiteTests` log through `LiveSite.Record`.
- **The sweep's sign-in ending the setup client's session, so Create Site fails:** the client sends a call again in a
  new session after a 401, uploads aside.
- **Delete Site retried and failing the cleanup with a 404:** its descriptor is `idempotent: false`, sent once.
- **`Assert.AreEqual(first.UserName, other.UserName)` printing the token owner's name:** Switch Site answers the same
  user, so it can't fail on a value.
- **`MatrixProblems()` outside a `try`, skipping the fixture scan and the log check:** the table is embedded and read
  on every PR, and `Parse` checks each index before it reads.
- **The Delete Site row flagged when no run site was made:** that follows a failed `RunSiteAsync`, which fails every
  content test already.
- **A leftover just under a day old kept a night longer:** nothing fails; the next night takes it.
- **The `Call` property as a one-way door; deleting the `ServerOnly` tests as a loosening:** `TableauLog` is internal
  and log text no door; each deleted assertion lives in a `Live` test (Q4).
- **Excusing a failed test's rows hiding a problem:** the failure fails the run already, as the plan designs and a
  `LiveCoverageTests` row checks.
- **`docs/live-coverage.md` citing S16; the catalog test's summary saying "answered, or refused":** the first says when
  the table fills in its own words; O2 settled on answered.
- **The test-side Create Group Set without a row; Query Site by name pinned `refused 404000`:** Q2 agreed the first; the
  second is a departure the PR and #60 explain.

## Improvements

| Improvement | Answer |
|---|---|
| `GroupsClient.ListPageAsync` in the local group test too, so its row stops hanging on the directory accounts | taken `8757b1f` |
| `LiveSite`'s remarks name the four directory settings and `TABLEAU_LIVE_MATRIX` | taken `8757b1f` |
| One permissions-outcome helper for `LiveAccessTests`, `LiveCatalogTests` and `LiveAreaTests` | left: each reads another grantee and content and pins its own strings; one helper would tie three classes' pinned outcomes together to save a few lines |
| The nightly prints the rows it excuses for a failed test | left: a failed test names itself in the run's log, and the rows it excuses are those naming it in the matrix |

## Live run

Tableau Server 2025.1.5 (REST API 3.25), locally, after the fixes, every `Live` test with `TABLEAU_LIVE_MATRIX=1`:
20 of 24 passed in 342 s. The two `LiveConnectedAppTests` failed for their four settings, which the local settings don't hold, and the two `LiveDirectoryTests` for the directory accounts (the import brings no member; Add User to Site answers 404002), as on every run since step 6; neither is the change's. The matrix check passed with F1's pins, and the output held none of the hidden values. After it, read only: Query Sites lists no run site, and the token's last session is signed out.

## Checks

- `dotnet run eng/gate.cs -- full` on `8757b1f`: passed, 1012 tests, lines 97.61%, branches 95.08%, the floors unchanged.
