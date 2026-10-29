using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Sockets;
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
        /// Returns a list of revision information (history) for the specified data source. Versions 2.3+
        /// </summary>
        /// <param name="session"></param>
        /// <param name="datasourceId">The ID of the data source to get revisions for.</param>
        /// <param name="pageSize">(Optional) The number of items to return in one response. The minimum is 1. The maximum is 1000. The default is 100. </param>
        /// <param name="pageNumber">(Optional) The offset for paging. The default is 1. </param>
        /// <param name="cancelToken"></param>
        /// <returns></returns>
        /// <exception cref="TableauRequestException"></exception>
        /// <exception cref="TableauApiVersionException"></exception>
        /// <exception cref="TableauOnlineNotSupportedException"></exception>
        [ApiVersion(2, 3)]
        public async Task<(paginationType pagination, revisionType[] revisions)> GetDataSourceRevisions(
            TableauSession session, string datasourceId, int pageSize = 100, int pageNumber = 1,
            CancellationToken? cancelToken = null)
        {
            checkEndpointAvailability();
            checkNullParameters((nameof(session), session), (nameof(datasourceId), datasourceId));
            checkParametersBetween((nameof(pageSize), pageSize, 1, 1000), (nameof(pageNumber), pageNumber, 1, int.MaxValue));
            var r = await getResponseAsObject<paginationType, revisionListType>(
                await apiRequest(buildUri($"sites/{session.SiteId}/datasources/{datasourceId}/revisions",
                        ("pageSize", pageSize.ToString()), ("pageNumber", pageNumber.ToString())),
                    HttpMethod.Get, 200, cancelToken, session));
            return (r.Item1, r.Item2.revision);
        }

        /// <summary>
        /// Returns a list of revision information (history) for the specified workbook. Versions 2.3+
        /// </summary>
        /// <param name="session"></param>
        /// <param name="workbookId">The ID of the workbook to get revisions for.</param>
        /// <param name="pageSize">(Optional) The number of items to return in one response. The minimum is 1. The maximum is 1000. The default is 100. </param>
        /// <param name="pageNumber">(Optional) The offset for paging. The default is 1. </param>
        /// <param name="cancelToken"></param>
        /// <returns></returns>
        /// <exception cref="TableauRequestException"></exception>
        /// <exception cref="TableauApiVersionException"></exception>
        /// <exception cref="TableauOnlineNotSupportedException"></exception>
        [ApiVersion(2, 3)]
        public async Task<(paginationType pagination, revisionType[] revisions)> GetWorkbookRevisions(
            TableauSession session, string workbookId, int pageSize = 100, int pageNumber = 1,
            CancellationToken? cancelToken = null)
        {
            checkEndpointAvailability();
            checkNullParameters((nameof(session), session), (nameof(workbookId), workbookId));
            checkParametersBetween((nameof(pageSize), pageSize, 1, 1000), (nameof(pageNumber), pageNumber, 1, int.MaxValue));
            var r = await getResponseAsObject<paginationType, revisionListType>(
                await apiRequest(buildUri($"sites/{session.SiteId}/workbooks/{workbookId}/revisions",
                        ("pageSize", pageSize.ToString()), ("pageNumber", pageNumber.ToString())),
                    HttpMethod.Get, 200, cancelToken, session));
            return (r.Item1, r.Item2.revision);
        }
        /// <summary>
        /// Downloads a specific version of a data source in .tdsx format. Returns data stream. Versions 2.3+
        /// </summary>
        /// <param name="session"></param>
        /// <param name="datasourceId">The ID of the data source to download.</param>
        /// <param name="revisionNumber">The revision number of the data source to download.</param>
        /// <param name="includeExtract">(Optional) The extract-value is a Boolean value (False or True). When the data source specified for download has an extract, if you add the parameter ?includeExtract=False, the extract is not included when you download the data source. You can use this parameter to improve performance if you are downloading workbooks or data sources that have large extracts.</param>
        /// <param name="cancelToken"></param>
        /// <returns></returns>
        /// <exception cref="TableauRequestException"></exception>
        /// <exception cref="TableauApiVersionException"></exception>
        /// <exception cref="TableauOnlineNotSupportedException"></exception>
        [ApiVersion(2, 3)]
        public async Task<Stream> DownloadDataSourceRevision(TableauSession session, string datasourceId,
            int revisionNumber, bool includeExtract = true, CancellationToken? cancelToken = null)
        {
            checkEndpointAvailability();
            checkNullParameters((nameof(session), session), (nameof(datasourceId), datasourceId));
            return await (await apiRequest(
                buildUri($"sites/{session.SiteId}/datasources/{datasourceId}/revisions/{revisionNumber}/content",
                    ("includeExtract", includeExtract.ToString())), HttpMethod.Get, 200,
                cancelToken, session)).ReadAsStreamAsync();
        }

        /// <summary>
        /// Downloads a specific version of a workbook in .twb or .twbx format. Returns data stream. Versions 2.3+
        /// </summary>
        /// <param name="session"></param>
        /// <param name="workbookId">The ID of the workbook to download.</param>
        /// <param name="revisionNumber">The revision number of the workbook to download.</param>
        /// <param name="includeExtract">(Optional) The extract-value is a Boolean value (False or True). When the data source specified for download has an extract, if you add the parameter ?includeExtract=False, the extract is not included when you download the data source. You can use this parameter to improve performance if you are downloading workbooks or data sources that have large extracts.</param>
        /// <param name="cancelToken"></param>
        /// <returns></returns>
        /// <exception cref="TableauRequestException"></exception>
        /// <exception cref="TableauApiVersionException"></exception>
        /// <exception cref="TableauOnlineNotSupportedException"></exception>
        [ApiVersion(2, 3)]
        public async Task<Stream> DownloadWorkbookRevision(TableauSession session, string workbookId,
            int revisionNumber, bool includeExtract = true, CancellationToken? cancelToken = null)
        {
            checkEndpointAvailability();
            checkNullParameters((nameof(session), session), (nameof(workbookId), workbookId));
            return await (await apiRequest(
                buildUri($"sites/{session.SiteId}/workbooks/{workbookId}/revisions/{revisionNumber}/content",
                    ("includeExtract", includeExtract.ToString())), HttpMethod.Get, 200,
                cancelToken, session)).ReadAsStreamAsync();
        }
        /// <summary>
        /// Removes a specific version of a data source. Versions 2.3+
        /// </summary>
        /// <param name="session"></param>
        /// <param name="datasourceId">The ID of the data source to remove the revision for.</param>
        /// <param name="revisionNumber">The revision number of the data source to remove.</param>
        /// <param name="cancelToken"></param>
        /// <returns></returns>
        /// <exception cref="TableauRequestException"></exception>
        /// <exception cref="TableauApiVersionException"></exception>
        /// <exception cref="TableauOnlineNotSupportedException"></exception>
        [ApiVersion(2, 3)]
        public async Task RemoveDataSourceRevision(TableauSession session, string datasourceId, int revisionNumber,
            CancellationToken? cancelToken = null)
        {
            checkEndpointAvailability();
            checkNullParameters((nameof(session), session), (nameof(datasourceId), datasourceId));
            await apiRequest(buildUri($"sites/{session.SiteId}/datasources/{datasourceId}/revisions/{revisionNumber}"),
                HttpMethod.Delete, 204, cancelToken, session);
        }

        /// <summary>
        /// Removes a specific version of a workbook. Versions 2.3+
        /// </summary>
        /// <param name="session"></param>
        /// <param name="workbookId">The ID of the workbook to remove the revision for.</param>
        /// <param name="revisionNumber">The revision number of the workbook to remove.</param>
        /// <param name="cancelToken"></param>
        /// <returns></returns>
        /// <exception cref="TableauRequestException"></exception>
        /// <exception cref="TableauApiVersionException"></exception>
        /// <exception cref="TableauOnlineNotSupportedException"></exception>
        [ApiVersion(2, 3)]
        public async Task RemoveWorkbookRevision(TableauSession session, string workbookId, int revisionNumber,
            CancellationToken? cancelToken = null)
        {
            checkEndpointAvailability();
            checkNullParameters((nameof(session), session), (nameof(workbookId), workbookId));
            await apiRequest(buildUri($"sites/{session.SiteId}/workbooks/{workbookId}/revisions/{revisionNumber}"),
                HttpMethod.Delete, 204, cancelToken, session);
        }
    }
}
