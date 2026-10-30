using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Tableau.Api.Tests.Fakes;

/// <summary>One part of a multipart body: its header lines and its bytes.</summary>
internal sealed class MultipartPart(IReadOnlyList<KeyValuePair<string, string>> headers, byte[] body)
{
    public IReadOnlyList<KeyValuePair<string, string>> Headers { get; } = headers;

    public byte[] Body { get; } = body;

    public string BodyText => Encoding.UTF8.GetString(Body);

    public string? Header(string name) => RecordedRequest.Find(Headers, name);
}

/// <summary>The parts of a recorded request's multipart body, by the boundary its Content-Type names.</summary>
internal static class MultipartBody
{
    // Latin-1 maps each byte to one char and back, so a part's bytes survive the string search.
    private static readonly Encoding _latin1 = Encoding.GetEncoding(28591);

    public static IReadOnlyList<MultipartPart> Parse(RecordedRequest request)
    {
        var contentType = request.Header("Content-Type")
            ?? throw new InvalidDataException("The request has no Content-Type");
        if (!contentType.StartsWith("multipart/", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"The request is not multipart: {contentType}");
        var boundary = contentType.Split(';').Select(parameter => parameter.Trim())
            .Where(parameter => parameter.StartsWith("boundary=", StringComparison.OrdinalIgnoreCase))
            .Select(parameter => parameter["boundary=".Length..].Trim('"'))
            .FirstOrDefault() ?? throw new InvalidDataException($"The Content-Type names no boundary: {contentType}");

        var text = _latin1.GetString(request.Body);
        var delimiter = "--" + boundary;
        var parts = new List<MultipartPart>();
        var at = text.IndexOf(delimiter, StringComparison.Ordinal);
        if (at < 0) throw new InvalidDataException("The body holds no boundary");
        at += delimiter.Length;
        while (string.CompareOrdinal(text, at, "--", 0, 2) != 0)
        {
            if (string.CompareOrdinal(text, at, "\r\n", 0, 2) != 0)
                throw new InvalidDataException("A boundary line does not end with CRLF");
            at += 2;
            var next = text.IndexOf("\r\n" + delimiter, at, StringComparison.Ordinal);
            if (next < 0) throw new InvalidDataException("A part has no closing boundary");
            parts.Add(Part(text[at..next]));
            at = next + 2 + delimiter.Length;
        }
        return parts;
    }

    private static MultipartPart Part(string text)
    {
        var headers = new List<KeyValuePair<string, string>>();
        var at = 0;
        while (string.CompareOrdinal(text, at, "\r\n", 0, 2) != 0)
        {
            var end = text.IndexOf("\r\n", at, StringComparison.Ordinal);
            if (end < 0) throw new InvalidDataException("A part's headers do not end with an empty line");
            var line = text[at..end];
            var colon = line.IndexOf(':');
            headers.Add(new(line[..colon].Trim(), line[(colon + 1)..].Trim()));
            at = end + 2;
        }
        return new MultipartPart(headers, _latin1.GetBytes(text[(at + 2)..]));
    }
}
