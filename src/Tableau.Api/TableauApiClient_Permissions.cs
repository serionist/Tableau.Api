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
    /// Adds permissions to the specified data source for a Tableau Server user or group. You can specify multiple sets of permissions using one call. Available in all versions
    /// </summary>
    /// <param name="session"></param>
    /// <param name="datasourceId">The ID of the datasource</param>
    /// <param name="granteeCapabilities">The permissions to assign. Permissions can either be for a user or group</param>
    /// <param name="cancelToken"></param>
    /// <returns></returns>
    [ApiVersion(2, 0)]
    public async Task<permissionsType> AddDataSourcePermissions(Models.TableauSession session, string datasourceId,
        granteeCapabilitiesType[] granteeCapabilities, CancellationToken? cancelToken = null)
    {
        checkEndpointAvailability();
        checkNullParameters((nameof(session), session), (nameof(datasourceId), datasourceId), (nameof(granteeCapabilities), granteeCapabilities));
        return await getResponseAsObject<permissionsType>(await apiRequest(
            buildUri($"sites/{session.SiteId}/datasources/{datasourceId}/permissions"), HttpMethod.Put, 200,
            cancelToken, session,
            getObjectAsRequestContent(new permissionsType()
            {
                Item = new dataSourceType() { id = datasourceId },
                granteeCapabilities = granteeCapabilities
            })));

    }

    /// <summary>
    /// Adds permissions to the specified project for a Tableau Server user or group. You can specify multiple sets of permissions using one call. Available in all versions
    /// </summary>
    /// <param name="session"></param>
    /// <param name="projectId">The ID of the project</param>
    /// <param name="granteeCapabilities">The permissions to assign. Permissions can either be for a user or group</param>
    /// <param name="cancelToken"></param>
    /// <returns></returns>
    [ApiVersion(2, 0)]
    public async Task<permissionsType> AddProjectPermissions(Models.TableauSession session, string projectId,
        granteeCapabilitiesType[] granteeCapabilities, CancellationToken? cancelToken = null)
    {
        checkEndpointAvailability();
        checkNullParameters((nameof(session), session), (nameof(projectId), projectId), (nameof(granteeCapabilities), granteeCapabilities));
        return await getResponseAsObject<permissionsType>(await apiRequest(
            buildUri($"sites/{session.SiteId}/projects/{projectId}/permissions"), HttpMethod.Put, 200,
            cancelToken, session,
            getObjectAsRequestContent(new permissionsType()
            {
                Item = new projectType() { id = projectId },
                granteeCapabilities = granteeCapabilities
            })));

    }

    /// <summary>
    /// Adds permissions to the specified project that will be applied by default to new workbooks as they are added to the project. Availablein all versions
    /// </summary>
    /// <param name="session"></param>
    /// <param name="projectId">The ID of the project</param>
    /// <param name="granteeCapabilities">The permissions to assign. Permissions can either be for a user or group</param>
    /// <param name="cancelToken"></param>
    /// <returns></returns>
    [ApiVersion(2, 0)]
    public async Task<permissionsType> AddDefaultPermissionsForWorkbooks(Models.TableauSession session, string projectId,
        granteeCapabilitiesType[] granteeCapabilities, CancellationToken? cancelToken = null)
    {
        checkEndpointAvailability();
        checkNullParameters((nameof(session), session), (nameof(projectId), projectId), (nameof(granteeCapabilities), granteeCapabilities));
        return await getResponseAsObject<permissionsType>(await apiRequest(
            buildUri($"sites/{session.SiteId}/projects/{projectId}/default-permissions/workbooks"), HttpMethod.Put, 200,
            cancelToken, session,
            getObjectAsRequestContent(new permissionsType()
            {
                granteeCapabilities = granteeCapabilities
            })));

    }

    /// <summary>
    /// Adds permissions to the specified project that will be applied by default to new DataSources as they are added to the project. Availablein all versions
    /// </summary>
    /// <param name="session"></param>
    /// <param name="projectId">The ID of the project</param>
    /// <param name="granteeCapabilities">The permissions to assign. Permissions can either be for a user or group</param>
    /// <param name="cancelToken"></param>
    /// <returns></returns>
    [ApiVersion(2, 0)]
    public async Task<permissionsType> AddDefaultPermissionsForDataSources(Models.TableauSession session, string projectId,
        granteeCapabilitiesType[] granteeCapabilities, CancellationToken? cancelToken = null)
    {
        checkEndpointAvailability();
        checkNullParameters((nameof(session), session), (nameof(projectId), projectId), (nameof(granteeCapabilities), granteeCapabilities));
        return await getResponseAsObject<permissionsType>(await apiRequest(
            buildUri($"sites/{session.SiteId}/projects/{projectId}/default-permissions/datasources"), HttpMethod.Put, 200,
            cancelToken, session,
            getObjectAsRequestContent(new permissionsType()
            {
                granteeCapabilities = granteeCapabilities
            })));

    }

    /// <summary>
    /// Adds permissions to the specified workbook for a Tableau Server user or group. You can specify multiple sets of permissions using one call. Available in all versions
    /// </summary>
    /// <param name="session"></param>
    /// <param name="workbookId">The ID of the workbook</param>
    /// <param name="granteeCapabilities">The permissions to assign. Permissions can either be for a user or group</param>
    /// <param name="cancelToken"></param>
    /// <returns></returns>
    [ApiVersion(2, 0)]
    public async Task<permissionsType> AddWorkbookPermissions(Models.TableauSession session, string workbookId,
        granteeCapabilitiesType[] granteeCapabilities, CancellationToken? cancelToken = null)
    {
        checkEndpointAvailability();
        checkNullParameters((nameof(session), session), (nameof(workbookId), workbookId), (nameof(granteeCapabilities), granteeCapabilities));
        return await getResponseAsObject<permissionsType>(await apiRequest(
            buildUri($"sites/{session.SiteId}/workbooks/{workbookId}/permissions"), HttpMethod.Put, 200,
            cancelToken, session,
            getObjectAsRequestContent(new permissionsType()
            {
                Item = new workbookType() { id = workbookId },
                granteeCapabilities = granteeCapabilities
            })));

    }

    /// <summary>
    /// Adds a task to refresh a workbook to an existing schedule. Versions 2.8+
    /// </summary>
    /// <param name="session"></param>
    /// <param name="workbookId">The ID of the workbook</param>
    ///  <param name="scheduleId">The ID of the schedule</param>
    /// <param name="granteeCapabilities">The permissions to assign. Permissions can either be for a user or group</param>
    /// <param name="cancelToken"></param>
    /// <returns></returns>
    [ApiVersion(2, 8)]
    public async Task<permissionsType> AddWorkbookToSchedule(Models.TableauSession session, string workbookId,
       string scheduleId, granteeCapabilitiesType[] granteeCapabilities, CancellationToken? cancelToken = null)
    {
        checkEndpointAvailability();
        checkNullParameters((nameof(session), session), (nameof(workbookId), workbookId), (nameof(scheduleId), scheduleId), (nameof(granteeCapabilities), granteeCapabilities));
        return await getResponseAsObject<permissionsType>(await apiRequest(
            buildUri($"sites/{session.SiteId}/schedules/{scheduleId}/workbooks"), HttpMethod.Put, 200,
            cancelToken, session,
            getObjectAsRequestContent(new taskType()
            {
                Item = new taskExtractRefreshType() { Item = new workbookType() { id = workbookId } }
            })));

    }
    /// <summary>
    /// Returns a list of permissions for a specific data source. Available in all versions
    /// </summary>
    /// <param name="session"></param>
    /// <param name="datasourceId">The ID of the datasource</param>
    /// <param name="cancelToken"></param>
    /// <returns></returns>
    [ApiVersion(2, 0)]
    public async Task<permissionsType> QueryDataSourcePermissions(Models.TableauSession session, string datasourceId,
        CancellationToken? cancelToken = null)
    {
        checkEndpointAvailability();
        checkNullParameters((nameof(session), session), (nameof(datasourceId), datasourceId));
        return await getResponseAsObject<permissionsType>(await apiRequest(
            buildUri($"sites/{session.SiteId}/datasources/{datasourceId}/permissions"), HttpMethod.Get, 200,
            cancelToken, session));

    }

    /// <summary>
    /// Returns a list of permissions for a specific project. Available in all versions
    /// </summary>
    /// <param name="session"></param>
    /// <param name="projectId">The ID of the project</param>
    /// <param name="cancelToken"></param>
    /// <returns></returns>
    [ApiVersion(2, 0)]
    public async Task<permissionsType> QueryProjectPermissions(Models.TableauSession session, string projectId,
        CancellationToken? cancelToken = null)
    {
        checkEndpointAvailability();
        checkNullParameters((nameof(session), session), (nameof(projectId), projectId));
        return await getResponseAsObject<permissionsType>(await apiRequest(
            buildUri($"sites/{session.SiteId}/projects/{projectId}/permissions"), HttpMethod.Get, 200,
            cancelToken, session));

    }

    /// <summary>
    /// Returns a list of default permissions for workbooks of a specific project. Available in all versions
    /// </summary>
    /// <param name="session"></param>
    /// <param name="workbookId">The ID of the workbook</param>
    /// <param name="cancelToken"></param>
    /// <returns></returns>
    [ApiVersion(2, 0)]
    public async Task<permissionsType> QueryDefaultPermissionsForWorkbooks(Models.TableauSession session, string workbookId,
        CancellationToken? cancelToken = null)
    {
        checkEndpointAvailability();
        checkNullParameters((nameof(session), session), (nameof(workbookId), workbookId));
        return await getResponseAsObject<permissionsType>(await apiRequest(
            buildUri($"sites/{session.SiteId}/projects/{workbookId}/default-permissions/workbooks"), HttpMethod.Get, 200,
            cancelToken, session));

    }

    /// <summary>
    /// Returns a list of default permissions for data sources of a specific project. Available in all versions
    /// </summary>
    /// <param name="session"></param>
    /// <param name="datasourceId">The ID of the data source</param>
    /// <param name="cancelToken"></param>
    /// <returns></returns>
    [ApiVersion(2, 0)]
    public async Task<permissionsType> QueryDefaultPermissionsForDataSources(Models.TableauSession session, string datasourceId,
        CancellationToken? cancelToken = null)
    {
        checkEndpointAvailability();
        checkNullParameters((nameof(session), session), (nameof(datasourceId), datasourceId));
        return await getResponseAsObject<permissionsType>(await apiRequest(
            buildUri($"sites/{session.SiteId}/projects/{datasourceId}/default-permissions/datasources"), HttpMethod.Get, 200,
            cancelToken, session));

    }

    /// <summary>
    /// Returns a list of permissions for a specific view.
    /// </summary>
    /// <param name="session"></param>
    /// <param name="viewId">The ID of the view to get permissions for</param>
    /// <param name="cancelToken"></param>
    /// <returns></returns>
    [ApiVersion(3, 2)]
    public async Task<permissionsType> QueryViewPermissions(Models.TableauSession session, string viewId,
        CancellationToken? cancelToken = null)
    {
        checkEndpointAvailability();
        checkNullParameters((nameof(session), session), (nameof(viewId), viewId));
        return await getResponseAsObject<permissionsType>(await apiRequest(
            buildUri($"sites/{session.SiteId}/views/{viewId}/permissions"), HttpMethod.Get, 200,
            cancelToken, session));

    }


    /// <summary>
    /// Returns a list of permissions for a specific project. Available in all versions
    /// </summary>
    /// <param name="session"></param>
    /// <param name="workbookId">The ID of the workbook</param>
    /// <param name="cancelToken"></param>
    /// <returns></returns>
    [ApiVersion(2, 0)]
    public async Task<permissionsType> QueryWorkbookPermissions(Models.TableauSession session, string workbookId,
        CancellationToken? cancelToken = null)
    {
        checkEndpointAvailability();
        checkNullParameters((nameof(session), session), (nameof(workbookId), workbookId));
        return await getResponseAsObject<permissionsType>(await apiRequest(
            buildUri($"sites/{session.SiteId}/workbooks/{workbookId}/permissions"), HttpMethod.Get, 200,
            cancelToken, session));

    }
    /// <summary>
    /// Deletes a data source permission for a specific user. Available in all versions
    /// </summary>
    /// <param name="session"></param>
    /// <param name="datasourceId">The ID of the datasource</param>
    /// <param name="userId">The ID of the user</param>
    /// <param name="capabilityName"></param>
    /// <param name="capabilityMode"></param>
    /// <param name="cancelToken"></param>
    /// <returns></returns>
    [ApiVersion(2, 0)]
    public async Task DeleteDataSourcePermissionForUser(Models.TableauSession session, string datasourceId, string userId,
        capabilityTypeName capabilityName, capabilityTypeMode capabilityMode, CancellationToken? cancelToken = null)
    {
        checkEndpointAvailability();
        checkNullParameters((nameof(session), session), (nameof(datasourceId), datasourceId), (nameof(userId), userId));
        await apiRequest(
            buildUri(
                $"sites/{session.SiteId}/datasources/{datasourceId}/permissions/users/{userId}/{Enum.GetName(typeof(capabilityTypeName), capabilityName)}/{Enum.GetName(typeof(capabilityTypeMode), capabilityMode)}"),
            HttpMethod.Delete, 204, cancelToken, session);
    }
    /// <summary>
    /// Deletes a data source permission for a specific group. Available in all versions
    /// </summary>
    /// <param name="session"></param>
    /// <param name="datasourceId">The ID of the datasource</param>
    /// <param name="groupId">The ID of the user</param>
    /// <param name="capabilityName"></param>
    /// <param name="capabilityMode"></param>
    /// <param name="cancelToken"></param>
    /// <returns></returns>
    [ApiVersion(2, 0)]
    public async Task DeleteDataSourcePermissionForGroup(Models.TableauSession session, string datasourceId, string groupId,
        capabilityTypeName capabilityName, capabilityTypeMode capabilityMode, CancellationToken? cancelToken = null)
    {
        checkEndpointAvailability();
        checkNullParameters((nameof(session), session), (nameof(datasourceId), datasourceId), (nameof(groupId), groupId));
        await apiRequest(
            buildUri(
                $"sites/{session.SiteId}/datasources/{datasourceId}/permissions/groups/{groupId}/{Enum.GetName(typeof(capabilityTypeName), capabilityName)}/{Enum.GetName(typeof(capabilityTypeMode), capabilityMode)}"),
            HttpMethod.Delete, 204, cancelToken, session);
    }

    /// <summary>
    /// Deletes a project permission for a specific user. Available in all versions
    /// </summary>
    /// <param name="session"></param>
    /// <param name="projectId">The ID of the project</param>
    /// <param name="userId">The ID of the user</param>
    /// <param name="capabilityName"></param>
    /// <param name="capabilityMode"></param>
    /// <param name="cancelToken"></param>
    /// <returns></returns>
    [ApiVersion(2, 0)]
    public async Task DeleteProjectPermissionForUser(Models.TableauSession session, string projectId, string userId,
        capabilityTypeName capabilityName, capabilityTypeMode capabilityMode, CancellationToken? cancelToken = null)
    {
        checkEndpointAvailability();
        checkNullParameters((nameof(session), session), (nameof(projectId), projectId), (nameof(userId), userId));
        await apiRequest(
            buildUri(
                $"sites/{session.SiteId}/projects/{projectId}/permissions/users/{userId}/{Enum.GetName(typeof(capabilityTypeName), capabilityName)}/{Enum.GetName(typeof(capabilityTypeMode), capabilityMode)}"),
            HttpMethod.Delete, 204, cancelToken, session);
    }
    /// <summary>
    /// Deletes a project permission for a specific group. Available in all versions
    /// </summary>
    /// <param name="session"></param>
    /// <param name="projectId">The ID of the project</param>
    /// <param name="groupId">The ID of the user</param>
    /// <param name="capabilityName"></param>
    /// <param name="capabilityMode"></param>
    /// <param name="cancelToken"></param>
    /// <returns></returns>
    [ApiVersion(2, 0)]
    public async Task DeleteProjectPermissionForGroup(Models.TableauSession session, string projectId, string groupId,
        capabilityTypeName capabilityName, capabilityTypeMode capabilityMode, CancellationToken? cancelToken = null)
    {
        checkEndpointAvailability();
        checkNullParameters((nameof(session), session), (nameof(projectId), projectId), (nameof(groupId), groupId));
        await apiRequest(
            buildUri(
                $"sites/{session.SiteId}/projects/{projectId}/permissions/groups/{groupId}/{Enum.GetName(typeof(capabilityTypeName), capabilityName)}/{Enum.GetName(typeof(capabilityTypeMode), capabilityMode)}"),
            HttpMethod.Delete, 204, cancelToken, session);
    }


    /// <summary>
    /// Deletes a default project data source permission for a specific user. Available in all versions
    /// </summary>
    /// <param name="session"></param>
    /// <param name="projectId">The ID of the project</param>
    /// <param name="userId">The ID of the user</param>
    /// <param name="capabilityName"></param>
    /// <param name="capabilityMode"></param>
    /// <param name="cancelToken"></param>
    /// <returns></returns>
    [ApiVersion(2, 0)]
    public async Task DeleteDefaultDataSourcePermissionForUser(Models.TableauSession session, string projectId, string userId,
        capabilityTypeName capabilityName, capabilityTypeMode capabilityMode, CancellationToken? cancelToken = null)
    {
        checkEndpointAvailability();
        checkNullParameters((nameof(session), session), (nameof(projectId), projectId), (nameof(userId), userId));
        await apiRequest(
            buildUri(
                $"sites/{session.SiteId}/projects/{projectId}/default-permissions/datasources/users/{userId}/{Enum.GetName(typeof(capabilityTypeName), capabilityName)}/{Enum.GetName(typeof(capabilityTypeMode), capabilityMode)}"),
            HttpMethod.Delete, 204, cancelToken, session);
    }
    /// <summary>
    /// Deletes a default project data source permission for a specific group. Available in all versions
    /// </summary>
    /// <param name="session"></param>
    /// <param name="projectId">The ID of the project</param>
    /// <param name="groupId">The ID of the user</param>
    /// <param name="capabilityName"></param>
    /// <param name="capabilityMode"></param>
    /// <param name="cancelToken"></param>
    /// <returns></returns>
    [ApiVersion(2, 0)]
    public async Task DeleteDefaultDataSourcePermissionForGroup(Models.TableauSession session, string projectId, string groupId,
        capabilityTypeName capabilityName, capabilityTypeMode capabilityMode, CancellationToken? cancelToken = null)
    {
        checkEndpointAvailability();
        checkNullParameters((nameof(session), session), (nameof(projectId), projectId), (nameof(groupId), groupId));
        await apiRequest(
            buildUri(
                $"sites/{session.SiteId}/projects/{projectId}/default-permissions/datasources/groups/{groupId}/{Enum.GetName(typeof(capabilityTypeName), capabilityName)}/{Enum.GetName(typeof(capabilityTypeMode), capabilityMode)}"),
            HttpMethod.Delete, 204, cancelToken, session);
    }

    /// <summary>
    /// Deletes a default project workbook permission for a specific user. Available in all versions
    /// </summary>
    /// <param name="session"></param>
    /// <param name="projectId">The ID of the project</param>
    /// <param name="userId">The ID of the user</param>
    /// <param name="capabilityName"></param>
    /// <param name="capabilityMode"></param>
    /// <param name="cancelToken"></param>
    /// <returns></returns>
    [ApiVersion(2, 0)]
    public async Task DeleteDefaultWorkbookPermissionForUser(Models.TableauSession session, string projectId, string userId,
        capabilityTypeName capabilityName, capabilityTypeMode capabilityMode, CancellationToken? cancelToken = null)
    {
        checkEndpointAvailability();
        checkNullParameters((nameof(session), session), (nameof(projectId), projectId), (nameof(userId), userId));
        await apiRequest(
            buildUri(
                $"sites/{session.SiteId}/projects/{projectId}/default-permissions/workbooks/users/{userId}/{Enum.GetName(typeof(capabilityTypeName), capabilityName)}/{Enum.GetName(typeof(capabilityTypeMode), capabilityMode)}"),
            HttpMethod.Delete, 204, cancelToken, session);
    }
    /// <summary>
    /// Deletes a default project workbook permission for a specific group. Available in all versions
    /// </summary>
    /// <param name="session"></param>
    /// <param name="projectId">The ID of the project</param>
    /// <param name="groupId">The ID of the user</param>
    /// <param name="capabilityName"></param>
    /// <param name="capabilityMode"></param>
    /// <param name="cancelToken"></param>
    /// <returns></returns>
    [ApiVersion(2, 0)]
    public async Task DeleteDefaultWorkbookPermissionForGroup(Models.TableauSession session, string projectId, string groupId,
        capabilityTypeName capabilityName, capabilityTypeMode capabilityMode, CancellationToken? cancelToken = null)
    {
        checkEndpointAvailability();
        checkNullParameters((nameof(session), session), (nameof(projectId), projectId), (nameof(groupId), groupId));
        await apiRequest(
            buildUri(
                $"sites/{session.SiteId}/projects/{projectId}/default-permissions/workbooks/groups/{groupId}/{Enum.GetName(typeof(capabilityTypeName), capabilityName)}/{Enum.GetName(typeof(capabilityTypeMode), capabilityMode)}"),
            HttpMethod.Delete, 204, cancelToken, session);
    }




    /// <summary>
    /// Deletes a workbook permission for a specific user. Available in all versions
    /// </summary>
    /// <param name="session"></param>
    /// <param name="workbookId">The ID of the workbook</param>
    /// <param name="userId">The ID of the user</param>
    /// <param name="capabilityName"></param>
    /// <param name="capabilityMode"></param>
    /// <param name="cancelToken"></param>
    /// <returns></returns>
    [ApiVersion(2, 0)]
    public async Task DeleteWorkbookPermissionForUser(Models.TableauSession session, string workbookId, string userId,
        capabilityTypeName capabilityName, capabilityTypeMode capabilityMode, CancellationToken? cancelToken = null)
    {
        checkEndpointAvailability();
        checkNullParameters((nameof(session), session), (nameof(workbookId), workbookId), (nameof(userId), userId));
        await apiRequest(
            buildUri(
                $"sites/{session.SiteId}/workbooks/{workbookId}/permissions/users/{userId}/{Enum.GetName(typeof(capabilityTypeName), capabilityName)}/{Enum.GetName(typeof(capabilityTypeMode), capabilityMode)}"),
            HttpMethod.Delete, 204, cancelToken, session);
    }


    /// <summary>
    /// Deletes a workbook permission for a specific group. Available in all versions
    /// </summary>
    /// <param name="session"></param>
    /// <param name="workbookId">The ID of the workbook</param>
    /// <param name="groupId">The ID of the user</param>
    /// <param name="capabilityName"></param>
    /// <param name="capabilityMode"></param>
    /// <param name="cancelToken"></param>
    /// <returns></returns>
    [ApiVersion(2, 0)]
    public async Task DeleteWorkbookPermissionForGroup(Models.TableauSession session, string workbookId, string groupId,
        capabilityTypeName capabilityName, capabilityTypeMode capabilityMode, CancellationToken? cancelToken = null)
    {
        checkEndpointAvailability();
        checkNullParameters((nameof(session), session), (nameof(workbookId), workbookId), (nameof(groupId), groupId));
        await apiRequest(
            buildUri(
                $"sites/{session.SiteId}/workbooks/{workbookId}/permissions/groups/{groupId}/{Enum.GetName(typeof(capabilityTypeName), capabilityName)}/{Enum.GetName(typeof(capabilityTypeMode), capabilityMode)}"),
            HttpMethod.Delete, 204, cancelToken, session);
    }
}
