using System;
using System.Collections.Generic;
using System.Text;

namespace Tableau.Api.Models
{
    public partial class granteeCapabilitiesType
    {
        public string UserId
        {
            get => (Item as userType)?.id;
            set => Item = new userType() {id = value};
        }

        public string GroupId
        {
            get => (Item as groupType)?.id;
            set => Item = new groupType() { id = value };
        }
    }
}
