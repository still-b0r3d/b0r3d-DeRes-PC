using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace B0r3dDeRes.Engine
{
    // Exports: the picture enlarged by a whole number, nearest neighbour, so every pixel stays a
    // crisp block, as an indexed PNG holding the machine's exact palette (open it in a pixel-art
    // editor and the palette is there). (The website's core/export.ts and core/filename.ts.)
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
            // which picture column each output column shows
            var column = new int[w];
            for (int x = 0; x < w; x++) column[x] = (int)Math.Min(r.Width - 1, Math.Floor((double)x * r.Width / w));
            Engine.Png.WriteIndexed(output, width, height, r.Palette, (y, row) =>
            {
                int sy = y / m * r.Width;
                for (int x = 0; x < w; x++) row[x] = r.Indices[sy + column[x]];
            });
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
    }
}
