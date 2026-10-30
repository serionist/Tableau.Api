using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Tableau.Api.Attributes;
using Tableau.Api.Exceptions;
using Tableau.Api.Models;

namespace Tableau.Api;

public partial class TableauApiClient
{
    /// <summary>
    /// Creates a site. Not available in Tableau Online. Versions 1.0+
    /// </summary>
    /// <param name="session">The session to use</param>
    /// <param name="name">	The name of the site.</param>
    /// <param name="contentUrl">The site URL. This value can contain only characters that are valid in a URL.</param>
    /// <param name="adminMode">(Optional) Specify ContentAndUsers to allow site administrators to use the server interface and tabcmd commands to add and remove users. (Specifying this option does not give site administrators permissions to manage users using the REST API.) Specify ContentOnly to prevent site administrators from adding or removing users. (Server administrators can always add or remove users.)</param>
    /// <param name="tierCreatorCapacity">(Optional) The maximum number of users for the site in each of the user-based license types (Creator, Explorer and Viewer). If you do not specify this value, the limit depends on the type of licensing configured for the server. For user-based license types, the maximum number of users is set by the licenses activated on that server. For core-based licensing, there is no limit to the number of users; if you specify a maximum value, only licensed users are counted and server administrators are excluded</param>
    /// <param name="tierExplorerCapacity">(Optional) The maximum number of users for the site in each of the user-based license types (Creator, Explorer and Viewer). If you do not specify this value, the limit depends on the type of licensing configured for the server. For user-based license types, the maximum number of users is set by the licenses activated on that server. For core-based licensing, there is no limit to the number of users; if you specify a maximum value, only licensed users are counted and server administrators are excluded</param>
    /// <param name="tierViewerCapacity">(Optional) The maximum number of users for the site in each of the user-based license types (Creator, Explorer and Viewer). If you do not specify this value, the limit depends on the type of licensing configured for the server. For user-based license types, the maximum number of users is set by the licenses activated on that server. For core-based licensing, there is no limit to the number of users; if you specify a maximum value, only licensed users are counted and server administrators are excluded</param>
    /// <param name="storageQuotaMb">(Optional) The maximum amount of space for the new site, in megabytes. If you set a quota and the site exceeds it, publishers will be prevented from uploading new content until the site is under the limit again.</param>
    /// <param name="disableSubscriptions">(Optional) Specify true to prevent users from being able to subscribe to workbooks on the specified site. The default is false.</param>
    /// <param name="cancelToken"></param>
    /// <returns></returns>
    /// <exception cref="TableauRequestException"></exception>
    /// <exception cref="TableauApiVersionException"></exception>
    /// <exception cref="TableauOnlineNotSupportedException"></exception>
    [ApiVersion(1, 0)]
    [OnPremiseOnly]
    public async Task<siteType> CreateSite(TableauSession session, string name, string contentUrl,
        siteTypeAdminMode? adminMode = null, int? tierCreatorCapacity = null, int? tierExplorerCapacity = null,
        int? tierViewerCapacity = null, int? storageQuotaMb = null, bool? disableSubscriptions = false,
        CancellationToken? cancelToken = null)
    {
        checkEndpointAvailability();
        checkNullParameters((nameof(session), session), (nameof(name), name), (nameof(contentUrl), contentUrl));
        return await getResponseAsObject<siteType>(await apiRequest(buildUri($"sites"),
            HttpMethod.Post, 201, cancelToken, session,
            getObjectAsRequestContent(new siteType()
            {
                name = name,
                contentUrl = contentUrl,
                adminMode = adminMode ?? siteTypeAdminMode.ContentOnly,
                adminModeSpecified = adminMode.HasValue,
                tierCreatorCapacity = tierCreatorCapacity?.ToString(CultureInfo.InvariantCulture),
                tierExplorerCapacity = tierExplorerCapacity?.ToString(CultureInfo.InvariantCulture),
                tierViewerCapacity = tierViewerCapacity?.ToString(CultureInfo.InvariantCulture),
                storageQuota = storageQuotaMb?.ToString(CultureInfo.InvariantCulture),
                disableSubscriptions = disableSubscriptions ?? false,
                disableSubscriptionsSpecified = disableSubscriptions.HasValue
            })));

    }

    /// <summary>
    /// Queries a single Site by siteId. Available in all versions
    /// </summary>
    /// <param name="session"></param>
    /// <param name="siteId">The id of the site to query</param>
    /// <param name="cancelToken"></param>
    /// <returns></returns>
    /// <exception cref="TableauRequestException"></exception>
    /// <exception cref="TableauApiVersionException"></exception>
    /// <exception cref="TableauOnlineNotSupportedException"></exception>
    [ApiVersion(1, 0)]
    public async Task<siteType> QuerySiteById(TableauSession session, string siteId,
        CancellationToken? cancelToken = null)
    {
        checkEndpointAvailability();
        checkNullParameters((nameof(session), session), (nameof(siteId), siteId));
        return await getResponseAsObject<siteType>(await apiRequest(buildUri($"sites/{siteId}"), HttpMethod.Get,
            200, cancelToken, session));
    }

    /// <summary>
    /// Queries a single Site by siteName. Available in all versions
    /// </summary>
    /// <param name="session"></param>
    /// <param name="siteName">The name of the site to query</param>
    /// <param name="cancelToken"></param>
    /// <returns></returns>
    /// <exception cref="TableauRequestException"></exception>
    /// <exception cref="TableauApiVersionException"></exception>
    /// <exception cref="TableauOnlineNotSupportedException"></exception>
    [ApiVersion(1, 0)]
    public async Task<siteType> QuerySiteByName(TableauSession session, string siteName,
        CancellationToken? cancelToken = null)
    {
        checkEndpointAvailability();
        checkNullParameters((nameof(session), session), (nameof(siteName), siteName));
        return await getResponseAsObject<siteType>(await apiRequest(buildUri($"sites/{siteName}", ("key", "name")), HttpMethod.Get,
            200, cancelToken, session));
    }

    /// <summary>
    /// Queries a single Site by siteContentUrl. Available in all versions
    /// </summary>
    /// <param name="session"></param>
    /// <param name="siteContentUrl">The content URL of the site to query</param>
    /// <param name="cancelToken"></param>
    /// <returns></returns>
    /// <exception cref="TableauRequestException"></exception>
    /// <exception cref="TableauApiVersionException"></exception>
    /// <exception cref="TableauOnlineNotSupportedException"></exception>
    [ApiVersion(1, 0)]
    public async Task<siteType> QuerySiteByContentUrl(TableauSession session, string siteContentUrl,
        CancellationToken? cancelToken = null)
    {
        checkEndpointAvailability();
        checkNullParameters((nameof(session), session), (nameof(siteContentUrl), siteContentUrl));
        return await getResponseAsObject<siteType>(await apiRequest(buildUri($"sites/{siteContentUrl}", ("key", "contentUrl")), HttpMethod.Get,
            200, cancelToken, session));
    }
    /// <summary>
    /// Queries sites. Not available in Tableau Online. Available in all versions
    /// </summary>
    /// <param name="session"></param>
    /// <param name="pageSize">(Optional) The number of items to return in one response. The minimum is 1. The maximum is 1000. The default is 100.</param>
    /// <param name="pageNumber">(Optional) The offset for paging. The default is 1.</param>
    /// <param name="cancelToken"></param>
    /// <returns></returns>
    /// <exception cref="TableauRequestException"></exception>
    /// <exception cref="TableauApiVersionException"></exception>
    /// <exception cref="TableauOnlineNotSupportedException"></exception>
    [ApiVersion(1, 0)]
    [OnPremiseOnly]
    public async Task<(paginationType pagination, siteListType sites)> QuerySites(TableauSession session, int pageSize = 100, int pageNumber = 1,
        CancellationToken? cancelToken = null)
    {
        checkEndpointAvailability();
        checkNullParameters((nameof(session), session));
        checkParametersBetween((nameof(pageSize), pageSize, 1, 1000), (nameof(pageNumber), pageNumber, 1, int.MaxValue));
        return await getResponseAsObject<paginationType, siteListType>(await apiRequest(
            buildUri($"sites", ("pageSize", pageSize.ToString(CultureInfo.InvariantCulture)),
                ("pageNumber", pageNumber.ToString(CultureInfo.InvariantCulture))),
            HttpMethod.Get, 200,
            cancelToken, session));

    }

    /// <summary>
    /// Queries views for specified site. Versions 2.2+
    /// </summary>
    /// <param name="session"></param>
    /// <param name="pageSize">(Optional) The number of items to return in one response. The minimum is 1. The maximum is 1000. The default is 100.</param>
    /// <param name="pageNumber">(Optional) The offset for paging. The default is 1.</param>
    /// <param name="includeUsageStatistics">(Optional) true to return usage statistics. The default is false.</param>
    /// <param name="fieldsExpression">(Optional) An expression that lets you specify the set of available fields to return. You can qualify the return values based upon predefined keywords such as _all_ or _default_, and you can specify individual fields for the workbooks or other supported resources. You can include multiple field expressions in a request.</param>
    /// <param name="filterExpression"></param>
    /// <param name="sortExpresion"></param>
    /// <param name="cancelToken"></param>
    /// <returns></returns>
    /// <exception cref="TableauRequestException"></exception>
    /// <exception cref="TableauApiVersionException"></exception>
    /// <exception cref="TableauOnlineNotSupportedException"></exception>
    [ApiVersion(2, 2)]
    public async Task<(paginationType pagination, viewType[] views)> QueryViewsForSite(TableauSession session,
        int pageSize = 100, int pageNumber = 1, bool? includeUsageStatistics = null, string fieldsExpression = null,
        string filterExpression = null, string sortExpresion = null, CancellationToken? cancelToken = null)
    {
        checkEndpointAvailability();
        checkNullParameters((nameof(session), session));
        checkParametersBetween((nameof(pageSize), pageSize, 1, 1000), (nameof(pageNumber), pageNumber, 1, int.MaxValue));
        var args = new List<(string, string)>()
        {
            ("pageSize", pageSize.ToString(CultureInfo.InvariantCulture)),
            ("pageNumber", pageNumber.ToString(CultureInfo.InvariantCulture))
        };
        if (includeUsageStatistics.HasValue)
            args.Add(("includeUsageStatistics", includeUsageStatistics.Value.ToString()));
        if (!string.IsNullOrEmpty(fieldsExpression))
            args.Add(("fields", fieldsExpression));
        if (!string.IsNullOrEmpty(filterExpression))
            args.Add(("filter", filterExpression));
        if (!string.IsNullOrEmpty(sortExpresion))
            args.Add(("sort", sortExpresion));
        var obj = await getResponseAsObject<paginationType, viewListType>(await apiRequest(buildUri($"sites/{session.SiteId}/views", args.ToArray()), HttpMethod.Get, 200,
            cancelToken, session));
        return (obj.Item1, obj.Item2.view);

    }
    /// <summary>
    /// Updates a site. Not available in Tableau Online. Available in all versions
    /// </summary>
    /// <param name="session"></param>
    /// <param name="siteId">The id of the site to update</param>
    /// <param name="newName">(Optional) The new name of the site.</param>
    /// <param name="newContentUrl">(Optional) The new site URL. This value can contain only characters that are valid in a URL.</param>
    /// <param name="newAdminMode">	(Optional) Specify ContentAndUsers to allow site administrators to use the server interface and tabcmd commands to add and remove users. (Specifying this option does not give site administrators permissions to manage users using the REST API.) Specify ContentOnly to prevent site administrators from adding or removing users. (Server administrators can always add or remove users.)</param>
    /// <param name="newTierCreatorCapacity">(Optional) The maximum number of users for the site in each of the user-based license types (Creator, Explorer and Viewer). Only licensed users are counted and server administrators are excluded. Setting this value to -1 removes any value that was set previously. In that case, the limit depends on the type of licensing configured for the server. For user-based license types, the maximum number of users is set by the licenses activated on that server. For core-based licensing, there is no limit to the number of users.</param>
    /// <param name="newTierExplorerCapacity">(Optional) The maximum number of users for the site in each of the user-based license types (Creator, Explorer and Viewer). Only licensed users are counted and server administrators are excluded. Setting this value to -1 removes any value that was set previously. In that case, the limit depends on the type of licensing configured for the server. For user-based license types, the maximum number of users is set by the licenses activated on that server. For core-based licensing, there is no limit to the number of users.</param>
    /// <param name="newTierViewerCapacity">(Optional) The maximum number of users for the site in each of the user-based license types (Creator, Explorer and Viewer). Only licensed users are counted and server administrators are excluded. Setting this value to -1 removes any value that was set previously. In that case, the limit depends on the type of licensing configured for the server. For user-based license types, the maximum number of users is set by the licenses activated on that server. For core-based licensing, there is no limit to the number of users.</param>
    /// <param name="newState">	(Optional) Active to set the site to active mode, or Suspended to suspend the site.</param>
    /// <param name="newStorageQuota">(Optional) The new maximum amount of space for the new site, in megabytes. If you set a quota and the site exceeds it, publishers will be prevented from uploading new content until the site is under the limit again.</param>
    /// <param name="newDisableSubscriptions">(Optional) true to prevent users from being able to subscribe to workbooks on the specified site.</param>
    /// <param name="newRevisionHistoryEnabled">(Optional) true if the site maintains revisions for changes to workbooks and data sources; otherwise, false. The default is false.</param>
    /// <param name="newRevisionLimit">	(Optional) An integer between 2 and 10000 to indicate a limited number of revisions for content. Setting this value to -1 removes any value that was set previously, and effectively removes any limit to the number of revisions that are maintained.</param>
    /// <param name="cancelToken"></param>
    /// <returns></returns>
    /// <exception cref="TableauRequestException"></exception>
    /// <exception cref="TableauApiVersionException"></exception>
    /// <exception cref="TableauOnlineNotSupportedException"></exception>
    [ApiVersion(1, 0)]
    [OnPremiseOnly]
    public async Task<siteType> UpdateSite(TableauSession session, string siteId, string newName = null,
        string newContentUrl = null, siteTypeAdminMode? newAdminMode = null, int? newTierCreatorCapacity = null,
        int? newTierExplorerCapacity = null, int? newTierViewerCapacity = null, stateType? newState = null,
        int? newStorageQuota = null, bool? newDisableSubscriptions = null, bool? newRevisionHistoryEnabled = null,
        int? newRevisionLimit = null, CancellationToken? cancelToken = null)
    {
        checkEndpointAvailability();
        checkNullParameters((nameof(session), session), (nameof(siteId), siteId));
        return await getResponseAsObject<siteType>(await apiRequest(buildUri($"sites/{siteId}"), HttpMethod.Put,
            200, cancelToken, session,
            getObjectAsRequestContent(new siteType()
            {
                name = newName,
                contentUrl = newContentUrl,
                adminMode = newAdminMode ?? siteTypeAdminMode.ContentAndUsers,
                adminModeSpecified = newAdminMode.HasValue,
                tierExplorerCapacity = newTierExplorerCapacity?.ToString(CultureInfo.InvariantCulture),
                tierViewerCapacity = newTierViewerCapacity?.ToString(CultureInfo.InvariantCulture),
                tierCreatorCapacity = newTierCreatorCapacity?.ToString(CultureInfo.InvariantCulture),
                state = newState ?? stateType.Active,
                stateSpecified = newState.HasValue,
                storageQuota = newStorageQuota?.ToString(CultureInfo.InvariantCulture),
                disableSubscriptions = newDisableSubscriptions ?? false,
                disableSubscriptionsSpecified = newDisableSubscriptions.HasValue,
                revisionHistoryEnabled = newRevisionHistoryEnabled ?? false,
                revisionHistoryEnabledSpecified = newRevisionHistoryEnabled.HasValue,
                revisionLimit = newRevisionLimit?.ToString(CultureInfo.InvariantCulture)
            })));
    }

    /// <summary>
    /// Deletes a single Site by siteId. Not available in Tableau Online. Available in all versions
    /// </summary>
    /// <param name="session"></param>
    /// <param name="siteId">The id of the site to query</param>
    /// <param name="cancelToken"></param>
    /// <returns></returns>
    /// <exception cref="TableauRequestException"></exception>
    /// <exception cref="TableauApiVersionException"></exception>
    /// <exception cref="TableauOnlineNotSupportedException"></exception>
    [ApiVersion(1, 0)]
    [OnPremiseOnly]
    public async Task DeleteSiteById(TableauSession session, string siteId,
        CancellationToken? cancelToken = null)
    {
        checkEndpointAvailability();
        checkNullParameters((nameof(session), session), (nameof(siteId), siteId));
        await apiRequest(buildUri($"sites/{siteId}"), HttpMethod.Delete, 204, cancelToken,
            session);

    }

    /// <summary>
    /// Deletes a single Site by siteName. Not available in Tableau Online. Available in all versions
    /// </summary>
    /// <param name="session"></param>
    /// <param name="siteName">The name of the site to delete</param>
    /// <param name="cancelToken"></param>
    /// <returns></returns>
    /// <exception cref="TableauRequestException"></exception>
    /// <exception cref="TableauApiVersionException"></exception>
    /// <exception cref="TableauOnlineNotSupportedException"></exception>
    [ApiVersion(1, 0)]
    [OnPremiseOnly]
    public async Task DeleteSiteByName(TableauSession session, string siteName,
        CancellationToken? cancelToken = null)
    {
        checkEndpointAvailability();
        checkNullParameters((nameof(session), session), (nameof(siteName), siteName));
        await apiRequest(buildUri($"sites/{siteName}", ("key", "name")), HttpMethod.Delete, 204, cancelToken,
            session);

    }
    /// <summary>
    /// Deletes a single Site by siteName. Not available in Tableau Online. Available in all versions
    /// </summary>
    /// <param name="session"></param>
    /// <param name="siteContentUrl">The content URL of the site to delete</param>
    /// <param name="cancelToken"></param>
    /// <returns></returns>
    /// <exception cref="TableauRequestException"></exception>
    /// <exception cref="TableauApiVersionException"></exception>
    /// <exception cref="TableauOnlineNotSupportedException"></exception>
    [ApiVersion(1, 0)]
    [OnPremiseOnly]
    public async Task DeleteSiteByContentUrl(TableauSession session, string siteContentUrl,
        CancellationToken? cancelToken = null)
    {
        checkEndpointAvailability();
        checkNullParameters((nameof(session), session), (nameof(siteContentUrl), siteContentUrl));
        await apiRequest(buildUri($"sites/{siteContentUrl}", ("key", "contentUrl")), HttpMethod.Delete, 204, cancelToken,
            session);

    }
}
