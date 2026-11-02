using System;
using System.Collections.Generic;

namespace B0r3dDeRes.Engine
{
    // The Amiga's HAM6 (Hold-And-Modify): 12-bit colour (16 levels a channel) from 16 base colours,
    // where each pixel along a line is either a base colour or the pixel before it with one
    // channel changed, which makes the colour fringes along every edge. (The website's
    // core/ham.ts.)
    static class Ham
    {
        public sealed class Result
        {
            public byte[] Indices;  // 0-15 a base colour, 16+ blue, 32+ red, 48+ green changed (the level after)
            public float[] Rgb;     // linear light
            public byte[] Palette;  // the 16 base colours
            public int UsedColours;
        }

        // green counts most, then red, then blue (about 4:2:1)
        static int Dist(int r1, int g1, int b1, int r2, int g2, int b2)
        {
            int dr = r1 - r2, dg = g1 - g2, db = b1 - b2;
            return 2 * dr * dr + 4 * dg * dg + db * db;
        }

        static int To4(int v8) => (int)JsMath.Round(v8 * 15 / 255.0);

        public static Result Ham6(float[] linear, int width, int height)
        {
            var indices = new byte[width * height];
            var output = new float[width * height * 3];
            var seen = new HashSet<int>();

            // the 16 base colours, 4 bits a channel (padded with black when the picture has fewer)
            byte[] basePalette = Quantize.Rgb(linear, 16, 4);
            if (basePalette.Length < 48) Array.Resize(ref basePalette, 48);
            var baseR = new int[16];
            var baseG = new int[16];
            var baseB = new int[16];
            for (int i = 0; i < 16; i++)
            {
                baseR[i] = To4(basePalette[i * 3]);
                baseG[i] = To4(basePalette[i * 3 + 1]);
                baseB[i] = To4(basePalette[i * 3 + 2]);
            }

            for (int y = 0; y < height; y++)
            {
                int curR = 0, curG = 0, curB = 0;
                for (int x = 0; x < width; x++)
                {
                    int p = y * width + x, i = p * 3;
                    int tr = To4(Colour.LinearToByte(linear[i])), tg = To4(Colour.LinearToByte(linear[i + 1])), tb = To4(Colour.LinearToByte(linear[i + 2]));

                    int bestBase = 0, minBase = int.MaxValue;
                    for (int b = 0; b < 16; b++)
                    {
                        int d = Dist(tr, tg, tb, baseR[b], baseG[b], baseB[b]);
                        if (d < minBase)
                        {
                            minBase = d;
                            bestBase = b;
                        }
                    }
                    if (x == 0)
                    {
                        // a line starts from a base colour
                        curR = baseR[bestBase];
                        curG = baseG[bestBase];
                        curB = baseB[bestBase];
                        indices[p] = (byte)bestBase;
                    }
                    else
                    {
                        int modB = Dist(tr, tg, tb, curR, curG, tb);
                        int modR = Dist(tr, tg, tb, tr, curG, curB);
                        int modG = Dist(tr, tg, tb, curR, tg, curB);
                        int best = Math.Min(Math.Min(minBase, modB), Math.Min(modR, modG));
                        if (best == minBase)
                        {
                            curR = baseR[bestBase];
                            curG = baseG[bestBase];
                            curB = baseB[bestBase];
                            indices[p] = (byte)bestBase;
                        }
                        else if (best == modB)
                        {
                            curB = tb;
                            indices[p] = (byte)(16 + tb);
                        }
                        else if (best == modR)
                        {
                            curR = tr;
                            indices[p] = (byte)(32 + tr);
                        }
                        else
                        {
                            curG = tg;
                            indices[p] = (byte)(48 + tg);
                        }
                    }

                    seen.Add((curR << 8) | (curG << 4) | curB);
                    output[i] = Colour.ByteToLinear[(int)JsMath.Round(curR * 255 / 15.0)];
                    output[i + 1] = Colour.ByteToLinear[(int)JsMath.Round(curG * 255 / 15.0)];
                    output[i + 2] = Colour.ByteToLinear[(int)JsMath.Round(curB * 255 / 15.0)];
                }
            }
            return new Result { Indices = indices, Rgb = output, Palette = basePalette, UsedColours = seen.Count };
        }
    }

    // The Nintendo 64's look: 15-bit colour (5 bits a channel), then its 3-point sample filter
    // and the video interface's sideways blur. (The website's core/n64.ts.)
    static class N64
    {
        public static void Filter(float[] linear, int width, int height, out byte[] indices, out float[] output)
        {
            indices = new byte[width * height];
            output = new float[width * height * 3];
            int n = width * height;

            // 15-bit colour
            var q = new float[n * 3];
            for (int i = 0; i < n * 3; i++) q[i] = Colour.ByteToLinear[Quantize.SnapToBits(Colour.LinearToByte(linear[i]), 5)];

            // the 3-point filter: half the pixel, a quarter each of the one right and the one below
            var filtered = new float[n * 3];
            for (int y = 0; y < height; y++)
            {
                int nextY = Math.Min(height - 1, y + 1);
                for (int x = 0; x < width; x++)
                {
                    int nextX = Math.Min(width - 1, x + 1);
                    int c = (y * width + x) * 3, r = (y * width + nextX) * 3, d = (nextY * width + x) * 3;
                    for (int k = 0; k < 3; k++) filtered[c + k] = (float)(0.5 * q[c + k] + 0.25 * q[r + k] + 0.25 * q[d + k]);
                }
            }

            // the video interface's blur along the line (0.2, 0.6, 0.2)
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int prevX = Math.Max(0, x - 1), nextX = Math.Min(width - 1, x + 1);
                    int p = y * width + x, c = p * 3, l = (y * width + prevX) * 3, r = (y * width + nextX) * 3;
                    for (int k = 0; k < 3; k++) output[c + k] = (float)(0.2 * filtered[l + k] + 0.6 * filtered[c + k] + 0.2 * filtered[r + k]);
                    int r5 = (Colour.LinearToByte(output[c]) >> 3) & 0x1f, g5 = (Colour.LinearToByte(output[c + 1]) >> 3) & 0x1f, b5 = (Colour.LinearToByte(output[c + 2]) >> 3) & 0x1f;
                    indices[p] = (byte)(((r5 << 10) | (g5 << 5) | b5) & 0xff);
                }
            }
        }
    }
}
