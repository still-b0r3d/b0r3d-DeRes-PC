using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace B0r3dDeRes.Engine
{
    // One frame for the GIF writer: a palette entry a pixel, how long it shows, its own palette
    // when it has one (r, g, b an entry), and the entry that's see-through (-1: none).
    sealed class GifFrame
    {
        public int Width, Height;
        public int DelayMs = 100;
        public byte[] Indices;
        public byte[] Palette;
        public int TransparentIndex = -1;
    }

    // GIF89a files, written as the website writes them: the screen and its global colour table,
    // the Netscape loop block, then each frame's control block, its descriptor (with a table of
    // its own when its palette differs) and its LZW data in 255-byte sub-blocks. Each frame is
    // compressed as it's added and goes straight into the file, so a long animation is never held
    // whole. The same frames give the same bytes as the website's, so files are compared byte for
    // byte. (The website's core/gif.ts.)
    sealed class GifWriter
    {
        readonly Stream output;
        readonly int width, height;
        readonly int? loop;          // the NETSCAPE2.0 loop count (0: forever); null writes none, so it plays once
        readonly byte[] palette;     // the global colour table, or null: the first frame's
        byte[] globalSource;
        int globalDepth;

        public int Frames { get; private set; }
        public int Width => width;
        public int Height => height;

        public GifWriter(Stream output, int width, int height, int? loop = 0, byte[] palette = null)
        {
            this.output = output;
            this.width = width;
            this.height = height;
            this.loop = loop;
            this.palette = palette;
        }

        public void Add(GifFrame frame)
        {
            if (Frames == 0) Start(frame);
            byte[] bytes = Encode(frame);
            output.Write(bytes, 0, bytes.Length);
            Frames++;
        }

        // Frames in order, compressed on every core (each frame's bytes depend on nothing but the
        // frame and the global colour table, so the file is the same as one added at a time).
        public void AddAll(IList<GifFrame> frames)
        {
            if (frames.Count == 0) return;
            if (Frames == 0) Start(frames[0]);
            var encoded = new byte[frames.Count][];
            EveryCore.For(0, frames.Count, i => encoded[i] = Encode(frames[i]));
            foreach (byte[] bytes in encoded) output.Write(bytes, 0, bytes.Length);
            Frames += frames.Count;
        }

        // the trailer: the file is whole
        public void Finish()
        {
            if (Frames == 0) throw new InvalidOperationException("GIF requires at least one frame");
            output.WriteByte(0x3b);
        }

        // A frame's control block, descriptor, colour table and LZW data.
        byte[] Encode(GifFrame frame)
        {
            int delayCs = Math.Max(1, (int)JsMath.Round(frame.DelayMs / 10.0));
            bool hasTrans = frame.TransparentIndex >= 0;
            var bytes = new MemoryStream();
            // the Graphic Control Extension: the disposal, the delay, the see-through entry. Frames
            // cover the whole screen, so each is kept (disposal 1) until the next is drawn over it;
            // a frame with see-through pixels is cleared afterwards (disposal 2), or the next one's
            // see-through pixels would show it.
            int disposal = hasTrans ? 2 : 1;
            bytes.Write(new byte[] { 0x21, 0xf9, 0x04, (byte)((disposal << 2) | (hasTrans ? 1 : 0)), (byte)(delayCs & 0xff), (byte)((delayCs >> 8) & 0xff), (byte)(hasTrans ? frame.TransparentIndex : 0), 0x00 }, 0, 8);

            // the image descriptor, at 0,0, with the frame's own table when its palette differs
            byte[] lct = null;
            int lctDepth = 0;
            if (frame.Palette != null && !SamePalette(frame.Palette, globalSource)) lct = PadPalette(frame.Palette, out lctDepth);
            var id = new byte[10];
            id[0] = 0x2c;
            id[5] = (byte)frame.Width;
            id[6] = (byte)(frame.Width >> 8);
            id[7] = (byte)frame.Height;
            id[8] = (byte)(frame.Height >> 8);
            id[9] = (byte)(lct != null ? 0x80 | ((lctDepth - 1) & 7) : 0x00);
            bytes.Write(id, 0, 10);
            if (lct != null) bytes.Write(lct, 0, lct.Length);

            int minCodeSize = Math.Max(2, lct != null ? lctDepth : globalDepth);
            bytes.WriteByte((byte)minCodeSize);
            Lzw.Compress(frame.Indices, minCodeSize, bytes);
            return bytes.ToArray();
        }

        void Start(GifFrame first)
        {
            byte[] source = palette ?? first.Palette;
            if (source == null || source.Length < 3) throw new InvalidOperationException("GIF requires a color palette");
            byte[] table = PadPalette(source, out int depth);
            globalSource = source;
            globalDepth = depth;

            // the header, then the logical screen: the global table's flag, colour resolution and
            // size (both depth - 1), background 0, aspect 0
            output.Write(new byte[] { 0x47, 0x49, 0x46, 0x38, 0x39, 0x61 }, 0, 6);
            output.Write(new byte[] { (byte)width, (byte)(width >> 8), (byte)height, (byte)(height >> 8), (byte)(0x80 | (((depth - 1) & 7) << 4) | ((depth - 1) & 7)), 0, 0 }, 0, 7);
            output.Write(table, 0, table.Length);

            // the Netscape 2.0 looping block (19 bytes)
            if (loop.HasValue)
            {
                int count = loop.Value;
                output.Write(new byte[]
                {
                    0x21, 0xff, 0x0b,
                    0x4e, 0x45, 0x54, 0x53, 0x43, 0x41, 0x50, 0x45, 0x32, 0x2e, 0x30,   // NETSCAPE2.0
                    0x03, 0x01, (byte)(count & 0xff), (byte)((count >> 8) & 0xff), 0x00,
                }, 0, 19);
            }
        }

        // a palette padded to a whole power of two (2 to 256 entries), and its bits
        internal static byte[] PadPalette(byte[] palette, out int depth)
        {
            int colours = palette.Length / 3;
            depth = 1;
            while (1 << depth < colours && depth < 8) depth++;
            var table = new byte[(1 << depth) * 3];
            Buffer.BlockCopy(palette, 0, table, 0, Math.Min(palette.Length, table.Length));
            return table;
        }

        static bool SamePalette(byte[] a, byte[] b)
        {
            if (a == b) return true;
            if (a == null || b == null || a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        // Every frame onto one file (the website's encodeGif), its palette the first frame's
        // unless one is given.
        public static void Write(Stream output, IList<GifFrame> frames, int? loop = 0, byte[] palette = null)
        {
            if (frames.Count == 0) throw new InvalidOperationException("GIF requires at least one frame");
            var writer = new GifWriter(output, frames[0].Width, frames[0].Height, loop, palette);
            writer.AddAll(frames);
            writer.Finish();
        }
    }

    // GIF's LZW, codes up to 12 bits, a clear sent when the table is full, packed into 255-byte
    // sub-blocks. The dictionary is the code for (prefix code, next index), 4096 × 256 entries; an
    // entry counts only while its stamp is the current generation, so clearing it is one step
    // instead of wiping 6 MB. A frame being compressed borrows one; as many as there are cores
    // are kept for the next frames, the rest let go.
    static class Lzw
    {
        const int TableSize = 4096 * 256;

        sealed class Table
        {
            public readonly ushort[] Codes = new ushort[TableSize];
            public readonly uint[] Stamps = new uint[TableSize];
            public uint Generation;
        }

        static readonly Stack<Table> spare = new Stack<Table>();

        public static void Compress(byte[] pixels, int minCodeSize, Stream output)
        {
            Table borrowed = null;
            lock (spare) if (spare.Count > 0) borrowed = spare.Pop();
            borrowed = borrowed ?? new Table();
            try
            {
                Compress(pixels, minCodeSize, output, borrowed);
            }
            finally
            {
                lock (spare) if (spare.Count < Environment.ProcessorCount) spare.Push(borrowed);
            }
        }

        static void Compress(byte[] pixels, int minCodeSize, Stream output, Table dictionary)
        {
            int clearCode = 1 << minCodeSize, eoiCode = clearCode + 1;
            var subBlock = new byte[256];
            int subLen = 0, accum = 0, accumBits = 0;

            void FlushSubBlock()
            {
                if (subLen == 0) return;
                subBlock[0] = (byte)subLen;
                output.Write(subBlock, 0, subLen + 1);
                subLen = 0;
            }
            void WriteBits(int val, int bits)
            {
                accum |= val << accumBits;
                accumBits += bits;
                while (accumBits >= 8)
                {
                    subBlock[++subLen] = (byte)(accum & 0xff);
                    if (subLen == 255) FlushSubBlock();
                    accum = (int)((uint)accum >> 8);
                    accumBits -= 8;
                }
            }
            void FlushBits()
            {
                if (accumBits > 0)
                {
                    subBlock[++subLen] = (byte)(accum & 0xff);
                    if (subLen == 255) FlushSubBlock();
                    accum = 0;
                    accumBits = 0;
                }
                FlushSubBlock();
            }

            int codeSize = minCodeSize + 1, nextCode = clearCode + 2;
            ushort[] table = dictionary.Codes;
            uint[] stamp = dictionary.Stamps;
            uint gen = ++dictionary.Generation;

            WriteBits(clearCode, codeSize);
            if (pixels.Length == 0)
            {
                WriteBits(eoiCode, codeSize);
                FlushBits();
                output.WriteByte(0x00);   // the block terminator
                return;
            }

            int prefix = pixels[0];
            for (int i = 1; i < pixels.Length; i++)
            {
                int c = pixels[i];
                int key = (prefix << 8) | c;
                if (stamp[key] == gen) prefix = table[key];
                else
                {
                    WriteBits(prefix, codeSize);
                    if (nextCode < 4096)
                    {
                        table[key] = (ushort)nextCode;
                        stamp[key] = gen;
                        nextCode++;
                        if (nextCode == (1 << codeSize) + 1 && codeSize < 12) codeSize++;
                    }
                    else
                    {
                        WriteBits(clearCode, codeSize);
                        gen = ++dictionary.Generation;
                        codeSize = minCodeSize + 1;
                        nextCode = clearCode + 2;
                    }
                    prefix = c;
                }
            }
            WriteBits(prefix, codeSize);
            WriteBits(eoiCode, codeSize);
            FlushBits();
            output.WriteByte(0x00);
        }
    }

    // The colours a full-colour GIF gets: one palette of up to 256 (255 with see-through pixels,
    // leaving room for their entry) for every frame, so still parts don't shimmer, from up to
    // 32,768 pixels sampled evenly across all of them; and each pixel's nearest entry.
    static class GifColours
    {
        // One palette for a set of full-colour frames (the website's rgbFramesPalette). With
        // `alphas` (one a frame, null where all show), only pixels that show are sampled.
        public static byte[] Palette(IList<byte[]> frames, int pixelsPerFrame, IList<byte[]> alphas = null)
        {
            var samples = new PaletteSamples((long)frames.Count * pixelsPerFrame);
            for (int k = 0; k < frames.Count; k++) samples.Add(frames[k], pixelsPerFrame, alphas?[k]);
            return samples.Palette(alphas != null ? 255 : 256);
        }

        // Frame indices with the see-through pixels pointing at an entry of their own. With fewer
        // than 256 colours it's one more entry at the end; with a full palette, the colour the
        // shown pixels use least gives up its entry, and its pixels move to the nearest other colour.
        public static void WithTransparentEntry(byte[] palette, byte[] indices, byte[] alpha, out byte[] newPalette, out byte[] newIndices, out int transparentIndex)
        {
            int n = palette.Length / 3;
            var output = (byte[])indices.Clone();
            int t = n;
            byte[] pal = palette;
            if (n < 256)
            {
                pal = new byte[palette.Length + 3];
                Buffer.BlockCopy(palette, 0, pal, 0, palette.Length);
            }
            else
            {
                var uses = new uint[256];
                for (int i = 0; i < output.Length; i++) if (alpha[i] != 0) uses[output[i]]++;
                t = 0;
                for (int k = 1; k < 256; k++) if (uses[k] < uses[t]) t = k;
                if (uses[t] > 0)
                {
                    int near = t == 0 ? 1 : 0;
                    long best = long.MaxValue;
                    for (int k = 0; k < 256; k++)
                    {
                        if (k == t) continue;
                        int dr = palette[k * 3] - palette[t * 3], dg = palette[k * 3 + 1] - palette[t * 3 + 1], db = palette[k * 3 + 2] - palette[t * 3 + 2];
                        long d = dr * dr + dg * dg + db * db;
                        if (d < best)
                        {
                            best = d;
                            near = k;
                        }
                    }
                    for (int i = 0; i < output.Length; i++) if (output[i] == t) output[i] = (byte)near;
                }
            }
            for (int i = 0; i < output.Length; i++) if (alpha[i] == 0) output[i] = (byte)t;
            newPalette = pal;
            newIndices = output;
            transparentIndex = t;
        }

        // a palette of up to 255 colours with a see-through entry after them
        public static byte[] TransparentAfter(byte[] palette, out int index)
        {
            index = Math.Min(255, palette.Length / 3);
            var output = new byte[(index + 1) * 3];
            Buffer.BlockCopy(palette, 0, output, 0, index * 3);
            return output;
        }

        // Full-colour frames as a GIF with one shared palette (or the one given; the website's
        // encodeRgbGif). Frames with `alpha` keep their see-through pixels: they get an entry of
        // their own after up to 255 colours.
        public static void WriteRgb(Stream output, IList<AnimFrame> frames, int width, int height, int[] delaysMs, int? loop, byte[] palette = null,
            System.Threading.CancellationToken cancel = default)
        {
            if (frames.Count == 0) throw new InvalidOperationException("GIF requires at least one frame");
            bool seeThrough = false;
            foreach (AnimFrame f in frames) seeThrough |= f.Alpha != null;
            var rgbs = new byte[frames.Count][];
            var alphas = new byte[frames.Count][];
            for (int k = 0; k < frames.Count; k++)
            {
                rgbs[k] = frames[k].Rgb;
                alphas[k] = frames[k].Alpha;
            }
            byte[] colours = palette != null && palette.Length > 0 ? palette : Palette(rgbs, width * height, seeThrough ? alphas : null);
            if (seeThrough && colours.Length > 255 * 3)
            {
                var cut = new byte[255 * 3];
                Buffer.BlockCopy(colours, 0, cut, 0, cut.Length);
                colours = cut;
            }
            var nearest = new NearestColour(colours);
            int t = -1;
            byte[] withEntry = seeThrough ? TransparentAfter(colours, out t) : null;
            var writer = new GifWriter(output, width, height, loop, withEntry ?? colours);
            WriteMapped(writer, frames.Count, k => frames[k], nearest, t, k => delaysMs[k], cancel);
            writer.Finish();
        }

        // Frames mapped to one palette and written, a few at a time on every core; `frame` hands
        // over frame k (and may let it go once it's asked for).
        public static void WriteMapped(GifWriter writer, int total, Func<int, AnimFrame> frame, NearestColour nearest, int transparentIndex, Func<int, int> delayMs,
            System.Threading.CancellationToken cancel = default)
        {
            const int window = 8;
            for (int start = 0; start < total; start += window)
            {
                cancel.ThrowIfCancellationRequested();
                int count = Math.Min(window, total - start);
                var batch = new GifFrame[count];
                for (int i = 0; i < count; i++)
                {
                    int k = start + i;
                    AnimFrame f = frame(k);
                    byte[] indices = nearest.Map(f.Rgb);
                    if (transparentIndex >= 0 && f.Alpha != null)
                        for (int p = 0; p < indices.Length; p++) if (f.Alpha[p] == 0) indices[p] = (byte)transparentIndex;
                    batch[i] = new GifFrame { Width = writer.Width, Height = writer.Height, DelayMs = delayMs(k), Indices = indices, TransparentIndex = transparentIndex };
                }
                writer.AddAll(batch);
            }
        }
    }

    // The samples a shared palette is made from, taken a frame at a time: frames can be let go as
    // soon as they're added, and the palette is the same as from all of them at once.
    sealed class PaletteSamples
    {
        readonly long step;
        readonly List<float> samples = new List<float>();
        long counter;

        // `totalPixels`: every pixel of every frame that will be added
        public PaletteSamples(long totalPixels)
        {
            step = Math.Max(1, totalPixels / 32768);
        }

        // a frame's pixels; with `alpha`, only the ones that show
        public void Add(byte[] rgb, int pixels, byte[] alpha)
        {
            for (int p = 0; p < pixels; p++)
            {
                if (alpha != null && alpha[p] == 0) continue;
                if (counter++ % step == 0)
                {
                    int idx = p * 3;
                    samples.Add(Colour.ByteToLinear[rgb[idx]]);
                    samples.Add(Colour.ByteToLinear[rgb[idx + 1]]);
                    samples.Add(Colour.ByteToLinear[rgb[idx + 2]]);
                }
            }
        }

        // up to `colours` colours (255 leaves room for a see-through entry); never empty
        public byte[] Palette(int colours = 256)
        {
            byte[] palette = Quantize.Rgb(samples.ToArray(), colours, 8);
            return palette.Length > 0 ? palette : new byte[3];
        }
    }

    // Full-colour pixels to their nearest palette entry. Each different colour is searched for once
    // and remembered in a table over every 24-bit colour (16 MB, and a 2 MB "seen" set): animation
    // frames repeat the same colours a great deal, and a noisy signal loop holds a million of them.
    sealed class NearestColour
    {
        readonly byte[] nearest = new byte[1 << 24];
        readonly byte[] seen = new byte[1 << 21];
        readonly byte[] palR, palG, palB;
        public readonly byte[] Palette;

        public NearestColour(byte[] palette)
        {
            Palette = palette;
            int n = palette.Length / 3;
            palR = new byte[n];
            palG = new byte[n];
            palB = new byte[n];
            for (int i = 0; i < n; i++)
            {
                palR[i] = palette[i * 3];
                palG[i] = palette[i * 3 + 1];
                palB[i] = palette[i * 3 + 2];
            }
        }

        // palette entries for r, g, b pixels (the nearest by plain distance, the first of equals)
        public byte[] Map(byte[] rgb)
        {
            int count = rgb.Length / 3, palLength = palR.Length;
            var indices = new byte[count];
            for (int p = 0; p < count; p++)
            {
                int r = rgb[p * 3], g = rgb[p * 3 + 1], b = rgb[p * 3 + 2];
                int key = (r << 16) | (g << 8) | b;
                if ((seen[key >> 3] & (1 << (key & 7))) != 0)
                {
                    indices[p] = nearest[key];
                    continue;
                }
                int bestIdx = 0, bestDist = int.MaxValue;
                for (int i = 0; i < palLength; i++)
                {
                    int dr = r - palR[i], dg = g - palG[i], db = b - palB[i];
                    int dist = dr * dr + dg * dg + db * db;
                    if (dist < bestDist)
                    {
                        bestDist = dist;
                        bestIdx = i;
                        if (dist == 0) break;
                    }
                }
                nearest[key] = (byte)bestIdx;
                seen[key >> 3] |= (byte)(1 << (key & 7));
                indices[p] = (byte)bestIdx;
            }
            return indices;
        }
    }
}
