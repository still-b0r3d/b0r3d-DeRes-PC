using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using B0r3dDeRes.Exif;

namespace B0r3dDeRes.Tests
{
    // Stage 8: the EXIF tools against the website's own engine (website\exif\expected.json, made by
    // deres-pc-plan\fixtures\make-exif-files.mjs): everything the inspector reads, every scrub and
    // injection byte for byte, the injector's form and every typed value, ZIPs and batch names.
    static partial class TestMain
    {
        static string ExifFolder => Path.Combine(TestsFolder, "website", "exif");

        static Dictionary<string, object> exifExpected;

        // (by hand, DERES_EXIF_EXPECTED names a deeper set the fixture maker made, run once)
        static Dictionary<string, object> ExifExpected => exifExpected ?? (exifExpected = (Dictionary<string, object>)Json.Parse(File.ReadAllText(
            Environment.GetEnvironmentVariable("DERES_EXIF_EXPECTED") is string deep && deep.Length > 0 ? deep : Path.Combine(ExifFolder, "expected.json"), Encoding.UTF8)));

        static List<object> L(object o) => (List<object>)o;
        static Dictionary<string, object> D(object o) => (Dictionary<string, object>)o;
        static string S(object o) => o is string s ? s : null;

        // JSON.stringify of a string: lone surrogates and controls escaped, the rest as it is
        static string Q(string s)
        {
            var b = new StringBuilder("\"");
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                switch (c)
                {
                    case '"': b.Append("\\\""); break;
                    case '\\': b.Append("\\\\"); break;
                    case '\b': b.Append("\\b"); break;
                    case '\f': b.Append("\\f"); break;
                    case '\n': b.Append("\\n"); break;
                    case '\r': b.Append("\\r"); break;
                    case '\t': b.Append("\\t"); break;
                    default:
                        bool lone = char.IsHighSurrogate(c) ? !(i + 1 < s.Length && char.IsLowSurrogate(s[i + 1])) : char.IsLowSurrogate(c) && !(i > 0 && char.IsHighSurrogate(s[i - 1]));
                        if (c < 0x20 || lone) b.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else b.Append(c);
                        break;
                }
            }
            return b.Append('"').ToString();
        }

        static string V(string s) => s == null ? "-" : Q(s);
        static string V(double? x) => x.HasValue ? JsText.Num(x.Value) : "-";
        static string B(bool b) => b ? "true" : "false";

        static string Utf16Sha(string s)
        {
            var b = new byte[s.Length * 2];
            for (int i = 0; i < s.Length; i++)
            {
                b[i * 2] = (byte)s[i];
                b[i * 2 + 1] = (byte)(s[i] >> 8);
            }
            return Sha(b);
        }

        // the fixture maker's dumpMeta, line for line
        static string DumpMeta(ImageMetadata m)
        {
            var lines = new List<string>
            {
                "format " + m.Format,
                "make " + V(m.Make), "model " + V(m.Model), "lensModel " + V(m.LensModel), "dateTime " + V(m.DateTime), "software " + V(m.Software), "artist " + V(m.Artist),
                "copyright " + V(m.Copyright), "description " + V(m.Description), "exposureTime " + V(m.ExposureTime), "orientation " + V(m.Orientation), "iso " + V(m.Iso),
                "fNumber " + V(m.FNumber), "focalLength " + V(m.FocalLength),
            };
            GpsCoordinates g = m.Gps;
            lines.Add("gps " + (g != null ? string.Join(" ", JsText.Num(g.Latitude), JsText.Num(g.Longitude), V(g.Altitude), V(g.LatRef), V(g.LonRef)) : "-"));
            lines.Add("icc " + B(m.HasIccProfile) + " thumb " + B(m.HasThumbnail) + " " + (m.Thumbnail != null ? m.Thumbnail.MimeType + " " + Sha(m.Thumbnail.Data) : "-"));
            lines.Add("trailer " + m.TrailerBytes + " raw " + m.RawTagCount + " tags " + m.AllTags.Count);
            lines.Add("xmp " + (m.XmpText == null ? "-" : m.XmpText.Length + " " + Utf16Sha(m.XmpText)));
            lines.Add("text " + (m.TextChunks != null ? string.Join(" ", m.TextChunks.Entries.Select(kv => Q(kv.Key) + "=" + Q(kv.Value))) : "-"));
            lines.Add("exifText " + (m.ExifText != null ? string.Join(" ", m.ExifText.Select(Q)) : "-"));
            GifDetails gf = m.Gif;
            lines.Add("gif " + (gf != null ? string.Join(" ", gf.Version, gf.Width, gf.Height, gf.Frames, gf.LoopCount?.ToString() ?? "null", gf.Comments) : "-"));
            AiMarkerReport a = m.AiDetection;
            lines.Add("ai " + string.Join(" ", B(a.Detected), V(a.Kind), V(a.Tool), a.Confidence, Q(a.Summary), V(a.PromptOrParams)));
            lines.Add("evidence " + string.Join(" ", a.Evidence.Select(Q)));
            ContentCredentials c = a.Credentials;
            lines.Add("credentials " + (c != null ? string.Join(" ", Q(c.Where), V(c.Generator), V(c.AiClaim), c.Bytes) : "-"));
            lines.Add("narrative " + Q(ExifReader.Narrative(m)));
            if (g != null) lines.Add("dms " + Q(ExifReader.GpsDms(g)) + " dec " + Q(ExifReader.GpsDecimal(g)));
            foreach (ParsedExifTag t in m.AllTags)
            {
                string shown;
                try
                {
                    shown = Q(t.DisplayValue);
                }
                catch (ExifRangeException x)
                {
                    shown = "throws RangeError: " + x.Message;
                }
                lines.Add("tag " + string.Join(" ", Q(t.TagHex), Q(t.Name), t.Category, shown));
            }
            return string.Join("\n", lines);
        }

        static string ErrorText(Exception x) =>
            x is ExifRangeException ? "RangeError: " + x.Message : x is ScrubException || x is InjectException ? "Error: " + x.Message : throw new CheckException("an exception the website doesn't have: " + x);

        static string InspectDump(byte[] bytes)
        {
            try
            {
                return DumpMeta(ExifReader.Inspect(bytes));
            }
            catch (ExifRangeException x)
            {
                return "throws RangeError: " + x.Message;
            }
        }

        static string ScrubDump(byte[] bytes, ScrubOptions o)
        {
            try
            {
                ScrubResult r = Scrubber.Scrub(bytes, o);
                SelfCheckReport p = r.Report;
                return string.Join(" ", "ok", r.Format, r.Data.Length, Sha(r.Data), p.Format, p.OriginalSize, p.ScrubbedSize, p.BytesSaved, p.TagsRemovedCount, B(p.GpsRemoved), B(p.CameraRemoved),
                    B(p.TrailersRemoved), B(p.OrientationPreserved), B(p.IccPreserved), B(p.IsClean), "[" + string.Join(",", p.Leftovers.Select(Q)) + "]", Q(p.Summary));
            }
            catch (Exception x) when (x is ScrubException || x is ExifRangeException || x is InjectException)
            {
                return "error " + ErrorText(x);
            }
        }

        static string InjectDump(byte[] bytes, ExifEdits edits, InjectOptions options)
        {
            try
            {
                byte[] output = Injector.Inject(bytes, edits, options);
                return "ok " + output.Length + " " + Sha(output);
            }
            catch (Exception x) when (x is ScrubException || x is ExifRangeException || x is InjectException)
            {
                return "error " + ErrorText(x);
            }
        }

        static string FieldsJson(InjectorFields f) => "{" + string.Join(",", InjectorFields.Keys.Select(k => Q(k) + ":" + Q(f[k]))) + "}";

        static InjectorFields FieldsFrom(Dictionary<string, object> d)
        {
            var f = new InjectorFields();
            foreach (string k in InjectorFields.Keys) f[k] = S(d[k]);
            return f;
        }

        static readonly Dictionary<string, ScrubOptions> exifScrubs = new Dictionary<string, ScrubOptions>
        {
            ["all"] = new ScrubOptions { Mode = "all", PreserveOrientation = true },
            ["bare"] = new ScrubOptions { Mode = "all", PreserveOrientation = false },
            ["location"] = new ScrubOptions { Mode = "location_only", PreserveOrientation = true },
        };

        // the fixture maker's edits (JSON: {b64} for bytes, {repeat: [text, n]} for long text)
        static ExifEdits EditsFrom(Dictionary<string, object> j)
        {
            var e = new ExifEdits();
            Edit<string> Text(object v)
            {
                if (v is Json.NullValue) return Edit<string>.Remove;
                if (v is Dictionary<string, object> r && r.ContainsKey("repeat"))
                {
                    var rep = L(r["repeat"]);
                    return Edit<string>.To(string.Concat(Enumerable.Repeat(S(rep[0]), (int)(double)rep[1])));
                }
                return Edit<string>.To(S(v));
            }
            Edit<double> Number(object v) => v is Json.NullValue ? Edit<double>.Remove : Edit<double>.To((double)v);
            foreach (var kv in j)
            {
                object v = kv.Value;
                switch (kv.Key)
                {
                    case "make": e.Make = Text(v); break;
                    case "model": e.Model = Text(v); break;
                    case "lensModel": e.LensModel = Text(v); break;
                    case "artist": e.Artist = Text(v); break;
                    case "copyright": e.Copyright = Text(v); break;
                    case "software": e.Software = Text(v); break;
                    case "description": e.Description = Text(v); break;
                    case "lensMake": e.LensMake = Text(v); break;
                    case "dateTime": e.DateTime = Text(v); break;
                    case "orientation": e.Orientation = Number(v); break;
                    case "iso": e.Iso = Number(v); break;
                    case "fNumber": e.FNumber = Number(v); break;
                    case "focalLength": e.FocalLength = Number(v); break;
                    case "focalLength35": e.FocalLength35 = Number(v); break;
                    case "forgetCamera": e.ForgetCamera = v is bool b && b; break;
                    case "exposureTime":
                        if (v is Json.NullValue) e.ExposureTime = Edit<(double, double)>.Remove;
                        else if (v is string s) e.ExposureText = Edit<string>.To(s);
                        else if (v is double n) e.ExposureText = Edit<string>.To(JsText.Num(n));
                        else e.ExposureTime = Edit<(double, double)>.To(((double)L(v)[0], (double)L(v)[1]));
                        break;
                    case "gps":
                        if (v is Json.NullValue) e.Gps = Edit<(double, double, double?)>.Remove;
                        else
                        {
                            var g = D(v);
                            e.Gps = Edit<(double, double, double?)>.To(((double)g["latitude"], (double)g["longitude"], g.ContainsKey("altitude") ? (double?)(double)g["altitude"] : null));
                        }
                        break;
                    case "thumbnail":
                        e.Thumbnail = v is Json.NullValue ? Edit<byte[]>.Remove : Edit<byte[]>.To(Convert.FromBase64String(S(D(v)["b64"])));
                        break;
                    default: throw new CheckException("an edit the test doesn't know: " + kv.Key);
                }
            }
            return e;
        }

        // the fixture maker's random changes: the same xorshift, the same steps
        static byte[] Fuzzed(byte[] source, uint seed)
        {
            uint x = seed == 0 ? 1 : seed;
            uint R()
            {
                x ^= x << 13;
                x ^= x >> 17;
                x ^= x << 5;
                return x;
            }
            var b = (byte[])source.Clone();
            uint k = 1 + R() % 8;
            for (uint i = 0; i < k; i++)
            {
                uint at = R() % (uint)b.Length;
                b[at] = (byte)(R() & 255);
            }
            if (R() % 4 == 0)
            {
                var cut = new byte[R() % (uint)b.Length];
                Array.Copy(b, cut, cut.Length);
                return cut;
            }
            return b;
        }

        static byte[] ExifInput(Dictionary<string, object> c)
        {
            if (c.ContainsKey("file")) return File.ReadAllBytes(Path.Combine(ExifFolder, S(c["file"])));
            var fuzz = D(c["fuzz"]);
            byte[] source = File.ReadAllBytes(Path.Combine(ExifFolder, S(fuzz["base"])));
            if (fuzz.ContainsKey("seed")) return Fuzzed(source, (uint)(double)fuzz["seed"]);
            var cut = new byte[(int)(double)fuzz["cut"]];
            Array.Copy(source, cut, cut.Length);
            return cut;
        }

        // the first line where two dumps part
        static string FirstDifference(string mine, string theirs)
        {
            string[] a = mine.Split('\n'), b = theirs.Split('\n');
            for (int i = 0; i < Math.Max(a.Length, b.Length); i++)
            {
                string x = i < a.Length ? a[i] : "(nothing)", y = i < b.Length ? b[i] : "(nothing)";
                if (x != y) return "line " + (i + 1) + ": PC " + Short(x, 160) + " / website " + Short(y, 160);
            }
            return "(the same)";
        }

        static string Short(string s, int n) => s.Length <= n ? s : s.Substring(0, n) + "…";

        static void ExifInspectsLikeTheWebsite()
        {
            var problems = new List<string>();
            int n = 0;
            foreach (object o in L(ExifExpected["cases"]))
            {
                var c = D(o);
                byte[] bytes = ExifInput(c);
                string dump = InspectDump(bytes);
                n++;
                if (Sha(Encoding.UTF8.GetBytes(dump)) == S(c["dumpSha"])) continue;
                problems.Add(S(c["name"]) + ": " + (c.ContainsKey("dump") ? FirstDifference(dump, S(c["dump"])) : "differs (" + Short(dump.Replace("\n", " | "), 300) + ")"));
            }
            Check(problems.Count == 0, problems.Count + " of " + n + " differ: " + string.Join("; ", problems.Take(6)));
        }

        static void ExifScrubsLikeTheWebsite()
        {
            var problems = new List<string>();
            int n = 0;
            foreach (object o in L(ExifExpected["cases"]))
            {
                var c = D(o);
                byte[] bytes = ExifInput(c);
                foreach (var kv in D(c["scrub"]))
                {
                    string mine = ScrubDump(bytes, exifScrubs[kv.Key]);
                    n++;
                    if (mine != S(kv.Value)) problems.Add(S(c["name"]) + " (" + kv.Key + "): PC " + Short(mine, 220) + " / website " + Short(S(kv.Value), 220));
                }
            }
            Check(problems.Count == 0, problems.Count + " of " + n + " scrubs differ: " + string.Join("; ", problems.Take(5)));
        }

        static void ExifInjectsLikeTheWebsite()
        {
            var edits = L(ExifExpected["edits"]).Select(D).ToDictionary(e => S(e["id"]), e => e);
            var problems = new List<string>();
            int n = 0;
            foreach (object o in L(ExifExpected["cases"]))
            {
                var c = D(o);
                byte[] bytes = ExifInput(c);
                foreach (var kv in D(c["inject"]))
                {
                    var e = edits[kv.Key];
                    var options = new InjectOptions { RemoveXmp = e.ContainsKey("options") && D(e["options"]).ContainsKey("removeXmp") && (bool)D(e["options"])["removeXmp"] };
                    string mine = InjectDump(bytes, EditsFrom(D(e["edits"])), options);
                    n++;
                    if (mine != S(kv.Value)) problems.Add(S(c["name"]) + " (" + kv.Key + "): PC " + Short(mine, 160) + " / website " + Short(S(kv.Value), 160));
                }
                string fields;
                try
                {
                    fields = FieldsJson(Injector.ReadFields(bytes));
                }
                catch (ExifRangeException x)
                {
                    fields = "throws RangeError: " + x.Message;
                }
                n++;
                if (fields != S(c["fields"])) problems.Add(S(c["name"]) + " (the form): PC " + Short(fields, 200) + " / website " + Short(S(c["fields"]), 200));
            }
            Check(problems.Count == 0, problems.Count + " of " + n + " differ: " + string.Join("; ", problems.Take(5)));
        }

        // the edits the form makes, as the fixture maker writes them
        static string Canon(ExifEdits e)
        {
            var parts = new List<string>();
            void Text(string k, Edit<string> v)
            {
                if (v != null) parts.Add(k + "=" + (v.Removed ? "null" : Q(v.Value)));
            }
            void Num(string k, Edit<double> v)
            {
                if (v != null) parts.Add(k + "=" + (v.Removed ? "null" : JsText.Num(v.Value)));
            }
            Text("make", e.Make);
            Text("model", e.Model);
            Text("lensModel", e.LensModel);
            Text("artist", e.Artist);
            Text("copyright", e.Copyright);
            Text("software", e.Software);
            Text("description", e.Description);
            Num("orientation", e.Orientation);
            Text("dateTime", e.DateTime);
            Num("fNumber", e.FNumber);
            if (e.ExposureTime != null) parts.Add("exposureTime=" + (e.ExposureTime.Removed ? "null" : "[" + JsText.Num(e.ExposureTime.Value.Num) + "," + JsText.Num(e.ExposureTime.Value.Den) + "]"));
            Num("iso", e.Iso);
            Num("focalLength", e.FocalLength);
            if (e.Gps != null)
                parts.Add("gps=" + (e.Gps.Removed ? "null" : "{\"latitude\":" + JsText.Num(e.Gps.Value.Latitude) + ",\"longitude\":" + JsText.Num(e.Gps.Value.Longitude) +
                    (e.Gps.Value.Altitude.HasValue ? ",\"altitude\":" + JsText.Num(e.Gps.Value.Altitude.Value) : "") + "}"));
            return string.Join(";", parts);
        }

        static void ExifTypedValuesLikeTheWebsite()
        {
            var x = ExifExpected;
            var problems = new List<string>();
            void Same(string what, string mine, string theirs)
            {
                if (mine != theirs) problems.Add(what + ": PC " + Short(mine ?? "null", 120) + " / website " + Short(theirs ?? "null", 120));
            }
            foreach (object o in L(x["forms"]))
            {
                var f = D(o);
                ExifEdits edits = Injector.EditsFrom(FieldsFrom(D(f["now"])), FieldsFrom(D(f["loaded"])), out FieldError error);
                Same("form " + Short(Json.Write(f["now"]), 60), Canon(edits) + (error != null ? " | " + error.Field + ": " + error.Message : ""), S(f["result"]));
            }
            foreach (object o in L(x["dates"])) Same("date " + S(L(o)[0]), Injector.ParseDate(S(L(o)[0])), S(L(o)[1]));
            foreach (object o in L(x["normalize"]))
            {
                string mine;
                try
                {
                    mine = "ok " + Injector.NormalizeDate(S(L(o)[0]));
                }
                catch (InjectException e)
                {
                    mine = "error " + e.Message;
                }
                Same("normalize " + S(L(o)[0]), mine, S(L(o)[1]));
            }
            foreach (object o in L(x["shutters"]))
            {
                var r = Injector.ParseShutter(S(L(o)[0]));
                object w = L(o)[1];
                Same("shutter " + S(L(o)[0]), r.HasValue ? JsText.Num(r.Value.Num) + "/" + JsText.Num(r.Value.Den) : null, w is List<object> wl ? JsText.Num((double)wl[0]) + "/" + JsText.Num((double)wl[1]) : null);
            }
            foreach (object o in L(x["formatShutters"]))
            {
                var fr = L(L(o)[0]);
                Same("format shutter", Injector.FormatShutter(((double)fr[0], (double)fr[1])), S(L(o)[1]));
            }
            foreach (object o in L(x["xmpReadings"]))
            {
                var r = D(o);
                string xmp = S(r["xmp"]);
                GpsCoordinates g = ExifReader.XmpGps(xmp);
                Same("xmp gps " + Short(xmp, 40), g != null ? string.Join(" ", JsText.Num(g.Latitude), JsText.Num(g.Longitude), V(g.Altitude), V(g.LatRef), V(g.LonRef)) : null, S(r["gps"]));
                Same("xmp location " + Short(xmp, 40), B(ExifReader.XmpHasLocation(xmp)), B((bool)r["location"]));
                Same("xmp copies " + Short(xmp, 40), string.Join(",", Injector.XmpCopies(xmp)), string.Join(",", L(r["copies"]).Select(S)));
            }
            foreach (object o in L(x["sourceTypes"]))
            {
                var r = AiMarkers.DigitalSourceType(S(L(o)[0]));
                object w = L(o)[1];
                Same("source type " + S(L(o)[0]), r.HasValue ? r.Value.Kind + " " + r.Value.Code : null, w is Dictionary<string, object> wd ? S(wd["kind"]) + " " + S(wd["code"]) : null);
            }
            // JavaScript's upper and lower case: full mappings, the final sigma, lone surrogates
            foreach (object o in L(x["casing"]))
            {
                string t = S(L(o)[0]);
                Same("upper case of " + Q(t), Q(JsText.Upper(t)), Q(S(L(o)[1])));
                Same("lower case of " + Q(t), Q(JsText.Lower(t)), Q(S(L(o)[2])));
            }
            Check(problems.Count == 0, problems.Count + " differ: " + string.Join("; ", problems.Take(6)));
        }

        static void ExifZipsAndPresetsLikeTheWebsite()
        {
            var x = ExifExpected;
            var problems = new List<string>();
            foreach (object o in L(x["zips"]))
            {
                var z = D(o);
                var entries = L(z["files"]).Select(D).Select(f => (S(f["name"]), f.ContainsKey("file") ? File.ReadAllBytes(Path.Combine(ExifFolder, S(f["file"]))) : Encoding.UTF8.GetBytes(S(f["text"])))).ToList();
                byte[] zip = ZipWriter.Create(entries);
                if (zip.Length != (int)(double)z["length"] || Sha(zip) != S(z["sha"])) problems.Add("ZIP of " + entries.Count + " files: " + zip.Length + " bytes, not " + z["length"]);
                // the same written with a file's bytes handed over as it goes (as a video's are)
                var streamed = new MemoryStream();
                using (var w = new ZipWriter(streamed))
                    foreach (var e in entries) w.Add(e.Item1, e.Item2.Length, s => s.Write(e.Item2, 0, e.Item2.Length));
                if (Sha(streamed.ToArray()) != S(z["sha"])) problems.Add("the streamed ZIP of " + entries.Count + " files differs");
            }
            foreach (object o in L(x["batchNames"]))
            {
                var bn = D(o);
                var used = new HashSet<string>();
                var names = L(bn["names"]).Select(S).ToList();
                var files = L(bn["files"]).Select(S).ToList();
                var mine = new List<string>();
                for (int i = 0; i < names.Count; i++)
                {
                    byte[] bytes = File.ReadAllBytes(Path.Combine(ExifFolder, files[i]));
                    try
                    {
                        ScrubResult r = Scrubber.Scrub(bytes);
                        mine.Add(Scrubber.BatchName(names[i], r.Format, used));
                    }
                    catch (ScrubException e)
                    {
                        mine.Add("error " + e.Message);
                    }
                }
                string want = string.Join(" | ", L(bn["out"]).Select(S)), got = string.Join(" | ", mine);
                if (got != want) problems.Add("batch names: PC " + got + " / website " + want);
            }
            var presets = L(x["presets"]).Select(D).ToList();
            if (presets.Count != DevicePresets.All.Length) problems.Add(DevicePresets.All.Length + " presets, not " + presets.Count);
            foreach (var p in presets)
            {
                DevicePreset mine = DevicePresets.ById(S(p["id"]));
                if (mine == null)
                {
                    problems.Add("no preset " + S(p["id"]));
                    continue;
                }
                var fields = D(p["fields"]);
                string Want(string k) => S(fields[k]);
                bool same = mine.Label == S(p["label"]) && mine.Group == S(p["group"]) && DevicePresets.FieldKeys.All(k => mine[k] == Want(k)) &&
                    mine.LensMake == (p.ContainsKey("lensMake") ? S(p["lensMake"]) : null) && mine.Focal35 == (p.ContainsKey("focal35") ? (double?)(double)p["focal35"] : null);
                var extras = D(p["extras"]);
                var e = new ExifEdits();
                DevicePresets.AddExtras(mine, e);
                same &= (extras["lensMake"] is Json.NullValue ? e.LensMake.Removed : !e.LensMake.Removed && e.LensMake.Value == S(extras["lensMake"])) &&
                    (extras["focalLength35"] is Json.NullValue ? e.FocalLength35.Removed : !e.FocalLength35.Removed && e.FocalLength35.Value == (double)extras["focalLength35"]) && e.ForgetCamera == (bool)extras["forgetCamera"];
                if (!same) problems.Add("preset " + mine.Id + " differs");
            }
            if (!presets.Select(p => S(p["id"])).SequenceEqual(DevicePresets.All.Select(p => p.Id))) problems.Add("the presets aren't in the website's order");
            foreach (object o in L(x["formShows"]))
            {
                var f = L(o);
                var fields = new InjectorFields { ["make"] = S(f[1]), ["model"] = S(f[2]) };
                if (DevicePresets.FormShows(DevicePresets.ById(S(f[0])), fields) != (bool)f[3]) problems.Add("form shows " + S(f[0]) + " with " + S(f[1]) + "/" + S(f[2]));
            }
            foreach (object o in L(x["tiffs"]))
            {
                var t = D(o);
                string mine;
                try
                {
                    mine = "ok " + Sha(Injector.BuildTiff(EditsFrom(D(t["edits"]))));
                }
                catch (InjectException e)
                {
                    mine = "error " + e.Message;
                }
                if (mine != S(t["result"])) problems.Add("TIFF " + S(t["id"]) + ": PC " + mine + " / website " + S(t["result"]));
            }
            Check(problems.Count == 0, problems.Count + " differ: " + string.Join("; ", problems.Take(6)));
        }

        // ZIP64, read back by .NET's own ZIP reader: 70,000 files (a plain ZIP holds 65,535), and with
        // ZIP64's limits lowered (so as not to write 4 GB) files bigger than the limit, files starting
        // past it and streamed ones; a streamed file longer than it said is stopped as it comes, and
        // no directory is written over its half-written entry.
        static void ZipsPastTheLimits()
        {
            var many = new MemoryStream();
            using (var zip = new ZipWriter(many))
                for (int i = 0; i < 70000; i++) zip.Add("f" + i + ".txt", new[] { (byte)i });
            using (var read = new System.IO.Compression.ZipArchive(new MemoryStream(many.ToArray(), false), System.IO.Compression.ZipArchiveMode.Read))
            {
                Check(read.Entries.Count == 70000, "70,000 files: .NET reads " + read.Entries.Count);
                foreach (int i in new[] { 0, 65534, 65535, 65536, 69999 })
                    using (Stream s = read.GetEntry("f" + i + ".txt").Open())
                        Check(s.ReadByte() == (byte)i && s.ReadByte() == -1, "file " + i + " of 70,000 read back wrong");
            }
            // the limits lowered: a size or offset of 1,000 bytes or more, or 5 files or more, is ZIP64's
            var random = new Random(8);
            var files = new List<(string Name, byte[] Data, bool Streamed)>();
            for (int i = 0; i < 9; i++)
            {
                var data = new byte[i % 3 == 0 ? 2500 : 300 + i];
                random.NextBytes(data);
                files.Add(("név " + i + (i % 2 == 1 ? ".mp4" : ".jpg"), data, i % 2 == 1));
            }
            var small = new MemoryStream();
            using (var zip = new ZipWriter(small, 1000, 5))
                foreach (var f in files)
                    if (f.Streamed)
                        zip.Add(f.Name, f.Data.Length, s =>
                        {
                            s.Write(f.Data, 0, f.Data.Length / 2);
                            s.Write(f.Data, f.Data.Length / 2, f.Data.Length - f.Data.Length / 2);
                        });
                    else zip.Add(f.Name, f.Data);
            byte[] bytes = small.ToArray();
            using (var read = new System.IO.Compression.ZipArchive(new MemoryStream(bytes, false), System.IO.Compression.ZipArchiveMode.Read))
            {
                Check(read.Entries.Count == files.Count, "the limits lowered: .NET reads " + read.Entries.Count + " files");
                foreach (var f in files)
                {
                    System.IO.Compression.ZipArchiveEntry e = read.GetEntry(f.Name);
                    Check(e != null && e.Length == f.Data.Length, f.Name + ": missing, or the wrong size");
                    var back = new MemoryStream();
                    using (Stream s = e.Open()) s.CopyTo(back);
                    Check(back.ToArray().SequenceEqual(f.Data), f.Name + " read back wrong");
                }
            }
            Check(HasSignature(bytes, 0x06064b50) && HasSignature(bytes, 0x07064b50), "ZIP64's end record and its locator should be there");
            // a streamed file longer than it said
            var broken = new MemoryStream();
            bool stopped = false;
            try
            {
                using (var zip = new ZipWriter(broken))
                {
                    zip.Add("one.jpg", new byte[] { 1, 2, 3 });
                    zip.Add("two.mp4", 10, s => s.Write(new byte[20], 0, 20));
                }
            }
            catch (IOException)
            {
                stopped = true;
            }
            Check(stopped && broken.Length == 30 + 7 + 3 + 30 + 7 && !HasSignature(broken.ToArray(), 0x02014b50), "a file longer than it said: " + broken.Length + " bytes written, then a directory?");
        }

        static bool HasSignature(byte[] b, uint signature)
        {
            for (int i = 0; i + 4 <= b.Length; i++)
                if (BitConverter.ToUInt32(b, i) == signature) return true;
            return false;
        }
    }
}
