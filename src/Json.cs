using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text;

namespace B0r3dDeRes
{
    // Numbers written and read the way JavaScript does: the shortest digits that read back as the
    // same double (what JSON.stringify writes), and text read to the nearest double (JSON.parse).
    // Both exact, with whole-number arithmetic, so a share link the PC makes is the website's to
    // the letter, and one the website made reads back to the very same settings. (.NET Framework's
    // own double formatting and parsing can be a last bit out past 15 digits.)
    static class JsNumber
    {
        static readonly double[] powers =
        {
            1e0, 1e1, 1e2, 1e3, 1e4, 1e5, 1e6, 1e7, 1e8, 1e9, 1e10, 1e11, 1e12, 1e13, 1e14, 1e15,
            1e16, 1e17, 1e18, 1e19, 1e20, 1e21, 1e22,
        };

        static int BitLength(BigInteger v)
        {
            if (v.IsZero) return 0;
            byte[] bytes = BigInteger.Abs(v).ToByteArray();
            int top = bytes.Length - 1;
            while (top > 0 && bytes[top] == 0) top--;
            int bits = top * 8, b = bytes[top];
            while (b != 0)
            {
                bits++;
                b >>= 1;
            }
            return bits;
        }

        // JavaScript's x.toFixed(digits) for a finite x under 10^21: the decimal with that many places
        // nearest the double's exact value, the bigger one when two are as near.
        public static string Fixed(double x, int digits)
        {
            bool negative = x < 0;
            long bits = BitConverter.DoubleToInt64Bits(Math.Abs(x));
            int exp = (int)((bits >> 52) & 0x7FF);
            long mant = bits & 0xFFFFFFFFFFFFFL;
            if (exp == 0) exp = 1;
            else mant |= 1L << 52;
            int e = exp - 1075;     // |x| = mant × 2^e
            BigInteger scaled = mant * BigInteger.Pow(10, digits);
            BigInteger n = e >= 0 ? scaled << e : (scaled * 2 + (BigInteger.One << -e)) / (BigInteger.One << (1 - e));
            string text = n.ToString(CultureInfo.InvariantCulture);
            if (digits > 0)
            {
                text = text.PadLeft(digits + 1, '0');
                text = text.Substring(0, text.Length - digits) + "." + text.Substring(text.Length - digits);
            }
            return (negative ? "-" : "") + text;
        }

        // Decimal digits (leading zeros allowed) × 10^exp10, to the nearest double. Past 800
        // significant digits the rest only decide which way to round, so they're one more digit,
        // 1 when any of them isn't 0: no halfway point between two doubles needs more than 767
        // digits, so it rounds the same, and a huge number stays quick to read.
        public static double FromDigits(string digits, int exp10, bool negative)
        {
            string all = digits.TrimStart('0');
            if (all.Length > 800)
            {
                bool sticky = false;
                for (int k = 800; k < all.Length && !sticky; k++) sticky = all[k] != '0';
                exp10 += all.Length - 800;
                all = all.Substring(0, 800);
                if (sticky)
                {
                    all += "1";
                    exp10--;
                }
            }
            return FromDecimal(all.Length == 0 ? BigInteger.Zero : BigInteger.Parse(all, CultureInfo.InvariantCulture), exp10, negative);
        }

        // digits × 10^exp10, to the nearest double (halves to even)
        public static double FromDecimal(BigInteger digits, int exp10, bool negative)
        {
            double sign = negative ? -1 : 1;
            if (digits.IsZero) return sign * 0.0;
            // the usual fast path: both exact as doubles, so one rounding
            if (digits < (BigInteger.One << 53) && Math.Abs(exp10) <= 22)
            {
                double d = (double)(long)digits;
                return sign * (exp10 >= 0 ? d * powers[exp10] : d / powers[-exp10]);
            }
            int length = digits.ToString().Length;
            if (length + exp10 > 330) return sign * double.PositiveInfinity;
            if (length + exp10 < -330) return sign * 0.0;
            BigInteger num = exp10 >= 0 ? digits * BigInteger.Pow(10, exp10) : digits;
            BigInteger den = exp10 >= 0 ? BigInteger.One : BigInteger.Pow(10, -exp10);

            // num / den = q × 2^e with 2^52 <= q < 2^53, plus what's left
            int e = BitLength(num) - BitLength(den) - 53;
            BigInteger q, rem, d2;
            while (true)
            {
                BigInteger n2 = e < 0 ? num << -e : num;
                d2 = e > 0 ? den << e : den;
                q = BigInteger.DivRem(n2, d2, out rem);
                if (q < (BigInteger.One << 52)) e--;
                else if (q >= (BigInteger.One << 53)) e++;
                else break;
            }
            if (e + 52 < -1022)
            {
                // too small for a normal double: in steps of 2^-1074
                e = -1074;
                d2 = den;
                q = BigInteger.DivRem(num << 1074, den, out rem);
            }
            int half = (rem * 2).CompareTo(d2);
            if (half > 0 || (half == 0 && !q.IsEven)) q += 1;
            if (q == (BigInteger.One << 53))
            {
                q >>= 1;
                e++;
            }
            if (e + 52 > 1023) return sign * double.PositiveInfinity;
            long mantissa = (long)q;
            long bits = mantissa < (1L << 52) ? mantissa : ((long)(e + 52 + 1023) << 52) | (mantissa & 0xFFFFFFFFFFFFFL);
            return sign * BitConverter.Int64BitsToDouble(bits);
        }

        // JavaScript's text of a number (String(x), and JSON.stringify's for a finite one).
        public static string Text(double x)
        {
            if (double.IsNaN(x)) return "NaN";
            if (double.IsInfinity(x)) return x > 0 ? "Infinity" : "-Infinity";
            if (x == 0) return "0";
            if (x < 0) return "-" + Text(-x);

            // x exactly, as decimal digits: x = 0.digits × 10^n
            long bits = BitConverter.DoubleToInt64Bits(x);
            int biased = (int)((bits >> 52) & 0x7FF);
            long fraction = bits & 0xFFFFFFFFFFFFFL;
            BigInteger mantissa = biased == 0 ? fraction : fraction | (1L << 52);
            int exp2 = biased == 0 ? -1074 : biased - 1075;
            BigInteger exact = exp2 >= 0 ? mantissa << exp2 : mantissa * BigInteger.Pow(5, -exp2);
            string all = exact.ToString(CultureInfo.InvariantCulture).TrimEnd('0');
            int n = exact.ToString(CultureInfo.InvariantCulture).Length + (exp2 >= 0 ? 0 : exp2);

            // the fewest digits that read back as x; of two that do, the nearer (or the even one)
            string digits = all;
            int point = n;
            for (int k = 1; k < all.Length; k++)
            {
                string down = all.Substring(0, k);
                string up = (BigInteger.Parse(down, CultureInfo.InvariantCulture) + 1).ToString(CultureInfo.InvariantCulture);
                int upPoint = n;
                if (up.Length > k)
                {
                    up = up.Substring(0, k);
                    upPoint = n + 1;
                }
                bool downOk = Reads(down, n, x), upOk = Reads(up, upPoint, x);
                if (!downOk && !upOk) continue;
                bool takeDown;
                if (downOk && upOk)
                {
                    // the rest after k digits against half a step
                    string rest = all.Substring(k);
                    int cmp = (BigInteger.Parse(rest, CultureInfo.InvariantCulture) * 2).CompareTo(BigInteger.Pow(10, rest.Length));
                    takeDown = cmp < 0 || (cmp == 0 && (down[k - 1] - '0') % 2 == 0);
                }
                else takeDown = downOk;
                digits = (takeDown ? down : up).TrimEnd('0');
                point = takeDown ? n : upPoint;
                break;
            }
            return Format(digits, point);
        }

        // whether 0.digits × 10^point reads back as x
        static bool Reads(string digits, int point, double x) =>
            FromDecimal(BigInteger.Parse(digits, CultureInfo.InvariantCulture), point - digits.Length, false) == x;

        // ECMAScript's Number::toString, from the digits and where the point goes
        static string Format(string s, int n)
        {
            int k = s.Length;
            if (k <= n && n <= 21) return s + new string('0', n - k);
            if (0 < n && n <= 21) return s.Substring(0, n) + "." + s.Substring(n);
            if (-6 < n && n <= 0) return "0." + new string('0', -n) + s;
            int e = n - 1;
            string exponent = "e" + (e < 0 ? "-" : "+") + Math.Abs(e).ToString(CultureInfo.InvariantCulture);
            return k == 1 ? s + exponent : s.Substring(0, 1) + "." + s.Substring(1) + exponent;
        }
    }

    // A JSON value read as JSON.parse reads it: objects as dictionaries (a repeated key: the last
    // one wins), arrays as lists, numbers as doubles (JsNumber), and null as Json.Null.
    static class Json
    {
        public sealed class NullValue
        {
            public override string ToString() => "null";
        }

        public static readonly NullValue Null = new NullValue();

        // Objects and arrays deeper than this are read through and checked, not kept (nothing a
        // look holds is more than two deep), so no depth of nesting can use up the stack.
        const int MaxDepth = 64;

        // the value, or throws FormatException as JSON.parse throws
        public static object Parse(string text)
        {
            int i = 0;
            Space(text, ref i);
            object value = Value(text, ref i, 0);
            Space(text, ref i);
            if (i != text.Length) throw new FormatException("Unexpected text after the JSON at " + i);
            return value;
        }

        static void Space(string t, ref int i)
        {
            while (i < t.Length && (t[i] == ' ' || t[i] == '\t' || t[i] == '\n' || t[i] == '\r')) i++;
        }

        static object Value(string t, ref int i, int depth)
        {
            if (i >= t.Length) throw new FormatException("The JSON ends too soon");
            char c = t[i];
            if ((c == '{' || c == '[') && depth >= MaxDepth)
            {
                Skip(t, ref i);
                return Null;
            }
            if (c == '{')
            {
                i++;
                var obj = new Dictionary<string, object>();
                Space(t, ref i);
                if (i < t.Length && t[i] == '}')
                {
                    i++;
                    return obj;
                }
                while (true)
                {
                    string key = Key(t, ref i);
                    Space(t, ref i);
                    obj[key] = Value(t, ref i, depth + 1);
                    Space(t, ref i);
                    if (i < t.Length && t[i] == ',')
                    {
                        i++;
                        continue;
                    }
                    Expect(t, ref i, '}');
                    return obj;
                }
            }
            if (c == '[')
            {
                i++;
                var list = new List<object>();
                Space(t, ref i);
                if (i < t.Length && t[i] == ']')
                {
                    i++;
                    return list;
                }
                while (true)
                {
                    Space(t, ref i);
                    list.Add(Value(t, ref i, depth + 1));
                    Space(t, ref i);
                    if (i < t.Length && t[i] == ',')
                    {
                        i++;
                        continue;
                    }
                    Expect(t, ref i, ']');
                    return list;
                }
            }
            if (c == '"') return Str(t, ref i);
            if (Word(t, ref i, "true")) return true;
            if (Word(t, ref i, "false")) return false;
            if (Word(t, ref i, "null")) return Null;
            return Number(t, ref i);
        }

        // an object's key and its colon
        static string Key(string t, ref int i)
        {
            Space(t, ref i);
            if (i >= t.Length || t[i] != '"') throw new FormatException("A key should be a string at " + i);
            string key = Str(t, ref i);
            Space(t, ref i);
            Expect(t, ref i, ':');
            return key;
        }

        // An object or array read through to its end as strictly as Value reads one, but with a
        // list of what's still open instead of calling itself.
        static void Skip(string t, ref int i)
        {
            var open = new List<char>();   // the closing brackets owed, innermost last
            while (true)
            {
                // a value
                Space(t, ref i);
                if (i >= t.Length) throw new FormatException("The JSON ends too soon");
                char c = t[i];
                if (c == '{' || c == '[')
                {
                    char close = c == '{' ? '}' : ']';
                    i++;
                    Space(t, ref i);
                    if (i < t.Length && t[i] == close) i++;   // empty: a whole value
                    else
                    {
                        open.Add(close);
                        if (close == '}') Key(t, ref i);
                        continue;   // its first value comes next
                    }
                }
                else if (c == '"') Str(t, ref i);
                else if (!Word(t, ref i, "true") && !Word(t, ref i, "false") && !Word(t, ref i, "null")) Number(t, ref i);

                // after a whole value: a comma and the next one, or the end of what holds it
                while (true)
                {
                    if (open.Count == 0) return;
                    Space(t, ref i);
                    char close = open[open.Count - 1];
                    if (i < t.Length && t[i] == ',')
                    {
                        i++;
                        if (close == '}') Key(t, ref i);
                        break;
                    }
                    Expect(t, ref i, close);
                    open.RemoveAt(open.Count - 1);
                }
            }
        }

        static bool Word(string t, ref int i, string word)
        {
            if (string.CompareOrdinal(t, i, word, 0, word.Length) != 0) return false;
            i += word.Length;
            return true;
        }

        static void Expect(string t, ref int i, char c)
        {
            if (i >= t.Length || t[i] != c) throw new FormatException("Expected '" + c + "' at " + i);
            i++;
        }

        static string Str(string t, ref int i)
        {
            Expect(t, ref i, '"');
            var s = new StringBuilder();
            while (true)
            {
                if (i >= t.Length) throw new FormatException("A string doesn't end");
                char c = t[i++];
                if (c == '"') return s.ToString();
                if (c < 0x20) throw new FormatException("A control character in a string");
                if (c != '\\')
                {
                    s.Append(c);
                    continue;
                }
                if (i >= t.Length) throw new FormatException("A string doesn't end");
                char e = t[i++];
                switch (e)
                {
                    case '"': s.Append('"'); break;
                    case '\\': s.Append('\\'); break;
                    case '/': s.Append('/'); break;
                    case 'b': s.Append('\b'); break;
                    case 'f': s.Append('\f'); break;
                    case 'n': s.Append('\n'); break;
                    case 'r': s.Append('\r'); break;
                    case 't': s.Append('\t'); break;
                    case 'u':
                        // four hex digits, each checked (.NET's own hex reading lets odd things by)
                        if (i + 4 > t.Length) throw new FormatException("A bad \\u escape");
                        int code = 0;
                        for (int k = 0; k < 4; k++)
                        {
                            char h = t[i + k];
                            int v = h >= '0' && h <= '9' ? h - '0' : h >= 'a' && h <= 'f' ? h - 'a' + 10 : h >= 'A' && h <= 'F' ? h - 'A' + 10 : -1;
                            if (v < 0) throw new FormatException("A bad \\u escape");
                            code = code * 16 + v;
                        }
                        s.Append((char)code);
                        i += 4;
                        break;
                    default: throw new FormatException("A bad escape");
                }
            }
        }

        static bool Digit(string t, int i) => i < t.Length && t[i] >= '0' && t[i] <= '9';

        // JSON's number: -?(0|[1-9][0-9]*)(\.[0-9]+)?([eE][+-]?[0-9]+)?
        static double Number(string t, ref int i)
        {
            bool negative = false;
            if (i < t.Length && t[i] == '-')
            {
                negative = true;
                i++;
            }
            if (!Digit(t, i)) throw new FormatException("Not a JSON value at " + i);
            var digits = new StringBuilder();
            if (t[i] == '0') i++;
            else while (Digit(t, i)) digits.Append(t[i++]);
            int exp10 = 0;
            if (i < t.Length && t[i] == '.')
            {
                i++;
                if (!Digit(t, i)) throw new FormatException("A number's point needs digits after it");
                while (Digit(t, i))
                {
                    digits.Append(t[i++]);
                    exp10--;
                }
            }
            if (i < t.Length && (t[i] == 'e' || t[i] == 'E'))
            {
                i++;
                bool minus = false;
                if (i < t.Length && (t[i] == '+' || t[i] == '-')) minus = t[i++] == '-';
                if (!Digit(t, i)) throw new FormatException("A number's exponent needs digits");
                long e = 0;
                while (Digit(t, i))
                {
                    e = Math.Min(100000, e * 10 + (t[i++] - '0'));
                }
                exp10 += (int)(minus ? -e : e);
            }
            return JsNumber.FromDigits(digits.ToString(), exp10, negative);
        }

        // JSON.stringify's text of a string
        public static string Quote(string s)
        {
            var b = new StringBuilder("\"");
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': b.Append("\\\""); break;
                    case '\\': b.Append("\\\\"); break;
                    case '\b': b.Append("\\b"); break;
                    case '\f': b.Append("\\f"); break;
                    case '\n': b.Append("\\n"); break;
                    case '\r': b.Append("\\r"); break;
                    case '\t': b.Append("\\t"); break;
                    default:
                        if (c < 0x20) b.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else b.Append(c);
                        break;
                }
            }
            return b.Append('"').ToString();
        }

        // JSON.stringify's text of a number (null when it isn't finite)
        public static string Number(double v) => double.IsNaN(v) || double.IsInfinity(v) ? "null" : JsNumber.Text(v);

        // a value Parse read, written back as JSON.stringify writes it (what was too deep to keep is null)
        public static string Write(object value)
        {
            switch (value)
            {
                case bool b: return b ? "true" : "false";
                case double d: return Number(d);
                case string s: return Quote(s);
                case List<object> list: return "[" + string.Join(",", list.Select(Write)) + "]";
                case Dictionary<string, object> obj: return "{" + string.Join(",", obj.Select(kv => Quote(kv.Key) + ":" + Write(kv.Value))) + "}";
                default: return "null";
            }
        }
    }
}
