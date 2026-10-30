using System;
using System.Collections.Generic;
using System.Text;

namespace Tableau.Api.Models;

public partial class granteeCapabilitiesType
{
    public string UserId
    {
        get => (Item as userType)?.id;
        set => Item = new userType() { id = value };
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
        get => personalAccessTokenNameField;
        set => personalAccessTokenNameField = value;
    }

    /// <remarks/>
    [System.Xml.Serialization.XmlAttributeAttribute()]
    public string personalAccessTokenSecret
    {
        get => personalAccessTokenSecretField;
        set => personalAccessTokenSecretField = value;
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
        get => isSevereField;
        set => isSevereField = value;
    }

    /// <remarks/>
    [System.Xml.Serialization.XmlIgnoreAttribute()]
    public bool isSevereSpecified
    {
        get => isSevereFieldSpecified;
        set => isSevereFieldSpecified = value;
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
