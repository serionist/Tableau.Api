using System.Collections.Generic;
using System.Threading.Tasks;
using Tableau.Api.Exceptions;
using Tableau.Api.Models;
using Tableau.Api.VizPortal.Models;

namespace Tableau.Api.VizPortal
{
    public partial class VizPortalClient
    {
        /// <summary>
        /// Returns Projects for a site. Returns an enumerable that will paginate results in the background
        /// </summary>
        /// <param name="session">The session obtained from the top level client</param>
        /// <returns></returns>
        /// <exception cref="TableauRequestException"></exception>
        public IAsyncEnumerable<Project> QueryProjects(
            TableauSession session) => queryProjects(session);

        /// <summary>
        /// Change the name of a single Project
        /// </summary>
        /// <param name="session">The session obtained from the top level client</param>
        /// <param name="projectId">The ID of the project</param>
        /// <param name="newName">New name for this project</param>
        /// <returns></returns>
        public async Task ChangeProjectName(TableauSession session, int projectId, string newName)
        {
            var req = await Request<ChangeProjectName, Empty>(session, "setProjectName",
                new ChangeProjectName()
                {
                    Id = projectId.ToString(),
                    Name = newName
                });
        }

        /// <summary>
        /// Move project to the selected destination project
        /// </summary>
        /// <param name="session">The session obtained from the top level client</param>
        /// <param name="containerId">Target Project Id</param>
        /// <param name="contentId">Project Id that will be moved</param>
        /// <returns></returns>
        public async Task MoveProject(TableauSession session, int containerId, int contentId)
        {
            var req = await Request<MoveProject, Empty>(session, "moveContentToContainer",
            new MoveProject()
            {
                ContainerId = containerId.ToString(),
                ContainerType = "PROJECT",
                ContentItems = new[]
                {
                    new MoveProject.ProjectContentItems()
                    {
                        ContentId = contentId.ToString(),
                        ContentType = "project",
                        MoveType = null
                    }
                }
            });
        }

        /// <summary>
        /// Delete a project
        /// </summary>
        /// <param name="session">The session obtained from the top level client</param>
        /// <param name="projectId">The project ID</param>
        /// <returns></returns>
        public async Task DeleteProject(TableauSession session, int projectId)
        {
            var req = await Request<DeleteProject, Empty>(session, "deleteProjects",
                new DeleteProject()
                {
                    Ids = new[] { projectId.ToString() }
                });
        }

        /// <summary>
        /// Change project owner
        /// </summary>
        /// <param name="session">The session obtained from the top level client</param>
        /// <param name="projectId">The project Id</param>
        /// <param name="newOwnerId">The site user id of the new owner</param>
        /// <returns></returns>
        public async Task ChangeProjectOwner(TableauSession session, int projectId, int newOwnerId)
        {
            var req = await Request<ChangeProjectOwner, Empty>(session, "setProjectsOwner",
                new ChangeProjectOwner()
                {
                    Ids = new[] { projectId.ToString() },
                    OwnerId = newOwnerId
                });
        }

        private IAsyncEnumerable<Project> queryProjects(TableauSession session)
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
                }
            };
            return PaginateRequest(session, "getProjects", q, (ProjectQueryResponse p) => p.Projects);
        }
    }
}
