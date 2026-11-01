# Live coverage

Which calls of the 2.0 client the live suite makes on the maintainers' Tableau Server 2025.1.5 (REST API 3.25), each
night and by hand from `main` (`.github/workflows/live.yml`), and why the others aren't made there. Tableau Cloud isn't
run: it stays checked by hand.

- **A row** is a public method and a REST call it sends: a method of a sub-client of `TableauClient`, or a
  `TableauCredentials` factory, whose kind of sign-in is its own Sign In call. A method that sends several calls, by
  its arguments, has a row for each; overloads that differ only in how a secret is given share a row.
- **Live** is `yes`, Tableau answered the call with a 2xx; `refused <code>`, Tableau answered it with that error, for a
  reason of the server's, such as a sample with no extract; or `no`.
- **Test, or why not** names, for `yes` and `refused`, the `Live` tests that make the call, `LiveSite` for the suite's
  own setup and `SuiteCleanup` for its cleanup; for `no`, the reason.
- **Checked from both sides:** `LiveCoverageTests`, on every PR, fails when a method or a call of `Endpoints.All` has no
  row, a row names a method, a call or a test that doesn't exist, a `no` row has no reason, or a method and a call have
  two rows. With `TABLEAU_LIVE_MATRIX=1`, which `live.yml` sets, the suite's cleanup fails the run, naming the row, when
  a `yes` row's call wasn't answered with a 2xx in one of its tests, a `refused` row's wasn't refused with its code there
  or was answered with a 2xx, or a `no` row's call was answered, unless another row holds that call live. A row whose
  test failed is left out, since that failure fails the run already.
- **What it can't show:** the record is per test and per call, not per method, so a list and its page, sent in one
  test, are told apart by the test's code, and a call of the same name made in the test by a helper also satisfies the
  row.
- **A new public method** adds its rows here in the change that adds it.

## TableauClient

| Method | Call | Live | Test, or why not |
|---|---|---|---|
| `ServerClient.GetInfoAsync` | Server Info | yes | `ServerInfoSiteTests.ServerInfoAnswersWithoutASignInAtTheNegotiatedVersion`, `LiveSessionTests.ASessionSignedInByHandServesABoundClientAcrossItsSubClients` |
| `SessionsClient.SignInAsync` | Sign In | no | a password: an automated run is given none, and `ServerOnlySessionTests` signs in with one by hand |
| `SessionsClient.SignInAsync` | Sign In, impersonating a user | no | a password: an automated run is given none, and `ServerOnlySessionTests` signs in with one by hand |
| `SessionsClient.SignInAsync` | Sign In with a personal access token | yes | `LiveSessionTests.ASessionSignedInByHandServesABoundClientAcrossItsSubClients` |
| `SessionsClient.SignInAsync` | Sign In with a personal access token, impersonating a user | yes | `LiveDirectoryTests.ADirectoryUserIsAddedMadeAViewerImpersonatedByTheTokenAndRemoved` |
| `SessionsClient.SignInAsync` | Sign In with a JWT | yes | `LiveConnectedAppTests.ASessionSignedInWithAMintedJwtServesABoundClientAndIsSignedOut` |
| `SessionsClient.SignInAsync` | Sign In with a unified access token | no | Tableau Cloud only |
| `SessionsClient.SwitchSiteAsync` | Switch Site | yes | `LiveSessionTests.SwitchSiteGivesASessionOnTheRunsSiteAndEndsTheFirst` |
| `SessionsClient.SignOutAsync` | Sign Out | yes | `LiveSessionTests.ASessionSignedInByHandServesABoundClientAcrossItsSubClients`, `LiveConnectedAppTests.ASessionSignedInWithAMintedJwtServesABoundClientAndIsSignedOut` |
| `TableauCredentials.PersonalAccessToken` | Sign In with a personal access token | yes | `LiveSessionTests.AClientSignsItselfInForACallAndSignsOutWhenDisposed`, `LiveAreaTests.AProjectIsMadeFoundByAFilterUpdatedAndDeletedInTheRunsProject` |
| `TableauCredentials.PersonalAccessToken` | Sign In with a personal access token, impersonating a user | yes | `LiveDirectoryTests.ADirectoryUserIsAddedMadeAViewerImpersonatedByTheTokenAndRemoved` |
| `TableauCredentials.ConnectedAppDirectTrust` | Sign In with a JWT | yes | `LiveConnectedAppTests.AClientWithTheConnectedAppSignsItselfInForAPageOfProjectsAndSignsOutWhenDisposed` |
| `TableauCredentials.Jwt` | Sign In with a JWT | yes | `LiveConnectedAppTests.ASessionSignedInWithAMintedJwtServesABoundClientAndIsSignedOut` |
| `TableauCredentials.UnifiedAccessToken` | Sign In with a unified access token | no | Tableau Cloud only |
| `TableauCredentials.Password` | Sign In | no | a password: an automated run is given none, and `ServerOnlySessionTests` signs in with one by hand |
| `TableauCredentials.Password` | Sign In, impersonating a user | no | a password: an automated run is given none, and `ServerOnlySessionTests` signs in with one by hand |
| `SitesClient.GetAsync` | Query Site | yes | `LiveSiteAdminTests.TheRunsSiteIsReadByItsIdContentUrlAndNameListedAndUpdatedWithAnotherRevisionLimit` |
| `SitesClient.GetByContentUrlAsync` | Query Site by content URL | yes | `LiveSiteAdminTests.TheRunsSiteIsReadByItsIdContentUrlAndNameListedAndUpdatedWithAnotherRevisionLimit` |
| `SitesClient.GetByNameAsync` | Query Site by name | refused 404000 | `LiveSiteAdminTests.TheRunsSiteIsReadByItsIdContentUrlAndNameListedAndUpdatedWithAnotherRevisionLimit` |
| `SitesClient.ListAsync` | Query Sites | yes | `LiveSiteAdminTests.TheRunsSiteIsReadByItsIdContentUrlAndNameListedAndUpdatedWithAnotherRevisionLimit`, `LiveSite` |
| `SitesClient.ListPageAsync` | Query Sites | yes | `LiveSiteAdminTests.TheRunsSiteIsReadByItsIdContentUrlAndNameListedAndUpdatedWithAnotherRevisionLimit` |
| `SitesClient.CreateAsync` | Create Site | yes | `LiveSite` |
| `SitesClient.UpdateAsync` | Update Site | yes | `LiveSiteAdminTests.TheRunsSiteIsReadByItsIdContentUrlAndNameListedAndUpdatedWithAnotherRevisionLimit` |
| `SitesClient.DeleteAsync` | Delete Site | yes | `SuiteCleanup` |
| `ProjectsClient.ListAsync` | Query Projects | yes | `LiveAreaTests.AProjectIsMadeFoundByAFilterUpdatedAndDeletedInTheRunsProject`, `LiveSessionTests.AClientSignsItselfInForACallAndSignsOutWhenDisposed` |
| `ProjectsClient.ListPageAsync` | Query Projects | yes | `LiveAreaTests.AProjectIsMadeFoundByAFilterUpdatedAndDeletedInTheRunsProject`, `LiveConnectedAppTests.AClientWithTheConnectedAppSignsItselfInForAPageOfProjectsAndSignsOutWhenDisposed` |
| `ProjectsClient.CreateAsync` | Create Project | yes | `LiveAreaTests.AProjectIsMadeFoundByAFilterUpdatedAndDeletedInTheRunsProject`, `LiveSite` |
| `ProjectsClient.UpdateAsync` | Update Project | yes | `LiveAreaTests.AProjectIsMadeFoundByAFilterUpdatedAndDeletedInTheRunsProject` |
| `ProjectsClient.DeleteAsync` | Delete Project | yes | `LiveAreaTests.AProjectIsMadeFoundByAFilterUpdatedAndDeletedInTheRunsProject` |
| `UsersClient.GetAsync` | Query User On Site | yes | `LiveAreaTests.UsersAreListedPageByPageAndTheSuitesOwnIsReadByIdAndByFilter` |
| `UsersClient.ListAsync` | Get Users on Site | yes | `LiveAreaTests.UsersAreListedPageByPageAndTheSuitesOwnIsReadByIdAndByFilter` |
| `UsersClient.ListPageAsync` | Get Users on Site | yes | `LiveAreaTests.UsersAreListedPageByPageAndTheSuitesOwnIsReadByIdAndByFilter` |
| `UsersClient.AddAsync` | Add User to Site | yes | `LiveDirectoryTests.ADirectoryUserIsAddedMadeAViewerImpersonatedByTheTokenAndRemoved` |
| `UsersClient.UpdateAsync` | Update User | yes | `LiveDirectoryTests.ADirectoryUserIsAddedMadeAViewerImpersonatedByTheTokenAndRemoved` |
| `UsersClient.RemoveAsync` | Remove User from Site | yes | `LiveDirectoryTests.ADirectoryUserIsAddedMadeAViewerImpersonatedByTheTokenAndRemoved` |
| `UsersClient.ListGroupsAsync` | Get Groups for a User | yes | `LiveAreaTests.ALocalGroupIsMadeFoundRenamedGivenTheSuitesUserAndDeleted`, `LiveAreaTests.UsersAreListedPageByPageAndTheSuitesOwnIsReadByIdAndByFilter` |
| `GroupsClient.ListAsync` | Query Groups | yes | `LiveAreaTests.ALocalGroupIsMadeFoundRenamedGivenTheSuitesUserAndDeleted` |
| `GroupsClient.ListPageAsync` | Query Groups | yes | `LiveDirectoryTests.ADirectoryGroupIsImportedWithItsMemberSynchronisedAndDeletedThenImportedAsAJobFoundAndDeleted` |
| `GroupsClient.CreateAsync` | Create Group | yes | `LiveAreaTests.ALocalGroupIsMadeFoundRenamedGivenTheSuitesUserAndDeleted`, `LiveAreaTests.ACapabilityIsGrantedToARunsGroupOnTheRunsProjectReadBackAndDeleted` |
| `GroupsClient.ImportFromActiveDirectoryAsync` | Create Group from Active Directory | yes | `LiveDirectoryTests.ADirectoryGroupIsImportedWithItsMemberSynchronisedAndDeletedThenImportedAsAJobFoundAndDeleted` |
| `GroupsClient.ImportFromActiveDirectoryAsJobAsync` | Create Group from Active Directory | yes | `LiveDirectoryTests.ADirectoryGroupIsImportedWithItsMemberSynchronisedAndDeletedThenImportedAsAJobFoundAndDeleted` |
| `GroupsClient.UpdateAsync` | Update Group | yes | `LiveAreaTests.ALocalGroupIsMadeFoundRenamedGivenTheSuitesUserAndDeleted` |
| `GroupsClient.UpdateFromActiveDirectoryAsync` | Update Group from Active Directory | yes | `LiveDirectoryTests.ADirectoryGroupIsImportedWithItsMemberSynchronisedAndDeletedThenImportedAsAJobFoundAndDeleted` |
| `GroupsClient.UpdateFromActiveDirectoryAsJobAsync` | Update Group from Active Directory | yes | `LiveDirectoryTests.ADirectoryGroupIsImportedWithItsMemberSynchronisedAndDeletedThenImportedAsAJobFoundAndDeleted` |
| `GroupsClient.DeleteAsync` | Delete Group | yes | `LiveAreaTests.ALocalGroupIsMadeFoundRenamedGivenTheSuitesUserAndDeleted`, `LiveAreaTests.ACapabilityIsGrantedToARunsGroupOnTheRunsProjectReadBackAndDeleted` |
| `GroupsClient.AddUserAsync` | Add User to Group | yes | `LiveAreaTests.ALocalGroupIsMadeFoundRenamedGivenTheSuitesUserAndDeleted` |
| `GroupsClient.RemoveUserAsync` | Remove User from Group | yes | `LiveAreaTests.ALocalGroupIsMadeFoundRenamedGivenTheSuitesUserAndDeleted` |
| `GroupsClient.ListUsersAsync` | Get Users in Group | yes | `LiveAreaTests.ALocalGroupIsMadeFoundRenamedGivenTheSuitesUserAndDeleted` |
| `FavoritesClient.ListAsync` | Get Favorites for User | no | not live yet (#57) |
| `FavoritesClient.AddAsync` | Add Workbook to Favorites | no | not live yet (#57) |
| `FavoritesClient.AddAsync` | Add View to Favorites | no | not live yet (#57) |
| `FavoritesClient.AddAsync` | Add Data Source to Favorites | no | not live yet (#57) |
| `FavoritesClient.AddAsync` | Add Project to Favorites | no | not live yet (#57) |
| `FavoritesClient.AddAsync` | Add Flow to Favorites | no | no flow in the run's site, and the 2.0 client can't publish one |
| `FavoritesClient.RemoveAsync` | Delete Workbook from Favorites | no | not live yet (#57) |
| `FavoritesClient.RemoveAsync` | Delete View from Favorites | no | not live yet (#57) |
| `FavoritesClient.RemoveAsync` | Delete Data Source from Favorites | no | not live yet (#57) |
| `FavoritesClient.RemoveAsync` | Delete Project from Favorites | no | not live yet (#57) |
| `FavoritesClient.RemoveAsync` | Delete Flow from Favorites | no | no flow in the run's site, and the 2.0 client can't publish one |
| `PermissionsClient.GetAsync` | List Project Permissions | yes | `LiveAreaTests.ACapabilityIsGrantedToARunsGroupOnTheRunsProjectReadBackAndDeleted` |
| `PermissionsClient.GetAsync` | List Workbook Permissions | no | not live yet (#57) |
| `PermissionsClient.GetAsync` | List View Permissions | no | not live yet (#57) |
| `PermissionsClient.GetAsync` | List Data Source Permissions | no | not live yet (#57) |
| `PermissionsClient.GetAsync` | List Flow Permissions | no | no flow in the run's site, and the 2.0 client can't publish one |
| `PermissionsClient.GetAsync` | List Virtual Connection Permissions | no | no virtual connection on the server, and the 2.0 client can't make one |
| `PermissionsClient.GetAsync` | List Collection Permissions | no | the 2.0 client can't make a collection |
| `PermissionsClient.GetAsync` | List Database Permissions | no | not live yet (#57) |
| `PermissionsClient.GetAsync` | List Table Permissions | no | not live yet (#57) |
| `PermissionsClient.AddAsync` | Add Project Permissions | yes | `LiveAreaTests.ACapabilityIsGrantedToARunsGroupOnTheRunsProjectReadBackAndDeleted` |
| `PermissionsClient.AddAsync` | Add Workbook Permissions | no | not live yet (#57) |
| `PermissionsClient.AddAsync` | Add View Permissions | no | not live yet (#57) |
| `PermissionsClient.AddAsync` | Add Data Source Permissions | no | not live yet (#57) |
| `PermissionsClient.AddAsync` | Add Flow Permissions | no | no flow in the run's site, and the 2.0 client can't publish one |
| `PermissionsClient.AddAsync` | Add Virtual Connection Permissions | no | no virtual connection on the server, and the 2.0 client can't make one |
| `PermissionsClient.AddAsync` | Add Collection Permissions | no | the 2.0 client can't make a collection |
| `PermissionsClient.AddAsync` | Add Database Permissions | no | not live yet (#57) |
| `PermissionsClient.AddAsync` | Add Table Permissions | no | not live yet (#57) |
| `PermissionsClient.DeleteAsync` | Delete Project Permission | yes | `LiveAreaTests.ACapabilityIsGrantedToARunsGroupOnTheRunsProjectReadBackAndDeleted` |
| `PermissionsClient.DeleteAsync` | Delete Workbook Permission | no | not live yet (#57) |
| `PermissionsClient.DeleteAsync` | Delete View Permission | no | not live yet (#57) |
| `PermissionsClient.DeleteAsync` | Delete Data Source Permission | no | not live yet (#57) |
| `PermissionsClient.DeleteAsync` | Delete Flow Permission | no | no flow in the run's site, and the 2.0 client can't publish one |
| `PermissionsClient.DeleteAsync` | Delete Virtual Connection Permission | no | no virtual connection on the server, and the 2.0 client can't make one |
| `PermissionsClient.DeleteAsync` | Delete Collection Permission | no | the 2.0 client can't make a collection |
| `PermissionsClient.DeleteAsync` | Delete Database Permissions | no | not live yet (#57) |
| `PermissionsClient.DeleteAsync` | Delete Table Permissions | no | not live yet (#57) |
| `PermissionsClient.GetDefaultAsync` | List Default Permissions for Workbooks | no | not live yet (#57) |
| `PermissionsClient.GetDefaultAsync` | List Default Permissions for Data Sources | no | not live yet (#57) |
| `PermissionsClient.GetDefaultAsync` | List Default Permissions for Data Roles | no | not live yet (#57) |
| `PermissionsClient.GetDefaultAsync` | List Default Permissions for Flows | no | not live yet (#57) |
| `PermissionsClient.GetDefaultAsync` | List Default Permissions for Virtual Connections | no | not live yet (#57) |
| `PermissionsClient.GetDefaultAsync` | List Default Permissions for Databases | no | not live yet (#57) |
| `PermissionsClient.GetDefaultAsync` | List Default Permissions for Tables | no | not live yet (#57) |
| `PermissionsClient.AddDefaultAsync` | Add Default Permissions for Workbooks | no | not live yet (#57) |
| `PermissionsClient.AddDefaultAsync` | Add Default Permissions for Data Sources | no | not live yet (#57) |
| `PermissionsClient.AddDefaultAsync` | Add Default Permissions for Data Roles | no | not live yet (#57) |
| `PermissionsClient.AddDefaultAsync` | Add Default Permissions for Flows | no | not live yet (#57) |
| `PermissionsClient.AddDefaultAsync` | Add Default Permissions for Virtual Connections | no | not live yet (#57) |
| `PermissionsClient.AddDefaultAsync` | Add Default Permissions for Databases | no | not live yet (#57) |
| `PermissionsClient.AddDefaultAsync` | Add Default Permissions for Tables | no | not live yet (#57) |
| `PermissionsClient.DeleteDefaultAsync` | Delete Default Permission for Workbooks | no | not live yet (#57) |
| `PermissionsClient.DeleteDefaultAsync` | Delete Default Permission for Data Sources | no | not live yet (#57) |
| `PermissionsClient.DeleteDefaultAsync` | Delete Default Permission for Data Roles | no | not live yet (#57) |
| `PermissionsClient.DeleteDefaultAsync` | Delete Default Permission for Flows | no | not live yet (#57) |
| `PermissionsClient.DeleteDefaultAsync` | Delete Default Permission for Virtual Connections | no | not live yet (#57) |
| `PermissionsClient.DeleteDefaultAsync` | Delete Default Permission for Databases | no | not live yet (#57) |
| `PermissionsClient.DeleteDefaultAsync` | Delete Default Permission for Tables | no | not live yet (#57) |
| `TagsClient.AddAsync` | Add Tags to Workbook | yes | `LiveContentTests.SamplesAreTaggedWithASpaceAndACommaOneAtATimeAndInABatchAndOneIsDeleted` |
| `TagsClient.AddAsync` | Add Tags to View | no | not live yet (#57) |
| `TagsClient.AddAsync` | Add Tags to Data Source | no | not live yet (#57) |
| `TagsClient.AddAsync` | Add Tags to Virtual Connection | no | no virtual connection on the server, and the 2.0 client can't make one |
| `TagsClient.AddAsync` | Add Tags to Database | no | not live yet (#57) |
| `TagsClient.AddAsync` | Add Tags to Table | no | not live yet (#57) |
| `TagsClient.AddAsync` | Add Tags to Column | no | not live yet (#57) |
| `TagsClient.DeleteAsync` | Delete Tag from Workbook | yes | `LiveContentTests.SamplesAreTaggedWithASpaceAndACommaOneAtATimeAndInABatchAndOneIsDeleted` |
| `TagsClient.DeleteAsync` | Delete Tag from View | no | not live yet (#57) |
| `TagsClient.DeleteAsync` | Delete Tag from Data Source | no | not live yet (#57) |
| `TagsClient.DeleteAsync` | Delete Tag from Virtual Connection | no | no virtual connection on the server, and the 2.0 client can't make one |
| `TagsClient.DeleteAsync` | Delete Tag from Database | no | not live yet (#57) |
| `TagsClient.DeleteAsync` | Delete Tag from Table | no | not live yet (#57) |
| `TagsClient.DeleteAsync` | Delete Tag from Column | no | not live yet (#57) |
| `TagsClient.AddBatchAsync` | Batch Add Tags | yes | `LiveContentTests.SamplesAreTaggedWithASpaceAndACommaOneAtATimeAndInABatchAndOneIsDeleted`, `LiveContentTests.ThePublishedSampleDataSourceIsReadDownloadedUpdatedAndDeleted` |
| `TagsClient.DeleteBatchAsync` | Batch Delete Tags | yes | `LiveContentTests.SamplesAreTaggedWithASpaceAndACommaOneAtATimeAndInABatchAndOneIsDeleted`, `LiveContentTests.ThePublishedSampleDataSourceIsReadDownloadedUpdatedAndDeleted` |
| `MetadataClient.ListDatabasesAsync` | Query Databases | no | not live yet (#57) |
| `MetadataClient.ListDatabasesPageAsync` | Query Databases | no | not live yet (#57) |
| `MetadataClient.GetDatabaseAsync` | Query Database | no | not live yet (#57) |
| `MetadataClient.UpdateDatabaseAsync` | Update Database | no | not live yet (#57) |
| `MetadataClient.DeleteDatabaseAsync` | Delete Database | no | not live yet (#57) |
| `MetadataClient.ListTablesAsync` | Query Tables | no | not live yet (#57) |
| `MetadataClient.ListTablesPageAsync` | Query Tables | no | not live yet (#57) |
| `MetadataClient.GetTableAsync` | Query Table | no | not live yet (#57) |
| `MetadataClient.UpdateTableAsync` | Update Table | no | not live yet (#57) |
| `MetadataClient.DeleteTableAsync` | Delete Table | no | not live yet (#57) |
| `MetadataClient.ListColumnsAsync` | Query Columns in a Table | no | not live yet (#57) |
| `MetadataClient.UpdateColumnAsync` | Update Column | no | not live yet (#57) |
| `MetadataClient.DeleteColumnAsync` | Delete Column | no | not live yet (#57) |
| `MetadataClient.AddDataQualityWarningAsync` | Add Data Quality Warning | no | not live yet (#57) |
| `MetadataClient.AddDataQualityWarningAsync` | Add Data Quality Warning to a column | no | not live yet (#57) |
| `MetadataClient.UpdateDataQualityWarningAsync` | Update Data Quality Warning | no | not live yet (#57) |
| `MetadataClient.ListDataQualityWarningsAsync` | Query Data Quality Warning by Content | no | not live yet (#57) |
| `MetadataClient.ListDataQualityWarningsAsync` | Query Data Quality Warning by Content, of a column | no | not live yet (#57) |
| `MetadataClient.DeleteDataQualityWarningAsync` | Delete Data Quality Warning by ID | no | not live yet (#57) |
| `MetadataClient.DeleteDataQualityWarningsAsync` | Delete Data Quality Warning by Content | no | not live yet (#57) |
| `MetadataClient.DeleteDataQualityWarningsAsync` | Delete Data Quality Warning by Content, of a column | no | not live yet (#57) |
| `MetadataClient.QueryAsync` | Metadata API query | yes | `LiveAreaTests.AMetadataQueryReadsTheWorkbooksPageByPage` |
| `MetadataClient.QueryAsync<T>` | Metadata API query | yes | `LiveAreaTests.AMetadataQueryReadsTheWorkbooksPageByPage` |
| `MetadataClient.QueryPageAsync<T>` | Metadata API query | no | not live yet (#57) |
| `MetadataClient.ListNodesAsync<T>` | Metadata API query | yes | `LiveAreaTests.AMetadataQueryReadsTheWorkbooksPageByPage` |
| `WorkbooksClient.ListAsync` | Query Workbooks for Site | yes | `LiveContentTests.SampleWorkbooksAreListedGotDownloadedAndUpdated`, `LiveSite` |
| `WorkbooksClient.ListPageAsync` | Query Workbooks for Site | yes | `LiveContentTests.SampleWorkbooksAreListedGotDownloadedAndUpdated` |
| `WorkbooksClient.GetAsync` | Get Workbook | yes | `LiveContentTests.SampleWorkbooksAreListedGotDownloadedAndUpdated`, `LiveContentTests.SamplesAreTaggedWithASpaceAndACommaOneAtATimeAndInABatchAndOneIsDeleted` |
| `WorkbooksClient.ListForUserAsync` | Query Workbooks for User | yes | `LiveContentTests.SampleWorkbooksAreListedGotDownloadedAndUpdated` |
| `WorkbooksClient.ListConnectionsAsync` | Query Workbook Connections | yes | `LiveContentTests.SampleWorkbooksAreListedGotDownloadedAndUpdated` |
| `WorkbooksClient.UpdateAsync` | Update Workbook | yes | `LiveContentTests.SampleWorkbooksAreListedGotDownloadedAndUpdated` |
| `WorkbooksClient.UpdateConnectionAsync` | Update Workbook Connection | refused 400039 | `LiveContentTests.SampleWorkbooksAreListedGotDownloadedAndUpdated` |
| `WorkbooksClient.UpdateConnectionAsync` | Update Workbook Connection with an authentication type | no | REST API 3.27; the server has 3.25, so the client refuses it before sending |
| `WorkbooksClient.RunExtractRefreshAsync` | Update Workbook Now | refused 403180 | `LiveContentTests.SampleWorkbooksAreListedGotDownloadedAndUpdated` |
| `WorkbooksClient.DeleteAsync` | Delete Workbook | yes | `LiveContentTests.SamplesAreTaggedWithASpaceAndACommaOneAtATimeAndInABatchAndOneIsDeleted` |
| `WorkbooksClient.DownloadAsync` | Download Workbook | yes | `LiveContentTests.SampleWorkbooksAreListedGotDownloadedAndUpdated` |
| `WorkbooksClient.DownloadPreviewImageAsync` | Query Workbook Preview Image | yes | `LiveContentTests.SampleWorkbooksAreListedGotDownloadedAndUpdated` |
| `WorkbooksClient.DownloadPdfAsync` | Download Workbook PDF | yes | `LiveContentTests.SampleWorkbooksAreListedGotDownloadedAndUpdated` |
| `WorkbooksClient.DownloadPdfAsync` | Download Workbook PDF with view filters | yes | `LiveContentTests.SampleWorkbooksAreListedGotDownloadedAndUpdated` |
| `WorkbooksClient.DownloadPdfAsync` | Download Workbook PDF at a size | no | REST API 3.26; the server has 3.25, so the client refuses it before sending |
| `WorkbooksClient.ListRevisionsAsync` | Get Workbook Revisions | yes | `LiveContentTests.SampleWorkbooksAreListedGotDownloadedAndUpdated` |
| `WorkbooksClient.ListRevisionsPageAsync` | Get Workbook Revisions | yes | `LiveContentTests.SampleWorkbooksAreListedGotDownloadedAndUpdated` |
| `WorkbooksClient.DownloadRevisionAsync` | Download Workbook Revision | refused 400057 | `LiveContentTests.SampleWorkbooksAreListedGotDownloadedAndUpdated` |
| `WorkbooksClient.RemoveRevisionAsync` | Remove Workbook Revision | refused 403055 | `LiveContentTests.SampleWorkbooksAreListedGotDownloadedAndUpdated` |
| `ViewsClient.ListAsync` | Query Views for Site | yes | `LiveContentTests.ASampleViewIsListedGotAndExportedWithAViewFilter` |
| `ViewsClient.ListPageAsync` | Query Views for Site | no | not live yet (#57) |
| `ViewsClient.GetAsync` | Get View | yes | `LiveContentTests.ASampleViewIsListedGotAndExportedWithAViewFilter` |
| `ViewsClient.ListForWorkbookAsync` | Query Views for Workbook | yes | `LiveContentTests.ASampleViewIsListedGotAndExportedWithAViewFilter` |
| `ViewsClient.DownloadDataAsync` | Query View Data | yes | `LiveContentTests.ASampleViewIsListedGotAndExportedWithAViewFilter` |
| `ViewsClient.DownloadImageAsync` | Query View Image | no | not live yet (#57) |
| `ViewsClient.DownloadImageAsync` | Query View Image at a size | yes | `LiveContentTests.ASampleViewIsListedGotAndExportedWithAViewFilter` |
| `ViewsClient.DownloadPdfAsync` | Query View PDF | yes | `LiveContentTests.ASampleViewIsListedGotAndExportedWithAViewFilter` |
| `ViewsClient.DownloadCrosstabAsync` | Download View Crosstab Excel | yes | `LiveContentTests.ASampleViewIsListedGotAndExportedWithAViewFilter` |
| `ViewsClient.DownloadPreviewImageAsync` | Query View Preview Image | yes | `LiveContentTests.ASampleViewIsListedGotAndExportedWithAViewFilter` |
| `DataSourcesClient.ListAsync` | Query Data Sources | yes | `LiveContentTests.ThePublishedSampleDataSourceIsReadDownloadedUpdatedAndDeleted` |
| `DataSourcesClient.ListPageAsync` | Query Data Sources | yes | `LiveContentTests.ThePublishedSampleDataSourceIsReadDownloadedUpdatedAndDeleted` |
| `DataSourcesClient.GetAsync` | Query Data Source | yes | `LiveContentTests.ThePublishedSampleDataSourceIsReadDownloadedUpdatedAndDeleted` |
| `DataSourcesClient.ListConnectionsAsync` | Query Data Source Connections | yes | `LiveContentTests.ThePublishedSampleDataSourceIsReadDownloadedUpdatedAndDeleted` |
| `DataSourcesClient.UpdateAsync` | Update Data Source | yes | `LiveContentTests.ThePublishedSampleDataSourceIsReadDownloadedUpdatedAndDeleted` |
| `DataSourcesClient.UpdateConnectionAsync` | Update Data Source Connection | yes | `LiveContentTests.ThePublishedSampleDataSourceIsReadDownloadedUpdatedAndDeleted` |
| `DataSourcesClient.UpdateConnectionAsync` | Update Data Source Connection with an authentication type | no | REST API 3.27; the server has 3.25, so the client refuses it before sending |
| `DataSourcesClient.RunExtractRefreshAsync` | Update Data Source Now | refused 403180 | `LiveContentTests.ThePublishedSampleDataSourceIsReadDownloadedUpdatedAndDeleted` |
| `DataSourcesClient.DeleteAsync` | Delete Data Source | yes | `LiveContentTests.ThePublishedSampleDataSourceIsReadDownloadedUpdatedAndDeleted` |
| `DataSourcesClient.DownloadAsync` | Download Data Source | yes | `LiveContentTests.ThePublishedSampleDataSourceIsReadDownloadedUpdatedAndDeleted` |
| `DataSourcesClient.ListRevisionsAsync` | Get Data Source Revisions | yes | `LiveContentTests.ThePublishedSampleDataSourceIsReadDownloadedUpdatedAndDeleted` |
| `DataSourcesClient.ListRevisionsPageAsync` | Get Data Source Revisions | yes | `LiveContentTests.ThePublishedSampleDataSourceIsReadDownloadedUpdatedAndDeleted` |
| `DataSourcesClient.DownloadRevisionAsync` | Download Data Source Revision | refused 400057 | `LiveContentTests.ThePublishedSampleDataSourceIsReadDownloadedUpdatedAndDeleted` |
| `DataSourcesClient.RemoveRevisionAsync` | Remove Data Source Revision | refused 403055 | `LiveContentTests.ThePublishedSampleDataSourceIsReadDownloadedUpdatedAndDeleted` |

## VizPortalClient

The Vizportal client's methods and the live tests that call them, filled by S16 of the modernization plan
(`docs/plans/2026-10-28-modernization.md`), in the same columns.

| Method | Call | Live | Test, or why not |
|---|---|---|---|
