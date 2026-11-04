using System;
using System.Runtime.InteropServices;
using B0r3dDeRes.Engine;

namespace B0r3dDeRes.Video
{
    // HLG HDR video (what iPhones record in HDR) into normal colour. Windows' own conversion makes it
    // come out dark and dull, so DeRes does it itself, by ITU-R BT.2408's display-light way: the
    // light an HLG screen of 1,000 nits gives (BT.2100's inverse OETF, then its OOTF, by
    // brightness so the hues keep), HLG's reference white (203 nits, 75% of the signal) as white,
    // BT.2020's colours into BT.709's, the brighter highlights eased in rather than cut off, then
    // normal video's curve (BT.1886's gamma of 2.4).
    static class Hdr
    {
        const double A = 0.17883277, B = 0.28466892, C = 0.55991073;
        const double PeakNits = 1000, ReferenceWhite = 203, SystemGamma = 1.2, VideoGamma = 2.4;
        // where the highlights start easing in (in linear light, white = 1)
        const double Knee = 0.9;

        // the HLG signal (0..1) to scene light (0..1), in steps of 1/4096
        static readonly float[] inverseOetf = BuildInverseOetf();

        static float[] BuildInverseOetf()
        {
            var t = new float[4097];
            for (int i = 0; i <= 4096; i++)
            {
                double e = i / 4096.0;
                t[i] = (float)(e <= 0.5 ? e * e / 3 : (Math.Exp((e - C) / A) + B) / 12);
            }
            return t;
        }

        static float SceneLight(float signal)
        {
            if (!(signal > 0)) return 0;
            if (signal >= 1) return inverseOetf[4096];
            float at = signal * 4096;
            int i = (int)at;
            float f = at - i;
            return inverseOetf[i] + (inverseOetf[i + 1] - inverseOetf[i]) * f;
        }

        // (Played and saved frames go through here for every pixel, so the powers are tables: a 4K
        // frame takes milliseconds, not the better part of a second.)

        // normal video's curve: byte k is linear light from gammaStarts[k] up to the next one's start
        // (the very bytes rounding linear^(1/2.4) × 255 gives)
        static readonly double[] gammaStarts = BuildGammaStarts();

        static double[] BuildGammaStarts()
        {
            var t = new double[256];
            for (int k = 1; k < 256; k++) t[k] = Math.Pow((k - 0.5) / 255, VideoGamma);
            return t;
        }

        // the byte each 1/65536th of linear light starts in (then up past any byte starting within it:
        // the curve is steep near black)
        const int CurveSteps = 65536;
        static readonly byte[] curveStart = BuildCurveStart();

        static byte[] BuildCurveStart()
        {
            var t = new byte[CurveSteps];
            int k = 0;
            for (int i = 0; i < CurveSteps; i++)
            {
                while (k < 255 && gammaStarts[k + 1] <= i / (double)CurveSteps) k++;
                t[i] = (byte)k;
            }
            return t;
        }

        static byte Video(double linear)
        {
            if (!(linear > 0)) return 0;
            if (linear >= 1) return 255;
            int k = curveStart[(int)(linear * CurveSteps)];
            while (k < 255 && gammaStarts[k + 1] <= linear) k++;
            return (byte)k;
        }

        // the OOTF's gain for a scene brightness (0..1): its power of 0.2, in steps of 1/65536, and
        // below 1/1024 (where the power climbs steeply) in steps of 1/4096 of that
        const int GainSteps = 65536, FineSteps = 4096;
        static readonly float[] gainTable = Table(GainSteps, i => Math.Pow(i / (double)GainSteps, SystemGamma - 1));
        static readonly float[] fineGainTable = Table(FineSteps, i => Math.Pow(i / (1024.0 * FineSteps), SystemGamma - 1));

        static float[] Table(int steps, Func<int, double> value)
        {
            var t = new float[steps + 1];
            for (int i = 0; i <= steps; i++) t[i] = (float)value(i);
            return t;
        }

        static double Lookup(float[] table, double at)
        {
            int i = (int)at;
            return table[i] + (table[i + 1] - table[i]) * (at - i);
        }

        static double Gain(double ys)
        {
            if (!(ys > 0)) return 0;
            if (ys >= 1) return PeakNits * Math.Pow(ys, SystemGamma - 1) / ReferenceWhite;
            return PeakNits * (ys < 1.0 / 1024 ? Lookup(fineGainTable, ys * 1024 * FineSteps) : Lookup(gainTable, ys * GainSteps)) / ReferenceWhite;
        }

        // highlights past the knee eased into what's left: by table up to 16 past it, in steps of 1/1024
        const int KneeSteps = 1024, KneeRange = 16;
        static readonly float[] kneeTable = Table(KneeSteps * KneeRange, i => Knee + (1 - Knee) * (1 - Math.Exp(-(i / (double)KneeSteps) / (1 - Knee))));

        static double Eased(double l)
        {
            double at = (l - Knee) * KneeSteps;
            return at < KneeSteps * KneeRange ? Lookup(kneeTable, at) : Knee + (1 - Knee) * (1 - Math.Exp(-(l - Knee) / (1 - Knee)));
        }

        // One pixel from 10-bit studio-range Y, Cb and Cr (BT.2020, HLG) to the bytes of normal video.
        public static void Pixel(int y10, int cb10, int cr10, out byte r8, out byte g8, out byte b8)
        {
            float yp = (y10 - 64) / 876f, cb = (cb10 - 512) / 896f, cr = (cr10 - 512) / 896f;
            // BT.2020's non-constant-luminance matrix
            float rp = yp + 1.4746f * cr, bp = yp + 1.8814f * cb;
            float gp = (yp - 0.2627f * rp - 0.0593f * bp) / 0.6780f;
            double rs = SceneLight(rp), gs = SceneLight(gp), bs = SceneLight(bp);
            // the OOTF: the light a 1,000-nit screen gives (by brightness), then 203 nits as white
            double ys = 0.2627 * rs + 0.6780 * gs + 0.0593 * bs;
            double gain = Gain(ys);
            double rd = rs * gain, gd = gs * gain, bd = bs * gain;
            // BT.2020's colours into BT.709's
            double r = 1.6605 * rd - 0.5876 * gd - 0.0728 * bd;
            double g = -0.1246 * rd + 1.1329 * gd - 0.0083 * bd;
            double b = -0.0182 * rd - 0.1006 * gd + 1.1187 * bd;
            // highlights past the knee eased into what's left, keeping their hue
            double l = 0.2126 * r + 0.7152 * g + 0.0722 * b;
            if (l > Knee)
            {
                double scale = Eased(l) / l;
                r *= scale;
                g *= scale;
                b *= scale;
            }
            r8 = Video(r);
            g8 = Video(g);
            b8 = Video(b);
        }

        // A P010 frame (Windows' 10-bit: a plane of Y, then one of Cb and Cr in turn at half size,
        // each 16-bit with the value in its top 10 bits) to b, g, r, a: the picture at (cropX, cropY),
        // turned upright by `rotation` degrees clockwise, outW × outH when done.
        public static byte[] HlgToBgra(IntPtr top, int pitch, int codedW, int codedH, int cropX, int cropY, int rotation, int outW, int outH)
        {
            bool sideways = rotation == 90 || rotation == 270;
            int w = sideways ? outH : outW, h = sideways ? outW : outH;
            var output = new byte[(long)outW * outH * 4];
            IntPtr chroma = top + pitch * codedH;
            EveryCore.For(0, h, () => (new short[codedW], new short[codedW]), (y, state, rows) =>
            {
                short[] luma = rows.Item1, uv = rows.Item2;
                int sy = cropY + y;
                Marshal.Copy(top + sy * pitch, luma, 0, codedW);
                Marshal.Copy(chroma + (sy >> 1) * pitch, uv, 0, codedW);
                for (int x = 0; x < w; x++)
                {
                    int sx = cropX + x, c = sx & ~1;
                    Pixel((ushort)luma[sx] >> 6, (ushort)uv[c] >> 6, (ushort)uv[c + 1] >> 6, out byte r, out byte g, out byte b);
                    Put(output, outW, outH, x, y, w, h, rotation, r, g, b);
                }
                return rows;
            });
            return output;
        }

        // the same from separate 10-bit planes (Y at w × h, Cb and Cr at half size; values 0..1023), unturned
        public static byte[] HlgToBgra(ushort[] luma, ushort[] cb, ushort[] cr, int w, int h)
        {
            var output = new byte[(long)w * h * 4];
            int cw = (w + 1) / 2;
            EveryCore.For(0, h, y =>
            {
                for (int x = 0; x < w; x++)
                {
                    int c = (y >> 1) * cw + (x >> 1);
                    Pixel(luma[y * w + x], cb[c], cr[c], out byte r, out byte g, out byte b);
                    Put(output, w, h, x, y, w, h, 0, r, g, b);
                }
            });
            return output;
        }

        // pixel (x, y) of a w × h picture, turned `rotation` degrees clockwise, into the outW × outH result
        static void Put(byte[] output, int outW, int outH, int x, int y, int w, int h, int rotation, byte r, byte g, byte b)
        {
            int ox, oy;
            switch (rotation)
            {
                case 90: ox = h - 1 - y; oy = x; break;
                case 180: ox = w - 1 - x; oy = h - 1 - y; break;
                case 270: ox = y; oy = w - 1 - x; break;
                default: ox = x; oy = y; break;
            }
            long o = ((long)oy * outW + ox) * 4;
            output[o] = b;
            output[o + 1] = g;
            output[o + 2] = r;
            output[o + 3] = 255;
        }
    }
}
