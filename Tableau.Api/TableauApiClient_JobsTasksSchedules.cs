using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Tableau.Api.Attributes;
using Tableau.Api.Exceptions;
using Tableau.Api.Models;

namespace Tableau.Api
{
    public partial class TableauApiClient
    {
       
        /// <summary>
        /// Cancels a job specified by job ID. Versions 3.1+
        /// </summary>
        /// <param name="session"></param>
        /// <param name="jobId">The ID of the job to cancel.</param>
        /// <param name="cancelToken"></param>
        /// <returns></returns>
        ///  <exception cref="TableauRequestException"></exception>
        /// <exception cref="TableauApiVersionException"></exception>
        /// <exception cref="TableauOnlineNotSupportedException"></exception>
        [ApiVersion(3, 1)]
        public async Task CancelJob(TableauSession session, string jobId, CancellationToken? cancelToken = null)
        {
            checkEndpointAvailability();
            checkNullParameters((nameof(session), session), (nameof(jobId), jobId));
            await apiRequest(buildUri($"sites/{session.SiteId}/jobs/{jobId}"), HttpMethod.Put, 200,
                cancelToken, session);
        }
        /// <summary>
        /// Cancels a job specified by job ID. Available in all versions
        /// </summary>
        /// <param name="session"></param>
        /// <param name="jobId">The ID of the job to cancel.</param>
        /// <param name="cancelToken"></param>
        /// <returns></returns>
        ///  <exception cref="TableauRequestException"></exception>
        /// <exception cref="TableauApiVersionException"></exception>
        /// <exception cref="TableauOnlineNotSupportedException"></exception>
        [ApiVersion(2, 0)]
        public async Task<jobType> QueryJob(TableauSession session, string jobId, CancellationToken? cancelToken = null)
        {
            checkEndpointAvailability();
            checkNullParameters((nameof(session), session), (nameof(jobId), jobId));
            return await getResponseAsObject<jobType>(await apiRequest(buildUri($"sites/{session.SiteId}/jobs/{jobId}"),
                HttpMethod.Get, 200, cancelToken, session));

        }

        /// <summary>
        /// Returns information about the specified extract refresh task. Versions 2.6+
        /// </summary>
        /// <param name="session"></param>
        /// <param name="taskId">The ID of the extract refresh that you want information about.</param>
        /// <param name="cancelToken"></param>
        /// <returns></returns>
        ///  <exception cref="TableauRequestException"></exception>
        /// <exception cref="TableauApiVersionException"></exception>
        /// <exception cref="TableauOnlineNotSupportedException"></exception>
        [ApiVersion(2, 6)]
        public async Task<taskExtractRefreshType> GetExtractRefreshTask(TableauSession session, string taskId, CancellationToken? cancelToken = null)
        {
            checkEndpointAvailability();
            checkNullParameters((nameof(session), session), (nameof(taskId), taskId));
            return (await getResponseAsObject<taskType>(await apiRequest(
                buildUri($"sites/{session.SiteId}/tasks/extractRefreshes/{taskId}"),
                HttpMethod.Get, 200, cancelToken, session))).Item as taskExtractRefreshType;

        }
        /// <summary>
        /// Returns a list of extract refresh tasks for the site. Versions 2.6+
        /// </summary>
        /// <param name="session"></param>
        /// <param name="cancelToken"></param>
        /// <returns>The site's tasks by the kind of item each holds; an array is empty, never null, when the site has none.</returns>
        ///  <exception cref="TableauRequestException"></exception>
        /// <exception cref="TableauApiVersionException"></exception>
        /// <exception cref="TableauOnlineNotSupportedException"></exception>
        [ApiVersion(2, 6)]
        public async Task<(taskExtractRefreshType[], taskRunFlowType[], scheduleType[])> GetExtractRefreshTasks(TableauSession session,
            CancellationToken? cancelToken = null)
        {
            checkEndpointAvailability();
            checkNullParameters((nameof(session), session));
            var resp = (await getResponseAsObject<taskListType>(await apiRequest(
                buildUri($"sites/{session.SiteId}/tasks/extractRefreshes"),
                HttpMethod.Get, 200, cancelToken, session)));
            // Each task holds one item; an empty <tasks/> has no task list at all.
            var items = (resp?.task ?? new taskType[0]).Select(e => e.Item).ToArray();
            return (items.OfType<taskExtractRefreshType>().ToArray(),
                items.OfType<taskRunFlowType>().ToArray(),
                items.OfType<scheduleType>().ToArray());
        }

        private async Task<scheduleType> _createSchedule(TableauSession session, string name, int priority,
            scheduleTypeFrequency frequency, scheduleTypeType type, scheduleTypeExecutionOrder executionOrder,
            TimeSpan startTime, TimeSpan? endTime, intervalType[] intervals, CancellationToken? cancelToken)
        {
            checkEndpointAvailability();
            checkNullParameters((nameof(session), session), (nameof(name), name));
            checkParametersBetween((nameof(priority), priority, 1, 100));
            checkTimeOfDay(nameof(startTime), startTime);
            if (endTime.HasValue) checkTimeOfDay(nameof(endTime), endTime.Value);
            return await getResponseAsObject<scheduleType>(await apiRequest(buildUri($"schedules"), HttpMethod.Post,
                201, cancelToken, session,
                getScheduleRequestContent(new scheduleType()
                {
                    name = name,
                    priority = priority.ToString(),
                    type = type,
                    frequency = frequency,
                    frequencySpecified = true,
                    typeSpecified = true,
                    executionOrder = executionOrder,
                    executionOrderSpecified = true,
                    frequencyDetails = new frequencyDetailsType() { intervals = intervals }
                }, startTime, endTime)));

        }

        private static void checkTimeOfDay(string name, TimeSpan time)
        {
            if (time < TimeSpan.Zero || time >= TimeSpan.FromDays(1))
                throw new ArgumentOutOfRangeException(name, time,
                    "A schedule's time is a time of day, from 00:00:00 up to but not including 24:00:00.");
        }

        // Tableau takes a schedule's times as HH:mm:ss. XmlSerializer would write the model's time-typed DateTime
        // members with the machine's UTC offset, so only the request is rewritten: the members keep their serialiser
        // attributes, and a schedules response still fills them.
        private StringContent getScheduleRequestContent(scheduleType schedule, TimeSpan startTime, TimeSpan? endTime)
        {
            var request = XElement.Parse(serializeRequest(schedule));
            var details = request.Descendants().First(e => e.Name.LocalName == "frequencyDetails");
            details.SetAttributeValue("start", startTime.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture));
            details.SetAttributeValue("end", endTime?.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture));
            return xmlRequestContent(request.ToString(SaveOptions.DisableFormatting));
        }

        /// <summary>
        /// Creates a new schedule on Tableau Server. Versions 2.3+
        /// </summary>
        /// <param name="session"></param>
        /// <param name="name">The name to give to the schedule. This name identifies the schedule in the server environment when users select a schedule and manage schedule information.</param>
        /// <param name="priority">An integer value between 1 and 100 that determines the default priority of the schedule if multiple tasks are pending in the queue. Higher numbers have higher priority.</param>
        /// <param name="type">Extract to create an extract refresh schedule, or Subscription to create a subscription schedule.</param>
        /// <param name="executionOrder">Parallel to allow jobs associated with this schedule to run at the same time, or Serial to require the jobs to run one after the other.</param>
        /// <param name="startTime">The time of day on which scheduled jobs should run (or if the frequency is hourly, on which they should start being run), in the format HH:MM:SS (for example, 18:30:00). </param>
        /// <param name="endTime">The time of day on which scheduled jobs should stop being run, in the format HH:MM:SS (for example, 23:30:00).</param>
        /// <param name="minutesBetweenJobs">The minutes to wait between jobs</param>
        /// <param name="cancelToken"></param>
        /// <returns></returns>
        ///  <exception cref="TableauRequestException"></exception>
        /// <exception cref="TableauApiVersionException"></exception>
        /// <exception cref="TableauOnlineNotSupportedException"></exception>
        /// <exception cref="ArgumentOutOfRangeException">startTime or endTime is not a time of day, from 00:00:00 up to but not including 24:00:00.</exception>
        [ApiVersion(2, 3)]
        [OnPremiseOnly]
        public async Task<scheduleType> CreateHourlySchedule(TableauSession session, string name, int priority,
            scheduleTypeType type, scheduleTypeExecutionOrder executionOrder,
            TimeSpan startTime, TimeSpan? endTime, intervalTypeMinutes minutesBetweenJobs,
            CancellationToken? cancelToken = null)
        {
            var i = new[] {new intervalType() {minutes = minutesBetweenJobs, minutesSpecified = true},};
            return await _createSchedule(session, name, priority, scheduleTypeFrequency.Hourly, type, executionOrder,
                startTime, endTime, i, cancelToken);

        }


        /// <summary>
        /// Creates a new schedule on Tableau Server. Versions 2.3+
        /// </summary>
        /// <param name="session"></param>
        /// <param name="name">The name to give to the schedule. This name identifies the schedule in the server environment when users select a schedule and manage schedule information.</param>
        /// <param name="priority">An integer value between 1 and 100 that determines the default priority of the schedule if multiple tasks are pending in the queue. Higher numbers have higher priority.</param>
        /// <param name="type">Extract to create an extract refresh schedule, or Subscription to create a subscription schedule.</param>
        /// <param name="executionOrder">Parallel to allow jobs associated with this schedule to run at the same time, or Serial to require the jobs to run one after the other.</param>
        /// <param name="startTime">The time of day on which scheduled jobs should run (or if the frequency is hourly, on which they should start being run), in the format HH:MM:SS (for example, 18:30:00). </param>
        /// <param name="endTime">The time of day on which scheduled jobs should stop being run, in the format HH:MM:SS (for example, 23:30:00).</param>
        /// <param name="hoursBetweenJobs">The hours to wait between jobs</param>
        /// <param name="cancelToken"></param>
        ///  <exception cref="TableauRequestException"></exception>
        /// <exception cref="TableauApiVersionException"></exception>
        /// <exception cref="TableauOnlineNotSupportedException"></exception>
        /// <exception cref="ArgumentOutOfRangeException">startTime or endTime is not a time of day, from 00:00:00 up to but not including 24:00:00.</exception>
        [ApiVersion(2, 3)]
        [OnPremiseOnly]
        public async Task<scheduleType> CreateHourlySchedule(TableauSession session, string name, int priority,
            scheduleTypeType type, scheduleTypeExecutionOrder executionOrder,
            TimeSpan startTime, TimeSpan endTime, intervalTypeHours hoursBetweenJobs, CancellationToken? cancelToken = null)
        {
            var i = new[] { new intervalType() { hours = hoursBetweenJobs, hoursSpecified = true }, };
            return await _createSchedule(session, name, priority, scheduleTypeFrequency.Hourly, type, executionOrder,
                startTime, endTime, i, cancelToken);

        }

        /// <summary>
        /// Creates a new schedule on Tableau Server. Versions 2.3+
        /// </summary>
        /// <param name="session"></param>
        /// <param name="name">The name to give to the schedule. This name identifies the schedule in the server environment when users select a schedule and manage schedule information.</param>
        /// <param name="priority">An integer value between 1 and 100 that determines the default priority of the schedule if multiple tasks are pending in the queue. Higher numbers have higher priority.</param>
        /// <param name="type">Extract to create an extract refresh schedule, or Subscription to create a subscription schedule.</param>
        /// <param name="executionOrder">Parallel to allow jobs associated with this schedule to run at the same time, or Serial to require the jobs to run one after the other.</param>
        /// <param name="startTime">The time of day on which scheduled jobs should run (or if the frequency is hourly, on which they should start being run), in the format HH:MM:SS (for example, 18:30:00). </param>
        /// <param name="cancelToken"></param>
        /// <returns></returns>
        ///  <exception cref="TableauRequestException"></exception>
        /// <exception cref="TableauApiVersionException"></exception>
        /// <exception cref="TableauOnlineNotSupportedException"></exception>
        /// <exception cref="ArgumentOutOfRangeException">startTime is not a time of day, from 00:00:00 up to but not including 24:00:00.</exception>
        [ApiVersion(2, 3)]
        [OnPremiseOnly]
        public async Task<scheduleType> CreateDailySchedule(TableauSession session, string name, int priority,
            scheduleTypeType type, scheduleTypeExecutionOrder executionOrder,
            TimeSpan startTime,  CancellationToken? cancelToken = null)
        {
          
            return await _createSchedule(session, name, priority, scheduleTypeFrequency.Daily, type, executionOrder,
                startTime, null, new intervalType[0], cancelToken);

        }
        /// <summary>
        /// Creates a new schedule on Tableau Server. Versions 2.3+
        /// </summary>
        /// <param name="session"></param>
        /// <param name="name">The name to give to the schedule. This name identifies the schedule in the server environment when users select a schedule and manage schedule information.</param>
        /// <param name="priority">An integer value between 1 and 100 that determines the default priority of the schedule if multiple tasks are pending in the queue. Higher numbers have higher priority.</param>
        /// <param name="type">Extract to create an extract refresh schedule, or Subscription to create a subscription schedule.</param>
        /// <param name="executionOrder">Parallel to allow jobs associated with this schedule to run at the same time, or Serial to require the jobs to run one after the other.</param>
        /// <param name="dayToRun">The day of week to run on</param>
        /// <param name="cancelToken"></param>
        /// <returns></returns>
        ///  <exception cref="TableauRequestException"></exception>
        /// <exception cref="TableauApiVersionException"></exception>
        /// <exception cref="TableauOnlineNotSupportedException"></exception>
        /// <exception cref="ArgumentOutOfRangeException">startTime is not a time of day, from 00:00:00 up to but not including 24:00:00.</exception>
        [ApiVersion(2, 3)]
        [OnPremiseOnly]
        public async Task<scheduleType> CreateWeeklySchedule(TableauSession session, string name, int priority,
            scheduleTypeType type, scheduleTypeExecutionOrder executionOrder,
            TimeSpan startTime, intervalTypeWeekDay dayToRun, CancellationToken? cancelToken = null)
        {

            return await _createSchedule(session, name, priority, scheduleTypeFrequency.Weekly, type, executionOrder,
                startTime, null, new []{new intervalType(){weekDay = dayToRun, weekDaySpecified = true}, }, cancelToken);

        }
        /// <summary>
        /// Creates a new schedule on Tableau Server. Versions 2.3+
        /// </summary>
        /// <param name="session"></param>
        /// <param name="name">The name to give to the schedule. This name identifies the schedule in the server environment when users select a schedule and manage schedule information.</param>
        /// <param name="priority">An integer value between 1 and 100 that determines the default priority of the schedule if multiple tasks are pending in the queue. Higher numbers have higher priority.</param>
        /// <param name="type">Extract to create an extract refresh schedule, or Subscription to create a subscription schedule.</param>
        /// <param name="executionOrder">Parallel to allow jobs associated with this schedule to run at the same time, or Serial to require the jobs to run one after the other.</param>
        /// <param name="dayToRun">Either the day of the month (1, 2, etc.) or LastDay, sent as the interval's monthDay.</param>
        /// <param name="cancelToken"></param>
        /// <returns></returns>
        ///  <exception cref="TableauRequestException"></exception>
        /// <exception cref="TableauApiVersionException"></exception>
        /// <exception cref="TableauOnlineNotSupportedException"></exception>
        /// <exception cref="ArgumentOutOfRangeException">startTime is not a time of day, from 00:00:00 up to but not including 24:00:00.</exception>
        [ApiVersion(2, 3)]
        [OnPremiseOnly]
        public async Task<scheduleType> CreateMonthlySchedule(TableauSession session, string name, int priority,
            scheduleTypeType type, scheduleTypeExecutionOrder executionOrder,
            TimeSpan startTime, string dayToRun, CancellationToken? cancelToken = null)
        {
            var intervals = string.IsNullOrEmpty(dayToRun)
                ? new intervalType[0]
                : new[] { new intervalType() { monthDay = dayToRun } };
            return await _createSchedule(session, name, priority, scheduleTypeFrequency.Monthly, type, executionOrder,
                startTime, null, intervals, cancelToken);

        }
        /// <summary>
        /// Returns a list of the extract refresh tasks for a specified schedule. Versions 2.2+
        /// </summary>
        /// <param name="session"></param>
        /// <param name="scheduleId">The ID of the schedule to get extract information for.</param>
        /// <param name="pageSize">	(Optional) The number of items to return in one response. The minimum is 1. The maximum is 1000. The default is 100.</param>
        /// <param name="pageNumber">(Optional) The offset for paging. The default is 1.</param>
        /// <param name="cancelToken"></param>
        /// <returns></returns>
        ///  <exception cref="TableauRequestException"></exception>
        /// <exception cref="TableauApiVersionException"></exception>
        /// <exception cref="TableauOnlineNotSupportedException"></exception>
        [ApiVersion(2, 2)]
        [OnPremiseOnly]
        public async Task<(paginationType pagination, extractType[] extracts)> QueryExtractRefreshTasks(
            TableauSession session, string scheduleId, int pageSize = 100, int pageNumber = 1,
            CancellationToken? cancelToken = null)
        {
            checkEndpointAvailability();
            checkNullParameters((nameof(session), session), (nameof(scheduleId), scheduleId));
            checkParametersBetween((nameof(pageSize), pageSize, 1, 1000), (nameof(pageNumber), pageNumber, 1, int.MaxValue));
            var ret = await getResponseAsObject<paginationType, extractListType>(
                await apiRequest(
                    buildUri($"sites/{session.SiteId}/schedules/{scheduleId}/extracts",
                        ("pageSize", pageSize.ToString()), ("pageNumber", pageNumber.ToString())), HttpMethod.Get, 200,
                    cancelToken, session));
            return (ret.Item1, ret.Item2.extract);
        }
        /// <summary>
        /// Returns a list of extract and subscription schedules. For each schedule, the API returns the name, frequency, priority, and other information. Versions 2.2+
        /// </summary>
        /// <param name="session"></param>
        /// <param name="pageSize">(Optional) The number of items to return in one response. The minimum is 1. The maximum is 1000. The default is 100.</param>
        /// <param name="pageNumber">(Optional) The offset for paging. The default is 1.</param>
        /// <param name="cancelToken"></param>
        /// <returns></returns>
        ///  <exception cref="TableauRequestException"></exception>
        /// <exception cref="TableauApiVersionException"></exception>
        /// <exception cref="TableauOnlineNotSupportedException"></exception>
        [ApiVersion(2, 2)]
        [OnPremiseOnly]
        public async Task<(paginationType pagination, scheduleType[] extracts)> QuerySchedules(
            TableauSession session, int pageSize = 100, int pageNumber = 1,
            CancellationToken? cancelToken = null)
        {
            checkEndpointAvailability();
            checkNullParameters((nameof(session), session));
            checkParametersBetween((nameof(pageSize), pageSize, 1, 1000), (nameof(pageNumber), pageNumber, 1, int.MaxValue));
            var ret = await getResponseAsObject<paginationType, scheduleListType>(
                await apiRequest(
                    buildUri($"schedules", ("pageSize", pageSize.ToString()), ("pageNumber", pageNumber.ToString())), HttpMethod.Get, 200,
                    cancelToken, session));
            return (ret.Item1, ret.Item2.schedule);
        }
        /// <summary>
        /// Runs the specified extract refresh task. Versions 2.6+
        /// </summary>
        /// <param name="session"></param>
        /// <param name="taskId">The ID of the extract refresh task that you want to run.</param>
        /// <param name="cancelToken"></param>
        /// <returns></returns>
        ///  <exception cref="TableauRequestException"></exception>
        /// <exception cref="TableauApiVersionException"></exception>
        /// <exception cref="TableauOnlineNotSupportedException"></exception>
        [ApiVersion(2, 6)]
        public async Task<jobType> RunExtractRefreshTask(TableauSession session, string taskId,
            CancellationToken? cancelToken = null)
        {
            checkEndpointAvailability();
            checkNullParameters((nameof(session), session), (nameof(taskId), taskId));
            return await getResponseAsObject<jobType>(await apiRequest(
                buildUri($"sites/{session.SiteId}/tasks/extractRefreshes/{taskId}/runNow"), HttpMethod.Post, 200,
                cancelToken, session, getObjectAsRequestContent(null)));
        }


        // Tableau updates a schedule with PUT schedules/{id}, and no UpdateTo*Schedule method takes the id: they posted a
        // new schedule instead, which any 2xx would now report as a success. So they are refused before sending.
        private Task<scheduleType> _updateSchedule(TableauSession session, string newName, int newPriority,
            scheduleTypeFrequency frequency, scheduleTypeType type, scheduleTypeExecutionOrder executionOrder,
            TimeSpan startTime, TimeSpan? endTime, intervalType[] intervals, CancellationToken? cancelToken)
        {
            throw new NotSupportedException(
                "Tableau.Api 1.x can't update a schedule: Tableau updates one by its id, which the UpdateTo*Schedule "
                + "methods don't take, and they created a new schedule instead. Delete the schedule and create it again.");
        }


        /// <summary>
        /// Not supported in Tableau.Api 1.x: throws NotSupportedException before anything is sent. Tableau updates a schedule by its id, which this method does not take, and 1.2.5 created a new schedule instead. Versions 2.3+
        /// </summary>
        /// <param name="session"></param>
        /// <param name="newName">The name to give to the schedule. This name identifies the schedule in the server environment when users select a schedule and manage schedule information.</param>
        /// <param name="newPriority">An integer value between 1 and 100 that determines the default priority of the schedule if multiple tasks are pending in the queue. Higher numbers have higher priority.</param>
        /// <param name="newType">Extract to create an extract refresh schedule, or Subscription to create a subscription schedule.</param>
        /// <param name="newExecutionOrder">Parallel to allow jobs associated with this schedule to run at the same time, or Serial to require the jobs to run one after the other.</param>
        /// <param name="newStartTime">The time of day on which scheduled jobs should run (or if the frequency is hourly, on which they should start being run), in the format HH:MM:SS (for example, 18:30:00). </param>
        /// <param name="newEndTime">The time of day on which scheduled jobs should stop being run, in the format HH:MM:SS (for example, 23:30:00).</param>
        /// <param name="newMinutesBetweenJobs">The minutes to wait between jobs</param>
        /// <param name="cancelToken"></param>
        /// <returns></returns>
        ///  <exception cref="TableauRequestException"></exception>
        /// <exception cref="TableauApiVersionException"></exception>
        /// <exception cref="TableauOnlineNotSupportedException"></exception>
        /// <exception cref="NotSupportedException">Always, before anything is sent.</exception>
        [ApiVersion(2, 3)]
        [OnPremiseOnly]
        public async Task<scheduleType> UpdateToHourlySchedule(TableauSession session, string newName, int newPriority,
            scheduleTypeType newType, scheduleTypeExecutionOrder newExecutionOrder,
            TimeSpan newStartTime, TimeSpan? newEndTime, intervalTypeMinutes newMinutesBetweenJobs,
            CancellationToken? cancelToken = null)
        {
            var i = new[] { new intervalType() { minutes = newMinutesBetweenJobs, minutesSpecified = true }, };
            return await _updateSchedule(session, newName, newPriority, scheduleTypeFrequency.Hourly, newType, newExecutionOrder,
                newStartTime, newEndTime, i, cancelToken);

        }


        /// <summary>
        /// Not supported in Tableau.Api 1.x: throws NotSupportedException before anything is sent. Tableau updates a schedule by its id, which this method does not take, and 1.2.5 created a new schedule instead. Versions 2.3+
        /// </summary>
        /// <param name="session"></param>
        /// <param name="newName">The name to give to the schedule. This name identifies the schedule in the server environment when users select a schedule and manage schedule information.</param>
        /// <param name="priority">An integer value between 1 and 100 that determines the default priority of the schedule if multiple tasks are pending in the queue. Higher numbers have higher priority.</param>
        /// <param name="type">Extract to create an extract refresh schedule, or Subscription to create a subscription schedule.</param>
        /// <param name="executionOrder">Parallel to allow jobs associated with this schedule to run at the same time, or Serial to require the jobs to run one after the other.</param>
        /// <param name="startTime">The time of day on which scheduled jobs should run (or if the frequency is hourly, on which they should start being run), in the format HH:MM:SS (for example, 18:30:00). </param>
        /// <param name="endTime">The time of day on which scheduled jobs should stop being run, in the format HH:MM:SS (for example, 23:30:00).</param>
        /// <param name="hoursBetweenJobs">The hours to wait between jobs</param>
        /// <param name="cancelToken"></param>
        ///  <exception cref="TableauRequestException"></exception>
        /// <exception cref="TableauApiVersionException"></exception>
        /// <exception cref="TableauOnlineNotSupportedException"></exception>
        /// <exception cref="NotSupportedException">Always, before anything is sent.</exception>
        [ApiVersion(2, 3)]
        [OnPremiseOnly]
        public async Task<scheduleType> UpdateToHourlySchedule(TableauSession session, string newName, int priority,
            scheduleTypeType type, scheduleTypeExecutionOrder executionOrder,
            TimeSpan startTime, TimeSpan endTime, intervalTypeHours hoursBetweenJobs, CancellationToken? cancelToken = null)
        {
            var i = new[] { new intervalType() { hours = hoursBetweenJobs, hoursSpecified = true }, };
            return await _updateSchedule(session, newName, priority, scheduleTypeFrequency.Hourly, type, executionOrder,
                startTime, endTime, i, cancelToken);

        }

        /// <summary>
        /// Not supported in Tableau.Api 1.x: throws NotSupportedException before anything is sent. Tableau updates a schedule by its id, which this method does not take, and 1.2.5 created a new schedule instead. Versions 2.3+
        /// </summary>
        /// <param name="session"></param>
        /// <param name="newName">The name to give to the schedule. This name identifies the schedule in the server environment when users select a schedule and manage schedule information.</param>
        /// <param name="newPriority">An integer value between 1 and 100 that determines the default priority of the schedule if multiple tasks are pending in the queue. Higher numbers have higher priority.</param>
        /// <param name="type">Extract to create an extract refresh schedule, or Subscription to create a subscription schedule.</param>
        /// <param name="executionOrder">Parallel to allow jobs associated with this schedule to run at the same time, or Serial to require the jobs to run one after the other.</param>
        /// <param name="startTime">The time of day on which scheduled jobs should run (or if the frequency is hourly, on which they should start being run), in the format HH:MM:SS (for example, 18:30:00). </param>
        /// <param name="cancelToken"></param>
        /// <returns></returns>
        ///  <exception cref="TableauRequestException"></exception>
        /// <exception cref="TableauApiVersionException"></exception>
        /// <exception cref="TableauOnlineNotSupportedException"></exception>
        /// <exception cref="NotSupportedException">Always, before anything is sent.</exception>
        [ApiVersion(2, 3)]
        [OnPremiseOnly]
        public async Task<scheduleType> UpdateToDailySchedule(TableauSession session, string newName, int newPriority,
            scheduleTypeType type, scheduleTypeExecutionOrder executionOrder,
            TimeSpan startTime, CancellationToken? cancelToken = null)
        {

            return await _updateSchedule(session, newName, newPriority, scheduleTypeFrequency.Daily, type, executionOrder,
                startTime, null, new intervalType[0], cancelToken);

        }
        /// <summary>
        /// Not supported in Tableau.Api 1.x: throws NotSupportedException before anything is sent. Tableau updates a schedule by its id, which this method does not take, and 1.2.5 created a new schedule instead. Versions 2.3+
        /// </summary>
        /// <param name="session"></param>
        /// <param name="newName">The name to give to the schedule. This name identifies the schedule in the server environment when users select a schedule and manage schedule information.</param>
        /// <param name="newPriority">An integer value between 1 and 100 that determines the default priority of the schedule if multiple tasks are pending in the queue. Higher numbers have higher priority.</param>
        /// <param name="type">Extract to create an extract refresh schedule, or Subscription to create a subscription schedule.</param>
        /// <param name="executionOrder">Parallel to allow jobs associated with this schedule to run at the same time, or Serial to require the jobs to run one after the other.</param>
        /// <param name="dayToRun">The day of week to run on</param>
        /// <param name="cancelToken"></param>
        /// <returns></returns>
        ///  <exception cref="TableauRequestException"></exception>
        /// <exception cref="TableauApiVersionException"></exception>
        /// <exception cref="TableauOnlineNotSupportedException"></exception>
        /// <exception cref="NotSupportedException">Always, before anything is sent.</exception>
        [ApiVersion(2, 3)]
        [OnPremiseOnly]
        public async Task<scheduleType> UpdateToWeeklySchedule(TableauSession session, string newName, int newPriority,
            scheduleTypeType type, scheduleTypeExecutionOrder executionOrder,
            TimeSpan startTime, intervalTypeWeekDay dayToRun, CancellationToken? cancelToken = null)
        {

            return await _updateSchedule(session, newName, newPriority, scheduleTypeFrequency.Weekly, type, executionOrder,
                startTime, null, new[] { new intervalType() { weekDay = dayToRun, weekDaySpecified = true }, }, cancelToken);

        }
        /// <summary>
        /// Not supported in Tableau.Api 1.x: throws NotSupportedException before anything is sent. Tableau updates a schedule by its id, which this method does not take, and 1.2.5 created a new schedule instead. Versions 2.3+
        /// </summary>
        /// <param name="session"></param>
        /// <param name="newName">The name to give to the schedule. This name identifies the schedule in the server environment when users select a schedule and manage schedule information.</param>
        /// <param name="newPriority">An integer value between 1 and 100 that determines the default priority of the schedule if multiple tasks are pending in the queue. Higher numbers have higher priority.</param>
        /// <param name="type">Extract to create an extract refresh schedule, or Subscription to create a subscription schedule.</param>
        /// <param name="executionOrder">Parallel to allow jobs associated with this schedule to run at the same time, or Serial to require the jobs to run one after the other.</param>
        /// <param name="dayToRun">Either the day of the month (1, 2, etc.) or LastDay.</param>
        /// <param name="cancelToken"></param>
        /// <returns></returns>
        ///  <exception cref="TableauRequestException"></exception>
        /// <exception cref="TableauApiVersionException"></exception>
        /// <exception cref="TableauOnlineNotSupportedException"></exception>
        /// <exception cref="NotSupportedException">Always, before anything is sent.</exception>
        [ApiVersion(2, 3)]
        [OnPremiseOnly]
        public async Task<scheduleType> UpdateToMonthlySchedule(TableauSession session, string newName, int newPriority,
            scheduleTypeType type, scheduleTypeExecutionOrder executionOrder,
            TimeSpan startTime, string dayToRun, CancellationToken? cancelToken = null)
        {

            return await _updateSchedule(session, newName, newPriority, scheduleTypeFrequency.Monthly, type, executionOrder,
                startTime, null, new intervalType[0], cancelToken);

        }
        /// <summary>
        /// Deletes the specified schedule. Versions 2.3+
        /// </summary>
        /// <param name="session"></param>
        /// <param name="scheduleId">The ID of the schedule to delete.</param>
        /// <param name="cancelToken"></param>
        /// <returns></returns>
        ///  <exception cref="TableauRequestException"></exception>
        /// <exception cref="TableauApiVersionException"></exception>
        /// <exception cref="TableauOnlineNotSupportedException"></exception>
        [ApiVersion(2, 3)]
        [OnPremiseOnly]
        public async Task DeleteSchedule(TableauSession session, string scheduleId, CancellationToken? cancelToken = null)
        {
            checkEndpointAvailability();
            checkNullParameters((nameof(session), session), (nameof(scheduleId), scheduleId));
            await apiRequest(buildUri($"schedules/{scheduleId}"), HttpMethod.Delete, 204,
                cancelToken, session);
        }
    }

}
