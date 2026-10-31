# Tableau's REST API schema

`ts-api_3_29.xsd` is the XML schema of Tableau's REST API 3.29, the newest Tableau publishes. `eng/generate-models.cs`
generates the 2.0 models in `src/Tableau.Api/Rest/Models/Generated/` from it, and the gate checks they are current.

| What | Value |
|---|---|
| URL | https://help.tableau.com/samples/en-us/rest_api/ts-api_3_29.xsd |
| Listed on | [REST API XML Schema](https://help.tableau.com/current/api/rest_api/en-us/REST/rest_api_concepts_schema.htm) |
| Downloaded | 2026-10-31 |
| Size | 160,752 bytes |
| SHA-256 | `af214a0ba183c6bf355384a27cd9fa41e9a87d0837b96c201ed5b5473d21c3d4` |

- **Kept as published:** `.gitattributes` stores it with no line-ending conversion, CRLF included, so the SHA-256 above
  is the file's on every OS and matches a fresh download.
- **Never edited by hand.** A value Tableau sends that the schema lacks is handled by the models, not the schema: an
  enumeration is a struct that keeps any text.
- **A newer schema:** `ts-api_3_30.xsd` answered 404 on 2026-10-28 and on 2026-10-31. When it is published, download it
  here, update this table, point `SchemaFile` in `eng/generate-models.cs` at it, delete this one, run the generator and
  review the diff of the models as a change to the public API.
