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
/// Every public method of the Vizportal client, called with an invented value for each of its parameters: each posts
/// to the Vizportal path that its body's method names, with the session's cookie and the XSRF header that repeats its
/// cookie, and every id and name the caller gave reaches a request. The methods are found by reflection, so a new one
/// is covered without a list. The server answers every call with an empty result, which each method takes as success
/// or as nothing found.
/// </summary>
[TestClass]
public sealed class EveryVizportalMethodTests
{
    private static readonly MethodInfo _drain =
        typeof(EveryVizportalMethodTests).GetMethod(nameof(DrainAsync), BindingFlags.NonPublic | BindingFlags.Static)!;

    [TestMethod]
    public async Task EveryMethodPostsItsNameWithTheSessionAndEveryValue()
    {
        using var server = FakeTableauServer.StartTls();
        var client = Fixtures.Client(server);
        // Vizportal's cookies are marked Secure, so they go only over HTTPS, to a certificate trusted nowhere here.
        client.IgnoreSSLErrors = true;
        server.MapPrefix("POST", Fixtures.VizportalPath, Fixtures.VizportalResult());
        var failures = new List<string>();
        var methods = PublicMethods().ToList();

        foreach (var method in methods)
        {
            var before = server.Requests.Count;
            var arguments = method.GetParameters().Select(Invented).ToArray();
            if (await InvokeAsync(client.VizPortal, method, arguments) is { } thrown)
                failures.Add($"{method.Name}: {thrown.GetType().Name}: {thrown.Message}");
            var sent = server.Requests.Skip(before).ToList();
            if (sent.Count == 0)
            {
                failures.Add($"{method.Name}: sent nothing");
                continue;
            }
            var parameters = new List<string>();
            foreach (var request in sent)
            {
                var name = request.Path[Fixtures.VizportalPath.Length..];
                var body = JsonNode.Parse(request.BodyText)!.AsObject();
                if (request.Method != "POST" || (string?)body["method"] != name || body["params"] is not JsonObject)
                    failures.Add($"{method.Name}: {request.RequestLine} with body {request.BodyText}");
                parameters.Add(body["params"]?.ToJsonString() ?? "");
                if (!(request.Header("Content-Type") ?? "").StartsWith("application/json", StringComparison.Ordinal))
                    failures.Add($"{method.Name}: Content-Type {request.Header("Content-Type")}");
                var cookies = (request.Header("Cookie") ?? "").Split(';').Select(part => part.Trim()).ToList();
                if (!cookies.Contains($"workgroup_session_id={Fixtures.SessionToken}"))
                    failures.Add($"{method.Name}: no session cookie in '{request.Header("Cookie")}'");
                if (!cookies.Contains($"XSRF-TOKEN={request.Header("X-Xsrf-Token")}"))
                    failures.Add($"{method.Name}: X-Xsrf-Token doesn't repeat its cookie: {request.Header("Cookie")}");
            }
            foreach (var (parameter, value) in method.GetParameters().Zip(arguments))
            {
                if (value is not (int or string)) continue;
                if (!parameters.Any(text => text.Contains(value.ToString()!, StringComparison.Ordinal)))
                    failures.Add($"{method.Name}({parameter.Name}): {value} reached no request");
            }
        }

        Assert.IsEmpty(failures, string.Join("\n", failures));
        // The client's public methods, so a change that drops one from the reflection shows here.
        Assert.HasCount(41, methods, string.Join(", ", methods.Select(method => method.Name)));
    }

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
