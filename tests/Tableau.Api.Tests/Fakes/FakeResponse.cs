using System.Text;

namespace Tableau.Api.Tests.Fakes;

/// <summary>What FakeTableauServer answers a request with.</summary>
internal sealed class FakeResponse
{
    private FakeResponse(int status, string? contentType, byte[] body)
    {
        Status = status;
        ContentType = contentType;
        Body = body;
    }

    public int Status { get; }

    public string Reason => Status switch
    {
        200 => "OK",
        201 => "Created",
        204 => "No Content",
        400 => "Bad Request",
        401 => "Unauthorized",
        403 => "Forbidden",
        404 => "Not Found",
        409 => "Conflict",
        500 => "Internal Server Error",
        _ => "Status",
    };

    public string? ContentType { get; }

    public byte[] Body { get; }

    /// <summary>A tsResponse in Tableau's namespace around content, as Tableau answers in XML.</summary>
    public static FakeResponse Xml(int status, string content) =>
        new(status, "application/xml;charset=UTF-8", Encoding.UTF8.GetBytes(
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>"
            + $"<tsResponse xmlns=\"http://tableau.com/api\">{content}</tsResponse>"));

    /// <summary>Tableau's error body: its code, summary and detail.</summary>
    public static FakeResponse Error(int status, string code, string summary, string detail) =>
        Xml(status, $"<error code=\"{code}\"><summary>{summary}</summary><detail>{detail}</detail></error>");

    public static FakeResponse Empty(int status) => new(status, null, []);

    public static FakeResponse Text(int status, string text) =>
        new(status, "text/plain; charset=utf-8", Encoding.UTF8.GetBytes(text));

    /// <summary>What a request with no route gets: a 404 that names it, so the failing test says which.</summary>
    public static FakeResponse NoRoute(RecordedRequest request) =>
        Error(404, "404000", "No route",
            Escape($"The fake server has no route for {request.Method} {request.Path}"));

    private static string Escape(string text) =>
        text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}
