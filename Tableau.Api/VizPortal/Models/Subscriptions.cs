using System;

namespace Tableau.Api.VizPortal.Models
{
    public class SubscriptionsQueryResponse : QueryResponseModel
    {
        public Subscription[] Subscriptions { get; set; }
    }

    public class Subscription
    {
        public string CreatorUserStatus { get; set; }
        public bool? CanManageSubscription { get; set; }
        public bool? RefreshExtractTriggered { get; set; }
        public bool? ExtractRefreshConvertable { get; set; }
        public bool? AttachImage { get; set; }
        public bool? AttachPdf { get; set; }
        public bool? CanSpecifyDataCondition { get; set; }
        public string TargetPath { get; set; }
        public string TargetId { get; set; }
        public DateTime? RunNextAt { get; set; }
        public string CreatorId { get; set; }
        public string ScheduleId { get; set; }
        public bool? Failed { get; set; }
        public string DataConditionType { get; set; }
        public Enums.PageOrientation PageOrientation { get; set; }
        public Enums.PageType PageSizeOption { get; set; }
        public string SiteId { get; set; }
        public string UserId { get; set; }
        public Enums.TargetType TargetType { get; set; }
        public string Subject { get; set; }
        public bool? Suspended { get; set; }
        public string TargetName { get; set; }
        public string Id { get; set; }
    }

    public class ChangeSubscriptionFrequency
    {
        public string[] Ids { get; set; }
        public string ScheduleId { get; set; }
        public bool? SubscribeOnExtractRefresh { get; set; }
    }

    public class DeleteSubscription
    {
        public string[] Ids { get; set; }
    }

    public class RunSubscription
    {
        public string[] Ids { get; set; }
    }


}
