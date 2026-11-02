using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace B0r3dDeRes.Engine
{
    // Exports: the picture enlarged by a whole number, nearest neighbour, so every pixel stays a
    // crisp block. It's an indexed PNG holding the machine's exact palette (open it in a pixel-art
    // editor and the palette is there); a look that makes its own colours (the PlayStation, HAM,
    // the N64 filter) holds exactly the colours it uses instead, or goes full colour past 256 of
    // them. Pixels kept see-through stay so: the indexed PNG gets one more palette entry that's
    // see-through (or goes full colour with alpha when the palette already has 256), the
    // full-colour PNG gets alpha. (The website's core/export.ts and core/filename.ts.)
    static class Export
    {
        // the export sizes offered (× the machine's resolution)
        public static readonly int[] Multipliers = { 1, 2, 3, 4, 6, 8, 12, 16 };

        // the size of an export at `multiplier`, stretched to the machine's pixel shape when `tv`
        public static void Size(int width, int height, int parX, int parY, int multiplier, bool tv, out int w, out int h)
        {
            w = tv && parX != parY ? (int)JsMath.Round((double)width * multiplier * parX / parY) : width * multiplier;
            h = height * multiplier;
        }

        // The PNG, written to `output`. TV pixels only stretch when the pixels aren't square.
        public static void WritePng(Stream output, Rendered r, int multiplier, bool tv, out int width, out int height)
        {
            int m = Math.Max(1, multiplier);
            Size(r.Width, r.Height, r.Geometry.ParX, r.Geometry.ParY, m, tv, out width, out height);
            int w = width;
            byte[] alpha = r.Alpha;
            // which picture column each output column shows
            var column = new int[w];
            for (int x = 0; x < w; x++) column[x] = (int)Math.Min(r.Width - 1, Math.Floor((double)x * r.Width / w));
            // the pixels as palette entries: the machine's own palette (every entry, in its order)
            // when its pixels are entries of it, else exactly the colours that show
            byte[] palette = r.Indexed ? r.Palette : Census.Palette(r.Rgb, alpha);
            byte[] indices = r.Indexed ? r.Indices : palette != null ? Census.EntriesIn(palette, r.Rgb, alpha) : null;
            int colours = palette != null ? palette.Length / 3 : 0;

            // full colour: too many colours for a palette, or a full one with no room for a see-through entry
            if (palette == null || colours > 256 || colours == 0 || (alpha != null && colours == 256))
            {
                Engine.Png.WriteTruecolour(output, width, height, alpha != null, (y, row) =>
                {
                    int sy = y / m * r.Width;
                    int channels = alpha != null ? 4 : 3;
                    for (int x = 0; x < w; x++)
                    {
                        int s = sy + column[x], o = x * channels;
                        row[o] = r.Rgb[s * 3];
                        row[o + 1] = r.Rgb[s * 3 + 1];
                        row[o + 2] = r.Rgb[s * 3 + 2];
                        if (alpha != null) row[o + 3] = alpha[s];
                    }
                });
                return;
            }

            // see-through pixels point at one more entry, after the machine's colours
            byte[] entries = alpha != null ? palette.Concat(new byte[3]).ToArray() : palette;
            Engine.Png.WriteIndexed(output, width, height, entries, (y, row) =>
            {
                int sy = y / m * r.Width;
                for (int x = 0; x < w; x++)
                {
                    int s = sy + column[x];
                    row[x] = alpha != null && alpha[s] == 0 ? (byte)colours : indices[s];
                }
            }, alpha != null ? colours : (int?)null);
        }

        public static byte[] PngBytes(Rendered r, int multiplier, bool tv)
        {
            using (var bytes = new MemoryStream())
            {
                WritePng(bytes, r, multiplier, tv, out _, out _);
                return bytes.ToArray();
            }
        }

        // A file name's stem, safe for a file name ("IMG_2041.HEIC" -> "img-2041").
        public static string BaseName(string fileName)
        {
            string stem = Regex.Replace(fileName ?? "", @"\.[^./\\]*$", "");
            // JavaScript lowers a dotted capital I (U+0130) to i and a dot above (which then goes
            // like the other accents), and the Kelvin and Angstrom signs (U+212A, U+212B) to k and
            // a ringed a; .NET Framework leaves those three as they are.
            string clean = stem.Replace(((char)0x130).ToString(), "i" + (char)0x307).Replace((char)0x212A, 'k').Replace((char)0x212B, (char)0xE5)
                .ToLowerInvariant().Normalize(NormalizationForm.FormKD);
            // then the accents go (the combining marks, U+0300 to U+036F)
            clean = new string(clean.Where(c => c < 0x300 || c > 0x36F).ToArray());
            clean = Regex.Replace(clean, "[^a-z0-9]+", "-");
            clean = Regex.Replace(clean, "^-+|-+$", "");
            if (clean.Length > 40) clean = clean.Substring(0, 40);
            clean = Regex.Replace(clean, "-+$", "");
            return clean.Length > 0 ? clean : "photo";
        }

        // what's inside, in the name: photo-nes-256x240-4x.png
        public static string FileName(string baseName, string profileId, int width, int height, int multiplier, bool tv) =>
            baseName + "-" + profileId + "-" + width.ToString(CultureInfo.InvariantCulture) + "x" + height.ToString(CultureInfo.InvariantCulture) + "-" +
            multiplier.ToString(CultureInfo.InvariantCulture) + "x" + (tv ? "-tv" : "") + ".png";

        // a machine's own file: photo-zx-spectrum.scr
        public static string EmulatorFileName(string baseName, string profileId, string format) => baseName + "-" + profileId + "." + format;
    }
}
