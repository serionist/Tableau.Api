using System;
using System.Net.Http;
using Microsoft.Extensions.Logging;

namespace Tableau.Api.Pipeline;

/// <summary>
/// Every line the 2.0 client logs. Requests at Debug, a path's query values as <c>***</c>; a failure the caller
/// receives at Debug, since the caller decides what it is worth; warnings only for a setting that weakens the client,
/// and for a Metadata API answer whose data may be partial, which the caller receives with no other sign.
/// No line holds a token, a password, a secret, a JWT, a cookie, an <c>X-Tableau-Auth</c> header or a body; a sign-in
/// names the kind of credentials, never the user.
/// </summary>
/// <remarks>
/// A request's lines, sending, answered and failed, name its call as the reference does, in <c>Call</c>: the path alone
/// can't say which, since Sign In is one path for every kind of credentials and Query Site one for its three keys.
/// </remarks>
internal static partial class TableauLog
{
    [LoggerMessage(EventId = 1, Level = LogLevel.Debug, Message = "Tableau client {Client}: {Call}: {Method} {RequestPath}")]
    public static partial void Sending(ILogger logger, string client, string call, HttpMethod method, string requestPath);

    [LoggerMessage(EventId = 2, Level = LogLevel.Debug,
        Message = "Tableau client {Client}: {Call}: {Method} {RequestPath} answered {StatusCode} in {ElapsedMilliseconds:F0} ms")]
    public static partial void Answered(ILogger logger, string client, string call, HttpMethod method, string requestPath, int statusCode,
        double elapsedMilliseconds);

    [LoggerMessage(EventId = 3, Level = LogLevel.Debug, Message = "Tableau client {Client}: {Call}: {Method} {RequestPath} failed: {Kind}")]
    public static partial void Failed(ILogger logger, string client, string call, HttpMethod method, string requestPath, TableauErrorKind kind,
        Exception exception);

    [LoggerMessage(EventId = 4, Level = LogLevel.Debug, Message = "Tableau client {Client}: {Method} {RequestPath} was cancelled by its caller")]
    public static partial void Cancelled(ILogger logger, string client, HttpMethod method, string requestPath);

    [LoggerMessage(EventId = 5, Level = LogLevel.Debug,
        Message = "Tableau client {Client}: {Method} {RequestPath} answered {StatusCode}, a redirect to {Target}, which it follows")]
    public static partial void Redirected(ILogger logger, string client, HttpMethod method, string requestPath, int statusCode,
        string target);

    [LoggerMessage(EventId = 6, Level = LogLevel.Debug,
        Message = "Tableau client {Client}: the server offers REST API {Offered}; the client uses {Used}")]
    public static partial void Negotiated(ILogger logger, string client, Version offered, Version used);

    [LoggerMessage(EventId = 7, Level = LogLevel.Warning,
        Message = "Tableau client {Client}: DangerousAcceptAnyServerCertificate is set, so any server can pose as {Origin} and "
            + "read the passwords and tokens sent to it")]
    public static partial void AcceptsAnyCertificate(ILogger logger, string client, Uri origin);

    [LoggerMessage(EventId = 8, Level = LogLevel.Warning,
        Message = "Tableau client {Client}: AllowInsecureHttp is set, so passwords and tokens go to {Origin} unencrypted")]
    public static partial void InsecureHttp(ILogger logger, string client, Uri origin);

    [LoggerMessage(EventId = 10, Level = LogLevel.Debug,
        Message = "Tableau client {Client}: {Method} {RequestPath} failed ({Reason}); retry {Retry} of {MaxRetries} in {DelayMilliseconds:F0} ms")]
    public static partial void Retrying(ILogger logger, string client, HttpMethod method, string requestPath, string reason, int retry,
        int maxRetries, double delayMilliseconds);

    [LoggerMessage(EventId = 11, Level = LogLevel.Debug,
        Message = "Tableau client {Client}: signed in with {Kind}; the session is expected to end at {ExpiresAt:O}")]
    public static partial void SignedIn(ILogger logger, string client, string kind, DateTimeOffset expiresAt);

    [LoggerMessage(EventId = 12, Level = LogLevel.Debug, Message = "Tableau client {Client}: signing in again: {Reason}")]
    public static partial void SigningInAgain(ILogger logger, string client, string reason);

    [LoggerMessage(EventId = 13, Level = LogLevel.Debug, Message = "Tableau client {Client}: signed out")]
    public static partial void SignedOut(ILogger logger, string client);

    [LoggerMessage(EventId = 14, Level = LogLevel.Debug,
        Message = "Tableau client {Client}: signing out failed; the session ends on its own")]
    public static partial void SignOutFailed(ILogger logger, string client, Exception exception);

    [LoggerMessage(EventId = 16, Level = LogLevel.Debug,
        Message = "Tableau client {Client}: signing in again failed; the session it has serves until {ExpiresAt:O}, and a minute from now it tries again")]
    public static partial void RenewalFailed(ILogger logger, string client, DateTimeOffset expiresAt, Exception exception);

    [LoggerMessage(EventId = 15, Level = LogLevel.Debug,
        Message = "Tableau client {Client}: the HttpClient's own handler sent a session's token to {Origin}; signing that session out")]
    public static partial void TokenSentElsewhere(ILogger logger, string client, string origin);

    [LoggerMessage(EventId = 17, Level = LogLevel.Warning,
        Message = "Tableau client {Client}: {Method} {RequestPath} answered with a warning, and its data, which may be partial, is returned: {Code}: {Text}")]
    public static partial void PartialAnswer(ILogger logger, string client, HttpMethod method, string requestPath, string code, string text);

    [LoggerMessage(EventId = 9, Level = LogLevel.Warning,
        Message = "Tableau client {Client}: REST API {Version} is below {Floor}, the oldest this client supports; "
            + "AllowUnsupportedServerVersion is set, so it is used, untested")]
    public static partial void UnsupportedVersion(ILogger logger, string client, Version version, Version floor);
}
