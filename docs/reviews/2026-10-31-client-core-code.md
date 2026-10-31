---
subject: "Add the 2.0 client core: TableauClient, its options, DI and one HTTP pipeline (#40)"
base: main ffc43f5
head: ae98f14
date: 2026-10-31
round: 1
tier: three fresh reviewers split by scope (the pipeline and its security; the public API and the doors; the tests, docs and harness), reading only; the major reproduced by a fresh refuter
verdict: needs-fixes
---

# Review: client core (#40)

Three fresh agents read the change at `ae98f14` and ran nothing.

- **The pipeline reviewer:** redirects stay on one origin, with the cap enforced. The token stays out of logs, messages and other hosts. TLS validation and revocation, the plain-http refusal, the gates before any send, disposal and `ConfigureAwait(false)` all hold.
- **The public API reviewer:** the doors match #39, and each listed departure is sound, `ApiVersion` as `Version?` included. The ApiCompat ledger adds only `TableauApiClient.ServerInfo`, which the migration guide replaces. It found the major, F1.
- **The tests and docs reviewer:** every S9 test the plan lists is present and none passes vacuously; the TLS and revocation fakes are real. It found no loosening and no data from the live server.

A fresh refuter reproduced F1 with a console program against the library: it threw on a sync scope, on a sync root provider and on the default client, while the async paths were clean.

| # | Sev | Where | Finding | Status |
|---|---|---|---|---|
| F1 | major | `TableauClient`, `AddTableauClient` | The client was only `IAsyncDisposable`. After a client was resolved, a sync `scope.Dispose()` or `provider.Dispose()` threw `InvalidOperationException`, and the container stopped there, leaving the scope's earlier services undisposed. This is the usual `BackgroundService` and console pattern. | fixed `bd7dd96`: `IDisposable` as well, in the standard pattern; `ASyncScopeAndASyncProviderDisposeTheirClients` |
| F2 | minor | `AddTableauClient` | The primary handler re-read `IOptionsMonitor` on each rotation, so a reloaded configuration could turn on `DangerousAcceptAnyServerCertificate` with no warning, against "immutable once built" (F46) | fixed `bd7dd96`: the handler is built from the keyed state's frozen options; `OptionsChangedAfterTheFirstClientReachNoClientOrHandler` |
| F3 | minor | `TableauPipeline`, `TableauErrorKind.Redirect` | When a caller's own handler followed a redirect, the message said "nothing was sent there", but the request had reached the other host | fixed `bd7dd96`: its own message, the Kind's doc corrected; the test asserts the other server received it |
| F4 | minor | `TableauPipeline.TableauError` | A JSON error body with a lone surrogate or bad UTF-8 in a string threw `InvalidOperationException` rather than `TableauException` | fixed `bd7dd96`: two `ErrorTests` rows |
| F5 | minor | `TableauClientState.ApiVersionAsync` | When negotiation failed, each queued first call retried it in turn, the k-th waiting about k request timeouts | fixed `bd7dd96`: one shared negotiation task, a failure not cached; ten concurrent calls make one request |
| F6 | minor | `RequestUri.Checked` | The LUID check trimmed whitespace but sent the untrimmed value | fixed `bd7dd96`: the value must equal its own `ToString("D")`; whitespace, bracket and empty rows |
| F7 | minor | `AddTableauClient` | An app's `ConfigureHttpClientDefaults` handlers, such as Aspire's standard resilience handler, ran in the Tableau client and their timeouts and exceptions escaped the pipeline | fixed `bd7dd96`: the named client clears them, as its remarks say; S10 adds a hook for the caller's own handlers |
| F8 | minor | CHANGELOG.md | The released 1.2.6 section gained two false bullets; three bullets were glued to the line before; "above 3.29" claimed too much | fixed `b8f2232`: 1.2.6 restored from `main`, the bullets split, the cap described as negotiated only |
| F9 | minor | `ClientCertificateTests` | A revocation list named a negative serial about 1 run in 256, failing the revoked-certificate test on Linux | fixed `bd7dd96`: the entry built from the certificate; rows for serials starting 0x01, 0x7F, 0x80 and 0xFF |
| F10 | minor | the log check | Only the shared log was checked for secrets, and a redirect's logged target was never checked for redaction | fixed `bd7dd96`: every `RecordingLogger` checked at cleanup, for secrets and shown query values; a redirect in the logged flow |
| F11 | minor | the redirect tests | No row changed only the host, so dropping the host from the same-origin check failed nothing | fixed `bd7dd96`: `ARedirectToAnotherHostOnTheSamePortIsRefused` |
| F12 | minor | both lens files | The public-api lens named only the 1.x surface, so it didn't apply to the new door | fixed `b8f2232`: it names the 2.0 surface |

## Refuted

- **Redirects to another origin through `//host`, a backslash or userinfo:** the target is resolved and compared on scheme, host and port before anything is sent.
- **A 301 or 302 after a POST is followed:** both are refused.
- **The token leaks:** it lives on the message only, and no log method takes it. A caller's own redirect-following handler is the documented exception, noted for S11.
- **The 4 KB cut splits a character:** it is cut by bytes and backs off to a character's start.
- **`ApiVersion` could have stayed a library struct:** the source-generated binder can't convert one.
- **S10 or S11 would break this surface:** each adds members.
- **Two `AddTableauClient` calls with one name:** the options merge and the first registration stays.
- **The conventions test can be escaped:** a new public type must be rooted or old. Sub-clients are now found by property type.
- **The CA5359 suppression is a loosening:** it sits on the opt-in callback only, and the maintainer agreed to it on #40.

| Improvement | Answer |
|---|---|
| Find sub-clients from `TableauClient`'s property types in `ConventionsTests` | taken `bd7dd96` |
| A bounded `UrlRetrievalTimeout` for revocation | taken `bd7dd96`: 10 seconds |
| Log the floor and unreadable-version failures like the pipeline's | taken `bd7dd96` |
| The plan's design section as shipped; S9's PR; S11's notes (no sign-in body kept, a caller's redirect-following handler, sign-out only for owned state) | taken `b8f2232` |
| AGENTS.md keeps "on Tableau Server and Tableau Cloud" for the old client | taken `b8f2232` |
| A hook for the caller's own handlers in DI | later: S10, in the plan |
| The duplicate base-path and query helpers | later: S17, which deletes the old client |

## Round 2

**Where:** PR #40 at `b8f2232`, the fixes since `ae98f14`, read by one fresh agent that ran nothing. **Verdict:** clean
after F13, a minor, fixed in `951daa1` and checked in the main session.

All twelve fixes hold, each with a test that fails on a revert:
- F1's test disposes a real sync scope and a sync `ServiceProvider`;
- F2 works for the default client too;
- F5's shared task survives one waiter's cancellation and is bounded by the request timeout;
- F7's clear runs after every default, per the order of precedence;
- F8's 1.2.6 section is byte for byte `release/1.x`'s.

| # | Sev | Where | Finding | Status |
|---|---|---|---|---|
| F13 | minor | `TableauClientState.ApiVersionAsync` | F5's shared negotiation was cleared only by a waiter. If every caller cancelled before it failed (a web request's first call aborted while Server Info timed out), the failure was kept: the next call, however much later, got the stale error without asking the server. The fault could also go unobserved. | fixed `951daa1`: the negotiation clears itself under the lock before it fails, and a fault-only continuation observes it; `AFailedNegotiationNoCallerWaitsForIsNotKept`, driven by signals from the fake, fails with the fix taken out |

## Refuted (round 2)

- **A DI client disposed mid-negotiation fails other scopes:** disposing it only sets a flag; it owns no `HttpClient`.
- **The negotiation runs unbounded on no token:** each send has the request timeout.
- **A default handler registered after `AddTableauClient` escapes the clear:** defaults run before per-name settings.

| Improvement | Answer |
|---|---|
| The plan's version line says what shipped (`ApiVersion` null) | taken `951daa1` |
| AGENTS.md names the abstractions packages | taken `951daa1` |
| An F7 row with the defaults registered after `AddTableauClient` | taken `951daa1` |
| S10 orders or dedupes the per-name handler clear | later: S10, in the plan |

## Live run

Tableau Server 2025.1 (REST API 3.25), read-only, no sign-in, at `ae98f14`. `ServerInfoSiteTests` passed:
- the client negotiated 3.25 and read Server Info with certificates validated and revocation checked;
- a pinned 9.0 failed as `NotFound` with Tableau's code.

A scratch probe confirmed, by shape only:
- Server Info answers without a token at 2.4, 3.23 and 3.25;
- error codes are strings;
- the `api/-` error shape;
- no request id header.

Tableau Cloud wasn't run. The fixes change no request the server sees.

## Checks

- `dotnet run eng/gate.cs -- full` passed on `ae98f14` (299 tests), on `b8f2232` (313 tests) and on `951daa1` (315 tests; lines 93.98%, branches 92.46%, floors 93 and 92).
- CI passed on `b8f2232` (run 41224747082) and on `951daa1` (run 41227750086), Native AOT (Linux) included.
- A console program outside the clone reproduced F1 before its fix.
