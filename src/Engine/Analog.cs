using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace B0r3dDeRes.Engine
{
    // The analog, print and video looks: the early digital camera's CCD (sensor noise and the
    // burnt-orange date stamp), the JPEG "deep fry" (8×8 blocks crunched), the newspaper's CMYK
    // halftone rosettes, the two-ink risograph and the streaky 1-bit fax. Each takes the picture
    // in linear light at the machine's size and gives back its colours (linear light), a small
    // number a pixel for the tile meter, and its paper. (The website's core/analog.ts, to the
    // last bit.)
    static class Analog
    {
        public sealed class Result
        {
            public byte[] Indices;
            public float[] Rgb;
            public byte[] Paper;
        }

        public const string Ccd = "ccd", DeepFry = "deepfry", Newspaper = "newspaper", Risograph = "risograph", Fax = "fax";

        public static Result Process(string mode, float[] rgb, int width, int height)
        {
            switch (mode)
            {
                case Ccd: return EarlyCcd(rgb, width, height);
                case DeepFry: return Fry(rgb, width, height);
                case Newspaper: return Halftone(rgb, width, height);
                case Risograph: return Riso(rgb, width, height);
                default: return FaxMachine(rgb, width, height);
            }
        }

        // the website's 2D hash: 0 <= h < 1
        static double Hash(int x, int y, int seed)
        {
            uint h = (uint)((x * 0x1f1f1f1f) ^ (y * 0x5b5b5b5b) ^ seed);
            h = (uint)((int)(h ^ (h >> 15)) * 0x2c1b3c6d);
            h = (uint)((int)(h ^ (h >> 12)) * 0x297a2d39);
            return (h ^ (h >> 15)) / 4294967296.0;
        }

        // ---- the date stamp's 7-segment digits and the fax header's 5×7 letters ----

        // segments a (top), b (top right), c (bottom right), d (bottom), e (bottom left), f (top left), g (middle)
        static readonly Dictionary<char, int> sevenSeg = new Dictionary<char, int>
        {
            { '0', 0x3f }, { '1', 0x06 }, { '2', 0x5b }, { '3', 0x4f }, { '4', 0x66 }, { '5', 0x6d }, { '6', 0x7d },
            { '7', 0x07 }, { '8', 0x7f }, { '9', 0x6f }, { '-', 0x40 }, { ' ', 0 },
        };

        // whether pixel (px, py) of a digit 8 wide and 14 high is lit
        static bool SevenSegmentPixel(char ch, int px, int py)
        {
            // the apostrophe: a short stroke at the top
            if (ch == '\'') return px >= 2 && px <= 4 && py >= 1 && py <= 4;
            if (!sevenSeg.TryGetValue(ch, out int mask) || mask == 0) return false;
            const int w = 8, h = 14, mid = 6;
            if ((mask & 0x01) != 0 && py >= 0 && py <= 1 && px >= 1 && px <= w - 2) return true;
            if ((mask & 0x02) != 0 && py >= 1 && py <= mid && px >= w - 2 && px < w) return true;
            if ((mask & 0x04) != 0 && py >= mid && py <= h - 2 && px >= w - 2 && px < w) return true;
            if ((mask & 0x08) != 0 && py >= h - 2 && py < h && px >= 1 && px <= w - 2) return true;
            if ((mask & 0x10) != 0 && py >= mid && py <= h - 2 && px >= 0 && px <= 1) return true;
            if ((mask & 0x20) != 0 && py >= 1 && py <= mid && px >= 0 && px <= 1) return true;
            if ((mask & 0x40) != 0 && py >= mid - 1 && py <= mid && px >= 1 && px <= w - 2) return true;
            return false;
        }

        // columns, bit 0 at the top
        static readonly Dictionary<char, int[]> font5x7 = new Dictionary<char, int[]>
        {
            { ' ', new[] { 0, 0, 0, 0, 0 } }, { ':', new[] { 0, 0x24, 0, 0, 0 } }, { '-', new[] { 0x08, 0x08, 0x08, 0x08, 0x08 } },
            { '+', new[] { 0x08, 0x08, 0x3e, 0x08, 0x08 } }, { '/', new[] { 0x03, 0x0c, 0x30, 0xc0, 0 } }, { '.', new[] { 0, 0x40, 0, 0, 0 } },
            { '0', new[] { 0x3e, 0x51, 0x49, 0x45, 0x3e } }, { '1', new[] { 0x00, 0x42, 0x7f, 0x40, 0x00 } }, { '2', new[] { 0x42, 0x61, 0x51, 0x49, 0x46 } },
            { '3', new[] { 0x21, 0x41, 0x45, 0x4b, 0x31 } }, { '4', new[] { 0x18, 0x14, 0x12, 0x7f, 0x10 } }, { '5', new[] { 0x27, 0x45, 0x45, 0x45, 0x39 } },
            { '6', new[] { 0x3c, 0x4a, 0x49, 0x49, 0x30 } }, { '7', new[] { 0x01, 0x71, 0x09, 0x05, 0x03 } }, { '8', new[] { 0x36, 0x49, 0x49, 0x49, 0x36 } },
            { '9', new[] { 0x06, 0x49, 0x49, 0x29, 0x1e } }, { 'A', new[] { 0x7e, 0x11, 0x11, 0x11, 0x7e } }, { 'B', new[] { 0x7f, 0x49, 0x49, 0x49, 0x36 } },
            { 'C', new[] { 0x3e, 0x41, 0x41, 0x41, 0x22 } }, { 'D', new[] { 0x7f, 0x41, 0x41, 0x22, 0x1c } }, { 'E', new[] { 0x7f, 0x49, 0x49, 0x49, 0x41 } },
            { 'F', new[] { 0x7f, 0x09, 0x09, 0x09, 0x01 } }, { 'G', new[] { 0x3e, 0x41, 0x49, 0x49, 0x7a } }, { 'H', new[] { 0x7f, 0x08, 0x08, 0x08, 0x7f } },
            { 'I', new[] { 0x00, 0x41, 0x7f, 0x41, 0x00 } }, { 'L', new[] { 0x7f, 0x40, 0x40, 0x40, 0x40 } }, { 'M', new[] { 0x7f, 0x02, 0x0c, 0x02, 0x7f } },
            { 'O', new[] { 0x3e, 0x41, 0x41, 0x41, 0x3e } }, { 'P', new[] { 0x7f, 0x09, 0x09, 0x09, 0x06 } }, { 'R', new[] { 0x7f, 0x09, 0x19, 0x29, 0x46 } },
            { 'T', new[] { 0x01, 0x01, 0x7f, 0x01, 0x01 } }, { 'X', new[] { 0x63, 0x14, 0x08, 0x14, 0x63 } }, { 'Y', new[] { 0x07, 0x08, 0x70, 0x08, 0x07 } },
        };

        static bool Font5x7Pixel(char ch, int px, int py)
        {
            if (!font5x7.TryGetValue(char.ToUpperInvariant(ch), out int[] glyph)) glyph = font5x7[' '];
            if (px < 0 || px >= 5 || py < 0 || py >= 7) return false;
            return ((glyph[px] >> py) & 1) == 1;
        }

        static int FloorDiv(int a, int b) => (int)Math.Floor((double)a / b);

        static double Clamp01(double v) => Math.Max(0, Math.Min(1, v));

        // ---- the early digital camera: 640×480, sensor noise and the date stamp ----

        static Result EarlyCcd(float[] lin, int width, int height)
        {
            int n = width * height;
            var output = new float[n * 3];
            var indices = new byte[n];

            // the burnt-orange date stamp at the bottom right
            const string date = "'99 10 24";
            const int digitW = 8, digitH = 14, spacing = 3;
            int totalW = date.Length * (digitW + spacing) - spacing;
            int dateX0 = width - totalW - 20, dateY0 = height - digitH - 18;
            float stampR = Colour.ByteToLinear[255], stampG = Colour.ByteToLinear[120], stampB = Colour.ByteToLinear[0];
            float haloR = Colour.ByteToLinear[130], haloG = Colour.ByteToLinear[45], haloB = Colour.ByteToLinear[0];

            EveryCore.For(0, height, y =>
            {
                for (int x = 0; x < width; x++)
                {
                    int idx = y * width + x, p = idx * 3;
                    // the sensor's noise: photon shot noise (stronger in the light) and chroma grain
                    double r = lin[p], g = lin[p + 1], b = lin[p + 2];
                    double lum = Colour.Luminance(r, g, b);
                    double n1 = Hash(x, y, 101) - 0.5, n2 = Hash(x, y, 202) - 0.5, n3 = Hash(x, y, 303) - 0.5;
                    double shot = 0.035 * Math.Sqrt(Math.Max(0.01, lum)) + 0.018;
                    const double chroma = 0.022;
                    double nr = Clamp01(r + n1 * shot + (n2 - n3) * chroma);
                    double ng = Clamp01(g + n2 * shot + (n3 - n1) * chroma);
                    double nb = Clamp01(b + n3 * shot + (n1 - n2) * chroma);

                    bool stamp = false, halo = false;
                    if (x >= dateX0 - 2 && x <= dateX0 + totalW + 2 && y >= dateY0 - 2 && y <= dateY0 + digitH + 2)
                    {
                        int relX = x - dateX0, relY = y - dateY0;
                        int charIdx = FloorDiv(relX, digitW + spacing), charX = relX % (digitW + spacing);
                        if (charIdx >= 0 && charIdx < date.Length && charX < digitW && SevenSegmentPixel(date[charIdx], charX, relY)) stamp = true;
                        // the glow: a lit pixel next door
                        for (int dy = -1; dy <= 1 && !stamp && !halo; dy++)
                            for (int dx = -1; dx <= 1 && !halo; dx++)
                            {
                                int nx = relX + dx, ny = relY + dy;
                                int c = FloorDiv(nx, digitW + spacing), cx = nx % (digitW + spacing);
                                if (c >= 0 && c < date.Length && cx < digitW && cx >= 0 && SevenSegmentPixel(date[c], cx, ny)) halo = true;
                            }
                    }

                    if (stamp)
                    {
                        output[p] = stampR;
                        output[p + 1] = stampG;
                        output[p + 2] = stampB;
                    }
                    else if (halo)
                    {
                        output[p] = (float)Math.Min(1, nr * 0.7 + haloR);
                        output[p + 1] = (float)Math.Min(1, ng * 0.7 + haloG);
                        output[p + 2] = (float)Math.Min(1, nb * 0.7 + haloB);
                    }
                    else
                    {
                        output[p] = (float)nr;
                        output[p + 1] = (float)ng;
                        output[p + 2] = (float)nb;
                    }
                    indices[idx] = (byte)((Colour.LinearToByte(output[p]) >> 3) ^ (Colour.LinearToByte(output[p + 1]) >> 3) ^ (Colour.LinearToByte(output[p + 2]) >> 3));
                }
            });
            return new Result { Indices = indices, Rgb = output, Paper = new byte[] { 255, 255, 255 } };
        }

        // ---- the JPEG deep fry: crushed colours, then 8×8 blocks crunched ----

        // the DCT's cosines, cos((2x + 1)uπ / 16), u a row
        static readonly float[] cosTable = MakeCosTable();

        static float[] MakeCosTable()
        {
            var t = new float[64];
            for (int u = 0; u < 8; u++)
                for (int x = 0; x < 8; x++)
                    t[u * 8 + x] = (float)JsMath.Cos((2 * x + 1) * u * Math.PI / 16);
            return t;
        }

        internal static float[] CosTable => cosTable;

        // steps far coarser than a JPEG's, as if saved over and over
        static readonly float[] jpegQuant =
        {
            32, 24, 28, 48, 64, 80, 112, 128,
            24, 28, 32, 54, 68, 96, 120, 140,
            28, 32, 44, 64, 88, 120, 140, 160,
            48, 54, 64, 80, 112, 144, 160, 180,
            64, 68, 88, 112, 144, 176, 192, 210,
            80, 96, 120, 144, 176, 208, 224, 240,
            112, 120, 140, 160, 192, 224, 240, 255,
            128, 140, 160, 180, 210, 240, 255, 255,
        };

        static Result Fry(float[] lin, int width, int height)
        {
            int n = width * height;
            var output = new float[n * 3];
            var indices = new byte[n];

            // more contrast, and half as much saturation again (and a bit)
            var srgb = new float[n * 3];
            for (int i = 0; i < n; i++)
            {
                double r = Colour.ToSrgb(lin[i * 3]), g = Colour.ToSrgb(lin[i * 3 + 1]), b = Colour.ToSrgb(lin[i * 3 + 2]);
                double l = 0.299 * r + 0.587 * g + 0.114 * b;
                double cl = Math.Min(1, Math.Max(0, (l - 0.45) * 1.55 + 0.45));
                srgb[i * 3] = (float)Math.Min(1, Math.Max(0, cl + (r - l) * 1.6));
                srgb[i * 3 + 1] = (float)Math.Min(1, Math.Max(0, cl + (g - l) * 1.6));
                srgb[i * 3 + 2] = (float)Math.Min(1, Math.Max(0, cl + (b - l) * 1.6));
            }

            // Each 8×8 block of a channel reads only its own pixels (the edge ones repeat the last
            // row and column), so the blocks go on every core and come out the same.
            int blocksX = (width + 7) / 8, blocksY = (height + 7) / 8;
            EveryCore.For(0, 3 * blocksY, () => new BlockWork(), (job, state, work) =>
            {
                int c = job / blocksY, by = job % blocksY * 8;
                for (int bx = 0; bx < blocksX * 8; bx += 8) Block(srgb, width, height, c, bx, by, work);
                return work;
            });

            for (int i = 0; i < n; i++)
            {
                int p = i * 3;
                output[p] = (float)Colour.ToLinear(srgb[p]);
                output[p + 1] = (float)Colour.ToLinear(srgb[p + 1]);
                output[p + 2] = (float)Colour.ToLinear(srgb[p + 2]);
                indices[i] = (byte)((Colour.LinearToByte(output[p]) >> 4) | ((Colour.LinearToByte(output[p + 1]) >> 4) << 4));
            }
            return new Result { Indices = indices, Rgb = output, Paper = new byte[] { 255, 255, 255 } };
        }

        // a thread's room for working on blocks
        sealed class BlockWork
        {
            public readonly float[] Input = new float[64], Dct = new float[64], Back = new float[64];
            public readonly double[] Products = new double[512];
        }

        // One block of one channel: forward DCT, quantised, back. The website's sums term for
        // term, in its order; only the products it makes over and over are made once and kept.
        static void Block(float[] srgb, int width, int height, int c, int bx, int by, BlockWork work)
        {
            float[] input = work.Input, dct = work.Dct, back = work.Back;
            double[] products = work.Products;
            for (int y = 0; y < 8; y++)
            {
                int sy = Math.Min(height - 1, by + y);
                for (int x = 0; x < 8; x++)
                {
                    int sx = Math.Min(width - 1, bx + x);
                    input[y * 8 + x] = (float)(((double)srgb[(sy * width + sx) * 3 + c] - 0.5) * 255);
                }
            }

            // forward: the sum over y and x of (input × cos u) × cos v
            for (int u = 0; u < 8; u++)
            {
                double alphaU = u == 0 ? 0.35355339 : 0.5;   // 1/√8, then √(2/8)
                for (int i = 0; i < 64; i++) products[i] = (double)input[i] * cosTable[u * 8 + (i & 7)];
                for (int v = 0; v < 8; v++)
                {
                    double alphaV = v == 0 ? 0.35355339 : 0.5;
                    double sum = 0;
                    for (int y = 0; y < 8; y++)
                    {
                        double cosV = cosTable[v * 8 + y];
                        for (int x = 0; x < 8; x++) sum += products[y * 8 + x] * cosV;
                    }
                    dct[v * 8 + u] = (float)(alphaU * alphaV * sum);
                }
            }

            for (int k = 0; k < 64; k++)
            {
                double q = jpegQuant[k] * 1.4;
                dct[k] = (float)(JsMath.Round(dct[k] / q) * q);
            }

            // back: the sum over v and u of (αu × αv × coefficient × cos u) × cos v
            for (int x = 0; x < 8; x++)
                for (int v = 0; v < 8; v++)
                {
                    double alphaV = v == 0 ? 0.35355339 : 0.5;
                    for (int u = 0; u < 8; u++)
                    {
                        double alphaU = u == 0 ? 0.35355339 : 0.5;
                        products[x * 64 + v * 8 + u] = alphaU * alphaV * dct[v * 8 + u] * cosTable[u * 8 + x];
                    }
                }
            for (int y = 0; y < 8; y++)
                for (int x = 0; x < 8; x++)
                {
                    double sum = 0;
                    for (int v = 0; v < 8; v++)
                    {
                        double cosV = cosTable[v * 8 + y];
                        for (int u = 0; u < 8; u++) sum += products[x * 64 + v * 8 + u] * cosV;
                    }
                    back[y * 8 + x] = (float)Math.Min(1, Math.Max(0, (sum + 128) / 255));
                }

            for (int y = 0; y < 8 && by + y < height; y++)
                for (int x = 0; x < 8 && bx + x < width; x++)
                    srgb[((by + y) * width + bx + x) * 3 + c] = back[y * 8 + x];
        }

        // ---- the newspaper: CMYK dots at the press's angles on newsprint ----

        // One ink's dot at (x, y) on a screen turned by an angle: how much of the pixel it covers.
        sealed class Screen
        {
            readonly double cos, sin, pitch, maxR;

            public Screen(double angle, double pitch, double maxR)
            {
                cos = JsMath.Cos(angle);
                sin = JsMath.Sin(angle);
                this.pitch = pitch;
                this.maxR = maxR;
            }

            public double Dot(int x, int y, double channel)
            {
                if (channel <= 0.01) return 0;
                if (channel >= 0.99) return 1;
                double u = x * cos + y * sin;
                double v = -(double)x * sin + y * cos;
                double modU = (u % pitch + pitch) % pitch - pitch / 2;
                double modV = (v % pitch + pitch) % pitch - pitch / 2;
                double dist = Math.Sqrt(modU * modU + modV * modV);
                double radius = maxR * Math.Sqrt(channel);
                // a soft edge
                if (dist < radius - 0.5) return 1;
                if (dist > radius + 0.5) return 0;
                return radius + 0.5 - dist;
            }
        }

        static Result Halftone(float[] lin, int width, int height)
        {
            int n = width * height;
            var output = new float[n * 3];
            var indices = new byte[n];
            const double pitch = 5.5, maxR = pitch * 0.7071;
            // the rotary press's angles: cyan 15°, magenta 75°, yellow 0°, black 45°
            var cyan = new Screen(15 * Math.PI / 180, pitch, maxR);
            var magenta = new Screen(75 * Math.PI / 180, pitch, maxR);
            var yellow = new Screen(0, pitch, maxR);
            var black = new Screen(45 * Math.PI / 180, pitch, maxR);
            // unbleached newsprint, #f2ede4
            double paperR = Colour.ByteToLinear[242], paperG = Colour.ByteToLinear[237], paperB = Colour.ByteToLinear[228];

            EveryCore.For(0, height, y =>
            {
                for (int x = 0; x < width; x++)
                {
                    int idx = y * width + x, p = idx * 3;
                    double r = Colour.ToSrgb(lin[p]), g = Colour.ToSrgb(lin[p + 1]), b = Colour.ToSrgb(lin[p + 2]);
                    // CMYK with under colour removal
                    double k = Math.Min(1 - r, Math.Min(1 - g, 1 - b)) * 0.85;
                    double denom = Math.Max(0.001, 1 - k);
                    double c = Math.Min(1, Math.Max(0, (1 - r - k) / denom));
                    double m = Math.Min(1, Math.Max(0, (1 - g - k) / denom));
                    double ye = Math.Min(1, Math.Max(0, (1 - b - k) / denom));
                    double dotC = cyan.Dot(x, y, c), dotM = magenta.Dot(x, y, m), dotY = yellow.Dot(x, y, ye), dotK = black.Dot(x, y, k);
                    // the inks multiply over the paper: cyan takes red, magenta green, yellow blue, black all
                    output[p] = (float)Math.Max(0, Math.Min(1, paperR * (1 - dotC * 0.95) * (1 - dotK * 0.97)));
                    output[p + 1] = (float)Math.Max(0, Math.Min(1, paperG * (1 - dotM * 0.92) * (1 - dotK * 0.97)));
                    output[p + 2] = (float)Math.Max(0, Math.Min(1, paperB * (1 - dotY * 0.90) * (1 - dotK * 0.97)));
                    indices[idx] = (byte)((dotK > 0.5 ? 8 : 0) | (dotC > 0.5 ? 4 : 0) | (dotM > 0.5 ? 2 : 0) | (dotY > 0.5 ? 1 : 0));
                }
            });
            return new Result { Indices = indices, Rgb = output, Paper = new byte[] { 242, 237, 228 } };
        }

        // ---- the risograph: fluorescent pink and teal, the teal drum slipped a little ----

        static Result Riso(float[] lin, int width, int height)
        {
            int n = width * height;
            var output = new float[n * 3];
            var indices = new byte[n];
            // warm off-white paper, #fbf9f4 (the inks: pink #ff48b0, teal #00838a)
            double paperR = Colour.ByteToLinear[251], paperG = Colour.ByteToLinear[249], paperB = Colour.ByteToLinear[244];
            const double pitch = 5.0, maxR = pitch * 0.7071;
            var pinkScreen = new Screen(15 * Math.PI / 180, pitch, maxR);
            var tealScreen = new Screen(75 * Math.PI / 180, pitch, maxR);
            const double jitterX = 1.5, jitterY = 1.0;

            EveryCore.For(0, height, y =>
            {
                for (int x = 0; x < width; x++)
                {
                    int idx = y * width + x, p = idx * 3;
                    double r = Colour.ToSrgb(lin[p]), g = Colour.ToSrgb(lin[p + 1]), b = Colour.ToSrgb(lin[p + 2]);
                    double dark = 1 - (0.2126 * r + 0.7152 * g + 0.0722 * b);
                    // pink takes the reds, magentas and warm tones
                    double warm = Math.Max(0, r - (g + b) * 0.5);
                    double pink = Math.Min(1, Math.Max(0, dark * (0.35 + 0.65 * warm) + 0.25 * (1 - g)));

                    // teal, from where its drum slipped to
                    int tx = (int)Math.Max(0, Math.Min(width - 1, JsMath.Round(x + jitterX)));
                    int ty = (int)Math.Max(0, Math.Min(height - 1, JsMath.Round(y + jitterY)));
                    int tp = (ty * width + tx) * 3;
                    double tr = Colour.ToSrgb(lin[tp]), tg = Colour.ToSrgb(lin[tp + 1]), tb = Colour.ToSrgb(lin[tp + 2]);
                    double tDark = 1 - (0.2126 * tr + 0.7152 * tg + 0.0722 * tb);
                    // teal takes the blues, cyans and cool shadows
                    double cool = Math.Max(0, (tg + tb) * 0.5 - tr);
                    double teal = Math.Min(1, Math.Max(0, tDark * (0.45 + 0.55 * cool) + 0.3 * (1 - tr)));

                    double dotPink = pinkScreen.Dot(x, y, pink), dotTeal = tealScreen.Dot(tx, ty, teal);
                    // the inks overprint: pink takes green, teal takes red, both make a deep violet
                    output[p] = (float)Math.Max(0, Math.Min(1, paperR * (1 - dotTeal * 0.98)));
                    output[p + 1] = (float)Math.Max(0, Math.Min(1, paperG * (1 - dotPink * 0.85) * (1 - dotTeal * 0.25)));
                    output[p + 2] = (float)Math.Max(0, Math.Min(1, paperB * (1 - dotPink * 0.35) * (1 - dotTeal * 0.55)));
                    indices[idx] = (byte)((dotPink > 0.5 ? 1 : 0) | (dotTeal > 0.5 ? 2 : 0));
                }
            });
            return new Result { Indices = indices, Rgb = output, Paper = new byte[] { 251, 249, 244 } };
        }

        // ---- the fax: 1 bit, error diffused, with roller streaks, dropped lines and the header ----

        const string faxHeader = "FROM: B0R3D FAX G3       TEL: +1-800-B0R3D       PAGE: 001/001       2026-10-07 14:20";

        static Result FaxMachine(float[] lin, int width, int height)
        {
            int n = width * height;
            var output = new float[n * 3];
            var indices = new byte[n];
            const int headerH = 20;
            // the dirty roller's streaks, and two lines lost in sending
            int[] streaks = { (int)JsMath.Round(width * 0.12), (int)JsMath.Round(width * 0.28), (int)JsMath.Round(width * 0.65), (int)JsMath.Round(width * 0.84) };
            int dropout1 = (int)JsMath.Round(height * 0.42), dropout2 = (int)JsMath.Round(height * 0.78);

            // a hard curve, then Floyd–Steinberg to 1 bit (a pixel at a time, in order)
            var grey = new float[n];
            for (int i = 0; i < n; i++)
                grey[i] = (float)Math.Min(1, Math.Max(0, JsMath.Pow(Colour.Luminance(lin[i * 3], lin[i * 3 + 1], lin[i * 3 + 2]), 1.25)));

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int idx = y * width + x, p = idx * 3;
                    float ink;
                    if (y < headerH)
                    {
                        // the header: a line of text, then a rule
                        bool lit = y == headerH - 2;
                        if (!lit && y >= 6 && y < 13)
                        {
                            int fontX = x - 12, charIdx = FloorDiv(fontX, 6), px = fontX % 6;
                            lit = charIdx >= 0 && charIdx < faxHeader.Length && px < 5 && Font5x7Pixel(faxHeader[charIdx], px, y - 6);
                        }
                        ink = lit ? 0 : 1;
                    }
                    else if (y == dropout1 || y == dropout2) ink = 1;
                    else if (Array.IndexOf(streaks, x) >= 0 && Hash(x, y, 77) < 0.88) ink = 0;
                    else
                    {
                        double old = grey[idx];
                        int now = old < 0.5 ? 0 : 1;
                        double err = old - now;
                        ink = now;
                        if (x + 1 < width) grey[idx + 1] = (float)(grey[idx + 1] + err * (7.0 / 16));
                        if (y + 1 < height)
                        {
                            if (x > 0) grey[idx + width - 1] = (float)(grey[idx + width - 1] + err * (3.0 / 16));
                            grey[idx + width] = (float)(grey[idx + width] + err * (5.0 / 16));
                            if (x + 1 < width) grey[idx + width + 1] = (float)(grey[idx + width + 1] + err * (1.0 / 16));
                        }
                    }
                    indices[idx] = (byte)(ink == 0 ? 1 : 0);
                    output[p] = output[p + 1] = output[p + 2] = ink;
                }
            }
            return new Result { Indices = indices, Rgb = output, Paper = new byte[] { 255, 255, 255 } };
        }
    }
}
