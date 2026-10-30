using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Tableau.Api.Models;
using Tableau.Api.VizPortal.Models;

namespace Tableau.Api.VizPortal
{
    public partial class VizPortalClient
    {
        /// <summary>
        /// Returns datasources for a site. Returns an enumerable that will paginate results in the background
        /// </summary>
        /// <param name="session">The session obtained from the top level client</param>
        /// <returns></returns>
        public IAsyncEnumerable<(Datasource[], ContentUser[], ContentProjectDs[])> QueryDatasources(
            TableauSession session) => queryDatasources(session, ("isPublished", true));

        /// <summary>
        /// Move a datasource to the selected destination project
        /// </summary>
        /// <param name="session">The session obtained from the top level client</param>
        /// <param name="containerId">Target Project Id</param>
        /// <param name="contentId">Datasource content ID (Id) that will be moved</param>
        /// <returns></returns>
        public async Task MoveDatasource(TableauSession session, int containerId, int contentId)
        {
            var req = await Request<MoveContent, Empty>(session, "moveContentToContainer",
            new MoveContent()
            {
                ContainerId = containerId.ToString(),
                ContainerType = "PROJECT",
                ContentItems = new[]
                {
                    new MoveContent.Content()
                    {
                        ContentId = contentId.ToString(),
                        ContentType = "datasource",
                        MoveType = null
                    }
                }
            });
        }

        /// <summary>
        /// Change the name of a single datasource
        /// </summary>
        /// <param name="session">The session obtained from the top level client</param>
        /// <param name="datasourceId">The content ID (Id) of the datasource</param>
        /// <param name="newName">New name for this datasource</param>
        /// <returns></returns>
        public async Task ChangeDatasourceName(TableauSession session, int datasourceId, string newName)
        {
            var req = await Request<RenameContent, Empty>(session, "renameDatasource",
                new RenameContent()
                {
                    Id = datasourceId.ToString(),
                    Name = newName
                });
        }

        /// <summary>
        /// Change datasource owner
        /// </summary>
        /// <param name="session">The session obtained from the top level client</param>
        /// <param name="datasourceId">The datasource content ID (Id)</param>
        /// <param name="newOwnerId">The site user id of the new owner</param>
        /// <returns></returns>
        public async Task ChangeDatasourceOwner(TableauSession session, int datasourceId, int newOwnerId)
        {
            var req = await Request<ChangeContentOwner, Empty>(session, "setDatasourcesOwner",
                new ChangeContentOwner()
                {
                    Ids = new[] { datasourceId.ToString() },
                    OwnerId = newOwnerId
                });
        }

        /// <summary>
        /// Delete a datasource
        /// </summary>
        /// <param name="session">The session obtained from the top level client</param>
        /// <param name="datasourceId">The datasource content ID (Id)</param>
        /// <returns></returns>
        public async Task DeleteDatasource(TableauSession session, int datasourceId)
        {
            var req = await Request<DeleteContent, Empty>(session, "deleteDatasources",
                new DeleteContent()
                {
                    Ids = new[] { datasourceId.ToString() }
                });
        }

        private IAsyncEnumerable<(Datasource[], ContentUser[], ContentProjectDs[])> queryDatasources(TableauSession session, params (string field, bool value)[] search)
        {
            var q = new QueryModel()
            {
                Filter = new QueryModel.FilterModel()
                {
                    Clauses = search.Select(s => new QueryModel.FilterModel.ClauseWithBool()
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
            return PaginateRequest(session, "getDatasources", q, (DatasourcesQueryResponse d) => new[] { (d.Datasources, d.Users, d.Projects) });
        }
    }
}
