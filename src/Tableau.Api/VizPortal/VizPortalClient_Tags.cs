using System.Threading.Tasks;
using Tableau.Api.Models;
using Tableau.Api.VizPortal.Models;

namespace Tableau.Api.VizPortal
{
    public partial class VizPortalClient
    {
        /// <summary>
        /// Adds a tag to a workbook
        /// </summary>
        /// <param name="session">The session obtained from the top level client</param>
        /// <param name="workbookId">The workbook Id</param>
        /// <param name="tag">The tag</param>
        /// <returns></returns>
        public async Task AddTagToWorkbook(TableauSession session, int workbookId, string tag)
        {
            var req = await Request<TagContentAction, Empty>(session, "addTagsToWorkbooks",
                new TagContentAction()
                {
                    Ids = new[] { workbookId.ToString() },
                    Tags = new[] { $"\"{tag}\"" }
                });
        }

        /// <summary>
        /// Removes a tag from a workbook
        /// </summary>
        /// <param name="session">The session obtained from the top level client</param>
        /// <param name="workbookId">The workbook Id</param>
        /// <param name="tag">The tag</param>
        /// <returns></returns>
        public async Task RemoveTagFromWorkbook(TableauSession session, int workbookId, string tag)
        {
            var req = await Request<TagContentAction, Empty>(session, "removeTagsFromWorkbooks",
                new TagContentAction()
                {
                    Ids = new[] { workbookId.ToString() },
                    Tags = new[] { $"\"{tag}\"" }
                });
        }

        /// <summary>
        /// Adds a tag to a datasource
        /// </summary>
        /// <param name="session">The session obtained from the top level client</param>
        /// <param name="datasourceId">The datasource Id</param>
        /// <param name="tag">The tag</param>
        /// <returns></returns>
        public async Task AddTagToDatasource(TableauSession session, int datasourceId, string tag)
        {
            var req = await Request<TagContentAction, Empty>(session, "addTagsToDatasources",
                new TagContentAction()
                {
                    Ids = new[] { datasourceId.ToString() },
                    Tags = new[] { $"\"{tag}\"" }
                });
        }

        /// <summary>
        /// Removes a tag from a datasource
        /// </summary>
        /// <param name="session">The session obtained from the top level client</param>
        /// <param name="datasourceId">The datasource Id</param>
        /// <param name="tag">The tag</param>
        /// <returns></returns>
        public async Task RemoveTagFromDatasource(TableauSession session, int datasourceId, string tag)
        {
            var req = await Request<TagContentAction, Empty>(session, "removeTagsFromDatasources",
                new TagContentAction()
                {
                    Ids = new[] { datasourceId.ToString() },
                    Tags = new[] { $"\"{tag}\"" }
                });
        }
    }
}
