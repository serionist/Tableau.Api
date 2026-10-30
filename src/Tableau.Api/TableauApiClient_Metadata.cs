using System;
using System.Collections.Generic;
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
        /// Get information about all database assets for a site.
        /// </summary>
        /// <param name="session"></param>
        /// <param name="cancelToken"></param>
        /// <returns></returns>
        /// <exception cref="TableauRequestException"></exception>
        /// <exception cref="TableauApiVersionException"></exception>
        /// <exception cref="TableauOnlineNotSupportedException"></exception>
        [ApiVersion(3, 5)]
        public async Task<databaseType[]> QueryDatabases(TableauSession session, CancellationToken? cancelToken = null)
        {
            checkEndpointAvailability();
            checkNullParameters((nameof(session), session));
            return (await getResponseAsObject<databaseListType>(await apiRequest(
                buildUri($"sites/{session.SiteId}/databases"), HttpMethod.Get, 200,
                cancelToken, session))).database;
        }

        /// <summary>
        /// Update the database description, certify a database, or assign a contact (must be a Tableau Server or Tableau Online user) to the database asset.
        ///
        /// This method is available if your Tableau Online site or Tableau Server is licensed with the Data Management Add-on
        /// </summary>
        /// <param name="session"></param>
        /// <param name="databaseId"></param>
        /// <param name="newDescription"></param>
        /// <param name="newContactId"></param>
        /// <param name="isCertified"></param>
        /// <param name="newCertificationNote"></param>
        /// <param name="cancelToken"></param>
        /// <returns></returns>
        /// <exception cref="TableauRequestException"></exception>
        /// <exception cref="TableauApiVersionException"></exception>
        /// <exception cref="TableauOnlineNotSupportedException"></exception>
        [ApiVersion(3, 5)]
        public async Task<databaseType> UpdateDatabase(TableauSession session, string databaseId,
            string newDescription = null, string newContactId = null, bool? isCertified = null,
            string newCertificationNote = null, CancellationToken? cancelToken = null)
        {
            checkEndpointAvailability();
            checkNullParameters((nameof(session), session));
            var t = new databaseType()
            {
                description = newDescription,
                contact = string.IsNullOrEmpty(newContactId) ? null : new userType() {id = newContactId},
                isCertifiedSpecified = isCertified.HasValue, isCertified = (isCertified ?? false),
                certificationNote = newCertificationNote
            };
            return (await getResponseAsObject<databaseType>(await apiRequest(
                buildUri($"sites/{session.SiteId}/databases/{databaseId}"), HttpMethod.Put, 200, cancelToken, session,
                getObjectAsRequestContent(t))));
        }


        /// <summary>
        /// Get information about all table assets for a site.
        /// </summary>
        /// <param name="session"></param>
        /// <param name="cancelToken"></param>
        /// <returns></returns>
        /// <exception cref="TableauRequestException"></exception>
        /// <exception cref="TableauApiVersionException"></exception>
        /// <exception cref="TableauOnlineNotSupportedException"></exception>
        [ApiVersion(3, 5)]
        public async Task<tableType[]> QueryTables(TableauSession session, CancellationToken? cancelToken = null)
        {
            checkEndpointAvailability();
            checkNullParameters((nameof(session), session));
            return (await getResponseAsObject<tableListType>(await apiRequest(
                buildUri($"sites/{session.SiteId}/tables"), HttpMethod.Get, 200,
                cancelToken, session))).table;
        }

        /// <summary>
        /// Update the table description, certify a table, or a assign a user contact to the table asset.
        /// 
        /// This method is available if your Tableau Online site or Tableau Server is licensed with the Data Management Add-on
        /// </summary>
        /// <param name="session"></param>
        /// <param name="tableId"></param>
        /// <param name="newDescription"></param>
        /// <param name="newContactId"></param>
        /// <param name="isCertified"></param>
        /// <param name="newCertificationNote"></param>
        /// <param name="cancelToken"></param>
        /// <returns></returns>
        /// <exception cref="TableauRequestException"></exception>
        /// <exception cref="TableauApiVersionException"></exception>
        /// <exception cref="TableauOnlineNotSupportedException"></exception>
        [ApiVersion(3, 5)]
        public async Task<tableType> UpdateTable(TableauSession session, string tableId,
            string newDescription = null, string newContactId = null, bool? isCertified = null,
            string newCertificationNote = null, CancellationToken? cancelToken = null)
        {
            checkEndpointAvailability();
            checkNullParameters((nameof(session), session), (nameof(tableId), tableId));
            var t = new tableType()
            {
                description = newDescription,
                contact = string.IsNullOrEmpty(newContactId) ? null : new userType() { id = newContactId },
                isCertifiedSpecified = isCertified.HasValue,
                isCertified = (isCertified ?? false),
                certificationNote = newCertificationNote
            };
            return (await getResponseAsObject<tableType>(await apiRequest(
                buildUri($"sites/{session.SiteId}/tables/{tableId}"), HttpMethod.Put, 200, cancelToken, session,
                getObjectAsRequestContent(t))));
        }

        /// <summary>
        /// Get information about the columns in a table asset.
        /// </summary>
        /// <param name="session"></param>
        /// <param name="tableId"></param>
        /// <param name="cancelToken"></param>
        /// <returns></returns>
        /// <exception cref="TableauRequestException"></exception>
        /// <exception cref="TableauApiVersionException"></exception>
        /// <exception cref="TableauOnlineNotSupportedException"></exception>
        [ApiVersion(3, 5)]
        public async Task<columnType[]> QueryColumnsInTable(TableauSession session, string tableId,
            CancellationToken? cancelToken = null)
        {
            checkEndpointAvailability();
            checkNullParameters((nameof(session), session), (nameof(tableId), tableId));
            return (await getResponseAsObject<columnListType>(await apiRequest(
                    buildUri($"sites/{session.SiteId}/tables/{tableId}/columns"), HttpMethod.Get, 200, cancelToken,
                    session)))
                .column;
        }

        /// <summary>
        /// Update the description of the column.
        /// 
        /// This method is available if your Tableau Online site or Tableau Server is licensed with the Data Management Add-on.
        /// </summary>
        /// <param name="session"></param>
        /// <param name="tableId"></param>
        /// <param name="columnId"></param>
        /// <param name="newDescription"></param>
        /// <param name="cancelToken"></param>
        /// <returns></returns>
        /// <exception cref="TableauRequestException"></exception>
        /// <exception cref="TableauApiVersionException"></exception>
        /// <exception cref="TableauOnlineNotSupportedException"></exception>
        [ApiVersion(3, 5)]
        public async Task<columnType> UpdateColumn(TableauSession session, string tableId, string columnId, string newDescription,
            CancellationToken? cancelToken = null)
        {
            checkEndpointAvailability();
            checkNullParameters((nameof(session), session), (nameof(tableId), tableId), (nameof(columnId), columnId), (nameof(newDescription), newDescription));
            return (await getResponseAsObject<columnType>(await apiRequest(
                buildUri($"sites/{session.SiteId}/tables/{tableId}/columns/{columnId}"), HttpMethod.Put, 200,
                cancelToken, session, getObjectAsRequestContent(new columnType() {description = newDescription}))));
        }

        /// <summary>
        /// Get information about the data quality warning for the database, table, published data source, or flow.
        /// </summary>
        /// <param name="session"></param>
        /// <param name="contentType">The type of asset that the data quality warning is being attached to</param>
        /// <param name="luid">The unique ID of the content type (database, table, published data source, or flow). This is the same as the content ID.</param>
        /// <param name="cancelToken"></param>
        /// <returns></returns>
        /// <exception cref="TableauRequestException"></exception>
        /// <exception cref="TableauApiVersionException"></exception>
        /// <exception cref="TableauOnlineNotSupportedException"></exception>
        [ApiVersion(3, 5)]
        public async Task<dataQualityWarningType[]> QueryDataQualityWarningByContent(TableauSession session,
            MetadataContentType contentType, string luid, CancellationToken? cancelToken = null)
        {
            checkEndpointAvailability();
            checkNullParameters((nameof(session), session), (nameof(luid), luid));
            return (await getResponseAsObject<dataQualityWarningListType>(await apiRequest(
                buildUri(
                    $"sites/{session.SiteId}/dataQualityWarnings/{Enum.GetName(typeof(MetadataContentType), contentType)}/{luid}"),
                HttpMethod.Get, 200, cancelToken, session))).dataQualityWarning;

        }

        /// <summary>
        /// Create and apply a data quality warning to a database, table, published data source, or flow. Only one data quality warning can be applied to the asset. Adding a data quality warning to the asset that already has one causes an error.
        /// This method is available if your Tableau Online site or Tableau Server is licensed with the Data Management Add-on.
        /// </summary>
        /// <param name="session"></param>
        /// <param name="contentType">The type of asset that the data quality warning is being attached to. </param>
        /// <param name="luid">The unique ID of the asset (database, table, published data source, or flow). This is the same as the content ID.</param>
        /// <param name="type">Type of data quality warning to apply to the asset</param>
        /// <param name="message">(Optional) A custom message to accompany the data quality warning.</param>
        /// <param name="severity">(Optional) Enables high visibility for the data quality warning when set to "true". Value can be "true" or "false".</param>
        /// <param name="state">(Optional) Controls whether the data quality warning displays. Value can be "true" or "false". If the state is not specified, the data quality warning is set to "true" and is visible by default.</param>
        /// <param name="cancelToken"></param>
        /// <returns></returns>
        /// <exception cref="TableauRequestException"></exception>
        /// <exception cref="TableauApiVersionException"></exception>
        /// <exception cref="TableauOnlineNotSupportedException"></exception>
        [ApiVersion(3, 5)]
        public async Task<dataQualityWarningType> AddDataQualityWarning(TableauSession session,
            MetadataContentType contentType, string luid, DataQualityWarningType type, string message = null, bool? severity = null, bool? state = null, CancellationToken? cancelToken = null)
        {
            checkEndpointAvailability();
            checkNullParameters((nameof(session), session), (nameof(luid), luid));
            return (await getResponseAsObject<dataQualityWarningType>(await apiRequest(
                buildUri(
                    $"sites/{session.SiteId}/dataQualityWarnings/{Enum.GetName(typeof(MetadataContentType), contentType)}/{luid}"),
                HttpMethod.Post, 201, cancelToken, session, getObjectAsRequestContent(new dataQualityWarningType()
                {
                    type = Enum.GetName(typeof(DataQualityWarningType), type),
                    isActiveSpecified = state.HasValue,
                    isActive = state ?? false,
                    message = message,
                    isSevereSpecified = severity.HasValue,
                    isSevere = severity ?? false
                }))));
        }

        /// <summary>
        /// Update the warning type, status, and message of a data quality warning.
        /// This method is available if your Tableau Online site or Tableau Server is licensed with the Data Management Add-on
        /// </summary>
        /// <param name="session"></param>
        /// <param name="dataQualityWarningId">The unique ID of the data quality warning attached to the asset (database, table, published data source, or flow).</param>
        /// <param name="type">Type of data quality warning to apply to the asset.</param>
        /// <param name="message">	(Optional) Custom text to accompany the data quality warning.</param>
        /// <param name="severity">	(Optional) Enables high visibility for the data quality warning when set to "true". Values can be "true" or "false".</param>
        /// <param name="state">(Optional) Controls whether the data quality warning displays.Value can be "true" or "false". If the state is not specified, the data quality warning is set to "true" and is visible by default.</param>
        /// <param name="cancelToken"></param>
        /// <returns></returns>
        [ApiVersion(3, 5)]
        public async Task<dataQualityWarningType> UpdateDataQualityWarning(TableauSession session, string dataQualityWarningId, DataQualityWarningType type, string message = null, bool? severity = null, bool? state = null, CancellationToken? cancelToken = null)
        {
            checkEndpointAvailability();
            checkNullParameters((nameof(session), session), (nameof(dataQualityWarningId), dataQualityWarningId));
            return (await getResponseAsObject<dataQualityWarningType>(await apiRequest(
                buildUri(
                    $"sites/{session.SiteId}/dataQualityWarnings/{dataQualityWarningId}"),
                HttpMethod.Put, 200, cancelToken, session, getObjectAsRequestContent(new dataQualityWarningType()
                {
                    type = Enum.GetName(typeof(DataQualityWarningType), type),
                    isActiveSpecified = state.HasValue,
                    isActive = state ?? false,
                    message = message,
                    isSevereSpecified = severity.HasValue,
                    isSevere = severity ?? false
                }))));
        }

    }
}
