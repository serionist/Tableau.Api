using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Tableau.Api.Attributes;
using Tableau.Api.Exceptions;
using Tableau.Api.Models;

namespace Tableau.Api
{
    public partial class TableauApiClient
    {
      

        /// <summary>
        /// Returns the version of Tableau Server and the supported version of the REST API. Versions 2.4+
        /// </summary>
        /// <param name="cancelToken"></param>
        /// <returns></returns>
        [ApiVersion(2, 4)]
        public async Task<serverInfo> ServerInfo(CancellationToken? cancelToken = null)
        {
            checkEndpointAvailability();
            return await getResponseAsObject<serverInfo>(await apiRequest(buildUri($"serverinfo"), HttpMethod.Get, 200,
                cancelToken));
        }
    }
}
