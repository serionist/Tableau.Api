using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Tableau.Api.Exceptions;
using Tableau.Api.Models;
using Tableau.Api.VizPortal.Models;

namespace Tableau.Api.VizPortal;

public partial class VizPortalClient
{
    /// <summary>
    /// Returns Users for a site. Returns an enumerable that will paginate results in the background with next page tokens
    /// </summary>
    /// <param name="session">The session obtained from the top level client</param>
    /// <returns></returns>
    /// <exception cref="TableauRequestException"></exception>
    public IAsyncEnumerable<User> QueryUsers(
        TableauSession session) => queryUsers(session);

    /// <summary>
    /// Change a user site role
    /// </summary>
    /// <param name="session">The session obtained from the top level client</param>
    /// <param name="userId">The user ID</param>
    /// <param name="role">The role that the user receives</param>
    /// <returns></returns>
    public async Task ChangeUserRole(TableauSession session, int userId, string role)
    {
        var req = await Request<ChangeUserRole, Empty>(session, "updateUsersSiteRole",
            new ChangeUserRole()
            {
                SiteRole = role,
                UserIds = new[] { userId.ToString() }
            });
    }

    /// <summary>
    /// Delete a user
    /// </summary>
    /// <param name="session">The session obtained from the top level client</param>
    /// <param name="userId">The user ID</param>
    /// <returns></returns>
    public async Task DeleteUser(TableauSession session, int userId)
    {
        var req = await Request<DeleteUser, Empty>(session, "deleteUsers",
            new DeleteUser()
            {
                Ids = new[] { userId.ToString() }
            });
    }

    /// <summary>
    /// User group memberships
    /// </summary>
    /// <param name="session">The session obtained from the top level client</param>
    /// <param name="userId">The user ID</param>
    /// <returns></returns>
    public async IAsyncEnumerable<UserGroup> UserGroupMemberships(TableauSession session, int userId)
    {
        var req = await Request<UserGroupMembership, UserGroupResponse>(session, "getUsersGroupMembership",
            new UserGroupMembership()
            {
                userIds = new[] { userId.ToString() }
            });

        foreach (var res in req.Groups ?? Array.Empty<UserGroup>())
        {
            yield return res;
        }
    }

    private IAsyncEnumerable<User> queryUsers(TableauSession session)
    {
        var q = new QueryModel()
        {
            Order = new[]
            {
                new QueryModel.OrderModel()
                {
                    Field = "displayName",
                    Ascending = true
                }
            }
        };
        return NextPageTokenRequest(session, "getSiteUsersWithToken", q, (UsersQueryResponse u) => u.Users);
    }
}
