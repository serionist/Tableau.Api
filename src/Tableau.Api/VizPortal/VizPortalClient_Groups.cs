using System;
using System.Collections.Generic;
using System.Globalization;
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
        Tableau.Api.Models.TableauSession session) => queryGroups(session);

    /// <summary>
    /// Returns users for a single Group. Returns an enumerable that will paginate results in the background
    /// </summary>
    /// <param name="session">The session obtained from the top level client</param>
    /// <param name="groupId">The group ID</param>
    /// <returns></returns>
    /// <exception cref="TableauRequestException"></exception>
    public IAsyncEnumerable<GroupUser> QueryGroupUsers(
        Tableau.Api.Models.TableauSession session, int groupId) =>
        queryGroupUsers(session, ("groupIds", groupId.ToString(CultureInfo.InvariantCulture)));

    /// <summary>
    /// Delete a group
    /// </summary>
    /// <param name="session">The session obtained from the top level client</param>
    /// <param name="groupId">The group ID</param>
    /// <returns></returns>
    public async Task DeleteGroup(Tableau.Api.Models.TableauSession session, int groupId)
    {
        var req = await Request<DeleteGroup, Empty>(session, "deleteGroups",
            new DeleteGroup()
            {
                Ids = new[] { groupId.ToString(CultureInfo.InvariantCulture) }
            });
    }

    /// <summary>
    /// Add user to a group
    /// </summary>
    /// <param name="session">The session obtained from the top level client</param>
    /// <param name="groupId">The group ID</param>
    /// <param name="userId">The user ID</param>
    /// <returns></returns>
    public async Task AddUserToGroup(Tableau.Api.Models.TableauSession session, int groupId, int userId)
    {
        var req = await Request<GroupUserAction, Empty>(session, "updateUsersGroupMembership",
            new GroupUserAction()
            {
                AddToGroupIds = new[] { groupId.ToString(CultureInfo.InvariantCulture) },
                RemoveFromGroupIds = Array.Empty<string>(),
                UserIds = new string[] { userId.ToString(CultureInfo.InvariantCulture) }
            });
    }

    /// <summary>
    /// Remove user from a group
    /// </summary>
    /// <param name="session">The session obtained from the top level client</param>
    /// <param name="groupId">The group ID</param>
    /// <param name="userId">The user ID</param>
    /// <returns></returns>
    public async Task RemoveUserFromGroup(Tableau.Api.Models.TableauSession session, int groupId, int userId)
    {
        var req = await Request<GroupUserAction, Empty>(session, "updateUsersGroupMembership",
            new GroupUserAction()
            {
                AddToGroupIds = Array.Empty<string>(),
                RemoveFromGroupIds = new[] { groupId.ToString(CultureInfo.InvariantCulture) },
                UserIds = new string[] { userId.ToString(CultureInfo.InvariantCulture) }
            });
    }

    private IAsyncEnumerable<GroupUser> queryGroupUsers(Tableau.Api.Models.TableauSession session, params (string field, string value)[] search)
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

    private IAsyncEnumerable<Group> queryGroups(Tableau.Api.Models.TableauSession session)
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
