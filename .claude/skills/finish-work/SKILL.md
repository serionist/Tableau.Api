---
name: finish-work
description: >-
  Use when a change is ready ("finish", "wrap up", "we're done", "open the PR"). Checks the docs moved with the
  change, runs the full gate and, for a request, a model or the Vizportal client, the tests on a Tableau site where
  one is at hand, commits, opens a draft PR, reviews it with review-code until it is clean, and asks a maintainer to
  mark it ready or merge it; a maintainer squash-merges.
---

# Finish a change

Done means `dotnet run eng/gate.cs -- full` passed, `REVIEW.md` is answered and `review-code` is clean, not that the
work feels complete. `main` takes PRs only, and a maintainer squash-merges them (`AGENTS.md`, How a change lands).

## Steps

1. **Docs in the same change.** Check each thing that describes the change moved with it: the XML doc comment of
   each public member it touches, `eng/schema/README.md` for a new schema (`how_to_generate_schema.txt` for the old
   `XmlSchema.cs`), `AGENTS.md` for a changed path, command or rule, and the plan when the work departed from it
   (where and why). A plan with a progress log gets the step's row. Write them the way `AGENTS.md` asks.
2. **The gate:** `dotnet run eng/gate.cs -- full`, which verifies the code style with `dotnet format`, and runs the
   tests in `tests/Tableau.Api.Tests/` in Release with coverage, then the coverage gate. A change to a request, a model
   or the Vizportal client has a test there that fails without it. Fix every failure; never weaken a test, a check, a warning setting or a coverage floor to pass. When the gate
   says a floor can rise, raise it in `eng/coverage.json`.
3. **The tests on a site,** for a change to a request, a model or the Vizportal client, when the developer has a
   Tableau site they may change (`AGENTS.md`, Build and test): the `Live` tests that reach the change, which the
   developer runs with `TABLEAU_LIVE=1` and the settings `tests/Tableau.Api.Tests/LiveSite.cs` names, set outside the
   clone (a connected app's, or a personal access token's), or where none does yet, the changed endpoints called from a
   scratch program outside the clone. A live test makes its objects in the run's project, which the suite deletes; with
   `TABLEAU_RECORD=1` it writes each JSON body, scrubbed, for a fixture. `live.yml` runs the `Live` tests on the
   maintainers' Tableau Server from `main`, by hand and nightly, never on a PR: after a merge, check its next run. A
   change to what only Tableau Server has also runs its `ServerOnly` tests by hand on a Tableau Server, with
   `TABLEAU_SERVER_ONLY=1`. Note Tableau Server and its version,
   or Tableau Cloud, what passed, which failures `main` has too on the same site, and what Tableau Cloud couldn't
   show. Never read, print or log those settings, and never run against a site the developer did not name. No site:
   say so; the PR says it did not run.
4. **Commit:** a plain imperative subject that says what changed, a body that says why, `Refs #<n>`. No tool
   attribution: `commit-msg` refuses it.
5. **Open a draft PR.** Write the body to `.local/pr-body.md` from `.github/pull_request_template.md`: what and why;
   the plan, or `Trivial: <why>`; how it was tested (the gate, its test count and coverage, what ran on a site or why
   not); what ran on a Tableau Server for a Server-only change; the review; the one-way doors it touches, or "none";
   `Closes #<n>`. Write the title, a blank line and the body to
   `.local/pr-text.md` and check them with `dotnet run eng/gate.cs -- text --file .local/pr-text.md`. Push with
   `git push -u origin HEAD` (`pre-push` runs gate quick), then
   `gh pr create --draft --title "<plain imperative sentence>" --body-file .local/pr-body.md`. The title becomes the
   commit subject on `main`. Say it's open and how to see it all: the PR's Files tab, or `git diff origin/main...HEAD`.
6. **Review** with `review-code` until it is `clean`. Commit each round's fixes with gate quick passing, and the
   record in `docs/reviews/`. Put the verdict and the record's path in the body's Review section:
   `gh pr edit <n> --body-file .local/pr-body.md`.
7. **CI** on the head commit: `gh pr checks <n> --watch`. A red check is fixed, never skipped.
8. **Ask a maintainer,** as one question with a recommendation: mark it ready (`gh pr ready <n>`), merge it, or
   leave it a draft.
   - If `main` moved: `git fetch origin`, `git rebase origin/main`, `git push --force-with-lease`. A rebase that
     changes code needs the gate again.
   - Merge only on a maintainer's word: `gh pr merge <n> --squash --delete-branch --subject "<title> (#<n>)"
     --body-file .local/squash.md`, a body that says what and why and ends `Refs #<issue>` or `Closes #<issue>`. No
     HTML comments, no tool attribution.
9. **Clean up** after the merge: `git switch main` and `git pull`; a worktree goes with `git worktree remove <path>`
   from the main checkout. Work that turned up on the way becomes an issue (`gh issue create`).

## Done means

- `gate full` passed on the head commit, `review-code` is clean for exactly this code, and the PR says what ran on a
  site, and on a Tableau Server for a Server-only change.
- The PR is ready for a maintainer, merged on their word, or left a draft by their choice.

## Never

- Open, mark ready or merge a PR with a failing gate or CI, or with a review of different code.
- Mark a `REVIEW.md` question yes without its evidence.
- Add AI co-author trailers, "Generated with" lines or session links: commits, PR titles, PR bodies.
- Push to `main`, merge without a maintainer's word, or push to someone else's branch.
- Read a site's settings, or put a site's address, user, password or token in a commit, a PR or a review record.
