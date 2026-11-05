using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace B0r3dDeRes.Engine
{
    // What a GIF or APNG of full-colour frames goes out with (the website's RgbAnimationOptions).
    sealed class RgbAnimationOptions
    {
        public bool Apng;
        public int Width, Height;
        public int[] DelaysMs;                  // how long each frame shows: one a frame
        public int? LoopCount;                  // 0: forever; null: it plays once
        public Func<int, int, byte[]> Frame;    // frame k (made on lane `lane`), r, g, b a pixel; asked for again in the second pass unless it's held
        public int Lanes = 1;                   // frames made at once
        public long? Budget;                    // pixels held between the passes (else Limits.Budget)
        public AnimationLimits Limits = AnimationLimits.Pc;
        public Action<int, int, string> Progress;   // frames done, of how many; the note names the pass that only reads the colours
        public CancellationToken Cancel;
    }

    // A GIF or APNG from full-colour frames made one at a time (the ASCII studio's pictures; the
    // website's core/encode-animation.ts). Every frame is looked at twice: first for its colours,
    // then to write it. When all the frames together use 256 colours or fewer (the 16 ANSI
    // colours, the C64's, Teletext's, greys), the file is indexed with exactly those colours: small,
    // and the very pixels of the PNG. Past that, a GIF gets one shared palette for every frame (so
    // still parts don't shimmer) and an APNG stays full colour. Frames are held between the two
    // passes while they fit the budget; past it, each is made again.
    static class RgbAnimation
    {
        public static AnimationResult Write(Stream output, RgbAnimationOptions o)
        {
            int total = o.DelaysMs.Length;
            if (total == 0) throw new AnimationException("There are no frames to export.");
            int width = o.Width, height = o.Height;
            long pixels = (long)width * height;
            if (Animation.Limit(width, height, 0, o.Limits) != null) throw new AnimationException("That size is too big for an animation. Pick a smaller size.");
            int lanes = Math.Max(1, o.Lanes);

            // first: every frame's colours (and, for a GIF, samples for a shared palette)
            List<byte[]> held = pixels * total <= (o.Budget ?? o.Limits.Budget) ? new List<byte[]>() : null;
            var census = new ColourCensus();
            PaletteSamples samples = o.Apng ? null : new PaletteSamples(total * pixels);
            int read = 0;
            foreach (byte[][] window in Windows(total, lanes, o.Cancel, o.Frame))
                foreach (byte[] rgb in window)
                {
                    census.Add(rgb, null);
                    samples?.Add(rgb, (int)pixels, null);
                    held?.Add(rgb);
                    o.Progress?.Invoke(++read, total, "reading the colours");
                }
            byte[] exact = census.Palette();

            // then each frame again (held, or made again), let go once it's written
            IEnumerable<byte[][]> Again()
            {
                if (held == null)
                {
                    foreach (byte[][] window in Windows(total, lanes, o.Cancel, o.Frame)) yield return window;
                    yield break;
                }
                for (int start = 0; start < total; start += 8)
                {
                    o.Cancel.ThrowIfCancellationRequested();
                    int n = Math.Min(8, total - start);
                    var window = new byte[n][];
                    for (int i = 0; i < n; i++)
                    {
                        window[i] = held[start + i];
                        held[start + i] = null;
                    }
                    yield return window;
                }
            }

            int written = 0;
            if (o.Apng)
            {
                var writer = new ApngWriter(output, width, height, total, Animation.ApngPlays(o.LoopCount), exact);
                NearestColour colours = exact != null ? new NearestColour(exact) : null;
                foreach (byte[][] window in Again())
                {
                    var frames = new ApngFrame[window.Length];
                    for (int i = 0; i < window.Length; i++)
                    {
                        int k = written + i;
                        frames[i] = colours != null ? new ApngFrame { DelayMs = o.DelaysMs[k], Indices = colours.Map(window[i]) } : new ApngFrame { DelayMs = o.DelaysMs[k], Rgb = window[i] };
                        window[i] = null;
                    }
                    writer.AddAll(frames);
                    written += frames.Length;
                    o.Progress?.Invoke(written, total, null);
                }
                o.Cancel.ThrowIfCancellationRequested();
                writer.Finish();
                return new AnimationResult { Apng = true, Width = width, Height = height, Frames = total };
            }

            byte[] palette = exact ?? samples.Palette();
            var nearest = new NearestColour(palette);
            var gif = new GifWriter(output, width, height, o.LoopCount, palette);
            foreach (byte[][] window in Again())
            {
                var frames = new GifFrame[window.Length];
                for (int i = 0; i < window.Length; i++)
                {
                    int k = written + i;
                    frames[i] = new GifFrame { Width = width, Height = height, DelayMs = o.DelaysMs[k], Indices = nearest.Map(window[i]) };
                    window[i] = null;
                }
                gif.AddAll(frames);
                written += frames.Length;
                o.Progress?.Invoke(written, total, null);
            }
            o.Cancel.ThrowIfCancellationRequested();
            gif.Finish();
            return new AnimationResult { Width = width, Height = height, Frames = total };
        }

        // frames 0 … count-1 in order, `lanes` of them made at once (lane j makes frames j, j + lanes, … of a window)
        static IEnumerable<byte[][]> Windows(int count, int lanes, CancellationToken cancel, Func<int, int, byte[]> make)
        {
            int size = Math.Max(4, lanes);
            for (int start = 0; start < count; start += size)
            {
                cancel.ThrowIfCancellationRequested();
                int n = Math.Min(size, count - start);
                var window = new byte[n][];
                if (lanes <= 1) for (int i = 0; i < n; i++) window[i] = make(start + i, 0);
                else EveryCore.For(0, Math.Min(lanes, n), lane =>
                {
                    for (int i = lane; i < n; i += lanes) window[i] = make(start + i, lane);
                });
                yield return window;
            }
        }
    }
}
