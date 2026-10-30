---
subject: "Reformat the hand-written code to .editorconfig (#33)"
base: main e0b3d06
head: f0eafdd
date: 2026-10-30
round: 1
tier: standard (one fresh reviewer, refuting in a scratch clone; findings checked against the code by the main session)
verdict: clean
---

# Review: reformat the hand-written code (#33)

One fresh agent read the change at `f0eafdd`, built both sides and compared what they compile to. No finding: the change
is formatting and nothing else. It was squash-merged as `80c76d7`, which `.git-blame-ignore-revs` lists; this record
lands with the next step, so that squash stays the reformat alone.

Verified:
- **The code is the same:** all 2,668 method bodies of the base and head Release DLLs are byte-identical, and ilspy's IL
  listings, the metadata and the XML docs are identical.
- **The public API is the same:** ApiCompat in strict mode found nothing, and a control with one member added fails as
  it should.
- **The diff is style only:** the whole `git diff -w` is formatting.
- **The rules hold:** each of the five rules the change took out of the TableauLegacy section (IDE0022, IDE0025,
  IDE0027, IDE0090, IDE0161) fails the build when broken in an old file.
- **The format holds:** `dotnet format --verify-no-changes` passes on the head and fails at the base.

## Refuted

- **IDE0003 is not enforced by the build:** that predates this change, which neither added nor relaxed it, so it is no
  finding here. `dotnet format --verify-no-changes` in CI, taken below, now catches what the build lets through.

| Improvement | Answer |
|---|---|
| Run `dotnet format Tableau.Api.slnx --verify-no-changes` in CI, so a later change can't undo the reformat | taken in the next step (#34): ci.yml's Checks (Linux) runs it, and so does the gate's full lane |

## Checks

- `dotnet run eng/gate.cs -- full` on `f0eafdd`: passed, 48 offline tests.
- CI on a dispatched run of the head: green.
