namespace Tableau.Api;

/// <summary>What went wrong in a call, so a caller can act on it without reading the message. A minor version may add kinds.</summary>
public enum TableauErrorKind
{
    /// <summary>
    /// Tableau answered with an error the other kinds don't name, such as 400 or 403, or the request never got an answer:
    /// the connection failed, or the server's certificate was refused (<see cref="System.Exception.InnerException"/>).
    /// </summary>
    Http,

    /// <summary>Tableau answered 401: the sign-in failed, or the session ended.</summary>
    Authentication,

    /// <summary>Tableau answered 404: the item, the site, or the endpoint at this API version doesn't exist.</summary>
    NotFound,

    /// <summary>Tableau answered 429: too many calls; <see cref="TableauException.RetryAfter"/> says when to try again.</summary>
    RateLimited,

    /// <summary>
    /// The call took longer than <see cref="TableauClientOptions.RequestTimeout"/> or
    /// <see cref="TableauClientOptions.TransferTimeout"/> allow. The caller's own cancellation is never this: it stays
    /// <see cref="System.OperationCanceledException"/>.
    /// </summary>
    Timeout,

    /// <summary>
    /// Tableau answered with a redirect the client doesn't follow: to another scheme, host or port, a method that would
    /// lose its body, or more than five in a row. The client sent nothing to its target. An HttpClient of the caller's
    /// own whose handler follows redirects may have sent the request there before the client could see it, and the
    /// message then says so.
    /// </summary>
    Redirect,

    /// <summary>
    /// The REST API version is below what the call needs, or below the oldest the client supports (REST API 3.23,
    /// Tableau Server 2024.2) without <see cref="TableauClientOptions.AllowUnsupportedServerVersion"/>. Nothing was sent.
    /// </summary>
    UnsupportedApiVersion,

    /// <summary>The call doesn't exist on this deployment, Tableau Server or Tableau Cloud. Nothing was sent.</summary>
    UnsupportedDeployment,

    /// <summary>A success answer that isn't Tableau's JSON, or lacks what the call returns.</summary>
    InvalidResponse,

    /// <summary>Vizportal, Tableau's internal web API, answered with an error.</summary>
    VizPortal,
}
