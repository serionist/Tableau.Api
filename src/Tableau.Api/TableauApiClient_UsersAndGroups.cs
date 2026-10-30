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
    /// Creates a group in Tableau Server. Available in all versions
    /// </summary>
    /// <param name="session"></param>
    /// <param name="groupName">The name for the new group.</param>
    /// <param name="cancelToken"></param>
    /// <returns></returns>
    /// <exception cref="TableauRequestException"></exception>
    /// <exception cref="TableauApiVersionException"></exception>
    /// <exception cref="TableauOnlineNotSupportedException"></exception>
    [ApiVersion(2, 0)]
    public async Task<groupType> CreateGroup(TableauSession session, string groupName,
        CancellationToken? cancelToken = null)
    {
        checkEndpointAvailability();
        checkNullParameters((nameof(session), session), (nameof(groupName), groupName));
        return await getResponseAsObject<groupType>(await apiRequest(buildUri($"sites/{session.SiteId}/groups"),
            HttpMethod.Post, 201, cancelToken, session,
            getObjectAsRequestContent(new groupType() { name = groupName })));
    }
    /// <summary>
    /// Creates a group in Tableau Server. This runs synchronously and may time out if the AD group contains a lot of users. Available in all versions
    /// </summary>
    /// <param name="session"></param>
    /// <param name="adGroupName">The name of the Active Directory group to import</param>
    /// <param name="domainName">The domain of the Active Directory group to import.</param>
    /// <param name="siteRole">The site role to assign to users who are imported from Active Directory. You can assign the following roles: Creator, Explorer, ExplorerCanPublish, SiteAdministratorExplorer, SiteAdministratorCreator, Unlicensed, or Viewer.</param>
    /// <param name="cancelToken"></param>
    /// <returns></returns>
    /// <exception cref="TableauRequestException"></exception>
    /// <exception cref="TableauApiVersionException"></exception>
    /// <exception cref="TableauOnlineNotSupportedException"></exception>
    [ApiVersion(2, 0)]
    public async Task<groupType> CreateGroupFromActiveDirectory(TableauSession session, string adGroupName,
        string domainName, siteRoleType siteRole, CancellationToken? cancelToken = null)
    {
        checkEndpointAvailability();
        checkNullParameters((nameof(session), session), (nameof(adGroupName), adGroupName), (nameof(domainName), domainName));
        return await getResponseAsObject<groupType>(await apiRequest(buildUri($"sites/{session.SiteId}/groups"),
            HttpMethod.Post, 201, cancelToken, session,
            getObjectAsRequestContent(new groupType()
            {
                name = adGroupName,
                import = new importDirectiveType()
                {
                    source = importSourceType.ActiveDirectory,
                    domainName = domainName,
                    siteRole = siteRole
                }
            })));
    }
    /// <summary>
    /// Creates a group in Tableau Server. This runs synchronously and may time out if the AD group contains a lot of users. Available in all versions
    /// </summary>
    /// <param name="session"></param>
    /// <param name="adGroupName">The name of the Active Directory group to import</param>
    /// <param name="domainName">The domain of the Active Directory group to import.</param>
    /// <param name="siteRole">The site role to assign to users who are imported from Active Directory. You can assign the following roles: Creator, Explorer, ExplorerCanPublish, SiteAdministratorExplorer, SiteAdministratorCreator, Unlicensed, or Viewer.</param>
    /// <param name="cancelToken"></param>
    /// <returns></returns>
    /// <exception cref="TableauRequestException"></exception>
    /// <exception cref="TableauApiVersionException"></exception>
    /// <exception cref="TableauOnlineNotSupportedException"></exception>
    [ApiVersion(2, 0)]
    public async Task<jobType> CreateGroupFromActiveDirectoryAsJob(TableauSession session, string adGroupName,
        string domainName, siteRoleType siteRole, CancellationToken? cancelToken = null)
    {
        checkEndpointAvailability();
        checkNullParameters((nameof(session), session), (nameof(adGroupName), adGroupName), (nameof(domainName), domainName));
        return await getResponseAsObject<jobType>(await apiRequest(buildUri($"sites/{session.SiteId}/groups", ("asJob", "true")),
            HttpMethod.Post, 202, cancelToken, session,
            getObjectAsRequestContent(new groupType()
            {
                name = adGroupName,
                import = new importDirectiveType()
                {
                    source = importSourceType.ActiveDirectory,
                    domainName = domainName,
                    siteRole = siteRole
                }
            })));
    }

    /// <summary>
    /// Creates a group in Tableau Server. Available in all versions
    /// </summary>
    /// <param name="session"></param>
    /// <param name="groupId">The id of the group</param>
    /// <param name="newGroupName">The new name for the group.</param>
    /// <param name="cancelToken"></param>
    /// <returns></returns>
    /// <exception cref="TableauRequestException"></exception>
    /// <exception cref="TableauApiVersionException"></exception>
    /// <exception cref="TableauOnlineNotSupportedException"></exception>
    [ApiVersion(2, 0)]
    public async Task<groupType> UpdateGroup(TableauSession session, string groupId, string newGroupName,
        CancellationToken? cancelToken = null)
    {
        checkEndpointAvailability();
        checkNullParameters((nameof(session), session), (nameof(newGroupName), newGroupName));
        return await getResponseAsObject<groupType>(await apiRequest(buildUri($"sites/{session.SiteId}/groups/{groupId}"),
            HttpMethod.Put, 200, cancelToken, session,
            getObjectAsRequestContent(new groupType() { name = newGroupName })));
    }
    /// <summary>
    /// Creates a group in Tableau Server. This runs synchronously and may time out if the AD group contains a lot of users. Available in all versions
    /// </summary>
    /// <param name="session"></param>
    /// <param name="newAdGroupName">The name of the Active Directory group to import</param>
    /// <param name="newDomainName">The domain of the Active Directory group to import.</param>
    /// <param name="newSiteRole">The site role to assign to users who are imported from Active Directory. You can assign the following roles: Creator, Explorer, ExplorerCanPublish, SiteAdministratorExplorer, SiteAdministratorCreator, Unlicensed, or Viewer.</param>
    /// <param name="cancelToken"></param>
    /// <returns></returns>
    /// <exception cref="TableauRequestException"></exception>
    /// <exception cref="TableauApiVersionException"></exception>
    /// <exception cref="TableauOnlineNotSupportedException"></exception>
    [ApiVersion(2, 0)]
    public async Task<groupType> UpdateGroupFromActiveDirectory(TableauSession session, string newAdGroupName,
        string newDomainName, siteRoleType newSiteRole, CancellationToken? cancelToken = null)
    {
        checkEndpointAvailability();
        checkNullParameters((nameof(session), session), (nameof(newAdGroupName), newAdGroupName), (nameof(newDomainName), newDomainName));
        return await getResponseAsObject<groupType>(await apiRequest(buildUri($"sites/{session.SiteId}/groups"),
            HttpMethod.Put, 200, cancelToken, session,
            getObjectAsRequestContent(new groupType()
            {
                name = newAdGroupName,
                import = new importDirectiveType()
                {
                    source = importSourceType.ActiveDirectory,
                    domainName = newDomainName,
                    siteRole = newSiteRole
                }
            })));
    }
    /// <summary>
    /// Creates a group in Tableau Server. This runs synchronously and may time out if the AD group contains a lot of users. Available in all versions
    /// </summary>
    /// <param name="session"></param>
    /// <param name="newAdGroupName">The name of the Active Directory group to import</param>
    /// <param name="newDomainName">The domain of the Active Directory group to import.</param>
    /// <param name="siteRole">The site role to assign to users who are imported from Active Directory. You can assign the following roles: Creator, Explorer, ExplorerCanPublish, SiteAdministratorExplorer, SiteAdministratorCreator, Unlicensed, or Viewer.</param>
    /// <param name="cancelToken"></param>
    /// <returns></returns>
    /// <exception cref="TableauRequestException"></exception>
    /// <exception cref="TableauApiVersionException"></exception>
    /// <exception cref="TableauOnlineNotSupportedException"></exception>
    [ApiVersion(2, 0)]
    public async Task<jobType> UpdateGroupFromActiveDirectoryAsJob(TableauSession session, string newAdGroupName,
        string newDomainName, siteRoleType siteRole, CancellationToken? cancelToken = null)
    {
        checkEndpointAvailability();
        checkNullParameters((nameof(session), session), (nameof(newAdGroupName), newAdGroupName), (nameof(newDomainName), newDomainName));
        return await getResponseAsObject<jobType>(await apiRequest(buildUri($"sites/{session.SiteId}/groups", ("asJob", "true")),
            HttpMethod.Put, 202, cancelToken, session,
            getObjectAsRequestContent(new groupType()
            {
                name = newAdGroupName,
                import = new importDirectiveType()
                {
                    source = importSourceType.ActiveDirectory,
                    domainName = newDomainName,
                    siteRole = siteRole
                }
            })));
    }
    /// <summary>
    /// Adds a user to a group in Tableau Server. Available in all versions
    /// </summary>
    /// <param name="session"></param>
    /// <param name="groupId">The ID of the group to add the user to.</param>
    /// <param name="userId">The ID of the site that contains the group.</param>
    /// <param name="cancelToken"></param>
    /// <returns></returns>
    /// <exception cref="TableauRequestException"></exception>
    /// <exception cref="TableauApiVersionException"></exception>
    /// <exception cref="TableauOnlineNotSupportedException"></exception>
    [ApiVersion(2, 0)]
    public async Task<userType> AddUserToGroup(TableauSession session, string groupId, string userId,
        CancellationToken? cancelToken = null)
    {
        checkEndpointAvailability();
        checkNullParameters((nameof(session), session), (nameof(groupId), groupId), (nameof(userId), userId));
        return await getResponseAsObject<userType>(await apiRequest(buildUri($"sites/{session.SiteId}/groups/{groupId}/users"),
            HttpMethod.Post, 200, cancelToken, session,
            getObjectAsRequestContent(new userType() { id = userId })));
    }

    /// <summary>
    /// Adds a user to a group in Tableau Server. Available in all versions
    /// </summary>
    /// <param name="session"></param>
    /// <param name="userName">The name of the user. If the server uses local authentication, this can be any name. If you are using Active Directory authentication, or if you are using Tableau Online, there are specific requirements for the name.</param>
    /// <param name="authSetting"></param>
    /// <param name="cancelToken"></param>
    /// <param name="siteRole"></param>
    /// <returns></returns>
    /// <exception cref="TableauRequestException"></exception>
    /// <exception cref="TableauApiVersionException"></exception>
    /// <exception cref="TableauOnlineNotSupportedException"></exception>
    [ApiVersion(2, 0)]
    public async Task<userType> AddUserToSite(TableauSession session, string userName,
        siteRoleType siteRole, siteUserAuthSettingType? authSetting = null,
        CancellationToken? cancelToken = null)
    {
        checkEndpointAvailability();
        checkNullParameters((nameof(session), session), (nameof(userName), userName));
        return await getResponseAsObject<userType>(await apiRequest(buildUri($"sites/{session.SiteId}/users"),
            HttpMethod.Post, 201, cancelToken, session,
            getObjectAsRequestContent(new userType()
            {
                name = userName,
                siteRole = siteRole,
                siteRoleSpecified = true,
                authSetting = authSetting ?? siteUserAuthSettingType.ServerDefault,
                authSettingSpecified = authSetting.HasValue
            })));
    }

    /// <summary>
    /// Gets the groups of a user. Can only be called by site/server administrators. Available 3.7
    /// </summary>
    /// <param name="session"></param>
    /// <param name="userId">The ID of the user to get the groups for.</param>
    /// <param name="pageSize">(Optional) The number of items to return in one response. The minimum is 1. The maximum is 1000. The default is 100.</param>
    /// <param name="pageNumber">(Optional) The offset for paging. The default is 1.</param>
    /// <param name="cancelToken"></param>
    /// <returns></returns>
    /// <exception cref="TableauRequestException"></exception>
    /// <exception cref="TableauApiVersionException"></exception>
    /// <exception cref="TableauOnlineNotSupportedException"></exception>
    [ApiVersion(3, 7)]
    public async Task<(paginationType pagination, groupType[] groups)> GetGroupsOfUser(TableauSession session,
        string userId, int pageSize = 100, int pageNumber = 1, CancellationToken? cancelToken = null)
    {
        checkEndpointAvailability();
        checkNullParameters((nameof(session), session), (nameof(userId), userId));
        checkParametersBetween((nameof(pageSize), pageSize, 1, 1000), (nameof(pageNumber), pageNumber, 1, int.MaxValue));
        var ret = await getResponseAsObject<paginationType, groupListType>(await apiRequest(
            buildUri($"sites/{session.SiteId}/users/{userId}/groups",
                ("pageSize", pageSize.ToString(CultureInfo.InvariantCulture)),
                ("pageNumber", pageNumber.ToString(CultureInfo.InvariantCulture))), HttpMethod.Get, 200, cancelToken,
            session));
        return (ret.Item1, ret.Item2.group);
    }

    /// <summary>
    /// Gets users in a group. Available in all versions
    /// </summary>
    /// <param name="session"></param>
    /// <param name="groupId">The ID of the group to get the users for.</param>
    /// <param name="pageSize">(Optional) The number of items to return in one response. The minimum is 1. The maximum is 1000. The default is 100.</param>
    /// <param name="pageNumber">(Optional) The offset for paging. The default is 1.</param>
    /// <param name="cancelToken"></param>
    /// <returns></returns>
    /// <exception cref="TableauRequestException"></exception>
    /// <exception cref="TableauApiVersionException"></exception>
    /// <exception cref="TableauOnlineNotSupportedException"></exception>
    [ApiVersion(2, 0)]
    public async Task<(paginationType pagination, userType[] users)> GetUsersInGroup(TableauSession session,
        string groupId, int pageSize = 100, int pageNumber = 1, CancellationToken? cancelToken = null)
    {
        checkEndpointAvailability();
        checkNullParameters((nameof(session), session), (nameof(groupId), groupId));
        checkParametersBetween((nameof(pageSize), pageSize, 1, 1000), (nameof(pageNumber), pageNumber, 1, int.MaxValue));
        var ret = await getResponseAsObject<paginationType, userListType>(await apiRequest(
            buildUri($"sites/{session.SiteId}/groups/{groupId}/users",
                ("pageSize", pageSize.ToString(CultureInfo.InvariantCulture)),
                ("pageNumber", pageNumber.ToString(CultureInfo.InvariantCulture))), HttpMethod.Get, 200, cancelToken,
            session));
        return (ret.Item1, ret.Item2.user);
    }
    /// <summary>
    /// Gets users on current site. Available in all versions
    /// </summary>
    /// <param name="session"></param>
    /// <param name="pageSize">	(Optional) The number of items to return in one response. The minimum is 1. The maximum is 1000. The default is 100.</param>
    /// <param name="pageNumber">(Optional) The offset for paging. The default is 1.</param>
    /// <param name="fieldsExpression">(Optional) An expression that lets you specify the set of available fields to return. You can qualify the return values based upon predefined keywords such as _all_ or _default_, and you can specify individual fields for the views or other supported resources. You can include multiple field expressions in a request. </param>
    /// <param name="filterExpression">(Optional) An expression that lets you specify a subset of users to return. You can filter on predefined fields such as name and lastLogin. You can include multiple filter expressions.</param>
    /// <param name="sortExpression">(Optional) An expression that lets you specify the order in which user information is returned. If you do not specify a sort expression, the sort order of the information that's returned is undefined.</param>
    /// <param name="cancelToken"></param>
    /// <returns></returns>
    /// <exception cref="TableauRequestException"></exception>
    /// <exception cref="TableauApiVersionException"></exception>
    /// <exception cref="TableauOnlineNotSupportedException"></exception>
    [ApiVersion(2, 0)]
    public async Task<(paginationType pagination, userType[] users)> GetUsersOnSite(TableauSession session,
        int pageSize = 100, int pageNumber = 1, string fieldsExpression = null, string filterExpression = null,
        string sortExpression = null, CancellationToken? cancelToken = null)
    {
        checkEndpointAvailability();
        checkNullParameters((nameof(session), session));
        checkParametersBetween((nameof(pageSize), pageSize, 1, 1000), (nameof(pageNumber), pageNumber, 1, int.MaxValue));
        var args = new List<(string, string)>()
        {
            ("pageSize", pageSize.ToString(CultureInfo.InvariantCulture)),
            ("pageNumber", pageNumber.ToString(CultureInfo.InvariantCulture))
        };
        if (!string.IsNullOrEmpty(fieldsExpression))
            args.Add(("fields", fieldsExpression));
        if (!string.IsNullOrEmpty(filterExpression))
            args.Add(("filter", filterExpression));
        if (!string.IsNullOrEmpty(sortExpression))
            args.Add(("sort", sortExpression));
        var ret = await getResponseAsObject<paginationType, userListType>(await apiRequest(
            buildUri($"sites/{session.SiteId}/users", args.ToArray()), HttpMethod.Get, 200, cancelToken, session));
        return (ret.Item1, ret.Item2.user);
    }
    /// <summary>
    /// Queries groups on current site. Available in all versions
    /// </summary>
    /// <param name="session"></param>
    /// <param name="pageSize">(Optional) The number of items to return in one response. The minimum is 1. The maximum is 1000. The default is 100.</param>
    /// <param name="pageNumber">(Optional) The offset for paging. The default is 1.</param>
    /// <param name="filterExpression">(Optional) An expression that lets you specify a subset of groups to return. You can filter on predefined fields such as name. You can include multiple filter expressions. </param>
    /// <param name="sortExpression">(Optional) An expression that lets you specify the order in which user information is returned. If you do not specify a sort expression, the sort order of the information that's returned is undefined.</param>
    /// <param name="cancelToken"></param>
    /// <returns></returns>
    /// <exception cref="TableauRequestException"></exception>
    /// <exception cref="TableauApiVersionException"></exception>
    /// <exception cref="TableauOnlineNotSupportedException"></exception>
    [ApiVersion(2, 0)]
    public async Task<(paginationType pagination, groupType[] groups)> QueryGroups(TableauSession session,
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
        var ret = await getResponseAsObject<paginationType, groupListType>(await apiRequest(
            buildUri($"sites/{session.SiteId}/groups", args.ToArray()), HttpMethod.Get, 200, cancelToken, session));
        return (ret.Item1, ret.Item2.group);
    }
    /// <summary>
    /// Queries a user on the current site. Available in all versions
    /// </summary>
    /// <param name="session"></param>
    /// <param name="userId">The ID of the user to get information for.</param>
    /// <param name="cancelToken"></param>
    /// <returns></returns>
    /// <exception cref="TableauRequestException"></exception>
    /// <exception cref="TableauApiVersionException"></exception>
    /// <exception cref="TableauOnlineNotSupportedException"></exception>
    [ApiVersion(2, 0)]
    public async Task<userType> QueryUser(TableauSession session, string userId,
        CancellationToken? cancelToken = null)
    {
        checkEndpointAvailability();
        checkNullParameters((nameof(session), session), (nameof(userId), userId));
        return await getResponseAsObject<userType>(await apiRequest(
            buildUri($"sites/{session.SiteId}/users/{userId}"), HttpMethod.Get, 200,
            cancelToken, session));

    }
    /// <summary>
    /// Updates a user on the current site. Available in all versions
    /// </summary>
    /// <param name="session"></param>
    /// <param name="userId">The ID of the user to update.</param>
    /// <param name="newFullName">(Optional) The new name for the user. Users can change names without affecting the groups they belong to. Tableau Server only. Not available in Tableau Online.</param>
    /// <param name="newEmail">	(Optional) The new email address for the user. Tableau Server only. Not available in Tableau Online.</param>
    /// <param name="newPassword">(Optional) The new password for the user. Tableau Server only. Not available in Tableau Online.</param>
    /// <param name="newSiteRole">(Optional) The new site role. Valid role names are ServerAdministratorExplorer, ExplorerCanPublish, SiteAdministrator, and Unlicensed.</param>
    /// <param name="newAuthSetting">(Optional) The new authentication type for the user. You can assign the following values for this attribute: SAML (the user signs in using SAML) or ServerDefault (the user signs in using the authentication method that's set for the server). These values appear on the Authentication tab on the Settings page in Tableau Online the SAML attribute value corresponds to Single sign-on, and the ServerDefault value corresponds to TableauID.</param>
    /// <param name="newDisplayName">(Optional, EXPERIMENTAL) The new display name of the user. This is an UNTESTED EXPERIMENTAL feature</param>
    /// <param name="cancelToken"></param>
    /// <returns></returns>
    /// <exception cref="TableauRequestException"></exception>
    /// <exception cref="TableauApiVersionException"></exception>
    /// <exception cref="TableauOnlineNotSupportedException"></exception>
    [ApiVersion(2, 0)]
    public async Task<userType> UpdateUser(TableauSession session, string userId, string newFullName = null,
        string newEmail = null, string newPassword = null, siteRoleType? newSiteRole = null,
        siteUserAuthSettingType? newAuthSetting = null, string newDisplayName = null, CancellationToken? cancelToken = null)
    {
        checkEndpointAvailability();
        checkNullParameters((nameof(session), session), (nameof(userId), userId));
        return await getResponseAsObject<userType>(await apiRequest(
            buildUri($"sites/{session.SiteId}/users/{userId}"), HttpMethod.Put, 200,
            cancelToken, session,
            getObjectAsRequestContent(new userType()
            {
                name = newDisplayName,
                fullName = newFullName,
                email = newEmail,
                password = newPassword,
                siteRole = newSiteRole ?? siteRoleType.Unlicensed,
                siteRoleSpecified = newSiteRole.HasValue,
                authSetting = newAuthSetting ?? siteUserAuthSettingType.ServerDefault,
                authSettingSpecified = newAuthSetting.HasValue
            })));
    }
    /// <summary>
    /// Removes a user from a group. Available in all versions
    /// </summary>
    /// <param name="session"></param>
    /// <param name="groupId">The ID of the group to remove the user from.</param>
    /// <param name="userId">The ID of the user to remove.</param>
    /// <param name="cancelToken"></param>
    /// <returns></returns>
    /// <exception cref="TableauRequestException"></exception>
    /// <exception cref="TableauApiVersionException"></exception>
    /// <exception cref="TableauOnlineNotSupportedException"></exception>
    [ApiVersion(2, 0)]
    public async Task RemoveUserFromGroup(TableauSession session, string groupId, string userId,
        CancellationToken? cancelToken = null)
    {
        checkEndpointAvailability();
        checkNullParameters((nameof(session), session), (nameof(userId), userId), (nameof(groupId), groupId));
        await apiRequest(buildUri($"sites/{session.SiteId}/groups/{groupId}/users/{userId}"),
            HttpMethod.Delete, 204, cancelToken, session);
    }
    /// <summary>
    /// Removes a user from a site. Available in all versions
    /// </summary>
    /// <param name="session"></param>
    /// <param name="userId">The ID of the user to remove.</param>
    /// <param name="cancelToken"></param>
    /// <returns></returns>
    /// <exception cref="TableauRequestException"></exception>
    /// <exception cref="TableauApiVersionException"></exception>
    /// <exception cref="TableauOnlineNotSupportedException"></exception>
    [ApiVersion(2, 0)]
    public async Task RemoveUserFromSite(TableauSession session, string userId,
        CancellationToken? cancelToken = null)
    {
        checkEndpointAvailability();
        checkNullParameters((nameof(session), session), (nameof(userId), userId));
        await apiRequest(buildUri($"sites/{session.SiteId}/users/{userId}"),
            HttpMethod.Delete, 204, cancelToken, session);
    }

    /// <summary>
    /// Deletes a group. Not available in Tableau Online. Available in all versions. 
    /// </summary>
    /// <param name="session"></param>
    /// <param name="groupId">The ID of the group to delete.</param>
    /// <param name="cancelToken"></param>
    /// <returns></returns>
    /// <exception cref="TableauRequestException"></exception>
    /// <exception cref="TableauApiVersionException"></exception>
    /// <exception cref="TableauOnlineNotSupportedException"></exception>
    [ApiVersion(2, 0)]
    [OnPremiseOnly]
    public async Task DeleteGroup(TableauSession session, string groupId,
        CancellationToken? cancelToken = null)
    {
        checkEndpointAvailability();
        checkNullParameters((nameof(session), session), (nameof(groupId), groupId));
        await apiRequest(buildUri($"sites/{session.SiteId}/groups/{groupId}"),
            HttpMethod.Delete, 204, cancelToken, session);
    }
}
