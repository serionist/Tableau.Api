---
subject: "Add CI and the open-source files, with the package description and the 1.x support statement (#16)"
base: main a836a28
head: daf2486
date: 2026-10-29
round: 1
tier: one fresh reviewer; it refuted each finding against the workflows, a scratch clone and PR #16's own CI
verdict: clean
---

# Review: Add CI and the open-source files (#16)

A fresh agent read the change at `daf2486` against AGENTS.md, REVIEW.md, issue #13 and the plan's S1. It ran
`gate full` in a scratch clone and read PR #16's own green CI. No blocker or major was found; the minors are fixed
in this session.

| # | Sev | Where | Finding | Status |
|---|---|---|---|---|
| F1 | minor | `.github/dependabot.yml` (the `dotnet-sdk` entry), `dependabot-auto-merge.yml` | A .NET SDK patch bump in `global.json` would merge itself, though it raises every developer's SDK floor (a machine one patch behind then can't build or commit, since the hooks run the gate) and CI installs the newest 10.0 SDK either way | fixed: auto-merge skips the `dotnet-sdk` ecosystem; a maintainer merges SDK bumps; the plan's progress log records it |
| F2 | minor | `pr-text.yml` comment, CONTRIBUTING.md | A major Dependabot update, merged by a maintainer with the repo's squash default, would land other projects' release notes on `main` unchecked; the comment claimed every Dependabot PR got a body of its own | fixed: the comment says which do; CONTRIBUTING.md tells a maintainer to replace the squash body with the update's line and `Refs #<PR>` |
| F3 | minor | README.md | The lead offered Vizportal, but no 1.x release has it, and the install line installs 1.2.5 | fixed: Vizportal is named as 2.0's, on `main`, never in 1.x |
| F4 | minor | CHANGELOG.md | Unreleased said netstandard2.1 reaches users in 2.0.0, which targets net10.0 only (D6) | fixed: net10.0 only, with .NET Framework and older .NET on 1.2.x |
| F5 | minor | PR #16's title | It didn't name the two one-way doors S1 lists, which AGENTS.md wants in a door PR's title | fixed: the PR is retitled to name the package description and the 1.x support statement |
| F6 | minor | `ci.yml` | CI is Linux only though the Design lists a Windows leg, unexplained, so a gate change that fails only on Windows would pass | fixed: the plan's progress log records that the Windows leg comes with S3's offline tests |

## Refuted

- **`if: always()` on `CI`:** a cancelled or failed needed job still fails it.
- **Script injection:** event text reaches scripts only through `env:` or the event file.
- **Spoofing Dependabot:** only Dependabot authors as `dependabot[bot]`, and a fork PR gets a read-only token.
- **Action pins:** all five match their tags.
- **The gitleaks download:** its sha256 matches the release's checksums.
- **The base SHA:** an all-zeros or missing `before` falls back; a stale base only widens the range.
- **Dependabot failing the gate's identity check:** a selftest case already passes it.
- **A skipped `PR text`:** it counts as passed, by design.
- **CRLF bodies:** the text lane handles them.
- **`package-check.cs`:** it builds under strict props.
- **The packed contents:** the README, XML docs, PDB with Source Link, the MIT expression and the repository URL are all there.
- **SECURITY.md:** it names classes only.

| Improvement | Answer |
|---|---|
| Check Source Link in `package-check.cs` (the PDB's map to the commit) | later: S5, when the packaging moves to `src/` |
| `dependabot.yml`'s header said security updates come once turned on; they are on | taken |

Not covered: a real Dependabot PR (fetch-metadata's ecosystem and update type for the SDK and SHA-pinned actions; the
ruleset's `require_extra_approval_for_unattributed_changes`, undocumented, is watched on the first one), the scheduled
runs, a fork PR's first run, and how nuget.org shows the README badges.
