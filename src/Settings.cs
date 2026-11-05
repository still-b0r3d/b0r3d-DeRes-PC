using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using B0r3dDeRes.Engine;

namespace B0r3dDeRes
{
    // View → Look: like Windows' apps (the default), or always light or dark.
    enum Look { Windows, Light, Dark }

    // The settings, kept as key=value lines in b0r3dDeRes.ini in the data folder (a name of its
    // own, so a --data folder's other files are never touched). Never the picture, its name or
    // where it came from: like the website, only how the program looks and the settings.
    sealed class Settings
    {
        public const string FileName = "b0r3dDeRes.ini";
        public const double MinWindow = 600, MaxWindow = 20000;

        public Look Look = Look.Windows;
        public double WindowWidth = 1280, WindowHeight = 800;   // device-independent pixels
        public bool Maximized;

        public DeresSettings Deres = new DeresSettings();
        public SignalSettings Signal = new SignalSettings();   // Signal
        public FxSettings Fx = new FxSettings();   // Screen
        public bool TvPixels;           // Export → Pixel shape: as on a TV, not raw
        public int Multiplier = 4;      // Export → Size
        public bool ExportFx;           // Export → With the screen effect
        // a signal loop's frames and frames a second (the website's; kept and passed on in links)
        public int LoopFrames = 16, LoopFps = 15;
        // Export → Animation loop: the Screen FX loop, the A/B wipe or the dither shimmer (an
        // animated GIF's own frames are picked for each GIF opened, never kept)
        public string AnimationStyle = Animation.ScreenFx;
        // Export, for a video: the frame rate it goes out at (0: the video's own), and whether an MP4 keeps its sound
        public int VideoRate;
        public bool VideoSound = true;
        // the ASCII / ANSI studio's settings (the website keeps them in the browser), and the size its picture is saved at
        public AsciiSettings Ascii = new AsciiSettings();
        public int AsciiScale = 1;

        public static readonly int[] LoopFrameCounts = { 16, 32, 48 }, LoopSpeeds = { 15, 30 };
        // the slower frame rates a video can go out at (each only below its own)
        public static readonly int[] VideoRates = { 30, 25, 24, 15, 12, 10 };

        // the frame rate a video of `own` frames a second goes out at
        public double VideoFps(double own) => VideoRate > 0 && VideoRate < own - 0.01 ? VideoRate : own;

        // a fresh start, as the website's first visit
        public static Settings Defaults() => new Settings();

        public Settings Copy()
        {
            var copy = (Settings)MemberwiseClone();
            copy.Deres = Deres.Clone();
            copy.Signal = Signal.Clone();
            copy.Fx = Fx.Clone();
            copy.Ascii = Ascii.Clone();
            return copy;
        }

        // the screen effect an export gets: with the box ticked and a screen on (from 3× up)
        public FxSettings ExportScreen => ExportFx && Fx.Screen != Screens.Off ? Fx : null;

        // the signal, while it's on (null: off)
        public SignalSettings SignalOn => Signal.Mode != SignalModes.Off ? Signal : null;

        public static Settings Load() => Load(System.IO.Path.Combine(DataFolder.Path, FileName));

        // missing or garbled lines keep their defaults, field by field, as the website checks its own
        internal static Settings Load(string path)
        {
            var s = new Settings();
            string[] lines;
            try
            {
                if (!File.Exists(path)) return s;
                lines = File.ReadAllLines(path);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is System.Security.SecurityException)
            {
                return s;
            }
            DeresSettings d = s.Deres;
            string dither = null;
            // the studio's, checked together at the end as the website checks what it kept
            string asciiMode = null, asciiRamp = null, asciiCustom = null, asciiColours = null;
            double? asciiColumns = null, asciiContrast = null, asciiBrightness = null, asciiGamma = null;
            bool? asciiInvert = null, asciiAspect = null, asciiSeparated = null, asciiHeader = null;
            foreach (string line in lines)
            {
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                string key = line.Substring(0, eq).Trim().ToLowerInvariant();
                string value = line.Substring(eq + 1).Trim();
                bool named = value.Length > 0 && char.IsLetter(value[0]);  // "dark", never a number
                switch (key)
                {
                    case "look":
                        if (named && Enum.TryParse(value, true, out Look look) && Enum.IsDefined(typeof(Look), look)) s.Look = look;
                        break;
                    case "window":
                        ReadWindow(s, value);
                        break;
                    case "machine":
                        if (Profiles.ById(value) != null) d.ProfileId = value;
                        break;
                    case "framing":
                        if (value == Fits.Fill || value == Fits.Fit || value == Fits.Stretch) d.Fit = value;
                        break;
                    case "resolution":
                        if (value == "native" || value == "custom") d.Custom = value == "custom";
                        break;
                    case "width":
                        if (Number(value, out double width)) d.CustomWidth = (int)JsMath.Round(Clamp(width, 1, Geometry.CustomMax));
                        break;
                    case "detail":
                        if (Number(value, out double detail)) d.Detail = Clamp(detail, 0, 1);
                        break;
                    case "transparent":
                        if (value == Backgrounds.White || value == Backgrounds.Black || value == Backgrounds.Transparent) d.Background = value;
                        break;
                    case "colours":
                        if (Number(value, out double count)) d.CustomCount = (int)JsMath.Round(Clamp(count, 2, 256));
                        break;
                    case "palette":
                        string[] palette = value.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries).Where(PaletteFile.IsColour).Take(PaletteFile.MaxColours).ToArray();
                        d.CustomPalette = palette.Length > 0 ? palette : null;
                        break;
                    case "brightness":
                        if (Number(value, out double brightness)) d.Adjust.Brightness = Clamp(brightness, -0.5, 0.5);
                        break;
                    case "contrast":
                        if (Number(value, out double contrast)) d.Adjust.Contrast = Clamp(contrast, 0, 2);
                        break;
                    case "saturation":
                        if (Number(value, out double saturation)) d.Adjust.Saturation = Clamp(saturation, 0, 2);
                        break;
                    case "gamma":
                        if (Number(value, out double gamma)) d.Adjust.Gamma = Clamp(gamma, 0.4, 2.5);
                        break;
                    case "dither":
                        dither = value;
                        break;
                    case "pixel_shape":
                        if (value == "raw" || value == "tv") s.TvPixels = value == "tv";
                        break;
                    case "size":
                        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int m) && Export.Multipliers.Contains(m)) s.Multiplier = m;
                        break;
                    case "screen_in_export":
                        if (value == "on" || value == "off") s.ExportFx = value == "on";
                        break;
                    case "screen":
                        if (Screens.All.Contains(value)) s.Fx.Screen = value;
                        break;
                    case "mask":
                        if (Screens.Masks.Contains(value)) s.Fx.Mask = value;
                        break;
                    case "scanlines":
                        if (Number(value, out double scanlines)) s.Fx.Scanlines = Clamp(scanlines, 0, 1);
                        break;
                    case "curve":
                        if (Number(value, out double curve)) s.Fx.Curve = Clamp(curve, 0, 1);
                        break;
                    case "bloom":
                        if (Number(value, out double bloom)) s.Fx.Bloom = Clamp(bloom, 0, 1);
                        break;
                    case "pixel_grid":
                        if (Number(value, out double grid)) s.Fx.Grid = Clamp(grid, 0, 1);
                        break;
                    case "ghosting":
                        if (Number(value, out double ghost)) s.Fx.Ghost = Clamp(ghost, 0, 1);
                        break;
                    case "tracking":
                        if (Number(value, out double tracking)) s.Fx.VhsTracking = Clamp(tracking, 0, 1);
                        break;
                    case "on_screen_display":
                        if (value == "on" || value == "off") s.Fx.VhsOsd = value == "on";
                        break;
                    case "signal":
                        if (SignalModes.All.Contains(value)) s.Signal.Mode = value;
                        break;
                    case "wear":
                        if (Number(value, out double wear)) s.Signal.Wear = Clamp(wear, 0, 1);
                        break;
                    case "signal_colour":
                        if (Number(value, out double colour)) s.Signal.Colour = Clamp(colour, 0, 1.5);
                        break;
                    case "sharpen":
                        if (Number(value, out double sharpen)) s.Signal.Sharpen = Clamp(sharpen, 0, 1);
                        break;
                    case "decoder":
                        if (value == SignalModes.Notch || value == SignalModes.Comb) s.Signal.Decoder = value;
                        break;
                    case "signal_engine":
                        try
                        {
                            s.Signal.Engine = Engine.Signal.CheckEngine(Json.Parse(value));
                        }
                        catch (FormatException)
                        {
                        }
                        break;
                    case "dubs":
                        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int dubs)) s.Signal.Dubs = Math.Min(SignalSettings.MaxDubs, Math.Max(0, dubs));
                        break;
                    case "loop_frames":
                        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int frames) && LoopFrameCounts.Contains(frames)) s.LoopFrames = frames;
                        break;
                    case "loop_fps":
                        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int fps) && LoopSpeeds.Contains(fps)) s.LoopFps = fps;
                        break;
                    case "animation":
                        if (Animation.Styles.Contains(value)) s.AnimationStyle = value;
                        break;
                    case "video_rate":
                        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int rate) && (rate == 0 || VideoRates.Contains(rate))) s.VideoRate = rate;
                        break;
                    case "video_sound":
                        if (value == "on" || value == "off") s.VideoSound = value == "on";
                        break;
                    case "ascii_mode":
                        asciiMode = value;
                        break;
                    case "ascii_ramp":
                        asciiRamp = value;
                        break;
                    case "ascii_colours":
                        asciiColours = value;
                        break;
                    case "ascii_custom_ramp":
                        // (kept as JSON text: a ramp can hold any character)
                        try
                        {
                            asciiCustom = Json.Parse(value) as string;
                        }
                        catch (FormatException)
                        {
                        }
                        break;
                    case "ascii_columns":
                        if (Number(value, out double asciiCols)) asciiColumns = asciiCols;
                        break;
                    case "ascii_contrast":
                        if (Number(value, out double ac)) asciiContrast = ac;
                        break;
                    case "ascii_brightness":
                        if (Number(value, out double ab)) asciiBrightness = ab;
                        break;
                    case "ascii_gamma":
                        if (Number(value, out double ag)) asciiGamma = ag;
                        break;
                    case "ascii_invert":
                        asciiInvert = OnOff(value);
                        break;
                    case "ascii_aspect":
                        asciiAspect = OnOff(value);
                        break;
                    case "ascii_teletext_separated":
                        asciiSeparated = OnOff(value);
                        break;
                    case "ascii_teletext_header":
                        asciiHeader = OnOff(value);
                        break;
                    case "ascii_size":
                        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int scale) && AsciiRender.Scales.Contains(scale)) s.AsciiScale = scale;
                        break;
                }
            }
            // a machine's dither when the one kept isn't one there is
            d.Dither = dither != null && Dither.ById(dither) != null ? dither : (Profiles.ById(d.ProfileId) ?? Profiles.Default).Dither;
            // The Colours slider starts at a machine's own count whenever one is picked, so a kept
            // count only stays for the Custom machine, as on the website.
            if (d.ProfileId != Profiles.CustomId) d.CustomCount = null;
            s.Ascii = AsciiSettings.Check(asciiMode, asciiColumns, asciiRamp, asciiCustom, asciiInvert, asciiColours, asciiContrast, asciiBrightness, asciiGamma, asciiAspect, asciiSeparated, asciiHeader);
            return s;
        }

        // a number as the website keeps them: plain, with a point, finite
        static bool Number(string text, out double value) =>
            double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && !double.IsNaN(value) && !double.IsInfinity(value);

        static double Clamp(double v, double lo, double hi) => Math.Min(hi, Math.Max(lo, v));

        // text as a JSON string of plain ASCII (everything else escaped as a UTF-16 unit), so it reads back exactly
        static string AsciiText(string text)
        {
            const char Backslash = (char)92;
            var b = new StringBuilder("\"");
            foreach (char c in text)
            {
                if (c == '"' || c == Backslash) b.Append(Backslash).Append(c);
                else if (c < 0x20 || c > 0x7e) b.Append(Backslash).Append('u').Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                else b.Append(c);
            }
            return b.Append('"').ToString();
        }

        static bool? OnOff(string value) => value == "on" ? true : value == "off" ? false : (bool?)null;

        // "1280x800", or "1280x800 maximized"
        static void ReadWindow(Settings s, string value)
        {
            string[] parts = value.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return;
            string[] wh = parts[0].Split('x');
            if (wh.Length != 2 || !Number(wh[0], out double w) || !Number(wh[1], out double h)) return;
            s.WindowWidth = Clamp(w, MinWindow, MaxWindow);
            s.WindowHeight = Clamp(h, MinWindow * 0.75, MaxWindow);
            s.Maximized = parts.Length > 1 && parts[1].Equals("maximized", StringComparison.OrdinalIgnoreCase);
        }

        static string N(double v) => v.ToString("R", CultureInfo.InvariantCulture);

        public string ToText()
        {
            DeresSettings d = Deres;
            var text = new StringBuilder();
            void Line(string key, string value) => text.Append(key).Append('=').Append(value).Append("\r\n");
            Line("look", Look.ToString().ToLowerInvariant());
            Line("window", Math.Round(WindowWidth).ToString(CultureInfo.InvariantCulture) + "x" + Math.Round(WindowHeight).ToString(CultureInfo.InvariantCulture) + (Maximized ? " maximized" : ""));
            Line("machine", d.ProfileId);
            Line("framing", d.Fit);
            Line("resolution", d.Custom ? "custom" : "native");
            if (d.CustomWidth.HasValue) Line("width", d.CustomWidth.Value.ToString(CultureInfo.InvariantCulture));
            Line("detail", N(d.Detail));
            Line("transparent", d.Background);
            Line("brightness", N(d.Adjust.Brightness));
            Line("contrast", N(d.Adjust.Contrast));
            Line("saturation", N(d.Adjust.Saturation));
            Line("gamma", N(d.Adjust.Gamma));
            Line("dither", d.Dither);
            if (d.CustomCount.HasValue) Line("colours", d.CustomCount.Value.ToString(CultureInfo.InvariantCulture));
            if (d.CustomPalette != null && d.CustomPalette.Length > 0) Line("palette", string.Join(" ", d.CustomPalette));
            Line("signal", Signal.Mode);
            Line("wear", N(Signal.Wear));
            Line("signal_colour", N(Signal.Colour));
            Line("sharpen", N(Signal.Sharpen));
            Line("decoder", Signal.Decoder);
            if (Signal.Engine.Count > 0) Line("signal_engine", Signal.EngineText);
            if (Signal.Dubs > 0) Line("dubs", Signal.Dubs.ToString(CultureInfo.InvariantCulture));
            Line("loop_frames", LoopFrames.ToString(CultureInfo.InvariantCulture));
            Line("loop_fps", LoopFps.ToString(CultureInfo.InvariantCulture));
            Line("animation", AnimationStyle);
            Line("video_rate", VideoRate.ToString(CultureInfo.InvariantCulture));
            Line("video_sound", VideoSound ? "on" : "off");
            Line("screen", Fx.Screen);
            Line("mask", Fx.Mask);
            Line("scanlines", N(Fx.Scanlines));
            Line("curve", N(Fx.Curve));
            Line("bloom", N(Fx.Bloom));
            Line("pixel_grid", N(Fx.Grid));
            Line("ghosting", N(Fx.Ghost));
            Line("tracking", N(Fx.VhsTracking));
            Line("on_screen_display", Fx.VhsOsd ? "on" : "off");
            Line("pixel_shape", TvPixels ? "tv" : "raw");
            Line("size", Multiplier.ToString(CultureInfo.InvariantCulture));
            Line("screen_in_export", ExportFx ? "on" : "off");
            Line("ascii_mode", Ascii.Mode);
            Line("ascii_columns", N(Ascii.Columns));
            Line("ascii_ramp", Ascii.RampPreset);
            Line("ascii_custom_ramp", AsciiText(Ascii.CustomRamp ?? ""));
            Line("ascii_invert", Ascii.InvertRamp ? "on" : "off");
            Line("ascii_colours", Ascii.ColorMode);
            Line("ascii_contrast", N(Ascii.Contrast));
            Line("ascii_brightness", N(Ascii.Brightness));
            Line("ascii_gamma", N(Ascii.Gamma));
            Line("ascii_aspect", Ascii.TwoToOneAspect ? "on" : "off");
            Line("ascii_teletext_header", Ascii.TeletextHeader ? "on" : "off");
            Line("ascii_teletext_separated", Ascii.TeletextSeparated ? "on" : "off");
            Line("ascii_size", AsciiScale.ToString(CultureInfo.InvariantCulture));
            return text.ToString();
        }

        // false when settings can't be kept (DataFolder.WhyNotKept says why)
        public bool Save() => DataFolder.TryWrite(FileName, ToText());
    }
}
