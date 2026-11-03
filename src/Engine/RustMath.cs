using System;

namespace B0r3dDeRes.Engine
{
    // The maths the website's signal engine does, to the last bit. The engine is Rust built to
    // WebAssembly, where a float's sin, cos, tan, powf and exp2, and a double's exp and ln, are
    // Rust's own maths library (libm, a port of musl and FreeBSD's msun, MIT): these are copies of
    // its routines as rustc 1.98.1 builds them. Like the WebAssembly, C# here does every multiply
    // and add on its own with a rounding after each (floats as floats, doubles as doubles), so the
    // copies come out the same. Rust's casts are here too: a float to a whole number saturates
    // (NaN is 0), and the engine's usize and isize are 32 bits in WebAssembly.
    static class RustMath
    {
        static float F(uint bits) => BitConverter.ToSingle(BitConverter.GetBytes(bits), 0);
        static uint Bits(float x) => BitConverter.ToUInt32(BitConverter.GetBytes(x), 0);
        static double D(ulong bits) => BitConverter.Int64BitsToDouble((long)bits);
        static ulong Bits(double x) => (ulong)BitConverter.DoubleToInt64Bits(x);

        // ---- Rust's casts

        // `x as u32` (the engine's usize): toward zero, saturating, NaN 0
        public static uint ToU32(double x) => double.IsNaN(x) || x <= 0 ? 0 : x >= 4294967295.0 ? uint.MaxValue : (uint)x;

        // `x as i32` (the engine's isize)
        public static int ToI32(double x) => double.IsNaN(x) ? 0 : x <= int.MinValue ? int.MinValue : x >= int.MaxValue ? int.MaxValue : (int)x;

        // f32::round: halves away from zero
        public static float Round(float x)
        {
            // past 2^23 a float is whole already
            if (float.IsNaN(x) || Math.Abs(x) >= 8388608f) return x;
            double r = Math.Floor(Math.Abs((double)x) + 0.5);
            return (float)(x < 0 ? -r : r);
        }

        // ---- sinf, cosf, tanf (FreeBSD's s_sinf.c, s_cosf.c, s_tanf.c)

        const double S1Pio2 = 1 * 1.5707963267948966, S2Pio2 = 2 * 1.5707963267948966, S3Pio2 = 3 * 1.5707963267948966, S4Pio2 = 4 * 1.5707963267948966;

        static float KSinf(double x)
        {
            const double S1 = -0.166666666416265235595, S2 = 0.0083333293858894631756, S3 = -0.000198393348360966317347, S4 = 0.0000027183114939898219064;
            double z = x * x;
            double w = z * z;
            double r = S3 + z * S4;
            double s = z * x;
            return (float)((x + s * (S1 + z * S2)) + s * w * r);
        }

        static float KCosf(double x)
        {
            const double C0 = -0.499999997251031003120, C1 = 0.0416666233237390631894, C2 = -0.00138867637746099294692, C3 = 0.0000243904487962774090654;
            double z = x * x;
            double w = z * z;
            double r = C2 + z * C3;
            return (float)(((1.0 + z * C0) + w * C1) + (w * z) * r);
        }

        static float KTanf(double x, bool odd)
        {
            const double T0 = 0.333331395030791399758, T1 = 0.133392002712976742718, T2 = 0.0533812378445670393523, T3 = 0.0245283181166547278873,
                T4 = 0.00297435743359967304927, T5 = 0.00946564784943673166728;
            double z = x * x;
            double r = T4 + z * T5;
            double t = T2 + z * T3;
            double w = z * z;
            double s = z * x;
            double u = T0 + z * T1;
            r = (x + s * u) + (s * w) * (t + w * r);
            return (float)(odd ? -1.0 / r : r);
        }

        // x - n·π/2 for the medium-sized x the engine sends (well under 2^28·π/2)
        static int RemPio2f(float x, out double y)
        {
            const double ToInt = 1.5 / 2.220446049250313e-16, InvPio2 = 6.36619772367581382433e-01, Pio2_1 = 1.57079631090164184570e+00,
                Pio2_1t = 1.58932547735281966916e-08;
            double x64 = x;
            double tmp = x64 * InvPio2 + ToInt;
            double fn = tmp - ToInt;
            y = x64 - fn * Pio2_1 - fn * Pio2_1t;
            return ToI32(fn);
        }

        public static float Sinf(float x)
        {
            double x64 = x;
            uint ix = Bits(x);
            bool sign = (ix >> 31) != 0;
            ix &= 0x7fffffff;
            if (ix <= 0x3f490fda)
            {
                if (ix < 0x39800000) return x;
                return KSinf(x64);
            }
            if (ix <= 0x407b53d1)
            {
                if (ix <= 0x4016cbe3) return sign ? -KCosf(x64 + S1Pio2) : KCosf(x64 - S1Pio2);
                return KSinf(sign ? -(x64 + S2Pio2) : -(x64 - S2Pio2));
            }
            if (ix <= 0x40e231d5)
            {
                if (ix <= 0x40afeddf) return sign ? KCosf(x64 + S3Pio2) : -KCosf(x64 - S3Pio2);
                return KSinf(sign ? x64 + S4Pio2 : x64 - S4Pio2);
            }
            if (ix >= 0x7f800000) return x - x;
            int n = RemPio2f(x, out double y);
            switch (n & 3)
            {
                case 0: return KSinf(y);
                case 1: return KCosf(y);
                case 2: return KSinf(-y);
                default: return -KCosf(y);
            }
        }

        public static float Cosf(float x)
        {
            double x64 = x;
            uint ix = Bits(x);
            bool sign = (ix >> 31) != 0;
            ix &= 0x7fffffff;
            if (ix <= 0x3f490fda)
            {
                if (ix < 0x39800000) return 1;
                return KCosf(x64);
            }
            if (ix <= 0x407b53d1)
            {
                if (ix > 0x4016cbe3) return -KCosf(sign ? x64 + S2Pio2 : x64 - S2Pio2);
                return sign ? KSinf(x64 + S1Pio2) : KSinf(S1Pio2 - x64);
            }
            if (ix <= 0x40e231d5)
            {
                if (ix > 0x40afeddf) return KCosf(sign ? x64 + S4Pio2 : x64 - S4Pio2);
                return sign ? KSinf(-x64 - S3Pio2) : KSinf(x64 - S3Pio2);
            }
            if (ix >= 0x7f800000) return x - x;
            int n = RemPio2f(x, out double y);
            switch (n & 3)
            {
                case 0: return KCosf(y);
                case 1: return KSinf(-y);
                case 2: return -KCosf(y);
                default: return KSinf(y);
            }
        }

        public static float Tanf(float x)
        {
            double x64 = x;
            uint ix = Bits(x);
            bool sign = (ix >> 31) != 0;
            ix &= 0x7fffffff;
            if (ix <= 0x3f490fda)
            {
                if (ix < 0x39800000) return x;
                return KTanf(x64, false);
            }
            if (ix <= 0x407b53d1)
            {
                if (ix <= 0x4016cbe3) return KTanf(sign ? x64 + S1Pio2 : x64 - S1Pio2, true);
                return KTanf(sign ? x64 + S2Pio2 : x64 - S2Pio2, false);
            }
            if (ix <= 0x40e231d5)
            {
                if (ix <= 0x40afeddf) return KTanf(sign ? x64 + S3Pio2 : x64 - S3Pio2, true);
                return KTanf(sign ? x64 + S4Pio2 : x64 - S4Pio2, false);
            }
            if (ix >= 0x7f800000) return x - x;
            int n = RemPio2f(x, out double y);
            return KTanf(y, (n & 1) != 0);
        }

        // ---- exp2f (FreeBSD's s_exp2f.c)

        static readonly ulong[] exp2ft =
        {
            0x3fe6a09e667f3bcd, 0x3fe7a11473eb0187, 0x3fe8ace5422aa0db, 0x3fe9c49182a3f090, 0x3feae89f995ad3ad, 0x3fec199bdd85529c,
            0x3fed5818dcfba487, 0x3feea4afa2a490da, 0x3ff0000000000000, 0x3ff0b5586cf9890f, 0x3ff172b83c7d517b, 0x3ff2387a6e756238,
            0x3ff306fe0a31b715, 0x3ff3dea64c123422, 0x3ff4bfdad5362a27, 0x3ff5ab07dd485429,
        };

        public static float Exp2f(float x)
        {
            float redux = F(0x4b400000) / 16;
            float p1 = F(0x3f317218), p2 = F(0x3e75fdf0), p3 = F(0x3d6359a4), p4 = F(0x3c1d964e);
            uint ui = Bits(x);
            uint ix = ui & 0x7fffffff;
            if (ix > 0x42fc0000)
            {
                if (ix > 0x7f800000) return x;
                if (ui >= 0x43000000 && ui < 0x80000000) return x * F(0x7f000000);
                if (ui >= 0xc3160000) return 0;
            }
            else if (ix <= 0x33000000) return 1 + x;
            ui = Bits(x + redux);
            uint i0 = ui + 8;
            uint k = i0 / 16;
            double ukf = D((ulong)(0x3ff + k) << 52);
            i0 &= 15;
            float uf = F(ui);
            uf -= redux;
            double z = x - uf;
            double r = D(exp2ft[i0]);
            double t = r * z;
            r = r + t * (p1 + z * p2) + t * (z * z) * (p3 + z * p4);
            return (float)(r * ukf);
        }

        // ---- powf (FreeBSD's e_powf.c)

        static readonly float[] bp = { 1, 1.5f }, dpH = { 0, F(0x3f15c000) }, dpL = { 0, F(0x35d1cfdc) };
        static readonly float L1 = F(0x3f19999a), L2 = F(0x3edb6db7), L3 = F(0x3eaaaaab), L4 = F(0x3e8ba305), L5 = F(0x3e6c3255), L6 = F(0x3e53f142),
            P1 = F(0x3e2aaaab), P2 = F(0xbb360b61), P3 = F(0x388ab355), P4 = F(0xb5ddea0e), P5 = F(0x3331bb4c),
            Lg2 = F(0x3f317218), Lg2H = F(0x3f317200), Lg2L = F(0x35bfbe8c), Cp = F(0x3f76384f), CpH = F(0x3f764000), CpL = F(0xb8f623c6);
        const float Two24 = 16777216, Huge = 1.0e30f, Tiny = 1.0e-30f, Ovt = 4.2995665694e-08f, Ivln2 = 1.4426950216e+00f, Ivln2H = 1.4426879883e+00f,
            Ivln2L = 7.0526075433e-06f;

        public static float Powf(float x, float y)
        {
            float z, ax, zH, zL, pH, pL, y1, t1, t2, r, s, sn, t, u, v, w;
            int i, j, k, yisint, n, ix, iy, iS;
            int hx = (int)Bits(x), hy = (int)Bits(y);
            ix = hx & 0x7fffffff;
            iy = hy & 0x7fffffff;
            if (iy == 0) return 1;
            if (hx == 0x3f800000) return 1;
            if (ix > 0x7f800000 || iy > 0x7f800000) return x + y;
            yisint = 0;
            if (hx < 0)
            {
                if (iy >= 0x4b800000) yisint = 2;
                else if (iy >= 0x3f800000)
                {
                    k = (iy >> 23) - 0x7f;
                    j = iy >> (23 - k);
                    if ((j << (23 - k)) == iy) yisint = 2 - (j & 1);
                }
            }
            if (iy == 0x7f800000)
            {
                if (ix == 0x3f800000) return 1;
                if (ix > 0x3f800000) return hy >= 0 ? y : 0;
                return hy >= 0 ? 0 : -y;
            }
            if (iy == 0x3f800000) return hy >= 0 ? x : 1 / x;
            if (hy == 0x40000000) return x * x;
            if (hy == 0x3f000000 && hx >= 0) return (float)Math.Sqrt(x);
            ax = Math.Abs(x);
            if (ix == 0x7f800000 || ix == 0 || ix == 0x3f800000)
            {
                z = ax;
                if (hy < 0) z = 1 / z;
                if (hx < 0)
                {
                    if (((ix - 0x3f800000) | yisint) == 0) z = (z - z) / (z - z);
                    else if (yisint == 1) z = -z;
                }
                return z;
            }
            sn = 1;
            if (hx < 0)
            {
                if (yisint == 0) return (x - x) / (x - x);
                if (yisint == 1) sn = -1;
            }
            if (iy > 0x4d000000)
            {
                // |y| is huge: |1-x| is tiny, so log(x) by x-x^2/2+x^3/3-x^4/4
                if (ix < 0x3f7ffff8) return hy < 0 ? sn * Huge * Huge : sn * Tiny * Tiny;
                if (ix > 0x3f800007) return hy > 0 ? sn * Huge * Huge : sn * Tiny * Tiny;
                t = ax - 1;
                w = (t * t) * (0.5f - t * (0.333333333333f - t * 0.25f));
                u = Ivln2H * t;
                v = t * Ivln2L - w * Ivln2;
                t1 = u + v;
                t1 = F(Bits(t1) & 0xfffff000);
                t2 = v - (t1 - u);
            }
            else
            {
                float s2, sH, sL, tH, tL;
                n = 0;
                if (ix < 0x00800000)
                {
                    ax *= Two24;
                    n -= 24;
                    ix = (int)Bits(ax);
                }
                n += (ix >> 23) - 0x7f;
                j = ix & 0x007fffff;
                ix = j | 0x3f800000;
                if (j <= 0x1cc471) k = 0;
                else if (j < 0x5db3d7) k = 1;
                else
                {
                    k = 0;
                    n += 1;
                    ix -= 0x00800000;
                }
                ax = F((uint)ix);
                // s = sH+sL = (x-1)/(x+1) or (x-1.5)/(x+1.5)
                u = ax - bp[k];
                v = 1 / (ax + bp[k]);
                s = u * v;
                sH = F(Bits(s) & 0xfffff000);
                iS = (int)((((uint)ix >> 1) & 0xfffff000) | 0x20000000);
                tH = F((uint)iS + 0x00400000 + ((uint)k << 21));
                tL = ax - (tH - bp[k]);
                sL = v * ((u - sH * tH) - sH * tL);
                // log(ax)
                s2 = s * s;
                r = s2 * s2 * (L1 + s2 * (L2 + s2 * (L3 + s2 * (L4 + s2 * (L5 + s2 * L6)))));
                r += sL * (sH + s);
                s2 = sH * sH;
                tH = 3 + s2 + r;
                tH = F(Bits(tH) & 0xfffff000);
                tL = r - ((tH - 3) - s2);
                u = sH * tH;
                v = sL * tH + tL * s;
                pH = u + v;
                pH = F(Bits(pH) & 0xfffff000);
                pL = v - (pH - u);
                zH = CpH * pH;
                zL = CpL * pH + pL * Cp + dpL[k];
                // log2(ax) = n + dpH + zH + zL
                t = n;
                t1 = ((zH + zL) + dpH[k]) + t;
                t1 = F(Bits(t1) & 0xfffff000);
                t2 = zL - (((t1 - t) - dpH[k]) - zH);
            }
            // (y1+y2)*(t1+t2)
            y1 = F(Bits(y) & 0xfffff000);
            pL = (y - y1) * t1 + y * t2;
            pH = y1 * t1;
            z = pL + pH;
            j = (int)Bits(z);
            if (j > 0x43000000) return sn * Huge * Huge;
            if (j == 0x43000000)
            {
                if (pL + Ovt > z - pH) return sn * Huge * Huge;
            }
            else if ((j & 0x7fffffff) > 0x43160000) return sn * Tiny * Tiny;
            else if ((uint)j == 0xc3160000 && pL <= z - pH) return sn * Tiny * Tiny;
            // 2**(pH+pL)
            i = j & 0x7fffffff;
            k = (i >> 23) - 0x7f;
            n = 0;
            if (i > 0x3f000000)
            {
                n = j + (0x00800000 >> (k + 1));
                k = ((n & 0x7fffffff) >> 23) - 0x7f;
                t = F((uint)n & ~(0x007fffffu >> k));
                n = ((n & 0x007fffff) | 0x00800000) >> (23 - k);
                if (j < 0) n = -n;
                pH -= t;
            }
            t = pL + pH;
            t = F(Bits(t) & 0xffff8000);
            u = t * Lg2H;
            v = (pL - (t - pH)) * Lg2 + t * Lg2L;
            z = u + v;
            w = v - (z - u);
            t = z * z;
            t1 = z - t * (P1 + t * (P2 + t * (P3 + t * (P4 + t * P5))));
            r = (z * t1) / (t1 - 2) - (w + z * w);
            z = 1 - (r - z);
            j = (int)Bits(z);
            j += n << 23;
            z = (j >> 23) <= 0 ? Scalbnf(z, n) : F((uint)j);
            return sn * z;
        }

        // ---- exp and log, for doubles (FreeBSD's e_exp.c and e_log.c)

        public static double Exp(double x)
        {
            const double Ln2Hi = 6.93147180369123816490e-01, Ln2Lo = 1.90821492927058770002e-10, InvLn2 = 1.44269504088896338700e+00,
                E1 = 1.66666666666666019037e-01, E2 = -2.77777777770155933842e-03, E3 = 6.61375632143793436117e-05, E4 = -1.65339022054652515390e-06,
                E5 = 4.13813679705723846039e-08;
            double hi, lo;
            int k;
            uint hx = (uint)(Bits(x) >> 32);
            int sign = (int)(hx >> 31);
            hx &= 0x7fffffff;
            if (hx >= 0x4086232b)
            {
                if (double.IsNaN(x)) return x;
                if (x > 709.782712893383973096) return x * D(0x7fe0000000000000);
                if (x < -708.39641853226410622 && x < -745.13321910194110842) return 0;
            }
            if (hx > 0x3fd62e42)
            {
                k = hx >= 0x3ff0a2b2 ? ToI32(InvLn2 * x + (sign == 0 ? 0.5 : -0.5)) : 1 - sign - sign;
                hi = x - k * Ln2Hi;
                lo = k * Ln2Lo;
                x = hi - lo;
            }
            else if (hx > 0x3e300000)
            {
                k = 0;
                hi = x;
                lo = 0;
            }
            else return 1 + x;
            double xx = x * x;
            double c = x - xx * (E1 + xx * (E2 + xx * (E3 + xx * (E4 + xx * E5))));
            double y = 1 + (x * c / (2 - c) - lo + hi);
            return k == 0 ? y : Scalbn(y, k);
        }

        public static double Log(double x)
        {
            const double Ln2Hi = 6.93147180369123816490e-01, Ln2Lo = 1.90821492927058770002e-10, Lg1 = 6.666666666666735130e-01,
                Lg2d = 3.999999999940941908e-01, Lg3 = 2.857142874366239149e-01, Lg4 = 2.222219843214978396e-01, Lg5 = 1.818357216161805012e-01,
                Lg6 = 1.531383769920937332e-01, Lg7 = 1.479819860511658591e-01;
            ulong ui = Bits(x);
            uint hx = (uint)(ui >> 32);
            int k = 0;
            if (hx < 0x00100000 || (hx >> 31) != 0)
            {
                if (ui << 1 == 0) return -1 / (x * x);
                if (hx >> 31 != 0) return (x - x) / 0.0;
                // subnormal: scale x up
                k -= 54;
                x *= D(0x4350000000000000);
                ui = Bits(x);
                hx = (uint)(ui >> 32);
            }
            else if (hx >= 0x7ff00000) return x;
            else if (hx == 0x3ff00000 && ui << 32 == 0) return 0;
            // x into [sqrt(2)/2, sqrt(2)]
            hx += 0x3ff00000 - 0x3fe6a09e;
            k += (int)(hx >> 20) - 0x3ff;
            hx = (hx & 0x000fffff) + 0x3fe6a09e;
            ui = ((ulong)hx << 32) | (ui & 0xffffffff);
            x = D(ui);
            double f = x - 1;
            double hfsq = 0.5 * f * f;
            double s = f / (2 + f);
            double z = s * s;
            double w = z * z;
            double t1 = w * (Lg2d + w * (Lg4 + w * Lg6));
            double t2 = z * (Lg1 + w * (Lg3 + w * (Lg5 + w * Lg7)));
            double r = t2 + t1;
            double dk = k;
            return s * (hfsq + r) + dk * Ln2Lo - hfsq + f + dk * Ln2Hi;
        }

        // x × 2^n (libm's generic scalbn), for doubles and floats
        static double Scalbn(double x, int n)
        {
            if (n > 1023)
            {
                x *= D(0x7fe0000000000000);
                n -= 1023;
                if (n > 1023)
                {
                    x *= D(0x7fe0000000000000);
                    n -= 1023;
                    if (n > 1023) n = 1023;
                }
            }
            else if (n < -1022)
            {
                double mul = D(0x0010000000000000) * D(0x4340000000000000);   // 2^-1022 × 2^53
                x *= mul;
                n += 1022 - 53;
                if (n < -1022)
                {
                    x *= mul;
                    n += 1022 - 53;
                    if (n < -1022) n = -1022;
                }
            }
            return x * D((ulong)(1023 + n) << 52);
        }

        static float Scalbnf(float x, int n)
        {
            if (n > 127)
            {
                x *= F(0x7f000000);
                n -= 127;
                if (n > 127)
                {
                    x *= F(0x7f000000);
                    n -= 127;
                    if (n > 127) n = 127;
                }
            }
            else if (n < -126)
            {
                float mul = F(0x00800000) * F(0x4b800000);    // 2^-126 × 2^24
                x *= mul;
                n += 126 - 24;
                if (n < -126)
                {
                    x *= mul;
                    n += 126 - 24;
                    if (n < -126) n = -126;
                }
            }
            return x * F((uint)(127 + n) << 23);
        }
    }
}
