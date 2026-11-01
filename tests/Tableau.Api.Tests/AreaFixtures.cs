using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Tableau.Api.Tests.Fakes;

namespace Tableau.Api.Tests;

/// <summary>
/// The 2.0 client's areas against the fake server: a client signed in to the invented site with a personal access token,
/// and the calls it sent after its sign-in, as Tableau would receive them.
/// </summary>
internal static class AreaFixtures
{
    public const string SitesPath = "/api/" + ClientFixtures.Version + "/sites";

    /// <summary>The invented site's path, under which every area's calls go.</summary>
    public const string SitePath = SitesPath + "/" + Fixtures.SiteId;

    public const string OtherUserId = "8d2e3f4a-5b6c-4d7e-9f0a-1b2c3d4e5f6a";
    public const string GroupId = "4a1b2c3d-0002-4e5f-8a9b-0c1d2e3f4a5b";
    public const string ProjectId = "33333333-4444-4555-8666-777777777777";
    public const string ItemId = "6c3d4e5f-0001-4a7b-8c9d-0e1f2a3b4c5d";

    /// <summary>A fake server that answers the client's sign-in to the invented site.</summary>
    public static FakeTableauServer Server()
    {
        var server = FakeTableauServer.Start();
        SessionFixtures.MapSignIns(server, Fixtures.SessionToken);
        return server;
    }

    /// <summary>A client with a personal access token, on the deployment given, at REST API 3.25.</summary>
    public static TableauClient Client(FakeTableauServer server, TableauDeployment deployment = TableauDeployment.Server)
    {
        var options = SessionFixtures.Options(server);
        options.Deployment = deployment;
        return SessionFixtures.Client(options, SessionFixtures.Pat());
    }

    /// <summary>The requests the client sent after signing in, in order.</summary>
    public static RecordedRequest[] Calls(FakeTableauServer server) =>
        [.. server.Requests.Where(request => request.Path != SessionFixtures.SignInPath)];

    /// <summary>
    /// Asserts the client sent one call after signing in: its method and target, path and query as sent, its body, or
    /// none, and the session's token.
    /// </summary>
    /// <param name="server">The fake server.</param>
    /// <param name="line">The method and the target.</param>
    /// <param name="body">The JSON body: its members in their order, each value of its type, escapes aside.</param>
    public static RecordedRequest Sent(FakeTableauServer server, string line, string? body = null)
    {
        var calls = Calls(server);
        Assert.HasCount(1, calls, string.Join("\n", calls.Select(call => call.RequestLine)));
        var call = calls[0];
        Assert.AreEqual(line, $"{call.Method} {call.Target}");
        Assert.AreEqual(Fixtures.SessionToken, call.Header("X-Tableau-Auth"));
        if (body is null)
        {
            Assert.IsEmpty(call.Body);
        }
        else
        {
            Assert.AreEqual(Json(body), Json(call.BodyText));
            Assert.AreEqual("application/json", call.Header("Content-Type"));
        }
        return call;
    }

    /// <summary>
    /// A JSON body written one way, so two compare by their members, their order and their values' types, whichever
    /// characters each escaped.
    /// </summary>
    public static string Json(string body) => JsonNode.Parse(body)!.ToJsonString();

    /// <summary>The bodies of the calls after the sign-in, each written as <see cref="Json"/> writes it.</summary>
    public static string[] Bodies(FakeTableauServer server) => [.. Calls(server).Select(call => Json(call.BodyText))];

    /// <summary>Asserts a call is refused before anything at all is sent, the sign-in included.</summary>
    public static async Task<T> RefusedAsync<T>(FakeTableauServer server, Func<Task> call)
        where T : Exception
    {
        var thrown = await Assert.ThrowsAsync<T>(call);
        Assert.IsEmpty(server.Requests, string.Join("\n", server.Requests.Select(request => request.RequestLine)));
        return thrown;
    }

    /// <summary>
    /// A page of a list, as Tableau answers one: <paramref name="count"/> items of <paramref name="element"/> in the wrapper
    /// <paramref name="list"/>, each with an id and a name that number them across pages.
    /// </summary>
    public static FakeResponse Page(string list, string element, int pageNumber, int pageSize, int total, int count)
    {
        var items = string.Join(',', Enumerable.Range((pageNumber - 1) * pageSize, count).Select(i =>
            $$"""{"id":"00000000-0000-4000-8000-{{i.ToString("x12", System.Globalization.CultureInfo.InvariantCulture)}}","name":"n{{i}}"}"""));
        return FakeResponse.Json(200, $$$"""
            {"pagination":{"pageNumber":"{{{pageNumber}}}","pageSize":"{{{pageSize}}}","totalAvailable":"{{{total}}}"},"{{{list}}}":{"{{{element}}}":[{{{items}}}]}}
            """);
    }

    /// <summary>The page number a request asked for.</summary>
    public static int PageNumber(RecordedRequest request) =>
        int.Parse(request.QueryParameters.Single(pair => pair.Key == "pageNumber").Value, System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Every item of an async list.</summary>
    public static async Task<List<T>> AllAsync<T>(IAsyncEnumerable<T> items)
    {
        var list = new List<T>();
        await foreach (var item in items)
            list.Add(item);
        return list;
    }
}
