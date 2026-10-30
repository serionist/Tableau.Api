using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Tableau.Api.Models;
using Tableau.Api.Tests.Fakes;
using Tableau.Api.VizPortal;

namespace Tableau.Api.Tests;

/// <summary>
/// Every public method of the Vizportal client, called with an invented value for each of its parameters, sends the
/// requests its row below expects: each Vizportal method by name, with the params it names, every id and name where
/// it goes. Each request posts to the path its body's method names, with the session's cookie and the XSRF header that
/// repeats its cookie. The methods are found by reflection, and a method without a row, or a row without a method,
/// fails the test. The server answers with an empty result, and with one schedule and a data source that has
/// extracts, so the methods that look first send their second request too.
/// </summary>
[TestClass]
public sealed class EveryVizportalMethodTests
{
    // Each int parameter is 4100 plus its position, each string Inv, its position and its name, each bool true.
    private static readonly Dictionary<string, string[]> _expected = new(StringComparer.Ordinal)
    {
        ["AddTagToDatasource"] = ["""addTagsToDatasources {"ids":["4101"],"tags":["\"Inv2tag\""]}"""],
        ["AddTagToWorkbook"] = ["""addTagsToWorkbooks {"ids":["4101"],"tags":["\"Inv2tag\""]}"""],
        ["AddUserToGroup"] =
            ["""updateUsersGroupMembership {"addToGroupIds":["4101"],"removeFromGroupIds":[],"userIds":["4102"]}"""],
        ["ChangeDatasourceName"] = ["""renameDatasource {"id":"4101","name":"Inv2newName"}"""],
        ["ChangeDatasourceOwner"] = ["""setDatasourcesOwner {"ids":["4101"],"ownerId":4102}"""],
        ["ChangeExtractRefreshScheduleFrequency"] =
            ["""setExtractTasksSchedule {"ids":["4101"],"scheduleId":"4102"}"""],
        ["ChangeProjectName"] = ["""setProjectName {"id":"4101","name":"Inv2newName"}"""],
        ["ChangeProjectOwner"] = ["""setProjectsOwner {"ids":["4101"],"ownerId":4102}"""],
        ["ChangeSubscriptionFrequency"] =
            ["""setSubscriptionsSchedule {"ids":["4101"],"scheduleId":"4102","subscribeOnExtractRefresh":true}"""],
        ["ChangeUserRole"] = ["""updateUsersSiteRole {"siteRole":"Inv2role","userIds":["4101"]}"""],
        ["ChangeWorkbookName"] = ["""renameWorkbook {"id":"4101","name":"Inv2newName"}"""],
        ["ChangeWorkbookOwner"] = ["""setWorkbooksOwner {"ids":["4101"],"ownerId":4102}"""],
        ["DeleteDatasource"] = ["""deleteDatasources {"ids":["4101"]}"""],
        ["DeleteExtractRefresh"] = ["""deleteExtractTasks {"ids":["4101"]}"""],
        ["DeleteGroup"] = ["""deleteGroups {"ids":["4101"]}"""],
        ["DeleteProject"] = ["""deleteProjects {"ids":["4101"]}"""],
        ["DeleteSubscription"] = ["""deleteSubscriptions {"ids":["4101"]}"""],
        ["DeleteUser"] = ["""deleteUsers {"ids":["4101"]}"""],
        ["DeleteWorkbook"] = ["""deleteWorkbooks {"ids":["4101"]}"""],
        ["MoveDatasource"] = [Move("datasource")],
        ["MoveProject"] = [Move("project")],
        ["MoveWorkbook"] = [Move("workbook")],
        ["QueryDatasources"] =
            [Query("getDatasources", "name", """{"field":"isPublished","operator":"eq","value":true}""")],
        ["QueryExtractRefreshesForDatasource"] =
        [
            """getDatasource {"id":"Inv1datasourceId"}""",
            Query("getExtractTasks", "scheduleName", Eq("datasourceId", "Inv1datasourceId")),
        ],
        ["QueryExtractRefreshesForSchedule"] =
        [
            ScheduleCheck,
            Query("getExtractTasks", "scheduleName", Eq("scheduleId", "Inv2scheduleId"), Eq("siteId", "Inv1siteId")),
        ],
        ["QueryExtractRefreshesForWorkbook"] =
            [Query("getExtractTasks", "scheduleName", Eq("workbookId", "Inv1workbookId"))],
        ["QueryGroups"] = [Query("getGroups", "name")],
        ["QueryGroupUsers"] =
            [Query("getSiteUsers", "displayName", """{"field":"groupIds","operator":"has","value":"4101"}""")],
        ["QueryProjects"] = [Query("getProjects", "name")],
        ["QuerySchedules"] = [Query("getSchedules", "name", Eq("siteId", "Inv1siteId"))],
        ["QuerySingleSchedule"] =
            [Query("getSchedules", "name", Eq("siteId", "Inv1siteId"), Eq("id", "Inv2scheduleId"))],
        ["QuerySubscriptionsForSchedule"] =
        [
            ScheduleCheck,
            Query("getSubscriptions", "subject", Eq("siteId", "Inv1siteId"), Eq("scheduleId", "Inv2scheduleId")),
        ],
        ["QuerySubscriptionsForWorkbook"] = [Query("getSubscriptions", "subject", Eq("workbookId", "Inv1workbookId"))],
        ["QueryUsers"] =
        [
            """
            getSiteUsersWithToken {"filter":null,"order":[{"field":"displayName","ascending":true}],"page":null,
             "pageWithToken":{"size":100,"token":null}}
            """,
        ],
        ["QueryWorkbooks"] = [Query("getWorkbooks", "name")],
        ["RemoveTagFromDatasource"] = ["""removeTagsFromDatasources {"ids":["4101"],"tags":["\"Inv2tag\""]}"""],
        ["RemoveTagFromWorkbook"] = ["""removeTagsFromWorkbooks {"ids":["4101"],"tags":["\"Inv2tag\""]}"""],
        ["RemoveUserFromGroup"] =
            ["""updateUsersGroupMembership {"addToGroupIds":[],"removeFromGroupIds":["4101"],"userIds":["4102"]}"""],
        ["TriggerExtractRefresh"] = ["""runExtractTasks {"ids":["4101"]}"""],
        ["TriggerSchedule"] = ["""runSchedules {"ids":["4101"]}"""],
        ["UserGroupMemberships"] = ["""getUsersGroupMembership {"userIds":["4101"]}"""],
    };

    // The check that the schedule exists, before the schedule's extract refreshes or subscriptions are read. Unlike
    // every other request it goes out in PascalCase, a known fault this row pins until the Vizportal client is rebuilt.
    private const string ScheduleCheck = """
        getSchedules {"Filter":{"Clauses":[{"Field":"siteId","Operator":"eq","Value":"Inv1siteId"},
         {"Field":"id","Operator":"eq","Value":"Inv2scheduleId"}],"Operator":"and"},"Order":null,
         "Page":{"StartIndex":0,"MaxItems":1},"PageWithToken":null}
        """;

    private static readonly MethodInfo _drain =
        typeof(EveryVizportalMethodTests).GetMethod(nameof(DrainAsync), BindingFlags.NonPublic | BindingFlags.Static)!;

    [TestMethod]
    public async Task EveryMethodSendsWhatItsRowExpectsWithTheSession()
    {
        using var server = FakeTableauServer.StartTls();
        var client = Fixtures.Client(server);
        // Vizportal's cookies are marked Secure, so they go only over HTTPS, to a certificate trusted nowhere here.
        client.IgnoreSSLErrors = true;
        server.MapPrefix("POST", Fixtures.VizportalPath, Fixtures.VizportalResult());
        server.Map("POST", Fixtures.VizportalPath + "getSchedules",
            FakeResponse.Json(200, """{"result":{"schedules":[{"id":"7"}],"moreItems":false}}"""));
        server.Map("POST", Fixtures.VizportalPath + "getDatasource",
            FakeResponse.Json(200, """{"result":{"hasExtracts":true}}"""));
        var failures = new List<string>();
        var methods = PublicMethods().ToList();

        foreach (var method in methods)
        {
            var before = server.Requests.Count;
            var arguments = method.GetParameters().Select(Invented).ToArray();
            if (await InvokeAsync(client.VizPortal, method, arguments) is { } thrown)
                failures.Add($"{method.Name}: {thrown.GetType().Name}: {thrown.Message}");
            var sent = new List<string>();
            foreach (var request in server.Requests.Skip(before))
            {
                var name = request.Path[Fixtures.VizportalPath.Length..];
                var body = JsonNode.Parse(request.BodyText)!.AsObject();
                if (request.Method != "POST" || (string?)body["method"] != name)
                    failures.Add($"{method.Name}: {request.RequestLine} with body {request.BodyText}");
                sent.Add($"{name} {body["params"]?.ToJsonString()}");
                if (!(request.Header("Content-Type") ?? "").StartsWith("application/json", StringComparison.Ordinal))
                    failures.Add($"{method.Name}: Content-Type {request.Header("Content-Type")}");
                var cookies = (request.Header("Cookie") ?? "").Split(';').Select(part => part.Trim()).ToList();
                if (!cookies.Contains($"workgroup_session_id={Fixtures.SessionToken}"))
                    failures.Add($"{method.Name}: no session cookie in '{request.Header("Cookie")}'");
                if (!cookies.Contains($"XSRF-TOKEN={request.Header("X-Xsrf-Token")}"))
                    failures.Add($"{method.Name}: X-Xsrf-Token doesn't repeat its cookie: {request.Header("Cookie")}");
            }
            if (!_expected.TryGetValue(method.Name, out var expected))
                failures.Add($"{method.Name}: no row; it sent:\n  {string.Join("\n  ", sent)}");
            else if (!SameRequests(expected, sent))
            {
                failures.Add($"{method.Name}: expected\n  {string.Join("\n  ", expected)}\n"
                    + $"sent\n  {string.Join("\n  ", sent)}");
            }
        }

        Assert.IsEmpty(failures, string.Join("\n", failures));
        CollectionAssert.AreEquivalent(_expected.Keys.ToArray(), methods.Select(method => method.Name).ToArray(),
            "a row for each public method, and a method for each row");
    }

    // Each request is the Vizportal method's name, a space, and its params, compared as JSON, so the order of keys and
    // the whitespace don't count.
    private static bool SameRequests(string[] expected, List<string> sent) =>
        expected.Length == sent.Count && expected.Zip(sent).All(pair =>
        {
            var (want, got) = (Split(pair.First), Split(pair.Second));
            return want.Name == got.Name
                && JsonNode.DeepEquals(JsonNode.Parse(want.Params), JsonNode.Parse(got.Params));
        });

    private static (string Name, string Params) Split(string request)
    {
        var space = request.IndexOf(' ', StringComparison.Ordinal);
        return (request[..space], request[(space + 1)..]);
    }

    // A query: its clauses joined by "and", or no filter; sorted ascending by one field; the first page of 100.
    private static string Query(string name, string order, params string[] clauses)
    {
        var filter = clauses.Length == 0 ? "null" : $$"""{"clauses":[{{string.Join(",", clauses)}}],"operator":"and"}""";
        return $$"""
            {{name}} {"filter":{{filter}},"order":[{"field":"{{order}}","ascending":true}],
             "page":{"startIndex":0,"maxItems":100},"pageWithToken":null}
            """;
    }

    private static string Eq(string field, string value) =>
        $$"""{"field":"{{field}}","operator":"eq","value":"{{value}}"}""";

    // A move of the content 4102 into the project 4101.
    private static string Move(string contentType) =>
        $$"""
        moveContentToContainer {"containerId":"4101","containerType":"PROJECT",
         "contentItems":[{"contentId":"4102","contentType":"{{contentType}}","moveType":null}]}
        """;

    private static IEnumerable<MethodInfo> PublicMethods() =>
        typeof(VizPortalClient).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(method => !method.IsGenericMethodDefinition)
            .OrderBy(method => method.ToString(), StringComparer.Ordinal);

    // Each parameter its own value, so a value found in a request is that parameter's.
    private static object? Invented(ParameterInfo parameter)
    {
        var type = parameter.ParameterType;
        if (type == typeof(TableauSession)) return Fixtures.Session();
        if (type == typeof(int)) return 4100 + parameter.Position;
        if (type == typeof(string)) return $"Inv{parameter.Position}{parameter.Name}";
        if (type == typeof(bool)) return true;
        throw new NotSupportedException($"No value for {parameter.Member.Name}'s {type.Name} {parameter.Name}");
    }

    /// <summary>Calls the method, reading every item of a list it returns; returns what it threw.</summary>
    private static async Task<Exception?> InvokeAsync(VizPortalClient client, MethodInfo method, object?[] arguments)
    {
        try
        {
            var returned = method.Invoke(client, arguments);
            if (returned is Task task) await task;
            else
            {
                var item = method.ReturnType.GetGenericArguments().Single();
                await (Task)_drain.MakeGenericMethod(item).Invoke(null, [returned])!;
            }
            return null;
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            return ex.InnerException;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    private static async Task DrainAsync<T>(IAsyncEnumerable<T> items)
    {
        await foreach (var _ in items)
        {
        }
    }
}
