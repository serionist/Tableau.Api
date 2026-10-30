using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Tableau.Api;

internal class PartialStream : Stream
{
    private long StartPos { get; }

    private long MaxBytes { get; }

    private Stream Src { get; }

    internal PartialStream(Stream src, long maxBytes)
    {
        Src = src;
        StartPos = src.Position;
        MaxBytes = maxBytes;
    }

    public override bool CanRead => Src.CanRead;

    public override bool CanWrite => Src.CanWrite;

    public override bool CanSeek => Src.CanSeek;

    public override bool CanTimeout => Src.CanTimeout;

    public override long Position
    {
        get => Src.Position - StartPos;
        set => Src.Position = StartPos + value;
    }

    public override long Length => Math.Min(MaxBytes, Src.Length - StartPos);

    public override void SetLength(long value) => throw new NotImplementedException();

    public override void Flush() => Src.Flush();

    public override int Read(byte[] buffer, int offset, int count)
    {
        if (Position >= Length)
            return 0;
        int count1 = (int)Math.Min(Length - (Position + (long)offset), (long)count);
        return Src.Read(buffer, offset, count1);
    }

    public override long Seek(long offset, SeekOrigin origin) => Src.Seek(StartPos + offset, origin);

    public override void Write(byte[] buffer, int offset, int count) => Src.Write(buffer, offset, count);
}
