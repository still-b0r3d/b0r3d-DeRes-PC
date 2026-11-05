using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace B0r3dDeRes.Engine
{
    // The ASCII art as a picture (its PNG, and an animation's frames), 8 × 16 pixels a cell at 1×,
    // 2× or 4× (the website's core/ascii-render.ts):
    //   - half blocks as plain pixels, the top half of a cell its foreground and the bottom its
    //     background, as the website's pixel renderer makes them;
    //   - Teletext's mosaics as the website's canvas draws them: each block a rectangle whose top
    //     and bottom fall between pixels (a cell is three blocks high), so those rows are partly
    //     covered, worked out and blended as Chromium's canvas does it (Skia's 8-bit coverage and
    //     its premultiplied blend), pixel for pixel;
    //   - characters (the ramps and PETSCII) from the font's own 8 × 16 letters: crisp blocks of
    //     the VGA's pixels where the browser draws the font smoothed. Characters the font lacks are
    //     drawn as browsers borrow them: the six quarter blocks PETSCII uses as the font's own
    //     half blocks quartered, anything else through Fallback (Windows' fonts, from the window).
    static class AsciiRender
    {
        public const int CellWidth = 8, CellHeight = 16;

        // the sizes the picture comes in
        public static readonly int[] Scales = { 1, 2, 4 };

        public static void Size(int columns, int rows, int multiplier, out int width, out int height)
        {
            int m = Math.Max(1, multiplier);
            width = columns * CellWidth * m;
            height = rows * CellHeight * m;
        }

        // whether the picture is drawn as the website's canvas draws it (with the font), not as plain pixels
        public static bool DrawnWithFont(AsciiArt art) => art.Settings.Mode != AsciiModes.HalfBlock;

        // the picture's name, as the website names it: photo-ansi-640x480-1x.png (half blocks), photo-ascii-… (the rest)
        public static string PngName(string baseName, AsciiArt art, int multiplier)
        {
            Size(art.Width, art.Height, multiplier, out int w, out int h);
            return baseName + "-" + Kind(art) + "-" + w.ToString(CultureInfo.InvariantCulture) + "x" + h.ToString(CultureInfo.InvariantCulture) + "-" + multiplier.ToString(CultureInfo.InvariantCulture) + "x.png";
        }

        // an animation's: photo-ansi-640x480-1x.gif, photo-ansi-640x480-1x-animated.png
        public static string AnimationName(string baseName, AsciiArt art, int multiplier, bool apng)
        {
            Size(art.Width, art.Height, multiplier, out int w, out int h);
            string stem = baseName + "-" + Kind(art) + "-" + w.ToString(CultureInfo.InvariantCulture) + "x" + h.ToString(CultureInfo.InvariantCulture) + "-" + multiplier.ToString(CultureInfo.InvariantCulture) + "x";
            return apng ? stem + "-animated.png" : stem + ".gif";
        }

        static string Kind(AsciiArt art) => DrawnWithFont(art) ? "ascii" : "ansi";

        // ---- the characters the font lacks ----

        // A character the font hasn't drawn as the window draws it with Windows' fonts: how much
        // of each pixel it covers (0-255), CellWidth × m by CellHeight × m. Set by the window; until
        // then (and for whatever it can't draw) such a character is drawn as the font's U+FFFD.
        public static Func<int, int, byte[]> Fallback;

        static readonly ConcurrentDictionary<long, byte[]> fallbacks = new ConcurrentDictionary<long, byte[]>();

        // the six quarter blocks, from the font's own half blocks
        static readonly Dictionary<int, Func<byte[]>> quarters = new Dictionary<int, Func<byte[]>>
        {
            { 0x2598, () => Both(0x2580, 0x258C) },     // ▘ top left
            { 0x259D, () => Both(0x2580, 0x2590) },     // ▝ top right
            { 0x2596, () => Both(0x2584, 0x258C) },     // ▖ bottom left
            { 0x2597, () => Both(0x2584, 0x2590) },     // ▗ bottom right
            { 0x259A, () => Either(Both(0x2580, 0x258C), Both(0x2584, 0x2590)) },   // ▚
            { 0x259E, () => Either(Both(0x2580, 0x2590), Both(0x2584, 0x258C)) },   // ▞
        };

        static byte[] Both(int a, int b)
        {
            byte[] x = VgaFont.Letter(a), y = VgaFont.Letter(b);
            return Enumerable.Range(0, VgaFont.Height).Select(i => (byte)(x[i] & y[i])).ToArray();
        }

        static byte[] Either(byte[] x, byte[] y) => Enumerable.Range(0, VgaFont.Height).Select(i => (byte)(x[i] | y[i])).ToArray();

        static readonly ConcurrentDictionary<int, byte[]> letters = new ConcurrentDictionary<int, byte[]>();

        // The 16 rows of the letter a character is drawn with, or null when it's drawn through
        // Fallback. As the canvas draws text: a tab, line feed, vertical tab, form feed or return
        // is a space, a soft hyphen shows nothing, and a lone half of a surrogate pair is U+FFFD.
        public static byte[] Letter(int c)
        {
            return letters.GetOrAdd(c, ch =>
            {
                if (ch >= 0x09 && ch <= 0x0D || ch == 0xAD) return new byte[VgaFont.Height];
                if (ch >= 0xD800 && ch <= 0xDFFF) return VgaFont.Letter(0xFFFD);
                byte[] own = VgaFont.Letter(ch);
                if (own != null) return own;
                return quarters.TryGetValue(ch, out Func<byte[]> make) ? make() : null;
            });
        }

        // the characters of the art (or of what the settings can make) that go through Fallback
        public static IEnumerable<int> Borrowed(IEnumerable<int> chars) => chars.Distinct().Where(c => c != ' ' && Letter(c) == null);

        // every character the settings can make: the ramp's (as it's read, a UTF-16 unit at a time), or PETSCII's
        public static IEnumerable<int> CharactersOf(AsciiSettings s)
        {
            if (s.Mode == AsciiModes.Ramp) return Ascii.RampOf(s).Select(ch => (int)ch);
            if (s.Mode == AsciiModes.Petscii) return Ascii.PetsciiScreenCodes.Keys;
            return Enumerable.Empty<int>();
        }

        // Fallback's picture of a character, kept (null: it couldn't draw it either)
        static byte[] Borrow(int c, int m)
        {
            long key = ((long)c << 8) | (uint)m;
            if (fallbacks.TryGetValue(key, out byte[] kept)) return kept;
            byte[] made = null;
            try
            {
                made = Fallback?.Invoke(c, m);
            }
            catch (InvalidOperationException)
            {
                // not from here (the window's thread draws them): the font's U+FFFD stands in
            }
            if (made != null && made.Length == CellWidth * m * CellHeight * m) fallbacks[key] = made;
            else made = null;
            return made;
        }

        // Draws the characters a picture will need through Fallback now (from the window's
        // thread, where it can), so the picture can be made on any thread afterwards.
        public static void Prepare(IEnumerable<int> chars, IEnumerable<int> multipliers)
        {
            if (Fallback == null) return;
            foreach (int c in Borrowed(chars))
                foreach (int m in multipliers.Distinct()) Borrow(c, m);
        }

        // ---- the rows ----

        // Row y of the picture at `multiplier`, as r, g, b into `rgb` (at least its width × 3).
        public static void Row(AsciiArt art, int multiplier, int y, byte[] rgb)
        {
            int m = Math.Max(1, multiplier);
            int cellW = CellWidth * m, cellH = CellHeight * m;
            int r = y / cellH, inY = y % cellH;
            if (r >= art.Height) return;
            if (art.Settings.Mode == AsciiModes.HalfBlock)
            {
                bool top = inY < cellH / 2;
                byte[] colours = top ? art.Fg : art.Bg;
                for (int c = 0; c < art.Width; c++)
                {
                    int i = (r * art.Width + c) * 3;
                    for (int x = c * cellW, end = x + cellW; x < end; x++)
                    {
                        rgb[x * 3] = colours[i];
                        rgb[x * 3 + 1] = colours[i + 1];
                        rgb[x * 3 + 2] = colours[i + 2];
                    }
                }
                return;
            }
            if (art.Settings.Mode == AsciiModes.Teletext)
            {
                TeletextRow(art, m, r, y, rgb);
                return;
            }
            for (int c = 0; c < art.Width; c++)
            {
                int cell = r * art.Width + c, i = cell * 3;
                byte bR = art.Bg[i], bG = art.Bg[i + 1], bB = art.Bg[i + 2];
                byte fR = art.Fg[i], fG = art.Fg[i + 1], fB = art.Fg[i + 2];
                int x0 = c * cellW;
                for (int x = 0; x < cellW; x++)
                {
                    int o = (x0 + x) * 3;
                    rgb[o] = bR;
                    rgb[o + 1] = bG;
                    rgb[o + 2] = bB;
                }
                int ch = art.Chars[cell];
                if (ch == ' ') continue;
                byte[] letter = Letter(ch);
                if (letter != null)
                {
                    byte bits = letter[inY / m];
                    if (bits == 0) continue;
                    for (int x = 0; x < cellW; x++)
                    {
                        if ((bits & (0x80 >> (x / m))) == 0) continue;
                        int o = (x0 + x) * 3;
                        rgb[o] = fR;
                        rgb[o + 1] = fG;
                        rgb[o + 2] = fB;
                    }
                    continue;
                }
                byte[] cover = Borrow(ch, m);
                if (cover == null)
                {
                    // nothing could draw it: the font's own stand-in
                    byte bits = VgaFont.Letter(0xFFFD)[inY / m];
                    for (int x = 0; x < cellW; x++)
                    {
                        if ((bits & (0x80 >> (x / m))) == 0) continue;
                        int o = (x0 + x) * 3;
                        rgb[o] = fR;
                        rgb[o + 1] = fG;
                        rgb[o + 2] = fB;
                    }
                    continue;
                }
                for (int x = 0; x < cellW; x++)
                {
                    int a = cover[inY * cellW + x];
                    if (a == 0) continue;
                    int o = (x0 + x) * 3;
                    rgb[o] = (byte)((fR * a + bR * (255 - a) + 127) / 255);
                    rgb[o + 1] = (byte)((fG * a + bG * (255 - a) + 127) / 255);
                    rgb[o + 2] = (byte)((fB * a + bB * (255 - a) + 127) / 255);
                }
            }
        }

        // A Teletext row as the website's canvas makes it: each cell's background, then each lit
        // block (top row first, left then right) drawn over it as a rectangle, separated blocks a
        // pixel in from each side (from 2× up). The canvas takes the rectangle's edges in single
        // precision to 1/256 of a pixel; a row the rectangle only partly covers gets that much of
        // its colour, premultiplied and blended in 8 bits as Skia does it.
        static void TeletextRow(AsciiArt art, int m, int r, int y, byte[] rgb)
        {
            int cellW = CellWidth * m, cellH = CellHeight * m, subW = cellW / 2;
            double subH = cellH / 3.0;
            int sep = art.Settings.TeletextSeparated && m >= 2 ? 1 : 0;
            int rowY = r * cellH;
            // how much of row y each block row covers (0: none, 256: all)
            var cover = new int[3];
            for (int sy = 0; sy < 3; sy++)
            {
                float top = (float)(rowY + sy * subH + sep), height = (float)(subH - sep * 2);
                float bottom = top + height;
                cover[sy] = Coverage(top, bottom, y);
            }
            for (int c = 0; c < art.Width; c++)
            {
                int cell = r * art.Width + c, i = cell * 3;
                int mask = art.TeletextMask(cell);
                if (mask == AsciiArt.None) mask = 0;
                int x0 = c * cellW;
                for (int x = 0; x < cellW; x++)
                {
                    int o = (x0 + x) * 3;
                    rgb[o] = art.Bg[i];
                    rgb[o + 1] = art.Bg[i + 1];
                    rgb[o + 2] = art.Bg[i + 2];
                }
                for (int sy = 0; sy < 3; sy++)
                {
                    int a = cover[sy];
                    if (a == 0) continue;
                    for (int sx = 0; sx < 2; sx++)
                    {
                        if ((mask & (1 << (sy * 2 + sx))) == 0) continue;
                        int from = x0 + sx * subW + sep, to = from + subW - sep * 2;
                        for (int x = from; x < to; x++)
                        {
                            int o = x * 3;
                            rgb[o] = Blend(art.Fg[i], rgb[o], a);
                            rgb[o + 1] = Blend(art.Fg[i + 1], rgb[o + 1], a);
                            rgb[o + 2] = Blend(art.Fg[i + 2], rgb[o + 2], a);
                        }
                    }
                }
            }
        }

        // how much of pixel row y a rectangle from `top` to `bottom` covers, as Skia's anti-aliased
        // rectangle fill counts it (its edges to 1/256 of a pixel; a whole row is 256)
        static int Coverage(float top, float bottom, int y)
        {
            int t = (int)Math.Floor(top * 256f + 0.5), b = (int)Math.Floor(bottom * 256f + 0.5);
            if (t >= b) return 0;
            int first = t >> 8;
            if (first == ((b - 1) >> 8)) return y == first ? b - t - 1 : 0;
            if ((t & 0xff) != 0)
            {
                if (y == first) return 256 - (t & 0xff);
                first++;
            }
            int last = b >> 8;
            if (y >= first && y < last) return 256;
            if ((b & 0xff) != 0 && y == last) return b & 0xff;
            return 0;
        }

        // a colour drawn over another with `a`/256 of it (256: all), premultiplied, in 8 bits
        static byte Blend(int src, int dst, int a) => a >= 256 ? (byte)src : (byte)(((src * (a + 1)) >> 8) + ((dst * (256 - a)) >> 8));

        // ---- the whole picture ----

        // the picture at `multiplier`, r, g, b a pixel, rows made on every core
        public static byte[] Rgb(AsciiArt art, int multiplier)
        {
            Size(art.Width, art.Height, multiplier, out int w, out int h);
            var rgb = new byte[(long)w * h * 3];
            EveryCore.For(0, h, () => new byte[w * 3], (y, state, row) =>
            {
                Row(art, multiplier, y, row);
                Buffer.BlockCopy(row, 0, rgb, y * w * 3, w * 3);
                return row;
            });
            return rgb;
        }

        // the picture as a PNG (r, g, b), its rows made as they're packed
        public static void WritePng(Stream output, AsciiArt art, int multiplier, out int width, out int height)
        {
            Size(art.Width, art.Height, multiplier, out int w, out int h);
            width = w;
            height = h;
            Png.WriteTruecolour(output, w, h, false, (y, row) => Row(art, multiplier, y, row));
        }
    }
}
