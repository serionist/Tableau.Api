---
subject: "Hold the tests to a coverage floor, run them under sv-SE and tr-TR, and delete the old live suite (#35)"
base: main 80c76d7
head: 8b37bba
date: 2026-10-30
round: 1
tier: two fresh reviewers split by scope (the library change and the tests; the coverage gate, CI, the gate and the docs), each in a full clone
verdict: clean
---

# Review: test platform and coverage (#35)

Two fresh agents read the change at `8b37bba`. Neither found a blocker or a major, and the minors are fixed in `4015735` and `73b32c9`.
- **The library and tests reviewer:**
  - ApiCompat strict mode and an ilspy comparison change nothing beyond the 82 culture sites and the four `vf_` checks;
  - it ran both culture legs in Docker;
  - the culture tests fail with the fix reverted.
- **The coverage reviewer:**
  - the gate reads Cobertura right, with the generated schema excluded;
  - it fails below a floor and refuses a floor lower than the base's;
  - the culture legs take effect, and format verify catches a regression;
  - `git blame` skips the reformat.

| # | Sev | Where | Finding | Status |
|---|---|---|---|---|
| F1 | minor | `EveryVizportalMethodTests` | It carries 15.8 points of the line floor but pinned nothing specific to a method: pointing `DeleteWorkbook` at `deleteDatasources`, or swapping `ChangeWorkbookOwner`'s ids, passed every test | fixed `4015735`: a row per method pins its Vizportal name and params; both mutations now fail it |
| F2 | minor | `TableauSite.cs`, AGENTS.md, the plan | They said "inconclusive unless set to 1", but any value but 1 fails | fixed `73b32c9`: unset or empty is inconclusive, any other value but 1 fails |
| F3 | minor | `eng/gate.cs`, AGENTS.md | The gate measured coverage in Debug and CI in Release, so a floor raised from a local run failed CI | fixed `73b32c9`: both measure Release, and the gate repeats a floor that can rise |
| F4 | minor | `.git-blame-ignore-revs`, `release/1.x` | Once setup points blame at the file, `git blame` fails on `release/1.x`, which lacks it | fixed in #36 on `release/1.x` |
| F5 | minor | the plan, the S5 and S6 review records | Dated 2026-10-31, a day after their PRs merged | fixed `73b32c9`: dated and named 2026-10-30 |

## Refuted

- **Numbers in interpolated strings escape CA1305:** `RequestPath` formats them invariantly.
- **`VF_x` against 1.2.6's rule:** both are ordinal and case-insensitive.
- **The canary proves nothing:** it checks culture data and the time zone.
- **The cancelled-call test passes vacuously:** a 404 would fail it.
- **The coverage package's licence:** not open source, but the tests are never packed.
- **The culture legs don't take:** the canary passed on both legs.

| Improvement | Answer |
|---|---|
| One `[SiteTest]` guard beside the category, and a suite check that they go together | taken `4015735` |
| The duplicate `StartsWith('/')`; the Vizportal error summary's invariant code | taken `4015735` |
| `dotnet format --verify-no-changes` in the gate's full lane, and in AGENTS.md | taken `73b32c9` |
| Selftest cases: a lowered branch floor, an unknown base, no report | taken `73b32c9` |
| `tests/coverage.config` in REVIEW.md's harness list, and the tests lens asks about a wider exclusion | taken `73b32c9` |
| `global.json` in the coverage selftest's scratch repo | taken `73b32c9` |

Re-run `dotnet run eng/gate.cs -- setup` once after this merges, so `git blame` skips the reformat.
