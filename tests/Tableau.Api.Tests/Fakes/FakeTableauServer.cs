using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Tableau.Api.Tests.Fakes;

/// <summary>
/// A Tableau server for tests: HTTP/1.1 on a loopback port, answering from a route table and recording every request
/// as it came off the wire, the request line included. The library builds its own HttpClient and handler for each
/// request, so a real socket is the only place a test can stand between it and Tableau, and what it records is what
/// Tableau would receive. One request per connection: every response says Connection: close, and once the client has
/// closed its side, the server resets the connection, so the suite's thousands of connections leave no socket in
/// TIME_WAIT holding a loopback port.
/// </summary>
internal sealed class FakeTableauServer : IDisposable
{
    private readonly TcpListener _listener;
    // Never disposed: a connection accepted as the server stops still reads its token.
    private readonly CancellationTokenSource _stop = new();
    private readonly Dictionary<string, Func<RecordedRequest, FakeResponse>> _routes = new(StringComparer.Ordinal);
    private readonly List<RecordedRequest> _requests = [];
    private readonly object _lock = new();

    private FakeTableauServer()
    {
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        var port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        BaseUrl = new Uri(string.Format(CultureInfo.InvariantCulture, "http://127.0.0.1:{0}/", port));
        _ = Task.Run(AcceptAsync);
    }

    /// <summary>The server's address, the base URL a client is built with.</summary>
    public Uri BaseUrl { get; }

    /// <summary>Every request so far, in the order they arrived, unrouted ones included.</summary>
    public IReadOnlyList<RecordedRequest> Requests
    {
        get
        {
            lock (_lock) return [.. _requests];
        }
    }

    /// <summary>A server on a free loopback port, already listening.</summary>
    public static FakeTableauServer Start() => new();

    /// <summary>
    /// Answer method and path with respond. The path is the request target before any `?`, as sent, escapes
    /// included. A request with no route gets a 404 with a Tableau error body, so the client throws.
    /// </summary>
    public void Map(string method, string path, Func<RecordedRequest, FakeResponse> respond)
    {
        lock (_lock) _routes[Key(method, path)] = respond;
    }

    public void Map(string method, string path, FakeResponse response) => Map(method, path, _ => response);

    public void Dispose()
    {
        _stop.Cancel();
        _listener.Stop();
    }

    private static string Key(string method, string path) => method + " " + path;

    private async Task AcceptAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync().ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is ObjectDisposedException or SocketException or InvalidOperationException)
            {
                return;
            }
            _ = Task.Run(() => ServeAsync(client));
        }
    }

    private async Task ServeAsync(TcpClient client)
    {
        // Taken once: TcpClient.Client is null once the client is closed. Stopping the server resets a connection as
        // its end does, so no stop leaves one in TIME_WAIT either.
        var socket = client.Client;
        using (client)
        using (_stop.Token.Register(() => Reset(socket)))
        {
            try
            {
                var stream = client.GetStream();
                var request = await ReadRequestAsync(new ConnectionReader(stream), stream).ConfigureAwait(false);
                Func<RecordedRequest, FakeResponse>? respond;
                lock (_lock)
                {
                    _requests.Add(request);
                    _routes.TryGetValue(Key(request.Method, request.Path), out respond);
                }
                var response = respond is null ? FakeResponse.NoRoute(request) : Respond(respond, request);
                await WriteAsync(stream, response).ConfigureAwait(false);
                // The side that closes first waits in TIME_WAIT, so the server waits for the client to close, as
                // Connection: close asks, and answers with a reset, which leaves neither side waiting. The socket is
                // closed here: disposing the TcpClient would shut it down gracefully first.
                await WaitForCloseAsync(stream).ConfigureAwait(false);
                Reset(socket);
            }
            catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
            {
                // The client went away, or the server is stopping: there is no one to answer.
            }
        }
    }

    private static void Reset(Socket socket)
    {
        try
        {
            socket.LingerState = new LingerOption(true, 0);
            socket.Close();
        }
        catch (ObjectDisposedException)
        {
            // Already closed, by the connection's end or the server's stop.
        }
    }

    /// <summary>Until the client closes its side, or ten seconds pass; anything it sends meanwhile is dropped.</summary>
    private static async Task WaitForCloseAsync(Stream stream)
    {
        var buffer = new byte[256];
        var deadline = Task.Delay(TimeSpan.FromSeconds(10));
        while (true)
        {
            var read = stream.ReadAsync(buffer, 0, buffer.Length);
            if (await Task.WhenAny(read, deadline).ConfigureAwait(false) == deadline)
            {
                // The read ends when the connection is reset; nothing waits for it.
                _ = read.ContinueWith(task => task.Exception, TaskContinuationOptions.OnlyOnFaulted);
                return;
            }
            if (await read.ConfigureAwait(false) == 0) return;
        }
    }

    private static FakeResponse Respond(Func<RecordedRequest, FakeResponse> respond, RecordedRequest request)
    {
        try
        {
            return respond(request);
        }
        catch (Exception ex)
        {
            return FakeResponse.Text(500, $"The fake server's route for {request.Method} {request.Path} threw: {ex}");
        }
    }

    private static async Task<RecordedRequest> ReadRequestAsync(ConnectionReader reader, Stream stream)
    {
        var requestLine = await reader.ReadLineAsync().ConfigureAwait(false);
        var headers = new List<KeyValuePair<string, string>>();
        while (await reader.ReadLineAsync().ConfigureAwait(false) is { Length: > 0 } line)
        {
            var colon = line.IndexOf(':');
            if (colon <= 0) throw new IOException($"Not a header line: {line}");
            headers.Add(new(line.Substring(0, colon).Trim(), line.Substring(colon + 1).Trim()));
        }

        // .NET Framework sends Expect: 100-continue with a body and waits for this before sending it.
        if (IsHeader(headers, "Expect", "100-continue"))
        {
            var proceed = Encoding.ASCII.GetBytes("HTTP/1.1 100 Continue\r\n\r\n");
            await stream.WriteAsync(proceed, 0, proceed.Length).ConfigureAwait(false);
        }

        byte[] body;
        if (IsHeader(headers, "Transfer-Encoding", "chunked"))
            body = await ReadChunkedAsync(reader).ConfigureAwait(false);
        else if (RecordedRequest.Find(headers, "Content-Length") is { } length)
            body = await reader.ReadBytesAsync(int.Parse(length, NumberStyles.None, CultureInfo.InvariantCulture))
                .ConfigureAwait(false);
        else
            body = [];
        return new RecordedRequest(requestLine, headers, body);
    }

    private static bool IsHeader(List<KeyValuePair<string, string>> headers, string name, string value) =>
        string.Equals(RecordedRequest.Find(headers, name), value, StringComparison.OrdinalIgnoreCase);

    private static async Task<byte[]> ReadChunkedAsync(ConnectionReader reader)
    {
        using var body = new MemoryStream();
        while (true)
        {
            var sizeLine = await reader.ReadLineAsync().ConfigureAwait(false);
            var semicolon = sizeLine.IndexOf(';');
            var size = int.Parse(semicolon < 0 ? sizeLine : sizeLine.Substring(0, semicolon),
                NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
            if (size == 0) break;
            var chunk = await reader.ReadBytesAsync(size).ConfigureAwait(false);
            body.Write(chunk, 0, chunk.Length);
            if ((await reader.ReadLineAsync().ConfigureAwait(false)).Length > 0)
                throw new IOException("A chunk does not end with CRLF");
        }
        // The trailer, up to its empty line.
        while ((await reader.ReadLineAsync().ConfigureAwait(false)).Length > 0)
        {
        }
        return body.ToArray();
    }

    private static async Task WriteAsync(Stream stream, FakeResponse response)
    {
        var head = new StringBuilder()
            .Append("HTTP/1.1 ").Append(response.Status.ToString(CultureInfo.InvariantCulture))
            .Append(' ').Append(response.Reason).Append("\r\n");
        if (response.ContentType is { } contentType) head.Append("Content-Type: ").Append(contentType).Append("\r\n");
        foreach (var header in response.Headers) head.Append(header.Key).Append(": ").Append(header.Value).Append("\r\n");
        head.Append("Content-Length: ").Append(response.Body.Length.ToString(CultureInfo.InvariantCulture))
            .Append("\r\nConnection: close\r\n\r\n");
        var bytes = Encoding.ASCII.GetBytes(head.ToString());
        await stream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
        await stream.WriteAsync(response.Body, 0, response.Body.Length).ConfigureAwait(false);
        await stream.FlushAsync().ConfigureAwait(false);
    }

    /// <summary>A connection's lines and bytes, through one buffer, so the body starts right after the head.</summary>
    private sealed class ConnectionReader(Stream stream)
    {
        private readonly byte[] _buffer = new byte[8192];
        private int _start;
        private int _end;

        /// <summary>The next line, without its CRLF, as Latin-1 so each byte stays one char.</summary>
        public async Task<string> ReadLineAsync()
        {
            var line = new StringBuilder();
            while (true)
            {
                if (_start == _end && !await FillAsync().ConfigureAwait(false))
                    throw new EndOfStreamException("The connection closed mid-line");
                var b = _buffer[_start++];
                if (b == '\n')
                {
                    if (line.Length > 0 && line[line.Length - 1] == '\r') line.Length--;
                    return line.ToString();
                }
                line.Append((char)b);
            }
        }

        public async Task<byte[]> ReadBytesAsync(int count)
        {
            var bytes = new byte[count];
            var read = 0;
            while (read < count)
            {
                if (_start == _end && !await FillAsync().ConfigureAwait(false))
                    throw new EndOfStreamException("The connection closed mid-body");
                var take = Math.Min(count - read, _end - _start);
                Buffer.BlockCopy(_buffer, _start, bytes, read, take);
                _start += take;
                read += take;
            }
            return bytes;
        }

        private async Task<bool> FillAsync()
        {
            _start = 0;
            _end = await stream.ReadAsync(_buffer, 0, _buffer.Length).ConfigureAwait(false);
            return _end > 0;
        }
    }
}
