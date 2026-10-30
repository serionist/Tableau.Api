using System;
using System.Collections.Generic;
using System.Text;

namespace Tableau.Api.Models
{
    public class TableauSession
    {
        public TableauSession(tableauCredentialsType credentials)
        {
            UserId = credentials.user.id;
            // A sign-in response names no user, so the sign-in methods put the name they signed in with on its user.
            UserName = credentials.user?.name ?? credentials.name;
            Token = credentials.token;
            SiteId = credentials.site.id;
        }
        public TableauSession() { }

        public string UserId { get; set; }
        /// <summary>
        /// The name the session signed in with: the user name, the token's name for a personal access token, and after
        /// SwitchSite the name of the session it switched from. Under impersonation it is the signing-in
        /// administrator's name, while UserId is the impersonated user's.
        /// </summary>
        public string UserName { get; set; }
        public string Token { get; set; }
        public string SiteId { get; set; }
    }
}
