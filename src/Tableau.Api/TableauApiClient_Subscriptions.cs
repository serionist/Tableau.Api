using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Tableau.Api.Attributes;
using Tableau.Api.Exceptions;
using Tableau.Api.Models;

namespace Tableau.Api;

public partial class TableauApiClient
{

    /// <summary>
    /// Creates a new subscription to a view or workbook for a specific user. When a user is subscribed to the content, Tableau Server sends the content to the user in email on the schedule that's defined in Tableau Server and specified in this call. Versions 2.3+
    /// </summary>
    /// <param name="session"></param>
    /// <param name="subscriptionSubject">A description for the subscription. This description is displayed when users list subscriptions for a site in the server environment. A description is required.</param>
    /// <param name="contentType">Workbook to create a subscription for a workbook, or View to create a subscription for a view.</param>
    /// <param name="contentId">The ID of the workbook or view to subscribe to.</param>
    /// <param name="scheduleId">The ID of a schedule to associate the subscription with. </param>
    /// <param name="userId">The ID of the user to create the subscription for. Note: The user must have an email address defined in Tableau Server.</param>
    /// <param name="cancelToken"></param>
    /// <returns></returns>
    /// <exception cref="TableauRequestException"></exception>
    /// <exception cref="TableauApiVersionException"></exception>
    /// <exception cref="TableauOnlineNotSupportedException"></exception>
    [ApiVersion(2, 3)]
    public async Task<subscriptionType> CreateSubscription(Models.TableauSession session, string subscriptionSubject,
        subscriptionContentTypeType contentType, string contentId, string scheduleId, string userId,
        CancellationToken? cancelToken)
    {
        checkEndpointAvailability();
        checkNullParameters((nameof(session), session), (nameof(subscriptionSubject), subscriptionSubject), (nameof(contentId), contentId), (nameof(scheduleId), scheduleId), (nameof(userId), userId));
        return await getResponseAsObject<subscriptionType>(await apiRequest(
            buildUri($"sites/{session.SiteId}/subscriptions"), HttpMethod.Post, 201,
            cancelToken, session,
            getObjectAsRequestContent(new subscriptionType()
            {
                subject = subscriptionSubject,
                content = new subscriptionContentType() { type = contentType, id = contentId },
                schedule = new scheduleType() { id = scheduleId },
                user = new userType() { id = userId }
            })));
    }
    /// <summary>
    /// Returns information about the specified subscription. Versions 2.3+
    /// </summary>
    /// <param name="session"></param>
    /// <param name="subscriptionId"></param>
    /// <param name="cancelToken"></param>
    /// <returns></returns>
    /// <exception cref="TableauRequestException"></exception>
    /// <exception cref="TableauApiVersionException"></exception>
    /// <exception cref="TableauOnlineNotSupportedException"></exception>
    [ApiVersion(2, 3)]
    public async Task<subscriptionType> QuerySubscription(Models.TableauSession session, string subscriptionId,
        CancellationToken? cancelToken = null)
    {
        checkEndpointAvailability();
        checkNullParameters((nameof(session), session), (nameof(subscriptionId), subscriptionId));
        return await getResponseAsObject<subscriptionType>(await apiRequest(
            buildUri($"sites/{session.SiteId}/subscriptions/{subscriptionId}"), HttpMethod.Get, 200,
            cancelToken, session));
    }
    /// <summary>
    /// Returns a list of all the subscriptions on the specified site. Versions 2.3+
    /// </summary>
    /// <param name="session"></param>
    /// <param name="pageSize">(Optional) The number of items to return in one response. The minimum is 1. The maximum is 1000. The default is 100.</param>
    /// <param name="pageNumber">(Optional) The offset for paging. The default is 1.</param>
    /// <param name="cancelToken"></param>
    /// <returns></returns>
    /// <exception cref="TableauRequestException"></exception>
    /// <exception cref="TableauApiVersionException"></exception>
    /// <exception cref="TableauOnlineNotSupportedException"></exception>
    [ApiVersion(2, 3)]
    public async Task<(paginationType pagination, subscriptionType[] subscriptions)> QuerySubscriptions(
        Models.TableauSession session, int pageSize = 100, int pageNumber = 1, CancellationToken? cancelToken = null)
    {
        checkEndpointAvailability();
        checkNullParameters((nameof(session), session));
        checkParametersBetween((nameof(pageSize), pageSize, 1, 1000), (nameof(pageNumber), pageNumber, 1, int.MaxValue));
        var ret = await getResponseAsObject<paginationType, subscriptionListType>(
            await apiRequest(
                buildUri($"sites/{session.SiteId}/subscriptions",
                    ("pageSize", pageSize.ToString(CultureInfo.InvariantCulture)),
                    ("pageNumber", pageNumber.ToString(CultureInfo.InvariantCulture))), HttpMethod.Get, 200,
                cancelToken, session));
        return (ret.Item1, ret.Item2.subscription);
    }
    /// <summary>
    /// Modifies an existing subscription, allowing you to change the subject or schedule for the subscription. Versions 2.3+
    /// </summary>
    /// <param name="session"></param>
    /// <param name="subscriptionId">The ID of the subscription to update.</param>
    /// <param name="newSubject">(Optional) A new subject for the subscription.</param>
    /// <param name="newScheduleId">(Optional) The ID of a schedule to associate this subscription with.</param>
    /// <param name="cancelToken"></param>
    /// <returns></returns>
    /// <exception cref="TableauRequestException"></exception>
    /// <exception cref="TableauApiVersionException"></exception>
    /// <exception cref="TableauOnlineNotSupportedException"></exception>
    [ApiVersion(2, 3)]
    public async Task<subscriptionType> UpdateSubscription(Models.TableauSession session, string subscriptionId,
        string newSubject = null, string newScheduleId = null, CancellationToken? cancelToken = null)
    {
        checkEndpointAvailability();
        checkNullParameters((nameof(session), session), (nameof(subscriptionId), subscriptionId));
        return await getResponseAsObject<subscriptionType>(await apiRequest(
            buildUri($"sites/{session.SiteId}/subscriptions/{subscriptionId}"), HttpMethod.Put, 200,
            cancelToken, session,
            getObjectAsRequestContent(new subscriptionType()
            {
                subject = newSubject,
                schedule = new scheduleType() { id = newScheduleId }
            })));
    }
    /// <summary>
    /// Deletes the specified subscription. Versions 2.3+
    /// </summary>
    /// <param name="session"></param>
    /// <param name="subscriptionId">The ID of the subscription to delete.</param>
    /// <param name="cancelToken"></param>
    /// <returns></returns>
    /// <exception cref="TableauRequestException"></exception>
    /// <exception cref="TableauApiVersionException"></exception>
    /// <exception cref="TableauOnlineNotSupportedException"></exception>
    [ApiVersion(2, 3)]
    public async Task DeleteSubscription(Models.TableauSession session, string subscriptionId,
        CancellationToken? cancelToken = null)
    {

        checkEndpointAvailability();
        checkNullParameters((nameof(session), session), (nameof(subscriptionId), subscriptionId));
        await apiRequest(buildUri($"sites/{session.SiteId}/subscriptions/{subscriptionId}"), HttpMethod.Delete, 204,
            cancelToken, session);
    }
}
