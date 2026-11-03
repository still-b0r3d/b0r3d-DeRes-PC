using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using B0r3dDeRes.Engine;

namespace B0r3dDeRes.Tests
{
    // b0r3dDeResTests.exe --shots <folder>: draws the main window (the Rukus sample on the Game Boy
    // as it starts, at 100 % and 150 %; a photo on the NES with Hardware and Dither open; Adjust and
    // Export open; Compare with its divider and a custom width; the ZX Spectrum with its tile meter;
    // Rukus kept see-through on the Super Nintendo; the Amstrad's Colours slider; the PlayStation's
    // dithers; the C64's .koa and the share link; the screens, a CRT close up at 150 %, the analog
    // looks, the export's wallpapers, a batch and the snip; nothing opened yet) and About, in
    // light and dark, into PNG files, straight from the controls. Nothing shows on screen and
    // nothing takes focus; menus that pop open, and full screen, aren't in them.
    static partial class TestMain
    {
        static int TakeShots(string folder)
        {
            Directory.CreateDirectory(folder);
            foreach (Look look in new[] { Look.Light, Look.Dark })
            {
                Theme.Set(look);
                string name = look.ToString().ToLowerInvariant();
                foreach (double scale in new[] { 1.0, 1.5 })
                {
                    MainWindow start = NewMain(scale);
                    WaitForPicture(start);
                    LayOut(start.Root, MainWidth, MainHeight);
                    Pump(() => false, 50);
                    LayOut(start.Root, MainWidth, MainHeight);
                    Shoot(start.Root, MainWidth, MainHeight, scale, Path.Combine(folder, "start-" + name + "-" + (int)(scale * 100) + ".png"));
                }

                // a photo on the NES, Hardware and Dither open
                MainWindow nes = NewMain();
                WaitForPicture(nes);
                nes.OpenSample("cat-nap");
                WaitForPicture(nes);
                Pick(nes, "nes");
                nes.ImageSection.IsExpanded = false;
                nes.HardwareSection.IsExpanded = true;
                nes.DitherSection.IsExpanded = true;
                Settle(nes);
                Shoot(nes.Root, MainWidth, MainHeight, 1, Path.Combine(folder, "machines-" + name + ".png"));

                // the crystal forest on EGA: Adjust and Export open, a custom width
                MainWindow ega = NewMain();
                WaitForPicture(ega);
                ega.OpenSample("forest");
                WaitForPicture(ega);
                Pick(ega, "ega");
                ega.ContrastSlider.Value = 130;
                ega.SaturationSlider.Value = 120;
                ega.HardwareSection.IsExpanded = false;
                ega.AdjustSection.IsExpanded = true;
                ega.ExportSection.IsExpanded = true;
                ega.ImageSection.IsExpanded = false;
                Settle(ega);
                Shoot(ega.Root, MainWidth, MainHeight, 1, Path.Combine(folder, "adjust-export-" + name + ".png"));

                // Compare, the divider part way; and a custom width with the resolution line
                MainWindow compare = NewMain();
                WaitForPicture(compare);
                compare.OpenSample("cat-nap");
                WaitForPicture(compare);
                Pick(compare, "cga-1");
                compare.CustomButton.IsChecked = true;
                compare.TypeWidth("200");
                Settle(compare);
                Commands.Compare.Execute(null, compare);
                compare.View.SetSplit(0.45);
                Settle(compare);
                Shoot(compare.Root, MainWidth, MainHeight, 1, Path.Combine(folder, "compare-custom-" + name + ".png"));

                // stage 2: a cell machine with its tile meter; Keep's checkerboard; the Colours
                // slider; the machines' own dithers; the Export section's machine file and links
                MainWindow cells = NewMain();
                WaitForPicture(cells);
                cells.OpenSample("cat-nap");
                WaitForPicture(cells);
                Pick(cells, "zx-spectrum");
                cells.ImageSection.IsExpanded = false;
                cells.HardwareSection.IsExpanded = true;
                Settle(cells);
                ScrollTo(cells, cells.AboutLine);
                Shoot(cells.Root, MainWidth, MainHeight, 1, Path.Combine(folder, "cells-" + name + ".png"));

                MainWindow keep = NewMain();
                WaitForPicture(keep);
                Pick(keep, "snes");
                keep.BackKeep.IsChecked = true;
                Settle(keep);
                Shoot(keep.Root, MainWidth, MainHeight, 1, Path.Combine(folder, "keep-" + name + ".png"));

                MainWindow colours = NewMain();
                WaitForPicture(colours);
                colours.OpenSample("forest");
                WaitForPicture(colours);
                Pick(colours, "amstrad-cpc-0");
                colours.ColoursSlider.Value = 6;
                colours.ImageSection.IsExpanded = false;
                colours.HardwareSection.IsExpanded = true;
                Settle(colours);
                ScrollTo(colours, colours.AboutLine);
                Shoot(colours.Root, MainWidth, MainHeight, 1, Path.Combine(folder, "colours-" + name + ".png"));

                MainWindow ps1 = NewMain();
                WaitForPicture(ps1);
                ps1.OpenSample("cat-nap");
                WaitForPicture(ps1);
                Pick(ps1, "ps1");
                ps1.ImageSection.IsExpanded = false;
                ps1.DitherSection.IsExpanded = true;
                Settle(ps1);
                Shoot(ps1.Root, MainWidth, MainHeight, 1, Path.Combine(folder, "dither-ps1-" + name + ".png"));

                MainWindow export = NewMain();
                WaitForPicture(export);
                export.OpenSample("forest");
                WaitForPicture(export);
                Pick(export, "c64-mc");
                export.ImageSection.IsExpanded = false;
                export.ExportSection.IsExpanded = true;
                Settle(export);
                ScrollTo(export, export.ResetButton);
                Shoot(export.Root, MainWidth, MainHeight, 1, Path.Combine(folder, "export-" + name + ".png"));

                // stage 3: the screens, the analog looks, the export's wallpapers, the batch and the snip
                foreach (var (machine, sample, screen, shot) in new[]
                {
                    ("nes", "cat-nap", Screens.Crt, "screen-crt"), ("gameboy", "forest", Screens.Lcd, "screen-lcd"), ("vhs", "cat-nap", Screens.Vhs, "screen-vhs"),
                    ("mac", "cat-nap", Screens.P3Amber, "screen-amber"), ("cga-1", "forest", Screens.Composite, "screen-composite"),
                })
                {
                    MainWindow w = NewMain();
                    WaitForPicture(w);
                    w.OpenSample(sample);
                    WaitForPicture(w);
                    Pick(w, machine);
                    PickScreen(w, screen);
                    w.ImageSection.IsExpanded = false;
                    w.ScreenSection.IsExpanded = true;
                    Settle(w);
                    ScrollTo(w, w.ScreenSection);
                    Shoot(w.Root, MainWidth, MainHeight, 1, Path.Combine(folder, shot + "-" + name + ".png"));
                }

                // a CRT close up: every screen pixel its own
                MainWindow close = NewMain(1.5);
                WaitForPicture(close);
                Pick(close, "nes");
                PickScreen(close, Screens.Crt);
                close.View.ZoomTo(8);
                Settle(close);
                Shoot(close.Root, MainWidth, MainHeight, 1.5, Path.Combine(folder, "screen-crt-close-" + name + "-150.png"));

                foreach (var (machine, sample) in new[] { ("ccd-early", "cat-nap"), ("deepfry", "forest"), ("newspaper", "cat-nap"), ("risograph", "forest"), ("fax", "cat-nap") })
                {
                    MainWindow w = NewMain();
                    WaitForPicture(w);
                    w.OpenSample(sample);
                    WaitForPicture(w);
                    Pick(w, machine);
                    w.ImageSection.IsExpanded = false;
                    w.HardwareSection.IsExpanded = true;
                    Settle(w);
                    ScrollTo(w, w.AboutLine);
                    Shoot(w.Root, MainWidth, MainHeight, 1, Path.Combine(folder, "analog-" + machine + "-" + name + ".png"));
                }

                MainWindow papers = NewMain();
                WaitForPicture(papers);
                Pick(papers, "nes");
                PickScreen(papers, Screens.Crt);
                papers.ExportFxBox.IsChecked = true;
                papers.ImageSection.IsExpanded = false;
                papers.ExportSection.IsExpanded = true;
                Settle(papers);
                ScrollTo(papers, papers.SizeLine);
                Shoot(papers.Root, MainWidth, MainHeight, 1, Path.Combine(folder, "export-screen-wallpapers-" + name + ".png"));

                // the batch: a folder of pictures, after a run with one that wouldn't open
                string pictures = Path.Combine(Work, "shots-batch");
                Fresh(pictures);
                foreach (var sample in Pictures.Samples) File.WriteAllBytes(Path.Combine(pictures, sample.File), SampleBytes(sample.Id));
                File.WriteAllText(Path.Combine(pictures, "notes.png"), "not a picture");
                BatchWindow batch = papers.OpenBatch(new[] { pictures });
                batch.AtOnce = 2;
                batch.Make(Clash.Number);
                Pump(() => batch.Running.IsCompleted, 30000);
                Size batchSize = LayOut(batch.Root, 640, 760);
                Shoot(batch.Root, batchSize.Width, batchSize.Height, 1, Path.Combine(folder, "batch-" + name + ".png"));
                batch.Close();

                // DeRes the screen: a box being drawn on a screen (here, a picture of this window)
                var shotOfWindow = new RenderTargetBitmap(1280, 800, 96, 96, PixelFormats.Pbgra32);
                shotOfWindow.Render(papers.Root);
                shotOfWindow.Freeze();
                var snip = new SnipWindow(new Monitors.Monitor { Bounds = new Int32Rect(0, 0, 1280, 800), Scale = 1, Main = true, Number = 1 }, shotOfWindow, true);
                var snipRoot = (FrameworkElement)snip.Content;
                LayOut(snipRoot, 1280, 800);
                snip.ShowBoxForShots(new Rect(560, 90, 520, 470));
                LayOut(snipRoot, 1280, 800);
                Shoot(snipRoot, 1280, 800, 1, Path.Combine(folder, "snip-" + name + ".png"));
                snip.Close();

                // stage 4: the Signal
                foreach (var (machine, sample, mode, shot) in new[]
                {
                    ("nes", "cat-nap", SignalModes.Rf, "signal-nes-rf"), ("nes", "cat-nap", SignalModes.VhsEp, "signal-nes-vhs-ep"),
                    ("cga-composite", "forest", SignalModes.Auto, "signal-cga-composite"), ("genesis", "rukus", SignalModes.Composite, "signal-genesis-comb"),
                    ("vhs", "cat-nap", SignalModes.Auto, "signal-vhs-look"),
                })
                {
                    MainWindow w = NewMain();
                    WaitForPicture(w);
                    w.OpenSample(sample);
                    WaitForPicture(w);
                    Pick(w, machine);
                    PickSignal(w, mode);
                    if (shot == "signal-genesis-comb")
                    {
                        w.SignalAdvanced.IsExpanded = true;
                        w.DecoderComb.IsChecked = true;
                        WaitForScreen(w);
                    }
                    w.ImageSection.IsExpanded = false;
                    w.SignalSection.IsExpanded = true;
                    Settle(w);
                    ScrollTo(w, w.SignalSection);
                    Shoot(w.Root, MainWidth, MainHeight, 1, Path.Combine(folder, shot + "-" + name + ".png"));
                }

                // the signal and a CRT together, close up at 150 %, and the export's line with the signal
                MainWindow both = NewMain(1.5);
                WaitForPicture(both);
                both.OpenSample("cat-nap");
                WaitForPicture(both);
                Pick(both, "nes");
                PickSignal(both, SignalModes.Rf);
                PickScreen(both, Screens.Crt);
                both.View.ZoomTo(6);
                Settle(both);
                Shoot(both.Root, MainWidth, MainHeight, 1.5, Path.Combine(folder, "signal-crt-close-" + name + "-150.png"));
                MainWindow line = NewMain();
                WaitForPicture(line);
                Pick(line, "nes");
                PickSignal(line, SignalModes.VhsSp);
                line.ImageSection.IsExpanded = false;
                line.ExportSection.IsExpanded = true;
                Settle(line);
                ScrollTo(line, line.SizeLine);
                Shoot(line.Root, MainWidth, MainHeight, 1, Path.Combine(folder, "signal-export-" + name + ".png"));

                // nothing opened yet (a picture that won't open)
                MainWindow empty = NewMain(1, false);
                Settle(empty);
                Shoot(empty.Root, MainWidth, MainHeight, 1, Path.Combine(folder, "empty-" + name + ".png"));

                var about = new AboutWindow();
                Size size = LayOut(about.Root, 640);
                Shoot(about.Root, size.Width, size.Height, 1, Path.Combine(folder, "about-" + name + ".png"));
            }
            Theme.Set(Look.Light);
            Console.WriteLine("pictures in " + folder);
            return 0;
        }

        // scrolls the settings so `element` is near the top
        static void ScrollTo(MainWindow window, FrameworkElement element)
        {
            double y = element.TranslatePoint(new Point(0, 0), (UIElement)window.PanelScroll.Content).Y;
            window.PanelScroll.ScrollToVerticalOffset(Math.Max(0, y - 120));
            LayOut(window.Root, MainWidth, MainHeight);
            Pump(() => false, 60);
            LayOut(window.Root, MainWidth, MainHeight);
        }

        // a sample's file, as it is inside the exe
        static byte[] SampleBytes(string id)
        {
            var sample = Pictures.Samples.First(s => s.Id == id);
            using (Stream stream = typeof(Pictures).Assembly.GetManifestResourceStream("B0r3dDeRes.samples." + sample.File))
            using (var bytes = new MemoryStream())
            {
                stream.CopyTo(bytes);
                return bytes.ToArray();
            }
        }

        static void Settle(MainWindow window)
        {
            Pump(() => !window.Rendering && !window.Opening && !window.ScreenRendering, 10000);
            LayOut(window.Root, MainWidth, MainHeight);
            Pump(() => false, 60);
            LayOut(window.Root, MainWidth, MainHeight);
        }

        static void Shoot(FrameworkElement root, double width, double height, double scale, string file)
        {
            var picture = new RenderTargetBitmap((int)Math.Round(width * scale), (int)Math.Round(height * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
            picture.Render(root);
            var png = new PngBitmapEncoder();
            png.Frames.Add(BitmapFrame.Create(picture));
            using (FileStream stream = File.Create(file)) png.Save(stream);
        }
    }
}
