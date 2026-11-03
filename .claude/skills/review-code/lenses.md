# Code review lenses

`review-code` applies every lens below, to a change or to a path. `REVIEW.md` holds the severities, the questions, the
checks for what a change touches, and the harness rule; the lenses say where to look. A lens whose "Applies when"
names nothing in the scope returns no findings at once, and an empty list is a good result.

**Not a bug hunt.** Report a problem only when it is real: it can be reproduced and causes an actual failure for a
user, their Tableau site or the next change.

- It has: a title; a severity (blocker, major, minor; most are minor); the file and the member or line.
- **The scenario:** the input or state, what happens, why it matters. No scenario, no finding.
- What it is judged against: a rule in `AGENTS.md` or `REVIEW.md`, the plan, Tableau's REST API reference, a
  library's documented behaviour, or "none".
- The evidence, and the fix. Style is `.editorconfig`'s, taste is the author's.

**Improvements too,** under any lens: a reuse it missed, a simpler approach, a clearer name, a step someone must
remember that a check could do. What, where, why it's better; no severity, never counted.

## plan-and-intent

Applies when: the change has a plan in `docs/plans/` or an issue.

Does it do what the plan and the issue say? A planned step missing, or behaviour nobody asked for that a user will
meet, is a finding; a small departure the PR explains is not.

## implementation

Real bugs a user would hit. The lenses below are where this repo breaks most; this one catches the rest.

- Wrong logic; an edge case real input reaches (an empty page, a missing item, a name with spaces, `&` or non-ASCII
  text, a large upload, a site with no content URL).
- An exception swallowed or turned into a null, so a failure looks like success to the caller.
- An `HttpClient`, a response or a stream not disposed on an error path; a timeout missing on a request.
- Blocking on async work (`.Result`, `.Wait()`) where a caller's synchronisation context can deadlock it.

## rest-contract

Applies when: the scope touches `TableauApiClient*.cs`, `buildUri`, `apiRequest`, a sub-client of `TableauClient`,
`Pipeline/` (an `Endpoints` descriptor, `TableauPipeline`, `RequestUri`), or a type in `Models/` or `Rest/Models/`.

- Does each request match the REST API reference: method, path (`sites/{site-id}/...`), query names, body, expected
  status? `AGENTS.md`, Traps, lists the endpoints that got this wrong before.
- Does its descriptor in `Endpoints` (the 2.0 client) name the version the reference gives, `Server` alone for what
  Tableau Cloud lacks, and whether it is idempotent and moves a file? On the old client, does its `[ApiVersion]` name
  the version, and `[OnPremiseOnly]` mark what Tableau Cloud lacks?
- Does it still deserialise what older versions send (the `tableausoftware.com` namespace) and what Tableau Cloud
  sends (`TableauIDWithMFA`)?
- Pagination: page size and number checked, every page reachable.

## credentials

Applies when: the scope touches sign-in, `TableauSession`, tokens, impersonation, logging, Vizportal's cookies,
`IgnoreSSLErrors` or `DangerousAcceptAnyServerCertificate`.

Name the concrete leak and what it gains.

- A password, personal access token or session token in a URL, a log line at any level, an exception message, a test
  fixture that looks real, or a file other users can read.
- Certificate validation off when the caller did not ask for it.
- Impersonation that outlives the sign-in that asked for it.

A leak, or certificate checks off by default, is a blocker.

## public-api

Applies when: the scope touches a public member of the 2.0 client (`TableauClient` and its sub-clients,
`TableauClientOptions`, `AddTableauClient` and `TableauClientBuilder`, `TableauException` and `TableauErrorKind`, the
models in `Tableau.Api.Rest.Models`) or of the old one (`TableauApiClient`, `VizPortalClient`, a type in `Models/` or
`VizPortal/Models/`).

- Does a public name, parameter, default or return type change? Named in the PR, with how callers move over? A
  silent change is a one-way door broken: a blocker. On the 2.0 client: does a new public type join
  `tests/Tableau.Api.Aot/roots.xml`, and does a sub-client keep its protected constructor and virtual methods?
- A regenerated `XmlSchema.cs`: do the lower-case generated type names users already reference still exist?
- Regenerated models (`eng/generate-models.cs`): does each change in `Rest/Models/Generated/` come from the schema or
  the generator, and is each renamed or dropped type or member named in the PR as a break?

## vizportal

Applies when: the scope touches `src/Tableau.Api/VizPortal/`.

- The API is internal and unversioned: does the PR name the Tableau version it was checked on, and does a failure
  reach the caller as an error it can act on, not as a null?
- Is the JSON still camel case with string enums, as Tableau's web client sends it?

## tests

- Would each new check fail without the change? Is a fix pinned by something that reproduced the bug?
- The offline tests in `tests/Tableau.Api.Tests/`: does a change to a request, a model or the Vizportal client add one
  there, against `FakeTableauServer`, that asserts what goes on the wire (the request line, a header, the body)? Does a
  new method that builds a path keep `EveryMethodPathTests`' count true?
- A 2.0 model change, which the fake server doesn't fit: is it pinned by a fixture in
  `tests/Tableau.Api.Tests/Fixtures/` with its `manifest.json` entry, are `EveryModelTests`' counts true, and is each new
  public type in `tests/Tableau.Api.Aot/roots.xml`?
- The `Live` and `ServerOnly` tests need a site: does the PR say what ran, against which Tableau Server version or
  Tableau Cloud, and what it can't cover? A Server-only change: did the `Live` tests that reach it run on a Tableau
  Server (the `ServerOnly` tests, by hand, for a password's sign-in), and does the PR say what Tableau Cloud couldn't
  show? Is a failure compared with `main`'s run on the same
  site before it is called the change's?
- Coverage: does the coverage gate pass without a lower floor or a wider exclusion in `tests/coverage.config`, and does
  a new test cover its code's branches?
- A test on a site: is it a `[SiteTest]` with its `TestCategory`, and does it name every setting it reads?
- Do tests use invented data, and delete what they create on the site?
- A gate change: does the selftest cover it?

A missing check for new behaviour is major; a missing edge-case test is minor.

## docs-freshness

- Did the docs that describe the scope move with it: the XML doc comments, `eng/schema/README.md` or
  `how_to_generate_schema.txt`, `AGENTS.md` for a changed path, command or rule?
- Does anything cite a path, a command, a flag or a setting that no longer exists?
- A comment or doc outside `docs/plans/` and `docs/reviews/` that cites a plan or review id instead of the reason?

## simplicity

A second implementation of one thing, a layer with one caller, an option nobody asked for, dead code: an improvement,
written as what it drops ("X could become Y, which drops Z"). A finding only when it causes a real failure, with its
scenario (the two copies already disagree).

## compatibility

Applies when: the scope touches the target framework, packages, paths, files, encodings or processes.

- The target framework: `main` targets net10.0 only; .NET Framework users stay on 1.2.x, on netstandard2.0. A change
  to the target, a second one included, is a one-way door (`AGENTS.md`).
- Tableau Server and Tableau Cloud, and every REST API version a client claims: `TableauClient` 3.23 (Tableau Server
  2024.2) to 3.29 and Tableau Cloud, older only with `AllowUnsupportedServerVersion`; `TableauApiClient` 2.0 and up. A
  change to that claim is a one-way door (`AGENTS.md`).
- Windows and Linux: path separators, `\r\n`, text read and written as UTF-8 explicitly, a tool assumed on PATH.

## harness-integrity

Applies when: the scope touches a harness file (`REVIEW.md`, The harness).

Anything that loosens a check: a gate step dropped or made a warning, a rule or a test skipped, a `NoWarn` or an
ignore added, a file added to the TableauLegacy section of `.editorconfig` or a rule the build already enforced
relaxed there, a package validation suppression added, a question or a lens removed, or attribution let through. A
blocker unless a maintainer agrees in the PR.

## Refuting

- Every finding gets a refutation attempt before it is reported.
- Rule it not real when:
  - its scenario can't happen (read further: it is often handled elsewhere);
  - it causes no actual failure;
  - it is taste;
  - it rests on a misread of the code or the library.
- Settle it with evidence: quote the line that proves or kills it. On your own branch a refuter may reproduce it with
  a short program or a selftest case, never against a live site.
- Someone else's PR is never run here.
- A refutation you couldn't check leaves it real. A refuted one stays in the record, with its reason.
