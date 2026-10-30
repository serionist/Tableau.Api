using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Tableau.Api
{
    internal class PartialStream : Stream
    {
        private long StartPos { get; }

        private long MaxBytes { get; }

        private Stream Src { get; }

        internal PartialStream(Stream src, long maxBytes)
        {
            this.Src = src;
            this.StartPos = src.Position;
            this.MaxBytes = maxBytes;
        }

        public override bool CanRead
        {
            get { return this.Src.CanRead; }
        }

        public override bool CanWrite
        {
            get { return this.Src.CanWrite; }
        }

        public override bool CanSeek
        {
            get { return this.Src.CanSeek; }
        }

        public override bool CanTimeout
        {
            get { return this.Src.CanTimeout; }
        }

        public override long Position
        {
            get { return this.Src.Position - this.StartPos; }
            set { this.Src.Position = this.StartPos + value; }
        }

        public override long Length
        {
            get { return Math.Min(this.MaxBytes, this.Src.Length - this.StartPos); }
        }

        public override void SetLength(long value)
        {
            throw new NotImplementedException();
        }

        public override void Flush()
        {
            this.Src.Flush();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (this.Position >= this.Length)
                return 0;
            int count1 = (int)Math.Min(this.Length - (this.Position + (long)offset), (long)count);
            return this.Src.Read(buffer, offset, count1);
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            return this.Src.Seek(this.StartPos + offset, origin);
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            this.Src.Write(buffer, offset, count);
        }
    }
}
