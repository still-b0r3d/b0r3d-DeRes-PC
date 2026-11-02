using System;
using System.Collections.Generic;
using System.Linq;

namespace B0r3dDeRes.Engine
{
    // Picking a palette from the picture: a weighted median cut for the machines that pick their
    // colours from a range (and Custom, and the grabber), and the most-used colours of a fixed pool
    // for the Amstrad CPC. The same picture always gives the same colours. (The website's
    // core/quantize.ts.)
    static class Quantize
    {
        // A byte snapped to a machine's levels per channel (2 bits: 0, 85, 170 or 255).
        public static int SnapToBits(double v8, int bits)
        {
            if (bits >= 8) return (int)Math.Min(255, Math.Max(0, JsMath.Round(v8)));
            if (bits <= 0) return 0;
            int maxVal = (1 << bits) - 1;
            double level = JsMath.Round(Math.Min(255, Math.Max(0, v8)) * maxVal / 255);
            return (int)JsMath.Round(level * 255 / maxVal);
        }

        sealed class Bucket
        {
            public int R, G, B;
            public double Weight;
        }

        sealed class Box
        {
            public List<Bucket> Buckets;
            public double TotalWeight;
            public int SpreadR, SpreadG, SpreadB, MaxSpread;
        }

        static Box MakeBox(List<Bucket> buckets)
        {
            double total = 0;
            int minR = 255, maxR = 0, minG = 255, maxG = 0, minB = 255, maxB = 0;
            foreach (Bucket b in buckets)
            {
                total += b.Weight;
                if (b.R < minR) minR = b.R;
                if (b.R > maxR) maxR = b.R;
                if (b.G < minG) minG = b.G;
                if (b.G > maxG) maxG = b.G;
                if (b.B < minB) minB = b.B;
                if (b.B > maxB) maxB = b.B;
            }
            var box = new Box { Buckets = buckets, TotalWeight = total, SpreadR = maxR - minR, SpreadG = maxG - minG, SpreadB = maxB - minB };
            box.MaxSpread = Math.Max(box.SpreadR, Math.Max(box.SpreadG, box.SpreadB));
            return box;
        }

        // Up to `maxColours` colours (r, g, b bytes an entry) for a linear-light picture (3 floats a
        // pixel), snapped to `bitsPerChannel`: pixels gathered into 5-bit buckets, the boxes with
        // the most spread split at their weighted median, each box's average snapped to the grid.
        public static byte[] Rgb(float[] rgb, int maxColours, int bitsPerChannel = 8)
        {
            var histogram = new Histogram();
            for (int i = 0, n = rgb.Length / 3; i < n; i++)
                histogram.Add(Colour.LinearToByte(rgb[i * 3]), Colour.LinearToByte(rgb[i * 3 + 1]), Colour.LinearToByte(rgb[i * 3 + 2]));
            return histogram.Cut(maxColours, bitsPerChannel);
        }

        // The grabber: the picture's own colours, as the website takes them from its copy of the
        // photo (each byte through linear light and back, alpha left out; a pixel that's wholly
        // see-through is black there, as the browser's canvas keeps it). From the bytes, so even a
        // 150 MP photo needs no copy of itself.
        public static byte[] Grab(PictureSource picture, int maxColours)
        {
            var through = new int[256];
            for (int v = 0; v < 256; v++) through[v] = Colour.LinearToByte(Colour.ByteToLinear[v]);
            var histogram = new Histogram();
            byte[] bgra = picture.Bgra;
            for (long i = 0, n = (long)picture.Width * picture.Height * 4; i < n; i += 4)
            {
                if (bgra[i + 3] == 0) histogram.Add(through[0], through[0], through[0]);
                else histogram.Add(through[bgra[i + 2]], through[bgra[i + 1]], through[bgra[i]]);
            }
            return histogram.Cut(maxColours, 8);
        }

        // pixels gathered in 5-bit buckets (32 levels a channel), then cut
        sealed class Histogram
        {
            readonly uint[] counts = new uint[32768];
            readonly double[] sumR = new double[32768], sumG = new double[32768], sumB = new double[32768];
            long pixels;

            public void Add(int r8, int g8, int b8)
            {
                int key = ((r8 >> 3) << 10) | ((g8 >> 3) << 5) | (b8 >> 3);
                counts[key]++;
                sumR[key] += r8;
                sumG[key] += g8;
                sumB[key] += b8;
                pixels++;
            }

            public byte[] Cut(int maxColours, int bitsPerChannel) => pixels == 0 ? new byte[0] : Quantize.Cut(counts, sumR, sumG, sumB, maxColours, bitsPerChannel);
        }

        static byte[] Cut(uint[] counts, double[] sumR, double[] sumG, double[] sumB, int maxColours, int bitsPerChannel)
        {
            int targetCount = Math.Max(1, Math.Min(256, maxColours));
            var buckets = new List<Bucket>();
            for (int key = 0; key < 32768; key++)
            {
                double w = counts[key];
                if (w > 0) buckets.Add(new Bucket { R = (int)JsMath.Round(sumR[key] / w), G = (int)JsMath.Round(sumG[key] / w), B = (int)JsMath.Round(sumB[key] / w), Weight = w });
            }

            // already few enough
            if (buckets.Count <= targetCount)
            {
                var snapped = new HashSet<int>();
                var few = new List<byte>();
                foreach (Bucket b in buckets)
                {
                    int r = SnapToBits(b.R, bitsPerChannel), g = SnapToBits(b.G, bitsPerChannel), bl = SnapToBits(b.B, bitsPerChannel);
                    if (snapped.Add((r << 16) | (g << 8) | bl)) few.AddRange(new[] { (byte)r, (byte)g, (byte)bl });
                }
                return few.ToArray();
            }

            // the median cut, until there are enough boxes
            var boxes = new List<Box> { MakeBox(buckets) };
            while (boxes.Count < targetCount)
            {
                int bestIdx = -1;
                double bestScore = -1;
                for (int i = 0; i < boxes.Count; i++)
                {
                    Box box = boxes[i];
                    if (box.Buckets.Count > 1 && box.MaxSpread > 0)
                    {
                        // spread and weight together, so big clusters come first
                        double score = box.MaxSpread * Math.Sqrt(box.TotalWeight);
                        if (score > bestScore)
                        {
                            bestScore = score;
                            bestIdx = i;
                        }
                    }
                }
                if (bestIdx == -1) break;

                Box split = boxes[bestIdx];
                boxes.RemoveAt(bestIdx);
                List<Bucket> list = split.Buckets;
                char axis = split.SpreadR >= split.SpreadG && split.SpreadR >= split.SpreadB ? 'r' : split.SpreadG >= split.SpreadB ? 'g' : 'b';
                // (no two buckets are the same colour, so this order is the website's whatever the sort)
                list.Sort((a, b) =>
                {
                    int d = axis == 'r' ? a.R - b.R : axis == 'g' ? a.G - b.G : a.B - b.B;
                    if (d != 0) return d;
                    if (a.R != b.R) return a.R - b.R;
                    if (a.G != b.G) return a.G - b.G;
                    return a.B - b.B;
                });

                double half = split.TotalWeight / 2, accum = 0;
                int splitIdx = 1;
                for (int i = 0; i < list.Count - 1; i++)
                {
                    accum += list[i].Weight;
                    if (accum >= half)
                    {
                        splitIdx = i + 1;
                        break;
                    }
                }
                boxes.Add(MakeBox(list.GetRange(0, splitIdx)));
                boxes.Add(MakeBox(list.GetRange(splitIdx, list.Count - splitIdx)));
            }

            // each box's weighted mean, snapped, the heaviest first (a stable sort, as the browser's)
            var palette = new List<(int R, int G, int B, double Weight)>();
            foreach (Box box in boxes)
            {
                double rs = 0, gs = 0, bs = 0;
                foreach (Bucket b in box.Buckets)
                {
                    rs += b.R * b.Weight;
                    gs += b.G * b.Weight;
                    bs += b.B * b.Weight;
                }
                palette.Add((SnapToBits(JsMath.Round(rs / box.TotalWeight), bitsPerChannel), SnapToBits(JsMath.Round(gs / box.TotalWeight), bitsPerChannel),
                    SnapToBits(JsMath.Round(bs / box.TotalWeight), bitsPerChannel), box.TotalWeight));
            }
            var seen = new HashSet<int>();
            var final = new List<byte>();
            foreach (var p in palette.OrderByDescending(p => p.Weight))
                if (seen.Add((p.R << 16) | (p.G << 8) | p.B)) final.AddRange(new[] { (byte)p.R, (byte)p.G, (byte)p.B });
            return final.ToArray();
        }

        // The `targetCount` colours of a fixed pool the picture uses most (each sampled pixel votes
        // for its nearest), the most-voted first.
        public static byte[] FromPool(float[] rgb, string[] pool, int targetCount)
        {
            int count = Math.Max(1, Math.Min(pool.Length, targetCount));
            byte[][] poolRgb = pool.Select(Colour.HexToRgb).ToArray();
            int pixelCount = rgb.Length / 3;
            if (pixelCount == 0 || poolRgb.Length == 0) return new byte[0];

            var votes = new double[poolRgb.Length];
            int step = Math.Max(1, pixelCount / 4096);
            for (int i = 0; i < pixelCount; i += step)
            {
                int r = Colour.LinearToByte(rgb[i * 3]), g = Colour.LinearToByte(rgb[i * 3 + 1]), b = Colour.LinearToByte(rgb[i * 3 + 2]);
                int nearest = 0, minD = int.MaxValue;
                for (int c = 0; c < poolRgb.Length; c++)
                {
                    int dr = r - poolRgb[c][0], dg = g - poolRgb[c][1], db = b - poolRgb[c][2];
                    int d = dr * dr + dg * dg + db * db;
                    if (d < minD)
                    {
                        minD = d;
                        nearest = c;
                    }
                }
                votes[nearest] += 1;
            }
            int[] chosen = Enumerable.Range(0, poolRgb.Length).OrderByDescending(i => votes[i]).Take(count).ToArray();
            var output = new byte[count * 3];
            for (int i = 0; i < count; i++)
            {
                output[i * 3] = poolRgb[chosen[i]][0];
                output[i * 3 + 1] = poolRgb[chosen[i]][1];
                output[i * 3 + 2] = poolRgb[chosen[i]][2];
            }
            return output;
        }
    }
}
