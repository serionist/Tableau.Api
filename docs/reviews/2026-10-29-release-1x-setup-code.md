---
subject: "Set up release/1.x for 1.2.6, with the version and the package description (#20)"
base: release/1.x b23503e
head: 4a5ed49
date: 2026-10-29
round: 1
tier: two fresh reviewers split by scope (workflows, release and gate; tests, metadata and docs); each refuted its findings in a scratch clone
verdict: clean
---

# Review: Set up release/1.x for 1.2.6 (#20)

Two fresh agents read the change at `4a5ed49`.
- **The first** took the workflows, `release.yml`, the gate and the hooks.
- **The second** took the tests, the csproj, the metadata and the docs. It built `b23503e` and the head in a scratch clone, decompiled nuget.org's 1.2.5 and the new 1.2.6, and found all 167 code files identical. The only differences are the version and metadata attributes, so the line changes no library behaviour.

No blocker or major. The minors are fixed in this session.

| # | Sev | Where | Finding | Status |
|---|---|---|---|---|
| F1 | minor | `release.yml`, the GitHub release job | The "Latest" check counted drafts and prereleases, so a 1.x patch released while only a 2.x prerelease existed would leave Latest on the older 1.x release | fixed: the check excludes drafts and prereleases, with no pipe |
| F2 | minor | `release.yml`, the publish job; `docs/releasing.md` | With `--skip-duplicate`, a `.nupkg` already on nuget.org makes NuGet skip its symbols, so a re-run after a partial push would leave the `.snupkg` off, silently | fixed: the `.nupkg` (with `--no-symbols`) and the `.snupkg` are pushed apart |
| F3 | minor | `Tableau.Api.Tests/` | The plan deletes the old live suite on this line, but it was kept unexplained. It still builds here, with vulnerable packages, under a name main's live-suite command would run | fixed: deleted, with its AGENTS.md row and the gate's mention |
| F4 | minor | `Tableau.Api.csproj` comment, AGENTS.md | They said the API checks fail "on any change", but an added public member passes both | fixed: both say the checks catch a removed or changed member, and a review looks for an added one |

## Refuted

- **The CI gate can be fooled:** no. A dispatch run, another branch's run or a same-named workflow don't count, and an annotated tag resolves to its commit.
- **Script injection:** none. Every value reaches scripts through environment variables.
- **Token or runsettings leaks:** none. The publish job checks out nothing, and NuGet/login masks the key.
- **A non-maintainer tag:** the "Release tags" ruleset and the `nuget` environment stop it.
- **Pins and hashes:** all six action SHAs, gitleaks' checksum and 1.2.5's SHA-512 match.
- **The gate's base:** right in every lane and hook.
- **LangVersion 10 or the new compiler changes the code:** no. The DLLs are byte-identical.
- **The public API differs from 1.2.5's:** no. Strict ApiCompat reports only the assembly version.
- **The fake server is flaky:** no. 80 sequential and 32 concurrent runs gave 0 failures.
- **The tests hint at the fixes:** no.

| Improvement | Answer |
|---|---|
| A fixed count of appends in the publish test, so a regression fails rather than hangs | taken |
| `global.json`'s floor 10.0.402, as on `main` | taken |
| Warnings as errors, except the doc-comment ones 1.2.5 had | taken |
| `docs/releasing.md`: deleting a tag to re-tag the same version, and the setup the release relies on | taken |
| A weekly CodeQL run of `release/1.x` from `main` | later: S3, with the weekly NuGet audit |

Not covered: `release.yml` end to end (it needs a real tag), and nuget.org's display of the packed README.
