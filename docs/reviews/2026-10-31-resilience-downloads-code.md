---
subject: "Retry safe calls, honour 429 and stream downloads (#42)"
base: main 18c8404
head: f9d4f86
date: 2026-10-31
round: 1
tier: three fresh reviewers split by scope (the resilience pipeline, the builder and its certificates; the downloads; the docs, packages and native AOT), reading only; the two certificate findings each checked by a fresh refuter, one by a program against the library
verdict: needs-fixes
---

# Review: resilience and downloads (#42)

Three fresh agents read the change at `f9d4f86` and ran nothing. `dotnet run eng/gate.cs -- full` passed on it first:
438 tests, lines 94.67% and branches 93.81%.

- **The pipeline reviewer:** a call that isn't idempotent is never sent twice. A body can be sent again on each
  attempt. Waits honour the caller's token, a timeout is never the caller's cancellation, and every failed attempt's
  answer is disposed. The retry log shows no query value.
- **The downloads reviewer:** no `Content-Disposition` gives a name with a separator, a drive, a colon, `.` or `..`.
  The temporary file is random, created new and deleted on failure. The read-ahead loses no byte.
- **The docs and packages reviewer:** found F13, below: Microsoft.Extensions.Http.Resilience changes how every app
  that uses the library builds. The licences are MIT and BSD-3-Clause, every version is locked, no Polly type is
  public, and `roots.xml` keeps the new types.

A fresh refuter reproduced F2 with a console program against the library and a loopback TLS server: a client whose
caller left the check on accepted an untrusted certificate. That made it a blocker, so a second round reads the
fixes. A second refuter confirmed F1 as a minor in the docs, and found that `IgnoreInvalidName` also turns off the
host name check.

| # | Sev | Where | Finding | Status |
|---|---|---|---|---|
| F1 | minor | `TableauClientBuilder.ConfigurePrimaryHandler`, `TableauClientOptions.DangerousAcceptAnyServerCertificate` | The docs said only `DangerousAcceptAnyServerCertificate` turns the check off, but a chain policy the caller sets is kept as set, and its `AllowUnknownCertificateAuthority`, `IgnoreNotTimeValid` or `IgnoreInvalidName` flags loosen it with no warning; `IgnoreInvalidName` also skips the host name | fixed `093356c`: the builder's remarks, `DangerousAcceptAnyServerCertificate`'s doc and the handler's summary say a callback can't turn the check off and a chain policy is used as set, its flags and revocation mode loosening it with no warning; docs only |
| F2 | blocker | `TableauHandler.Create` | The library's certificate rules were written into the `SslClientAuthenticationOptions` object the caller assigned. Two named clients sharing one, `test` with the check on and `prod` with `DangerousAcceptAnyServerCertificate`: resolve `test`, then `prod`, and `test` accepted an untrusted certificate. An app's own handler sharing the object lost its pinning callback or gained accept-any | fixed `093356c`: the handler's options are a copy of its own, all 15 members, the rules set on the copy; `TlsOptionsTwoClientsShareKeepEachClientsCheck` in both resolve orders, `TheHandlersCopyOfTheTlsOptionsCarriesEveryMember`, which counts the members |
| F3 | minor | `TableauResilience` | `Retry-After` was read and capped on every status retried, not only 429 and 503 as `MaxRetryAfter`'s doc and the agreed departure say: a 502 with `Retry-After: 600` failed at once instead of three back-off retries | fixed `093356c`: `Retry-After` on 429 and 503 only, the body on 429 only; `ARetryAfterOnAnotherStatusIsLeftToTheBackOff` |
| F4 | minor | `TableauPipeline.Failure`, `AddHttpMessageHandler` | A caller's handler's own timeout or cancellation surfaced as `Timeout`, blaming `RequestTimeout` or the `HttpClient`'s timeout, where the remarks say a handler's exception is `Http` with it as the cause | fixed `093356c`: the library's own attempt timeout is told apart; a handler's own timeout or cancellation is `Http` with it as the cause, not retried; `AHandlersOwnTimeoutOrCancellationIsHttpWithItsCause`, `AHandlersOwnTimeoutInsideTheRetriesIsNeitherRetriedNorATimeout` |
| F5 | minor | `ClientBuilderTests` | `ASettingThatThrowsFailsTheHandlerAndLeavesNoneOpen` passed with the disposal removed; `TheNamedHttpClientSentOnItsOwnReadsItsAnswerWhole` passed whether or not the answer was buffered | fixed `093356c`: one asserts the captured handler is disposed, the other that the answer was buffered |
| F6 | minor | `DownloadFiles.Clean` | The 255 cut ran after the trailing-space and device-name rules: `NUL`, 250 spaces and `x.twbx` came out as `NUL`, spaces and `.twbx`, which Windows 10 opens as the device | fixed `093356c`: cut, then trimmed and checked for a device name, then cut again; `WhatTheCutLeavesIsTrimmedAndChecked` |
| F7 | minor | `DownloadFiles.Clean` | The cut counted UTF-16 characters; Linux and macOS allow 255 bytes of UTF-8, so a 100-character Japanese name failed at the move, after the whole download | fixed `093356c`: 255 bytes of UTF-8 on a character boundary, the extension kept; `ALongNameIsCutByItsBytesOfUtf8` |
| F8 | minor | `DownloadFiles.FileNameOf` | An empty `filename*`, or one that cleaned to nothing, hid a good `filename` | fixed `093356c`: three file-name rows |
| F9 | minor | `TableauDownload.SaveToFileAsync` | A path naming an existing folder was not refused up front: the whole file was downloaded, then the move failed. `UnauthorizedAccessException` wasn't documented | fixed `093356c`: a folder refused before anything is read, `UnauthorizedAccessException` documented; `SavingToAFolderIsRefusedBeforeAnythingIsRead` |
| F10 | minor | `TableauDownload.SaveToFileAsync` | A save that failed before reading a byte, its folder missing, still refused every later save as "read already" | fixed `093356c`: the temporary file is opened first; `ASaveThatCantOpenItsFileMayBeTriedAgain` |
| F11 | minor | `TableauPipeline.DownloadAsync` | Its comment said a download is a transfer whatever its descriptor says, but the attempt handler and the timeout read the descriptor: an export declared without `TransferKind.Download` would be buffered whole under `RequestTimeout` | fixed `093356c`: only a download descriptor is downloaded; `ACallThatIsNoDownloadIsRefusedBeforeSending` |
| F12 | minor | `DownloadTests`, `FakeTableauServer` | No offline download came without `Content-Length`, as Tableau's do, so the fake's chunked branch never ran | fixed `093356c`: the 5 MB download is chunked, `Length` null, every byte checked |
| F13 | minor | `Tableau.Api.csproj` | Microsoft.Extensions.Http.Resilience brings Microsoft.Extensions.Telemetry.Abstractions, whose build files remove the runtime's `[LoggerMessage]` generator from every app that uses the library and put their own in its place; nothing said so. The maintainer chose Polly.Core alone | fixed `093356c`, `06760f3`: Polly.Core 8.9.0 alone, run by the library's `TableauResilienceHandler`; the library's lock file from 33 packages to 17, no `Microsoft.Gen.Logging` in the dll; the docs and the plan name it |
| F14 | minor | `docs/migration-2.0.md`, CHANGELOG.md | Both said a 503 over the cap throws `RateLimited`; it throws `Http` with `RetryAfter` | fixed `06760f3` |
| F15 | minor | `docs/migration-2.0.md` | It named 1.2.x's `DownloadDataSource` as the one that gave the file's kind; it was `DownloadWorkbook`, with `WorkbookFileType` | fixed `06760f3`: checked against `v1.2.6`; `WorkbookFileType` and `main`'s `DatasourceFileType` mapped to `TableauFileKind` |

## Refuted

- **A POST, PATCH, publish or run sent twice:** only a descriptor marked idempotent is retried, tested for each.
- **A body gone on a retry:** it is a `ByteArrayContent`, sent again on each attempt; a caller's `HttpClient` with a
  resilience handler of its own gets it on all four attempts.
- **A server's wait capped at the back-off's 30 seconds:** Polly ignores `MaxDelay` for a generated delay; the cap
  is `MaxRetryAfter`.
- **`Retry-After` overflow, negative or past:** the delta parses as a non-negative int, a past date waits zero.
- **The 429 body's pattern backtracks or depends on culture:** no nested quantifiers, at most six digits, invariant.
- **A name escapes the folder through `..`, a drive, UNC, `\\?\` or `a:b`:** only the part after the last separator
  is kept, and colons and trailing dots go.
- **Look-alike slashes (U+2215, U+FF0F):** no Windows or Linux file API takes them as separators.
- **A private certificate authority through `CustomRootTrust` is a loosening:** the caller names the anchor, and the
  name and expiry checks still run.
- **`AddTableauClient` registers resilience services or telemetry:** it registers only the client, its options and
  state; the pipeline has no telemetry listener.
- **A transitive version conflicts or floats:** each is a minimum, resolved and locked; nothing resolves below a pin.

| Improvement | Answer |
|---|---|
| Compose a caller's certificate callback with the default check | left: it is replaced, as agreed on #41, and the docs say so |
| A proxy's 407 is permanent, not retried | taken `093356c`: `AProxyThatRefusesTheCredentialsIsNotRetried` |
| `AddHttpMessageHandler`'s advice: a caller's own retries only for safe methods | taken `093356c` |
| Wider bounds in the 429 wait tests | taken `093356c`: measured from the retry's log line, the logged delay asserted |
| The public constructor parses its media type | taken `093356c` |
| A read-ahead that breaks off says so | taken `093356c` |
| The download docs say up to 8 bytes are read when nothing names the kind | taken `093356c` |
| `FileName`'s doc: leading whitespace, private-use characters, the extension's length | taken `093356c` |
| `SaveToFileAsync`'s example for a download with no name | taken `093356c` |
| The plan: its sample's token argument, S10's PR and six departures | taken `06760f3` |
| Append to File Upload is a PUT that must never be retried | later: S12, in the plan (`06760f3`) |
| `DownloadAsync` refuses a descriptor that isn't a download | taken `093356c`, with F11 |
| The migration guide names the packages | taken `06760f3`, with F13: one package |

## Round 2

**Where:** PR #42 at `dd567cf`, the fixes since `f9d4f86`, read by two fresh agents that ran nothing: one took the
resilience half (Polly.Core, F1 to F5), the other the downloads and the docs (F6 to F15). **Verdict:** clean after F16
to F20, minors, fixed in `6d76ba9` and checked in the main session, with three blank lines
between members added in `709372d`.

Every round 1 fix holds, each code fix with a test that fails on a revert:
- F2's two-client test fails without the copy in both resolve orders, and the copy sets all 15 members .NET 10 and
  .NET 11 document;
- the handler on Polly.Core retries only idempotent calls, sends the body again on each attempt, disposes every
  discarded answer, returns its context to the pool on every path, and keeps the caller's cancellation apart from
  both timeouts;
- Polly.Core 8.9.0 has no dependency on net10.0, and nothing of Microsoft.Extensions.Http.Resilience is left in a
  project or lock file;
- F6 and F7 hold against the same attacks as round 1, in the new order: no separator, drive, colon, `.`, `..`, device
  name, trailing dot or space, or split character comes through.

`dotnet run eng/gate.cs -- full` failed once on `dd567cf`, in a test S9 added: F20.

| # | Sev | Where | Finding | Status |
|---|---|---|---|---|
| F16 | minor | `CallerClientHandler`, `TableauClientState.Resilience` | Two comments still described Microsoft.Extensions.Http.Resilience's design: the request's options holding the resilience context, and the pipeline holding the attempt timeout | fixed `6d76ba9`: both say what the code does now; comments only |
| F17 | minor | `AddHttpMessageHandler`, `docs/migration-2.0.md` | The new advice let a caller's own handler retry any PUT, while the plan says Append to File Upload, a PUT, is never sent twice: from S17, a chunk would be appended twice | fixed `6d76ba9`: GET and HEAD, or a call known safe to send twice, never a POST nor Append to File Upload's PUT; and a handler's own timeout must be shorter than `RequestTimeout` |
| F18 | minor | `TableauPipeline.Failure` | A DI handler's own timeout shaped like `HttpClient`'s, a `TaskCanceledException` around a `TimeoutException`, was `Timeout`, blaming the `HttpClient`'s timeout, which is infinite there; the remarks promise `Http` | fixed `6d76ba9`: `Timeout` only through a caller's `HttpClient`; the `HttpClient` row of `AHandlersOwnTimeoutOrCancellationIsHttpWithItsCause` |
| F19 | minor | `TableauDownload.SaveToFileAsync` | A save whose token was already cancelled read nothing but refused every later save as "read already" | fixed `6d76ba9`: the token is checked before anything is claimed; `ASaveCancelledBeforeItStartsLeavesTheDownloadToSave` |
| F20 | minor | `ClientFixtures.ClosedPort` | It gave back a port a listener had just freed, and a parallel test's fake server could take it: `NoAnswerIsAnHttpFailureWithItsCause` got `NotFound`, once in the gate and once in 12 runs with coverage | fixed `6d76ba9`: a socket bound to the port and never listening holds it for the run; ten runs with coverage in a row passed |

## Refuted (round 2)

- **A 429's answer is kept when the body read is cancelled:** its content was already disposed, so only memory is
  held, and the outcome is the caller's cancellation.
- **A retried answer leaks when the wait is cancelled:** Polly 8.9.0 disposes it before the wait.
- **A caller's handler's own retries defeated by the library's attempt timeout:** a handler's own timeout fires first
  at the defaults, and `RequestTimeout` can be infinite.
- **A timeout during the read-ahead reported as `Http`:** the runtime's content streams throw a cancellation, which
  `Failure` maps to `Timeout`; `TheTransferTimeoutStopsTheReadAhead`.
- **A folder appears between the check and the move:** the move fails with a documented exception, and the temporary
  file is deleted.
- **`CON` before a non-breaking space:** Windows strips only ASCII spaces in its device rule.

| Improvement | Answer |
|---|---|
| The 429 and 503 wait tests' upper bounds widened | taken `6d76ba9`: the wait and ten seconds |
| A 503 whose body names a wait is left to the back-off | taken `6d76ba9`: `A503BodysWaitIsLeftToTheBackOff` |
| A caller's handler's own timeout shorter than `RequestTimeout` | taken `6d76ba9`, with F17 |
| The dead branch in the cut dropped | taken `6d76ba9` |
| A test for the device rule's trim | taken `6d76ba9` |
| The guide links `main`'s `DownloadDataSource` to its section | taken `6d76ba9` |
| The blank lines between members restored | taken `6d76ba9`, `709372d` |
| The PR body's floor sentence | taken: the line floor stays 94, the branch floor rises to 94 |
| The TLS copy test run on a newer runtime | left: it runs on the runtime the repo targets, so a retarget runs it there |

## Live run

Tableau Server 2025.1 (REST API 3.25), read-only, by a scratch program outside the clone that printed shapes only:
- **At `f9d4f86`:** the client negotiated through the resilience chain. Two workbooks' downloads answered
  `Content-Disposition: name="tableau_workbook"; filename="<name>.twbx"`, which .NET's typed header refuses and the
  library reads, `application/octet-stream`, and no `Content-Length`. A first run signed in but read the answer as
  XML, so it could not sign out; that session ends on its own.
- **At `dd567cf`, on Polly.Core:** negotiation and Server Info through `TableauResilienceHandler`, certificates
  validated. One workbook download's headers read as before, its name and `PackagedWorkbook` kind read by the library.
  Signed in with a personal access token and signed out (204).

Round 2's fixes change nothing a server sees. Tableau Cloud wasn't run, so no real 429 was seen.

## Checks

- `dotnet run eng/gate.cs -- full` passed on `f9d4f86` (438 tests) and on `709372d` (475 tests; lines 94.74%,
  branches 94.09%, floors 94 and 94). It failed once on `dd567cf`, F20; the tests with coverage then ran twelve times,
  failing once, and ten times on `6d76ba9`, failing none.
- CI passed on `f9d4f86` (run 41234217629), `dd567cf` (run 41238905639) and `6d76ba9` (run 41241296854), Native AOT
  (Linux) included; branches 94.32% on Linux.
- A console program outside the clone, against the library and a loopback TLS server, reproduced F2 before its fix
  and showed every case reversed after it.
- `dotnet run eng/apicompat.cs` passed with the ledger unchanged, 19 breaks.
