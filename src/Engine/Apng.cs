using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace B0r3dDeRes.Engine
{
    // One frame for the APNG writer: how long it shows, and its pixels as r, g, b, as r, g, b, a,
    // or as palette entries.
    sealed class ApngFrame
    {
        public int DelayMs = 100;
        public byte[] Rgb, Rgba, Indices;
    }

    // Animated PNG files, written as the website writes them: IHDR, acTL, the palette (and its
    // see-through entry) for indexed frames, then each frame's fcTL and its data (the first as a
    // plain IDAT, so a viewer without APNG shows it; the rest as fdAT), every frame the whole
    // picture, replacing the one before. Frames are indexed when a palette is given (every frame
    // then uses it), otherwise r, g, b, or r, g, b, a when the first frame is. Nothing else goes in.
    // Browsers and .NET squeeze the same rows into different bytes, so the tests compare the
    // chunks and the rows. (The website's core/apng.ts.)
    sealed class ApngWriter
    {
        enum Mode { Rgb, Rgba, Indexed }

        readonly Stream output;
        readonly int width, height, frames, numPlays, transparentIndex;
        readonly byte[] palette;
        Mode mode = Mode.Rgb;
        int depth = 8;
        uint seq;
        // the tests' Stored, taken on their thread: frames are packed on others
        readonly bool stored = Png.Stored;

        public int Added { get; private set; }

        // `frames`: how many will be added (the acTL that says so comes first); `numPlays`: 0 plays forever
        public ApngWriter(Stream output, int width, int height, int frames, int numPlays = 0, byte[] palette = null, int transparentIndex = -1)
        {
            this.output = output;
            this.width = width;
            this.height = height;
            this.frames = frames;
            this.numPlays = numPlays;
            this.palette = palette;
            this.transparentIndex = transparentIndex;
        }

        public void Add(ApngFrame frame)
        {
            if (Added == 0) Start(frame);
            Write(Pack(frame), frame.DelayMs);
        }

        // frames in order, packed on every core
        public void AddAll(IList<ApngFrame> list)
        {
            if (list.Count == 0) return;
            if (Added == 0) Start(list[0]);
            var packed = new byte[list.Count][];
            EveryCore.For(0, list.Count, i => packed[i] = Pack(list[i]));
            for (int i = 0; i < list.Count; i++) Write(packed[i], list[i].DelayMs);
        }

        // the end: the file is whole
        public void Finish()
        {
            if (Added == 0) throw new InvalidOperationException("APNG requires at least one frame");
            if (Added != frames) throw new InvalidOperationException("The APNG was told " + frames + " frames and got " + Added);
            Png.WriteChunk(output, "IEND", new byte[0], 0);
        }

        void Start(ApngFrame first)
        {
            int colourType = 2;   // r, g, b
            if (palette != null && first.Indices != null)
            {
                int colours = palette.Length / 3;
                if (colours < 1 || colours > 256 || palette.Length % 3 != 0) throw new ArgumentException("Bad APNG palette");
                depth = Png.IndexDepth(colours);
                colourType = 3;
                mode = Mode.Indexed;
            }
            else if (first.Rgba != null)
            {
                colourType = 6;
                mode = Mode.Rgba;
            }
            output.Write(Png.Signature, 0, Png.Signature.Length);
            byte[] ihdr = Png.Header(width, height, depth, colourType);
            Png.WriteChunk(output, "IHDR", ihdr, ihdr.Length);
            // acTL: how many frames, and how many times to play them
            var actl = new byte[8];
            BigEndian(actl, 0, (uint)frames);
            BigEndian(actl, 4, (uint)numPlays);
            Png.WriteChunk(output, "acTL", actl, 8);
            if (mode == Mode.Indexed)
            {
                Png.WriteChunk(output, "PLTE", palette, palette.Length);
                if (transparentIndex >= 0)
                {
                    var alphas = new byte[transparentIndex + 1];
                    for (int i = 0; i < transparentIndex; i++) alphas[i] = 255;
                    Png.WriteChunk(output, "tRNS", alphas, alphas.Length);
                }
            }
        }

        void Write(byte[] compressed, int delayMs)
        {
            byte[] fctl = FrameControl(seq++, width, height, delayMs);
            Png.WriteChunk(output, "fcTL", fctl, fctl.Length);
            if (Added == 0) Png.WriteChunk(output, "IDAT", compressed, compressed.Length);
            else
            {
                // fdAT: a sequence number, then the compressed data
                var fdat = new byte[4 + compressed.Length];
                BigEndian(fdat, 0, seq++);
                Buffer.BlockCopy(compressed, 0, fdat, 4, compressed.Length);
                Png.WriteChunk(output, "fdAT", fdat, fdat.Length);
            }
            Added++;
        }

        static void BigEndian(byte[] into, int at, uint v)
        {
            into[at] = (byte)(v >> 24);
            into[at + 1] = (byte)(v >> 16);
            into[at + 2] = (byte)(v >> 8);
            into[at + 3] = (byte)v;
        }

        static int Gcd(int a, int b)
        {
            int x = Math.Abs(a), y = Math.Abs(b);
            while (y != 0)
            {
                int t = y;
                y = x % y;
                x = t;
            }
            return x;
        }

        // fcTL: the frame's number, its size at 0,0, its delay as a fraction of a second, and
        // "leave it" and "replace" (each frame is the whole picture)
        static byte[] FrameControl(uint seq, int width, int height, int delayMs)
        {
            var fctl = new byte[26];
            BigEndian(fctl, 0, seq);
            BigEndian(fctl, 4, (uint)width);
            BigEndian(fctl, 8, (uint)height);
            int ms = (int)Math.Max(1, JsMath.Round(delayMs));
            int g = Gcd(ms, 1000);
            int num = Math.Min(65535, ms / g), den = Math.Min(65535, 1000 / g);
            fctl[20] = (byte)(num >> 8);
            fctl[21] = (byte)num;
            fctl[22] = (byte)(den >> 8);
            fctl[23] = (byte)den;
            return fctl;
        }

        // A frame's rows, as zlib data: palette entries with no filter, or full colour with each
        // row's best filter (the usual "sum of absolute values" rule), as the website packs them.
        byte[] Pack(ApngFrame f)
        {
            using (var bytes = new MemoryStream())
            {
                using (var zlib = new Png.ZlibStream(bytes, stored))
                {
                    if (mode == Mode.Indexed)
                    {
                        int stride = (int)(((long)width * depth + 7) / 8), perByte = 8 / depth;
                        var raw = new byte[stride + 1];   // filter byte 0 ("none") first
                        for (int y = 0; y < height; y++)
                        {
                            long rowOffset = (long)y * width;
                            if (depth == 8) Buffer.BlockCopy(f.Indices, (int)rowOffset, raw, 1, width);
                            else
                            {
                                Array.Clear(raw, 1, stride);
                                for (int x = 0; x < width; x++) raw[1 + x / perByte] |= (byte)(f.Indices[rowOffset + x] << (8 - depth - (x % perByte) * depth));
                            }
                            zlib.Write(raw, 0, raw.Length);
                        }
                    }
                    else
                    {
                        int channels = mode == Mode.Rgba ? 4 : 3, stride = width * channels;
                        byte[] source = mode == Mode.Rgba ? f.Rgba : f.Rgb;
                        var prev = new byte[stride];
                        var cur = new byte[stride];
                        var trial = new byte[stride];
                        var best = new byte[stride + 1];
                        for (int y = 0; y < height; y++)
                        {
                            Buffer.BlockCopy(source, y * stride, cur, 0, stride);
                            long bestSum = long.MaxValue;
                            for (int type = 0; type <= 4; type++)
                            {
                                long sum = Png.FilterRow(type, cur, prev, trial, channels);
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
                return bytes.ToArray();
            }
        }
    }

    static class ApngInfo
    {
        // An APNG read through once from the start of `stream` without unpacking it (a file too
        // big to hold): its size, and its frames when acTL's count is the frame controls there
        // are. False when it isn't a PNG, its frames don't add up, or it doesn't end with IEND.
        public static bool Shape(Stream stream, out int width, out int height, out int frames)
        {
            width = height = frames = 0;
            var bytes = new byte[1 << 16];
            if (!GifInfo.ReadAll(stream, bytes, 8)) return false;
            for (int i = 0; i < 8; i++) if (bytes[i] != Png.Signature[i]) return false;
            long declared = -1;
            int controls = 0;
            while (true)
            {
                // the length, the type, the data, then its CRC
                if (!GifInfo.ReadAll(stream, bytes, 8)) return false;
                long length = (long)bytes[0] << 24 | (long)bytes[1] << 16 | (long)bytes[2] << 8 | bytes[3];
                string type = Encoding.ASCII.GetString(bytes, 4, 4);
                if (type == "IHDR" || type == "acTL")
                {
                    if (length < 8 || length > 13 || !GifInfo.ReadAll(stream, bytes, (int)length + 4)) return false;
                    long first = (long)bytes[0] << 24 | (long)bytes[1] << 16 | (long)bytes[2] << 8 | bytes[3];
                    if (type == "acTL") declared = first;
                    else
                    {
                        width = (int)Math.Min(int.MaxValue, first);
                        height = (int)Math.Min(int.MaxValue, (long)bytes[4] << 24 | (long)bytes[5] << 16 | (long)bytes[6] << 8 | bytes[7]);
                    }
                    continue;
                }
                if (type == "fcTL") controls++;
                for (long left = length + 4; left > 0;)
                {
                    int n = (int)Math.Min(left, bytes.Length);
                    if (!GifInfo.ReadAll(stream, bytes, n)) return false;
                    left -= n;
                }
                if (type == "IEND")
                {
                    frames = controls;
                    return declared == controls && width > 0 && height > 0;
                }
            }
        }
    }
}
