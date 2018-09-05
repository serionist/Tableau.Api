using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Tableau.Api.Models;

namespace Tableau.Api
{
    public partial class TableauApiClient
    {
        
        public async Task<TableauSession> SignIn(string userName, string password, string siteContentUrl = "",
            string userIdToImpersonate = null, CancellationToken? cancelToken = null)
        {
            checkEndpointAvailability();
            checkNullParameters((nameof(userName), userName), (nameof(password), password));
            var userModel = string.IsNullOrEmpty(userIdToImpersonate)
                ? null
                : new userType() {id = userIdToImpersonate};
            var ret = await getResponseAsObject<tableauCredentialsType>(await apiRequest(buildUri("auth/signin"),
                HttpMethod.Post, 200, cancelToken ?? CancellationToken.None, null,
                new tableauCredentialsType()
                {
                    name = userName,
                    password = password,
                    site = new siteType() {contentUrl = siteContentUrl},
                    user = userModel
                }));
            return new TableauSession(ret);
        }
    }
}
