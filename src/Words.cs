using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using B0r3dDeRes.Engine;

namespace B0r3dDeRes
{
    // The lines the window shows about the picture, in the website's words (its ui/controls.ts and
    // ui/app.ts), so the PC and img.b0r3d.org read the same.
    static class Words
    {
        // a number to one place, as the website writes it: 4.5, 3, 12.3
        static string OneDp(double v) => (JsMath.Round(v * 10) / 10).ToString(CultureInfo.InvariantCulture);

        // a whole number, rounded as the website rounds (halves up)
        static string Whole(double v) => JsMath.Round(v).ToString(CultureInfo.InvariantCulture);

        // The line under Resolution: the machine's own size, a custom size against it, and how many
        // of the photo's pixels go into each of the machine's (no picture: the first part only).
        public static string Resolution(Profile p, bool custom, Geometry g, double coverX, double coverY, bool hasPicture)
        {
            string shape = p.SquarePixels ? "" : p.ParX < p.ParY ? ", each taller than wide" : ", each wider than tall";
            var parts = new List<string>
            {
                p.AutoHeight ? p.Name + ": " + p.Width + " pixels across, as long as the photo needs." : p.Name + ": " + p.Width + "×" + p.Height + " pixels" + shape + ".",
            };
            if (custom && g != null)
            {
                double r = (double)g.Width / p.Width;
                string against = g.Width == p.Width ? "the same width" : r < 1 ? Whole(r * 100) + "% of its width" : OneDp(r) + "× its width";
                parts.Add("Yours: " + g.Width + "×" + g.Height + ", " + against + ".");
            }
            if (hasPicture)
            {
                double mean = Math.Sqrt(coverX * coverY);
                parts.Add(mean < 1
                    ? "Your photo is smaller, so it’s enlarged " + OneDp(1 / mean) + "×."
                    : "Each pixel averages " + OneDp(coverX) + " × " + OneDp(coverY) + " of your photo’s pixels.");
            }
            return string.Join(" ", parts);
        }

        // "14 of 16 colours used · 320×200 · 5:6 pixels · 217 / 256 tiles (fits in VRAM)"
        public static string Readout(Rendered r)
        {
            int? total = Profiles.ColourTotal(r.Profile, r.Palette);
            string colours = total == null ? r.Used + " colours used" : r.Used + " of " + total + " colours used";
            string shape = r.Geometry.SquarePixels ? "square pixels" : r.Geometry.ParX + ":" + r.Geometry.ParY + " pixels";
            return colours + " · " + r.Width + "×" + r.Height + " · " + shape + (r.TileFit != null ? " · " + r.TileFit.Summary : "");
        }

        // "a", "a and b", "a, b and c"
        public static string List(IList<string> items) =>
            items.Count <= 1 ? string.Join("", items) : string.Join(", ", items.Take(items.Count - 1)) + " and " + items[items.Count - 1];

        // "rukus.png · 256×256"; an animated GIF's "· 58 frames · 3.9 s" (at its own size), or a
        // GIF opened as a still's "· first of 58 frames"
        public static string PictureInfo(LoadedPicture picture)
        {
            if (picture == null) return "No picture yet";
            // a video: its frames' size, its length and speed ("clip.mp4 · 1920×1080 · 12.4 s at 30 a second")
            if (picture.Video != null)
            {
                var file = picture.Video.File;
                return picture.Name + " · " + picture.Source.Width + "×" + picture.Source.Height + " · " + Length(file.Duration) + " at " + FrameRate(file.Fps) + " a second" + (file.HasSound ? "" : ", no sound");
            }
            AnimatedGif a = picture.Animation;
            string text = picture.Name + " · " + (a != null ? a.OrigW + "×" + a.OrigH : picture.Source.Width + "×" + picture.Source.Height);
            string frames = a != null ? GifInfo.FramesText(picture.Frames, a.Frames.Length, a.Total, a.DurationMs, true) : GifInfo.FramesText(picture.Frames, 0, 0, 0, false);
            return frames.Length > 0 ? text + " · " + frames : text;
        }

        // What GIF and APNG come out at, when it isn't the Size chosen (the website's words by its
        // buttons); "" when it is. `withFx`: the screen effect goes into exports; `signalLoop`: the
        // Screen FX loop with the signal on (its length counts); `screenFxLoop`: the Screen FX loop
        // without the signal (it's the effect itself, at any size).
        public static string AnimationHint(AnimationFit fit, Geometry g, bool tv, int chosen, bool withFx, bool signalLoop, bool screenFxLoop, bool custom)
        {
            if (fit.Next == null) return "";
            string whyNot = fit.Limit == "frame" ? "too big for an animation" : "too big for " + fit.Held + " frames";
            if (fit.Multiplier == null)
            {
                Export.Size(g.Width, g.Height, g.ParX, g.ParY, fit.Next.Value, tv, out int aw, out int ah);
                return "GIF and APNG can't be made: even " + fit.Next + "× (" + aw + "×" + ah + " px) is " + whyNot + "." +
                    (fit.Limit == "frames" && signalLoop ? " Pick a shorter loop." : fit.Limit == "frame" && custom ? " Pick a smaller width." : "");
            }
            Export.Size(g.Width, g.Height, g.ParX, g.ParY, fit.Multiplier.Value, tv, out int w, out int h);
            bool fxLost = withFx && fit.Multiplier < Export.FxMinMultiplier && !screenFxLoop;
            return "GIF and APNG come out at " + fit.Multiplier + "× (" + w + "×" + h + " px)" + (fxLost ? ", without the screen effect" : "") + ": " +
                (fit.Next == chosen ? fit.Next + "× is" : fit.Next + "× and up are") + " " + whyNot + ".";
        }

        // what's being made, in the bottom line ("Making the Screen FX loop GIF… frame 3 of 12")
        public static string AnimationName(string style, bool signalOn) =>
            style == Animation.GifFrames ? "the" : style == Animation.ScreenFx ? (signalOn ? "signal loop" : "Screen FX loop") : style == Animation.CompareWipe ? "Before & After wipe" : "Dither shimmer";

        public static string Native(Profile p) => "Native " + p.Width + "×" + (p.AutoHeight ? "auto" : p.Height.ToString(CultureInfo.InvariantCulture));

        public static string About(Profile p) => p.Origin + ". " + p.About;

        public static string DetailHint(bool nothingToBringBack) => nothingToBringBack
            ? "Your photo isn’t shrunk here, so there’s nothing for it to bring back."
            : "Brings back thin lines (eyes, outlines, stripes) that shrinking blurs away. 0% is a plain average.";

        public static string ShapeNote(Geometry g) =>
            "This machine's pixels are " + g.ParX + ":" + g.ParY + " (width:height). \"As on a TV\" stretches the PNG to match.";

        // "640×576 px · 4-colour palette": the PNG's size and what its colours are (full colour
        // with the screen effect)
        // `connection`: the signal's ("rf"…), when it's on
        public static string SizeLine(int width, int height, Profile p, DeresSettings s, bool withFx = false, string connection = null) =>
            width + "×" + height + " px · " + (connection != null ? "full colour, through " + Signal.ConnectionNames[connection] : withFx ? "full colour" : ColoursLabel(p, s));

        // the line under the Signal's buttons
        public static string SignalNote(Profile p, SignalMachine machine, string connection) =>
            connection == null
                ? "Off. The real thing: the picture goes out as an analog TV signal and comes back with the colour bleed, fringes and noise a TV showed."
                : "The " + p.Name + " over " + Signal.ConnectionNames[connection] + "." +
                    (machine.CgaArtifact ? " Its colours are made by the signal itself: each one is a pattern of black and white dots."
                        : machine.LineLocked ? " Its colour fringes stand still, as they did on the real thing." : "");

        static string ColoursLabel(Profile p, DeresSettings s)
        {
            switch (p.Kind)
            {
                case PaletteKind.Direct: return "15-bit direct";
                case PaletteKind.Ham: return "12-bit HAM6";
                case PaletteKind.N64: return "16-bit RGBA5551";
                case PaletteKind.Pool: return Profiles.ColourCount(p, s.CustomCount) + " from " + p.Pool.Length + " colours";
                case PaletteKind.Cell:
                    if (p.Mode == "gbc-tile") return "8 palettes of 4 colours (32 max)";
                    if (p.Mode == "msx-screen2") return "2 colours per 8×1 line (15 colours)";
                    return "cell clash (" + p.Colours.Length + " colours)";
                case PaletteKind.Analog:
                    if (p.Mode == Analog.Fax) return "1-bit fax";
                    if (p.Mode == Analog.Risograph) return "2-ink risograph";
                    if (p.Mode == Analog.Newspaper) return "CMYK halftone rosette";
                    if (p.Mode == Analog.DeepFry) return "8×8 DCT crunch";
                    return "640×480 CCD sensor";
                case PaletteKind.Fixed: return p.Colours.Length + "-colour palette";
                case PaletteKind.Range: return Profiles.ColourCount(p, s.CustomCount) + "-colour palette";
                default: return (s.CustomPalette?.Length ?? Profiles.ColourCount(p, s.CustomCount) ?? 16) + "-colour palette";
            }
        }

        // the Export section's button for a machine's own file
        public static string EmulatorButton(Profile p, string format)
        {
            switch (format)
            {
                case "scr": return "Save .scr (ZX Spectrum)…";
                case "koa": return "Save .koa (C64 Koala)…";
                default: return p.Mode == "nes-attr" ? "Save .chr (NES tiles)…" : "Save .chr (Game Boy tiles)…";
            }
        }

        // what a kind of machine file is, in the Save dialog
        public static string EmulatorType(string format) =>
            format == "scr" ? "ZX Spectrum screen" : format == "koa" ? "Koala Painter picture" : "Tiles";

        // under Transparent areas, with Keep picked
        public const string KeepNote = "They stay see-through in PNGs (a pixel shows when at least half of it is covered). " +
            "In .scr and .koa files they’re black; in .chr tiles they’re colour 0, see-through on sprites.";

        // the sliders' values, as the website shows them
        public static string Percent(double v) => Whole(v * 100) + "%";

        // Dub it's value: how many times the tape was copied
        public static string Dubs(int n) => n <= 0 ? "off" : n == 1 ? "once" : n == 2 ? "twice" : n + " times";

        // a frame rate: "30", "29.97"
        public static string FrameRate(double fps) => Math.Abs(fps - Math.Round(fps)) < 0.005 ? Math.Round(fps).ToString(CultureInfo.InvariantCulture) : fps.ToString("0.##", CultureInfo.InvariantCulture);

        // a video's length (100 ns steps): "12.4 s", "3:05", "1:02:07"
        public static string Length(long time)
        {
            double s = Math.Max(0, time / 1e7);
            if (s < 59.95) return s.ToString("0.0", CultureInfo.InvariantCulture) + " s";
            long whole = (long)Math.Round(s);
            return whole >= 3600 ? (whole / 3600) + ":" + (whole / 60 % 60).ToString("00") + ":" + (whole % 60).ToString("00") : (whole / 60) + ":" + (whole % 60).ToString("00");
        }
        public static string SignedPercent(double v) => (v > 0 ? "+" : "") + Whole(v * 100);
        public static string Gamma(double v) => v.ToString("0.00", CultureInfo.InvariantCulture);
    }
}
