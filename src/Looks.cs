using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using B0r3dDeRes.Engine;

namespace B0r3dDeRes
{
    // Looks: every setting that makes the picture, without the picture. They go about as the
    // website's share links ("https://img.b0r3d.org/#s=…", made and read exactly as the website
    // does, so a link from either opens on the other) and as look files (.json, the website's own
    // names for its settings). Whatever comes in is checked field by field as the website checks a
    // link: anything missing or odd keeps what was there. (The website's core/share.ts and the
    // checks in ui/settings.ts.)
    static class Looks
    {
        public const string Site = "https://img.b0r3d.org/";
        public const string FileKind = "b0r3d DeRes";

        static readonly string[] screens = { "off", "crt", "lcd", "vhs", "composite", "p1-green", "p3-amber", "p4-white" };
        static readonly string[] screenNames = { "", "CRT", "LCD", "VHS", "composite", "green phosphor", "amber phosphor", "white phosphor" };
        static readonly string[] signals = { "off", "auto", "rf", "composite", "vhs-sp", "vhs-lp", "vhs-ep" };

        // ---- share links ----

        static string ToBase64Url(string text) =>
            Convert.ToBase64String(Encoding.UTF8.GetBytes(text)).Replace('+', '-').Replace('/', '_').TrimEnd('=');

        // As the website reads it: padded by its whole length, then atob's own reading (white space
        // dropped, one or two "=" taken off a whole length, anything else not base64 an error).
        static string FromBase64Url(string text)
        {
            string standard = text.Replace('-', '+').Replace('_', '/');
            while (standard.Length % 4 != 0) standard += "=";
            var data = new StringBuilder();
            foreach (char c in standard) if (c != '\t' && c != '\n' && c != '\f' && c != '\r' && c != ' ') data.Append(c);
            if (data.Length % 4 == 0 && data.Length > 0 && data[data.Length - 1] == '=')
            {
                data.Length--;
                if (data.Length > 0 && data[data.Length - 1] == '=') data.Length--;
            }
            if (data.Length % 4 == 1) throw new FormatException("Not base64");
            foreach (char c in data.ToString())
                if (!(c >= 'A' && c <= 'Z' || c >= 'a' && c <= 'z' || c >= '0' && c <= '9' || c == '+' || c == '/')) throw new FormatException("Not base64");
            while (data.Length % 4 != 0) data.Append('=');
            // (a broken UTF-8 sequence is an error, as decodeURIComponent makes it on the website)
            return new UTF8Encoding(false, true).GetString(Convert.FromBase64String(data.ToString()));
        }

        // the link's settings as the website writes them: short names, in its order
        public static string LinkJson(Settings settings)
        {
            DeresSettings d = settings.Deres;
            var parts = new List<string>
            {
                "\"p\":" + Json.Quote(d.ProfileId),
                "\"f\":" + Json.Quote(d.Fit),
                "\"r\":" + Json.Quote(d.Custom ? "custom" : "native"),
            };
            // (JSON.stringify leaves out a field that's undefined)
            if (d.CustomWidth.HasValue) parts.Add("\"w\":" + Json.Number(d.CustomWidth.Value));
            parts.Add("\"dt\":" + Json.Number(d.Detail));
            parts.Add("\"bg\":" + Json.Quote(d.Background));
            parts.Add("\"d\":" + Json.Quote(d.Dither));
            parts.Add("\"ps\":" + Json.Quote(settings.TvPixels ? "tv" : "raw"));
            parts.Add("\"m\":" + Json.Number(settings.Multiplier));
            parts.Add("\"ef\":0");
            Adjust a = d.Adjust;
            if (!a.IsNeutral) parts.Add("\"a\":[" + string.Join(",", new[] { a.Brightness, a.Contrast, a.Saturation, a.Gamma }.Select(Json.Number)) + "]");
            if (d.CustomCount.HasValue && d.CustomCount.Value != 0) parts.Add("\"cc\":" + Json.Number(d.CustomCount.Value));
            if (d.CustomPalette != null && d.CustomPalette.Length > 0) parts.Add("\"cp\":[" + string.Join(",", d.CustomPalette.Select(Json.Quote)) + "]");
            return "{" + string.Join(",", parts) + "}";
        }

        // "https://img.b0r3d.org/#s=…": opens these settings on the website
        public static string Link(Settings settings) => Site + "#s=" + ToBase64Url(LinkJson(settings));

        // A share link's settings by the website's names, as written (unchecked), or null if the
        // text isn't one. Takes a whole link, or what follows its "#".
        public static Dictionary<string, object> FromLink(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            text = text.Trim();
            int hash = text.IndexOf('#');
            string cleaned = (hash >= 0 ? text.Substring(hash + 1) : text).Trim();
            if (cleaned.Length == 0) return null;
            string b64 = cleaned.StartsWith("s=", StringComparison.Ordinal) ? cleaned.Substring(2) : cleaned.StartsWith("settings=", StringComparison.Ordinal) ? cleaned.Substring(9) : cleaned;
            object raw;
            try
            {
                raw = Json.Parse(FromBase64Url(b64));
            }
            catch (Exception e) when (e is FormatException || e is ArgumentException)
            {
                return null;
            }
            // an object (an array is one too, with none of the names)
            var fields = raw as Dictionary<string, object>;
            if (fields == null && !(raw is List<object>)) return null;
            fields = fields ?? new Dictionary<string, object>();

            var output = new Dictionary<string, object>();
            void Set(string name, string key)
            {
                if (fields.TryGetValue(key, out object v)) output[name] = v;
            }
            Set("profileId", "p");
            Set("fit", "f");
            Set("resolution", "r");
            Set("customWidth", "w");
            Set("detail", "dt");
            Set("background", "bg");
            Set("dither", "d");
            Set("pixelShape", "ps");
            Set("multiplier", "m");
            if (fields.TryGetValue("ef", out object ef) && (ef is double || ef is bool)) output["exportFx"] = ef is bool b ? b : (double)ef != 0 && !double.IsNaN((double)ef);
            if (fields.TryGetValue("a", out object adj) && adj is List<object> a)
            {
                var adjust = new Dictionary<string, object>();
                string[] names = { "brightness", "contrast", "saturation", "gamma" };
                for (int i = 0; i < 4 && i < a.Count; i++) adjust[names[i]] = a[i];
                output["adjust"] = adjust;
            }
            if (fields.TryGetValue("fx", out object fx) && fx is List<object> f && f.Count > 0) output["fx"] = new Dictionary<string, object> { { "screen", f[0] } };
            if (fields.TryGetValue("sg", out object sg) && sg is List<object> g && g.Count > 0) output["signal"] = new Dictionary<string, object> { { "mode", g[0] } };
            else output["signal"] = new Dictionary<string, object> { { "mode", "off" } };
            Set("customCount", "cc");
            Set("customPalette", "cp");
            return output;
        }

        // ---- look files ----

        static string Pretty(IEnumerable<string> items, string open, string close, string indent) =>
            open + "\r\n" + string.Join(",\r\n", items.Select(i => indent + "  " + i)) + "\r\n" + indent + close;

        // A look file: the settings by the website's own names (as it keeps them in the browser).
        public static string FileText(Settings settings)
        {
            DeresSettings d = settings.Deres;
            Adjust a = d.Adjust;
            var fields = new List<string>
            {
                "\"look\": " + Json.Quote(FileKind),
                "\"profileId\": " + Json.Quote(d.ProfileId),
                "\"fit\": " + Json.Quote(d.Fit),
                "\"resolution\": " + Json.Quote(d.Custom ? "custom" : "native"),
            };
            if (d.CustomWidth.HasValue) fields.Add("\"customWidth\": " + Json.Number(d.CustomWidth.Value));
            fields.Add("\"detail\": " + Json.Number(d.Detail));
            fields.Add("\"background\": " + Json.Quote(d.Background));
            fields.Add("\"adjust\": " + Pretty(new[]
            {
                "\"brightness\": " + Json.Number(a.Brightness), "\"contrast\": " + Json.Number(a.Contrast),
                "\"saturation\": " + Json.Number(a.Saturation), "\"gamma\": " + Json.Number(a.Gamma),
            }, "{", "}", "  "));
            fields.Add("\"dither\": " + Json.Quote(d.Dither));
            fields.Add("\"pixelShape\": " + Json.Quote(settings.TvPixels ? "tv" : "raw"));
            fields.Add("\"multiplier\": " + Json.Number(settings.Multiplier));
            if (d.CustomCount.HasValue) fields.Add("\"customCount\": " + Json.Number(d.CustomCount.Value));
            if (d.CustomPalette != null && d.CustomPalette.Length > 0) fields.Add("\"customPalette\": " + Pretty(d.CustomPalette.Select(Json.Quote), "[", "]", "  "));
            return Pretty(fields, "{", "}", "") + "\r\n";
        }

        // A look file's settings (by the website's names), or null if it isn't one.
        public static Dictionary<string, object> FromFile(string text)
        {
            try
            {
                return Json.Parse(text.TrimStart((char)0xfeff)) is Dictionary<string, object> fields && (fields.ContainsKey("look") || fields.ContainsKey("profileId")) ? fields : null;
            }
            catch (FormatException)
            {
                return null;
            }
        }

        // ---- checking ----

        static bool Finite(object v, out double n)
        {
            n = v is double d ? d : double.NaN;
            return v is double && !double.IsNaN(n) && !double.IsInfinity(n);
        }

        static double Num(object v, double lo, double hi, double fallback) => Finite(v, out double n) ? Math.Min(hi, Math.Max(lo, n)) : fallback;

        static string OneOf(object v, string[] options, string fallback) => v is string s && options.Contains(s) ? s : fallback;

        // What came in, checked field by field on top of `current` (the website's checkSettings):
        // the new settings, and a line for what it holds that comes with a later version (or null).
        public static Settings Apply(Dictionary<string, object> loaded, Settings current, out string later)
        {
            Settings s = current.Copy();
            DeresSettings d = s.Deres, b = current.Deres;
            var notes = new List<string>();

            string id = loaded.TryGetValue("profileId", out object p) ? p as string : null;
            d.ProfileId = id != null && Profiles.ById(id) != null ? id : b.ProfileId;
            if (id != null && Profiles.Later.TryGetValue(id, out string laterName)) notes.Add("the " + laterName);
            string ditherFallback = d.ProfileId == b.ProfileId ? b.Dither : Profiles.ById(d.ProfileId).Dither;

            if (loaded.TryGetValue("customPalette", out object cp) && cp is List<object> list)
            {
                string[] colours = list.OfType<string>().Where(PaletteFile.IsColour).Take(256).ToArray();
                d.CustomPalette = colours.Length > 0 ? colours : null;
            }
            d.Fit = OneOf(Get(loaded, "fit"), new[] { Fits.Fill, Fits.Fit, Fits.Stretch }, b.Fit);
            d.Custom = OneOf(Get(loaded, "resolution"), new[] { "native", "custom" }, b.Custom ? "custom" : "native") == "custom";
            // (settings and links from before the website's v0.9.4 have a block size instead: left behind)
            d.CustomWidth = Finite(Get(loaded, "customWidth"), out double w) ? (int)JsMath.Round(Math.Min(Geometry.CustomMax, Math.Max(1, w))) : b.CustomWidth;
            d.Detail = Num(Get(loaded, "detail"), 0, 1, b.Detail);
            d.Background = OneOf(Get(loaded, "background"), new[] { Backgrounds.White, Backgrounds.Black, Backgrounds.Transparent }, b.Background);
            var adjust = Get(loaded, "adjust") as Dictionary<string, object> ?? new Dictionary<string, object>();
            d.Adjust = new Adjust
            {
                Brightness = Num(Get(adjust, "brightness"), -0.5, 0.5, b.Adjust.Brightness),
                Contrast = Num(Get(adjust, "contrast"), 0, 2, b.Adjust.Contrast),
                Saturation = Num(Get(adjust, "saturation"), 0, 2, b.Adjust.Saturation),
                Gamma = Num(Get(adjust, "gamma"), 0.4, 2.5, b.Adjust.Gamma),
            };
            d.Dither = OneOf(Get(loaded, "dither"), Dither.All.Select(x => x.Id).ToArray(), ditherFallback);
            s.TvPixels = OneOf(Get(loaded, "pixelShape"), new[] { "raw", "tv" }, current.TvPixels ? "tv" : "raw") == "tv";
            s.Multiplier = Finite(Get(loaded, "multiplier"), out double m) && Export.Multipliers.Any(x => x == m) ? (int)m : current.Multiplier;
            d.CustomCount = Finite(Get(loaded, "customCount"), out double cc) ? (int)JsMath.Round(Math.Min(256, Math.Max(2, cc))) : b.CustomCount;

            // the screen effect and the Signal come with later versions: said, and left out
            if (Get(loaded, "fx") is Dictionary<string, object> fx && Get(fx, "screen") is string screen && screen != "off" && screens.Contains(screen))
                notes.Add("the " + screenNames[Array.IndexOf(screens, screen)] + " screen effect");
            if (Get(loaded, "signal") is Dictionary<string, object> signal && Get(signal, "mode") is string mode && mode != "off" && signals.Contains(mode))
                notes.Add("the Signal");
            later = notes.Count == 0 ? null : Words.List(notes);
            return s;
        }

        static object Get(Dictionary<string, object> d, string key) => d.TryGetValue(key, out object v) ? v : null;
    }
}
