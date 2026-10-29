# Tableau.Api 1.x

**Not affiliated with or endorsed by Tableau or Salesforce.**

[![CI](https://github.com/serionist/Tableau.Api/actions/workflows/ci.yml/badge.svg?branch=release/1.x)](https://github.com/serionist/Tableau.Api/actions/workflows/ci.yml?query=branch%3Arelease%2F1.x)
[![NuGet](https://img.shields.io/nuget/v/Tableau.Api)](https://www.nuget.org/packages/Tableau.Api)

A .NET client for the Tableau REST API, on Tableau Server and Tableau Cloud, for netstandard2.0: .NET Framework 4.6.1
and later, .NET Core 2.0 and later.

## Status

| Version | Where | Use it? |
|---|---|---|
| 1.2.x | [nuget.org](https://www.nuget.org/packages/Tableau.Api), built from the `release/1.x` branch | Yes. It takes security fixes only ([SECURITY.md](https://github.com/serionist/Tableau.Api/blob/release/1.x/SECURITY.md)) |
| 2.0 | In progress on `main`: a rebuild for .NET 10 | Not yet: none of it is on nuget.org |

## Install

```
dotnet add package Tableau.Api
```

## Use

```csharp
using Tableau.Api;

var client = new TableauApiClient(new Uri("https://tableau.example.com"), "3.4");
var session = await client.SignIn("user name", "password", "site-content-url");
var (pagination, projects) = await client.QueryProjects(session);
await client.SignOut(session);
```

Each method takes the session it acts for. Pass the REST API version your Tableau Server or Tableau Cloud site
supports; each method's XML documentation says which version it needs.

## Changes and security

- [CHANGELOG.md](https://github.com/serionist/Tableau.Api/blob/release/1.x/CHANGELOG.md): what changed in each 1.x
  release.
- [SECURITY.md](https://github.com/serionist/Tableau.Api/blob/release/1.x/SECURITY.md): report a vulnerability
  privately, never in an issue.

## Licence

[MIT](https://github.com/serionist/Tableau.Api/blob/release/1.x/LICENSE).

Tableau is a trademark of Salesforce, Inc.
