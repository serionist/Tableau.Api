using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading;
using Microsoft.Extensions.Options;
using Tableau.Api.Pipeline;

namespace Tableau.Api;

/// <summary>
/// How a <see cref="TableauClient"/> reaches its Tableau: the server's address, the site, the REST API version and
/// the rules for its connection. Set them, then build a client; from then on they can't change, and setting one throws
/// <see cref="InvalidOperationException"/>, so no call made through the client sees another value than the next.
/// </summary>
/// <remarks>
/// <para>
/// From configuration, bind them in the delegate <c>AddTableauClient</c> takes:
/// <c>services.AddTableauClient("prod", options => configuration.GetSection("Tableau:Prod").Bind(options))</c>. Every
/// member is a type the configuration binder's source generator reads, so the binding stays safe under native AOT.
/// </para>
/// <para>
/// They are checked when the client is built, or at start-up under a host: a missing or relative server URL, plain
/// http without <see cref="AllowInsecureHttp"/>, a version that isn't major.minor, a timeout that isn't positive, or a
/// negative <see cref="MaxRetries"/> or <see cref="MaxRetryAfter"/>.
/// </para>
/// </remarks>
public sealed class TableauClientOptions : IValidatableObject
{
    private static readonly TimeSpan _longestTimeout = TimeSpan.FromMilliseconds(int.MaxValue);

    private Uri? _serverUrl;
    private string _site = "";
    private Version? _apiVersion;
    private TableauDeployment _deployment;
    private bool _allowUnsupportedServerVersion;
    private bool _dangerousAcceptAnyServerCertificate;
    private bool _allowInsecureHttp;
    private TimeSpan _requestTimeout = TimeSpan.FromMinutes(2);
    private TimeSpan _transferTimeout = Timeout.InfiniteTimeSpan;
    private int _maxRetries = 3;
    private TimeSpan _maxRetryAfter = TimeSpan.FromMinutes(2);
    private bool _readOnly;

    /// <summary>
    /// The server's address, such as <c>https://tableau.example.com/</c> or, for Tableau Cloud, its pod's, such as
    /// <c>https://prod-useast-a.online.tableau.com/</c>. A path in it is kept, for a server behind a reverse proxy's
    /// prefix: <c>https://example.com/tableau/</c>. It is the server's address, not a site's or a view's link.
    /// </summary>
    [Required]
    public Uri? ServerUrl
    {
        get => _serverUrl;
        set => Set(ref _serverUrl, value);
    }

    /// <summary>The site's content URL, as its address shows it after <c>/site/</c>; <c>""</c>, the default, is the default site.</summary>
    public string Site
    {
        get => _site;
        set => Set(ref _site, value);
    }

    /// <summary>
    /// The REST API version to call, major.minor only, such as <c>new Version(3, 25)</c>; null, the default, asks the
    /// server for its version on the first call and uses it, up to REST API 3.29, the newest this client knows.
    /// </summary>
    public Version? ApiVersion
    {
        get => _apiVersion;
        set => Set(ref _apiVersion, value);
    }

    /// <summary>Tableau Server or Tableau Cloud, which decides the calls the client allows; <see cref="TableauDeployment.Auto"/> by default.</summary>
    public TableauDeployment Deployment
    {
        get => _deployment;
        set => Set(ref _deployment, value);
    }

    /// <summary>
    /// Whether to call a server below REST API 3.23 (Tableau Server 2024.2), the oldest Tableau still supports, rather
    /// than refuse it. Such a server is untested: the client logs a warning once, and a call may fail in ways it can't
    /// describe.
    /// </summary>
    public bool AllowUnsupportedServerVersion
    {
        get => _allowUnsupportedServerVersion;
        set => Set(ref _allowUnsupportedServerVersion, value);
    }

    /// <summary>
    /// Whether to accept any certificate the server shows, which turns off the check that the server is who it says:
    /// anyone between the client and the server can then read its passwords and tokens. For a test server only. The
    /// client logs a warning when it is built with it. Without it, a certificate that doesn't validate, or that its
    /// issuer has revoked, fails the call.
    /// </summary>
    public bool DangerousAcceptAnyServerCertificate
    {
        get => _dangerousAcceptAnyServerCertificate;
        set => Set(ref _dangerousAcceptAnyServerCertificate, value);
    }

    /// <summary>
    /// Whether an <c>http://</c> <see cref="ServerUrl"/> is allowed, which sends passwords and tokens unencrypted. The
    /// client logs a warning when it is built with one.
    /// </summary>
    public bool AllowInsecureHttp
    {
        get => _allowInsecureHttp;
        set => Set(ref _allowInsecureHttp, value);
    }

    /// <summary>
    /// How long each attempt of a call that moves no file may take, from sending it to reading its whole answer: two
    /// minutes by default, or <see cref="Timeout.InfiniteTimeSpan"/>. An attempt that takes longer is retried when the
    /// call is safe to send again (<see cref="MaxRetries"/>); the last one fails the call with
    /// <see cref="TableauErrorKind.Timeout"/>. The waits between attempts don't count.
    /// </summary>
    public TimeSpan RequestTimeout
    {
        get => _requestTimeout;
        set => Set(ref _requestTimeout, value);
    }

    /// <summary>
    /// How long a call that downloads or uploads a file may take, all of it: its attempts, the waits between them and,
    /// for a download, reading the file to its end. No limit by default (<see cref="Timeout.InfiniteTimeSpan"/>), since
    /// a large file on a slow line takes what it takes; <see cref="RequestTimeout"/> doesn't apply to such a call. A call
    /// that takes longer fails with <see cref="TableauErrorKind.Timeout"/>.
    /// </summary>
    public TimeSpan TransferTimeout
    {
        get => _transferTimeout;
        set => Set(ref _transferTimeout, value);
    }

    /// <summary>
    /// How many times a call is sent again after its first attempt fails for a reason that may pass: a 408, a 429, a
    /// 5xx, a connection that failed or an attempt that took longer than <see cref="RequestTimeout"/>. Three by default;
    /// 0 turns retries off. Only a call that is safe to send twice is retried, such as a read, an update of a resource
    /// or a delete; a publish, a create or a run is sent once. The client waits between attempts as the server asks
    /// (<c>Retry-After</c>, or the wait a 429's body names), otherwise from about two seconds, doubling, with jitter.
    /// </summary>
    public int MaxRetries
    {
        get => _maxRetries;
        set => Set(ref _maxRetries, value);
    }

    /// <summary>
    /// The longest wait the client accepts from a server that asks it to come back later, by a 429's
    /// <c>Retry-After</c> header or its body, or a 503's header: two minutes by default. A server that asks for longer
    /// isn't waited for: the call fails at once, with <see cref="TableauErrorKind.RateLimited"/> for a 429, and
    /// <see cref="TableauException.RetryAfter"/> says how long the server asked for. Tableau Cloud's hourly limits ask
    /// for minutes, which a batch job may rather schedule than wait out in a call.
    /// </summary>
    public TimeSpan MaxRetryAfter
    {
        get => _maxRetryAfter;
        set => Set(ref _maxRetryAfter, value);
    }

    /// <summary>Makes the options read-only, once a client is built from them.</summary>
    internal void MakeReadOnly() => _readOnly = true;

    /// <summary>What is wrong with the options, each with the member it is about; none when they are good.</summary>
    internal IEnumerable<(string Member, string Problem)> Problems()
    {
        if (ServerUrl is { } url)
        {
            if (!url.IsAbsoluteUri)
            {
                yield return (nameof(ServerUrl), $"ServerUrl '{url}' is relative: give the server's whole address, such as https://tableau.example.com/.");
                yield break;
            }
            if (url.Scheme != Uri.UriSchemeHttps && url.Scheme != Uri.UriSchemeHttp)
                yield return (nameof(ServerUrl), $"ServerUrl's scheme is {url.Scheme}: Tableau answers https, or http.");
            if (url.Scheme == Uri.UriSchemeHttp && !AllowInsecureHttp)
            {
                yield return (nameof(ServerUrl), "ServerUrl is plain http, which sends passwords and tokens unencrypted: use https, "
                    + "or set AllowInsecureHttp.");
            }
            if (url.UserInfo.Length > 0)
                yield return (nameof(ServerUrl), "ServerUrl holds a user name or a password, which a client never sends in an address.");
            if (url.Query.Length > 0 || url.Fragment.Length > 0)
            {
                yield return (nameof(ServerUrl), "ServerUrl has a query or a fragment: give the server's address, with the path "
                    + "prefix it may have, not a site's or a view's link.");
            }
        }
        if (Site is null)
            yield return (nameof(Site), "Site is null: \"\" is the default site.");
        if (ApiVersion is { } version && !ApiVersions.IsMajorMinor(version))
            yield return (nameof(ApiVersion), $"ApiVersion {version} names more than a major and a minor version, such as 3.25.");
        if (!Enum.IsDefined(Deployment))
            yield return (nameof(Deployment), $"Deployment {(int)Deployment} is none of Auto, Server and Cloud.");
        foreach (var (name, timeout) in new[] { (nameof(RequestTimeout), RequestTimeout), (nameof(TransferTimeout), TransferTimeout) })
        {
            if (timeout != Timeout.InfiniteTimeSpan && (timeout <= TimeSpan.Zero || timeout > _longestTimeout))
                yield return (name, $"{name} is {timeout}: give a positive time up to 24 days, or Timeout.InfiniteTimeSpan.");
        }
        if (MaxRetries < 0)
            yield return (nameof(MaxRetries), $"MaxRetries is {MaxRetries}: give 0, for no retries, or more.");
        if (MaxRetryAfter < TimeSpan.Zero || MaxRetryAfter > _longestTimeout)
            yield return (nameof(MaxRetryAfter), $"MaxRetryAfter is {MaxRetryAfter}: give a time from zero up to 24 days.");
    }

    /// <inheritdoc/>
    IEnumerable<ValidationResult> IValidatableObject.Validate(ValidationContext validationContext)
    {
        foreach (var (member, problem) in Problems())
            yield return new ValidationResult(problem, [member]);
    }

    private void Set<T>(ref T field, T value)
    {
        if (_readOnly)
        {
            throw new InvalidOperationException(
                "These TableauClientOptions belong to a client already, so they can't change: build another client with new options.");
        }
        field = value;
    }
}

/// <summary>Checks <see cref="TableauClientOptions"/>, by the code the options validation source generator writes.</summary>
[OptionsValidator]
internal sealed partial class TableauClientOptionsValidator : IValidateOptions<TableauClientOptions>;
