using System;

namespace B0r3dDeRes.Engine
{
    // The website's sums, to the last bit. JavaScript's numbers are doubles and its typed arrays
    // hold floats; C# does the same sums the same way as long as every sum is done in double (a
    // sum of two floats in C# rounds to float at once, where JavaScript keeps the double until it
    // is stored). So the engine reads floats into doubles before any sum, and writes 7.0 / 16
    // where the website writes 7 / 16.
    //
    // The library routines whose last bit differs between .NET and the browser are copies:
    // cube root is the browser's own (fdlibm's, as in V8), and powers use fdlibm's too, so they
    // come out the same on every PC. (The browser's power is the system's own, which can differ
    // in the last bit; the engine only keeps powers as floats, where none of them differ: the
    // tests check every one against the website's.)
    static class JsMath
    {
        // Math.round: the nearest whole number, halves upward (C#'s Math.Round sends 2.5 to 2)
        public static double Round(double x)
        {
            double r = Math.Floor(x);
            return x - r >= 0.5 ? r + 1 : r;
        }

        static long Bits(double x) => BitConverter.DoubleToInt64Bits(x);
        static double FromBits(long bits) => BitConverter.Int64BitsToDouble(bits);
        static int High(double x) => (int)(Bits(x) >> 32);
        static uint Low(double x) => (uint)Bits(x);
        static double Words(int high, uint low) => FromBits(((long)high << 32) | low);
        static double WithHigh(double x, int high) => Words(high, Low(x));
        static double WithLow(double x, uint low) => Words(High(x), low);

        // Math.cbrt, as V8 has it (FreeBSD's s_cbrt.c, from fdlibm)
        public static double Cbrt(double x)
        {
            const uint B1 = 715094163, B2 = 696219795;
            const double P0 = 1.87595182427177009643, P1 = -1.88497979543377169875, P2 = 1.621429720105354466140,
                P3 = -0.758397934778766047437, P4 = 0.145996192886612446982;
            int hx = High(x);
            uint low = Low(x);
            uint sign = (uint)hx & 0x80000000;
            hx = (int)((uint)hx ^ sign);
            if (hx >= 0x7FF00000) return x + x;  // NaN and infinities are themselves
            double t;
            if (hx < 0x00100000)
            {
                if ((hx | (int)low) == 0) return x;  // zero is itself
                t = Words(0x43500000, 0);   // 2^54
                t *= x;
                uint high = (uint)High(t);
                t = Words((int)(sign | ((high & 0x7FFFFFFF) / 3 + B2)), 0);
            }
            else t = Words((int)(sign | ((uint)hx / 3 + B1)), 0);

            double r = (t * t) * (t / x);
            t = t * ((P0 + r * (P1 + r * P2)) + ((r * r) * r) * (P3 + r * P4));
            ulong bits = (ulong)Bits(t);
            bits = (bits + 0x80000000UL) & 0xFFFFFFFFC0000000UL;
            t = FromBits((long)bits);

            double s = t * t;
            r = x / s;
            double w = t + t;
            r = (r - t) / (w + r);
            return t + t * r;
        }

        // x to the power y: fdlibm's e_pow.c, as V8 keeps it (its legacy::pow). Exact for whole
        // powers that fit, within a last bit otherwise.
        public static double Pow(double x, double y)
        {
            const double zero = 0.0, one = 1.0, two = 2.0, two53 = 9007199254740992.0, huge = 1.0e300, tiny = 1.0e-300,
                L1 = 5.99999999999994648725e-01, L2 = 4.28571428578550184252e-01, L3 = 3.33333329818377432918e-01,
                L4 = 2.72728123808534006489e-01, L5 = 2.30660745775561754067e-01, L6 = 2.06975017800338417784e-01,
                P1 = 1.66666666666666019037e-01, P2 = -2.77777777770155933842e-03, P3 = 6.61375632143793436117e-05,
                P4 = -1.65339022054652515390e-06, P5 = 4.13813679705723846039e-08,
                lg2 = 6.93147180559945286227e-01, lg2_h = 6.93147182464599609375e-01, lg2_l = -1.90465429995776804525e-09,
                ovt = 8.0085662595372944372e-0017,
                cp = 9.61796693925975554329e-01, cp_h = 9.61796700954437255859e-01, cp_l = -7.02846165095275826516e-09,
                ivln2 = 1.44269504088896338700e+00, ivln2_h = 1.44269502162933349609e+00, ivln2_l = 1.92596299112661746887e-08;
            double[] bp = { 1.0, 1.5 }, dp_h = { 0.0, 5.84962487220764160156e-01 }, dp_l = { 0.0, 1.35003920212974897128e-08 };

            double z, ax, z_h, z_l, p_h, p_l, y1, t1, t2, r, s, t, u, v, w;
            int i, j, k, yisint, n;
            int hx = High(x), hy = High(y);
            uint lx = Low(x), ly = Low(y);
            int ix = hx & 0x7fffffff, iy = hy & 0x7fffffff;

            if ((iy | (int)ly) == 0) return one;  // x**0 = 1

            if (ix > 0x7ff00000 || ix == 0x7ff00000 && lx != 0 || iy > 0x7ff00000 || iy == 0x7ff00000 && ly != 0)
                return x + y;  // NaN

            // y is 0 (not an integer), 1 (an odd integer) or 2 (an even one), when x < 0
            yisint = 0;
            if (hx < 0)
            {
                if (iy >= 0x43400000) yisint = 2;
                else if (iy >= 0x3ff00000)
                {
                    k = (iy >> 20) - 0x3ff;
                    if (k > 20)
                    {
                        j = (int)(ly >> (52 - k));
                        if ((uint)(j << (52 - k)) == ly) yisint = 2 - (j & 1);
                    }
                    else if (ly == 0)
                    {
                        j = iy >> (20 - k);
                        if ((j << (20 - k)) == iy) yisint = 2 - (j & 1);
                    }
                }
            }

            if (ly == 0)
            {
                if (iy == 0x7ff00000)
                {
                    // y is +-infinity
                    if (((ix - 0x3ff00000) | (int)lx) == 0) return y - y;       // inf**+-1 is NaN
                    if (ix >= 0x3ff00000) return hy >= 0 ? y : zero;           // (|x|>1)**+-inf = inf, 0
                    return hy < 0 ? -y : zero;                                  // (|x|<1)**-,+inf = inf, 0
                }
                if (iy == 0x3ff00000) return hy < 0 ? one / x : x;             // y is +-1
                if (hy == 0x40000000) return x * x;                            // y is 2
                if (hy == 0x3fe00000 && hx >= 0) return Math.Sqrt(x);          // y is 0.5
            }

            ax = Math.Abs(x);
            if (lx == 0 && (ix == 0x7ff00000 || ix == 0 || ix == 0x3ff00000))
            {
                // x is +-0, +-inf or +-1
                z = ax;
                if (hy < 0) z = one / z;
                if (hx < 0)
                {
                    if (((ix - 0x3ff00000) | yisint) == 0) z = double.NaN;    // (-1)**non-int
                    else if (yisint == 1) z = -z;                               // (x<0)**odd = -(|x|**odd)
                }
                return z;
            }

            n = (hx >> 31) + 1;
            if ((n | yisint) == 0) return double.NaN;  // (x<0)**(non-int)

            s = one;
            if ((n | (yisint - 1)) == 0) s = -one;  // (-ve)**(odd int)

            if (iy > 0x41e00000)
            {
                // |y| > 2**31
                if (iy > 0x43f00000)
                {
                    if (ix <= 0x3fefffff) return hy < 0 ? huge * huge : tiny * tiny;
                    if (ix >= 0x3ff00000) return hy > 0 ? huge * huge : tiny * tiny;
                }
                if (ix < 0x3fefffff) return hy < 0 ? s * huge * huge : s * tiny * tiny;
                if (ix > 0x3ff00000) return hy > 0 ? s * huge * huge : s * tiny * tiny;
                // |1-x| is tiny: log(x) by x-x^2/2+x^3/3-x^4/4
                t = ax - one;
                w = (t * t) * (0.5 - t * (0.3333333333333333333333 - t * 0.25));
                u = ivln2_h * t;
                v = t * ivln2_l - w * ivln2;
                t1 = u + v;
                t1 = WithLow(t1, 0);
                t2 = v - (t1 - u);
            }
            else
            {
                double ss, s2, s_h, s_l, t_h, t_l;
                n = 0;
                if (ix < 0x00100000)
                {
                    // subnormal x
                    ax *= two53;
                    n -= 53;
                    ix = High(ax);
                }
                n += (ix >> 20) - 0x3ff;
                j = ix & 0x000fffff;
                ix = j | 0x3ff00000;
                if (j <= 0x3988E) k = 0;            // |x|<sqrt(3/2)
                else if (j < 0xBB67A) k = 1;        // |x|<sqrt(3)
                else
                {
                    k = 0;
                    n += 1;
                    ix -= 0x00100000;
                }
                ax = WithHigh(ax, ix);

                // ss = s_h+s_l = (x-1)/(x+1) or (x-1.5)/(x+1.5)
                u = ax - bp[k];
                v = one / (ax + bp[k]);
                ss = u * v;
                s_h = ss;
                s_h = WithLow(s_h, 0);
                t_h = zero;
                t_h = WithHigh(t_h, ((ix >> 1) | 0x20000000) + 0x00080000 + (k << 18));
                t_l = ax - (t_h - bp[k]);
                s_l = v * ((u - s_h * t_h) - s_h * t_l);
                // log(ax)
                s2 = ss * ss;
                r = s2 * s2 * (L1 + s2 * (L2 + s2 * (L3 + s2 * (L4 + s2 * (L5 + s2 * L6)))));
                r += s_l * (s_h + ss);
                s2 = s_h * s_h;
                t_h = 3.0 + s2 + r;
                t_h = WithLow(t_h, 0);
                t_l = r - ((t_h - 3.0) - s2);
                u = s_h * t_h;
                v = s_l * t_h + t_l * ss;
                p_h = u + v;
                p_h = WithLow(p_h, 0);
                p_l = v - (p_h - u);
                z_h = cp_h * p_h;
                z_l = cp_l * p_h + p_l * cp + dp_l[k];
                // log2(ax) = n + dp_h + z_h + z_l
                t = n;
                t1 = ((z_h + z_l) + dp_h[k]) + t;
                t1 = WithLow(t1, 0);
                t2 = z_l - (((t1 - t) - dp_h[k]) - z_h);
            }

            // (y1+y2)*(t1+t2)
            y1 = y;
            y1 = WithLow(y1, 0);
            p_l = (y - y1) * t1 + y * t2;
            p_h = y1 * t1;
            z = p_l + p_h;
            j = High(z);
            i = (int)Low(z);
            if (j >= 0x40900000)
            {
                // z >= 1024
                if (((j - 0x40900000) | i) != 0) return s * huge * huge;
                if (p_l + ovt > z - p_h) return s * huge * huge;
            }
            else if ((j & 0x7fffffff) >= 0x4090cc00)
            {
                // z <= -1075
                if (((j - unchecked((int)0xc090cc00)) | i) != 0) return s * tiny * tiny;
                if (p_l <= z - p_h) return s * tiny * tiny;
            }

            // 2**(p_h+p_l)
            i = j & 0x7fffffff;
            k = (i >> 20) - 0x3ff;
            n = 0;
            if (i > 0x3fe00000)
            {
                n = j + (0x00100000 >> (k + 1));
                k = ((n & 0x7fffffff) >> 20) - 0x3ff;
                t = zero;
                t = WithHigh(t, n & ~(0x000fffff >> k));
                n = ((n & 0x000fffff) | 0x00100000) >> (20 - k);
                if (j < 0) n = -n;
                p_h -= t;
            }
            t = p_l + p_h;
            t = WithLow(t, 0);
            u = t * lg2_h;
            v = (p_l - (t - p_h)) * lg2 + t * lg2_l;
            z = u + v;
            w = v - (z - u);
            t = z * z;
            t1 = z - t * (P1 + t * (P2 + t * (P3 + t * (P4 + t * P5))));
            r = (z * t1) / ((t1 - two) - (w + z * w));
            z = one - (r - z);
            j = High(z);
            j += (int)((uint)n << 20);
            if ((j >> 20) <= 0) z = Scalbn(z, n);  // a subnormal result
            else z = WithHigh(z, High(z) + (int)((uint)n << 20));
            return s * z;
        }

        const int SignAndFraction = unchecked((int)0x800fffff);

        // x × 2^n, rounded once
        static double Scalbn(double x, int n)
        {
            const double two54 = 1.80143985094819840000e+16, twom54 = 5.55111512312578270212e-17, huge = 1.0e+300, tiny = 1.0e-300;
            int hx = High(x);
            uint lx = Low(x);
            int k = (hx & 0x7ff00000) >> 20;
            if (k == 0)
            {
                if ((lx | (uint)(hx & 0x7fffffff)) == 0) return x;   // +-0
                x *= two54;
                hx = High(x);
                k = ((hx & 0x7ff00000) >> 20) - 54;
                if (n < -50000) return tiny * x;
            }
            if (k == 0x7ff) return x + x;  // NaN or infinity
            k = k + n;
            if (k > 0x7fe) return huge * CopySign(huge, x);
            if (k > 0) return WithHigh(x, (hx & SignAndFraction) | (k << 20));
            if (k <= -54)
                return n > 50000 ? huge * CopySign(huge, x) : tiny * CopySign(tiny, x);
            k += 54;
            x = WithHigh(x, (hx & SignAndFraction) | (k << 20));
            return x * twom54;
        }

        static double CopySign(double magnitude, double sign) => (Bits(sign) < 0) == (Bits(magnitude) < 0) ? magnitude : -magnitude;
    }
}
