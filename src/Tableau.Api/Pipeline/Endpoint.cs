using System;
using System.Buffers;
using System.Collections.Generic;
using System.Net.Http;

namespace Tableau.Api.Pipeline;

/// <summary>The deployments an endpoint runs on.</summary>
[Flags]
internal enum TableauDeployments
{
    /// <summary>Tableau Server, on the customer's own machines.</summary>
    Server = 1,

    /// <summary>Tableau Cloud, Tableau's hosted service.</summary>
    Cloud = 2,

    /// <summary>Both.</summary>
    Both = Server | Cloud,
}

/// <summary>What an endpoint moves besides JSON, which decides the timeout its call runs under.</summary>
internal enum TransferKind
{
    /// <summary>JSON only, under <see cref="TableauClientOptions.RequestTimeout"/>.</summary>
    None,

    /// <summary>A file or an export it answers with, under <see cref="TableauClientOptions.TransferTimeout"/>.</summary>
    Download,

    /// <summary>A file it is sent, under <see cref="TableauClientOptions.TransferTimeout"/>.</summary>
    Upload,
}

/// <summary>
/// One REST API call as Tableau's reference gives it, which the pipeline reads for the path, the gates, the retry rule
/// and the timeout. Each call has one, in <see cref="Endpoints"/>.
/// </summary>
/// <param name="Name">The reference's name for it, which errors and logs give: "Server Info".</param>
/// <param name="Method">The HTTP method.</param>
/// <param name="Template">
/// The path under the base URL's <c>api/</c>, by segments: <c>{version}</c> is the version in use, so
/// <c>{version}/serverinfo</c>, <c>-/...</c> for the resource-versioned endpoints and <c>metadata/graphql</c> are
/// the three shapes; <c>{siteId}</c> is the session's site; every other <c>{name}</c> takes the caller's value of that
/// parameter, escaped as one segment, and <c>{name:luid}</c> one that must be a LUID.
/// </param>
/// <param name="MinVersion">The version the reference says it appeared in.</param>
/// <param name="Deployments">Where it runs.</param>
/// <param name="Idempotent">Whether sending it twice does what sending it once does, so a retry is safe.</param>
/// <param name="Transfer">What it moves besides JSON.</param>
internal sealed record Endpoint(
    string Name,
    HttpMethod Method,
    string Template,
    Version MinVersion,
    TableauDeployments Deployments,
    bool Idempotent,
    TransferKind Transfer)
{
    /// <summary>The template's segments, read once; a template that can't be read fails here, in a test of the registry.</summary>
    public IReadOnlyList<PathSegment> Segments { get; } = PathSegment.Parse(Template);

    /// <summary>
    /// Whether the call is made in a session: the client's own, signed in on demand, or the one a bound client holds. A
    /// call given a token of its own, such as Sign Out, sends that one instead.
    /// </summary>
    public bool NeedsSession { get; init; } = true;

    /// <summary>
    /// Whether an exception keeps the start of the answer's body. Not for a call whose answer holds a session token,
    /// such as Sign In, whose body could carry it into a log.
    /// </summary>
    public bool KeepsBody { get; init; } = true;
}

/// <summary>One segment of an endpoint's template: literal text, the version, or a caller's value.</summary>
/// <param name="Text">The literal text, or the value's parameter name.</param>
/// <param name="Kind">What the segment holds.</param>
internal sealed record PathSegment(string Text, PathSegmentKind Kind)
{
    /// <summary>The segments of a template; throws <see cref="FormatException"/> on one it can't read.</summary>
    public static IReadOnlyList<PathSegment> Parse(string template)
    {
        var segments = new List<PathSegment>();
        foreach (var part in template.Split('/'))
        {
            if (part.Length > 1 && part[0] == '{' && part[^1] == '}')
            {
                var name = part[1..^1];
                segments.Add(name switch
                {
                    "version" => new PathSegment(name, PathSegmentKind.Version),
                    "siteId" => new PathSegment(name, PathSegmentKind.SiteId),
                    _ when name.EndsWith(":luid", StringComparison.Ordinal) && IsName(name[..^5]) =>
                        new PathSegment(name[..^5], PathSegmentKind.Luid),
                    _ when IsName(name) => new PathSegment(name, PathSegmentKind.Value),
                    _ => throw new FormatException($"'{part}' in the template '{template}' names no parameter."),
                });
            }
            else if (part.Length > 0 && part.AsSpan().IndexOfAnyExcept(_literalCharacters) < 0)
            {
                segments.Add(new PathSegment(part, PathSegmentKind.Literal));
            }
            else
            {
                throw new FormatException($"'{part}' in the template '{template}' is no literal segment.");
            }
        }
        return segments;
    }

    private static readonly SearchValues<char> _literalCharacters =
        SearchValues.Create("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-._");

    private static bool IsName(string name) =>
        name.Length > 0 && char.IsAsciiLetter(name[0]) && name.AsSpan().IndexOfAnyExcept(_literalCharacters) < 0;
}

/// <summary>What a template segment holds.</summary>
internal enum PathSegmentKind
{
    /// <summary>Text written in the template.</summary>
    Literal,

    /// <summary>The API version in use.</summary>
    Version,

    /// <summary>A caller's value, escaped as one segment.</summary>
    Value,

    /// <summary>A caller's value that must be a LUID.</summary>
    Luid,

    /// <summary>The session's site, a LUID, which the pipeline fills from the session the call is made in.</summary>
    SiteId,
}

/// <summary>Every REST call the 2.0 client makes, one descriptor each. Each port step adds its area's.</summary>
internal static class Endpoints
{
    private const string SignInTemplate = "{version}/auth/signin";

    /// <summary>Server Info: the product version and the REST API version; it needs no sign-in.</summary>
    public static readonly Endpoint ServerInfo = new("Server Info", HttpMethod.Get, "{version}/serverinfo", new Version(2, 4),
        TableauDeployments.Both, Idempotent: true, TransferKind.None)
    { NeedsSession = false };

    // Sign In is one call with a body per kind of credentials, each from its own version, so each kind has its own
    // descriptor, and its gates refuse a kind the server lacks before the credentials are read. A password's or a
    // personal access token's sent twice leaves at most a session nobody uses, which ends on its own, or, with a token,
    // ends the first: a retry is safe. A JWT's is never sent twice: Tableau refuses a JWT whose jti it has seen, and a
    // late attempt may pass the JWT's exp; the next call mints or asks for a new one. Its answer holds the session's
    // token, so no exception keeps it.

    /// <summary>Sign In with a user's name and password (REST API 1.0).</summary>
    public static readonly Endpoint SignIn = SignInAs("Sign In", new Version(1, 0), TableauDeployments.Both, idempotent: true);

    /// <summary>Sign In with a name and password, impersonating another user: Tableau Server only.</summary>
    public static readonly Endpoint SignInImpersonating = SignInAs("Sign In, impersonating a user", new Version(1, 0),
        TableauDeployments.Server, idempotent: true);

    /// <summary>Sign In with a personal access token (REST API 3.6, Tableau Server 2019.4).</summary>
    public static readonly Endpoint SignInWithPersonalAccessToken = SignInAs("Sign In with a personal access token",
        new Version(3, 6), TableauDeployments.Both, idempotent: true);

    /// <summary>
    /// Sign In with a personal access token, impersonating another user: Tableau Server only, from REST API 3.11 (Tableau
    /// Server 2021.1), with <c>tsm authentication pat-impersonation enable</c> on the server.
    /// </summary>
    public static readonly Endpoint SignInWithPersonalAccessTokenImpersonating = SignInAs(
        "Sign In with a personal access token, impersonating a user", new Version(3, 11), TableauDeployments.Server, idempotent: true);

    /// <summary>
    /// Sign In with a JWT: a connected app's, by direct trust or OAuth 2.0 trust (REST API 3.17, Tableau Server 2022.3;
    /// Tableau Cloud from 3.16). Never sent twice.
    /// </summary>
    public static readonly Endpoint SignInWithJwt = SignInAs("Sign In with a JWT", new Version(3, 17), TableauDeployments.Both,
        idempotent: false);

    /// <summary>Sign In with a unified access token's JWT, <c>isUat</c>: Tableau Cloud only, REST API 3.27. Never sent twice.</summary>
    public static readonly Endpoint SignInWithUnifiedAccessToken = SignInAs("Sign In with a unified access token",
        new Version(3, 27), TableauDeployments.Cloud, idempotent: false);

    /// <summary>Sign Out: ends the session whose token it sends, answering 204. Ending it twice ends it once.</summary>
    public static readonly Endpoint SignOut = new("Sign Out", HttpMethod.Post, "{version}/auth/signout", new Version(1, 0),
        TableauDeployments.Both, Idempotent: true, TransferKind.None)
    { NeedsSession = false };

    /// <summary>
    /// Switch Site: a new session on another site, the token sent ending; Tableau Server only. Not sent twice: the second
    /// would send a token the first ended. Its answer holds the new token.
    /// </summary>
    public static readonly Endpoint SwitchSite = new("Switch Site", HttpMethod.Post, "{version}/auth/switchSite", new Version(2, 6),
        TableauDeployments.Server, Idempotent: false, TransferKind.None)
    { NeedsSession = false, KeepsBody = false };

    // Sites. Query Site answers only for the site the session is on; Create, Delete and Query Sites are Tableau Server's.

    /// <summary>Query Site, by the site's LUID (REST API 1.0).</summary>
    public static readonly Endpoint QuerySite = Read("Query Site", "{version}/sites/{id:luid}", 1, 0);

    /// <summary>Query Site, by its content URL, with <c>key=contentUrl</c> (REST API 1.0).</summary>
    public static readonly Endpoint QuerySiteByContentUrl = Read("Query Site by content URL", "{version}/sites/{contentUrl}", 1, 0);

    /// <summary>Query Site, by its name, with <c>key=name</c> (REST API 1.0).</summary>
    public static readonly Endpoint QuerySiteByName = Read("Query Site by name", "{version}/sites/{name}", 1, 0);

    /// <summary>Query Sites: the sites the user can reach (REST API 1.0), Tableau Server only.</summary>
    public static readonly Endpoint QuerySites = Read("Query Sites", "{version}/sites", 1, 0, TableauDeployments.Server);

    /// <summary>Create Site (REST API 1.0), Tableau Server only; a second would fail on the content URL, so it is sent once.</summary>
    public static readonly Endpoint CreateSite = Write("Create Site", HttpMethod.Post, "{version}/sites", 1, 0, idempotent: false,
        TableauDeployments.Server);

    /// <summary>Update Site (REST API 1.0): the same settings set twice are set once.</summary>
    public static readonly Endpoint UpdateSite = Write("Update Site", HttpMethod.Put, "{version}/sites/{id:luid}", 1, 0, idempotent: true);

    /// <summary>Delete Site (REST API 1.0), Tableau Server only; a second answers 404 once the first went through.</summary>
    public static readonly Endpoint DeleteSite = Write("Delete Site", HttpMethod.Delete, "{version}/sites/{id:luid}", 1, 0, idempotent: true,
        TableauDeployments.Server);

    // Projects.

    /// <summary>Query Projects (REST API 2.0).</summary>
    public static readonly Endpoint QueryProjects = Read("Query Projects", "{version}/sites/{siteId}/projects", 2, 0);

    /// <summary>Create Project (REST API 1.0): a create, sent once.</summary>
    public static readonly Endpoint CreateProject = Write("Create Project", HttpMethod.Post, "{version}/sites/{siteId}/projects", 1, 0,
        idempotent: false);

    /// <summary>Update Project (REST API 1.0).</summary>
    public static readonly Endpoint UpdateProject = Write("Update Project", HttpMethod.Put, "{version}/sites/{siteId}/projects/{id:luid}", 1, 0,
        idempotent: true);

    /// <summary>Delete Project (REST API 1.0).</summary>
    public static readonly Endpoint DeleteProject = Write("Delete Project", HttpMethod.Delete, "{version}/sites/{siteId}/projects/{id:luid}", 1, 0,
        idempotent: true);

    // Users.

    /// <summary>Query User On Site (REST API 2.0).</summary>
    public static readonly Endpoint QueryUserOnSite = Read("Query User On Site", "{version}/sites/{siteId}/users/{id:luid}", 2, 0);

    /// <summary>Get Users on Site (REST API 1.0).</summary>
    public static readonly Endpoint GetUsersOnSite = Read("Get Users on Site", "{version}/sites/{siteId}/users", 1, 0);

    /// <summary>Add User to Site (REST API 1.0): it creates the user, or imports one from Active Directory, so it is sent once.</summary>
    public static readonly Endpoint AddUserToSite = Write("Add User to Site", HttpMethod.Post, "{version}/sites/{siteId}/users", 1, 0,
        idempotent: false);

    /// <summary>Update User (REST API 1.0).</summary>
    public static readonly Endpoint UpdateUser = Write("Update User", HttpMethod.Put, "{version}/sites/{siteId}/users/{id:luid}", 1, 0,
        idempotent: true);

    /// <summary>Remove User from Site (REST API 1.0).</summary>
    public static readonly Endpoint RemoveUserFromSite = Write("Remove User from Site", HttpMethod.Delete, "{version}/sites/{siteId}/users/{id:luid}",
        1, 0, idempotent: true);

    /// <summary>Get Groups for a User (REST API 3.7).</summary>
    public static readonly Endpoint GetGroupsForUser = Read("Get Groups for a User", "{version}/sites/{siteId}/users/{userId:luid}/groups", 3, 7);

    // Groups. Create Group and Update Group take an Active Directory group on Tableau Server alone, and the reference
    // gives both from REST API 3.9, when their bodies took the minimum site role. Importing or synchronising a group
    // can take minutes, so it is never sent again after a timeout: a second would run beside the first.

    /// <summary>Query Groups (REST API 2.0).</summary>
    public static readonly Endpoint QueryGroups = Read("Query Groups", "{version}/sites/{siteId}/groups", 2, 0);

    /// <summary>Create Group, a local group (REST API 3.9): a create, sent once.</summary>
    public static readonly Endpoint CreateGroup = Write("Create Group", HttpMethod.Post, "{version}/sites/{siteId}/groups", 3, 9, idempotent: false);

    /// <summary>Create Group, importing an Active Directory group (REST API 3.9), Tableau Server only.</summary>
    public static readonly Endpoint ImportGroup = Write("Create Group from Active Directory", HttpMethod.Post, "{version}/sites/{siteId}/groups",
        3, 9, idempotent: false, TableauDeployments.Server);

    /// <summary>Update Group, a local group (REST API 3.9).</summary>
    public static readonly Endpoint UpdateGroup = Write("Update Group", HttpMethod.Put, "{version}/sites/{siteId}/groups/{id:luid}", 3, 9,
        idempotent: true);

    /// <summary>Update Group, synchronising it with Active Directory (REST API 3.9), Tableau Server only.</summary>
    public static readonly Endpoint SynchronizeGroup = Write("Update Group from Active Directory", HttpMethod.Put,
        "{version}/sites/{siteId}/groups/{id:luid}", 3, 9, idempotent: false, TableauDeployments.Server);

    /// <summary>Delete Group (REST API 2.1).</summary>
    public static readonly Endpoint DeleteGroup = Write("Delete Group", HttpMethod.Delete, "{version}/sites/{siteId}/groups/{id:luid}", 2, 1,
        idempotent: true);

    /// <summary>
    /// Add User to Group (REST API 2.0): a second leaves one membership, answering 409 when the first went through, so it
    /// is sent again like an update.
    /// </summary>
    public static readonly Endpoint AddUserToGroup = Write("Add User to Group", HttpMethod.Post, "{version}/sites/{siteId}/groups/{groupId:luid}/users",
        2, 0, idempotent: true);

    /// <summary>Remove User from Group (REST API 2.0).</summary>
    public static readonly Endpoint RemoveUserFromGroup = Write("Remove User from Group", HttpMethod.Delete,
        "{version}/sites/{siteId}/groups/{groupId:luid}/users/{userId:luid}", 2, 0, idempotent: true);

    /// <summary>Get Users in Group (REST API 2.0).</summary>
    public static readonly Endpoint GetUsersInGroup = Read("Get Users in Group", "{version}/sites/{siteId}/groups/{groupId:luid}/users", 2, 0);

    // Favorites. Adding one is a PUT that sets the favorite's label, so sending it twice sets it once; each kind of
    // content has its own call, from its own version.

    /// <summary>Get Favorites for User (REST API 2.5).</summary>
    public static readonly Endpoint GetFavoritesForUser = Read("Get Favorites for User", "{version}/sites/{siteId}/favorites/{userId:luid}", 2, 5);

    /// <summary>Add Workbook to Favorites (REST API 1.0).</summary>
    public static readonly Endpoint AddWorkbookToFavorites = AddFavorite("Add Workbook to Favorites", 1, 0);

    /// <summary>Add View to Favorites (REST API 1.0).</summary>
    public static readonly Endpoint AddViewToFavorites = AddFavorite("Add View to Favorites", 1, 0);

    /// <summary>Add Data Source to Favorites (REST API 2.3).</summary>
    public static readonly Endpoint AddDataSourceToFavorites = AddFavorite("Add Data Source to Favorites", 2, 3);

    /// <summary>Add Project to Favorites (REST API 3.1).</summary>
    public static readonly Endpoint AddProjectToFavorites = AddFavorite("Add Project to Favorites", 3, 1);

    /// <summary>Add Flow to Favorites (REST API 3.3).</summary>
    public static readonly Endpoint AddFlowToFavorites = AddFavorite("Add Flow to Favorites", 3, 3);

    /// <summary>Delete Workbook from Favorites (REST API 2.0).</summary>
    public static readonly Endpoint DeleteWorkbookFromFavorites = DeleteFavorite("Delete Workbook from Favorites", "workbooks", 2, 0);

    /// <summary>Delete View from Favorites (REST API 2.0).</summary>
    public static readonly Endpoint DeleteViewFromFavorites = DeleteFavorite("Delete View from Favorites", "views", 2, 0);

    /// <summary>Delete Data Source from Favorites (REST API 2.3).</summary>
    public static readonly Endpoint DeleteDataSourceFromFavorites = DeleteFavorite("Delete Data Source from Favorites", "datasources", 2, 3);

    /// <summary>Delete Project from Favorites (REST API 3.1).</summary>
    public static readonly Endpoint DeleteProjectFromFavorites = DeleteFavorite("Delete Project from Favorites", "projects", 3, 1);

    /// <summary>
    /// Delete Flow from Favorites (REST API 3.3), under <c>flows/</c>: the reference's URI repeats Delete Data Source's
    /// <c>datasources/</c>, where its text and Tableau's own Python client name the flow.
    /// </summary>
    public static readonly Endpoint DeleteFlowFromFavorites = DeleteFavorite("Delete Flow from Favorites", "flows", 3, 3);

    /// <summary>Every descriptor, for the tests that hold each to the rules.</summary>
    public static IReadOnlyList<Endpoint> All { get; } =
    [
        ServerInfo, SignIn, SignInImpersonating, SignInWithPersonalAccessToken, SignInWithPersonalAccessTokenImpersonating,
        SignInWithJwt, SignInWithUnifiedAccessToken, SignOut, SwitchSite,
        QuerySite, QuerySiteByContentUrl, QuerySiteByName, QuerySites, CreateSite, UpdateSite, DeleteSite,
        QueryProjects, CreateProject, UpdateProject, DeleteProject,
        QueryUserOnSite, GetUsersOnSite, AddUserToSite, UpdateUser, RemoveUserFromSite, GetGroupsForUser,
        QueryGroups, CreateGroup, ImportGroup, UpdateGroup, SynchronizeGroup, DeleteGroup, AddUserToGroup, RemoveUserFromGroup,
        GetUsersInGroup,
        GetFavoritesForUser, AddWorkbookToFavorites, AddViewToFavorites, AddDataSourceToFavorites, AddProjectToFavorites,
        AddFlowToFavorites, DeleteWorkbookFromFavorites, DeleteViewFromFavorites, DeleteDataSourceFromFavorites,
        DeleteProjectFromFavorites, DeleteFlowFromFavorites,
    ];

    /// <summary>Whether a descriptor is a Sign In's, whose body holds credentials.</summary>
    public static bool IsSignIn(Endpoint endpoint) => endpoint.Template == SignInTemplate;

    private static Endpoint SignInAs(string name, Version minVersion, TableauDeployments deployments, bool idempotent) =>
        new(name, HttpMethod.Post, SignInTemplate, minVersion, deployments, idempotent, TransferKind.None)
        { NeedsSession = false, KeepsBody = false };

    // A GET in a session, safe to send again.
    private static Endpoint Read(string name, string template, int major, int minor,
        TableauDeployments deployments = TableauDeployments.Both) =>
        new(name, HttpMethod.Get, template, new Version(major, minor), deployments, Idempotent: true, TransferKind.None);

    // A call in a session that changes something, safe to send again or not by what it does.
    private static Endpoint Write(string name, HttpMethod method, string template, int major, int minor, bool idempotent,
        TableauDeployments deployments = TableauDeployments.Both) =>
        new(name, method, template, new Version(major, minor), deployments, idempotent, TransferKind.None);

    private static Endpoint AddFavorite(string name, int major, int minor) =>
        Write(name, HttpMethod.Put, "{version}/sites/{siteId}/favorites/{userId:luid}", major, minor, idempotent: true);

    private static Endpoint DeleteFavorite(string name, string kind, int major, int minor) =>
        Write(name, HttpMethod.Delete, $"{{version}}/sites/{{siteId}}/favorites/{{userId:luid}}/{kind}/{{id:luid}}", major, minor, idempotent: true);
}
