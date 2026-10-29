# Changelog

What changed in Tableau.Api 1.x for people who use it, newest first. This is the `release/1.x` branch, which 1.2.x is
released from; `main`'s changelog covers 2.0. A change users notice adds its line under Unreleased in the same PR, and
a release turns that section into its own, dated (docs/releasing.md). The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the versions
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [1.2.6] - 2026-10-29

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
- **A redirect fails** with `TableauRequestException`, where 1.2.5 followed it.
- **Any 2xx status is a success,** where 1.2.5 wanted the one status each call named.
- **A schedule time outside a day throws `ArgumentOutOfRangeException`** before anything is sent: a start or end time
  runs from 00:00:00 up to but not including 24:00:00. 1.2.5 wrapped 25:00 round to 01:00.
- **The `UpdateTo*Schedule` methods throw `NotSupportedException`** before anything is sent. Tableau updates a
  schedule by its id, which they don't take, and 1.2.5 created a second schedule instead. Delete the schedule and
  create it again; updating one comes in 2.0.
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
- **A call Tableau answers with another 2xx than 1.2.5 expected returns its result,** such as `CreateSubscription` on
  Tableau Cloud, which answers 200 where Tableau Server answers 201. 1.2.5 threw after the call had succeeded.
- **Creating a schedule returns it** on the 201 Tableau answers with, where 1.2.5 threw after Tableau had created it.
  Its start and end times go as `HH:mm:ss` with no UTC offset, and a monthly schedule sends its day from `dayToRun`,
  which 1.2.5 ignored.
- **`GetExtractRefreshTasks` returns the site's tasks,** each by the kind of item it holds, and empty arrays for a site
  with none. 1.2.5 threw in both cases.
- **`GetExtractRefreshTask` calls `tasks/extractRefreshes/{id}`,** the path Tableau documents; 1.2.5 called
  `tasks/{id}`.
- **`GetDataSourceRevisions` and `GetWorkbookRevisions` send `pageSize` and `pageNumber`,** which 1.2.5 checked and
  then left out, so every call returned the first page.
- **Responses with values Tableau has added since 1.2.5's schema parse:** `OpenID` and `TableauIDWithMFA` as a user's
  auth setting, and the values Tableau's REST API 3.29 schema lists for site roles, schedule types, project content
  permissions, job types and capability names. 1.2.5 failed the whole response on one of them. Each is added after
  the existing members, so no member's value changes.
- **`TableauSession.UserName` holds the name the session signed in with:** the user name, the token's name for a
  personal access token, and after `SwitchSite` the name of the session it switched from. 1.2.5 left it null.

### Known issues

- **On .NET Framework, apps that target a version older than 4.7.2 refuse some path values.** Those targets' URI
  parsing changes some paths after they are escaped, so a call whose path would change throws `ArgumentException`
  before anything is sent. Such a call did not reach the resource it named in 1.2.5 either. Apps that target 4.7.2 or
  later, and .NET Core and .NET 5 or later, are not affected.
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

[Unreleased]: https://github.com/serionist/Tableau.Api/commits/release/1.x
[1.2.6]: https://www.nuget.org/packages/Tableau.Api/1.2.6
[1.2.5]: https://www.nuget.org/packages/Tableau.Api/1.2.5
