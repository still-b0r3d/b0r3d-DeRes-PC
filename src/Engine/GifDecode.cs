using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace B0r3dDeRes.Engine
{
    // How big a GIF may be read: the biggest screen, the most frames, the longest it may take.
    sealed class GifLimits
    {
        public long MaxPixels = 4096L * 4096;
        public int MaxFrames = 2000;
        public long MaxMs = 20000;

        // the website's own
        public static GifLimits Website => new GifLimits();
    }

    // Why the frames ended before the file did (None: every frame was read).
    enum GifStop { None, Damaged, TooManyFrames, TooSlow }

    sealed class GifRect
    {
        public int X, Y, W, H;
    }

    // One frame of a GIF, as it shows: the whole screen after this frame, r, g, b, a a pixel
    // (alpha 0 or 255). The reader's own buffer, used again for the next frame: copy it before
    // asking for another.
    sealed class DecodedGifFrame
    {
        public int Index;
        public byte[] Rgba;
        public int DelayMs, Disposal;
        public GifRect Rect;    // where the frame's own image sat (before clipping to the screen)
        public bool Interlaced;
    }

    sealed class GifException : Exception
    {
        public GifException(string message) : base(message) { }
    }

    // A GIF decoder of DeRes's own, for opening animated GIFs frame by frame. It composites each
    // frame onto the GIF's screen the way browsers do: disposal 0/1 keeps the frame, 2 clears its
    // rectangle to see-through, 3 puts back what was there before; frames that run off the screen
    // are clipped; delays under 2 hundredths of a second count as 100 ms. A damaged file stops at
    // the damage and keeps the frames before it, as browsers do. (The website's core/gif-decode.ts.)
    sealed class GifReader
    {
        public readonly string Version;     // "87a" or "89a"
        public readonly int Width, Height;
        // the NETSCAPE2.0 loop count (0 = forever), or null when the file has none (it plays once);
        // known once Frames() has gone past it, before the second frame in practice
        public int? LoopCount;
        public GifStop Stop = GifStop.None;

        readonly byte[] bytes;
        readonly GifLimits limits;
        readonly int globalAt, globalSize;    // the global colour table (size 0: none)
        int pos;

        public GifReader(byte[] bytes, GifLimits limits = null)
        {
            this.bytes = bytes;
            this.limits = limits ?? GifLimits.Website;
            if (bytes.Length < 13) throw new GifException("Not a GIF");
            string sig = Encoding.ASCII.GetString(bytes, 0, 6);
            if (sig != "GIF87a" && sig != "GIF89a") throw new GifException("Not a GIF");
            Version = sig == "GIF87a" ? "87a" : "89a";
            Width = bytes[6] | (bytes[7] << 8);
            Height = bytes[8] | (bytes[9] << 8);
            if (Width == 0 || Height == 0) throw new GifException("The GIF has no size");
            if ((long)Width * Height > this.limits.MaxPixels) throw new GifException("The GIF is too big");
            int flags = bytes[10];
            pos = 13;
            if ((flags & 0x80) != 0)
            {
                int size = 3 << ((flags & 7) + 1);
                if (13 + size > bytes.Length) throw new GifException("The GIF is cut off");
                globalAt = 13;
                globalSize = size;
                pos += size;
            }
        }

        // The browsers' rule: 0 or 1 hundredths of a second plays as 100 ms.
        public static int FrameDelayMs(int centiseconds) => centiseconds < 2 ? 100 : centiseconds * 10;

        const int MaxCodes = 4096;

        // The frames, composited onto the screen. Stops early (and sets Stop) at damage, after
        // MaxFrames, or when MaxMs runs out. A frame that is cut off or holds bad data is dropped,
        // with everything after it.
        public IEnumerable<DecodedGifFrame> Frames()
        {
            int n = bytes.Length, W = Width, H = Height;
            Stopwatch clock = Stopwatch.StartNew();
            var screen = new byte[(long)W * H * 4];
            // the previous frame, to be disposed of before the next is drawn, and (for "restore to
            // previous") what it covered
            int pendingDisposal = -1;
            GifRect pendingRect = null;
            SavedRect pendingSaved = null;
            int gceDisposal = 0, gceDelay = 0, gceTransparent = -1;
            int index = 0;
            var lzw = new LzwDecoder();

            // a file that just ends between blocks (no trailer) is fine
            while (pos < n)
            {
                int block = bytes[pos++];
                if (block == 0x3b) yield break;
                if (block == 0x21)
                {
                    pos++;
                    int label = pos - 1 < n ? bytes[pos - 1] : -1;
                    int start = pos;
                    if (!SkipSubBlocks())
                    {
                        Stop = GifStop.Damaged;
                        yield break;
                    }
                    if (label == 0xf9 && bytes[start] >= 4)
                    {
                        int packed = bytes[start + 1];
                        gceDisposal = (packed >> 2) & 7;
                        gceDelay = bytes[start + 2] | (bytes[start + 3] << 8);
                        gceTransparent = (packed & 1) != 0 ? bytes[start + 4] : -1;
                    }
                    else if (label == 0xff && bytes[start] == 11 && LoopCount == null)
                    {
                        string id = Encoding.ASCII.GetString(bytes, start + 1, 11);
                        int sub = start + 12;
                        if ((id == "NETSCAPE2.0" || id == "ANIMEXTS1.0") && bytes[sub] >= 3 && bytes[sub + 1] == 1)
                            LoopCount = bytes[sub + 2] | (bytes[sub + 3] << 8);
                    }
                    continue;
                }
                if (block != 0x2c)
                {
                    Stop = GifStop.Damaged;
                    yield break;
                }

                // an image: its descriptor, its own colour table, then the LZW data
                if (pos + 10 > n)
                {
                    Stop = GifStop.Damaged;
                    yield break;
                }
                int p = pos;
                var rect = new GifRect
                {
                    X = bytes[p] | (bytes[p + 1] << 8),
                    Y = bytes[p + 2] | (bytes[p + 3] << 8),
                    W = bytes[p + 4] | (bytes[p + 5] << 8),
                    H = bytes[p + 6] | (bytes[p + 7] << 8),
                };
                int flags = bytes[p + 8];
                bool interlaced = (flags & 0x40) != 0;
                pos += 9;
                int tableAt = globalAt, tableSize = globalSize;
                if ((flags & 0x80) != 0)
                {
                    int size = 3 << ((flags & 7) + 1);
                    if (pos + size > n)
                    {
                        Stop = GifStop.Damaged;
                        yield break;
                    }
                    tableAt = pos;
                    tableSize = size;
                    pos += size;
                }
                int minCodeSize = pos < n ? bytes[pos] : -1;
                pos++;
                int dataStart = pos;
                if (!SkipSubBlocks() || tableSize == 0 || minCodeSize < 1 || minCodeSize > 11)
                {
                    Stop = GifStop.Damaged;
                    yield break;
                }
                if (index >= limits.MaxFrames)
                {
                    Stop = GifStop.TooManyFrames;
                    yield break;
                }
                if (clock.ElapsedMilliseconds > limits.MaxMs)
                {
                    Stop = GifStop.TooSlow;
                    yield break;
                }

                if (pendingDisposal == 2) ClearRect(screen, W, H, pendingRect);
                else if (pendingDisposal == 3 && pendingSaved != null) PutRect(screen, W, pendingSaved);
                // disposal 4 is an old way of writing "restore to previous" (browsers read it so)
                int disposal = gceDisposal == 4 ? 3 : gceDisposal;
                SavedRect saved = disposal == 3 ? CopyRect(screen, W, H, rect) : null;

                var sink = new RowSink(screen, W, H, rect, interlaced, bytes, tableAt, tableSize, gceTransparent);
                GifStop result = lzw.Decode(bytes, dataStart, minCodeSize, sink, clock, limits.MaxMs);
                if (result != GifStop.None)
                {
                    Stop = result;
                    yield break;
                }

                pendingDisposal = disposal;
                pendingRect = rect;
                pendingSaved = saved;
                var frame = new DecodedGifFrame { Index = index, Rgba = screen, DelayMs = FrameDelayMs(gceDelay), Disposal = gceDisposal, Rect = rect, Interlaced = interlaced };
                gceDisposal = 0;
                gceDelay = 0;
                gceTransparent = -1;
                index++;
                yield return frame;
            }
        }

        // Steps over sub-blocks to the 0 that ends them; false when the file ends first.
        bool SkipSubBlocks()
        {
            while (pos < bytes.Length)
            {
                int len = bytes[pos++];
                if (len == 0) return true;
                pos += len;
            }
            return false;
        }

        sealed class SavedRect
        {
            public int X, Y, W, H;
            public byte[] Rgba;
        }

        // a rectangle clipped to the screen; false when none of it is on screen
        static bool Clip(GifRect r, int W, int H, out int x0, out int y0, out int w, out int h)
        {
            x0 = Math.Min(W, r.X);
            y0 = Math.Min(H, r.Y);
            int x1 = Math.Min(W, r.X + r.W), y1 = Math.Min(H, r.Y + r.H);
            w = x1 - x0;
            h = y1 - y0;
            return x1 > x0 && y1 > y0;
        }

        static void ClearRect(byte[] screen, int W, int H, GifRect r)
        {
            if (!Clip(r, W, H, out int x0, out int y0, out int w, out int h)) return;
            for (int y = y0; y < y0 + h; y++) Array.Clear(screen, (int)(((long)y * W + x0) * 4), w * 4);
        }

        static SavedRect CopyRect(byte[] screen, int W, int H, GifRect r)
        {
            if (!Clip(r, W, H, out int x0, out int y0, out int w, out int h)) return null;
            var rgba = new byte[(long)w * h * 4];
            for (int y = 0; y < h; y++) Buffer.BlockCopy(screen, (int)(((long)(y0 + y) * W + x0) * 4), rgba, y * w * 4, w * 4);
            return new SavedRect { X = x0, Y = y0, W = w, H = h, Rgba = rgba };
        }

        static void PutRect(byte[] screen, int W, SavedRect s)
        {
            for (int y = 0; y < s.H; y++) Buffer.BlockCopy(s.Rgba, y * s.W * 4, screen, (int)(((long)(s.Y + y) * W + s.X) * 4), s.W * 4);
        }

        static readonly int[] InterlaceStart = { 0, 4, 2, 1 }, InterlaceStep = { 8, 8, 4, 2 };

        // Where decoded pixels go: one row at a time, drawn onto the screen when the row is full
        // (in interlaced order when the frame is interlaced), clipped, with see-through pixels and
        // entries past the colour table left as they were. Done once no row still to come can land
        // on the screen.
        sealed class RowSink
        {
            public readonly byte[] Row;
            public bool Done;
            readonly int colours, x0, x1, W, H, tableAt, transparent;
            readonly GifRect rect;
            readonly bool interlaced;
            readonly byte[] screen, table;
            int y, pass, rows;

            public RowSink(byte[] screen, int W, int H, GifRect rect, bool interlaced, byte[] table, int tableAt, int tableSize, int transparent)
            {
                this.screen = screen;
                this.W = W;
                this.H = H;
                this.rect = rect;
                this.interlaced = interlaced;
                this.table = table;
                this.tableAt = tableAt;
                this.transparent = transparent;
                Row = new byte[rect.W];
                colours = tableSize / 3;
                x0 = Math.Min(rect.X, W);
                x1 = Math.Min(rect.X + rect.W, W);
                Done = rect.W == 0 || rect.H == 0 || rect.Y >= H || x1 <= x0;
            }

            // the row is full
            public void Flush()
            {
                int sy = rect.Y + y;
                if (sy < H)
                {
                    long o = ((long)sy * W + x0) * 4;
                    for (int x = x0 - rect.X, end = x1 - rect.X; x < end; x++, o += 4)
                    {
                        int k = Row[x];
                        if (k == transparent || k >= colours) continue;
                        screen[o] = table[tableAt + k * 3];
                        screen[o + 1] = table[tableAt + k * 3 + 1];
                        screen[o + 2] = table[tableAt + k * 3 + 2];
                        screen[o + 3] = 255;
                    }
                }
                rows++;
                if (rows >= rect.H)
                {
                    Done = true;
                    return;
                }
                if (!interlaced)
                {
                    y++;
                    if (rect.Y + y >= H) Done = true;
                    return;
                }
                // four passes: every 8th row from 0, every 8th from 4, every 4th from 2, every 2nd from 1
                y += InterlaceStep[pass];
                while (y >= rect.H && pass < 3) y = InterlaceStart[++pass];
            }
        }

        // LZW for GIF, codes up to 12 bits; the tables are used again from frame to frame. Handles
        // the clear and end codes, a code for the entry about to be made ("KwKwK"), and a full
        // table with no clear (codes stay 12 bits and nothing more is added). A missing end code
        // is fine; a code past the table is damage.
        sealed class LzwDecoder
        {
            readonly ushort[] prefix = new ushort[MaxCodes];
            readonly byte[] suffix = new byte[MaxCodes], first = new byte[MaxCodes], stack = new byte[MaxCodes];

            public GifStop Decode(byte[] bytes, int pos, int minCodeSize, RowSink sink, Stopwatch clock, long maxMs)
            {
                int clear = 1 << minCodeSize, end = clear + 1;
                for (int i = 0; i < clear; i++)
                {
                    suffix[i] = (byte)i;
                    first[i] = (byte)i;
                }
                int codeSize = minCodeSize + 1, mask = (1 << codeSize) - 1, next = clear + 2, prev = -1;
                byte[] row = sink.Row;
                int rowLen = row.Length, x = 0, codes = 0;
                int bits = 0, acc = 0, p = pos, blockLeft = 0, n = bytes.Length;

                while (!sink.Done)
                {
                    while (bits < codeSize)
                    {
                        if (blockLeft == 0)
                        {
                            blockLeft = p < n ? bytes[p++] : 0;
                            if (blockLeft == 0) return GifStop.None;  // the data ended with no end code
                        }
                        if (p >= n) return GifStop.None;
                        acc |= bytes[p++] << bits;
                        bits += 8;
                        blockLeft--;
                    }
                    int code = acc & mask;
                    acc = (int)((uint)acc >> codeSize);
                    bits -= codeSize;
                    if ((++codes & 4095) == 0 && clock.ElapsedMilliseconds > maxMs) return GifStop.TooSlow;

                    if (code == clear)
                    {
                        codeSize = minCodeSize + 1;
                        mask = (1 << codeSize) - 1;
                        next = clear + 2;
                        prev = -1;
                        continue;
                    }
                    if (code == end) return GifStop.None;

                    int top = 0;
                    if (prev == -1)
                    {
                        if (code > clear) return GifStop.Damaged;
                    }
                    else if (code <= next)
                    {
                        if (code == next && next == MaxCodes) return GifStop.Damaged;
                        if (next < MaxCodes)
                        {
                            // the new entry: the previous one plus the first pixel of this one (of
                            // the previous one itself, when this code names the new entry)
                            prefix[next] = (ushort)prev;
                            suffix[next] = first[code == next ? prev : code];
                            first[next] = first[prev];
                            next++;
                            if (next == mask + 1 && codeSize < 12)
                            {
                                codeSize++;
                                mask = (1 << codeSize) - 1;
                            }
                        }
                    }
                    else return GifStop.Damaged;
                    // the entry's pixels, unwound last first
                    int c = code;
                    while (c >= clear)
                    {
                        stack[top++] = suffix[c];
                        c = prefix[c];
                    }
                    stack[top++] = (byte)c;
                    prev = code;

                    while (top > 0)
                    {
                        row[x++] = stack[--top];
                        if (x == rowLen)
                        {
                            sink.Flush();
                            x = 0;
                            if (sink.Done) return GifStop.None;
                        }
                    }
                }
                return GifStop.None;
            }
        }
    }

    // How many frames a GIF holds, from its blocks alone (nothing is decoded), and the words for
    // a GIF's frames. (The website's core/gif-info.ts.)
    static class GifInfo
    {
        // the images in a GIF file; 0 when it isn't one. A damaged file counts what it has.
        public static int FrameCount(byte[] bytes)
        {
            if (bytes == null || bytes.Length < 13) return 0;
            string sig = Encoding.ASCII.GetString(bytes, 0, 6);
            if (sig != "GIF87a" && sig != "GIF89a") return 0;
            int n = bytes.Length;
            long p = 13;
            // the global colour table
            if ((bytes[10] & 0x80) != 0) p += 3 << ((bytes[10] & 7) + 1);

            // a run of sub-blocks (a length, then that many bytes) up to the 0 that ends it
            bool SkipSubBlocks()
            {
                while (p < n)
                {
                    int len = bytes[p++];
                    if (len == 0) return true;
                    p += len;
                }
                return false;
            }

            int frames = 0;
            while (p < n)
            {
                int block = bytes[p++];
                if (block == 0x2c)
                {
                    // the image descriptor: where and how big (8 bytes), then flags
                    if (p + 9 > n) break;
                    int flags = bytes[p + 8];
                    p += 9;
                    if ((flags & 0x80) != 0) p += 3 << ((flags & 7) + 1);
                    // the LZW starting size, then the image data
                    p++;
                    if (!SkipSubBlocks()) break;
                    frames++;
                }
                else if (block == 0x21)
                {
                    // an extension: its label, then sub-blocks
                    p++;
                    if (!SkipSubBlocks()) break;
                }
                // 0x3b ends the file; anything else means the file is damaged here
                else break;
            }
            return frames;
        }

        // A GIF read through once from the start of `stream` without decoding it (a file too big
        // to hold): its screen's size and its images, block by block as FrameCount counts them.
        // False when it isn't a GIF, or doesn't end as one ends (its trailer).
        public static bool Shape(Stream stream, out int width, out int height, out int frames)
        {
            width = height = frames = 0;
            var bytes = new byte[768];   // a colour table, a descriptor or a sub-block
            if (!ReadAll(stream, bytes, 13)) return false;
            string sig = Encoding.ASCII.GetString(bytes, 0, 6);
            if (sig != "GIF87a" && sig != "GIF89a") return false;
            width = bytes[6] | bytes[7] << 8;
            height = bytes[8] | bytes[9] << 8;
            if ((bytes[10] & 0x80) != 0 && !ReadAll(stream, bytes, 3 << ((bytes[10] & 7) + 1))) return false;

            bool SubBlocks()
            {
                while (true)
                {
                    int len = stream.ReadByte();
                    if (len <= 0) return len == 0;
                    if (!ReadAll(stream, bytes, len)) return false;
                }
            }

            while (true)
            {
                int block = stream.ReadByte();
                if (block == 0x3b) return true;
                if (block == 0x2c)
                {
                    // where and how big, the flags (and a table of its own), the LZW starting size, the data
                    if (!ReadAll(stream, bytes, 9)) return false;
                    int flags = bytes[8];
                    if ((flags & 0x80) != 0 && !ReadAll(stream, bytes, 3 << ((flags & 7) + 1))) return false;
                    if (stream.ReadByte() < 0 || !SubBlocks()) return false;
                    frames++;
                }
                // an extension: its label, then sub-blocks
                else if (block == 0x21)
                {
                    if (stream.ReadByte() < 0 || !SubBlocks()) return false;
                }
                else return false;
            }
        }

        // `count` bytes into `into`, or false when the stream ends first
        internal static bool ReadAll(Stream stream, byte[] into, int count)
        {
            for (int got = 0; got < count;)
            {
                int n = stream.Read(into, got, count - got);
                if (n <= 0) return false;
                got += n;
            }
            return true;
        }

        // "3.9 s", "42 s", "2 min 5 s"
        public static string DurationText(double ms)
        {
            double s = ms / 1000;
            if (s < 10) return JsNumber.Fixed(s, 1) + " s";
            if (s < 60) return JsMath.Round(s).ToString(System.Globalization.CultureInfo.InvariantCulture) + " s";
            long whole = (long)JsMath.Round(s);
            return (whole / 60).ToString(System.Globalization.CultureInfo.InvariantCulture) + " min " + (whole % 60).ToString(System.Globalization.CultureInfo.InvariantCulture) + " s";
        }

        // What the picture's line says about a GIF's frames: "58 frames · 3.9 s" for an animation,
        // "first 40 of 300 frames · 2.7 s" when some were cut (the time is what was kept), "first of
        // 58 frames" when it opened as a still.
        public static string FramesText(int fileFrames, int frames, int total, double durationMs, bool animated)
        {
            if (animated)
            {
                string count = frames < total ? "first " + frames + " of " + total + " frames" : frames + " frames";
                return count + " · " + DurationText(durationMs);
            }
            return fileFrames > 1 ? "first of " + fileFrames + " frames" : "";
        }
    }
}
