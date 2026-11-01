using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Tableau.Api.Pipeline;
using Tableau.Api.Rest.Models;

namespace Tableau.Api;

/// <summary>
/// Permissions on the site's content and a project's default permissions: <see cref="TableauClient.Permissions"/>.
/// </summary>
/// <remarks>
/// <para>
/// One API over every kind of content Tableau's reference gives permissions: projects, workbooks and data sources (REST
/// API 2.0), views (3.2), flows (3.3), databases and tables (3.5, Tableau Catalog's), collections (3.12) and virtual
/// connections (3.23). A call below its kind's version, or for a kind that has no permissions, such as a column, is
/// refused before anything is sent.
/// </para>
/// <para>
/// A rule grants or denies capabilities to one user or group: a <see cref="GranteeCapabilities"/> whose
/// <see cref="GranteeCapabilities.User"/> or <see cref="GranteeCapabilities.Group"/> names the grantee by its LUID, and
/// whose <see cref="GranteeCapabilities.Capabilities"/> each have a <see cref="CapabilityName"/> and a
/// <see cref="CapabilityMode"/>.
/// </para>
/// </remarks>
public class PermissionsClient
{
    private const string Takes =
        "a project, a workbook, a view, a data source, a flow, a virtual connection, a collection, a database or a table";

    private readonly TableauPipeline? _pipeline;

    /// <summary>A sub-client with no connection, for a mock to derive from: its methods throw until overridden.</summary>
    protected PermissionsClient()
    {
    }

    internal PermissionsClient(TableauPipeline pipeline)
    {
        _pipeline = pipeline;
    }

    private TableauPipeline Pipeline => _pipeline ?? throw TableauClient.ForMocksOnly();

    /// <summary>List Permissions: the users and groups the content grants or denies capabilities, and which.</summary>
    /// <param name="content">The content, such as <c>TableauContent.Project(id)</c>.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>
    /// The permissions, the content in its member of its kind, such as <see cref="Permissions.Project"/>, and
    /// <see cref="Permissions.GranteeCapabilities"/> empty, never null, when it grants none.
    /// </returns>
    /// <exception cref="ArgumentException"><paramref name="content"/> is of a kind that has no permissions: nothing is sent.</exception>
    /// <exception cref="TableauException">The call failed; <see cref="TableauException.Kind"/> says how.</exception>
    public virtual Task<Permissions> GetAsync(TableauContent content, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        return ReadAsync(Pipeline.SendAsync(Calls(content).List, new TableauRequest([content.Id]), static body => body.Permissions, "permissions",
            cancellationToken));
    }

    /// <summary>
    /// Add Permissions: grants or denies capabilities on the content to users and groups. A capability a grantee already
    /// has in either mode is left as it is; delete it first to change its mode.
    /// </summary>
    /// <param name="content">The content, such as <c>TableauContent.Workbook(id)</c>.</param>
    /// <param name="rules">Each grantee and its capabilities, one rule at least.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The permissions Tableau answers with, <see cref="Permissions.GranteeCapabilities"/> never null.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="content"/> is of a kind that has no permissions, or a rule names no grantee, more than one, one that
    /// is no LUID, no capability, or a capability without its name or mode: nothing is sent.
    /// </exception>
    /// <exception cref="TableauException">The call failed; <see cref="TableauException.Kind"/> says how.</exception>
    public virtual Task<Permissions> AddAsync(TableauContent content, IEnumerable<GranteeCapabilities> rules,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        var calls = Calls(content);
        var permissions = new Permissions
        {
            // As the reference's bodies write them: a workbook's, a view's, a data source's, a flow's, a database's and a
            // table's name it beside the grantees (a workbook's and a data source's "not required"); a project's mustn't,
            // and a collection's and a virtual connection's don't.
            Workbook = content.Kind == TableauContentKind.Workbook ? new Workbook { Id = content.Id } : null,
            View = content.Kind == TableauContentKind.View ? new View { Id = content.Id } : null,
            Datasource = content.Kind == TableauContentKind.DataSource ? new DataSource { Id = content.Id } : null,
            Flow = content.Kind == TableauContentKind.Flow ? new Flow { Id = content.Id } : null,
            Database = content.Kind == TableauContentKind.Database ? new Database { Id = content.Id } : null,
            Table = content.Kind == TableauContentKind.Table ? new Table { Id = content.Id } : null,
            GranteeCapabilities = Rules(rules, nameof(rules)),
        };
        return ReadAsync(Pipeline.SendAsync(calls.Add, new TableauRequest([content.Id], Body: new TsRequest { Permissions = permissions }),
            static body => body.Permissions, "permissions", cancellationToken));
    }

    /// <summary>Delete Permission: removes one capability, in one mode, that the content grants a user, a group or a group set.</summary>
    /// <param name="content">The content, such as <c>TableauContent.View(id)</c>.</param>
    /// <param name="grantee">The user, group or group set, such as <c>PermissionGrantee.Group(id)</c>.</param>
    /// <param name="capability">The capability, such as <see cref="CapabilityName.Read"/>.</param>
    /// <param name="mode">The mode it is granted in: <see cref="CapabilityMode.Allow"/> or <see cref="CapabilityMode.Deny"/>.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <exception cref="ArgumentException">
    /// <paramref name="content"/> is of a kind that has no permissions, or <paramref name="capability"/> or
    /// <paramref name="mode"/> is a default value, which names none: nothing is sent.
    /// </exception>
    /// <exception cref="TableauException">The call failed; <see cref="TableauException.Kind"/> says how.</exception>
    public virtual Task DeleteAsync(TableauContent content, PermissionGrantee grantee, CapabilityName capability, CapabilityMode mode,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(grantee);
        var calls = Calls(content);
        return Pipeline.SendAsync(calls.Delete, new TableauRequest(
            [content.Id, grantee.Segment, grantee.Id, Name(capability, nameof(capability)), Mode(mode, nameof(mode))]), cancellationToken);
    }

    /// <summary>
    /// List Default Permissions: the rules a project gives the content of one kind that is added to it, or, with its
    /// permissions locked, all of it.
    /// </summary>
    /// <param name="projectId">The project's LUID.</param>
    /// <param name="kind">Which kind of content's defaults.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The default permissions, <see cref="Permissions.GranteeCapabilities"/> empty, never null, when there are none.</returns>
    /// <exception cref="ArgumentException"><paramref name="projectId"/> is no LUID, or <paramref name="kind"/> is none of the kinds.</exception>
    /// <exception cref="TableauException">The call failed; <see cref="TableauException.Kind"/> says how.</exception>
    public virtual Task<Permissions> GetDefaultAsync(string projectId, DefaultPermissionsKind kind, CancellationToken cancellationToken = default)
    {
        RequestUri.Luid(projectId, nameof(projectId));
        return ReadAsync(Pipeline.SendAsync(Defaults(kind).List, new TableauRequest([projectId]), static body => body.Permissions, "permissions",
            cancellationToken));
    }

    /// <summary>Add Default Permissions: rules a project gives the content of one kind added to it.</summary>
    /// <param name="projectId">The project's LUID.</param>
    /// <param name="kind">Which kind of content's defaults.</param>
    /// <param name="rules">Each grantee and its capabilities, one rule at least.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The default permissions Tableau answers with, <see cref="Permissions.GranteeCapabilities"/> never null.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="projectId"/> is no LUID, <paramref name="kind"/> is none of the kinds, or a rule names no grantee,
    /// more than one, one that is no LUID, no capability, or a capability without its name or mode: nothing is sent.
    /// </exception>
    /// <exception cref="TableauException">The call failed; <see cref="TableauException.Kind"/> says how.</exception>
    public virtual Task<Permissions> AddDefaultAsync(string projectId, DefaultPermissionsKind kind, IEnumerable<GranteeCapabilities> rules,
        CancellationToken cancellationToken = default)
    {
        RequestUri.Luid(projectId, nameof(projectId));
        var calls = Defaults(kind);
        var body = new TsRequest { Permissions = new Permissions { GranteeCapabilities = Rules(rules, nameof(rules)) } };
        return ReadAsync(Pipeline.SendAsync(calls.Add, new TableauRequest([projectId], Body: body), static body => body.Permissions, "permissions",
            cancellationToken));
    }

    /// <summary>Delete Default Permission: removes one capability, in one mode, from a project's defaults for one kind of content.</summary>
    /// <param name="projectId">The project's LUID.</param>
    /// <param name="kind">Which kind of content's defaults.</param>
    /// <param name="grantee">The user, group or group set, such as <c>PermissionGrantee.User(id)</c>.</param>
    /// <param name="capability">The capability, such as <see cref="CapabilityName.Read"/>.</param>
    /// <param name="mode">The mode it is granted in: <see cref="CapabilityMode.Allow"/> or <see cref="CapabilityMode.Deny"/>.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <exception cref="ArgumentException">
    /// <paramref name="projectId"/> is no LUID, <paramref name="kind"/> is none of the kinds, or <paramref name="capability"/>
    /// or <paramref name="mode"/> is a default value, which names none: nothing is sent.
    /// </exception>
    /// <exception cref="TableauException">The call failed; <see cref="TableauException.Kind"/> says how.</exception>
    public virtual Task DeleteDefaultAsync(string projectId, DefaultPermissionsKind kind, PermissionGrantee grantee, CapabilityName capability,
        CapabilityMode mode, CancellationToken cancellationToken = default)
    {
        RequestUri.Luid(projectId, nameof(projectId));
        ArgumentNullException.ThrowIfNull(grantee);
        var calls = Defaults(kind);
        return Pipeline.SendAsync(calls.Delete, new TableauRequest(
            [projectId, grantee.Segment, grantee.Id, Name(capability, nameof(capability)), Mode(mode, nameof(mode))]), cancellationToken);
    }

    // Each kind of content's own calls; a kind with no permissions is refused.
    private static PermissionCalls Calls(TableauContent content) => content.Kind switch
    {
        TableauContentKind.Project => Endpoints.ProjectPermissions,
        TableauContentKind.Workbook => Endpoints.WorkbookPermissions,
        TableauContentKind.View => Endpoints.ViewPermissions,
        TableauContentKind.DataSource => Endpoints.DataSourcePermissions,
        TableauContentKind.Flow => Endpoints.FlowPermissions,
        TableauContentKind.VirtualConnection => Endpoints.VirtualConnectionPermissions,
        TableauContentKind.Collection => Endpoints.CollectionPermissions,
        TableauContentKind.Database => Endpoints.DatabasePermissions,
        TableauContentKind.Table => Endpoints.TablePermissions,
        _ => throw content.Refused("Permissions take", Takes, nameof(content)),
    };

    private static PermissionCalls Defaults(DefaultPermissionsKind kind) => kind switch
    {
        DefaultPermissionsKind.Workbooks => Endpoints.DefaultWorkbookPermissions,
        DefaultPermissionsKind.DataSources => Endpoints.DefaultDataSourcePermissions,
        DefaultPermissionsKind.DataRoles => Endpoints.DefaultDataRolePermissions,
        DefaultPermissionsKind.Flows => Endpoints.DefaultFlowPermissions,
        DefaultPermissionsKind.VirtualConnections => Endpoints.DefaultVirtualConnectionPermissions,
        DefaultPermissionsKind.Databases => Endpoints.DefaultDatabasePermissions,
        DefaultPermissionsKind.Tables => Endpoints.DefaultTablePermissions,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "None of the kinds of content a project's default permissions are for."),
    };

    // The rules as the reference's bodies write them: each grantee by its id alone, and each capability by its name and
    // mode, so nothing else a caller's model holds is sent.
    private static List<GranteeCapabilities> Rules(IEnumerable<GranteeCapabilities> rules, string parameter)
    {
        ArgumentNullException.ThrowIfNull(rules, parameter);
        var sent = new List<GranteeCapabilities>();
        foreach (var rule in rules)
        {
            if (rule is null) throw new ArgumentException("A rule is null.", parameter);
            var grantees = (rule.User is null ? 0 : 1) + (rule.Group is null ? 0 : 1) + (rule.GroupSet is null ? 0 : 1);
            if (grantees != 1)
                throw new ArgumentException("A rule names one grantee: its User, its Group or its GroupSet, by its id.", parameter);
            var id = RequestUri.Luid(rule.User?.Id ?? rule.Group?.Id ?? rule.GroupSet?.Id, parameter);
            if (rule.Capabilities is not { Count: > 0 } capabilities)
                throw new ArgumentException($"The rule for {id} grants no capability.", parameter);
            sent.Add(new GranteeCapabilities
            {
                User = rule.User is null ? null : new User { Id = id },
                Group = rule.Group is null ? null : new Group { Id = id },
                GroupSet = rule.GroupSet is null ? null : new GroupSet { Id = id },
                Capabilities = [.. capabilities.Select(capability => new Capability
                {
                    Name = capability?.Name is { Value: not null } name ? name : throw new ArgumentException($"A capability for {id} has no name.", parameter),
                    Mode = capability.Mode is { Value: not null } mode ? mode : throw new ArgumentException($"A capability for {id} has no mode.", parameter),
                })],
            });
        }
        return sent.Count > 0 ? sent : throw new ArgumentException("Add takes one rule at least.", parameter);
    }

    private static string Name(CapabilityName capability, string parameter) =>
        capability.Value ?? throw new ArgumentException("A capability is needed, such as CapabilityName.Read; a default one names none.", parameter);

    private static string Mode(CapabilityMode mode, string parameter) =>
        mode.Value ?? throw new ArgumentException("A mode is needed, CapabilityMode.Allow or CapabilityMode.Deny; a default one names none.", parameter);

    // An answer's permissions, their grantees never null.
    private static async Task<Permissions> ReadAsync(Task<Permissions> answer)
    {
        var permissions = await answer.ConfigureAwait(false);
        return permissions.GranteeCapabilities is null ? permissions with { GranteeCapabilities = [] } : permissions;
    }
}

/// <summary>The kinds of content a project's default permissions are for, one set each.</summary>
/// <remarks>
/// Tableau's reference also lists lenses and metrics, whose features Tableau retired in Tableau Server 2024.2 and Tableau
/// Cloud in February 2024, before the oldest version the client supports.
/// </remarks>
public enum DefaultPermissionsKind
{
    /// <summary>Workbooks (REST API 2.1).</summary>
    Workbooks,

    /// <summary>Published data sources (REST API 2.1).</summary>
    DataSources,

    /// <summary>Data roles (REST API 2.1). Tableau Server 2025.1 answers 405 to their list.</summary>
    DataRoles,

    /// <summary>Flows (REST API 2.1).</summary>
    Flows,

    /// <summary>Virtual connections (REST API 3.23).</summary>
    VirtualConnections,

    /// <summary>Database assets, Tableau Catalog's (REST API 2.1).</summary>
    Databases,

    /// <summary>Table assets, Tableau Catalog's (REST API 2.1).</summary>
    Tables,
}

/// <summary>What a <see cref="PermissionGrantee"/> is.</summary>
public enum PermissionGranteeKind
{
    /// <summary>A user.</summary>
    User,

    /// <summary>A group.</summary>
    Group,

    /// <summary>A group set, a set of groups whose members all hold what it is granted (REST API 3.22).</summary>
    GroupSet,
}

/// <summary>
/// The user, group or group set whose permission a delete names, by its LUID: <c>PermissionGrantee.Group(id)</c>.
/// </summary>
/// <remarks>
/// <para>
/// A delete goes to <c>permissions/users/{id}</c>, <c>permissions/groups/{id}</c> or <c>permissions/groupSets/{id}</c>.
/// The reference documents the first two; a group set's comes from Tableau's schema, whose rules name a group set beside
/// users and groups, and Tableau's Python client, which deletes a group set's permission there. Group sets came in REST
/// API 3.22 (Tableau Server 2024.2).
/// </para>
/// <para>
/// Two values are equal when they name the same grantee: the same kind and LUID, the LUID's case aside. The value is
/// immutable.
/// </para>
/// </remarks>
public sealed class PermissionGrantee : IEquatable<PermissionGrantee>
{
    private PermissionGrantee(PermissionGranteeKind kind, string id)
    {
        Kind = kind;
        Id = id;
    }

    /// <summary>Whether it is a user, a group or a group set.</summary>
    public PermissionGranteeKind Kind { get; }

    /// <summary>The user's, the group's or the group set's LUID.</summary>
    public string Id { get; }

    /// <summary>The path segment the delete calls name it under: <c>users</c>, <c>groups</c> or <c>groupSets</c>.</summary>
    internal string Segment => Kind switch
    {
        PermissionGranteeKind.Group => "groups",
        PermissionGranteeKind.GroupSet => "groupSets",
        _ => "users",
    };

    /// <summary>A user, by its LUID.</summary>
    /// <param name="id">The user's LUID.</param>
    /// <exception cref="ArgumentException"><paramref name="id"/> is no LUID.</exception>
    public static PermissionGrantee User(string id) => new(PermissionGranteeKind.User, RequestUri.Luid(id, nameof(id)));

    /// <summary>A group, by its LUID.</summary>
    /// <param name="id">The group's LUID.</param>
    /// <exception cref="ArgumentException"><paramref name="id"/> is no LUID.</exception>
    public static PermissionGrantee Group(string id) => new(PermissionGranteeKind.Group, RequestUri.Luid(id, nameof(id)));

    /// <summary>A group set, by its LUID (REST API 3.22).</summary>
    /// <param name="id">The group set's LUID.</param>
    /// <exception cref="ArgumentException"><paramref name="id"/> is no LUID.</exception>
    public static PermissionGrantee GroupSet(string id) => new(PermissionGranteeKind.GroupSet, RequestUri.Luid(id, nameof(id)));

    /// <summary>Whether two values name the same grantee.</summary>
    /// <param name="left">One value, or null.</param>
    /// <param name="right">The other, or null.</param>
    public static bool operator ==(PermissionGrantee? left, PermissionGrantee? right) => left is null ? right is null : left.Equals(right);

    /// <summary>Whether two values name different grantees.</summary>
    /// <param name="left">One value, or null.</param>
    /// <param name="right">The other, or null.</param>
    public static bool operator !=(PermissionGrantee? left, PermissionGrantee? right) => !(left == right);

    /// <summary>Whether <paramref name="other"/> names the same grantee.</summary>
    /// <param name="other">The other value.</param>
    public bool Equals(PermissionGrantee? other) =>
        other is not null && Kind == other.Kind && string.Equals(Id, other.Id, StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => Equals(obj as PermissionGrantee);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Kind, StringComparer.OrdinalIgnoreCase.GetHashCode(Id));

    /// <summary>The kind and the LUID, such as <c>group 1f2f3e4e-...</c> or <c>group set 1f2f3e4e-...</c>.</summary>
    public override string ToString() => Kind switch
    {
        PermissionGranteeKind.Group => $"group {Id}",
        PermissionGranteeKind.GroupSet => $"group set {Id}",
        _ => $"user {Id}",
    };
}

