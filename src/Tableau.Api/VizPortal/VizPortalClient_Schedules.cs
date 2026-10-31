using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Tableau.Api.Models;
using Tableau.Api.VizPortal.Models;

namespace Tableau.Api.VizPortal;

public partial class VizPortalClient
{
    /// <summary>
    /// Returns with the Schedules. Returns an enumerable that will paginate results in the background
    /// </summary>
    /// <param name="session">The session obtained from the top level client</param>
    /// <param name="siteId">The site ID</param>
    /// <returns></returns>
    public IAsyncEnumerable<Schedule> QuerySchedules(
        Tableau.Api.Models.TableauSession session, string siteId) => querySchedules(session, ("siteId", siteId));

    /// <summary>
    /// Returns with a single Schedule
    /// </summary>
    /// <param name="session">The session obtained from the top level client</param>
    /// <param name="siteId">The site ID</param>
    /// <param name="scheduleId">The schedule ID</param>
    /// <returns></returns>
    public IAsyncEnumerable<Schedule> QuerySingleSchedule(
        Tableau.Api.Models.TableauSession session, string siteId, string scheduleId) => querySchedules(session, ("siteId", siteId), ("id", scheduleId));

    /// <summary>
    /// Triggers a Schedule
    /// </summary>
    /// <param name="session">The session obtained from the top level client</param>
    /// <param name="scheduleId">The schedule ID</param>
    /// <returns></returns>
    public async Task TriggerSchedule(Tableau.Api.Models.TableauSession session, int scheduleId)
    {
        var req = await Request<TriggerSchedule, Empty>(session, "runSchedules",
            new TriggerSchedule()
            {
                Ids = new[] { scheduleId.ToString(CultureInfo.InvariantCulture) }
            });
    }

    private IAsyncEnumerable<Schedule> querySchedules(Tableau.Api.Models.TableauSession session, params (string field, string value)[] search)
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
                    Field = "name",
                    Ascending = true
                }
            }
        };
        return PaginateRequest(session, "getSchedules", q, (ScheduleQueryResponse p) => p.Schedules);
    }
}
