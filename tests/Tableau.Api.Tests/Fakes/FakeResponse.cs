using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Tableau.Api.Tests.Fakes;

/// <summary>What FakeTableauServer answers a request with.</summary>
internal sealed class FakeResponse
{
    private FakeResponse(int status, string? contentType, byte[] body,
        IReadOnlyList<KeyValuePair<string, string>>? headers = null, Func<Stream, Task>? writer = null, long? length = null)
    {
        Status = status;
        ContentType = contentType;
        Body = body;
        Headers = headers ?? [];
        Writer = writer;
        StreamedLength = length;
    }

    /// <summary>
    /// What writes a streamed body, after the head, instead of <see cref="Body"/>: it may wait between writes, as a large
    /// file on a slow line does.
    /// </summary>
    public Func<Stream, Task>? Writer { get; }

    /// <summary>A streamed body's length, sent as Content-Length; null sends it chunked, with no length.</summary>
    public long? StreamedLength { get; }

    /// <summary>
    /// A body <paramref name="writer"/> writes as it goes, of the length given or, with none, chunked; with the headers
    /// given, such as a Content-Disposition.
    /// </summary>
    public static FakeResponse Streamed(int status, string? contentType, long? length, Func<Stream, Task> writer,
        params (string Name, string Value)[] headers) =>
        new(status, contentType, [], [.. headers.Select(header => new KeyValuePair<string, string>(header.Name, header.Value))], writer, length);

    /// <summary>A file of bytes, with the Content-Type and the headers given; a null Content-Type sends none.</summary>
    public static FakeResponse File(byte[] body, string? contentType, params (string Name, string Value)[] headers) =>
        new(200, contentType, body, [.. headers.Select(header => new KeyValuePair<string, string>(header.Name, header.Value))]);

    public int Status { get; }

    public string Reason => Status switch
    {
        200 => "OK",
        201 => "Created",
        202 => "Accepted",
        203 => "Non-Authoritative Information",
        204 => "No Content",
        301 => "Moved Permanently",
        302 => "Found",
        303 => "See Other",
        307 => "Temporary Redirect",
        308 => "Permanent Redirect",
        400 => "Bad Request",
        401 => "Unauthorized",
        403 => "Forbidden",
        404 => "Not Found",
        409 => "Conflict",
        429 => "Too Many Requests",
        500 => "Internal Server Error",
        502 => "Bad Gateway",
        503 => "Service Unavailable",
        _ => "Status",
    };

    public string? ContentType { get; }

    public byte[] Body { get; }

    /// <summary>Header lines besides Content-Type, Content-Length and Connection.</summary>
    public IReadOnlyList<KeyValuePair<string, string>> Headers { get; }

    /// <summary>A redirect to location, with no body.</summary>
    public static FakeResponse Redirect(int status, string location) =>
        new(status, null, [], [new("Location", location)]);

    /// <summary>A tsResponse in Tableau's namespace around content, as Tableau answers in XML.</summary>
    public static FakeResponse Xml(int status, string content) =>
        new(status, "application/xml;charset=UTF-8", Encoding.UTF8.GetBytes(
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>"
            + $"<tsResponse xmlns=\"http://tableau.com/api\">{content}</tsResponse>"));

    /// <summary>Tableau's error body: its code, summary and detail.</summary>
    public static FakeResponse Error(int status, string code, string summary, string detail) =>
        Xml(status, $"<error code=\"{code}\"><summary>{summary}</summary><detail>{detail}</detail></error>");

    /// <summary>A JSON body, as Vizportal answers.</summary>
    public static FakeResponse Json(int status, string json) =>
        new(status, "application/json;charset=UTF-8", Encoding.UTF8.GetBytes(json));

    public static FakeResponse Empty(int status) => new(status, null, []);

    public static FakeResponse Text(int status, string text) =>
        new(status, "text/plain; charset=utf-8", Encoding.UTF8.GetBytes(text));

    /// <summary>A body with the Content-Type given, as a download or a proxy's error page comes.</summary>
    public static FakeResponse Content(int status, string contentType, string body) =>
        new(status, contentType, Encoding.UTF8.GetBytes(body));

    /// <summary>A body of bytes with the Content-Type given, such as a certificate revocation list.</summary>
    public static FakeResponse Bytes(int status, string contentType, byte[] body) => new(status, contentType, body);

    /// <summary>The fixture file from Fixtures/, as Tableau sends JSON.</summary>
    public static FakeResponse Fixture(int status, string file) => Json(status, ModelFixtures.Text(file));

    /// <summary>This response with one more header line.</summary>
    public FakeResponse WithHeader(string name, string value) =>
        new(Status, ContentType, Body, [.. Headers, new(name, value)], Writer, StreamedLength);

    /// <summary>What a request with no route gets: a 404 that names it, so the failing test says which.</summary>
    public static FakeResponse NoRoute(RecordedRequest request) =>
        Error(404, "404000", "No route",
            Escape($"The fake server has no route for {request.Method} {request.Path}"));

    private static string Escape(string text) =>
        text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}
