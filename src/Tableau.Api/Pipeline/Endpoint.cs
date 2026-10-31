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
    // descriptor, and its gates refuse a kind the server lacks before the credentials are read. Sending one twice leaves
    // at most a session nobody uses, which ends on its own, or, with a personal access token, ends the first: a retry is
    // safe. Its answer holds the session's token, so no exception keeps it.

    /// <summary>Sign In with a user's name and password (REST API 1.0).</summary>
    public static readonly Endpoint SignIn = SignInAs("Sign In", new Version(1, 0), TableauDeployments.Both);

    /// <summary>Sign In with a name and password, impersonating another user: Tableau Server only.</summary>
    public static readonly Endpoint SignInImpersonating = SignInAs("Sign In, impersonating a user", new Version(1, 0),
        TableauDeployments.Server);

    /// <summary>Sign In with a personal access token (REST API 3.6, Tableau Server 2019.4).</summary>
    public static readonly Endpoint SignInWithPersonalAccessToken = SignInAs("Sign In with a personal access token",
        new Version(3, 6), TableauDeployments.Both);

    /// <summary>Sign In with a personal access token, impersonating another user: Tableau Server only.</summary>
    public static readonly Endpoint SignInWithPersonalAccessTokenImpersonating = SignInAs(
        "Sign In with a personal access token, impersonating a user", new Version(3, 6), TableauDeployments.Server);

    /// <summary>
    /// Sign In with a JWT: a connected app's, by direct trust or OAuth 2.0 trust (REST API 3.17, Tableau Server 2022.3;
    /// Tableau Cloud from 3.16).
    /// </summary>
    public static readonly Endpoint SignInWithJwt = SignInAs("Sign In with a JWT", new Version(3, 17), TableauDeployments.Both);

    /// <summary>Sign In with a unified access token's JWT, <c>isUat</c>: Tableau Cloud only, REST API 3.27.</summary>
    public static readonly Endpoint SignInWithUnifiedAccessToken = SignInAs("Sign In with a unified access token",
        new Version(3, 27), TableauDeployments.Cloud);

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

    /// <summary>Every descriptor, for the tests that hold each to the rules.</summary>
    public static IReadOnlyList<Endpoint> All { get; } =
    [
        ServerInfo, SignIn, SignInImpersonating, SignInWithPersonalAccessToken, SignInWithPersonalAccessTokenImpersonating,
        SignInWithJwt, SignInWithUnifiedAccessToken, SignOut, SwitchSite,
    ];

    private static Endpoint SignInAs(string name, Version minVersion, TableauDeployments deployments) =>
        new(name, HttpMethod.Post, SignInTemplate, minVersion, deployments, Idempotent: true, TransferKind.None)
        { NeedsSession = false, KeepsBody = false };
}
