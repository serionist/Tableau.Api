using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tableau.Api.Tests;

/// <summary>
/// The shape every public member of the 2.0 types keeps, read from the native AOT app's root list, which holds them all
/// and none of the old client's: an async method ends in Async, takes its token last as
/// <c>CancellationToken cancellationToken = default</c>, and returns a task, or an async enumerable for a list; a
/// sub-client's methods start with one of the verbs and are virtual, and a mock derives from its protected constructor.
/// </summary>
[TestClass]
public sealed class ConventionsTests
{
    /// <summary>The verbs a sub-client's methods start with.</summary>
    private static readonly string[] _verbs =
        ["ListPage", "List", "Get", "Create", "Update", "Delete", "Download", "Publish", "Run", "Cancel", "Add", "Remove"];

    /// <summary>
    /// The verbs of the sub-clients that have their own: the explicit session API's, on <see cref="SessionsClient"/>
    /// alone, a group's import from Active Directory beside the common verbs, and a Metadata API query beside them.
    /// </summary>
    private static readonly Dictionary<Type, string[]> _ownVerbs = new()
    {
        [typeof(SessionsClient)] = ["SignIn", "SwitchSite", "SignOut"],
        [typeof(GroupsClient)] = [.. _verbs, "Import"],
        [typeof(MetadataClient)] = [.. _verbs, "Query"],
    };

    private static readonly HashSet<string> _objectMembers = new(StringComparer.Ordinal)
    {
        "Equals", "GetHashCode", "ToString", "GetType", "Deconstruct", "PrintMembers", "<Clone>$", "Dispose", "DisposeAsync",
    };

    [TestMethod]
    public void TheRootListHoldsTheClientAndItsSubClients()
    {
        var types = TwoPointZeroTypes().ToList();

        CollectionAssert.IsSubsetOf(new[] { typeof(TableauClient), typeof(TableauException) }, types);
        CollectionAssert.IsSubsetOf(_subClients.ToList(), types);
        CollectionAssert.Contains(_subClients.ToList(), typeof(ServerClient));
    }

    [TestMethod]
    public void AsyncMethodsEndInAsyncAndTakeTheirTokenLast()
    {
        var problems = new List<string>();
        foreach (var method in PublicMethods())
        {
            var name = $"{method.DeclaringType!.Name}.{method.Name}";
            var async = IsAsyncShape(method.ReturnType);
            if (async != method.Name.EndsWith("Async", StringComparison.Ordinal))
                problems.Add($"{name}: an async method, and only one, ends in Async");
            var parameters = method.GetParameters();
            if (parameters.Any(parameter => parameter.ParameterType == typeof(CancellationToken?)))
                problems.Add($"{name}: takes a CancellationToken?, where the token is CancellationToken cancellationToken = default");
            var tokens = parameters.Where(parameter => parameter.ParameterType == typeof(CancellationToken)).ToList();
            if (async && method.Name != "DisposeAsync" && tokens.Count != 1)
                problems.Add($"{name}: an async method takes one CancellationToken");
            foreach (var token in tokens)
            {
                if (token.Position != parameters.Length - 1 || token.Name != "cancellationToken" || !token.HasDefaultValue)
                    problems.Add($"{name}: its token is CancellationToken cancellationToken = default, last");
                if (method.ReturnType.IsGenericType && method.ReturnType.GetGenericTypeDefinition() == typeof(IAsyncEnumerable<>)
                    && token.GetCustomAttribute<EnumeratorCancellationAttribute>() is null && method.GetCustomAttribute<AsyncIteratorStateMachineAttribute>() is not null)
                    problems.Add($"{name}: an async iterator's token is [EnumeratorCancellation]");
            }
        }
        Assert.IsEmpty(problems, string.Join("\n", problems));
    }

    [TestMethod]
    public void SubClientMethodsStartWithAVerbAndReturnTheirShape()
    {
        var problems = new List<string>();
        foreach (var method in PublicMethods().Where(method => IsSubClient(method.DeclaringType!)))
        {
            var name = $"{method.DeclaringType!.Name}.{method.Name}";
            var verbs = _ownVerbs.GetValueOrDefault(method.DeclaringType!, _verbs);
            var verb = verbs.FirstOrDefault(verb => method.Name.StartsWith(verb, StringComparison.Ordinal));
            if (verb is null)
            {
                problems.Add($"{name}: starts with none of {string.Join(", ", verbs)}");
                continue;
            }
            // A sub-client of several resources names the one a page is of: ListDatabasesPageAsync is a ListPage method.
            if (verb == "List" && method.Name.EndsWith("PageAsync", StringComparison.Ordinal))
                verb = "ListPage";
            var returns = method.ReturnType;
            var shape = verb switch
            {
                "List" => returns.IsGenericType && returns.GetGenericTypeDefinition() == typeof(IAsyncEnumerable<>),
                "ListPage" => returns.IsGenericType && returns.GetGenericTypeDefinition() == typeof(Task<>)
                    && returns.GetGenericArguments()[0].Name == "Page`1",
                _ => returns == typeof(Task) || (returns.IsGenericType && returns.GetGenericTypeDefinition() == typeof(Task<>)),
            };
            if (!shape)
                problems.Add($"{name}: a {verb} method returns {(verb == "List" ? "IAsyncEnumerable<T>" : verb == "ListPage" ? "Task<Page<T>>" : "Task or Task<T>")}");
        }
        Assert.IsEmpty(problems, string.Join("\n", problems));
    }

    [TestMethod]
    public void TheClientAndItsSubClientsAreMockable()
    {
        var problems = new List<string>();
        foreach (var type in TwoPointZeroTypes().Where(type => type == typeof(TableauClient) || IsSubClient(type)))
        {
            if (type.IsSealed) problems.Add($"{type.Name} is sealed");
            if (type.GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, Type.EmptyTypes) is not { IsFamily: true })
                problems.Add($"{type.Name} has no protected constructor without parameters");
            if (IsSubClient(type) && type.GetConstructors().Length > 0)
                problems.Add($"{type.Name} has a public constructor: the client makes its sub-clients");
            foreach (var method in type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
                         .Where(method => !_objectMembers.Contains(method.Name)))
            {
                if (!method.IsVirtual || method.IsFinal) problems.Add($"{type.Name}.{method.Name} is not virtual");
            }
        }
        Assert.IsEmpty(problems, string.Join("\n", problems));
    }

    [TestMethod]
    public async Task TheClientMakesEachSubClientOnce()
    {
        var options = new TableauClientOptions { ServerUrl = new Uri("https://tableau.example.com/") };
        using var http = new System.Net.Http.HttpClient();
        await using var client = new TableauClient(http, options);

        foreach (var property in typeof(TableauClient).GetProperties().Where(property => IsSubClient(property.PropertyType)))
            Assert.AreSame(property.GetValue(client), property.GetValue(client), property.Name);
    }

    /// <summary>The sub-clients: the types of TableauClient's public properties, whatever their names or namespaces.</summary>
    private static readonly HashSet<Type> _subClients =
        [.. typeof(TableauClient).GetProperties(BindingFlags.Instance | BindingFlags.Public).Select(property => property.PropertyType)];

    private static bool IsSubClient(Type type) => _subClients.Contains(type);

    private static bool IsAsyncShape(Type type) =>
        type == typeof(Task) || type == typeof(ValueTask)
        || (type.IsGenericType && type.GetGenericTypeDefinition() is var definition
            && (definition == typeof(Task<>) || definition == typeof(ValueTask<>) || definition == typeof(IAsyncEnumerable<>)));

    private static IEnumerable<Type> TwoPointZeroTypes()
    {
        var roots = ModelRootsTests.Roots();
        return typeof(TableauClient).Assembly.GetExportedTypes().Where(type => ModelRootsTests.Rooted(type, roots));
    }

    private static IEnumerable<MethodInfo> PublicMethods() =>
        TwoPointZeroTypes().SelectMany(type => type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public
                | BindingFlags.DeclaredOnly))
            .Where(method => !method.IsSpecialName && !_objectMembers.Contains(method.Name) || method.Name == "DisposeAsync");
}
