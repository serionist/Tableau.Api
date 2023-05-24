using System;
using System.Collections.Generic;
using System.Text;

namespace Tableau.Api.VizPortal.Models
{
    public class ExtractRefreshQueryResponse: QueryResponseModel
    {
        public ExtractRefreshTask[] Tasks { get; set; }
    }

    public class ExtractRefreshTask
    {
        public string Id { get; set; }
        public DateTime? LastRunAt { get; set; }
        public int? Priority { get; set; }
        public DateTime? RunNextAt { get; set; }
        public string ScheduleId { get; set; }
        public string SiteId { get; set; }
        public string State { get; set; }
        public string TargetId { get; set; }
        public string TargetType { get; set; }
        public string Type { get; set; }
    }

    public class ChangeExtractRefreshTaskSchedule
    {
        public string[] Ids { get; set; }
        public string ScheduleId { get; set; }
    }

    public class TriggerExtractRefreshTask
    {
        public string[] Ids { get; set; }
    }

}
