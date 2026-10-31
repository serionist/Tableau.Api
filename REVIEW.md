# Review

What every review of a change to this repo checks, whoever does it: a maintainer, an AI agent (`review-plan`,
`review-code`) or another tool. Same rules for all.

## What a finding is

Not a bug hunt. A finding is a real problem with a scenario you can reproduce: the input or state, what happens, and
why it matters to a user, their Tableau site or the next change.

- No scenario, no finding. Style is `.editorconfig`'s; taste is the author's, at most an improvement (below).
- Refute each finding before you report it: quote the line that proves or kills it. A refuted one stays in the
  record, with its reason.

| Severity | What it is |
|---|---|
| blocker | a password, personal access token, session token or runsettings file leaked (code, logs, exceptions, tests, commits, the PR); certificate checks turned off that the caller didn't turn off; a one-way door broken; a check loosened |
| major | a real user hits it in normal use: a request Tableau refuses or answers otherwise than the code expects, a response that fails to deserialise, a stream left open or read past its end, the library's build broken or warning, new behaviour that nothing checked |
| minor | everything else that is real: an edge case few meet, a missing doc comment, a clumsy error message |

- Most findings are minor. An edge case that needs several unlikely things at once is a minor at most.
- Another round only after a blocker or a major. Minors are fixed in the same round, or recorded with a reason, and
  the review ends.

## Improvements

Apart from findings, a review lists only the improvements worth more than the edit they cost: a reuse it missed, a
simpler approach, a clearer name, a step a person must remember that a check could do.

- Not improvements: rewording or reshuffling for taste, options for cases nobody hits, "while you're here" extras.
- No severity, no scenario. Never counted, never a reason for another round.
- The author answers each: `taken`, `left: <why>` or `later: <issue>`. "left: not worth it" is a normal answer.

## The questions

Each is a yes or no; a "no" is a finding.

1. Did `dotnet run eng/gate.cs -- full` pass on the reviewed commit, and CI on the PR's head? A PR from outside the
   maintainers is never run here, so until CI has passed on its head the answer is "not verified" and it waits.
2. Does the change do what its issue and plan say, and nothing nobody asked for?
3. Is new behaviour checked: by a test in `tests/Tableau.Api.Tests/` that fails without it, and what no offline test
   can show by a `Live` or `ServerOnly` test or a run against a site, which the PR names (Tableau Server and its
   version, or Tableau Cloud) with what it could not show? Does the coverage gate pass without a lower floor or a wider
   exclusion? Do tests use invented data and delete what they create on the site?
4. Does each request match Tableau's REST API reference for its method, path (`sites/{site-id}/...`), query, body and
   expected status, and do its `Endpoints` descriptor (the 2.0 client) or its `[ApiVersion]` (the old one) name the
   version and the deployments the reference gives?
5. No password, token or runsettings in code, tests, logs, exception messages, docs, commits or the PR?
6. Do the public members of `TableauApiClient`, `VizPortalClient` and the `Models` types stay as they were, or does the
   change name the one it breaks, in `eng/apicompat/1.2.5.xml` and `docs/migration-2.0.md`?
7. Does the library still build alone on net10.0 with 0 warnings, without a `NoWarn`, and with no file added to the
   TableauLegacy section of `.editorconfig`?
8. Do paths, encodings, line endings and processes work on Windows and Linux?
9. Does it leave the one-way doors in `AGENTS.md` alone, or name the one it changes and link a maintainer's
   agreement in its issue?
10. Did the docs move with it (XML doc comments, `eng/schema/README.md` or `how_to_generate_schema.txt`,
    `AGENTS.md`), written the way `AGENTS.md` asks?
11. Is each new package needed, from nuget.org, under a licence compatible with MIT, and free of known advisories
    (NuGet audit)?
12. Is the PR title a plain imperative sentence, does the body say `Closes #<n>` or `Refs #<n>`, and is there no tool
    attribution anywhere?

## When the change touches

- **Authentication** (`TableauCredentials`, `TableauSession`, `SessionsClient`, `Pipeline/TableauSessions.cs`): a
  password, a personal access token, a JWT, a connected app's secret and a session token travel only in a request body
  or the `X-Tableau-Auth` header, never in a URL, a log line, an exception's body or a `ToString`; impersonation
  (`impersonateUserId`) reaches only the sign-in that asked for it; a client that owns its session signs it out when
  disposed, and `SignOutAsync` still ends one.
- **Logging** (`Log?.Log...`, `TableauLog`): no password, token, cookie or request body that holds one, in any level; a logged URL
  holds no secret.
- **URLs** (`buildUri`): every value in a path or a query is escaped; `filter` and `sort` pass through as the caller
  wrote them, so a value inside them is the caller's to escape; a new endpoint keeps the base URL's scheme, host and
  port.
- **XML and JSON:** a type regenerated in `XmlSchema.cs` keeps the manual additions `how_to_generate_schema.txt`
  lists; a model in `src/Tableau.Api/Rest/Models/Generated/` changes only through `eng/generate-models.cs` and the
  schema in `eng/schema/`, and the PR names each type or member it renames or drops; a new field or enum value
  deserialises from both Tableau Server and Tableau Cloud; Vizportal's JSON keeps its camel case and string enums; an
  XML reader keeps DTDs off.
- **Streams and uploads** (`PartialStream`, `TableauApiClient_Publishing.cs`): an `AppendToFileUpload` chunk reads at
  most its limit, never over 64 MB, counting the stream's position apart from the buffer's offset; the upload session
  id from `InitiateFileUpload` reaches the publish; each stream is disposed by whoever owns it, the caller's by the
  caller.
- **Pagination, cancellation, versions:** page size 1 to 1,000, page number from 1, every page reachable; every public
  async method takes its token last and passes it to the request: `CancellationToken cancellationToken = default` on the
  2.0 client, which `ConventionsTests` checks, `CancellationToken?` on the old one; a new 2.0 endpoint has its
  descriptor in `Endpoints`, with the version the reference gives and `Server` alone when Tableau Cloud lacks it (an old
  one carries `[ApiVersion]`, and `[OnPremiseOnly]`); a call below its version or on the wrong deployment throws before
  sending.
- **Vizportal** (`src/Tableau.Api/VizPortal/`): an internal, unversioned API, so the PR names the Tableau version it was
  checked on; the session token goes only in the `workgroup_session_id` cookie, with an `X-Xsrf-Token` that matches
  its cookie; certificate validation follows the caller's `IgnoreSSLErrors`.
- **The harness** (below).

## The harness

These decide how every other change is checked:

- `AGENTS.md`, `REVIEW.md`, `.claude/` (`CLAUDE.md`, the settings, the skills), `.agents/`, `.mcp.json`;
- `eng/`, the model generator `generate-models.cs` and the schema in `eng/schema/` among it, `.githooks/`,
  `.gitleaks.toml`, `.github/`;
- `tests/Tableau.Api.Aot/roots.xml` and `tests/Tableau.Api.Tests/OldPublicTypes.cs`, which say what the native AOT
  job checks;
- the `Directory.Build.props` and `Directory.Build.targets` files, `Directory.Packages.props`, `.editorconfig` and the
  csproj settings that make warnings errors; `src/Tableau.Api/CompatibilitySuppressions.xml`,
  `.config/dotnet-tools.json`, and `Tableau.Api.slnx`, whose projects CI builds and tests;
  `tests/coverage.config`, which says what coverage measures and so what the floors in `eng/coverage.json` mean;
- `nuget.config` (its sources and their mapping), `global.json`, and the secrets block of `.gitignore`.

Rules:

- A change that loosens a check is a blocker unless a maintainer agrees in the PR.
- That includes a gate step dropped or made a warning, a rule or a test skipped, a `NoWarn` or an ignore added, a
  file added to the TableauLegacy section of `.editorconfig` or a rule the build already enforced relaxed there, a
  package validation suppression added, a coverage floor lowered or an exclusion added to `tests/coverage.config`, a
  question or a lens removed, or attribution let through. A break added to
  `eng/apicompat/1.2.5.xml` by its regeneration is no loosening: it is a change to the public API, which question 6
  asks about.
- A change that only makes a check stricter needs no more than a normal review.
- The attribution rule in `eng/gate.cs` is omniverse-mcp's, with Python's meaning, and its selftest cases are that
  repo's: a change to one changes the other, or says why not.
- `.claude/settings.json` sets no permission rules: each developer's own Claude Code settings decide what may run.
  A secret is kept safe by living outside the clone (`AGENTS.md`, Build and test), and every commit is checked by the
  gate and pre-push.
