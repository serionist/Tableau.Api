using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Tableau.Api.Rest.Models;
using Tableau.Api.Tests.Fakes;

namespace Tableau.Api.Tests;

/// <summary>
/// <see cref="PermissionsClient"/>: each kind of content's list, add and delete, and each kind of a project's defaults,
/// from one table each: the method and the path the reference gives, the body, the answer read, and the version below
/// which nothing is sent. A kind with no permissions, and a rule without a grantee or a capability, are refused before
/// sending.
/// </summary>
[TestClass]
public sealed class PermissionsTests
{
    private const string Site = AreaFixtures.SitePath;
    private const string Rules = """
        {"permissions":{"granteeCapabilities":[{"group":{"id":"4a1b2c3d-0002-4e5f-8a9b-0c1d2e3f4a5b"},"capabilities":{"capability":[{"mode":"Allow","name":"Read"},{"mode":"Deny","name":"Write"}]}},{"user":{"id":"8d2e3f4a-5b6c-4d7e-9f0a-1b2c3d4e5f6a"},"capabilities":{"capability":[{"mode":"Allow","name":"ProjectLeader"}]}}]}}
        """;

    /// <summary>Each kind of content with permissions: its path's segment and the REST API version it has them from.</summary>
    public static IEnumerable<object[]> Kinds =>
    [
        [TableauContentKind.Project, "projects", "2.0", "Project"],
        [TableauContentKind.Workbook, "workbooks", "2.0", "Workbook"],
        [TableauContentKind.View, "views", "3.2", "View"],
        [TableauContentKind.DataSource, "datasources", "2.0", "Data Source"],
        [TableauContentKind.Flow, "flows", "3.3", "Flow"],
        [TableauContentKind.VirtualConnection, "virtualconnections", "3.23", "Virtual Connection"],
        [TableauContentKind.Collection, "collections", "3.12", "Collection"],
        [TableauContentKind.Database, "databases", "3.5", "Database"],
        [TableauContentKind.Table, "tables", "3.5", "Table"],
    ];

    /// <summary>Each kind of a project's default permissions: its path's segment and its version.</summary>
    public static IEnumerable<object[]> DefaultKinds =>
    [
        [DefaultPermissionsKind.Workbooks, "workbooks", "2.1", "Workbooks"],
        [DefaultPermissionsKind.DataSources, "datasources", "2.1", "Data Sources"],
        [DefaultPermissionsKind.DataRoles, "dataroles", "2.1", "Data Roles"],
        [DefaultPermissionsKind.Flows, "flows", "2.1", "Flows"],
        [DefaultPermissionsKind.VirtualConnections, "virtualconnections", "3.23", "Virtual Connections"],
        [DefaultPermissionsKind.Databases, "databases", "2.1", "Databases"],
        [DefaultPermissionsKind.Tables, "tables", "2.1", "Tables"],
    ];

    [TestMethod]
    [DynamicData(nameof(Kinds))]
    public async Task EachKindIsListedAddedAndDeletedOnItsOwnPath(TableauContentKind kind, string segment, string from, string name)
    {
        using var server = AreaFixtures.Server();
        var permissions = $"{Site}/{segment}/{AreaFixtures.ItemId}/permissions";
        server.Map("GET", permissions, FakeResponse.Fixture(200, "permissions.json"));
        server.Map("PUT", permissions, FakeResponse.Fixture(200, "permissions.json"));
        server.Map("DELETE", $"{permissions}/groups/{AreaFixtures.GroupId}/Read/Allow", FakeResponse.Empty(204));
        server.Map("DELETE", $"{permissions}/users/{AreaFixtures.OtherUserId}/Write/Deny", FakeResponse.Empty(204));
        await using var client = AreaFixtures.Client(server);
        var content = Content(kind);

        var listed = await client.Permissions.GetAsync(content);
        var added = await client.Permissions.AddAsync(content, Granted());
        await client.Permissions.DeleteAsync(content, PermissionGrantee.Group(AreaFixtures.GroupId), CapabilityName.Read, CapabilityMode.Allow);
        await client.Permissions.DeleteAsync(content, PermissionGrantee.User(AreaFixtures.OtherUserId), CapabilityName.Write, CapabilityMode.Deny);

        var calls = AreaFixtures.Calls(server);
        CollectionAssert.AreEqual(new[]
        {
            $"GET {permissions}", $"PUT {permissions}", $"DELETE {permissions}/groups/{AreaFixtures.GroupId}/Read/Allow",
            $"DELETE {permissions}/users/{AreaFixtures.OtherUserId}/Write/Deny",
        }, calls.Select(call => $"{call.Method} {call.Target}").ToArray());
        // The reference's bodies name a database or a table beside its grantees; the other kinds' name none.
        var asset = kind switch
        {
            TableauContentKind.Database => $$"""{"database":{"id":"{{AreaFixtures.ItemId}}"},""",
            TableauContentKind.Table => $$"""{"table":{"id":"{{AreaFixtures.ItemId}}"},""",
            _ => "{",
        };
        Assert.AreEqual(AreaFixtures.Json(Rules.Replace("{\"permissions\":{", "{\"permissions\":" + asset, StringComparison.Ordinal)),
            AreaFixtures.Json(calls[1].BodyText));
        Assert.IsTrue(calls.All(call => call.Header("X-Tableau-Auth") == Fixtures.SessionToken));
        Assert.HasCount(2, listed.GranteeCapabilities!);
        Assert.AreEqual(AreaFixtures.GroupId, listed.GranteeCapabilities![0].Group?.Id);
        Assert.AreEqual((CapabilityName.Write, CapabilityMode.Deny), (listed.GranteeCapabilities[0].Capabilities![1].Name, listed.GranteeCapabilities[0].Capabilities![1].Mode));
        Assert.AreEqual("Quarterly reports", added.Project?.Name);

        await AssertGatedAsync(from, $"List {name} Permissions", client => client.Permissions.GetAsync(content));
        await AssertGatedAsync(from, $"Add {name} Permissions", client => client.Permissions.AddAsync(content, Granted()));
        await AssertGatedAsync(from, kind is TableauContentKind.Database or TableauContentKind.Table ? $"Delete {name} Permissions" : $"Delete {name} Permission",
            client => client.Permissions.DeleteAsync(content, PermissionGrantee.User(AreaFixtures.OtherUserId), CapabilityName.Read, CapabilityMode.Allow));
    }

    [TestMethod]
    [DynamicData(nameof(DefaultKinds))]
    public async Task EachKindOfDefaultsIsListedAddedAndDeletedOnItsOwnPath(DefaultPermissionsKind kind, string segment, string from, string name)
    {
        using var server = AreaFixtures.Server();
        var defaults = $"{Site}/projects/{AreaFixtures.ProjectId}/default-permissions/{segment}";
        server.Map("GET", defaults, FakeResponse.Fixture(200, "permissions.json"));
        server.Map("PUT", defaults, FakeResponse.Fixture(200, "permissions.json"));
        server.Map("DELETE", $"{defaults}/groups/{AreaFixtures.GroupId}/ExportXml/Deny", FakeResponse.Empty(204));
        await using var client = AreaFixtures.Client(server);

        var listed = await client.Permissions.GetDefaultAsync(AreaFixtures.ProjectId, kind);
        var added = await client.Permissions.AddDefaultAsync(AreaFixtures.ProjectId, kind, Granted());
        await client.Permissions.DeleteDefaultAsync(AreaFixtures.ProjectId, kind, PermissionGrantee.Group(AreaFixtures.GroupId), CapabilityName.ExportXml,
            CapabilityMode.Deny);

        var calls = AreaFixtures.Calls(server);
        CollectionAssert.AreEqual(new[] { $"GET {defaults}", $"PUT {defaults}", $"DELETE {defaults}/groups/{AreaFixtures.GroupId}/ExportXml/Deny" },
            calls.Select(call => $"{call.Method} {call.Target}").ToArray());
        Assert.AreEqual(AreaFixtures.Json(Rules), AreaFixtures.Json(calls[1].BodyText));
        Assert.HasCount(2, listed.GranteeCapabilities!);
        Assert.HasCount(2, added.GranteeCapabilities!);

        await AssertGatedAsync(from, $"List Default Permissions for {name}", client => client.Permissions.GetDefaultAsync(AreaFixtures.ProjectId, kind));
        await AssertGatedAsync(from, $"Add Default Permissions for {name}", client => client.Permissions.AddDefaultAsync(AreaFixtures.ProjectId, kind, Granted()));
        await AssertGatedAsync(from, $"Delete Default Permission for {name}", client => client.Permissions.DeleteDefaultAsync(AreaFixtures.ProjectId, kind,
            PermissionGrantee.User(AreaFixtures.OtherUserId), CapabilityName.Read, CapabilityMode.Allow));
    }

    [TestMethod]
    public async Task ContentWithoutExplicitPermissionsListsNoGrantees()
    {
        using var server = AreaFixtures.Server();
        server.Map("GET", $"{Site}/databases/{AreaFixtures.ItemId}/permissions", FakeResponse.Fixture(200, "permissions-none.json"));
        server.Map("PUT", $"{Site}/databases/{AreaFixtures.ItemId}/permissions", FakeResponse.Fixture(200, "permissions-none.json"));
        await using var client = AreaFixtures.Client(server);

        var listed = await client.Permissions.GetAsync(TableauContent.Database(AreaFixtures.ItemId));
        var added = await client.Permissions.AddAsync(TableauContent.Database(AreaFixtures.ItemId), Granted());

        Assert.IsNotNull(listed.GranteeCapabilities);
        Assert.IsEmpty(listed.GranteeCapabilities);
        Assert.AreEqual("stage", listed.Database?.Name);
        Assert.IsEmpty(added.GranteeCapabilities!);
    }

    [TestMethod]
    public async Task AColumnHasNoPermissionsAndIsRefusedBeforeSending()
    {
        using var server = AreaFixtures.Server();
        await using var client = AreaFixtures.Client(server);
        var column = TableauContent.Column(AreaFixtures.ProjectId, AreaFixtures.ItemId);

        var listed = await AreaFixtures.RefusedAsync<ArgumentException>(server, () => client.Permissions.GetAsync(column));
        await AreaFixtures.RefusedAsync<ArgumentException>(server, () => client.Permissions.AddAsync(column, Granted()));
        await AreaFixtures.RefusedAsync<ArgumentException>(server, () => client.Permissions.DeleteAsync(column,
            PermissionGrantee.User(AreaFixtures.OtherUserId), CapabilityName.Read, CapabilityMode.Allow));

        Assert.AreEqual("content", listed.ParamName);
        Assert.StartsWith("Permissions take a project, a workbook, a view, a data source, a flow, a virtual connection, a collection, a database "
            + "or a table, not a column.", listed.Message);
    }

    [TestMethod]
    public async Task ARuleIsSentWithItsGranteesIdAloneAndEachCapabilitysNameAndMode()
    {
        using var server = AreaFixtures.Server();
        server.Map("PUT", $"{Site}/workbooks/{AreaFixtures.ItemId}/permissions", FakeResponse.Fixture(200, "permissions.json"));
        await using var client = AreaFixtures.Client(server);

        await client.Permissions.AddAsync(TableauContent.Workbook(AreaFixtures.ItemId),
        [
            new GranteeCapabilities
            {
                GroupSet = new GroupSet { Id = AreaFixtures.GroupId, Name = "Invented set", GroupCount = 3 },
                Capabilities = [new Capability { Name = new CapabilityName("InventedFutureCapability"), Mode = CapabilityMode.Allow }],
            },
        ]);

        AreaFixtures.Sent(server, $"PUT {Site}/workbooks/{AreaFixtures.ItemId}/permissions", $$$"""
            {"permissions":{"granteeCapabilities":[{"groupSet":{"id":"{{{AreaFixtures.GroupId}}}"},"capabilities":{"capability":[{"mode":"Allow","name":"InventedFutureCapability"}]}}]}}
            """);
    }

    [TestMethod]
    public async Task ABadRuleIsRefusedBeforeSending()
    {
        using var server = AreaFixtures.Server();
        await using var client = AreaFixtures.Client(server);
        var project = TableauContent.Project(AreaFixtures.ProjectId);
        Capability[] read = [new Capability { Name = CapabilityName.Read, Mode = CapabilityMode.Allow }];
        var bad = new List<(string What, GranteeCapabilities[] Rules)>
        {
            ("none", []),
            ("a null rule", [null!]),
            ("no grantee", [new GranteeCapabilities { Capabilities = read }]),
            ("two grantees", [new GranteeCapabilities { User = new User { Id = AreaFixtures.OtherUserId }, Group = new Group { Id = AreaFixtures.GroupId }, Capabilities = read }]),
            ("a grantee's name", [new GranteeCapabilities { User = new User { Name = "ada" }, Capabilities = read }]),
            ("a grantee that is no LUID", [new GranteeCapabilities { Group = new Group { Id = "Finance" }, Capabilities = read }]),
            ("no capability", [new GranteeCapabilities { Group = new Group { Id = AreaFixtures.GroupId } }]),
            ("an empty capability list", [new GranteeCapabilities { Group = new Group { Id = AreaFixtures.GroupId }, Capabilities = [] }]),
            ("a capability without a name", [new GranteeCapabilities { Group = new Group { Id = AreaFixtures.GroupId }, Capabilities = [new Capability { Mode = CapabilityMode.Allow }] }]),
            ("a capability without a mode", [new GranteeCapabilities { Group = new Group { Id = AreaFixtures.GroupId }, Capabilities = [new Capability { Name = CapabilityName.Read }] }]),
            ("a null capability", [new GranteeCapabilities { Group = new Group { Id = AreaFixtures.GroupId }, Capabilities = [null!] }]),
        };

        foreach (var (what, rules) in bad)
        {
            var thrown = await AreaFixtures.RefusedAsync<ArgumentException>(server, () => client.Permissions.AddAsync(project, rules));
            Assert.AreEqual("rules", thrown.ParamName, what);
            thrown = await AreaFixtures.RefusedAsync<ArgumentException>(server,
                () => client.Permissions.AddDefaultAsync(AreaFixtures.ProjectId, DefaultPermissionsKind.Workbooks, rules));
            Assert.AreEqual("rules", thrown.ParamName, what);
        }
        await AreaFixtures.RefusedAsync<ArgumentNullException>(server, () => client.Permissions.AddAsync(project, null!));
    }

    [TestMethod]
    public async Task ARefusedArgumentSendsNothing()
    {
        using var server = AreaFixtures.Server();
        await using var client = AreaFixtures.Client(server);
        var project = TableauContent.Project(AreaFixtures.ProjectId);
        var user = PermissionGrantee.User(AreaFixtures.OtherUserId);

        Assert.AreEqual("capability", (await AreaFixtures.RefusedAsync<ArgumentException>(server,
            () => client.Permissions.DeleteAsync(project, user, default, CapabilityMode.Allow))).ParamName);
        Assert.AreEqual("mode", (await AreaFixtures.RefusedAsync<ArgumentException>(server,
            () => client.Permissions.DeleteAsync(project, user, CapabilityName.Read, default))).ParamName);
        Assert.AreEqual("capability", (await AreaFixtures.RefusedAsync<ArgumentException>(server,
            () => client.Permissions.DeleteDefaultAsync(AreaFixtures.ProjectId, DefaultPermissionsKind.Flows, user, default, CapabilityMode.Allow))).ParamName);
        await AreaFixtures.RefusedAsync<ArgumentNullException>(server, () => client.Permissions.GetAsync(null!));
        await AreaFixtures.RefusedAsync<ArgumentNullException>(server, () => client.Permissions.DeleteAsync(project, null!, CapabilityName.Read, CapabilityMode.Allow));
        await AreaFixtures.RefusedAsync<ArgumentNullException>(server,
            () => client.Permissions.DeleteDefaultAsync(AreaFixtures.ProjectId, DefaultPermissionsKind.Flows, null!, CapabilityName.Read, CapabilityMode.Allow));
        Assert.AreEqual("projectId", (await AreaFixtures.RefusedAsync<ArgumentException>(server,
            () => client.Permissions.GetDefaultAsync("Finance", DefaultPermissionsKind.Workbooks))).ParamName);
        Assert.AreEqual("kind", (await AreaFixtures.RefusedAsync<ArgumentOutOfRangeException>(server,
            () => client.Permissions.GetDefaultAsync(AreaFixtures.ProjectId, (DefaultPermissionsKind)42))).ParamName);
    }

    [TestMethod]
    public async Task ACapabilityAndAModeAreEachSentAsOneSegment()
    {
        using var server = AreaFixtures.Server();
        var path = $"{Site}/projects/{AreaFixtures.ProjectId}/permissions/users/{AreaFixtures.OtherUserId}/Invented%2FCapability%20%231/Allow";
        server.Map("DELETE", path, FakeResponse.Empty(204));
        await using var client = AreaFixtures.Client(server);

        await client.Permissions.DeleteAsync(TableauContent.Project(AreaFixtures.ProjectId), PermissionGrantee.User(AreaFixtures.OtherUserId),
            new CapabilityName("Invented/Capability #1"), CapabilityMode.Allow);

        AreaFixtures.Sent(server, $"DELETE {path}");
    }

    // The rules the add tests send: a group allowed Read and denied Write, and a user its project leader, the user's name
    // left out of the body.
    private static GranteeCapabilities[] Granted() =>
    [
        new GranteeCapabilities
        {
            Group = new Group { Id = AreaFixtures.GroupId },
            Capabilities =
            [
                new Capability { Name = CapabilityName.Read, Mode = CapabilityMode.Allow },
                new Capability { Name = CapabilityName.Write, Mode = CapabilityMode.Deny },
            ],
        },
        new GranteeCapabilities
        {
            User = new User { Id = AreaFixtures.OtherUserId, Name = "ada", SiteRole = SiteRole.Viewer },
            Capabilities = [new Capability { Name = CapabilityName.ProjectLeader, Mode = CapabilityMode.Allow }],
        },
    ];

    private static TableauContent Content(TableauContentKind kind) => kind switch
    {
        TableauContentKind.Project => TableauContent.Project(AreaFixtures.ItemId),
        TableauContentKind.Workbook => TableauContent.Workbook(AreaFixtures.ItemId),
        TableauContentKind.View => TableauContent.View(AreaFixtures.ItemId),
        TableauContentKind.DataSource => TableauContent.DataSource(AreaFixtures.ItemId),
        TableauContentKind.Flow => TableauContent.Flow(AreaFixtures.ItemId),
        TableauContentKind.VirtualConnection => TableauContent.VirtualConnection(AreaFixtures.ItemId),
        TableauContentKind.Collection => TableauContent.Collection(AreaFixtures.ItemId),
        TableauContentKind.Database => TableauContent.Database(AreaFixtures.ItemId),
        _ => TableauContent.Table(AreaFixtures.ItemId),
    };

    /// <summary>
    /// Asserts a call is refused at the version just below <paramref name="from"/>, naming its descriptor, with nothing
    /// sent; a call from REST API 1.0 has no version below it.
    /// </summary>
    internal static async Task AssertGatedAsync(string from, string name, Func<TableauClient, Task> call)
    {
        var version = Version.Parse(from);
        if (version <= new Version(1, 0)) return;
        var below = version.Minor > 0 ? $"{version.Major}.{version.Minor - 1}" : $"{version.Major - 1}.9";
        using var server = AreaFixtures.Server();
        var options = SessionFixtures.Options(server, below);
        options.Deployment = TableauDeployment.Server;
        options.AllowUnsupportedServerVersion = true;
        await using var client = SessionFixtures.Client(options, SessionFixtures.Pat());

        var thrown = await ClientFixtures.FailsAsync(TableauErrorKind.UnsupportedApiVersion, () => call(client));

        Assert.Contains($"{name} needs REST API {from}", thrown.Message);
        Assert.IsEmpty(server.Requests);
    }
}
