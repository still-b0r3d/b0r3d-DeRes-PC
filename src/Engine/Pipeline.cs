using System;
using System.Collections.Generic;
using System.Linq;

namespace B0r3dDeRes.Engine
{
    // What transparent parts of the photo sit on, or "transparent": they stay see-through.
    static class Backgrounds
    {
        public const string White = "white", Black = "black", Transparent = "transparent";

        // the coverage from which a machine pixel shows, when they stay see-through
        public const double AlphaThreshold = 0.5;
    }

    // Everything that makes the picture, by the website's names and values.
    sealed class DeresSettings
    {
        public string ProfileId = Profiles.DefaultId;
        public string Fit = Fits.Fill;
        public bool Custom;             // resolution: the machine's own, or a custom width
        public int? CustomWidth;        // missing: the machine's own width
        public double Detail = Engine.Detail.Default;   // 0-1
        public string Background = Backgrounds.White;
        public Adjust Adjust = new Adjust();
        public string Dither = DitherIds.Bayer4;
        public int? CustomCount;        // the Colours slider (missing: the machine's own count)
        public string[] CustomPalette;  // Custom's palette, loaded from a file or grabbed from a picture

        public DeresSettings Clone()
        {
            var copy = (DeresSettings)MemberwiseClone();
            copy.Adjust = Adjust.Clone();
            copy.CustomPalette = (string[])CustomPalette?.Clone();
            return copy;
        }
    }

    // A machine's own file, made with the picture (.scr, .koa or .chr).
    sealed class EmulatorFile
    {
        public string Format;       // scr, koa or chr
        public byte[] Data;
        public string DefaultName;  // "zx-spectrum.scr"
    }

    // A finished picture at the machine's resolution.
    sealed class Rendered
    {
        public int Width, Height;
        // one palette entry a pixel (for the looks that make their own colours, something else: see Indexed)
        public byte[] Indices;
        // the machine's colours, r, g, b an entry
        public byte[] Palette;
        // Whether each pixel is the palette entry its index says. When it isn't (HAM, the N64,
        // the PlayStation, the analog looks), the palette is only the first colours seen and
        // exports go by Rgb.
        public bool Indexed;
        // the picture, r, g, b a pixel
        public byte[] Rgb;
        // how many of the palette's colours the picture uses (of its own colours, where it makes them)
        public int Used;
        public Geometry Geometry;
        public Profile Profile;
        // the palette's lightest colour, or the look's paper (an LCD screen shows it between pixels)
        public byte[] Paper;
        // which pixels show (255) and which are see-through (0) when they're kept; null when all show
        public byte[] Alpha;
        public HardwareFitReport TileFit;   // null for machines without limits
        public EmulatorFile EmulatorFile;   // the machine's own file, when it has one
    }

    // The DeRes pipeline: the photo -> crop and area-average to the machine's resolution ->
    // pre-filters -> its colours (a dither to its palette, or its own rules). It keeps the last
    // resize, so moving a slider only redoes the cheap steps on a few thousand pixels. (The
    // website's core/pipeline.ts.)
    // Not for two threads at once: the program renders on one thread at a time.
    //
    // Transparent parts of the photo sit on white or black, or stay see-through. Then a machine
    // pixel shows when at least half of it was covered, and the rest are hidden: the machines had
    // no half-transparent pixels, and indexed PNG can't hold them either. Under the hidden pixels
    // the picture's own edge colours carry on outward, so the dither and the per-cell colours see
    // more of the picture there, never a fill colour: the edge gets no light or dark fringe. The
    // mask (Rendered.Alpha) then cuts the result.
    sealed class Deres
    {
        PictureSource source;
        string resizedKey;
        float[] resizedRgb;
        byte[] resizedAlpha;
        readonly Dictionary<string, PaletteState> palettes = new Dictionary<string, PaletteState>();

        sealed class PaletteState
        {
            public byte[] Display;          // the colours shown (grey-level machines: darkest first)
            public PaletteMatcher Matcher;  // matched against; for grey levels, even grey steps in the same order
            public YliluomaPlanner Planner;
            public int Darkest, Lightest;
        }

        public PictureSource Source => source;

        public void SetSource(PictureSource picture)
        {
            source = picture;
            resizedKey = null;
            resizedRgb = null;
            resizedAlpha = null;
        }

        public Rendered Render(DeresSettings s)
        {
            PictureSource src = source ?? throw new InvalidOperationException("No picture loaded");
            Profile profile = Profiles.ById(s.ProfileId) ?? Profiles.Default;
            // the engine works from the whole photo, so it is its own "working copy"
            Geometry g = Geometry.Plan(src.Width, src.Height, src.Width, src.Height, profile, s.Custom, s.CustomWidth, s.Fit);
            int width = g.Width, height = g.Height;
            RectI dst = g.Dst;

            // Resize (kept until the framing changes), sharpen back what it blurred (cheap, at
            // the machine's size), then lay it on the frame. A picture with nothing see-through
            // comes out as it always did, black bars included.
            string background = s.Background == Backgrounds.Transparent && !src.HasAlpha ? Backgrounds.White : s.Background;
            Resize(src, g, background, out float[] part, out byte[] partAlpha);
            g.Coverage(src.Width, src.Height, src.Width, src.Height, out double coverX, out double coverY);
            double amount = Engine.Detail.Amount(s.Detail, coverX, coverY);
            float[] shown = amount > 0 ? Engine.Detail.Keep(part, dst.W, dst.H, amount) : part;
            var rgb = new float[width * height * 3];
            bool bars = dst.W != width || dst.H != height;
            for (int y = 0; y < dst.H; y++) Array.Copy(shown, y * dst.W * 3, rgb, ((dst.Y + y) * width + dst.X) * 3, dst.W * 3);

            // Kept transparency: the "fit" bars are see-through too, and the picture's edge carries
            // on into them like everywhere else.
            bool keep = background == Backgrounds.Transparent;
            byte[] alpha = null;
            if (keep && (bars || partAlpha != null))
            {
                alpha = new byte[width * height];
                for (int y = 0; y < dst.H; y++)
                {
                    int o = (dst.Y + y) * width + dst.X;
                    if (partAlpha != null) Array.Copy(partAlpha, y * dst.W, alpha, o, dst.W);
                    else for (int x = 0; x < dst.W; x++) alpha[o + x] = 255;
                }
                if (bars) ExtendIntoBars(rgb, width, height, dst);
            }
            bool blackBars = bars && !keep;

            s.Adjust.Apply(rgb);
            if (profile.Mono)
            {
                Adjust.ToGrey(rgb);
                if (profile.EdgeBoost > 0) Adjust.EdgeBoost(rgb, width, height, profile.EdgeBoost);
            }
            // "Fit" bars go in pure black (the adjustments above would have tinted them), so a
            // machine's own file (.scr, .koa, .chr) holds them as shown.
            if (blackBars) FillBars(rgb, width, height, dst);

            var r = new Rendered { Width = width, Height = height, Geometry = g, Profile = profile, Alpha = alpha };
            switch (profile.Kind)
            {
                case PaletteKind.Direct:
                    Ps1(r, rgb, s, blackBars);
                    break;
                case PaletteKind.Cell:
                    CellMachine(r, rgb);
                    break;
                case PaletteKind.Ham:
                    HamMachine(r, rgb, blackBars);
                    break;
                case PaletteKind.N64:
                    N64Machine(r, rgb, blackBars);
                    break;
                case PaletteKind.Analog:
                    AnalogLook(r, rgb, blackBars);
                    break;
                default:
                    PaletteMachine(r, rgb, s, blackBars);
                    break;
            }
            return r;
        }

        static bool Outside(RectI dst, int x, int y) => y < dst.Y || y >= dst.Y + dst.H || x < dst.X || x >= dst.X + dst.W;

        static HardwareFitReport FitOf(Profile p, byte[] indices, int width, int height, byte[] alpha) =>
            p.HasLimits ? Tiles.Fit(indices, width, height, p.MaxTiles, p.MaxColours, alpha) : null;

        static byte[] Bytes(float[] linear)
        {
            var bytes = new byte[linear.Length];
            for (int i = 0; i < linear.Length; i++) bytes[i] = (byte)Colour.LinearToByte(linear[i]);
            return bytes;
        }

        // The PlayStation: 15-bit colour straight, with its GPU's 4×4 dither table (or none).
        void Ps1(Rendered r, float[] rgb, DeresSettings s, bool blackBars)
        {
            int width = r.Width, height = r.Height;
            RectI dst = r.Geometry.Dst;
            byte[] alpha = r.Alpha;
            var output = new byte[width * height * 3];
            var indices = new byte[width * height];
            var seen = new Dictionary<int, int>();
            var palette = new List<byte>();
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int i = y * width + x, cr = 0, cg = 0, cb = 0;
                    if (!(blackBars && Outside(dst, x, y)))
                    {
                        int d = s.Dither == DitherIds.Ps1 ? Dither.Ps1Table[(y & 3) * 4 + (x & 3)] : 0;
                        cr = Quantize.SnapToBits(Math.Min(255, Math.Max(0, JsMath.Round(Colour.ToSrgb(rgb[i * 3]) * 255 + d))), 5);
                        cg = Quantize.SnapToBits(Math.Min(255, Math.Max(0, JsMath.Round(Colour.ToSrgb(rgb[i * 3 + 1]) * 255 + d))), 5);
                        cb = Quantize.SnapToBits(Math.Min(255, Math.Max(0, JsMath.Round(Colour.ToSrgb(rgb[i * 3 + 2]) * 255 + d))), 5);
                    }
                    output[i * 3] = (byte)cr;
                    output[i * 3 + 1] = (byte)cg;
                    output[i * 3 + 2] = (byte)cb;
                    // a hidden pixel's colour doesn't count as used
                    if (alpha != null && alpha[i] == 0) continue;
                    int key = (cr << 16) | (cg << 8) | cb;
                    if (!seen.TryGetValue(key, out int idx))
                    {
                        idx = seen.Count;
                        seen[key] = idx;
                        if (palette.Count < 256 * 3) palette.AddRange(new[] { (byte)cr, (byte)cg, (byte)cb });
                    }
                    indices[i] = (byte)(idx < 256 ? idx : 0);
                }
            }
            r.Indices = indices;
            r.Palette = palette.Count > 0 ? palette.ToArray() : new byte[3];
            r.Indexed = false;
            r.Rgb = output;
            r.Used = seen.Count;
            r.Paper = new byte[] { 255, 255, 255 };
            r.TileFit = FitOf(r.Profile, indices, width, height, alpha);
        }

        // The machines that pick their colours cell by cell (no dither: their own rules).
        void CellMachine(Rendered r, float[] rgb)
        {
            Profile p = r.Profile;
            int width = r.Width, height = r.Height;
            byte[] alpha = r.Alpha;
            Cell.Result cell;
            byte[] file = null;
            string format = null;
            bool chr = p.EmulatorFormats.Contains("chr");
            switch (p.Mode)
            {
                case "spectrum":
                    cell = Cell.Spectrum(rgb, width, height);
                    file = cell.ScreenFile;
                    format = "scr";
                    break;
                case "c64-mc":
                    cell = Cell.C64Multicolour(rgb, width, height);
                    file = cell.ScreenFile;
                    format = "koa";
                    break;
                case "nes-attr":
                    cell = Cell.NesAttributes(rgb, width, height);
                    // NES tiles hold each pixel's number in its sub-palette, not the colour
                    if (chr)
                    {
                        file = Tiles.NesChr(HiddenAsZero(cell.ColourNumbers, alpha), width, height);
                        format = "chr";
                    }
                    break;
                case "msx-screen2":
                    cell = Cell.MsxScreen2(rgb, width, height);
                    break;
                case "gbc-tile":
                    cell = Cell.GbcTiles(rgb, width, height);
                    if (chr)
                    {
                        file = Tiles.GbChr(HiddenAsZero(cell.Indices, alpha), width, height);
                        format = "chr";
                    }
                    break;
                default:
                    cell = Cell.Apple2Hires(rgb, width, height);
                    break;
            }
            // No repainting of the bars here: they went in black (or carry on the edge, when
            // they're see-through), and the machine's file must show exactly what the preview shows.
            byte[] palette = cell.Palette ?? Profiles.PaletteBytes(p);
            r.Indices = cell.Indices;
            r.Palette = palette;
            r.Indexed = true;
            r.Rgb = Bytes(cell.Rgb);
            r.Used = UsedIndices(cell.Indices, alpha);
            r.Paper = FindLightest(palette);
            r.TileFit = FitOf(p, cell.Indices, width, height, alpha);
            if (file != null) r.EmulatorFile = new EmulatorFile { Format = format, Data = file, DefaultName = p.Id + "." + format };
        }

        void HamMachine(Rendered r, float[] rgb, bool blackBars)
        {
            int width = r.Width, height = r.Height;
            Ham.Result ham = Ham.Ham6(rgb, width, height);
            if (blackBars) ZeroBars(r.Geometry.Dst, width, height, ham.Indices, ham.Rgb);
            r.Indices = ham.Indices;
            r.Palette = ham.Palette;
            r.Indexed = false;
            r.Rgb = Bytes(ham.Rgb);
            r.Used = r.Alpha != null ? UsedColours(r.Rgb, r.Alpha) : ham.UsedColours;
            r.Paper = FindLightest(ham.Palette);
            r.TileFit = FitOf(r.Profile, ham.Indices, width, height, r.Alpha);
        }

        void N64Machine(Rendered r, float[] rgb, bool blackBars)
        {
            N64.Filter(rgb, r.Width, r.Height, out byte[] indices, out float[] filtered);
            OwnColours(r, indices, filtered, new byte[] { 255, 255, 255 }, blackBars);
        }

        // the early CCD camera, Deep Fry, the newspaper, the risograph and the fax
        void AnalogLook(Rendered r, float[] rgb, bool blackBars)
        {
            Analog.Result look = Analog.Process(r.Profile.Mode, rgb, r.Width, r.Height);
            OwnColours(r, look.Indices, look.Rgb, look.Paper, blackBars);
        }

        // A look that makes its own colours: the bars in black, and its first 256 colours (of the
        // pixels that show) as its palette.
        void OwnColours(Rendered r, byte[] indices, float[] linear, byte[] paper, bool blackBars)
        {
            int width = r.Width, height = r.Height;
            if (blackBars) ZeroBars(r.Geometry.Dst, width, height, indices, linear);
            byte[] output = Bytes(linear);
            var seen = new HashSet<int>();
            var palette = new List<byte>();
            for (int i = 0; i < width * height; i++)
            {
                if (r.Alpha != null && r.Alpha[i] == 0) continue;
                if (seen.Add((output[i * 3] << 16) | (output[i * 3 + 1] << 8) | output[i * 3 + 2]) && palette.Count < 256 * 3)
                    palette.AddRange(new[] { output[i * 3], output[i * 3 + 1], output[i * 3 + 2] });
            }
            r.Indices = indices;
            r.Palette = palette.Count > 0 ? palette.ToArray() : new byte[3];
            r.Indexed = false;
            r.Rgb = output;
            r.Used = seen.Count;
            r.Paper = paper;
            r.TileFit = FitOf(r.Profile, indices, width, height, r.Alpha);
        }

        static void ZeroBars(RectI dst, int width, int height, byte[] indices, float[] rgb)
        {
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    if (Outside(dst, x, y))
                    {
                        int i = y * width + x;
                        indices[i] = 0;
                        rgb[i * 3] = rgb[i * 3 + 1] = rgb[i * 3 + 2] = 0;
                    }
        }

        // The machines with a palette: their own list, or colours picked from the picture; then the dither.
        void PaletteMachine(Rendered r, float[] rgb, DeresSettings s, bool blackBars)
        {
            Profile p = r.Profile;
            int width = r.Width, height = r.Height;
            byte[] alpha = r.Alpha;
            RectI dst = r.Geometry.Dst;
            PaletteState pal;
            if (p.Kind == PaletteKind.Fixed) pal = Palette(p);
            else
            {
                // the colours are picked from the pixels that show
                float[] picked = alpha != null ? ShownPixels(rgb, alpha) : rgb;
                byte[] display;
                // a grabbed or loaded palette belongs to the Custom machine only
                if (p.Kind == PaletteKind.Custom && s.CustomPalette != null && s.CustomPalette.Length > 0)
                    display = s.CustomPalette.SelectMany(Colour.HexToRgb).ToArray();
                else if (p.Kind == PaletteKind.Pool)
                {
                    display = Quantize.FromPool(picked, p.Pool, Profiles.ColourCount(p, s.CustomCount).Value);
                    if (display.Length == 0) display = Profiles.PaletteBytes(p);
                }
                else
                {
                    int count = Profiles.ColourCount(p, s.CustomCount) ?? 16;
                    display = Quantize.Rgb(picked, count, p.Kind == PaletteKind.Range ? p.BitsPerChannel : 8);
                    if (display.Length == 0) display = Profiles.PaletteBytes(p);
                }
                pal = CreatePaletteState(display, p.Mono);
            }

            // a machine's own dither only applies to that machine
            string ditherId = (s.Dither == DitherIds.GameBoyCamera && p.NativeDither != DitherIds.GameBoyCamera) || (s.Dither == DitherIds.Ps1 && p.NativeDither != DitherIds.Ps1)
                ? p.Dither : s.Dither;
            var indices = new byte[width * height];
            Dither.Run(ditherId, new Dither.Input
            {
                Rgb = rgb,
                Width = width,
                Height = height,
                Matcher = pal.Matcher,
                Mono = p.Mono,
                Planner = () => pal.Planner ?? (pal.Planner = new YliluomaPlanner(pal.Matcher)),
            }, indices);
            if (blackBars)
                for (int y = 0; y < height; y++)
                    for (int x = 0; x < width; x++)
                        if (Outside(dst, x, y)) indices[y * width + x] = (byte)pal.Darkest;

            var output = new byte[width * height * 3];
            for (int i = 0; i < indices.Length; i++)
            {
                int k = indices[i];
                output[i * 3] = pal.Display[k * 3];
                output[i * 3 + 1] = pal.Display[k * 3 + 1];
                output[i * 3 + 2] = pal.Display[k * 3 + 2];
            }
            r.Indices = indices;
            r.Palette = pal.Display;
            r.Indexed = true;
            r.Rgb = output;
            r.Used = UsedIndices(indices, alpha);
            int l = pal.Lightest * 3;
            r.Paper = new[] { pal.Display[l], pal.Display[l + 1], pal.Display[l + 2] };
            r.TileFit = FitOf(p, indices, width, height, alpha);
            if (p.EmulatorFormats.Contains("chr"))
            {
                // Grey-level indices count darkest first; a Game Boy's colour 0 is the lightest
                // (with the usual palette register), so they're turned round.
                int n = pal.Display.Length / 3;
                byte[] numbers = p.Mono ? indices.Select(v => (byte)(n - 1 - v)).ToArray() : indices;
                r.EmulatorFile = new EmulatorFile { Format = "chr", Data = Tiles.GbChr(HiddenAsZero(numbers, alpha), width, height), DefaultName = p.Id + ".chr" };
            }
        }

        static void FillBars(float[] rgb, int width, int height, RectI dst)
        {
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    if (y < dst.Y || y >= dst.Y + dst.H || x < dst.X || x >= dst.X + dst.W) Array.Clear(rgb, (y * width + x) * 3, 3);
        }

        // each pixel of the "fit" bars takes the colour of the picture's nearest pixel
        static void ExtendIntoBars(float[] rgb, int width, int height, RectI dst)
        {
            for (int y = 0; y < height; y++)
            {
                int sy = Math.Min(dst.Y + dst.H - 1, Math.Max(dst.Y, y));
                for (int x = 0; x < width; x++)
                {
                    if (y >= dst.Y && y < dst.Y + dst.H && x >= dst.X && x < dst.X + dst.W) continue;
                    int s = (sy * width + Math.Min(dst.X + dst.W - 1, Math.Max(dst.X, x))) * 3;
                    Array.Copy(rgb, s, rgb, (y * width + x) * 3, 3);
                }
            }
        }

        // Gives every hidden pixel the colour of the nearest shown one (a flood outward from the
        // edge, a step at a time), so the picture carries on under them. With nothing shown they
        // stay black.
        static void SpreadEdges(float[] rgb, byte[] alpha, int w, int h)
        {
            int n = w * h, tail = 0;
            var queue = new int[n];
            var reached = new bool[n];
            for (int i = 0; i < n; i++)
                if (alpha[i] != 0)
                {
                    reached[i] = true;
                    queue[tail++] = i;
                }
            for (int head = 0; head < tail; head++)
            {
                int i = queue[head], x = i % w;
                for (int k = 0; k < 4; k++)
                {
                    int j = k == 0 ? (x > 0 ? i - 1 : -1) : k == 1 ? (x < w - 1 ? i + 1 : -1) : k == 2 ? i - w : i + w;
                    if (j < 0 || j >= n || reached[j]) continue;
                    reached[j] = true;
                    rgb[j * 3] = rgb[i * 3];
                    rgb[j * 3 + 1] = rgb[i * 3 + 1];
                    rgb[j * 3 + 2] = rgb[i * 3 + 2];
                    queue[tail++] = j;
                }
            }
        }

        // only the pixels that show, 3 floats each
        static float[] ShownPixels(float[] rgb, byte[] alpha)
        {
            int n = 0;
            for (int i = 0; i < alpha.Length; i++) if (alpha[i] != 0) n++;
            var output = new float[n * 3];
            for (int i = 0, o = 0; i < alpha.Length; i++)
            {
                if (alpha[i] == 0) continue;
                Array.Copy(rgb, i * 3, output, o, 3);
                o += 3;
            }
            return output;
        }

        // how many different entries the pixels that show use
        static int UsedIndices(byte[] indices, byte[] alpha)
        {
            var seen = new bool[256];
            int used = 0;
            for (int i = 0; i < indices.Length; i++)
            {
                if ((alpha != null && alpha[i] == 0) || seen[indices[i]]) continue;
                seen[indices[i]] = true;
                used++;
            }
            return used;
        }

        // how many different colours the pixels that show have
        static int UsedColours(byte[] rgb, byte[] alpha)
        {
            var seen = new HashSet<int>();
            for (int i = 0; i < alpha.Length; i++) if (alpha[i] != 0) seen.Add((rgb[i * 3] << 16) | (rgb[i * 3 + 1] << 8) | rgb[i * 3 + 2]);
            return seen.Count;
        }

        // tile colour numbers with the hidden pixels as colour 0, which sprites show as see-through
        static byte[] HiddenAsZero(byte[] numbers, byte[] alpha)
        {
            if (alpha == null) return numbers;
            var output = new byte[numbers.Length];
            for (int i = 0; i < numbers.Length; i++) output[i] = alpha[i] != 0 ? numbers[i] : (byte)0;
            return output;
        }

        static byte[] FindLightest(byte[] display)
        {
            if (display.Length < 3) return new byte[] { 255, 255, 255 };
            int best = 0;
            double maxLum = -1;
            for (int i = 0; i < display.Length / 3; i++)
            {
                double y = Lum(display, i);
                if (y > maxLum)
                {
                    maxLum = y;
                    best = i;
                }
            }
            return new[] { display[best * 3], display[best * 3 + 1], display[best * 3 + 2] };
        }

        // The cropped part at the frame's size, linear light, 3 floats a pixel. On white or black,
        // the transparent parts are laid on it. Kept see-through, `alpha` says which pixels show
        // (null: all of them), each shown pixel has its own colour (not darkened by the part of it
        // that was see-through), and the hidden ones carry on the colours of the nearest shown ones.
        void Resize(PictureSource src, Geometry g, string background, out float[] rgb, out byte[] alpha)
        {
            RectD r = g.Src;
            RectI dst = g.Dst;
            string key = string.Join("|", r.X.ToString("R"), r.Y.ToString("R"), r.W.ToString("R"), r.H.ToString("R"), dst.W, dst.H, background);
            if (resizedKey == key)
            {
                rgb = resizedRgb;
                alpha = resizedAlpha;
                return;
            }
            float[] premul = Resample.Area(src, r, dst.W, dst.H);
            int n = dst.W * dst.H;
            rgb = new float[n * 3];
            alpha = null;
            if (background == Backgrounds.Transparent)
            {
                alpha = new byte[n];
                int hidden = 0;
                for (int i = 0; i < n; i++)
                {
                    double a = premul[i * 4 + 3];
                    if (a >= Backgrounds.AlphaThreshold)
                    {
                        alpha[i] = 255;
                        rgb[i * 3] = (float)(premul[i * 4] / a);
                        rgb[i * 3 + 1] = (float)(premul[i * 4 + 1] / a);
                        rgb[i * 3 + 2] = (float)(premul[i * 4 + 2] / a);
                    }
                    else hidden++;
                }
                if (hidden == 0) alpha = null;
                else SpreadEdges(rgb, alpha, dst.W, dst.H);
            }
            else
            {
                double bg = background == Backgrounds.Black ? 0 : 1;
                for (int i = 0; i < n; i++)
                {
                    // (1.0, not 1: one minus a float would round to a float, where the website keeps the double)
                    double under = bg * (1.0 - premul[i * 4 + 3]);
                    rgb[i * 3] = (float)(premul[i * 4] + under);
                    rgb[i * 3 + 1] = (float)(premul[i * 4 + 1] + under);
                    rgb[i * 3 + 2] = (float)(premul[i * 4 + 2] + under);
                }
            }
            resizedKey = key;
            resizedRgb = rgb;
            resizedAlpha = alpha;
        }

        PaletteState Palette(Profile profile)
        {
            if (palettes.TryGetValue(profile.Id, out PaletteState hit)) return hit;
            PaletteState state = CreatePaletteState(Profiles.PaletteBytes(profile), profile.Mono);
            palettes[profile.Id] = state;
            return state;
        }

        static PaletteState CreatePaletteState(byte[] display, bool mono)
        {
            int n = display.Length / 3;
            byte[] match = display;
            if (mono)
            {
                // even grey steps: the machine's levels 0 … n-1, whatever their tint
                match = new byte[n * 3];
                for (int i = 0; i < n; i++)
                {
                    byte v = (byte)(n == 1 ? 255 : JsMath.Round(255.0 * i / (n - 1)));
                    match[i * 3] = match[i * 3 + 1] = match[i * 3 + 2] = v;
                }
            }
            var state = new PaletteState { Display = display, Matcher = new PaletteMatcher(match) };
            for (int i = 0; i < n; i++)
            {
                double y = Lum(display, i);
                if (y < Lum(display, state.Darkest)) state.Darkest = i;
                if (y > Lum(display, state.Lightest)) state.Lightest = i;
            }
            return state;
        }

        static double Lum(byte[] display, int i) => Profiles.Lum(new[] { display[i * 3], display[i * 3 + 1], display[i * 3 + 2] });
    }
}
