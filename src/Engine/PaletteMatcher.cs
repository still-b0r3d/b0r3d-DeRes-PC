using System;

namespace B0r3dDeRes.Engine
{
    // Finds the nearest palette colour to a linear-light colour, compared in OKLab. Answers are
    // kept by the colour cut to 6 bits a channel (262,144 of them, filled in as they're needed),
    // so after the first few thousand pixels a lookup is one array read. One matcher a palette.
    // (The website's core/matcher.ts.)
    sealed class PaletteMatcher
    {
        const ushort Empty = 0xffff;

        public readonly int Size;
        // the colours matched against, as sRGB bytes (r, g, b an entry)
        public readonly byte[] Bytes;
        // the same colours in linear light, in OKLab, and their luminance
        public readonly float[] Linear, Lab, Luma;
        // the typical gap between neighbouring colours (sRGB, 0-1), for ordered dithering
        public readonly double Spread;
        readonly ushort[] cache = new ushort[1 << 18];

        public PaletteMatcher(byte[] colours)
        {
            if (colours.Length == 0 || colours.Length % 3 != 0 || colours.Length / 3 > 256) throw new ArgumentException("A palette needs 1 to 256 colours");
            Size = colours.Length / 3;
            Bytes = (byte[])colours.Clone();
            Linear = new float[Size * 3];
            Lab = new float[Size * 3];
            Luma = new float[Size];
            for (int i = 0; i < Size; i++)
            {
                double r = Colour.ByteToLinear[colours[i * 3]], g = Colour.ByteToLinear[colours[i * 3 + 1]], b = Colour.ByteToLinear[colours[i * 3 + 2]];
                Linear[i * 3] = (float)r;
                Linear[i * 3 + 1] = (float)g;
                Linear[i * 3 + 2] = (float)b;
                Colour.LinearToOklab(r, g, b, out double L, out double A, out double B);
                Lab[i * 3] = (float)L;
                Lab[i * 3 + 1] = (float)A;
                Lab[i * 3 + 2] = (float)B;
                Luma[i] = (float)Colour.Luminance(r, g, b);
            }
            for (int i = 0; i < cache.Length; i++) cache[i] = Empty;
            Spread = MeasureSpread();
        }

        // the byte a 6-bit cache bucket stands for (0 -> 0, 63 -> 255)
        public static int BucketByte(int k) => (k << 2) | (k >> 4);

        // the nearest entry to a linear-light colour (clamped to 0-1)
        public int Nearest(double r, double g, double b) => NearestByte(Colour.LinearToByte(r), Colour.LinearToByte(g), Colour.LinearToByte(b));

        // the nearest entry to an sRGB colour given as bytes
        public int NearestByte(int r8, int g8, int b8)
        {
            int key = ((r8 >> 2) << 12) | ((g8 >> 2) << 6) | (b8 >> 2);
            int idx = cache[key];
            if (idx == Empty)
            {
                idx = Search(BucketByte(r8 >> 2), BucketByte(g8 >> 2), BucketByte(b8 >> 2));
                cache[key] = (ushort)idx;
            }
            return idx;
        }

        // the whole search, no cache: the entry closest in OKLab to an sRGB colour
        public int Search(int r8, int g8, int b8)
        {
            Colour.LinearToOklab(Colour.ByteToLinear[r8], Colour.ByteToLinear[g8], Colour.ByteToLinear[b8], out double t0, out double t1, out double t2);
            int best = 0;
            double bestD = double.PositiveInfinity;
            for (int i = 0; i < Size; i++)
            {
                double dl = Lab[i * 3] - t0, da = Lab[i * 3 + 1] - t1, db = Lab[i * 3 + 2] - t2;
                double d = dl * dl + da * da + db * db;
                if (d < bestD)
                {
                    bestD = d;
                    best = i;
                }
            }
            return best;
        }

        // The mean, over all entries, of the distance to the closest other entry (largest
        // single-channel difference, sRGB). Black and white gives 1, so an ordered dither spans
        // the full range; the EGA's steps of 0x55 give a third, the web-safe cube's 0x33 a fifth.
        double MeasureSpread()
        {
            if (Size < 2) return 0;
            byte[] c = Bytes;
            double total = 0;
            for (int i = 0; i < Size; i++)
            {
                double min = double.PositiveInfinity;
                for (int j = 0; j < Size; j++)
                {
                    if (i == j) continue;
                    double d = Math.Max(Math.Abs(c[i * 3] - c[j * 3]), Math.Max(Math.Abs(c[i * 3 + 1] - c[j * 3 + 1]), Math.Abs(c[i * 3 + 2] - c[j * 3 + 2]))) / 255.0;
                    if (d > 0 && d < min) min = d;
                }
                if (min < double.PositiveInfinity) total += min;
            }
            return total / Size;
        }
    }
}
