using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;

namespace B0r3dDeRes.Engine
{
    // The Signal's settings, by the website's names and values.
    static class SignalModes
    {
        public const string Off = "off", Auto = "auto", Rf = "rf", Composite = "composite", VhsSp = "vhs-sp", VhsLp = "vhs-lp", VhsEp = "vhs-ep";
        public static readonly string[] All = { Off, Auto, Rf, Composite, VhsSp, VhsLp, VhsEp };
        public const string Notch = "notch", Comb = "comb";
    }

    sealed class SignalSettings
    {
        public string Mode = SignalModes.Off;
        public double Wear = 0.5;       // 0 = a clean signal, 0.5 = as the connection usually looked, 1 = worn out
        public double Colour = 1;       // colour after decoding, 1 = as decoded
        public double Sharpen = 0.25;   // composite pre-emphasis
        public string Decoder = SignalModes.Notch;  // how the TV split colour from brightness

        public SignalSettings Clone() => (SignalSettings)MemberwiseClone();

        public bool SameAs(SignalSettings o) => Mode == o.Mode && Wear == o.Wear && Colour == o.Colour && Sharpen == o.Sharpen && Decoder == o.Decoder;
    }

    // How a machine's picture went out on the wire.
    sealed class SignalMachine
    {
        public double SamplesPerPixel;  // 14.318 MHz samples a picture pixel
        public bool LineLocked;         // 228 colour cycles a line: the same carrier phase on every line
        public string Connection;       // how it usually reached the TV ("Machine's own")
        public double? Colour;          // extra colour gain for this machine
        public bool CgaArtifact;        // the palette goes out as CGA hi-res bit patterns: the colours come from the signal
    }

    // A picture through the signal: r, g, b a sample, a line a picture row, and which samples
    // show (the picture's own mask held across each pixel's samples; null when all do).
    sealed class SignalImage
    {
        public byte[] Rgb;
        public int Width, Height;
        public byte[] Alpha;
    }

    // Something that draws at a size of its own, a row at a time, in any order and on any thread.
    abstract class RowRenderer
    {
        public abstract int Width { get; }
        public abstract int Height { get; }

        // One output row: r, g, b a pixel into `row`, and which pixels show into `alpha` (if given).
        public abstract void Row(int oy, byte[] row, byte[] alpha);

        // Rows from..from + count - 1, on every core: r, g, b (and a) a pixel; with a backdrop,
        // solid, the hidden pixels in its colour.
        public byte[][] Rows(int from, int count, bool withAlpha, byte[] backdrop)
        {
            int w = Width;
            var rows = new byte[count][];
            EveryCore.For(0, count, () => (new byte[w * 3], new byte[w]), (i, state, buffers) =>
            {
                byte[] rgb = buffers.Item1, shows = buffers.Item2;
                Row(from + i, rgb, shows);
                var output = new byte[w * (withAlpha ? 4 : 3)];
                for (int x = 0, o = 0; x < w; x++)
                {
                    bool hidden = backdrop != null && shows[x] == 0;
                    output[o++] = hidden ? backdrop[0] : rgb[x * 3];
                    output[o++] = hidden ? backdrop[1] : rgb[x * 3 + 1];
                    output[o++] = hidden ? backdrop[2] : rgb[x * 3 + 2];
                    if (withAlpha) output[o++] = shows[x];
                }
                rows[i] = output;
                return buffers;
            });
            return rows;
        }

        // the whole of it as rows of r, g, b, a, on every core
        public byte[] Rgba()
        {
            int w = Width;
            var output = new byte[(long)w * Height * 4];
            EveryCore.For(0, Height, () => (new byte[w * 3], new byte[w]), (y, state, buffers) =>
            {
                Row(y, buffers.Item1, buffers.Item2);
                long o = (long)y * w * 4;
                for (int x = 0; x < w; x++, o += 4)
                {
                    output[o] = buffers.Item1[x * 3];
                    output[o + 1] = buffers.Item1[x * 3 + 1];
                    output[o + 2] = buffers.Item1[x * 3 + 2];
                    output[o + 3] = buffers.Item2[x];
                }
                return buffers;
            });
            return output;
        }
    }

    // The Signal step: the finished picture leaves the machine as an analog TV signal (RF,
    // composite, or recorded on a VHS tape) and comes back the way a TV decoded it. The work is
    // ntsc-rs (Ntsc.cs); this decides what to send it, as the website does: how wide each pixel
    // was on the wire, how the machine timed its lines, and the engine settings for each
    // connection. (The website's core/signal.ts.)
    static class Signal
    {
        public static readonly Dictionary<string, string> ConnectionNames = new Dictionary<string, string>
        {
            { SignalModes.Rf, "RF (aerial socket)" },
            { SignalModes.Composite, "composite video" },
            { SignalModes.VhsSp, "a VHS tape (SP)" },
            { SignalModes.VhsLp, "a VHS tape (LP)" },
            { SignalModes.VhsEp, "a VHS tape (EP)" },
        };

        // ---- machines on the wire
        //
        // The engine works in samples of 4× the colour carrier (14.318 MHz), so one colour cycle
        // is 4 samples. A machine's pixel clock decides how many samples a pixel lasts: CGA's
        // 640-pixel mode is exactly 1, the NES 2.67 (5.37 MHz). That's what makes artifact colour
        // and dither blending come out right. Machines that ran 228 colour cycles a line (not
        // broadcast NTSC's 227.5) kept the carrier in the same phase on every line, so their
        // colour fringes stand still; the engine is told to do the same.

        const double Master = 14.31818;
        static double Clock(double mhz) => Master / mhz;

        static SignalMachine M(double samples, bool locked, string connection, bool cga = false) =>
            new SignalMachine { SamplesPerPixel = samples, LineLocked = locked, Connection = connection, CgaArtifact = cga };

        static readonly Dictionary<string, SignalMachine> machines = new Dictionary<string, SignalMachine>
        {
            // IBM CGA: 14.318 MHz hi-res, 7.16 MHz in the 320 modes; 228 cycles a line
            { "cga-0", M(2, true, SignalModes.Composite) },
            { "cga-1", M(2, true, SignalModes.Composite) },
            // each colour is a 4-pixel pattern in the 640 mode; the TV makes the colour
            { "cga-composite", M(2, true, SignalModes.Composite, true) },
            { "ega", M(2, true, SignalModes.Composite) },
            // VIC-II NTSC dot clock 8.18 MHz; 227.5 cycles a line (its colours crawl)
            { "c64-hires", M(Clock(8.181816), false, SignalModes.Composite) },
            { "c64-mc", M(2 * Clock(8.181816), false, SignalModes.Composite) },
            // a PAL machine (shown through the NTSC engine); 7 MHz pixels, RF out
            { "zx-spectrum", M(Clock(7), false, SignalModes.Rf) },
            // NES / SNES PPU dot clock 5.37 MHz (227⅓ cycles a line; shown as broadcast)
            { "nes", M(Clock(5.369318), false, SignalModes.Rf) },
            { "nes-attr", M(Clock(5.369318), false, SignalModes.Rf) },
            { "snes", M(Clock(5.369318), false, SignalModes.Composite) },
            // the TIA's pixel is one colour clock; 228 clocks a line
            { "atari-2600", M(4, true, SignalModes.Rf) },
            // Apple II: 7.16 MHz hi-res dots, 14 samples a lo-res block; 228 cycles a line
            { "apple2-hires", M(2, true, SignalModes.Composite) },
            { "apple2-lores", M(14, true, SignalModes.Composite) },
            // Genesis H40: 6.71 MHz; Master System / MSX TMS9918: 5.37 MHz; all 228 cycles a line
            { "genesis", M(Clock(6.711647), true, SignalModes.Composite) },
            { "mastersystem", M(Clock(5.369318), true, SignalModes.Composite) },
            { "msx-screen2", M(Clock(5.369318), true, SignalModes.Composite) },
            // Amiga low-res 7.16 MHz (its lines alternate 227 and 228 cycles)
            { "amiga-ham6", M(2, false, SignalModes.Composite) },
            { "atari-st", M(Clock(8), false, SignalModes.Rf) },
            { "ps1", M(Clock(6.708), false, SignalModes.Composite) },
            // the tape look and the early camcorder-era camera go through a VCR
            { "vhs", M(2, false, SignalModes.VhsSp) },
            { "ccd-early", M(1, false, SignalModes.VhsSp) },
        };

        // How a machine's picture went out. Machines that never had a TV output get a plain
        // composite line, about 640 samples across (the width of an NTSC picture).
        public static SignalMachine Machine(string profileId, int pictureWidth) =>
            profileId != null && machines.TryGetValue(profileId, out SignalMachine known) ? known
                : M(Math.Min(16, Math.Max(1, 640.0 / Math.Max(1, pictureWidth))), false, SignalModes.Composite);

        // the connection a mode stands for on this machine (null: off)
        public static string Connection(string mode, SignalMachine machine) =>
            mode == SignalModes.Off || mode == null ? null : mode == SignalModes.Auto ? machine.Connection : mode;

        // ---- CGA composite: DeRes's 16 colours are the 16 four-pixel patterns of the 640-pixel
        // mode. Lined up with the engine's colour carrier, pattern rotl(reverse(k), 2) gives
        // colour k of the cga-composite table.

        // the 4-bit hi-res pattern for colour k (bit 3 = the first pixel of each group of four)
        public static int CgaPattern(int k)
        {
            k &= 15;
            int reversed = ((k & 1) << 3) | ((k & 2) << 1) | ((k & 4) >> 1) | ((k & 8) >> 3);
            return ((reversed << 2) | (reversed >> 2)) & 15;
        }

        // the picture as the machine put it on the wire (r, g, b a sample): each pixel held for its share of samples
        public static byte[] Input(Rendered r, SignalMachine machine, out int width)
        {
            width = Math.Max(1, (int)JsMath.Round(r.Width * machine.SamplesPerPixel));
            int w = width, height = r.Height;
            var rgb = new byte[w * height * 3];
            var column = new int[w];
            for (int x = 0; x < w; x++) column[x] = Math.Min(r.Width - 1, (int)Math.Floor((double)x * r.Width / w));
            bool cga = machine.CgaArtifact && r.Palette.Length == 16 * 3;
            EveryCore.For(0, height, y =>
            {
                for (int x = 0; x < w; x++)
                {
                    int p = y * r.Width + column[x], o = (y * w + x) * 3;
                    if (cga)
                    {
                        byte v = ((CgaPattern(r.Indices[p]) >> (3 - (x & 3))) & 1) != 0 ? (byte)255 : (byte)0;
                        rgb[o] = rgb[o + 1] = rgb[o + 2] = v;
                    }
                    else
                    {
                        rgb[o] = r.Rgb[p * 3];
                        rgb[o + 1] = r.Rgb[p * 3 + 1];
                        rgb[o + 2] = r.Rgb[p * 3 + 2];
                    }
                }
            });
            return rgb;
        }

        // ---- the engine's settings: its defaults, then the connection's own, the wear scaling
        // the noises, and what the website always sends. The website hands them over as a
        // settings file, where each number is JavaScript's shortest text for it and the engine
        // reads that text straight to a float, then holds it to the setting's range.

        public static NtscSettings EngineSettings(string connection, SignalSettings s, int lines)
        {
            // the engine's defaults, as the website reads them back from its settings file
            double compositeNoise = 0.05, lumaNoise = 0.01, chromaNoise = 0.1, snow = 0.00025, phaseNoise = 0.001, trackingWave = 15,
                trackingSnow = 0.025, trackingNoise = 0.25, headShift = 72, edgeWave = 0.5, chromaLoss = 0.000025;
            int tapeSpeed = 2;
            bool vhs = true;
            switch (connection)
            {
                case SignalModes.Composite:
                    vhs = false;
                    break;
                case SignalModes.Rf:
                    vhs = false;
                    snow = 0.004;
                    compositeNoise = 0.12;
                    lumaNoise = 0.03;
                    chromaNoise = 0.25;
                    phaseNoise = 0.004;
                    break;
                case SignalModes.VhsSp:
                    tapeSpeed = 1;
                    edgeWave = 0.5;
                    break;
                case SignalModes.VhsLp:
                    tapeSpeed = 2;
                    edgeWave = 0.8;
                    chromaLoss = 0.0002;
                    snow = 0.001;
                    break;
                case SignalModes.VhsEp:
                    tapeSpeed = 3;
                    trackingWave = 25;
                    trackingSnow = 0.1;
                    edgeWave = 1.2;
                    chromaLoss = 0.0005;
                    snow = 0.002;
                    compositeNoise = 0.1;
                    chromaNoise = 0.2;
                    phaseNoise = 0.003;
                    break;
            }
            // the wear scales the noises: 0 = none of it, 0.5 = the connection's own, 1 = double
            double wear = Math.Min(1, Math.Max(0, s.Wear)) * 2;
            float Worn(double v, float lo, float hi) => Clamp(JsonFloat(v * wear), lo, hi);
            // up to 288 lines is one field a frame (a console's 240p); taller pictures are interlaced
            bool progressive = lines <= 288;
            return new NtscSettings
            {
                Interlaced = !progressive,
                Comb = s.Decoder == SignalModes.Comb,
                CompositePreemphasis = Clamp(JsonFloat(Math.Min(1, Math.Max(0, s.Sharpen))), -1, 2),
                CompositeNoise = Worn(compositeNoise, 0, 1),
                Snow = Worn(snow, 0, 100),
                HeadSwitching = vhs,
                HeadSwitchingShift = Worn(headShift, -100, 100),
                TrackingNoise = vhs,
                TrackingWave = Worn(trackingWave, -50, 50),
                TrackingSnow = Worn(trackingSnow, 0, 1),
                TrackingNoiseIntensity = Worn(trackingNoise, 0, 1),
                LumaNoise = Worn(lumaNoise, 0, 1),
                ChromaNoise = Worn(chromaNoise, 0, 1),
                ChromaPhaseNoise = Worn(phaseNoise, 0, 1),
                Vhs = vhs,
                TapeSpeed = tapeSpeed,
                ChromaLoss = Worn(chromaLoss, 0, 1),
                EdgeWave = Worn(edgeWave, 0, 20),
                VerticalScale = Clamp(JsonFloat((progressive ? lines : lines / 2.0) / 240), 0.125f, 8.8f),
            };
        }

        // Rust's f32::clamp
        static float Clamp(float v, float lo, float hi) => v < lo ? lo : v > hi ? hi : v;

        // A double as the engine reads it from JavaScript's settings file: JSON.stringify writes
        // the shortest digits that read back as the double, and the engine reads those straight
        // to a float. That's the double to the nearest float, but where the double lies exactly
        // halfway between two floats: then its digits decide.
        internal static float JsonFloat(double v)
        {
            float f = (float)v;
            if ((double)f == v || double.IsNaN(v) || double.IsInfinity(v) || float.IsInfinity(f)) return f;
            float other = NextFloat(f, v > f);
            if (((double)f + other) / 2 != v) return f;
            // halfway: the digits, against the double exactly
            string text = JsNumber.Text(v);
            int e = text.IndexOf('e');
            int exp10 = e >= 0 ? int.Parse(text.Substring(e + 1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture) : 0;
            string mantissa = (e >= 0 ? text.Substring(0, e) : text).TrimStart('-');
            int point = mantissa.IndexOf('.');
            if (point >= 0)
            {
                exp10 -= mantissa.Length - point - 1;
                mantissa = mantissa.Remove(point, 1);
            }
            BigInteger digits = BigInteger.Parse(mantissa, CultureInfo.InvariantCulture);
            long bits = BitConverter.DoubleToInt64Bits(Math.Abs(v));
            int biased = (int)((bits >> 52) & 0x7FF);
            BigInteger m = biased == 0 ? (bits & 0xFFFFFFFFFFFFFL) : (bits & 0xFFFFFFFFFFFFFL) | (1L << 52);
            int exp2 = (biased == 0 ? 1 : biased) - 1075;
            // digits × 10^exp10 against m × 2^exp2
            BigInteger left = digits * (exp10 >= 0 ? BigInteger.Pow(10, exp10) : 1) * (exp2 < 0 ? BigInteger.One << -exp2 : 1);
            BigInteger right = m * (exp2 >= 0 ? BigInteger.One << exp2 : 1) * (exp10 < 0 ? BigInteger.Pow(10, -exp10) : 1);
            int side = left.CompareTo(right) * Math.Sign(v);
            if (side == 0) return f;
            // the digits lie past v on one side: the float on that side
            bool up = side > 0;
            return (f > v) == up ? f : other;
        }

        static float NextFloat(float f, bool up)
        {
            int bits = BitConverter.ToInt32(BitConverter.GetBytes(f), 0);
            if (f == 0) bits = up ? 1 : unchecked((int)0x80000001);
            else bits += (f > 0) == up ? 1 : -1;
            return BitConverter.ToSingle(BitConverter.GetBytes(bits), 0);
        }

        // ---- running it

        // the edge colour added each side while the signal runs, so its filters don't darken the edges
        const int Pad = 16;

        // One frame of the picture through the signal; null when the signal is off.
        public static SignalImage Run(Rendered r, string profileId, SignalSettings s, int frame)
        {
            SignalMachine machine = Machine(profileId, r.Width);
            string connection = Connection(s.Mode, machine);
            if (connection == null) return null;
            byte[] input = Input(r, machine, out int width);
            NtscSettings settings = EngineSettings(connection, s, r.Height);
            float colour = (float)(Math.Max(0, s.Colour) * (machine.Colour ?? 1));
            byte[] rgb = Ntsc.Frame(input, width, r.Height, unchecked((uint)frame), settings, colour, machine.LineLocked, Pad);
            return new SignalImage { Rgb = rgb, Width = width, Height = r.Height, Alpha = Alpha(r, width) };
        }

        // the picture's mask held across each pixel's samples, as Input holds its colours
        static byte[] Alpha(Rendered r, int width)
        {
            if (r.Alpha == null) return null;
            var alpha = new byte[width * r.Height];
            for (int x = 0; x < width; x++)
            {
                int sx = Math.Min(r.Width - 1, (int)Math.Floor((double)x * r.Width / width));
                for (int y = 0; y < r.Height; y++) alpha[y * width + x] = r.Alpha[y * r.Width + sx];
            }
            return alpha;
        }

        // The colours this signal really makes from a machine's artifact patterns (CGA composite),
        // entry k for colour k ("#rrggbb"); null for other machines. DeRes then dithers to these,
        // so the picture aims at what the TV will show rather than at a table of approximations.
        public static string[] ArtifactPalette(string profileId, SignalSettings s)
        {
            SignalMachine machine = Machine(profileId, 320);
            if (!machine.CgaArtifact || Connection(s.Mode, machine) == null) return null;
            // 16 bands of 16 pixels (8 whole patterns each), without the noise
            const int band = 16, width = 16 * band, height = 32;
            var indices = new byte[width * height];
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++) indices[y * width + x] = (byte)(x / band);
            var swatches = new Rendered { Width = width, Height = height, Indices = indices, Palette = new byte[16 * 3], Indexed = true, Rgb = new byte[width * height * 3] };
            SignalSettings clean = s.Clone();
            clean.Wear = 0;
            SignalImage img = Run(swatches, profileId, clean, 0);
            if (img == null) return null;
            double per = img.Width / 16.0;
            var colours = new string[16];
            for (int k = 0; k < 16; k++)
            {
                var acc = new double[3];
                int n = 0;
                // the middle of the band, away from its neighbours' fringes
                for (int y = 4; y < height - 4; y++)
                    for (int x = (int)JsMath.Round(k * per + per / 4); x < JsMath.Round((k + 1) * per - per / 4); x++)
                    {
                        for (int c = 0; c < 3; c++) acc[c] += img.Rgb[(y * img.Width + x) * 3 + c];
                        n++;
                    }
                colours[k] = "#" + string.Concat(Array.ConvertAll(acc, v => ((int)JsMath.Round(v / n)).ToString("x2", CultureInfo.InvariantCulture)));
            }
            return colours;
        }

        static readonly object artifactLock = new object();
        static string artifactKey;
        static string[] artifactColours;

        // The settings to make the picture with: with the signal on, CGA composite dithers to the
        // colours this signal really makes from its patterns (worked out once for each setting
        // that changes them); anything else as it is.
        public static DeresSettings ForRender(DeresSettings d, SignalSettings s)
        {
            if (s == null || s.Mode == SignalModes.Off || !Machine(d.ProfileId, 0).CgaArtifact) return d;
            string key = string.Join("|", d.ProfileId, s.Mode, s.Colour.ToString("R", CultureInfo.InvariantCulture), s.Sharpen.ToString("R", CultureInfo.InvariantCulture), s.Decoder);
            string[] colours;
            lock (artifactLock)
            {
                if (key != artifactKey)
                {
                    artifactColours = ArtifactPalette(d.ProfileId, s);
                    artifactKey = key;
                }
                colours = artifactColours;
            }
            if (colours == null) return d;
            DeresSettings copy = d.Clone();
            copy.FixedPalette = colours;
            return copy;
        }

        // ---- showing it

        // The signal resized across to outW samples: area-averaged when it shrinks (so the colour
        // carrier's fine pattern doesn't alias), linear when it grows. Lines stay lines.
        public static SignalImage Resample(SignalImage img, int outW)
        {
            if (outW == img.Width) return img;
            int w = img.Width, h = img.Height;
            var output = new byte[outW * h * 3];
            double scale = (double)w / outW;
            // the mask stays sharp: each output sample takes the one under its middle
            byte[] alpha = null;
            if (img.Alpha != null)
            {
                alpha = new byte[outW * h];
                for (int x = 0; x < outW; x++)
                {
                    int sx = (int)Math.Min(w - 1, Math.Floor((x + 0.5) * scale));
                    for (int y = 0; y < h; y++) alpha[y * outW + x] = img.Alpha[y * w + sx];
                }
            }
            byte[] rgb = img.Rgb;
            EveryCore.For(0, h, y =>
            {
                int row = y * w * 3;
                for (int x = 0; x < outW; x++)
                {
                    int o = (y * outW + x) * 3;
                    if (scale > 1)
                    {
                        double a = x * scale, b = a + scale, r = 0, g = 0, bl = 0;
                        for (int i = (int)Math.Floor(a); i < Math.Ceiling(b) && i < w; i++)
                        {
                            double weight = Math.Min(b, i + 1) - Math.Max(a, i);
                            r += rgb[row + i * 3] * weight;
                            g += rgb[row + i * 3 + 1] * weight;
                            bl += rgb[row + i * 3 + 2] * weight;
                        }
                        output[o] = (byte)JsMath.Round(r / scale);
                        output[o + 1] = (byte)JsMath.Round(g / scale);
                        output[o + 2] = (byte)JsMath.Round(bl / scale);
                    }
                    else
                    {
                        double u = Math.Max(0, Math.Min(w - 1, (x + 0.5) * scale - 0.5));
                        int i0 = (int)Math.Floor(u), i1 = Math.Min(w - 1, i0 + 1);
                        double f = u - i0;
                        for (int c = 0; c < 3; c++) output[o + c] = (byte)JsMath.Round(rgb[row + i0 * 3 + c] * (1 - f) + rgb[row + i1 * 3 + c] * f);
                    }
                }
            });
            return new SignalImage { Rgb = output, Width = outW, Height = h, Alpha = alpha };
        }

        // The signal shown at outW × outH, through the screen effect when one is given (CRT,
        // LCD…), at `phase` of an animation.
        public static RowRenderer Renderer(SignalImage img, int outW, int outH, FxSettings fx, double phase, byte[] paper)
        {
            SignalImage src = Resample(img, outW);
            if (fx != null && fx.Screen != Screens.Off) return new FxRenderer(src.Rgb, outW, src.Height, paper, src.Alpha, outW, outH, fx, phase);
            return new Lines(src, outH);
        }

        // the signal's lines, each output row the line it falls on
        sealed class Lines : RowRenderer
        {
            readonly SignalImage src;
            readonly int outH;

            public Lines(SignalImage src, int outH)
            {
                this.src = src;
                this.outH = outH;
            }

            public override int Width => src.Width;
            public override int Height => outH;

            public override void Row(int oy, byte[] row, byte[] alpha)
            {
                int line = (int)Math.Min(src.Height - 1, (long)oy * src.Height / outH);
                Buffer.BlockCopy(src.Rgb, line * src.Width * 3, row, 0, src.Width * 3);
                if (alpha == null) return;
                if (src.Alpha != null) Buffer.BlockCopy(src.Alpha, line * src.Width, alpha, 0, src.Width);
                else for (int x = 0; x < src.Width; x++) alpha[x] = 255;
            }
        }

        // the settings checked field by field (kept settings, share links and look files)
        public static SignalSettings Check(object loaded, SignalSettings b)
        {
            var s = loaded as Dictionary<string, object> ?? new Dictionary<string, object>();
            object Get(string key) => s.TryGetValue(key, out object v) ? v : null;
            double Num(object v, double lo, double hi, double fallback) => v is double d && !double.IsNaN(d) && !double.IsInfinity(d) ? Math.Min(hi, Math.Max(lo, d)) : fallback;
            return new SignalSettings
            {
                Mode = Get("mode") is string mode && Array.IndexOf(SignalModes.All, mode) >= 0 ? mode : b.Mode,
                Wear = Num(Get("wear"), 0, 1, b.Wear),
                Colour = Num(Get("colour"), 0, 1.5, b.Colour),
                Sharpen = Num(Get("sharpen"), 0, 1, b.Sharpen),
                Decoder = Get("decoder") is string dec && (dec == SignalModes.Comb || dec == SignalModes.Notch) ? dec : b.Decoder,
            };
        }
    }
}
