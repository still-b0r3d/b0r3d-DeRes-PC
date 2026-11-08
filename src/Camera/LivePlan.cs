using System;
using System.Collections.Generic;
using B0r3dDeRes.Engine;

namespace B0r3dDeRes.Camera
{
    // The live camera's sums and words, kept apart from the camera itself so they can be tested
    // without one: how big a frame to grab, how far to enlarge the screen effect while live, how big
    // a clip's frames are held, and what the camera's pictures and clips are called. (The website's
    // ui/camera-plan.ts.)
    static class LivePlan
    {
        // camera pixels grabbed per machine pixel: enough for the area average and Detail to work on
        public const int Detail = 3;
        // the longest side grabbed for the live view (custom widths past WideFrom get MaxWide)
        public const int Max = 960, MaxWide = 1280, WideFrom = 320;
        // the most pixels the screen effect is drawn at while live (a whole frame, every frame)
        public const int FxPixels = 300000;
        // below this many frames a second for SlowAfterMs, the live view says the look is slow
        public const int SlowFps = 8, SlowAfterMs = 2000;
        // a clip: 3 seconds at 10 frames a second (a GIF's delays are hundredths, so 100 ms is exact)
        public const int ClipFrames = 30, ClipDelayMs = 100;

        // The size to grab a camera frame at for the live view: the part the machine shows is about
        // Detail camera pixels across for every machine pixel, never more than the camera gives, and
        // no side past Max (MaxWide for wide custom sizes).
        public static void InputSize(int videoW, int videoH, Profile p, bool custom, int? customWidth, string fit, out int width, out int height)
        {
            Geometry g = Geometry.Plan(videoW, videoH, videoW, videoH, p, custom, customWidth, fit);
            double want = Detail * Math.Max(g.Dst.W / g.Src.W, g.Dst.H / g.Src.H);
            bool wide = custom && (customWidth ?? p.Width) > WideFrom;
            double s = Math.Min(1, Math.Min(want, (double)(wide ? MaxWide : Max) / Math.Max(videoW, videoH)));
            width = (int)Math.Max(1, JsMath.Round(videoW * s));
            height = (int)Math.Max(1, JsMath.Round(videoH * s));
        }

        // The screen effect's enlargement while live: the preview's own (`still`), stepped down
        // until a frame is at most FxPixels, never below 2× (a CRT at 2× still shows its mask).
        // 0 stays 0 (no effect).
        public static int FxScale(int still, int width, int height, int parX, int parY, bool tv)
        {
            if (still <= 0) return 0;
            double ratio = tv ? (double)parX / parY : 1;
            int s = still;
            while (s > 2 && width * s * ratio * height * s > FxPixels) s--;
            return s;
        }

        // A clip's frames: as an animated GIF of ClipFrames frames from this camera is held (the
        // PC's budget, so a camera's frames are usually kept whole).
        public static void ClipSize(int videoW, int videoH, double budget, out int width, out int height)
        {
            GifFrames.Plan(videoW, videoH, ClipFrames, double.PositiveInfinity, budget, out width, out height, out _);
        }

        // A camera picture's or clip's name, from the local time: "camera-2026-10-22-2140-05".
        public static string Name(string kind, DateTime at) =>
            kind + "-" + at.ToString("yyyy-MM-dd-HHmm-ss", System.Globalization.CultureInfo.InvariantCulture);

        // The camera's frame (blue, green, red, unused) at width × height as the engine's picture,
        // each pixel the plain average of the camera pixels it covers (the website grabs it through a
        // canvas), mirrored left to right if asked. The same size is a copy. Never see-through.
        public static PictureSource Grab(Frame frame, int width, int height, bool mirror, int origW = 0, int origH = 0)
        {
            var bgra = new byte[(long)width * height * 4];
            int fw = frame.Width, fh = frame.Height, stride = fw * 4;
            byte[] px = frame.Pixels;
            if (width == fw && height == fh)
            {
                for (int y = 0; y < height; y++)
                {
                    int from = y * stride, to = y * width * 4;
                    if (!mirror) Buffer.BlockCopy(px, from, bgra, to, stride);
                    else
                        for (int x = 0; x < width; x++)
                        {
                            int s = from + (fw - 1 - x) * 4, d = to + x * 4;
                            bgra[d] = px[s];
                            bgra[d + 1] = px[s + 1];
                            bgra[d + 2] = px[s + 2];
                        }
                    for (int x = 0, d = to + 3; x < width; x++, d += 4) bgra[d] = 255;
                }
            }
            else
            {
                // where each output column's pixels start and end in the camera's row
                var left = new int[width + 1];
                for (int x = 0; x <= width; x++) left[x] = (int)((long)x * fw / width);
                for (int y = 0; y < height; y++)
                {
                    int top = (int)((long)y * fh / height), bottom = Math.Max(top + 1, (int)((long)(y + 1) * fh / height));
                    for (int x = 0; x < width; x++)
                    {
                        int a = left[x], b = Math.Max(a + 1, left[x + 1]);
                        long sb = 0, sg = 0, sr = 0;
                        for (int row = top; row < bottom; row++)
                            for (int at = row * stride + a * 4, end = row * stride + b * 4; at < end; at += 4)
                            {
                                sb += px[at];
                                sg += px[at + 1];
                                sr += px[at + 2];
                            }
                        long n = (long)(bottom - top) * (b - a);
                        int d = (y * width + (mirror ? width - 1 - x : x)) * 4;
                        bgra[d] = (byte)((sb + n / 2) / n);
                        bgra[d + 1] = (byte)((sg + n / 2) / n);
                        bgra[d + 2] = (byte)((sr + n / 2) / n);
                        bgra[d + 3] = 255;
                    }
                }
            }
            return new PictureSource(bgra, width, height, false, origW, origH);
        }
    }

    // Frames shown a second over the last second, and whether the look has been slow for a while.
    sealed class FpsMeter
    {
        readonly Queue<double> shown = new Queue<double>();
        double fastAt;

        // a new start (the camera came on, or another one): no history
        public void Reset(double now)
        {
            shown.Clear();
            fastAt = now;
        }

        // a frame was shown at `now` (ms)
        public void Frame(double now)
        {
            shown.Enqueue(now);
            while (shown.Count > 0 && shown.Peek() <= now - 1000) shown.Dequeue();
            if (Fps(now) >= LivePlan.SlowFps) fastAt = now;
        }

        public int Fps(double now)
        {
            int n = 0;
            foreach (double t in shown) if (t > now - 1000) n++;
            return n;
        }

        // under SlowFps for the last SlowAfterMs
        public bool Slow(double now) => now - fastAt >= LivePlan.SlowAfterMs;
    }
}
