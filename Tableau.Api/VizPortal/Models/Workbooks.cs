using System;

namespace Tableau.Api.VizPortal.Models
{
    public class WorkbooksQueryResponse : QueryResponseModel
    {
        public Workbook[] Workbooks { get; set; }
        public ContentUser[] Users { get; set; }
        public ContentProjectWb[] Projects { get; set; }
    }

    public class Workbook
    {
        public string ThumbnailUrl { get; set; }
        public int? SheetCount { get; set; }
        public bool? HasAlert { get; set; }
        public bool? ExtractCreationPending { get; set; }
        public string DefaultViewUrl { get; set; }
        public bool? HasEncryptableExtracts { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public string RepositoryUrl { get; set; }
        public string ModifiedByUserId { get; set; }
        public bool? DisplayTabs { get; set; }
        public string ExtractEncryptionState { get; set; }
        public bool? HasExtracts { get; set; }
        public string DefaultViewId { get; set; }
        public string DownloadUrl { get; set; }
        public string Luid { get; set; }
        public string OwnerId { get; set; }
        public string[] Tags { get; set; }
        public string Description { get; set; }
        public string Name { get; set; }
        public string Id { get; set; }
        public double? Size { get; set; }
    }
}
