using System;
using System.Text;

namespace B0r3dDeRes
{
    // Tells an sRGB colour profile from the rest. Most photos and pictures carry one (phones,
    // cameras and editors add it); browsers see that it's sRGB, which is what they show anyway, and
    // leave the pixels alone. Windows' own conversion isn't quite exact even then (a level or two
    // off here and there), so b0r3d DeRes does as browsers do: only other profiles (Display P3,
    // Adobe RGB and the like) are applied.
    static class ColourProfile
    {
        // sRGB's red, green and blue as ICC profiles hold them (D50), and how near counts
        static readonly double[][] Primaries =
        {
            new[] { 0.4361, 0.2225, 0.0139 },
            new[] { 0.3851, 0.7169, 0.0971 },
            new[] { 0.1431, 0.0606, 0.7141 },
        };
        const double NearPrimary = 0.0025, NearCurve = 0.002;

        // Is it an RGB matrix profile with sRGB's primaries and tone curve? False for anything
        // unreadable or different, which then goes through Windows' conversion.
        public static bool IsSrgb(byte[] icc)
        {
            try
            {
                if (icc == null || icc.Length < 132) return false;
                if (Ascii(icc, 16) != "RGB " || Ascii(icc, 20) != "XYZ ") return false;
                string[] colorants = { "rXYZ", "gXYZ", "bXYZ" };
                string[] curves = { "rTRC", "gTRC", "bTRC" };
                for (int c = 0; c < 3; c++)
                {
                    int at = Tag(icc, colorants[c]);
                    if (at < 0 || Ascii(icc, at) != "XYZ ") return false;
                    for (int k = 0; k < 3; k++)
                        if (Math.Abs(Fixed(icc, at + 8 + k * 4) - Primaries[c][k]) > NearPrimary) return false;
                    int curve = Tag(icc, curves[c]);
                    if (curve < 0) return false;
                    for (int b = 0; b <= 255; b += 15)
                    {
                        double x = b / 255.0;
                        double y = Curve(icc, curve, x);
                        double srgb = x <= 0.04045 ? x / 12.92 : Math.Pow((x + 0.055) / 1.055, 2.4);
                        if (double.IsNaN(y) || Math.Abs(y - srgb) > NearCurve) return false;
                    }
                }
                return true;
            }
            catch (Exception e) when (e is IndexOutOfRangeException || e is ArgumentException || e is OverflowException)
            {
                return false;   // a broken profile: Windows' conversion decides what to do with it
            }
        }

        static string Ascii(byte[] b, int at) => Encoding.ASCII.GetString(b, at, 4);

        static uint U32(byte[] b, int at) => (uint)((b[at] << 24) | (b[at + 1] << 16) | (b[at + 2] << 8) | b[at + 3]);

        static double Fixed(byte[] b, int at) => (int)U32(b, at) / 65536.0;

        // where a tag's data starts, or -1
        static int Tag(byte[] icc, string signature)
        {
            uint count = U32(icc, 128);
            for (uint i = 0; i < count && i < 200; i++)
            {
                int entry = 132 + (int)i * 12;
                if (Ascii(icc, entry) != signature) continue;
                uint offset = U32(icc, entry + 4);
                return offset < icc.Length ? (int)offset : -1;
            }
            return -1;
        }

        // a tone curve's value at x (0-1): a table ("curv") or a formula ("para")
        static double Curve(byte[] icc, int at, double x)
        {
            string type = Ascii(icc, at);
            if (type == "curv")
            {
                uint n = U32(icc, at + 8);
                if (n == 0) return x;
                if (n == 1) return Math.Pow(x, ((icc[at + 12] << 8) | icc[at + 13]) / 256.0);
                double p = x * (n - 1);
                int i = (int)Math.Floor(p);
                if (i >= n - 1) return Entry(icc, at, (int)n - 1);
                double a = Entry(icc, at, i), b = Entry(icc, at, i + 1);
                return a + (b - a) * (p - i);
            }
            if (type == "para")
            {
                int kind = (icc[at + 8] << 8) | icc[at + 9];
                double g = Fixed(icc, at + 12);
                double P(int k) => Fixed(icc, at + 12 + k * 4);
                switch (kind)
                {
                    case 0: return Math.Pow(x, g);
                    case 1: return x >= -P(2) / P(1) ? Math.Pow(P(1) * x + P(2), g) : 0;
                    case 2: return x >= -P(2) / P(1) ? Math.Pow(P(1) * x + P(2), g) + P(3) : P(3);
                    case 3: return x >= P(4) ? Math.Pow(P(1) * x + P(2), g) : P(3) * x;
                    case 4: return x >= P(4) ? Math.Pow(P(1) * x + P(2), g) + P(5) : P(3) * x + P(6);
                }
            }
            return double.NaN;
        }

        static double Entry(byte[] icc, int at, int i) => ((icc[at + 12 + i * 2] << 8) | icc[at + 13 + i * 2]) / 65535.0;
    }
}
