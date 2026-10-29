using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Tableau.Api.Models;
using Tableau.Api.Tests.Fakes;

namespace Tableau.Api.Tests;

/// <summary>
/// Every public method of the client, driven with each of its string parameters set to a marker: wherever the marker
/// reaches a request path, it is one whole segment, and the same parameter set to ".." is refused before sending.
/// The methods and their path parameters are found by calling them, so a new one is covered without a list.
/// </summary>
[TestClass]
public sealed class EveryMethodPathTests
{
    // Characters a path must escape, a non-ASCII one, and no : or , so a filter keeps it whole.
    private const string Marker = "Mk/?#%\\ é;&=+$[]";

    [TestMethod]
    public async Task EveryValueInARequestPathIsOneSegmentAndDotsAreRefused()
    {
        using var server = FakeTableauServer.Start();
        var client = Fixtures.Client(server);
        var session = Fixtures.Session();
        var failures = new List<string>();
        var silent = new List<string>();
        var pathParameters = new List<string>();

        foreach (var method in PublicMethods())
        {
            var before = server.Requests.Count;
            await InvokeAsync(client, session, method, null, null);
            if (server.Requests.Count == before) silent.Add(Name(method));

            foreach (var parameter in method.GetParameters().Where(parameter => parameter.ParameterType == typeof(string)))
            {
                before = server.Requests.Count;
                await InvokeAsync(client, session, method, parameter, Marker);
                var sent = server.Requests.Skip(before).ToList();
                var inPath = false;
                foreach (var request in sent)
                {
                    var segments = PathRows.Segments(request.Path);
                    foreach (var segment in segments.Where(segment => segment.Contains("Mk")))
                    {
                        if (segment == Marker) inPath = true;
                        else failures.Add($"{Name(method)}({parameter.Name}): the value is split in {request.RequestLine}");
                    }
                }
                if (!inPath) continue;
                pathParameters.Add($"{Name(method)}({parameter.Name})");

                before = server.Requests.Count;
                var thrown = await InvokeAsync(client, session, method, parameter, "..");
                if (thrown is not ArgumentException || server.Requests.Count != before)
                    failures.Add($"{Name(method)}({parameter.Name}) = \"..\": expected ArgumentException and no request; got "
                        + $"{thrown?.GetType().Name ?? "no exception"} and {server.Requests.Count - before} request(s)");
            }
        }

        Assert.IsEmpty(failures, string.Join("\n", failures));
        // Every method sends a request with plain values, so none is skipped unseen.
        Assert.IsEmpty(silent, "methods that sent nothing: " + string.Join(", ", silent));
        // 1.2.5's methods put this many string parameters in request paths; fewer would mean some went unseen.
        Assert.HasCount(122, pathParameters, string.Join("\n", pathParameters));
    }

    private static IEnumerable<MethodInfo> PublicMethods() =>
        typeof(TableauApiClient).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(method => typeof(Task).IsAssignableFrom(method.ReturnType))
            .OrderBy(method => method.ToString(), StringComparer.Ordinal);

    private static string Name(MethodInfo method) =>
        $"{method.Name}({string.Join(", ", method.GetParameters().Select(parameter => parameter.ParameterType.Name))})";

    /// <summary>Calls the method with a plain value for every parameter, or value for the one named; returns what it threw.</summary>
    private static async Task<Exception?> InvokeAsync(TableauApiClient client, TableauSession session, MethodInfo method,
        ParameterInfo? set, string? value)
    {
        var arguments = method.GetParameters()
            .Select(parameter => parameter == set ? value : Plain(parameter, session))
            .ToArray();
        try
        {
            await (Task)method.Invoke(client, arguments)!;
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

    private static object? Plain(ParameterInfo parameter, TableauSession session)
    {
        var type = parameter.ParameterType;
        if (type == typeof(TableauSession)) return session;
        if (type == typeof(string)) return "v" + parameter.Position;
        if (type == typeof(int)) return 1;
        if (type == typeof(bool)) return false;
        if (type == typeof(TimeSpan)) return TimeSpan.FromHours(1);
        if (type == typeof(Stream)) return new MemoryStream([1, 2, 3]);
        if (type.IsEnum) return Enum.GetValues(type).GetValue(0);
        if (Nullable.GetUnderlyingType(type) is not null) return null;
        if (type.IsArray)
        {
            var array = Array.CreateInstance(type.GetElementType()!, 1);
            array.SetValue(Element(type.GetElementType()!, parameter.Position), 0);
            return array;
        }
        if (type.IsClass && type.GetConstructor(Type.EmptyTypes) is not null) return Activator.CreateInstance(type);
        throw new NotSupportedException($"No plain value for {parameter.Member.Name}'s {type.Name} {parameter.Name}");
    }

    private static object? Element(Type type, int position)
    {
        if (type == typeof(string)) return "v" + position;
        if (type == typeof(ValueTuple<string, string>)) return ("f" + position, "v" + position);
        return Activator.CreateInstance(type);
    }
}
