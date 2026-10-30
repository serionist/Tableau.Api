using System;
using System.Collections.Generic;
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
    /// Adds the specified data source to the user's favorites. Versions 2.3+
    /// </summary>
    /// <param name="session"></param>
    /// <param name="userId">The ID of the user to add the favorite for.</param>
    /// <param name="datasourceId">The ID of the data source to add as a favorite.</param>
    /// <param name="favoriteLabel">A label to assign to the favorite. This value is displayed when you search for favorites on the server.</param>
    /// <param name="cancelToken"></param>
    /// <returns></returns>
    /// <exception cref="TableauRequestException"></exception>
    /// <exception cref="TableauApiVersionException"></exception>
    /// <exception cref="TableauOnlineNotSupportedException"></exception>
    [ApiVersion(2, 3)]
    public async Task<favoriteType> AddDataSourceToFavorites(TableauSession session, string userId,
        string datasourceId, string favoriteLabel, CancellationToken? cancelToken = null)
    {
        checkEndpointAvailability();
        checkNullParameters((nameof(session), session), (nameof(userId), userId), (nameof(datasourceId), datasourceId), (nameof(favoriteLabel), favoriteLabel));
        return await getResponseAsObject<favoriteType>(await apiRequest(
            buildUri($"sites/{session.SiteId}/favorites/{userId}"), HttpMethod.Put, 200,
            cancelToken, session,
            getObjectAsRequestContent(new favoriteType()
            {
                label = favoriteLabel,
                Item = new dataSourceType() { id = datasourceId }
            })));

    }
    /// <summary>
    /// Adds the specified project to the user's favorites. Versions 3.1+
    /// </summary>
    /// <param name="session"></param>
    /// <param name="userId">The ID of the user to add the favorite for.</param>
    /// <param name="projectId">The ID of the project to add as a favorite.</param>
    /// <param name="favoriteLabel">A label to assign to the favorite. This value is displayed when you search for favorites on the server.</param>
    /// <param name="cancelToken"></param>
    /// <returns></returns>
    /// <exception cref="TableauRequestException"></exception>
    /// <exception cref="TableauApiVersionException"></exception>
    /// <exception cref="TableauOnlineNotSupportedException"></exception>
    [ApiVersion(3, 1)]
    public async Task<favoriteType> AddProjectToFavorites(TableauSession session, string userId,
        string projectId, string favoriteLabel, CancellationToken? cancelToken = null)
    {
        checkEndpointAvailability();
        checkNullParameters((nameof(session), session), (nameof(userId), userId), (nameof(projectId), projectId), (nameof(favoriteLabel), favoriteLabel));
        return await getResponseAsObject<favoriteType>(await apiRequest(
            buildUri($"sites/{session.SiteId}/favorites/{userId}"), HttpMethod.Put, 200,
            cancelToken, session,
            getObjectAsRequestContent(new favoriteType()
            {
                label = favoriteLabel,
                Item = new projectType() { id = projectId }
            })));

    }
    /// <summary>
    /// Adds the specified workbook to the user's favorites. Available in all versions
    /// </summary>
    /// <param name="session"></param>
    /// <param name="userId">The ID of the user to add the favorite for.</param>
    /// <param name="workbookId">The ID of the workbook to add as a favorite.</param>
    /// <param name="favoriteLabel">A label to assign to the favorite. This value is displayed when you search for favorites on the server.</param>
    /// <param name="cancelToken"></param>
    /// <returns></returns>
    /// <exception cref="TableauRequestException"></exception>
    /// <exception cref="TableauApiVersionException"></exception>
    /// <exception cref="TableauOnlineNotSupportedException"></exception>
    [ApiVersion(2, 0)]
    public async Task<favoriteType> AddWorkbookToFavorites(TableauSession session, string userId,
        string workbookId, string favoriteLabel, CancellationToken? cancelToken = null)
    {
        checkEndpointAvailability();
        checkNullParameters((nameof(session), session), (nameof(userId), userId), (nameof(workbookId), workbookId), (nameof(favoriteLabel), favoriteLabel));
        return await getResponseAsObject<favoriteType>(await apiRequest(
            buildUri($"sites/{session.SiteId}/favorites/{userId}"), HttpMethod.Put, 200,
            cancelToken, session,
            getObjectAsRequestContent(new favoriteType()
            {
                label = favoriteLabel,
                Item = new workbookType() { id = workbookId }
            })));

    }
    /// <summary>
    /// Adds the specified view to the user's favorites. Available in all versions
    /// </summary>
    /// <param name="session"></param>
    /// <param name="userId">The ID of the user to add the favorite for.</param>
    /// <param name="viewId">The ID of the view to add as a favorite.</param>
    /// <param name="favoriteLabel">A label to assign to the favorite. This value is displayed when you search for favorites on the server.</param>
    /// <param name="cancelToken"></param>
    /// <returns></returns>
    /// <exception cref="TableauRequestException"></exception>
    /// <exception cref="TableauApiVersionException"></exception>
    /// <exception cref="TableauOnlineNotSupportedException"></exception>
    [ApiVersion(2, 0)]
    public async Task<favoriteType> AddViewToFavorites(TableauSession session, string userId,
        string viewId, string favoriteLabel, CancellationToken? cancelToken = null)
    {
        checkEndpointAvailability();
        checkNullParameters((nameof(session), session), (nameof(userId), userId), (nameof(viewId), viewId), (nameof(favoriteLabel), favoriteLabel));
        return await getResponseAsObject<favoriteType>(await apiRequest(
            buildUri($"sites/{session.SiteId}/favorites/{userId}"), HttpMethod.Put, 200,
            cancelToken, session,
            getObjectAsRequestContent(new favoriteType()
            {
                label = favoriteLabel,
                Item = new viewType() { id = viewId }
            })));

    }
    /// <summary>
    /// Deletes the specified data source from the user's favorites. Versions 2.3+
    /// </summary>
    /// <param name="session"></param>
    /// <param name="userId">The ID of the user to remove the favorite from.</param>
    /// <param name="datasourceId">The ID of the data source to remove from the user's favorites.</param>
    /// <param name="cancelToken"></param>
    /// <returns></returns>
    /// <exception cref="TableauRequestException"></exception>
    /// <exception cref="TableauApiVersionException"></exception>
    /// <exception cref="TableauOnlineNotSupportedException"></exception>
    [ApiVersion(2, 3)]
    public async Task DeleteDataSourceFromFavorites(TableauSession session, string userId, string datasourceId,
        CancellationToken? cancelToken = null)
    {
        checkEndpointAvailability();
        checkNullParameters((nameof(session), session), (nameof(userId), userId), (nameof(datasourceId), datasourceId));
        await apiRequest(buildUri($"sites/{session.SiteId}/favorites/{userId}/datasources/{datasourceId}"),
            HttpMethod.Delete, 204, cancelToken, session);

    }
    /// <summary>
    /// Deletes the specified project from the user's favorites. Versions 3.1+
    /// </summary>
    /// <param name="session"></param>
    /// <param name="userId">The ID of the user to remove the favorite from.</param>
    /// <param name="projectId">The ID of the project to remove from the user's favorites.</param>
    /// <param name="cancelToken"></param>
    /// <returns></returns>
    /// <exception cref="TableauRequestException"></exception>
    /// <exception cref="TableauApiVersionException"></exception>
    /// <exception cref="TableauOnlineNotSupportedException"></exception>
    [ApiVersion(3, 1)]
    public async Task DeleteProjectFromFavorites(TableauSession session, string userId, string projectId,
        CancellationToken? cancelToken = null)
    {
        checkEndpointAvailability();
        checkNullParameters((nameof(session), session), (nameof(userId), userId), (nameof(projectId), projectId));
        await apiRequest(buildUri($"sites/{session.SiteId}/favorites/{userId}/projects/{projectId}"),
            HttpMethod.Delete, 204, cancelToken, session);

    }
    /// <summary>
    /// Deletes the specified workbook from the user's favorites. Available in all versions
    /// </summary>
    /// <param name="session"></param>
    /// <param name="userId">The ID of the user to remove the favorite from.</param>
    /// <param name="workbookId">The ID of the workbook to remove from the user's favorites.</param>
    /// <param name="cancelToken"></param>
    /// <returns></returns>
    /// <exception cref="TableauRequestException"></exception>
    /// <exception cref="TableauApiVersionException"></exception>
    /// <exception cref="TableauOnlineNotSupportedException"></exception>
    [ApiVersion(2, 0)]
    public async Task DeleteWorkbookFromFavorites(TableauSession session, string userId, string workbookId,
        CancellationToken? cancelToken = null)
    {
        checkEndpointAvailability();
        checkNullParameters((nameof(session), session), (nameof(userId), userId), (nameof(workbookId), workbookId));
        await apiRequest(buildUri($"sites/{session.SiteId}/favorites/{userId}/workbooks/{workbookId}"),
            HttpMethod.Delete, 204, cancelToken, session);

    }
    /// <summary>
    /// Deletes the specified view from the user's favorites. Available in all versions
    /// </summary>
    /// <param name="session"></param>
    /// <param name="userId">The ID of the user to remove the favorite from.</param>
    /// <param name="viewId">The ID of the view to remove from the user's favorites.</param>
    /// <param name="cancelToken"></param>
    /// <returns></returns>
    /// <exception cref="TableauRequestException"></exception>
    /// <exception cref="TableauApiVersionException"></exception>
    /// <exception cref="TableauOnlineNotSupportedException"></exception>
    [ApiVersion(2, 0)]
    public async Task DeleteViewFromFavorites(TableauSession session, string userId, string viewId,
        CancellationToken? cancelToken = null)
    {
        checkEndpointAvailability();
        checkNullParameters((nameof(session), session), (nameof(userId), userId), (nameof(viewId), viewId));
        await apiRequest(buildUri($"sites/{session.SiteId}/favorites/{userId}/views/{viewId}"),
            HttpMethod.Delete, 204, cancelToken, session);
    }
    /// <summary>
    /// Returns a list of favorite projects, data sources, views and workbooks for a user.
    /// </summary>
    /// <param name="session"></param>
    /// <param name="userId">The ID of the user for which you want to get a list favorites.</param>
    /// <param name="cancelToken"></param>
    /// <returns></returns>
    /// <exception cref="TableauRequestException"></exception>
    /// <exception cref="TableauApiVersionException"></exception>
    /// <exception cref="TableauOnlineNotSupportedException"></exception>
    [ApiVersion(2, 5)]
    public async Task<favoriteType[]> GetFavoritesForUser(TableauSession session, string userId,
        CancellationToken? cancelToken = null)
    {
        checkEndpointAvailability();
        checkNullParameters((nameof(session), session), (nameof(userId), userId));
        return (await getResponseAsObject<favoriteListType>(await apiRequest(
            buildUri($"sites/{session.SiteId}/favorites/{userId}"), HttpMethod.Get, 200,
            cancelToken, session))).favorite;

    }

}
