---
subject: "Generate 2.0's REST models from Tableau's 3.29 schema and run them under native AOT (#38)"
base: main 4ba9b69
head: 6de5397
date: 2026-10-31
round: 1
tier: three fresh reviewers split by scope (the generator, gate, CI and AOT; the models against the schema; the tests, fixtures and docs), reading only; the fixes checked by the main session
verdict: clean
---

# Review: models, generator and AOT (#38)

Three fresh agents read the change at `6de5397` and ran nothing. None found a blocker or a major. All eight minors are
fixed in `e4bfddf`, `5861dc4`, `ef63c7b` and `86105bb`.

- **The generator, gate, CI and AOT reviewer:**
  - the output is deterministic: ordinal order, LF, no BOM, invariant casing, and the hash header;
  - every construct it doesn't map stops it, except a second level of extension (F1);
  - `--check` compares bytes, but misses subfolders (F2);
  - the gate runs the repo's own generator, and the selftest compares a scratch run with the committed folder;
  - the AOT job fails on any warning, `CI` needs it, and it reads no secret;
  - the ILLink pin survives an SDK patch;
  - the schema's SHA-256 matches the README.
- **The models reviewer:**
  - the list and enumeration converters are right for every JSON shape the live check saw: wrapper arrays, a single object, `{}`, integers as strings, `value` for text, the 404's `{"error":{...}}`;
  - so are `xs:choice`, `xs:extension` and the merged flow parameter domain;
  - it listed the departures from #37's words that the PR hadn't named.
- **The tests and docs reviewer:**
  - every plan S8 test is there, and none passes vacuously;
  - the fixtures hold no data from the live server;
  - the round trip compares with the fixture, not with itself.

| # | Sev | Where | Finding | Status |
|---|---|---|---|---|
| F1 | minor | `eng/generate-models.cs`, `Reader.Read` | Only a base's direct subtypes are merged, so a later schema's `c extends b extends a` would drop `c`'s members without an error, against "a new schema can't drop a member silently". 3.29 has one level only. | fixed `e4bfddf`: a subtype extended in turn stops the generator; selftest refusal case in `5861dc4` |
| F2 | minor | `eng/generate-models.cs`, `--check` | It listed only top-level files, so a hand-made `Generated/Partials/X.cs` compiled and passed the check | fixed `e4bfddf`: a folder in the output fails the check; selftest case in `5861dc4` |
| F3 | minor | `ModelRootsTests`, `OldPublicTypes.Most` | The "only shrinks" test asserted count ≤ `Most`, so after S12 cut 20 types a new 2.0 type could join the old list instead of `roots.xml`, escaping the AOT analysis | fixed `e4bfddf` and `5861dc4`: `Most` equals the count, and the gate's new quick-lane `old types` check refuses a `Most` above the base's; 7 selftest cases |
| F4 | minor | AGENTS.md, the plan | AGENTS.md reached the 150-line cap from 148, against the plan's rule that a step adding lines removes as many; S7's log said 146 lines, its squash had 148 | fixed `ef63c7b`: 148 lines, two additions folded in; S7's note corrected |
| F5 | minor | the generated records, `ToString` | A record printed its password, token or secret: `logger.LogDebug("{C}", response.Credentials)` would log the session token, against REVIEW.md's credentials rule and the plan's rule for `TableauSession` | fixed `e4bfddf`: one list of 13 (type, attribute) pairs, a generated `PrintMembers` printing them as `***`, a stale pair stops the generator; `ModelPrintTests` checks each, and fails on any string member named like a secret on neither list |
| F6 | minor | `tableauCredentialsType`, plan S11 | The generated `TableauCredentials` would clash (CS0104) with S11's public `Tableau.Api.TableauCredentials` in a file importing both namespaces | fixed `e4bfddf`: the model is `Credentials`; `NoModelSharesItsNameWithAnotherPublicType` fails on any such clash outside the old client's types |
| F7 | minor | `DataSourceList`, `ConnectorList`, `SalesforceAppList`, `ServiceTokenList` | A list type with attributes reads `{}` as a record whose list is null, where a plain wrapper reads `[]`, unlike the PR's stated rule | fixed `e4bfddf` by documenting it: reading `[]` would write an empty list back, and init members can't be filled after reading without hand-made backing fields. The doc comment and the plan say so, and a test pins it |
| F8 | minor | `TableauEnumConverter<T>.Write` | A default enumeration struct wrote `"siteRole":null`, past `WhenWritingNull` | fixed `e4bfddf`: it throws a `JsonException` naming the type; tested |

## Refuted

- **The output isn't deterministic:** order, line endings, encoding, casing and the hash are all fixed, and the Windows selftest compares a fresh run with the committed files.
- **`--check` passes on a stale, missing or edited file:** it compares bytes and names missing and foreign files; only folders escaped (F2).
- **Name clashes or C# keywords:** one case-insensitive table for types, members and values; imported and reserved names refused.
- **The gate's check can be fooled:** it runs the repo's generator by absolute path and fails on a generator that doesn't compile.
- **The ILLink pin breaks on an SDK patch:** the lock file names 10.0.14 either way.
- **The AOT job misses warnings or holds secrets:** `IlcTreatWarningsAsErrors`, `contents: read`, no secret read.
- **The `obj/` exclusion hides hand-written code:** the SDK compiles nothing from `obj/` by default; the pattern is now the library's own `obj/` only.
- **The list converter mishandles null, `{}`, unknown siblings or nesting:** pinned by tests; malformed JSON throws in the reader.
- **An enumeration of numbers fails on `"24"`:** it is `long?`, read from strings.
- **`xs:choice`, `xs:extension` or the flow parameter domain lose data:** a JSON name with two shapes stops the generator.
- **`JsonEquivalence` hides a number typed wrongly:** every numeric-looking fixture field checked against the xsd.
- **Fixture data from the live server:** invented LUIDs, tokens, `example.org` and `tableau.example.com` only.

| Improvement | Answer |
|---|---|
| Name every departure from #37's words in the PR's door list | taken: the PR names ten; the maintainer agreed to them on #37 |
| One casing rule for acronyms in types, members and values | taken `e4bfddf`: 13 names, no new collisions; the three case-only file renames fixed in git's index, and the gate catches one, in `86105bb` |
| Don't emit the five unreachable flow parameter domain subtypes | taken `e4bfddf`: 197 records |
| The generator checks the README's SHA-256 | taken `e4bfddf` |
| A selftest table of refusals | taken `5861dc4` |
| Narrow the coverage pattern to the library's `obj/` | taken `e4bfddf`, with the maintainer's agreement to the exclusion on #38 |
| AGENTS.md: the schema kept byte for byte, the solution holds the AOT app | taken `ef63c7b` |
| The tests lens checks a model change by fixture, manifest, counts and roots | taken `ef63c7b` |
| Confirm on a site that Tableau takes a JSON number for an `xs:integer` in a request | later: S12, in the plan |
| A caller's own source-generated context and the internal converters (SYSLIB1220) | later: S9, in the plan |
| Lock the AOT app's packages | later: S17, in the plan |

## Live run

Tableau Server 2025.1, REST API 3.25, with a personal access token: sign-in, about 20 read-only GETs and sign-out, by a
scratch program outside the clone that printed shapes only. It confirmed the JSON rules the models follow (above);
the fixtures stay synthetic. Tableau Cloud wasn't run, and no request but sign-in was sent.

## Checks

- `dotnet run eng/gate.cs -- full` passed on `6de5397` (146 tests; lines 92.57%, branches 89.11%) and on `86105bb`
  (154 tests, 416 selftest cases; lines 92.88%, branches 89.65%).
- CI passed on `86105bb`, Native AOT (Linux) included: runs 41210696087 (dispatch) and 41210699675 (the PR).
- The first dispatch on `ef63c7b` failed the models check on Linux: three files renamed only by case kept their old
  names in git's index. Fixed in `86105bb`.
