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

Applies when: the scope touches `TableauApiClient*.cs`, `buildUri`, `apiRequest`, or a type in `Models/`.

- Does each request match the REST API reference: method, path (`sites/{site-id}/...`), query names, body, expected
  status? `AGENTS.md`, Traps, lists the endpoints that got this wrong before.
- Does its `[ApiVersion]` name the version the reference gives, and `[OnPremiseOnly]` mark what Tableau Cloud lacks?
- Does it still deserialise what older versions send (the `tableausoftware.com` namespace) and what Tableau Cloud
  sends (`TableauIDWithMFA`)?
- Pagination: page size and number checked, every page reachable.

## credentials

Applies when: the scope touches sign-in, `TableauSession`, tokens, impersonation, logging, Vizportal's cookies, or
`IgnoreSSLErrors`.

Name the concrete leak and what it gains.

- A password, personal access token or session token in a URL, a log line at any level, an exception message, a test
  fixture that looks real, or a file other users can read.
- Certificate validation off when the caller did not ask for it.
- Impersonation that outlives the sign-in that asked for it.

A leak, or certificate checks off by default, is a blocker.

## public-api

Applies when: the scope touches a public member of `TableauApiClient`, `VizPortalClient`, or a type in `Models/` or
`VizPortal/Models/`.

- Does a public name, parameter, default or return type change? Named in the PR, with how callers move over? A
  silent change is a one-way door broken: a blocker.
- A regenerated `XmlSchema.cs`: do the lower-case generated type names users already reference still exist?

## vizportal

Applies when: the scope touches `Tableau.Api/VizPortal/`.

- The API is internal and unversioned: does the PR name the Tableau version it was checked on, and does a failure
  reach the caller as an error it can act on, not as a null?
- Is the JSON still camel case with string enums, as Tableau's web client sends it?

## tests

- Would each new check fail without the change? Is a fix pinned by something that reproduced the bug?
- The live suite needs a site: does the PR say whether it ran, against which Tableau Server version or Tableau Cloud,
  and what it can't cover? Is a failure compared with `main`'s run on the same site before it is called the change's?
- Do tests use invented data, and delete what they create on the site?
- A gate change: does the selftest cover it?

A missing check for new behaviour is major; a missing edge-case test is minor.

## docs-freshness

- Did the docs that describe the scope move with it: the XML doc comments, `how_to_generate_schema.txt`, `AGENTS.md`
  for a changed path, command or rule?
- Does anything cite a path, a command, a flag or a setting that no longer exists?
- A comment or doc outside `docs/plans/` and `docs/reviews/` that cites a plan or review id instead of the reason?

## simplicity

A second implementation of one thing, a layer with one caller, an option nobody asked for, dead code: an improvement,
written as what it drops ("X could become Y, which drops Z"). A finding only when it causes a real failure, with its
scenario (the two copies already disagree).

## compatibility

Applies when: the scope touches the target framework, packages, paths, files, encodings or processes.

- The target framework: an API or a package that raises it. 1.2.5 ships netstandard2.0, which .NET Framework loads;
  `main` targets netstandard2.1, which it can't. A change to the target is a one-way door (`AGENTS.md`).
- Tableau Server and Tableau Cloud, and every REST API version the client claims.
- Windows and Linux: path separators, `\r\n`, text read and written as UTF-8 explicitly, a tool assumed on PATH.

## harness-integrity

Applies when: the scope touches a harness file (`REVIEW.md`, The harness).

Anything that loosens a check: a gate step dropped or made a warning, a rule or a test skipped, a `NoWarn` or an
ignore added, a question or a lens removed, attribution let through, a permission widened. A blocker unless a
maintainer agrees in the PR.

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
