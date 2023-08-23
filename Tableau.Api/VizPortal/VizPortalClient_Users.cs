using System.Threading.Tasks;
using Tableau.Api.Models;
using Tableau.Api.VizPortal.Models;

namespace Tableau.Api.VizPortal
{
    public partial class VizPortalClient
    {
        /// <summary>
        /// Unlicense a user
        /// </summary>
        /// <param name="session">The session obtained from the top level client</param>
        /// <param name="userId">The user ID</param>
        /// <returns></returns>
        public async Task UnlicenseUser(TableauSession session, int userId)
        {
            var req = await Request<ChangeUserRole, Empty>(session, "updateUsersSiteRole",
                new ChangeUserRole()
                {
                    SiteRole = "Unlicensed",
                    UserIds = new[] { userId.ToString() }
                });
        }
    }
}
