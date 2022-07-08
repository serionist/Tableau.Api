using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Tableau.Api.Attributes;
using Tableau.Api.Enums;
using Tableau.Api.Exceptions;
using Tableau.Api.Models;

namespace Tableau.Api
{
    public partial class TableauApiClient
    {
      
        /// <summary>
        /// Adds one or more tags to the specified data source. Available in all versions 
        /// </summary>
        /// <param name="session"></param>
        /// <param name="datasourceId">The ID of the data source to add tags to.</param>
        /// <param name="tagsToAdd">The data source to add the tag to. If the data source is already tagged with a tag that's included in the request body, those tags are ignored and the data source retains them.</param>
        /// <param name="cancelToken"></param>
        /// <returns></returns>
        /// <exception cref="TableauRequestException"></exception>
        /// <exception cref="TableauApiVersionException"></exception>
        /// <exception cref="TableauOnlineNotSupportedException"></exception>
        [ApiVersion(1, 0)]
        public async Task<tagType[]> AddTagsToDataSource(TableauSession session, string datasourceId,
            string[] tagsToAdd, CancellationToken? cancelToken = null)
        {
            checkEndpointAvailability();
            checkNullParameters((nameof(session), session), (nameof(datasourceId), datasourceId), (nameof(tagsToAdd), tagsToAdd));
            checkEmptyArrays((nameof(tagsToAdd), tagsToAdd));
            return (await getResponseAsObject<tagListType>(await apiRequest(
                buildUri($"sites/{session.SiteId}/datasources/{datasourceId}/tags"), HttpMethod.Put, 200,
                cancelToken, session,
                getObjectAsRequestContent(new tagListType()
                {
                    tag = tagsToAdd.Select(e => new tagType() {label = e}).ToArray()
                })))).tag;
        }
        /// <summary>
        /// Deletes a tag from the specified data source. Available in all versions
        /// </summary>
        /// <param name="session"></param>
        /// <param name="datasourceId">The ID of the data source to remove the tag from.</param>
        /// <param name="tag">The name of the tag to remove from the data source.</param>
        /// <param name="cancelToken"></param>
        /// <returns></returns>
        /// <exception cref="TableauRequestException"></exception>
        /// <exception cref="TableauApiVersionException"></exception>
        /// <exception cref="TableauOnlineNotSupportedException"></exception>
        [ApiVersion(1, 0)]
        public async Task DeleteTagFromDataSource(TableauSession session, string datasourceId, string tag,
            CancellationToken? cancelToken = null)
        {
            checkEndpointAvailability();
            checkNullParameters((nameof(session), session), (nameof(datasourceId), datasourceId), (nameof(tag), tag));
            await apiRequest(buildUri($"sites/{session.SiteId}/datasources/{datasourceId}/tags/{tag}"),
                HttpMethod.Delete, 204, cancelToken, session);
        }

        /// <summary>
        /// Returns information about the specified data source. Available in all versions
        /// </summary>
        /// <param name="session"></param>
        /// <param name="datasourceId">	The ID of the data source to get.</param>
        /// <param name="cancelToken"></param>
        /// <returns></returns>
        /// <exception cref="TableauRequestException"></exception>
        /// <exception cref="TableauApiVersionException"></exception>
        /// <exception cref="TableauOnlineNotSupportedException"></exception>
        [ApiVersion(1, 0)]
        public async Task<dataSourceType> QueryDataSource(TableauSession session, string datasourceId,
            CancellationToken? cancelToken = null)
        {
            checkEndpointAvailability();
            checkNullParameters((nameof(session), session), (nameof(datasourceId), datasourceId));
            return await getResponseAsObject<dataSourceType>(await apiRequest(
                buildUri($"sites/{session.SiteId}/datasources/{datasourceId}"), HttpMethod.Get, 200, cancelToken,
                session));


        }

        /// <summary>
        /// Returns a list of data sources on the specified site, with optional parameters for specifying the paging of large results. Available in all versions
        /// </summary>
        /// <param name="session"></param>
        /// <param name="pageSize">(Optional) The number of items to return in one response. The minimum is 1. The maximum is 1000.</param>
        /// <param name="pageNumber">(Optional) The offset for paging. The default is 1. </param>
        /// <param name="fieldsExpression">(Optional) An expression that lets you specify a subset of data sources to return. You can filter on predefined fields such as name and updatedAt. You can include multiple filter expressions.</param>
        /// <param name="filterExpression">(Optional) An expression that lets you specify the order in which user information is returned. If you do not specify a sort expression, the sort order of the information that's returned is undefined. </param>
        /// <param name="sortExpression">(Optional) An expression that lets you specify the set of available fields to return. You can qualify the return values based upon predefined keywords such as _all_ or _default_, and you can specify individual fields for the workbooks or other supported resources. You can include multiple field expressions in a request.</param>
        /// <param name="cancelToken"></param>
        /// <returns></returns>
        /// <exception cref="TableauRequestException"></exception>
        /// <exception cref="TableauApiVersionException"></exception>
        /// <exception cref="TableauOnlineNotSupportedException"></exception>
        [ApiVersion(1, 0)]
        public async Task<(paginationType pagination, dataSourceType[] datasources)> QueryDataSources(TableauSession session, 
            int pageSize = 100, int pageNumber = 1, string fieldsExpression = null, string filterExpression = null,
            string sortExpression = null,
            CancellationToken? cancelToken = null)
        {
            checkEndpointAvailability();
            checkNullParameters((nameof(session), session));
            checkParametersBetween((nameof(pageSize), pageSize, 1, 1000), (nameof(pageNumber), pageNumber, 1, int.MaxValue));
            var args = new List<(string, string)>()
            {
                ("pageSize", pageSize.ToString()),
                ("pageNumber", pageNumber.ToString())
            };
            if (!string.IsNullOrEmpty(fieldsExpression))
                args.Add(("fields", fieldsExpression));
            if (!string.IsNullOrEmpty(filterExpression))
                args.Add(("filter", filterExpression));
            if (!string.IsNullOrEmpty(sortExpression))
                args.Add(("sort", sortExpression));
            var ret = await getResponseAsObject<paginationType, dataSourceListType>(
                await apiRequest(buildUri($"sites/{session.SiteId}/datasources", args.ToArray()), HttpMethod.Get, 200,
                    cancelToken, session));
            return (ret.Item1, ret.Item2.datasource);
        }
        /// <summary>
        /// Returns a list of data connections for the specified data source. Versions 2.3+
        /// </summary>
        /// <param name="session"></param>
        /// <param name="datasourceId">The ID of the data source to return connection information about.</param>
        /// <param name="cancelToken"></param>
        /// <returns></returns>
        /// <exception cref="TableauRequestException"></exception>
        /// <exception cref="TableauApiVersionException"></exception>
        /// <exception cref="TableauOnlineNotSupportedException"></exception>
        [ApiVersion(2, 3)]
        public async Task<connectionType[]> QueryDataSourceConnections(TableauSession session, string datasourceId,
            CancellationToken? cancelToken = null)
        {
            checkEndpointAvailability();
            checkNullParameters((nameof(session), session), (nameof(datasourceId), datasourceId));
            return (await getResponseAsObject<connectionListType>(await apiRequest(
                buildUri($"sites/{session.SiteId}/datasources/{datasourceId}/connections"), HttpMethod.Get, 200,
                cancelToken, session))).connection;

        }
        /// <summary>
        /// Downloads a data source in .tdsx format. Returns the network stream that can be read to retrieve the data. Available in all versions
        /// </summary>
        /// <param name="session"></param>
        /// <param name="datasourceId">The ID of the data source to download.</param>
        /// <param name="includeExtract">(Optional) The extract-value is a Boolean value (False or True). When the data source specified for download has an extract, if you add the parameter ?includeExtract=False, the extract is not included when you download the data source. You can use this parameter to improve performance if you are downloading workbooks or data sources that have large extracts.</param>
        /// <param name="cancelToken"></param>
        /// <returns></returns>
        /// <exception cref="TableauRequestException"></exception>
        /// <exception cref="TableauApiVersionException"></exception>
        /// <exception cref="TableauOnlineNotSupportedException"></exception>
        [ApiVersion(2, 0)]
        public async Task<(Stream stream, DatasourceFileType type)> DownloadDataSource(TableauSession session, string datasourceId,
            bool includeExtract = true, CancellationToken? cancelToken = null)
        {
            checkEndpointAvailability();
            checkNullParameters((nameof(session), session), (nameof(datasourceId), datasourceId));
            var resp = await apiRequest(
                buildUri($"sites/{session.SiteId}/datasources/{datasourceId}/content",
                    ("includeExtract", includeExtract.ToString())), HttpMethod.Get, 200, cancelToken, session);

            return (await resp.ReadAsStreamAsync(),
                resp.Headers.ContentType.MediaType.Equals("application/xml", StringComparison.CurrentCultureIgnoreCase)
                    ? DatasourceFileType.tds
                    : DatasourceFileType.tdsx);
        }
        /// <summary>
        /// Updates the owner, project or certification status of the specified data source. Available in all versions
        /// </summary>
        /// <param name="session"></param>
        /// <param name="datasourceId">The ID of the data source to update.</param>
        /// <param name="newName">(Optional) The new name to give to the datasource.</param>
        /// <param name="newProjectId">(Optional) The ID of a project to add the data source to.</param>
        /// <param name="newOwnerId">(Optional) The ID of a user to assign the data source to as owner.</param>
        /// <param name="newCertificationStatus">(Optional) A Boolean value that indicates whether the data source is certified.</param>
        /// <param name="newCertificationNote">	(Optional) A note that provides more information on the certification of the data source, if applicable.</param>
        /// <param name="cancelToken"></param>
        /// <returns></returns>
        /// <exception cref="TableauRequestException"></exception>
        /// <exception cref="TableauApiVersionException"></exception>
        /// <exception cref="TableauOnlineNotSupportedException"></exception>
        [ApiVersion(2, 0)]
        public async Task<dataSourceType> UpdateDataSource(TableauSession session, string datasourceId, 
            string newProjectId = null, string newOwnerId = null, bool? newCertificationStatus = null,
            string newCertificationNote = null, string newName = null, CancellationToken? cancelToken = null)
        {

            checkEndpointAvailability();
            checkNullParameters((nameof(session), session), (nameof(datasourceId), datasourceId));
            return await getResponseAsObject<dataSourceType>(await apiRequest(
                buildUri($"sites/{session.SiteId}/datasources/{datasourceId}"), HttpMethod.Put, 200, cancelToken,
                session, getObjectAsRequestContent(new dataSourceType()
                {
                    isCertified = newCertificationStatus ?? false,
                    isCertifiedSpecified = newCertificationStatus.HasValue,
                    certificationNote = newCertificationNote,
                    project = new projectType() {id = newProjectId},
                    owner = new userType() {id = newOwnerId},
                    name = newName
                })));
        }
        /// <summary>
        /// Updates the server address, port, username, or password for the specified data source connection. Versions 2.3+
        /// </summary>
        /// <param name="session"></param>
        /// <param name="datasourceId">The ID of the data source to update.</param>
        /// <param name="connectionId">	The ID of the connection to update. </param>
        /// <param name="serverAddress">The new server for the connection.</param>
        /// <param name="serverPort">The new port for the connection</param>
        /// <param name="userName">The new username for the connection.</param>
        /// <param name="password">The new password for the connection.</param>
        /// <param name="cancelToken"></param>
        /// <returns></returns>
        /// <exception cref="TableauRequestException"></exception>
        /// <exception cref="TableauApiVersionException"></exception>
        /// <exception cref="TableauOnlineNotSupportedException"></exception>
        [ApiVersion(2, 3)]
        public async Task<connectionType> UpdateDataSourceConnection(TableauSession session, string datasourceId,
            string connectionId, string serverAddress, string serverPort, string userName, string password, CancellationToken? cancelToken = null)
        {
            checkEndpointAvailability();
            checkNullParameters((nameof(session), session), (nameof(datasourceId), datasourceId), (nameof(connectionId), connectionId), (nameof(serverAddress), serverAddress), (nameof(serverPort), serverPort), (nameof(userName), userName), (nameof(password), password));
            return await getResponseAsObject<connectionType>(await apiRequest(
                buildUri($"sites/{session.SiteId}/datasources/{datasourceId}/connections/{connectionId}"),
                HttpMethod.Put, 200, cancelToken, session,
                getObjectAsRequestContent(new connectionType()
                {
                    serverAddress = serverAddress,
                    serverPort = serverPort,
                    userName = userName,
                    password = password
                })));
        }
        /// <summary>
        /// Runs an extract refresh on the specified data source. Version 2.8+
        /// 
        /// This method runs an extract refresh for the specified data source, with no need to associate that extract refresh with a scheduled task. This method is the equivalent of selecting a data source using the Tableau Server UI, and then selecting Refresh Extracts from the menu (also known as a "manual refresh"). Similar to a manual refresh, if the extract is a .tde file, it will be converted to a .hyper file as a result of the refresh.
        /// </summary>
        /// <param name="session"></param>
        /// <param name="datasourceId">The ID of the data source to refresh.</param>
        /// <param name="cancelToken"></param>
        /// <returns></returns>
        /// <exception cref="TableauRequestException"></exception>
        /// <exception cref="TableauApiVersionException"></exception>
        /// <exception cref="TableauOnlineNotSupportedException"></exception>
        [ApiVersion(2, 8)]
        public async Task<jobType> UpdateDataSourceNow(TableauSession session, string datasourceId,
            CancellationToken? cancelToken = null)
        {
            checkEndpointAvailability();
            checkNullParameters((nameof(session), session), (nameof(datasourceId), datasourceId));
            return await getResponseAsObject<jobType>(await apiRequest(
                buildUri($"sites/{session.SiteId}/datasources/{datasourceId}/refresh"), HttpMethod.Post, 202,
                cancelToken, session, getObjectAsRequestContent(null)));

        }

        /// <summary>
        /// Deletes the specified data source from a site. When a data source is deleted, its associated data connection is also deleted. Workbooks that use the data source are not deleted, but they will no longer work properly. Available in all versions
        /// </summary>
        /// <param name="session"></param>
        /// <param name="datasourceId">	The ID of the data source to get.</param>
        /// <param name="cancelToken"></param>
        /// <returns></returns>
        /// <exception cref="TableauRequestException"></exception>
        /// <exception cref="TableauApiVersionException"></exception>
        /// <exception cref="TableauOnlineNotSupportedException"></exception>
        [ApiVersion(2, 0)]
        public async Task DeleteDataSource(TableauSession session, string datasourceId,
            CancellationToken? cancelToken = null)
        {
            checkEndpointAvailability();
            checkNullParameters((nameof(session), session), (nameof(datasourceId), datasourceId));
            await apiRequest(buildUri($"sites/{session.SiteId}/datasources/{datasourceId}"), HttpMethod.Delete, 204,
                cancelToken, session);


        }
    }


}
