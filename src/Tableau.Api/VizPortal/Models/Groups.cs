using System;

namespace Tableau.Api.VizPortal.Models;

public class GroupsQueryResponse : QueryResponseModel
{
    public Group[] Groups { get; set; }
}

public class GroupsUserQueryResponse : QueryResponseModel
{
    public GroupUser[] Users { get; set; }
}

public class Group
{
    public bool? IsAllUsersGroup { get; set; }
    public int? UserCount { get; set; }
    public string DomainName { get; set; }
    public string Name { get; set; }
    public string Id { get; set; }
}

public class GroupUser
{
    public DateTime? LastSignIn { get; set; }
    public string[] SystemUserIdentities { get; set; }
    public string SiteRole { get; set; }
    public bool? NotificationEnabled { get; set; }
    public string DomainName { get; set; }
    public bool? HasEmail { get; set; }
    public string Luid { get; set; }
    public string DisplayName { get; set; }
    public string UserName { get; set; }
    public string Id { get; set; }
}

public class DeleteGroup
{
    public string[] Ids { get; set; }
}

public class GroupUserAction
{
    public string[] AddToGroupIds { get; set; }
    public string[] RemoveFromGroupIds { get; set; }
    public string[] UserIds { get; set; }
}

public class GetUserGroupMemberships
{
    public string[] UserIds { get; set; }
}
