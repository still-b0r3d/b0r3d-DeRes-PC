using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using B0r3dDeRes.Engine;

namespace B0r3dDeRes
{
    // Signal presets: whole Signals (the connection, the wear, the colour, sharpening and decoder,
    // every engine setting changed, and the tape's copies) under a name. A few come built in; your
    // own are kept in the data folder as "b0r3dDeRes presets.json". What the file holds that this
    // version can't read (an entry it doesn't know, a setting of a later version) is written back
    // as it was; a file that can't be read at all is never written over.
    static class SignalPresets
    {
        public const string FileName = "b0r3dDeRes presets.json";

        static SignalSettings S(string mode, double wear, int dubs = 0, params (string Key, object Value)[] engine)
        {
            var s = new SignalSettings { Mode = mode, Wear = wear, Dubs = dubs };
            foreach (var (key, value) in engine) s.Engine[key] = value;
            return s;
        }

        public static readonly (string Name, SignalSettings Signal)[] BuiltIn =
        {
            ("Fresh rental tape", S(SignalModes.VhsSp, 0.3)),
            ("Taped off the TV", S(SignalModes.VhsLp, 0.5, 0, ("vhs_chroma_vert_blend", true))),
            ("Copy of a copy", S(SignalModes.VhsLp, 0.6, 4, ("chroma_delay_vertical", 1.0))),
            ("Worn-out EP tape", S(SignalModes.VhsEp, 0.9, 0, ("tracking_noise_height", 30.0))),
            ("Bad tracking", S(SignalModes.VhsEp, 0.6, 0, ("tracking_noise_height", 64.0), ("tracking_noise_wave_intensity", 40.0), ("tracking_noise_snow_intensity", 0.4))),
            ("Camcorder", S(SignalModes.VhsSp, 0.4, 0, ("chroma_lowpass_in", 1.0), ("head_switching_horizontal_shift", 20.0))),
            ("Late-night broadcast", S(SignalModes.Rf, 0.9)),
            ("Clean cable box", S(SignalModes.Composite, 0.1)),
            ("Old TV, every other line", S(SignalModes.Composite, 0.5, 0, ("use_field", 0.0))),
        };

        // one of your presets: its name and Signal, and its entry in the file as it was read
        // (null: made or changed just now), which goes back into the file as it was
        public sealed class Preset
        {
            public string Name;
            public SignalSettings Signal;
            public object Entry;
        }

        // Your presets file as read: its entries in order, each a Preset or, for one this version
        // can't read, the entry itself (kept as it is); the file's other fields; and why it
        // couldn't be read at all (null: it could, or there's none yet). Nothing is written over a
        // file that couldn't be read.
        public sealed class Kept
        {
            public readonly List<object> Entries = new List<object>();
            public Dictionary<string, object> File;
            public string Problem;

            public IEnumerable<Preset> Presets => Entries.OfType<Preset>();
        }

        public static Kept Read()
        {
            var kept = new Kept();
            try
            {
                string path = Path.Combine(DataFolder.Path, FileName);
                if (!System.IO.File.Exists(path)) return kept;
                if (!(Json.Parse(System.IO.File.ReadAllText(path)) is Dictionary<string, object> file) || !(file.TryGetValue("presets", out object list) && list is List<object> entries))
                {
                    kept.Problem = "it isn't a presets file";
                    return kept;
                }
                kept.File = file;
                foreach (object entry in entries)
                    kept.Entries.Add(entry is Dictionary<string, object> p && p.TryGetValue("name", out object name) && name is string n && n.Trim().Length > 0 &&
                        p.TryGetValue("signal", out object signal) && signal is Dictionary<string, object>
                            ? new Preset { Name = n.Trim(), Signal = Signal.Check(signal, new SignalSettings()), Entry = entry }
                            : entry);
            }
            catch (FormatException x)
            {
                kept.Problem = "it isn't JSON (" + x.Message.TrimEnd('.') + ")";
            }
            catch (Exception x) when (x is IOException || x is UnauthorizedAccessException || x is System.Security.SecurityException)
            {
                kept.Problem = x.Message.TrimEnd('.');
            }
            return kept;
        }

        // a Signal as a preset file's object
        static string Text(SignalSettings s) =>
            "{\"mode\":" + Json.Quote(s.Mode) + ",\"wear\":" + Json.Number(s.Wear) + ",\"colour\":" + Json.Number(s.Colour) + ",\"sharpen\":" + Json.Number(s.Sharpen) +
            ",\"decoder\":" + Json.Quote(s.Decoder) + ",\"engine\":" + s.EngineText + ",\"dubs\":" + Json.Number(s.Dubs) + "}";

        // your presets kept, every entry not made or changed now as it was read (false: the file
        // couldn't be read, so it's left alone, or the data folder can't be written to)
        public static bool Keep(Kept kept)
        {
            if (kept.Problem != null) return false;
            IEnumerable<string> entries = kept.Entries.Select(e => e is Preset p
                ? p.Entry != null ? Json.Write(p.Entry) : "{\"name\":" + Json.Quote(p.Name) + ",\"signal\":" + Text(p.Signal) + "}"
                : Json.Write(e));
            string others = kept.File == null ? "" : string.Concat(kept.File.Where(kv => kv.Key != "presets").Select(kv => "," + Json.Quote(kv.Key) + ":" + Json.Write(kv.Value)));
            return DataFolder.TryWrite(FileName, "{\"presets\":[\r\n" + string.Join(",\r\n", entries) + "\r\n]" + others + "}\r\n");
        }
    }
}
