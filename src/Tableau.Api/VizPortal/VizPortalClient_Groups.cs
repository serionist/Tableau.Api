using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Tableau.Api.Exceptions;
using Tableau.Api.Models;
using Tableau.Api.VizPortal.Models;

namespace Tableau.Api.VizPortal;

public partial class VizPortalClient
{
    /// <summary>
    /// Returns Groups for a site. Returns an enumerable that will paginate results in the background
    /// </summary>
    /// <param name="session">The session obtained from the top level client</param>
    /// <returns></returns>
    /// <exception cref="TableauRequestException"></exception>
    public IAsyncEnumerable<Group> QueryGroups(
        TableauSession session) => queryGroups(session);

    /// <summary>
    /// Returns users for a single Group. Returns an enumerable that will paginate results in the background
    /// </summary>
    /// <param name="session">The session obtained from the top level client</param>
    /// <param name="groupId">The group ID</param>
    /// <returns></returns>
    /// <exception cref="TableauRequestException"></exception>
    public IAsyncEnumerable<GroupUser> QueryGroupUsers(
        TableauSession session, int groupId) => queryGroupUsers(session, ("groupIds", groupId.ToString()));

    /// <summary>
    /// Delete a group
    /// </summary>
    /// <param name="session">The session obtained from the top level client</param>
    /// <param name="groupId">The group ID</param>
    /// <returns></returns>
    public async Task DeleteGroup(TableauSession session, int groupId)
    {
        var req = await Request<DeleteGroup, Empty>(session, "deleteGroups",
            new DeleteGroup()
            {
                Ids = new[] { groupId.ToString() }
            });
    }

    /// <summary>
    /// Add user to a group
    /// </summary>
    /// <param name="session">The session obtained from the top level client</param>
    /// <param name="groupId">The group ID</param>
    /// <param name="userId">The user ID</param>
    /// <returns></returns>
    public async Task AddUserToGroup(TableauSession session, int groupId, int userId)
    {
        var req = await Request<GroupUserAction, Empty>(session, "updateUsersGroupMembership",
            new GroupUserAction()
            {
                AddToGroupIds = new[] { groupId.ToString() },
                RemoveFromGroupIds = Array.Empty<string>(),
                UserIds = new string[] { userId.ToString() }
            });
    }

    /// <summary>
    /// Remove user from a group
    /// </summary>
    /// <param name="session">The session obtained from the top level client</param>
    /// <param name="groupId">The group ID</param>
    /// <param name="userId">The user ID</param>
    /// <returns></returns>
    public async Task RemoveUserFromGroup(TableauSession session, int groupId, int userId)
    {
        var req = await Request<GroupUserAction, Empty>(session, "updateUsersGroupMembership",
            new GroupUserAction()
            {
                AddToGroupIds = Array.Empty<string>(),
                RemoveFromGroupIds = new[] { groupId.ToString() },
                UserIds = new string[] { userId.ToString() }
            });
    }

    private IAsyncEnumerable<GroupUser> queryGroupUsers(TableauSession session, params (string field, string value)[] search)
    {
        var q = new QueryModel()
        {
            Filter = new QueryModel.FilterModel()
            {
                Clauses = search.Select(s => new QueryModel.FilterModel.Clause()
                {
                    Operator = "has",
                    Field = s.field,
                    Value = s.value
                }).ToArray(),
                Operator = "and"
            },
            Order = new[]
            {
                new QueryModel.OrderModel()
                {
                    Field = "displayName",
                    Ascending = true
                }
            }
        };
        return PaginateRequest(session, "getSiteUsers", q, (GroupsUserQueryResponse p) => p.Users);
    }

    private IAsyncEnumerable<Group> queryGroups(TableauSession session)
    {
        var q = new QueryModel()
        {
            Order = new[]
            {
                new QueryModel.OrderModel()
                {
                    Field = "name",
                    Ascending = true
                }
            }
        };
        return PaginateRequest(session, "getGroups", q, (GroupsQueryResponse p) => p.Groups);
    }
}
