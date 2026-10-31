using System;
using System.Net.Http;
using Microsoft.Extensions.Logging;

namespace Tableau.Api.Pipeline;

/// <summary>
/// Every line the 2.0 client logs. Requests at Debug, a path's query values as <c>***</c>; a failure the caller
/// receives at Debug, since the caller decides what it is worth; warnings only for a setting that weakens the client.
/// No line holds a token, a password, a cookie, an <c>X-Tableau-Auth</c> header or a body.
/// </summary>
internal static partial class TableauLog
{
    [LoggerMessage(EventId = 1, Level = LogLevel.Debug, Message = "Tableau client {Client}: {Method} {RequestPath}")]
    public static partial void Sending(ILogger logger, string client, HttpMethod method, string requestPath);

    [LoggerMessage(EventId = 2, Level = LogLevel.Debug,
        Message = "Tableau client {Client}: {Method} {RequestPath} answered {StatusCode} in {ElapsedMilliseconds:F0} ms")]
    public static partial void Answered(ILogger logger, string client, HttpMethod method, string requestPath, int statusCode,
        double elapsedMilliseconds);

    [LoggerMessage(EventId = 3, Level = LogLevel.Debug, Message = "Tableau client {Client}: {Method} {RequestPath} failed: {Kind}")]
    public static partial void Failed(ILogger logger, string client, HttpMethod method, string requestPath, TableauErrorKind kind,
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

    [LoggerMessage(EventId = 9, Level = LogLevel.Warning,
        Message = "Tableau client {Client}: REST API {Version} is below {Floor}, the oldest this client supports; "
            + "AllowUnsupportedServerVersion is set, so it is used, untested")]
    public static partial void UnsupportedVersion(ILogger logger, string client, Version version, Version floor);
}
