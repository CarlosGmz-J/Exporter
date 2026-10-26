namespace Exporter {
    //Reads a file that is saved by SchuleApi with every byte shifted by one and gives back the real bytes,
    //so big files like videos do not have to be read into memory at once.
    public class ShiftedStream : Stream {
        private readonly FileStream file;

        public ShiftedStream(string path) {
            file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20);
        }

        public override int Read(byte[] buffer, int offset, int count) {
            int read = file.Read(buffer, offset, count);

            for (int i = offset; i < offset + read; i++)
                buffer[i] = unchecked((byte)(buffer[i] - 1));

            return read;
        }

        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => file.Length;

        public override long Position {
            get => file.Position;
            set => file.Position = value;
        }

        public override long Seek(long offset, SeekOrigin origin) {
            return file.Seek(offset, origin);
        }

        public override void Flush() {
        }

        public override void SetLength(long value) {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count) {
            throw new NotSupportedException();
        }

        protected override void Dispose(bool disposing) {
            if (disposing)
                file.Dispose();

            base.Dispose(disposing);
        }
    }
}
