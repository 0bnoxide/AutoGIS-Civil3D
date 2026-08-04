using System.Text;

namespace AutoGIS.Civil3D.Handoff.Tests;

internal static class TestLandXml
{
    internal const string Valid = """
        <?xml version="1.0" encoding="utf-8"?>
        <LandXML xmlns="http://www.landxml.org/schema/LandXML-1.2" version="1.2">
          <Units>
            <Metric linearUnit="meter" elevationUnit="meter" />
          </Units>
          <CoordinateSystem epsgCode="26913" />
          <Surfaces>
            <Surface name="Existing Ground">
              <Definition surfType="TIN">
                <Pnts>
                  <P id="1">0 0 100</P>
                  <P id="2">0 10 101</P>
                  <P id="3">10 0 102</P>
                </Pnts>
                <Faces>
                  <F>1 2 3</F>
                </Faces>
              </Definition>
            </Surface>
          </Surfaces>
        </LandXML>
        """;

    internal static Stream Stream(string xml, int maximumReadSize = 7) =>
        new ChunkedNonSeekableReadStream(Encoding.UTF8.GetBytes(xml), maximumReadSize);

    internal static Stream Utf16Stream(
        string xml,
        bool bigEndian,
        int maximumReadSize = 1)
    {
        Encoding encoding = new UnicodeEncoding(bigEndian, byteOrderMark: true, throwOnInvalidBytes: true);
        byte[] preamble = encoding.GetPreamble();
        byte[] content = encoding.GetBytes(xml);
        byte[] bytes = new byte[preamble.Length + content.Length];
        preamble.CopyTo(bytes, 0);
        content.CopyTo(bytes, preamble.Length);
        return new ChunkedNonSeekableReadStream(bytes, maximumReadSize);
    }

    internal static Stream Utf32Stream(
        string xml,
        bool bigEndian,
        int maximumReadSize = 1)
    {
        Encoding encoding = new UTF32Encoding(bigEndian, byteOrderMark: true, throwOnInvalidCharacters: true);
        byte[] preamble = encoding.GetPreamble();
        byte[] content = encoding.GetBytes(xml);
        byte[] bytes = new byte[preamble.Length + content.Length];
        preamble.CopyTo(bytes, 0);
        content.CopyTo(bytes, preamble.Length);
        return new ChunkedNonSeekableReadStream(bytes, maximumReadSize);
    }

    private sealed class ChunkedNonSeekableReadStream : Stream
    {
        private readonly MemoryStream inner;
        private readonly int maximumReadSize;

        internal ChunkedNonSeekableReadStream(byte[] bytes, int maximumReadSize)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumReadSize);
            inner = new MemoryStream(bytes, writable: false);
            this.maximumReadSize = maximumReadSize;
        }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            inner.Read(buffer, offset, Math.Min(count, maximumReadSize));

        public override int Read(Span<byte> buffer) =>
            inner.Read(buffer[..Math.Min(buffer.Length, maximumReadSize)]);

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
