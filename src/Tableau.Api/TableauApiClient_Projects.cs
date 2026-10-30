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
    /// Creates a project. Available in all versions
    /// </summary>
    /// <param name="session"></param>
    /// <param name="projectName">The name to assign to the project.</param>
    /// <param name="description">(Optional) A description for the project.</param>
    /// <param name="contentPermissions">(Optional) Specify LockedToProject to lock permissions so that users cannot overwrite the default permissions set for the project, or specify ManagedByOwner to allow users to manage permissions for content that they own. For more information, see Lock Content Permissions to the Project. The default is ManagedByOwner.</param>
    /// <param name="parentProjectId">(Optional) The identifier of the parent project. Use this option to create project hierarchies. </param>
    /// <param name="publishSamples">(Optional) A Boolean value that specifies whether to publish the sample workbooks provided by Tableau to the project. When the publish-value is not specified in the request, or the publishSamples parameter is missing, no samples will be published. To publish the sample workbooks, set publishSamples parameter to true. This option is equivalent to the tabcmd command-line utility option, publishsamples.</param>
    /// <param name="cancelToken"></param>
    /// <returns></returns>
    /// <exception cref="TableauRequestException"></exception>
    /// <exception cref="TableauApiVersionException"></exception>
    /// <exception cref="TableauOnlineNotSupportedException"></exception>
    [ApiVersion(1, 0)]
    public async Task<projectType> CreateProject(TableauSession session, string projectName, string description,
        projectTypeContentPermissions contentPermissions = projectTypeContentPermissions.ManagedByOwner,
        string parentProjectId = null, bool publishSamples = false, CancellationToken? cancelToken = null)
    {
        checkEndpointAvailability();
        checkNullParameters((nameof(session), session), (nameof(projectName), projectName));
        return await getResponseAsObject<projectType>(await apiRequest(
            buildUri($"sites/{session.SiteId}/projects", ("publishSamples", publishSamples.ToString())),
            HttpMethod.Post, 201, cancelToken, session,
           getObjectAsRequestContent(new projectType()
           {
               name = projectName,
               parentProjectId = parentProjectId,
               description = description,
               contentPermissions = contentPermissions,
               contentPermissionsSpecified = true
           })));
    }

    /// <summary>
    /// Queries projects. Available in all versions
    /// </summary>
    /// <param name="session"></param>
    /// <param name="pageSize">	(Optional) The number of items to return in one response. The minimum is 1. The maximum is 1000. The default is 100. </param>
    /// <param name="pageNumber">(Optional) The offset for paging. The default is 1.</param>
    /// <param name="filterExpression">(Optional) An expression that lets you specify a subset of data sources to return. You can filter on predefined fields such as name, ownerName, and parentProjectId. You can include multiple filter expressions.</param>
    /// <param name="sortExpression">(Optional) An expression that lets you specify the order in which user information is returned. If you do not specify a sort expression, the sort order of the information that's returned is undefined.</param>
    /// <param name="cancelToken"></param>
    /// <returns></returns>
    /// <exception cref="TableauRequestException"></exception>
    /// <exception cref="TableauApiVersionException"></exception>
    /// <exception cref="TableauOnlineNotSupportedException"></exception>
    [ApiVersion(2, 0)]
    public async Task<(paginationType pagination, projectType[] projects)> QueryProjects(TableauSession session,
        int pageSize = 100, int pageNumber = 1, string filterExpression = null, string sortExpression = null,
        CancellationToken? cancelToken = null)
    {

        checkEndpointAvailability();
        checkNullParameters((nameof(session), session));
        checkParametersBetween((nameof(pageSize), pageSize, 1, 1000), (nameof(pageNumber), pageNumber, 1, int.MaxValue));
        var args = new List<(string, string)>()
        {
            ("pageSize", pageSize.ToString(CultureInfo.InvariantCulture)),
            ("pageNumber", pageNumber.ToString(CultureInfo.InvariantCulture))
        };
        if (!string.IsNullOrEmpty(filterExpression))
            args.Add(("filter", filterExpression));
        if (!string.IsNullOrEmpty(sortExpression))
            args.Add(("sort", sortExpression));
        var ret = await getResponseAsObject<paginationType, projectListType>(
            await apiRequest(buildUri($"sites/{session.SiteId}/projects", args.ToArray()), HttpMethod.Get, 200,
                cancelToken, session));
        return (ret.Item1, ret.Item2.project);
    }


    /// <summary>
    /// Updates a project. Available in all versions
    /// </summary>
    /// <param name="session"></param>
    /// <param name="projectId">The ID of the project to update.</param>
    /// <param name="newProjectName">(Optional) The new name for the project.</param>
    /// <param name="newDescription">	(Optional) The new description for the project.</param>
    /// <param name="newContentPermissions">(Optional) Specify LockedToProject to lock permissions so that users cannot overwrite the default permissions set for the project, or specify ManagedByOwner to allow users to manage permissions for content that they own. For more information, see Lock Content Permissions to the Project. The default is ManagedByOwner.</param>
    /// <param name="newParentProjectId">	(Optional) The new identifier of the parent project. Use this option to create or change project hierarchies.</param>
    /// <param name="publishSamples">(Optional) A Boolean value that specifies whether to publish the sample workbooks provided by Tableau to the project. When the publish-value is not specified in the request, or the publishSamples parameter is missing, no samples will be published. To publish the sample workbooks, set publishSamples parameter to true. This option is equivalent to the tabcmd command-line utility option, publishsamples.</param>
    /// <param name="cancelToken"></param>
    /// <returns></returns>
    /// <exception cref="TableauRequestException"></exception>
    /// <exception cref="TableauApiVersionException"></exception>
    /// <exception cref="TableauOnlineNotSupportedException"></exception>
    [ApiVersion(1, 0)]
    public async Task<projectType> UpdateProject(TableauSession session, string projectId, string newProjectName, string newDescription,
        projectTypeContentPermissions? newContentPermissions = null,
        string newParentProjectId = null, bool publishSamples = false, CancellationToken? cancelToken = null)
    {
        checkEndpointAvailability();
        checkNullParameters((nameof(session), session), (nameof(projectId), projectId));
        return await getResponseAsObject<projectType>(await apiRequest(
            buildUri($"sites/{session.SiteId}/projects/{projectId}", ("publishSamples", publishSamples.ToString())),
            HttpMethod.Put, 200, cancelToken, session,
           getObjectAsRequestContent(new projectType()
           {
               name = newProjectName,
               parentProjectId = newParentProjectId,
               description = newDescription,
               contentPermissions = newContentPermissions ?? projectTypeContentPermissions.ManagedByOwner,
               contentPermissionsSpecified = newContentPermissions.HasValue
           })));
    }


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
    public async Task DeleteProject(TableauSession session, string projectId, CancellationToken? cancelToken = null)
    {
        checkEndpointAvailability();
        checkNullParameters((nameof(session), session), (nameof(projectId), projectId));
        await apiRequest(buildUri($"sites/{session.SiteId}/projects/{projectId}"), HttpMethod.Delete, 204,
            cancelToken, session);

    }
}
