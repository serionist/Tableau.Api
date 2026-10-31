using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Tableau.Api.Pipeline;

/// <summary>
/// A download's content: forward-only, first the bytes read to tell its kind, then the rest of the answer as it comes.
/// Each read honours its own token and the call's, which carries the caller's token and the transfer timeout; a failure
/// is the call's (<see cref="DownloadCall"/>). Disposing it disposes the stream under it.
/// </summary>
internal sealed class DownloadStream(Stream inner, ReadOnlyMemory<byte> start, DownloadCall? call) : Stream
{
    private ReadOnlyMemory<byte> _start = start;
    private long _read;
    private int _disposed;

    /// <summary>Whether any of the content has been read, by the caller or by a save.</summary>
    public bool Started => Volatile.Read(ref _read) > 0;

    public override bool CanRead => Volatile.Read(ref _disposed) == 0;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException("A download's length is the Length its answer gave, when it gave one.");

    public override long Position
    {
        get => Volatile.Read(ref _read);
        set => throw new NotSupportedException("A download is read forward, once.");
    }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (TakeStart(buffer) is var taken and > 0) return taken;
        var token = call?.Token ?? CancellationToken.None;
        try
        {
            token.ThrowIfCancellationRequested();
            // A synchronous read takes no token, so the call's cancellation or timeout ends it by closing the stream.
            using (token.Register(static state => ((Stream)state!).Dispose(), inner))
                return Counted(inner.Read(buffer));
        }
        catch (Exception e) when (call is not null)
        {
            throw call.Failure(e, Position, CancellationToken.None);
        }
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (TakeStart(buffer.Span) is var taken and > 0) return taken;
        var callToken = call?.Token ?? CancellationToken.None;
        using var linked = cancellationToken.CanBeCanceled && callToken.CanBeCanceled
            ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, callToken)
            : null;
        try
        {
            var token = linked?.Token ?? (cancellationToken.CanBeCanceled ? cancellationToken : callToken);
            return Counted(await inner.ReadAsync(buffer, token).ConfigureAwait(false));
        }
        catch (Exception e) when (call is not null)
        {
            throw call.Failure(e, Position, cancellationToken);
        }
    }

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException("A download is read forward, once.");

    public override void SetLength(long value) => throw new NotSupportedException("A download is read, not written.");

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException("A download is read, not written.");

    public override async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
            await inner.DisposeAsync().ConfigureAwait(false);
        await base.DisposeAsync().ConfigureAwait(false);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && Interlocked.Exchange(ref _disposed, 1) == 0)
            inner.Dispose();
        base.Dispose(disposing);
    }

    // The bytes read ahead to tell the kind come first.
    private int TakeStart(Span<byte> buffer)
    {
        if (_start.IsEmpty || buffer.IsEmpty) return 0;
        var taken = Math.Min(buffer.Length, _start.Length);
        _start.Span[..taken].CopyTo(buffer);
        _start = _start[taken..];
        return Counted(taken);
    }

    private int Counted(int read)
    {
        Interlocked.Add(ref _read, read);
        return read;
    }
}
