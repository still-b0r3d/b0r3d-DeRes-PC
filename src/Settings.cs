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
        public bool TvPixels;           // Export → Pixel shape: as on a TV, not raw
        public int Multiplier = 4;      // Export → Size

        // a fresh start, as the website's first visit
        public static Settings Defaults() => new Settings();

        public Settings Copy()
        {
            var copy = (Settings)MemberwiseClone();
            copy.Deres = Deres.Clone();
            return copy;
        }

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
                }
            }
            // a machine's dither when the one kept isn't one there is
            d.Dither = dither != null && Dither.ById(dither) != null ? dither : (Profiles.ById(d.ProfileId) ?? Profiles.Default).Dither;
            // The Colours slider starts at a machine's own count whenever one is picked, so a kept
            // count only stays for the Custom machine, as on the website.
            if (d.ProfileId != Profiles.CustomId) d.CustomCount = null;
            return s;
        }

        // a number as the website keeps them: plain, with a point, finite
        static bool Number(string text, out double value) =>
            double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && !double.IsNaN(value) && !double.IsInfinity(value);

        static double Clamp(double v, double lo, double hi) => Math.Min(hi, Math.Max(lo, v));

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
            Line("pixel_shape", TvPixels ? "tv" : "raw");
            Line("size", Multiplier.ToString(CultureInfo.InvariantCulture));
            return text.ToString();
        }

        // false when settings can't be kept (DataFolder.WhyNotKept says why)
        public bool Save() => DataFolder.TryWrite(FileName, ToText());
    }
}
