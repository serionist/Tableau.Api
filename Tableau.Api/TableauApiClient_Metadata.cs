using System;
using System.Collections.Generic;
using System.Linq;
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
        /// <param name=""></param>
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

    }
}
