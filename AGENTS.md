# Tableau.Api

The rules for anyone who changes this repo, person or agent. Claude Code reads this file through `.claude/CLAUDE.md`,
Codex reads it directly; the gate caps it at 150 lines.

## What this is

A .NET client for Tableau's REST API and its internal Vizportal API, published on nuget.org as `Tableau.Api` (last
release 1.2.5, 2021, built for netstandard2.0). `main` targets netstandard2.1, on Newtonsoft.Json and
Microsoft.Extensions.Logging.

- **`TableauApiClient`:** the REST API, one partial class per area (`TableauApiClient_<Area>.cs`). It sends and reads
  XML through `XmlSerializer`, with the types in `Models/XmlSchema.cs`, generated from Tableau's REST API 3.16 schema.
- **`VizPortalClient`:** Tableau's own web client's API (`/vizportal/api/web/v1/`), JSON through Newtonsoft.Json. It
  is internal and unversioned: Tableau documents none of it and may change it in any release.
- **Where the work stands:** issue #2 reviews the code and modernizes it; its plan in `docs/plans/` records the
  decisions. Until then the code is as it was in 2024.

## Where things are

| Path | What |
|---|---|
| `Tableau.Api/` | the library: `TableauApiClient.cs` (requests, `buildUri`, versions), `TableauApiClient_*.cs` (endpoints by area), `VizPortal/` (the Vizportal client and its `Models/`), `Models/` (`XmlSchema.cs`, generated; `how_to_generate_schema.txt`, the steps; `SchemaExtensions.cs`, `TableauSession.cs`), `Attributes/` (`ApiVersion`, `OnPremiseOnly`), `Enums/`, `Exceptions/`, `PartialStream.cs`, `Tableau.Api.csproj` |
| `Tableau.Api.Tests/` | MSTest, live only: ordered classes `A01_Prepare` to `A06_DataSources`, `Base/` (the shared context, prerequisites), `Resources/sample.tde` |
| `Tableau.Api.sln` | the solution |
| `eng/` | `gate.cs`, the gate, the hooks' entry points and the setup, as one .NET file-based app |
| `.githooks/` | `commit-msg` refuses attribution, `pre-push` runs gate quick on what the push sends, `post-checkout` and `post-merge` name changed AI setup files |
| `docs/plans/`, `docs/reviews/` | plans of non-trivial changes, with the template; review records. Dated records, never current truth |
| `.github/` | `CODEOWNERS`, `pull_request_template.md` |
| `.claude/` | `CLAUDE.md` (imports this file), `settings.json` (no attribution; allows exact read-only git and gh commands, the library's build and the gate; denies the flags of Claude Code's built-in read-only commands that read a file, since those commands run without a prompt, and asks for bundles of those flags and, in Bash, for a quote or backslash in those commands and in any git command, and for one that starts with a redirection; a live test run always asks; denies reading secrets), `settings.local.json.example`, `skills/` (below) |
| `.agents/skills/` | links to `.claude/skills/<name>` for Codex, written into git's index by `dotnet run eng/gate.cs -- link` |
| `AGENTS.md`, `REVIEW.md` | this file; what a review finding is, and the questions every review answers |
| `.mcp.json` | Microsoft Learn and Context7 over HTTP; `.claude/settings.local.json.example` turns them on |
| `Directory.Build.props` | what every project shares: the package metadata, the MIT licence expression, NuGet audit |
| `global.json`, `nuget.config` | the .NET 10 SDK; nuget.org as the only package source |
| `.gitleaks.toml` | the secret scan: gitleaks' own rules, and any exception with its reason |
| `.editorconfig`, `.gitattributes`, `.gitignore` | the target code style; LF everywhere; what git ignores, secrets first |
| `LICENSE` | MIT |

A new top-level file or folder adds its row in the same change.

## Build and test

You need the .NET 10 SDK (`global.json`), git, and gitleaks for the secret scan. Every command is the same in bash and
PowerShell. The `--` after `eng/gate.cs` is needed: without it `dotnet run` takes `--file` as its own.

| What | Command |
|---|---|
| once per clone | `dotnet run eng/gate.cs -- setup` (hooks, `git blame`, skill links) |
| the library | `dotnet build Tableau.Api/Tableau.Api.csproj`: 0 warnings, and its csproj treats warnings as errors |
| the gate | `dotnet run eng/gate.cs -- quick` (selftest, build, AGENTS.md, skills, links, hooks, attribution, secrets); `full` adds the offline tests; `text --file -` checks a PR's title and description: no attribution, and the issue named |
| the live suite | `dotnet test Tableau.Api.Tests --settings <full path outside the clone>.runsettings`, once the test project builds |

- **The solution doesn't build today:** `dotnet build Tableau.Api.sln` fails with NU1201, since the test project's
  netcoreapp2.1 can't reference netstandard2.1. Build the library alone; issue #2 moves the tests on.
- **No offline tests yet:** gate full says so and passes. The modernization adds them, and their coverage floor.
- **The live suite** signs in to a Tableau Server or Tableau Cloud site, creates projects and publishes data sources.
  Run it only on a site you may change. Its settings come from a `.runsettings` file: `Url`, `Version`, `User`,
  `Password`, `Site`, `NonAdminUser`, `NonAdminPassword`, `NonAdminUserId` (`A01_Prepare.cs`, `A02_Authentication.cs`).
- **Keep that file outside the clone,** pass its full path to `--settings`; the solution's `test.runsettings` is a
  leftover. Read denies stop a command naming the file, not one reaching it unnamed (`grep -r`, a glob, `diff -r`);
  outside the working directory, Claude Code's path checks narrow the reads, and the denies and asks cover the
  file-valued flags known for its current built-in table. `*.runsettings` stays git-ignored as a backstop.
- **Hosted CI doesn't exist yet:** a PR says what ran, and on which Tableau version.

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

The skills in `.claude/skills/` run this loop: `start-work` (1 to 3), `review-plan`, `review-code`, `finish-work`
(5), and `write-skill` for writing or fixing a skill.

## Writing

- A lead line, then a table or short bullets. Conclusion first; name the file, the command, the number.
- Plain words. No "ensure", "robust", "comprehensive", "leverage" or "seamless"; spaced hyphens, never em dashes;
  British spelling (behaviour, licence).
- A comment says why, not what the next line does, and says it in its own words, not as a plan or review id.
- Docs move in the same change: the XML doc comment of a public member, `how_to_generate_schema.txt` for the schema,
  this file for a changed path, command or rule.

## Commits and PRs

- A plain imperative subject that says what changed; a body that says why, ending `Refs #<n>`, `Closes #<n>` or
  `Fixes #<n>`.
- PRs are squash-merged: the PR title becomes the subject on `main`, its description the body.
- No tool attribution anywhere: no AI co-author trailers, no "Generated with" lines, no session links, in commits,
  PR titles or PR bodies. `commit-msg` refuses them, and the gate checks every message on the branch.
- The gate also refuses a commit whose author or committer is a tool: a tool's address, a bot's other than
  Dependabot's, GitHub Actions' or Renovate's, or a tool's name with a model or a digit ("Claude Opus 4.6",
  "gpt-4o") or in full ("Claude Code", "GitHub Copilot", "OpenAI Codex"). "Claude" or "Claude Max" alone passes.

## One-way doors

Users depend on these from 1.2.5 on nuget.org. Change one only in a PR that names it in its title, after the
maintainers agree in its issue.

- the NuGet id `Tableau.Api`;
- the root namespace `Tableau.Api`, and the namespaces under it;
- the public API of `TableauApiClient`, `VizPortalClient` and the `Models` types users get back
  (`Tableau.Api.Models`, `Tableau.Api.VizPortal.Models`): names, parameters, defaults, return types;
- the target frameworks: 1.2.5 ships netstandard2.0, which .NET Framework 4.6.1 and later load; `main` already
  targets netstandard2.1 (`e19a822`), which .NET Framework can't;
- the Tableau versions it claims: REST API 2.0 and up, a version above 3.14 used as 3.14 with a warning
  (`_minVersion`, `_maxVersion` in `TableauApiClient.cs`), on Tableau Server and Tableau Cloud.

## Traps

What this repo's history already paid for:

| What broke | When | The guard |
|---|---|---|
| Revisions, group users and user updates were sent to `site/{id}/...`, a path the REST API doesn't have | `ad7e475` | Site paths start `sites/{session.SiteId}/` |
| Older API versions answer in the `http://tableausoftware.com/api` namespace, which didn't deserialise | `a43049b`, `54ff383` | `_readResponseString` rewrites it to `http://tableau.com/api` |
| Older API versions refused a request with the XML declaration or namespaces on `tsRequest` | `a43049b`, `6000591` | `getObjectAsRequestContent` strips both |
| Tableau Cloud sends `TableauIDWithMFA` as a user's auth setting, which the 3.16 schema lacks | `4267562` | Added to `siteUserAuthSettingType` by hand; step 4 of `how_to_generate_schema.txt` re-adds it after a regeneration |
| The constructor detected the version with `ServerInfo().Result`, logging through `Log` before it was set; removed a release later | `9be1ae9`, `a169131` | The caller names the version; a constructor sends no request |
| `AppendToFileUpload` POSTed a new upload instead of PUTting to `fileUploads/{id}` | `b5edc7b` | Method and path from the REST API reference |
| `SwitchSite` sent the site's id where the API takes its content URL | `60e39fd` | `siteType.contentUrl` |
| `QueryViewPreviewImage` and `UpdateGroup` left an id out of their path; each fix added a parameter, a public API break | `56a69c4`, `faf9c4a` | Every id the reference's path names is a parameter |
| View filters lost their `vf_` prefix: the condition was inverted | `2d2c50f` (PDF), `315da09` (image), `ada1595` (crosstab) | A name gets `vf_` unless it has it; `QueryViewData` still strips it (#2) |

## Look up APIs before coding against them

- **Tableau's REST API:** the reference at https://help.tableau.com/current/api/rest_api/en-us/REST/rest_api_ref.htm,
  for the method, path, body and version of each endpoint, and its schema page for the xsd. Vizportal has no public
  reference: a change to it is checked on a live site.
- **.NET** (`HttpClient`, `XmlSerializer`, logging, file-based apps): Microsoft Learn, through `.mcp.json`.
- **Newtonsoft.Json:** Context7, through `.mcp.json`. Libraries move faster than any model's memory.

## Never

- Push to `main`, or merge with a failing gate.
- Weaken a test, a check, a hook or the gate to make a change pass: no skip, no new ignore, no `--no-verify`.
- Check out, build or run code from outside the maintainers (a contributor's PR or branch) on your machine: read
  it. It would run with every credential there, a runsettings file's Tableau passwords included.
- Add tool attribution: commits, PR titles, PR bodies.
- Commit secrets: a runsettings file, passwords, personal access tokens, session tokens. Tests use invented ones.
- Change a one-way door without the maintainers' agreement.
