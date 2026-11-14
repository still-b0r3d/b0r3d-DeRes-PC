using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using B0r3dDeRes.Engine;

namespace B0r3dDeRes.Tests
{
    // One picture's art the website made (WebsiteAscii.cs): the picture, the settings, then what came out.
    sealed class AsciiCase
    {
        public readonly string Input, Art, Text, Markdown, Ansi, Html, Ans, Seq, Prg, Txt;
        public readonly AsciiSettings Settings;
        public readonly int Width, Height, Colours;

        public AsciiCase(string input, AsciiSettings settings, int width, int height, int colours, string art, string text, string markdown, string ansi, string html, string ans, string seq, string prg, string txt)
        {
            Input = input; Settings = settings; Width = width; Height = height; Colours = colours; Art = art; Text = text; Markdown = markdown; Ansi = ansi; Html = html; Ans = ans; Seq = seq; Prg = prg; Txt = txt;
        }

        public override string ToString() => Input + " " + Settings.Mode + " " + Settings.ColorMode + " " + Settings.Columns + " cols" + (Settings.Mode == AsciiModes.Ramp ? " " + Settings.RampPreset : "");
    }

    // A GIF or APNG of the art's pictures the website made: the size, then the file's hash and length (and an APNG's parts).
    sealed class AsciiGif
    {
        public readonly bool Apng;
        public readonly int M, Width, Height, Length;
        public readonly string Sha;
        public readonly ApngCase Png;

        public AsciiGif(bool apng, int m, int width, int height, string sha, int length, ApngCase png)
        {
            Apng = apng; M = m; Width = width; Height = height; Sha = sha; Length = length; Png = png;
        }
    }

    // The GIF sample's frames through the settings: every frame's art, the ANSImation, the GIFs and APNGs.
    sealed class AsciiAnimation
    {
        public readonly AsciiSettings Settings;
        public readonly int Frames, AnsimationLength;
        public readonly string FrameShas, Ansimation;
        public readonly AsciiGif[] Gifs;

        public AsciiAnimation(AsciiSettings settings, int frames, string frameShas, string ansimation, int ansimationLength, AsciiGif[] gifs)
        {
            Settings = settings; Frames = frames; FrameShas = frameShas; Ansimation = ansimation; AnsimationLength = ansimationLength; Gifs = gifs;
        }
    }

    // The ASCII / ANSI studio's engine against the website's own (WebsiteAscii.cs, from its engine
    // in Node and its canvas in Chromium).
    static partial class TestMain
    {
        static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);

        // the art's hash as the website's test-file maker takes it: the characters (32-bit, little
        // end first), the colours, then the ANSI colours, masks and C64 colours
        static string ArtSha(AsciiArt art)
        {
            var bytes = new MemoryStream();
            foreach (int c in art.Chars) bytes.Write(BitConverter.GetBytes((uint)c), 0, 4);
            bytes.Write(art.Fg, 0, art.Fg.Length);
            bytes.Write(art.Bg, 0, art.Bg.Length);
            bytes.Write(art.Extra, 0, art.Extra.Length);
            return Sha(bytes.ToArray());
        }

        static SauceInfo DatedSauce(string title) => new SauceInfo { Title = title, Author = "", Group = "b0r3d.org", Date = WebsiteAscii.Date };

        static void AsciiLikeTheWebsite()
        {
            var wrong = new List<string>();
            Parallel.For(0, WebsiteAscii.Cases.Length, i =>
            {
                AsciiCase c = WebsiteAscii.Cases[i];
                // (the pictures as the website's engine was handed them: they're its canvas's pixels already)
                AsciiArt art = Ascii.Generate(Input(c.Input), c.Settings, false);
                var why = new List<string>();
                if (art.Width != c.Width || art.Height != c.Height) why.Add(art.Width + "×" + art.Height + " cells, not " + c.Width + "×" + c.Height);
                if (art.ColourCount != c.Colours) why.Add(art.ColourCount + " colours, not " + c.Colours);
                if (ArtSha(art) != c.Art) why.Add("the cells");
                if (Sha(Utf8.GetBytes(Ascii.PlainText(art))) != c.Text) why.Add("text");
                if (Sha(Utf8.GetBytes(Ascii.Markdown(art))) != c.Markdown) why.Add("Markdown");
                if (Sha(Utf8.GetBytes(Ascii.AnsiText(art))) != c.Ansi) why.Add("ANSI escapes");
                if (Sha(Utf8.GetBytes(Ascii.Html(art))) != c.Html) why.Add("HTML");
                if (Sha(Ascii.AnsBytes(art, DatedSauce(c.Input))) != c.Ans) why.Add(".ans");
                if (c.Seq.Length > 0 && Sha(Ascii.PetsciiSeq(art)) != c.Seq) why.Add(".seq");
                if (c.Prg.Length > 0 && Sha(Ascii.PetsciiPrg(art)) != c.Prg) why.Add(".prg");
                if (c.Txt.Length > 0 && Sha(Utf8.GetBytes(Ascii.TeletextPage(art))) != c.Txt) why.Add(".txt");
                if (why.Count > 0) lock (wrong) wrong.Add("#" + i + " " + c + ": " + string.Join(", ", why));
            });
            Check(wrong.Count == 0, wrong.Count + " differ: " + string.Join("; ", wrong.OrderBy(w => w).Take(6)));
        }

        static void AsciiTablesLikeTheWebsite()
        {
            foreach (var (id, units) in WebsiteAscii.Ramps)
            {
                string ramp = Ascii.Ramps.FirstOrDefault(r => r.Id == id).Ramp;
                Check(ramp != null && string.Join(",", ramp.Select(ch => ((int)ch).ToString("x"))) == units, "the " + id + " ramp isn't the website's");
            }
            Check(Ascii.Ramps.Length == WebsiteAscii.Ramps.Length, "a ramp too many");
            Check(string.Join(",", Ascii.TeletextMosaics.Select(c => c.ToString("x"))) == WebsiteAscii.Mosaics, "Teletext's mosaics aren't the website's");
            Check(string.Join(",", Ascii.Ansi16Palette) == WebsiteAscii.Ansi16 && string.Join(",", Ascii.C64Palette) == WebsiteAscii.C64 && string.Join(",", Ascii.TeletextPalette) == WebsiteAscii.Teletext, "a palette isn't the website's");
            var codes = WebsiteAscii.ScreenCodes.Split(',').Select(p => p.Split(':')).ToDictionary(p => Convert.ToInt32(p[0], 16), p => int.Parse(p[1]));
            Check(codes.Count == Ascii.PetsciiScreenCodes.Count && codes.All(p => Ascii.PetsciiScreenCodes.TryGetValue(p.Key, out int v) && v == p.Value), "PETSCII's screen codes aren't the website's");
            // the settings kept between visits, checked as the website checks them
            foreach (var (kept, used) in WebsiteAscii.Checks)
            {
                var o = (Dictionary<string, object>)Json.Parse(kept);
                double? N(string k) => o.TryGetValue(k, out object v) && v is double d ? d : (double?)null;
                string T(string k) => o.TryGetValue(k, out object v) ? v as string : null;
                bool? B(string k) => o.TryGetValue(k, out object v) && v is bool b ? b : (bool?)null;
                // (JSON writes NaN and Infinity as null: the website was handed the numbers themselves)
                double? columns = kept.Contains("\"columns\":null") ? double.NaN : N("columns"), gamma = kept.Contains("\"gamma\":null") ? double.PositiveInfinity : N("gamma");
                AsciiSettings got = AsciiSettings.Check(T("mode"), columns, T("rampPreset"), T("customRamp"), B("invertRamp"), T("colorMode"), N("contrast"), N("brightness"), gamma,
                    B("twoToOneAspect"), B("teletextSeparated"), B("teletextHeader"));
                Check(got.Key == used.Key, "kept " + Short(kept) + ": " + got.Key + ", not " + used.Key);
            }
            // a ramp pasted with line breaks, on one line as Chromium's box takes it (what it made of each, pasted)
            foreach (var (pasted, kept) in new[] { ("abc\ndef\n", "abc def"), ("abc\r\ndef", "abc def"), ("\nabc", " abc"), ("a\rb", "a b"), ("a\n\nb", "a  b"), ("ab\tc", "ab\tc"), ("x y", "x y"), ("\r\n\n", "") })
                Check(AsciiSettings.OneLine(pasted) == kept, "pasted " + Json.Quote(pasted) + ": " + Json.Quote(AsciiSettings.OneLine(pasted)) + ", not " + Json.Quote(kept));
        }

        static void AsciiPicturesLikeTheWebsite()
        {
            var wrong = new List<string>();
            foreach (var (index, m, w, h, rgb) in WebsiteAscii.HalfBlockPngs.Concat(WebsiteAscii.TeletextPngs))
            {
                AsciiCase c = WebsiteAscii.Cases[index];
                AsciiArt art = Ascii.Generate(Input(c.Input), c.Settings, false);
                AsciiRender.Size(art.Width, art.Height, m, out int width, out int height);
                if (width != w || height != h) wrong.Add(c + " at " + m + "×: " + width + "×" + height);
                else if (Sha(AsciiRender.Rgb(art, m)) != rgb) wrong.Add(c + " at " + m + "×: the pixels");
            }
            Check(wrong.Count == 0, string.Join("; ", wrong.Take(6)));
            // the PNG is those pixels, and the website's names
            AsciiCase half = WebsiteAscii.Cases[WebsiteAscii.HalfBlockPngs[0].Case];
            AsciiArt a = Ascii.Generate(Input(half.Input), half.Settings, false);
            var png = new MemoryStream();
            AsciiRender.WritePng(png, a, 2, out int pw, out int ph);
            byte[] back = DecodeRgb(png.ToArray(), out int bw, out int bh);
            Check(bw == pw && bh == ph && back.SequenceEqual(AsciiRender.Rgb(a, 2)), "the PNG isn't the picture's pixels");
            Check(AsciiRender.PngName("rukus", a, 2) == "rukus-ansi-" + pw + "x" + ph + "-2x.png", "the PNG's name: " + AsciiRender.PngName("rukus", a, 2));
            AsciiArt ramp = Ascii.Generate(Input("rukus"), new AsciiSettings { Mode = AsciiModes.Ramp }, false);
            Check(AsciiRender.PngName("rukus", ramp, 4) == "rukus-ascii-2560x" + (ramp.Height * 64) + "-4x.png", "the font PNG's name: " + AsciiRender.PngName("rukus", ramp, 4));
            Check(AsciiRender.AnimationName("rukus", ramp, 1, false) == "rukus-ascii-640x" + (ramp.Height * 16) + "-1x.gif" && AsciiRender.AnimationName("rukus", ramp, 1, true) == "rukus-ascii-640x" + (ramp.Height * 16) + "-1x-animated.png", "the animations' names");
            // characters from the font are its letters, crisp, in each cell's colours
            AsciiCase withFont = WebsiteAscii.Cases.First(x => x.Settings.Mode == AsciiModes.Ramp && x.Settings.ColorMode == AsciiColours.Truecolor);
            AsciiArt f = Ascii.Generate(Input(withFont.Input), withFont.Settings, false);
            foreach (int m in AsciiRender.Scales)
            {
                byte[] rgb = AsciiRender.Rgb(f, m);
                AsciiRender.Size(f.Width, f.Height, m, out int w, out int h);
                for (int cell = 0; cell < f.Cells; cell += 7)
                {
                    int cx = cell % f.Width, cy = cell / f.Width;
                    byte[] letter = f.Chars[cell] == ' ' ? new byte[16] : AsciiRender.Letter(f.Chars[cell]);
                    for (int y = 0; y < 16 * m; y++)
                        for (int x = 0; x < 8 * m; x++)
                        {
                            bool lit = (letter[y / m] & (0x80 >> (x / m))) != 0;
                            byte[] want = lit ? f.Fg : f.Bg;
                            long o = ((long)(cy * 16 * m + y) * w + cx * 8 * m + x) * 3;
                            if (rgb[o] != want[cell * 3] || rgb[o + 1] != want[cell * 3 + 1] || rgb[o + 2] != want[cell * 3 + 2])
                                throw new CheckException("cell " + cell + " at " + m + "×: pixel " + x + "," + y + " isn't the letter's");
                        }
                }
            }
        }

        // a PNG's pixels as r, g, b (it's one of ours: 8-bit r, g, b, rows filtered)
        static byte[] DecodeRgb(byte[] png, out int width, out int height)
        {
            var bitmap = System.Windows.Media.Imaging.BitmapDecoder.Create(new MemoryStream(png), System.Windows.Media.Imaging.BitmapCreateOptions.PreservePixelFormat | System.Windows.Media.Imaging.BitmapCreateOptions.IgnoreColorProfile,
                System.Windows.Media.Imaging.BitmapCacheOption.OnLoad).Frames[0];
            var rgb24 = new System.Windows.Media.Imaging.FormatConvertedBitmap(bitmap, System.Windows.Media.PixelFormats.Rgb24, null, 0);
            width = rgb24.PixelWidth;
            height = rgb24.PixelHeight;
            var pixels = new byte[width * height * 3];
            rgb24.CopyPixels(pixels, width * 3, 0);
            return pixels;
        }

        static void VgaFontLikeChromium()
        {
            using (Stream stream = typeof(VgaFont).Assembly.GetManifestResourceStream(VgaFont.ResourceName))
            {
                var bytes = new MemoryStream();
                stream.CopyTo(bytes);
                Check(Sha(bytes.ToArray()) == WebsiteAscii.FontSha, "the font in the exe isn't the website's file");
            }
            int[] points = WebsiteAscii.FontPoints.Split(',').Select(p => Convert.ToInt32(p, 16)).ToArray();
            Check(VgaFont.Characters.OrderBy(c => c).SequenceEqual(points), "the font's characters: " + VgaFont.Characters.Count() + ", not " + points.Length);
            var wrong = new List<string>();
            for (int i = 0; i < points.Length; i++)
            {
                // (what's drawn: the canvas shows the five whitespace controls and the soft hyphen as nothing)
                string want = WebsiteAscii.FontRows.Substring(i * 32, 32), got = Hex(AsciiRender.Letter(points[i]));
                if (got != want) wrong.Add("U+" + points[i].ToString("X4") + " " + got + " not " + want);
            }
            Check(wrong.Count == 0, wrong.Count + " letters differ: " + string.Join(", ", wrong.Select(w => w.Substring(0, 6))) + "; " + string.Join("; ", wrong.Take(2)));
            // the six quarter blocks PETSCII uses (not in the font) are its half blocks quartered:
            // the top half's seven rows over the bottom half's nine, the left four columns beside the right four
            string Rows(string top, string bottom) => string.Concat(Enumerable.Repeat(top, 7)) + string.Concat(Enumerable.Repeat(bottom, 9));
            Check(Hex(VgaFont.Letter(0x2580)) == Rows("ff", "00") && Hex(VgaFont.Letter(0x2584)) == Rows("00", "ff") && Hex(VgaFont.Letter(0x258C)) == Rows("f0", "f0"),
                "the font's half blocks aren't where they were");
            var quarters = new (int Point, string Name, string Rows)[]
            {
                (0x2598, "▘", Rows("f0", "00")), (0x259D, "▝", Rows("0f", "00")), (0x2596, "▖", Rows("00", "f0")),
                (0x2597, "▗", Rows("00", "0f")), (0x259A, "▚", Rows("f0", "0f")), (0x259E, "▞", Rows("0f", "f0")),
            };
            foreach (var q in quarters)
                Check(Hex(AsciiRender.Letter(q.Point)) == q.Rows, q.Name + " isn't the font's half blocks quartered: " + Hex(AsciiRender.Letter(q.Point)));
            Check(Hex(AsciiRender.Letter(0xD83D)) == Hex(VgaFont.Letter(0xFFFD)), "a lone surrogate isn't drawn as U+FFFD");
            Check(AsciiRender.Letter(0xFF71) == null && AsciiRender.Borrowed(new[] { 0xFF71, 'a', ' ' }).SequenceEqual(new[] { 0xFF71 }), "katakana should be borrowed from Windows' fonts");
        }

        static void CanvasLevelsLikeChromium()
        {
            byte[] want = Convert.FromBase64String(WebsiteAscii.CanvasLevels);
            var wrong = new List<string>();
            for (int a = 0; a < 256; a++)
                for (int c = 0; c < 256; c++)
                    if (Ascii.CanvasLevel(c, a) != want[a * 256 + c]) wrong.Add("level " + c + " at alpha " + a + ": " + Ascii.CanvasLevel(c, a) + ", not " + want[a * 256 + c]);
            Check(wrong.Count == 0, wrong.Count + " of 65,536 differ: " + string.Join("; ", wrong.Take(4)));
            // the Ruckus sample, opened from its own file as the window opens it, makes the website's art of the very same picture
            PictureSource own = Pictures.Sample("rukus").Source;
            AsciiCase start = WebsiteAscii.Cases.First(x => x.Input == "rukus" && x.Settings.Key == new AsciiSettings().Key);
            foreach (AsciiCase c in WebsiteAscii.Cases.Where(x => x.Input == "rukus"))
                Check(ArtSha(Ascii.Generate(own, c.Settings)) == c.Art, "the Ruckus sample's art, read as the website's canvas reads it, isn't the website's: " + c);
        }

        static void AsciiAnimationsLikeTheWebsite()
        {
            AnimatedGif gif = OpenedGif("gif-rukus");
            Check(string.Join(",", gif.DelaysMs) == WebsiteAscii.GifDelays && (gif.LoopCount ?? -1) == WebsiteAscii.GifLoop, "the GIF sample's delays or loop");
            var wrong = new List<string>();
            foreach (AsciiAnimation a in WebsiteAscii.Animations)
            {
                string what = a.Settings.Mode + " " + a.Settings.ColorMode + " " + a.Settings.Columns;
                AsciiArt[] arts = gif.Frames.AsParallel().AsOrdered().Select(f => Ascii.Generate(f, a.Settings, false)).ToArray();
                if (string.Join(",", arts.Select(ArtSha)) != a.FrameShas) wrong.Add(what + ": a frame's art");
                var ans = new MemoryStream();
                var writer = new Ascii.AnsimationWriter(ans);
                foreach (AsciiArt art in arts) writer.Add(art);
                writer.Finish(DatedSauce("gif-rukus"));
                if (ans.Length != a.AnsimationLength || Sha(ans.ToArray()) != a.Ansimation) wrong.Add(what + ": the ANSImation (" + ans.Length + " bytes, not " + a.AnsimationLength + ")");
                foreach (AsciiGif g in a.Gifs)
                {
                    var file = new MemoryStream();
                    AnimationResult r = RgbAnimation.Write(file, new RgbAnimationOptions
                    {
                        Apng = g.Apng, Width = g.Width, Height = g.Height, DelaysMs = gif.DelaysMs, LoopCount = gif.LoopCount, Limits = AnimationLimits.Website, Lanes = 2,
                        Frame = (k, lane) => AsciiRender.Rgb(arts[k], g.M),
                    });
                    string kind = (g.Apng ? "APNG" : "GIF") + " at " + g.M + "×";
                    if (r.Width != g.Width || r.Height != g.Height || r.Frames != arts.Length) wrong.Add(what + " " + kind + ": " + r.Width + "×" + r.Height);
                    else if (g.Apng)
                    {
                        string why = SameApng(file.ToArray(), g.Png);
                        if (why != null) wrong.Add(what + " " + kind + ": " + why);
                    }
                    else if (file.Length != g.Length || Sha(file.ToArray()) != g.Sha) wrong.Add(what + " " + kind + " (" + file.Length + " bytes, not " + g.Length + ")");
                }
            }
            Check(wrong.Count == 0, string.Join("; ", wrong.Take(6)));
        }

        static void SauceRecords()
        {
            AsciiArt art = Ascii.Generate(Input("rukus"), new AsciiSettings(), false);
            byte[] ans = Ascii.AnsBytes(art, new SauceInfo { Title = "rukus", Author = "", Group = "b0r3d.org" });
            SauceInfo s = Sauce.Parse(ans);
            Check(s != null && s.Title == "rukus" && s.Author == "" && s.Group == "b0r3d.org" && s.Date == Sauce.Today() && s.TInfo1 == art.Width && s.TInfo2 == art.Height &&
                s.DataType == 1 && s.FileType == 1 && s.FontName == "IBM VGA" && s.FileSize == ans.Length - 129 && ans[ans.Length - 129] == Sauce.Eof, "the SAUCE record: " + (s == null ? "none" : s.Title + " " + s.Date + " " + s.FileSize));
            // a long title (in UTF-8) is cut at 35 bytes; nothing else spills
            byte[] record = Sauce.Record(10, new SauceInfo { Title = new string('é', 40), Group = "g", Date = "19990101", FontName = new string('x', 30) });
            Check(record.Length == 128 && Encoding.ASCII.GetString(record, 0, 7) == "SAUCE00" && record[7] == 0xc3 && record[41] == 0xc3 && record[42] == 0x20 && Encoding.ASCII.GetString(record, 82, 8) == "19990101" &&
                record[90] == 10 && record[96] == 80 && record[127] == (byte)'x', "the record's fields");
        }
    }
}
