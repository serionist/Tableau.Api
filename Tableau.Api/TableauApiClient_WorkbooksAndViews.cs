using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Tableau.Api.Attributes;
using Tableau.Api.Enums;
using Tableau.Api.Exceptions;
using Tableau.Api.Models;

namespace Tableau.Api
{
    public partial class TableauApiClient
    {
        /// <summary>
        /// Deletes a project. Available in all versions
        /// </summary>
        /// <param name="session"></param>
        /// <param name="projectId">The id of the project to delete</param>
        /// <param name="cancelToken"></param>
        /// <returns></returns>
        /// <exception cref="TableauRequestException"></exception>
        /// <exception cref="TableauApiVersionException"></exception>
        /// <exception cref="TableauOnlineNotSupportedException"></exception>
        [ApiVersion(1, 0)]
        public async Task DeleteProject2(TableauSession session, string projectId, CancellationToken? cancelToken = null)
        {
            checkEndpointAvailability();
            checkNullParameters((nameof(session), session), (nameof(projectId), projectId));
            await apiRequest(buildUri($"sites/{session.SiteId}/projects/{projectId}"), HttpMethod.Delete, 204,
                cancelToken, session);

        }
        /// <summary>
        /// Adds one or more tags to the specified view. Available in all versions
        /// </summary>
        /// <param name="session"></param>
        /// <param name="viewId">The ID of the views to add tags to.</param>
        /// <param name="tagsToAdd">The view to add the tag to. If the view is already tagged with a tag that's included in the request body, those tags are ignored and the view retains them.</param>
        /// <param name="cancelToken"></param>
        /// <returns></returns>
        /// <exception cref="TableauRequestException"></exception>
        /// <exception cref="TableauApiVersionException"></exception>
        /// <exception cref="TableauOnlineNotSupportedException"></exception>
        [ApiVersion(1, 0)]
        public async Task<tagType[]> AddTagsToView(TableauSession session, string viewId, string[] tagsToAdd,
            CancellationToken? cancelToken = null)
        {
            checkEndpointAvailability();
            checkNullParameters((nameof(session), session), (nameof(viewId), viewId), (nameof(tagsToAdd), tagsToAdd));
            checkEmptyArrays((nameof(tagsToAdd), tagsToAdd));
            return (await getResponseAsObject<tagListType>(await apiRequest(
                buildUri($"sites/{session.SiteId}/views/{viewId}/tags"), HttpMethod.Put, 200, cancelToken, session,
                getObjectAsRequestContent(new tagListType()
                {
                    tag = tagsToAdd.Select(e => new tagType() {label = e}).ToArray()
                })))).tag;
        }
        /// <summary>
        /// Adds one or more tags to the specified workbook. Available in all versions
        /// </summary>
        /// <param name="session"></param>
        /// <param name="workbookId">The ID of the workbook to add tags to.</param>
        /// <param name="tagsToAdd">The workbook to add the tag to. If the workbook is already tagged with a tag that's included in the request body, those tags are ignored and the workbook retains them.</param>
        /// <param name="cancelToken"></param>
        /// <returns></returns>
        /// <exception cref="TableauRequestException"></exception>
        /// <exception cref="TableauApiVersionException"></exception>
        /// <exception cref="TableauOnlineNotSupportedException"></exception>
        [ApiVersion(1, 0)]
        public async Task<tagType[]> AddTagsToWorkbook(TableauSession session, string workbookId, string[] tagsToAdd,
            CancellationToken? cancelToken = null)
        {
            checkEndpointAvailability();
            checkNullParameters((nameof(session), session), (nameof(workbookId), workbookId), (nameof(tagsToAdd), tagsToAdd));
            checkEmptyArrays((nameof(tagsToAdd), tagsToAdd));
            return (await getResponseAsObject<tagListType>(await apiRequest(
                buildUri($"sites/{session.SiteId}/workbooks/{workbookId}/tags"), HttpMethod.Put, 200, cancelToken, session,
                getObjectAsRequestContent(new tagListType()
                {
                    tag = tagsToAdd.Select(e => new tagType() { label = e }).ToArray()
                })))).tag;
        }
        /// <summary>
        /// Returns all the views for the specified workbook, optionally including usage statistics. Available in all versions
        /// </summary>
        /// <param name="session"></param>
        /// <param name="workbookId">The ID of the workbook to get the views for.</param>
        /// <param name="includeUsageStatistics">true to return usage statistics. The default is false.</param>
        /// <param name="cancelToken"></param>
        /// <returns></returns>
        /// <exception cref="TableauRequestException"></exception>
        /// <exception cref="TableauApiVersionException"></exception>
        /// <exception cref="TableauOnlineNotSupportedException"></exception>
        [ApiVersion(1, 0)]
        public async Task<viewType[]> QueryViewsForWorkbook(TableauSession session, string workbookId,
            bool includeUsageStatistics = false, CancellationToken? cancelToken = null)
        {
            checkEndpointAvailability();
            checkNullParameters((nameof(session), session), (nameof(workbookId), workbookId));
            return (await getResponseAsObject<viewListType>(await apiRequest(
                buildUri($"sites/{session.SiteId}/workbooks/{workbookId}/views",
                    ("includeUsageStatistics", includeUsageStatistics.ToString())), HttpMethod.Get, 200, cancelToken,
                session))).view;

        }
        /// <summary>
        /// Returns a data stream of the specified view rendered as data in comma-separated-value (CSV) format. Save it to file, or read it using StreamReader. Versions 2.8+
        /// </summary>
        /// <param name="session"></param>
        /// <param name="viewId">The ID of the view to render as data.</param>
        /// <param name="cancelToken"></param>
        /// <param name="filters">When provided, the filters will be set to the specified values on the view before downloading data.</param>
        /// <returns></returns>
        /// <exception cref="TableauRequestException"></exception>
        /// <exception cref="TableauApiVersionException"></exception>
        /// <exception cref="TableauOnlineNotSupportedException"></exception>
        [ApiVersion(2,8)]
        public async Task<Stream> QueryViewData(TableauSession session, string viewId,
            CancellationToken? cancelToken = null, params (string filterName, string filterValue)[] filters)
        {
            checkEndpointAvailability();
            checkNullParameters((nameof(session), session), (nameof(viewId), viewId));
            foreach (var (filterName, filterValue) in filters)
                checkNullParameters((nameof(filterName), filterName), (nameof(filterValue), filterValue));
            return await (await apiRequest(
                    buildUri($"sites/{session.SiteId}/views/{viewId}/data",
                        filters.Select(e =>
                            (e.filterName.StartsWith("vf_", StringComparison.CurrentCultureIgnoreCase)
                                ? e.filterName.Substring(3)
                                : e.filterName, e.filterValue)).ToArray()), HttpMethod.Get, 200, cancelToken, session))
                .ReadAsStreamAsync();
        }
        /// <summary>
        /// Returns an image of the specified view as data stream. Versions 2.5+
        /// </summary>
        /// <param name="session"></param>
        /// <param name="viewId">The ID of the view to return an image for.</param>
        /// <param name="imageWidth">(Optional) The resolution of the image. By default, the width of the returned image is 784 pixels. If you set this parameter value to high, the width of the returned image is 1568 pixels. For both resolutions, the height varies to preserve the aspect ratio of the view.</param>
        /// <param name="cancelToken"></param>
        /// <param name="filters">When provided, the filters will be set to the specified values on the view before downloading data.</param>
        /// <returns></returns>
        /// <exception cref="TableauRequestException"></exception>
        /// <exception cref="TableauApiVersionException"></exception>
        /// <exception cref="TableauOnlineNotSupportedException"></exception>
        [ApiVersion(2, 5)]
        public async Task<Stream> QueryViewImage(TableauSession session, string viewId, TableauImageWidth imageWidth = TableauImageWidth.Default_784px,
            CancellationToken? cancelToken = null, params (string filterName, string filterValue)[] filters)
        {
            checkEndpointAvailability();
            checkNullParameters((nameof(session), session), (nameof(viewId), viewId));
            foreach (var (filterName, filterValue) in filters)
                checkNullParameters((nameof(filterName), filterName), (nameof(filterValue), filterValue));
            var args = new List<(string,string)>();
            if (imageWidth == TableauImageWidth.High_1568)
                args.Add(("resolution", "high"));
            args.AddRange(filters.Select(e =>
                (e.filterName.StartsWith("vf_", StringComparison.CurrentCultureIgnoreCase)
                    ? e.filterName.Substring(3)
                    : e.filterName, e.filterValue)));

            return await (await apiRequest(
                    buildUri($"sites/{session.SiteId}/views/{viewId}/image", args.ToArray()), HttpMethod.Get, 200, cancelToken, session))
                .ReadAsStreamAsync();
        }

        /// <summary>
        /// Returns a specified view rendered as a .pdf file as data stream. Versions 2.8+
        /// </summary>
        /// <param name="session"></param>
        /// <param name="viewId">The ID of the view to render as a .pdf file.</param>
        /// <param name="cancelToken"></param>
        /// <param name="orientation"></param>
        /// <param name="filters">When provided, the filters will be set to the specified values on the view before downloading data.</param>
        /// <param name="pageType"></param>
        /// <returns></returns>
        /// <exception cref="TableauRequestException"></exception>
        /// <exception cref="TableauApiVersionException"></exception>
        /// <exception cref="TableauOnlineNotSupportedException"></exception>
        [ApiVersion(2, 8)]
        public async Task<Stream> QueryViewPDF(TableauSession session, string viewId, 
            CancellationToken? cancelToken = null, PageType pageType = PageType.Legal, Orientation orientation = Orientation.Portrait,
            (string filterName, string filterValue)[] filters = null)
        {
            checkEndpointAvailability();
            checkNullParameters((nameof(session), session), (nameof(viewId), viewId));
            foreach (var (filterName, filterValue) in filters ?? new (string filterName, string filterValue)[0])
                checkNullParameters((nameof(filterName), filterName), (nameof(filterValue), filterValue));
            var args = new List<(string, string)>
            {
                ("type", Enum.GetName(typeof(PageType), pageType)),
                ("orientation", Enum.GetName(typeof(Orientation), orientation))
            };
            if (filters != null)
                args.AddRange(filters.Select(e =>
                    (!e.filterName.StartsWith("vf_", StringComparison.CurrentCultureIgnoreCase)
                        ? $"vf_{e.filterName}"
                        : e.filterName, e.filterValue)));

            return await (await apiRequest(
                    buildUri($"sites/{session.SiteId}/views/{viewId}/pdf", args.ToArray()), HttpMethod.Get, 200, cancelToken, session))
                .ReadAsStreamAsync();
        }

        /// <summary>
        /// Returns the thumbnail image for the specified view as data stream. Available in all versions
        /// </summary>
        /// <param name="session"></param>
        /// <param name="workbookId">The ID of the workbook that the view belongs to</param>
        /// <param name="viewId">The ID of the view to return an image for.</param>
        /// <param name="cancelToken"></param>
        /// <returns></returns>
        /// <exception cref="TableauRequestException"></exception>
        /// <exception cref="TableauApiVersionException"></exception>
        /// <exception cref="TableauOnlineNotSupportedException"></exception>
        [ApiVersion(1, 0)]
        public async Task<Stream> QueryViewPreviewImage(TableauSession session, string workbookId, string viewId,
            CancellationToken? cancelToken = null)
        {
            checkEndpointAvailability();
            checkNullParameters((nameof(session), session), (nameof(viewId), viewId));
            return await (await apiRequest(
                    buildUri($"sites/{session.SiteId}/workbooks/{workbookId}/views/{viewId}/previewImage"), HttpMethod.Get, 200, cancelToken, session))
                .ReadAsStreamAsync();
        }
        /// <summary>
        /// Returns information about the specified workbook, including information about views and tags. Available in all versions
        /// </summary>
        /// <param name="session"></param>
        /// <param name="workbookId">The ID of the workbook to return information about.</param>
        /// <param name="cancelToken"></param>
        /// <returns></returns>
        [ApiVersion(1, 0)]
        public async Task<workbookType> QueryWorkbook(TableauSession session, string workbookId,
            CancellationToken? cancelToken = null)
        {
            checkEndpointAvailability();
            checkNullParameters((nameof(session), session), (nameof(workbookId), workbookId));
            return await getResponseAsObject<workbookType>(await apiRequest(
                buildUri($"sites/{session.SiteId}/workbooks/{workbookId}"), HttpMethod.Get, 200, cancelToken, session));
        }
        /// <summary>
        /// Returns a list of data connections for the specific workbook. Available in all versions
        /// </summary>
        /// <param name="session"></param>
        /// <param name="workbookId">The ID of the workbook to return connection information about.</param>
        /// <param name="cancelToken"></param>
        /// <returns></returns>
        /// <exception cref="TableauRequestException"></exception>
        /// <exception cref="TableauApiVersionException"></exception>
        /// <exception cref="TableauOnlineNotSupportedException"></exception>
        [ApiVersion(2, 0)]
        public async Task<connectionType[]> QueryWorkbookConnections(TableauSession session, string workbookId,
            CancellationToken? cancelToken = null)
        {
            checkEndpointAvailability();
            checkNullParameters((nameof(session), session), (nameof(workbookId), workbookId));
            return (await getResponseAsObject<connectionListType>(await apiRequest(
                buildUri($"sites/{session.SiteId}/workbooks/{workbookId}/connections"), HttpMethod.Get, 200,
                cancelToken, session))).connection;
        }

        /// <summary>
        /// Returns the thumbnail image for the specified workbook as data stream. Available in all versions
        /// </summary>
        /// <param name="session"></param>
        /// <param name="workbookId">The ID of the workbook to return an image for.</param>
        /// <param name="cancelToken"></param>
        /// <returns></returns>
        /// <exception cref="TableauRequestException"></exception>
        /// <exception cref="TableauApiVersionException"></exception>
        /// <exception cref="TableauOnlineNotSupportedException"></exception>
        [ApiVersion(1, 0)]
        public async Task<Stream> QueryWorkbookPreviewImage(TableauSession session, string workbookId,
            CancellationToken? cancelToken = null)
        {
            checkEndpointAvailability();
            checkNullParameters((nameof(session), session), (nameof(workbookId), workbookId));
            return await (await apiRequest(
                    buildUri($"sites/{session.SiteId}/workbooks/{workbookId}/previewImage"), HttpMethod.Get, 200, cancelToken, session))
                .ReadAsStreamAsync();
        }

        /// <summary>
        /// Returns the PDF for the specified workbook as data stream. Available 3.4+
        /// </summary>
        /// <param name="session"></param>
        /// <param name="workbookId">The ID of the workbook to return an image for.</param>
        /// <param name="pageType"></param>
        /// <param name="cancelToken"></param>
        /// <param name="orientation"></param>
        /// <returns></returns>
        /// <exception cref="TableauRequestException"></exception>
        /// <exception cref="TableauApiVersionException"></exception>
        /// <exception cref="TableauOnlineNotSupportedException"></exception>
        [ApiVersion(3, 4)]
        public async Task<Stream> QueryWorkbookPDF(TableauSession session, string workbookId, PageOrientation orientation = PageOrientation.Portrait,
            PageType pageType = PageType.Legal,
            CancellationToken? cancelToken = null)
        {
            checkEndpointAvailability();
            checkNullParameters((nameof(session), session), (nameof(workbookId), workbookId));
            return await (await apiRequest(
                    buildUri($"sites/{session.SiteId}/workbooks/{workbookId}/pdf",
                        ("type", Enum.GetName(typeof(PageType), pageType)),
                        ("orientation", Enum.GetName(typeof(PageOrientation), orientation))), HttpMethod.Get, 200,
                    cancelToken, session))
                .ReadAsStreamAsync();
        }

        /// <summary>
        /// Queries workbooks for specified site. Versions 2.2+
        /// </summary>
        /// <param name="session"></param>
        /// <param name="pageSize">(Optional) The number of items to return in one response. The minimum is 1. The maximum is 1000. The default is 100.</param>
        /// <param name="pageNumber">(Optional) The offset for paging. The default is 1.</param>
        /// <param name="fieldsExpression">(Optional) An expression that lets you specify the set of available fields to return. You can qualify the return values based upon predefined keywords such as _all_ or _default_, and you can specify individual fields for the workbooks or other supported resources. You can include multiple field expressions in a request.</param>
        /// <param name="filterExpression">(Optional) An expression that lets you specify a subset of workbooks to return. You can filter on predefined fields such as name, tags, and createdAt. You can include multiple filter expressions.</param>
        /// <param name="sortExpresion">Optional) An expression that lets you specify the order in which workbook information is returned. If you do not specify a sort expression, the sort order of the information that's returned is undefined.</param>
        /// <param name="cancelToken"></param>
        /// <returns></returns>
        /// <exception cref="TableauRequestException"></exception>
        /// <exception cref="TableauApiVersionException"></exception>
        /// <exception cref="TableauOnlineNotSupportedException"></exception>
        [ApiVersion(2, 3)]
        public async Task<(paginationType pagination, workbookType[] views)> QueryWorkbooksForSite(TableauSession session,
            int pageSize = 100, int pageNumber = 1, string fieldsExpression = null,
            string filterExpression = null, string sortExpresion = null, CancellationToken? cancelToken = null)
        {
            checkEndpointAvailability();
            checkNullParameters((nameof(session), session));
            checkParametersBetween((nameof(pageSize), pageSize, 1, 1000), (nameof(pageNumber), pageNumber, 1, int.MaxValue));
            var args = new List<(string, string)>()
            {
                ("pageSize", pageSize.ToString()),
                ("pageNumber", pageNumber.ToString())
            };
              if (!string.IsNullOrEmpty(fieldsExpression))
                args.Add(("fields", fieldsExpression));
            if (!string.IsNullOrEmpty(filterExpression))
                args.Add(("filter", filterExpression));
            if (!string.IsNullOrEmpty(sortExpresion))
                args.Add(("sort", sortExpresion));
            var obj = await getResponseAsObject<paginationType, workbookListType>(await apiRequest(buildUri($"sites/{session.SiteId}/workbooks", args.ToArray()), HttpMethod.Get, 200,
                cancelToken, session));
            return (obj.Item1, obj.Item2.workbook);

        }
        /// <summary>
        /// Returns the workbooks that the specified user owns in addition to those that the user has Read (view) permissions for. Available in all versions
        /// </summary>
        /// <param name="session"></param>
        /// <param name="userId">The ID of the user to get workbooks for.</param>
        /// <param name="isOwner">(Optional) true to return only workbooks that the specified user owns, or false to return all workbooks that the specified user has at least read access to. The default is false.</param>
        /// <param name="pageSize">(Optional) The number of items to return in one response. The minimum is 1. The maximum is 1000. The default is 100</param>
        /// <param name="pageNumber">(Optional) The offset for paging. The default is 1.</param>
        /// <param name="cancelToken"></param>
        /// <returns></returns>
        /// <exception cref="TableauRequestException"></exception>
        /// <exception cref="TableauApiVersionException"></exception>
        /// <exception cref="TableauOnlineNotSupportedException"></exception>
        [ApiVersion(1, 0)]
        public async Task<(paginationType pagination, workbookType[] workbooks)> QueryWorkbooksForUser(
            TableauSession session, string userId, bool isOwner = false, int pageSize = 100, int pageNumber = 1,
            CancellationToken? cancelToken = null)
        {
            checkEndpointAvailability();
            checkNullParameters((nameof(session), session), (nameof(userId), userId));
            var obj = await getResponseAsObject<paginationType, workbookListType>(
                await apiRequest(
                    buildUri($"sites/{session.SiteId}/users/{userId}/workbooks", ("ownedBy", isOwner.ToString()),
                        ("pageSize", pageSize.ToString()), ("pageNumber", pageNumber.ToString())), HttpMethod.Get, 200,
                    cancelToken, session));
            return (obj.Item1, obj.Item2.workbook);
        }
        /// <summary>
        /// Downloads a workbook in .twb or .twbx format. Available in all versions
        /// </summary>
        /// <param name="session"></param>
        /// <param name="workbookId">The ID of the workbook to download.</param>
        /// <param name="includeExtract">(Optional) The extract-value is a Boolean value (False or True). When the workbook specified for download has an extract, if you add the parameter ?includeExtract=False, the extract is not included when you download the workbook. You can use this option to improve performance if you are downloading workbooks or data sources that have large extracts.</param>
        /// <param name="cancelToken"></param>
        /// <returns></returns>
        /// <exception cref="TableauRequestException"></exception>
        /// <exception cref="TableauApiVersionException"></exception>
        /// <exception cref="TableauOnlineNotSupportedException"></exception>
        [ApiVersion(2, 0)]
        public async Task<(Stream stream, WorkbookFileType type)> DownloadWorkbook(TableauSession session, string workbookId,
            bool includeExtract = true, CancellationToken? cancelToken = null)
        {
            checkEndpointAvailability();
            checkNullParameters((nameof(session), session), (nameof(workbookId), workbookId));
            var resp = (await apiRequest(
                buildUri($"sites/{session.SiteId}/workbooks/{workbookId}/content",
                    ("includeExtract", includeExtract.ToString())), HttpMethod.Get, 200, cancelToken, session));

            return (await resp.ReadAsStreamAsync(),
                resp.Headers.ContentType.MediaType.Equals("application/xml", StringComparison.CurrentCultureIgnoreCase)
                    ? WorkbookFileType.twb
                    : WorkbookFileType.twbx);
        }
        /// <summary>
        /// Modifies an existing workbook, allowing you to change the owner or project that the workbook belongs to and whether the workbook shows views in tabs. Available in all versions
        /// </summary>
        /// <param name="session"></param>
        /// <param name="workbookId">The ID of the workbook to update.</param>
        /// <param name="newName">(Optional) New name for the workbook being updated.</param>
        /// <param name="newShowTabs">(Optional) Specify true to have the published workbook show views in tabs; otherwise, false. The default is false.</param>
        /// <param name="newProjectId">(Optional) The ID of a project to assign the workbook to.</param>
        /// <param name="newOwnerId">(Optional) The ID of a user to assign the workbook to as owner.</param>
        /// <param name="cancelToken"></param>
        /// <returns></returns>
        /// <exception cref="TableauRequestException"></exception>
        /// <exception cref="TableauApiVersionException"></exception>
        /// <exception cref="TableauOnlineNotSupportedException"></exception>
        [ApiVersion(2, 0)]
        public async Task<workbookType> UpdateWorkbook(TableauSession session, string workbookId, 
            bool? newShowTabs = null, string newProjectId = null, string newOwnerId = null, string newName = null,
            CancellationToken? cancelToken = null)
        {
            checkEndpointAvailability();
            checkNullParameters((nameof(session), session), (nameof(workbookId), workbookId));
            return await getResponseAsObject<workbookType>(await apiRequest(
                buildUri($"sites/{session.SiteId}/workbooks/{workbookId}"), HttpMethod.Put, 200, cancelToken, session,
                getObjectAsRequestContent(new workbookType()
                {
                    name = newName,
                    showTabs = newShowTabs ?? false,
                    showTabsSpecified = newShowTabs.HasValue,
                    project = new projectType() {id = newProjectId},
                    owner = new userType() {id = newOwnerId}
                })));
        }
        /// <summary>
        /// Updates the server address, port, username, or password for the specified workbook connection. Available in all versions
        /// </summary>
        /// <param name="session"></param>
        /// <param name="workbookId">The ID of the workbook to update.</param>
        /// <param name="connectionId">The ID of the connection to update.</param>
        /// <param name="serverAddress">The new server for the connection.</param>
        /// <param name="serverPort">The new port for the connection.</param>
        /// <param name="userName">The new username for the connection.</param>
        /// <param name="password">The new password for the connection.</param>
        /// <param name="cancelToken"></param>
        /// <returns></returns>
        /// <exception cref="TableauRequestException"></exception>
        /// <exception cref="TableauApiVersionException"></exception>
        /// <exception cref="TableauOnlineNotSupportedException"></exception>
        [ApiVersion(2, 0)]
        public async Task<connectionType> UpdateWorkbookConnection(TableauSession session, string workbookId,
            string connectionId, string serverAddress, int serverPort, string userName, string password,
            CancellationToken? cancelToken = null)
        {
            checkEndpointAvailability();
            checkNullParameters((nameof(session), session), (nameof(workbookId), workbookId), (nameof(connectionId), connectionId), (nameof(serverAddress), serverAddress), (nameof(userName), userName), (nameof(password), password));
            return await getResponseAsObject<connectionType>(await apiRequest(
                buildUri($"sites/{session.SiteId}/workbooks/{workbookId}/connections/{connectionId}"), HttpMethod.Put,
                200, cancelToken, session,
                getObjectAsRequestContent(new connectionType()
                {
                    serverPort = serverPort.ToString(),
                    serverAddress = serverAddress,
                    userName = userName,
                    password = password
                })));
        }

        /// <summary>
        /// Runs an extract refresh on the specified workbook. Version 2.8+. 
        /// 
        /// This method runs an extract refresh for the specified workbook, with no need to associate that extract refresh with a scheduled task. This method is the equivalent of selecting a workbook using the Tableau Server UI, and then selecting Refresh Extracts from the menu (also known as a "manual refresh").
        /// </summary>
        /// <param name="session"></param>
        /// <param name="workbookId">The ID of the data source to refresh.</param>
        /// <param name="cancelToken"></param>
        /// <returns></returns>
        /// <exception cref="TableauRequestException"></exception>
        /// <exception cref="TableauApiVersionException"></exception>
        /// <exception cref="TableauOnlineNotSupportedException"></exception>
        [ApiVersion(2, 8)]
        public async Task<jobType> UpdateWorkbookNow(TableauSession session, string workbookId,
            CancellationToken? cancelToken = null)
        {
            checkEndpointAvailability();
            checkNullParameters((nameof(session), session), (nameof(workbookId), workbookId));
            return await getResponseAsObject<jobType>(await apiRequest(
                buildUri($"sites/{session.SiteId}/workbooks/{workbookId}/refresh"), HttpMethod.Post, 202,
                cancelToken, session, getObjectAsRequestContent(null)));

        }
        /// <summary>
        /// Deletes a workbook. When a workbook is deleted, all of its assets are also deleted, including associated views, data connections, and so on. Available in all versions
        /// </summary>
        /// <param name="session"></param>
        /// <param name="workbookId">The ID of the workbook to remove.</param>
        /// <param name="cancelToken"></param>
        /// <returns></returns>
        /// <exception cref="TableauRequestException"></exception>
        /// <exception cref="TableauApiVersionException"></exception>
        /// <exception cref="TableauOnlineNotSupportedException"></exception>
        [ApiVersion(2, 0)]
        public async Task DeleteWorkbook(TableauSession session, string workbookId,
            CancellationToken? cancelToken = null)
        {
            checkEndpointAvailability();
            checkNullParameters((nameof(session), session), (nameof(workbookId), workbookId));
            await apiRequest(buildUri($"sites/{session.SiteId}/workbooks/{workbookId}"), HttpMethod.Delete, 204,
                cancelToken, session);
        }

        /// <summary>
        /// Deletes a tag from the specified view.
        /// </summary>
        /// <param name="session"></param>
        /// <param name="viewId">The ID of the view to remove the tag from.</param>
        ///<param name="tagName">The name of the tag to remove from the view.</param>
        /// <param name="cancelToken"></param>
        /// <returns></returns>
        /// <exception cref="TableauRequestException"></exception>
        /// <exception cref="TableauApiVersionException"></exception>
        /// <exception cref="TableauOnlineNotSupportedException"></exception>
        [ApiVersion(1, 0)]
        public async Task DeleteTagFromView(TableauSession session, string viewId, string tagName,
            CancellationToken? cancelToken = null)
        {
            checkEndpointAvailability();
            checkNullParameters((nameof(session), session), (nameof(viewId), viewId), (nameof(tagName), tagName));
            await apiRequest(buildUri($"sites/{session.SiteId}/views/{viewId}/tags/{tagName}"), HttpMethod.Delete, 204,
                cancelToken, session);
        }

        /// <summary>
        /// Deletes a tag from the specified workbook. Available in all versions
        /// </summary>
        /// <param name="session"></param>
        /// <param name="workbookId">The ID of the workbook to remove the tag from.</param>
        ///<param name="tagName">The name of the tag to remove from the workbook.</param>
        /// <param name="cancelToken"></param>
        /// <returns></returns>
        /// <exception cref="TableauRequestException"></exception>
        /// <exception cref="TableauApiVersionException"></exception>
        /// <exception cref="TableauOnlineNotSupportedException"></exception>
        [ApiVersion(1, 0)]
        public async Task DeleteTagFromWorkbook(TableauSession session, string workbookId, string tagName,
            CancellationToken? cancelToken = null)
        {
            checkEndpointAvailability();
            checkNullParameters((nameof(session), session), (nameof(workbookId), workbookId), (nameof(tagName), tagName));
            await apiRequest(buildUri($"sites/{session.SiteId}/workbooks/{workbookId}/tags/{tagName}"), HttpMethod.Delete, 204,
                cancelToken, session);
        }
    }
}
