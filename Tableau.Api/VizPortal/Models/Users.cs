using System;

namespace Tableau.Api.VizPortal.Models
{
    public class UsersQueryResponse : QueryResponseModel
    {
        public User[] Users { get; set; }
    }

    public class User
    {
        public DateTime? LastSignIn { get; set; }
        public string SiteRole { get; set; }
        public bool? NotificationEnabled { get; set; }
        public string DomainName { get; set; }
        public bool? HasEmail { get; set; }
        public string Luid { get; set; }
        public string DisplayName { get; set; }
        public string UserName { get; set; }
        public string Id { get; set; }
    }

    public class ChangeUserRole
    {
        public string SiteRole { get; set; }
        public string[] UserIds { get; set; }
    }
}
