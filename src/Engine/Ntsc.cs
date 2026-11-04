using System;

namespace B0r3dDeRes.Engine
{
    // ntsc-rs, the analog TV engine by valadaptive (crates/ntscrs at 450c6ee, MIT OR ISC OR
    // Apache-2.0), copied from its Rust, every setting of it (NtscSettings), with the website's own
    // wrapper (its signal/src/lib.rs) around it. The website runs it as WebAssembly with SIMD
    // (4 floats at a time, a multiply and an add each rounded): every sum here is the same sum in
    // the same order, row by row, so the frames come out the same, byte for byte. A frame's noise
    // comes from its number: the same frame and settings always give the same picture.
    static class Ntsc
    {
        static float F(uint bits) => BitConverter.ToSingle(BitConverter.GetBytes(bits), 0);
        static uint Bits(float x) => BitConverter.ToUInt32(BitConverter.GetBytes(x), 0);

        static readonly float Pi = F(0x40490fdb), FracSqrt2 = F(0x3f3504f3);
        // 315/88 MHz × 4, and its half (the pre-emphasis), as the engine's floats
        static readonly float NtscRate = F(0x4b5a7a66), PreemphasisCutoff = F(0x49da7a66);
        // 16/255: the black level a box filter starts from
        static readonly float BoxBlack = F(0x3d808081);

        // ---- filters (filter.rs): IIR filters of up to 4 coefficients a side

        sealed class Filter
        {
            // the numerator, and the denominator without its leading 1, 4 lanes each (0 past Len)
            public readonly float[] Num = new float[4], Den = new float[4];
            public readonly int Len;

            public Filter(float[] num, float[] den)
            {
                Array.Copy(num, Num, num.Length);
                Array.Copy(den, Den, den.Length);
                Len = den.Length + 1;
            }

            public static Filter Lowpass(float cutoff, float rate)
            {
                float timeInterval = 1f / rate;
                float tau = 1f / (cutoff * 2f * Pi);
                float alpha = timeInterval / (tau + timeInterval);
                return new Filter(new[] { alpha }, new[] { -(1f - alpha) });
            }

            // the setting's kind of lowpass: three constant-k ones in a row, or a Butterworth
            public static Filter LowpassFor(float cutoff, float rate, FilterType type) =>
                type == FilterType.ConstantK ? Lowpass(cutoff, rate).CascadeSelf(3) : Butterworth(cutoff, rate);

            public static Filter Notch(float freq, float quality)
            {
                float bandwidth = freq / quality * Pi;
                freq = freq * Pi;
                float beta = RustMath.Tanf(bandwidth * 0.5f);
                float gain = 1f / (1f + beta);
                float cos = RustMath.Cosf(freq);
                return new Filter(new[] { gain, -2f * cos * gain, gain }, new[] { -2f * cos * gain, 2f * gain - 1f });
            }

            public static Filter Butterworth(float cutoff, float rate)
            {
                float freq = Math.Min(2f * cutoff, rate) / rate;
                freq = freq * Pi;
                float omegaS = RustMath.Sinf(freq), omegaC = RustMath.Cosf(freq);
                float alpha = omegaS / (2f * FracSqrt2);
                float gain = 1f / (1f + alpha);
                return new Filter(new[] { (1f - omegaC) * 0.5f * gain, (1f - omegaC) * gain, (1f - omegaC) * 0.5f * gain },
                    new[] { -2f * omegaC * gain, (1f - alpha) * gain });
            }

            public Filter WithScale(float scale)
            {
                var num = new float[Len];
                num[0] = scale * Num[0] + (1f - scale);
                for (int i = 1; i < Len; i++) num[i] = scale * Num[i] + (1f - scale) * Den[i - 1];
                var den = new float[Len - 1];
                Array.Copy(Den, den, Len - 1);
                return new Filter(num, den);
            }

            // this filter then `b` (the polynomials multiplied, as the engine's Mul does it)
            Filter Then(Filter b)
            {
                var num = new float[7];
                var den = new float[7];
                for (int i = 0; i < Len; i++)
                    for (int j = 0; j < b.Len; j++) num[i + j] += Num[i] * b.Num[j];
                for (int i = 0; i < Len; i++)
                    for (int j = 0; j < b.Len; j++) den[i + j] += (i == 0 ? 1f : Den[i - 1]) * (j == 0 ? 1f : b.Den[j - 1]);
                int numLen = 7, denLen = 7;
                while (numLen > 0 && num[numLen - 1] == 0) numLen--;
                while (denLen > 0 && den[denLen - 1] == 0) denLen--;
                var n = new float[numLen];
                Array.Copy(num, n, numLen);
                var d = new float[Math.Max(0, denLen - 1)];
                Array.Copy(den, 1, d, 0, d.Length);
                return new Filter(n, d);
            }

            public Filter CascadeSelf(int n)
            {
                Filter f = this;
                for (int i = 1; i < n; i++) f = f.Then(this);
                return f;
            }

            // the state that starts the filter as if every sample before the row were `value`
            void Initial(float value, float[] z)
            {
                Array.Clear(z, 0, 4);
                if (Math.Abs(value) == 0) return;
                float bSum = 0;
                for (int i = 1; i < Len; i++) bSum += Num[i] - Den[i - 1] * Num[0];
                float denSum = -0f;
                for (int i = 0; i < Len - 1; i++) denSum += Den[i];
                z[0] = bSum / (denSum + 1f);
                float aSum = 1, cSum = 0;
                for (int i = 1; i < Len - 1; i++)
                {
                    aSum += Den[i - 1];
                    cSum += Num[i] - Den[i - 1] * Num[0];
                    z[i] = (aSum * z[0] - cSum) * value;
                }
                z[0] *= value;
            }

            // A row in place, its output `delay` samples to the left (past the end it carries on
            // with the last sample), the way the engine's SIMD filter runs: the coefficients in 4
            // lanes, a multiply and an add each rounded.
            public void Row(float[] a, int o, int width, float initial, int delay)
            {
                if (width == 0) return;
                var z = new float[4];
                Initial(initial, z);
                float z0 = z[0], z1 = z[1], z2 = z[2], z3 = z[3];
                float n0 = Num[0], n1 = Num[1], n2 = Num[2], n3 = Num[3];
                float d0 = -Den[0], d1 = -Den[1], d2 = -Den[2], d3 = -Den[3];
                for (int i = 0; i < width + delay; i++)
                {
                    float x = a[o + Math.Min(i, width - 1)];
                    float f0 = n0 * x + z0, f1 = n1 * x + z1, f2 = n2 * x + z2, f3 = n3 * x + z3;
                    z0 = d0 * f0 + f1;
                    z1 = d1 * f0 + f2;
                    z2 = d2 * f0 + f3;
                    z3 = d3 * f0 + 0f;
                    if (i >= delay) a[o + i - delay] = f0;
                }
            }
        }

        // every row of a plane (the first sample's steady state when `fromFirst`, else 0)
        static void FilterPlane(float[] plane, int width, int rows, Filter filter, bool fromFirst, int delay) =>
            EveryCore.For(0, rows, r => filter.Row(plane, r * width, width, fromFirst ? plane[r * width] : 0f, delay));

        // ---- random numbers (random.rs): SplitMix64, forked by mixing in more seeds

        struct Rng
        {
            const ulong Phi = 0x9e3779b97f4a7c15;
            ulong state;

            public Rng(ulong seed) => state = seed;

            static uint Finish32(ulong s)
            {
                s = (s ^ (s >> 33)) * 0x62A9D9ED799705F5;
                s = (s ^ (s >> 28)) * 0xCB24D0A5C88C35B3;
                return (uint)(s >> 32);
            }

            static ulong Finish64(ulong s)
            {
                s = (s ^ (s >> 30)) * 0xbf58476d1ce4e5b9;
                s = (s ^ (s >> 27)) * 0x94d049bb133111eb;
                return s ^ (s >> 31);
            }

            // a new one with `input` mixed in (this one stays as it is)
            public Rng Mix(ulong input) => new Rng(Finish64(state + input + Phi));

            public uint U32()
            {
                state += Phi;
                return Finish32(state);
            }

            public ulong U64()
            {
                state += Phi;
                return Finish64(state);
            }

            public int I32() => (int)U32();
            public float F32() => (U32() >> 8) * F(0x33800000);       // × 2^-24
            public double F64() => (U64() >> 11) * (1.0 / 9007199254740992.0);
            public float Range(float lo, float hi) => lo + F32() * (hi - lo);

            // the gap to the next event, each sample one with probability p (lambda = ln(1 - p))
            public uint Geometric(double lambda) => RustMath.ToU32(RustMath.Log(F64()) / lambda);
        }

        // ---- noise (noise.rs, simplex.rs, grid.rs, hash.rs; adapted from Clatter, see the
        // notices): simplex noise and fractal noise over it, made 4 samples at a time, the
        // samples' places added up as the engine adds them

        static readonly float Skew = F(0x3ebb67ae), Unskew = F(0x3e58658c), Unskew2 = F(0x3ed8658c);

        static uint Hash1(uint v)
        {
            v = (v ^ 2747636419) * 2654435769;
            v = (v ^ (v >> 16)) * 2654435769;
            v = (v ^ (v >> 16)) * 2654435769;
            return v;
        }

        // the first of PCG3D's three
        static uint Pcg3d(uint vx, uint vy, uint vz)
        {
            vx = vx * 1664525 + 1013904223;
            vy = vy * 1664525 + 1013904223;
            vz = vz * 1664525 + 1013904223;
            vx += vy * vz;
            vy += vz * vx;
            vz += vx * vy;
            vx ^= vx >> 16;
            vy ^= vy >> 16;
            vz ^= vz >> 16;
            vx += vy * vz;
            return vx;
        }

        static float Floor(float x) => (float)Math.Floor(x);

        static float Simplex1(float x, int seed)
        {
            float i = Floor(x);
            int i0 = RustMath.ToI32(i), i1 = i0 + 1;
            float x0 = x - i, x1 = x0 - 1f;
            uint gi0 = Hash1((uint)(i0 ^ seed)), gi1 = Hash1((uint)(i1 ^ seed));
            float t0 = -x0 * x0 + 1f;
            float t20 = t0 * t0, t40 = t20 * t20;
            float t1 = -x1 * x1 + 1f;
            float t21 = t1 * t1, t41 = t21 * t21;
            return t40 * Gradient1(gi0) * x0 + t41 * Gradient1(gi1) * x1;
        }

        // a whole number from ±1 to ±8
        static float Gradient1(uint hash) => F(Bits((float)(int)(((hash >> 28) & 7) + 1)) | (hash & 0x80000000));

        static float Simplex2(float x, float y, int seed)
        {
            float s = (x + y) * Skew;
            float ips = Floor(x + s), jps = Floor(y + s);
            int i = RustMath.ToI32(ips), j = RustMath.ToI32(jps);
            float t = (float)(i + j) * Unskew;
            float x0 = x - (ips - t), y0 = y - (jps - t);
            int i1 = x0 >= y0 ? 1 : 0, j1 = y0 > x0 ? 1 : 0;
            float x1 = x0 - i1 + Unskew, y1 = y0 - j1 + Unskew;
            float x2 = x0 - 1f + Unskew2, y2 = y0 - 1f + Unskew2;
            uint g0 = Pcg3d((uint)i, (uint)j, (uint)seed), g1 = Pcg3d((uint)(i + i1), (uint)(j + j1), (uint)seed), g2 = Pcg3d((uint)(i + 1), (uint)(j + 1), (uint)seed);
            float w0 = Math.Max(y0 * -y0 + (x0 * -x0 + 0.5f), 0f), w1 = Math.Max(y1 * -y1 + (x1 * -x1 + 0.5f), 0f), w2 = Math.Max(y2 * -y2 + (x2 * -x2 + 0.5f), 0f);
            float w20 = w0 * w0, w40 = w20 * w20, w21 = w1 * w1, w41 = w21 * w21, w22 = w2 * w2, w42 = w22 * w22;
            float n0 = w40 * Gradient2(g0, x0, y0), n1 = w41 * Gradient2(g1, x1, y1), n2 = w42 * Gradient2(g2, x2, y2);
            return n0 + n1 + n2;
        }

        // the gradient (±1 or ±2 across, ±2 or ±1 down) dotted with (x, y)
        static float Gradient2(uint hash, float x, float y)
        {
            uint h = hash & 7;
            bool low = h < 4, even1 = (h & 1) == 0, even2 = (h & 2) == 0;
            float xm = low ? 1f : 2f, ym = low ? 2f : 1f;
            float gx = (low ? even1 : even2) ? xm : -xm, gy = (low ? even2 : even1) ? ym : -ym;
            return gx * x + gy * y;
        }

        // fractal noise: octaves of simplex noise, each `lacunarity` × the frequency, `gain` × as strong
        static float Fbm1(float x, int seed, int octaves, float gain, float lacunarity, float frequency)
        {
            x = x * frequency;
            float result = Simplex1(x, seed), amplitude = gain;
            for (int o = 1; o < octaves; o++)
            {
                x = x * lacunarity;
                result = Simplex1(x, seed) * amplitude + result;
                amplitude *= gain;
            }
            return result;
        }

        static float Fbm2(float x, float y, int seed, int octaves, float gain, float lacunarity, float frequency)
        {
            x = x * frequency;
            y = y * frequency;
            float result = Simplex2(x, y, seed), amplitude = gain;
            for (int o = 1; o < octaves; o++)
            {
                x = x * lacunarity;
                y = y * lacunarity;
                result = Simplex2(x, y, seed) * amplitude + result;
                amplitude *= gain;
            }
            return result;
        }

        // Noise at `count` samples from `start`, 1 apart: 4 at a time, each lane's place moving on
        // by 4 (a float sum, so far along the places are the engine's, not start + n).
        static void Noise1(float start, int count, Func<float, float> noise, Action<int, float> take)
        {
            var at = new float[4];
            for (int l = 0; l < 4; l++) at[l] = start + l;
            for (int p = 0; p < count; p += 4)
            {
                for (int l = 0; l < 4 && p + l < count; l++) take(p + l, noise(at[l]));
                for (int l = 0; l < 4; l++) at[l] += 4f;
            }
        }

        // a setting's octaves of noise, 1 to 5 (a negative number counts as 0)
        static int Octaves(int detail) => Math.Max(1, Math.Min(5, detail));

        // ---- shifting a row by a fraction of a sample (shift.rs), linear between its neighbours:
        // the engine does whole blocks of 4 one way ((l - r) × frac + r) and the edges another
        // (l × frac + r × (1 - frac))

        [ThreadStatic] static float[] shiftCopy;

        static void ShiftRow(float[] a, int o, int width, float shift, bool extend)
        {
            if (shiftCopy == null || shiftCopy.Length < width) shiftCopy = new float[Math.Max(width, 256)];
            Array.Copy(a, o, shiftCopy, 0, width);
            ShiftRowTo(shiftCopy, 0, a, o, width, shift, extend);
        }

        static void ShiftRowTo(float[] src, int so, float[] dst, int dOff, int width, float shift, bool extend)
        {
            if (width == 0) return;
            int shiftInt = RustMath.ToI32(shift) - (shift < 0 ? 1 : 0);
            float boundary = !extend ? 0f : shiftInt >= 0 ? src[so] : src[so + width - 1];
            float frac = shift - shiftInt;
            // the blocks of 4 that need nothing past either end
            int safeStart = 0, simdEnd = 0;
            if (width >= 4)
            {
                int start = Math.Max(shiftInt + 1, 0), upper = width - 4 + Math.Min(shiftInt, 0);
                if (upper >= start)
                {
                    safeStart = start;
                    simdEnd = start + ((upper - start) / 4 + 1) * 4;
                }
            }
            for (int i = 0; i < width; i++)
            {
                int left = i - shiftInt - 1, right = left + 1;
                if (i >= safeStart && i < simdEnd)
                {
                    float l = src[so + left], r = src[so + right];
                    dst[dOff + i] = (l - r) * frac + r;
                }
                else
                {
                    float l = left >= 0 && left < width ? src[so + left] : boundary;
                    float r = right >= 0 && right < width ? src[so + right] : boundary;
                    dst[dOff + i] = l * frac + r * (1f - frac);
                }
            }
        }

        // ---- the picture in YIQ (yiq_fielding.rs): planes of Y, I and Q, and a scratch plane

        sealed class Field
        {
            public int Width, Rows;
            public float[] Y, I, Q, S;

            public Field(int width, int rows)
            {
                Width = width;
                Rows = rows;
                Y = new float[width * rows];
                I = new float[width * rows];
                Q = new float[width * rows];
                S = new float[width * rows];
            }
        }

        static readonly float Inv255 = F(0x3b808081);
        static readonly float Ry = F(0x3e991687), Gy = F(0x3f1645a2), By = F(0x3de978d5), Ri = F(0x3f188ce7), Gi = F(0xbe8c985f), Bi = F(0xbea4816f),
            Rq = F(0x3e589375), Gq = F(0xbf05cfab), Bq = F(0x3e9f559b);
        static readonly float Qr = F(0x3f1e76c9), Ir = F(0x3f74bc6a), Qg = F(0xbf25a1cb), Ig = F(0xbe8b4396), Qb = F(0x3fd9fbe7), Ib = F(0xbf8d9168);

        static byte FromNorm(float v)
        {
            float t = v * 255f;
            if (float.IsNaN(t)) return 0;
            if (t > 255f) t = 255f;
            if (t < 0f) t = 0f;
            return (byte)(int)t;
        }

        // the rows a field keeps of a picture `height` lines tall (num_image_rows), and of those
        // the ones the picture really has (num_actual_image_rows: none for the lower field of a
        // one-line picture)
        static int UpperRows(int height) => (height + 1) / 2;
        static int LowerRows(int height) => Math.Max(1, height / 2);

        // which field a frame uses (UseField::to_yiq_field): alternating fields take the lower on even frames
        static UseField FieldOf(UseField use, uint frame) => use != UseField.Alternating ? use : (frame & 1) == 0 ? UseField.Lower : UseField.Upper;

        // ---- one frame: the website's wrapper (signal/src/lib.rs)

        const int HalfCycle = 2;

        // Does this line's colour carrier come flipped (the engine's own rule for one field a frame)?
        static bool CarrierFlipped(uint frame, int line) => ((frame + 2 * (uint)line) & 2) != 0;

        // a part of the picture the engine runs on: a field (or the whole of it), the source rows it
        // holds (every one, or every other one from `first`), and its frame number
        sealed class Part
        {
            public Field Field;
            public int First, Step;
            public uint Frame;
        }

        // One frame of `rgb` (width × height, r, g, b a pixel) through the engine, back as r, g, b.
        // `frame`: which frame (its noise; sums wrap at 32 bits, as the engine's usize does in
        // WebAssembly); `chroma`: the colour after decoding (1 = as decoded); `lineLocked`: the
        // same carrier phase on every line (machines with 228 colour cycles a line, one field a
        // frame only): lines whose carrier the engine flips go in half a cycle to the right and
        // come back; `pad`: samples of the edge colour each side while it runs (its filters start
        // from 0).
        public static byte[] Frame(byte[] rgb, int width, int height, uint frame, NtscSettings s, float chroma, bool lineLocked, int pad)
        {
            bool locked = lineLocked && s.UseField == UseField.Both;
            pad = Math.Max(pad, locked ? HalfCycle : 0);
            int pw = width + 2 * pad;
            UseField field = FieldOf(s.UseField, frame);

            // the parts, in the order their rows sit in the engine's buffer
            Part P(int rows, int first, int step, uint number) => new Part { Field = new Field(pw, rows), First = first, Step = step, Frame = number };
            Part[] parts;
            switch (field)
            {
                case UseField.Upper:
                    parts = new[] { P(UpperRows(height), 0, 2, frame) };
                    break;
                case UseField.Lower:
                    parts = new[] { P(LowerRows(height), height == 1 ? 0 : 1, 2, frame) };
                    break;
                case UseField.InterleavedUpper:
                    parts = height / 2 > 0 ? new[] { P(UpperRows(height), 0, 2, frame * 2), P(height / 2, 1, 2, frame * 2 + 1) } : new[] { P(UpperRows(height), 0, 2, frame * 2) };
                    break;
                case UseField.InterleavedLower:
                    parts = height / 2 > 0 ? new[] { P(height / 2, 1, 2, frame * 2), P(UpperRows(height), 0, 2, frame * 2 + 1) } : new[] { P(UpperRows(height), 0, 2, frame * 2 + 1) };
                    break;
                default:
                    parts = new[] { P(height, 0, 1, frame) };
                    break;
            }

            // into YIQ
            var norm = new float[256];
            for (int v = 0; v < 256; v++) norm[v] = v * Inv255;
            foreach (Part part in parts)
            {
                Field f = part.Field;
                EveryCore.For(0, f.Rows, row =>
                {
                    int y = height == 1 ? 0 : part.First + row * part.Step;
                    int start = pad + (locked && CarrierFlipped(frame, y) ? HalfCycle : 0);
                    for (int x = 0; x < pw; x++)
                    {
                        int sx = Math.Min(width - 1, Math.Max(0, x - start)), si = (y * width + sx) * 3, o = row * pw + x;
                        float r = norm[rgb[si]], g = norm[rgb[si + 1]], b = norm[rgb[si + 2]];
                        f.Y[o] = r * Ry + (g * Gy + b * By);
                        f.I[o] = r * Ri + (g * Gi + b * Bi);
                        f.Q[o] = r * Rq + (g * Gq + b * Bq);
                    }
                });
            }

            foreach (Part part in parts) Effect(part.Field, part.Frame, s, height);

            if (chroma != 1f)
                foreach (Part part in parts)
                {
                    float[] i = part.Field.I, q = part.Field.Q;
                    for (int k = 0; k < i.Length; k++)
                    {
                        i[k] *= chroma;
                        q[k] *= chroma;
                    }
                }

            // back out: the engine's buffer row each picture row shows (Bob: the rows a single field
            // skips are the mean of the rows above and below)
            int bufferRows = 0;
            foreach (Part part in parts) bufferRows += part.Field.Rows;
            (Field, int) BufferRow(int k)
            {
                foreach (Part part in parts)
                {
                    if (k < part.Field.Rows) return (part.Field, k);
                    k -= part.Field.Rows;
                }
                return (parts[parts.Length - 1].Field, parts[parts.Length - 1].Field.Rows - 1);
            }
            var outRgb = new byte[width * height * 3];
            EveryCore.For(0, height, y =>
            {
                int start = pad + (locked && CarrierFlipped(frame, y) ? HalfCycle : 0);
                Field f, f2 = null;
                int row, row2 = 0;
                switch (field)
                {
                    case UseField.Upper:
                    case UseField.Lower:
                        {
                            Field only = parts[0].Field;
                            int skip = field == UseField.Upper ? 1 : 0;
                            if ((y & 1) == skip && y != 0 && y != height - 1)
                            {
                                f = only;
                                row = (y + 1) >> 1;
                                f2 = only;
                                row2 = (y - 1) >> 1;
                            }
                            else
                            {
                                f = only;
                                row = Math.Min(y >> 1, only.Rows - 1);
                            }
                            break;
                        }
                    case UseField.InterleavedUpper:
                        (f, row) = BufferRow(Math.Min((y >> 1) + UpperRows(height) * (y & 1), height - 1));
                        break;
                    case UseField.InterleavedLower:
                        (f, row) = BufferRow(Math.Min((y >> 1) + LowerRows(height) * (1 - (y & 1)), height - 1));
                        break;
                    default:
                        f = parts[0].Field;
                        row = Math.Min(y, f.Rows - 1);
                        break;
                }
                for (int x = 0; x < width; x++)
                {
                    int o = row * pw + start + x, d = (y * width + x) * 3;
                    float yy = f.Y[o], ii = f.I[o], qq = f.Q[o];
                    if (f2 != null)
                    {
                        int o2 = row2 * pw + start + x;
                        yy = (yy + f2.Y[o2]) * 0.5f;
                        ii = (ii + f2.I[o2]) * 0.5f;
                        qq = (qq + f2.Q[o2]) * 0.5f;
                    }
                    outRgb[d] = FromNorm(qq * Qr + (ii * Ir + yy));
                    outRgb[d + 1] = FromNorm(qq * Qg + (ii * Ig + yy));
                    outRgb[d + 2] = FromNorm(qq * Qb + (ii * Ib + yy));
                }
            });
            return outRgb;
        }

        // The tape copied again and again ("Dub it N times"): `generations` runs, each one's picture
        // the next one's input and each with its own noise (the seed plus the generation); the colour
        // after decoding only on the last, the TV it's played on.
        public static byte[] Dub(byte[] rgb, int width, int height, uint frame, NtscSettings s, float chroma, bool lineLocked, int pad, int generations)
        {
            byte[] tape = rgb;
            for (int g = 0; g < Math.Max(1, generations); g++)
            {
                NtscSettings pass = s;
                if (g > 0)
                {
                    pass = s.Clone();
                    pass.RandomSeed = unchecked(s.RandomSeed + g);
                }
                tape = Frame(tape, width, height, frame, pass, g == Math.Max(1, generations) - 1 ? chroma : 1f, lineLocked, pad);
            }
            return tape;
        }

        // ---- the effect (ntsc.rs), on one field

        static readonly float[] iMult = { 1, 0, -1, 0 }, qMult = { 0, 1, 0, -1 }, iMultInv = { -1, 0, 1, 0 }, qMultInv = { 0, -1, 0, 1 };

        // the colour carrier's phase on a line (0 or 2 samples)
        static int CarrierPhase(uint frame, int line) => (int)((frame + ((uint)line << 1)) & 2);

        // `fullHeight`: the whole picture's lines (a field holds about half of them)
        static void Effect(Field f, uint frame, NtscSettings s, int fullHeight)
        {
            int w = f.Width, rows = f.Rows;
            ulong seed = (uint)s.RandomSeed;
            float videoScale = s.Scale && s.ScaleWithVideoSize ? (float)fullHeight / 480f : 1f;
            float hs = 1f * videoScale * (s.Scale ? s.HorizontalScale : 1f);
            float vs = 1f * videoScale * (s.Scale ? s.VerticalScale : 1f);

            // the brightness filtered on its way in
            if (s.InputLumaFilter == LumaLowpass.Box) BoxLuma(f);
            else if (s.InputLumaFilter == LumaLowpass.Notch) FilterPlane(f.Y, w, rows, Filter.Notch(0.5f, 2f), true, 0);

            BlurColour(f, s.ChromaLowpassIn, s.FilterType, hs);

            // the colour onto the carrier, into the one signal
            EveryCore.For(0, rows, r =>
            {
                int xi = CarrierPhase(frame, r), o = r * w;
                for (int p = 0; p < w; p++)
                {
                    int phase = (p + xi) & 3;
                    f.Y[o + p] += f.I[o + p] * iMult[phase] + f.Q[o + p] * qMult[phase];
                }
            });

            if (s.CompositePreemphasis != 0)
                FilterPlane(f.Y, w, rows, Filter.Lowpass(PreemphasisCutoff * hs, NtscRate * hs).WithScale(-s.CompositePreemphasis), false, 0);

            if (s.CompositeNoise)
            {
                Rng composite = new Rng(seed).Mix(0).Mix((ulong)frame);
                float frequency = s.CompositeNoiseFrequency / hs;
                int octaves = Octaves(s.CompositeNoiseDetail);
                EveryCore.For(0, rows, r => NoiseLine(f.Y, r * w, w, composite, (uint)r, frequency, s.CompositeNoiseIntensity, octaves));
            }

            if (s.Snow > 0 && hs > 0)
            {
                Rng snow = new Rng(seed).Mix(6).Mix((ulong)frame);
                float intensity = s.Snow * 0.01f;
                EveryCore.For(0, rows, r => Speckles(f.Y, r * w, w, snow.Mix((ulong)r), intensity, s.SnowAnisotropy, hs));
            }

            if (s.HeadSwitching) HeadSwitching(f, frame, s, seed, hs, vs);
            if (s.TrackingNoise) TrackingNoise(f, frame, s, seed, hs, vs);

            LumaIntoChroma(f, frame, s.ChromaDemodulation);

            if (s.LumaSmear > 0) FilterPlane(f.Y, w, rows, Filter.Lowpass(RustMath.Exp2f(-4f * s.LumaSmear) * 0.25f, hs), false, 0);

            if (s.Ringing)
            {
                float frequency = Math.Max(0f, Math.Min(1f, s.RingingFrequency / hs));
                FilterPlane(f.Y, w, rows, Filter.Notch(frequency, s.RingingPower).WithScale(s.RingingScale), true, 1);
            }

            if (s.LumaNoise) PlaneNoise(f.Y, w, rows, seed, 10, frame, s.LumaNoiseFrequency / hs, s.LumaNoiseIntensity, s.LumaNoiseDetail);
            if (s.ChromaNoise)
            {
                PlaneNoise(f.I, w, rows, seed, 1, frame, s.ChromaNoiseFrequency / hs, s.ChromaNoiseIntensity, s.ChromaNoiseDetail);
                PlaneNoise(f.Q, w, rows, seed, 9, frame, s.ChromaNoiseFrequency / hs, s.ChromaNoiseIntensity, s.ChromaNoiseDetail);
            }

            if (s.ChromaPhaseError > 0) EveryCore.For(0, rows, r => PhaseLine(f, r, s.ChromaPhaseError));

            if (s.ChromaPhaseNoise > 0)
            {
                Rng phase = new Rng(seed).Mix(4).Mix((ulong)frame);
                EveryCore.For(0, rows, r =>
                {
                    float shift = (phase.Mix((ulong)r).F32() - 0.5f) * 2f * s.ChromaPhaseNoise;
                    PhaseLine(f, r, shift);
                });
            }

            if (s.ChromaDelayHorizontal != 0 || s.ChromaDelayVertical != 0) ChromaDelay(f, s.ChromaDelayHorizontal, s.ChromaDelayVertical, hs, vs);

            if (s.Vhs)
            {
                if (s.EdgeWave && s.EdgeWaveIntensity > 0) EdgeWave(f, frame, s, seed, hs, vs);

                // the tape's bandwidth (none: no tape filters, and no sharpening)
                float lumaCut = 0, chromaCut = 0;
                int chromaDelay = 0;
                bool tape = true;
                switch (s.TapeSpeed)
                {
                    case TapeSpeed.SP: lumaCut = 2400000; chromaCut = 320000; chromaDelay = 4; break;
                    case TapeSpeed.LP: lumaCut = 1900000; chromaCut = 300000; chromaDelay = 5; break;
                    case TapeSpeed.EP: lumaCut = 1400000; chromaCut = 280000; chromaDelay = 6; break;
                    default: tape = false; break;
                }
                if (tape)
                {
                    int delay = (int)RustMath.ToU32(RustMath.Round(chromaDelay * hs));
                    FilterPlane(f.Y, w, rows, Filter.LowpassFor(lumaCut, NtscRate * hs, s.FilterType), false, 0);
                    Filter chromaFilter = Filter.LowpassFor(chromaCut, NtscRate * hs, s.FilterType);
                    FilterPlane(f.I, w, rows, chromaFilter, false, delay);
                    FilterPlane(f.Q, w, rows, chromaFilter, false, delay);
                    FilterPlane(f.Y, w, rows, Filter.Lowpass(lumaCut, NtscRate * hs).WithScale(-1.6f), false, 0);
                }

                if (s.ChromaLoss > 0)
                {
                    Rng loss = new Rng(seed).Mix(7).Mix((ulong)frame);
                    double lambda = RustMath.Log(1.0 - s.ChromaLoss);
                    uint row = 0;
                    while (true)
                    {
                        row += loss.Geometric(lambda);
                        if (row >= rows) break;
                        Array.Clear(f.I, (int)row * w, w);
                        Array.Clear(f.Q, (int)row * w, w);
                        row++;
                    }
                }

                // sharpened (constant-k filters take 4× the frequency, Butterworth ones 1×)
                if (s.Sharpen && tape)
                {
                    float extra = s.FilterType == FilterType.ConstantK ? 4f : 1f;
                    FilterPlane(f.Y, w, rows, Filter.LowpassFor(lumaCut * extra * s.SharpenFrequency, NtscRate * hs, s.FilterType)
                        .WithScale(-s.SharpenIntensity * 2f * s.SharpenFrequency), false, 0);
                }
            }

            if (s.ChromaVertBlend) ChromaVertBlend(f, fullHeight);

            BlurColour(f, s.ChromaLowpassOut, s.FilterType, hs);
        }

        // the colour blurred: full (I to 1.3 MHz, Q to 0.6 MHz) or light (both to 2.6 MHz)
        static void BlurColour(Field f, ChromaLowpass mode, FilterType type, float hs)
        {
            if (mode == ChromaLowpass.Full)
            {
                FilterPlane(f.I, f.Width, f.Rows, Filter.LowpassFor(1300000f, NtscRate * hs, type), false, 2);
                FilterPlane(f.Q, f.Width, f.Rows, Filter.LowpassFor(600000f, NtscRate * hs, type), false, 4);
            }
            else if (mode == ChromaLowpass.Light)
            {
                Filter filter = Filter.LowpassFor(2600000f, NtscRate * hs, type);
                FilterPlane(f.I, f.Width, f.Rows, filter, false, 1);
                FilterPlane(f.Q, f.Width, f.Rows, filter, false, 1);
            }
        }

        // the brightness through a box filter 4 samples wide, starting from black (luma_filter's Box)
        static void BoxLuma(Field f)
        {
            int w = f.Width;
            if (w < 2) return;
            EveryCore.For(0, f.Rows, r =>
            {
                float[] y = f.Y;
                int o = r * w, head = 0;
                var queue = new[] { BoxBlack, BoxBlack, y[o], y[o + 1] };
                float sum = queue[0] + queue[1] + queue[2] + queue[3];
                float Push(float c)
                {
                    float old = queue[head];
                    queue[head] = c;
                    head = (head + 1) & 3;
                    return old;
                }
                for (int i = 0; i < w - 2; i++)
                {
                    float c = y[o + i + 2];
                    sum -= Push(c);
                    sum += c;
                    y[o + i] = sum * 0.25f;
                }
                float last = y[o + w - 1];
                for (int i = w - 2; i < w; i++)
                {
                    sum -= Push(last);
                    sum += last;
                    y[o + i] = sum * 0.25f;
                }
            });
        }

        // The colour taken back off the carrier, by the setting's decoder (the comb and 2D ones
        // need 2 lines, the 2D ones 3 samples across; short of that it's the notch).
        static void LumaIntoChroma(Field f, uint frame, ChromaDemodulation mode)
        {
            int w = f.Width, rows = f.Rows;
            bool lines = mode == ChromaDemodulation.OneLineComb || mode == ChromaDemodulation.TwoLineComb || mode == ChromaDemodulation.TwoD || mode == ChromaDemodulation.TwoDAdaptive;
            if (rows == 1 && lines || w < 3 && (mode == ChromaDemodulation.TwoD || mode == ChromaDemodulation.TwoDAdaptive)) mode = ChromaDemodulation.Notch;
            float[] y = f.Y, m = f.S;
            Array.Copy(y, m, w * rows);
            switch (mode)
            {
                case ChromaDemodulation.Box:
                    EveryCore.For(0, rows, r =>
                    {
                        int o = r * w;
                        for (int p = 0; p < w; p++)
                        {
                            float a0 = p > 0 ? m[o + p - 1] : BoxBlack;
                            y[o + p] = (a0 + m[o + p] + m[o + Math.Min(p + 1, w - 1)] + m[o + Math.Min(p + 2, w - 1)]) * 0.25f;
                        }
                        Demodulate(f, r, CarrierPhase(frame, r));
                    });
                    break;
                case ChromaDemodulation.OneLineComb:
                    EveryCore.For(0, rows, r =>
                    {
                        // the line before (the first line takes the second: the carrier must flip between them)
                        int o = r * w, top = (r == 0 ? 1 : r - 1) * w;
                        for (int p = 0; p < w; p++) y[o + p] = (m[top + p] + m[o + p]) * 0.5f;
                        Demodulate(f, r, CarrierPhase(frame, r));
                    });
                    break;
                case ChromaDemodulation.TwoLineComb:
                    EveryCore.For(0, rows, r =>
                    {
                        int prev = r == 0 ? 1 : r - 1, next = r == rows - 1 ? rows - 2 : r + 1;
                        int o = r * w, po = prev * w, no = next * w;
                        for (int p = 0; p < w; p++) y[o + p] = m[o + p] * 0.5f + m[po + p] * 0.25f + m[no + p] * 0.25f;
                        Demodulate(f, r, CarrierPhase(frame, r));
                    });
                    break;
                case ChromaDemodulation.TwoD:
                case ChromaDemodulation.TwoDAdaptive:
                    {
                        bool adaptive = mode == ChromaDemodulation.TwoDAdaptive;
                        EveryCore.For(0, rows, r =>
                        {
                            int prev = r == 0 ? 1 : r - 1, next = r == rows - 1 ? rows - 2 : r + 1;
                            int o = r * w, po = prev * w, no = next * w;
                            for (int p = 0; p < w; p++)
                            {
                                int pl = p < 2 ? p + 2 : p - 2, pr = p > w - 3 ? p - 2 : p + 2;
                                float tl = m[po + pl], t = m[po + p], tr = m[po + pr], l = m[o + pl], c = m[o + p], rr = m[o + pr], bl = m[no + pl], b = m[no + p], br = m[no + pr];
                                y[o + p] = adaptive ? Adaptive2D(tl, t, tr, l, c, rr, bl, b, br) : c * 0.75f + ((l + rr) + (t + b)) * 0.125f - ((tl + tr) + (bl + br)) * 0.0625f;
                            }
                            Demodulate(f, r, CarrierPhase(frame, r));
                        });
                        break;
                    }
                default:
                    FilterPlane(y, w, rows, Filter.Notch(0.5f, 2f), false, 0);
                    EveryCore.For(0, rows, r => Demodulate(f, r, CarrierPhase(frame, r)));
                    break;
            }
        }

        // 1/0.001, 1/(0.001 × √2) and 2/0.001², as the engine's floats
        static readonly float InvThreshold = F(0x4479ffff), InvThresholdDiag = F(0x4430c6d6), DiagLimit = F(0x49f423fe);

        // the edge-following 2D decoder's one sample: weights that follow the edges around it
        static float Adaptive2D(float tl, float t, float tr, float l, float c, float r, float bl, float b, float br)
        {
            float gx = (tr - tl) + (r - l) + (br - bl);
            float gy = (bl - tl) + (b - t) + (br - tr);
            float gxT = gx * InvThreshold, gyT = gy * InvThreshold;
            float gd1T = (gx + gy) * InvThresholdDiag, gd2T = (gx - gy) * InvThresholdDiag;
            float ax = 1f / (1f + gxT * gxT), ay = 1f / (1f + gyT * gyT);
            float norm = 0.25f / (ax + ay);
            float wx = ax * norm, wy = ay * norm;
            float wd1 = norm / Math.Max(1f + gd1T * gd1T, DiagLimit), wd2 = norm / Math.Max(1f + gd2T * gd2T, DiagLimit);
            float wCenter = 0.5f + 2f * (wd1 + wd2);
            return c * wCenter + ((l + r) * wx + (t + b) * wy) - ((tl + br) * wd1 + (tr + bl) * wd2);
        }

        // I and Q from a line's chroma (the signal less its luma, in S), each sample with half its neighbours'
        static void Demodulate(Field f, int r, int xi)
        {
            int w = f.Width, o = r * w;
            float[] y = f.Y, m = f.S;
            for (int p = 0; p < w; p++)
            {
                int oc = (p + xi) & 3;
                float c = y[o + p] - m[o + p];
                float iv = c * iMultInv[oc], qv = c * qMultInv[oc];
                if (p < w - 1)
                {
                    int or = (p + 1 + xi) & 3;
                    float cr = y[o + p + 1] - m[o + p + 1];
                    iv += cr * iMultInv[or] * 0.5f;
                    qv += cr * qMultInv[or] * 0.5f;
                }
                if (p > 0)
                {
                    int ol = (p - 1 + xi) & 3;
                    float cl = y[o + p - 1] - m[o + p - 1];
                    iv += cl * iMultInv[ol] * 0.5f;
                    qv += cl * qMultInv[ol] * 0.5f;
                }
                f.I[o + p] = iv;
                f.Q[o + p] = qv;
            }
        }

        // a line's noise: fractal simplex noise from a place of its own (video_noise_line)
        static void NoiseLine(float[] a, int o, int width, Rng rng, uint index, float frequency, float intensity, int detail)
        {
            Rng r = rng.Mix(index);
            int seed = r.I32();
            float offset = r.F32() * width;
            int octaves = Octaves(detail);
            float scale = intensity * 0.25f;
            Noise1(offset, width, x => Fbm1(x, seed, octaves, 1f, 2f, frequency), (p, n) => a[o + p] = a[o + p] + n * scale);
        }

        // noise over a whole plane, each line its own (plane_noise)
        static void PlaneNoise(float[] plane, int w, int rows, ulong seed, ulong which, uint frame, float frequency, float intensity, int detail)
        {
            Rng rng = new Rng(seed).Mix(which).Mix((ulong)frame);
            EveryCore.For(0, rows, r => NoiseLine(plane, r * w, w, rng, (uint)r, frequency, intensity, detail));
        }

        // snow on a line: short bright transients, clumped along some lines more than others (row_speckles)
        static void Speckles(float[] a, int o, int width, Rng rng, float intensityF, float anisotropyF, float hs)
        {
            double intensity = intensityF, anisotropy = anisotropyF;
            double logistic = RustMath.Exp((rng.F64() - intensity) / (intensity * (1.0 - intensity) * (1.0 - anisotropy)));
            double line = anisotropy / (1.0 + logistic) + intensity * (1.0 - anisotropy);
            line *= 0.125;
            if (line < 0) line = 0;
            if (line > 1) line = 1;
            if (line <= 0) return;
            double lambda = RustMath.Log(1.0 - line);
            // from 64 samples to the left, so transients that started before the line show too
            int pixel = -64;
            while (true)
            {
                pixel += (int)Math.Min(rng.Geometric(lambda), (uint)int.MaxValue);
                if (pixel >= width) break;
                float length = rng.Range(8f, 64f) * hs;
                float freq = rng.Range(length * 3f, length * 5f);
                int end = pixel + RustMath.ToI32(Math.Ceiling(length));
                var transient = new Rng(rng.U64());
                int from = Math.Max(0, Math.Min(width, pixel)), to = Math.Max(0, Math.Min(width, end));
                for (int i = from; i < to; i++)
                {
                    float x = i - pixel;
                    float fade = 1f - x / length;
                    a[o + i] += RustMath.Cosf(x * Pi / freq) * (fade * fade) * transient.Range(-1f, 2f);
                }
                pixel++;
            }
        }

        // the colour turned round by `offset` turns (chroma_phase_offset_line)
        static void PhaseLine(Field f, int r, float offset)
        {
            float angle = offset * Pi * 2f;
            float sin = RustMath.Sinf(angle), cos = RustMath.Cosf(angle);
            int o = r * f.Width;
            for (int p = 0; p < f.Width; p++)
            {
                float i = f.I[o + p], q = f.Q[o + p];
                f.I[o + p] = i * cos - q * sin;
                f.Q[o + p] = i * sin + q * cos;
            }
        }

        // a setting's count of lines scaled to the picture (`(n as f32 * scale).round() as usize`)
        static uint ScaledLines(int n, float scale) => RustMath.ToU32(RustMath.Round((float)(uint)Math.Max(0, n) * scale));

        // VHS head switching: the bottom lines pulled sideways, the top one (if set) from mid-line
        static void HeadSwitching(Field f, uint frame, NtscSettings s, ulong seed, float hs, float vs)
        {
            uint numRows = ScaledLines(s.HeadSwitchingHeight, vs), offset = ScaledLines(s.HeadSwitchingOffset, vs);
            if (offset > numRows) return;
            uint affected = numRows - offset;
            int w = f.Width, height = f.Rows;
            uint startRow = Math.Max((uint)height, affected) - affected, cutOff = affected > height ? affected - (uint)height : 0;
            Rng rng = new Rng(seed).Mix(2).Mix((ulong)frame);
            EveryCore.For((int)startRow, height, r =>
            {
                uint index = affected - ((uint)r - startRow + cutOff);
                float rowShift = s.HeadSwitchingShift * RustMath.Powf((float)(index + offset) / numRows, 1.5f);
                float shift = (rowShift + (rng.Mix(index).F32() - 0.5f)) * hs;
                int o = r * w;
                if (index != affected || !s.HeadSwitchingMidLine)
                {
                    ShiftRow(f.Y, o, w, shift, false);
                    return;
                }
                // the whole line shifted, but only from mid-line on
                ShiftRowTo(f.Y, o, f.S, o, w, shift, false);
                Rng mid = new Rng(seed).Mix(8).Mix((ulong)frame);
                float jitterRand = (mid.F32() + mid.F32()) * 0.5f;
                float jitter = (jitterRand - 0.5f) * s.MidLineJitter;
                uint copyStart = RustMath.ToU32((float)w * (s.MidLinePosition + jitter));
                if (copyStart > w) return;
                Array.Copy(f.S, o + (int)copyStart, f.Y, o + (int)copyStart, w - (int)copyStart);
                // and a transient where the switch starts
                float transientIntensity = (mid.F32() + 0.5f) * 0.5f;
                float length = 16f * hs;
                int end = (int)Math.Min(copyStart + RustMath.ToU32(Math.Ceiling(length)), (uint)w);
                for (int i = (int)copyStart; i < end; i++)
                {
                    float x = i - (int)copyStart;
                    float fade = 1f - x / length;
                    f.Y[o + i] += fade * (fade * fade) * transientIntensity;
                }
            });
        }

        // VHS tracking noise: the bottom lines waver, with noise and snow, more toward the bottom
        static void TrackingNoise(Field f, uint frame, NtscSettings s, ulong seed, float hs, float vs)
        {
            uint numRows = ScaledLines(s.TrackingNoiseHeight, vs);
            int w = f.Width, height = f.Rows;
            Rng rng = new Rng(seed).Mix(3).Mix((ulong)frame);
            int noiseSeed = rng.I32();
            float offset = rng.F32() * height;
            uint startRow = Math.Max((uint)height, numRows) - numRows, cutOff = numRows > height ? numRows - (uint)height : 0;
            int count = (int)Math.Min(numRows, (uint)height);
            // the waver, one value a line (simplex noise at frequency 0.5), in S
            Noise1(offset, count, x => Simplex1(x * 0.5f, noiseSeed), (p, n) => f.S[p] = n);
            EveryCore.For((int)startRow, height, r =>
            {
                uint index = (uint)r - startRow + cutOff;
                float scale = (float)index / numRows;
                int o = r * w;
                ShiftRow(f.Y, o, w, f.S[index - cutOff] * scale * s.TrackingWave * 0.25f * hs, false);
                NoiseLine(f.Y, o, w, rng, index, 0.25f / hs, scale * scale * s.TrackingNoiseIntensity * 4f, 1);
                Speckles(f.Y, o, w, rng.Mix(index), s.TrackingSnow * (scale * scale), s.TrackingSnowAnisotropy, hs);
            });
        }

        // The colour moved sideways by a fraction of a sample and up or down by whole lines (chroma_delay).
        static void ChromaDelay(Field f, float horizontal, int vertical, float hs, float vs)
        {
            float across = horizontal * hs;
            int down = RustMath.ToI32(RustMath.Round((float)vertical * vs));
            int w = f.Width, rows = f.Rows;
            if (down == 0)
            {
                EveryCore.For(0, rows, r =>
                {
                    ShiftRow(f.I, r * w, w, across, false);
                    ShiftRow(f.Q, r * w, w, across, false);
                });
                return;
            }
            void Move(int from, int to)
            {
                if (Math.Abs(across) == 0)
                {
                    Array.Copy(f.I, from * w, f.I, to * w, w);
                    Array.Copy(f.Q, from * w, f.Q, to * w, w);
                }
                else
                {
                    ShiftRowTo(f.I, from * w, f.I, to * w, w, across, false);
                    ShiftRowTo(f.Q, from * w, f.Q, to * w, w, across, false);
                }
            }
            if (down < 0)
            {
                // up: from the top down, each line from `up` lines below it (none left: no colour)
                int up = -down;
                for (int row = 0; row < rows; row++)
                {
                    if (row >= Math.Max(rows, up) - up)
                    {
                        Array.Clear(f.I, row * w, w);
                        Array.Clear(f.Q, row * w, w);
                    }
                    else Move(row + up, row);
                }
            }
            else
            {
                for (int row = rows - 1; row >= 0; row--)
                {
                    if (row < down)
                    {
                        Array.Clear(f.I, row * w, w);
                        Array.Clear(f.Q, row * w, w);
                    }
                    else Move(row - down, row);
                }
            }
        }

        // the VHS edge wave: every line moved sideways by fractal noise down the picture, which drifts with the frames
        static void EdgeWave(Field f, uint frame, NtscSettings s, ulong seed, float hs, float vs)
        {
            int w = f.Width, height = f.Rows;
            Rng rng = new Rng(seed).Mix(5);
            int noiseSeed = rng.I32();
            float offset = rng.F32() * height;
            float frequency = s.EdgeWaveFrequency / vs, time = frame * s.EdgeWaveSpeed;
            int octaves = Octaves(s.EdgeWaveDetail);
            Noise1(offset, height, x => Fbm2(x, time, noiseSeed, octaves, FracSqrt2, 2f, frequency), (p, n) => f.S[p] = n);
            foreach (float[] plane in new[] { f.Y, f.I, f.Q })
                EveryCore.For(0, height, r => ShiftRow(plane, r * w, w, f.S[r] / F(0x3cb43958) * s.EdgeWaveIntensity * 0.5f * hs, true));
        }

        // Each line's colour mixed with the line above's, as VHS does (chroma_vert_blend). ntsc-rs
        // keeps its two delay lines in a field's scratch rows, so a field of one line (a picture of
        // 2 or 3 lines in fields) stops the website's engine; here that line is left as it is.
        static void ChromaVertBlend(Field f, int fullHeight)
        {
            if (fullHeight < 2 || f.Rows < 2) return;
            int w = f.Width;
            foreach (float[] plane in new[] { f.I, f.Q })
            {
                var delay = new float[w];
                for (int r = 0; r < f.Rows; r++)
                {
                    int o = r * w;
                    for (int p = 0; p < w; p++)
                    {
                        float c = plane[o + p];
                        plane[o + p] = (delay[p] + c) * 0.5f;
                        delay[p] = c;
                    }
                }
            }
        }
    }
}
