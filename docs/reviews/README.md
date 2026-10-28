# Reviews

What each `review-code` round found and what became of it, committed on the branch it reviewed. A plan's review lives
in the plan's "Plan review" section instead.

| Record | File |
|---|---|
| a change: a branch or a PR | `YYYY-MM-DD-<slug>-code.md`, such as `2026-10-28-dev-harness-code.md` |
| a folder or the repo, as it stands | `YYYY-MM-DD-<slug>.md` |

Dated the day of the first round. Later rounds go into the same file.

## Frontmatter

It describes the first round:

```yaml
---
subject: "Add the development harness (PR #3)"
base: main (4267562)
head: 1a2b3c4
date: 2026-10-28
round: 1
tier: standard (one fresh reviewer; findings checked against the code by the main session)
verdict: needs-fixes
---
```

| Key | What |
|---|---|
| `subject` | what was reviewed, and its PR; quoted when it holds a `#`, which otherwise starts a YAML comment |
| `base` | the branch it was compared with, and that branch's commit |
| `head` | the commit reviewed |
| `date` | the day of the first round |
| `round` | `1`; later rounds are sections of their own |
| `tier` | how it was read: the reviewers, and who checked their findings against the code |
| `verdict` | `clean`, `needs-fixes` or `needs-answers` |

## Body

- `# Review: <subject>`, a line on how it was read, then the findings: `| # | Sev | Where | Finding | Status |`.
  - Ids `F1`, `F2`, ..., numbered on across rounds; severity as `REVIEW.md` says.
  - The finding states its scenario. Status says what fixed it (the commit, what checks it) or why it stays.
- Improvements, each with its answer: taken, left (why) or later (where).
- `## Round <n>` for each later round, opening with its **Where** (the PR, the commit, how it was read) and its
  **Verdict**, then its table.
- `## Live run` when the live suite or a scratch program ran against a site: Tableau Server and its version, or
  Tableau Cloud, what ran and passed, which failures `main` has too, and what could not be shown. Never the site's
  address, a user or a password.
- `## Checks` for anything else that was run: the gate, a pack, a command tried by hand.

A record says what was found at the time and is not kept current. Its open findings become issues.
