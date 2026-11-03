using System;
using System.Collections.Generic;

namespace B0r3dDeRes.Engine
{
    // An animated GIF, opened: every frame as it shows (shrunk only when they wouldn't all fit),
    // each a picture the engine works from. Frame 0 is "the picture" every control works on.
    sealed class AnimatedGif
    {
        public int Width, Height;       // every frame's size, after any shrinking
        public int OrigW, OrigH;        // the GIF's own size
        public PictureSource[] Frames;
        public int[] DelaysMs;
        public int? LoopCount;          // NETSCAPE2.0's (0 = forever); null when the file has none (it plays once)
        public int Total;               // frames in the file; more than Frames.Length when some were cut
        public bool HasAlpha;           // some frame has see-through pixels (they all count as having them)
        public double DurationMs;       // every frame's delay, added up
        // Engine frames per GIF frame, so the signal's noise moves at the GIF's own speed (the
        // website's signalStep).
        public int SignalStep;

        // where frame k sits in its loop (0 to 1), for the screen effect's moving parts
        public double Phase(int k) => (double)k / Frames.Length;
    }

    // Opening an animated GIF: every frame decoded, shrunk as it comes if all of them wouldn't fit
    // the budget, and kept one copy each. (The website's core/gif-frames.ts.)
    static class GifFrames
    {
        // frames are never shrunk below this long edge to make room; frames are cut instead
        public const int MinEdge = 256;

        // The size to hold an animation at, and how many of its frames fit: as big as maxEdge
        // allows while every frame fits the budget, shrinking to no less than MinEdge on the long
        // side (or the GIF's own size, if smaller); past that, only the first `keep` frames.
        public static void Plan(int origW, int origH, int total, double maxEdge, double budget, out int width, out int height, out int keep)
        {
            double longEdge = Math.Max(origW, origH);
            double most = Math.Min(1, maxEdge / longEdge);
            double least = Math.Min(most, MinEdge / longEdge);
            double fit = Math.Sqrt(budget / ((double)origW * origH * Math.Max(1, total)));
            double s = Math.Max(least, Math.Min(most, fit));
            width = (int)Math.Max(1, JsMath.Round(origW * s));
            height = (int)Math.Max(1, JsMath.Round(origH * s));
            // rounding up can tip a frame count that just fits over the budget
            if (s == fit && (double)width * height * total > budget)
            {
                width = (int)Math.Max(1, Math.Floor(origW * s));
                height = (int)Math.Max(1, Math.Floor(origH * s));
            }
            keep = (int)Math.Max(1, Math.Min(total, Math.Floor(budget / ((double)width * height))));
        }

        // An animated GIF's frames, or null when it isn't one, can't be read, or has fewer than two
        // frames that decode (it then opens as a still).
        public static AnimatedGif Open(byte[] bytes, double maxEdge, double budget, GifLimits limits = null)
        {
            int total = GifInfo.FrameCount(bytes);
            if (total < 2) return null;
            GifReader reader;
            try
            {
                reader = new GifReader(bytes, limits);
            }
            catch (GifException)
            {
                return null;
            }
            int origW = reader.Width, origH = reader.Height;
            Plan(origW, origH, total, maxEdge, budget, out int width, out int height, out int keep);
            var frames = new List<byte[]>();
            var delays = new List<int>();
            bool hasAlpha = false;
            foreach (DecodedGifFrame f in reader.Frames())
            {
                byte[] frame = ShrinkToBgra(f.Rgba, origW, origH, width, height);
                if (!hasAlpha)
                    for (long i = 3; i < frame.LongLength; i += 4)
                        if (frame[i] < 255)
                        {
                            hasAlpha = true;
                            break;
                        }
                frames.Add(frame);
                delays.Add(f.DelayMs);
                if (frames.Count >= keep) break;
            }
            if (frames.Count < 2) return null;
            var sources = new PictureSource[frames.Count];
            for (int k = 0; k < sources.Length; k++) sources[k] = new PictureSource(frames[k], width, height, hasAlpha, origW, origH);
            double duration = 0;
            foreach (int d in delays) duration += d;
            return new AnimatedGif
            {
                Width = width, Height = height, OrigW = origW, OrigH = origH, Frames = sources, DelaysMs = delays.ToArray(),
                LoopCount = reader.LoopCount, Total = Math.Max(total, frames.Count), HasAlpha = hasAlpha,
                DurationMs = duration, SignalStep = Signal.EngineStep(1000 / (duration / delays.Count)),
            };
        }

        // An sRGB picture (r, g, b, a bytes a pixel) shrunk to dw × dh by area average in linear
        // light, see-through parts included (premultiplied, so a see-through pixel's colour can't
        // bleed in), a row at a time; as b, g, r, a bytes for the engine. The same size: a plain
        // copy. (The website's shrinkRgba.)
        public static byte[] ShrinkToBgra(byte[] src, int sw, int sh, int dw, int dh)
        {
            var output = new byte[(long)dw * dh * 4];
            if (dw == sw && dh == sh)
            {
                for (long i = 0; i < output.LongLength; i += 4)
                {
                    output[i] = src[i + 2];
                    output[i + 1] = src[i + 1];
                    output[i + 2] = src[i];
                    output[i + 3] = src[i + 3];
                }
                return output;
            }
            Resample.AxisWeights xw = Resample.Axis(sw, 0, sw, dw), yw = Resample.Axis(sh, 0, sh, dh);
            var sum = new float[dw * 4];
            var across = new float[dw * 4];
            for (int y = 0; y < dh; y++)
            {
                Array.Clear(sum, 0, sum.Length);
                for (int j = yw.Start[y]; j < yw.Start[y + 1]; j++)
                {
                    // one source row, shrunk across
                    long rowBase = (long)yw.Src[j] * sw;
                    for (int i = 0, o = 0; i < dw; i++, o += 4)
                    {
                        double r = 0, g = 0, b = 0, a = 0;
                        for (int k = xw.Start[i], end = xw.Start[i + 1]; k < end; k++)
                        {
                            long s = (rowBase + xw.Src[k]) << 2;
                            double alpha = src[s + 3] / 255.0 * xw.Weight[k];
                            r += Colour.ByteToLinear[src[s]] * alpha;
                            g += Colour.ByteToLinear[src[s + 1]] * alpha;
                            b += Colour.ByteToLinear[src[s + 2]] * alpha;
                            a += alpha;
                        }
                        across[o] = (float)r;
                        across[o + 1] = (float)g;
                        across[o + 2] = (float)b;
                        across[o + 3] = (float)a;
                    }
                    double w = yw.Weight[j];
                    for (int k = 0; k < sum.Length; k++) sum[k] = (float)(sum[k] + across[k] * w);
                }
                long at = (long)y * dw * 4;
                for (int i = 0; i < dw; i++, at += 4)
                {
                    double a = sum[i * 4 + 3];
                    if (a <= 0) continue;
                    output[at] = (byte)Colour.LinearToByte(sum[i * 4 + 2] / a);
                    output[at + 1] = (byte)Colour.LinearToByte(sum[i * 4 + 1] / a);
                    output[at + 2] = (byte)Colour.LinearToByte(sum[i * 4] / a);
                    output[at + 3] = (byte)Math.Min(255, JsMath.Round(a * 255));
                }
            }
            return output;
        }
    }
}
