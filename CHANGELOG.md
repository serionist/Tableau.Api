# Changelog

What changed for people who use Tableau.Api, newest first. A change users notice adds its line under Unreleased in the
same PR. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the versions
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

2.0.0 is in progress on `main`: a rebuild for .NET 10, not for use yet. It targets net10.0 only; .NET Framework and
older .NET stay on 1.2.x. The changes on `main` since 1.2.5, the Vizportal client among them, reach users in 2.0.0,
and [the migration guide](docs/migration-2.0.md) lists every change from 1.2.5 a caller has to act on.

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

### Added

- **2.0's models of Tableau's REST API,** in `Tableau.Api.Rest.Models`, generated from Tableau's newest schema, REST
  API 3.29: 197 records, such as `Workbook` for the schema's `workbookType`, and 50 enumerations, ahead of the client
  that will return and take them. The 1.x types in `Tableau.Api.Models` stay until the client that uses them is
  replaced.
  - Every member is nullable and has an `init` setter, under the schema's name in JSON.
  - A list is an `IReadOnlyList<T>`, read from Tableau's wrapper object, such as `"tags":{"tag":[...]}`; an empty
    wrapper reads as an empty list, and a missing one as null.
  - An enumeration is a struct that keeps its text, such as `SiteRole`, so a value Tableau adds later reads instead of
    failing the response; the values the schema knows are its static properties, such as `SiteRole.Creator`.
  - A number Tableau sends as a string reads as a number.
  - A record's `ToString` prints a password, a token or a secret as `***`, such as `Credentials.Password`.
- **The library builds with the trim and native AOT analyzers on** (`IsAotCompatible`), and the models pass them: they
  read and write JSON through System.Text.Json's source generator, with no reflection, and CI runs them in a native
  AOT app. The 1.x client is exempt: its `XmlSerializer` and Newtonsoft.Json code doesn't work trimmed or under native
  AOT, and goes before 2.0.0.

### Changed

- **The package targets net10.0 only,** where `main` targeted netstandard2.1 and 1.2.x targets netstandard2.0. On
  .NET Framework or an older .NET, stay on 1.2.x.
- **The version is 2.0,** from git tags: a build of `main` is `2.0.0-alpha.0.<n>`, and nothing is published before
  2.0.0's release candidate. Every break of 1.2.5's public API is in the migration guide; so far, six methods whose
  parameters or return type `main` had changed, and the `capabilityTypeName` members renumbered by the capabilities
  added among them.
- **Values in paths and queries are escaped,** so a value the caller had already URL-encoded is encoded twice.
- **The base URL's path is used,** by the REST and the Vizportal clients alike, so a server behind a path prefix,
  such as `https://example.com/tableau/`, is reachable. A base URL must be the server's address, not a site's or a
  view's link.
- **A redirect fails,** where it was followed: use the address the exception names as the base URL.
- **The licence is MIT.**
- **The package's metadata:** a description that says what it is and ends "Not affiliated with or endorsed by
  Tableau or Salesforce."; the README packed into it; the project and repository links on GitHub; symbols as a
  `.snupkg`, which nuget.org's symbol server takes, with Source Link to the code on GitHub.

### Fixed

- **Vizportal's `QueryDatasources` reads data quality warnings:** `Datasource.DataQualityIndicators` is a
  `DataQualityIndicator[]`, each with its id, category, type, message, owner, whether it is active and severe, and
  when it was updated. It was a `string[]`, so a site with a warning on a published data source failed the whole
  query with `JsonReaderException`, as on Tableau Server 2025.1.5.
- **Vizportal's `QuerySubscriptionsForSchedule` reads a subscription to a flow:** `TargetType` gains `Flow`, after its
  existing members. Without it, a schedule holding such a subscription failed with `JsonSerializationException`.
- **Requests read the same in every culture:** numbers are written in the invariant culture, and a download's media
  type and a view filter's `vf_` prefix are compared ordinally. In a Swedish culture, `-1` for a site's capacities,
  storage quota or revision limit went as `−1` (U+2212 MINUS SIGN), which is no number; in a Turkish one, a download
  whose media type came in capitals read as packaged.

## [1.2.6] - 2026-10-30

### Security

- **Path segments are escaped and checked before sending.** Every value a method puts in a request path, ids, tag
  names, site names and content URLs among them, goes as one path segment: each character outside RFC 3986's
  unreserved set is escaped as UTF-8 in upper-case hex, except `:`, `@`, `!`, `'`, `(`, `)`, `*` and `,`, which go
  raw, as 1.2.5 sent them. A value that is empty or only dots throws `ArgumentException` before anything is sent. So
  does a path that the platform's URI parsing would change, which some .NET Framework targets do (Known issues).
- **Redirects are not followed,** so credentials, the session token and request bodies never go to another address.
  A 3xx response throws `TableauRequestException` with its status, and its message names the scheme, host and port
  the redirect pointed to. The session token goes on each request, not on the HTTP client's default headers.

### Changed

- **Values in paths and queries are escaped,** so a value the caller had already URL-encoded is now encoded twice,
  whether a filter or a path value: a tag, a site name, a content URL.
- **A path value made only of dots is refused** on every platform, three dots or more included, which 1.2.5 sent as
  written. 1.2.5 never sent `.` or `..` to the resource they named.
- **The base URL's path is used.** 1.2.5 ignored it, so a base URL holding a site or view link, such as
  `https://tableau.example.com/t/finance/`, or the API's path, such as `https://tableau.example.com/api/`, worked
  there. Now the base URL must be the server's address: `https://tableau.example.com/`.
- **A redirect fails** with `TableauRequestException`, where 1.2.5 followed it. A server that redirects `http` to
  `https`, or one host name to another, now fails every call: use the address the message names as the base URL.
  1.2.5 followed a 301 or 302 on sign-in by sending a GET in place of the POST.
- **Any 2xx status is a success,** where 1.2.5 wanted the one status each call named.
- **A schedule time outside a day throws `ArgumentOutOfRangeException`** before anything is sent: a start or end time
  runs from 00:00:00 up to but not including 24:00:00. 1.2.5 wrapped 25:00 round to 01:00.
- **The `UpdateTo*Schedule` methods throw `NotSupportedException`** before anything is sent. Tableau updates a
  schedule by its id, which they don't take, and 1.2.5 created a second schedule instead. Delete the schedule, create
  it again, then add its tasks to it again; updating one comes in 2.0.
- **The licence is MIT.**
- **The package's metadata:** a description that says what it is and ends "Not affiliated with or endorsed by
  Tableau or Salesforce."; the README packed into it; the project and repository links on GitHub; symbols as a
  `.snupkg`, which nuget.org's symbol server takes, with Source Link to the code on GitHub.
- **The assembly and file versions are 1.0.2.6,** after 1.2.5's 1.0.2.5.

### Fixed

- **The base URL's path is kept,** so a Tableau Server behind a path prefix, such as `https://example.com/tableau/`,
  is reachable. 1.2.5 sent every request to the host's root.
- **Every query key and value is escaped,** so `&`, `#`, `+` and `%` in a filter, a sort or a view filter reach
  Tableau as written. In `filter` and `sort` expressions, `:` and `,` stay as the separators, and the parts between
  them are escaped.
- **View filters apply to view data and view images:** each name gets the `vf_` prefix unless it has it, as on PDFs.
  1.2.5 took the prefix off, so Tableau did not read them as view filters.
- **`QueryViewPDF` with no filters** sends the request; 1.2.5 threw `ArgumentNullException`.
- **A call Tableau answers with another 2xx than 1.2.5 expected returns its result,** where 1.2.5 threw after the call
  had succeeded. Creating a schedule, below, is one.
- **Creating a schedule returns it** on the 201 Tableau answers with, where 1.2.5 threw after Tableau had created it.
  Its start and end times go as `HH:mm:ss` with no UTC offset, which Tableau applies in the server's time zone, and a
  monthly schedule sends its day from `dayToRun`, which 1.2.5 ignored.
- **`GetExtractRefreshTasks` returns the site's tasks,** each by the kind of item it holds, and empty arrays for a site
  with none. 1.2.5 threw in both cases.
- **`GetExtractRefreshTask` calls `tasks/extractRefreshes/{id}`,** the path Tableau documents; 1.2.5 called
  `tasks/{id}`.
- **`GetDataSourceRevisions` and `GetWorkbookRevisions` send `pageSize` and `pageNumber`,** which 1.2.5 checked and
  then left out, so every call returned the first page.
- **Responses with values Tableau has added since 1.2.5's schema parse:** `OpenID` and `TableauIDWithMFA` as a user's
  auth setting; a task interval of 24 hours or 60 minutes, which Tableau Cloud sends; and the values Tableau's REST
  API 3.29 schema lists for site roles, schedule types, project content permissions, job types and capability names.
  1.2.5 failed the whole response on one of them, such as a Tableau Cloud task list with a task that runs once a
  day. Each is added after the existing members, so no member's value changes.
- **`TableauSession.UserName` holds the name the session signed in with:** the user name, the token's name for a
  personal access token, and after `SwitchSite` the name of the session it switched from. 1.2.5 left it null.

### Known issues

- **On .NET Framework, apps that target a version older than 4.7.2 refuse some path values.** Those targets' URI
  parsing changes some paths after they are escaped, so a call whose path, the base URL's path included, would change
  throws `ArgumentException` before anything is sent. Such a call did not reach the resource it named in 1.2.5
  either. Apps that target 4.7.2 or later, and .NET Core and .NET 5 or later, are not affected.
- **On .NET Framework, apps that target 4.5 to 4.7.1 send an escaped colon in a query unescaped** when the request URI
  holds a non-ASCII character, as 1.2.5 did. The same goes for `!`, `'`, `(`, `)` and `*`; commas stay escaped. So a
  colon in a view filter's value reaches Tableau as a plain colon when the URI also holds an accented letter. Apps
  that target 4.0, or 4.7.2 or later, and .NET Core and .NET 5 or later, are not affected.
- **Left for 2.0, since each fix needs a new signature:** `UpdateGroupFromActiveDirectory` and
  `UpdateGroupFromActiveDirectoryAsJob` take no group id and send their update to the group list, not to a group;
  `AddWorkbookToSchedule` returns null; `QueryDatabases` and `QueryTables` return the first page only.

## [1.2.5] - 2021-10-13

The last release before this changelog was kept. Its changes, and those of every earlier release, are in the git
history up to 1.2.5's commit, `b23503e`.

[Unreleased]: https://github.com/serionist/Tableau.Api/commits/main
[1.2.6]: https://www.nuget.org/packages/Tableau.Api/1.2.6
[1.2.5]: https://www.nuget.org/packages/Tableau.Api/1.2.5
