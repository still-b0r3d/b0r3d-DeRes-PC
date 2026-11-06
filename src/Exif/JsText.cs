using System;
using System.Globalization;
using System.Text;

namespace B0r3dDeRes.Exif
{
    // Text the way the website's browser code makes it, where .NET's own differs: the WHATWG
    // decoders a TextDecoder uses ('utf-8' with replacement or fatal, the 'latin1' label, which is
    // really windows-1252, and UTF-16), each taking off a leading byte-order mark as TextDecoder
    // does; String.prototype.trim's whitespace; Number(text); toFixed and String of a number; and
    // toLocaleString of a whole number as en-US writes it.
    static class JsText
    {
        const char Replacement = (char)0xFFFD;

        // new TextDecoder('utf-8').decode(bytes): bad bytes become U+FFFD, as the standard says
        // (a maximal bad run once); a leading byte-order mark is dropped.
        public static string Utf8(byte[] b, int start, int count) => Utf8(b, start, count, false);

        // new TextDecoder('utf-8', { fatal: true }).decode(bytes), or null where it would throw
        public static string Utf8Fatal(byte[] b, int start, int count) => Utf8(b, start, count, true);

        public static string Utf8(byte[] b) => Utf8(b, 0, b.Length, false);

        static string Utf8(byte[] b, int start, int count, bool fatal)
        {
            int end = start + count;
            if (count >= 3 && b[start] == 0xEF && b[start + 1] == 0xBB && b[start + 2] == 0xBF) start += 3;
            var sb = new StringBuilder(Math.Max(0, end - start));
            int needed = 0, seen = 0, cp = 0, lower = 0x80, upper = 0xBF;
            int i = start;
            while (i < end)
            {
                int x = b[i];
                if (needed == 0)
                {
                    i++;
                    if (x <= 0x7F) sb.Append((char)x);
                    else if (x >= 0xC2 && x <= 0xDF)
                    {
                        needed = 1;
                        cp = x & 0x1F;
                    }
                    else if (x >= 0xE0 && x <= 0xEF)
                    {
                        if (x == 0xE0) lower = 0xA0;
                        if (x == 0xED) upper = 0x9F;
                        needed = 2;
                        cp = x & 0xF;
                    }
                    else if (x >= 0xF0 && x <= 0xF4)
                    {
                        if (x == 0xF0) lower = 0x90;
                        if (x == 0xF4) upper = 0x8F;
                        needed = 3;
                        cp = x & 0x7;
                    }
                    else
                    {
                        if (fatal) return null;
                        sb.Append(Replacement);
                    }
                    continue;
                }
                if (x < lower || x > upper)
                {
                    // the byte starts again (it isn't used up), after a replacement for what came before
                    cp = needed = seen = 0;
                    lower = 0x80;
                    upper = 0xBF;
                    if (fatal) return null;
                    sb.Append(Replacement);
                    continue;
                }
                i++;
                lower = 0x80;
                upper = 0xBF;
                cp = (cp << 6) | (x & 0x3F);
                if (++seen != needed) continue;
                if (cp > 0xFFFF)
                {
                    cp -= 0x10000;
                    sb.Append((char)(0xD800 + (cp >> 10)));
                    sb.Append((char)(0xDC00 + (cp & 0x3FF)));
                }
                else sb.Append((char)cp);
                cp = needed = seen = 0;
            }
            if (needed != 0)
            {
                if (fatal) return null;
                sb.Append(Replacement);
            }
            return sb.ToString();
        }

        // windows-1252 where it differs from Latin-1 (0x80 to 0x9F)
        static readonly ushort[] w1252 =
        {
            0x20AC, 0x0081, 0x201A, 0x0192, 0x201E, 0x2026, 0x2020, 0x2021, 0x02C6, 0x2030, 0x0160, 0x2039, 0x0152, 0x008D, 0x017D, 0x008F,
            0x0090, 0x2018, 0x2019, 0x201C, 0x201D, 0x2022, 0x2013, 0x2014, 0x02DC, 0x2122, 0x0161, 0x203A, 0x0153, 0x009D, 0x017E, 0x0178,
        };

        // new TextDecoder('latin1').decode(bytes): the label means windows-1252
        public static string Latin1(byte[] b, int start, int count)
        {
            var chars = new char[count];
            for (int k = 0; k < count; k++)
            {
                int x = b[start + k];
                chars[k] = x >= 0x80 && x <= 0x9F ? (char)w1252[x - 0x80] : (char)x;
            }
            return new string(chars);
        }

        // new TextDecoder('utf-16le' or 'utf-16be').decode(bytes): a lone surrogate or an odd last
        // byte becomes U+FFFD; a leading byte-order mark is dropped
        public static string Utf16(byte[] b, int start, int count, bool littleEndian)
        {
            int end = start + count;
            if (count >= 2 && (littleEndian ? b[start] == 0xFF && b[start + 1] == 0xFE : b[start] == 0xFE && b[start + 1] == 0xFF)) start += 2;
            var sb = new StringBuilder(Math.Max(0, (end - start) / 2 + 1));
            int lead = -1;
            int i = start;
            for (; i + 1 < end; i += 2)
            {
                int unit = littleEndian ? b[i] | (b[i + 1] << 8) : (b[i] << 8) | b[i + 1];
                if (lead >= 0)
                {
                    if (unit >= 0xDC00 && unit <= 0xDFFF)
                    {
                        sb.Append((char)lead);
                        sb.Append((char)unit);
                        lead = -1;
                        continue;
                    }
                    sb.Append(Replacement);
                    lead = -1;
                }
                if (unit >= 0xD800 && unit <= 0xDBFF) lead = unit;
                else if (unit >= 0xDC00 && unit <= 0xDFFF) sb.Append(Replacement);
                else sb.Append((char)unit);
            }
            if (lead >= 0 || i < end) sb.Append(Replacement);
            return sb.ToString();
        }

        // String.fromCharCode(...bytes): each byte as the character of that number
        public static string Chars(byte[] b, int start, int count)
        {
            var chars = new char[Math.Max(0, count)];
            for (int k = 0; k < count; k++) chars[k] = (char)b[start + k];
            return new string(chars);
        }

        // JavaScript's whitespace and line ends (what trim takes off)
        public static bool IsSpace(char c) =>
            c == 0x09 || c == 0x0B || c == 0x0C || c == 0x20 || c == 0xA0 || c == 0xFEFF || c == 0x1680 || (c >= 0x2000 && c <= 0x200A) ||
            c == 0x202F || c == 0x205F || c == 0x3000 || c == 0x0A || c == 0x0D || c == 0x2028 || c == 0x2029;

        // JavaScript's \s as a .NET regex class, built from the code points (.NET's own \s is
        // another set, and the escapes can't be typed into the source here)
        public static readonly string SpaceClass = BuildSpaceClass();

        static string BuildSpaceClass()
        {
            string Code(int c) => "\\" + "u" + c.ToString("x4", CultureInfo.InvariantCulture);
            var sb = new StringBuilder("[");
            foreach (int c in new[] { 0x09, 0x0B, 0x0C, 0x20, 0xA0, 0xFEFF, 0x1680, 0x202F, 0x205F, 0x3000, 0x0A, 0x0D, 0x2028, 0x2029 }) sb.Append(Code(c));
            sb.Append(Code(0x2000)).Append('-').Append(Code(0x200A));
            return sb.Append(']').ToString();
        }

        public static string Trim(string s)
        {
            int a = 0, z = s.Length;
            while (a < z && IsSpace(s[a])) a++;
            while (z > a && IsSpace(s[z - 1])) z--;
            return a == 0 && z == s.Length ? s : s.Substring(a, z - a);
        }

        public static string TrimEnd(string s)
        {
            int z = s.Length;
            while (z > 0 && IsSpace(s[z - 1])) z--;
            return z == s.Length ? s : s.Substring(0, z);
        }

        // s.replace(/\0+$/, ''): the zeros at the end off
        public static string NoTrailingZeros(string s)
        {
            int z = s.Length;
            while (z > 0 && s[z - 1] == '\0') z--;
            return z == s.Length ? s : s.Substring(0, z);
        }

        // String(x) of a number
        public static string Num(double x) => JsNumber.Text(x);

        // x.toFixed(digits)
        public static string Fixed(double x, int digits)
        {
            if (double.IsNaN(x)) return "NaN";
            if (Math.Abs(x) >= 1e21 || double.IsInfinity(x)) return JsNumber.Text(x);
            return JsNumber.Fixed(x, digits);
        }

        // Number(x.toFixed(digits))
        public static double FixedNumber(double x, int digits) => ToNumber(Fixed(x, digits));

        // n.toLocaleString() of a whole number, as en-US writes it ("1,234,567")
        public static string Grouped(double n)
        {
            if (double.IsNaN(n)) return "NaN";
            if (double.IsInfinity(n)) return n > 0 ? "∞" : "-∞";
            if (n == Math.Floor(n) && Math.Abs(n) < 1e15) return ((long)n).ToString("#,0", CultureInfo.InvariantCulture);
            // (only whole numbers are written this way here; others as en-US would, to 3 places)
            return n.ToString("#,0.###", CultureInfo.InvariantCulture);
        }

        // Number(text): JavaScript's reading of a string as a number (NaN when it isn't one)
        public static double ToNumber(string text)
        {
            string s = Trim(text);
            if (s.Length == 0) return 0;
            if (s.Length > 2 && s[0] == '0')
            {
                char p = (char)(s[1] | 0x20);
                int radix = p == 'x' ? 16 : p == 'o' ? 8 : p == 'b' ? 2 : 0;
                if (radix != 0)
                {
                    double v = 0;
                    for (int k = 2; k < s.Length; k++)
                    {
                        int d = Digit(s[k]);
                        if (d < 0 || d >= radix) return double.NaN;
                        v = v * radix + d;
                    }
                    return v;
                }
            }
            bool negative = false;
            int i = 0;
            if (s[0] == '+' || s[0] == '-')
            {
                negative = s[0] == '-';
                i = 1;
            }
            if (string.CompareOrdinal(s, i, "Infinity", 0, 8) == 0 && s.Length == i + 8) return negative ? double.NegativeInfinity : double.PositiveInfinity;
            var digits = new StringBuilder();
            int exp10 = 0, intDigits = 0, fracDigits = 0;
            while (i < s.Length && s[i] >= '0' && s[i] <= '9')
            {
                digits.Append(s[i++]);
                intDigits++;
            }
            if (i < s.Length && s[i] == '.')
            {
                i++;
                while (i < s.Length && s[i] >= '0' && s[i] <= '9')
                {
                    digits.Append(s[i++]);
                    fracDigits++;
                }
            }
            if (intDigits == 0 && fracDigits == 0) return double.NaN;
            exp10 -= fracDigits;
            if (i < s.Length && (s[i] == 'e' || s[i] == 'E'))
            {
                i++;
                bool expNegative = false;
                if (i < s.Length && (s[i] == '+' || s[i] == '-'))
                {
                    expNegative = s[i] == '-';
                    i++;
                }
                int start = i;
                long e = 0;
                while (i < s.Length && s[i] >= '0' && s[i] <= '9')
                {
                    if (e < 100000) e = e * 10 + (s[i] - '0');
                    i++;
                }
                if (i == start) return double.NaN;
                exp10 += (int)(expNegative ? -e : e);
            }
            if (i != s.Length) return double.NaN;
            return JsNumber.FromDigits(digits.ToString(), exp10, negative);
        }

        static int Digit(char c)
        {
            if (c >= '0' && c <= '9') return c - '0';
            if (c >= 'a' && c <= 'z') return c - 'a' + 10;
            if (c >= 'A' && c <= 'Z') return c - 'A' + 10;
            return -1;
        }

        // String(n).padStart(width, '0') for the website's pads
        public static string Pad(string s, int width, char with = '0') => s.Length >= width ? s : new string(with, width - s.Length) + s;

        // n.toString(16).padStart(width, '0') of a whole number
        public static string Hex(long n, int width) => Pad(n.ToString("x", CultureInfo.InvariantCulture), width);

        // text.toLowerCase() / toUpperCase(), as JavaScript makes them (JsCase)
        // String.prototype.toWellFormed: a lone surrogate becomes U+FFFD (as a browser hands over a
        // file's name, and as UTF-8 writes it)
        public static string WellFormed(string s)
        {
            char[] c = null;
            for (int i = 0; i < s.Length; i++)
            {
                if (char.IsHighSurrogate(s[i]) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
                {
                    i++;
                    continue;
                }
                if (char.IsSurrogate(s[i])) (c ?? (c = s.ToCharArray()))[i] = (char)0xFFFD;
            }
            return c == null ? s : new string(c);
        }

        public static string Lower(string s) => JsCase.Lower(s);

        public static string Upper(string s) => JsCase.Upper(s);
    }
}
