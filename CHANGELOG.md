# Changelog

What changed in Tableau.Api 1.x for people who use it, newest first. This is the `release/1.x` branch, which 1.2.x is
released from; `main`'s changelog covers 2.0. A change users notice adds its line under Unreleased in the same PR, and
a release turns that section into its own, dated (docs/releasing.md). The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the versions
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Security

- **Path segments are escaped and checked before sending.** Every value a method puts in a request path, ids, tag
  names, site names and content URLs among them, goes as one path segment: each character outside RFC 3986's
  unreserved set is escaped as UTF-8 in upper-case hex, except `:`, `@`, `!`, `'`, `(`, `)`, `*` and `,`, which go
  raw, as 1.2.5 sent them. A value that is empty or only dots throws `ArgumentException` before anything is sent. So
  does a path that the platform's URI parsing would change, which some .NET Framework targets do (Known issues).

### Changed

- **A value in a path is escaped,** so a value the caller had already URL-encoded, such as a tag, a site name or a
  content URL, is now encoded twice.
- **The licence is MIT.**
- **The package's metadata:** a description that says what it is and ends "Not affiliated with or endorsed by
  Tableau or Salesforce."; the README packed into it; the project and repository links on GitHub; symbols as a
  `.snupkg`, which nuget.org's symbol server takes, with Source Link to the code on GitHub.
- **The assembly and file versions are 1.0.2.6,** after 1.2.5's 1.0.2.5.

### Fixed

- **The base URL's path is kept,** so a Tableau Server behind a path prefix, such as `https://example.com/tableau/`,
  is reachable. 1.2.5 sent every request to the host's root.

### Known issues

- **On .NET Framework, apps that target a version older than 4.7.2 refuse some path values.** Those targets' URI
  parsing changes some paths after they are escaped, so a call whose path would change throws `ArgumentException`
  before anything is sent. Such a call did not reach the resource it named in 1.2.5 either. Apps that target 4.7.2 or
  later, and .NET Core and .NET 5 or later, are not affected.

## [1.2.5] - 2021-10-13

The last release before this changelog was kept. Its changes, and those of every earlier release, are in the git
history up to 1.2.5's commit, `b23503e`.

[Unreleased]: https://github.com/serionist/Tableau.Api/commits/release/1.x
[1.2.5]: https://www.nuget.org/packages/Tableau.Api/1.2.5
