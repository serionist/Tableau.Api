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

    public partial class dataQualityWarningType
    {

        private bool isSevereField;

        private bool isSevereFieldSpecified;

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public bool isSevere
        {
            get
            {
                return this.isSevereField;
            }
            set
            {
                this.isSevereField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlIgnoreAttribute()]
        public bool isSevereSpecified
        {
            get
            {
                return this.isSevereFieldSpecified;
            }
            set
            {
                this.isSevereFieldSpecified = value;
            }
        }

    }

    public enum capabilityTypeName
    {

        /// <remarks/>
        AddComment,

        /// <remarks/>
        ChangeHierarchy,

        /// <remarks/>
        ChangePermissions,

        /// <remarks/>
        Connect,

        /// <remarks/>
        CreateRefreshMetrics,

        /// <remarks/>
        Delete,

        /// <remarks/>
        RunExplainData,

        /// <remarks/>
        Execute,

        /// <remarks/>
        ExportData,

        /// <remarks/>
        ExportImage,

        /// <remarks/>
        ExportXml,

        /// <remarks/>
        Filter,

        /// <remarks/>
        ProjectLeader,

        /// <remarks/>
        Read,

        /// <remarks/>
        ShareView,

        /// <remarks/>
        ViewComments,

        /// <remarks/>
        ViewUnderlyingData,

        /// <remarks/>
        WebAuthoring,

        /// <remarks/>
        WebAuthoringForFlows,

        /// <remarks/>
        Write,

        /// <remarks/>
        SaveAs,
    }
}
