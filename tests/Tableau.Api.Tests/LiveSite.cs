using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Tableau.Api.Pipeline;
using Tableau.Api.Rest.Models;

namespace Tableau.Api.Tests;

/// <summary>
/// The live suite's site: its settings, the client it signs in with, and the run's project, which every object a live
/// test makes goes in.
/// </summary>
/// <remarks>
/// <para>
/// Settings, as environment variables set outside the clone (<see cref="TableauSite"/> checks them before a test runs):
/// TABLEAU_LIVE_SERVER_URL, the server's address; TABLEAU_LIVE_SITE, the site's content URL, unset or empty for the
/// default site; and one sign-in: a connected app's direct trust (TABLEAU_LIVE_CLIENT_ID, TABLEAU_LIVE_SECRET_ID,
/// TABLEAU_LIVE_SECRET_VALUE and TABLEAU_LIVE_USER, the user the JWT names), or else a personal access token
/// (TABLEAU_LIVE_PAT_NAME and TABLEAU_LIVE_PAT_SECRET), which signing in with ends any other session of the token.
/// </para>
/// <para>
/// Every live client sends through one HttpClient, which follows no redirect and keeps no cookies, and which keeps each
/// session token it carries, a renewal's among them, for the log check. With TABLEAU_RECORD=1 it sends through a
/// <see cref="FixtureRecorder"/> too. The suite's cleanup disposes it, after the cleanup's own client has signed out.
/// </para>
/// <para>
/// The run's project is named <see cref="ProjectPrefix"/> and the run, made by the first test that needs it and deleted
/// when the suite ends. Before it is made, the suite's projects older than a day, which a run that broke off left behind,
/// are deleted: a top-level project with the suite's description and a run's name, nothing else.
/// </para>
/// </remarks>
internal static partial class LiveSite
{
    public const string ServerUrl = "TABLEAU_LIVE_SERVER_URL";
    public const string Site = "TABLEAU_LIVE_SITE";

    /// <summary>What every run's project is named first.</summary>
    public const string ProjectPrefix = "Tableau.Api live run ";

    /// <summary>The description every run's project has, which the sweep also asks of a project before it deletes one.</summary>
    public const string ProjectDescription = "Made by Tableau.Api's live suite; deleted when the run ends.";

    /// <summary>The scopes the suite's connected app asks for: to read content, and to make and delete its projects.</summary>
    public static readonly string[] Scopes = ["tableau:content:read", "tableau:projects:*"];

    public static readonly Endpoint QueryProjects = new("Query Projects", HttpMethod.Get, "{version}/sites/{siteId}/projects", new Version(2, 0),
        TableauDeployments.Both, Idempotent: true, TransferKind.None);

    public static readonly Endpoint CreateProject = new("Create Project", HttpMethod.Post, "{version}/sites/{siteId}/projects", new Version(2, 0),
        TableauDeployments.Both, Idempotent: false, TransferKind.None);

    public static readonly Endpoint DeleteProject = new("Delete Project", HttpMethod.Delete, "{version}/sites/{siteId}/projects/{projectId:luid}",
        new Version(2, 0), TableauDeployments.Both, Idempotent: true, TransferKind.None);

    /// <summary>The settings whose values name the site or the sign-in without being secrets.</summary>
    private static readonly string[] _knownSettings =
    [
        Site, "TABLEAU_LIVE_USER", "TABLEAU_LIVE_PAT_NAME", "TABLEAU_LIVE_CLIENT_ID", "TABLEAU_LIVE_SECRET_ID", "TABLEAU_SERVER_ONLY_SITE",
        "TABLEAU_SERVER_ONLY_USER", "TABLEAU_SERVER_ONLY_OTHER_SITE",
    ];

    private static readonly ConcurrentDictionary<string, byte> _secrets = new(StringComparer.Ordinal);
    private static readonly SemaphoreSlim _projectLock = new(1, 1);
    private static readonly Lazy<HttpClient> _http = new(Http);
    private static string? _projectId;
    private static string? _recorded;

    /// <summary>The run's name: CI's run and attempt, or the time and a random part here.</summary>
    public static string Run { get; } = Environment.GetEnvironmentVariable("GITHUB_RUN_ID") is { Length: > 0 } run
        ? $"{run}-{Environment.GetEnvironmentVariable("GITHUB_RUN_ATTEMPT") ?? "1"}"
        : string.Create(CultureInfo.InvariantCulture, $"local-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{Random.Shared.Next(0x10000):x4}");

    /// <summary>The run's project's name.</summary>
    public static string ProjectName => ProjectPrefix + Run;

    /// <summary>
    /// Every live secret the suite held, its settings' and every session token its clients carried: no log line may hold
    /// one, and a failure that finds one names no line.
    /// </summary>
    public static IReadOnlyCollection<string> Secrets => [.. _secrets.Keys];

    /// <summary>The values of the settings that name the site, its server or the sign-in, set or not.</summary>
    public static IReadOnlyCollection<string> Known =>
    [
        .. _knownSettings.Select(Environment.GetEnvironmentVariable).OfType<string>().Where(value => value.Length > 0),
        .. new[] { ServerUrl, "TABLEAU_SERVER_ONLY_SERVER_URL" }.Select(Environment.GetEnvironmentVariable)
            .Where(value => Uri.TryCreate(value, UriKind.Absolute, out _)).Select(value => new Uri(value!).Host),
    ];

    /// <summary>The folder the recorder wrote to in this run, or null when it recorded nothing.</summary>
    public static string? Recorded => _recorded;

    /// <summary>The site's content URL: unset or empty is the default site.</summary>
    public static string SiteContentUrl => Environment.GetEnvironmentVariable(Site) ?? "";

    public static TableauClientOptions Options() => new()
    {
        ServerUrl = new Uri(TableauSite.Setting(ServerUrl)),
        Site = SiteContentUrl,
    };

    /// <summary>Whether the suite signs in with the connected app, and not a personal access token.</summary>
    public static bool UsesDirectTrust => TableauSite.SignInSettings(Environment.GetEnvironmentVariable) == TableauSite.DirectTrustSettings;

    /// <summary>The credentials the settings give, each secret kept for the log check.</summary>
    public static TableauCredentials Credentials()
    {
        if (UsesDirectTrust)
        {
            var value = Secret("TABLEAU_LIVE_SECRET_VALUE");
            return TableauCredentials.ConnectedAppDirectTrust(TableauSite.Setting("TABLEAU_LIVE_CLIENT_ID"), TableauSite.Setting("TABLEAU_LIVE_SECRET_ID"),
                value, TableauSite.Setting("TABLEAU_LIVE_USER"), Scopes);
        }
        return TableauCredentials.PersonalAccessToken(TableauSite.Setting("TABLEAU_LIVE_PAT_NAME"), Secret("TABLEAU_LIVE_PAT_SECRET"));
    }

    /// <summary>
    /// A client of the site with the credentials given, over the suite's HttpClient: it owns its session and signs it out
    /// when disposed, as one from <see cref="TableauClient.Create"/> does.
    /// </summary>
    public static TableauClient Client(TableauCredentials? credentials, TableauClientOptions? options = null) =>
        new(_http.Value, options ?? Options(), credentials, Fixtures.Log.Factory);

    /// <summary>Keeps a session's token for the log check, and returns the session.</summary>
    public static TableauSession Saw(TableauSession session)
    {
        if (session.Token is { } token) _secrets.TryAdd(token, 0);
        return session;
    }

    /// <summary>What a failed sign-in may mean, for an assertion's message: never a setting's value.</summary>
    public static string SignInHint => UsesDirectTrust
        ? "the connected app refused the JWT: check that it is enabled, that its secret is current, and that TABLEAU_LIVE_USER is the "
            + "user's name as Tableau Server shows it; with Active Directory or LDAP that may need the domain, as the server's users "
            + "page shows it, such as domain\\user or domain/user"
        : "the personal access token was refused: check that it is current and not revoked";

    /// <summary>
    /// The run's project, made by the first caller after the leftovers of earlier runs are swept, in the session of the
    /// client given.
    /// </summary>
    public static async Task<string> ProjectAsync(TableauClient client, CancellationToken cancellationToken = default)
    {
        await _projectLock.WaitAsync(cancellationToken);
        try
        {
            if (_projectId is { } id) return id;
            await SweepAsync(client, cancellationToken);
            var made = await client.Pipeline.SendAsync(CreateProject, new TableauRequest(Body: new TsRequest
            {
                Project = new Project { Name = ProjectName, Description = ProjectDescription },
            }), static body => body.Project, "project", cancellationToken);
            return _projectId = made.Id ?? throw new InvalidOperationException("Create Project answered no id.");
        }
        finally
        {
            _projectLock.Release();
        }
    }

    /// <summary>
    /// Whether a project is one of the suite's: top-level, with the suite's description and a run's name, the prefix and
    /// then CI's run and attempt or a local run's time and random part. Any other project is left alone, whatever its name.
    /// </summary>
    public static bool IsRunProject(Project project) =>
        project.ParentProjectId is null && project.Description == ProjectDescription && project.Name is { } name && RunName().IsMatch(name);

    /// <summary>Whether the sweep deletes a project: one of the suite's, made more than a day before <paramref name="now"/>.</summary>
    public static bool IsLeftover(Project project, DateTimeOffset now) =>
        IsRunProject(project) && project.Id is not null && project.CreatedAt is { } created && created < now.AddDays(-1);

    /// <summary>Every project of the suite's (<see cref="IsRunProject"/>), page by page.</summary>
    /// <remarks>
    /// Tableau Server lists a project from its search index: a project just made shows a second or so later, and one just
    /// deleted stays for minutes. <see cref="ListedAsync"/> waits for a new one, and the sweep passes over one that is
    /// gone already.
    /// </remarks>
    public static async Task<List<Project>> RunProjectsAsync(TableauClient client, CancellationToken cancellationToken = default)
    {
        var found = new List<Project>();
        for (var page = 1; ; page++)
        {
            var body = await client.Pipeline.SendAsync(QueryProjects, new TableauRequest(Query: ClientFixtures.Query(
                ("pageSize", "1000"), ("pageNumber", page.ToString(CultureInfo.InvariantCulture)))),
                static body => body, "projects", cancellationToken);
            var projects = body.Projects ?? [];
            found.AddRange(projects.Where(IsRunProject));
            if (projects.Count < 1000 || page * 1000 >= (body.Pagination?.TotalAvailable ?? 0)) return found;
        }
    }

    /// <summary>The run's project as the listing shows it, waited for up to 30 seconds while Tableau's index catches up.</summary>
    public static async Task<Project> ListedAsync(TableauClient client, string projectId)
    {
        for (var waited = 0; ; waited++)
        {
            if ((await RunProjectsAsync(client)).Find(project => project.Id == projectId) is { } listed) return listed;
            if (waited == 30) throw new InvalidOperationException("The run's project wasn't listed within 30 seconds of being made.");
            await Task.Delay(TimeSpan.FromSeconds(1));
        }
    }

    /// <summary>Deletes the run's project, if a test made it; the suite's cleanup calls it once, at its end.</summary>
    public static async Task CleanupAsync()
    {
        if (_projectId is not { } id) return;
        await using var client = Client(Credentials());
        await client.Pipeline.SendAsync(DeleteProject, new TableauRequest(PathValues: [id]), default);
        if (client.Pipeline.State.Sessions.Current is { } session) Saw(session);
        _projectId = null;
    }

    /// <summary>Disposes the suite's HttpClient, once every live client has signed out through it.</summary>
    public static void DisposeHttp()
    {
        if (_http.IsValueCreated) _http.Value.Dispose();
    }

    /// <summary>A setting that holds a secret, kept for the log check.</summary>
    internal static string Secret(string name)
    {
        var value = TableauSite.Setting(name);
        _secrets.TryAdd(value, 0);
        return value;
    }

    // Projects a run left behind, more than a day old, so one still running elsewhere keeps its own.
    private static async Task SweepAsync(TableauClient client, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var leftover in (await RunProjectsAsync(client, cancellationToken)).Where(project => IsLeftover(project, now)))
        {
            try
            {
                await client.Pipeline.SendAsync(DeleteProject, new TableauRequest(PathValues: [leftover.Id!]), cancellationToken);
            }
            catch (TableauException e) when (e.Kind == TableauErrorKind.NotFound)
            {
                // Deleted already: the index still listed it.
            }
        }
    }

    // One chain for every live client: the token watcher outermost, then the recorder when recording, then the connection.
    private static HttpClient Http()
    {
        HttpMessageHandler? connection = null;
        HttpMessageHandler? chain = null;
        try
        {
            connection = new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false };
            chain = connection;
            connection = null;
            if (Environment.GetEnvironmentVariable("TABLEAU_RECORD") == "1")
            {
                _recorded = Environment.GetEnvironmentVariable("TABLEAU_RECORD_DIR") is { Length: > 0 } dir
                    ? dir
                    : Path.Combine(AppContext.BaseDirectory, "recorded");
                chain = new FixtureRecorder(_recorded, new FixtureScrubber([new Uri(TableauSite.Setting(ServerUrl)).Host], Known))
                {
                    InnerHandler = chain,
                };
            }
            chain = new TokenWatcher(token => _secrets.TryAdd(token, 0)) { InnerHandler = chain };
            var http = new HttpClient(chain, disposeHandler: true) { Timeout = Timeout.InfiniteTimeSpan };
            chain = null;
            return http;
        }
        finally
        {
            connection?.Dispose();
            chain?.Dispose();
        }
    }

    [GeneratedRegex(@"^Tableau\.Api live run (?:[0-9]+-[0-9]+|local-[0-9]{8}-[0-9]{6}-[0-9a-f]{4})$", RegexOptions.CultureInvariant)]
    private static partial Regex RunName();

    /// <summary>Keeps each session token a request carries, in <c>X-Tableau-Auth</c>.</summary>
    private sealed class TokenWatcher(Action<string> saw) : DelegatingHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Headers.TryGetValues("X-Tableau-Auth", out var tokens))
            {
                foreach (var token in tokens)
                    saw(token);
            }
            return base.SendAsync(request, cancellationToken);
        }
    }
}
