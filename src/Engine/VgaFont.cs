using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

namespace B0r3dDeRes.Engine
{
    // The ASCII studio's font, the website's own file (WebPlus IBM VGA 8x16 by VileR, from The
    // Ultimate Oldschool PC Font Pack, CC BY-SA 4.0), read here as its 8×16 letters: the font is
    // an outline of the VGA's pixels (every edge on the pixel grid), so each pixel is in or out of
    // the outline at its centre. A letter is 16 rows of 8 bits (bit 7 the left column).
    static class VgaFont
    {
        public const int Width = 8, Height = 16;
        public const string ResourceName = "B0r3dDeRes.fonts.WebPlus_IBM_VGA_8x16.woff";

        static readonly object gate = new object();
        static Font font;
        static readonly Dictionary<int, byte[]> letters = new Dictionary<int, byte[]>();

        // for the tests: the font file's bytes (null: the exe's own)
        internal static byte[] FileOverride;

        sealed class Font
        {
            public Dictionary<int, int> Cmap;   // code point -> glyph
            public byte[] Glyf;
            public int[] Loca;
            public int UnitsPerEm, Ascender;
        }

        static Font Load()
        {
            byte[] woff = FileOverride;
            if (woff == null)
                using (Stream stream = typeof(VgaFont).Assembly.GetManifestResourceStream(ResourceName))
                {
                    if (stream == null) throw new InvalidOperationException("The VGA font is missing from this build.");
                    var bytes = new MemoryStream();
                    stream.CopyTo(bytes);
                    woff = bytes.ToArray();
                }
            Dictionary<string, byte[]> tables = Tables(woff);
            byte[] head = tables["head"], hhea = tables["hhea"], maxp = tables["maxp"], loca = tables["loca"];
            int glyphs = U16(maxp, 4);
            bool longOffsets = S16(head, 50) != 0;
            var offsets = new int[glyphs + 1];
            for (int i = 0; i <= glyphs; i++) offsets[i] = longOffsets ? (int)U32(loca, i * 4) : U16(loca, i * 2) * 2;
            return new Font { Cmap = Cmap(tables["cmap"]), Glyf = tables["glyf"], Loca = offsets, UnitsPerEm = U16(head, 18), Ascender = S16(hhea, 4) };
        }

        static Font TheFont()
        {
            lock (gate) return font ?? (font = Load());
        }

        // a WOFF's tables, unpacked (each is zlib data when it's smaller than the table)
        static Dictionary<string, byte[]> Tables(byte[] woff)
        {
            if (U32(woff, 0) != 0x774f4646) throw new InvalidDataException("The VGA font isn't a WOFF file.");
            int count = U16(woff, 12);
            var tables = new Dictionary<string, byte[]>();
            for (int i = 0; i < count; i++)
            {
                int at = 44 + i * 20;
                string tag = System.Text.Encoding.ASCII.GetString(woff, at, 4);
                int offset = (int)U32(woff, at + 4), compressed = (int)U32(woff, at + 8), length = (int)U32(woff, at + 12);
                byte[] data = new byte[length];
                if (compressed < length)
                {
                    // zlib: a two-byte header, deflate, then a checksum
                    using (var inflate = new DeflateStream(new MemoryStream(woff, offset + 2, compressed - 2), CompressionMode.Decompress))
                    {
                        int read = 0;
                        while (read < length)
                        {
                            int n = inflate.Read(data, read, length - read);
                            if (n == 0) break;
                            read += n;
                        }
                    }
                }
                else Buffer.BlockCopy(woff, offset, data, 0, length);
                tables[tag] = data;
            }
            return tables;
        }

        // the Windows Unicode table (format 4): code point -> glyph
        static Dictionary<int, int> Cmap(byte[] cmap)
        {
            var map = new Dictionary<int, int>();
            int subtables = U16(cmap, 2);
            for (int i = 0; i < subtables; i++)
            {
                int platform = U16(cmap, 4 + i * 8), encoding = U16(cmap, 6 + i * 8), at = (int)U32(cmap, 8 + i * 8);
                if (platform != 3 || encoding != 1 || U16(cmap, at) != 4) continue;
                int segments = U16(cmap, at + 6) / 2;
                int ends = at + 14, starts = ends + segments * 2 + 2, deltas = starts + segments * 2, ranges = deltas + segments * 2;
                for (int s = 0; s < segments; s++)
                {
                    int end = U16(cmap, ends + s * 2), start = U16(cmap, starts + s * 2), delta = S16(cmap, deltas + s * 2), range = U16(cmap, ranges + s * 2);
                    for (int c = start; c <= end && c != 0xffff; c++)
                    {
                        int glyph;
                        if (range == 0) glyph = (c + delta) & 0xffff;
                        else
                        {
                            glyph = U16(cmap, ranges + s * 2 + range + (c - start) * 2);
                            if (glyph != 0) glyph = (glyph + delta) & 0xffff;
                        }
                        if (glyph != 0) map[c] = glyph;
                    }
                }
            }
            return map;
        }

        // Whether the font has a letter for this character.
        public static bool Has(int codePoint) => TheFont().Cmap.ContainsKey(codePoint);

        // The letter's 16 rows (bit 7 the left column); null when the font hasn't one.
        public static byte[] Letter(int codePoint)
        {
            lock (gate)
            {
                if (letters.TryGetValue(codePoint, out byte[] kept)) return kept;
                Font f = font ?? (font = Load());
                byte[] rows = f.Cmap.TryGetValue(codePoint, out int glyph) ? Draw(f, glyph) : null;
                letters[codePoint] = rows;
                return rows;
            }
        }

        // every character the font has
        public static IEnumerable<int> Characters => TheFont().Cmap.Keys;

        // ---- drawing a glyph's outline ----

        // an edge of the outline, in font units
        struct Edge
        {
            public double X0, Y0, X1, Y1;
        }

        static byte[] Draw(Font f, int glyph)
        {
            var edges = new List<Edge>();
            Outline(f, glyph, 0, 0, 1, 0, 0, 1, edges, 0);
            var rows = new byte[Height];
            double unitsPerPixel = f.UnitsPerEm / (double)Height;
            for (int py = 0; py < Height; py++)
            {
                double y = f.Ascender - (py + 0.5) * unitsPerPixel;
                for (int px = 0; px < Width; px++)
                {
                    double x = (px + 0.5) * unitsPerPixel;
                    // the non-zero rule: edges crossing a line to the right of the point, by direction
                    int winding = 0;
                    foreach (Edge e in edges)
                    {
                        if (e.Y0 <= y && e.Y1 > y || e.Y1 <= y && e.Y0 > y)
                        {
                            double at = e.X0 + (y - e.Y0) * (e.X1 - e.X0) / (e.Y1 - e.Y0);
                            if (at > x) winding += e.Y1 > e.Y0 ? 1 : -1;
                        }
                    }
                    if (winding != 0) rows[py] |= (byte)(0x80 >> px);
                }
            }
            return rows;
        }

        // a glyph's contours as edges (a composite glyph's parts moved and scaled into place)
        static void Outline(Font f, int glyph, double dx, double dy, double a, double b, double c, double d, List<Edge> edges, int depth)
        {
            if (depth > 8 || glyph < 0 || glyph + 1 >= f.Loca.Length) return;
            int at = f.Loca[glyph], end = f.Loca[glyph + 1];
            if (end <= at) return;
            byte[] g = f.Glyf;
            int contours = S16(g, at);
            if (contours < 0)
            {
                int p = at + 10;
                while (true)
                {
                    int flags = U16(g, p), part = U16(g, p + 2);
                    p += 4;
                    double ox, oy;
                    if ((flags & 1) != 0)
                    {
                        ox = S16(g, p);
                        oy = S16(g, p + 2);
                        p += 4;
                    }
                    else
                    {
                        ox = (sbyte)g[p];
                        oy = (sbyte)g[p + 1];
                        p += 2;
                    }
                    if ((flags & 2) == 0) ox = oy = 0;   // points matched, not moved: not in this font
                    double sa = 1, sb = 0, sc = 0, sd = 1;
                    if ((flags & 8) != 0)
                    {
                        sa = sd = F2Dot14(g, p);
                        p += 2;
                    }
                    else if ((flags & 0x40) != 0)
                    {
                        sa = F2Dot14(g, p);
                        sd = F2Dot14(g, p + 2);
                        p += 4;
                    }
                    else if ((flags & 0x80) != 0)
                    {
                        sa = F2Dot14(g, p);
                        sb = F2Dot14(g, p + 2);
                        sc = F2Dot14(g, p + 4);
                        sd = F2Dot14(g, p + 6);
                        p += 8;
                    }
                    // the part's own transform, then this one's
                    Outline(f, part, dx + a * ox + c * oy, dy + b * ox + d * oy, a * sa + c * sb, b * sa + d * sb, a * sc + c * sd, b * sc + d * sd, edges, depth + 1);
                    if ((flags & 0x20) == 0) break;
                }
                return;
            }
            int q = at + 10;
            var ends = new int[contours];
            for (int i = 0; i < contours; i++, q += 2) ends[i] = U16(g, q);
            int points = contours > 0 ? ends[contours - 1] + 1 : 0;
            q += 2 + U16(g, q);   // the instructions
            var flagsOf = new byte[points];
            for (int i = 0; i < points;)
            {
                byte fl = g[q++];
                flagsOf[i++] = fl;
                if ((fl & 8) != 0)
                {
                    int repeat = g[q++];
                    for (int r = 0; r < repeat && i < points; r++) flagsOf[i++] = fl;
                }
            }
            var xs = new int[points];
            var ys = new int[points];
            int v = 0;
            for (int i = 0; i < points; i++)
            {
                byte fl = flagsOf[i];
                if ((fl & 2) != 0) v += (fl & 0x10) != 0 ? g[q++] : -g[q++];
                else if ((fl & 0x10) == 0)
                {
                    v += S16(g, q);
                    q += 2;
                }
                xs[i] = v;
            }
            v = 0;
            for (int i = 0; i < points; i++)
            {
                byte fl = flagsOf[i];
                if ((fl & 4) != 0) v += (fl & 0x20) != 0 ? g[q++] : -g[q++];
                else if ((fl & 0x20) == 0)
                {
                    v += S16(g, q);
                    q += 2;
                }
                ys[i] = v;
            }
            int first = 0;
            foreach (int last in ends)
            {
                // the contour's points in place, with the on-curve point between two off-curve
                // ones put back, so every off-curve point sits between two on-curve ones
                var seq = new List<(double X, double Y, bool On)>();
                int n = last - first + 1;
                for (int k = 0; k < n; k++)
                {
                    int i = first + k, j = first + (k + 1) % n;
                    var point = (X: dx + a * xs[i] + c * ys[i], Y: dy + b * xs[i] + d * ys[i], On: (flagsOf[i] & 1) != 0);
                    seq.Add(point);
                    bool nextOn = (flagsOf[j] & 1) != 0;
                    if (!point.On && !nextOn)
                    {
                        double nx = dx + a * xs[j] + c * ys[j], ny = dy + b * xs[j] + d * ys[j];
                        seq.Add(((point.X + nx) / 2, (point.Y + ny) / 2, true));
                    }
                }
                int start = seq.FindIndex(p => p.On);
                if (start >= 0)
                {
                    // round the contour from an on-curve point, its quadratic curves (none in this
                    // font) as eight short lines each
                    int m = seq.Count;
                    var current = seq[start];
                    for (int k = 1; k <= m; k++)
                    {
                        var p = seq[(start + k) % m];
                        if (p.On)
                        {
                            edges.Add(new Edge { X0 = current.X, Y0 = current.Y, X1 = p.X, Y1 = p.Y });
                            current = p;
                            continue;
                        }
                        var next = seq[(start + k + 1) % m];
                        double fromX = current.X, fromY = current.Y;
                        for (int s = 1; s <= 8; s++)
                        {
                            double t = s / 8.0, u = 1 - t;
                            double x = u * u * current.X + 2 * u * t * p.X + t * t * next.X, y = u * u * current.Y + 2 * u * t * p.Y + t * t * next.Y;
                            edges.Add(new Edge { X0 = fromX, Y0 = fromY, X1 = x, Y1 = y });
                            fromX = x;
                            fromY = y;
                        }
                        current = next;
                        k++;
                    }
                }
                first = last + 1;
            }
        }

        static double F2Dot14(byte[] b, int at) => S16(b, at) / 16384.0;
        static int U16(byte[] b, int at) => (b[at] << 8) | b[at + 1];
        static int S16(byte[] b, int at) => (short)((b[at] << 8) | b[at + 1]);
        static uint U32(byte[] b, int at) => (uint)((b[at] << 24) | (b[at + 1] << 16) | (b[at + 2] << 8) | b[at + 3]);
    }
}
