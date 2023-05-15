using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tableau.Api.Exceptions;
using Tableau.Api.Models;
using Tableau.Api.VizPortal.Models;

namespace Tableau.Api.VizPortal
{
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
            TableauSession session, string workbookId) => queryExtractRefreshes(session, "workbookId", workbookId);

        /// <summary>
        /// Returns Extract Refreshes for a single published Datasource. Returns an enumerable that will paginate results in the background
        /// </summary>
        /// <param name="session">The session obtained from the top level client</param>
        /// <param name="datasourceId">The datasource ID</param>
        /// <returns></returns>
        /// <exception cref="TableauRequestException"></exception>
        public IAsyncEnumerable<ExtractRefreshTask> QueryExtractRefreshesForDatasource(
            TableauSession session, string datasourceId) => queryExtractRefreshes(session, "datasourceId", datasourceId);

        private IAsyncEnumerable<ExtractRefreshTask> queryExtractRefreshes(TableauSession session, string field, string value)
        {
            //This query can only be sent like that (1 clause and 'and' operator)
            var q = new QueryModel()
            {
                Filter = new QueryModel.FilterModel()
                {
                    Clauses = new[]
                    {
                        new QueryModel.FilterModel.Clause()
                        {
                            Operator = "eq",
                            Field = field,
                            Value = value
                        }
                    },
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
    }
}
