---
subject: "Let Dependabot merge every update once CI passes, but a major of the library's own dependencies (#50)"
base: main 3433e9b
head: 5eb80c9
date: 2026-11-01
round: 1
tier: one fresh reviewer, reading only; the findings checked against the workflow by the main session
verdict: clean
---

# Review: Dependabot's auto-merge for majors (#50)

One fresh agent read the change at `5eb80c9` and ran nothing. `dotnet run eng/gate.cs -- full` and actionlint passed on
it first. The change loosens a check, a maintainer's review of the majors of the tests', the tools' and the actions'
packages and of the SDK's minor and patch updates; @serionist agreed on the PR (REVIEW.md, The harness).

What the reviewer confirmed:
- **No injection:** nothing a PR or Dependabot controls runs as shell. Every value arrives through `env`, quoted, and
  reaches jq only as data.
- **The CI guard:** every path that turns on auto-merge still passes the check that a ruleset requires CI.
- **The squash body:** it is still the job's own, never the PR's description.
- **The lock file:** it holds every package the library ships with, MinVer and the ILLink pin among them.
- **A shipped major can't hide in a group:** Dependabot's groups take minor and patch updates only.

| # | Sev | Where | Finding | Status |
|---|---|---|---|---|
| F1 | minor | `CONTRIBUTING.md`, `pr-text.yml` | Both still said a major or an SDK update waits for a maintainer, who writes its squash body | fixed `fbb02aa` |
| F2 | minor | `dependabot-auto-merge.yml` | The step had no `shell:`, so it ran without pipefail: a fetch of the lock file that failed with no output left jq an empty input, and the run failed on `--argjson` instead of holding the update with its warning | fixed `fbb02aa`: `shell: bash` |
| F3 | minor | `dependabot-auto-merge.yml` | Holding an update left on the auto-merge an earlier run had turned on, so a push that made the update a held one could still merge on green CI | fixed `fbb02aa`: a held update has its auto-merge turned off |

## Refuted

- **A branch name or a dependency's name runs as shell:** both reach the script through `env`, quoted, and jq only as
  data.
- **An early exit skips the CI guard:** every exit comes before the only `gh pr merge`, so nothing was turned on.
- **`index` at position 0:** jq treats only `false` and `null` as false; the membership test is now `any`.
- **An SDK major merges itself:** `dependabot.yml` ignores them, and the job now holds one too.

| Improvement | Answer |
|---|---|
| `shell: bash` on the step | taken `fbb02aa`, with F2 |
| Hold an SDK major in the workflow too | taken `fbb02aa` |
| Decide per package from its own update type, an untyped one counting as a major | taken `fbb02aa` |
| `any` for the membership test | taken `fbb02aa` |
| The header says MinVer is held, and that the PR can't change what is held | taken `fbb02aa` |
| Rewrap `dependabot.yml`'s paragraph | taken `fbb02aa` |

Only minors, so no second round: the main session read the fixes. It ran the per-package rule through `gh`'s jq
against `main`'s lock file: a major of Polly.Core is held; a major of MSTest is not; a minor of Polly.Core beside an
untyped Microsoft.Extensions.Http holds the latter; a minor of Polly.Core alone is not held. actionlint is clean on
`fbb02aa`.
