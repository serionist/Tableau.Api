using System;
using System.Net;
using System.Net.Http;

namespace Tableau.Api;

/// <summary>
/// A call to Tableau failed: the one exception type the library defines. <see cref="Kind"/> says what went wrong; the
/// other members carry what Tableau answered, where it is known. An argument the library refuses is still an
/// <see cref="ArgumentException"/>, and the caller's own cancellation an <see cref="OperationCanceledException"/>.
/// </summary>
/// <remarks>
/// Its constructors are public and its members set by initialisers, so a test can make the exception a call would throw.
/// No member holds a password, a token or a request body.
/// </remarks>
/// <param name="kind">What went wrong.</param>
/// <param name="message">What failed, for a person to read.</param>
/// <param name="innerException">The cause, if another exception caused it.</param>
public sealed class TableauException(TableauErrorKind kind, string message, Exception? innerException = null)
    : Exception(message, innerException)
{
    /// <summary>An <see cref="TableauErrorKind.Http"/> failure with a general message.</summary>
    public TableauException()
        : this(TableauErrorKind.Http, "A call to Tableau failed.")
    {
    }

    /// <summary>An <see cref="TableauErrorKind.Http"/> failure.</summary>
    /// <param name="message">What failed.</param>
    public TableauException(string message)
        : this(TableauErrorKind.Http, message)
    {
    }

    /// <summary>An <see cref="TableauErrorKind.Http"/> failure that another exception caused.</summary>
    /// <param name="message">What failed.</param>
    /// <param name="innerException">The cause.</param>
    public TableauException(string message, Exception? innerException)
        : this(TableauErrorKind.Http, message, innerException)
    {
    }

    /// <summary>What went wrong.</summary>
    public TableauErrorKind Kind { get; } = kind;

    /// <summary>The HTTP status Tableau answered with, or null when no answer came or none was needed.</summary>
    public HttpStatusCode? StatusCode { get; init; }

    /// <summary>Tableau's own error code, such as <c>404004</c>, when its answer held one.</summary>
    public string? ErrorCode { get; init; }

    /// <summary>Tableau's summary of the error, such as "Resource Not Found", when its answer held one.</summary>
    public string? Summary { get; init; }

    /// <summary>Tableau's detail of the error, when its answer held one.</summary>
    public string? Detail { get; init; }

    /// <summary>The HTTP method of the request, when one was made or about to be.</summary>
    public HttpMethod? Method { get; init; }

    /// <summary>
    /// The request's path and query as sent, without the host, each query value written as <c>***</c>, such as
    /// <c>/api/3.25/sites/{site-id}/users?filter=***</c>.
    /// </summary>
    public string? RequestPath { get; init; }

    /// <summary>
    /// The id the server, or a proxy in front of it, gave the request in an <c>X-Request-Id</c> header, when it sent one.
    /// </summary>
    public string? RequestId { get; init; }

    /// <summary>
    /// How long Tableau asked the caller to wait before trying again: its <c>Retry-After</c> header, as a delay or a date, or
    /// else the wait a 429's body names, such as "Please retry after 29 minutes". Null when the answer named none.
    /// </summary>
    public TimeSpan? RetryAfter { get; init; }

    /// <summary>The start of the answer's body, at most 4 KB of it, for an error or an answer the client couldn't read.</summary>
    public string? ResponseBody { get; init; }
}
