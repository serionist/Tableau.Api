---
subject: "Sign the live suite in with a token for content and keep the connected app for its own sign-ins (#59)"
base: main c7ffe6f
head: ca12559
date: 2026-11-01
round: 1
tier: one fresh reviewer, reading only, every lens; the one finding, on the suite's secrets, checked in the main session by reading its lines
verdict: clean
---

# Review: the live suite's two sign-ins (#59)

One fresh agent read the change at `ca12559` and ran nothing. `dotnet run eng/gate.cs -- full` passed on it first:
972 tests, lines 97.61%, branches 95.08%.

- **No skipped run:** a site test is inconclusive only when its switch is unset, as before. At 1, each missing setting of
  the sign-in it names fails it, and one sign-in's settings never stand in for the other's. A test that names none
  fails at its first credential.
- **The secrets:**
  - the token's secret, the connected app's secret and each JWT `Jwt()` mints join `LiveSite.Secrets` before any
    client sends;
  - the JWT the client mints for direct trust is caught by the log check's HS256 header prefix;
  - `live.yml` echoes nothing.
- **One token, one session:** every class that signs in is `[DoNotParallelize]`, each client is disposed, and the
  cleanup signs in only after every test.
- **The connected app's tests:**
  - each signs in only through the app;
  - each needs a token the server issued to read a page;
  - each proves its sign-out by a 401 from the server.
- **The workflow:** only the token's two secrets and the header changed. The environment and the `main` guard stand.
- **The harness:** nothing is loosened. The scopes narrow from 19 to `tableau:content:read`, which is stricter.

| # | Sev | Where | Finding | Status |
|---|---|---|---|---|
| F1 | minor | `LiveSite.Secrets`, `LogTests.NoLogLineInTheSuiteHoldsASecretOrAQueryValue` | Both docs said the set holds the settings and the session tokens, but it also holds every JWT `Jwt()` mints: someone changing the log check would read that such a JWT isn't covered | fixed `c805532`: both name the minted JWTs |

## Refuted

- **A test naming no sign-in gets no credentials and passes:** `Credentials()`, `ConnectedApp()` and `Jwt()` fail it at
  their first line, naming the setting.
- **The client's own JWT escapes the log check:** every HS256 header `ConnectedAppJwt.Mint` writes starts with the
  prefix the check looks for, and the recorder hides it by key.
- **Two tests sign each other out:** see above; `ServerInfoSiteTests` runs in parallel but never signs in.
- **The connected app's tests pass without the app:** their clients hold only its credentials.
- **`tableau:content:read` can't cover Query Projects:** the reference gives that scope from Tableau Server 2022.3.
- **The removed selection code is still used, or a row was dropped:** nothing outside the plan names it, and each old
  row is replaced or went with what it tested.
- **Content work no longer under a scoped JWT loosens a check:** that path couldn't pass on 2025.1 (401002), and #57
  records the maintainer's decision.
- **The two new tests didn't run locally, as the plan's step 1 says:** the clone has no app settings, the PR says so,
  and the step ends with `live.yml` on `main`.

| Improvement | Answer |
|---|---|
| A connected app's 401002, a missing scope, named as such, and only 401001 called a failed sign-in | taken `c805532`: `LiveSite.Refused` names the JWT's scopes for 401002 and calls only 401001 a failed sign-in; `LiveSiteTests` rows for each code and sign-in |

## Not covered

- **The connected app's two tests on a site:** they first run in `live.yml` on `main`, after the merge.
- **Requests, models, the public API, Vizportal, compatibility:** the change touches no `src/` file.

The main session read the fix and the improvement in `c805532`. Both docs name the minted JWTs. `LiveSite.Refused`
keeps Tableau's refusal as the inner exception, and calls only 401001 a failed sign-in. For the connected app, a
401002 names `LiveSite.Scopes`. For the token, a 401002 says another sign-in with it may have ended its session.

## Checks

- **`dotnet run eng/gate.cs -- full`:** the main session's run on `ca12559` passed 13 checks, with 972 tests, 97.61%
  of lines and 95.08% of branches. The floors stay at 97 and 95.
- **CI:**
  - `ca12559`: the PR's run 41326382491 and the dispatched 41326404165 passed;
  - `c805532`: the PR's run 41327909149 passed with every job, Native AOT and Windows included.
- **On a site:** Tableau Server 2025.1.5 (REST API 3.25), with the local token. The 13 existing `Live` tests passed in
  49.9 s. The two connected-app tests stopped before any call, naming the four settings the clone lacks.
