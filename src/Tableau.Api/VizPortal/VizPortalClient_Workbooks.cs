using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Tableau.Api.Exceptions;
using Tableau.Api.Models;
using Tableau.Api.VizPortal.Models;

namespace Tableau.Api.VizPortal;

public partial class VizPortalClient
{
    /// <summary>
    /// Returns workbooks for a site. Returns an enumerable that will paginate results in the background
    /// </summary>
    /// <param name="session">The session obtained from the top level client</param>
    /// <returns></returns>
    /// <exception cref="TableauRequestException"></exception>
    public IAsyncEnumerable<(Workbook[], ContentUser[], ContentProjectWb[])> QueryWorkbooks(
        TableauSession session) => queryWorkbooks(session);

    /// <summary>
    /// Move a workbook to the selected destination project
    /// </summary>
    /// <param name="session">The session obtained from the top level client</param>
    /// <param name="containerId">Target Project Id</param>
    /// <param name="contentId">Workbook Id that will be moved</param>
    /// <returns></returns>
    public async Task MoveWorkbook(TableauSession session, int containerId, int contentId)
    {
        var req = await Request<MoveContent, Empty>(session, "moveContentToContainer",
        new MoveContent()
        {
            ContainerId = containerId.ToString(CultureInfo.InvariantCulture),
            ContainerType = "PROJECT",
            ContentItems = new[]
            {
                new MoveContent.Content()
                {
                    ContentId = contentId.ToString(CultureInfo.InvariantCulture),
                    ContentType = "workbook",
                    MoveType = null
                }
            }
        });
    }

    /// <summary>
    /// Change the name of a single workbook
    /// </summary>
    /// <param name="session">The session obtained from the top level client</param>
    /// <param name="workbookId">The Id of the workbook</param>
    /// <param name="newName">New name for this workbook</param>
    /// <returns></returns>
    public async Task ChangeWorkbookName(TableauSession session, int workbookId, string newName)
    {
        var req = await Request<RenameContent, Empty>(session, "renameWorkbook",
            new RenameContent()
            {
                Id = workbookId.ToString(CultureInfo.InvariantCulture),
                Name = newName
            });
    }

    /// <summary>
    /// Change workbook owner
    /// </summary>
    /// <param name="session">The session obtained from the top level client</param>
    /// <param name="workbookId">The workbook Id</param>
    /// <param name="newOwnerId">The site user id of the new owner</param>
    /// <returns></returns>
    public async Task ChangeWorkbookOwner(TableauSession session, int workbookId, int newOwnerId)
    {
        var req = await Request<ChangeContentOwner, Empty>(session, "setWorkbooksOwner",
            new ChangeContentOwner()
            {
                Ids = new[] { workbookId.ToString(CultureInfo.InvariantCulture) },
                OwnerId = newOwnerId
            });
    }

    /// <summary>
    /// Delete a workbook
    /// </summary>
    /// <param name="session">The session obtained from the top level client</param>
    /// <param name="workbookId">The workbook Id</param>
    /// <returns></returns>
    public async Task DeleteWorkbook(TableauSession session, int workbookId)
    {
        var req = await Request<DeleteContent, Empty>(session, "deleteWorkbooks",
            new DeleteContent()
            {
                Ids = new[] { workbookId.ToString(CultureInfo.InvariantCulture) }
            });
    }

    private IAsyncEnumerable<(Workbook[], ContentUser[], ContentProjectWb[])> queryWorkbooks(TableauSession session)
    {
        var q = new QueryModel()
        {
            Order = new[]
            {
                new QueryModel.OrderModel()
                {
                    Field = "name",
                    Ascending = true
                }
            },
        };
        return PaginateRequest(session, "getWorkbooks", q, (WorkbooksQueryResponse d) => new[] { (d.Workbooks, d.Users, d.Projects) });
    }
}
