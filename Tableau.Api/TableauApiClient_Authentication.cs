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
        /// Signs into Tableau Server. Available in all versions
        /// </summary>
        /// <param name="userName">The name of the user</param>
        /// <param name="password">The password of the user</param>
        /// <param name="siteContentUrl">The ContentUrl of the site to log in to. Default: ''</param>
        /// <param name="userIdToImpersonate">The id of the user to impersonate (Optional)</param>
        /// <param name="cancelToken"></param>
        /// <returns></returns>
        /// <exception cref="TableauRequestException"></exception>
        [ApiVersion(1,0)]
        public async Task<TableauSession> SignIn(string userName, string password, string siteContentUrl = "",
            string userIdToImpersonate = null, CancellationToken? cancelToken = null)
        {
            checkEndpointAvailability();
            checkNullParameters((nameof(userName), userName), (nameof(password), password));
            var userModel = string.IsNullOrEmpty(userIdToImpersonate)
                ? null
                : new userType() {id = userIdToImpersonate};
            var ret = await getResponseAsObject<tableauCredentialsType>(await apiRequest(buildUri("auth/signin"),
                HttpMethod.Post, 200, cancelToken, null, getObjectAsRequestContent(
                    new tableauCredentialsType()
                    {
                        name = userName,
                        password = password,
                        site = new siteType() {contentUrl = siteContentUrl},
                        user = userModel
                    })));
            ret.user.name = userName;
            return new TableauSession(ret);
        }

        /// <summary>
        /// Signs into Tableau Server with a personal access token. Versions 3.7+
        /// </summary>
        /// <param name="tokenName">The name of the access token</param>
        /// <param name="token">The token itself</param>
        /// <param name="siteContentUrl">The ContentUrl of the site to log in to. Default: ''</param>
        /// <param name="cancelToken"></param>
        /// <returns></returns>
        /// <exception cref="TableauRequestException"></exception>
        [ApiVersion(3, 7)]
        public async Task<TableauSession> SignInWithPAT(string tokenName, string token, string siteContentUrl = "", CancellationToken? cancelToken = null)
        {
            checkEndpointAvailability();
            checkNullParameters((nameof(tokenName), tokenName), (nameof(token), token));
            var ret = await getResponseAsObject<tableauCredentialsType>(await apiRequest(buildUri("auth/signin"),
                HttpMethod.Post, 200, cancelToken, null, getObjectAsRequestContent(
                    new tableauCredentialsType()
                    {
                        personalAccessTokenName  = tokenName,
                        personalAccessTokenSecret = token,
                        site = new siteType() { contentUrl = siteContentUrl },
                    })));
            ret.user.name = tokenName;
            return new TableauSession(ret);
        }

        /// <summary>
        /// Signs out of Tableau Server. Available in all versions
        /// </summary>
        /// <param name="session"></param>
        /// <param name="cancelToken"></param>
        /// <returns></returns>
        /// /// <exception cref="TableauRequestException"></exception>
        [ApiVersion(1, 0)]
        public async Task SignOut(TableauSession session, CancellationToken? cancelToken = null)
        {
            checkEndpointAvailability();
            checkNullParameters((nameof(session), session));
            await apiRequest(buildUri($"auth/signout"), HttpMethod.Post, 204, cancelToken,
                session);
        }

        /// <summary>
        /// Changes sites using specified Session. Not available in Tableau Online. Versions 2.6+
        /// </summary>
        /// <param name="session"></param>
        /// <param name="newSiteContentUrl"></param>
        /// <param name="cancelToken"></param>
        /// <returns></returns>
        /// <exception cref="TableauRequestException"></exception>
        /// <exception cref="TableauApiVersionException"></exception>
        /// <exception cref="TableauOnlineNotSupportedException"></exception>
        [ApiVersion(2, 6)]
        [OnPremiseOnly]
        public async Task<TableauSession> SwitchSite(TableauSession session, string newSiteContentUrl, CancellationToken? cancelToken = null)
        {
            checkEndpointAvailability();
            checkNullParameters((nameof(session), session));
            var ret = await getResponseAsObject<tableauCredentialsType>(await apiRequest(buildUri("auth/switchsite"),
                HttpMethod.Post, 200, cancelToken, session,
                getObjectAsRequestContent(new siteType() {contentUrl = newSiteContentUrl})));
            ret.user.name = session.UserName;
            return new TableauSession(ret);

        }
    }
}
