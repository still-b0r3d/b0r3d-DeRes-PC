using System;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace B0r3dDeRes.Engine
{
    // PNG files, written the way the website writes them: pictures with 256 colours or fewer as
    // indexed PNGs (1, 2, 4 or 8 bits a pixel), which come out several times smaller, the rest in
    // full colour, each row with the filter that suits it. Nothing but the picture goes in: no
    // text, no dates, no program name. (The website's core/png.ts.) Rows are made one at a time
    // and packed as they come, so even a huge export never needs the whole picture in memory.
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
        // a pixel. `transparent`: the entry that's see-through, if any (every one before it solid).
        public static void WriteIndexed(Stream output, int width, int height, byte[] palette, Action<int, byte[]> getRow, int? transparent = null)
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
            if (transparent.HasValue)
            {
                var alphas = new byte[transparent.Value + 1];
                for (int i = 0; i < transparent.Value; i++) alphas[i] = 255;
                WriteChunk(output, "tRNS", alphas, alphas.Length);
            }
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

        // A full-colour PNG, with alpha when `alpha`. getRow(y, row) fills `row` with 3 bytes a
        // pixel (r, g, b), or 4 (r, g, b, a).
        public static void WriteTruecolour(Stream output, int width, int height, bool alpha, Action<int, byte[]> getRow)
        {
            int channels = alpha ? 4 : 3;
            int stride = width * channels;
            output.Write(Signature, 0, Signature.Length);
            byte[] ihdr = Header(width, height, 8, alpha ? 6 : 2);
            WriteChunk(output, "IHDR", ihdr, ihdr.Length);
            WriteChunk(output, "sRGB", new byte[] { 0 }, 1);
            var prev = new byte[stride];
            var cur = new byte[stride];
            var trial = new byte[stride];
            var best = new byte[stride + 1];
            using (var idat = new IdatStream(output))
            {
                using (var zlib = new ZlibStream(idat))
                {
                    for (int y = 0; y < height; y++)
                    {
                        getRow(y, cur);
                        // each filter tried, the one whose bytes look smallest kept (the usual
                        // "sum of absolute values" rule)
                        long bestSum = long.MaxValue;
                        for (int type = 0; type <= 4; type++)
                        {
                            long sum = FilterRow(type, cur, prev, trial, channels);
                            if (sum < bestSum)
                            {
                                bestSum = sum;
                                best[0] = (byte)type;
                                Buffer.BlockCopy(trial, 0, best, 1, stride);
                            }
                        }
                        zlib.Write(best, 0, best.Length);
                        byte[] swap = prev;
                        prev = cur;
                        cur = swap;
                    }
                }
            }
            WriteChunk(output, "IEND", new byte[0], 0);
        }

        static int Paeth(int a, int b, int c)
        {
            int p = a + b - c, pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
            return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
        }

        static long FilterRow(int type, byte[] cur, byte[] prev, byte[] output, int bpp)
        {
            long sum = 0;
            for (int i = 0; i < cur.Length; i++)
            {
                int a = i >= bpp ? cur[i - bpp] : 0, b = prev[i], c = i >= bpp ? prev[i - bpp] : 0, v;
                switch (type)
                {
                    case 0: v = cur[i]; break;
                    case 1: v = cur[i] - a; break;
                    case 2: v = cur[i] - b; break;
                    case 3: v = cur[i] - ((a + b) >> 1); break;
                    default: v = cur[i] - Paeth(a, b, c); break;
                }
                v &= 255;
                output[i] = (byte)v;
                sum += v < 128 ? v : 256 - v;
            }
            return sum;
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
