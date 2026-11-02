using System;
using System.Collections.Generic;
using System.Linq;

namespace B0r3dDeRes.Engine
{
    // The dithers, by the website's ids ("floyd", "bayer4", …), which go into settings and files.
    static class DitherIds
    {
        public const string None = "none", Bayer2 = "bayer2", Bayer4 = "bayer4", Bayer8 = "bayer8", Floyd = "floyd",
            Atkinson = "atkinson", Yliluoma = "yliluoma", GameBoyCamera = "gbcam", Ps1 = "ps1";
    }

    sealed class DitherInfo
    {
        public string Id, Name, Hint;
        // only offered on machines that have it as their own
        public bool Native;
    }

    // Turns a linear-light picture into palette indices. Every dither here gives the same result
    // for the same picture, every time. (The website's core/dither.ts and core/bayer.ts.)
    static class Dither
    {
        // The website's list, in its order. The Game Boy Camera's and the PlayStation's own come
        // with those machines.
        public static readonly DitherInfo[] All =
        {
            new DitherInfo { Id = DitherIds.None, Name = "None", Hint = "Flat areas of colour" },
            new DitherInfo { Id = DitherIds.Bayer2, Name = "Bayer 2×2", Hint = "Coarse crosshatch" },
            new DitherInfo { Id = DitherIds.Bayer4, Name = "Bayer 4×4", Hint = "Classic crosshatch" },
            new DitherInfo { Id = DitherIds.Bayer8, Name = "Bayer 8×8", Hint = "Fine crosshatch" },
            new DitherInfo { Id = DitherIds.Floyd, Name = "Floyd–Steinberg", Hint = "Smooth, grainy" },
            new DitherInfo { Id = DitherIds.Atkinson, Name = "Atkinson", Hint = "Crisp, Mac-style" },
            new DitherInfo { Id = DitherIds.Yliluoma, Name = "Yliluoma", Hint = "Patterned, for uneven palettes" },
        };

        public static DitherInfo ById(string id) => All.FirstOrDefault(d => d.Id == id);

        public sealed class Input
        {
            // linear-light RGB, 3 floats a pixel; not changed
            public float[] Rgb;
            public int Width, Height;
            public PaletteMatcher Matcher;
            // Grey-level palette (Game Boy, Mac, receipt printer): the pixels are grey and the
            // matcher's colours are grey levels, so ordered dithers pick between the two levels
            // around each pixel exactly.
            public bool Mono;
            // made on first use and kept by the caller, one a palette
            public Func<YliluomaPlanner> Planner;
            // where the patterns start, for the moving loops of later stages
            public int OffsetX, OffsetY;
        }

        public static void Run(string id, Input input, byte[] output)
        {
            switch (id)
            {
                case DitherIds.Bayer2: Ordered(input, 2, output); break;
                case DitherIds.Bayer4: Ordered(input, 4, output); break;
                case DitherIds.Bayer8: Ordered(input, 8, output); break;
                case DitherIds.Floyd: FloydSteinberg(input, output); break;
                case DitherIds.Atkinson: Atkinson(input, output); break;
                case DitherIds.Yliluoma: Yliluoma(input, output); break;
                default: NearestOnly(input, output); break;
            }
        }

        static void NearestOnly(Input input, byte[] output)
        {
            float[] rgb = input.Rgb;
            for (int i = 0, n = input.Width * input.Height; i < n; i++)
                output[i] = (byte)input.Matcher.Nearest(rgb[i * 3], rgb[i * 3 + 1], rgb[i * 3 + 2]);
        }

        // ---- Bayer matrices: each holds 0 … n²-1 once, every threshold as far as can be from the
        // ones just below it ----

        static readonly Dictionary<int, byte[]> bayer = new Dictionary<int, byte[]>();

        // the n×n Bayer matrix (n = 1, 2, 4, 8 or 16), row by row
        public static byte[] BayerMatrix(int n)
        {
            lock (bayer)
            {
                if (bayer.TryGetValue(n, out byte[] hit)) return hit;
                byte[] m;
                if (n == 1) m = new byte[] { 0 };
                else
                {
                    if (n < 1 || n > 16 || (n & (n - 1)) != 0) throw new ArgumentException("No Bayer matrix of size " + n);
                    int half = n / 2;
                    byte[] b = BayerMatrix(half);
                    m = new byte[n * n];
                    // quadrant offsets of the 2×2 matrix [0 2; 3 1]
                    int[] offset = { 0, 2, 3, 1 };
                    for (int y = 0; y < n; y++)
                        for (int x = 0; x < n; x++)
                        {
                            int q = (y >= half ? 2 : 0) + (x >= half ? 1 : 0);
                            m[y * n + x] = (byte)(4 * b[(y % half) * half + (x % half)] + offset[q]);
                        }
                }
                bayer[n] = m;
                return m;
            }
        }

        static int Wrap(int v, int n) => (v % n + n) % n;

        // a channel as a byte, rounded (the website's (v + 0.5) | 0, clamped)
        static int ToByte(double v) => v <= 0 ? 0 : v >= 255 ? 255 : (int)(v + 0.5);

        // Classic ordered dithering for colour palettes: add the threshold (scaled to the
        // palette's typical step) and take the nearest colour. Done in sRGB, where most hardware
        // palettes are evenly spaced (the EGA's 0x55 steps, the web-safe cube's 0x33), so every
        // step gets the full pattern. Uneven palettes do better with Yliluoma.
        static void Ordered(Input input, int n, byte[] output)
        {
            if (input.Mono)
            {
                OrderedLevels(input, n, false, output);
                return;
            }
            float[] rgb = input.Rgb;
            byte[] mat = BayerMatrix(n);
            int nn = n * n;
            double spread = input.Matcher.Spread * 255;
            for (int y = 0; y < input.Height; y++)
            {
                int my = Wrap(y + input.OffsetY, n);
                for (int x = 0; x < input.Width; x++)
                {
                    int mx = Wrap(x + input.OffsetX, n);
                    int i = y * input.Width + x;
                    double d = ((mat[my * n + mx] + 0.5) / nn - 0.5) * spread;
                    output[i] = (byte)input.Matcher.NearestByte(
                        ToByte(Colour.ToSrgb(rgb[i * 3]) * 255 + d),
                        ToByte(Colour.ToSrgb(rgb[i * 3 + 1]) * 255 + d),
                        ToByte(Colour.ToSrgb(rgb[i * 3 + 2]) * 255 + d));
                }
            }
        }

        // Ordered dithering over grey levels: find the two levels around the pixel and show the
        // brighter one on the share of thresholds that matches where the pixel sits between them.
        // In linear light (the right brightness) or, for the Game Boy Camera, in sRGB like its
        // sensor's evenly spaced thresholds.
        static void OrderedLevels(Input input, int n, bool perceptual, byte[] output)
        {
            PaletteMatcher matcher = input.Matcher;
            float[] rgb = input.Rgb;
            byte[] mat = BayerMatrix(n);
            int nn = n * n;
            int[] order = Enumerable.Range(0, matcher.Size).ToArray();
            Array.Sort(order, (a, b) =>
            {
                int c = ((double)matcher.Luma[a]).CompareTo(matcher.Luma[b]);
                return c != 0 ? c : a.CompareTo(b);
            });
            Func<double, double> curve = perceptual ? (Func<double, double>)Colour.ToSrgb : v => v;
            double[] levels = order.Select(i => curve(matcher.Luma[i])).ToArray();
            int last = levels.Length - 1;
            for (int y = 0; y < input.Height; y++)
            {
                int my = Wrap(y + input.OffsetY, n);
                for (int x = 0; x < input.Width; x++)
                {
                    int mx = Wrap(x + input.OffsetX, n);
                    int i = y * input.Width + x;
                    double v = curve(rgb[i * 3]);
                    int k = 0;
                    while (k < last && v >= levels[k + 1]) k++;
                    if (k == last)
                    {
                        output[i] = (byte)order[last];
                        continue;
                    }
                    double lo = levels[k];
                    double f = v <= lo ? 0 : (v - lo) / (levels[k + 1] - lo);
                    double t = (mat[my * n + mx] + 0.5) / nn;
                    output[i] = (byte)order[t < f ? k + 1 : k];
                }
            }
        }

        static double Clamp01(double v) => v < 0 ? 0 : v > 1 ? 1 : v;

        // Serpentine Floyd–Steinberg: rows alternate direction, which stops the diagonal "worm"
        // streaks. The error is carried in linear light.
        static void FloydSteinberg(Input input, byte[] output)
        {
            float[] work = (float[])input.Rgb.Clone();
            PaletteMatcher matcher = input.Matcher;
            float[] lin = matcher.Linear;
            int width = input.Width, height = input.Height;
            for (int y = 0; y < height; y++)
            {
                bool ltr = (y & 1) == 0;
                int dir = ltr ? 1 : -1;
                for (int s = 0; s < width; s++)
                {
                    int x = ltr ? s : width - 1 - s;
                    int i = y * width + x;
                    double r = Clamp01(work[i * 3]), g = Clamp01(work[i * 3 + 1]), b = Clamp01(work[i * 3 + 2]);
                    int idx = matcher.Nearest(r, g, b);
                    output[i] = (byte)idx;
                    double er = r - lin[idx * 3], eg = g - lin[idx * 3 + 1], eb = b - lin[idx * 3 + 2];
                    int xn = x + dir, xp = x - dir;
                    if (xn >= 0 && xn < width) Spread(work, i + dir, er, eg, eb, 7.0 / 16);
                    if (y + 1 < height)
                    {
                        int below = i + width;
                        if (xp >= 0 && xp < width) Spread(work, below - dir, er, eg, eb, 3.0 / 16);
                        Spread(work, below, er, eg, eb, 5.0 / 16);
                        if (xn >= 0 && xn < width) Spread(work, below + dir, er, eg, eb, 1.0 / 16);
                    }
                }
            }
        }

        // Atkinson (as in MacPaint): six neighbours get 1/8 each, so only 3/4 of the error is
        // passed on. Highlights and shadows blow out; that's the look.
        static readonly int[] AtkinsonTaps = { 1, 0, 2, 0, -1, 1, 0, 1, 1, 1, 0, 2 };

        static void Atkinson(Input input, byte[] output)
        {
            float[] work = (float[])input.Rgb.Clone();
            PaletteMatcher matcher = input.Matcher;
            float[] lin = matcher.Linear;
            int width = input.Width, height = input.Height;
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    int i = y * width + x;
                    double r = Clamp01(work[i * 3]), g = Clamp01(work[i * 3 + 1]), b = Clamp01(work[i * 3 + 2]);
                    int idx = matcher.Nearest(r, g, b);
                    output[i] = (byte)idx;
                    double er = r - lin[idx * 3], eg = g - lin[idx * 3 + 1], eb = b - lin[idx * 3 + 2];
                    for (int t = 0; t < AtkinsonTaps.Length; t += 2)
                    {
                        int nx = x + AtkinsonTaps[t], ny = y + AtkinsonTaps[t + 1];
                        if (nx >= 0 && nx < width && ny < height) Spread(work, ny * width + nx, er, eg, eb, 1.0 / 8);
                    }
                }
        }

        static void Spread(float[] work, int j, double er, double eg, double eb, double w)
        {
            work[j * 3] = (float)(work[j * 3] + er * w);
            work[j * 3 + 1] = (float)(work[j * 3 + 1] + eg * w);
            work[j * 3 + 2] = (float)(work[j * 3 + 2] + eb * w);
        }

        static void Yliluoma(Input input, byte[] output)
        {
            float[] rgb = input.Rgb;
            YliluomaPlanner planner = input.Planner();
            byte[] mat = BayerMatrix(8);
            for (int y = 0; y < input.Height; y++)
            {
                int my = (y + input.OffsetY) & 7;
                for (int x = 0; x < input.Width; x++)
                {
                    int mx = (x + input.OffsetX) & 7;
                    int i = y * input.Width + x;
                    // the website's (mat * PLAN / 64) | 0, and PLAN is 64
                    int t = mat[my * 8 + mx];
                    output[i] = (byte)planner.Pick(rgb[i * 3], rgb[i * 3 + 1], rgb[i * 3 + 2], t);
                }
            }
        }
    }
}
