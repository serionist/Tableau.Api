# Contributing

Thanks for helping. This is the short path; [AGENTS.md](AGENTS.md) has the full rules, for people and agents alike,
and [REVIEW.md](REVIEW.md) what a review checks.

- **Open an issue first,** for every change, a typo included, with the bug or feature form. Say there that you want
  to work on it.
- **Write a plan first** for a non-trivial change ([docs/plans/README.md](docs/plans/README.md) says when), and open it
  as a draft PR. A maintainer approves it before any code.
- **Open the PR from a fork,** on a branch `<issue>-<slug>`, such as `42-view-paging`, with the template filled in.
  Its title becomes the commit on `main`, a plain imperative sentence; its description ends `Closes #<issue>`. No AI
  co-author trailers or "Generated with" lines, in commits or the PR.
- **Run the gate** before you push: `dotnet run eng/gate.cs -- setup` once, then `dotnet run eng/gate.cs -- full`. You
  need the .NET 10 SDK, git and gitleaks.
- **Leave the one-way doors alone** unless a maintainer agrees in the issue first (AGENTS.md, One-way doors).
- **Use invented data.** No real server address, user name, password or token in code, tests, logs or the PR.
- **Report security issues privately,** never in an issue: see [SECURITY.md](SECURITY.md).
- **Your contribution is MIT licensed.** Opening a pull request means you agree to license it under the repository's
  [MIT licence](LICENSE).

## How your PR is checked

The maintainers read a contributor's PR. They never check it out, build or run it on their machines, where it would
run next to their credentials, Tableau passwords included (AGENTS.md, Never). CI builds and checks it on GitHub's
runners, and CodeQL scans it; nothing in CI reaches a Tableau site, so say in the PR what you ran against one, and on
which Tableau version. Your PR is ready for review when CI is green. A maintainer squash-merges it.

A maintainer merging a Dependabot PR that waits for them, a major update of a package the library ships with or one
`dependabot-auto-merge.yml` couldn't judge, replaces the squash body with the update's line ("Bumps X from a to b.")
and `Refs #<the PR's number>`: Dependabot's own description quotes other projects' release notes, which `PR text`
doesn't check.
