using System;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Tableau.Api.Models;
using Tableau.Api.Tests.Fakes;

namespace Tableau.Api.Tests;

/// <summary>Schedules: created on 201 with their times as HH:mm:ss, checked times, monthDay; updates refused.</summary>
[TestClass]
public sealed class ScheduleTests
{
    private const string SchedulesPath = "/api/" + Fixtures.ApiVersion + "/schedules";
    private const string Created =
        "<schedule id=\"sch1\" name=\"Nightly\" state=\"Active\" priority=\"50\" type=\"Extract\" frequency=\"Daily\""
        + " executionOrder=\"Parallel\"><frequencyDetails start=\"18:30:00\"><intervals/></frequencyDetails></schedule>";

    // XmlSerializer wrote the machine's offset with the time, so the test runs under one that is not UTC.
    [TestMethod]
    [DoNotParallelize]
    public async Task ADailyScheduleIsCreatedOn201WithItsTimeAsWritten()
    {
        var offset = new TimeSpan(5, 30, 0);
        using var zone = LocalTimeZone.Set(TimeZoneInfo.CreateCustomTimeZone("Tableau.Api.Tests", offset, "UTC+05:30", "UTC+05:30"));
        Assert.AreEqual(offset, TimeZoneInfo.Local.GetUtcOffset(new DateTime(2000, 1, 1)), "the zone is not UTC");
        using var server = FakeTableauServer.Start();
        var client = Fixtures.Client(server);
        server.Map("POST", SchedulesPath, FakeResponse.Xml(201, Created));

        var schedule = await client.CreateDailySchedule(Fixtures.Session(), "Nightly", 50, scheduleTypeType.Extract,
            scheduleTypeExecutionOrder.Parallel, new TimeSpan(18, 30, 0));

        Assert.AreEqual("sch1", schedule.id);
        var sent = Schedule(server.Requests.Single().BodyText);
        Assert.AreEqual("Nightly", (string?)sent.Attribute("name"));
        Assert.AreEqual("Daily", (string?)sent.Attribute("frequency"));
        var details = FrequencyDetails(sent);
        Assert.AreEqual("18:30:00", (string?)details.Attribute("start"));
        Assert.IsNull(details.Attribute("end"));
    }

    [TestMethod]
    public async Task AnHourlyScheduleSendsItsStartAndEnd()
    {
        using var server = FakeTableauServer.Start();
        var client = Fixtures.Client(server);
        server.Map("POST", SchedulesPath, FakeResponse.Xml(201, Created));

        await client.CreateHourlySchedule(Fixtures.Session(), "Every two hours", 50, scheduleTypeType.Extract,
            scheduleTypeExecutionOrder.Serial, new TimeSpan(6, 0, 0), new TimeSpan(23, 30, 0), intervalTypeHours.Item2);

        var details = FrequencyDetails(Schedule(server.Requests.Single().BodyText));
        Assert.AreEqual("06:00:00", (string?)details.Attribute("start"));
        Assert.AreEqual("23:30:00", (string?)details.Attribute("end"));
        Assert.AreEqual("2", (string?)details.Descendants().Single(e => e.Name.LocalName == "interval").Attribute("hours"));
    }

    [TestMethod]
    public async Task AMonthlyScheduleSendsItsDayOfTheMonth()
    {
        using var server = FakeTableauServer.Start();
        var client = Fixtures.Client(server);
        server.Map("POST", SchedulesPath, FakeResponse.Xml(201, Created));

        await client.CreateMonthlySchedule(Fixtures.Session(), "Month end", 50, scheduleTypeType.Extract,
            scheduleTypeExecutionOrder.Parallel, new TimeSpan(1, 0, 0), "LastDay");

        var interval = FrequencyDetails(Schedule(server.Requests.Single().BodyText)).Descendants()
            .Single(e => e.Name.LocalName == "interval");
        Assert.AreEqual("LastDay", (string?)interval.Attribute("monthDay"));
    }

    [TestMethod]
    [DataRow(24 * 60)]
    [DataRow(25 * 60)]
    [DataRow(-1)]
    public async Task AStartTimeOutsideADayIsRefusedBeforeSending(int minutes)
    {
        using var server = FakeTableauServer.Start();
        var client = Fixtures.Client(server);

        var thrown = await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(() => client.CreateDailySchedule(
            Fixtures.Session(), "Nightly", 50, scheduleTypeType.Extract, scheduleTypeExecutionOrder.Parallel,
            TimeSpan.FromMinutes(minutes)));

        Assert.AreEqual("startTime", thrown.ParamName);
        Assert.IsEmpty(server.Requests);
    }

    [TestMethod]
    public async Task AnEndTimeOfMidnightIsRefusedBeforeSending()
    {
        using var server = FakeTableauServer.Start();
        var client = Fixtures.Client(server);

        var thrown = await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(() => client.CreateHourlySchedule(
            Fixtures.Session(), "Hourly", 50, scheduleTypeType.Extract, scheduleTypeExecutionOrder.Parallel,
            new TimeSpan(6, 0, 0), TimeSpan.FromHours(24), intervalTypeHours.Item1));

        Assert.AreEqual("endTime", thrown.ParamName);
        Assert.IsEmpty(server.Requests);
    }

    [TestMethod]
    public async Task ASchedulesResponseStillFillsStartAndEnd()
    {
        using var server = FakeTableauServer.Start();
        var client = Fixtures.Client(server);
        server.Map("GET", SchedulesPath, FakeResponse.Xml(200,
            "<pagination pageNumber=\"1\" pageSize=\"100\" totalAvailable=\"1\"/><schedules>"
            + "<schedule id=\"sch2\" name=\"Every two hours\" frequency=\"Hourly\"><frequencyDetails start=\"06:00:00\""
            + " end=\"23:30:00\"><intervals><interval hours=\"2\"/></intervals></frequencyDetails></schedule></schedules>"));

        var (_, schedules) = await client.QuerySchedules(Fixtures.Session());

        var details = schedules.Single().frequencyDetails;
        Assert.AreEqual(new TimeSpan(6, 0, 0), details.start.TimeOfDay);
        Assert.IsTrue(details.endSpecified);
        Assert.AreEqual(new TimeSpan(23, 30, 0), details.end.TimeOfDay);
    }

    [TestMethod]
    public async Task EveryUpdateToScheduleIsRefusedBeforeSending()
    {
        using var server = FakeTableauServer.Start();
        var client = Fixtures.Client(server);
        var session = Fixtures.Session();
        var start = new TimeSpan(18, 30, 0);
        const scheduleTypeType extract = scheduleTypeType.Extract;
        const scheduleTypeExecutionOrder parallel = scheduleTypeExecutionOrder.Parallel;

        Func<Task>[] updates =
        [
            () => client.UpdateToHourlySchedule(session, "Nightly", 50, extract, parallel, start, null, intervalTypeMinutes.Item15),
            () => client.UpdateToHourlySchedule(session, "Nightly", 50, extract, parallel, start, start, intervalTypeHours.Item1),
            () => client.UpdateToDailySchedule(session, "Nightly", 50, extract, parallel, start),
            () => client.UpdateToWeeklySchedule(session, "Nightly", 50, extract, parallel, start, intervalTypeWeekDay.Monday),
            () => client.UpdateToMonthlySchedule(session, "Nightly", 50, extract, parallel, start, "LastDay"),
        ];
        foreach (var update in updates)
            await Assert.ThrowsExactlyAsync<NotSupportedException>(update);

        Assert.IsEmpty(server.Requests);
    }

    private static XElement Schedule(string body) =>
        XElement.Parse(body).Elements().Single(e => e.Name.LocalName == "schedule");

    private static XElement FrequencyDetails(XElement schedule) =>
        schedule.Elements().Single(e => e.Name.LocalName == "frequencyDetails");
}
