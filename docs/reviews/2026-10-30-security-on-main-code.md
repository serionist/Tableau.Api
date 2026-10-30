---
subject: "Validate Vizportal's certificates as REST does, and take 1.2.6's request rules on main (#25)"
base: main 4ea869b
head: 4286fde
date: 2026-10-30
round: 1
tier: two fresh reviewers split by scope (the library's security code and its tests; the gate, CI, CodeQL and the docs), each refuting in a scratch clone
verdict: clean
---

# Review: security on main (#25)

Two fresh agents read the change at `4286fde`.
- **The security-code reviewer** made 24 mutations in a scratch clone and ran the suite after each. Every one but three fails its tests, and the three are documented as invisible on net10.0. ApiCompat in assembly mode with `--strict-mode` finds main's public API unchanged.
- **The harness reviewer** ran the audit script on real `dotnet list` output with and without advisories, checked the code-scanning API for where the release/1.x CodeQL results land, and checked the plan's rows against GitHub.

No blocker or major. The minors are fixed in this session.

| # | Sev | Where | Finding | Status |
|---|---|---|---|---|
| F1 | minor | `ci.yml`, the release/1.x package audit | It ran on Linux, where release/1.x's test project targets net10.0 only, so the 9 packages only its net48 graph holds were never checked | fixed: the audit runs on Windows |
| F2 | minor | `ci.yml` | The audit sat in CI's workflow, so a 1.x advisory would turn main's README badge red every Monday though main is fine | fixed: it is `audit-1x.yml`, a workflow of its own, and ci.yml drops its second cron |
| F3 | minor | `REVIEW.md` question 3, the review-plan lens, `docs/plans/README.md` | They still said "once offline tests exist, and until then", which this change makes dead text | fixed: they name `tests/Tableau.Api.Tests/` |

## Refuted

- **Coverage:** every handler comes from `TableauHttp.CreateHandler`, and every path goes through `RequestPath`; `buildUri` takes nothing else.
- **Redirects:** followed by neither client.
- **The TLS fake:** really untrusted.
- **The public API:** unchanged.
- **The CodeQL upload:** the release/1.x scan lands on that ref and leaves main's results alone.
- **The audit script:** reads real output right, and fails when the data can't be fetched.
- **The gate:** can't reach the old live suite, and a hung test can't hang it.
- **Secrets:** none reach a log or an exception.

| Improvement | Answer |
|---|---|
| `EveryMethodPathTests` requires the refusal to name the parameter, so it is `RequestPath`'s, not a later check's | taken |
| The Vizportal TLS test checks that the XSRF header repeats the XSRF cookie | taken |
| gitleaks on the Windows leg, so its selftest runs every case | taken, pinned by SHA-256 |
| One `TableauHttp.Root(Uri)` instead of building the root in two places | left: S9 rebuilds the HTTP core |

**Live run:** 2026-10-30, Tableau Server 2025.1.5, recorded in the PR.
- Vizportal's read calls passed with certificates validated.
- One pre-existing model bug, filed as #24, is out of this change.
