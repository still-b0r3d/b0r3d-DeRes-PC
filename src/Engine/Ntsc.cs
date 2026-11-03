using System;

namespace B0r3dDeRes.Engine
{
    // What the engine is told, by its own names (its settings files), and only what the website
    // can send it: the rest stays at the engine's defaults (or the values the website always sends,
    // in brackets), and the steps the website never reaches aren't here. Every number is the float
    // the engine reads from the website's settings file.
    sealed class NtscSettings
    {
        public bool Interlaced;                 // use_field: interleaved upper (else both, one field a frame)
        public bool Comb;                       // chroma_demodulation: the 2-line comb (else the notch)
        public float CompositePreemphasis;      // composite_preemphasis
        public float CompositeNoise;            // composite_noise_intensity (frequency 0.5, detail 1)
        public float Snow;                      // snow_intensity (anisotropy 0.5)
        public bool HeadSwitching;              // height 8, offset 3, starting mid-line at 0.95 with a jitter of 0.03
        public float HeadSwitchingShift;        // head_switching_horizontal_shift
        public bool TrackingNoise;              // height 12, snow anisotropy 0.25
        public float TrackingWave, TrackingSnow, TrackingNoiseIntensity;
        public float LumaNoise;                 // luma_noise_intensity (frequency 0.5, detail 1)
        public float ChromaNoise;               // chroma_noise_intensity (frequency 0.05, detail 2)
        public float ChromaPhaseNoise;          // chroma_phase_noise_intensity
        public bool Vhs;                        // vhs_settings: sharpened (0.25, frequency 1), the edge wave on (speed 4, frequency 0.05, detail 2)
        public int TapeSpeed = 2;               // vhs_tape_speed: 0 none, 1 SP, 2 LP, 3 EP
        public float ChromaLoss;                // vhs_chroma_loss
        public float EdgeWave;                  // vhs_edge_wave
        public float VerticalScale = 1;         // vertical_scale (bandwidth_scale 1)
        // and always: no input luma filter, Butterworth filters, the full chroma lowpass in and out,
        // luma smear 0.5, ringing (0.45, power 4, scale 4), no chroma phase error or delay, no vertical blend
    }

    // ntsc-rs, the analog TV engine by valadaptive (crates/ntscrs at 450c6ee, MIT OR ISC OR
    // Apache-2.0), copied from its Rust for the steps the website uses, with the website's own
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

        // ---- filters (filter.rs): IIR filters of up to 3 coefficients a side

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

        // ---- one frame: the website's wrapper (signal/src/lib.rs)

        const int HalfCycle = 2;

        // Does this line's colour carrier come flipped (the engine's own rule for one field a frame)?
        static bool CarrierFlipped(uint frame, int line) => ((frame + 2 * (uint)line) & 2) != 0;

        // One frame of `rgb` (width × height, r, g, b a pixel) through the engine, back as r, g, b.
        // `frame`: which frame (its noise; sums wrap at 32 bits, as the engine's usize does in
        // WebAssembly); `chroma`: the colour after decoding (1 = as decoded); `lineLocked`: the
        // same carrier phase on every line (machines with 228 colour cycles a line, one field a
        // frame only): lines whose carrier the engine flips go in half a cycle to the right and
        // come back; `pad`: samples of the edge colour each side while it runs (its filters start
        // from 0).
        public static byte[] Frame(byte[] rgb, int width, int height, uint frame, NtscSettings s, float chroma, bool lineLocked, int pad)
        {
            bool locked = lineLocked && !s.Interlaced;
            pad = Math.Max(pad, locked ? HalfCycle : 0);
            int pw = width + 2 * pad;

            // into YIQ: one field of every line, or the even lines then the odd ones
            Field[] fields = s.Interlaced
                ? new[] { new Field(pw, (height + 1) / 2), new Field(pw, height / 2) }
                : new[] { new Field(pw, height) };
            var norm = new float[256];
            for (int v = 0; v < 256; v++) norm[v] = v * Inv255;
            EveryCore.For(0, height, y =>
            {
                Field f = fields[s.Interlaced ? y & 1 : 0];
                int row = s.Interlaced ? y >> 1 : y;
                if (f.Rows == 0) return;
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

            if (s.Interlaced)
            {
                // the even lines are a frame's first field, the odd ones its second
                if (fields[0].Rows > 0) Effect(fields[0], frame * 2, s);
                if (fields[1].Rows > 0) Effect(fields[1], frame * 2 + 1, s);
            }
            else Effect(fields[0], frame, s);

            var outRgb = new byte[width * height * 3];
            EveryCore.For(0, height, y =>
            {
                Field f = fields[s.Interlaced ? y & 1 : 0];
                int row = s.Interlaced ? y >> 1 : y;
                int start = pad + (locked && CarrierFlipped(frame, y) ? HalfCycle : 0);
                for (int x = 0; x < width; x++)
                {
                    int o = row * pw + start + x, d = (y * width + x) * 3;
                    float yy = f.Y[o], ii = f.I[o], qq = f.Q[o];
                    if (chroma != 1f)
                    {
                        ii *= chroma;
                        qq *= chroma;
                    }
                    outRgb[d] = FromNorm(qq * Qr + (ii * Ir + yy));
                    outRgb[d + 1] = FromNorm(qq * Qg + (ii * Ig + yy));
                    outRgb[d + 2] = FromNorm(qq * Qb + (ii * Ib + yy));
                }
            });
            return outRgb;
        }

        // ---- the effect (ntsc.rs), on one field

        static readonly float[] iMult = { 1, 0, -1, 0 }, qMult = { 0, 1, 0, -1 }, iMultInv = { -1, 0, 1, 0 }, qMultInv = { 0, -1, 0, 1 };

        // the colour carrier's phase on a line (0 or 2 samples)
        static int CarrierPhase(uint frame, int line) => (int)((frame + ((uint)line << 1)) & 2);

        static void Effect(Field f, uint frame, NtscSettings s)
        {
            int w = f.Width, rows = f.Rows;
            const ulong seed = 0;
            const float hs = 1f;            // bandwidth scale 1 (the samples are already at the engine's rate)
            float vs = s.VerticalScale;

            ChromaLowpass(f, hs);

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

            // composite noise (seed 0)
            Rng composite = new Rng(seed).Mix(0).Mix((ulong)frame);
            EveryCore.For(0, rows, r => NoiseLine(f.Y, r * w, w, composite, (uint)r, 0.5f / hs, s.CompositeNoise, 1));

            if (s.Snow > 0 && hs > 0)
            {
                Rng snow = new Rng(seed).Mix(6).Mix((ulong)frame);
                float intensity = s.Snow * 0.01f;
                EveryCore.For(0, rows, r => Speckles(f.Y, r * w, w, snow.Mix((ulong)r), intensity, 0.5f, hs));
            }

            if (s.HeadSwitching) HeadSwitching(f, frame, s, hs, vs);
            if (s.TrackingNoise) TrackingNoise(f, frame, s, hs, vs);

            // the colour back off the carrier
            Array.Copy(f.Y, f.S, w * rows);
            if (s.Comb && rows > 1)
            {
                EveryCore.For(0, rows, r =>
                {
                    int prev = r == 0 ? 1 : r - 1, next = r == rows - 1 ? rows - 2 : r + 1;
                    int o = r * w, po = prev * w, no = next * w;
                    for (int p = 0; p < w; p++) f.Y[o + p] = f.S[o + p] * 0.5f + f.S[po + p] * 0.25f + f.S[no + p] * 0.25f;
                    Demodulate(f, r, CarrierPhase(frame, r));
                });
            }
            else
            {
                FilterPlane(f.Y, w, rows, Filter.Notch(0.5f, 2f), false, 0);
                EveryCore.For(0, rows, r => Demodulate(f, r, CarrierPhase(frame, r)));
            }

            // luma smear (0.5)
            FilterPlane(f.Y, w, rows, Filter.Lowpass(RustMath.Exp2f(-4f * 0.5f) * 0.25f, hs), false, 0);

            // ringing
            float ringing = Math.Max(0f, Math.Min(1f, 0.45f / hs));
            FilterPlane(f.Y, w, rows, Filter.Notch(ringing, 4f).WithScale(4f), true, 1);

            // luma noise (seed 10), chroma noise (1 and 9)
            Rng luma = new Rng(seed).Mix(10).Mix((ulong)frame);
            EveryCore.For(0, rows, r => NoiseLine(f.Y, r * w, w, luma, (uint)r, 0.5f / hs, s.LumaNoise, 1));
            Rng chromaI = new Rng(seed).Mix(1).Mix((ulong)frame), chromaQ = new Rng(seed).Mix(9).Mix((ulong)frame);
            EveryCore.For(0, rows, r => NoiseLine(f.I, r * w, w, chromaI, (uint)r, 0.05f / hs, s.ChromaNoise, 2));
            EveryCore.For(0, rows, r => NoiseLine(f.Q, r * w, w, chromaQ, (uint)r, 0.05f / hs, s.ChromaNoise, 2));

            if (s.ChromaPhaseNoise > 0)
            {
                Rng phase = new Rng(seed).Mix(4).Mix((ulong)frame);
                EveryCore.For(0, rows, r =>
                {
                    float shift = (phase.Mix((ulong)r).F32() - 0.5f) * 2f * s.ChromaPhaseNoise;
                    PhaseLine(f, r, shift);
                });
            }

            if (s.Vhs)
            {
                if (s.EdgeWave > 0) EdgeWave(f, frame, s, hs, vs);

                // the tape's bandwidth (none: no tape filters, and no sharpening)
                float lumaCut = 0, chromaCut = 0;
                int chromaDelay = 0;
                bool tape = true;
                switch (s.TapeSpeed)
                {
                    case 1: lumaCut = 2400000; chromaCut = 320000; chromaDelay = 4; break;
                    case 2: lumaCut = 1900000; chromaCut = 300000; chromaDelay = 5; break;
                    case 3: lumaCut = 1400000; chromaCut = 280000; chromaDelay = 6; break;
                    default: tape = false; break;
                }
                if (tape)
                {
                    int delay = (int)RustMath.ToU32(RustMath.Round(chromaDelay * hs));
                    FilterPlane(f.Y, w, rows, Filter.Butterworth(lumaCut, NtscRate * hs), false, 0);
                    Filter chromaFilter = Filter.Butterworth(chromaCut, NtscRate * hs);
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

                // sharpened (0.25, frequency 1; Butterworth filters take 1× the frequency)
                const float sharpen = 0.25f, frequency = 1f;
                if (tape) FilterPlane(f.Y, w, rows, Filter.Butterworth(lumaCut * 1f * frequency, NtscRate * hs).WithScale(-sharpen * 2f * frequency), false, 0);
            }

            ChromaLowpass(f, hs);
        }

        // the full chroma lowpass (Butterworth): I to 1.3 MHz, Q to 0.6 MHz
        static void ChromaLowpass(Field f, float hs)
        {
            FilterPlane(f.I, f.Width, f.Rows, Filter.Butterworth(1300000f, NtscRate * hs), false, 2);
            FilterPlane(f.Q, f.Width, f.Rows, Filter.Butterworth(600000f, NtscRate * hs), false, 4);
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
            int octaves = Math.Max(1, Math.Min(5, detail));
            float scale = intensity * 0.25f;
            Noise1(offset, width, x => Fbm1(x, seed, octaves, 1f, 2f, frequency), (p, n) => a[o + p] = a[o + p] + n * scale);
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

        // VHS head switching: the bottom lines pulled sideways, the top one from mid-line
        static void HeadSwitching(Field f, uint frame, NtscSettings s, float hs, float vs)
        {
            uint numRows = RustMath.ToU32(RustMath.Round(8 * vs)), offset = RustMath.ToU32(RustMath.Round(3 * vs));
            if (offset > numRows) return;
            uint affected = numRows - offset;
            int w = f.Width, height = f.Rows;
            uint startRow = Math.Max((uint)height, affected) - affected, cutOff = affected > height ? affected - (uint)height : 0;
            Rng rng = new Rng(0).Mix(2).Mix((ulong)frame);
            EveryCore.For((int)startRow, height, r =>
            {
                uint index = affected - ((uint)r - startRow + cutOff);
                float rowShift = s.HeadSwitchingShift * RustMath.Powf((float)(index + offset) / numRows, 1.5f);
                float shift = (rowShift + (rng.Mix(index).F32() - 0.5f)) * hs;
                int o = r * w;
                if (index != affected)
                {
                    ShiftRow(f.Y, o, w, shift, false);
                    return;
                }
                // the whole line shifted, but only from mid-line on
                ShiftRowTo(f.Y, o, f.S, o, w, shift, false);
                Rng mid = new Rng(0).Mix(8).Mix((ulong)frame);
                float jitterRand = (mid.F32() + mid.F32()) * 0.5f;
                float jitter = (jitterRand - 0.5f) * 0.03f;
                uint copyStart = RustMath.ToU32(w * (0.95f + jitter));
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
        static void TrackingNoise(Field f, uint frame, NtscSettings s, float hs, float vs)
        {
            uint numRows = RustMath.ToU32(RustMath.Round(12 * vs));
            int w = f.Width, height = f.Rows;
            Rng rng = new Rng(0).Mix(3).Mix((ulong)frame);
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
                Speckles(f.Y, o, w, rng.Mix(index), s.TrackingSnow * (scale * scale), 0.25f, hs);
            });
        }

        // the VHS edge wave: every line moved sideways by fractal noise down the picture, which drifts with the frames
        static void EdgeWave(Field f, uint frame, NtscSettings s, float hs, float vs)
        {
            int w = f.Width, height = f.Rows;
            Rng rng = new Rng(0).Mix(5);
            int noiseSeed = rng.I32();
            float offset = rng.F32() * height;
            float frequency = 0.05f / vs, time = frame * 4f;
            Noise1(offset, height, x => Fbm2(x, time, noiseSeed, 2, FracSqrt2, 2f, frequency), (p, n) => f.S[p] = n);
            foreach (float[] plane in new[] { f.Y, f.I, f.Q })
                EveryCore.For(0, height, r => ShiftRow(plane, r * w, w, f.S[r] / F(0x3cb43958) * s.EdgeWave * 0.5f * hs, true));
        }
    }
}
