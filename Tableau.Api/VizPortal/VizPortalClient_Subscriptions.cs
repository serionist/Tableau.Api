using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Tableau.Api.Exceptions;
using Tableau.Api.Models;
using Tableau.Api.VizPortal.Models;

namespace Tableau.Api.VizPortal
{
    public partial class VizPortalClient
    {
        /// <summary>
        /// Returns Subscriptions for a single Workbook. Returns an enumerable that will paginate results in the background
        /// </summary>
        /// <param name="session">The session obtained from the top level client</param>
        /// <param name="workbookId">The workbook ID</param>
        /// <returns></returns>
        /// <exception cref="TableauRequestException"></exception>
        public IAsyncEnumerable<Subscription> QuerySubscriptionsForWorkbook(
            TableauSession session, string workbookId) => querySubscriptions(session, ("workbookId", workbookId));

        /// <summary>
        /// Returns Subscriptions for a single Schedule. Returns an enumerable that will paginate results in the background
        /// </summary>
        /// <param name="session">The session obtained from the top level client</param>
        /// <param name="siteId">The site ID</param>
        /// <param name="scheduleId">The schedule ID</param>
        /// <returns></returns>
        public async IAsyncEnumerable<Subscription> QuerySubscriptionsForSchedule(TableauSession session, string siteId, string scheduleId)
        {
            var scheduleInfo = await Request(session, "getSchedules", JObject.FromObject(new QueryModel()
            {
                Page = new QueryModel.PageModel()
                {
                    MaxItems = 1,
                    StartIndex = 0
                },
                Order = null,
                Filter = new QueryModel.FilterModel()
                {
                    Operator = "and",
                    Clauses = new[] {
                    new QueryModel.FilterModel.Clause()
                    {
                        Field = "siteId",
                        Operator = "eq",
                        Value = siteId.ToString()
                    },
                    new QueryModel.FilterModel.Clause()
                    {
                        Field = "id",
                        Operator = "eq",
                        Value = scheduleId.ToString()
                    },
                    }
                }
            }));
            if (!scheduleInfo.ContainsKey("schedules") || !(scheduleInfo.Property("schedules").Value is JArray j) || j.Count == 0)
                yield break;

            await foreach (var e in querySubscriptions(session, ("siteId", siteId), ("scheduleId", scheduleId)))
                yield return e;
        }

        /// <summary>
        /// Change an subscription schedule frequency
        /// </summary>
        /// <param name="session">The session obtained from the top level client</param>
        /// <param name="subscriptionId">The subscription ID</param>
        /// <param name="scheduleId">The schedule ID</param>
        /// <param name="subscribeOnExtractRefresh">Bind subscription to data refreshes</param>
        /// <returns></returns>
        public async Task ChangeSubscriptionFrequency(TableauSession session, int subscriptionId, int scheduleId, bool subscribeOnExtractRefresh = false)
        {
            var req = await Request<ChangeSubscriptionFrequency, Empty>(session, "setSubscriptionsSchedule",
                new ChangeSubscriptionFrequency()
                {
                    ScheduleId = scheduleId.ToString(),
                    Ids = new[] { subscriptionId.ToString() },
                    SubscribeOnExtractRefresh = subscribeOnExtractRefresh
                });
        }

        /// <summary>
        /// Delete a subscription
        /// </summary>
        /// <param name="session">The session obtained from the top level client</param>
        /// <param name="subscriptionId">The subscription ID</param>
        /// <returns></returns>
        public async Task DeleteSubscription(TableauSession session, int subscriptionId)
        {
            var req = await Request<DeleteSubscription, Empty>(session, "deleteSubscriptions",
                new DeleteSubscription()
                {
                    Ids = new[] { subscriptionId.ToString() }
                });
        }


        private IAsyncEnumerable<Subscription> querySubscriptions(TableauSession session, params (string field, string value)[] search)
        {
            var q = new QueryModel()
            {
                Filter = new QueryModel.FilterModel()
                {
                    Clauses = search.Select(s => new QueryModel.FilterModel.Clause()
                    {
                        Operator = "eq",
                        Field = s.field,
                        Value = s.value
                    }).ToArray(),
                    Operator = "and"
                },
                Order = new[]
                {
                    new QueryModel.OrderModel()
                    {
                        Field = "subject",
                        Ascending = true
                    }
                }
            };
            return PaginateRequest(session, "getSubscriptions", q, (SubscriptionsQueryResponse p) => p.Subscriptions);
        }
    }
}
