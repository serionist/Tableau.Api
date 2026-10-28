---
subject: "Add the development harness (#3)"
base: main 4267562
head: 724c056
date: 2026-10-28
round: 1
tier: two fresh reviewers, split by lens; each refuted its own findings with probes in scratch clones
verdict: needs-fixes
---

# Review: Add the development harness (#3)

Two fresh agents read the change at `724c056`:
- **the gate, the hooks and the settings:** implementation, credentials, tests, simplicity, compatibility and harness-integrity;
- **the docs, the skills and the build files:** plan-and-intent, docs-freshness, compatibility, simplicity and harness-integrity.

Neither saw the session that wrote it. Probes ran in scratch clones only.

The port of the attribution rule matches omniverse-mcp's:
- the selftest's refused, allowed and co-author lists are the same, word for word;
- `\w`, `\d`, `\s` and IGNORECASE agree with Python 3.14 on every code point;
- 837,899 fuzzed inputs showed no difference.

| # | Sev | Where | Finding | Status |
|---|---|---|---|---|
| F1 | minor | `start-work` steps 4 to 8, `review-plan` step 1, `AGENTS.md` How a change lands, `docs/plans/README.md` | The plan was written and committed before its branch existed, so `git switch -c <n>-<slug> origin/main` left the plan behind on local `main` and step 8 had nothing to commit | fixed `695193e`: the branch starts before the plan is written |
| F2 | minor | `AGENTS.md` Traps, the `vf_` row | The row claimed a guard that `QueryViewData` lacks: it still strips the prefix, so a view-data filter is sent as a plain parameter and the caller gets unfiltered data | fixed `695193e`: the row says so; the code fix is in #2's plan |
| F3 | minor | `AGENTS.md` What this is and One-way doors, both compatibility lenses | The target framework wasn't a one-way door, and the lenses described netstandard2.1 users on .NET Framework, who can't exist; 1.2.5 ships netstandard2.0 | fixed `695193e` |
| F4 | minor | `.editorconfig` header | The naming target could only be reached by renaming public members, a one-way door, or wire names such as `personalAccessTokenName` | fixed `695193e`: public and wire names stay behind the door |
| F5 | minor | `review-code` steps 6 and Never | One maintainer's spoken yes could approve a one-way-door fix; AGENTS.md wants the maintainers' agreement in the issue | fixed `695193e`: points at AGENTS.md and REVIEW.md |
| F6 | minor | `REVIEW.md` question 1, `review-code` step 2 | A PR from a fork could never be `clean` before CI exists, since it may not be run here | fixed `695193e`: it waits for CI, with question 1 recorded as not verified |
| F7 | minor | `LICENSE`, issues #1 and #2 | #1 deferred the licence to #2, and no agreement was recorded | fixed: #1 and #2 edited; MIT and László Nemes's agreement recorded on #1 |
| F8 | major | `.claude/settings.json` allows | `git blame -S <file>`, `git blame --ignore-revs-file <file>` and `dotnet build/test/restore/format @<file>` print a git-ignored runsettings file without a prompt, past the `Read` denies | fixed `3d03082`: denies `git blame *-S*`, `git blame *--ignore-revs-file*` and `dotnet *@*`; `dotnet test` no longer allowed; build, restore and format allowed only on the library project. Checked by matching each probe command against the rules |
| F9 | minor | `eng/gate.cs` gitleaks check | gitleaks exits 0 having scanned nothing when its git log fails (a blobless clone with no origin, a base with a space), and the gate reports pass | fixed `3d03082`: both ends resolve to full ids; an ERR or FTL line, or 0 commits scanned where the range changes files, fails; `--no-color`. Checked by the blobless-clone and `HEAD@{1 x}` probes and the `gitleaks:` selftest cases |
| F10 | minor | `eng/gate.cs` commit-msg | Any scissors-shaped line cut the message, so a `;` cut line hid a real `Co-Authored-By` trailer from `git commit -F`; pre-push still refused it | fixed `3d03082`: cuts only at the cut line in git's comment character with a `diff --git` line below. Checked by the reviewer's messages as selftest cases and by `-F` and `-v` commits in a clone |
| F11 | minor | `.claude/settings.json` denies | `git commit -n`, `--no-veri` and `git push --no-veri` skip the hooks past the `*--no-verify*` denies | fixed `3d03082`: denies `*--no-v*` on commit and push; `git commit -n` asks. Checked by the probes |
| F12 | minor | `eng/gate.cs` commit listing | A commit made under a tool's identity (author `Claude <noreply@anthropic.com>`) passed: only messages were checked | fixed `3d03082`: author and committer checked as co-authors are. Checked by the `a commit's people` selftest cases and a pre-push refusal in a clone |
| F13 | minor | `.claude/settings.json` denies, `.gitignore` | `*.secrets` and `*.token` files were readable without a prompt | fixed `3d03082`: `Read(**/*.secrets)`, `Read(**/*.token)` denied; `*.token` ignored |
| F14 | minor | `eng/gate.cs` the missing `origin/main` message | Its hint, `git fetch origin`, doesn't create `origin/main` in a single-branch clone | fixed `3d03082`: names `git fetch origin main:refs/remotes/origin/main`. Checked by a selftest case |

## Refuted

- **"Its csproj treats warnings as errors" is false:** it's true. Both property groups apply, and Platform defaults to AnyCPU.
- **The other Traps rows lack their guards:** each cited commit shows the bug, and each guard is in today's code.
- **The packed nuspec loses something from 1.2.5:** it loses nothing. It gains MIT, the GitHub URLs and the commit.
- **Line endings or ignore rules mangle tracked files:** they don't. Renormalising changes nothing, and no tracked file is ignored.
- **The 5,004 code points where .NET and Python 3.12 differ:** none is assigned in Python 3.12's tables, and the gate's header says so.
- **pre-push ranges:** a new branch, an update, a deletion, a force push, several refs, a tag and a detached HEAD all refused an attributed commit.
- **A missing dotnet or gitleaks:** the hooks skip and say so. `GATE_REQUIRE_GITLEAKS` makes a missing gitleaks fail.
- **Platforms:** Windows PowerShell 5.1, pwsh 7.6, Git Bash and WSL Ubuntu all ran the gate and hooks.

## Improvements

| Improvement | Answer |
|---|---|
| REVIEW.md's harness list covers nuget.config, global.json and the secrets block of .gitignore | taken `695193e` |
| review-code's pack paths in full | taken `695193e` |
| AGENTS.md step 5 in finish-work's order | taken `695193e` |
| docs/reviews/README.md gives each review kind's verdicts | taken `695193e` |
| PR #3's body names its plan and one-way doors | taken: the PR body |
| gitleaks `--no-color` | taken `3d03082` |
| A selftest case for the `core.hooksPath` warning | taken `3d03082` |
| Pin gate.cs against the warnings-as-errors and code-style settings #2 adds | taken `3d03082`: `#:property` pins |
| `dotnet test` with a settings file asks first | taken `3d03082`: `dotnet test` is no longer allowed |
| pre-push requires gitleaks until CI exists | later: CI requires it (#2, S1) |

## Not covered

- rest-contract, public-api and vizportal: nothing in scope.
- A macOS run, and Claude Code's permission engine itself: its rules were read as prefix patterns.
- A live Tableau site: none exists.
