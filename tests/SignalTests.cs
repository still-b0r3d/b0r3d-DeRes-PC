using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using B0r3dDeRes.Engine;

namespace B0r3dDeRes.Tests
{
    // The Signal against the website's own (its ntsc-rs, built to WebAssembly, run in Node), and in the window.
    static partial class TestMain
    {
        // the pictures through the signal, kept for the PNG and wallpaper checks
        static readonly System.Collections.Concurrent.ConcurrentDictionary<int, SignalImage> signalled = new System.Collections.Concurrent.ConcurrentDictionary<int, SignalImage>();

        static SignalImage Signalled(int at) => signalled.GetOrAdd(at, i =>
        {
            var s = Website.Signals[i];
            return Signal.Run(Made(s.Render), Website.Renders[s.Render].Profile, s.Signal, s.Frame);
        });

        static void SignalsLikeTheWebsite()
        {
            var keep = new HashSet<int>(Website.SignalExports.Select(e => e.Signal).Concat(Website.SignalWallpapers.Select(w => w.Signal)));
            var found = new System.Collections.Concurrent.ConcurrentBag<(int At, string Why)>();
            // a frame at a time: each runs on every core
            for (int at = 0; at < Website.Signals.Length; at++)
            {
                var c = Website.Signals[at];
                RenderCase picture = Website.Renders[c.Render];
                SignalImage img = Signal.Run(Made(c.Render), picture.Profile, c.Signal, c.Frame);
                if (keep.Contains(at)) signalled[at] = img;
                var why = new List<string>();
                if (img.Width != c.Width || img.Height != c.Height) why.Add("size " + img.Width + "×" + img.Height);
                else if (Sha(img.Rgb) != c.Rgb) why.Add("colours");
                if ((img.Alpha == null ? "" : Sha(img.Alpha)) != c.Alpha) why.Add("which samples show");
                if (why.Count > 0) found.Add((at, picture + " " + c.Signal.Mode + " wear " + c.Signal.Wear + " colour " + c.Signal.Colour + " sharpen " + c.Signal.Sharpen + " " +
                    c.Signal.Decoder + " frame " + c.Frame + ": " + string.Join(", ", why)));
            }
            List<string> wrong = found.OrderBy(f => f.At).Select(f => f.Why).ToList();
            Check(wrong.Count == 0, wrong.Count + " of " + Website.Signals.Length + " differ: " + string.Join("; ", wrong.Take(6)));
        }

        static void SignalPngsLikeTheWebsite()
        {
            var wrong = new List<string>();
            foreach (var (at, e) in Website.SignalExports)
            {
                var c = Website.Signals[at];
                Rendered r = Made(e.Render);
                bool tv = e.Tv && !r.Geometry.SquarePixels;
                Engine.Png.Stored = true;
                byte[] png;
                try
                {
                    png = Export.PngBytes(r, e.Multiplier, tv, e.Fx, Signalled(at));
                }
                finally
                {
                    Engine.Png.Stored = false;
                }
                PngParts(png, out string chunks, out int width, out int height, out int depth, out int colourType, out byte[] palette, out byte[] rows, out byte[] trns);
                var why = new List<string>();
                if (chunks != e.Chunks) why.Add("chunks " + chunks);
                if (width != e.Width || height != e.Height) why.Add(width + "×" + height);
                if (depth != e.Depth || colourType != e.ColourType) why.Add("depth " + depth + " type " + colourType);
                if (rows.Length != e.RowBytes || Sha(rows) != e.Rows) why.Add("rows");
                RenderCase picture = Website.Renders[e.Render];
                string connection = Signal.Connection(c.Signal.Mode, Signal.Machine(picture.Profile, r.Width));
                string name = Export.FileName(Export.BaseName(picture.Input + ".jpg"), picture.Profile, r.Width, r.Height, e.Multiplier, tv, Export.WithFx(e.Fx, e.Multiplier), connection);
                if (name != e.Name) why.Add("named " + name);
                if (why.Count > 0) wrong.Add(e.Name + ": " + string.Join(", ", why));
            }
            foreach (var (at, e) in Website.SignalWallpapers)
            {
                Rendered r = Made(e.Render);
                byte[] png;
                using (var bytes = new MemoryStream())
                {
                    Engine.Png.Stored = true;
                    try
                    {
                        Export.WriteWallpaper(bytes, r, e.TargetW, e.TargetH, e.Tv && !r.Geometry.SquarePixels, e.Fx, Signalled(at));
                    }
                    finally
                    {
                        Engine.Png.Stored = false;
                    }
                    png = bytes.ToArray();
                }
                PngParts(png, out string chunks, out int width, out int height, out int depth, out int colourType, out _, out byte[] rows, out _);
                if (chunks != e.Chunks || width != e.Width || height != e.Height || depth != e.Depth || colourType != e.ColourType || rows.Length != e.RowBytes || Sha(rows) != e.Rows)
                    wrong.Add("the " + e.Wallpaper + " wallpaper of " + Website.Renders[e.Render] + " through " + Website.Signals[at].Signal.Mode);
            }
            signalled.Clear();
            Check(wrong.Count == 0, wrong.Count + " of " + (Website.SignalExports.Length + Website.SignalWallpapers.Length) + " differ: " + string.Join("; ", wrong.Take(6)));
        }

        // Every engine setting the PC lets you change, every field mode and dubbing, as the website's
        // own wasm makes them from the same settings file (the website's page sends only some of them).
        static void EveryEngineSettingLikeTheWebsite()
        {
            var found = new System.Collections.Concurrent.ConcurrentBag<(int At, string Why)>();
            for (int at = 0; at < Website.EngineCases.Length; at++)
            {
                var c = Website.EngineCases[at];
                SignalSettings s = c.Signal.Clone();
                s.Engine = Signal.CheckEngine(Json.Parse(c.Engine));
                s.Dubs = c.Dubs;
                RenderCase picture = Website.Renders[c.Render];
                SignalImage img = Signal.Run(Made(c.Render), picture.Profile, s, c.Frame);
                string why = img.Width != c.Width || img.Height != c.Height ? "size " + img.Width + "×" + img.Height : Sha(img.Rgb) != c.Rgb ? "colours" : null;
                if (why != null) found.Add((at, picture + " " + c.Signal.Mode + " frame " + c.Frame + " " + c.Engine + (c.Dubs > 0 ? " dubbed " + c.Dubs : "") + ": " + why));
            }
            List<string> wrong = found.OrderBy(f => f.At).Select(f => f.Why).ToList();
            Check(wrong.Count == 0, wrong.Count + " of " + Website.EngineCases.Length + " differ: " + string.Join("; ", wrong.Take(6)));
        }

        // ntsc-rs settings files read as the engine reads them (the legacy defaults for what's left
        // out, fractions cut to whole numbers, ranges held), and refused where it refuses them
        static void EngineFilesLikeTheWebsite()
        {
            foreach (var c in Website.EngineFileCases)
            {
                Rendered r = Made(c.Render);
                SignalMachine machine = Signal.Machine(Website.Renders[c.Render].Profile, r.Width);
                byte[] input = Signal.Input(r, machine, out int width);
                byte[] rgb = Ntsc.Frame(input, width, r.Height, 1, NtscSettings.FromFile(c.Json), 1f, machine.LineLocked, 16);
                Check(Sha(rgb) == c.Rgb, "the settings file " + c.Json + " runs differently");
            }
            // the defaults, written as the engine writes them, letter for letter, and read back
            NtscSettings defaults = NtscSettings.FromValues(NtscSettings.DefaultValues());
            Check(defaults.ToFile() == Website.EngineDefaults, "the defaults are written as " + defaults.ToFile());
            Check(NtscSettings.FromFile(Website.EngineDefaults).SameAs(defaults) && EngineFileSettings(Website.EngineDefaults).SameAs(defaults), "the defaults read back as themselves");
            foreach (string bad in new[] { "{}", "{\"version\":2}", "[1]", "{\"version\":1,\"use_field\":9}", "{\"version\":1,\"ringing\":1}", "{\"version\":1,\"luma_smear\":true}", "not json" })
            {
                bool refused = false;
                try
                {
                    NtscSettings.FromFile(bad);
                }
                catch (FormatException)
                {
                    refused = true;
                }
                Check(refused, "the engine refuses " + bad + ", so the PC must too");
            }
            // an ntscQT preset (the engine converts those; DeRes says what it is instead)
            try
            {
                NtscSettings.FromFile("{\"_composite_preemphasis\":1}");
                Check(false, "an ntscQT preset is said to be one");
            }
            catch (FormatException x)
            {
                Check(x.Message.Contains("ntscQT"), "an ntscQT preset is said to be one: " + x.Message);
            }
            // a float written as Rust writes it: the fewest digits, never an exponent
            Check(NtscSettings.F32Text(1e-7f) == "0.0000001" && NtscSettings.F32Text(3e10f) == "30000000000" && NtscSettings.F32Text(0.1f) == "0.1" &&
                NtscSettings.F32Text(-0f) == "-0" && NtscSettings.F32Text(16777217f) == "16777216" && NtscSettings.F32Text(1.5e-45f) == "0." + new string('0', 44) + "1",
                "floats are written as Rust writes them");
        }

        static void ArtifactColoursLikeTheWebsite()
        {
            foreach (var a in Website.Artifacts)
            {
                string[] colours = Signal.ArtifactPalette("cga-composite", a.Signal);
                Check(colours != null && string.Join(",", colours) == a.Colours, "CGA composite through " + a.Signal.Mode + " (" + a.Signal.Decoder + ", sharpen " + a.Signal.Sharpen +
                    ", colour " + a.Signal.Colour + "): " + (colours == null ? "none" : string.Join(",", colours)) + ", not " + a.Colours);
            }
            Check(Signal.ArtifactPalette("cga-1", new SignalSettings { Mode = SignalModes.Composite }) == null && Signal.ArtifactPalette("cga-composite", new SignalSettings()) == null,
                "only CGA composite, with the signal on, has colours from the signal");
        }

        // A number of a settings file as the engine reads it: the decimal text straight to the
        // nearest float (halves to even), worked out here with whole numbers.
        static float FloatOf(string text)
        {
            Match m = Regex.Match(text, @"^(-?)(\d+)(?:\.(\d+))?(?:[eE]([+-]?\d+))?$");
            if (!m.Success) throw new FormatException(text);
            bool negative = m.Groups[1].Value == "-";
            string fraction = m.Groups[3].Value;
            BigInteger digits = BigInteger.Parse(m.Groups[2].Value + fraction, CultureInfo.InvariantCulture);
            int exp10 = (m.Groups[4].Success ? int.Parse(m.Groups[4].Value, CultureInfo.InvariantCulture) : 0) - fraction.Length;
            if (digits.IsZero) return negative ? -0f : 0f;
            BigInteger num = exp10 >= 0 ? digits * BigInteger.Pow(10, exp10) : digits, den = exp10 >= 0 ? BigInteger.One : BigInteger.Pow(10, -exp10);
            // num / den = q × 2^e, 2^23 <= q < 2^24 (or 2^-149 steps below the normal floats)
            int e = (int)Math.Floor(BigInteger.Log(num, 2) - BigInteger.Log(den, 2)) - 23;
            BigInteger q, rem, d2;
            while (true)
            {
                if (e < -149) e = -149;
                BigInteger n2 = e < 0 ? num << -e : num;
                d2 = e > 0 ? den << e : den;
                q = BigInteger.DivRem(n2, d2, out rem);
                if (q >= (BigInteger.One << 24)) e++;
                else if (q < (BigInteger.One << 23) && e > -149) e--;
                else break;
            }
            int half = (rem * 2).CompareTo(d2);
            if (half > 0 || half == 0 && !q.IsEven) q += 1;
            double value = (double)q * Math.Pow(2, e);
            return (float)(negative ? -value : value);
        }

        // A settings file as the engine reads it: every setting it has, each number straight from its
        // text to the nearest float (FloatOf), over the engine's legacy defaults.
        internal static NtscSettings EngineFileSettings(string json)
        {
            var values = (Dictionary<string, object>)Json.Parse(json);
            foreach (string key in values.Keys.ToList())
                if (values[key] is double && NtscSettings.Find(key) is NtscSetting s && (s.Kind == SettingKind.Float || s.Kind == SettingKind.Percent))
                    values[key] = (double)FloatOf(Regex.Match(json, "\"" + key + "\":([^,}]+)").Groups[1].Value);
            return NtscSettings.FromValues(values);
        }

        static void EngineSettingsLikeTheWebsite()
        {
            foreach (var f in Website.EngineFiles)
            {
                NtscSettings s = Signal.EngineSettings(f.Connection, new SignalSettings { Mode = f.Connection, Wear = f.Wear, Sharpen = f.Sharpen }, f.Lines);
                List<string> wrong = s.Differences(EngineFileSettings(f.Json));
                Check(wrong.Count == 0, f.Connection + " wear " + f.Wear + " sharpen " + f.Sharpen + " " + f.Lines + " lines: " + string.Join(", ", wrong));
            }
            // the comb filter, and a double exactly halfway between two floats (its digits decide)
            Check(Signal.EngineSettings(SignalModes.Rf, new SignalSettings { Decoder = SignalModes.Comb }, 240).ChromaDemodulation == ChromaDemodulation.TwoLineComb, "the comb filter");
            double between = (1.0 + Math.Pow(2, -24)) * 0.25;   // halfway between two floats, 0.25 and the next
            Check(Signal.JsonFloat(between) == FloatOf(JsNumber.Text(between)) && Signal.JsonFloat(0.1) == 0.1f && Signal.JsonFloat(1 / 3.0) == FloatOf(JsNumber.Text(1 / 3.0)),
                "a number's float, as the engine reads JavaScript's text of it");
        }

        // ---- in the window ----

        static RadioButton SignalButton(MainWindow window, string mode) => window.SignalButtons.Children.OfType<RadioButton>().First(b => (string)b.Tag == mode);

        // The Signal section's controls and words, and the preview through the signal: the
        // engine's own frame, at the zoom shown (with the screen effect over it when one's on).
        // CGA composite's colours come from the signal, made again when it changes.
        static void SignalSectionAndPreview()
        {
            MainWindow window = NewMain();
            WaitForPicture(window);
            window.OpenSample("cat-nap");
            WaitForPicture(window);
            Pick(window, "nes");
            Check(window.Current.Signal.Mode == SignalModes.Off && window.SignalPanel.Visibility != Visibility.Visible && !window.View.HasScreen, "the signal should start off");
            Check(window.SignalButtons.Children.OfType<RadioButton>().Select(b => (string)b.Content).SequenceEqual(new[] { "Off", "Machine's own", "RF", "Composite", "VHS SP", "VHS LP", "VHS EP" }),
                "the connections, in the website's order and words");
            Check(window.SignalNote.Text.StartsWith("Off. The real thing: the picture goes out as an analog TV signal", StringComparison.Ordinal), "the note while it's off: " + window.SignalNote.Text);
            int renders = window.Renders;
            PickSignal(window, SignalModes.Auto);
            Check(window.SignalNote.Text == "The NES over RF (aerial socket)." && window.SignalPanel.Visibility == Visibility.Visible, "the NES's own connection: " + window.SignalNote.Text);
            Rendered r = window.Shown;
            void SamePreview(string what, FxSettings fx = null)
            {
                int scale = MainWindow.ScreenScale(r.Width, r.Height, 1, window.View.Zoom);
                SignalImage sig = Signal.Run(r, r.Profile.Id, window.Current.Signal, 0);
                byte[] want = Signal.Renderer(sig, r.Width * scale, r.Height * scale, fx, 0, r.Paper).Rgba();
                Check(window.View.HasScreen && window.View.ScreenWidth == r.Width * scale && window.View.ScreenPixels.SequenceEqual(want), what + ": the preview isn't the engine's signal at the zoom shown");
            }
            SamePreview("RF");
            foreach (string mode in new[] { SignalModes.Composite, SignalModes.VhsSp, SignalModes.VhsEp })
            {
                PickSignal(window, mode);
                SamePreview(mode);
            }
            Check(window.Renders == renders, "the signal made the picture again (only the preview's signal should be)");

            // the sliders and the decoder, in the website's steps; then a screen over the signal
            window.SignalAdvanced.IsExpanded = true;
            window.WearSlider.Value = 85;
            window.SignalColourSlider.Value = 125;
            window.SharpenSlider.Value = 60;
            window.DecoderComb.IsChecked = true;
            WaitForScreen(window);
            SignalSettings s = window.Current.Signal;
            Check(s.Wear == 0.85 && s.Colour == 1.25 && s.Sharpen == 0.6 && s.Decoder == SignalModes.Comb && window.WearValue.Text == "85%" && window.SignalColourValue.Text == "125%" &&
                window.SharpenValue.Text == "60%", "the sliders and the decoder: " + s.Wear + "/" + s.Colour + "/" + s.Sharpen + "/" + s.Decoder);
            SamePreview("worn, coloured, sharpened, combed");
            PickScreen(window, Screens.Crt);
            SamePreview("with the CRT", window.Current.Fx);
            Check(window.SizeLine.Text.EndsWith("· full colour, through a VHS tape (EP)", StringComparison.Ordinal), "the size line: " + window.SizeLine.Text);
            PickScreen(window, Screens.Off);
            Check(window.Renders == renders, "the signal's controls made the picture again");

            // a machine with 228 colour cycles a line keeps its fringes still
            Pick(window, "genesis");
            PickSignal(window, SignalModes.Composite);
            Check(window.SignalNote.Text == "The Sega Genesis over composite video. Its colour fringes stand still, as they did on the real thing.", "the Genesis's note: " + window.SignalNote.Text);

            // CGA composite: dithered to the colours the signal makes, made again as they change
            Pick(window, "cga-composite");
            PickSignal(window, SignalModes.Auto);
            Check(window.SignalNote.Text.EndsWith("Its colours are made by the signal itself: each one is a pattern of black and white dots.", StringComparison.Ordinal), "CGA composite's note: " + window.SignalNote.Text);
            string Colours(Rendered pic) => string.Join(",", Enumerable.Range(0, pic.Palette.Length / 3).Select(i => "#" + pic.Palette[i * 3].ToString("x2") + pic.Palette[i * 3 + 1].ToString("x2") + pic.Palette[i * 3 + 2].ToString("x2")));
            Check(Colours(window.Shown) == string.Join(",", Signal.ArtifactPalette("cga-composite", window.Current.Signal)), "CGA composite's colours should be the signal's");
            renders = window.Renders;
            window.SharpenSlider.Value = 0;
            WaitForPicture(window);
            WaitForScreen(window);
            Check(window.Renders > renders && Colours(window.Shown) == string.Join(",", Signal.ArtifactPalette("cga-composite", window.Current.Signal)), "a new sharpening should make CGA composite's colours again");
            // Wear changes no colour: only the signal is made again, as on the website
            renders = window.Renders;
            window.WearSlider.Value = 20;
            WaitForPicture(window);
            WaitForScreen(window);
            r = window.Shown;
            Check(window.Renders == renders, "Wear made CGA composite's picture again");
            SamePreview("CGA composite, less worn");
            PickSignal(window, SignalModes.Off);
            Check(Hex(window.Shown.Palette) == Hex(Profiles.PaletteBytes(Profiles.ById("cga-composite"))) && !window.View.HasScreen, "with the signal off, CGA composite's own table");
        }

        // PNGs, copies, wallpapers and batches keep the signal, full colour, named for the connection.
        static void SignalInPngsAndWallpapers()
        {
            MainWindow window = NewMain();
            WaitForPicture(window);
            window.OpenSample("cat-nap");
            WaitForPicture(window);
            Pick(window, "nes");
            PickSignal(window, SignalModes.Rf);
            Rendered r = window.Shown;
            SignalImage sig = Signal.Run(r, "nes", window.Current.Signal, 0);
            string folder = Path.Combine(Work, "signal");
            Fresh(folder);

            MainWindow.Shared share = window.ToShare(r);
            Check(share.Name == "cat-nap-nes-256x240-4x-rf.png", "named " + share.Name);
            share.Make(r);
            byte[] want = Export.PngBytes(r, 4, false, null, sig);
            Check(share.Png.SequenceEqual(want) && share.Picture.PixelWidth == 1024, "the copy should be the engine's PNG through the signal");
            string path = Path.Combine(folder, share.Name);
            Check(Await(window.SavePng(path, r, 4, false, null, window.Current.Signal)) == null && File.ReadAllBytes(path).SequenceEqual(want), "the saved PNG isn't the engine's through the signal");
            PngParts(want, out _, out int width, out int height, out _, out int colourType, out _, out _);
            Check(width == 1024 && height == 960 && colourType == 2, "a PNG through the signal is full colour at the Size: " + width + "×" + height + " type " + colourType);
            window.ShapeTv.IsChecked = true;
            Check(window.ToShare(r).Name == "cat-nap-nes-256x240-4x-tv-rf.png", "as on a TV: " + window.ToShare(r).Name);
            window.ShapeRaw.IsChecked = true;

            Wallpaper paper = window.WallpaperChoices.First(w => w.Id == "win95");
            string file = Path.Combine(folder, window.WallpaperName(r, paper));
            Check(Await(window.SaveWallpaper(file, r, paper)) == null, "the wallpaper wasn't saved");
            using (var bytes = new MemoryStream())
            {
                Export.WriteWallpaper(bytes, r, paper.Width, paper.Height, false, null, sig);
                Check(File.ReadAllBytes(file).SequenceEqual(bytes.ToArray()), "the wallpaper isn't the engine's through the signal");
            }

            // a batch: each picture through the signal, named for it
            string pictures = Path.Combine(folder, "pictures");
            Fresh(pictures);
            File.WriteAllBytes(Path.Combine(pictures, "a.png"), Encode(new System.Windows.Media.Imaging.PngBitmapEncoder(), Quarters(40, 30)));
            BatchWindow batch = window.OpenBatch(new[] { pictures });
            Check(batch.LookLine.Text.Contains("through RF (aerial socket)"), "the batch's look line: " + batch.LookLine.Text);
            batch.Make(Clash.Number);
            Pump(() => batch.Running.IsCompleted, 30000);
            string made = Path.Combine(pictures, "DeRes", "a-nes-256x240-4x-rf.png");
            var engine = new Deres();
            engine.SetSource(Pictures.Load(Path.Combine(pictures, "a.png")).Source);
            Rendered a = engine.Render(window.Current.Deres);
            Check(File.Exists(made) && File.ReadAllBytes(made).SequenceEqual(Export.PngBytes(a, 4, false, null, Signal.Run(a, "nes", window.Current.Signal, 0))),
                "the batch's file isn't the engine's through the signal: " + string.Join(", ", Directory.GetFiles(Path.Combine(pictures, "DeRes")).Select(Path.GetFileName)));
            batch.Close();
        }
    }
}
