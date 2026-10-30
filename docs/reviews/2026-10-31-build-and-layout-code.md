---
subject: "Target net10.0 only, version 2.0 by MinVer and keep the Tableau.Api id, on a strict src/ build (#31)"
base: main fbb10b9
head: e9a1890
date: 2026-10-31
round: 1
tier: two fresh reviewers split by scope (the build and packaging; CI, the gate and the docs), each in a full clone
verdict: clean
---

# Review: build and layout for 2.0 (#31)

Two fresh agents read the change at `e9a1890` in full clones. Neither found a blocker or a major, and the minors are fixed in this session.
- **The build reviewer:**
  - each of the plan's CA rules fails a new file as an error;
  - `TableauLegacy` lists exactly the 57 old files and relaxes only the rules they break;
  - MinVer gives `2.0.0-alpha.0.N` and `2.0.0-rc.1` on a tag, and no `v1.*` tag can reach it;
  - package validation reports PKV006 alone;
  - the ledger's 18 breaks are exact: the 12 renumbered capabilities come from `7736a5f` (2022), already on main;
  - removing an entry fails the check.
- **The harness reviewer:**
  - the move to `src/` is 58 byte-identical renames;
  - CI uses the new paths and refuses a shallow clone;
  - the CA2021 selftest case really fails a strict build;
  - AGENTS.md stays at 147 lines, with true paths and doors.

| # | Sev | Where | Finding | Status |
|---|---|---|---|---|
| F1 | minor | `.github/dependabot.yml`, the tool manifest | Dependabot reads a tool manifest only at `.config/dotnet-tools.json`, so the root `dotnet-tools.json`'s ApiCompat tool would never be moved, though the comment said it would | fixed: the manifest is `.config/dotnet-tools.json`; the slnx, AGENTS.md, REVIEW.md, `apicompat.cs`, dependabot.yml and the plan follow |
| F2 | minor | AGENTS.md, Build and test | Pack then `package-check` fails from the second commit on, since each commit packs a new version beside the last | fixed: the cell says to pack into an emptied `artifacts/packages` |

## Refuted

- **Unannotated legacy files under Nullable claim non-null:** main is unreleased, and the legacy section must be empty before the release candidate.
- **The PKV006 suppression loosens a check:** package validation is new here, and PKV006 is the agreed net10.0 door.
- **Changed defaults hidden from ApiCompat:** only the five F24 methods differ.
- **`CompilerServices.cs` deleted unsafely:** net10.0 ships those attributes; keeping them would warn.
- **Old paths left in `audit-1x.yml` and the 1.x CodeQL job:** they check out `release/1.x`, which keeps the old layout.

| Improvement | Answer |
|---|---|
| The ledger ends in one newline, and a trailing newline never makes it differ | taken |
| `Tableau.Api.slnx` in REVIEW.md's harness list, since CI builds and tests its projects | taken |
| `cancelToken: cancellationToken` in the migration guide | taken |
| Compare against 1.2.6 instead of 1.2.5 | left: the guide counts from 1.2.5 (D2), and S8 replaces the types where 1.2.6 adds members |
| NuGet audit on `eng/apicompat/Baseline.csproj` | left: decided by a maintainer if an advisory ever reaches its frozen packages |
| The CA2021 probe copies the real `src/` build files too | later: S7, with the test platform |
| "Legacy section empty" as an automated check | later: S18's release check, as planned |

A reviewer wrote to the reviewed worktree once by mistake, deleting one ledger entry, and restored it at once; the worktree was clean at `e9a1890` afterwards.
