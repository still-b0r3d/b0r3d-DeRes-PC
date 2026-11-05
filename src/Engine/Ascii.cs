using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace B0r3dDeRes.Engine
{
    // The ASCII / ANSI studio's render modes and colour profiles, by the website's ids.
    static class AsciiModes
    {
        public const string HalfBlock = "half-block", Ramp = "ramp", Petscii = "petscii", Teletext = "teletext";
        public static readonly string[] All = { HalfBlock, Ramp, Petscii, Teletext };
    }

    static class AsciiColours
    {
        public const string Ansi16 = "ansi16", C64 = "c64", Teletext = "teletext", Green = "green", Amber = "amber", Mono = "mono", Truecolor = "truecolor";
        public static readonly string[] All = { Ansi16, C64, Teletext, Green, Amber, Mono, Truecolor };
    }

    // The studio's settings (the website's AsciiSettings), with its defaults.
    sealed class AsciiSettings
    {
        public string Mode = AsciiModes.HalfBlock;
        public double Columns = 80;
        public string RampPreset = "standard";
        public string CustomRamp = "";
        public bool InvertRamp;
        public string ColorMode = AsciiColours.Ansi16;
        public double Contrast;      // -100 to 100
        public double Brightness;    // -100 to 100
        public double Gamma = 1.0;   // 0.5 to 2.5 (the slider's)
        public bool TwoToOneAspect = true;
        public bool TeletextSeparated;
        public bool TeletextHeader = true;

        public AsciiSettings Clone() => (AsciiSettings)MemberwiseClone();

        // what the art is made with, for knowing when it must be made again
        public string Key => string.Join("|", Mode, JsNumber.Text(Columns), RampPreset, CustomRamp, InvertRamp, ColorMode, JsNumber.Text(Contrast), JsNumber.Text(Brightness),
            JsNumber.Text(Gamma), TwoToOneAspect, TeletextSeparated, TeletextHeader);

        // A typed-in ramp longer than this is cut (a ramp is a handful of characters).
        public const int MaxCustomRamp = 256;

        static double Num(double v, double lo, double hi) => Math.Min(hi, Math.Max(lo, v));

        // The settings checked field by field, as the website checks what it kept (checkAsciiSettings):
        // anything odd keeps `base`'s value. Numbers here are already numbers (NaN and infinities count as odd).
        public static AsciiSettings Check(string mode, double? columns, string rampPreset, string customRamp, bool? invertRamp, string colorMode, double? contrast,
            double? brightness, double? gamma, bool? twoToOne, bool? separated, bool? header, AsciiSettings b = null)
        {
            b = b ?? new AsciiSettings();
            bool Finite(double? v) => v.HasValue && !double.IsNaN(v.Value) && !double.IsInfinity(v.Value);
            return new AsciiSettings
            {
                Mode = AsciiModes.All.Contains(mode) ? mode : b.Mode,
                Columns = JsMath.Round(Finite(columns) ? Num(columns.Value, 20, 200) : b.Columns),
                RampPreset = Ascii.RampIds.Contains(rampPreset) ? rampPreset : b.RampPreset,
                CustomRamp = customRamp != null ? CutRamp(customRamp) : b.CustomRamp,
                InvertRamp = invertRamp ?? b.InvertRamp,
                ColorMode = AsciiColours.All.Contains(colorMode) ? colorMode : b.ColorMode,
                Contrast = Finite(contrast) ? Num(contrast.Value, -100, 100) : b.Contrast,
                Brightness = Finite(brightness) ? Num(brightness.Value, -100, 100) : b.Brightness,
                Gamma = Finite(gamma) ? Num(gamma.Value, 0.5, 2.5) : b.Gamma,
                TwoToOneAspect = twoToOne ?? b.TwoToOneAspect,
                TeletextSeparated = separated ?? b.TeletextSeparated,
                TeletextHeader = header ?? b.TeletextHeader,
            };
        }

        // the first MaxCustomRamp characters (code points, as [...text] counts them)
        public static string CutRamp(string text)
        {
            var output = new StringBuilder();
            int count = 0;
            for (int i = 0; i < text.Length && count < MaxCustomRamp; i++, count++)
            {
                output.Append(text[i]);
                if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1])) output.Append(text[++i]);
            }
            return output.ToString();
        }

        // Text pasted into a one-line box as Chromium's <input type=text> takes it: the line
        // breaks at its end dropped, every other one (CR LF, CR or LF) a space.
        public static string OneLine(string text)
        {
            int end = text.Length;
            while (end > 0 && (text[end - 1] == '\n' || text[end - 1] == '\r')) end--;
            return text.Substring(0, end).Replace("\r\n", " ").Replace('\r', ' ').Replace('\n', ' ');
        }
    }

    // ASCII art as a few arrays rather than an object a cell (the website's PackedAscii): each
    // cell's character, its colours, and its ANSI colours, Teletext mask and C64 colour (None
    // where it has none), row by row. Two frames can be compared row by row.
    sealed class AsciiArt
    {
        public const byte None = 255;

        public readonly int Width, Height;   // columns and rows
        public readonly AsciiSettings Settings;
        public readonly int ColourCount;
        // each cell's character as JavaScript's string has it: a code point, or a lone half of a
        // surrogate pair (a ramp is read one UTF-16 unit at a time, as on the website)
        public readonly int[] Chars;
        public readonly byte[] Fg, Bg;       // r, g, b a cell
        public readonly byte[] Extra;        // a cell's ANSI foreground and background, Teletext mask, C64 colour

        public AsciiArt(int width, int height, AsciiSettings settings, int colourCount, int[] chars, byte[] fg, byte[] bg, byte[] extra)
        {
            Width = width;
            Height = height;
            Settings = settings;
            ColourCount = colourCount;
            Chars = chars;
            Fg = fg;
            Bg = bg;
            Extra = extra;
        }

        public int Cells => Width * Height;

        public int FgAnsi(int i) => Extra[i * 4];
        public int BgAnsi(int i) => Extra[i * 4 + 1];
        public int TeletextMask(int i) => Extra[i * 4 + 2];
        public int C64Colour(int i) => Extra[i * 4 + 3];

        // a cell's character as text
        public string Text(int i) => Ascii.CharText(Chars[i]);

        // whether row y looks the same in both (characters and colours); false when they differ in size
        public bool SameRow(AsciiArt other, int y)
        {
            if (other == null || other.Width != Width || other.Height != Height) return false;
            int from = y * Width, to = from + Width;
            for (int i = from; i < to; i++) if (Chars[i] != other.Chars[i]) return false;
            for (int i = from * 3; i < to * 3; i++) if (Fg[i] != other.Fg[i] || Bg[i] != other.Bg[i]) return false;
            return true;
        }
    }

    // Pictures as BBS-style text art (the website's core/ascii.ts): half blocks (▀, two pixels a
    // character, in truecolour or the ANSI colours), character ramps with 2:1 cells, PETSCII on a
    // C64 screen and Teletext mosaics; then the art as plain text, Markdown, ANSI escapes, HTML,
    // .ans with a SAUCE record, an ANSImation, a C64 .seq stream or .prg, and Teletext as text.
    // The same characters, colours and bytes as the website's.
    static class Ascii
    {
        // the ramps, darkest first (the website's ASCII_RAMPS)
        public static readonly (string Id, string Ramp)[] Ramps =
        {
            ("standard", " .:-=+*#%@"),
            ("detailed", @" .'`^"",:;Il!i><~+_-?][}{1)(|\/tfjrxnuvczXYUJCLQ0OZmwqpdbkhao*#MW&8%B@$"),
            ("blocks", " ░▒▓█"),
            ("katakana", " ｦｱｳｴｵｶｷｹｺｻｼｽｾｿﾀﾂﾃﾅﾆﾇﾈﾊﾋﾎﾏﾐﾑﾒﾓﾔﾕﾗﾘﾜ"),
            ("binary", " 01"),
        };

        public const string CustomRamp = "custom";
        public static readonly string[] RampIds = Ramps.Select(r => r.Id).Concat(new[] { CustomRamp }).ToArray();

        // the standard 16 ANSI / PC CGA colours (0 black … 15 bright white)
        public static readonly byte[] Ansi16Palette =
        {
            0, 0, 0, 0, 0, 170, 0, 170, 0, 0, 170, 170, 170, 0, 0, 170, 0, 170, 170, 85, 0, 170, 170, 170,
            85, 85, 85, 85, 85, 255, 85, 255, 85, 85, 255, 255, 255, 85, 85, 255, 85, 255, 255, 255, 85, 255, 255, 255,
        };

        // the Commodore 64's VIC-II colours
        public static readonly byte[] C64Palette =
        {
            0, 0, 0, 255, 255, 255, 129, 51, 56, 117, 206, 200, 142, 60, 151, 86, 172, 77, 46, 44, 155, 237, 241, 113,
            142, 80, 41, 85, 56, 0, 196, 108, 113, 74, 74, 74, 123, 123, 123, 169, 255, 159, 112, 109, 235, 178, 178, 178,
        };

        // broadcast Teletext's eight colours
        public static readonly byte[] TeletextPalette = { 0, 0, 0, 255, 0, 0, 0, 255, 0, 255, 255, 0, 0, 0, 255, 255, 0, 255, 0, 255, 255, 255, 255, 255 };

        static readonly PaletteMatcher ansi16 = new PaletteMatcher(Ansi16Palette), c64 = new PaletteMatcher(C64Palette), teletext = new PaletteMatcher(TeletextPalette);

        // (one matcher is used from many threads: its cache fills in the same answers whoever asks)
        public static int MatchAnsi16(int r, int g, int b) => ansi16.NearestByte(r, g, b);
        public static int MatchC64(int r, int g, int b) => c64.NearestByte(r, g, b);
        public static int MatchTeletext(int r, int g, int b) => teletext.NearestByte(r, g, b);

        // Teletext's 2×3 mosaics by mask (bit 0 top left, 1 top right, 2 and 3 the middle, 4 and
        // 5 the bottom): Unicode's sextants (U+1FB00 on), which leave out the four shapes that had
        // characters already: empty (space), the left half 21 (▌), the right half 42 (▐), all 63 (█)
        public static readonly int[] TeletextMosaics = Enumerable.Range(0, 64).Select(m =>
            m == 0 ? 0x20 : m == 21 ? 0x258C : m == 42 ? 0x2590 : m == 63 ? 0x2588 : 0x1FB00 + m - (m < 21 ? 1 : m < 42 ? 2 : 3)).ToArray();

        // the C64 screen codes (upper case and graphics) for the characters PETSCII mode uses: $80
        // more shows a character reversed
        public static readonly Dictionary<int, int> PetsciiScreenCodes = new Dictionary<int, int>
        {
            { ' ', 0x20 }, { '█', 0xa0 }, { '▌', 0x61 }, { '▐', 0xe1 }, { '▄', 0x62 }, { '▀', 0xe2 }, { '▒', 0x66 }, { '░', 0x66 }, { '▓', 0xa0 },
            { '▖', 0x7b }, { '▗', 0x6c }, { '▘', 0x7e }, { '▝', 0x7c }, { '▚', 0x7f }, { '▞', 0xff }, { '━', 0x40 }, { '┃', 0x5d },
            { '♠', 0x41 }, { '♥', 0x53 }, { '♣', 0x58 }, { '♦', 0x5a }, { '●', 0x51 },
        };

        public const int C64Columns = 40, C64Rows = 25;

        // the colour control codes a .seq prints to change colour, by C64 colour
        public static readonly byte[] PetsciiColourCodes = { 0x90, 0x05, 0x1c, 0x9f, 0x9c, 0x1e, 0x1f, 0x9e, 0x81, 0x95, 0x96, 0x97, 0x98, 0x99, 0x9a, 0x9b };

        // the code page 437 bytes of the characters a .ans holds (the rest: printable ASCII as itself, else a space)
        static readonly Dictionary<int, byte> cp437 = new Dictionary<int, byte>
        {
            { ' ', 0x20 }, { '▀', 0xdf }, { '▄', 0xdc }, { '█', 0xdb }, { '░', 0xb0 }, { '▒', 0xb1 }, { '▓', 0xb2 },
            { '│', 0xb3 }, { '┤', 0xb4 }, { '╡', 0xb5 }, { '╢', 0xb6 }, { '╖', 0xb7 }, { '╕', 0xb8 }, { '╣', 0xb9 }, { '║', 0xba }, { '╗', 0xbb },
            { '╝', 0xbc }, { '╜', 0xbd }, { '╛', 0xbe }, { '┐', 0xbf }, { '└', 0xc0 }, { '┴', 0xc1 }, { '┬', 0xc2 }, { '├', 0xc3 }, { '─', 0xc4 },
            { '┼', 0xc5 }, { '╞', 0xc6 }, { '╟', 0xc7 }, { '╚', 0xc8 }, { '╔', 0xc9 }, { '╩', 0xca }, { '╦', 0xcb }, { '╠', 0xcc }, { '═', 0xcd }, { '╬', 0xce },
        };

        // an ANSI 16 colour as the DOS ANSI colour number (0 black, 1 red, 2 green, 3 yellow, 4 blue, 5 magenta, 6 cyan, 7 white)
        static readonly int[] ansiToDos = { 0, 4, 2, 6, 1, 5, 3, 7 };

        // a cell's character as JavaScript's string has it (a lone surrogate stays one)
        public static string CharText(int c) => c >= 0x10000 ? char.ConvertFromUtf32(c) : ((char)c).ToString();

        // ---- the picture, as the website's canvas holds it ----

        // A picture's pixels as the website reads them (its canvas, then getImageData): every
        // see-through pixel's colour premultiplied by its alpha and divided out again in single
        // precision, rounding half to even (fully see-through ones come out black). Opaque pixels
        // and an animated GIF's frames (which never go through a canvas there) stay as they are.
        // A level a channel, by alpha, from 1 to 254.
        static readonly byte[] canvasLevels = CanvasLevels();

        static byte[] CanvasLevels()
        {
            var table = new byte[256 * 256];
            float k255 = (float)(1.0 / 255);
            for (int a = 1; a < 255; a++)
            {
                float pa = a * k255, inverse = 1f / pa;
                for (int c = 0; c < 256; c++)
                {
                    // premultiplied as Blink's decoders do, then back as Skia's pipeline does it
                    int p = (c * a * 257 + 257 * 128) >> 16;
                    float v = p * k255;
                    v *= inverse;
                    float scaled = v * 255;
                    table[a * 256 + c] = (byte)Math.Min(255, Math.Round((double)scaled, MidpointRounding.ToEven));
                }
            }
            return table;
        }

        // one channel's level as the canvas gives it back
        public static int CanvasLevel(int c, int a) => a == 255 ? c : a == 0 ? 0 : canvasLevels[a * 256 + c];

        // The pixels the art is made from: r, g, b of each pixel, read straight from the picture's
        // b, g, r, a bytes (`asCanvas`: through CanvasLevel when any pixel is see-through).
        sealed class Pixels
        {
            readonly byte[] bgra;
            readonly bool canvas;
            public readonly int Width, Height;

            public Pixels(PictureSource src, bool asCanvas)
            {
                bgra = src.Bgra;
                Width = src.Width;
                Height = src.Height;
                canvas = asCanvas && src.HasAlpha;
            }

            // The mean colour of a box of pixels (the website's sampleArea): whole pixels from
            // floor(x0) to floor(x1 - 0.0001), each channel's sum over their count, rounded. A box
            // that starts below the picture (the half-block's last half row, when the rows are odd)
            // reads past its end: JavaScript makes that NaN, which goes on as NaN.
            public void Sample(double x0, double y0, double x1, double y1, out double r, out double g, out double b)
            {
                int minX = (int)Math.Max(0, Math.Floor(x0));
                int maxX = (int)Math.Max(minX, Math.Min(Width - 1, Math.Floor(x1 - 0.0001)));
                int minY = (int)Math.Max(0, Math.Floor(y0));
                int maxY = (int)Math.Max(minY, Math.Min(Height - 1, Math.Floor(y1 - 0.0001)));
                if (minY >= Height)
                {
                    r = g = b = double.NaN;
                    return;
                }
                long rSum = 0, gSum = 0, bSum = 0;
                long count = 0;
                for (int y = minY; y <= maxY; y++)
                {
                    long row = (long)y * Width * 4;
                    for (int x = minX; x <= maxX; x++)
                    {
                        long i = row + (long)x * 4;
                        if (canvas)
                        {
                            int a = bgra[i + 3];
                            rSum += CanvasLevel(bgra[i + 2], a);
                            gSum += CanvasLevel(bgra[i + 1], a);
                            bSum += CanvasLevel(bgra[i], a);
                        }
                        else
                        {
                            rSum += bgra[i + 2];
                            gSum += bgra[i + 1];
                            bSum += bgra[i];
                        }
                        count++;
                    }
                }
                r = JsMath.Round((double)rSum / count);
                g = JsMath.Round((double)gSum / count);
                b = JsMath.Round((double)bSum / count);
            }
        }

        // the contrast, brightness and gamma, in linear light (the website's applyAdjustments);
        // a NaN level stays NaN when there's nothing to adjust, and comes out 0 when there is
        static void Adjust(ref double r, ref double g, ref double b, AsciiSettings s)
        {
            if (s.Contrast == 0 && s.Brightness == 0 && s.Gamma == 1.0) return;
            double lr = Linear(r), lg = Linear(g), lb = Linear(b);
            if (s.Brightness != 0)
            {
                double shift = s.Brightness / 100;
                lr = Math.Max(0, lr + shift);
                lg = Math.Max(0, lg + shift);
                lb = Math.Max(0, lb + shift);
            }
            if (s.Contrast != 0)
            {
                double factor = Math.Max(0, (100 + s.Contrast) / 100);
                lr = Math.Max(0, (lr - 0.5) * factor + 0.5);
                lg = Math.Max(0, (lg - 0.5) * factor + 0.5);
                lb = Math.Max(0, (lb - 0.5) * factor + 0.5);
            }
            if (s.Gamma != 1.0 && s.Gamma > 0.01)
            {
                double inverse = 1 / s.Gamma;
                lr = JsMath.Pow(Math.Max(0, lr), inverse);
                lg = JsMath.Pow(Math.Max(0, lg), inverse);
                lb = JsMath.Pow(Math.Max(0, lb), inverse);
            }
            r = Colour.LinearToByte(lr);
            g = Colour.LinearToByte(lg);
            b = Colour.LinearToByte(lb);
        }

        static double Linear(double level) => double.IsNaN(level) ? double.NaN : Colour.ByteToLinear[(int)level];

        // the luminance (Rec. 709) of an 8-bit colour
        public static double Luminance(double r, double g, double b) => 0.2126 * (r / 255) + 0.7152 * (g / 255) + 0.0722 * (b / 255);

        // a colour as JavaScript's set of "r,g,b" texts tells them apart (NaN is a value of its own)
        static int ColourKey(double r, double g, double b)
        {
            int K(double v) => double.IsNaN(v) ? 256 : (int)v;
            return (K(r) * 257 + K(g)) * 257 + K(b);
        }

        // a level as it's kept (a Uint8Array keeps NaN as 0)
        static byte Level(double v) => double.IsNaN(v) ? (byte)0 : (byte)Math.Max(0, Math.Min(255, v));

        // the ramp the settings use
        public static string RampOf(AsciiSettings s)
        {
            string ramp = Ramps[0].Ramp;
            if (s.RampPreset == CustomRamp && !string.IsNullOrEmpty(s.CustomRamp)) ramp = s.CustomRamp;
            else
                foreach (var r in Ramps)
                    if (r.Id == s.RampPreset) ramp = r.Ramp;
            if (ramp.Length == 0) ramp = " #";
            return ramp;
        }

        // the columns the art gets (the PETSCII screen aside)
        public static int ColumnsOf(AsciiSettings s) => (int)Math.Max(1, Math.Min(320, JsMath.Round(s.Columns)));

        const string TeletextHeaderText = "P100  CEEFAX 100  OCT 07  b0r3d.org      ";

        // The art of a picture (the website's generateAscii). `asCanvas`: read it as the
        // website's canvas holds it (see CanvasLevel); an animated GIF's frames aren't.
        public static AsciiArt Generate(PictureSource src, AsciiSettings settings, bool asCanvas = true)
        {
            AsciiSettings s = settings.Clone();
            var px = new Pixels(src, asCanvas);
            int srcW = src.Width, srcH = src.Height;
            int cols = ColumnsOf(s);
            int aspect = s.TwoToOneAspect ? 2 : 1;
            string ramp = RampOf(s);
            var used = new HashSet<int>();
            Builder art;

            if (s.Mode == AsciiModes.Teletext)
            {
                int rows = (int)Math.Max(1, JsMath.Round((double)cols * srcH / ((double)srcW * aspect)));
                art = new Builder(cols, rows);
                double cellW = (double)srcW / cols, cellH = (double)srcH / rows;
                for (int r = 0; r < rows; r++)
                {
                    bool header = s.TeletextHeader && r == 0;
                    double y0 = r * cellH, y1 = (r + 1) * cellH;
                    for (int c = 0; c < cols; c++)
                    {
                        if (header)
                        {
                            char ch = c < TeletextHeaderText.Length ? TeletextHeaderText[c] : ' ';
                            byte[] fg = c < 4 ? new byte[] { 255, 255, 0 } : c < 16 ? new byte[] { 0, 255, 255 } : new byte[] { 255, 255, 255 };
                            art.Put(ch, fg[0], fg[1], fg[2], 0, 0, 170, None, None, 0, None);
                            continue;
                        }
                        double x0 = c * cellW, x1 = (c + 1) * cellW, xm = (x0 + x1) / 2, yh = (y1 - y0) / 3;
                        double[][] boxes =
                        {
                            new[] { x0, y0, xm, y0 + yh }, new[] { xm, y0, x1, y0 + yh },
                            new[] { x0, y0 + yh, xm, y0 + 2 * yh }, new[] { xm, y0 + yh, x1, y0 + 2 * yh },
                            new[] { x0, y0 + 2 * yh, xm, y1 }, new[] { xm, y0 + 2 * yh, x1, y1 },
                        };
                        int mask = 0, active = 0;
                        double rSum = 0, gSum = 0, bSum = 0;
                        for (int k = 0; k < 6; k++)
                        {
                            double[] box = boxes[k];
                            px.Sample(box[0], box[1], box[2], box[3], out double sr, out double sg, out double sb);
                            Adjust(ref sr, ref sg, ref sb, s);
                            if (Luminance(sr, sg, sb) > 0.35)
                            {
                                mask |= 1 << k;
                                rSum += sr;
                                gSum += sg;
                                bSum += sb;
                                active++;
                            }
                        }
                        int fgR = 255, fgG = 255, fgB = 255;
                        if (active > 0)
                        {
                            int index = MatchTeletext((int)JsMath.Round(rSum / active), (int)JsMath.Round(gSum / active), (int)JsMath.Round(bSum / active));
                            if (index == 0) index = 7;
                            fgR = TeletextPalette[index * 3];
                            fgG = TeletextPalette[index * 3 + 1];
                            fgB = TeletextPalette[index * 3 + 2];
                        }
                        used.Add(ColourKey(fgR, fgG, fgB));
                        used.Add(ColourKey(0, 0, 0));
                        art.Put(TeletextMosaics[mask], fgR, fgG, fgB, 0, 0, 0, None, None, mask, None);
                    }
                }
                return art.Done(s, used.Count);
            }

            if (s.Mode == AsciiModes.Petscii)
            {
                // a C64 screen is 40×25 characters: a wide picture fills the 40 columns, a tall
                // one is 25 rows high and narrower (the exports centre it)
                int pc = C64Columns;
                int rows = (int)Math.Max(1, JsMath.Round((double)pc * srcH / ((double)srcW * aspect)));
                if (rows > C64Rows)
                {
                    rows = C64Rows;
                    pc = (int)Math.Max(1, Math.Min(C64Columns, JsMath.Round((double)rows * srcW * aspect / srcH)));
                }
                art = new Builder(pc, rows);
                double cellW = (double)srcW / pc, cellH = (double)srcH / rows;
                for (int r = 0; r < rows; r++)
                {
                    double y0 = r * cellH, y1 = (r + 1) * cellH, ym = (y0 + y1) / 2;
                    for (int c = 0; c < pc; c++)
                    {
                        double x0 = c * cellW, x1 = (c + 1) * cellW, xm = (x0 + x1) / 2;
                        px.Sample(x0, y0, xm, ym, out double r0, out double g0, out double b0);
                        px.Sample(xm, y0, x1, ym, out double r1, out double g1, out double b1);
                        px.Sample(x0, ym, xm, y1, out double r2, out double g2, out double b2);
                        px.Sample(xm, ym, x1, y1, out double r3, out double g3, out double b3);
                        Adjust(ref r0, ref g0, ref b0, s);
                        Adjust(ref r1, ref g1, ref b1, s);
                        Adjust(ref r2, ref g2, ref b2, s);
                        Adjust(ref r3, ref g3, ref b3, s);
                        double l0 = Luminance(r0, g0, b0), l1 = Luminance(r1, g1, b1), l2 = Luminance(r2, g2, b2), l3 = Luminance(r3, g3, b3);
                        double avg = (l0 + l1 + l2 + l3) / 4;
                        double thresh = Math.Max(0.25, Math.Min(0.75, avg));
                        int bits = (l0 >= thresh ? 8 : 0) | (l1 >= thresh ? 4 : 0) | (l2 >= thresh ? 2 : 0) | (l3 >= thresh ? 1 : 0);
                        int ch;
                        switch (bits)
                        {
                            case 0b1111: ch = '█'; break;
                            case 0b0000: ch = ' '; break;
                            case 0b1100: ch = '▀'; break;
                            case 0b0011: ch = '▄'; break;
                            case 0b1010: ch = '▌'; break;
                            case 0b0101: ch = '▐'; break;
                            case 0b1000: ch = '▘'; break;
                            case 0b0100: ch = '▝'; break;
                            case 0b0010: ch = '▖'; break;
                            case 0b0001: ch = '▗'; break;
                            case 0b1001: ch = '▚'; break;
                            case 0b0110: ch = '▞'; break;
                            default:
                                ch = avg > 0.8 ? '█' : avg > 0.6 ? '▓' : avg > 0.4 ? '▒' : avg > 0.2 ? '░' : ' ';
                                break;
                        }
                        px.Sample(x0, y0, x1, y1, out double wr, out double wg, out double wb);
                        Adjust(ref wr, ref wg, ref wb, s);
                        int index = MatchC64(Byte(wr), Byte(wg), Byte(wb));
                        int shown = index == 0 && ch != ' ' ? 1 : index;
                        int fr = C64Palette[shown * 3], fg = C64Palette[shown * 3 + 1], fb = C64Palette[shown * 3 + 2];
                        used.Add(ColourKey(fr, fg, fb));
                        used.Add(ColourKey(0, 0, 0));
                        art.Put(ch, fr, fg, fb, 0, 0, 0, None, None, None, index);
                    }
                }
                return art.Done(s, used.Count);
            }

            if (s.Mode == AsciiModes.HalfBlock)
            {
                // each character (▀) is two pixels, its top the foreground and its bottom the background
                int pixelH = (int)Math.Max(2, JsMath.Round((double)cols * srcH / srcW));
                int rows = (int)Math.Ceiling(pixelH / 2.0);
                art = new Builder(cols, rows);
                double cellW = (double)srcW / cols, sub = (double)srcH / pixelH;
                for (int r = 0; r < rows; r++)
                {
                    double topY0 = r * 2 * sub, topY1 = (r * 2 + 1) * sub, botY0 = topY1, botY1 = Math.Min(srcH, (r * 2 + 2) * sub);
                    for (int c = 0; c < cols; c++)
                    {
                        double x0 = c * cellW, x1 = (c + 1) * cellW;
                        px.Sample(x0, topY0, x1, topY1, out double tr, out double tg, out double tb);
                        px.Sample(x0, botY0, x1, botY1, out double br, out double bg, out double bb);
                        Adjust(ref tr, ref tg, ref tb, s);
                        Adjust(ref br, ref bg, ref bb, s);
                        double fR, fG, fB, kR, kG, kB;
                        int fgAnsi = None, bgAnsi = None;
                        switch (s.ColorMode)
                        {
                            case AsciiColours.Ansi16:
                                fgAnsi = MatchAnsi16(Byte(tr), Byte(tg), Byte(tb));
                                bgAnsi = MatchAnsi16(Byte(br), Byte(bg), Byte(bb));
                                Entry(Ansi16Palette, fgAnsi, out fR, out fG, out fB);
                                Entry(Ansi16Palette, bgAnsi, out kR, out kG, out kB);
                                break;
                            case AsciiColours.C64:
                                Entry(C64Palette, MatchC64(Byte(tr), Byte(tg), Byte(tb)), out fR, out fG, out fB);
                                Entry(C64Palette, MatchC64(Byte(br), Byte(bg), Byte(bb)), out kR, out kG, out kB);
                                break;
                            case AsciiColours.Teletext:
                                Entry(TeletextPalette, MatchTeletext(Byte(tr), Byte(tg), Byte(tb)), out fR, out fG, out fB);
                                Entry(TeletextPalette, MatchTeletext(Byte(br), Byte(bg), Byte(bb)), out kR, out kG, out kB);
                                break;
                            case AsciiColours.Green:
                            {
                                double tl = Luminance(tr, tg, tb), bl = Luminance(br, bg, bb);
                                fR = 0;
                                fG = JsMath.Round(tl * 255);
                                fB = JsMath.Round(tl * 102);
                                kR = 10;
                                kG = JsMath.Round(bl * 36 + 18);
                                kB = 10;
                                break;
                            }
                            case AsciiColours.Amber:
                            {
                                double tl = Luminance(tr, tg, tb), bl = Luminance(br, bg, bb);
                                fR = JsMath.Round(tl * 255);
                                fG = JsMath.Round(tl * 176);
                                fB = 0;
                                kR = 20;
                                kG = JsMath.Round(bl * 24 + 13);
                                kB = 0;
                                break;
                            }
                            case AsciiColours.Mono:
                            {
                                double tl = JsMath.Round(Luminance(tr, tg, tb) * 230), bl = JsMath.Round(Luminance(br, bg, bb) * 40);
                                fR = fG = fB = tl;
                                kR = kG = kB = bl;
                                break;
                            }
                            default:
                                fR = tr;
                                fG = tg;
                                fB = tb;
                                kR = br;
                                kG = bg;
                                kB = bb;
                                break;
                        }
                        used.Add(ColourKey(fR, fG, fB));
                        used.Add(ColourKey(kR, kG, kB));
                        art.Put(0x2580, fR, fG, fB, kR, kG, kB, fgAnsi, bgAnsi, None, None);
                    }
                }
                return art.Done(s, used.Count);
            }

            // a character a cell, from the ramp by its brightness
            {
                int rows = (int)Math.Max(1, JsMath.Round((double)cols * srcH / ((double)srcW * aspect)));
                art = new Builder(cols, rows);
                double cellW = (double)srcW / cols, cellH = (double)srcH / rows;
                for (int r = 0; r < rows; r++)
                {
                    double y0 = r * cellH, y1 = (r + 1) * cellH;
                    for (int c = 0; c < cols; c++)
                    {
                        double x0 = c * cellW, x1 = (c + 1) * cellW;
                        px.Sample(x0, y0, x1, y1, out double cr, out double cg, out double cb);
                        Adjust(ref cr, ref cg, ref cb, s);
                        double lum = Math.Max(0, Math.Min(1, Luminance(cr, cg, cb)));
                        int idx = (int)Math.Floor(lum * ramp.Length);
                        if (idx >= ramp.Length) idx = ramp.Length - 1;
                        if (s.InvertRamp) idx = ramp.Length - 1 - idx;
                        int ch = ramp[idx];
                        double fR, fG, fB, kR, kG, kB;
                        int fgAnsi = None, bgAnsi = None;
                        switch (s.ColorMode)
                        {
                            case AsciiColours.Ansi16:
                                fgAnsi = MatchAnsi16(Byte(cr), Byte(cg), Byte(cb));
                                bgAnsi = 0;
                                Entry(Ansi16Palette, fgAnsi, out fR, out fG, out fB);
                                Entry(Ansi16Palette, 0, out kR, out kG, out kB);
                                break;
                            case AsciiColours.C64:
                            {
                                int i = MatchC64(Byte(cr), Byte(cg), Byte(cb));
                                Entry(C64Palette, i == 0 ? 1 : i, out fR, out fG, out fB);
                                kR = kG = kB = 0;
                                break;
                            }
                            case AsciiColours.Teletext:
                            {
                                int i = MatchTeletext(Byte(cr), Byte(cg), Byte(cb));
                                Entry(TeletextPalette, i == 0 ? 7 : i, out fR, out fG, out fB);
                                kR = kG = kB = 0;
                                break;
                            }
                            case AsciiColours.Green:
                                fR = 0;
                                fG = JsMath.Round(lum * 255);
                                fB = JsMath.Round(lum * 102);
                                kR = 10;
                                kG = 18;
                                kB = 10;
                                break;
                            case AsciiColours.Amber:
                                fR = JsMath.Round(lum * 255);
                                fG = JsMath.Round(lum * 176);
                                fB = 0;
                                kR = 20;
                                kG = 13;
                                kB = 0;
                                break;
                            case AsciiColours.Mono:
                            {
                                double v = JsMath.Round(lum * 230);
                                fR = fG = fB = v;
                                kR = kG = kB = 19;
                                break;
                            }
                            default:
                                fR = cr;
                                fG = cg;
                                fB = cb;
                                kR = kG = kB = 0;
                                break;
                        }
                        used.Add(ColourKey(fR, fG, fB));
                        art.Put(ch, fR, fG, fB, kR, kG, kB, fgAnsi, bgAnsi, None, None);
                    }
                }
                return art.Done(s, used.Count);
            }
        }

        const int None = AsciiArt.None;

        // a level for the palette matchers: JavaScript's NaN goes through `>> 2` as 0
        static int Byte(double v) => double.IsNaN(v) ? 0 : (int)v;

        static void Entry(byte[] palette, int i, out double r, out double g, out double b)
        {
            r = palette[i * 3];
            g = palette[i * 3 + 1];
            b = palette[i * 3 + 2];
        }

        // the art's arrays, filled in cell by cell
        sealed class Builder
        {
            readonly int width, height;
            readonly int[] chars;
            readonly byte[] fg, bg, extra;
            int at;

            public Builder(int width, int height)
            {
                this.width = width;
                this.height = height;
                int n = width * height;
                chars = new int[n];
                fg = new byte[n * 3];
                bg = new byte[n * 3];
                extra = new byte[n * 4];
            }

            public void Put(int ch, double fr, double fgr, double fb, double br, double bgr, double bb, int fgAnsi, int bgAnsi, int mask, int c64)
            {
                chars[at] = ch;
                fg[at * 3] = Level(fr);
                fg[at * 3 + 1] = Level(fgr);
                fg[at * 3 + 2] = Level(fb);
                bg[at * 3] = Level(br);
                bg[at * 3 + 1] = Level(bgr);
                bg[at * 3 + 2] = Level(bb);
                extra[at * 4] = (byte)fgAnsi;
                extra[at * 4 + 1] = (byte)bgAnsi;
                extra[at * 4 + 2] = (byte)mask;
                extra[at * 4 + 3] = (byte)c64;
                at++;
            }

            public AsciiArt Done(AsciiSettings s, int colours) => new AsciiArt(width, height, s, colours, chars, fg, bg, extra);
        }

        // ---- the art as text ----

        public static string PlainText(AsciiArt art)
        {
            var text = new StringBuilder();
            for (int y = 0; y < art.Height; y++)
            {
                if (y > 0) text.Append('\n');
                for (int x = 0; x < art.Width; x++) text.Append(art.Text(y * art.Width + x));
            }
            return text.ToString();
        }

        public static string Markdown(AsciiArt art) => "```text\n" + PlainText(art) + "\n```";

        const char Esc = (char)0x1b;

        // the art as a terminal's ANSI escapes: the ANSI colours where the cells have them (with
        // ANSI 16 picked), truecolour codes otherwise, a reset at the end of each line
        public static string AnsiText(AsciiArt art)
        {
            bool isAnsi16 = art.Settings.ColorMode == AsciiColours.Ansi16;
            var lines = new List<string>();
            for (int y = 0; y < art.Height; y++)
            {
                var line = new StringBuilder();
                int lastFg = -1, lastBg = -1;
                string lastFgRgb = "", lastBgRgb = "";
                for (int x = 0; x < art.Width; x++)
                {
                    int i = y * art.Width + x;
                    int fa = art.FgAnsi(i), ba = art.BgAnsi(i);
                    if (isAnsi16 && fa != None && ba != None)
                    {
                        if (fa != lastFg || ba != lastBg)
                        {
                            line.Append(Esc).Append('[').Append(AnsiCodes(fa, ba)).Append('m');
                            lastFg = fa;
                            lastBg = ba;
                        }
                    }
                    else
                    {
                        string fgKey = Triple(art.Fg, i), bgKey = Triple(art.Bg, i);
                        if (fgKey != lastFgRgb || bgKey != lastBgRgb)
                        {
                            line.Append(Esc).Append("[38;2;").Append(fgKey).Append(";48;2;").Append(bgKey).Append('m');
                            lastFgRgb = fgKey;
                            lastBgRgb = bgKey;
                        }
                    }
                    line.Append(art.Text(i));
                }
                line.Append(Esc).Append("[0m");
                lines.Add(line.ToString());
            }
            return string.Join("\n", lines);
        }

        static string Triple(byte[] c, int i) => c[i * 3].ToString(CultureInfo.InvariantCulture) + ";" + c[i * 3 + 1].ToString(CultureInfo.InvariantCulture) + ";" + c[i * 3 + 2].ToString(CultureInfo.InvariantCulture);

        // "0;31;40", "1;37;44": the foreground (bright from 8 up) and the background
        static string AnsiCodes(int fg, int bg) => (fg >= 8 ? "1;3" + ansiToDos[fg - 8] : "0;3" + ansiToDos[fg]) + ";4" + ansiToDos[bg % 8];

        // the art as a self-contained HTML <pre>, a span for each run of cells that share their colours
        public static string Html(AsciiArt art)
        {
            var lines = new List<string>();
            for (int y = 0; y < art.Height; y++)
            {
                var line = new StringBuilder();
                var run = new StringBuilder();
                string runFg = "", runBg = "";
                void Flush()
                {
                    if (run.Length == 0) return;
                    line.Append("<span style=\"color:#").Append(runFg).Append(";background-color:#").Append(runBg).Append("\">").Append(EscapeHtml(run.ToString())).Append("</span>");
                    run.Clear();
                }
                for (int x = 0; x < art.Width; x++)
                {
                    int i = y * art.Width + x;
                    string fg = Hex(art.Fg, i), bg = Hex(art.Bg, i);
                    if (fg != runFg || bg != runBg)
                    {
                        Flush();
                        runFg = fg;
                        runBg = bg;
                    }
                    run.Append(art.Text(i));
                }
                Flush();
                lines.Add(line.ToString());
            }
            return "<pre style=\"font-family:'IBM VGA 8x16',monospace;background:#000;color:#aaa;line-height:1;letter-spacing:0;font-size:16px;white-space:pre;overflow:auto\">\n" +
                string.Join("\n", lines) + "\n</pre>";
        }

        static string Hex(byte[] c, int i) => c[i * 3].ToString("x2", CultureInfo.InvariantCulture) + c[i * 3 + 1].ToString("x2", CultureInfo.InvariantCulture) + c[i * 3 + 2].ToString("x2", CultureInfo.InvariantCulture);

        static string EscapeHtml(string text) => text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

        // ---- .ans files ----

        // a row of the art as .ans bytes: code page 437 characters, with an ANSI colour code
        // wherever the colours change (and at the row's start), for cells that have ANSI colours
        static void WriteAnsiRow(AsciiArt art, int y, Stream output)
        {
            int lastFg = -1, lastBg = -1;
            for (int x = 0; x < art.Width; x++)
            {
                int i = y * art.Width + x;
                int fa = art.FgAnsi(i), ba = art.BgAnsi(i);
                if (fa != None && ba != None && (fa != lastFg || ba != lastBg))
                {
                    WriteAscii(output, Esc + "[" + AnsiCodes(fa, ba) + "m");
                    lastFg = fa;
                    lastBg = ba;
                }
                output.WriteByte(Cp437(art.Chars[i]));
            }
        }

        // a character's code page 437 byte: the art's own block characters, printable ASCII as
        // itself (the first UTF-16 unit of the character), anything else a space
        static byte Cp437(int c)
        {
            if (cp437.TryGetValue(c, out byte b)) return b;
            int code = c >= 0x10000 ? char.ConvertFromUtf32(c)[0] : c;
            return code >= 32 && code <= 126 ? (byte)code : (byte)0x20;
        }

        static void WriteAscii(Stream output, string text)
        {
            for (int i = 0; i < text.Length; i++) output.WriteByte((byte)text[i]);
        }

        // the SAUCE record's fields for art of this size (no author: the person made the art)
        static SauceInfo AnsiSauce(AsciiArt art, SauceInfo given) => new SauceInfo
        {
            Title = given?.Title ?? "B0r3d ANSI Art",
            Author = given?.Author ?? "",
            Group = given?.Group ?? "b0r3d.org",
            Date = given?.Date,
            DataType = given?.DataType,
            FileType = given?.FileType,
            TInfo1 = given?.TInfo1 ?? art.Width,
            TInfo2 = given?.TInfo2 ?? art.Height,
            TFlags = given?.TFlags,
            FontName = given?.FontName ?? "IBM VGA",
        };

        // the art as a .ans: code page 437 with ANSI colours, CR LF line ends, a reset, then the SAUCE record
        public static byte[] AnsBytes(AsciiArt art, SauceInfo sauce = null)
        {
            var output = new MemoryStream();
            for (int y = 0; y < art.Height; y++)
            {
                WriteAnsiRow(art, y, output);
                WriteAscii(output, "\r\n");
            }
            WriteAscii(output, Esc + "[0m");
            return Sauce.Attach(output.ToArray(), AnsiSauce(art, sauce));
        }

        // Puts the cursor top left: every frame of an ANSImation starts with it.
        public static readonly string AnsiHome = Esc + "[H";

        // An ANSImation (.ans): frames of the same size, each drawn over the last (the website's
        // AnsimationWriter). The screen is cleared once, at the start; every frame begins with
        // cursor-home and each row after the first with the cursor put at its line (a row as wide
        // as the screen wraps by itself in some viewers, so a CR LF would skip a line). A .ans has
        // no timing: it plays as fast as the viewer (or the modem) sends it. One SAUCE record at the
        // end gives one frame's size. It's written as it goes, so a long video's never held whole.
        public sealed class AnsimationWriter
        {
            readonly Stream output;
            readonly CountingStream counted;
            int width = -1, height = -1;
            AsciiArt first;

            public int Frames { get; private set; }

            public AnsimationWriter(Stream output)
            {
                this.output = output;
                counted = new CountingStream(output);
                WriteAscii(counted, Esc + "[0m" + Esc + "[2J");
            }

            public void Add(AsciiArt art)
            {
                if (first != null && (art.Width != width || art.Height != height)) throw new InvalidOperationException("Every frame of an ANSImation has to be the same size.");
                if (first == null)
                {
                    first = art;
                    width = art.Width;
                    height = art.Height;
                }
                for (int y = 0; y < art.Height; y++)
                {
                    WriteAscii(counted, y == 0 ? AnsiHome : Esc + "[" + (y + 1).ToString(CultureInfo.InvariantCulture) + ";1H");
                    WriteAnsiRow(art, y, counted);
                }
                Frames++;
            }

            // the end, with its SAUCE record
            public void Finish(SauceInfo sauce = null)
            {
                if (first == null) throw new InvalidOperationException("An ANSImation needs at least one frame.");
                WriteAscii(counted, Esc + "[0m");
                output.WriteByte(Sauce.Eof);
                byte[] record = Sauce.Record(counted.Count, AnsiSauce(first, sauce));
                output.Write(record, 0, record.Length);
            }
        }

        // a stream that passes bytes on and counts them (a SAUCE record gives the file's size)
        sealed class CountingStream : Stream
        {
            readonly Stream inner;
            public long Count;
            public CountingStream(Stream inner) => this.inner = inner;
            public override void Write(byte[] buffer, int offset, int count)
            {
                inner.Write(buffer, offset, count);
                Count += count;
            }
            public override void WriteByte(byte value)
            {
                inner.WriteByte(value);
                Count++;
            }
            public override bool CanRead => false;
            public override bool CanSeek => false;
            public override bool CanWrite => true;
            public override long Length => throw new NotSupportedException();
            public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
            public override void Flush() => inner.Flush();
            public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
        }

        // ---- PETSCII: a C64 screen ----

        // The art laid out on a C64 screen: 1000 screen codes and 1000 colours (40×25, row by
        // row), centred left to right, spaces around it.
        public static void PetsciiScreen(AsciiArt art, out byte[] screen, out byte[] colour)
        {
            screen = Enumerable.Repeat((byte)0x20, C64Columns * C64Rows).ToArray();
            colour = Enumerable.Repeat((byte)1, C64Columns * C64Rows).ToArray();
            int pad = Math.Max(0, (int)Math.Floor((C64Columns - art.Width) / 2.0));
            for (int r = 0; r < Math.Min(C64Rows, art.Height); r++)
                for (int c = 0; c < Math.Min(C64Columns - pad, art.Width); c++)
                {
                    int i = r * art.Width + c;
                    screen[r * C64Columns + pad + c] = PetsciiScreenCodes.TryGetValue(art.Chars[i], out int code) ? (byte)code : (byte)0x20;
                    int c64Colour = art.C64Colour(i);
                    colour[r * C64Columns + pad + c] = (byte)((c64Colour == None ? 1 : c64Colour) & 0x0f);
                }
        }

        // The PETSCII art as a Commodore .seq stream: printed character by character (the C64's
        // CHROUT), so it holds PETSCII codes with colour codes, reverse on and off, and returns.
        public static byte[] PetsciiSeq(AsciiArt art)
        {
            PetsciiScreen(art, out byte[] screen, out byte[] colour);
            int rows = Math.Min(C64Rows, art.Height);
            var bytes = new List<byte> { 0x93, 0x8e };   // clear the screen; upper case and graphics
            int lastColour = -1;
            bool reverse = false;
            for (int r = 0; r < rows; r++)
            {
                // up to the last character that shows (plain spaces after it are left out)
                int end = C64Columns;
                while (end > 0 && screen[r * C64Columns + end - 1] == 0x20) end--;
                // printing a full screen's very last cell would scroll it up a line
                if (r == C64Rows - 1 && end == C64Columns) end--;
                for (int c = 0; c < end; c++)
                {
                    int code = screen[r * C64Columns + c];
                    bool rev = code >= 0x80;
                    if (code != 0x20 && colour[r * C64Columns + c] != lastColour)
                    {
                        lastColour = colour[r * C64Columns + c];
                        bytes.Add(lastColour < PetsciiColourCodes.Length ? PetsciiColourCodes[lastColour] : (byte)0x05);
                    }
                    if (rev != reverse)
                    {
                        bytes.Add(rev ? (byte)0x12 : (byte)0x92);
                        reverse = rev;
                    }
                    bytes.Add((byte)ScreenCodeToPetscii(code & 0x7f));
                }
                if (end < C64Columns && r < rows - 1)
                {
                    bytes.Add(0x0d);   // a return, which also turns reverse off
                    reverse = false;
                }
            }
            return bytes.ToArray();
        }

        // the PETSCII code that prints a (not reversed) screen code, upper case and graphics
        static int ScreenCodeToPetscii(int code)
        {
            if (code < 0x20) return code + 0x40;
            if (code < 0x40) return code;
            if (code < 0x60) return code + 0x80;
            return code + 0x40;
        }

        // The PETSCII art as a C64 program that shows it (LOAD"FILE",8 then RUN): a one-line
        // BASIC program (10 SYS 2061) starts a routine that copies the picture into screen memory
        // ($0400) and colour memory ($D800), sets a black border and background and the upper
        // case and graphics, waits for a key, then clears the screen and puts the usual colours back.
        public static byte[] PetsciiPrg(AsciiArt art)
        {
            PetsciiScreen(art, out byte[] screen, out byte[] colour);
            const int BasicStart = 0x0801;
            byte[] basic = { 0x0b, 0x08, 0x0a, 0x00, 0x9e, 0x32, 0x30, 0x36, 0x31, 0x00, 0x00, 0x00 };
            int codeAt = BasicStart + basic.Length;   // $080D = 2061
            List<byte> Routine(int data)
            {
                byte Lo(int v) => (byte)(v & 0xff);
                byte Hi(int v) => (byte)((v >> 8) & 0xff);
                var code = new List<byte>
                {
                    0xa9, 0x00,         // LDA #0       black
                    0x8d, 0x20, 0xd0,   // STA $D020    border
                    0x8d, 0x21, 0xd0,   // STA $D021    background
                    0xa9, 0x15,         // LDA #$15     screen at $0400, upper case and graphics
                    0x8d, 0x18, 0xd0,   // STA $D018
                    0xa2, 0x00,         // LDX #0
                };
                int loop = codeAt + code.Count;
                // four quarters of 250 bytes each, for the screen and the colours
                foreach (var (from, to) in new[] { (data, 0x0400), (data + 1000, 0xd800) })
                    for (int q = 0; q < 4; q++)
                    {
                        code.AddRange(new[] { (byte)0xbd, Lo(from + q * 250), Hi(from + q * 250) });   // LDA from,X
                        code.AddRange(new[] { (byte)0x9d, Lo(to + q * 250), Hi(to + q * 250) });       // STA to,X
                    }
                code.Add(0xe8);                                                             // INX
                code.AddRange(new byte[] { 0xe0, 250 });                                    // CPX #250
                code.AddRange(new[] { (byte)0xd0, (byte)((loop - (codeAt + code.Count + 2)) & 0xff) });  // BNE loop
                int wait = codeAt + code.Count;
                code.AddRange(new byte[] { 0x20, 0xe4, 0xff });                             // JSR GETIN    a key?
                code.AddRange(new byte[] { 0xc9, 0x00 });                                   // CMP #0
                code.AddRange(new[] { (byte)0xf0, (byte)((wait - (codeAt + code.Count + 2)) & 0xff) });  // BEQ wait
                code.AddRange(new byte[] { 0xa9, 0x93, 0x20, 0xd2, 0xff });                 // LDA #CLR, JSR CHROUT
                code.AddRange(new byte[] { 0xa9, 0x0e, 0x8d, 0x20, 0xd0 });                 // light blue border
                code.AddRange(new byte[] { 0xa9, 0x06, 0x8d, 0x21, 0xd0 });                 // blue background
                code.Add(0x60);                                                             // RTS          back to BASIC
                return code;
            }
            int length = Routine(0).Count;
            List<byte> routine = Routine(codeAt + length);
            var prg = new byte[2 + basic.Length + routine.Count + 2000];
            prg[0] = BasicStart & 0xff;
            prg[1] = BasicStart >> 8;
            Buffer.BlockCopy(basic, 0, prg, 2, basic.Length);
            routine.CopyTo(prg, 2 + basic.Length);
            Buffer.BlockCopy(screen, 0, prg, 2 + basic.Length + routine.Count, 1000);
            Buffer.BlockCopy(colour, 0, prg, 2 + basic.Length + routine.Count + 1000, 1000);
            return prg;
        }

        // The Teletext art as plain Unicode text, a line a row (saved as .txt: it isn't a
        // broadcast teletext file).
        public static string TeletextPage(AsciiArt art) => PlainText(art);
    }
}
