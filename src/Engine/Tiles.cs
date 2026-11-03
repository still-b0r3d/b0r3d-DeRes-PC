using System;
using System.Collections.Generic;
using System.Globalization;

namespace B0r3dDeRes.Engine
{
    // "Would it fit?": how many different 8×8 tiles and colours a picture has, against what the
    // machine could hold.
    sealed class HardwareFitReport
    {
        public int TotalTiles, UniqueTiles, UniqueColours;
        public int? MaxTiles, MaxColours;
        public bool TilesFit, ColoursFit;
        public double Ratio;    // of the tile limit used (0 without one)
        public string Summary;  // "98 / 256 tiles (fits in VRAM)"
    }

    // The tile meter, and the Game Boy and NES tile files (.chr). (The website's core/tiles.ts.)
    static class Tiles
    {
        // Cuts the picture into 8×8 tiles and counts the different ones. Pixels `alpha` hides count
        // as no colour, and as the same in every tile.
        public static HardwareFitReport Fit(byte[] indices, int width, int height, int? maxTiles, int? maxColours, byte[] alpha)
        {
            int tilesX = width / 8, tilesY = height / 8;
            var tiles = new HashSet<string>();
            var colours = new HashSet<byte>();
            var key = new char[64];
            for (int ty = 0; ty < tilesY; ty++)
            {
                for (int tx = 0; tx < tilesX; tx++)
                {
                    for (int py = 0; py < 8; py++)
                    {
                        int rowStart = (ty * 8 + py) * width + tx * 8;
                        for (int px = 0; px < 8; px++)
                        {
                            if (alpha != null && alpha[rowStart + px] == 0)
                            {
                                key[py * 8 + px] = (char)0x100;   // hidden: not a colour
                                continue;
                            }
                            byte v = indices[rowStart + px];
                            colours.Add(v);
                            key[py * 8 + px] = (char)v;
                        }
                    }
                    tiles.Add(new string(key));
                }
            }

            int unique = tiles.Count;
            var report = new HardwareFitReport
            {
                TotalTiles = tilesX * tilesY,
                UniqueTiles = unique,
                UniqueColours = colours.Count,
                MaxTiles = maxTiles,
                MaxColours = maxColours,
                TilesFit = maxTiles == null || unique <= maxTiles,
                ColoursFit = maxColours == null || colours.Count <= maxColours,
                Ratio = maxTiles > 0 ? (double)unique / maxTiles.Value : 0,
            };
            string u = unique.ToString(CultureInfo.InvariantCulture);
            if (maxTiles > 0)
            {
                string m = maxTiles.Value.ToString(CultureInfo.InvariantCulture);
                report.Summary = report.TilesFit ? u + " / " + m + " tiles (fits in VRAM)" : u + " / " + m + " tiles (+" + (unique - maxTiles.Value).ToString(CultureInfo.InvariantCulture) + " over limit)";
            }
            else report.Summary = u + " unique 8×8 tiles";
            return report;
        }

        // An 8×8 tile of 2-bit colours as the Game Boy's 16 bytes: each row a byte of low bits,
        // then a byte of high bits, the leftmost pixel in bit 7.
        public static byte[] GbTile(byte[] tile)
        {
            var output = new byte[16];
            for (int row = 0; row < 8; row++)
            {
                int low = 0, high = 0;
                for (int col = 0; col < 8; col++)
                {
                    int v = tile[row * 8 + col] & 3, bit = 7 - col;
                    if ((v & 1) != 0) low |= 1 << bit;
                    if ((v & 2) != 0) high |= 1 << bit;
                }
                output[row * 2] = (byte)low;
                output[row * 2 + 1] = (byte)high;
            }
            return output;
        }

        // A picture's different 8×8 tiles, in reading order, as a Game Boy tile sheet.
        public static byte[] GbChr(byte[] indices, int width, int height)
        {
            int tilesX = width / 8, tilesY = height / 8;
            var seen = new HashSet<string>();
            var output = new List<byte>();
            var tile = new byte[64];
            var key = new char[64];
            for (int ty = 0; ty < tilesY; ty++)
            {
                for (int tx = 0; tx < tilesX; tx++)
                {
                    for (int py = 0; py < 8; py++)
                    {
                        int rowStart = (ty * 8 + py) * width + tx * 8;
                        for (int px = 0; px < 8; px++)
                        {
                            int v = indices[rowStart + px] & 3;
                            tile[py * 8 + px] = (byte)v;
                            key[py * 8 + px] = (char)('0' + v);
                        }
                    }
                    if (seen.Add(new string(key))) output.AddRange(GbTile(tile));
                }
            }
            return output.ToArray();
        }

        // A picture's different 8×8 tiles as a NES pattern table: each pixel is its 2-bit colour
        // number in its tile's sub-palette (0 the backdrop); a tile is 8 bytes of low bits (a row
        // each), then 8 of high bits, the leftmost pixel in bit 7.
        public static byte[] NesChr(byte[] numbers, int width, int height)
        {
            int tilesX = width / 8, tilesY = height / 8;
            var seen = new HashSet<string>();
            var output = new List<byte>();
            for (int ty = 0; ty < tilesY; ty++)
            {
                for (int tx = 0; tx < tilesX; tx++)
                {
                    var tile = new byte[16];
                    for (int row = 0; row < 8; row++)
                    {
                        int lo = 0, hi = 0;
                        for (int col = 0; col < 8; col++)
                        {
                            int v = numbers[(ty * 8 + row) * width + tx * 8 + col] & 3;
                            if ((v & 1) != 0) lo |= 0x80 >> col;
                            if ((v & 2) != 0) hi |= 0x80 >> col;
                        }
                        tile[row] = (byte)lo;
                        tile[row + 8] = (byte)hi;
                    }
                    if (seen.Add(Convert.ToBase64String(tile))) output.AddRange(tile);
                }
            }
            return output.ToArray();
        }
    }

    // The exact colours of a picture while they fit a palette: a file holding just those is
    // indexed and still shows the very pixels (the looks that make their own colours: HAM, the
    // N64 filter, the PlayStation). (The website's core/census.ts.)
    static class Census
    {
        // r, g, b a colour in the order first seen, of the pixels that show; null with more than 256 (or none)
        public static byte[] Palette(byte[] rgb, byte[] alpha)
        {
            var seen = new HashSet<int>();
            var order = new List<int>();
            for (int i = 0, p = 0; i < rgb.Length; i += 3, p++)
            {
                if (alpha != null && alpha[p] == 0) continue;
                int c = (rgb[i] << 16) | (rgb[i + 1] << 8) | rgb[i + 2];
                if (!seen.Add(c)) continue;
                order.Add(c);
                if (order.Count > 256) return null;
            }
            if (order.Count == 0) return null;
            var palette = new byte[order.Count * 3];
            for (int k = 0; k < order.Count; k++)
            {
                palette[k * 3] = (byte)(order[k] >> 16);
                palette[k * 3 + 1] = (byte)(order[k] >> 8);
                palette[k * 3 + 2] = (byte)order[k];
            }
            return palette;
        }

        // each pixel's entry in a palette holding every colour that shows; hidden pixels get entry 0
        // (the website's entriesIn)
        public static byte[] EntriesIn(byte[] palette, byte[] rgb, byte[] alpha)
        {
            var entry = new Dictionary<int, int>();
            for (int k = 0; k < palette.Length / 3; k++) entry[(palette[k * 3] << 16) | (palette[k * 3 + 1] << 8) | palette[k * 3 + 2]] = k;
            var output = new byte[rgb.Length / 3];
            for (int i = 0, p = 0; i < rgb.Length; i += 3, p++)
            {
                if (alpha != null && alpha[p] == 0) continue;
                if (!entry.TryGetValue((rgb[i] << 16) | (rgb[i + 1] << 8) | rgb[i + 2], out int k)) throw new InvalidOperationException("A colour is missing from the palette");
                output[p] = (byte)k;
            }
            return output;
        }
    }

    // The exact colours in a set of frames while there are no more than 256: a file holding exactly
    // those is indexed and still shows the very pixels (the looks that make their own colours, in an
    // animation). (The website's core/census.ts.)
    sealed class ColourCensus
    {
        readonly HashSet<int> seen = new HashSet<int>();
        readonly List<int> order = new List<int>();
        bool over;

        // a frame's pixels, r, g, b each; with `alpha`, only the ones that show
        public void Add(byte[] rgb, byte[] alpha)
        {
            if (over) return;
            int last = -1;
            for (long i = 0, p = 0; i < rgb.LongLength; i += 3, p++)
            {
                if (alpha != null && alpha[p] == 0) continue;
                int c = (rgb[i] << 16) | (rgb[i + 1] << 8) | rgb[i + 2];
                // (made pictures come in runs of one colour)
                if (c == last) continue;
                last = c;
                if (!seen.Add(c)) continue;
                order.Add(c);
                if (seen.Count > 256)
                {
                    over = true;
                    seen.Clear();
                    order.Clear();
                    return;
                }
            }
        }

        // r, g, b a colour, in the order first seen; null when there are more than 256 (or none)
        public byte[] Palette()
        {
            if (over || order.Count == 0) return null;
            var output = new byte[order.Count * 3];
            for (int k = 0; k < order.Count; k++)
            {
                output[k * 3] = (byte)(order[k] >> 16);
                output[k * 3 + 1] = (byte)(order[k] >> 8);
                output[k * 3 + 2] = (byte)order[k];
            }
            return output;
        }
    }
}
