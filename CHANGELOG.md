# Changelog

What changed for people who use Tableau.Api, newest first. A change users notice adds its line under Unreleased in the
same PR. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the versions
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

2.0.0 is in progress on `main`: a rebuild for .NET 10, not for use yet. It targets net10.0 only; .NET Framework and
older .NET stay on 1.2.x. The changes on `main` since 1.2.5, the Vizportal client among them, reach users in 2.0.0,
and its migration guide will list every change from 1.2.5.

### Security

- **The Vizportal client validates server certificates as the REST client does:** only `IgnoreSSLErrors` turns the
  check off. Both clients build their HTTP handler in one place, and neither follows a redirect.
- **Request paths, queries and redirects follow 1.2.6's rules,** on every method `main` has, those 1.2.5 lacks among
  them:
  - each value in a request path is escaped and sent as one path segment, and a value that is empty or only dots
    throws `ArgumentException` before anything is sent. The Vizportal method name that `PaginateRequest` and
    `NextPageTokenRequest` take is such a value too;
  - every query key and value is escaped; filter and sort expressions keep their `:` and `,` separators, and the parts
    between them are escaped;
  - a redirect is not followed: a 3xx response throws `TableauRequestException`, whose message names the scheme, host
    and port it pointed to;
  - the session token goes on each request, not on the HTTP client's default headers.

### Changed

- **Values in paths and queries are escaped,** so a value the caller had already URL-encoded is encoded twice.
- **The base URL's path is used,** by the REST and the Vizportal clients alike, so a server behind a path prefix,
  such as `https://example.com/tableau/`, is reachable. A base URL must be the server's address, not a site's or a
  view's link.
- **A redirect fails,** where it was followed: use the address the exception names as the base URL.
- **The licence is MIT.**
- **The package's metadata:** a description that says what it is and ends "Not affiliated with or endorsed by
  Tableau or Salesforce."; the README packed into it; the project and repository links on GitHub; symbols as a
  `.snupkg`, which nuget.org's symbol server takes, with Source Link to the code on GitHub.

## [1.2.5] - 2021-10-13

The last release before this changelog was kept. Its changes, and those of every earlier release, are in the git
history up to 1.2.5's commit, `b23503e`.

[Unreleased]: https://github.com/serionist/Tableau.Api/commits/main
[1.2.5]: https://www.nuget.org/packages/Tableau.Api/1.2.5
