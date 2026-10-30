using System;

namespace Tableau.Api.VizPortal.Models
{
    public class ProjectQueryResponse : QueryResponseModel
    {
        public Project[] Projects { get; set; }
    }

    public class Project
    {
        public int? DatabaseCount { get; set; }
        public int? TableCount { get; set; }
        public int? ChildProjectCount { get; set; }
        public int? WorkbookCount { get; set; }
        public int? DatasourceCount { get; set; }
        public int? FavoritesTotal { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public bool? TopLevelProject { get; set; }
        public int? ViewCount { get; set; }
        public bool? ControlledPermissionsEnabled { get; set; }
        public bool? NestedProjectsPermissionsIncluded { get; set; }
        public string Luid { get; set; }
        public DateTime? CreatedAt { get; set; }
        public string OwnerId { get; set; }
        public string Description { get; set; }
        public string Name { get; set; }
        public string Id { get; set; }
    }

    public class ChangeProjectName
    {
        public string Id { get; set; }
        public string Name { get; set; }
    }

    public class MoveProject
    {
        public string ContainerId { get; set; } // Target Project Id
        public string ContainerType { get; set; } = "PROJECT";
        public ProjectContentItems[] ContentItems { get; set; }

        public class ProjectContentItems
        {
            public string ContentId { get; set; } // Project Id that will be moved
            public string ContentType { get; set; } = "project";
            public string MoveType { get; set; } = null;
        }
    }

    public class DeleteProject
    {
        public string[] Ids { get; set; }
    }

    public class ChangeProjectOwner
    {
        public string[] Ids { get; set; } // Project ids
        public int OwnerId { get; set; } // New Owner User Site Id
    }
}
