using System;

namespace Tableau.Api.VizPortal.Models;

public class DatasourcesQueryResponse : QueryResponseModel
{
    public Datasource[] Datasources { get; set; }
    public ContentUser[] Users { get; set; }
    public ContentProjectDs[] Projects { get; set; }
}

public class Datasource
{
    public CertificationDetails CertificationDetails { get; set; }
    public DateTime? DatasourceModifiedAt { get; set; }
    public bool? HasAlert { get; set; }
    public DateTime? LastRefreshedAt { get; set; }
    public bool? ExtractCreationPending { get; set; }
    public bool? IsFileBased { get; set; }
    public int? ConnectedWorkbooksCount { get; set; }
    public bool? HasEncryptableExtracts { get; set; }
    public bool? HasIncrementalExtract { get; set; }
    public bool? IsExtractOnly { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string RepositoryUrl { get; set; }
    public string ModifiedByUserId { get; set; }
    public string ExtractEncryptionState { get; set; }
    public DataQualityIndicator[] DataQualityIndicators { get; set; }
    public bool? HasExtracts { get; set; }
    public string DownloadUrl { get; set; }
    public string Luid { get; set; }
    public int? DataConnectionsCount { get; set; }
    public string DatasourceId { get; set; } // Database ID in Datasources Table
    public string[] Tags { get; set; }
    public string Name { get; set; }
    public string ConnectionTypeDisplayName { get; set; }
    public bool? TestingConnectionAllowed { get; set; }
    public ConnectionDetails ConnectionDetails { get; set; }
    public bool? ConnectionTypeEditable { get; set; }
    public bool? ConnectionOAuth { get; set; }
    public bool? IsHierarchical { get; set; }
    public bool? UsingRemoteQueryAgent { get; set; }
    public string ConnectionType { get; set; }
    public string OwnerId { get; set; }
    public string Id { get; set; } // ID of the Content
}

/// <summary>
/// A data quality warning on a data source, one object per indicator, as Tableau Server 2025.1 sends them.
/// <see cref="Category"/> and <see cref="Type"/> are strings, so a value Tableau adds later still reads.
/// </summary>
public class DataQualityIndicator
{
    public string Id { get; set; }
    public string Category { get; set; }
    public string Type { get; set; }
    public string Message { get; set; }
    public bool? IsActive { get; set; }
    public bool? IsSevere { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public ContentUser Owner { get; set; }
}

public class CertificationDetails
{
    public bool? IsCertified { get; set; }
}

public class ConnectionDetails
{
    public bool? HasEmbeddedPassword { get; set; }
    public string DatabaseUsername { get; set; }
    public string FileName { get; set; }
    public string ServerName { get; set; }
    public string ServerPort { get; set; }
    public string Type { get; set; }
}
