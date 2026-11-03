using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using B0r3dDeRes.Engine;

namespace B0r3dDeRes.Tests
{
    // The Screen section and the preview's screen effect, PNGs and wallpapers with it, the
    // analog looks in the window, the batch, and DeRes the screen.
    static partial class TestMain
    {
        // waits for the picture and its screen effect
        static void WaitForScreen(MainWindow window)
        {
            Pump(() => !window.Opening && !window.Rendering && !window.ScreenRendering && window.Shown != null, 15000);
            Check(!window.ScreenRendering, "the screen effect wasn't made within 15 seconds");
            LayOut(window.Root, MainWidth, MainHeight);
        }

        static void PickScreen(MainWindow window, string id)
        {
            window.ScreenButtons.Children.OfType<RadioButton>().First(b => (string)b.Tag == id).IsChecked = true;
            WaitForScreen(window);
        }

        // The Screen section's controls follow the screen, and the preview shows the engine's own
        // screen effect, made at the zoom shown: whole screen pixels a machine pixel, 3 at least.
        static void ScreenSectionAndPreview()
        {
            MainWindow window = NewMain();
            WaitForPicture(window);
            Check(window.Current.Fx.Screen == Screens.Off && !window.View.HasScreen && window.CrtPanel.Visibility != Visibility.Visible, "the screen effect should start off");
            Check(window.ScreenButtons.Children.OfType<RadioButton>().Select(b => (string)b.Content).SequenceEqual(new[] { "Off", "CRT", "LCD", "VHS", "Composite", "P1 Green", "P3 Amber", "P4 White" }),
                "the screens, in the website's order and words");
            int renders = window.Renders;
            foreach (string id in Screens.All)
            {
                PickScreen(window, id);
                Check(window.Current.Fx.Screen == id, "picked " + id + ", the settings say " + window.Current.Fx.Screen);
                bool phosphor = id.StartsWith("p", StringComparison.Ordinal);
                bool crt = id == Screens.Crt || id == Screens.Vhs || id == Screens.Composite || phosphor;
                Check((window.CrtPanel.Visibility == Visibility.Visible) == crt && (window.MaskPanel.Visibility == Visibility.Visible) == (id == Screens.Crt) &&
                    (window.BloomPanel.Visibility == Visibility.Visible) == (id == Screens.Crt || phosphor) && (window.VhsPanel.Visibility == Visibility.Visible) == (id == Screens.Vhs) &&
                    (window.LcdPanel.Visibility == Visibility.Visible) == (id == Screens.Lcd), id + "'s controls aren't the website's");
                if (id == Screens.Off)
                {
                    Check(!window.View.HasScreen, "with the screen off the preview should show the picture itself");
                    continue;
                }
                Rendered r = window.Shown;
                int scale = MainWindow.ScreenScale(r.Width, r.Height, 1, window.View.Zoom);
                Check(scale == Math.Max(3, (int)window.View.Zoom), "the screen effect should be made at the zoom (" + window.View.Zoom + "), not " + scale);
                Check(window.View.ScreenWidth == r.Width * scale && window.View.ScreenHeight == r.Height * scale, id + " was made at " + window.View.ScreenWidth + "×" + window.View.ScreenHeight);
                byte[] want = new FxRenderer(r.Rgb, r.Width, r.Height, r.Paper, r.Alpha, r.Width * scale, r.Height * scale, window.Current.Fx).Rgba();
                Check(window.View.ScreenPixels.SequenceEqual(want), id + "'s preview isn't the engine's screen effect");
            }
            Check(window.Renders == renders, "changing the screen made the picture again (only its screen effect should be)");

            // the preview draws it pixel for pixel: at 150 % as at 100 %
            PickScreen(window, Screens.Crt);
            foreach (double scaling in new[] { 1.0, 1.5 })
            {
                window.ScaleOverride = scaling;
                window.View.ZoomTo(4);
                WaitForScreen(window);
                Rendered r = window.Shown;
                Check(window.View.ScreenWidth == r.Width * 4, "at 4× the screen effect should be made 4×, not " + window.View.ScreenWidth + " across");
                int vw = (int)Math.Round(window.View.ActualWidth * scaling), vh = (int)Math.Round(window.View.ActualHeight * scaling);
                var canvas = new int[vw * vh];
                window.View.Paint(canvas, vw, vh);
                byte[] s = window.View.ScreenPixels;
                int sw = window.View.ScreenWidth, sh = window.View.ScreenHeight;
                // the view's top left pixel of the picture is a screen-effect pixel; find it by the drawn size
                int drawnLeft = (vw - sw) / 2, drawnTop = (vh - sh) / 2;
                int wrong = 0;
                for (int y = Math.Max(0, drawnTop); y < Math.Min(vh, drawnTop + sh); y += 3)
                    for (int x = Math.Max(0, drawnLeft); x < Math.Min(vw, drawnLeft + sw); x += 3)
                    {
                        int p = ((y - drawnTop) * sw + x - drawnLeft) * 4;
                        if (canvas[y * vw + x] != ((s[p] << 16) | (s[p + 1] << 8) | s[p + 2])) wrong++;
                    }
                Check(wrong == 0, wrong + " screen pixels differ from the screen effect at " + scaling * 100 + " %");
            }
            window.ScaleOverride = 1;
            window.View.ZoomToFit();

            // the sliders, in the website's steps; they remake only the screen effect
            renders = window.Renders;
            window.ScanlinesSlider.Value = 37;
            window.CurveSlider.Value = 0;
            window.BloomSlider.Value = 100;
            window.MaskShadow.IsChecked = true;
            WaitForScreen(window);
            FxSettings fx = window.Current.Fx;
            Check(fx.Scanlines == 0.37 && fx.Curve == 0 && fx.Bloom == 1 && fx.Mask == "shadow" && window.ScanlinesValue.Text == "37%", "the CRT's sliders: " + fx.Scanlines + "/" + fx.Curve + "/" + fx.Bloom + "/" + fx.Mask);
            PickScreen(window, Screens.Vhs);
            window.TrackingSlider.Value = 35;
            window.OsdBox.IsChecked = false;
            PickScreen(window, Screens.Lcd);
            window.GridSlider.Value = 100;
            window.GhostSlider.Value = 5;
            WaitForScreen(window);
            Check(fx.VhsTracking == 0.35 && !fx.VhsOsd && fx.Grid == 1 && fx.Ghost == 0.05, "the VHS's and LCD's controls");
            Check(window.Renders == renders, "the screen's sliders made the picture again");
            Rendered last = window.Shown;
            byte[] lcd = new FxRenderer(last.Rgb, last.Width, last.Height, last.Paper, last.Alpha, window.View.ScreenWidth, window.View.ScreenHeight, fx).Rgba();
            Check(window.View.ScreenPixels.SequenceEqual(lcd), "the LCD's sliders aren't in the preview");

            // each machine brings its own screen while one is on; the VHS tape always brings its own
            Pick(window, "nes");
            Check(window.Current.Fx.Screen == Screens.Crt, "the NES should bring its CRT, not " + window.Current.Fx.Screen);
            PickScreen(window, Screens.Off);
            Pick(window, "mac");
            Check(window.Current.Fx.Screen == Screens.Off, "with the screen off, a machine shouldn't turn it on");
            Pick(window, "vhs");
            Check(window.Current.Fx.Screen == Screens.Vhs && window.VhsPanel.Visibility == Visibility.Visible, "the VHS tape should bring its own screen");
            WaitForScreen(window);
            Check(window.View.HasScreen, "the VHS screen isn't in the preview");
            Pick(window, "fax");
            Check(window.Current.Fx.Screen == Screens.Off, "the fax has no screen");
            WaitForScreen(window);
            Check(!window.View.HasScreen, "the fax's preview should be the picture itself");
        }

        // With the box ticked (3× and up, a screen on) the PNG has the screen effect, full colour,
        // named -fx; copies too. Wallpapers keep it from 3× up.
        static void PngsAndWallpapersWithTheScreen()
        {
            MainWindow window = NewMain();
            WaitForPicture(window);
            window.OpenSample("cat-nap");
            WaitForPicture(window);
            Pick(window, "cga-1");
            Check(!window.ExportFxBox.IsEnabled, "with no screen on, the box should be greyed out");
            PickScreen(window, Screens.Composite);
            Check(window.ExportFxBox.IsEnabled && window.ExportFxBox.IsChecked == false, "with a screen on at 4×, the box should be there to tick");
            int renders = window.Renders;
            window.ExportFxBox.IsChecked = true;
            Check(window.Current.ExportFx && window.SizeLine.Text.EndsWith("· full colour"), "the size line with the screen effect: " + window.SizeLine.Text);
            window.Sizes.Children.OfType<RadioButton>().First(b => (int)b.Tag == 2).IsChecked = true;
            Check(!window.ExportFxBox.IsEnabled && window.SizeLine.Text.EndsWith("4-colour palette"), "at 2× the box should be greyed out, the PNG paletted: " + window.SizeLine.Text);
            window.Sizes.Children.OfType<RadioButton>().First(b => (int)b.Tag == 3).IsChecked = true;
            Check(window.Renders == renders && !window.Rendering && !window.ScreenRendering, "the export's box and Size change no picture, yet something was made again");
            window.ShapeTv.IsChecked = true;
            WaitForScreen(window);

            Rendered r = window.Shown;
            string folder = Path.Combine(Work, "screens");
            Fresh(folder);
            string path = Path.Combine(folder, "fx.png");
            FxSettings fx = window.Current.ExportScreen;
            Check(fx != null && fx.Screen == Screens.Composite, "the export's screen effect");
            window.SavePng(path, r, 3, true, fx).Wait(0);
            Pump(() => !window.Saving, 15000);
            byte[] saved = File.ReadAllBytes(path);
            Check(saved.SequenceEqual(Export.PngBytes(r, 3, true, fx)), "the saved PNG isn't the engine's with the screen effect");
            PngParts(saved, out _, out int width, out int height, out _, out int colourType, out _, out _);
            Export.Size(r.Width, r.Height, r.Geometry.ParX, r.Geometry.ParY, 3, true, out int w, out int h);
            Check(width == w && height == h && colourType == 2, "the PNG with the screen effect should be " + w + "×" + h + " full colour, is " + width + "×" + height + " type " + colourType);
            MainWindow.Shared share = window.ToShare(r);
            Check(share.Name == "cat-nap-cga-1-320x200-3x-tv-fx.png" && share.Fx != null, "copied as " + share.Name);
            share.Make(r);
            Check(share.Png.SequenceEqual(saved) && share.Picture.PixelWidth == w, "the copy should be the same PNG");

            // the wallpapers: the website's three and this PC's screens
            Check(window.WallpaperChoices.Take(3).Select(x => x.Id).SequenceEqual(new[] { "phone", "win95", "desktop-1080p" }) && window.WallpaperChoices.Count == 3 + Monitors.All().Count,
                "the wallpapers should be the website's three and one for each screen");
            Check(window.WallpaperButtons.Children.Count == window.WallpaperChoices.Count && window.WallpaperMenu.Items.Count == window.WallpaperChoices.Count, "a button and a menu line for each wallpaper");
            // a screen's wallpaper is named for its size (made as the others are, so not made here)
            Wallpaper screenPaper = window.WallpaperChoices[3];
            Check(window.WallpaperName(r, screenPaper) == "cat-nap-cga-1-" + screenPaper.Width + "x" + screenPaper.Height + "-wallpaper.png", "a screen's wallpaper name: " + window.WallpaperName(r, screenPaper));
            foreach (Wallpaper paper in window.WallpaperChoices.Where(w => w.Id == "phone" || w.Id == "desktop-1080p"))
            {
                string file = Path.Combine(folder, window.WallpaperName(r, paper));
                Check(Path.GetFileName(file) == "cat-nap-cga-1-" + paper.Id + "-wallpaper.png", "the wallpaper's name: " + Path.GetFileName(file));
                string problem = Await(window.SaveWallpaper(file, r, paper));
                Check(problem == null, "the " + paper.Name + " wallpaper wasn't saved: " + problem);
                using (var bytes = new MemoryStream())
                {
                    Export.WriteWallpaper(bytes, r, paper.Width, paper.Height, true, fx);
                    Check(File.ReadAllBytes(file).SequenceEqual(bytes.ToArray()), "the " + paper.Name + " wallpaper isn't the engine's");
                }
                BitmapFrame frame = BitmapDecoder.Create(new MemoryStream(File.ReadAllBytes(file)), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
                Check(frame.PixelWidth == paper.Width && frame.PixelHeight == paper.Height, "the " + paper.Name + " wallpaper came out " + frame.PixelWidth + "×" + frame.PixelHeight);
            }
        }

        // the analog looks in the window: their words, no dithers, and their colours made
        static void AnalogLooksInTheWindow()
        {
            MainWindow window = NewMain();
            WaitForPicture(window);
            foreach (string id in new[] { "ccd-early", "deepfry", "newspaper", "risograph", "fax", "vhs" })
            {
                Pick(window, id);
                Profile p = Profiles.ById(id);
                Check(window.AboutLine.Text == Words.About(p), id + "'s line: " + window.AboutLine.Text);
                bool analog = p.Kind == PaletteKind.Analog;
                Check(window.Dithers.Children.OfType<RadioButton>().All(b => b.IsEnabled != analog || b.Visibility != Visibility.Visible) == true, id + "'s dithers should be " + (analog ? "greyed out" : "there"));
                Check(!analog || window.DitherNote.Text == "This look makes its own colours, so dithering doesn’t apply.", id + "'s dither note: " + window.DitherNote.Text);
                Check(!analog || window.Readout.Text.StartsWith(window.Shown.Used + " colours used ·"), id + "'s readout: " + window.Readout.Text);
            }
            Check(window.SizeLine.Text.EndsWith("· 256-colour palette"), "the VHS tape's size line: " + window.SizeLine.Text);
            Pick(window, "fax");
            Check(window.SizeLine.Text.EndsWith("· 1-bit fax") && window.Shown.Used <= 2, "the fax's size line: " + window.SizeLine.Text);
        }

        // A folder (or several pictures) through the look, into a folder: every file read back,
        // the broken one said, files already there numbered, replaced or skipped as asked.
        static void BatchMakesFiles()
        {
            MainWindow window = NewMain(1, false);
            string folder = Path.Combine(Work, "batch");
            Fresh(folder);
            byte[] png = Encode(new PngBitmapEncoder(), Quarters(40, 30));
            File.WriteAllBytes(Path.Combine(folder, "a.png"), png);
            File.WriteAllBytes(Path.Combine(folder, "b.jpg"), Encode(new JpegBitmapEncoder { QualityLevel = 95 }, Quarters(64, 48)));
            File.WriteAllBytes(Path.Combine(folder, "c.png"), Encode(new PngBitmapEncoder(), Quarters(30, 40)));
            File.WriteAllText(Path.Combine(folder, "broken.png"), "not a picture");
            File.WriteAllText(Path.Combine(folder, "notes.txt"), "not even trying");
            Directory.CreateDirectory(Path.Combine(folder, "inner"));
            File.WriteAllBytes(Path.Combine(folder, "inner", "d.png"), png);

            window.Current.Deres.ProfileId = "nes";
            window.Current.Deres.Dither = DitherIds.Floyd;
            window.Sync();
            BatchWindow batch = window.OpenBatch(new[] { folder });
            Check(batch.Files.Select(Path.GetFileName).SequenceEqual(new[] { "a.png", "b.jpg", "broken.png", "c.png" }), "the folder's own pictures, by name: " + string.Join(", ", batch.Files.Select(Path.GetFileName)));
            Check(batch.Into == Path.Combine(folder, "DeRes") && batch.LookLine.Text.Contains("NES, Floyd") && batch.MakeLine.Text.StartsWith("At 4×"), "the batch's lines: " + batch.LookLine.Text + " / " + batch.MakeLine.Text);
            batch.AtOnce = 2;
            string into = Path.Combine(folder, "DeRes");

            void Make(Clash clash, string outcome)
            {
                batch.Make(clash);
                Pump(() => batch.Running.IsCompleted, 30000);
                Check(batch.Running.IsCompleted, "the batch didn't finish within 30 seconds");
                Check(batch.LastOutcome == outcome, "the batch came out " + batch.LastOutcome + ", not " + outcome + ": " + batch.StatusLine.Text);
            }
            Make(Clash.Number, "3 made, 0 skipped, 1 failed");
            string[] names = { "a-nes-256x240-4x.png", "b-nes-256x240-4x.png", "c-nes-256x240-4x.png" };
            Check(names.All(n => File.Exists(Path.Combine(into, n))) && Directory.GetFiles(into).Length == 3, "made: " + string.Join(", ", Directory.GetFiles(into).Select(Path.GetFileName)));
            Check(batch.OutcomeList.Items.Count == 1 && ((TextBlock)batch.OutcomeList.Items[0]).Text.StartsWith("broken.png: "), "the broken picture should be said");
            // each file is the engine's own, made from its picture
            var engine = new Deres();
            engine.SetSource(Pictures.Load(Path.Combine(folder, "a.png")).Source);
            Rendered r = engine.Render(window.Current.Deres);
            Check(File.ReadAllBytes(Path.Combine(into, names[0])).SequenceEqual(Export.PngBytes(r, 4, false)), "a.png's file isn't the engine's PNG");

            // again: numbered, then skipped, then replaced
            Make(Clash.Number, "3 made, 0 skipped, 1 failed");
            Check(File.Exists(Path.Combine(into, "a-nes-256x240-4x (2).png")) && Directory.GetFiles(into).Length == 6, "numbered: " + string.Join(", ", Directory.GetFiles(into).Select(Path.GetFileName)));
            Make(Clash.Skip, "0 made, 3 skipped, 1 failed");
            File.WriteAllText(Path.Combine(into, names[1]), "old");
            Make(Clash.Replace, "3 made, 0 skipped, 1 failed");
            Check(new FileInfo(Path.Combine(into, names[1])).Length > 100 && Directory.GetFiles(into).Length == 6, "replaced: " + string.Join(", ", Directory.GetFiles(into).Select(Path.GetFileName)));

            // wallpapers with the screen effect, and the machine's own file; two pictures of one
            // name get two files (the second numbered)
            window.Current.Deres.ProfileId = "zx-spectrum";
            window.Current.Fx.Screen = Screens.Crt;
            window.Current.ExportFx = true;
            window.Sync();
            batch.Add(new[] { Path.Combine(folder, "inner", "d.png"), Path.Combine(folder, "a.png") });
            File.WriteAllBytes(Path.Combine(folder, "d.png"), png);
            batch.Add(new[] { Path.Combine(folder, "d.png") });
            Check(batch.Files.Count == 6, "the batch should hold six pictures now, not " + batch.Files.Count);
            string papers = Path.Combine(folder, "papers");
            batch.SetInto(papers);
            batch.WallpaperChoice.IsChecked = true;
            batch.WallpaperBox.SelectedIndex = 1;
            batch.MachineFileBox.IsChecked = true;
            Check(batch.MachineFileBox.Visibility == Visibility.Visible && batch.MakeLine.Text.Contains("1024×768") && batch.MakeLine.Text.Contains("with the CRT screen from 3× up") &&
                !batch.LookLine.Text.Contains("CRT"), "the wallpaper's lines: " + batch.LookLine.Text + " / " + batch.MakeLine.Text);
            Make(Clash.Number, "10 made, 0 skipped, 1 failed");
            // a picture's files are numbered together: d.png's wallpaper and .scr both get (2)
            Check(File.Exists(Path.Combine(papers, "d-zx-spectrum-win95-wallpaper.png")) && File.Exists(Path.Combine(papers, "d-zx-spectrum-win95-wallpaper (2).png")) &&
                File.Exists(Path.Combine(papers, "d-zx-spectrum.scr")) && File.Exists(Path.Combine(papers, "d-zx-spectrum (2).scr")) &&
                File.Exists(Path.Combine(papers, "a-zx-spectrum.scr")), "made: " + string.Join(", ", Directory.GetFiles(papers).Select(Path.GetFileName)));
            engine.SetSource(Pictures.Load(Path.Combine(folder, "a.png")).Source);
            r = engine.Render(window.Current.Deres);
            using (var bytes = new MemoryStream())
            {
                Export.WriteWallpaper(bytes, r, 1024, 768, false, window.Current.ExportScreen);
                Check(File.ReadAllBytes(Path.Combine(papers, "a-zx-spectrum-win95-wallpaper.png")).SequenceEqual(bytes.ToArray()), "a.png's wallpaper isn't the engine's");
            }
            Check(File.ReadAllBytes(Path.Combine(papers, "a-zx-spectrum.scr")).SequenceEqual(r.EmulatorFile.Data), "a.png's .scr isn't the engine's");
            Check(!Directory.EnumerateFiles(papers, "*.tmp", SearchOption.AllDirectories).Any() && !Directory.EnumerateFiles(papers).Any(f => Path.GetFileName(f).StartsWith(".", StringComparison.Ordinal)),
                "a half-written file was left behind");

            // the screen on but not in the files: said
            window.Current.ExportFx = false;
            window.Sync();
            batch.WallpaperBox.SelectedIndex = 0;
            Check(batch.MakeLine.Text.Contains("The CRT screen isn't in the files"), "the screen left out of the files should be said: " + batch.MakeLine.Text);

            // kept see-through parts: the .scr is made on black, as Save for an emulator makes it
            string clear = Path.Combine(folder, "clear");
            Fresh(clear);
            File.WriteAllBytes(Path.Combine(clear, "rukus.png"), SampleBytes("rukus"));
            window.Current.Deres.Background = Backgrounds.Transparent;
            window.Sync();
            batch.ClearButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            batch.Add(new[] { clear });
            batch.SetInto(Path.Combine(clear, "out"));
            Make(Clash.Number, "2 made, 0 skipped, 0 failed");
            DeresSettings onBlack = window.Current.Deres.Clone();
            onBlack.Background = Backgrounds.Black;
            engine.SetSource(Pictures.Load(Path.Combine(clear, "rukus.png")).Source);
            Check(File.ReadAllBytes(Path.Combine(clear, "out", "rukus-zx-spectrum.scr")).SequenceEqual(engine.Render(onBlack).EmulatorFile.Data),
                "with see-through parts kept, the .scr should be made on black");

            // a drop while a batch runs is said, not dropped quietly
            batch.Make(Clash.Number);
            Check(batch.Busy, "the batch should be running");
            var drop = new DataObject();
            drop.SetFileDropList(new System.Collections.Specialized.StringCollection { folder });
            window.OpenData(drop, "nothing");
            Check(window.NoteText.StartsWith("A batch is running"), "a drop during a batch should be said: " + window.NoteText);
            Pump(() => batch.Running.IsCompleted, 30000);
            batch.Close();
            Check(window.BatchShown == null, "the window should let the closed batch go");

            // closing the main window while a batch runs: the pictures under way finish, then it closes
            MainWindow closing = NewMain(1, false);
            BatchWindow late = closing.OpenBatch(new[] { folder });
            late.AtOnce = 1;
            string lateInto = Path.Combine(folder, "closing");
            late.SetInto(lateInto);
            bool closed = false;
            closing.Closed += (s, e) => closed = true;
            late.Make(Clash.Number);
            closing.Close();
            Check(!closed && late.Busy, "the window should wait for the batch before it closes");
            Pump(() => closed, 30000);
            Check(closed && late.LastOutcome == "1 made, 0 skipped, 0 failed", "the window should close once the picture under way is done: " + late.LastOutcome);
            Check(!Directory.EnumerateFiles(lateInto).Any(f => Path.GetFileName(f).StartsWith(".", StringComparison.Ordinal)), "a half-written file was left behind");
        }

        // DeRes the screen: the screens in real pixels, a picture of one, and a box, a screen or all
        // of them as the picture
        static void DeresTheScreen()
        {
            List<Monitors.Monitor> monitors = Monitors.All();
            if (monitors.Count == 0) throw new SkipException("Windows reports no screens here");
            Check(monitors.All(m => m.Bounds.Width > 0 && m.Bounds.Height > 0 && m.Scale >= 1), "a screen with no size or scaling");
            Check(monitors.Select(m => m.Number).SequenceEqual(Enumerable.Range(1, monitors.Count)), "the screens aren't numbered 1, 2, 3");
            Monitors.Monitor main = monitors.FirstOrDefault(m => m.Main) ?? monitors[0];
            BitmapSource part = Monitors.Capture(new Int32Rect(main.Bounds.X, main.Bounds.Y, 64, 48));
            Check(part.PixelWidth == 64 && part.PixelHeight == 48 && part.IsFrozen, "a 64×48 picture of the screen came out " + part.PixelWidth + "×" + part.PixelHeight);
            Int32Rect box = SnipWindow.ToPixels(new Rect(100, 50, 200, 100), new Size(1000, 600), 1500, 900);
            Check(box.X == 150 && box.Y == 75 && box.Width == 300 && box.Height == 150, "at 150 % the box came out " + box);

            MainWindow window = NewMain(1, false);
            SnipWindow[] Two() => new[]
            {
                new SnipWindow(new Monitors.Monitor { Bounds = new Int32Rect(0, 0, 40, 30), Scale = 1, Main = true, Number = 1 }, Quarters(40, 30), false),
                new SnipWindow(new Monitors.Monitor { Bounds = new Int32Rect(40, 10, 20, 20), Scale = 1, Number = 2 }, Quarters(20, 20), false),
            };
            SnipWindow[] snips = Two();
            window.WatchSnips(snips.ToList());
            window.OnSnipChosen(snips[0], SnipWindow.Choice.Box, new Int32Rect(20, 0, 20, 15));
            WaitForPicture(window);
            Check(window.PictureInfo.Text == "screen.png · 20×15" && snips.All(s => s.IsClosed), "a box of the screen: " + window.PictureInfo.Text);
            Check(QuarterColours(window.OpenedPicture.Source) == "G G G G", "the box should be the screen's top right quarter, came out " + QuarterColours(window.OpenedPicture.Source));

            snips = Two();
            window.WatchSnips(snips.ToList());
            window.OnSnipChosen(snips[1], SnipWindow.Choice.Screen, Int32Rect.Empty);
            Pump(() => window.PictureInfo.Text.Contains("20×20"), 5000);
            WaitForPicture(window);
            Check(window.PictureInfo.Text == "screen.png · 20×20", "a click on the second screen: " + window.PictureInfo.Text);

            // every screen: one picture, laid out as the screens are, black where there's none
            snips = Two();
            window.WatchSnips(snips.ToList());
            window.OnSnipChosen(snips[0], SnipWindow.Choice.Everything, Int32Rect.Empty);
            Pump(() => window.PictureInfo.Text.Contains("60×30"), 5000);
            WaitForPicture(window);
            PictureSource all = window.OpenedPicture.Source;
            Check(window.PictureInfo.Text == "screen.png · 60×30" && !all.HasAlpha, "every screen: " + window.PictureInfo.Text);
            int Pixel(int x, int y) => (all.Bgra[(y * 60 + x) * 4 + 2] << 16) | (all.Bgra[(y * 60 + x) * 4 + 1] << 8) | all.Bgra[(y * 60 + x) * 4];
            Check(Pixel(5, 5) == 0xff0000 && Pixel(50, 2) == 0 && Pixel(45, 12) == 0xff0000 && Pixel(55, 25) == 0xffffff, "the screens laid out together came out wrong");

            // cancelled: the picture stays
            snips = Two();
            window.WatchSnips(snips.ToList());
            window.OnSnipChosen(snips[0], SnipWindow.Choice.Cancel, Int32Rect.Empty);
            Pump(() => false, 200);
            Check(window.PictureInfo.Text == "screen.png · 60×30" && !window.Snipping, "a cancelled snip changed the picture");
        }
    }
}
