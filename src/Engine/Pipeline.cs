using System;
using System.Collections.Generic;

namespace B0r3dDeRes.Engine
{
    // What transparent parts of the photo sit on.
    static class Backgrounds
    {
        public const string White = "white", Black = "black";
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

        public DeresSettings Clone()
        {
            var copy = (DeresSettings)MemberwiseClone();
            copy.Adjust = Adjust.Clone();
            return copy;
        }
    }

    // A finished picture at the machine's resolution.
    sealed class Rendered
    {
        public int Width, Height;
        // one palette entry a pixel
        public byte[] Indices;
        // the machine's colours, r, g, b an entry
        public byte[] Palette;
        // the picture, r, g, b a pixel
        public byte[] Rgb;
        // how many of the palette's colours the picture uses
        public int Used;
        public Geometry Geometry;
        public Profile Profile;
        // the palette's lightest colour (LCD screens show it between pixels, in a later version)
        public byte[] Paper;
    }

    // The DeRes pipeline: the photo -> crop and area-average to the machine's resolution ->
    // pre-filters -> dither to its palette. It keeps the last resize, so moving a slider only
    // redoes the cheap steps on a few thousand pixels. (The website's core/pipeline.ts, for the
    // machines with fixed palettes; the rest come with the next version.) Not for two threads at
    // once: the program renders on one thread at a time.
    sealed class Deres
    {
        PictureSource source;
        string resizedKey;
        float[] resized;
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
            resized = null;
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
            // the machine's size), then lay it on the frame.
            float[] part = Resize(src, g, s.Background);
            g.Coverage(src.Width, src.Height, src.Width, src.Height, out double coverX, out double coverY);
            double amount = Engine.Detail.Amount(s.Detail, coverX, coverY);
            float[] shown = amount > 0 ? Engine.Detail.Keep(part, dst.W, dst.H, amount) : part;
            var rgb = new float[width * height * 3];
            bool bars = dst.W != width || dst.H != height;
            for (int y = 0; y < dst.H; y++) Array.Copy(shown, y * dst.W * 3, rgb, ((dst.Y + y) * width + dst.X) * 3, dst.W * 3);

            s.Adjust.Apply(rgb);
            if (profile.Mono) Adjust.ToGrey(rgb);
            // "Fit" bars go in pure black (the adjustments above would have tinted them)
            if (bars) FillBars(rgb, width, height, dst);

            PaletteState pal = Palette(profile);
            var indices = new byte[width * height];
            // a dither this version doesn't have (a machine's own, from a later one) is the machine's
            string ditherId = Dither.ById(s.Dither) != null ? s.Dither : profile.Dither;
            Dither.Run(ditherId, new Dither.Input
            {
                Rgb = rgb,
                Width = width,
                Height = height,
                Matcher = pal.Matcher,
                Mono = profile.Mono,
                Planner = () => pal.Planner ?? (pal.Planner = new YliluomaPlanner(pal.Matcher)),
            }, indices);
            if (bars)
                for (int y = 0; y < height; y++)
                    for (int x = 0; x < width; x++)
                        if (y < dst.Y || y >= dst.Y + dst.H || x < dst.X || x >= dst.X + dst.W) indices[y * width + x] = (byte)pal.Darkest;

            var output = new byte[width * height * 3];
            var seen = new bool[256];
            int used = 0;
            for (int i = 0; i < indices.Length; i++)
            {
                int k = indices[i];
                output[i * 3] = pal.Display[k * 3];
                output[i * 3 + 1] = pal.Display[k * 3 + 1];
                output[i * 3 + 2] = pal.Display[k * 3 + 2];
                if (!seen[k])
                {
                    seen[k] = true;
                    used++;
                }
            }
            int l = pal.Lightest * 3;
            return new Rendered
            {
                Width = width,
                Height = height,
                Indices = indices,
                Palette = pal.Display,
                Rgb = output,
                Used = used,
                Geometry = g,
                Profile = profile,
                Paper = new[] { pal.Display[l], pal.Display[l + 1], pal.Display[l + 2] },
            };
        }

        static void FillBars(float[] rgb, int width, int height, RectI dst)
        {
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    if (y < dst.Y || y >= dst.Y + dst.H || x < dst.X || x >= dst.X + dst.W) Array.Clear(rgb, (y * width + x) * 3, 3);
        }

        // The cropped part at the frame's size, linear light, 3 floats a pixel, with the
        // transparent parts laid on white or black.
        float[] Resize(PictureSource src, Geometry g, string background)
        {
            RectD r = g.Src;
            RectI dst = g.Dst;
            string key = string.Join("|", r.X.ToString("R"), r.Y.ToString("R"), r.W.ToString("R"), r.H.ToString("R"), dst.W, dst.H, background);
            if (resizedKey == key) return resized;
            float[] premul = Resample.Area(src, r, dst.W, dst.H);
            int n = dst.W * dst.H;
            var rgb = new float[n * 3];
            double bg = background == Backgrounds.Black ? 0 : 1;
            for (int i = 0; i < n; i++)
            {
                // (1.0, not 1: one minus a float would round to a float, where the website keeps the double)
                double under = bg * (1.0 - premul[i * 4 + 3]);
                rgb[i * 3] = (float)(premul[i * 4] + under);
                rgb[i * 3 + 1] = (float)(premul[i * 4 + 1] + under);
                rgb[i * 3 + 2] = (float)(premul[i * 4 + 2] + under);
            }
            resizedKey = key;
            resized = rgb;
            return rgb;
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
