using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Tableau.Api.Exceptions;
using Tableau.Api.Models;
using Tableau.Api.VizPortal.Models;

namespace Tableau.Api.VizPortal;

public partial class VizPortalClient
{

    /// <summary>
    /// Returns Extract Refreshes for a single Workbook. Returns an enumerable that will paginate results in the background
    /// </summary>
    /// <param name="session">The session obtained from the top level client</param>
    /// <param name="workbookId">The workbook ID</param>
    /// <returns></returns>
    /// <exception cref="TableauRequestException"></exception>
    public IAsyncEnumerable<ExtractRefreshTask> QueryExtractRefreshesForWorkbook(
        Tableau.Api.Models.TableauSession session, string workbookId) => queryExtractRefreshes(session, ("workbookId", workbookId));

    /// <summary>
    /// Returns Extract Refreshes for a single published Datasource. Returns an enumerable that will paginate results in the background
    /// </summary>
    /// <param name="session">The session obtained from the top level client</param>
    /// <param name="datasourceId">The datasource ID</param>
    /// <returns></returns>
    /// <exception cref="TableauRequestException"></exception>
    public async IAsyncEnumerable<ExtractRefreshTask> QueryExtractRefreshesForDatasource(
        Tableau.Api.Models.TableauSession session, string datasourceId)
    {
        // get datasource info
        var dsInfo = await Request(session, "getDatasource", new JObject(new JProperty("id", datasourceId)));
        if (!dsInfo.ContainsKey("hasExtracts") || !dsInfo.Value<bool>("hasExtracts"))
            yield break;

        await foreach (var e in queryExtractRefreshes(session, ("datasourceId", datasourceId)))
            yield return e;

    }

    /// <summary>
    /// Returns Extract Refreshes for a single Schedule. Returns an enumerable that will paginate results in the background
    /// </summary>
    /// <param name="session">The session obtained from the top level client</param>
    /// <param name="siteId">The site ID</param>
    /// <param name="scheduleId">The schedule ID</param>
    /// <returns></returns>
    public async IAsyncEnumerable<ExtractRefreshTask> QueryExtractRefreshesForSchedule(
      Tableau.Api.Models.TableauSession session, string siteId, string scheduleId)
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


        await foreach (var e in queryExtractRefreshes(session, ("scheduleId", scheduleId), ("siteId", siteId)))
            yield return e;

    }

    private async IAsyncEnumerable<ExtractRefreshTask> empty()
    {
        await Task.CompletedTask;
        yield break;

    }

    private IAsyncEnumerable<ExtractRefreshTask> queryExtractRefreshes(Tableau.Api.Models.TableauSession session, params (string field, string value)[] search)
    {
        //This query can only be sent like that (1 clause and 'and' operator)
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
                    Field = "scheduleName",
                    Ascending = true
                }
            }
        };
        return PaginateRequest(session, "getExtractTasks", q, (ExtractRefreshQueryResponse p) => p.Tasks);
    }


    /// <summary>
    /// Change an extract refresh job schedule frequency
    /// </summary>
    /// <param name="session">The session obtained from the top level client</param>
    /// <param name="extractRefreshId">The extract refresh ID</param>
    /// <param name="scheduleId">The schedule ID</param>
    /// <returns></returns>
    /// <exception cref="TableauRequestException"></exception>
    public async Task ChangeExtractRefreshScheduleFrequency(Tableau.Api.Models.TableauSession session, int extractRefreshId, int scheduleId)
    {
        var req = await Request<ChangeExtractRefreshTaskSchedule, Empty>(session, "setExtractTasksSchedule",
            new ChangeExtractRefreshTaskSchedule()
            {
                ScheduleId = scheduleId.ToString(CultureInfo.InvariantCulture),
                Ids = new[] { extractRefreshId.ToString(CultureInfo.InvariantCulture) }
            });

    }

    /// <summary>
    /// Triggers an Extract Refresh Job
    /// </summary>
    /// <param name="session">The session obtained from the top level client</param>
    /// <param name="extractRefreshId">The extract refresh ID</param>
    /// <returns></returns>
    /// <exception cref="TableauRequestException"></exception>
    public async Task TriggerExtractRefresh(Tableau.Api.Models.TableauSession session, int extractRefreshId)
    {
        var req = await Request<TriggerExtractRefreshTask, Empty>(session, "runExtractTasks",
            new TriggerExtractRefreshTask()
            {
                Ids = new[] { extractRefreshId.ToString(CultureInfo.InvariantCulture) }
            });
    }

    /// <summary>
    /// Deletes an Extract Refresh Job
    /// </summary>
    /// <param name="session">The session obtained from the top level client</param>
    /// <param name="extractRefreshId">The extract refresh ID</param>
    /// <returns></returns>
    public async Task DeleteExtractRefresh(Tableau.Api.Models.TableauSession session, int extractRefreshId)
    {
        var req = await Request<DeleteExtractRefreshTask, Empty>(session, "deleteExtractTasks",
            new DeleteExtractRefreshTask()
            {
                Ids = new[] { extractRefreshId.ToString(CultureInfo.InvariantCulture) }
            });
    }
}
