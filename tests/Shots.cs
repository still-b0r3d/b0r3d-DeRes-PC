using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace B0r3dDeRes.Tests
{
    // b0r3dDeResTests.exe --shots <folder>: draws the main window (the Rukus sample on the Game Boy
    // as it starts, at 100 % and 150 %; a photo on the NES with Hardware and Dither open; Adjust and
    // Export open; Compare with its divider and a custom width; the ZX Spectrum with its tile meter;
    // Rukus kept see-through on the Super Nintendo; the Amstrad's Colours slider; the PlayStation's
    // dithers; the C64's .koa and the share link; nothing opened yet) and About, in light and dark,
    // into PNG files, straight from the controls. Nothing shows on screen and nothing takes focus;
    // menus that pop open, and full screen, aren't in them.
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

        static void Settle(MainWindow window)
        {
            Pump(() => !window.Rendering && !window.Opening, 10000);
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
