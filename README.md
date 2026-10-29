# Tableau.Api

**Not affiliated with or endorsed by Tableau or Salesforce.**

[![CI](https://github.com/serionist/Tableau.Api/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/serionist/Tableau.Api/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/Tableau.Api)](https://www.nuget.org/packages/Tableau.Api)

A .NET client for the Tableau REST API, on Tableau Server and Tableau Cloud. 2.0 adds a client for Vizportal, the
undocumented API behind Tableau's web client, which is on `main` and was never in a 1.x release.

## Status

| Version | Where | Use it? |
|---|---|---|
| 1.2.x | [nuget.org](https://www.nuget.org/packages/Tableau.Api), netstandard2.0 (.NET Framework 4.6.1 and later, .NET Core 2.0 and later) | Yes. It takes security fixes only ([SECURITY.md](https://github.com/serionist/Tableau.Api/blob/main/SECURITY.md)) |
| 2.0 | In progress on `main`: a rebuild for .NET 10 | Not yet: `main` changes with every pull request, and none of it is on nuget.org |

## Install

```
dotnet add package Tableau.Api
```

## Contributing and security

- [CONTRIBUTING.md](https://github.com/serionist/Tableau.Api/blob/main/CONTRIBUTING.md): the short path for a pull
  request.
- [SECURITY.md](https://github.com/serionist/Tableau.Api/blob/main/SECURITY.md): report a vulnerability privately,
  never in an issue.

## Licence

[MIT](https://github.com/serionist/Tableau.Api/blob/main/LICENSE).

Tableau is a trademark of Salesforce, Inc.
