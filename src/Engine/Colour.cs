using System;
using System.Globalization;

namespace B0r3dDeRes.Engine
{
    // Colour maths shared by every stage: sRGB <-> linear light, and OKLab (Björn Ottosson's
    // perceptual colour space) for comparing colours. (The website's core/color.ts.)
    //
    // Pixels are kept and mixed in linear light (that's how light adds up, so dithering keeps the
    // right brightness), and compared in OKLab (that's how different two colours look).
    static class Colour
    {
        public static double SrgbToLinear(double c) => c <= 0.04045 ? c / 12.92 : JsMath.Pow((c + 0.055) / 1.055, 2.4);

        public static double LinearToSrgb(double v) => v <= 0.0031308 ? v * 12.92 : 1.055 * JsMath.Pow(v, 1 / 2.4) - 0.055;

        // an sRGB byte (0-255) in linear light (0-1)
        public static readonly float[] ByteToLinear = new float[256];

        // linear light in 1/4096 steps to an sRGB byte: fine enough that byte -> linear -> byte
        // comes back exact for every byte
        const int Steps = 4096;
        static readonly byte[] linearToByte = new byte[Steps + 1];

        // the two curves as tables, read with linear interpolation (keeping more than 8 bits)
        static readonly float[] srgbCurve = new float[Steps + 2], linearCurve = new float[Steps + 2];

        static Colour()
        {
            for (int i = 0; i < 256; i++) ByteToLinear[i] = (float)SrgbToLinear(i / 255.0);
            for (int i = 0; i <= Steps; i++)
            {
                linearToByte[i] = (byte)JsMath.Round(LinearToSrgb(i / (double)Steps) * 255);
                srgbCurve[i] = (float)LinearToSrgb(i / (double)Steps);
                linearCurve[i] = (float)SrgbToLinear(i / (double)Steps);
            }
            srgbCurve[Steps + 1] = srgbCurve[Steps];
            linearCurve[Steps + 1] = linearCurve[Steps];
        }

        // linear light (any value, clamped to 0-1) to an sRGB byte
        public static int LinearToByte(double v)
        {
            if (!(v > 0)) return 0;
            if (v >= 1) return 255;
            return linearToByte[(int)(v * Steps + 0.5)];
        }

        // linearToSrgb for 0-1 (clamped), through the table
        public static double ToSrgb(double x) => Curve(srgbCurve, x);

        // srgbToLinear for 0-1 (clamped), through the table
        public static double ToLinear(double x) => Curve(linearCurve, x);

        static double Curve(float[] t, double x)
        {
            if (!(x > 0)) return t[0];
            if (x >= 1) return t[Steps];
            double f = x * Steps;
            int i = (int)f;
            double a = t[i], b = t[i + 1];
            return a + (b - a) * (f - i);
        }

        // for the tests: the tables, as the website's color.ts makes them
        internal static float[] SrgbCurveTable => srgbCurve;
        internal static float[] LinearCurveTable => linearCurve;
        internal static byte[] LinearToByteTable => linearToByte;

        // Linear-light RGB to OKLab.
        public static void LinearToOklab(double r, double g, double b, out double L, out double A, out double B)
        {
            double l = JsMath.Cbrt(0.4122214708 * r + 0.5363325363 * g + 0.0514459929 * b);
            double m = JsMath.Cbrt(0.2119034982 * r + 0.6806995451 * g + 0.1073969566 * b);
            double s = JsMath.Cbrt(0.0883024619 * r + 0.2817188376 * g + 0.6299787005 * b);
            L = 0.2104542553 * l + 0.793617785 * m - 0.0040720468 * s;
            A = 1.9779984951 * l - 2.428592205 * m + 0.4505937099 * s;
            B = 0.0259040371 * l + 0.7827717662 * m - 0.808675766 * s;
        }

        // relative luminance (Rec. 709 / sRGB primaries) of a linear-light colour
        public static double Luminance(double r, double g, double b) => 0.2126 * r + 0.7152 * g + 0.0722 * b;

        // "#rrggbb" to its three bytes
        public static byte[] HexToRgb(string hex)
        {
            string h = hex.StartsWith("#", StringComparison.Ordinal) ? hex.Substring(1) : hex;
            if (h.Length != 6 || !int.TryParse(h, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out int n))
                throw new FormatException("Not a colour: " + hex);
            return new[] { (byte)(n >> 16), (byte)(n >> 8), (byte)n };
        }

        public static string RgbToHex(int r, int g, int b) => "#" + ((r << 16) | (g << 8) | b).ToString("x6", CultureInfo.InvariantCulture);
    }
}
