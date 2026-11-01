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
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Tableau.Api.Pipeline;
using Tableau.Api.Rest.Models;
using Group = Tableau.Api.Rest.Models.Group;

namespace Tableau.Api.Tests;

/// <summary>
/// The live suite's server: its settings, its two sign-ins, and the site the run makes for itself, which every content
/// test works in, and the run's project there, with Tableau's samples.
/// </summary>
/// <remarks>
/// <para>
/// Settings, as environment variables set outside the clone (<see cref="TableauSite"/> checks them before a test runs):
/// TABLEAU_LIVE_SERVER_URL, the server's address; TABLEAU_LIVE_SITE, the content URL of the site the token signs in to
/// first, unset or empty for the default site; and two sign-ins, each with its own job. A personal access token
/// (TABLEAU_LIVE_PAT_NAME and TABLEAU_LIVE_PAT_SECRET, <see cref="Credentials"/>), a server administrator's, does every
/// content test's work; it allows one session at a time, at any site, so signing in with it ends any other session of the
/// token, and a local run takes another token than CI's. A connected app's direct trust (TABLEAU_LIVE_CLIENT_ID,
/// TABLEAU_LIVE_SECRET_ID, TABLEAU_LIVE_SECRET_VALUE and TABLEAU_LIVE_USER, the user the JWT names;
/// <see cref="ConnectedApp"/> and <see cref="Jwt"/>) signs in for its own tests alone, on TABLEAU_LIVE_SITE, which read
/// and change nothing but a page of projects.
/// </para>
/// <para>
/// Every live client sends through one HttpClient, which follows no redirect and keeps no cookies, and which keeps each
/// session token it carries, a renewal's among them, for the log check. With TABLEAU_RECORD=1 it sends through a
/// <see cref="FixtureRecorder"/> too. Every live client logs through <see cref="Record"/>. The suite's cleanup disposes
/// the HttpClient, after the cleanup's own client has signed out.
/// </para>
/// <para>
/// The run's site (<see cref="RunSiteAsync"/>) is made once a run, by the first test that needs it, with revision history
/// and a limit, and named after the run and the second it was made, since a site has no date of its own:
/// <see cref="RunSiteName"/> and <see cref="RunSiteContentUrl"/>. Every content test signs in there
/// (<see cref="RunClientAsync"/>), and the suite deletes it when it ends, everything in it with it. Before it is made, the
/// run sites older than a day, which a run that broke off left behind, are deleted: a site whose name and content URL are
/// a run's exactly (<see cref="IsRunSite"/>), nothing else. One the sweep can't delete is kept and named at the run's end.
/// </para>
/// <para>
/// In the run's site, the run's project (<see cref="ProjectAsync"/>), with Tableau's sample workbooks and their data
/// source published into it, is shared: a test reads it, and changes it only as it changes it back. A test that deletes
/// content takes samples of its own (<see cref="OwnSamplesAsync"/>). A group a test makes is local, and named after the
/// run and the second it was made (<see cref="GroupName"/>); the site's deletion takes it, if the test didn't.
/// </para>
/// </remarks>
internal static partial class LiveSite
{
    public const string ServerUrl = "TABLEAU_LIVE_SERVER_URL";
    public const string Site = "TABLEAU_LIVE_SITE";

    /// <summary>What every run's site, project and groups are named first.</summary>
    public const string ProjectPrefix = "Tableau.Api live run ";

    /// <summary>What every run's site's content URL starts with.</summary>
    public const string SiteContentUrlPrefix = "tableau-api-live-run-";

    /// <summary>The description every project the suite makes has.</summary>
    public const string ProjectDescription = "Made by Tableau.Api's live suite; deleted with the run's site.";

    /// <summary>The revisions the run's site keeps of each workbook and data source, made with revision history on.</summary>
    public const int RevisionLimit = 25;

    /// <summary>
    /// How long Delete Site may take, from the cleanup and the sweep: a site with content can take minutes to delete, and
    /// Delete Site is never sent twice.
    /// </summary>
    public static readonly TimeSpan DeleteSiteTimeout = TimeSpan.FromMinutes(10);

    /// <summary>
    /// The scopes the connected app's JWT asks for: <c>tableau:content:read</c>, for Query Projects, the one call its
    /// tests make in its session. The content tests sign in with the token, since on Tableau Server 2025.1 a connected
    /// app's session can't call a method whose scope came in REST API 3.27, such as the tags and the revision lists.
    /// </summary>
    public static readonly string[] Scopes = ["tableau:content:read"];

    /// <summary>The settings whose values name the site or the sign-in without being secrets.</summary>
    private static readonly string[] _knownSettings =
    [
        Site, "TABLEAU_LIVE_USER", "TABLEAU_LIVE_PAT_NAME", "TABLEAU_LIVE_CLIENT_ID", "TABLEAU_LIVE_SECRET_ID", "TABLEAU_SERVER_ONLY_SITE",
        "TABLEAU_SERVER_ONLY_USER", "TABLEAU_SERVER_ONLY_OTHER_SITE", "TABLEAU_SERVER_ONLY_AD_GROUP", "TABLEAU_SERVER_ONLY_AD_DOMAIN",
        "TABLEAU_SERVER_ONLY_NEW_USER",
    ];

    private static readonly ConcurrentDictionary<string, byte> _secrets = new(StringComparer.Ordinal);
    private static readonly SemaphoreSlim _projectLock = new(1, 1);
    private static readonly ConcurrentQueue<string> _fileHeaders = new();
    private static readonly SemaphoreSlim _samplesLock = new(1, 1);
    private static readonly Lock _siteLock = new();
    private static readonly ConcurrentQueue<string> _kept = new();
    private static IReadOnlyList<Workbook>? _samples;
    private static readonly Lazy<HttpClient> _http = new(Http);
    private static Task<Site>? _runSite;
    private static Site? _made;
    private static string? _projectId;
    private static string? _recorded;

    /// <summary>
    /// What the live clients' calls answered, by the site test that made each, for the matrix's nightly check: every live
    /// client logs through it, and it passes each line on to <see cref="Fixtures.Log"/>, which the log check reads.
    /// </summary>
    public static CallRecord Record { get; } = new(Fixtures.Log);

    /// <summary>The name the suite's own setup calls under in <see cref="Record"/>.</summary>
    public const string SetupName = "LiveSite";

    /// <summary>The run's name: CI's run and attempt, or the time and a random part here.</summary>
    public static string Run { get; } = Environment.GetEnvironmentVariable("GITHUB_RUN_ID") is { Length: > 0 } run
        ? $"{run}-{Environment.GetEnvironmentVariable("GITHUB_RUN_ATTEMPT") ?? "1"}"
        : string.Create(CultureInfo.InvariantCulture, $"local-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{Random.Shared.Next(0x10000):x4}");

    /// <summary>The run's project's name.</summary>
    public static string ProjectName => ProjectPrefix + Run;

    /// <summary>
    /// Every live secret the suite held, its settings', every session token its clients carried and every JWT
    /// <see cref="Jwt"/> minted: no log line may hold one, and a failure that finds one names no line.
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

    /// <summary>The content URL of the site the token signs in to first: unset or empty is the default site.</summary>
    public static string SiteContentUrl => Environment.GetEnvironmentVariable(Site) ?? "";

    /// <summary>Options for TABLEAU_LIVE_SITE, or for the site given.</summary>
    public static TableauClientOptions Options(string? site = null) => new()
    {
        ServerUrl = new Uri(TableauSite.Setting(ServerUrl)),
        Site = site ?? SiteContentUrl,
    };

    /// <summary>The personal access token every content test signs in with, its secret kept for the log check.</summary>
    public static TableauCredentials Credentials() =>
        TableauCredentials.PersonalAccessToken(TableauSite.Setting("TABLEAU_LIVE_PAT_NAME"), Secret("TABLEAU_LIVE_PAT_SECRET"));

    /// <summary>
    /// The connected app's direct trust, asking for <see cref="Scopes"/>: the client mints its JWT at each sign-in. Its
    /// secret value is kept for the log check.
    /// </summary>
    public static TableauCredentials ConnectedApp() =>
        TableauCredentials.ConnectedAppDirectTrust(TableauSite.Setting("TABLEAU_LIVE_CLIENT_ID"), TableauSite.Setting("TABLEAU_LIVE_SECRET_ID"),
            Secret("TABLEAU_LIVE_SECRET_VALUE"), TableauSite.Setting("TABLEAU_LIVE_USER"), Scopes);

    /// <summary>
    /// A JWT sign-in whose JWT the suite mints at each sign-in, as a caller's own code would, with the library's minting
    /// and the connected app's settings: each JWT it gives is kept with the live secrets, so the log check and the
    /// recorder's scan know it.
    /// </summary>
    public static TableauCredentials Jwt()
    {
        var (clientId, secretId) = (TableauSite.Setting("TABLEAU_LIVE_CLIENT_ID"), TableauSite.Setting("TABLEAU_LIVE_SECRET_ID"));
        var (value, user) = (Secret("TABLEAU_LIVE_SECRET_VALUE"), TableauSite.Setting("TABLEAU_LIVE_USER"));
        return TableauCredentials.Jwt(_ =>
        {
            var jwt = ConnectedAppJwt.Mint(clientId, secretId, value, user, Scopes, DateTimeOffset.UtcNow);
            _secrets.TryAdd(jwt, 0);
            return ValueTask.FromResult(jwt);
        });
    }

    /// <summary>
    /// A client of the site with the credentials given, over the suite's HttpClient: it owns its session and signs it out
    /// when disposed, as one from <see cref="TableauClient.Create"/> does.
    /// </summary>
    public static TableauClient Client(TableauCredentials? credentials, TableauClientOptions? options = null) =>
        new(_http.Value, options ?? Options(), credentials, Record.Factory);

    /// <summary>
    /// A client of the run's site with the token, which signs in there, and there again on renewal or after a 401. The
    /// run's site is made first, if no test made it yet, so the setup's sign-in never ends a session a test holds.
    /// </summary>
    public static async Task<TableauClient> RunClientAsync() => Client(Credentials(), await RunOptionsAsync());

    /// <summary>Options for the run's site, made first if no test made it yet.</summary>
    public static async Task<TableauClientOptions> RunOptionsAsync() => Options((await RunSiteAsync()).ContentUrl);

    /// <summary>
    /// The run's site, made once a run with revision history and <see cref="RevisionLimit"/>, after the sweep, from a client
    /// of the token on TABLEAU_LIVE_SITE, which is disposed before anything signs in to the new site. It fails, naming the
    /// site, unless Create Site's answer passes the sweep's own rule (<see cref="IsRunSite"/>), which a name or content URL
    /// Tableau changed would leave out of every later sweep; the cleanup deletes it anyway.
    /// </summary>
    public static Task<Site> RunSiteAsync()
    {
        lock (_siteLock) return _runSite ??= Record.AsAsync(SetupName, MakeRunSiteAsync);
    }

    private static async Task<Site> MakeRunSiteAsync()
    {
        await using var client = Client(Credentials());
        await SweepAsync(client);
        var made = DateTimeOffset.UtcNow;
        var site = await client.Sites.CreateAsync(new Site
        {
            Name = RunSiteName(Run, made),
            ContentUrl = RunSiteContentUrl(Run, made),
            RevisionHistoryEnabled = true,
            RevisionLimit = RevisionLimit,
        });
        // Kept for the cleanup before it is checked, so a site the check refuses is deleted too.
        _made = site;
        return Checked(site);
    }

    /// <summary>
    /// Create Site's answer, when the sweep would take it for a run's site; otherwise an exception naming it, since a site
    /// the sweep can't find would pile up unseen once a run broke off.
    /// </summary>
    public static Site Checked(Site made) => IsRunSite(made) && made.Id is not null
        ? made
        : throw new InvalidOperationException($"Create Site answered the site '{made.Name}' ({made.ContentUrl}), which the sweep wouldn't take for a "
            + "run's: its name or content URL is not as sent. The suite's cleanup deletes it; should that fail, delete it by hand.");

    /// <summary>A run's site's name: <see cref="ProjectPrefix"/>, the run, and the second it was made, in UTC.</summary>
    public static string RunSiteName(string run, DateTimeOffset made) =>
        string.Create(CultureInfo.InvariantCulture, $"{ProjectPrefix}{run} {made.UtcDateTime:yyyyMMdd'T'HHmmss'Z'}");

    /// <summary>
    /// A run's site's content URL: <see cref="SiteContentUrlPrefix"/>, the run, and the second it was made in digits alone,
    /// lower-case letters, digits and hyphens, which Create Site keeps as given.
    /// </summary>
    public static string RunSiteContentUrl(string run, DateTimeOffset made) =>
        string.Create(CultureInfo.InvariantCulture, $"{SiteContentUrlPrefix}{run}-{made.UtcDateTime:yyyyMMddHHmmss}");

    /// <summary>
    /// Whether a site is a run's: its name a run's (<see cref="RunSiteName"/>), exactly, and its content URL the one that
    /// name gives (<see cref="RunSiteContentUrl"/>), exactly, case included. Any other site is left alone, whatever its name.
    /// </summary>
    public static bool IsRunSite(Site site) => Made(site) is not null;

    /// <summary>Whether the sweep deletes a site: a run's, made more than a day before <paramref name="now"/>, by its name.</summary>
    public static bool IsLeftover(Site site, DateTimeOffset now) => site.Id is not null && Made(site) is { } made && made < now.AddDays(-1);

    // The second a run's site was made, by its name, when its name and content URL are a run's; null for any other site.
    private static DateTimeOffset? Made(Site site)
    {
        if (site.Name is not { } name || RunSiteNamed().Match(name) is not { Success: true } match) return null;
        if (!DateTimeOffset.TryParseExact(match.Groups["made"].Value, "yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal, out var made))
            return null;
        return string.Equals(site.ContentUrl, RunSiteContentUrl(match.Groups["run"].Value, made), StringComparison.Ordinal) ? made : null;
    }

    /// <summary>Keeps a session's token for the log check, and returns the session.</summary>
    public static TableauSession Saw(TableauSession session)
    {
        if (session.Token is { } token) _secrets.TryAdd(token, 0);
        return session;
    }

    /// <summary>
    /// What <paramref name="call"/> answers. A session Tableau refuses fails the test with what its code may mean for
    /// <paramref name="signIn"/> (<see cref="Refused"/>), never a setting's value.
    /// </summary>
    public static async Task<T> SignedInAsync<T>(LiveSignIn signIn, Func<Task<T>> call)
    {
        try
        {
            return await call();
        }
        catch (TableauException e) when (e.Kind == TableauErrorKind.Authentication)
        {
            throw new AssertFailedException(Refused(signIn, e.ErrorCode, e.Summary), e);
        }
    }

    /// <summary>
    /// What a refused session may mean, for an assertion's message, never a setting's value: 401001 is a sign-in Tableau
    /// refused; 401002 a call the session may not make. The client signs in again once after a 401 before it gives up, so
    /// a 401002 in the connected app's session is most likely a scope its JWT lacks, such as one that came in a REST API
    /// version above the server's.
    /// </summary>
    public static string Refused(LiveSignIn signIn, string? errorCode, string? summary) => (errorCode, signIn) switch
    {
        ("401001", _) => $"The sign-in failed (401001: {summary}): {SignInHint(signIn)}.",
        ("401002", LiveSignIn.ConnectedApp) => $"Tableau refused a call in the connected app's session (401002: {summary}): its JWT asks "
            + $"for the scopes {string.Join(", ", Scopes)} (LiveSite.Scopes), and the call may need one it doesn't ask for, or one this "
            + "server's version lacks: the reference's scope line for the call says which, and from which REST API version.",
        ("401002", _) => $"Tableau refused a call in the token's session (401002: {summary}): a sign-in elsewhere with the same token ends "
            + "its session, so check that no other run signs in with it at the same time.",
        _ => $"Tableau or the client refused the session ({errorCode ?? "no error code"}: {summary}); if it was the sign-in, "
            + $"{SignInHint(signIn)}.",
    };

    // What to check when a sign-in itself was refused.
    private static string SignInHint(LiveSignIn signIn) => signIn == LiveSignIn.ConnectedApp
        ? "the connected app refused the JWT: check that it is enabled, that its secret is current, and that TABLEAU_LIVE_USER is the "
            + "user's name as Tableau Server shows it; with Active Directory or LDAP that may need the domain, as the server's users "
            + "page shows it, such as domain\\user or domain/user"
        : "the personal access token was refused: check that it is current and not revoked";

    /// <summary>
    /// The run's project in the run's site, made by the first caller, in the session of the client given, with Tableau's
    /// sample workbooks published into it: the content of the suite's own that its workbook, view and tag tests share.
    /// </summary>
    public static async Task<string> ProjectAsync(TableauClient client, CancellationToken cancellationToken = default)
    {
        await _projectLock.WaitAsync(cancellationToken);
        try
        {
            if (_projectId is { } id) return id;
            return _projectId = await Record.AsAsync(SetupName, async () =>
                (await client.Projects.CreateAsync(new Project { Name = ProjectName, Description = ProjectDescription }, publishSamples: true,
                    cancellationToken)).Id ?? throw new InvalidOperationException("Create Project answered no id."));
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
            if (_samples is { } samples) return samples;
            var project = await ProjectAsync(client, cancellationToken);
            var started = DateTimeOffset.UtcNow;
            _samples = await Record.AsAsync(SetupName, () => PublishedSamplesAsync(client, project, ProjectName, cancellationToken));
            SamplesTook = DateTimeOffset.UtcNow - started;
            return _samples;
        }
        finally
        {
            _samplesLock.Release();
        }
    }

    /// <summary>How long the run's samples took to be listed, once <see cref="SamplesAsync"/> found them.</summary>
    public static TimeSpan? SamplesTook { get; private set; }

    /// <summary>
    /// Samples of a test's own, for a test that deletes content: a project in the run's site named after the run and
    /// <paramref name="label"/>, with Tableau's samples published into it, which no other test reads; the site's deletion
    /// takes it.
    /// </summary>
    public static async Task<OwnSamples> OwnSamplesAsync(TableauClient client, string label, CancellationToken cancellationToken = default)
    {
        var name = $"{ProjectName} {label}";
        var project = (await client.Projects.CreateAsync(new Project { Name = name, Description = ProjectDescription }, publishSamples: true,
            cancellationToken)).Id ?? throw new InvalidOperationException("Create Project answered no id.");
        return new OwnSamples(project, name, await PublishedSamplesAsync(client, project, name, cancellationToken));
    }

    // Tableau Server 2025.1 publishes its two samples before it answers Create Project, and lists them at once; the wait is
    // for a server whose search index lags.
    private static async Task<IReadOnlyList<Workbook>> PublishedSamplesAsync(TableauClient client, string project, string projectName,
        CancellationToken cancellationToken)
    {
        var options = new WorkbookListOptions { Filter = TableauFilter.Eq(WorkbookField.ProjectName, projectName) };
        var samples = await EventuallyAsync(async () =>
        {
            var found = new List<Workbook>();
            await foreach (var workbook in client.Workbooks.ListAsync(options, cancellationToken))
            {
                if (workbook.Project?.Id == project) found.Add(workbook);
            }
            return found.Count >= Samples ? found : null;
        }, $"{Samples} sample workbooks in the project '{projectName}'", TimeSpan.FromMinutes(2));
        return [.. samples.OrderBy(workbook => workbook.Name, StringComparer.Ordinal)];
    }

    /// <summary>How many sample workbooks Tableau Server 2025.1 publishes into a project, the fewest the suite takes.</summary>
    public const int Samples = 2;

    /// <summary>
    /// The Content-Type and Content-Disposition of each answer that wasn't JSON, by its method and path, the LUIDs in it
    /// written as <c>{id}</c>: what Tableau sends for a file, which the live suite records.
    /// </summary>
    public static IReadOnlyCollection<string> FileHeaders => [.. _fileHeaders];

    /// <summary>The run's project as a list of the run's site's top-level projects shows it, waited for while Tableau's index catches up.</summary>
    public static Task<Project> ListedAsync(TableauClient client, string projectId) =>
        EventuallyAsync(async () =>
        {
            var options = new ProjectListOptions { Filter = TableauFilter.Eq(ProjectField.Name, ProjectName) };
            await foreach (var project in client.Projects.ListAsync(options))
            {
                if (project.Id == projectId) return project;
            }
            return null;
        }, "The run's project");

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

    /// <summary>A local group for the run, made in the session of the client given; the run's site's deletion takes it.</summary>
    public static async Task<Group> NewGroupAsync(TableauClient client, CancellationToken cancellationToken = default) =>
        await client.Groups.CreateAsync(GroupName(DateTimeOffset.UtcNow), cancellationToken: cancellationToken);

    /// <summary>
    /// Deletes the run's site, if a test made it, from a session on it, as Delete Site asks, with
    /// <see cref="DeleteSiteTimeout"/>; then fails, naming each, for the run's site if it stays and for each leftover the
    /// sweep couldn't delete. The suite's cleanup calls it once, at its end.
    /// </summary>
    public static async Task CleanupAsync()
    {
        var failures = new List<Exception>();
        if (Interlocked.Exchange(ref _made, null) is { } site)
        {
            try
            {
                await using var client = Client(Credentials(), CleanupOptions(site.ContentUrl!));
                await client.Sites.DeleteAsync(site.Id!);
            }
            catch (TableauException e)
            {
                failures.Add(new InvalidOperationException($"The run's site '{site.Name}' ({site.ContentUrl}) is left on the server: the next run's "
                    + "sweep deletes it once it is a day old, or delete it by hand.", e));
            }
        }
        while (_kept.TryDequeue(out var kept))
            failures.Add(new InvalidOperationException(kept));
        if (failures.Count == 1) throw failures[0];
        if (failures.Count > 1) throw new AggregateException("The live suite left sites on the server.", failures);
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

    // A client to delete a site with signs in to it, and gives Delete Site, sent once, its time.
    private static TableauClientOptions CleanupOptions(string site)
    {
        var options = Options(site);
        options.RequestTimeout = DeleteSiteTimeout;
        return options;
    }

    // Run sites a run left behind, more than a day old, so one still running elsewhere keeps its own. One the sweep can't
    // delete is kept, and named when the suite ends, so the run's own site is still made and every test still runs. A
    // sign-in to a site deleted since Query Sites listed it fails too: a site no longer listed is gone.
    private static async Task SweepAsync(TableauClient client)
    {
        var now = DateTimeOffset.UtcNow;
        var leftovers = new List<Site>();
        await foreach (var site in client.Sites.ListAsync())
        {
            if (IsLeftover(site, now)) leftovers.Add(site);
        }
        foreach (var leftover in leftovers)
        {
            try
            {
                await using var there = Client(Credentials(), CleanupOptions(leftover.ContentUrl!));
                await there.Sites.DeleteAsync(leftover.Id!);
            }
            catch (TableauException e)
            {
                var listed = false;
                await foreach (var site in client.Sites.ListAsync())
                    listed |= site.Id == leftover.Id;
                if (listed)
                {
                    _kept.Enqueue($"The sweep couldn't delete the run site '{leftover.Name}' ({leftover.ContentUrl}), left by an earlier run: "
                        + $"{e.Message} Delete it by hand.");
                }
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

    [GeneratedRegex(@"^Tableau\.Api live run (?<run>[0-9]+-[0-9]+|local-[0-9]{8}-[0-9]{6}-[0-9a-f]{4}) (?<made>[0-9]{8}T[0-9]{6}Z)$",
        RegexOptions.CultureInvariant)]
    private static partial Regex RunSiteNamed();

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

/// <summary>A test's own samples (<see cref="LiveSite.OwnSamplesAsync"/>): its project, and the sample workbooks in it, by name.</summary>
/// <param name="ProjectId">The project's LUID.</param>
/// <param name="ProjectName">The project's name, which a list's filter takes.</param>
/// <param name="Workbooks">The sample workbooks, by name.</param>
internal sealed record OwnSamples(string ProjectId, string ProjectName, IReadOnlyList<Workbook> Workbooks);
