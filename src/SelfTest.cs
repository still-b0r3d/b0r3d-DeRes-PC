using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using B0r3dDeRes.Engine;

namespace B0r3dDeRes
{
    // b0r3dDeRes.exe --selftest <file>: no window shown. Opens a sample from inside the exe, makes
    // the picture, saves it as a PNG the way File → Save PNG does and reads that back through
    // Windows, makes it on every machine, through every screen effect and through the signal (the
    // website's very frame), makes the ASCII studio's art, reads the sample's EXIF, cleans it and
    // writes into it (the website's very files), reads back a share link, writes an MP4 beside
    // <file> and reads it back (then deletes it), builds the windows in both looks with the
    // picture in them, and writes what it found to <file> as key=value lines; the exit code is 0
    // when it all worked.
    // The tests run it on the finished exe, on its own in a folder, to prove it works with
    // nothing beside it.
    static class SelfTest
    {
        // the website's own frame of the Rukus sample on the Game Boy over RF (its SHA-256; the
        // tests check it against their copy of the website's, Website.SelfTestSignal)
        internal const string RukusOverRf = "e4144a90e4de3245bdc926c59c83d478e993d8aa8d53702ace86b81d116de9d6";

        // the website's own GIF of the GIF sample's frames on the Game Boy at 1× (its SHA-256;
        // the tests check it against Website.SelfTestGif)
        internal const string RukusGif = "24928182a5d65f11597832d9f3e8a428f984a3079d353681bcaf0d89abd0481c";

        // the website's own art of the Rukus sample as the ASCII studio starts, and its picture of
        // the sample as Teletext 33 columns wide in 16 colours at 2× (their SHA-256; the tests
        // check them against WebsiteAscii.cs)
        internal const string RukusArt = "60b7aa1abe97328070130d140e9443e72badf42c5255859bec06055cfbc1ee89";
        internal const string RukusTeletext = "ee419be2f1f1d55782e2dbd4c47b96db5577058d37a745e0283cd7f10c02ea25";
        // every letter of the website's font as Chromium draws it, 16 rows each in code point order
        internal const string VgaLetters = "12d9ab6682bb36099543f2c94c26e4c8fc5099222190981a4f1bcfa00141b7d8";

        // the website's clean copy of the Rukus sample (all of its metadata out), and the sample
        // with "me" injected as its Artist (their SHA-256; the tests check them against the
        // website's results in tests\website\exif)
        internal const string RukusScrubbed = "d90f9881c17954eeb3bf0f535616cf6698302fec73c1d965ba927d9c615f0c61";
        internal const string RukusArtist = "d6ab1702fef1cdc30dd45cd6f8e7fa91b344806791f24cd2f2f3dc5bdb3950bd";

        public static int Run(string reportPath)
        {
            var lines = new List<string>();
            bool ok = true;
            try
            {
                lines.Add("version=" + Program.FullVersion);
                lines.Add("dpi=" + Native.DpiAwareness());
                // the screens, in real pixels (one wallpaper each)
                lines.Add("screens_seen=" + string.Join(" ", Monitors.All().Select(m => m.Bounds.Width + "x" + m.Bounds.Height)));
                lines.Add("bits=" + (Environment.Is64BitProcess ? "64" : "32"));
                ok &= Environment.Is64BitProcess;

                // a sample from inside the exe, made into a Game Boy picture
                LoadedPicture rukus = Pictures.Sample("rukus");
                var engine = new Deres();
                engine.SetSource(rukus.Source);
                Rendered r = engine.Render(new DeresSettings());
                bool rendered = r.Width == 160 && r.Height == 144 && r.Used >= 2 && r.Used <= 4;
                lines.Add("render=" + (rendered ? "ok" : "failed"));
                ok &= rendered;

                // saved, then read back by Windows: the same pixels, and the palette kept
                byte[] png = Export.PngBytes(r, 3, false);
                BitmapFrame frame = BitmapDecoder.Create(new MemoryStream(png), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
                bool indexed = frame.Format == PixelFormats.Indexed2 && frame.Palette != null && frame.Palette.Colors.Count == 4;
                var back = new FormatConvertedBitmap(frame, PixelFormats.Rgb24, null, 0);
                var pixels = new byte[back.PixelWidth * back.PixelHeight * 3];
                back.CopyPixels(pixels, back.PixelWidth * 3, 0);
                bool same = back.PixelWidth == 480 && back.PixelHeight == 432;
                for (int y = 0; same && y < 432; y++)
                    for (int x = 0; same && x < 480; x++)
                    {
                        int s = ((y / 3) * 160 + x / 3) * 3, o = (y * 480 + x) * 3;
                        same = pixels[o] == r.Rgb[s] && pixels[o + 1] == r.Rgb[s + 1] && pixels[o + 2] == r.Rgb[s + 2];
                    }
                lines.Add("png=" + (same && indexed ? "ok" : "failed"));
                ok &= same && indexed;

                // every machine makes a picture of it, kept see-through too, and writes its PNG and its own file
                bool machines = Profiles.All.All(p => new[] { Backgrounds.White, Backgrounds.Transparent }.All(bg =>
                {
                    Rendered m = engine.Render(new DeresSettings { ProfileId = p.Id, Dither = p.Dither, Background = bg });
                    return m.Used >= 1 && Export.PngBytes(m, 1, false).Length > 0 && (p.EmulatorFormats.Length == 0 || m.EmulatorFile?.Data.Length > 0);
                }));
                lines.Add("machines=" + (machines ? "ok" : "failed"));
                ok &= machines;

                // every screen effect in a 3× PNG (full colour, read back by Windows), and a wallpaper with one
                bool screens = Screens.All.Where(s => s != Screens.Off).All(s =>
                {
                    byte[] withFx = Export.PngBytes(r, 3, false, new FxSettings { Screen = s });
                    BitmapFrame f = BitmapDecoder.Create(new MemoryStream(withFx), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
                    return f.PixelWidth == 480 && f.PixelHeight == 432 && f.Format.BitsPerPixel >= 24;
                });
                using (var paper = new MemoryStream())
                {
                    Export.WriteWallpaper(paper, r, 1920, 1080, false, new FxSettings { Screen = Screens.Crt });
                    BitmapFrame f = BitmapDecoder.Create(new MemoryStream(paper.ToArray()), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
                    screens &= f.PixelWidth == 1920 && f.PixelHeight == 1080;
                }
                lines.Add("screens=" + (screens ? "ok" : "failed"));
                ok &= screens;

                // the signal: the picture over RF comes out as the website's own, byte for byte (so
                // this PC works its sums out as the website's engine does), and a PNG through it
                SignalImage through = Signal.Run(r, r.Profile.Id, new SignalSettings { Mode = SignalModes.Rf }, 0);
                bool signal;
                using (var sha = System.Security.Cryptography.SHA256.Create())
                    signal = string.Concat(sha.ComputeHash(through.Rgb).Select(b => b.ToString("x2"))) == RukusOverRf;
                BitmapFrame overRf = BitmapDecoder.Create(new MemoryStream(Export.PngBytes(r, 3, false, null, through)), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
                signal &= overRf.PixelWidth == 480 && overRf.PixelHeight == 432 && overRf.Format.BitsPerPixel >= 24;
                lines.Add("signal=" + (signal ? "ok" : "failed"));
                ok &= signal;

                // GIFs: the GIF sample read by DeRes's own decoder, its frames through the Game Boy
                // into a GIF: the website's very file, byte for byte
                LoadedPicture gif = Pictures.Sample("rukus-gif");
                bool animated = gif.Animation != null && gif.Animation.Frames.Length == gif.Frames && gif.Frames > 1;
                if (animated)
                {
                    var frames = new Deres();
                    var settings = new DeresSettings();
                    using (var bytes = new MemoryStream())
                    using (var sha = System.Security.Cryptography.SHA256.Create())
                    {
                        Animation.FramesOf(bytes, new FramesOptions
                        {
                            Multiplier = 1, DelaysMs = gif.Animation.DelaysMs, LoopCount = gif.Animation.LoopCount, Indexed = true, SharedPalette = true,
                            Render = (k, lane) =>
                            {
                                frames.SetSource(gif.Animation.Frames[k]);
                                return frames.Render(settings);
                            },
                        });
                        animated = string.Concat(sha.ComputeHash(bytes.ToArray()).Select(b => b.ToString("x2"))) == RukusGif;
                    }
                }
                lines.Add("gif=" + (animated ? "ok" : "failed"));
                ok &= animated;

                // the ASCII studio: the Rukus sample's art as the website makes it (the sample read
                // as the website's canvas reads it), its Teletext picture at 2× as Chromium's canvas
                // draws it, and every letter of the font file inside the exe as Chromium draws it
                bool ascii;
                using (var sha = System.Security.Cryptography.SHA256.Create())
                {
                    string Hash(byte[] bytes) => string.Concat(sha.ComputeHash(bytes).Select(b => b.ToString("x2")));
                    AsciiArt art = Ascii.Generate(rukus.Source, new AsciiSettings());
                    var cells = new MemoryStream();
                    foreach (int c in art.Chars) cells.Write(BitConverter.GetBytes((uint)c), 0, 4);
                    cells.Write(art.Fg, 0, art.Fg.Length);
                    cells.Write(art.Bg, 0, art.Bg.Length);
                    cells.Write(art.Extra, 0, art.Extra.Length);
                    ascii = Hash(cells.ToArray()) == RukusArt;
                    AsciiArt teletext = Ascii.Generate(rukus.Source, new AsciiSettings { Mode = AsciiModes.Teletext, ColorMode = AsciiColours.Ansi16, Columns = 33 });
                    ascii &= Hash(AsciiRender.Rgb(teletext, 2)) == RukusTeletext;
                    var letters = new MemoryStream();
                    foreach (int c in VgaFont.Characters.OrderBy(c => c))
                    {
                        byte[] rows = AsciiRender.Letter(c);
                        letters.Write(rows, 0, rows.Length);
                    }
                    ascii &= Hash(letters.ToArray()) == VgaLetters && Ascii.AnsBytes(art).Length > 0;
                }
                lines.Add("ascii=" + (ascii ? "ok" : "failed"));
                ok &= ascii;

                // the EXIF tools: the Rukus sample's file inside the exe read as the website reads
                // it, its clean copy the website's byte for byte and Verified Clean, and an Artist
                // written into it the website's very file
                bool exif;
                using (var sha = System.Security.Cryptography.SHA256.Create())
                {
                    string Hash(byte[] bytes) => string.Concat(sha.ComputeHash(bytes).Select(b => b.ToString("x2")));
                    Exif.ImageMetadata meta = Exif.ExifReader.Inspect(rukus.FileBytes);
                    Exif.ScrubResult clean = Exif.Scrubber.Scrub(rukus.FileBytes);
                    exif = meta.Make == "b0r3d optics" && meta.Gps != null && meta.HasThumbnail && Hash(clean.Data) == RukusScrubbed && clean.Report.IsClean &&
                        Exif.Scrubber.Verify(meta, rukus.FileBytes.Length, clean.Data).IsClean;
                    exif &= Hash(Exif.Injector.Inject(rukus.FileBytes, new Exif.ExifEdits { Artist = Exif.Edit<string>.To("me") })) == RukusArtist;
                }
                lines.Add("exif=" + (exif ? "ok" : "failed"));
                ok &= exif;

                // a share link, made and read back (with the signal in it)
                Settings shared = Settings.Defaults();
                shared.Deres.ProfileId = "snes";
                shared.Deres.CustomCount = 7;
                shared.Signal.Mode = SignalModes.VhsEp;
                shared.Signal.Wear = 0.85;
                bool link = Looks.Link(Looks.Apply(Looks.FromLink(Looks.Link(shared)), Settings.Defaults())) == Looks.Link(shared);
                lines.Add("link=" + (link ? "ok" : "failed"));
                ok &= link;

                // the samples are all inside: each opens
                bool samples = Pictures.Samples.All(sample => Pictures.Sample(sample.Id).Source.Width > 0);
                lines.Add("samples=" + (samples ? "ok" : "failed"));
                ok &= samples;

                // video: an MP4 through Windows' video system and back (a Windows without its video
                // parts, the "N" editions, says so: pictures work there all the same)
                string video = VideoRoundTrip(reportPath);
                lines.Add("video=" + video);
                ok &= video == "ok" || video == "none";

                // the windows, built from inside this exe and laid out (never shown), in both looks:
                // their pages, styles and pictures all load
                var app = new App();
                app.InitializeComponent();
                // a window that throws ends the self-test with a line in the report, never a message box
                App.OnProblem = x =>
                {
                    lines.Add("error=a window threw " + x.GetType().Name + ": " + Flat(x.Message));
                    lines.Add("result=failed");
                    Environment.Exit(Report(reportPath, lines, false));
                };
                // (the window opens its picture off this thread and comes back to it)
                System.Threading.SynchronizationContext.SetSynchronizationContext(new System.Windows.Threading.DispatcherSynchronizationContext(System.Windows.Threading.Dispatcher.CurrentDispatcher));
                foreach (Look look in new[] { Look.Light, Look.Dark })
                {
                    Theme.Set(look);
                    var window = new MainWindow(new Settings(), null);
                    var until = DateTime.UtcNow.AddSeconds(20);
                    while ((window.Opening || window.Rendering || window.Shown == null) && DateTime.UtcNow < until) Pump();
                    window.Root.Measure(new Size(1280, 800));
                    window.Root.Arrange(new Rect(0, 0, 1280, 800));
                    window.Root.UpdateLayout();
                    var about = new AboutWindow();
                    about.Root.Measure(new Size(640, double.PositiveInfinity));
                    about.Root.Arrange(new Rect(about.Root.DesiredSize));
                    about.Root.UpdateLayout();
                    var batch = new BatchWindow(window);
                    batch.Root.Measure(new Size(640, 760));
                    batch.Root.Arrange(new Rect(0, 0, 640, 760));
                    batch.Root.UpdateLayout();
                    if (window.Shown == null || !window.View.HasPicture) throw new InvalidOperationException("the main window made no picture in the " + look + " look");
                }
                lines.Add("windows=ok");
            }
            catch (Exception e)
            {
                ok = false;
                string why = e.GetType().Name + ": " + e.Message;
                for (Exception inner = e.InnerException; inner != null; inner = inner.InnerException) why += " <- " + inner.GetType().Name + ": " + inner.Message;
                lines.Add("error=" + why.Replace('\r', ' ').Replace('\n', ' '));
            }
            lines.Add("result=" + (ok ? "ok" : "failed"));
            return Report(reportPath, lines, ok);
        }

        // Ten frames and a third of a second of sound written as an MP4 the way Save MP4 writes one
        // (H.264 and AAC), beside the report, read back frame by frame and deleted: "ok", "none"
        // on a Windows without video parts, or what came back.
        static string VideoRoundTrip(string reportPath)
        {
            if (!Video.Mf.Available) return "none";
            string full = Path.GetFullPath(reportPath);
            string path = Path.Combine(Path.GetDirectoryName(full), Path.GetFileNameWithoutExtension(full) + "-selftest.mp4");
            try
            {
                using (FileStream file = File.Create(path))
                using (var mp4 = new Video.Mp4Writer(file, 64, 48, 30, true))
                {
                    var bgra = new byte[64 * 48 * 4];
                    for (int k = 0; k < 10; k++)
                    {
                        for (int i = 0; i < bgra.Length; i += 4)
                        {
                            bgra[i] = (byte)(k * 25);
                            bgra[i + 1] = 128;
                            bgra[i + 2] = (byte)(255 - k * 25);
                            bgra[i + 3] = 255;
                        }
                        long time = (long)Math.Round(k * 1e7 / 30), next = (long)Math.Round((k + 1) * 1e7 / 30);
                        mp4.AddFrame(bgra, time, next - time);
                    }
                    var quiet = new short[16000 * 2];
                    mp4.AddSound(quiet, 0, quiet.Length, 0);
                    mp4.Finish();
                }
                Video.VideoFile back = Video.VideoFile.Open(path);
                int frames = 0;
                using (Video.VideoReader reader = back.Frames())
                    while (reader.Next() != null) frames++;
                return back.Width == 64 && back.Height == 48 && frames == 10 && back.HasSound ? "ok"
                    : "failed (" + back.Width + "x" + back.Height + ", " + frames + " frames" + (back.HasSound ? "" : ", no sound") + ")";
            }
            catch (Video.VideoException x)
            {
                return "failed (" + Flat(x.Message) + ")";
            }
            finally
            {
                try
                {
                    File.Delete(path);
                }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
                {
                }
            }
        }

        // lets the window's queued work run for a moment
        static void Pump()
        {
            var frame = new System.Windows.Threading.DispatcherFrame();
            System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() => frame.Continue = false));
            System.Windows.Threading.Dispatcher.PushFrame(frame);
            System.Threading.Thread.Sleep(5);
        }

        // a message on one line
        static string Flat(string text) => text.Replace('\r', ' ').Replace('\n', ' ');

        static int Report(string reportPath, List<string> lines, bool ok)
        {
            try
            {
                File.WriteAllLines(reportPath, lines);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is NotSupportedException || e is ArgumentException)
            {
                return 3;
            }
            return ok ? 0 : 1;
        }
    }
}
