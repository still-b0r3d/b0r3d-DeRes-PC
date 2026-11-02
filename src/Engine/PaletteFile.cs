using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace B0r3dDeRes.Engine
{
    // Palette files: .hex, JASC .pal and GIMP .gpl read and written, and palette PNGs (every
    // different colour in one). At most 256 colours. (The website's core/palette-file.ts, read the
    // same way down to JavaScript's parseInt and how its colour text handles odd numbers.)
    static class PaletteFile
    {
        public const int MaxColours = 256;

        // JavaScript's white space (String.prototype.trim and \s), by code (written as characters,
        // a no-break space and the rest look like any other space)
        static bool JsSpace(char c) => c == '\t' || c == '\n' || c == '\v' || c == '\f' || c == '\r' || c == ' ' || c == (char)0xa0 || c == (char)0x1680 ||
            (c >= (char)0x2000 && c <= (char)0x200a) || c == (char)0x2028 || c == (char)0x2029 || c == (char)0x202f || c == (char)0x205f || c == (char)0x3000 || c == (char)0xfeff;

        static string JsTrim(string s)
        {
            int a = 0, b = s.Length;
            while (a < b && JsSpace(s[a])) a++;
            while (b > a && JsSpace(s[b - 1])) b--;
            return s.Substring(a, b - a);
        }

        // parseInt(s, 10): white space, a sign, then as many digits as there are (NaN with none)
        static double ParseInt(string s)
        {
            int i = 0;
            while (i < s.Length && JsSpace(s[i])) i++;
            bool negative = false;
            if (i < s.Length && (s[i] == '+' || s[i] == '-')) negative = s[i++] == '-';
            int start = i;
            while (i < s.Length && s[i] >= '0' && s[i] <= '9') i++;
            if (i == start) return double.NaN;
            // to the nearest double, as JavaScript reads it (a number too big for one is Infinity)
            return JsNumber.FromDigits(s.Substring(start, i - start), 0, negative);
        }

        // JavaScript's toUpperCase as far as the headers need: it also turns the dotless i and the
        // long s into I and S, which .NET leaves as they are
        static string JsUpper(string s) => new string(s.Select(c => c == (char)0x131 ? 'I' : c == (char)0x17f ? 'S' : char.ToUpperInvariant(c)).ToArray());

        // JavaScript's ToInt32: whole, wrapped round 2^32
        static int ToInt32(double d)
        {
            if (double.IsNaN(d) || double.IsInfinity(d)) return 0;
            double m = Math.Truncate(d) % 4294967296.0;
            if (m < 0) m += 4294967296.0;
            return (int)(uint)m;
        }

        // the website's rgbToHex, for any numbers: out-of-range ones wrap (or come out as a broken colour)
        static string JsRgbToHex(double r, double g, double b)
        {
            int v = (1 << 24) | (ToInt32(r) << 16) | (ToInt32(g) << 8) | ToInt32(b);
            string hex = v < 0 ? "-" + (-(long)v).ToString("x", CultureInfo.InvariantCulture) : v.ToString("x", CultureInfo.InvariantCulture);
            return "#" + hex.Substring(1);
        }

        // ("\z", not "$": .NET's "$" also matches before a last line end, JavaScript's doesn't)
        static readonly Regex colour = new Regex("^#[0-9a-f]{6}\\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        public static bool IsColour(string s) => s != null && colour.IsMatch(s);

        // A palette text file (.hex, JASC-PAL or GIMP .gpl) as "#rrggbb" colours. (Colours the
        // website would read as broken text are left out: it would stop at them.)
        public static string[] ParseText(string text)
        {
            string[] lines = Regex.Split(text, "\r?\n").Select(JsTrim).Where(l => l.Length > 0).ToArray();
            if (lines.Length == 0) return new string[0];
            var output = new List<string>();

            if (JsUpper(lines[0]) == "JASC-PAL")
            {
                // JASC-PAL, 0100, the count, then a line of r g b each
                double declared = lines.Length > 2 ? ParseInt(lines[2]) : double.NaN;
                double count = double.IsNaN(declared) || declared == 0 ? lines.Length - 3 : declared;
                for (int i = 3; i < lines.Length && output.Count < Math.Min(256, count); i++)
                {
                    double[] parts = SplitJsSpace(lines[i]).Select(ParseInt).ToArray();
                    if (parts.Length >= 3 && !parts.Take(3).Any(double.IsNaN)) output.Add(JsRgbToHex(parts[0], parts[1], parts[2]));
                }
            }
            else if (JsUpper(lines[0]).StartsWith("GIMP PALETTE", StringComparison.Ordinal))
            {
                for (int i = 1; i < lines.Length; i++)
                {
                    string line = lines[i];
                    if (line.StartsWith("#", StringComparison.Ordinal) || line.StartsWith("Name:", StringComparison.Ordinal) || line.StartsWith("Columns:", StringComparison.Ordinal)) continue;
                    Match m = gimpLine.Match(line);
                    if (!m.Success) continue;
                    output.Add(JsRgbToHex(ParseInt(m.Groups[1].Value), ParseInt(m.Groups[2].Value), ParseInt(m.Groups[3].Value)));
                    if (output.Count >= 256) break;
                }
            }
            else
            {
                // .hex: a colour a line ("00ffff", "#00ffff" or "0ff")
                foreach (string line in lines)
                {
                    if (line.StartsWith(";", StringComparison.Ordinal) || line.StartsWith("//", StringComparison.Ordinal)) continue;
                    string cleaned = JsTrim(line.StartsWith("#", StringComparison.Ordinal) ? line.Substring(1) : line);
                    if (Regex.IsMatch(cleaned, "^[0-9a-fA-F]{6}\\z")) output.Add("#" + cleaned.ToLowerInvariant());
                    else if (Regex.IsMatch(cleaned, "^[0-9a-fA-F]{3}\\z"))
                        output.Add(("#" + cleaned[0] + cleaned[0] + cleaned[1] + cleaned[1] + cleaned[2] + cleaned[2]).ToLowerInvariant());
                    if (output.Count >= 256) break;
                }
            }
            return output.Where(IsColour).ToArray();
        }

        // split on runs of JavaScript white space (as "a b".split(/\s+/))
        static IEnumerable<string> SplitJsSpace(string s)
        {
            var part = new StringBuilder();
            var parts = new List<string>();
            foreach (char c in s)
            {
                if (JsSpace(c))
                {
                    if (part.Length > 0 || parts.Count == 0)
                    {
                        parts.Add(part.ToString());
                        part.Clear();
                    }
                    continue;
                }
                part.Append(c);
            }
            parts.Add(part.ToString());
            // (a leading run gives an empty first part, as in JavaScript; the lines are trimmed, so it never does)
            return parts;
        }

        // JavaScript's \s and \d spelled out (.NET's take in more of Unicode)
        const string JsSpaces = "[\\t\\n\\v\\f\\r \\u00a0\\u1680\\u2000-\\u200a\\u2028\\u2029\\u202f\\u205f\\u3000\\ufeff]";
        static readonly Regex gimpLine = new Regex("^([0-9]+)" + JsSpaces + "+([0-9]+)" + JsSpaces + "+([0-9]+)", RegexOptions.CultureInvariant);

        // A palette PNG's different colours (see-through or not), in reading order, at most 256.
        public static string[] FromPixels(byte[] bgra, int width, int height)
        {
            var seen = new HashSet<string>();
            var output = new List<string>();
            for (long i = 0, n = (long)width * height; i < n; i++)
            {
                string hex = Colour.RgbToHex(bgra[i * 4 + 2], bgra[i * 4 + 1], bgra[i * 4]);
                if (seen.Add(hex))
                {
                    output.Add(hex);
                    if (output.Count >= 256) break;
                }
            }
            return output.ToArray();
        }

        // ---- writing ----

        // .hex: a "#rrggbb" a line
        public static string Hex(IEnumerable<string> colours) => string.Join("\n", colours.Select(c => c.ToLowerInvariant())) + "\n";

        // JASC-PAL, with Windows line ends
        public static string Jasc(IList<string> colours)
        {
            var lines = new List<string> { "JASC-PAL", "0100", colours.Count.ToString(CultureInfo.InvariantCulture) };
            lines.AddRange(colours.Select(c =>
            {
                byte[] rgb = Colour.HexToRgb(c);
                return rgb[0] + " " + rgb[1] + " " + rgb[2];
            }));
            return string.Join("\r\n", lines) + "\r\n";
        }

        // a GIMP palette, named
        public static string Gpl(IEnumerable<string> colours, string name = "b0r3d DeRes")
        {
            var lines = new List<string> { "GIMP Palette", "Name: " + name, "Columns: 8", "#" };
            lines.AddRange(colours.Select(c =>
            {
                byte[] rgb = Colour.HexToRgb(c);
                return rgb[0].ToString(CultureInfo.InvariantCulture).PadLeft(3) + " " + rgb[1].ToString(CultureInfo.InvariantCulture).PadLeft(3) + " " +
                    rgb[2].ToString(CultureInfo.InvariantCulture).PadLeft(3) + "\t" + c;
            }));
            return string.Join("\n", lines) + "\n";
        }

        // a 1×N indexed PNG holding the colours in order
        public static byte[] Png(IList<string> colours)
        {
            int n = Math.Min(256, Math.Max(1, colours.Count));
            var palette = new byte[n * 3];
            for (int i = 0; i < n; i++) Buffer.BlockCopy(Colour.HexToRgb(colours[i]), 0, palette, i * 3, 3);
            using (var bytes = new MemoryStream())
            {
                Engine.Png.WriteIndexed(bytes, n, 1, palette, (y, row) =>
                {
                    for (int x = 0; x < n; x++) row[x] = (byte)x;
                });
                return bytes.ToArray();
            }
        }
    }
}
