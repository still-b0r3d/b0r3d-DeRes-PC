using System;
using System.Collections.Generic;
using System.Linq;

namespace B0r3dDeRes.Engine
{
    // The machines that pick their colours cell by cell, with the clash that came with it: the ZX
    // Spectrum (8×8 cells, an ink and a paper each), the C64 in multicolour (4×8 cells, a shared
    // backdrop and three colours each), the NES's 16×16 attribute blocks (four sub-palettes and a
    // backdrop), the Apple II's hi-res bytes (7 pixels, one colour group each), the MSX's Screen 2
    // (two colours per 8×1 slice) and the Game Boy Color's tiles (8 palettes of 4). (The website's
    // core/cell.ts.)
    //
    // Arrays the website writes past the end of are typed arrays in JavaScript, which drop such a
    // write without a word; Put does the same here, so the machine files come out byte for byte.
    static class Cell
    {
        public static readonly string[] SpectrumNormal = { "#000000", "#0000d7", "#d70000", "#d700d7", "#00d700", "#00d7d7", "#d7d700", "#d7d7d7" };
        public static readonly string[] SpectrumBright = { "#000000", "#0000ff", "#ff0000", "#ff00ff", "#00ff00", "#00ffff", "#ffff00", "#ffffff" };

        // the 15 different colours (the two blacks are one)
        public static readonly string[] SpectrumPalette =
        {
            "#000000",
            "#0000d7", "#d70000", "#d700d7", "#00d700", "#00d7d7", "#d7d700", "#d7d7d7",
            "#0000ff", "#ff0000", "#ff00ff", "#00ff00", "#00ffff", "#ffff00", "#ffffff",
        };

        public static readonly string[] C64Palette =
        {
            "#000000", "#ffffff", "#813338", "#75cec8", "#8e3c97", "#56ac4d", "#2e2c9b", "#edf171",
            "#8e5029", "#553800", "#c46c71", "#4a4a4a", "#7b7b7b", "#a9ff9f", "#706deb", "#b2b2b2",
        };

        public static readonly string[] NesPalette =
        {
            "#7c7c7c", "#0000fc", "#0000bc", "#4428bc", "#940084", "#a80020", "#a81000", "#881400",
            "#503000", "#007800", "#006800", "#005800", "#004058", "#000000",
            "#bcbcbc", "#0078f8", "#0058f8", "#6844fc", "#d800cc", "#e40058", "#f83800", "#e45c10",
            "#ac7c00", "#00b800", "#00a800", "#00a844", "#008888",
            "#f8f8f8", "#3cbcfc", "#6888fc", "#9878f8", "#f878f8", "#f85898", "#f87858", "#fca044",
            "#f8b800", "#b8f818", "#58d854", "#58f898", "#00e8d8", "#787878",
            "#fcfcfc", "#a4e4fc", "#b8b8f8", "#d8b8f8", "#f8b8f8", "#f8a4c0", "#f0d0b0", "#fce0a8",
            "#f8d878", "#d8f878", "#b8f8b8", "#b8f8d8", "#00fcfc", "#f8d8f8",
        };

        // black, green, violet, white, then black, orange, blue, white (the high bit's other group)
        public static readonly string[] Apple2HiresPalette = { "#000000", "#14f53c", "#e31e60", "#ffffff", "#000000", "#ff6a3c", "#14cffd", "#ffffff" };

        // the TMS9918's 15 colours (its "transparent" left out)
        public static readonly string[] Tms9918Palette =
        {
            "#000000", "#21c842", "#5edc78", "#5455ed", "#7d76fc", "#d4524d", "#42ebf5", "#fc5554",
            "#ff7978", "#d4c154", "#e6ce80", "#21b03b", "#c95bba", "#cccccc", "#ffffff",
        };

        // what the Game Boy Color's card shows (its real palettes come from each picture)
        public static readonly string[] GbcRepresentativePalette =
        {
            "#000000", "#002080", "#e03020", "#ffffff",
            "#083810", "#387820", "#78b838", "#f8f8b8",
            "#001048", "#1848a0", "#4898e0", "#b8e8f8",
            "#301808", "#784820", "#c89048", "#f8d898",
            "#380800", "#981810", "#f06818", "#f8e038",
            "#181818", "#484850", "#888890", "#d8d8e0",
            "#280838", "#681888", "#b848c8", "#f8a8e8",
            "#281810", "#603828", "#a87850", "#f0e8d8",
        };

        static readonly byte[][] spectrumNormalRgb = SpectrumNormal.Select(Colour.HexToRgb).ToArray();
        static readonly byte[][] spectrumBrightRgb = SpectrumBright.Select(Colour.HexToRgb).ToArray();
        static readonly byte[][] c64Rgb = C64Palette.Select(Colour.HexToRgb).ToArray();
        static readonly byte[][] nesRgb = NesPalette.Select(Colour.HexToRgb).ToArray();
        static readonly byte[][] a2Pal0 = new[] { "#000000", "#14f53c", "#e31e60", "#ffffff" }.Select(Colour.HexToRgb).ToArray();
        static readonly byte[][] a2Pal1 = new[] { "#000000", "#ff6a3c", "#14cffd", "#ffffff" }.Select(Colour.HexToRgb).ToArray();
        static readonly byte[][] tmsRgb = Tms9918Palette.Select(Colour.HexToRgb).ToArray();

        static int DistSq(int r1, int g1, int b1, int r2, int g2, int b2)
        {
            int dr = r1 - r2, dg = g1 - g2, db = b1 - b2;
            return dr * dr + dg * dg + db * db;
        }

        static double DistSq(double r1, double g1, double b1, double r2, double g2, double b2)
        {
            double dr = r1 - r2, dg = g1 - g2, db = b1 - b2;
            return dr * dr + dg * dg + db * db;
        }

        // a typed array's write: one past its end is dropped
        static void Put(byte[] a, int i, int v)
        {
            if ((uint)i < (uint)a.Length) a[i] = (byte)v;
        }

        // the picture as sRGB bytes
        static byte[] Bytes(float[] linear, int n)
        {
            var bytes = new byte[n * 3];
            for (int i = 0; i < n * 3; i++) bytes[i] = (byte)Colour.LinearToByte(linear[i]);
            return bytes;
        }

        static void Show(float[] rgb, int i, byte[] colour)
        {
            rgb[i] = Colour.ByteToLinear[colour[0]];
            rgb[i + 1] = Colour.ByteToLinear[colour[1]];
            rgb[i + 2] = Colour.ByteToLinear[colour[2]];
        }

        public sealed class Result
        {
            public byte[] Indices;      // an entry of the machine's palette a pixel
            public float[] Rgb;         // the picture, linear light
            public byte[] Palette;      // the Game Boy Color's own 32 colours (else null: the machine's list)
            public byte[] ScreenFile;   // the Spectrum's .scr or the C64's .koa, when the machine has one
            public byte[] ColourNumbers;    // the NES: each pixel's colour in its block's sub-palette (what its tiles hold)
        }

        // ---- ZX Spectrum ----

        // 256×192, each 8×8 cell an ink and a paper from the normal or the bright colours.
        public static Result Spectrum(float[] linear, int width, int height)
        {
            var indices = new byte[width * height];
            var output = new float[width * height * 3];
            var attributes = new byte[32 * 24];
            var bitmap = new byte[6144];
            byte[] srgb = Bytes(linear, width * height);
            int cellsX = width / 8, cellsY = height / 8;

            for (int cy = 0; cy < cellsY; cy++)
            {
                for (int cx = 0; cx < cellsX; cx++)
                {
                    int cellIndex = cy * cellsX + cx;
                    int bestBright = 0, bestInk = 0, bestPaper = 7;
                    long minErr = long.MaxValue;
                    for (int bright = 0; bright <= 1; bright++)
                    {
                        byte[][] pal = bright == 1 ? spectrumBrightRgb : spectrumNormalRgb;
                        for (int i = 0; i < 8; i++)
                        {
                            for (int p = i; p < 8; p++)
                            {
                                long err = 0;
                                byte[] ink = pal[i], paper = pal[p];
                                for (int py = 0; py < 8; py++)
                                    for (int px = 0; px < 8; px++)
                                    {
                                        int idx = ((cy * 8 + py) * width + cx * 8 + px) * 3;
                                        int r = srgb[idx], g = srgb[idx + 1], b = srgb[idx + 2];
                                        err += Math.Min(DistSq(r, g, b, ink[0], ink[1], ink[2]), DistSq(r, g, b, paper[0], paper[1], paper[2]));
                                    }
                                if (err < minErr)
                                {
                                    minErr = err;
                                    bestBright = bright;
                                    bestInk = i;
                                    bestPaper = p;
                                }
                            }
                        }
                    }

                    byte[][] chosen = bestBright == 1 ? spectrumBrightRgb : spectrumNormalRgb;
                    byte[] inkRgb = chosen[bestInk], paperRgb = chosen[bestPaper];
                    // the attribute byte: flash, bright, paper (3 bits), ink (3 bits)
                    Put(attributes, cellIndex, (bestBright << 6) | (bestPaper << 3) | bestInk);

                    for (int py = 0; py < 8; py++)
                    {
                        int y = cy * 8 + py, byteVal = 0;
                        for (int px = 0; px < 8; px++)
                        {
                            int x = cx * 8 + px, p = y * width + x, idx = p * 3;
                            int r = srgb[idx], g = srgb[idx + 1], b = srgb[idx + 2];
                            if (DistSq(r, g, b, inkRgb[0], inkRgb[1], inkRgb[2]) <= DistSq(r, g, b, paperRgb[0], paperRgb[1], paperRgb[2]))
                            {
                                byteVal |= 1 << (7 - px);
                                indices[p] = (byte)(bestBright == 1 ? (bestInk == 0 ? 0 : bestInk + 7) : bestInk);
                                Show(output, idx, inkRgb);
                            }
                            else
                            {
                                indices[p] = (byte)(bestBright == 1 ? (bestPaper == 0 ? 0 : bestPaper + 7) : bestPaper);
                                Show(output, idx, paperRgb);
                            }
                        }
                        // the Spectrum's own interleaved screen memory
                        if (cx < 32 && y < 192) bitmap[SpectrumBitmapOffset(cx, y)] = (byte)byteVal;
                    }
                }
            }

            var scr = new byte[6912];
            Buffer.BlockCopy(bitmap, 0, scr, 0, 6144);
            Buffer.BlockCopy(attributes, 0, scr, 6144, 768);
            return new Result { Indices = indices, Rgb = output, ScreenFile = scr };
        }

        // where the byte for cell column cx of screen line y sits in the Spectrum's bitmap
        public static int SpectrumBitmapOffset(int cx, int y) => ((y / 64) << 11) | ((y % 8) << 8) | (((y % 64) / 8) << 5) | cx;

        // ---- Commodore 64 multicolour ----

        // 160×200 wide pixels in 4×8 cells: the shared backdrop (black) and the cell's three most
        // used colours. The file is Koala Painter's: the load address $6000, 8000 bytes of bitmap,
        // 1000 of screen RAM, 1000 of colour RAM and the backdrop.
        public static Result C64Multicolour(float[] linear, int width, int height)
        {
            const int bgCol = 0;
            var indices = new byte[width * height];
            var output = new float[width * height * 3];
            var koala = new byte[10003];
            koala[0] = 0x00;
            koala[1] = 0x60;
            koala[10002] = bgCol;
            byte[] srgb = Bytes(linear, width * height);
            int cellsX = width / 4, cellsY = height / 8;

            for (int cy = 0; cy < cellsY; cy++)
            {
                for (int cx = 0; cx < cellsX; cx++)
                {
                    int cellIdx = cy * cellsX + cx;
                    var counts = new int[16];
                    for (int py = 0; py < 8; py++)
                        for (int px = 0; px < 4; px++)
                        {
                            int idx = ((cy * 8 + py) * width + cx * 4 + px) * 3;
                            int nearest = 0, minD = int.MaxValue;
                            for (int c = 0; c < 16; c++)
                            {
                                int d = DistSq(srgb[idx], srgb[idx + 1], srgb[idx + 2], c64Rgb[c][0], c64Rgb[c][1], c64Rgb[c][2]);
                                if (d < minD)
                                {
                                    minD = d;
                                    nearest = c;
                                }
                            }
                            counts[nearest]++;
                        }

                    // the three most used besides the backdrop (ties keep colour order, as the browser's stable sort)
                    int[] top = Enumerable.Range(0, 16).Where(c => c != bgCol && counts[c] > 0).OrderByDescending(c => counts[c]).ToArray();
                    int c1 = top.Length > 0 ? top[0] : (bgCol == 1 ? 0 : 1);
                    int c2 = top.Length > 1 ? top[1] : (bgCol == 2 ? 0 : 2);
                    int c3 = top.Length > 2 ? top[2] : (bgCol == 7 ? 0 : 7);
                    Put(koala, 8002 + cellIdx, ((c1 & 0x0f) << 4) | (c2 & 0x0f));
                    Put(koala, 9002 + cellIdx, c3 & 0x0f);

                    int[] cellPalette = { bgCol, c1, c2, c3 };
                    for (int py = 0; py < 8; py++)
                    {
                        int y = cy * 8 + py, byteVal = 0;
                        for (int px = 0; px < 4; px++)
                        {
                            int x = cx * 4 + px, p = y * width + x, idx = p * 3;
                            int best = 0, minD = int.MaxValue;
                            for (int k = 0; k < 4; k++)
                            {
                                byte[] c = c64Rgb[cellPalette[k]];
                                int d = DistSq(srgb[idx], srgb[idx + 1], srgb[idx + 2], c[0], c[1], c[2]);
                                if (d < minD)
                                {
                                    minD = d;
                                    best = k;
                                }
                            }
                            byteVal |= (best & 3) << ((3 - px) * 2);
                            indices[p] = (byte)cellPalette[best];
                            Show(output, idx, c64Rgb[cellPalette[best]]);
                        }
                        int bitmapOffset = 2 + cellIdx * 8 + py;
                        if (bitmapOffset < 8002) koala[bitmapOffset] = (byte)byteVal;
                    }
                }
            }
            return new Result { Indices = indices, Rgb = output, ScreenFile = koala };
        }

        // ---- NES attributes ----

        // the NES colour nearest an sRGB colour, skipping any in `skip`
        static int NearestNes(double r, double g, double b, ICollection<int> skip = null)
        {
            int best = 0;
            double bestD = double.PositiveInfinity;
            for (int c = 0; c < nesRgb.Length; c++)
            {
                if (skip != null && skip.Contains(c)) continue;
                double d = DistSq(r, g, b, nesRgb[c][0], nesRgb[c][1], nesRgb[c][2]);
                if (d < bestD)
                {
                    bestD = d;
                    best = c;
                }
            }
            return best;
        }

        static double NesLum(int c) => 0.299 * nesRgb[c][0] + 0.587 * nesRgb[c][1] + 0.114 * nesRgb[c][2];

        static double VectorDist(double[] a, double[] b)
        {
            double s = 0;
            for (int j = 0; j < a.Length; j++)
            {
                double d = a[j] - b[j];
                s += d * d;
            }
            return s;
        }

        // The NES's 4 background sub-palettes, from the picture: the shared backdrop is the colour it
        // uses most, and each 16×16 block's three most used other colours are clustered (k-means)
        // into 4 sets of 3, each snapped to different NES colours.
        static int[][] NesSubPalettes(byte[] srgb, int width, int height)
        {
            var nearest = new byte[width * height];
            var counts = new uint[nesRgb.Length];
            for (int i = 0; i < width * height; i++)
            {
                nearest[i] = (byte)NearestNes(srgb[i * 3], srgb[i * 3 + 1], srgb[i * 3 + 2]);
                counts[nearest[i]]++;
            }
            int backdrop = 0;
            for (int c = 1; c < counts.Length; c++) if (counts[c] > counts[backdrop]) backdrop = c;

            // each block's three most used colours besides the backdrop, darkest first
            var vectors = new List<double[]>();
            int blocksX = (width + 15) / 16, blocksY = (height + 15) / 16;
            for (int by = 0; by < blocksY; by++)
            {
                for (int bx = 0; bx < blocksX; bx++)
                {
                    var local = new Dictionary<int, int>();
                    for (int y = by * 16; y < Math.Min(height, by * 16 + 16); y++)
                        for (int x = bx * 16; x < Math.Min(width, bx * 16 + 16); x++)
                        {
                            int c = nearest[y * width + x];
                            if (c != backdrop) local[c] = local.TryGetValue(c, out int n) ? n + 1 : 1;
                        }
                    if (local.Count == 0) continue;   // the backdrop alone: any sub-palette will do
                    List<int> top = local.OrderByDescending(e => e.Value).ThenBy(e => e.Key).Take(3).Select(e => e.Key).ToList();
                    while (top.Count < 3) top.Add(top[top.Count - 1]);
                    top = top.OrderBy(NesLum).ToList();
                    vectors.Add(top.SelectMany(c => nesRgb[c].Select(v => (double)v)).ToArray());
                }
            }

            const int K = 4;
            var centroids = new List<double[]>();
            if (vectors.Count > 0)
            {
                // start from blocks far apart, so a small colourful area gets its own set
                centroids.Add((double[])vectors[0].Clone());
                while (centroids.Count < K)
                {
                    int far = -1;
                    double farD = 0;
                    for (int v = 0; v < vectors.Count; v++)
                    {
                        double d = centroids.Min(c => VectorDist(vectors[v], c));
                        if (d > farD)
                        {
                            farD = d;
                            far = v;
                        }
                    }
                    if (far < 0) break;
                    centroids.Add((double[])vectors[far].Clone());
                }
                for (int iter = 0; iter < 8; iter++)
                {
                    var sums = centroids.Select(_ => new double[9]).ToList();
                    var n = new int[centroids.Count];
                    foreach (double[] v in vectors)
                    {
                        int k = 0;
                        for (int j = 1; j < centroids.Count; j++) if (VectorDist(v, centroids[j]) < VectorDist(v, centroids[k])) k = j;
                        n[k]++;
                        for (int j = 0; j < 9; j++) sums[k][j] += v[j];
                    }
                    for (int k = 0; k < centroids.Count; k++)
                        if (n[k] > 0)
                        {
                            int count = n[k];
                            centroids[k] = sums[k].Select(s => s / count).ToArray();
                        }
                }
            }

            var subPalettes = new List<int[]>();
            foreach (double[] cent in centroids)
            {
                var pal = new List<int> { backdrop };
                for (int j = 0; j < 3; j++) pal.Add(NearestNes(cent[j * 3], cent[j * 3 + 1], cent[j * 3 + 2], new HashSet<int>(pal)));
                subPalettes.Add(pal.ToArray());
            }
            if (subPalettes.Count == 0) subPalettes.Add(new[] { backdrop, backdrop, backdrop, backdrop });
            while (subPalettes.Count < K) subPalettes.Add((int[])subPalettes[subPalettes.Count - 1].Clone());
            return subPalettes.ToArray();
        }

        // 256×240 in 16×16 blocks, each with one of 4 sub-palettes (3 colours and the shared backdrop).
        public static Result NesAttributes(float[] linear, int width, int height)
        {
            var indices = new byte[width * height];
            var numbers = new byte[width * height];
            var output = new float[width * height * 3];
            byte[] srgb = Bytes(linear, width * height);
            int[][] subPalettes = NesSubPalettes(srgb, width, height);
            int blocksX = (width + 15) / 16, blocksY = (height + 15) / 16;

            for (int by = 0; by < blocksY; by++)
            {
                for (int bx = 0; bx < blocksX; bx++)
                {
                    int bestPal = 0;
                    long minErr = long.MaxValue;
                    for (int p = 0; p < 4; p++)
                    {
                        long err = 0;
                        for (int py = 0; py < 16; py++)
                        {
                            int y = by * 16 + py;
                            if (y >= height) continue;
                            for (int px = 0; px < 16; px++)
                            {
                                int x = bx * 16 + px;
                                if (x >= width) continue;
                                int i = (y * width + x) * 3, dMin = int.MaxValue;
                                for (int c = 0; c < 4; c++)
                                {
                                    byte[] col = nesRgb[subPalettes[p][c]];
                                    int d = DistSq(srgb[i], srgb[i + 1], srgb[i + 2], col[0], col[1], col[2]);
                                    if (d < dMin) dMin = d;
                                }
                                err += dMin;
                            }
                        }
                        if (err < minErr)
                        {
                            minErr = err;
                            bestPal = p;
                        }
                    }

                    int[] active = subPalettes[bestPal];
                    for (int py = 0; py < 16; py++)
                    {
                        int y = by * 16 + py;
                        if (y >= height) continue;
                        for (int px = 0; px < 16; px++)
                        {
                            int x = bx * 16 + px;
                            if (x >= width) continue;
                            int pIdx = y * width + x, i = pIdx * 3, bestC = 0, minD = int.MaxValue;
                            for (int c = 0; c < 4; c++)
                            {
                                byte[] col = nesRgb[active[c]];
                                int d = DistSq(srgb[i], srgb[i + 1], srgb[i + 2], col[0], col[1], col[2]);
                                if (d < minD)
                                {
                                    minD = d;
                                    bestC = c;
                                }
                            }
                            indices[pIdx] = (byte)active[bestC];
                            numbers[pIdx] = (byte)bestC;
                            Show(output, i, nesRgb[active[bestC]]);
                        }
                    }
                }
            }
            return new Result { Indices = indices, Rgb = output, ColourNumbers = numbers };
        }

        // ---- Apple II hi-res ----

        // 280×192, 7 pixels a byte, whose high bit picks green and violet or orange and blue.
        public static Result Apple2Hires(float[] linear, int width, int height)
        {
            var indices = new byte[width * height];
            var output = new float[width * height * 3];
            byte[] srgb = Bytes(linear, width * height);
            int bytesX = width / 7;

            for (int y = 0; y < height; y++)
            {
                for (int bx = 0; bx < bytesX; bx++)
                {
                    int err0 = 0, err1 = 0;
                    for (int px = 0; px < 7; px++)
                    {
                        int x = bx * 7 + px, i = (y * width + x) * 3;
                        int r = srgb[i], g = srgb[i + 1], b = srgb[i + 2];
                        bool odd = x % 2 == 1;
                        byte[] c0 = odd ? a2Pal0[1] : a2Pal0[2];
                        err0 += Math.Min(Math.Min(DistSq(r, g, b, a2Pal0[0][0], a2Pal0[0][1], a2Pal0[0][2]), DistSq(r, g, b, a2Pal0[3][0], a2Pal0[3][1], a2Pal0[3][2])),
                            DistSq(r, g, b, c0[0], c0[1], c0[2]));
                        byte[] c1 = odd ? a2Pal1[1] : a2Pal1[2];
                        err1 += Math.Min(Math.Min(DistSq(r, g, b, a2Pal1[0][0], a2Pal1[0][1], a2Pal1[0][2]), DistSq(r, g, b, a2Pal1[3][0], a2Pal1[3][1], a2Pal1[3][2])),
                            DistSq(r, g, b, c1[0], c1[1], c1[2]));
                    }

                    bool usePal1 = err1 < err0;
                    byte[][] active = usePal1 ? a2Pal1 : a2Pal0;
                    for (int px = 0; px < 7; px++)
                    {
                        int x = bx * 7 + px, pIdx = y * width + x, i = pIdx * 3;
                        int r = srgb[i], g = srgb[i + 1], b = srgb[i + 2];
                        bool odd = x % 2 == 1;
                        byte[] chromatic = odd ? active[1] : active[2];
                        int chromaticIdx = usePal1 ? (odd ? 5 : 6) : (odd ? 1 : 2);
                        int dBlack = DistSq(r, g, b, active[0][0], active[0][1], active[0][2]);
                        int dWhite = DistSq(r, g, b, active[3][0], active[3][1], active[3][2]);
                        int dChrom = DistSq(r, g, b, chromatic[0], chromatic[1], chromatic[2]);
                        int chosen = 0;
                        byte[] colour = active[0];
                        if (dWhite <= dBlack && dWhite <= dChrom)
                        {
                            chosen = usePal1 ? 7 : 3;
                            colour = active[3];
                        }
                        else if (dChrom <= dBlack && dChrom <= dWhite)
                        {
                            chosen = chromaticIdx;
                            colour = chromatic;
                        }
                        indices[pIdx] = (byte)chosen;
                        Show(output, i, colour);
                    }
                }
            }
            return new Result { Indices = indices, Rgb = output };
        }

        // ---- MSX Screen 2 ----

        // 256×192, two of the TMS9918's 15 colours in each 8×1 slice of a line.
        public static Result MsxScreen2(float[] linear, int width, int height)
        {
            var indices = new byte[width * height];
            var output = new float[width * height * 3];
            byte[] srgb = Bytes(linear, width * height);
            int slicesX = width / 8;

            for (int y = 0; y < height; y++)
            {
                for (int sx = 0; sx < slicesX; sx++)
                {
                    int startX = sx * 8, bestC1 = 0, bestC2 = 14, minErr = int.MaxValue;
                    for (int c1 = 0; c1 < 15; c1++)
                    {
                        byte[] k1 = tmsRgb[c1];
                        for (int c2 = c1; c2 < 15; c2++)
                        {
                            byte[] k2 = tmsRgb[c2];
                            int err = 0;
                            for (int px = 0; px < 8; px++)
                            {
                                int i = (y * width + startX + px) * 3;
                                err += Math.Min(DistSq(srgb[i], srgb[i + 1], srgb[i + 2], k1[0], k1[1], k1[2]), DistSq(srgb[i], srgb[i + 1], srgb[i + 2], k2[0], k2[1], k2[2]));
                            }
                            if (err < minErr)
                            {
                                minErr = err;
                                bestC1 = c1;
                                bestC2 = c2;
                            }
                        }
                    }

                    byte[] a = tmsRgb[bestC1], b = tmsRgb[bestC2];
                    for (int px = 0; px < 8; px++)
                    {
                        int pIdx = y * width + startX + px, i = pIdx * 3;
                        bool first = DistSq(srgb[i], srgb[i + 1], srgb[i + 2], a[0], a[1], a[2]) <= DistSq(srgb[i], srgb[i + 1], srgb[i + 2], b[0], b[1], b[2]);
                        indices[pIdx] = (byte)(first ? bestC1 : bestC2);
                        Show(output, i, first ? a : b);
                    }
                }
            }
            return new Result { Indices = indices, Rgb = output };
        }

        // ---- Game Boy Color ----

        // 160×144 in 8×8 tiles, each with one of 8 palettes of 4 colours, all from 15-bit colour:
        // each tile's own four (its pixels' brightness at 4 points) are clustered (k-means) into
        // the 8, then each tile takes the one that suits it.
        public static Result GbcTiles(float[] linear, int width, int height)
        {
            var indices = new byte[width * height];
            var output = new float[width * height * 3];
            var srgb = new byte[width * height * 3];
            for (int i = 0; i < width * height * 3; i++) srgb[i] = (byte)Quantize.SnapToBits(Colour.LinearToByte(linear[i]), 5);
            int tilesX = width / 8, tilesY = height / 8, totalTiles = tilesX * tilesY;
            const int K = 8;
            var paletteBytes = new byte[K * 4 * 3];
            // (a picture too small for one tile is a mistake on the website; here it comes out black)
            if (totalTiles == 0) return new Result { Indices = indices, Rgb = output, Palette = paletteBytes };

            var candidates = new int[totalTiles][];
            var pixels = new (int R, int G, int B, double Lum, int At)[64];
            for (int ty = 0; ty < tilesY; ty++)
            {
                for (int tx = 0; tx < tilesX; tx++)
                {
                    for (int py = 0; py < 8; py++)
                        for (int px = 0; px < 8; px++)
                        {
                            int i = ((ty * 8 + py) * width + tx * 8 + px) * 3;
                            int r = srgb[i], g = srgb[i + 1], b = srgb[i + 2];
                            pixels[py * 8 + px] = (r, g, b, 0.299 * r + 0.587 * g + 0.114 * b, py * 8 + px);
                        }
                    // by brightness, ties in reading order (the browser's sort is stable)
                    Array.Sort(pixels, (a, b) => a.Lum < b.Lum ? -1 : a.Lum > b.Lum ? 1 : a.At - b.At);
                    var c = new int[12];
                    int[] picks = { 4, 20, 40, 59 };
                    for (int k = 0; k < 4; k++)
                    {
                        c[k * 3] = pixels[picks[k]].R;
                        c[k * 3 + 1] = pixels[picks[k]].G;
                        c[k * 3 + 2] = pixels[picks[k]].B;
                    }
                    candidates[ty * tilesX + tx] = c;
                }
            }

            var centroids = new int[K][];
            int step = Math.Max(1, totalTiles / K);
            for (int k = 0; k < K; k++) centroids[k] = (int[])candidates[Math.Min(candidates.Length - 1, k * step)].Clone();
            var assignments = new int[totalTiles];
            for (int iter = 0; iter < 4; iter++)
            {
                for (int t = 0; t < totalTiles; t++)
                {
                    int bestK = 0, minD = int.MaxValue;
                    for (int k = 0; k < K; k++)
                    {
                        int d = 0;
                        for (int j = 0; j < 12; j++)
                        {
                            int diff = candidates[t][j] - centroids[k][j];
                            d += diff * diff;
                        }
                        if (d < minD)
                        {
                            minD = d;
                            bestK = k;
                        }
                    }
                    assignments[t] = bestK;
                }
                var counts = new double[K];
                var sums = new double[K][];
                for (int k = 0; k < K; k++) sums[k] = new double[12];
                for (int t = 0; t < totalTiles; t++)
                {
                    int k = assignments[t];
                    counts[k]++;
                    for (int j = 0; j < 12; j++) sums[k][j] += candidates[t][j];
                }
                for (int k = 0; k < K; k++)
                    if (counts[k] > 0)
                        for (int j = 0; j < 12; j++) centroids[k][j] = Quantize.SnapToBits(JsMath.Round(sums[k][j] / counts[k]), 5);
            }

            var palettes = new byte[K][][];
            for (int k = 0; k < K; k++)
            {
                palettes[k] = new byte[4][];
                for (int c = 0; c < 4; c++)
                {
                    var colour = new[] { (byte)Quantize.SnapToBits(centroids[k][c * 3], 5), (byte)Quantize.SnapToBits(centroids[k][c * 3 + 1], 5), (byte)Quantize.SnapToBits(centroids[k][c * 3 + 2], 5) };
                    palettes[k][c] = colour;
                    Buffer.BlockCopy(colour, 0, paletteBytes, (k * 4 + c) * 3, 3);
                }
            }

            for (int ty = 0; ty < tilesY; ty++)
            {
                for (int tx = 0; tx < tilesX; tx++)
                {
                    int bestK = 0, minTileErr = int.MaxValue;
                    for (int k = 0; k < K; k++)
                    {
                        int err = 0;
                        for (int py = 0; py < 8; py++)
                            for (int px = 0; px < 8; px++)
                            {
                                int i = ((ty * 8 + py) * width + tx * 8 + px) * 3, minD = int.MaxValue;
                                for (int c = 0; c < 4; c++)
                                {
                                    byte[] col = palettes[k][c];
                                    int d = DistSq(srgb[i], srgb[i + 1], srgb[i + 2], col[0], col[1], col[2]);
                                    if (d < minD) minD = d;
                                }
                                err += minD;
                            }
                        if (err < minTileErr)
                        {
                            minTileErr = err;
                            bestK = k;
                        }
                    }

                    byte[][] active = palettes[bestK];
                    for (int py = 0; py < 8; py++)
                        for (int px = 0; px < 8; px++)
                        {
                            int pIdx = (ty * 8 + py) * width + tx * 8 + px, i = pIdx * 3, bestC = 0, minD = int.MaxValue;
                            for (int c = 0; c < 4; c++)
                            {
                                int d = DistSq(srgb[i], srgb[i + 1], srgb[i + 2], active[c][0], active[c][1], active[c][2]);
                                if (d < minD)
                                {
                                    minD = d;
                                    bestC = c;
                                }
                            }
                            indices[pIdx] = (byte)(bestK * 4 + bestC);
                            Show(output, i, active[bestC]);
                        }
                }
            }
            return new Result { Indices = indices, Rgb = output, Palette = paletteBytes };
        }
    }
}
