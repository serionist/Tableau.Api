using System;
using System.Collections.Generic;
using System.Text;

namespace Tableau.Api.Models
{
    public class TableauSession
    {
        internal TableauSession(tableauCredentialsType credentials)
        {
            Credentials = credentials;
        }

        internal tableauCredentialsType Credentials { get; }

        public string UserId => Credentials.user.id;
        public string UserName => Credentials.name;
        public string Token => Credentials.token;
        public string SiteId => Credentials.site.id;
    }
}
