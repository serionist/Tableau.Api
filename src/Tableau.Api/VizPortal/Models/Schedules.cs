using System;

namespace Tableau.Api.VizPortal.Models;

public class ScheduleQueryResponse : QueryResponseModel
{
    public Schedule[] Schedules { get; set; }
}

public class Schedule
{
    public bool? EnabledForLinkedTasks { get; set; }
    public bool? Parallel { get; set; }
    public string Name { get; set; }
    public int? Priority { get; set; }
    public ScheduleDetails ScheduleDetails { get; set; }
    public string TimeZoneDisplayName { get; set; }
    public int? ServerTimezoneOffset { get; set; }
    public Enums.DefinedBy DefinedBy { get; set; }
    public DateTime? RunNextAt { get; set; }
    public string ScheduleType { get; set; }
    public string ScheduledAction { get; set; }
    public int? TaskCount { get; set; }
    public string Id { get; set; }
}

public class ScheduleDetails
{
    public int? StartTime { get; set; }
    public int? EndTime { get; set; }
    public int? RecurrenceMinutes { get; set; }
    public string[] RecurrenceDaysOfWeek { get; set; }
    public string RecurrenceDayOfMonth { get; set; }
}

public class TriggerSchedule
{
    public string[] Ids { get; set; }
}
