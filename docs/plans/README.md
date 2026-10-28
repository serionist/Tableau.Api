# Plans

A non-trivial change gets a plan here before any code: a new area of endpoints, a change to a one-way door
(`AGENTS.md`), to authentication or the session, a regenerated `XmlSchema.cs`, a new package or target framework, a
change to the harness. A fix a reviewer can follow from the PR alone needs none; its PR says `Trivial: <why>`.

- One file per plan: `YYYY-MM-DD-<slug>.md`, dated the day it was started.
- Written with `start-work`, reviewed with `review-plan` until approved, approved by a maintainer, and committed as
  the first commit of its branch. Someone outside the maintainers puts it up as a draft PR for that approval
  (`start-work`).
- A plan records what was decided and why at the time. It changes while its branch is open, when the work departs from
  it, and not after the merge.
- A plan that spans several PRs, such as the modernization's, is the exception: each step's PR updates it, and after
  its first merge only its progress log, the decisions taken on the way and their answers change.
- Fill every section. "None" is an answer; a blank is not.

## Template

```markdown
---
title: <a plain imperative sentence, which can become the PR title>
issue: "#<n>"
status: draft
approved_by:
approved_at:
---

# <title>

## Goal

What is true when this is done, and why it matters. In scope; out of scope.

## Design

How it works: the endpoints, models and helpers it adds or changes, in `TableauApiClient`, `VizPortalClient` and
`Models/`. The alternatives considered and why this one.

## Compatibility

The one-way doors it touches (`AGENTS.md`), what breaks for users (their code against the public API, the Tableau
versions they run, Tableau Server or Tableau Cloud) and how they move over. Or "none".

## Steps

Ordered, each one buildable and checkable on its own, each naming what proves it.

## Tests

The new and changed tests and checks that prove it: the gate, the offline tests once they exist, the live suite (by
hand, against which Tableau Server version or Tableau Cloud), and what none of them can show.

## Docs

The docs that change with it: XML doc comments, `Tableau.Api/Models/how_to_generate_schema.txt`, `AGENTS.md`.

## Risks

What could go wrong, and whether each risk is resolved or accepted.

## Plan review

Written by `review-plan`: each round's reviewer, date and verdict, the findings with their status, the owner calls
with their answers, and the improvements with theirs.
```

`issue` is quoted: a bare `#` starts a YAML comment.
