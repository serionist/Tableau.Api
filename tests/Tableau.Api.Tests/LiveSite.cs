using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
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
/// The run's project is named <see cref="ProjectPrefix"/> and the run, made by the first test that needs it and deleted
/// when the suite ends. Before it is made, projects with the prefix older than a day, which a run that broke off left
/// behind, are deleted. With TABLEAU_RECORD=1 every call goes through a <see cref="FixtureRecorder"/>.
/// </para>
/// </remarks>
internal static class LiveSite
{
    public const string ServerUrl = "TABLEAU_LIVE_SERVER_URL";
    public const string Site = "TABLEAU_LIVE_SITE";

    /// <summary>What every run's project is named first, so the sweep finds a run's leftovers and nothing else.</summary>
    public const string ProjectPrefix = "Tableau.Api live run ";

    /// <summary>The scopes the suite's connected app asks for: to read content, and to make and delete its projects.</summary>
    public static readonly string[] Scopes = ["tableau:content:read", "tableau:projects:*"];

    public static readonly Endpoint QueryProjects = new("Query Projects", HttpMethod.Get, "{version}/sites/{siteId}/projects", new Version(2, 0),
        TableauDeployments.Both, Idempotent: true, TransferKind.None);

    public static readonly Endpoint CreateProject = new("Create Project", HttpMethod.Post, "{version}/sites/{siteId}/projects", new Version(2, 0),
        TableauDeployments.Both, Idempotent: false, TransferKind.None);

    public static readonly Endpoint DeleteProject = new("Delete Project", HttpMethod.Delete, "{version}/sites/{siteId}/projects/{projectId:luid}",
        new Version(2, 0), TableauDeployments.Both, Idempotent: true, TransferKind.None);

    private static readonly ConcurrentBag<string> _secrets = [];
    private static readonly SemaphoreSlim _projectLock = new(1, 1);
    private static readonly Lazy<HttpClient> _recording = new(Recording);
    private static string? _projectId;

    /// <summary>The run's name: CI's run and attempt, or the time and a random part here.</summary>
    public static string Run { get; } = Environment.GetEnvironmentVariable("GITHUB_RUN_ID") is { Length: > 0 } run
        ? $"{run}-{Environment.GetEnvironmentVariable("GITHUB_RUN_ATTEMPT") ?? "1"}"
        : string.Create(CultureInfo.InvariantCulture, $"local-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{Random.Shared.Next(0x10000):x4}");

    /// <summary>The run's project's name.</summary>
    public static string ProjectName => ProjectPrefix + Run;

    /// <summary>
    /// Every live secret the suite held, its settings' and its sessions' tokens: no log line may hold one, and a failure
    /// that finds one names no line.
    /// </summary>
    public static IReadOnlyCollection<string> Secrets => _secrets;

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
    /// A client of the site, with the credentials given: built by <see cref="TableauClient.Create"/>, or with
    /// TABLEAU_RECORD=1 over the recorder's HttpClient.
    /// </summary>
    public static TableauClient Client(TableauCredentials? credentials) =>
        Environment.GetEnvironmentVariable("TABLEAU_RECORD") == "1"
            ? new TableauClient(_recording.Value, Options(), credentials, Fixtures.Log.Factory)
            : TableauClient.Create(Options(), credentials, Fixtures.Log.Factory);

    /// <summary>Keeps a session's token for the log check, and returns the session.</summary>
    public static TableauSession Saw(TableauSession session)
    {
        if (session.Token is { } token) _secrets.Add(token);
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
    /// client given, which must have credentials.
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
                Project = new Project { Name = ProjectName, Description = "Made by Tableau.Api's live suite; deleted when the run ends." },
            }), static body => body.Project, "project", cancellationToken);
            return _projectId = made.Id ?? throw new InvalidOperationException("Create Project answered no id.");
        }
        finally
        {
            _projectLock.Release();
        }
    }

    /// <summary>Every project whose name starts with the prefix, page by page.</summary>
    /// <remarks>
    /// Tableau Server lists a project from its search index, which takes a second or so to show one just made and to drop
    /// one just deleted: the sweep, which takes projects a day old, never meets that, and <see cref="ListedAsync"/> waits.
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
            found.AddRange(projects.Where(project => project.Name?.StartsWith(ProjectPrefix, StringComparison.Ordinal) == true));
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
        _projectId = null;
        if (_recording.IsValueCreated) _recording.Value.Dispose();
    }

    // Projects a run left behind, more than a day old, so one still running elsewhere keeps its own.
    private static async Task SweepAsync(TableauClient client, CancellationToken cancellationToken)
    {
        var dayAgo = DateTimeOffset.UtcNow.AddDays(-1);
        foreach (var leftover in await RunProjectsAsync(client, cancellationToken))
        {
            if (leftover.CreatedAt is not { } created || created >= dayAgo || leftover.Id is not { } id) continue;
            try
            {
                await client.Pipeline.SendAsync(DeleteProject, new TableauRequest(PathValues: [id]), cancellationToken);
            }
            catch (TableauException e) when (e.Kind == TableauErrorKind.NotFound)
            {
                // Deleted already: the index still listed it.
            }
        }
    }

    /// <summary>A setting that holds a secret, kept for the log check.</summary>
    internal static string Secret(string name)
    {
        var value = TableauSite.Setting(name);
        _secrets.Add(value);
        return value;
    }

    private static HttpClient Recording()
    {
        var folder = Environment.GetEnvironmentVariable("TABLEAU_RECORD_DIR") is { Length: > 0 } dir
            ? dir
            : Path.Combine(AppContext.BaseDirectory, "recorded");
        var host = new Uri(TableauSite.Setting(ServerUrl)).Host;
        FixtureRecorder? recorder = null;
        try
        {
            recorder = new FixtureRecorder(folder, new FixtureScrubber([host]))
            {
                InnerHandler = new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false },
            };
            var http = new HttpClient(recorder, disposeHandler: true) { Timeout = Timeout.InfiniteTimeSpan };
            recorder = null;
            return http;
        }
        finally
        {
            recorder?.Dispose();
        }
    }
}
