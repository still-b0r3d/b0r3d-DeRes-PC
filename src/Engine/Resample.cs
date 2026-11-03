using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace B0r3dDeRes.Engine
{
    // The picture the engine works from: the whole photo as Windows decoded it, upright and in
    // sRGB, blue, green, red and alpha bytes a pixel. It's never changed, so it can be shared.
    sealed class PictureSource
    {
        public readonly byte[] Bgra;
        public readonly int Width, Height;
        // the size of the picture it was made from: its own, or an animated GIF's when its frames
        // were shrunk to be held (Custom's height and Detail go by it, as the website's do)
        public readonly int OrigW, OrigH;
        // some pixel is at least partly see-through
        public readonly bool HasAlpha;

        // `seeThrough`: counts as having see-through parts whatever its own pixels say (an animated
        // GIF's frames, when any of them has some, as the website takes them); `origW` × `origH`:
        // the size it was shrunk from (0: it wasn't)
        public PictureSource(byte[] bgra, int width, int height, bool seeThrough = false, int origW = 0, int origH = 0)
        {
            if (width < 1 || height < 1 || bgra.Length < (long)width * height * 4) throw new ArgumentException("A picture needs pixels");
            Bgra = bgra;
            Width = width;
            Height = height;
            OrigW = origW > 0 ? origW : width;
            OrigH = origH > 0 ? origH : height;
            HasAlpha = seeThrough;
            for (long i = 3; !HasAlpha && i < (long)width * height * 4; i += 4)
                if (bgra[i] != 255)
                {
                    HasAlpha = true;
                    break;
                }
        }
    }

    // Resizing by area average: each output pixel is the mean of the source area it covers, in
    // linear light with alpha premultiplied. One-step shrinking that samples a few pixels
    // aliases; this doesn't. When an axis is enlarged instead, the window is one source pixel
    // wide, the same as linear interpolation. (The website's core/resample.ts.)
    //
    // The website first turns its working copy into floats (linear light times alpha); this reads
    // the photo's bytes through a table of those same floats instead, so a big photo never needs
    // a float copy of itself four times its size.
    static class Resample
    {
        internal sealed class AxisWeights
        {
            // Start[i] … Start[i+1] index into Src and Weight for output i
            public int[] Start, Src;
            public float[] Weight;
            public int Min, Max;
        }

        internal static AxisWeights Axis(int srcLen, double from, double span, int outLen)
        {
            double ratio = span / outLen;
            double width = Math.Max(ratio, 1);
            var start = new int[outLen + 1];
            var src = new List<int>();
            var weight = new List<double>();
            int min = srcLen, max = 0;
            for (int i = 0; i < outLen; i++)
            {
                start[i] = src.Count;
                double c = from + (i + 0.5) * ratio;
                double lo = Math.Max(0, c - width / 2);
                double hi = Math.Min(srcLen, c + width / 2);
                double total = 0;
                int first = src.Count;
                for (double k = Math.Floor(lo); k < Math.Ceiling(hi); k++)
                {
                    double w = Math.Min(hi, k + 1) - Math.Max(lo, k);
                    if (w <= 1e-9) continue;
                    src.Add((int)k);
                    weight.Add(w);
                    total += w;
                    if (k < min) min = (int)k;
                    if (k > max) max = (int)k;
                }
                if (src.Count == first)
                {
                    // the window fell outside the picture (only possible with odd inputs)
                    int k = (int)Math.Min(srcLen - 1, Math.Max(0, Math.Floor(c)));
                    src.Add(k);
                    weight.Add(1);
                    total = 1;
                    min = Math.Min(min, k);
                    max = Math.Max(max, k);
                }
                for (int j = first; j < src.Count; j++) weight[j] /= total;
            }
            start[outLen] = src.Count;
            var weights = new float[weight.Count];
            for (int j = 0; j < weights.Length; j++) weights[j] = (float)weight[j];
            return new AxisWeights { Start = start, Src = src.ToArray(), Weight = weights, Min = min, Max = max };
        }

        // what the website's setSource stores for a byte at an alpha: the byte in linear light
        // times alpha / 255, as a float; and the alpha itself, as a float
        static readonly float[] premultiplied = new float[256 * 256];
        static readonly float[] alphas = new float[256];

        static Resample()
        {
            for (int a = 0; a < 256; a++)
            {
                double alpha = a / 255.0;
                alphas[a] = (float)alpha;
                for (int c = 0; c < 256; c++) premultiplied[(a << 8) | c] = (float)(Colour.ByteToLinear[c] * alpha);
            }
        }

        // The `rect` part of the picture at dw × dh: r, g, b, a floats a pixel, linear light,
        // premultiplied. Rows are shared out over the PC's cores; each output number is the same
        // sum in the same order as on one core, so the result never depends on how many there are.
        public static float[] Area(PictureSource picture, RectD rect, int dw, int dh)
        {
            AxisWeights xw = Axis(picture.Width, rect.X, rect.W, dw);
            AxisWeights yw = Axis(picture.Height, rect.Y, rect.H, dh);
            int rows = yw.Max - yw.Min + 1;
            int sw = picture.Width;
            byte[] data = picture.Bgra;
            int span = dw * 4;
            // across first, over only the rows the pass down will read
            var tmp = new float[(long)rows * span];
            Parallel.For(0, rows, Many(rows, sw), r =>
            {
                long rowBase = (long)(yw.Min + r) * sw;
                long o = (long)r * span;
                for (int i = 0; i < dw; i++)
                {
                    double a0 = 0, a1 = 0, a2 = 0, a3 = 0;
                    for (int j = xw.Start[i], end = xw.Start[i + 1]; j < end; j++)
                    {
                        long s = (rowBase + xw.Src[j]) * 4;
                        double w = xw.Weight[j];
                        int alpha = data[s + 3] << 8;
                        a0 += premultiplied[alpha | data[s + 2]] * w;
                        a1 += premultiplied[alpha | data[s + 1]] * w;
                        a2 += premultiplied[alpha | data[s]] * w;
                        a3 += alphas[data[s + 3]] * w;
                    }
                    tmp[o] = (float)a0;
                    tmp[o + 1] = (float)a1;
                    tmp[o + 2] = (float)a2;
                    tmp[o + 3] = (float)a3;
                    o += 4;
                }
            });
            // then down: each output row sums its rows in turn, rounding to a float after each
            var output = new float[(long)dw * dh * 4];
            Parallel.For(0, dh, Many(dh, span * Math.Max(1, rows / Math.Max(1, dh))), y =>
            {
                long o = (long)y * span;
                for (int j = yw.Start[y]; j < yw.Start[y + 1]; j++)
                {
                    long s = (long)(yw.Src[j] - yw.Min) * span;
                    double w = yw.Weight[j];
                    for (int k = 0; k < span; k++) output[o + k] = (float)(output[o + k] + tmp[s + k] * w);
                }
            });
            return output;
        }

        // all the cores when there's enough work to share, else one
        static ParallelOptions Many(int count, long workEach) =>
            new ParallelOptions { MaxDegreeOfParallelism = Cores ?? ((long)count * workEach > 200000 ? Environment.ProcessorCount : 1) };

        // for the tests: how many cores to use (null: as many as suit the work)
        internal static int? Cores;
    }
}
