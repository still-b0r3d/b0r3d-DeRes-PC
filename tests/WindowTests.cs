using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using B0r3dDeRes.Engine;
using Geometry = B0r3dDeRes.Engine.Geometry;

namespace B0r3dDeRes.Tests
{
    static partial class TestMain
    {
        // every machine pixel is exactly k × k screen pixels in its colour, at 100 % and 150 %
        static void PreviewIsSharp()
        {
            foreach (double scale in new[] { 1.0, 1.5 })
            {
                MainWindow window = NewMain(scale);
                WaitForPicture(window);
                foreach (string id in new[] { "gameboy", "ega", "apple2-lores" })
                {
                    Pick(window, id);
                    PixelView view = window.View;
                    Rendered r = window.Shown;
                    int vw = (int)Math.Round(view.ActualWidth * scale), vh = (int)Math.Round(view.ActualHeight * scale);
                    Check(view.Fit && view.Zoom >= 1 && view.Zoom == Math.Floor(view.Zoom), id + " at " + scale * 100 + " %: Fit should pick a whole zoom, not " + view.Zoom);
                    Check(view.Zoom == Math.Floor(Math.Min((double)vw / r.Width, (double)vh / r.Height)), id + ": Fit picked " + view.Zoom + " where " +
                        Math.Floor(Math.Min((double)vw / r.Width, (double)vh / r.Height)) + " fits");
                    var screen = new int[vw * vh];
                    view.Paint(screen, vw, vh);
                    int k = (int)view.Zoom, left = (vw - r.Width * k) / 2, top = (vh - r.Height * k) / 2;
                    for (int y = 0; y < r.Height * k; y++)
                        for (int x = 0; x < r.Width * k; x++)
                        {
                            int s = ((y / k) * r.Width + x / k) * 3;
                            int want = (r.Rgb[s] << 16) | (r.Rgb[s + 1] << 8) | r.Rgb[s + 2];
                            int got = screen[(top + y) * vw + left + x];
                            if (got != want) throw new CheckException(id + " at " + scale * 100 + " %: screen pixel (" + (left + x) + ", " + (top + y) + ") isn't its machine pixel's colour");
                        }
                    Check(screen[0] == screen[vw * vh - 1] && screen[0] != 0x123456, "the area round the picture isn't the stage's colour");
                }
            }
        }

        static void ZoomAndMove()
        {
            MainWindow window = NewMain();
            WaitForPicture(window);
            PixelView view = window.View;
            double fit = view.Zoom;
            Check(window.ZoomText.Text == "Fit " + fit + "×", "the zoom says " + window.ZoomText.Text);
            Commands.ZoomIn.Execute(null, window);
            Check(!view.Fit && view.Zoom > fit && view.Zoom == Math.Floor(view.Zoom), "zooming in from " + fit + " went to " + view.Zoom);
            Check(window.ZoomText.Text == view.Zoom + "×", "the zoom says " + window.ZoomText.Text);
            for (int i = 0; i < 6; i++) Commands.ZoomIn.Execute(null, window);
            Check(view.CanMove, "zoomed right in, the picture should be bigger than the view");
            var screen = new int[(int)view.ActualWidth * (int)view.ActualHeight];
            view.Move(-100000, -100000);
            view.Paint(screen, (int)view.ActualWidth, (int)view.ActualHeight);
            Rendered r = window.Shown;
            int last = ((r.Height - 1) * r.Width + r.Width - 1) * 3;
            Check(screen[screen.Length - 1] == ((r.Rgb[last] << 16) | (r.Rgb[last + 1] << 8) | r.Rgb[last + 2]), "moved to the far corner, the corner pixel should be there");
            Commands.ZoomOne.Execute(null, window);
            Check(view.Zoom == 1 && window.ZoomText.Text == "1×", "1 screen pixel a pixel: " + window.ZoomText.Text);
            Commands.ZoomOut.Execute(null, window);
            Check(view.Zoom == 1, "zooming out from 1× on a picture that fits goes no further, not to " + view.Zoom);
            Commands.ZoomFit.Execute(null, window);
            Check(view.Fit && view.Zoom == fit, "Fit should come back to " + fit + ", not " + view.Zoom);

            // a picture bigger than the view at 1× (the receipt printer's long strip of a tall
            // picture) is shrunk to fit
            var tall = new DataObject();
            tall.SetData("PNG", new MemoryStream(Encode(new PngBitmapEncoder(), Quarters(100, 1500))), false);
            window.OpenData(tall, "nothing");
            WaitForPicture(window);
            Pick(window, "receipt");
            Check(window.Shown.Height == Geometry.AutoMax, "the strip should be the longest the printer makes, not " + window.Shown.Height);
            Check(view.Fit && view.Zoom < 1 && window.ZoomText.Text.StartsWith("Fit ", StringComparison.Ordinal) && window.ZoomText.Text.EndsWith("%", StringComparison.Ordinal),
                "the long strip should fit at less than 1×: " + window.ZoomText.Text);
            // and a new picture of the same size keeps the zoom (moving a slider never moves it)
            Pick(window, "gameboy");
            Commands.ZoomIn.Execute(null, window);
            double zoom = view.Zoom;
            window.BrightnessSlider.Value = 20;
            WaitForPicture(window);
            Check(view.Zoom == zoom && !view.Fit, "a slider changed the zoom from " + zoom + " to " + view.Zoom);
        }

        static void CompareAndHold()
        {
            MainWindow window = NewMain();
            WaitForPicture(window);
            PixelView view = window.View;
            Check(!view.ShowingOriginal && window.TagOriginal.Visibility != Visibility.Visible, "the original shows before it's asked for");
            Commands.Compare.Execute(null, window);
            Check(view.Comparing && view.ShowingOriginal && window.CompareButton.IsChecked == true && window.CompareMenu.IsChecked, "Compare didn't turn on");
            Check(window.TagOriginal.Visibility == Visibility.Visible && window.TagDeres.Visibility == Visibility.Visible, "comparing shows both labels");
            view.SetSplit(0.3);
            Check(view.Split == 0.3, "the divider should move");
            Commands.Compare.Execute(null, window);
            Check(!view.Comparing && !view.ShowingOriginal, "Compare didn't turn off");

            // Space held shows the original; let go, the picture (through the window's own key handlers)
            KeyEventArgs Space(RoutedEvent kind) => new KeyEventArgs(Keyboard.PrimaryDevice, new FakeSource(), 0, Key.Space) { RoutedEvent = kind };
            window.RaiseEvent(Space(Keyboard.PreviewKeyDownEvent));
            Check(view.Holding && view.ShowingOriginal && window.TagOriginal.Visibility == Visibility.Visible, "holding Space should show the original");
            window.RaiseEvent(Space(Keyboard.PreviewKeyUpEvent));
            Check(!view.Holding && !view.ShowingOriginal, "letting Space go should show the picture");

            // the original is the photo framed as the picture is: its shape, black bars and all
            window.FitFit.IsChecked = true;
            Pick(window, "cga-1");
            Geometry g = window.Shown.Geometry;
            Check(g.Dst.W < g.Width || g.Dst.H < g.Height, "Fit, with bars should leave bars on Rukus in CGA's shape");
        }

        // what the window makes is what the engine makes with the same settings
        static void ControlsMakeTheSamePicture()
        {
            MainWindow window = NewMain();
            WaitForPicture(window);
            window.OpenSample("forest");
            WaitForPicture(window);
            void Same(string what)
            {
                WaitForPicture(window);
                Rendered shown = window.Shown;
                var engine = new Deres();
                engine.SetSource(Pictures.Sample("forest").Source);
                Rendered direct = engine.Render(window.ShownWith);
                Check(shown.Indices.SequenceEqual(direct.Indices) && shown.Width == direct.Width, what + ": the window's picture isn't the engine's");
            }
            Pick(window, "pico8");
            Same("PICO-8");
            window.Dithers.Children.OfType<RadioButton>().First(d => (string)d.Tag == DitherIds.Atkinson).IsChecked = true;
            Same("Atkinson");
            Check(window.ShownWith.Dither == DitherIds.Atkinson, "the dither didn't change");
            window.FitStretch.IsChecked = true;
            Same("Stretch");
            window.CustomButton.IsChecked = true;
            window.TypeWidth("200");
            Same("a custom width");
            Check(window.Shown.Width == 200 && window.ShownWith.CustomWidth == 200, "the custom width should be 200, not " + window.Shown.Width);
            Check(window.ResolutionInfo.Text.StartsWith("PICO-8: 128×128 pixels. Yours: 200×113, 1.6× its width.", StringComparison.Ordinal), "the resolution line: " + window.ResolutionInfo.Text);
            window.TypeWidth("5");
            WaitForPicture(window);
            Check(window.Shown.Width == Geometry.CustomMin && window.WidthBox.Text == "8", "a width under the least should become 8: " + window.WidthBox.Text);
            window.WidthSlider.Value = 1000;
            WaitForPicture(window);
            Check(window.Shown.Width == 1024, "the slider's end should be the most, 1024, not " + window.Shown.Width);
            window.NativeButton.IsChecked = true;
            window.DetailSlider.Value = 7;
            Same("Detail 35%");
            Check(window.ShownWith.Detail == 0.35 && window.DetailValue.Text == "35%", "Detail should be 0.35 (\"35%\"), not " + window.ShownWith.Detail);
            window.BrightnessSlider.Value = -17;
            window.ContrastSlider.Value = 137;
            window.SaturationSlider.Value = 1;
            window.GammaSlider.Value = 203;
            Same("the adjustments");
            Adjust a = window.ShownWith.Adjust;
            Check(a.Brightness == -0.17 && a.Contrast == 1.37 && a.Saturation == 0.01 && a.Gamma == 2.03, "the sliders give " + a.Brightness + "/" + a.Contrast + "/" + a.Saturation + "/" + a.Gamma);
            Check(window.BrightnessValue.Text == "-17" && window.ContrastValue.Text == "137%" && window.GammaValue.Text == "2.03", "the sliders' values: " + window.BrightnessValue.Text + " " + window.ContrastValue.Text + " " + window.GammaValue.Text);
            window.AdjustReset.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            WaitForPicture(window);
            Check(window.ShownWith.Adjust.IsNeutral && window.BrightnessValue.Text == "0", "Reset should put the adjustments back");

            // a machine starts with its own dither, as on the website
            Pick(window, "mac");
            Check(window.ShownWith.Dither == DitherIds.Atkinson, "the Mac should start with Atkinson, not " + window.ShownWith.Dither);
            Pick(window, "nes");
            Check(window.ShownWith.Dither == DitherIds.Floyd && window.Readout.Text.Contains("· 256×240 · 8:7 pixels · ") && window.Readout.Text.EndsWith(" tiles (+704 over limit)", StringComparison.Ordinal), "the NES: " + window.ShownWith.Dither + ", " + window.Readout.Text);
            Check(window.ShapePanel.Visibility == Visibility.Visible, "the NES's pixels aren't square, so Pixel shape should show");
            window.ShapeTv.IsChecked = true;
            WaitForPicture(window);
            Check(window.SizeLine.Text == "1170×960 px · 55-colour palette", "as on a TV at 4×: " + window.SizeLine.Text);

            // see-through parts on black
            window.OpenSample("rukus");
            WaitForPicture(window);
            window.BackBlack.IsChecked = true;
            WaitForPicture(window);
            Check(window.ShownWith.Background == Backgrounds.Black, "Black didn't take");
        }

        // File → Save PNG, read back by Windows: the preview's pixels at the chosen size, the
        // machine's palette, and nothing else in the file
        static void SaveReadsBack()
        {
            MainWindow window = NewMain();
            WaitForPicture(window);
            window.OpenSample("cat-nap");
            WaitForPicture(window);
            Pick(window, "nes");
            Rendered r = window.Shown;
            string folder = Path.Combine(Work, "saved");
            Fresh(folder);
            foreach (var (m, tv) in new[] { (1, false), (3, false), (2, true) })
            {
                string path = Path.Combine(folder, "photo-" + m + (tv ? "-tv" : "") + ".png");
                window.SavePng(path, r, m, tv).Wait(0);
                Pump(() => !window.Saving, 15000);
                Check(File.Exists(path), "nothing was saved at " + m + "×");
                byte[] bytes = File.ReadAllBytes(path);
                BitmapFrame frame = BitmapDecoder.Create(new MemoryStream(bytes), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
                Export.Size(r.Width, r.Height, 8, 7, m, tv, out int w, out int h);
                Check(frame.PixelWidth == w && frame.PixelHeight == h, "saved at " + frame.PixelWidth + "×" + frame.PixelHeight + ", not " + w + "×" + h);
                Check(frame.Format == PixelFormats.Indexed8 && frame.Palette.Colors.Count == 55, "the PNG should hold the NES's 55 colours, not " + frame.Format);
                var rgb = new FormatConvertedBitmap(frame, PixelFormats.Rgb24, null, 0);
                var px = new byte[w * h * 3];
                rgb.CopyPixels(px, w * 3, 0);
                for (int y = 0; y < h; y += 7)
                    for (int x = 0; x < w; x += 5)
                    {
                        int sx = (int)Math.Min(r.Width - 1, Math.Floor((double)x * r.Width / w)), s = ((y / m) * r.Width + sx) * 3, o = (y * w + x) * 3;
                        if (px[o] != r.Rgb[s] || px[o + 1] != r.Rgb[s + 1] || px[o + 2] != r.Rgb[s + 2]) throw new CheckException("the saved PNG differs from the picture at (" + x + ", " + y + ")");
                    }
                Png(bytes, out string chunks);
                Check(chunks == "IHDR,sRGB,PLTE,IDAT,IEND", "the PNG should hold nothing but the picture: " + chunks);
            }
            Check(Directory.GetFiles(folder).All(f => f.EndsWith(".png", StringComparison.Ordinal)), "a temporary file was left behind: " + string.Join(", ", Directory.GetFiles(folder)));
            Check(window.NoteText.StartsWith("Saved photo-2-tv.png (585×480)", StringComparison.Ordinal), "the bottom line: " + window.NoteText);
        }

        static void Png(byte[] png, out string chunks)
        {
            var kinds = new List<string>();
            for (int o = 8; o < png.Length;)
            {
                int len = (png[o] << 24) | (png[o + 1] << 16) | (png[o + 2] << 8) | png[o + 3];
                string type = System.Text.Encoding.ASCII.GetString(png, o + 4, 4);
                if (!kinds.Contains(type)) kinds.Add(type);
                o += 12 + len;
            }
            chunks = string.Join(",", kinds);
        }

        // Copy and drag: the PNG and the same picture plain, never the cloud clipboard, for drag
        // a file named as the website names it; past 16 megapixels, the biggest Size within
        static void CopyAndDragData()
        {
            MainWindow window = NewMain();
            WaitForPicture(window);
            Rendered r = window.Shown;
            MainWindow.Shared share = window.ToShare(r);
            DataObject data = window.DataToShare(r, share, true);
            Check(share.Multiplier == 4 && share.Width == 640 && share.Height == 576, "the Game Boy at 4× should go as a 640×576 picture");
            Check(data.GetData("PNG") is MemoryStream png && png.Length > 100, "no PNG in what's handed over");
            Check(data.GetData(DataFormats.Bitmap) is BitmapSource plain && plain.PixelWidth == 640 && plain.PixelHeight == 576, "no plain picture (640×576) in what's handed over");
            Check(data.GetData("CanUploadToCloudClipboard") is MemoryStream cloud && BitConverter.ToInt32(cloud.ToArray(), 0) == 0, "it would go to Windows' cloud clipboard");
            string[] files = (string[])data.GetData(DataFormats.FileDrop);
            Check(files.Length == 1 && Path.GetFileName(files[0]) == "rukus-gameboy-160x144-4x.png" && File.Exists(files[0]) && files[0].StartsWith(Exports.DragFolder, StringComparison.Ordinal),
                "the dragged file: " + string.Join(", ", files));
            Exports.ClearDragFolder();
            Check(!Directory.Exists(Exports.DragFolder), "the drag folder should be gone once emptied");

            // 16× web-safe (10240×7680) is more than can be handed over: it goes at 6× (3840×2880),
            // named so, and plain too
            window.OpenSample("cat-nap");
            WaitForPicture(window);
            Pick(window, "web-safe");
            window.Sizes.Children.OfType<RadioButton>().First(b => (int)b.Tag == 16).IsChecked = true;
            MainWindow.Shared big = window.ToShare(window.Shown);
            DataObject bigData = window.DataToShare(window.Shown, big, false);
            Check(big.Multiplier == 6 && big.Width == 3840 && big.Height == 2880 && big.Name == "cat-nap-web-safe-640x480-6x.png", "16× web-safe should go at 6×, not " + big.Multiplier + "× (" + big.Name + ")");
            Check(bigData.GetDataPresent(DataFormats.Bitmap) && bigData.GetDataPresent("PNG"), "the smaller Size should go both ways");
        }

        static void WindowKeepsSettings()
        {
            string data = DataFolder.Path;
            DataFolder.Reset(Path.Combine(Work, "window-settings"));
            try
            {
                MainWindow window = NewMain();
                WaitForPicture(window);
                window.OpenSample("cat-nap");
                WaitForPicture(window);
                Pick(window, "c64-hires");
                window.Dithers.Children.OfType<RadioButton>().First(d => (string)d.Tag == DitherIds.Bayer8).IsChecked = true;
                window.GammaSlider.Value = 77;
                window.Sizes.Children.OfType<RadioButton>().First(b => (int)b.Tag == 8).IsChecked = true;
                Theme.Set(Look.Dark);
                window.SaveSettings();
                Settings back = Settings.Load();
                Check(back.Deres.ProfileId == "c64-hires" && back.Deres.Dither == DitherIds.Bayer8 && back.Deres.Adjust.Gamma == 0.77 && back.Multiplier == 8 && back.Look == Look.Dark,
                    "read back: " + back.ToText());
                foreach (string file in Directory.GetFiles(DataFolder.Path, "*", SearchOption.AllDirectories))
                    Check(!File.ReadAllText(file).Contains("cat-nap"), "the picture's name got into " + Path.GetFileName(file));
                // and a new window starts as it was left
                MainWindow again = NewMain(1, true, Settings.Load());
                WaitForPicture(again);
                Check(again.Shown.Profile.Id == "c64-hires" && again.ShownWith.Dither == DitherIds.Bayer8, "the next start isn't as it was left");
            }
            finally
            {
                Theme.Set(Look.Light);
                DataFolder.Reset(data);
            }
        }

        static void FullScreenAndBack()
        {
            MainWindow window = NewMain();
            WaitForPicture(window);
            object before = window.Content;
            window.SetFullScreen(true);
            Check(window.IsFullScreen && window.Content == window.Stage && window.WindowStyle == WindowStyle.None && window.FullScreenMenu.IsChecked, "full screen should show only the picture");
            Check(window.View.Fit, "full screen should fit the picture to the screen");
            window.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, new FakeSource(), 0, Key.Escape) { RoutedEvent = Keyboard.KeyDownEvent });
            Check(!window.IsFullScreen && window.Content == before && window.StageArea.Children.Contains(window.Stage) && window.WindowStyle == WindowStyle.SingleBorderWindow,
                "Esc should bring the window back as it was");
        }

        sealed class FakeSource : PresentationSource
        {
            public override Visual RootVisual { get; set; }
            public override bool IsDisposed => false;
            protected override CompositionTarget GetCompositionTargetCore() => null;
        }

        // two things on screen at once never share an access key (Alt+letter), and every list's
        // lines each have their own
        static void AccessKeysAreUnique()
        {
            MainWindow window = NewMain();
            WaitForPicture(window);
            string Key(object label)
            {
                string text = label as string ?? (label as TextBlock)?.Text ?? "";
                int at = text.IndexOf('_');
                return at >= 0 && at + 1 < text.Length ? char.ToUpperInvariant(text[at + 1]).ToString() : null;
            }
            void Unique(string where, IEnumerable<string> keys)
            {
                var list = keys.ToList();
                Check(list.All(k => k != null), where + ": something has no access key");
                var twice = list.GroupBy(k => k).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
                Check(twice.Count == 0, where + ": " + string.Join(", ", twice) + " used twice");
            }
            List<MenuItem> top = window.MenuBar.Items.OfType<MenuItem>().ToList();
            // each tab with what it shows: the menu bar and both tabs' names are always there
            foreach (bool ascii in new[] { false, true })
            {
                window.Tabs.SelectedItem = ascii ? window.AsciiTab : window.DeresTab;
                Pump(() => false, 60);
                string tab = ascii ? "the ASCII studio" : "the DeRes tab";
                IEnumerable<HeaderedContentControl> sections = ascii
                    ? new HeaderedContentControl[] { window.AsciiImageSection, window.AsciiModeSection, window.AsciiRampSection, window.AsciiTeletextSection, window.AsciiColourSection, window.AsciiAdjustSection, window.AsciiExportSection }
                    : new HeaderedContentControl[] { window.ImageSection, window.HardwareSection, window.AdjustSection, window.DitherSection, window.ScreenSection, window.ExportSection };
                IEnumerable<ContentControl> buttons = ascii
                    ? new ContentControl[] { window.AsciiOpenButton, window.AsciiSavePng, window.AsciiCopyText, window.AsciiResetButton }
                    : new ContentControl[] { window.OpenButton, window.SaveButton, window.CopyButton, window.SurpriseButton };
                Unique(tab, top.Select(m => Key(m.Header)).Concat(new[] { Key(window.DeresTab.Header), Key(window.AsciiTab.Header) })
                    .Concat(sections.Select(s => Key(s.Header))).Concat(buttons.Select(b => Key(b.Content))));
                // every line of a list that shows has a key of its own
                void Lists(MenuItem item)
                {
                    List<MenuItem> lines = item.Items.OfType<MenuItem>().Where(l => l.Visibility == Visibility.Visible).ToList();
                    if (lines.Count == 0) return;
                    Unique(tab + "'s " + Key(item.Header) + " menu", lines.Select(l => Key(l.Header)));
                    lines.ForEach(Lists);
                }
                top.ForEach(Lists);
            }
        }

        // the width box's right-click menu is b0r3d DeRes's own, so it takes the light or dark look
        static void TextMenusAreThemed()
        {
            MainWindow window = NewMain();
            var about = new AboutWindow();
            LayOut(about.Root, 640);
            foreach (TextBox box in new[] { window.WidthBox, about.Licences })
            {
                ContextMenu menu = box.ContextMenu;
                Check(menu != null && menu.GetType() == typeof(ContextMenu), "a text box has Windows' own right-click menu, which stays light in dark mode");
                var commands = menu.Items.OfType<MenuItem>().Select(m => m.Command).ToList();
                Check(commands.Contains(ApplicationCommands.Copy) && commands.Contains(ApplicationCommands.Paste), "the right-click menu lacks Copy or Paste");
            }
        }

        static void AboutShowsItAll()
        {
            var about = new AboutWindow();
            LayOut(about.Root, 640);
            Check(about.VersionText.Text == "v" + Program.Version + " · Build: " + Program.Build, "the version line: " + about.VersionText.Text);
            Check(NewMain(1, false).VersionRun.Text == "b0r3d DeRes v" + Program.Version + " · Build: " + Program.Build, "the window's corner should show the version and the build apart");
            string licences = about.Licences.Text;
            foreach (string part in new[] { "Commons Clause", "MIT License", "Licensor: b0r3d.org", "Software: b0r3d DeRes", "fdlibm", "Sun Microsystems", "V8 project authors", "ntsc-rs", "Copyright © valadaptive", "Jorge Aparicio", "Rich Felker", "David Schultz", "VileR", "CC BY-SA 4.0" })
                Check(licences.Contains(part), "the licences don't mention " + part);
            Check(!licences.Contains("is missing from this build"), "a licence file is missing from the build");
            Check(about.FolderText.Text.StartsWith(DataFolder.Path), "the settings folder line: " + about.FolderText.Text);
        }
    }
}
