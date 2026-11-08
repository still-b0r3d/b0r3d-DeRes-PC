using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using B0r3dDeRes.Engine;

namespace B0r3dDeRes.Tests
{
    // A look's settings, by the website's names, as the test files hold them.
    sealed class LookCase
    {
        public readonly string Profile, Fit, Background, Dither, CustomPalette;
        public readonly bool Custom, Tv;
        public readonly int CustomWidth, Multiplier, CustomCount;
        public readonly double Detail, Brightness, Contrast, Saturation, Gamma;

        public LookCase(string profile, string fit, bool custom, int customWidth, double detail, string background, string dither, bool tv, int multiplier,
            double brightness, double contrast, double saturation, double gamma, int customCount, string customPalette)
        {
            Profile = profile; Fit = fit; Custom = custom; CustomWidth = customWidth; Detail = detail; Background = background; Dither = dither; Tv = tv;
            Multiplier = multiplier; Brightness = brightness; Contrast = contrast; Saturation = saturation; Gamma = gamma; CustomCount = customCount; CustomPalette = customPalette;
        }

        // the screen effect and the export's box; the signal, and its loop's frames and speed
        public FxSettings Fx = new FxSettings();
        public bool ExportFx;
        public SignalSettings Signal = new SignalSettings();
        public int LoopFrames = 16, LoopFps = 15;

        public Settings Settings()
        {
            var s = B0r3dDeRes.Settings.Defaults();
            s.Fx = Fx.Clone();
            s.ExportFx = ExportFx;
            s.Signal = Signal.Clone();
            s.LoopFrames = LoopFrames;
            s.LoopFps = LoopFps;
            DeresSettings d = s.Deres;
            d.ProfileId = Profile;
            d.Fit = Fit;
            d.Custom = Custom;
            d.CustomWidth = CustomWidth >= 0 ? CustomWidth : (int?)null;
            d.Detail = Detail;
            d.Background = Background;
            d.Dither = Dither;
            d.Adjust = new Adjust { Brightness = Brightness, Contrast = Contrast, Saturation = Saturation, Gamma = Gamma };
            d.CustomCount = CustomCount >= 0 ? CustomCount : (int?)null;
            d.CustomPalette = CustomPalette?.Split(',');
            s.TvPixels = Tv;
            s.Multiplier = Multiplier;
            return s;
        }

        // what differs between these and `s`, or null
        public string Against(Settings s)
        {
            DeresSettings d = s.Deres;
            var wrong = new List<string>();
            void Same(bool same, string what, object got)
            {
                if (!same) wrong.Add(what + " " + got);
            }
            Same(d.ProfileId == Profile, "machine", d.ProfileId);
            Same(d.Fit == Fit, "framing", d.Fit);
            Same(d.Custom == Custom, "resolution", d.Custom ? "custom" : "native");
            Same((d.CustomWidth ?? -1) == CustomWidth, "width", d.CustomWidth);
            Same(d.Detail == Detail, "detail", d.Detail.ToString("R"));
            Same(d.Background == Background, "background", d.Background);
            Same(d.Dither == Dither, "dither", d.Dither);
            Same(s.TvPixels == Tv, "pixel shape", s.TvPixels ? "tv" : "raw");
            Same(s.Multiplier == Multiplier, "size", s.Multiplier);
            Same(d.Adjust.Brightness == Brightness && d.Adjust.Contrast == Contrast && d.Adjust.Saturation == Saturation && d.Adjust.Gamma == Gamma, "adjust",
                d.Adjust.Brightness.ToString("R") + "/" + d.Adjust.Contrast.ToString("R") + "/" + d.Adjust.Saturation.ToString("R") + "/" + d.Adjust.Gamma.ToString("R"));
            Same((d.CustomCount ?? -1) == CustomCount, "colours", d.CustomCount);
            Same((d.CustomPalette == null ? null : string.Join(",", d.CustomPalette)) == CustomPalette, "palette", d.CustomPalette == null ? "none" : d.CustomPalette.Length + " colours");
            FxSettings f = s.Fx;
            Same(f.SameAs(Fx), "screen", f.Screen + "/" + f.Mask + "/" + f.Scanlines.ToString("R") + "/" + f.Curve.ToString("R") + "/" + f.Bloom.ToString("R") + "/" +
                f.Grid.ToString("R") + "/" + f.Ghost.ToString("R") + "/" + f.VhsTracking.ToString("R") + "/" + f.VhsOsd);
            Same(s.ExportFx == ExportFx, "screen in the export", s.ExportFx);
            SignalSettings g = s.Signal;
            Same(g.SameAs(Signal), "signal", g.Mode + "/" + g.Wear.ToString("R") + "/" + g.Colour.ToString("R") + "/" + g.Sharpen.ToString("R") + "/" + g.Decoder);
            Same(s.LoopFrames == LoopFrames && s.LoopFps == LoopFps, "loop", s.LoopFrames + " frames at " + s.LoopFps);
            return wrong.Count == 0 ? null : string.Join(", ", wrong);
        }
    }

    // Share links, look files, palette files, the grabber and JavaScript's numbers, against the
    // website's own.
    static partial class TestMain
    {
        static void GrabsLikeTheWebsite()
        {
            foreach (var g in Website.Grabs)
            {
                byte[] colours = Quantize.Grab(Input(g.Input), g.Count);
                Check(Hex(colours) == g.Colours, "grabbing " + g.Count + " colours from " + g.Input + " gives " + colours.Length / 3 + " colours, not the website's");
            }
        }

        static void LinksLikeTheWebsite()
        {
            foreach (var l in Website.Links)
            {
                string link = Looks.Link(l.Settings.Settings());
                Check(link == Looks.Site + l.Hash, "the link is " + Short(link) + ", the website's " + Short(l.Hash) + " (" + Looks.LinkJson(l.Settings.Settings()) + ")");
            }
        }

        static void LinksReadLikeTheWebsite()
        {
            foreach (var d in Website.Decodes)
            {
                Dictionary<string, object> loaded = Looks.FromLink(d.Hash);
                if (d.Settings == null)
                {
                    Check(loaded == null, Short(d.Hash) + " should be refused, as the website refuses it");
                    continue;
                }
                Check(loaded != null, Short(d.Hash) + " should open, as on the website");
                Settings s = Looks.Apply(loaded, Settings.Defaults());
                string wrong = d.Settings.Against(s);
                Check(wrong == null, Short(d.Hash) + " reads differently: " + wrong);
                Check(s.Fx.Screen == d.Screen, Short(d.Hash) + " has the " + s.Fx.Screen + " screen, not " + d.Screen);
                Check(s.Signal.Mode == d.Signal, Short(d.Hash) + " has the signal " + s.Signal.Mode + ", not " + d.Signal);
            }
            // the website's own whole link, pasted
            Check(Looks.FromLink("https://img.b0r3d.org/" + Website.Links[1].Hash) != null && Looks.FromLink("  " + Looks.Site + Website.Links[1].Hash + "\r\n") != null, "a whole link should read");
            Check(Looks.FromLink("hello") == null && Looks.FromLink("") == null && Looks.FromLink("https://img.b0r3d.org/") == null, "text that isn't a link should be refused");
        }

        static void NumbersLikeJavaScript()
        {
            byte[] bytes = Convert.FromBase64String(Website.Numbers);
            Check(bytes.Length / 8 == Website.NumberTexts.Length && Website.NumberTexts.Length > 3000, "the test numbers don't line up");
            var wrong = new List<string>();
            for (int i = 0; i < Website.NumberTexts.Length; i++)
            {
                double x = BitConverter.ToDouble(bytes, i * 8);
                string text = JsNumber.Text(x);
                if (text != Website.NumberTexts[i]) wrong.Add(x.ToString("R") + " written " + text + ", JavaScript writes " + Website.NumberTexts[i]);
                // and read back (but -0, which JavaScript writes "0")
                if (BitConverter.DoubleToInt64Bits(x) == long.MinValue) continue;
                double back = (double)Json.Parse(Website.NumberTexts[i]);
                if (BitConverter.DoubleToInt64Bits(back) != BitConverter.DoubleToInt64Bits(x)) wrong.Add(Website.NumberTexts[i] + " read as " + back.ToString("R"));
            }
            byte[] parsed = Convert.FromBase64String(Website.Parsed);
            for (int i = 0; i < Website.ParseTexts.Length; i++)
            {
                double want = BitConverter.ToDouble(parsed, i * 8), got = (double)Json.Parse(Website.ParseTexts[i]);
                if (BitConverter.DoubleToInt64Bits(want) != BitConverter.DoubleToInt64Bits(got)) wrong.Add(Website.ParseTexts[i] + " read as " + got.ToString("R") + ", JSON.parse reads " + want.ToString("R"));
            }
            Check(wrong.Count == 0, wrong.Count + " differ: " + string.Join("; ", wrong.Take(6)));
            // what JSON.parse refuses
            foreach (string bad in new[] { "01", "1.", ".5", "+1", "1e", "-", "[1,]", "{\"a\":1,}", "{'a':1}", "\"\t\"", "nul", "1 2" })
            {
                bool refused = false;
                try
                {
                    Json.Parse(bad);
                }
                catch (FormatException)
                {
                    refused = true;
                }
                Check(refused, bad + " should be refused, as JSON.parse refuses it");
            }
        }

        static void PaletteFilesLikeTheWebsite()
        {
            foreach (var p in Website.PaletteTexts)
            {
                // the website reads broken colour text in now and then (and then can't draw it): the PC leaves those out
                string want = string.Join(",", p.Colours.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Where(PaletteFile.IsColour));
                string got = string.Join(",", PaletteFile.ParseText(p.Text));
                Check(got == want, "\"" + Short(p.Text.Replace("\n", "|")) + "\" reads as " + Short(got) + ", the website's " + Short(want));
            }
            Check(PaletteFile.Hex(new[] { "#ABCDEF" }.Concat(Website.Pal4).ToList()) == Website.PaletteHex, "the .hex differs: " + Short(PaletteFile.Hex(Website.Pal4)));
            Check(PaletteFile.Jasc(Website.Pal4) == Website.PaletteJasc, "the .pal differs");
            Check(PaletteFile.Gpl(Website.Pal4) == Website.PaletteGpl, "the .gpl differs");
            foreach (var (colours, want) in new[] { (Website.Pal4, Website.PalettePng4), (Website.Pal256, Website.PalettePng256) })
            {
                PngParts(PaletteFile.Png(colours), out string chunks, out int w, out int h, out int depth, out int type, out byte[] palette, out byte[] rows);
                Check(chunks == want.Chunks && w == want.Width && h == want.Height && depth == want.Depth && type == want.ColourType && Hex(palette) == want.Palette &&
                    Sha(rows) == want.Rows, "the palette PNG of " + colours.Length + " colours differs");
            }
            // and the colours read back from what's written
            Check(PaletteFile.ParseText(PaletteFile.Jasc(Website.Pal256)).SequenceEqual(Website.Pal256) && PaletteFile.ParseText(PaletteFile.Gpl(Website.Pal4)).SequenceEqual(Website.Pal4) &&
                PaletteFile.ParseText(PaletteFile.Hex(Website.Pal4)).SequenceEqual(Website.Pal4), "a palette written doesn't read back");
            string file = Path.Combine(Work, "palette.png");
            File.WriteAllBytes(file, PaletteFile.Png(Website.Pal256));
            Check(Pictures.PaletteColours(file).SequenceEqual(Website.Pal256), "a palette PNG doesn't read back");
        }

        static void LookFilesRoundTrip()
        {
            foreach (var l in Website.Links)
            {
                Settings s = l.Settings.Settings();
                Dictionary<string, object> loaded = Looks.FromFile(Looks.FileText(s));
                Check(loaded != null, "a look file isn't read back");
                string wrong = l.Settings.Against(Looks.Apply(loaded, Settings.Defaults()));
                Check(wrong == null, "a look file reads back differently: " + wrong);
            }
            // the website's own settings (as it keeps them in the browser) read as a look
            string website = "{\"profileId\":\"pico8\",\"fit\":\"fit\",\"resolution\":\"native\",\"detail\":0.25,\"background\":\"white\",\"adjust\":{\"brightness\":0.1,\"contrast\":1.2," +
                "\"saturation\":1,\"gamma\":1},\"dither\":\"atkinson\",\"fx\":{\"screen\":\"crt\"},\"pixelShape\":\"raw\",\"multiplier\":8,\"exportFx\":false}";
            Settings w = Looks.Apply(Looks.FromFile(website), Settings.Defaults());
            Check(w.Deres.ProfileId == "pico8" && w.Deres.Fit == "fit" && w.Deres.Detail == 0.25 && w.Deres.Adjust.Contrast == 1.2 && w.Deres.Dither == "atkinson" && w.Multiplier == 8 &&
                w.Fx.Screen == Screens.Crt && w.Fx.Mask == "grille" && !w.ExportFx, "the website's own settings should read as a look, its CRT and all");
            Check(Looks.FromFile("{\"hello\":1}") == null && Looks.FromFile("[1]") == null && Looks.FromFile("not json") == null && Looks.FromFile("") == null, "what isn't a look should be refused");
        }

        // What a crafted link or look file can hold: nesting deeper than any stack (JSON.parse reads
        // it; so must this, without falling over), and numbers a megabyte long, read quickly and
        // to the right double.
        static void OddTextDoesNoHarm()
        {
            string Link(string json) => "#s=" + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(json));
            string deep = new string('[', 200000) + new string(']', 200000);
            Dictionary<string, object> loaded = Looks.FromLink(Link(deep));
            Check(loaded != null && Looks.Link(Looks.Apply(loaded, Settings.Defaults())) == Looks.Link(Settings.Defaults()),
                "a link of 200,000 nested arrays should read as no fields, as JSON.parse reads it");
            loaded = Looks.FromLink(Link("{\"p\":\"nes\",\"x\":{\"y\":" + deep + "},\"m\":8}"));
            Check(loaded != null && (string)loaded["profileId"] == "nes" && (double)loaded["multiplier"] == 8, "the fields round a deep part should still be read");
            Check(Looks.FromLink(Link(new string('[', 200000) + new string(']', 199999))) == null && Looks.FromLink(Link("[" + deep + ",]")) == null &&
                Looks.FromLink(Link(new string('{', 1000))) == null, "broken deep text should be refused");
            Check(Json.Parse("[" + new string('[', 100) + "{\"a\":[1,{\"b\":\"c\"},true,null,-0.5e3]}" + new string(']', 100) + "]") is List<object>, "a deep part that's right should read");

            // a number a megabyte long: quick, and the nearest double
            var watch = System.Diagnostics.Stopwatch.StartNew();
            double big = (double)Json.Parse("1" + new string('0', 1000000)), small = (double)Json.Parse("0." + new string('0', 1000000) + "1");
            double third = (double)Json.Parse("0." + new string('3', 1000000)), near = (double)Json.Parse("0.1" + new string('0', 999990) + "1");
            Check(watch.ElapsedMilliseconds < 3000, "a megabyte of digits took " + watch.ElapsedMilliseconds + " ms");
            Check(double.IsPositiveInfinity(big) && small == 0 && third == 1.0 / 3 && near == 0.1, "long numbers read as " + big + ", " + small + ", " + third.ToString("R") + ", " + near.ToString("R"));
            // just past a halfway point, it must round up: 9007199254740993 is halfway, one more digit decides
            Check((double)Json.Parse("9007199254740993" + new string('0', 900) + "1" + "e-901") == 9007199254740994.0 &&
                (double)Json.Parse("9007199254740993" + new string('0', 900) + "e-900") == 9007199254740992.0, "a halfway number past 800 digits rounds the wrong way");
            Check(Looks.FromFile("{\"profileId\":\"nes\",\"detail\":0." + new string('5', 900000) + "}") != null, "a look file with a huge number should still read");
        }
    }
}
