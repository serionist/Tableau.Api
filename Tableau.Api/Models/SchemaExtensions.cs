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

   
    public partial class tableauCredentialsType
    {


        private string personalAccessTokenNameField;

        private string personalAccessTokenSecretField;


        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public string personalAccessTokenName
        {
            get { return this.personalAccessTokenNameField; }
            set { this.personalAccessTokenNameField = value; }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public string personalAccessTokenSecret
        {
            get { return this.personalAccessTokenSecretField; }
            set { this.personalAccessTokenSecretField = value; }
        }

    }


}
