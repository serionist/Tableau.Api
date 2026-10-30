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
            UserName = credentials.name;
            Token = credentials.token;
            SiteId = credentials.site.id;
        }
        public TableauSession() { }

        public string UserId { get; set; }
        public string UserName { get; set; }
        public string Token { get; set; }
        public string SiteId { get; set; }
    }
}
