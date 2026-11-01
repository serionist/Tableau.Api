# Moving from 1.2.x to 2.0

2.0 is a rebuild of Tableau.Api for .NET 10, in progress on `main` and not on nuget.org yet. This guide lists every
change from 1.2.5 that a caller has to act on, and grows with each change that makes one. Stay on 1.2.x until 2.0.0
is released: it takes security fixes ([SECURITY.md](../SECURITY.md)).

**Where the list comes from:** `eng/apicompat/1.2.5.xml`, the ledger of every break in the public API, which CI
compares on every pull request: the ApiCompat tool reads 1.2.5's DLL from nuget.org and the library's build, and fails
on a break the ledger doesn't list. Each section below groups the ledger's entries, with how to move over.

| Area | Change | Ledger entries |
|---|---|---|
| [Target](#target-net100-only) | net10.0 only, where 1.2.x is netstandard2.0 | none: package validation's one suppression, PKV006 |
| [Version](#version) | 2.0.0, from git tags | none |
| [Signatures](#five-methods-whose-signature-changed) | five methods whose parameters or return type changed | 5, `CP0002` |
| [Permission capabilities](#permission-capabilities-renumbered) | `capabilityTypeName` members renumbered | 12, `CP0011` |
| [The 2.0 client](#the-20-client) | `TableauClient` takes over `TableauApiClient`'s calls area by area, Server Info first | 1, `CP0002` |
| [Sessions](#sessions) | `TableauApiClient`'s sign-in methods gone: the 2.0 client signs itself in, or you sign in through `client.Sessions` | 4, `CP0002` |
| [Sites, projects, users, groups and favorites](#sites-projects-users-groups-and-favorites) | `TableauApiClient`'s methods for them gone, `DeleteProject2` with them: `client.Sites`, `.Projects`, `.Users`, `.Groups` and `.Favorites`, with lists that page themselves | 40, `CP0002` |
| [Permissions, tags and metadata](#permissions-tags-and-metadata) | `TableauApiClient`'s permissions, metadata and tag methods gone, `AddWorkbookToSchedule` excepted: `client.Permissions`, `.Tags` and `.Metadata`, over every kind of content | 35, `CP0002` |
| [Dependencies](#dependencies) | Microsoft.Extensions' abstractions in place of Microsoft.Extensions.Logging, and Newtonsoft.Json added | none |

## Target: net10.0 only

1.2.x targets netstandard2.0, which .NET Framework 4.6.1 and later and .NET Core 2.0 and later load. 2.0 targets
net10.0 alone.

- **On .NET 10 or later:** take 2.0 when it is released.
- **On .NET Framework, or on .NET before 10:** stay on 1.2.x, with a version range that keeps you there, such as
  `<PackageReference Include="Tableau.Api" Version="[1.2.6,2.0.0)" />`. 1.2.x takes security fixes only.

## Version

2.0 follows Semantic Versioning: 2.0.0, with release candidates `2.0.0-rc.N` before it. Builds of `main` are
`2.0.0-alpha.0.<n>`, made by CI and never published.

## Five methods whose signature changed

`main` changed these 1.2.5 methods of `TableauApiClient` before the rebuild started. Code compiled against 1.2.5 that
calls one fails with `MissingMethodException`; recompiled, a call that passes its arguments by position may not
compile, or may bind a value to the new parameter.

| 1.2.5 | Now | How to move over |
|---|---|---|
| `CreateMonthlySchedule(session, name, priority, type, executionOrder, startTime, dayToRun, cancelToken)` | `dayToRun` is gone | Drop the argument |
| `UpdateToMonthlySchedule(session, newName, newPriority, type, executionOrder, startTime, dayToRun, cancelToken)` | `dayToRun` is gone | Drop the argument |
| `DownloadDataSource(session, datasourceId, includeExtract, cancelToken)` returns `Task<Stream>` | It returns `Task<(Stream stream, DatasourceFileType type)>` | Take `.stream` from the result; `.type` is the file's kind |
| `UpdateDataSource(session, datasourceId, newProjectId, newOwnerId, newCertificationStatus, newCertificationNote, cancelToken)` | `newName` comes before `cancelToken` | Pass the token by name |
| `UpdateWorkbook(session, workbookId, newShowTabs, newProjectId, newOwnerId, cancelToken)` | `newName` comes before `cancelToken` | Pass the token by name |

## Permission capabilities renumbered

`Tableau.Api.Models.capabilityTypeName` gained the capabilities newer Tableau versions grant: `CreateRefreshMetrics`,
`RunExplainData`, `Execute` and `WebAuthoringForFlows` went in among the existing members, and `SaveAs` at the end. So
every member from `Delete` on has another number: `Delete`, `ExportData`, `ExportImage`, `ExportXml`, `Filter`,
`ProjectLeader`, `Read`, `ShareView`, `ViewComments`, `ViewUnderlyingData`, `WebAuthoring` and `Write`.

- **Code compiled against 1.2.5** holds the old numbers, so it sends another capability than it names: recompile it.
- **A capability stored as a number,** in a database or a file, reads as another one: store and read it by name.

## The 2.0 client

`TableauClient` is 2.0's client. It takes `TableauApiClient`'s calls over one area at a time, and each old method goes
when its new one lands. Build it from DI or on its own:

```csharp
// With DI: one named client per Tableau server; a service takes [FromKeyedServices("prod")] TableauClient.
services.AddTableauClient("prod", options => configuration.GetSection("Tableau:Prod").Bind(options));

// Without DI: dispose it when done.
await using var tableau = TableauClient.Create(new TableauClientOptions { ServerUrl = new Uri("https://tableau.example.com/") });
var info = await tableau.Server.GetInfoAsync(cancellationToken);
```

What changes for a caller who moves a call over:

- **Versions:** the client asks the server's REST API version on its first call and uses it, up to 3.29; set
  `ApiVersion` to pin one, major.minor only. A server below REST API 3.23 (Tableau Server 2024.2) is refused unless
  `AllowUnsupportedServerVersion` is set. 1.2.5 used the version it was given, and 3.14 above that.
- **Gates:** a call the server's version or deployment, Tableau Server or Tableau Cloud, lacks throws before it is sent.
- **Errors:** every failure is a `TableauException`, its `Kind` saying which: switch on it where 1.2.5 had
  `TableauRequestException`, `TableauApiVersionException` and `TableauOnlineNotSupportedException`. A refused argument is
  still an `ArgumentException`, and your own cancellation an `OperationCanceledException`; a timeout is
  `TableauErrorKind.Timeout`.
- **Tokens:** `CancellationToken cancellationToken = default`, last, where 1.2.5 took `CancellationToken? cancelToken`.
- **Connections:** plain http is refused unless `AllowInsecureHttp` is set; `DangerousAcceptAnyServerCertificate` is
  `IgnoreSSLErrors`' successor, and logs a warning; a certificate its issuer revoked is refused; a redirect is
  followed only to the same scheme, host and port; no cookie is kept between requests.
- **Options** can't change once a client is built from them.
- **Retries:** 1.2.5 sent each call once. A call that is safe to send twice, such as a read, is now sent again after
  a 408, a 429, a 5xx, a connection that failed or an attempt longer than `RequestTimeout`, three times by default
  (`MaxRetries`; 0 turns them off). A 429's or a 503's wait, by `Retry-After` or a 429's body, is waited out up to
  `MaxRetryAfter`, two minutes by default; a longer one fails at once, a 429 as `TableauErrorKind.RateLimited` and a
  503 as `TableauErrorKind.Http`, each with `RetryAfter`. A publish, a create or a run is never sent twice. Retries of
  your own, such as a resilience handler on every `HttpClient`, don't reach the client: set `MaxRetries` to 0 and add
  yours with `AddTableauClient(...).AddHttpMessageHandler(...)`, retrying GET and HEAD, or a call you know is safe to
  send twice, never a POST nor Append to File Upload's PUT. The client's attempt timeout runs inside your handler, so
  give a timeout of yours less than `RequestTimeout`, or set that to `Timeout.InfiniteTimeSpan`.
- **Timeouts:** `RequestTimeout` limits each attempt, two minutes by default; a download or an upload has
  `TransferTimeout` instead, for the whole of it, with no limit by default.
- **Downloads,** as their areas move over: a `TableauDownload`, returned once the headers have come, whose `Content`
  streams from the server, where 1.2.5 read the whole file into memory first. Dispose it; `SaveToFileAsync` writes it
  to a file. Its `FileName` is cleaned to a name alone, and `Kind` says what it is, as the `WorkbookFileType` beside
  the stream of 1.2.x's `DownloadWorkbook` did: `twb` is `TableauFileKind.Workbook`, `twbx` is `PackagedWorkbook`.
  1.2.x's `DownloadDataSource` returned the stream alone;
  [`main`'s returned a `DatasourceFileType`](#five-methods-whose-signature-changed) beside it, whose `hyper`, `tds` and
  `tdsx` are `Hyper`, `DataSource` and `PackagedDataSource`, and whose `tde`, an extract Tableau no longer makes, is
  `Unknown`.

| 1.2.5 | 2.0 | How to move over |
|---|---|---|
| `TableauApiClient.ServerInfo(cancelToken)` returns `serverInfo` | `TableauClient.Server.GetInfoAsync(cancellationToken)` returns `Tableau.Api.Rest.Models.ServerInfo` | `info.restApiVersion` is `info.RestApiVersion`, `info.productVersion.Value` is `info.ProductVersion?.Value`. It needs no sign-in |

## Sessions

`TableauApiClient`'s `SignIn`, `SignInWithPAT`, `SignOut` and `SwitchSite` are gone (`SignInWithPAT` had already
changed on `main`, by its `userIdToImpersonate`). The 2.0 client holds the session itself: give it credentials, and
it signs in on the first call that needs a session, one sign-in at a time, signs in again shortly before the session is
due to end and once after a 401, and signs out when you dispose it.

```csharp
// Without DI.
await using var tableau = TableauClient.Create(options, TableauCredentials.PersonalAccessToken("nightly-export", secret));

// With DI: a secret can come from a vault at each sign-in.
services.AddTableauClient("prod", options => configuration.GetSection("Tableau:Prod").Bind(options))
    .UseCredentials(provider => TableauCredentials.ConnectedAppDirectTrust(clientId, secretId,
        provider.GetRequiredService<Vault>().TableauSecretAsync, "svc-reports", ["tableau:content:read"]));
```

| 1.2.5 | 2.0 | How to move over |
|---|---|---|
| `SignIn(userName, password, siteContentUrl, userIdToImpersonate, cancelToken)` | `TableauCredentials.Password(name, password, impersonateUserId)` on the client, or `client.Sessions.SignInAsync(credentials, site, cancellationToken)` | Give the client the credentials; sign in by hand only to hold a session yourself. Impersonation is refused on Tableau Cloud before sending |
| `SignInWithPAT(tokenName, token, siteContentUrl, cancelToken)` | `TableauCredentials.PersonalAccessToken(name, secret, impersonateUserId)` | The same; a PAT still allows one session at a time |
| `SignOut(session, cancelToken)` | Dispose the client, or `client.Sessions.SignOutAsync(session, cancellationToken)` for a session you signed in | A client from DI never signs out: its session is the name's, and ends on its own |
| `SwitchSite(session, newSiteContentUrl, cancelToken)` | `client.Sessions.SwitchSiteAsync(session, site, cancellationToken)` | Tableau Server only; the session you switch from ends |
| `Tableau.Api.Models.TableauSession`, mutable, its `Token` public, its `UserName` always null | `Tableau.Api.TableauSession`, a record whose token is internal and never printed; `UserName` is the user's, or the token's name for a PAT | Call through `client.WithSession(session)`. The 1.x type stays until the old client goes, and the old client's methods still take it |

On `main` until 2.0.0, an area not yet moved to `TableauClient` can't get a session from the library: its old
`TableauApiClient` methods, and Vizportal's, take a `Models.TableauSession` that no sign-in makes now, and the new
session's token is internal. Each area moves in its own step, and 2.0.0 ships with every one moved.

Also new: `TableauCredentials.ConnectedAppDirectTrust`, which mints a connected app's JWT at each sign-in;
`TableauCredentials.Jwt`, for a JWT minted elsewhere; and `TableauCredentials.UnifiedAccessToken`, Tableau Cloud's,
from REST API 3.27. A sign-in's `TableauException` keeps none of Tableau's answer. A namespace that imports both
`Tableau.Api` and `Tableau.Api.Models` must name which `TableauSession` it means.

## Sites, projects, users, groups and favorites

`TableauApiClient`'s methods for these areas are gone, `QueryViewsForSite` excepted, which stays with the old views
methods until views move; `TableauClient` has a sub-client for each. They take the session's site from the client,
string LUIDs, and the token last, and return the models in `Tableau.Api.Rest.Models`.

```csharp
await foreach (var user in tableau.Users.ListAsync(new UserListOptions
{
    Filter = TableauFilter.And(TableauFilter.Eq(UserField.SiteRole, "Viewer"), TableauFilter.Gte(UserField.LastLogin, since)),
}, cancellationToken))
{
    // every page, read as you go
}
```

What changes for a caller:

- **Lists:** `ListAsync` reads every page as you go, and stops at Tableau's total or on a short page; `ListPageAsync`
  returns one `Page<T>` (`Items`, `PageNumber`, `PageSize`, `TotalAvailable`). Neither is ever null. 1.2.5 returned a
  `paginationType` and an array, null when Tableau sent none, and some returned the first page alone.
- **Filters and sorts:** `TableauFilter` and `TableauSort` over a resource's fields (`ProjectField`, `UserField`,
  `GroupField`), which a list refuses for another resource before sending. Write values as they are: the client
  escapes them. A filter you wrote by hand is `TableauFilter.FromString(filterExpression)`. A value can't hold a comma,
  which Tableau refuses even escaped. A list you don't sort comes in an order that doesn't change from page to page:
  projects oldest first, then by name; users by name, then on Tableau Server by domain; groups by name. A sort you give
  is followed by the default fields it doesn't name. `Eq` and `In` take a `SiteRole` too. Query Sites takes no filter or
  sort, as
  Tableau Server 2025.1 refuses the first, the reference's one included, and ignores the second.
- **Updates** take a model with only what changes set, such as `new Project { Name = "Finance" }`, where 1.2.5 took
  one parameter for each.
- **Deployments:** Create Site, Delete Site, Query Sites and Active Directory groups are refused on Tableau Cloud before
  anything is sent; Update Site and Delete Group, which 1.2.5 refused there, run on it.

| 1.2.5 | 2.0 | How to move over |
|---|---|---|
| `CreateSite(session, name, contentUrl, adminMode, tierCreatorCapacity, tierExplorerCapacity, tierViewerCapacity, storageQuotaMb, disableSubscriptions, cancelToken)` | `client.Sites.CreateAsync(site, cancellationToken)` | `new Site { Name = ..., ContentUrl = ..., AdminMode = ..., TierCreatorCapacity = ..., StorageQuota = "100", DisableSubscriptions = ... }`; any setting the reference lists for Create Site |
| `QuerySiteById(session, siteId, cancelToken)` | `client.Sites.GetAsync(id, cancellationToken)` | Tableau answers for the session's site alone |
| `QuerySiteByName(session, siteName, cancelToken)` | `client.Sites.GetByNameAsync(name, cancellationToken)` | The same |
| `QuerySiteByContentUrl(session, siteContentUrl, cancelToken)` | `client.Sites.GetByContentUrlAsync(contentUrl, cancellationToken)` | The default site has no content URL: get it by its LUID |
| `QuerySites(session, pageSize, pageNumber, cancelToken)` returns `(paginationType, siteListType)` | `client.Sites.ListAsync(options, cancellationToken)`, or `ListPageAsync(pageNumber, options, cancellationToken)` | `new SiteListOptions { PageSize = ... }` |
| `UpdateSite(session, siteId, newName, newContentUrl, newAdminMode, newTierCreatorCapacity, newTierExplorerCapacity, newTierViewerCapacity, newState, newStorageQuota, newDisableSubscriptions, newRevisionHistoryEnabled, newRevisionLimit, cancelToken)` | `client.Sites.UpdateAsync(id, changes, cancellationToken)` | Set only what changes, such as `new Site { RevisionLimit = 25, State = "Suspended" }` |
| `DeleteSiteById(session, siteId, cancelToken)` | `client.Sites.DeleteAsync(id, cancellationToken)` | Signed in to that site, as Tableau asks |
| `DeleteSiteByName(session, siteName, cancelToken)` | `client.Sites.DeleteAsync(id, cancellationToken)` | Take the id from `GetByNameAsync` |
| `DeleteSiteByContentUrl(session, siteContentUrl, cancelToken)` | `client.Sites.DeleteAsync(id, cancellationToken)` | Take the id from `GetByContentUrlAsync` |
| `CreateProject(session, projectName, description, contentPermissions, parentProjectId, publishSamples, cancelToken)` | `client.Projects.CreateAsync(project, publishSamples, cancellationToken)` | `new Project { Name = ..., Description = ..., ParentProjectId = ..., ContentPermissions = ... }`; left null, the permissions are Tableau's default, `ManagedByOwner`, which 1.2.5 always sent |
| `QueryProjects(session, pageSize, pageNumber, filterExpression, sortExpression, cancelToken)` returns `(paginationType, projectType[])` | `client.Projects.ListAsync(options, cancellationToken)`, or `ListPageAsync(pageNumber, options, cancellationToken)` | `new ProjectListOptions { Filter = ..., Sort = ..., PageSize = ... }` |
| `UpdateProject(session, projectId, newProjectName, newDescription, newContentPermissions, newParentProjectId, publishSamples, cancelToken)` | `client.Projects.UpdateAsync(id, changes, cancellationToken)` | Set only what changes; an empty `ParentProjectId` moves the project to the top. Samples are published on create alone |
| `DeleteProject(session, projectId, cancelToken)` | `client.Projects.DeleteAsync(id, cancellationToken)` | None |
| `DeleteProject2(session, projectId, cancelToken)` | `client.Projects.DeleteAsync(id, cancellationToken)` | It was the same call |
| `QueryUser(session, userId, cancelToken)` | `client.Users.GetAsync(id, cancellationToken)` | None |
| `GetUsersOnSite(session, pageSize, pageNumber, fieldsExpression, filterExpression, sortExpression, cancelToken)` returns `(paginationType, userType[])` | `client.Users.ListAsync(options, cancellationToken)`, or `ListPageAsync(pageNumber, options, cancellationToken)` | `new UserListOptions { Filter = ..., Sort = ..., PageSize = ... }`; no `fields`: each user comes with Tableau's default fields |
| `AddUserToSite(session, userName, siteRole, authSetting, cancelToken)` | `client.Users.AddAsync(name, siteRole, options, cancellationToken)` | `new UserAddOptions { AuthSetting = ... }`, with the reference's identity pool, identity provider, email, language and locale too |
| `UpdateUser(session, userId, newFullName, newEmail, newPassword, newSiteRole, newAuthSetting, newDisplayName, cancelToken)` | `client.Users.UpdateAsync(id, changes, cancellationToken)` | `new User { FullName = ..., SiteRole = ... }`, and the reference's `IdentityPoolName`, `IdpConfigurationId`, `Language` and `Locale`. `newDisplayName` went as the user's name, which Update User doesn't take |
| `RemoveUserFromSite(session, userId, cancelToken)` | `client.Users.RemoveAsync(id, mapAssetsToUserId, cancellationToken)` | Name a user to take over what the user owns, or leave it null |
| `GetGroupsOfUser(session, userId, pageSize, pageNumber, cancelToken)` returns `(paginationType, groupType[])` | `client.Users.ListGroupsAsync(userId, cancellationToken)` | Every page, 100 at a time |
| `QueryGroups(session, pageSize, pageNumber, filterExpression, sortExpression, cancelToken)` returns `(paginationType, groupType[])` | `client.Groups.ListAsync(options, cancellationToken)`, or `ListPageAsync(pageNumber, options, cancellationToken)` | `new GroupListOptions { Filter = ..., Sort = ..., PageSize = ... }` |
| `CreateGroup(session, groupName, cancelToken)` | `client.Groups.CreateAsync(name, minimumSiteRole, externalUserEnabled, cancellationToken)` | A local group |
| `CreateGroupFromActiveDirectory(session, adGroupName, domainName, siteRole, cancelToken)` | `client.Groups.ImportFromActiveDirectoryAsync(name, domainName, siteRole, grantLicenseMode, cancellationToken)` | Tableau Server only |
| `CreateGroupFromActiveDirectoryAsJob(session, adGroupName, domainName, siteRole, cancelToken)` returns `jobType` | `client.Groups.ImportFromActiveDirectoryAsJobAsync(name, domainName, siteRole, grantLicenseMode, cancellationToken)` returns `Job` | Tableau Server only |
| `UpdateGroup(session, groupId, newGroupName, cancelToken)` | `client.Groups.UpdateAsync(id, name, minimumSiteRole, externalUserEnabled, cancellationToken)` | A local group; what you leave null stays |
| `UpdateGroupFromActiveDirectory(session, newAdGroupName, newDomainName, newSiteRole, cancelToken)` | `client.Groups.UpdateFromActiveDirectoryAsync(id, name, domainName, siteRole, grantLicenseMode, cancellationToken)` | It takes the group's id, which 1.2.5's left out, so its update went to the group list, not to the group |
| `UpdateGroupFromActiveDirectoryAsJob(session, newAdGroupName, newDomainName, siteRole, cancelToken)` returns `jobType` | `client.Groups.UpdateFromActiveDirectoryAsJobAsync(id, name, domainName, siteRole, grantLicenseMode, cancellationToken)` returns `Job` | The same |
| `DeleteGroup(session, groupId, cancelToken)` | `client.Groups.DeleteAsync(id, cancellationToken)` | Now on Tableau Cloud too |
| `AddUserToGroup(session, groupId, userId, cancelToken)` | `client.Groups.AddUserAsync(groupId, userId, cancellationToken)` | None |
| `RemoveUserFromGroup(session, groupId, userId, cancelToken)` | `client.Groups.RemoveUserAsync(groupId, userId, cancellationToken)` | None |
| `GetUsersInGroup(session, groupId, pageSize, pageNumber, cancelToken)` returns `(paginationType, userType[])` | `client.Groups.ListUsersAsync(groupId, cancellationToken)` | Every page, 100 at a time |
| `GetFavoritesForUser(session, userId, cancelToken)` returns `favoriteType[]` | `client.Favorites.ListAsync(userId, cancellationToken)` | Every page, where 1.2.5 read the first |
| `AddWorkbookToFavorites(session, userId, workbookId, favoriteLabel, cancelToken)` returns `favoriteType` | `client.Favorites.AddAsync(userId, label, TableauContent.Workbook(id), cancellationToken)` | It returns the favorites Tableau answers with |
| `AddViewToFavorites(session, userId, viewId, favoriteLabel, cancelToken)` | `client.Favorites.AddAsync(userId, label, TableauContent.View(id), cancellationToken)` | The same |
| `AddDataSourceToFavorites(session, userId, datasourceId, favoriteLabel, cancelToken)` | `client.Favorites.AddAsync(userId, label, TableauContent.DataSource(id), cancellationToken)` | The same |
| `AddProjectToFavorites(session, userId, projectId, favoriteLabel, cancelToken)` | `client.Favorites.AddAsync(userId, label, TableauContent.Project(id), cancellationToken)` | The same; `TableauContent.Flow(id)` is new |
| `DeleteWorkbookFromFavorites(session, userId, workbookId, cancelToken)` | `client.Favorites.RemoveAsync(userId, TableauContent.Workbook(id), cancellationToken)` | None |
| `DeleteViewFromFavorites(session, userId, viewId, cancelToken)` | `client.Favorites.RemoveAsync(userId, TableauContent.View(id), cancellationToken)` | None |
| `DeleteDataSourceFromFavorites(session, userId, datasourceId, cancelToken)` | `client.Favorites.RemoveAsync(userId, TableauContent.DataSource(id), cancellationToken)` | None |
| `DeleteProjectFromFavorites(session, userId, projectId, cancelToken)` | `client.Favorites.RemoveAsync(userId, TableauContent.Project(id), cancellationToken)` | None |

A favorite names its content with a `TableauContent`, as permissions, tags and data quality warnings do; a kind favorites
don't take, such as a table, is refused before anything is sent.

## Permissions, tags and metadata

`TableauApiClient`'s permissions, metadata and tag methods are gone, `AddWorkbookToSchedule` excepted, which stays with
the old schedule methods until schedules move. `TableauClient` has one API for each over every kind of content:
`client.Permissions`, `client.Tags` and `client.Metadata`, which name content with a `TableauContent`, such as
`TableauContent.Workbook(id)` or `TableauContent.Column(tableId, columnId)`.

```csharp
var project = TableauContent.Project(projectId);
await tableau.Permissions.AddAsync(project,
[
    new GranteeCapabilities
    {
        Group = new Group { Id = groupId },
        Capabilities = [new Capability { Name = CapabilityName.Read, Mode = CapabilityMode.Allow }],
    },
], cancellationToken);
await tableau.Permissions.DeleteAsync(project, PermissionGrantee.Group(groupId), CapabilityName.Read, CapabilityMode.Allow, cancellationToken);
```

What changes for a caller:

- **One call per verb, every kind:** permissions on projects, workbooks, views, data sources, flows, virtual
  connections, collections, databases and tables, where 1.2.5 had them for projects, workbooks, data sources and a
  view's list; a project's default permissions for workbooks, data sources, data roles, flows, virtual connections,
  databases and tables, where 1.2.5 had the first two; tags on workbooks, views, data sources, virtual connections,
  databases, tables and columns, and in a batch. A kind a call doesn't take is refused before anything is sent, and
  each kind is gated by the REST API version it came in.
- **Capabilities:** `CapabilityName` and `CapabilityMode` in place of `capabilityTypeName` and `capabilityTypeMode`,
  such as `CapabilityName.Read`; a capability Tableau adds later is `new CapabilityName("...")`. A delete names its user
  or group with `PermissionGrantee.User(id)` or `.Group(id)`, where 1.2.5 had a method for each; a group set's is
  `.GroupSet(id)`, new.
- **Rules:** `GranteeCapabilities` with its `User` or `Group` by id, where 1.2.5 took `granteeCapabilitiesType[]`; the
  client sends each grantee's id and each capability's name and mode, nothing else.
- **Lists:** databases and tables page themselves, where 1.2.5 returned the first page alone; a table's columns come
  from one call of up to 1,000, as Tableau Server 2025.1 refuses the second page of them.
- **Tags** are escaped as one segment of the request's path, so a tag with `/`, `#` or `%` is deleted as written. In
  a body, a tag with whitespace or a comma goes in double quotes, so Tableau keeps it one tag, where 1.2.5 sent it as
  written and Tableau split it; delete it by its text, unquoted.
- **Data quality warnings** take a `DataQualityWarning` with its `Type` as text, such as `"WARNING"`, where 1.2.5 took
  `DataQualityWarningType` and `MetadataContentType`, which stay, unused, until the old client goes.
- **New:** default permissions for every kind, permissions on views, flows, virtual connections, collections, databases
  and tables, tags on the catalog's assets and virtual connections, batch tags, data quality warnings deleted by id or by
  content, Query Database, Query Table, Delete Database, Delete Table, Delete Column, and the Metadata API's GraphQL:
  `client.Metadata.QueryAsync`, with your own `JsonTypeInfo<T>`, and `ListNodesAsync`, a connection read page by page
  by its cursor.

| 1.2.5 | 2.0 | How to move over |
|---|---|---|
| `QueryProjectPermissions(session, projectId, cancelToken)` returns `permissionsType` | `client.Permissions.GetAsync(TableauContent.Project(id), cancellationToken)` returns `Permissions` | `GranteeCapabilities` is never null |
| `QueryWorkbookPermissions(session, workbookId, cancelToken)` | `client.Permissions.GetAsync(TableauContent.Workbook(id), cancellationToken)` | The same |
| `QueryDataSourcePermissions(session, datasourceId, cancelToken)` | `client.Permissions.GetAsync(TableauContent.DataSource(id), cancellationToken)` | The same |
| `QueryViewPermissions(session, viewId, cancelToken)` | `client.Permissions.GetAsync(TableauContent.View(id), cancellationToken)` | The same; never released: `main` added it after 1.2.5, so it is no ledger entry |
| `AddProjectPermissions(session, projectId, granteeCapabilities, cancelToken)` returns `permissionsType` | `client.Permissions.AddAsync(TableauContent.Project(id), rules, cancellationToken)` returns `Permissions` | `rules` is `GranteeCapabilities`, as above |
| `AddWorkbookPermissions(session, workbookId, granteeCapabilities, cancelToken)` | `client.Permissions.AddAsync(TableauContent.Workbook(id), rules, cancellationToken)` | The same |
| `AddDataSourcePermissions(session, datasourceId, granteeCapabilities, cancelToken)` | `client.Permissions.AddAsync(TableauContent.DataSource(id), rules, cancellationToken)` | The same |
| `DeleteProjectPermissionForUser(session, projectId, userId, capabilityName, capabilityMode, cancelToken)` | `client.Permissions.DeleteAsync(TableauContent.Project(id), PermissionGrantee.User(userId), capability, mode, cancellationToken)` | `capabilityTypeName.Read` is `CapabilityName.Read`, `capabilityTypeMode.Allow` is `CapabilityMode.Allow` |
| `DeleteProjectPermissionForGroup(session, projectId, groupId, capabilityName, capabilityMode, cancelToken)` | `client.Permissions.DeleteAsync(TableauContent.Project(id), PermissionGrantee.Group(groupId), capability, mode, cancellationToken)` | The same |
| `DeleteWorkbookPermissionForUser(session, workbookId, userId, capabilityName, capabilityMode, cancelToken)` | `client.Permissions.DeleteAsync(TableauContent.Workbook(id), PermissionGrantee.User(userId), capability, mode, cancellationToken)` | The same |
| `DeleteWorkbookPermissionForGroup(session, workbookId, groupId, capabilityName, capabilityMode, cancelToken)` | `client.Permissions.DeleteAsync(TableauContent.Workbook(id), PermissionGrantee.Group(groupId), capability, mode, cancellationToken)` | The same |
| `DeleteDataSourcePermissionForUser(session, datasourceId, userId, capabilityName, capabilityMode, cancelToken)` | `client.Permissions.DeleteAsync(TableauContent.DataSource(id), PermissionGrantee.User(userId), capability, mode, cancellationToken)` | The same |
| `DeleteDataSourcePermissionForGroup(session, datasourceId, groupId, capabilityName, capabilityMode, cancelToken)` | `client.Permissions.DeleteAsync(TableauContent.DataSource(id), PermissionGrantee.Group(groupId), capability, mode, cancellationToken)` | The same |
| `QueryDefaultPermissionsForWorkbooks(session, workbookId, cancelToken)` | `client.Permissions.GetDefaultAsync(projectId, DefaultPermissionsKind.Workbooks, cancellationToken)` | Its parameter, named `workbookId`, took the project's id |
| `QueryDefaultPermissionsForDataSources(session, datasourceId, cancelToken)` | `client.Permissions.GetDefaultAsync(projectId, DefaultPermissionsKind.DataSources, cancellationToken)` | Its parameter, named `datasourceId`, took the project's id |
| `AddDefaultPermissionsForWorkbooks(session, projectId, granteeCapabilities, cancelToken)` | `client.Permissions.AddDefaultAsync(projectId, DefaultPermissionsKind.Workbooks, rules, cancellationToken)` | `rules` is `GranteeCapabilities` |
| `AddDefaultPermissionsForDataSources(session, projectId, granteeCapabilities, cancelToken)` | `client.Permissions.AddDefaultAsync(projectId, DefaultPermissionsKind.DataSources, rules, cancellationToken)` | The same |
| `DeleteDefaultWorkbookPermissionForUser(session, projectId, userId, capabilityName, capabilityMode, cancelToken)` | `client.Permissions.DeleteDefaultAsync(projectId, DefaultPermissionsKind.Workbooks, PermissionGrantee.User(userId), capability, mode, cancellationToken)` | As for a delete above |
| `DeleteDefaultWorkbookPermissionForGroup(session, projectId, groupId, capabilityName, capabilityMode, cancelToken)` | `client.Permissions.DeleteDefaultAsync(projectId, DefaultPermissionsKind.Workbooks, PermissionGrantee.Group(groupId), capability, mode, cancellationToken)` | The same |
| `DeleteDefaultDataSourcePermissionForUser(session, projectId, userId, capabilityName, capabilityMode, cancelToken)` | `client.Permissions.DeleteDefaultAsync(projectId, DefaultPermissionsKind.DataSources, PermissionGrantee.User(userId), capability, mode, cancellationToken)` | The same |
| `DeleteDefaultDataSourcePermissionForGroup(session, projectId, groupId, capabilityName, capabilityMode, cancelToken)` | `client.Permissions.DeleteDefaultAsync(projectId, DefaultPermissionsKind.DataSources, PermissionGrantee.Group(groupId), capability, mode, cancellationToken)` | The same |
| `AddTagsToWorkbook(session, workbookId, tagsToAdd, cancelToken)` returns `tagType[]` | `client.Tags.AddAsync(TableauContent.Workbook(id), tags, cancellationToken)` returns `IReadOnlyList<Tag>` | Never null |
| `AddTagsToView(session, viewId, tagsToAdd, cancelToken)` | `client.Tags.AddAsync(TableauContent.View(id), tags, cancellationToken)` | The same |
| `AddTagsToDataSource(session, datasourceId, tagsToAdd, cancelToken)` | `client.Tags.AddAsync(TableauContent.DataSource(id), tags, cancellationToken)` | The same |
| `DeleteTagFromWorkbook(session, workbookId, tagName, cancelToken)` | `client.Tags.DeleteAsync(TableauContent.Workbook(id), tag, cancellationToken)` | None |
| `DeleteTagFromView(session, viewId, tagName, cancelToken)` | `client.Tags.DeleteAsync(TableauContent.View(id), tag, cancellationToken)` | None |
| `DeleteTagFromDataSource(session, datasourceId, tag, cancelToken)` | `client.Tags.DeleteAsync(TableauContent.DataSource(id), tag, cancellationToken)` | None |
| `QueryDatabases(session, cancelToken)` returns `databaseType[]` | `client.Metadata.ListDatabasesAsync(options, cancellationToken)`, or `ListDatabasesPageAsync(pageNumber, options, cancellationToken)` | Every page, where 1.2.5 returned the first; `new DatabaseListOptions { PageSize = ... }` |
| `QueryTables(session, cancelToken)` returns `tableType[]` | `client.Metadata.ListTablesAsync(options, cancellationToken)`, or `ListTablesPageAsync(pageNumber, options, cancellationToken)` | The same; `TableListOptions` |
| `UpdateDatabase(session, databaseId, newDescription, newContactId, isCertified, newCertificationNote, cancelToken)` | `client.Metadata.UpdateDatabaseAsync(id, changes, cancellationToken)` | `new Database { Description = ..., Contact = new User { Id = ... }, IsCertified = ..., CertificationNote = ... }`, and `ContentPermissions` |
| `UpdateTable(session, tableId, newDescription, newContactId, isCertified, newCertificationNote, cancelToken)` | `client.Metadata.UpdateTableAsync(id, changes, cancellationToken)` | `new Table { ... }`, as for a database |
| `QueryColumnsInTable(session, tableId, cancelToken)` returns `columnType[]` | `client.Metadata.ListColumnsAsync(tableId, cancellationToken)` | `Column` in `Tableau.Api.Rest.Models` |
| `UpdateColumn(session, tableId, columnId, newDescription, cancelToken)` | `client.Metadata.UpdateColumnAsync(tableId, columnId, new Column { Description = ... }, cancellationToken)` | None |
| `QueryDataQualityWarningByContent(session, contentType, luid, cancelToken)` returns `dataQualityWarningType[]` | `client.Metadata.ListDataQualityWarningsAsync(content, cancellationToken)` | `MetadataContentType.database` and `luid` are `TableauContent.Database(luid)`; a flow, a column and a virtual connection too |
| `AddDataQualityWarning(session, contentType, luid, type, message, severity, state, cancelToken)` | `client.Metadata.AddDataQualityWarningAsync(content, warning, cancellationToken)` | `new DataQualityWarning { Type = "WARNING", Message = ..., IsSevere = severity, IsActive = state }` |
| `UpdateDataQualityWarning(session, dataQualityWarningId, type, message, severity, state, cancelToken)` | `client.Metadata.UpdateDataQualityWarningAsync(id, changes, cancellationToken)` | The same; the type is needed |

## Dependencies

| Package | 1.2.5 | Now |
|---|---|---|
| Microsoft.Extensions.Logging | 2.1.1 | 10.0.14, through Microsoft.Extensions.Http; the library itself references Microsoft.Extensions.Logging.Abstractions alone |
| Microsoft.Extensions.Http, .Options, .DependencyInjection.Abstractions | none | 10.0.14, for `AddTableauClient` and the client's options |
| Polly.Core | none | 8.9.0, for the client's retries, run by a handler of the library's own; none of its types is in the library's API, and it brings no other package |
| Newtonsoft.Json | none | 13.0.4, for the Vizportal client |
