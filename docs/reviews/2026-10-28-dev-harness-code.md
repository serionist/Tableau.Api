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

## Round 2

**Where:** PR #3 at `4c49db3`, the fixes since round 1 (`695193e`, `3d03082`). A fresh reviewer read the delta and the open findings, re-ran round 1's reproductions in scratch clones, and checked every command the docs name against the permission rules. **Verdict:** needs-fixes.

- **Round 1 findings:** F2 to F7, F9, F10, F12, F13 and F14 are confirmed fixed.
- **Partly fixed:**
  - F1: the start-work description, now F22;
  - F8: other spellings, now F15 and F16;
  - F11: bundles and the PowerShell tool, now F20 and F21.

| # | Sev | Where | Finding | Status |
|---|---|---|---|---|
| F15 | major | `.claude/settings.json`, allow `git blame *` | git takes bundled short flags and unique prefixes, so `git blame -wS <file>`, `--cont <file>` and `--ignore-revs <file>` print a git-ignored file past the denies | fixed `06617b2`: the `git blame` allow is gone; every allow is now exact. Checked by the 12 blame probes and the permission sweep |
| F16 | major | `.claude/settings.json`, allow `git diff origin/main...HEAD*` | With no space before `*`, `git diff origin/main...HEAD/../../<file> <outside path>` switches git to implicit no-index and prints the file on Windows | fixed `06617b2`: the `origin/main...HEAD` forms are exact. Checked by the probes and the sweep's implicit no-index rows (0 allowed) |
| F17 | major | `eng/gate.cs` gitleaks check | A regression from F9's fix: a range whose commits only delete, rename or change a mode fails "scanned 0 commits", and so does the `--chmod=+x` the hooks check asks for | fixed `06617b2`: the commit-count comparison is gone; full ids, `--no-color` and failing on ERR or FTL stay. Checked by selftest cases for delete-, rename-, mode- and empty-file-only commits and a blobless clone |
| F18 | minor | `eng/gate.cs` identity check | A person whose name is just `Claude` or `Devin` is refused as a tool | fixed `06617b2`: identities are decided by address, and by name only with a tool's qualifier. Checked by 17 `tool identity:` selftest cases and a pre-push in a clone |
| F19 | minor | `eng/gate.cs` commit-msg | A cut line followed by a no-break space or `\x0B`, with a real trailer below, passes the hook; pre-push refuses it | fixed `06617b2`: git's line splitting, its exact cut line, and a cut only above a diff. Checked by the reviewer's three messages as selftest cases and `-F` and `-v` commits in a clone |
| F20 | minor | `.claude/settings.json` asks | Bundled flags (`-an`, `-qn`) skip commit-msg after "don't ask again" on `git commit *` | wontfix: pre-push refuses the commit whatever skipped commit-msg, and `git push *--no-v*` is denied |
| F21 | minor | `.claude/settings.json` | Every deny and ask is a Bash rule; the PowerShell tool has its own rules, so `git push --no-verify` there meets no deny | fixed `06617b2`: every deny and ask is mirrored as a `PowerShell(...)` rule |
| F22 | minor | `start-work` description | Still gives the old order: plan, then branch | fixed `06617b2` |
| F23 | minor | `AGENTS.md`, `.claude/settings.json` | "A live test run always asks" has no ask rule behind it, and Commits and PRs doesn't name the identity check | fixed `06617b2`: `dotnet test` asks in both shells; AGENTS.md names the identity check |

Refuted: the following don't print content or are refused by git:
- `git diff --cached` with an outside path;
- abbreviated `--output` on log, show and diff;
- `git log --stdin < file`, which a Read deny covers;
- `rev-parse --resolve-git-dir` and `ls-files -X`;
- the gate's text lane on a secrets file;
- `dotnet *@*` blocking `--base HEAD@{1}`: undocumented, and a full id works.

The `#:property` pins hold on a clean machine under a hostile Directory.Build.props.

Two rounds have each found one more spelling of the same hole. So the fix for F15 and F16 narrows the allows, rather than adding denies, and comes with an exhaustive sweep of each allowed command's file-reading options instead of more examples.

| Improvement | Answer |
|---|---|
| review-code step 1 writes every pack path in full | taken `06617b2` |
| AGENTS.md How a change lands numbered in the new order | taken `06617b2` |
| `ResolveRange` peels after resolving, so a `:/<text>` base works | taken `06617b2` |
| REVIEW.md names bundled flags, abbreviations and implicit no-index, and says permission rules are no security boundary | taken `06617b2` |

Not covered: Claude Code's permission engine itself was not run; macOS and Linux for F16; GitHub's co-author parsing of F19's messages.

The permission sweep behind F15 and F16:
- **What it tried:** each allowed git and gh command's 760 options, 165 of which read, write or run something. Every abbreviation and bundle was tried at each position, plus implicit no-index: 224,871 commands in all.
- **Result:** 0 reach an allow other than the 15 allowed commands themselves.

## Round 3

**Where:** PR #3 at `d901d86`, the fixes in `06617b2`.
- A fresh reviewer re-ran round 2's reproductions.
- It audited the permission sweep.
- It ran Claude Code's own permission engine (2.1.312, headless `claude -p`, project settings only) in scratch clones holding git-ignored canary files.

**Verdict:** needs-fixes.

**Status of round 2's findings:**
- **Fixed:** F16 to F19 and F21 to F23. The engine itself refuses F16's `..` path and the implicit no-index with an outside path.
- **F20:** stays wontfix.
- **F15:** only partly fixed; see F24.
- **The sweep:** faithful for the rules it models. It can't see Claude Code's built-in read-only set, which is decisive here.

| # | Sev | Where | Finding | Status |
|---|---|---|---|---|
| F24 | major | `.claude/settings.json` | Claude Code runs a built-in set of read-only commands without a prompt, whatever the allows say. Round 2 dropped the denies that covered its file-reading flags, so `git diff --no-index <dir> <dir>` and `git blame --ignore-revs-file <file>` print git-ignored files again, in both shells | fixed `e6350c8`, `98afc37`: 74 denies per shell, derived from Claude Code 2.1.312's built-in read-only table (every flag that reads past the named paths), and asks for bundles of those short flags. Checked with the engine headless: 238 commands, 234 as expected; the 4 bundles that ran now prompt |
| F25 | major | `AGENTS.md` the live suite and `.claude/` row, `REVIEW.md` | A recursive read (`grep -r`, `rg --no-ignore`, `head dir/*`, `diff -r`, `git grep --no-index`) prints a runsettings file inside the clone without a prompt. Read denies only cover a path a command names. AGENTS.md said Claude Code may not read one | fixed `e6350c8`: AGENTS.md keeps the runsettings file outside the clone and passes its full path; it says what Read denies cover. Checked with the engine: recursive reads of a folder outside the clone are blocked |
| F26 | minor | `eng/gate.cs` identity check | `Claude Max`, `Devin Max` and `Claude (work)` are refused as tools, since some qualifiers are also surnames | fixed `e6350c8`: a name decides only with a model or version token or a product's full name. Checked by selftest cases for Claude Max, Devin Max, Claude (work), Claude Pro and Gemini Flash |

**Refuted:**
- a single named path through `git diff --no-index`, which the engine refuses;
- `git status --ignored` and `ls-files --ignored`, which list names only;
- `ls-files -X` and `diff -O`, which read patterns and print none of the file;
- a crafted `-F` message with attribution as diff context: pre-push refuses it;
- dropping F17's count reopening F9: it doesn't.

**Engine checks of the classes the sweep didn't model:**
- `&&` and `;` are split and each part checked;
- `GIT_PAGER=` and `git -c core.pager=` prompt;
- no allowed lane writes git config.

`git fetch origin main:refs/remotes/origin/main` only creates or fast-forwards that ref.

| Improvement | Answer |
|---|---|
| Check a permission change against Claude Code's engine, headless, not only a matcher | taken `e6350c8`: REVIEW.md, The harness |
| REVIEW.md's "a `*` after `--`" is unsafe for `git diff`, whose paths can still switch it to no-index | taken `e6350c8` |

Not covered: the engine on macOS or Linux and on other Claude Code versions; trusted sessions' project allows; GitHub's trailer parsing.

## Round 4

**Where:** PR #3 at `9bef2ff`, the fixes in `e6350c8` and `98afc37`.
- A fresh reviewer ran Claude Code's engine headless: 197 commands, one process at a time, in a clone of the head with canary files inside and outside it.
- It re-ran every earlier reproduction and sampled the derived denies and the bundle asks.
- It ran the commands the docs name.

**Verdict:** needs-fixes.

**Status:**
- **F26:** confirmed fixed.
- **F24:** confirmed for what it added. Round 3's reproductions and every earlier one deny or prompt, and 24 sampled denies and 19 sampled bundle asks behave as derived. It missed a class, now F27.
- **F25:** fixed for its scenario, but F27 makes its claim about the outside folder false.
- **No documented command is denied** in either shell.

| # | Sev | Where | Finding | Status |
|---|---|---|---|---|
| F27 | major | `.claude/settings.json`, `AGENTS.md`, `REVIEW.md` | The built-in commands the engine takes with any arguments (`diff`, `wc`, `du`, `strings`, `tree` and others) are path-checked only on their positional arguments. So `diff --from-file=<runsettings outside the clone> LICENSE`, `wc --files0-from=<file>`, `du --files0-from=<file>`, their abbreviations and `strings @<file>` print the file without a prompt in Bash | fixed `c019d41`: every command the table takes with any arguments or by a pattern was walked; diff, wc, du, strings, hexdump, jq, tree and lsof have file-valued options, now denied in both shells by shortest unique prefix (short ones denied in Bash with bundle asks in both); `du *` asks. Checked with the engine: 170 cases as expected, the reviewer's ten forms denied in both shells, no canary printed |
| F28 | minor | `.claude/settings.json` PowerShell rules | PowerShell rules ignore case, so the short-flag denies also deny harmless `grep -r`, `grep -F`, `rg -l`, `pgrep -f`, `git ls-files -x` and `tree -L` outright | fixed `c019d41`: the PowerShell short-flag denies are gone; the bundle asks cover them. Checked with the engine: `grep -rn`, `grep -F`, `rg -l`, `pgrep -f` prompt in PowerShell and run in Bash |
| F29 | minor | `REVIEW.md`, `AGENTS.md` `.claude/` row | The bundle asks aren't described, and REVIEW.md still says the table takes only full spellings | fixed `c019d41`: REVIEW.md splits the commands taken flag by flag from those taken with any flag, says only positional paths are checked, that bundle asks are load-bearing and that PowerShell ignores case; AGENTS.md's row and live-suite text follow |

**Refuted:**
- git's global options before a denied subcommand, which prompt;
- `xargs cat` and `cd <outside> && cat`, which prompt;
- sed's `e` command, which prompts;
- PowerShell's path cmdlets on a secret or outside path, which prompt;
- `strings @` on a `.runsettings` or `.env` name, which a Read deny catches;
- F26 as a loosened check: the identity check is new in this PR.

| Improvement | Answer |
|---|---|
| An ask for `du *`, which has no path check and lists an outside folder's names | taken `c019d41` |
| Keep the derivation and the engine check in the repo, so each Claude Code release can be re-derived | later: #4 |

Not covered: the engine on macOS, Linux, other Claude Code versions and trusted sessions; `tree` and `hexdump`, which aren't installed here; GitHub's trailer parsing.

## Round 5

**Where:** PR #3 at `6978b73`, the fixes in `c019d41`.
- A fresh reviewer ran Claude Code's engine headless: 154 commands, one process at a time, against a clone of the head with canaries inside and outside it.
- It read the manuals of the commands the derivation lists as having no file option.

**Verdict:** needs-fixes.

**Status:**
- **F28:** confirmed fixed.
- **F27:** confirmed for what it covers. Round 4's eighteen leaking forms deny, `du -a <outside>` prompts, and the derivation is complete for the commands taken with any arguments and the pattern commands. 20 sampled rules behave as derived.
- **F29:** confirmed fixed, but F30 makes two of its sentences untrue.
- **No documented command is denied** in either shell.

| # | Sev | Where | Finding | Status |
|---|---|---|---|---|
| F30 | major | `.claude/settings.json`, `REVIEW.md`, `AGENTS.md` | Deny rules match the command as typed, but the built-in read-only check reads it after the shell removes quotes and backslashes. So a quote or backslash inside a denied option's name (`git grep --no-i''ndex`, `git blame --ignore-revs-f''ile <file>`, `diff -\-from-file=<outside file>`, `wc -''-files0-from=<file>`) prints the file without a prompt in Bash | fixed `1ef5d60`: Bash asks on `'`, `"` and `\` for each of the 23 built-in commands with a deny, generated with the derivation; the backslash rule needs four backslashes, as Claude Code unescapes rule text twice. Checked with the engine: 129 cases as expected, every spelling prompts, the documented commands still run; a quoted argument to those commands now prompts, the accepted cost |
| F31 | minor | `.claude/settings.json` | lsof's `-A` reads a name list file, as `-k` does, with no rule | fixed `1ef5d60`: a Bash deny and bundle asks in both shells, checked with the engine |

Refuted:
- input redirection, which a Read deny or a prompt catches;
- `sed r`, capitalised command names, and xargs, which isn't in the Windows table;
- `gh auth status` without `--show-token`;
- `tree --info`, which needs a planted file;
- `ls-files --exclude-from`, which lists names only;
- wrappers (`timeout`, `nice`, `command`, `\diff`) and quoted command names, which the engine normalises before the deny.

| Improvement | Answer |
|---|---|
| Asks on `tree *`, `fd *` and `fdfind *`, which list an outside folder's names | taken `1ef5d60` |
| REVIEW.md: PowerShell asks are case-blind too, and allows count only in a trusted folder or with `--settings` | taken `1ef5d60` |
| The extractor accepts any flag-type string | taken: re-run on 2.1.312, it adds xargs' `-I` and `-E`, neither reads a file |
| `printf` and `[[` in the no-file-option list | taken |
| Report the quoting mismatch to Claude Code | taken: drafted as feedback for the maintainer to send |

`git commit` and `git push` aren't read-only commands, so they always prompt here. Their `--no-v` denies carry no quote asks, because those would make every `git commit -m "..."` prompt for someone who allowed `git commit`. pre-push stays the backstop.

**Stopping the loop:** after round 5 the maintainer (serionist) asked for one more round. It confirms this fix with the engine, and the PR merges if it finds no new major.

Not covered: macOS, Linux, other Claude Code versions, trusted sessions. Tree, fd, hexdump, jq and lsof aren't installed here, so for them only the engine's decision was seen.

## Round 6

**Where:** PR #3 at `3b7da85`, the fix in `1ef5d60`. This is the confirming round the maintainer asked for. A fresh reviewer ran Claude Code's engine headless: 204 commands, one process at a time, against a clone of the head with canaries inside and outside it. **Verdict:** needs-fixes.

- **F30:** fixed for option names. Round 5's thirteen forms prompt in Bash, and deny or prompt in PowerShell. The four-backslash rule matches a real backslash and is load-bearing: a control with two backslashes leaked.
- **F31:** fixed.
- **Quote asks:** generated for all 23 commands with a deny.
- **Shell rewrites:** none printed a canary: `$''`, `$""`, `${IFS}`, brace expansion, line continuations, tabs, globs, wrappers and quoted command names.
- **Documented commands:** none denied in Bash (57 run).

| # | Sev | Where | Finding | Status |
|---|---|---|---|---|
| F32 | major | `.claude/settings.json`, `REVIEW.md`, `AGENTS.md` | F30's root cause in the git subcommand's name. `git bl''ame --ignore-revs-file <file outside the clone>`, `git "blame"`, `git gr\ep --no-index` and `git grep'' --no-index` match neither the deny nor the per-subcommand quote ask. Blame printed a one-line credential file outside the clone, without a prompt | fixed `875dec0`: Bash asks on `'`, `"` and `\` for every git command replace the per-subcommand ones. Checked with the engine: round 6's eight forms prompt; `git status`, `git log`, `git diff --stat`, `git blame LICENSE`, `git grep -n MIT LICENSE` and `diff` still run |

**Refuted** (none printed a canary at the reviewed head):
- `$''` and `$""` strings, `${IFS}`, brace expansion and globs, which prompt;
- line continuations and tabs, which deny;
- `git.exe`, `env git` and absolute paths, which deny;
- PowerShell's `&` and quoted subcommands, which deny.

**Stopping the loop:** the fix was checked with the engine in this session, not by another fresh round. Whether to merge now or run another round is the maintainer's call.

Not covered: macOS, Linux, other Claude Code versions, trusted sessions.

## Round 7

**Where:** PR #3 at `e66370b`, the fix in `875dec0`. This is the round the maintainer asked for before the merge. A fresh reviewer ran Claude Code's engine headless: about 262 commands in 20 processes, one at a time, against a clone of the head with canaries inside and outside it. **Verdict:** needs-fixes.

- **F32:** confirmed fixed.
  - Bash: round 6's 21 forms prompt, and 15 new git spellings prompt or deny (a quote in `git` itself, tabs, continuations, `$@`, `--no-pager`).
  - PowerShell: they deny or prompt.
- **Quoting elsewhere:** no other command has the gap. The engine strips quotes from a command's name, find's own validator sees through them, and gh and docker have no file-reading flag.
- **Documented commands:** none denied (60 run).
- **The cost:** `git commit -m "..."` now asks even where `git commit` is allowed. This reverses round 5's reason for leaving commit without quote asks, and REVIEW.md names it.

| # | Sev | Where | Finding | Status |
|---|---|---|---|---|
| F33 | major | `.claude/settings.json`, `REVIEW.md`, `AGENTS.md` | Every rule starts with a command's name, but bash takes redirections before the name and the read-only check skips them. So `2>&1 git blame --ignore-revs-file <file outside the clone>`, `<LICENSE git grep --no-index`, `<LICENSE wc --files0-from=<outside file>` and `2>&1 strings @<outside file>` ran in Bash with no prompt and printed canaries, also after `true &&`, `;` or `\|`. PowerShell refuses these. The gap predates the delta | fixed `edfc7d7`: Bash asks for a command that starts with `<`, `>`, `&` or a digit. Checked with the engine: the reviewer's 17 forms prompt with the candidate settings, which match the committed ones exactly, and a re-run here gave 7 of 7 prompting. `git status --short 2>&1`, the documented git commands and the gate still run |

**Refuted:**
- a redirection between `git` and its subcommand, which prompts;
- `X=1`, `!`, `time`, `exec`, `nohup`, `if` and a comment line before the command, which deny;
- `{x}<` and `<>`, which prompt;
- `<f cat .env`, which denies.

| Improvement | Answer |
|---|---|
| Name `git commit -m` among the quote ask's costs | taken `edfc7d7` |
| Rewrap REVIEW.md lines 104 and 105 | taken `edfc7d7` |

Not covered: macOS, Linux, other Claude Code versions, trusted sessions; the tools not installed here.
