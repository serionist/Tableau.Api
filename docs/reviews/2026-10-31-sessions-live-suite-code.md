---
subject: "Add sessions and credentials to the 2.0 client, and the nightly live suite (#44)"
base: main 54b6764
head: acbefbd
date: 2026-10-31
round: 1
tier: three fresh reviewers split by scope (credentials and sign-in; the session lifecycle and the public API; the live suite, the harness, the docs and the old client's edits), reading only; the four findings on credentials and secrets each checked by a fresh refuter
verdict: clean
---

# Review: sessions and the live suite (#44)

Three fresh agents read the change at `acbefbd` and ran nothing. `dotnet run eng/gate.cs -- full` passed on it first:
551 tests, lines 95.01% and branches 94.56%.

- **The credentials reviewer:** found no secret that can leak today.
  - The direct-trust JWT is right: its header, its claims, `exp` from the client's clock, Base64Url with no padding,
    and the HMAC over the secret's UTF-8 bytes.
  - Each kind's sign-in body matches the reference, and nothing that isn't set is written.
  - Impersonation on Tableau Cloud and a unified access token on Tableau Server are refused before any secret's
    delegate runs.
  - No sign-in or Switch Site exception keeps its body.
  - The retry log names a status or a type, never a body.
- **The session reviewer:**
  - Twenty first calls make one sign-in, and the semaphore is released on every path. Callers already waiting share
    a refusal, and a caller who comes later tries again.
  - The session is swapped atomically. A 401 signs in again once, never loops, and resends only a body that can be
    sent again.
  - A bound client never signs in. Only an owning client signs out, and its synchronous `Dispose` can't deadlock.
  - The public API matches #43.
- **The live suite and harness reviewer:**
  - `live.yml` runs on a schedule and by hand only, from `main`, with read-only contents, pinned actions and no
    persisted credentials. Its secrets go to the test step alone.
  - The nightly runs only with `TABLEAU_LIVE_NIGHTLY` set, and then a missing setting fails by name.
  - Nothing in the harness is loosened.
  - The 23 old files' edits only qualify `Models.TableauSession`.
  - The ledger gains exactly the three sign-in methods that were not already breaks.

Four findings touched credentials or secrets, and each went to a fresh refuter. All four were confirmed as minor:
- **F1:** Tableau's troubleshooting pages list 10091, `JTI_ALREADY_USED`. A unified access token's `jti` is
  optional, so for it only the `exp` argument holds.
- **F2:** the right version is 3.11, not the reviewer's 3.10: REST API 3.11 is Tableau Server 2021.1. The refuter
  proposed checking the user an impersonating sign-in answers with.
- **F6:** the refuter found the cleanup's token untracked too.
- **F9:** the refuter found three secret attributes in the 3.29 schema that the scrubber misses: a connected app
  secret's `value`, `tokenSecret` and `clientSecret`. It also found `encryptedKeychain` and a webhook's `url`.

| # | Sev | Where | Finding | Status |
|---|---|---|---|---|
| F1 | minor | `Endpoints.SignInWithJwt`, `SignInWithUnifiedAccessToken` | Every sign-in was retried like a read, so a JWT went out again with the same `jti`, which Tableau refuses (10091), and a late attempt could pass its 5-minute `exp`; departure 8 said a retried sign-in was harmless. The maintainer narrowed it on #43 | fixed `1ff5ca9`: a JWT's and a unified access token's sign-ins are sent once, a password's and a token's still retried; a direct-trust sign-in answered 503 makes one request, and the next has a new `jti` |
| F2 | minor | `Endpoints.SignInWithPersonalAccessTokenImpersonating` | Its floor was REST API 3.6, where Tableau added a token's impersonation in 3.11 (Server 2021.1): with `AllowUnsupportedServerVersion` on Server 2019.4 to 2020.4, the `user` element was sent, and a server that ignored it would act as the token's owner | fixed `1ff5ca9`: 3.11, refused at 3.10 before sending; an impersonating sign-in that answers for another user is signed out and fails as `Authentication`, for a token and a password |
| F3 | minor | `SessionLifecycleTests` | The half-life test passed with the half-life rule taken out | fixed `1ff5ca9`, with F5: the half-life rule is gone, and the tests assert the token each call sent |
| F4 | minor | `TableauSessions.SignInAsync` | No test pinned that a cancelled sign-in isn't handed to the callers waiting behind it | fixed `1ff5ca9`: a caller cancelled during a slow sign-in, and the one waiting signs in for itself |
| F5 | minor | `TableauSessions.ExpiresAt`, `RenewAt` | In a token's last minute, `ExpiresAt` was the sign-in's own instant, so every call signed in again, and a personal access token's sessions ended each other | fixed `1ff5ca9`: renewal by the deployment's lifetime, five minutes early; a token's sooner end shows in `ExpiresAt` and comes as a 401; a token at `0:00:00` serves three calls with one sign-in |
| F6 | minor | `LogTests`, `LiveSite` | In a live run, the log check printed any line holding a JWT the client minted, which no live setting covers, so a regression would put a valid JWT in the public Actions log; the cleanup's own token wasn't tracked | fixed `5d10e48`: a run that reached a site counts lines and shows none; every token the live clients carried, renewals and the cleanup's among them, is checked |
| F7 | minor | `LiveSite.CleanupAsync` | With `TABLEAU_RECORD=1`, the recorder's `HttpClient` was disposed before the cleanup's client signed out through it, so that session stayed live | fixed `5d10e48`: the suite's `HttpClient` is disposed after the cleanup's client signs out |
| F8 | minor | `LiveSite.SweepAsync`, `RunProjectsAsync` | The sweep deleted any project older than a day whose name started with the prefix, nested or not, so a user's project named so would go with its content | fixed `5d10e48`: only a top-level project with the suite's description and a run's exact name; seven names, a nested project, another description and a recent one kept, in `LiveSiteTests` |
| F9 | minor | `FixtureScrubber` | Secrets were matched by an exact key list that misses three of the schema's; names survived in URL paths, error details and keys outside the list; the run's own settings weren't replaced; an unparseable JSON body was written through the text rules | fixed `5d10e48`: secret keys by pattern and everything under `secret` or `keychain`; names fail closed; the run's settings replaced, file names included; an unparseable body writes nothing; at the suite's end a file holding a live value is deleted and named; all 13 of the schema's secret pairs, `encryptedKeychain` and a webhook's `url` tested |
| F10 | minor | `docs/migration-2.0.md`, CHANGELOG.md | Neither said that, until each area moves to 2.0, the old client's and Vizportal's methods can't get a session from the library on `main` | fixed `1ff5ca9` |
| F11 | minor | the plan | S11's row named no PR, and F27 and F34 cited the issue | fixed `5d10e48` |

## Refuted

- **A secret in a URL, a log line or an exception:** sign-in, Switch Site and Sign Out carry nothing in a path or a
  query; the logs name the credentials' kind and a status; no sign-in keeps its body; a delegate's exception is
  named by its type.
- **The JWT's claims or signature are wrong:** they match the direct-trust page, and the test recomputes the HMAC.
- **A retried password sign-in counts twice towards a lockout:** a 401 is never retried; only a lost answer to a
  wrong password could, at most `MaxRetries` times, as any HTTP client's retry would.
- **A refused sign-in is kept too long:** only the callers already waiting share it; the next one signs in.
- **Synchronous `Dispose` deadlocks:** it runs the sign-out on the thread pool, with no context to capture, and waits
  eleven seconds at most.
- **The redirect's sign-out ends the wrong session:** it ends the token that was sent, and drops the held session
  only when it is that one.
- **`live.yml` runs from another branch:** the job checks `refs/heads/main`, and the environment deploys from `main`
  alone.
- **The old files' edits change behaviour:** each added line equals its removed one without `Models.`.
- **The two `TableauSession` types bind silently:** a file importing both namespaces fails with CS0104.

| Improvement | Answer |
|---|---|
| `TableauRequest`'s `ToString` leaves the token out | taken `1ff5ca9` |
| A sign-in redirected elsewhere says its credentials went there | taken `1ff5ca9` |
| A test of synchronous `Dispose` under a single-threaded context | taken `1ff5ca9` |
| A failed renewal before expiry keeps the held session | taken `1ff5ca9`: it serves until its end, and the renewal is tried again a minute later |
| The dispose's own sign-out keeps its ten seconds | taken `1ff5ca9`: a sign-out of a token sent elsewhere is bounded by its caller's time |
| Line widths in finish-work and the CHANGELOG | taken `5d10e48`; finish-work's line 23 was that long on `main` |
| REVIEW.md: a connected app's secret value never leaves the process | taken `1ff5ca9` |
| Every token the live clients get tracked for the log check | taken `5d10e48`, with F6 |
| The hand-made session's test signs out in a `finally` | taken `5d10e48` |
| The Server-only tests prove the session ended | taken `5d10e48` |
| The remark on how soon a project is listed | taken `5d10e48` |
| The plan says the maintainers' Tableau Server replaced the Developer Program site | taken `5d10e48` |
| A JWT sign-in not retried, tested | taken `1ff5ca9`, with F1 |
| An impersonating sign-in that answers for another user fails and signs out | taken `1ff5ca9`, with F2 |
| The recorder replaces the run's settings and refuses a file holding one | taken `5d10e48`, with F9 |

Only minors, so no second round: the main session read the fixes against each finding.
- **F1:** only the two JWT descriptors are not idempotent.
- **F2:** the check compares the user asked for with the one answered, ignoring case, and signs out before it throws.
- **F5:** the held session's renewal time comes from the deployment's lifetime. A failed renewal falls back to the
  held session only before its end, and only when no 401 refused it.
- **F6:** every failure of the log check is quiet once the run reached a site.
- **F8:** the run name is matched exactly, from the start of the name to its end.

The maintainer agreed on #43 to the narrowed departure 8. F5 changes when a session is renewed, not
`TableauSession.ExpiresAt`, whose meaning departure 1 agreed.

## Live run

Tableau Server 2025.1 (REST API 3.25), the default site, signed in with a personal access token. The settings were
mapped into the test process only.
- **At `acbefbd` and again at `5d10e48`:** all four `Live` tests passed, the cleanup included. Each sign-out was
  proved by the old token's 401. The run's project was made, listed and deleted.
- **A recording run at `5d10e48`:** 19 fixtures. Neither a scan nor the suite's own end-of-run scan found the
  secret, the token's name, the host, a real LUID, an email or a token. The files were deleted.
- **Two sessions of one personal access token end each other,** so the session test classes run one at a time.
- **Listing:** a new project is listed about a second after it is made, and a deleted one is still listed minutes
  later.

Not run:
- Switch Site: the server has one site.
- The password and impersonation sign-ins: no password.
- The direct-trust sign-in: its first run is `live.yml`'s, from `main`, after the merge.
- Tableau Cloud.

## Checks

- `dotnet run eng/gate.cs -- full` passed on `acbefbd` (551 tests) and on `5d10e48` (577 tests; lines 95.05%, branches
  94.68%, floors 95 and 94).
- **CI** passed on `acbefbd` (run 41248323953) and `5d10e48` (run 41251921235), Native AOT (Linux) included; branches
  94.88% on Linux.
- **Coverage loop:** five runs of the tests with coverage in a row passed each time.
- **ApiCompat:** `dotnet run eng/apicompat.cs` passed with the ledger at 22 breaks: the three sign-in methods that
  weren't already in it.
