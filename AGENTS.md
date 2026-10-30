# Tableau.Api 1.x

The rules for anyone who changes the `release/1.x` branch, person or agent. Claude Code reads this file through
`.claude/CLAUDE.md`, Codex reads it directly; the gate caps it at 150 lines. `main`'s AGENTS.md has the rest of the
repo's rules (`git show origin/main:AGENTS.md`); where the two differ, on this branch this file wins.

## What this is

- **The 1.x line** of `Tableau.Api` on nuget.org: 1.2.5's code (`b23503e`), netstandard2.0, with
  Microsoft.Extensions.Logging 2.1.1 its one dependency. `TableauApiClient` sends and reads XML through
  `XmlSerializer`, one partial class per area. It has no Vizportal client: that came after 1.2.5, on `main`.
- **Security fixes only** (SECURITY.md). Everything else goes to `main`, for 2.0. A fix both lines need is written for
  each: `release/1.x` is never merged into `main`, nor `main` into it.

## Where things are

| Path | What |
|---|---|
| `Tableau.Api/` | the library: 1.2.5's code with the line's fixes, every request path built by `RequestPath.cs`; its project holds the version and the package settings |
| `tests/Tableau.Api.Tests/` | the offline tests (MSTest on Microsoft.Testing.Platform, net10.0, and net48 on Windows), against `Fakes/FakeTableauServer`, an HTTP server on a loopback port that records each request as sent; on net48 they also run in AppDomains that target older .NET Framework versions, beside 1.2.5's own assembly, which the project downloads |
| `Tableau.Api.sln` | the library and the offline tests |
| `eng/` | `gate.cs`, the gate, the hooks' entry points and the setup; `package-check.cs`, what a pack must hold |
| `.githooks/` | `commit-msg` refuses attribution, `pre-push` runs gate quick on what the push sends, `post-checkout` and `post-merge` name changed AI setup files |
| `.github/workflows/` | `ci.yml` (PRs into and pushes to `release/1.x`), `pr-text.yml`, `codeql.yml`, `release.yml` (tags `v1.*`) |
| `docs/releasing.md` | how a 1.x patch is released |
| `.claude/` | `CLAUDE.md` (imports this file), `settings.json` (no attribution) |
| `dotnet-tools.json` | the ApiCompat tool, pinned |
| `Directory.Build.props` | the package metadata, the MIT licence expression, NuGet audit |
| `global.json`, `nuget.config` | the .NET 10 SDK and the test runner; nuget.org as the only package source |
| `.gitleaks.toml`, `.gitattributes`, `.gitignore` | the secret scan; LF everywhere; what git ignores, secrets first |
| `README.md`, `CHANGELOG.md`, `SECURITY.md`, `LICENSE` | the package's README; what changed in 1.x; how to report a vulnerability; MIT |

## Build and test

You need the .NET 10 SDK (`global.json`), git, and gitleaks for the secret scan. Every command is the same in bash and
PowerShell.

| What | Command |
|---|---|
| once per clone | `dotnet run eng/gate.cs -- setup` |
| the library | `dotnet build Tableau.Api/Tableau.Api.csproj` |
| the offline tests | `dotnet test --project tests/Tableau.Api.Tests/Tableau.Api.Tests.csproj` |
| the gate | `dotnet run eng/gate.cs -- quick`, against `origin/release/1.x`; `full` adds the offline tests |
| the package | `dotnet pack Tableau.Api/Tableau.Api.csproj -c Release -o artifacts/packages`, then `dotnet run eng/package-check.cs -- artifacts/packages` |
| the API against 1.2.5 | `dotnet tool restore`, then `dotnet apicompat package artifacts/packages/Tableau.Api.<version>.nupkg --baseline-package <1.2.5's .nupkg> --enable-rule-cannot-change-parameter-name`, with the reference assemblies (below) |

- **The library builds with the doc-comment warnings 1.2.5 had** (CS1570 to CS1591); any other warning is an error.
- **1.2.5's `.nupkg`:** a pack puts it in NuGet's global packages folder, under `tableau.api/1.2.5/`
  (`dotnet nuget locals global-packages --list` names the folder). CI downloads it and checks its SHA-512.
- **ApiCompat needs the reference assemblies,** or it can't tell an enum from a class and misses a moved enum value.
  Pass `--package-assembly-references` and `--baseline-package-assembly-references`, each
  `".NETStandard,Version=v2.0|<folder>,<folder>"`: the folders of `netstandard.dll` (NETStandard.Library's
  `build/netstandard2.0/ref`) and `Microsoft.Extensions.Logging.Abstractions.dll`. A short `netstandard2.0` matches
  nothing: the tool warns "Could not find a reference directory" and passes. CI takes the folders from MSBuild
  (`-getItem:ReferencePath`) and fails on any "Could not" warning.
- **CI** runs the gate, the offline tests on Linux and Windows, the pack, the package check and both API checks on
  every PR into `release/1.x` and every push to it.

## What this line takes

- **A fix that is in 1.2.5's code,** small, and changes no public signature.
- **1.2.5's public API, unchanged:** package validation against 1.2.5 in every pack, and the ApiCompat tool on the
  package in CI, both with no suppressions. They fail on a removed or changed member, not on an added one, which a
  review looks for. CI fails when a `CompatibilitySuppressions.xml` exists; no `NoWarn` for a
  `CP` rule. A new enum member goes after the existing ones, so no value moves.
- **netstandard2.0, `LangVersion` 10,** and no new dependency.
- **A security fix is described by what it changes** until the fixed package is on nuget.org: its issue, PR,
  commits, changelog line and release notes never say how to attack the code before it (SECURITY.md).

## How a change lands

1. **An issue,** as on `main`.
2. **A branch** `<issue>-<slug>` from `origin/release/1.x`, and a PR into `release/1.x`.
3. **The code,** with its tests in `tests/Tableau.Api.Tests/`, its CHANGELOG line under Unreleased, and its XML doc
   comments.
4. **`dotnet run eng/gate.cs -- full`,** then CI: the ruleset requires `CI` and `PR text`.
5. **A maintainer squash-merges.** A release is a tag (`docs/releasing.md`).

Commits and PRs follow `main`'s rules: a plain imperative subject, a body that says why and ends `Refs #<n>`,
`Closes #<n>` or `Fixes #<n>`, and no tool attribution anywhere. `commit-msg` and the gate refuse it. Docs, comments
and PR text are written as `main`'s AGENTS.md asks (Writing): the conclusion first, plain words, British spelling, no
em dashes, and no plan or review ids in code.

## One-way doors

As on `main`, for users of 1.2.5: the NuGet id `Tableau.Api`; the namespaces; the public API of `TableauApiClient` and
the `Tableau.Api.Models` types (names, parameters, defaults, return types); netstandard2.0; the Tableau versions it
claims, REST API 2.0 and up (`_minVersion`), on Tableau Server and Tableau Cloud. Change one only in a PR that names it
in its title, after a maintainer agrees in its issue; the other can object on the PR before it merges.

## Never

- Merge `release/1.x` into `main`, or `main` into it.
- Push to `release/1.x`, or merge with a failing gate or CI.
- Weaken a test, a check, a hook or the gate to make a change pass: no skip, no new ignore, no suppression, no
  `--no-verify`.
- Check out, build or run code from outside the maintainers on your machine: read it.
- Add tool attribution, or commit secrets: a runsettings file, passwords, tokens. Tests use invented ones.
