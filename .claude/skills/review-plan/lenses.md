# Plan review lenses

`review-plan` applies every lens below. `REVIEW.md` holds the severities, the questions and the harness rule; the
lenses say where to look in a plan. A lens that names nothing in the plan returns no findings at once, and an empty
list is a good result.

**Not a hunt.** A finding on a plan is a defect that would really hurt if the plan were followed as written: a step
that can't be built, a credential that would leak, a one-way door changed silently, a check that can't prove what it
claims.

- It has: a title; a severity (blocker, major, minor; most are minor); the plan section.
- **The scenario:** what goes wrong when the plan is followed as written. No scenario, no finding.
- What it is judged against: a rule in `AGENTS.md` or `REVIEW.md`, the code, Tableau's REST API reference, a
  library's documented behaviour.
- The evidence (quote the plan), and the proposed plan edit.

**Improvements too,** under any lens: what would make the plan, or the code it leads to, simpler, safer or cheaper
to change. No severity, never counted. Mechanical over manual: a step someone must remember, which a check could do.

## plan-and-intent

- Does the plan do what its issue asks, and nothing nobody asked for?
- Does it contradict a rule in `AGENTS.md` or `REVIEW.md`, or change a one-way door without naming it under
  Compatibility? Named openly, it is a choice for the maintainers; silent, it is a blocker.

## omissions

Read `REVIEW.md` and the code the plan changes, and list what they require that the plan leaves out and that would
really bite: the model types a new endpoint returns, its `[ApiVersion]`, the manual additions to `XmlSchema.cs` a
regeneration must keep, what a new schema changes in the generated models, a doc comment, how existing users move
over.

## rest-contract

Applies when: the plan adds or changes an endpoint, a request, a model, or how a URL is built.

Is each request named as Tableau's REST API reference has it: method, path (`sites/{site-id}/...`), query, body,
expected status, the version it appeared in? Does it work on the oldest version the client claims, or does it say
which version it needs? `AGENTS.md`, Traps, lists the ways this broke before.

## credentials

Applies when: the plan touches sign-in, sessions, tokens, impersonation, logging, Vizportal's cookies or certificate
checks.

Name the concrete leak and what it gains. Does a password, a personal access token or a session token stay out of
URLs, logs, exception messages, tests and docs? Does the design fail closed: certificate checks on unless the caller
turns them off? A leak is a blocker.

## public-api

Applies when: the plan changes a public member of `TableauApiClient`, `VizPortalClient` or a `Models` type.

Is the break named, with how a caller's code moves over? A new parameter goes where it breaks no caller, or the old
signature stays beside the new one. A silent break is a one-way door broken: a blocker.

## compatibility

Does it work for the library's users: the target framework they build for (`main` targets net10.0 only; .NET
Framework users stay on 1.2.x, on netstandard2.0), Windows and Linux, Tableau
Server and Tableau Cloud, every REST API version the client claims? A behaviour checked on one Tableau version only is
the usual way this breaks.

## sequencing-and-checks

- Can each step be built, checked and committed on its own, in that order?
- Does each step name what proves it: an offline test that fails without it, and for what no offline test can show,
  a `Live` or `ServerOnly` test or a run against a site? Does the plan say which Tableau version that is, and what it
  cannot show?

## simplicity

A seam with no caller, a setting nobody asked for, a second way of doing one thing, scope beyond the issue: an
improvement, written as what it drops and who would notice. A finding only when following the plan as written causes
a real failure.

## harness-integrity

Applies when: the plan touches a harness file (`REVIEW.md`, The harness).

Does it loosen a check, a hook, the gate or the agent setup? A blocker unless the plan says so and the maintainers
agree.

## premortem

Assume the plan shipped and a user's integration broke after the upgrade: what was the likeliest cause, and what plan
edit prevents it? Report it only when the plan's text makes the cause plausible.

## Refuting

- Every finding gets a refutation attempt before it is reported.
- Rule it not real when:
  - the plan covers it elsewhere (read the whole plan: this is the usual false positive);
  - it is deferred openly, under Later or Out of scope;
  - it is taste;
  - it rests on a misread rule or code.
- Quote the plan and the rule or code; a refutation without quoted text leaves it real.
- A refuted one stays in the record, with its reason; if the edit would still make the plan simpler, list it as an
  improvement too.
