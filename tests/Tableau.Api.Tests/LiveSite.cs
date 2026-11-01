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
using Group = Tableau.Api.Rest.Models.Group;

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
/// The run's project is named <see cref="ProjectPrefix"/> and the run, made by the first test that needs it, with
/// Tableau's sample workbooks and their data source published into it, and deleted with them when the suite ends. Before
/// it is made, the suite's projects older than a day, which a run that broke off left behind, are deleted, their samples
/// with them: a top-level project with the suite's description and a run's name, nothing else.
/// </para>
/// <para>
/// A group the suite makes is local, and named after the run and the second it was made, since Tableau's groups have no
/// description or date of their own: <see cref="GroupName"/>. The suite deletes it when done with it, or when it ends;
/// before it makes one, it deletes the local groups older than a day whose names have that exact form, nothing else.
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

    /// <summary>
    /// The scopes the suite's connected app asks for: to read content and users, the Metadata API's queries and the
    /// revision lists among the reads; to make, change and delete its projects and groups; to read, add and delete a
    /// permission on its project; to update, download and delete its sample workbooks and data source, their revisions
    /// among them, export their views and run their extract refreshes; and to tag them, one at a time and in a batch.
    /// </summary>
    public static readonly string[] Scopes =
    [
        "tableau:content:read", "tableau:projects:*", "tableau:groups:*", "tableau:users:read", "tableau:permissions:read",
        "tableau:permissions:update", "tableau:permissions:delete", "tableau:workbooks:update", "tableau:workbooks:download",
        "tableau:workbooks:delete", "tableau:views:download", "tableau:datasources:update", "tableau:datasources:download",
        "tableau:datasources:delete", "tableau:tasks:run", "tableau:workbook_tags:update", "tableau:workbook_tags:delete",
        "tableau:tags:batch_create", "tableau:tags:batch_delete",
    ];

    /// <summary>The settings whose values name the site or the sign-in without being secrets.</summary>
    private static readonly string[] _knownSettings =
    [
        Site, "TABLEAU_LIVE_USER", "TABLEAU_LIVE_PAT_NAME", "TABLEAU_LIVE_CLIENT_ID", "TABLEAU_LIVE_SECRET_ID", "TABLEAU_SERVER_ONLY_SITE",
        "TABLEAU_SERVER_ONLY_USER", "TABLEAU_SERVER_ONLY_OTHER_SITE", "TABLEAU_SERVER_ONLY_AD_GROUP", "TABLEAU_SERVER_ONLY_AD_DOMAIN",
        "TABLEAU_SERVER_ONLY_NEW_USER",
    ];

    private static readonly ConcurrentDictionary<string, byte> _secrets = new(StringComparer.Ordinal);
    private static readonly SemaphoreSlim _projectLock = new(1, 1);
    private static readonly SemaphoreSlim _groupLock = new(1, 1);
    private static readonly ConcurrentDictionary<string, byte> _groups = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentQueue<string> _fileHeaders = new();
    private static readonly SemaphoreSlim _samplesLock = new(1, 1);
    private static IReadOnlyList<Workbook>? _samples;
    private static readonly Lazy<HttpClient> _http = new(Http);
    private static string? _projectId;
    private static bool _groupsSwept;
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
    /// client given, with Tableau's sample workbooks published into it: the content of the suite's own that its workbook,
    /// view and tag tests work on. Deleting the project deletes them.
    /// </summary>
    public static async Task<string> ProjectAsync(TableauClient client, CancellationToken cancellationToken = default)
    {
        await _projectLock.WaitAsync(cancellationToken);
        try
        {
            if (_projectId is { } id) return id;
            await SweepAsync(client, cancellationToken);
            var made = await client.Projects.CreateAsync(new Project { Name = ProjectName, Description = ProjectDescription }, publishSamples: true,
                cancellationToken);
            return _projectId = made.Id ?? throw new InvalidOperationException("Create Project answered no id.");
        }
        finally
        {
            _projectLock.Release();
        }
    }

    /// <summary>
    /// The sample workbooks Tableau published into the run's project, by name, once it lists <see cref="Samples"/> of them,
    /// found once a run. Read with a filter on the run's project's name, so no other content is listed.
    /// </summary>
    public static async Task<IReadOnlyList<Workbook>> SamplesAsync(TableauClient client, CancellationToken cancellationToken = default)
    {
        await _samplesLock.WaitAsync(cancellationToken);
        try
        {
            // Found once a run, so a test that deletes a sample leaves the others' list as it was.
            return _samples ??= await PublishedSamplesAsync(client, cancellationToken);
        }
        finally
        {
            _samplesLock.Release();
        }
    }

    /// <summary>How long the samples took to be listed, once <see cref="SamplesAsync"/> found them.</summary>
    public static TimeSpan? SamplesTook { get; private set; }

    // Tableau Server 2025.1 publishes its two samples before it answers Create Project, and lists them at once; the wait is
    // for a server whose search index lags.
    private static async Task<IReadOnlyList<Workbook>> PublishedSamplesAsync(TableauClient client, CancellationToken cancellationToken)
    {
        var project = await ProjectAsync(client, cancellationToken);
        var options = new WorkbookListOptions { Filter = TableauFilter.Eq(WorkbookField.ProjectName, ProjectName) };
        var started = DateTimeOffset.UtcNow;
        var samples = await EventuallyAsync(async () =>
        {
            var found = new List<Workbook>();
            await foreach (var workbook in client.Workbooks.ListAsync(options, cancellationToken))
            {
                if (workbook.Project?.Id == project) found.Add(workbook);
            }
            return found.Count >= Samples ? found : null;
        }, $"{Samples} sample workbooks in the run's project", TimeSpan.FromMinutes(2));
        SamplesTook = DateTimeOffset.UtcNow - started;
        return [.. samples.OrderBy(workbook => workbook.Name, StringComparer.Ordinal)];
    }

    /// <summary>How many sample workbooks Tableau Server 2025.1 publishes into a project, the fewest the suite takes.</summary>
    public const int Samples = 2;

    /// <summary>
    /// The Content-Type and Content-Disposition of each answer that wasn't JSON, by its method and path, the LUIDs in it
    /// written as <c>{id}</c>: what Tableau sends for a file, which the live suite records.
    /// </summary>
    public static IReadOnlyCollection<string> FileHeaders => [.. _fileHeaders];

    /// <summary>
    /// Whether a project is one of the suite's: top-level, with the suite's description and a run's name, the prefix and
    /// then CI's run and attempt or a local run's time and random part. Any other project is left alone, whatever its name.
    /// </summary>
    public static bool IsRunProject(Project project) =>
        project.ParentProjectId is null && project.Description == ProjectDescription && project.Name is { } name && RunName().IsMatch(name);

    /// <summary>Whether the sweep deletes a project: one of the suite's, made more than a day before <paramref name="now"/>.</summary>
    public static bool IsLeftover(Project project, DateTimeOffset now) =>
        IsRunProject(project) && project.Id is not null && project.CreatedAt is { } created && created < now.AddDays(-1);

    /// <summary>Every project of the suite's (<see cref="IsRunProject"/>), from the top-level projects, a page of 1,000 at a time.</summary>
    /// <remarks>
    /// Tableau Server lists a project from its search index: a project just made shows a second or so later, and one just
    /// deleted stays for minutes. <see cref="ListedAsync"/> waits for a new one, and the sweep passes over one that is
    /// gone already.
    /// </remarks>
    public static async Task<List<Project>> RunProjectsAsync(TableauClient client, CancellationToken cancellationToken = default)
    {
        var found = new List<Project>();
        var options = new ProjectListOptions { Filter = TableauFilter.Eq(ProjectField.TopLevelProject, true), PageSize = 1000 };
        await foreach (var project in client.Projects.ListAsync(options, cancellationToken))
        {
            if (IsRunProject(project)) found.Add(project);
        }
        return found;
    }

    /// <summary>The run's project as the listing shows it, waited for up to 30 seconds while Tableau's index catches up.</summary>
    public static Task<Project> ListedAsync(TableauClient client, string projectId) =>
        EventuallyAsync(async () => (await RunProjectsAsync(client)).Find(project => project.Id == projectId), "The run's project");

    /// <summary>
    /// What <paramref name="find"/> finds, asked once a second for up to <paramref name="within"/>, 30 seconds unless
    /// given, while Tableau's index catches up with a change.
    /// </summary>
    public static async Task<T> EventuallyAsync<T>(Func<Task<T?>> find, string what, TimeSpan? within = null)
        where T : class
    {
        var wait = within ?? TimeSpan.FromSeconds(30);
        var deadline = DateTimeOffset.UtcNow + wait;
        while (true)
        {
            if (await find() is { } found) return found;
            if (DateTimeOffset.UtcNow > deadline)
                throw new InvalidOperationException(string.Create(CultureInfo.InvariantCulture, $"{what} wasn't listed within {wait.TotalSeconds:F0} seconds."));
            await Task.Delay(TimeSpan.FromSeconds(1));
        }
    }

    /// <summary>A group's name for the run, made at <paramref name="made"/>: the run's name and the second, in UTC.</summary>
    public static string GroupName(DateTimeOffset made) =>
        string.Create(CultureInfo.InvariantCulture, $"{ProjectName} {made.UtcDateTime:yyyyMMdd'T'HHmmss'Z'}");

    /// <summary>
    /// Whether a group is one of the suite's: local, and named as <see cref="GroupName"/> names one, a run's name and then
    /// the second it was made. Any other group is left alone, whatever its name.
    /// </summary>
    public static bool IsRunGroup(Group group) =>
        group.Domain?.Name == "local" && group.Name is { } name && RunGroupName().IsMatch(name);

    /// <summary>
    /// Whether the sweep deletes a group: one of the suite's, its name saying it was made more than a day before
    /// <paramref name="now"/>.
    /// </summary>
    public static bool IsLeftover(Group group, DateTimeOffset now) =>
        IsRunGroup(group) && group.Id is not null
        && DateTimeOffset.TryParseExact(RunGroupName().Match(group.Name!).Groups["made"].Value, "yyyyMMdd'T'HHmmss'Z'",
            CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var made)
        && made < now.AddDays(-1);

    /// <summary>
    /// A local group for the run, made in the session of the client given, which the suite deletes when it ends unless a
    /// test deleted it and said so (<see cref="GroupGone"/>); first, once a run, the groups earlier runs left are deleted.
    /// </summary>
    public static async Task<Group> NewGroupAsync(TableauClient client, CancellationToken cancellationToken = default)
    {
        await _groupLock.WaitAsync(cancellationToken);
        try
        {
            if (!_groupsSwept)
            {
                await SweepGroupsAsync(client, cancellationToken);
                _groupsSwept = true;
            }
            var made = await client.Groups.CreateAsync(GroupName(DateTimeOffset.UtcNow), cancellationToken: cancellationToken);
            _groups.TryAdd(made.Id ?? throw new InvalidOperationException("Create Group answered no id."), 0);
            return made;
        }
        finally
        {
            _groupLock.Release();
        }
    }

    /// <summary>Takes a group a test deleted off the cleanup's list.</summary>
    public static void GroupGone(string id) => _groups.TryRemove(id, out _);

    /// <summary>
    /// Deletes the groups the run made and didn't delete, then the run's project, if a test made them; the suite's cleanup
    /// calls it once, at its end.
    /// </summary>
    public static async Task CleanupAsync()
    {
        if (_projectId is null && _groups.IsEmpty) return;
        await using var client = Client(Credentials());
        // Each goes whatever happened to the one before; the first failure is thrown once all were tried.
        var failures = new List<Exception>();
        foreach (var group in _groups.Keys)
            await TryAsync(() => client.Groups.DeleteAsync(group), failures);
        _groups.Clear();
        if (_projectId is { } id)
            await TryAsync(() => client.Projects.DeleteAsync(id), failures);
        _projectId = null;
        if (client.Pipeline.State.Sessions.Current is { } session) Saw(session);
        if (failures.Count > 0) throw failures[0];

        static async Task TryAsync(Func<Task> delete, List<Exception> failures)
        {
            try
            {
                await delete();
            }
            catch (TableauException e)
            {
                failures.Add(e);
            }
        }
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
                await client.Projects.DeleteAsync(leftover.Id!, cancellationToken);
            }
            catch (TableauException e) when (e.Kind == TableauErrorKind.NotFound)
            {
                // Deleted already: the index still listed it.
            }
        }
    }

    // Groups a run left behind, more than a day old by their names, so one still running elsewhere keeps its own.
    private static async Task SweepGroupsAsync(TableauClient client, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var leftovers = new List<Group>();
        var options = new GroupListOptions { Filter = TableauFilter.Eq(GroupField.IsLocal, true), PageSize = 1000 };
        await foreach (var group in client.Groups.ListAsync(options, cancellationToken))
        {
            if (IsLeftover(group, now)) leftovers.Add(group);
        }
        foreach (var leftover in leftovers)
        {
            try
            {
                await client.Groups.DeleteAsync(leftover.Id!, cancellationToken);
            }
            catch (TableauException e) when (e.Kind == TableauErrorKind.NotFound)
            {
                // Deleted already, between the list and the delete.
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
            chain = new FileHeaderWatcher(_fileHeaders.Enqueue) { InnerHandler = chain };
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

    [GeneratedRegex(@"^Tableau\.Api live run (?:[0-9]+-[0-9]+|local-[0-9]{8}-[0-9]{6}-[0-9a-f]{4}) (?<made>[0-9]{8}T[0-9]{6}Z)$",
        RegexOptions.CultureInvariant)]
    private static partial Regex RunGroupName();

    [GeneratedRegex("[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}", RegexOptions.CultureInvariant)]
    private static partial Regex Luid();

    [GeneratedRegex("filename(\\*?)=\"?[^\";]*?(\\.[A-Za-z0-9]{1,8})?\"?(;|$)", RegexOptions.CultureInvariant)]
    private static partial Regex FileName();

    /// <summary>
    /// Keeps the shape of each answer that isn't JSON: its method, its path with every LUID as <c>{id}</c> and every query
    /// value hidden, its status, its Content-Type and its Content-Disposition with each file name's stem as <c>name</c>.
    /// </summary>
    private sealed class FileHeaderWatcher(Action<string> saw) : DelegatingHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = await base.SendAsync(request, cancellationToken);
            var type = response.Content.Headers.ContentType?.ToString();
            if (response.IsSuccessStatusCode && response.StatusCode != System.Net.HttpStatusCode.NoContent
                && type?.Contains("json", StringComparison.OrdinalIgnoreCase) != true)
            {
                var path = Luid().Replace(RequestUri.Redacted(request.RequestUri!), "{id}");
                var disposition = response.Content.Headers.TryGetValues("Content-Disposition", out var values) ? string.Join(", ", values) : null;
                saw(string.Create(CultureInfo.InvariantCulture,
                    $"{request.Method} {path} {(int)response.StatusCode}: Content-Type {type ?? "(none)"}; Content-Disposition "
                    + $"{(disposition is null ? "(none)" : FileName().Replace(disposition, "filename$1=\"name$2\"$3"))}"));
            }
            return response;
        }
    }

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
