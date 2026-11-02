using System;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace B0r3dDeRes.Engine
{
    // PNG files, written the way the website writes them: pictures with 256 colours or fewer as
    // indexed PNGs (1, 2, 4 or 8 bits a pixel), which come out several times smaller. Nothing but
    // the picture goes in: no text, no dates, no program name. (The website's core/png.ts.) Rows
    // are made one at a time and packed as they come, so even a huge export never needs the
    // whole picture in memory.
    static class Png
    {
        static readonly byte[] Signature = { 137, 80, 78, 71, 13, 10, 26, 10 };

        static readonly uint[] crcTable = MakeCrcTable();

        static uint[] MakeCrcTable()
        {
            var t = new uint[256];
            for (uint n = 0; n < 256; n++)
            {
                uint c = n;
                for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xedb88320 ^ (c >> 1) : c >> 1;
                t[n] = c;
            }
            return t;
        }

        static uint Crc(uint crc, byte[] bytes, int offset, int count)
        {
            for (int i = offset; i < offset + count; i++) crc = crcTable[(crc ^ bytes[i]) & 255] ^ (crc >> 8);
            return crc;
        }

        // bits a pixel for an indexed PNG with this many colours
        public static int IndexDepth(int colours) => colours <= 2 ? 1 : colours <= 4 ? 2 : colours <= 16 ? 4 : 8;

        static void WriteChunk(Stream output, string type, byte[] data, int count)
        {
            var head = new byte[8];
            head[0] = (byte)(count >> 24);
            head[1] = (byte)(count >> 16);
            head[2] = (byte)(count >> 8);
            head[3] = (byte)count;
            Encoding.ASCII.GetBytes(type, 0, 4, head, 4);
            output.Write(head, 0, 8);
            output.Write(data, 0, count);
            uint crc = Crc(0xffffffff, head, 4, 4);
            crc = Crc(crc, data, 0, count) ^ 0xffffffff;
            output.Write(new[] { (byte)(crc >> 24), (byte)(crc >> 16), (byte)(crc >> 8), (byte)crc }, 0, 4);
        }

        static byte[] Header(int width, int height, int depth, int colourType)
        {
            if (width <= 0 || height <= 0) throw new ArgumentException("A PNG needs a size");
            var ihdr = new byte[13];
            ihdr[0] = (byte)(width >> 24);
            ihdr[1] = (byte)(width >> 16);
            ihdr[2] = (byte)(width >> 8);
            ihdr[3] = (byte)width;
            ihdr[4] = (byte)(height >> 24);
            ihdr[5] = (byte)(height >> 16);
            ihdr[6] = (byte)(height >> 8);
            ihdr[7] = (byte)height;
            ihdr[8] = (byte)depth;
            ihdr[9] = (byte)colourType;
            return ihdr;
        }

        // An indexed (palette) PNG. getRow(y, row) fills `row` with the row's palette entries, one
        // a pixel.
        public static void WriteIndexed(Stream output, int width, int height, byte[] palette, Action<int, byte[]> getRow)
        {
            int colours = palette.Length / 3;
            if (colours < 1 || colours > 256 || palette.Length % 3 != 0) throw new ArgumentException("Bad palette");
            int depth = IndexDepth(colours);
            int stride = (int)(((long)width * depth + 7) / 8);
            int perByte = 8 / depth;
            output.Write(Signature, 0, Signature.Length);
            byte[] ihdr = Header(width, height, depth, 3);
            WriteChunk(output, "IHDR", ihdr, ihdr.Length);
            WriteChunk(output, "sRGB", new byte[] { 0 }, 1);
            WriteChunk(output, "PLTE", palette, palette.Length);
            var row = new byte[width];
            var raw = new byte[stride + 1];   // filter byte 0 ("none") first
            using (var idat = new IdatStream(output))
            {
                using (var zlib = new ZlibStream(idat))
                {
                    for (int y = 0; y < height; y++)
                    {
                        getRow(y, row);
                        if (depth == 8) Buffer.BlockCopy(row, 0, raw, 1, width);
                        else
                        {
                            Array.Clear(raw, 1, stride);
                            for (int x = 0; x < width; x++) raw[1 + x / perByte] |= (byte)(row[x] << (8 - depth - (x % perByte) * depth));
                        }
                        zlib.Write(raw, 0, raw.Length);
                    }
                }
            }
            WriteChunk(output, "IEND", new byte[0], 0);
        }

        // zlib's own format (what PNG keeps): a two-byte header, the deflated bytes, and an
        // Adler-32 check of what went in
        sealed class ZlibStream : Stream
        {
            readonly Stream inner;
            readonly DeflateStream deflate;
            uint a = 1, b;

            public ZlibStream(Stream output)
            {
                inner = output;
                inner.Write(new byte[] { 0x78, 0x9C }, 0, 2);
                deflate = new DeflateStream(inner, CompressionLevel.Optimal, true);
            }

            public override void Write(byte[] buffer, int offset, int count)
            {
                deflate.Write(buffer, offset, count);
                for (int i = offset; i < offset + count;)
                {
                    int end = Math.Min(offset + count, i + 5552);
                    for (; i < end; i++)
                    {
                        a += buffer[i];
                        b += a;
                    }
                    a %= 65521;
                    b %= 65521;
                }
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    deflate.Dispose();
                    uint adler = (b << 16) | a;
                    inner.Write(new[] { (byte)(adler >> 24), (byte)(adler >> 16), (byte)(adler >> 8), (byte)adler }, 0, 4);
                }
                base.Dispose(disposing);
            }

            public override bool CanRead => false;
            public override bool CanSeek => false;
            public override bool CanWrite => true;
            public override long Length => throw new NotSupportedException();
            public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
            public override void Flush() => deflate.Flush();
            public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
        }

        // what's written to it goes out as IDAT chunks of up to 256 KB
        sealed class IdatStream : Stream
        {
            const int ChunkSize = 256 * 1024;
            readonly Stream output;
            readonly byte[] buffer = new byte[ChunkSize];
            int filled;

            public IdatStream(Stream output) => this.output = output;

            public override void Write(byte[] data, int offset, int count)
            {
                while (count > 0)
                {
                    int take = Math.Min(count, ChunkSize - filled);
                    Buffer.BlockCopy(data, offset, buffer, filled, take);
                    filled += take;
                    offset += take;
                    count -= take;
                    if (filled == ChunkSize) Emit();
                }
            }

            void Emit()
            {
                if (filled == 0) return;
                WriteChunk(output, "IDAT", buffer, filled);
                filled = 0;
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing) Emit();
                base.Dispose(disposing);
            }

            public override bool CanRead => false;
            public override bool CanSeek => false;
            public override bool CanWrite => true;
            public override long Length => throw new NotSupportedException();
            public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
            public override void Flush() { }
            public override int Read(byte[] data, int offset, int count) => throw new NotSupportedException();
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
        }
    }
}
