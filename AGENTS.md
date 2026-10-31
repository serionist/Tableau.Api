# Tableau.Api

The rules for anyone who changes this repo, person or agent. Claude Code reads this file through `.claude/CLAUDE.md`,
Codex reads it directly; the gate caps it at 150 lines.

## What this is

A .NET client for Tableau's REST API and its internal Vizportal API, on nuget.org as `Tableau.Api` (last release 1.2.6,
2026-10-30, netstandard2.0, from `release/1.x`). `main` builds 2.0 (#2, `docs/plans/`), net10.0 only, on Polly.Core,
Newtonsoft.Json and Microsoft.Extensions.Logging.Abstractions, .Options, .DependencyInjection.Abstractions and .Http.

- **`TableauClient`:** 2.0's client: options, DI, sessions, one pipeline (`Pipeline/`: an `Endpoints` descriptor per
  call, the gates, redirects, errors), a sub-client per area, `Server` and `Sessions` first, models in `Rest/Models/`.
- **`TableauApiClient`:** the old REST client, one partial class per area (`TableauApiClient_<Area>.cs`), XML through
  `XmlSerializer` and `Models/XmlSchema.cs` (REST API 3.16); each area goes as `TableauClient` takes it over.
- **`VizPortalClient`:** Tableau's own web client's API (`/vizportal/api/web/v1/`), JSON through Newtonsoft.Json. It
  is internal and unversioned: Tableau documents none of it and may change it in any release.

## Where things are

| Path | What |
|---|---|
| `src/` | `Directory.Build.targets`: the trim analyzer's package pinned, so an SDK patch leaves the lock file alone; `Directory.Build.props`: the package (MinVer from `v` tags, at least 2.0; the README; symbols; package validation against 1.2.5, with `Tableau.Api/CompatibilitySuppressions.xml` passing only the dropped netstandard2.0); `Tableau.Api/`, the library: `TableauClient.cs`, `TableauClientOptions.cs`, `TableauServiceCollectionExtensions.cs` (`AddTableauClient`), `TableauException.cs`, `TableauDownload.cs`, `TableauCredentials.cs` (the kinds of sign-in, and a connected app's JWT), `TableauSession.cs`, `ServerClient.cs`, `SessionsClient.cs` and `Pipeline/` (2.0's client: `TableauPipeline`, `TableauSessions`, the client's own session, signed in on demand, `RequestUri` with 1.2.6's path and query rules, `TableauHandler`, `TableauResilience` and `TableauResilienceHandler`, the retries and the attempt timeout, `ApiVersions`, `Endpoint`, a download's `DownloadStream` and `DownloadFiles`), `TableauApiClient.cs` (the old client's requests, `buildUri`, versions), `RequestPath.cs` (its paths, each value escaped and checked), `TableauHttp.cs` (the old clients' handler: certificates, no redirects, the base URL's path), `TableauApiClient_*.cs` (endpoints by area), `VizPortal/` (the Vizportal client and its `Models/`), `Rest/Models/` (2.0's JSON models: `Generated/`, which `eng/generate-models.cs` writes and nothing else edits; `TableauJsonContext` and the converters), `Models/` (`XmlSchema.cs`, generated; `how_to_generate_schema.txt`, the steps; `SchemaExtensions.cs`, `TableauSession.cs`), `Attributes/` (`ApiVersion`, `OnPremiseOnly`), `Enums/`, `Exceptions/`, `PartialStream.cs`, `Tableau.Api.csproj` |
| `tests/` | `Directory.Build.props`: test applications on MSTest and Microsoft.Testing.Platform, with its coverage extension, never packed; `coverage.config`, what coverage measures: the library without `XmlSchema.cs` and what source generators write into its `obj/`; `Tableau.Api.Aot/`, the app CI publishes with native AOT, with `roots.xml`, the 2.0 types it keeps; `Tableau.Api.Tests/`, the tests: `Fixtures/` (Tableau's JSON bodies, invented, and `manifest.json`), `Fakes/` (`FakeTableauServer`, an HTTP/1.1 server on a loopback port, with a TLS mode, that records each request as sent; `RecordingLogger`, each log line), `Fixtures.cs` (the invented site, user and tokens), `TableauSite.cs` (`[SiteTest]` and the `Live` and `ServerOnly` categories), `LiveSite.cs` (the live suite's settings, sign-in and run project), `FixtureRecorder.cs` (with `TABLEAU_RECORD=1`, each JSON body written scrubbed, for a fixture), one class per behaviour |
| `Tableau.Api.slnx` | the solution: the library, the tests and the native AOT app |
| `eng/` | `gate.cs`, the gate, the hooks' entry points and the setup, as one .NET file-based app; `generate-models.cs`, 2.0's models from `schema/`, Tableau's xsd, whose README gives its URL and SHA-256 (`dotnet run eng/generate-models.cs` rewrites them, `-- --check` compares); `coverage-gate.cs`, the tests' coverage against the floors in `coverage.json`; `package-check.cs`, what a packed package must hold (`dotnet run eng/package-check.cs -- <folder> [<version>]`); `apicompat.cs`, 1.2.5's API against the build's, by the ApiCompat tool, and `apicompat/`: `1.2.5.xml`, the ledger of every break, and `Baseline.csproj`, the assemblies 1.2.5 compiles against |
| `.githooks/` | `commit-msg` refuses attribution, `pre-push` runs gate quick on what the push sends, `post-checkout` and `post-merge` name changed AI setup files |
| `docs/plans/`, `docs/reviews/`, `docs/migration-2.0.md` | plans of non-trivial changes, with the template; review records. Dated records, never current truth. The guide from 1.2.x to 2.0, grouped from the ledger |
| `.github/` | `CODEOWNERS`, `pull_request_template.md`; `workflows/`: `ci.yml` (gate quick and `dotnet format`; locked restore, build, the offline tests and the coverage gate, pack with package validation, the package check, `apicompat.cs`; the tests and the coverage gate again under sv-SE and tr-TR in time zones off UTC, and on Windows with the selftest; the app in `tests/Tableau.Api.Aot` published with native AOT and run; summed up by `CI`), `audit-1x.yml` (weekly, the package audit of `release/1.x`, on Windows), `pr-text.yml` (`PR text`, gate text on the PR), `codeql.yml` (`main`, and weekly `release/1.x`), `live.yml` (the `Live` tests on the maintainers' Tableau Server, from `main` in the `tableau-live` environment: by hand, and nightly once the variable `TABLEAU_LIVE_NIGHTLY` is `true`), `dependabot-auto-merge.yml` (minor and patch updates, once CI is required); `dependabot.yml`; `ISSUE_TEMPLATE/` (the bug and feature forms; security reports go to private reporting) |
| `.claude/` | `CLAUDE.md` (imports this file), `settings.json` (no attribution in commits or PRs, and no permission rules: each developer's own settings decide what Claude Code may run), `settings.local.json.example`, `skills/` (below) |
| `.agents/skills/` | links to `.claude/skills/<name>` for Codex, written into git's index by `dotnet run eng/gate.cs -- link` |
| `AGENTS.md`, `REVIEW.md` | this file; what a review finding is, and the questions every review answers |
| `.mcp.json` | Microsoft Learn and Context7 over HTTP; `.claude/settings.local.json.example` turns them on |
| `Directory.Build.props`, `Directory.Packages.props`, `.config/dotnet-tools.json`, `global.json`, `nuget.config` | what every project shares: net10.0, Nullable, XML docs, warnings as errors, the analyzers and the code style in the build, the package metadata, the MIT licence expression, NuGet audit, a `packages.lock.json` beside each project, locked in CI; every package's version; the ApiCompat tool; the .NET 10 SDK, and Microsoft.Testing.Platform for `dotnet test`; nuget.org as the only package source |
| `.gitleaks.toml`, `.editorconfig`, `.gitattributes`, `.gitignore`, `.git-blame-ignore-revs` | the secret scan: gitleaks' own rules, and any exception with its reason; the code style and the analyzer rules, the old files' relaxed in its TableauLegacy section; LF everywhere, but Tableau's xsd, kept byte for byte (`-text`); what git ignores, secrets first; the reformatting commits `git blame` skips |
| `LICENSE`, `README.md`, `SECURITY.md`, `CONTRIBUTING.md`, `CHANGELOG.md` | MIT; what users read first, also packed into the package; how to report a vulnerability, and the supported versions; the short path for a contributor; what changed, under Unreleased until a release |

A new top-level file or folder adds its row in the same change.

## Build and test

You need the .NET 10 SDK (`global.json`), git, and gitleaks for the secret scan. Every command is the same in bash and
PowerShell. The `--` after `eng/gate.cs` is needed: without it `dotnet run` takes `--file` as its own.

| What | Command |
|---|---|
| once per clone | `dotnet run eng/gate.cs -- setup` (hooks, `git blame`, skill links) |
| the library | `dotnet build src/Tableau.Api/Tableau.Api.csproj`: 0 warnings, and every warning is an error. Its package, into an emptied `artifacts/packages` (each commit packs a new version): `dotnet pack src/Tableau.Api/Tableau.Api.csproj -c Release -o artifacts/packages`, then `dotnet run eng/package-check.cs -- artifacts/packages` and `dotnet run eng/apicompat.cs` (`-- --update` rewrites the ledger after a break a maintainer agreed) |
| the tests | `dotnet test --project tests/Tableau.Api.Tests/Tableau.Api.Tests.csproj`; for coverage, measured in Release as CI measures it, add `-c Release --coverage --coverage-output-format cobertura --coverage-settings tests/coverage.config --results-directory artifacts/TestResults` with that folder emptied, then `dotnet run eng/coverage-gate.cs` |
| the code style | `dotnet format Tableau.Api.slnx --verify-no-changes`, which catches style the build lets through; without `--verify-no-changes` it fixes it |
| the gate | `dotnet run eng/gate.cs -- quick` (selftest, build, the models current, the old types' list no longer than at the base, AGENTS.md, skills, links, hooks, attribution, secrets); `full` adds the code style, the tests with coverage and the coverage gate; `text --file -` checks a PR's title and description: no attribution, and the issue named |

- **Versions come from git tags** through MinVer, so a clone needs its history. After a package version changes in
  `Directory.Packages.props`, `dotnet restore Tableau.Api.slnx --force-evaluate` rewrites the lock files.
- **The offline tests** run on the fake server with invented data. CI runs them on Linux, again under sv-SE and tr-TR
  in time zones off UTC, and on Windows, never on a Tableau site: a PR says what ran there, and on which version.
- **Coverage only rises:** `eng/coverage.json` holds the library's line and branch floors, in whole percent. The
  coverage gate fails a run below them, and a floor lower than `origin/main`'s; a floor it says can rise is raised in
  the change.
- **A test on a Tableau site** is a `[SiteTest]` in the `Live` or `ServerOnly` category (`TableauSite.cs`). It is
  inconclusive when its switch, `TABLEAU_LIVE` or `TABLEAU_SERVER_ONLY`, is unset or empty; any value but 1 fails, so a
  typo can't skip a run meant for a site; at 1, each missing setting fails it, named. Its settings are environment
  variables set outside the clone (`LiveSite.cs` names the live suite's); run it only on a site you may change.

## How a change lands

`main` takes squash-merged PRs only. The maintainers, @serionist (Osvay Soma) and @NemesLaszlo (László Nemes), merge,
their own PRs included: no approval is required, and merging is the approval. Anyone else opens a PR from a fork. In
the skills, *the developer* is the person driving the session; approving a plan and merging belong to a maintainer.

1. **An issue** for every change, a typo included: the PR names it, and gate text checks that.
2. **A branch** `<issue>-<slug>` from `origin/main`, started before the plan is written.
3. **A plan** in `docs/plans/` for non-trivial work (`docs/plans/README.md` says when), the branch's first commit,
   reviewed with `review-plan` until approved, then approved by a maintainer.
4. **The code,** with its docs in the same change.
5. **finish-work:** `dotnet run eng/gate.cs -- full`, a draft PR, `review-code` until clean.
6. **A maintainer** squash-merges.

The skills run it: `start-work` (1 to 3), `review-plan`, `review-code`, `finish-work` (5), and `write-skill`.

## Writing

- A lead line, then a table or short bullets. Conclusion first; name the file, the command, the number.
- Plain words. No "ensure", "robust", "comprehensive", "leverage" or "seamless"; spaced hyphens, never em dashes;
  British spelling (behaviour, licence).
- A comment says why, not what the next line does, and says it in its own words, not as a plan or review id.
- Docs move in the same change: the XML doc comment of a public member, `eng/schema/README.md` for the schema,
  this file for a changed path, command or rule.

## Commits and PRs

- An imperative subject saying what changed; a body saying why, ending `Refs #<n>`, `Closes #<n>` or `Fixes #<n>`.
- PRs are squash-merged: the PR title becomes the subject on `main`, its description the body.
- No tool attribution anywhere: no AI co-author trailers, no "Generated with" lines, no session links, in commits,
  PR titles or PR bodies. `commit-msg` refuses them, and the gate checks every message on the branch.
- The gate also refuses a commit whose author or committer is a tool: a tool's address, a bot's other than
  Dependabot's, GitHub Actions' or Renovate's, or a tool's name with a model or a digit ("Claude Opus 4.6",
  "gpt-4o") or in full ("Claude Code", "GitHub Copilot", "OpenAI Codex"). "Claude" or "Claude Max" alone passes.

## One-way doors

Users depend on these from 1.2.x on nuget.org. Change one only in a PR that names it in its title, after a
maintainer agrees in its issue; the other can object on the PR before it merges.

- the NuGet id `Tableau.Api`;
- the root namespace `Tableau.Api`, and the namespaces under it;
- the public API of `TableauClient` (its sub-clients and their verbs, the options, `AddTableauClient`,
  `TableauException` and its kinds), `TableauApiClient`, `VizPortalClient` and the models users get back
  (`Tableau.Api.Rest.Models`, `.Models`, `.VizPortal.Models`): names, parameters, defaults, return types;
- the target framework: net10.0 only for 2.x (#30); .NET Framework 4.6.1 and later stay on 1.2.x, on netstandard2.0;
- the version scheme: 2.x from MinVer on `v` tags, at least 2.0; 1.x by `<Version>` on `release/1.x`, tags `v1.*`;
- the Tableau versions claimed: `TableauClient`, REST API 3.23 (Server 2024.2) to 3.29 and Tableau Cloud, older only
  with `AllowUnsupportedServerVersion` (`Pipeline/ApiVersions.cs`, #39); `TableauApiClient`, REST API 2.0 and up, a
  version above 3.14 used as 3.14 with a warning, on Tableau Server and Tableau Cloud.

## Traps

What this repo's history already paid for:

| What broke | When | The guard |
|---|---|---|
| Revisions, group users and user updates were sent to `site/{id}/...`, a path the REST API doesn't have | `ad7e475` | Site paths start `sites/{session.SiteId}/` |
| Older API versions answer in the `http://tableausoftware.com/api` namespace, which didn't deserialise | `a43049b`, `54ff383` | `_readResponseString` rewrites it to `http://tableau.com/api` |
| Older API versions refused a request with the XML declaration or namespaces on `tsRequest` | `a43049b`, `6000591` | `getObjectAsRequestContent` strips both |
| Tableau Cloud sends `TableauIDWithMFA` as a user's auth setting, which the 3.16 schema lacks | `4267562` | Added to `siteUserAuthSettingType` by hand; step 4 of `how_to_generate_schema.txt` re-adds it after a regeneration. 2.0's enumerations keep any value's text (`eng/generate-models.cs`), so the schema is never edited |
| The constructor detected the version with `ServerInfo().Result`, logging through `Log` before it was set; removed a release later | `9be1ae9`, `a169131` | A constructor sends no request: the old client takes the version it is given, `TableauClient` asks Server Info on its first call |
| `AppendToFileUpload` POSTed a new upload instead of PUTting to `fileUploads/{id}` | `b5edc7b` | Method and path from the REST API reference |
| `SwitchSite` sent the site's id where the API takes its content URL | `60e39fd` | `site.contentUrl`, as `SessionsClient.SwitchSiteAsync` sends it |
| `QueryViewPreviewImage` and `UpdateGroup` left an id out of their path; each fix added a parameter, a public API break | `56a69c4`, `faf9c4a` | Every id the reference's path names is a parameter |
| View filters lost their `vf_` prefix: the condition was inverted | `2d2c50f` (PDF), `315da09` (image), `ada1595` (crosstab) | A name gets `vf_` unless it has it; `QueryViewData` still strips it (#2) |

## Look up APIs before coding against them

- **Tableau's REST API:** the reference at https://help.tableau.com/current/api/rest_api/en-us/REST/rest_api_ref.htm,
  for the method, path, body and version of each endpoint, and its schema page for the xsd. Vizportal has no public
  reference: a change to it is checked on a live site.
- **.NET** (`HttpClient`, options, DI, logging, `XmlSerializer`, file-based apps): Microsoft Learn, through `.mcp.json`.
- **Newtonsoft.Json:** Context7, through `.mcp.json`. Libraries move faster than any model's memory.

## Never

- Push to `main`, or merge with a failing gate.
- Weaken a test, a check, a hook or the gate to make a change pass: no skip, no new ignore, no `--no-verify`.
- Check out, build or run code from outside the maintainers (a contributor's PR or branch) on your machine: read
  it. It would run with every credential there, a runsettings file's Tableau passwords included.
- Add tool attribution: commits, PR titles, PR bodies.
- Commit secrets: a runsettings file, passwords, personal access tokens, session tokens. Tests use invented ones.
- Change a one-way door without a maintainer's agreement in its issue.
