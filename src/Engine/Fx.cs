using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace B0r3dDeRes.Engine
{
    // The screens, by the website's ids.
    static class Screens
    {
        public const string Off = "off", Crt = "crt", Lcd = "lcd", Vhs = "vhs", Composite = "composite", P1Green = "p1-green", P3Amber = "p3-amber", P4White = "p4-white";
        public static readonly string[] All = { Off, Crt, Lcd, Vhs, Composite, P1Green, P3Amber, P4White };
        public static readonly string[] Masks = { "none", "shadow", "grille" };
    }

    // The screen effect's settings, by the website's names and values.
    sealed class FxSettings
    {
        public string Screen = Screens.Off;
        public string Mask = "grille";      // CRT: none, shadow (mask) or grille (aperture)
        public double Scanlines = 0.7, Curve = 0.3, Bloom = 0.3;   // CRT, 0-1
        public double Grid = 0.5, Ghost = 0.4;                      // LCD, 0-1: the gaps, the shadow dark pixels cast
        public double VhsTracking = 0.8;    // VHS: the tracking jitter and noise, 0-1
        public bool VhsOsd = true;          // VHS: the on-screen display (PLAY ▶)

        public FxSettings Clone() => (FxSettings)MemberwiseClone();

        public bool SameAs(FxSettings o) => Screen == o.Screen && Mask == o.Mask && Scanlines == o.Scanlines && Curve == o.Curve && Bloom == o.Bloom &&
            Grid == o.Grid && Ghost == o.Ghost && VhsTracking == o.VhsTracking && VhsOsd == o.VhsOsd;
    }

    // Screen effects: what the picture looked like on the machine's screen. They need more pixels
    // than the picture has (a scanline is a part of a pixel), so they render an enlarged copy, a
    // row at a time, in any order and on any thread. Nothing here moves or flickers (but for the
    // phase of an animation's frame). (The website's core/fx.ts, to the last bit: its sums in
    // double, what it keeps in floats rounded to floats, its sin and exp fdlibm's, as V8 has them.)
    sealed class FxRenderer
    {
        // the electron beam across one pixel row
        const int BeamLevels = 16, BeamSteps = 64;
        const double BeamPeak = 1.2, MaskDim = 0.45;
        static readonly float[] beam = MakeBeam();

        static float[] MakeBeam()
        {
            var b = new float[BeamLevels * (BeamSteps + 1)];
            for (int l = 0; l < BeamLevels; l++)
            {
                double sigma = 0.12 + 0.18 * ((l + 0.5) / BeamLevels);
                for (int i = 0; i <= BeamSteps; i++)
                {
                    double d = (double)i / BeamSteps - 0.5;
                    b[l * (BeamSteps + 1) + i] = (float)JsMath.Exp(-(d * d) / (2 * sigma * sigma));
                }
            }
            return b;
        }

        internal static float[] BeamTable => beam;

        // a 5×7 font for the VHS on-screen display (columns, bit 0 at the top)
        static readonly Dictionary<char, int[]> osdFont = new Dictionary<char, int[]>
        {
            { ' ', new[] { 0, 0, 0, 0, 0 } }, { ':', new[] { 0, 0x24, 0, 0, 0 } }, { '-', new[] { 0x08, 0x08, 0x08, 0x08, 0x08 } },
            { '0', new[] { 0x3e, 0x51, 0x49, 0x45, 0x3e } }, { '1', new[] { 0x00, 0x42, 0x7f, 0x40, 0x00 } }, { '2', new[] { 0x42, 0x61, 0x51, 0x49, 0x46 } },
            { '3', new[] { 0x21, 0x41, 0x45, 0x4b, 0x31 } }, { '4', new[] { 0x18, 0x14, 0x12, 0x7f, 0x10 } }, { '5', new[] { 0x27, 0x45, 0x45, 0x45, 0x39 } },
            { '6', new[] { 0x3c, 0x4a, 0x49, 0x49, 0x30 } }, { '7', new[] { 0x01, 0x71, 0x09, 0x05, 0x03 } }, { '8', new[] { 0x36, 0x49, 0x49, 0x49, 0x36 } },
            { '9', new[] { 0x06, 0x49, 0x49, 0x29, 0x1e } }, { 'A', new[] { 0x7e, 0x11, 0x11, 0x11, 0x7e } }, { 'L', new[] { 0x7f, 0x40, 0x40, 0x40, 0x40 } },
            { 'P', new[] { 0x7f, 0x09, 0x09, 0x09, 0x06 } }, { 'S', new[] { 0x26, 0x49, 0x49, 0x49, 0x32 } }, { 'Y', new[] { 0x07, 0x08, 0x70, 0x08, 0x07 } },
            { (char)0x25B6, new[] { 0x7f, 0x3e, 0x1c, 0x08, 0x00 } },   // ▶
        };

        static readonly string topOsd = "PLAY " + (char)0x25B6, bottomOsd = "SP -0:12:34";

        // whether a pixel of the on-screen text is lit (fg) or its shadow (bg)
        static void OsdPixel(string text, int rx, int ry, int scale, out bool fg, out bool bg)
        {
            fg = bg = false;
            int charW = 6 * scale, charH = 8 * scale;
            if (ry < 0 || ry >= charH) return;
            int cIdx = (int)Math.Floor((double)rx / charW);
            if (cIdx < 0 || cIdx >= text.Length) return;
            if (!osdFont.TryGetValue(text[cIdx], out int[] glyph)) glyph = osdFont[' '];
            int gx = (int)Math.Floor((double)(rx % charW) / scale), gy = (int)Math.Floor((double)ry / scale);
            if (gx >= 0 && gx < 5 && gy >= 0 && gy < 7 && ((glyph[gx] >> gy) & 1) == 1)
            {
                fg = true;
                return;
            }
            // the drop shadow: one step down and right
            int bgGx = (int)Math.Floor((double)((rx - scale) % charW) / scale), bgGy = (int)Math.Floor((double)(ry - scale) / scale);
            if (bgGx >= 0 && bgGx < 5 && bgGy >= 0 && bgGy < 7 && ((glyph[bgGx] >> bgGy) & 1) == 1) bg = true;
        }

        // the website's 2D hash: 0 <= h < 1
        static double Hash(int x, int y, int seed)
        {
            uint h = (uint)((x * 0x3456789a) ^ (y * unchecked((int)0x9abcdef0)) ^ seed);
            h = (uint)((int)(h ^ (h >> 15)) * 0x1234567b);
            return (h ^ (h >> 16)) / 4294967296.0;
        }

        static double Clamp01(double v) => v < 0 ? 0 : v > 1 ? 1 : v;

        readonly int w, h, outW, outH;
        readonly float[] lin, glow;
        readonly byte[] outline;
        readonly double sx, sy, scan, curve, k, bloom, gain, grid, ghost, gapU, gapV, tracking, phase;
        readonly double[] paper;
        readonly bool crt, lcd, vhs, composite, p1, p3, p4, phosphor, showOsd;
        readonly string mask;
        readonly int osdScale;

        // `rgb`: the finished picture, sRGB bytes, 3 a pixel; `paper`: its lightest colour (an
        // LCD's gaps); `alpha`: which pixels show (null: all). An output pixel shows when the
        // picture pixel it's drawn from does; outside a curved screen is see-through then too,
        // and the VHS on-screen display always shows.
        public FxRenderer(byte[] rgb, int width, int height, byte[] paper, byte[] alpha, int outWidth, int outHeight, FxSettings fx, double phase = 0)
        {
            w = width;
            h = height;
            outW = outWidth;
            outH = outHeight;
            outline = alpha;
            this.phase = phase;
            lin = new float[w * h * 3];
            for (int i = 0; i < lin.Length; i++) lin[i] = Colour.ByteToLinear[rgb[i]];
            sx = (double)outW / w;
            sy = (double)outH / h;

            crt = fx.Screen == Screens.Crt;
            lcd = fx.Screen == Screens.Lcd;
            vhs = fx.Screen == Screens.Vhs;
            composite = fx.Screen == Screens.Composite;
            p1 = fx.Screen == Screens.P1Green;
            p3 = fx.Screen == Screens.P3Amber;
            p4 = fx.Screen == Screens.P4White;
            phosphor = p1 || p3 || p4;
            bool crtPhysics = crt || phosphor || vhs || composite;

            // scanlines need at least 2 output rows a picture row to show
            scan = crtPhysics ? fx.Scanlines * Clamp01(sy - 1) : 0;
            curve = crtPhysics ? fx.Curve : 0;
            k = curve * 0.08;
            glow = (crtPhysics || phosphor) && fx.Bloom > 0 ? Blur(lin, w, h) : null;
            bloom = glow != null ? fx.Bloom * 0.7 : 0;
            mask = crt ? fx.Mask : "none";
            gain = mask != "none" ? 3 / (1 + 2 * MaskDim) : 1;

            grid = lcd ? fx.Grid * 0.65 * Clamp01(Math.Min(sx, sy) - 2) : 0;
            ghost = lcd ? fx.Ghost * 0.45 : 0;
            this.paper = new double[] { Colour.ByteToLinear[paper[0]], Colour.ByteToLinear[paper[1]], Colour.ByteToLinear[paper[2]] };
            gapU = 1 / sx;
            gapV = 1 / sy;

            tracking = fx.VhsTracking;
            showOsd = fx.VhsOsd;
            osdScale = Math.Max(1, Math.Min(4, (int)Math.Floor(outH / 160.0)));
        }

        public int Width => outW;
        public int Height => outH;

        // a picture float as the website reads it: past the start of the array, NaN (JavaScript's
        // undefined), which comes out black
        double Lin(int i) => i >= 0 && i < lin.Length ? lin[i] : double.NaN;

        // One output row: r, g, b a pixel into `row`, and which pixels show into `alpha` (if given).
        public void Row(int oy, byte[] row, byte[] alpha)
        {
            var c = new float[3];
            var chroma = new float[3];
            var g = new float[3];
            double ny = (oy + 0.5) / outH * 2 - 1;

            // the VHS tracking band near the bottom (it rolls with the phase)
            double jitterX = 0;
            bool headNoise = false;
            double roll = phase > 0 ? phase * 0.15 : 0;
            int band = (int)Math.Floor(outH * Math.Max(0.1, 0.94 - roll));
            int phaseSeed = (int)Math.Floor(phase * 100);
            if (vhs && oy >= band && oy <= band + Math.Floor(outH * 0.06))
            {
                double progress = (oy - band) / (outH * 0.06);
                double wave = JsMath.Sin((oy + phase * 40) * 0.5) * 8 * progress;
                double noise = (Hash(0, oy, 11 + phaseSeed) - 0.5) * 16 * tracking;
                jitterX = (wave + noise) * sx * 0.5;
                headNoise = Hash(1, oy, 22 + phaseSeed) < 0.28 * tracking;
            }

            // composite's chroma filters, from the start of each row
            double compI = 0, compQ = 0, lastY = 0, compRing = 0;

            for (int ox = 0; ox < outW; ox++)
            {
                double u, v, fade = 1;
                if (curve > 0)
                {
                    double nx = (ox + 0.5) / outW * 2 - 1;
                    double cx = nx * (1 + k * ny * ny), cy = ny * (1 + k * nx * nx);
                    double edge = Math.Min(1 - Math.Abs(cx), 1 - Math.Abs(cy));
                    if (edge <= 0)
                    {
                        row[ox * 3] = row[ox * 3 + 1] = row[ox * 3 + 2] = 0;
                        if (alpha != null) alpha[ox] = outline != null ? (byte)0 : (byte)255;
                        continue;
                    }
                    fade = Math.Min(1, edge * 60) * (1 - curve * 0.25 * (cx * cx + cy * cy) * 0.5);
                    u = (cx + 1) / 2 * w;
                    v = (cy + 1) / 2 * h;
                }
                else
                {
                    u = (ox + 0.5) / sx;
                    v = (oy + 0.5) / sy;
                }
                // the VHS heads' jitter
                if (vhs && jitterX != 0) u += jitterX / sx;

                int px = (int)Math.Min(w - 1, Math.Max(0, Math.Floor(u))), py = (int)Math.Min(h - 1, Math.Max(0, Math.Floor(v)));
                int p = (py * w + px) * 3;
                bool shows = outline == null || outline[py * w + px] != 0;
                c[0] = lin[p];
                c[1] = lin[p + 1];
                c[2] = lin[p + 2];

                if (lcd)
                {
                    if (ghost > 0)
                    {
                        int qx = (int)Math.Max(0, Math.Floor(u - 0.35)), qy = (int)Math.Max(0, Math.Floor(v - 0.35));
                        int q = (qy * w + qx) * 3;
                        // a hidden pixel casts no shadow
                        if ((outline == null || outline[qy * w + qx] != 0) && Colour.Luminance(lin[q], lin[q + 1], lin[q + 2]) < Colour.Luminance(c[0], c[1], c[2]))
                            for (int j = 0; j < 3; j++) c[j] = (float)(c[j] + ((double)lin[q + j] - c[j]) * ghost);
                    }
                    if (grid > 0 && (u - px < gapU || v - py < gapV))
                        for (int j = 0; j < 3; j++) c[j] = (float)(c[j] + (paper[j] - c[j]) * grid);
                }
                else if (phosphor)
                {
                    // monochrome phosphors: P1 green, P3 amber, P4 white
                    double lum = Colour.Luminance(c[0], c[1], c[2]);
                    Tint(c, lum);
                    double b = 1;
                    if (scan > 0)
                    {
                        int level = Math.Min(BeamLevels - 1, (int)(Math.Sqrt(lum) * BeamLevels));
                        int across = Math.Min(BeamSteps, (int)((v - py) * BeamSteps + 0.5));
                        b = 1 - scan + scan * BeamPeak * beam[level * (BeamSteps + 1) + across];
                    }
                    double f = b * fade;
                    for (int j = 0; j < 3; j++) c[j] = (float)(c[j] * f);
                    if (glow != null)
                    {
                        g[0] = g[1] = g[2] = 0;
                        SampleBilinear(glow, w, h, u - 0.5, v - 0.5, g, 1.0);
                        double glowLum = Colour.Luminance(g[0], g[1], g[2]) * bloom * fade;
                        double r0, r1, r2;
                        if (p1) { r0 = 0.16; r1 = 1.0; r2 = 0.2; }
                        else if (p3) { r0 = 1.0; r1 = 0.58; r2 = 0.04; }
                        else { r0 = 0.92; r1 = 0.96; r2 = 1.0; }
                        c[0] = (float)(c[0] + glowLum * r0);
                        c[1] = (float)(c[1] + glowLum * r1);
                        c[2] = (float)(c[2] + glowLum * r2);
                    }
                }
                else if (composite)
                {
                    // NTSC composite: chroma bleed and the subcarrier's cross-talk
                    double curY = 0.299 * c[0] + 0.587 * c[1] + 0.114 * c[2];
                    double inI = 0.596 * c[0] - 0.274 * c[1] - 0.322 * c[2];
                    double inQ = 0.211 * c[0] - 0.523 * c[1] + 0.312 * c[2];
                    compI = compI * 0.72 + inI * 0.28;
                    compQ = compQ * 0.65 + inQ * 0.35;
                    double dY = curY - lastY;
                    lastY = curY;
                    compRing = compRing * 0.65 + dY * 0.35;
                    double phaseI = compI + compRing * JsMath.Sin(ox * 1.2 + (phase > 0 ? phase * Math.PI * 2 : 0));
                    c[0] = (float)Math.Max(0, curY + 0.956 * phaseI + 0.621 * compQ);
                    c[1] = (float)Math.Max(0, curY - 0.272 * phaseI - 0.647 * compQ);
                    c[2] = (float)Math.Max(0, curY - 1.106 * phaseI + 1.703 * compQ);
                    if (scan > 0)
                    {
                        int across = Math.Min(BeamSteps, (int)((v - py) * BeamSteps + 0.5));
                        double b = 1 - scan + scan * BeamPeak * beam[across];
                        for (int j = 0; j < 3; j++) c[j] = (float)(c[j] * (b * fade));
                    }
                }
                else if (vhs)
                {
                    // the tape's chroma smear and the tracking noise band
                    double curLum = 0.299 * c[0] + 0.587 * c[1] + 0.114 * c[2];
                    const double a = 0.22;
                    for (int j = 0; j < 3; j++) chroma[j] = (float)(chroma[j] * (1 - a) + (c[j] - curLum) * a);
                    double smearU = Math.Min(w - 1, u + 2.5 / sx);
                    int smearP = (int)((py * w + Math.Floor(smearU)) * 3);
                    // (past the picture's start the website reads undefined: NaN, which Math.Max keeps and the byte makes black)
                    double s0 = Lin(smearP), s1 = Lin(smearP + 1), s2 = Lin(smearP + 2);
                    double smearLum = 0.299 * s0 + 0.587 * s1 + 0.114 * s2;
                    c[0] = (float)Math.Max(0, curLum + chroma[0] * 0.6 + (s0 - smearLum) * 0.4);
                    c[1] = (float)Math.Max(0, curLum + chroma[1] * 0.6 + (s1 - smearLum) * 0.4);
                    c[2] = (float)Math.Max(0, curLum + chroma[2] * 0.6 + (s2 - smearLum) * 0.4);
                    // the heads' snow in the tracking band
                    if (headNoise && Hash(ox, oy, 33 + phaseSeed) < 0.45 * tracking)
                    {
                        float snow = Hash(ox, oy, 44 + phaseSeed) > 0.5 ? 0.95f : 0.05f;
                        c[0] = c[1] = c[2] = snow;
                    }
                    if (scan > 0)
                    {
                        int across = Math.Min(BeamSteps, (int)((v - py) * BeamSteps + 0.5));
                        double b = 1 - scan * 0.6 + scan * 0.6 * BeamPeak * beam[across];
                        for (int j = 0; j < 3; j++) c[j] = (float)(c[j] * (b * fade));
                    }
                }
                else if (crt)
                {
                    double b = 1;
                    if (scan > 0)
                    {
                        int level = Math.Min(BeamLevels - 1, (int)(Math.Sqrt(Colour.Luminance(c[0], c[1], c[2])) * BeamLevels));
                        int across = Math.Min(BeamSteps, (int)((v - py) * BeamSteps + 0.5));
                        b = 1 - scan + scan * BeamPeak * beam[level * (BeamSteps + 1) + across];
                    }
                    double mr = 1, mg = 1, mb = 1;
                    if (mask != "none")
                    {
                        int third = ox % 3;
                        bool dark = mask == "shadow" && (oy + ((ox / 3) & 1) * 2) % 4 == 0;
                        mr = third == 0 ? 1 : MaskDim;
                        mg = third == 1 ? 1 : MaskDim;
                        mb = third == 2 ? 1 : MaskDim;
                        if (dark)
                        {
                            mr *= MaskDim;
                            mg *= MaskDim;
                            mb *= MaskDim;
                        }
                    }
                    double f = b * gain * fade;
                    c[0] = (float)(c[0] * (f * mr));
                    c[1] = (float)(c[1] * (f * mg));
                    c[2] = (float)(c[2] * (f * mb));
                    if (glow != null) SampleBilinear(glow, w, h, u - 0.5, v - 0.5, c, bloom * fade);
                }

                // the VHS on-screen display: PLAY ▶ at the top left, SP -0:12:34 at the bottom left
                if (vhs && showOsd)
                {
                    OsdPixel(topOsd, ox - 14 * osdScale, oy - 12 * osdScale, osdScale, out bool topFg, out bool topBg);
                    OsdPixel(bottomOsd, ox - 14 * osdScale, oy - (outH - 22 * osdScale), osdScale, out bool botFg, out bool botBg);
                    if (topFg || botFg)
                    {
                        // phosphor green, sRGB 0, 255, 65
                        c[0] = 0;
                        c[1] = 1.0f;
                        c[2] = 0.06f;
                        shows = true;
                    }
                    else if (topBg || botBg)
                    {
                        c[0] = c[1] = c[2] = 0;
                        shows = true;
                    }
                }

                row[ox * 3] = (byte)Colour.LinearToByte(c[0]);
                row[ox * 3 + 1] = (byte)Colour.LinearToByte(c[1]);
                row[ox * 3 + 2] = (byte)Colour.LinearToByte(c[2]);
                if (alpha != null) alpha[ox] = shows ? (byte)255 : (byte)0;
            }
        }


        void Tint(float[] c, double lum)
        {
            if (p1)
            {
                c[0] = (float)(lum * 0.16);
                c[1] = (float)(lum * 1.0);
                c[2] = (float)(lum * 0.2);
            }
            else if (p3)
            {
                c[0] = (float)(lum * 1.0);
                c[1] = (float)(lum * 0.58);
                c[2] = (float)(lum * 0.04);
            }
            else
            {
                c[0] = (float)(lum * 0.92);
                c[1] = (float)(lum * 0.96);
                c[2] = (float)(lum * 1.0);
            }
        }

        // Rows from..from + count - 1, on every core: r, g, b (and a) a pixel; with a backdrop,
        // solid, the hidden pixels in its colour.
        public byte[][] Rows(int from, int count, bool withAlpha, byte[] backdrop)
        {
            var rows = new byte[count][];
            EveryCore.For(0, count, () => (new byte[outW * 3], new byte[outW]), (i, state, buffers) =>
            {
                byte[] rgb = buffers.Item1, shows = buffers.Item2;
                Row(from + i, rgb, shows);
                var output = new byte[outW * (withAlpha ? 4 : 3)];
                for (int x = 0, o = 0; x < outW; x++)
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

        // the whole effect as rows of r, g, b, a, on every core
        public byte[] Rgba()
        {
            var output = new byte[(long)outW * outH * 4];
            EveryCore.For(0, outH, () => (new byte[outW * 3], new byte[outW]), (y, state, buffers) =>
            {
                Row(y, buffers.Item1, buffers.Item2);
                long o = (long)y * outW * 4;
                for (int x = 0; x < outW; x++, o += 4)
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

        // a soft blur of the picture at its own size (a 3-tap box, twice each way), for the bloom
        static float[] Blur(float[] lin, int w, int h)
        {
            var a = (float[])lin.Clone();
            var b = new float[lin.Length];
            for (int pass = 0; pass < 2; pass++)
            {
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        int l = Math.Max(0, x - 1), r = Math.Min(w - 1, x + 1);
                        for (int j = 0; j < 3; j++)
                            b[(y * w + x) * 3 + j] = (float)(((double)a[(y * w + l) * 3 + j] + a[(y * w + x) * 3 + j] + a[(y * w + r) * 3 + j]) / 3);
                    }
                Swap(ref a, ref b);
                for (int y = 0; y < h; y++)
                {
                    int up = Math.Max(0, y - 1), down = Math.Min(h - 1, y + 1);
                    for (int x = 0; x < w; x++)
                        for (int j = 0; j < 3; j++)
                            b[(y * w + x) * 3 + j] = (float)(((double)a[(up * w + x) * 3 + j] + a[(y * w + x) * 3 + j] + a[(down * w + x) * 3 + j]) / 3);
                }
                Swap(ref a, ref b);
            }
            return a;
        }

        static void Swap(ref float[] a, ref float[] b)
        {
            float[] t = a;
            a = b;
            b = t;
        }

        static void SampleBilinear(float[] img, int w, int h, double x, double y, float[] into, double amount)
        {
            int x0 = (int)Math.Max(0, Math.Min(w - 1, Math.Floor(x))), y0 = (int)Math.Max(0, Math.Min(h - 1, Math.Floor(y)));
            int x1 = Math.Min(w - 1, x0 + 1), y1 = Math.Min(h - 1, y0 + 1);
            double fx = Clamp01(x - x0), fy = Clamp01(y - y0);
            for (int j = 0; j < 3; j++)
            {
                double top = img[(y0 * w + x0) * 3 + j] * (1 - fx) + img[(y0 * w + x1) * 3 + j] * fx;
                double bot = img[(y1 * w + x0) * 3 + j] * (1 - fx) + img[(y1 * w + x1) * 3 + j] * fx;
                into[j] = (float)(into[j] + (top * (1 - fy) + bot * fy) * amount);
            }
        }
    }
}
