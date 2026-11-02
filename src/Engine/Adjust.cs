namespace B0r3dDeRes.Engine
{
    // Pre-filters, run on the small output-sized picture before dithering. Photos are low in
    // contrast next to a 4- or 16-colour palette; a push here is what turns mud into crisp retro
    // art. They work on sRGB values (how the sliders feel in every photo editor) and hand back
    // linear light. (The website's core/adjust.ts.)
    sealed class Adjust
    {
        public double Brightness;       // -0.5 … 0.5, added
        public double Contrast = 1;     // 0 … 2, around mid-grey
        public double Saturation = 1;   // 0 (grey) … 2
        public double Gamma = 1;        // 0.4 … 2.5; above 1 lifts the shadows

        public bool IsNeutral => Brightness == 0 && Contrast == 1 && Saturation == 1 && Gamma == 1;

        public Adjust Clone() => (Adjust)MemberwiseClone();

        public bool SameAs(Adjust o) => Brightness == o.Brightness && Contrast == o.Contrast && Saturation == o.Saturation && Gamma == o.Gamma;

        static double Clamp01(double v) => v < 0 ? 0 : v > 1 ? 1 : v;

        // the gamma curve as a table, so the loop has no powers in it
        internal static float[] GammaTable(double gamma)
        {
            double inv = 1 / gamma;
            var table = new float[1025];
            for (int i = 0; i <= 1024; i++) table[i] = (float)JsMath.Pow(i / 1024.0, inv);
            return table;
        }

        // Applies the pre-filters to linear-light RGB, in place.
        public void Apply(float[] rgb)
        {
            if (IsNeutral) return;
            double brightness = Brightness, contrast = Contrast, saturation = Saturation;
            float[] table = Gamma == 1 ? null : GammaTable(Gamma);
            for (int i = 0; i < rgb.Length; i += 3)
            {
                double r = Colour.ToSrgb(rgb[i]), g = Colour.ToSrgb(rgb[i + 1]), b = Colour.ToSrgb(rgb[i + 2]);
                if (saturation != 1)
                {
                    double l = 0.2126 * r + 0.7152 * g + 0.0722 * b;
                    r = l + (r - l) * saturation;
                    g = l + (g - l) * saturation;
                    b = l + (b - l) * saturation;
                }
                r = Clamp01((r - 0.5) * contrast + 0.5 + brightness);
                g = Clamp01((g - 0.5) * contrast + 0.5 + brightness);
                b = Clamp01((b - 0.5) * contrast + 0.5 + brightness);
                rgb[i] = (float)Colour.ToLinear(Curve(table, r));
                rgb[i + 1] = (float)Colour.ToLinear(Curve(table, g));
                rgb[i + 2] = (float)Colour.ToLinear(Curve(table, b));
            }
        }

        static double Curve(float[] table, double v)
        {
            if (table == null) return v;
            double f = v * 1024;
            int i = (int)f;
            if (i >= 1024) return table[1024];
            double a = table[i], b = table[i + 1];
            return a + (b - a) * (f - i);
        }

        // Turns linear-light RGB to grey (its luminance), in place.
        public static void ToGrey(float[] rgb)
        {
            for (int i = 0; i < rgb.Length; i += 3)
            {
                float y = (float)Colour.Luminance(rgb[i], rgb[i + 1], rgb[i + 2]);
                rgb[i] = rgb[i + 1] = rgb[i + 2] = y;
            }
        }

        // Sharpens a grey picture's edges in sRGB (the Game Boy Camera did this before its dither),
        // in place.
        public static void EdgeBoost(float[] rgb, int width, int height, double amount)
        {
            if (amount <= 0) return;
            var v = new float[width * height];
            for (int i = 0; i < v.Length; i++) v[i] = (float)Colour.ToSrgb(rgb[i * 3]);
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int i = y * width + x;
                    // (doubles: the website adds the four as doubles)
                    double c = v[i], n = v[y > 0 ? i - width : i], s = v[y < height - 1 ? i + width : i], w = v[x > 0 ? i - 1 : i], e = v[x < width - 1 ? i + 1 : i];
                    double sharp = Clamp01(c + amount * 2 * (c - (n + s + w + e) / 4));
                    rgb[i * 3] = rgb[i * 3 + 1] = rgb[i * 3 + 2] = (float)Colour.ToLinear(sharp);
                }
            }
        }
    }

    // "Detail": shrinking makes each machine pixel the average of the photo pixels it covers.
    // That's right for light, but the thin lines a small picture lives on (eyes, outlines,
    // stripes) are a sliver of each block and fade into it. An unsharp mask on the shrunk picture
    // brings their contrast back. It works in sRGB (in linear light it darkens the dark lines far
    // more than it lightens the light ones), and fades out as the shrink does: a picture that
    // isn't shrunk has nothing lost to bring back. (The website's core/detail.ts.)
    static class Detail
    {
        // sharpening strength at Detail 100% (50%, the default, is 1)
        public const double Max = 2;
        public const double Default = 0.5;

        // the strength for a Detail setting (0-1) when each output pixel covers `cover` photo
        // pixels across and down: full from a 2× shrink, nothing at 1×
        public static double Amount(double detail, double coverX, double coverY)
        {
            double shrink = System.Math.Sqrt(coverX * coverY);
            double ramp = System.Math.Min(1, System.Math.Max(0, shrink - 1));
            return Max * System.Math.Min(1, System.Math.Max(0, detail)) * ramp;
        }

        // Sharpens a linear-light picture (r, g, b a pixel) against the mean of each pixel's 3×3
        // neighbourhood (edges repeat). Returns a new buffer.
        public static float[] Keep(float[] rgb, int w, int h, double amount)
        {
            int n = w * h * 3;
            var s = new float[n];
            for (int i = 0; i < n; i++) s[i] = (float)Colour.ToSrgb(rgb[i]);
            // sums of three across, then of three of those down
            var across = new float[n];
            for (int y = 0; y < h; y++)
            {
                int row = y * w * 3;
                for (int x = 0; x < w; x++)
                {
                    int l = row + System.Math.Max(0, x - 1) * 3, m = row + x * 3, r = row + System.Math.Min(w - 1, x + 1) * 3;
                    for (int c = 0; c < 3; c++) across[m + c] = (float)((double)s[l + c] + s[m + c] + s[r + c]);
                }
            }
            var output = new float[n];
            for (int y = 0; y < h; y++)
            {
                int up = System.Math.Max(0, y - 1) * w * 3, row = y * w * 3, down = System.Math.Min(h - 1, y + 1) * w * 3;
                for (int k = 0; k < w * 3; k++)
                {
                    double v = s[row + k];
                    double mean = ((double)across[up + k] + across[row + k] + across[down + k]) / 9;
                    output[row + k] = (float)Colour.ToLinear(v + amount * (v - mean));
                }
            }
            return output;
        }
    }
}
