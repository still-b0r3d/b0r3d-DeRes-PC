using System;

namespace B0r3dDeRes.Engine
{
    // Where the photo goes in the output: which part of it is used (Src, in the picture's pixels)
    // and where it lands (Dst, in output pixels). Native resolution is the main control; "Custom"
    // is a width in pixels you pick (the machine's own width until you do), square pixels, the
    // photo's own shape. (The website's core/geometry.ts.)
    static class Fits
    {
        public const string Fill = "fill", Fit = "fit", Stretch = "stretch";
    }

    struct RectD
    {
        public double X, Y, W, H;
        public RectD(double x, double y, double w, double h) { X = x; Y = y; W = w; H = h; }
    }

    struct RectI
    {
        public int X, Y, W, H;
        public RectI(int x, int y, int w, int h) { X = x; Y = y; W = w; H = h; }
    }

    sealed class Geometry
    {
        public int Width, Height;
        public RectD Src;
        public RectI Dst;
        // pixel width : height
        public int ParX, ParY;

        public bool SquarePixels => ParX == ParY;

        // the largest output side for custom sizes, the narrowest custom width, and the tallest
        // strip the receipt printer makes
        public const int CustomMax = 1024, CustomMin = 8, AutoMax = 4096;

        // The widths Custom can make of this photo: never wider than the photo is (or the part of
        // it the engine works from), and no side past CustomMax.
        public static void CustomWidthRange(int srcW, int srcH, int origW, int origH, out int min, out int max)
        {
            double cap = Math.Min(CustomMax, Math.Max(srcW, srcH));
            max = (int)Math.Max(1, Math.Floor(cap * origW / Math.Max(origW, origH)));
            min = Math.Min(CustomMin, max);
        }

        // How many of the photo's own pixels each output pixel covers, across and down (below 1:
        // the photo is enlarged). The more, the more fine detail the shrink averages away.
        public void Coverage(int srcW, int srcH, int origW, int origH, out double x, out double y)
        {
            x = Src.W * origW / srcW / Dst.W;
            y = Src.H * origH / srcH / Dst.H;
        }

        public static Geometry Plan(int srcW, int srcH, int origW, int origH, Profile p, bool custom, int? customWidth, string fit)
        {
            var whole = new RectD(0, 0, srcW, srcH);
            if (custom)
            {
                CustomWidthRange(srcW, srcH, origW, origH, out int min, out int max);
                int w = (int)Math.Min(max, Math.Max(min, JsMath.Round(customWidth ?? p.Width)));
                int h = (int)Math.Max(1, JsMath.Round((double)w * origH / origW));
                return new Geometry { Width = w, Height = h, Src = whole, Dst = new RectI(0, 0, w, h), ParX = 1, ParY = 1 };
            }

            double ratio = (double)p.ParX / p.ParY;
            int width = p.Width;
            if (p.AutoHeight)
            {
                int autoH = (int)Math.Min(AutoMax, Math.Max(1, JsMath.Round((double)width * srcH / srcW / ratio)));
                return new Geometry { Width = width, Height = autoH, Src = whole, Dst = new RectI(0, 0, width, autoH), ParX = p.ParX, ParY = p.ParY };
            }
            int height = p.Height;
            var full = new RectI(0, 0, width, height);
            // shapes as seen on the machine's screen
            double screenAspect = width * ratio / height;
            double photoAspect = (double)srcW / srcH;
            var g = new Geometry { Width = width, Height = height, Src = whole, Dst = full, ParX = p.ParX, ParY = p.ParY };

            // the same shape (within rounding): nothing to crop or pad
            if (fit == Fits.Stretch || Math.Abs(photoAspect / screenAspect - 1) < 1e-9) return g;

            if (fit == Fits.Fill)
            {
                // crop the middle of the photo to the screen's shape
                if (photoAspect > screenAspect)
                {
                    double cw = srcH * screenAspect;
                    g.Src = new RectD((srcW - cw) / 2, 0, cw, srcH);
                }
                else
                {
                    double ch = srcW / screenAspect;
                    g.Src = new RectD(0, (srcH - ch) / 2, srcW, ch);
                }
                return g;
            }

            // fit: the whole photo, with bars
            if (photoAspect > screenAspect)
            {
                int fh = (int)Math.Max(1, Math.Min(height, JsMath.Round(width * ratio / photoAspect)));
                g.Dst = new RectI(0, (int)Math.Floor((height - fh) / 2.0), width, fh);
                return g;
            }
            int fw = (int)Math.Max(1, Math.Min(width, JsMath.Round(height * photoAspect / ratio)));
            g.Dst = new RectI((int)Math.Floor((width - fw) / 2.0), 0, fw, height);
            return g;
        }
    }
}
