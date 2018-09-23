using System;
using System.Collections.Generic;
using System.IO;
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
        /// Initiates a file upload to Tableau Server. Available in all versions
        /// </summary>
        /// <param name="session"></param>
        /// <param name="cancelToken"></param>
        /// <returns></returns>
        /// <exception cref="TableauRequestException"></exception>
        /// <exception cref="TableauApiVersionException"></exception>
        /// <exception cref="TableauOnlineNotSupportedException"></exception>
        [ApiVersion(2, 0)]
        public async Task<fileUploadType> InitiateFileUpload(TableauSession session, CancellationToken? cancelToken = null)
        {
            checkEndpointAvailability();
            checkNullParameters((nameof(session), session));
            return await getResponseAsObject<fileUploadType>(await apiRequest(buildUri($"sites/{session.SiteId}/fileUploads"),
                HttpMethod.Post, 201, cancelToken, session));

        }

        /// <summary>
        /// Appends to a file upload to Tableau Server from an existing stream. Available in all versions
        /// </summary>
        /// <param name="session"></param>
        /// <param name="fileUploadId">The file upload session id to append to</param>
        /// <param name="sourceStream">The source stream to read and upload. This operation afvances the stream and does not close it</param>
        /// <param name="maxBytesToRead">The maximum number of bytes to read from the stream. Tableau doesn't accept chunks larger than 64MB, so the defailt (and maximum) value is 67108864</param>
        /// <param name="cancelToken"></param>
        /// <returns></returns>
        /// <exception cref="TableauRequestException"></exception>
        /// <exception cref="TableauApiVersionException"></exception>
        /// <exception cref="TableauOnlineNotSupportedException"></exception>
        [ApiVersion(2, 0)]
        public async Task<fileUploadType> AppendToFileUpload(TableauSession session, string fileUploadId, Stream sourceStream, int maxBytesToRead = 67108864, CancellationToken? cancelToken = null)
        {
            checkEndpointAvailability();
            checkNullParameters((nameof(session), session), (nameof(fileUploadId), fileUploadId), (nameof(sourceStream), sourceStream));
            checkParametersBetween((nameof(maxBytesToRead), maxBytesToRead, 1, 67108864));
            return await getResponseAsObject<fileUploadType>(await apiRequest(buildUri($"sites/{session.SiteId}/fileUploads/{fileUploadId}"),
                HttpMethod.Put, 200, cancelToken, session, prepareFileUploadContent(new StringContent("", Encoding.UTF8, "text/xml"),  new PartialStream(sourceStream, maxBytesToRead))));

        }
        /// <summary>
        /// Publishes a Datasource. Available in all versions
        /// </summary>
        /// <param name="session"></param>
        /// <param name="fileUploadId">The file upload session id that contains the uploaded file</param>
        /// <param name="datasourceName">The name of the new datasource</param>
        /// <param name="type">The type of the new datasource. This must be the format of the uploaded file</param>
        /// <param name="projectId">The ID of the project to assign the data source to</param>
        /// <param name="connectionCredentials">(Optional) The connection credentials</param>
        /// <param name="overwrite">(Optional) true to overwrite a data source that has the same name, or false to fail if the specified data source already exists. The default is false. If overwrite-flag is set to true but the data source doesn't already exist, the operation succeeds. You can include both the overwrite and append parameters in a request, but they cannot both be true.</param>
        /// <param name="append">(Optional) true to append the data being published to an existing data source that has the same name. The default is false. If append-flag is set to true but the data source doesn't already exist, the operation fails. In order to append data to an existing data source, both the data source on the server and the data source you are publishing must be extracts (.tde and .hyper files). The schemas of the two extracts must match. You can include both the overwrite and append parameters in a request, but they cannot both be true.</param>
        /// <param name="cancelToken"></param>
        /// <returns></returns>
        /// <exception cref="TableauRequestException"></exception>
        /// <exception cref="TableauApiVersionException"></exception>
        /// <exception cref="TableauOnlineNotSupportedException"></exception>
        [ApiVersion(2, 0)]
        public async Task<dataSourceType> PublishDatasource(TableauSession session, string fileUploadId,
            string datasourceName, DatasourceFileType type, string projectId,
            connectionCredentialsType connectionCredentials = null, bool overwrite = false, bool append = false, CancellationToken? cancelToken = null)
        {
            checkEndpointAvailability();
            checkNullParameters((nameof(session), session), (nameof(fileUploadId), fileUploadId), (nameof(datasourceName), datasourceName), (nameof(projectId), projectId));
            var url = buildUri($"sites/{session.SiteId}/datasources", ("uploadSessionId", fileUploadId),
                ("datasourceType", Enum.GetName(typeof(DatasourceFileType), type)), ("overwrite", overwrite.ToString()),
                ("append", append.ToString()));
            return await getResponseAsObject<dataSourceType>(await apiRequest(url, HttpMethod.Post, 201,
                cancelToken, session,
                prepareFileUploadContent(
                    getObjectAsRequestContent(new dataSourceType()
                    {
                        name = datasourceName,
                        project = new projectType() {id = projectId},
                        connectionCredentials = connectionCredentials
                    }), null)));


        }
        /// <summary>
        ///  Publishes a Workbook. Available in all versions
        /// </summary>
        /// <param name="session"></param>
        /// <param name="fileUploadId">The file upload session id that contains the uploaded file</param>
        /// <param name="workbookName">	The name to assign to the workbook when it is saved on the server.</param>
        /// <param name="type">	twb or twbx to indicate whether you have uploaded a workbook file (twb) or a packaged workbook file (twbx).</param>
        /// <param name="projectId">The ID of the project to assign the workbook to.</param>
        /// <param name="showTabs">	(Optional) Specify true to have the published workbook show views in tabs; otherwise, false. The default is false.</param>
        /// <param name="connections">(Optional) The connections to publish the workbooks with</param>
        /// <param name="overwrite">(Optional) true to overwrite a workbook that has the same name, or false to fail if the specified workbook already exists. The default is false. If overwrite-flag is set to true but the workbook doesn't already exist, the operation succeeds.</param>
        /// <param name="cancelToken"></param>
        /// <returns></returns>
        /// <exception cref="TableauRequestException"></exception>
        /// <exception cref="TableauApiVersionException"></exception>
        /// <exception cref="TableauOnlineNotSupportedException"></exception>
        [ApiVersion(2, 0)]
        public async Task<workbookType> PublishWorkbook(TableauSession session, string fileUploadId,
            string workbookName, WorkbookFileType type, string projectId, bool showTabs = false,
            connectionType[] connections = null, bool overwrite = false, 
            CancellationToken? cancelToken = null)
        {
            checkEndpointAvailability();
            checkNullParameters((nameof(session), session), (nameof(fileUploadId), fileUploadId), (nameof(workbookName), workbookName), (nameof(projectId), projectId));
            var url = buildUri($"sites/{session.SiteId}/workbooks", ("uploadSessionId", fileUploadId),
                ("workbookType", Enum.GetName(typeof(WorkbookFileType), type)), ("overwrite", overwrite.ToString()));
            return await getResponseAsObject<workbookType>(await apiRequest(url, HttpMethod.Post, 201,
                cancelToken, session,
                prepareFileUploadContent(
                    getObjectAsRequestContent(new workbookType()
                    {
                        name = workbookName,
                        showTabs = showTabs,
                        showTabsSpecified = true,
                        project = new projectType() {id = projectId},
                        connections = connections
                    }), null)));

        }
    }
}
