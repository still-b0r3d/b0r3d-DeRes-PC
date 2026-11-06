using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace B0r3dDeRes.Exif
{
    // JavaScript's toUpperCase and toLowerCase (Unicode's full mappings: ß to SS, µ to Μ, İ to i
    // and a dot; a sigma that ends a word to ς), from tables made from Node's own (JsCase.Data.cs):
    // .NET Framework's casing is older and maps a character at a time. A lone surrogate stays.
    static partial class JsCase
    {
        static Dictionary<int, string> upper, lower;
        static int[] cased, ignorable;   // ranges: start, end, start, end …

        static Dictionary<int, string> Map(string data)
        {
            var map = new Dictionary<int, string>();
            foreach (string entry in data.Split(','))
            {
                int colon = entry.IndexOf(':');
                int from = int.Parse(entry.Substring(0, colon), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                var to = new StringBuilder();
                foreach (string cp in entry.Substring(colon + 1).Split(' ')) to.Append(char.ConvertFromUtf32(int.Parse(cp, NumberStyles.HexNumber, CultureInfo.InvariantCulture)));
                map[from] = to.ToString();
            }
            return map;
        }

        static int[] Ranges(string data)
        {
            var list = new List<int>();
            foreach (string r in data.Split(','))
            {
                int dash = r.IndexOf('-');
                int a = int.Parse(dash < 0 ? r : r.Substring(0, dash), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                int b = dash < 0 ? a : int.Parse(r.Substring(dash + 1), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                list.Add(a);
                list.Add(b);
            }
            return list.ToArray();
        }

        static readonly object loading = new object();

        static void Load()
        {
            if (upper != null) return;
            lock (loading)
            {
                if (upper != null) return;
                lower = Map(LowerData);
                cased = Ranges(CasedData);
                ignorable = Ranges(IgnorableData);
                upper = Map(UpperData);
            }
        }

        static bool In(int[] ranges, int c)
        {
            int lo = 0, hi = ranges.Length / 2 - 1;
            while (lo <= hi)
            {
                int mid = (lo + hi) / 2;
                if (c < ranges[mid * 2]) hi = mid - 1;
                else if (c > ranges[mid * 2 + 1]) lo = mid + 1;
                else return true;
            }
            return false;
        }

        // the code point at i (a lone surrogate as itself), and how many units it takes
        static int At(string s, int i, out int units)
        {
            char c = s[i];
            if (char.IsHighSurrogate(c) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
            {
                units = 2;
                return char.ConvertToUtf32(c, s[i + 1]);
            }
            units = 1;
            return c;
        }

        // the code point that ends just before i
        static int Before(string s, int i, out int units)
        {
            if (i >= 2 && char.IsLowSurrogate(s[i - 1]) && char.IsHighSurrogate(s[i - 2]))
            {
                units = 2;
                return char.ConvertToUtf32(s[i - 2], s[i - 1]);
            }
            units = 1;
            return s[i - 1];
        }

        public static string Upper(string s)
        {
            if (s.Length == 0) return s;
            Load();
            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length;)
            {
                int c = At(s, i, out int units);
                if (upper.TryGetValue(c, out string to)) sb.Append(to);
                else sb.Append(s, i, units);
                i += units;
            }
            return sb.ToString();
        }

        public static string Lower(string s)
        {
            if (s.Length == 0) return s;
            Load();
            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length;)
            {
                int c = At(s, i, out int units);
                if (c == 0x03A3) sb.Append(FinalSigma(s, i) ? (char)0x03C2 : (char)0x03C3);
                else if (lower.TryGetValue(c, out string to)) sb.Append(to);
                else sb.Append(s, i, units);
                i += units;
            }
            return sb.ToString();
        }

        // Unicode's Final_Sigma: a cased letter before it (case-ignorable ones between), and none after
        static bool FinalSigma(string s, int at)
        {
            bool before = false;
            for (int i = at; i > 0;)
            {
                int c = Before(s, i, out int units);
                i -= units;
                if (In(ignorable, c)) continue;
                before = In(cased, c);
                break;
            }
            if (!before) return false;
            for (int i = at + 1; i < s.Length;)
            {
                int c = At(s, i, out int units);
                i += units;
                if (In(ignorable, c)) continue;
                return !In(cased, c);
            }
            return true;
        }
    }
}
