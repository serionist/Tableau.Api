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
- **`TableauClient`, 2.0's client,** beside the old `TableauApiClient`, which it takes over area by area; so far it
  has Server Info, as `client.Server.GetInfoAsync()`, which needs no sign-in.
  - **Build it** from DI with `services.AddTableauClient(name, options => ...)`, a keyed client over a named
    `HttpClient` from `IHttpClientFactory`, or the default one without a name; or with `TableauClient.Create(options)`;
    or over an `HttpClient` of your own, which it never disposes. Dispose it, with `using` or `await using`.
  - **`TableauClientOptions`:** the server's address, its path kept; the site; the REST API version, asked of the server
    on the first call unless pinned; Tableau Server or Tableau Cloud, `Auto` taking Cloud for `online.tableau.com`'s
    hosts; `AllowUnsupportedServerVersion`, `DangerousAcceptAnyServerCertificate`, `AllowInsecureHttp`; a request and a
    transfer timeout. They bind from configuration under native AOT, are checked at start-up, and can't change once a
    client is built from them.
  - **Supported:** REST API 3.23 (Tableau Server 2024.2, the oldest release Tableau supports) to 3.29, and Tableau
    Cloud. Below 3.23 every call throws unless `AllowUnsupportedServerVersion` is set; a server that offers more than
    3.29 is called at 3.29, and a version pinned in the options is called as given. A call the version or the deployment
    lacks throws before it is sent.
  - **One exception, `TableauException`,** with a `Kind` (`Http`, `Authentication`, `NotFound`, `RateLimited`,
    `Timeout`, `Redirect`, `UnsupportedApiVersion`, `UnsupportedDeployment`, `InvalidResponse`, `VizPortal`), the
    status, Tableau's code, summary and detail, the request's method and path with query values hidden, `Retry-After`,
    and the body's first 4 KB. Its constructors are public, for your tests.
  - **The connection:** certificates validated, one its issuer revoked refused; redirects followed only to the same
    scheme, host and port, at most five; no cookies kept; any 2xx a success. Handlers an app gives every
    `HttpClient` with `ConfigureHttpClientDefaults` don't reach the client's.
  - **Logging** at Debug, through `ILogger`, with no token, password, body or query value; warnings only for an option
    that weakens the client, and for a Metadata API answer whose data may be partial.
  - **Retries:** a call that is safe to send twice, such as a read, is sent again after a 408, a 429, a 5xx, a
    connection that failed or an attempt longer than `RequestTimeout`: `MaxRetries` times, three by default, 0 for
    none, with jittered back-off. A publish, a create or a run is sent once. After a 429 or a 503 the client waits as
    the server asks, by `Retry-After` or the wait a 429's body names, up to `MaxRetryAfter`, two minutes by default; a
    longer wait fails at once, a 429 as `RateLimited` and a 503 as `Http`, each with `RetryAfter`. The same retries
    run from DI, from `TableauClient.Create` and over an `HttpClient` of your own, on Polly.Core from a handler of the
    library's own; no Polly type is in the API.
  - **Timeouts:** `RequestTimeout` limits each attempt of a call, its answer read whole within it;
    `TransferTimeout` limits a download or an upload as a whole, reading the file included.
  - **The builder** `AddTableauClient` returns has `ConfigurePrimaryHandler`, for a proxy, a client certificate or a
    private certificate authority, after which the client's rules are set again (no redirects, no cookies, the
    certificate check); and `AddHttpMessageHandler`, for handlers of your own, outside the retries. A second
    `AddTableauClient` for a name keeps what the first added.
  - **`TableauDownload`,** for the downloads that come with their areas: the file streams from the server as it is
    read, under the call's token and `TransferTimeout`; `FileName` is cleaned to a name alone, read from Tableau's
    `Content-Disposition`; `ContentType`, `Length` and `Kind` (`TableauFileKind`: a workbook, a data source, an
    extract, a flow, packaged or not, a PDF, a PNG, a CSV, a zip) say what it is; `SaveToFileAsync` writes it, leaving
    no part of a file if it fails and replacing a file only when asked.
  - The client and its sub-clients are classes with virtual members and protected constructors, for mocks.
  - **Sessions:** give the client `TableauCredentials` (`TableauClient.Create`, its constructor over an `HttpClient`, or
    `AddTableauClient(...).UseCredentials`) and it signs in on the first call that needs a session, one sign-in at a
    time, signs in again five minutes before the session's 240 minutes on Tableau Server or 120 on Tableau Cloud are
    up, and once after a 401, sending the refused call again, and signs out when you dispose it. A renewal that fails
    leaves the call in the session it has until the session ends. A client from DI shares its name's session and signs
    nothing out.
  - **Credentials:** `PersonalAccessToken` and `Password`, each optionally impersonating a user on Tableau Server, a
    token's from Tableau Server 2021.1; `ConnectedAppDirectTrust`, which mints the connected app's JWT at each sign-in
    with HMAC-SHA256, valid five minutes; `Jwt`, for a JWT minted elsewhere; `UnifiedAccessToken`, Tableau Cloud's from
    REST API 3.27. Each secret can come from a delegate, such as a vault's, called at each sign-in. Impersonation on
    Tableau Cloud and a unified access token on Tableau Server are refused before anything is sent, and an impersonating
    sign-in that comes back as another user is signed out and refused. A JWT's sign-in is never sent twice. `ToString`
    names the kind and the user, never a secret.
  - **`client.Sessions`** signs in a session you hold, switches it to another site (Tableau Server only) and signs it
    out; `client.WithSession(session)` is a client that calls in it and never signs in, so one per site serves work
    across sites. `TableauSession` is a record with the site, the user, when it ends, the deployment and the version;
    its token is never printed. A sign-in's `TableauException` keeps none of Tableau's answer.
  - A session token that an `HttpClient` of yours sent to another host, by following a redirect, is signed out.
  - **Sites, projects, users, groups and favorites:** `client.Sites` gets a site by its id, content URL or name, lists
    the sites, and creates, updates and deletes one, creating, deleting and listing on Tableau Server alone;
    `client.Projects` lists, creates (with Tableau's samples if asked), updates and deletes projects; `client.Users`
    gets, lists, adds, updates and removes users, giving what they own to another, and lists a user's groups; Update
    User takes every attribute the reference lists, the email, the identity provider, the language and the locale
    among them. `client.Groups` lists, creates, updates and deletes local groups, imports and synchronises Active
    Directory groups on Tableau Server, at once or as a job, to the group's own path, and adds, removes and lists a
    group's users; `client.Favorites` lists a user's favorites and adds and removes a workbook, a view, a data source,
    a project or a flow, each a `TableauContent`. Creating a site, a project or a group, adding a user to the site,
    importing or synchronising an Active Directory group, at once or as a job, and deleting a site are sent once; an
    update, another delete, adding a user to a group and a favorite may be sent again after a failure, as a read is.
  - **Lists:** `ListAsync` reads every page lazily, in an `await foreach`, and stops at Tableau's total or on a short
    page, never asking for a page past the end, which Tableau answers with an error; `ListPageAsync` returns one
    `Page<T>`. A list is never null. Options records (`ProjectListOptions`, `UserListOptions`, `GroupListOptions`,
    `SiteListOptions`) hold a page size, 100 by default and 1,000 at most, and a filter and a sort: `TableauFilter`
    (`Eq`, `In`, `Gt`, `Gte`, `Lt`, `Lte`, `Has`, `CiEq`, joined by `And`, or `FromString` for an expression written by
    hand) and `TableauSort` (`Asc`, `Desc`, `Then`) over the reference's fields (`ProjectField`, `UserField`,
    `GroupField`). Values are escaped on the wire, `&`, `+`, `#` and `%` among them; a value with a comma, which Tableau
    can't take, and a field of another resource are refused before anything is sent. `Eq` and `In` take a `SiteRole`
    too, such as `SiteRole.Viewer`. A list you don't sort comes in an order that doesn't change from page to page:
    projects oldest first, then by name; users by name, then on Tableau Server by domain; groups by name. A sort you
    give is followed by those of the default fields it doesn't name, to break its ties. A server that answers an
    earlier page than the one asked for fails the list with `InvalidResponse`, where reading on would repeat it.
  - **`TableauContent`** names one piece of content by its kind (`TableauContentKind`) and LUID, such as
    `TableauContent.Workbook(id)` or `TableauContent.Column(tableId, columnId)`, for favorites, permissions, tags and data
    quality warnings; two that name the same content are equal, with `==` too. Each call takes the kinds the reference
    gives it and refuses another before anything is sent.
  - **Permissions:** `client.Permissions` lists, adds and deletes permissions on projects, workbooks, views, data
    sources, flows, virtual connections, collections, databases and tables, each kind from its own REST API version,
    and a project's default permissions for workbooks, data sources, data roles, flows, virtual connections, databases
    and tables. A rule is a `GranteeCapabilities` naming its user, group or group set by id; a delete names its grantee
    with `PermissionGrantee.User(id)`, `.Group(id)` or `.GroupSet(id)`, and its capability and mode with
    `CapabilityName` and `CapabilityMode`. An add names the content beside its rules where the reference's body does.
  - **Tags:** `client.Tags` adds and deletes tags on workbooks, views, data sources, virtual connections, databases,
    tables and columns, a tag escaped as one segment of the path, and in a batch over databases, tables, columns, data
    sources, workbooks and flows, Batch Delete Tags a DELETE with its body, as the reference documents it, each content
    named by its type and LUID, as Tableau Server 2025.1 asks. In a body, a tag with whitespace or a comma goes in
    double quotes, since Tableau splits an unquoted one there into several.
  - **Metadata:** `client.Metadata` lists Tableau Catalog's databases and tables page by page, gets, updates and
    deletes one, lists a table's columns (one call of up to 1,000, as Tableau Server 2025.1 refuses their second page),
    updates and deletes a column, and adds, updates, lists and deletes data quality warnings, by id or by content.
    `Column`, which Tableau's schema lacks, is written by hand in `Tableau.Api.Rest.Models`. An update sends the
    members the reference lists and no others. A column list whose answer counts more columns than it holds fails.
    Adding a data quality warning is sent once; every other call of the three may be sent again after a failure.
  - **The Metadata API:** `client.Metadata.QueryAsync` posts a GraphQL query to `api/metadata/graphql` in the client's
    session and returns its `data` as JSON, or through a `JsonTypeInfo<T>` of your own, so it runs under native AOT;
    `ListNodesAsync` reads a connection page by page by its `pageInfo`'s `endCursor` until `hasNextPage` is false, and
    `QueryPageAsync` returns one `CursorPage<T>`. An `errors` entry Tableau marks a warning, such as
    `BACKFILL_RUNNING` or `NODE_LIMIT_EXCEEDED`, which comes with partial data, is logged at Warning and the data
    returned; any other throws `TableauException`, the first entry that isn't a warning giving `ErrorCode` and
    `Summary`, `RateLimited` for `RATE_EXCEEDED` whatever the status; warnings with no data throw too, under the
    first warning's code; a 403 says the Metadata API may be off.
  - **Workbooks, views and data sources:** `client.Workbooks` lists the site's workbooks and a user's, gets, updates
    and deletes one, lists and updates its connections, runs its extract refresh, and downloads its file, with or
    without its extract, its preview image and its PDF; `client.Views` lists the site's views, with their usage if
    asked, and a workbook's, gets one, and exports one as data (CSV), an image (PNG), a PDF or a crosstab (an .xlsx),
    and downloads its preview image; `client.DataSources` lists, gets, updates and deletes published data sources,
    lists and updates their connections, runs their extract refresh and downloads their file. Workbooks and data
    sources list, download and remove their revisions, a revision list sending its page size and number. Lists filter
    and sort over `WorkbookField`, `ViewField` and `DataSourceField`, and come oldest first, then by name, a view by
    its workbook's name first.
  - **`ExportOptions`** holds every export's options: view filters by field name, sent as `vf_` and the name on every
    export; how many minutes old a cached export may be; an image's resolution (`ImageResolution`); a width and a
    height; a PDF's page (`PageType`) and orientation (`PageOrientation`). An export refuses an option it doesn't take,
    and one below its REST API version, before anything is sent, such as a workbook PDF's size below 3.26.
  - **Updates** take `WorkbookUpdate`, `DataSourceUpdate` and `ConnectionUpdate`, records of the reference's members
    for each, and send what is set and no element for what isn't. A `ConnectionUpdate` prints its password as `***`.
  - Every file comes as a `TableauDownload`, streamed. An extract refresh is sent once; every other call of the three
    may be sent again after a failure, as a read is.
- **The library builds with the trim and native AOT analyzers on** (`IsAotCompatible`), and the models pass them: they
  read and write JSON through System.Text.Json's source generator, with no reflection, and CI runs them in a native
  AOT app. The 1.x client is exempt: its `XmlSerializer` and Newtonsoft.Json code doesn't work trimmed or under native
  AOT, and goes before 2.0.0.

### Changed

- **The package targets net10.0 only,** where `main` targeted netstandard2.1 and 1.2.x targets netstandard2.0. On
  .NET Framework or an older .NET, stay on 1.2.x.
- **The version is 2.0,** from git tags: a build of `main` is `2.0.0-alpha.0.<n>`, and nothing is published before
  2.0.0's release candidate. Every break of 1.2.5's public API is in the migration guide; so far, five methods whose
  parameters or return type `main` had changed, the `capabilityTypeName` members renumbered by the capabilities added
  among them, and the old client's Server Info, sign-in, sites, projects, users, groups, favorites, permissions,
  metadata, tag, workbook, view, data source and revision methods.
- **Values in paths and queries are escaped,** so a value the caller had already URL-encoded is encoded twice.
- **The base URL's path is used,** by the REST and the Vizportal clients alike, so a server behind a path prefix,
  such as `https://example.com/tableau/`, is reachable. A base URL must be the server's address, not a site's or a
  view's link.
- **A redirect fails,** where it was followed: use the address the exception names as the base URL.
- **`TableauApiClient.ServerInfo` is gone:** use `TableauClient.Server.GetInfoAsync`, which returns the 2.0 model
  `ServerInfo` (the migration guide).
- **`TableauApiClient`'s `SignIn`, `SignInWithPAT`, `SignOut` and `SwitchSite` are gone:** give `TableauClient`
  credentials, or use `client.Sessions` (the migration guide). Until an area moves to `TableauClient`, its old methods,
  and Vizportal's, can't get a session from the library on `main`; 2.0.0 ships with every area moved.
- **`TableauApiClient`'s sites, projects, users, groups and favorites methods are gone,** `DeleteProject2` with them:
  use `client.Sites`, `client.Projects`, `client.Users`, `client.Groups` and `client.Favorites` (the migration guide
  maps each).
- **`TableauApiClient`'s workbooks, views, data sources and revisions methods are gone,** `QueryViewsForSite` with
  them, and the `Orientation` and `TableauImageWidth` enumerations: use `client.Workbooks`, `client.Views` and
  `client.DataSources` (the migration guide maps each). A view's data now keeps its filters' `vf_` prefix, which 1.2.5
  stripped, and a view's PDF works without filters.
- **The package depends on Microsoft.Extensions' abstractions,** 10.0.14: Logging.Abstractions, Options,
  DependencyInjection.Abstractions and Http, in place of Microsoft.Extensions.Logging 2.2.0; and on Polly.Core 8.9.0,
  for the retries, which brings no other package.
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
