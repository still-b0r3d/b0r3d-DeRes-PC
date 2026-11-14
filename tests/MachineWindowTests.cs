using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using B0r3dDeRes.Engine;

namespace B0r3dDeRes.Tests
{
    // The window's stage-2 controls: Keep, the dithers each machine offers, the Colours slider,
    // the machines' own files, palettes, looks, Surprise me and Reset settings.
    static partial class TestMain
    {
        static RadioButton DitherButton(MainWindow window, string id) => window.Dithers.Children.OfType<RadioButton>().First(b => (string)b.Tag == id);

        // the picture again, made by the engine on its own with the window's settings
        static Rendered Again(MainWindow window, DeresSettings s)
        {
            var d = new Deres();
            d.SetSource(EngineSource(window));
            return d.Render(s);
        }

        static PictureSource EngineSource(MainWindow window) => window.OpenedPicture.Source;

        static void KeepShowsACheckerboard()
        {
            MainWindow window = NewMain();
            WaitForPicture(window);
            Check(window.BackgroundPanel.Visibility == Visibility.Visible, "Ruckus has see-through parts: Transparent areas should show");
            window.BackKeep.IsChecked = true;
            WaitForPicture(window);
            Rendered r = window.Shown;
            Check(window.ShownWith.Background == Backgrounds.Transparent && r.Alpha != null && r.Alpha.Any(a => a == 0), "kept, Ruckus's corners should be see-through");
            Check(window.KeepNote.Visibility == Visibility.Visible && window.KeepNote.Text.Contains(".chr"), "the note under Keep should show");
            Check(window.View.Checker, "the preview should show a checkerboard behind");
            // a see-through pixel shows the checkerboard; one that shows, its colour
            PixelView view = window.View;
            int vw = (int)Math.Round(view.ActualWidth), vh = (int)Math.Round(view.ActualHeight);
            var screen = new int[vw * vh];
            view.Paint(screen, vw, vh);
            int k = (int)view.Zoom, left = (vw - r.Width * k) / 2, top = (vh - r.Height * k) / 2;
            int hidden = Array.FindIndex(r.Alpha, a => a == 0), shown = Array.FindIndex(r.Alpha, a => a != 0);
            int hx = left + hidden % r.Width * k, hy = top + hidden / r.Width * k, sx = left + shown % r.Width * k, sy = top + shown / r.Width * k;
            int checker = screen[hy * vw + hx];
            Check(checker == 0x3a3a3a || checker == 0x262626, "a see-through pixel shows " + checker.ToString("x6") + ", not the checkerboard");
            Check(screen[sy * vw + sx] == ((r.Rgb[shown * 3] << 16) | (r.Rgb[shown * 3 + 1] << 8) | r.Rgb[shown * 3 + 2]), "a pixel that shows should be its colour");
            // beside the picture, the squares: 8 pixels, light at the top left
            int row = vh / 2 / 16 * 16;
            Check(left > 16 && screen[row * vw] == 0x3a3a3a && screen[row * vw + 8] == 0x262626 && screen[(row + 8) * vw] == 0x262626 && screen[(row + 8) * vw + 8] == 0x3a3a3a,
                "the checkerboard's squares should be 8 pixels, light at the top left");
            // and the PNG keeps them see-through: one more palette entry
            PngParts(Export.PngBytes(r, 1, false), out _, out _, out _, out _, out int type, out byte[] palette, out _, out byte[] trns);
            Check(type == 3 && palette.Length == r.Palette.Length + 3 && trns != null && trns.Length == r.Palette.Length / 3 + 1 && trns.Last() == 0, "the PNG should have a see-through entry");
            // white again: no checkerboard
            window.BackWhite.IsChecked = true;
            WaitForPicture(window);
            Check(!window.View.Checker && window.Shown.Alpha == null && window.KeepNote.Visibility != Visibility.Visible, "on white, nothing see-through");
        }

        static void DithersFollowTheMachine()
        {
            MainWindow window = NewMain();
            WaitForPicture(window);
            // the Game Boy: the seven, its camera's and the PlayStation's hidden
            Check(DitherButton(window, DitherIds.GameBoyCamera).Visibility != Visibility.Visible && DitherButton(window, DitherIds.Ps1).Visibility != Visibility.Visible &&
                window.DitherNote.Visibility != Visibility.Visible, "the Game Boy shouldn't offer the machines' own dithers");
            Pick(window, "gameboy-camera");
            Check(DitherButton(window, DitherIds.GameBoyCamera).Visibility == Visibility.Visible && DitherButton(window, DitherIds.GameBoyCamera).IsChecked == true,
                "the Game Boy Camera should start on its own dither, shown");
            Pick(window, "zx-spectrum");
            Check(window.Dithers.Children.OfType<RadioButton>().All(b => !b.IsEnabled && b.IsChecked != true) && window.DitherNote.Visibility == Visibility.Visible &&
                window.DitherNote.Text == "This machine picks its colours cell by cell, so dithering doesn’t apply.", "the Spectrum's dithers should all be greyed out, said why");
            Pick(window, "ps1");
            Check(DitherButton(window, DitherIds.Ps1).IsEnabled && DitherButton(window, DitherIds.Ps1).IsChecked == true && DitherButton(window, DitherIds.None).IsEnabled &&
                !DitherButton(window, DitherIds.Floyd).IsEnabled, "the PlayStation should offer only None and its own");
            DitherButton(window, DitherIds.None).IsChecked = true;
            WaitForPicture(window);
            Check(window.ShownWith.Dither == DitherIds.None, "picking None on the PlayStation");
            Pick(window, "amiga-ham6");
            Check(window.DitherNote.Text == "This look makes its own colours, so dithering doesn’t apply." && window.Readout.Text.Contains(" colours used · 320×200"),
                "HAM: the note, and its readout counts the colours it makes: " + window.Readout.Text);
            Check(!window.Readout.Text.Contains(" of "), "HAM has no list of colours to count against: " + window.Readout.Text);
        }

        static void ColoursSlider()
        {
            MainWindow window = NewMain();
            WaitForPicture(window);
            Check(window.PaletteTools.Visibility != Visibility.Visible, "the Game Boy has its own colours: no Colours slider");
            Pick(window, "snes");
            Check(window.PaletteTools.Visibility == Visibility.Visible && window.ColoursSlider.Maximum == 256 && window.ColoursSlider.Value == 256 && window.ColoursValue.Text == "256 colours",
                "the Super Nintendo's slider should start at its 256");
            window.ColoursSlider.Value = 7;
            WaitForPicture(window);
            Check(window.Current.Deres.CustomCount == 7 && window.Shown.Palette.Length <= 7 * 3 && window.ColoursValue.Text == "7 colours" && window.SizeLine.Text.EndsWith("· 7-colour palette"),
                "7 on the slider should make at most 7 colours: " + window.Shown.Palette.Length / 3 + ", " + window.SizeLine.Text);
            Check(Again(window, window.ShownWith).Palette.SequenceEqual(window.Shown.Palette), "the window's 7 colours should be the engine's");
            Pick(window, "amstrad-cpc-0");
            Check(window.Current.Deres.CustomCount == null && window.ColoursSlider.Maximum == 16 && window.ColoursSlider.Value == 16 && window.SizeLine.Text.EndsWith("· 16 from 27 colours"),
                "picking a machine should start its slider at its own count: " + window.SizeLine.Text);
        }

        static void MachineFiles()
        {
            MainWindow window = NewMain();
            WaitForPicture(window);
            window.OpenSample("cat-nap");
            WaitForPicture(window);
            Check(window.EmulatorButton.Visibility == Visibility.Visible && (string)window.EmulatorButton.Content == "Save .chr (Game Boy tiles)…", "the Game Boy's tiles should be offered");
            Pick(window, "nes");
            Check(window.EmulatorButton.Visibility != Visibility.Visible && window.ExportNote.Visibility == Visibility.Visible && window.ExportNote.Text.StartsWith("No .chr here"),
                "the NES should say why there's no .chr");
            Pick(window, "nes-attr");
            Check((string)window.EmulatorButton.Content == "Save .chr (NES tiles)…", "NES attributes: " + window.EmulatorButton.Content);
            Pick(window, "zx-spectrum");
            Check((string)window.EmulatorButton.Content == "Save .scr (ZX Spectrum)…" && window.EmulatorButton.IsEnabled, "the Spectrum: " + window.EmulatorButton.Content);
            string scr = Path.Combine(Work, "picture.scr");
            Check(Await(window.SaveEmulator(scr, "scr")) == null, "saving the .scr");
            byte[] saved = File.ReadAllBytes(scr);
            Check(saved.Length == 6912 && saved.SequenceEqual(window.Shown.EmulatorFile.Data), "the .scr should be the picture's own 6,912 bytes");
            Check(window.NoteText.StartsWith("Saved picture.scr (6,912 bytes)"), "the bottom line: " + window.NoteText);

            // kept see-through parts: a .scr can't hold them, so it's made as if on black
            window.OpenSample("rukus");
            WaitForPicture(window);
            window.BackKeep.IsChecked = true;
            WaitForPicture(window);
            Check(Await(window.SaveEmulator(scr, "scr")) == null, "saving the .scr");
            DeresSettings onBlack = window.ShownWith.Clone();
            onBlack.Background = Backgrounds.Black;
            Check(File.ReadAllBytes(scr).SequenceEqual(Again(window, onBlack).EmulatorFile.Data), "with Keep, the .scr should be made on black");
            Pick(window, "c64-mc");
            string koa = Path.Combine(Work, "picture.koa");
            Check(Await(window.SaveEmulator(koa, "koa")) == null, "saving the .koa");
            Check(new FileInfo(koa).Length == 10003, "the .koa should be Koala Painter's 10,003 bytes");
        }

        static void PalettesInAndOut()
        {
            MainWindow window = NewMain();
            WaitForPicture(window);
            string gpl = Path.Combine(Work, "test.gpl");
            File.WriteAllText(gpl, "GIMP Palette\nName: test\n#\n  0   0   0\tBlack\n255 128   0\tOrange\n 40 200 255\n");
            window.LoadPalette(gpl);
            WaitForPicture(window);
            Check(window.Current.Deres.ProfileId == "custom" && string.Join(",", window.Current.Deres.CustomPalette) == "#000000,#ff8000,#28c8ff" &&
                Hex(window.Shown.Palette) == "000000ff800028c8ff", "loading a .gpl should make it Custom's palette: " + Hex(window.Shown.Palette));
            Check(window.NoteText == "Loaded palette \"test.gpl\" (3 colours).", "the bottom line: " + window.NoteText);
            Check(window.ColoursPanel.Visibility != Visibility.Visible, "with a loaded palette, the Colours slider goes");
            File.WriteAllText(gpl, "nothing here\n");
            window.LoadPalette(gpl);
            Check(window.NoteText == "No valid colours found in test.gpl.", "an empty palette: " + window.NoteText);

            // the grabber: the picture's own colours, as many as the machine picks (16 for the rest)
            Pick(window, "gameboy");
            string[] grabbed = Await(window.GrabPalette());
            WaitForPicture(window);
            byte[] want = Quantize.Grab(EngineSource(window), 16);
            Check(grabbed != null && grabbed.Length == want.Length / 3 && window.Current.Deres.ProfileId == "custom" && Hex(window.Shown.Palette) == Hex(want),
                "grabbing should make the picture's 16 colours Custom's palette");

            // saved as each kind, read back
            string[] colours = window.Current.Deres.CustomPalette;
            int type = 0;
            foreach (string ext in new[] { ".hex", ".pal", ".gpl", ".png" })
            {
                string file = Path.Combine(Work, "saved" + ext);
                Check(Await(window.SavePalette(file, type++, colours)) == null, "saving " + ext);
                string[] back = ext == ".png" ? Pictures.PaletteColours(file) : PaletteFile.ParseText(File.ReadAllText(file));
                Check(back.SequenceEqual(colours), ext + " doesn't read back");
            }
        }

        static void LooksInAndOut()
        {
            MainWindow window = NewMain();
            WaitForPicture(window);
            Pick(window, "snes");
            window.ColoursSlider.Value = 64;
            window.BackKeep.IsChecked = true;
            window.ContrastSlider.Value = 137;
            WaitForPicture(window);
            Settings before = window.Current.Copy();
            string link = Looks.Link(before);
            string look = Path.Combine(Work, "snes-look.json");
            Check(Await(window.SaveLook(look)) == null, "saving the look");
            Check(File.ReadAllText(look).Contains("\"profileId\": \"snes\"") && window.NoteText == "Saved the look as snes-look.json.", "the look file: " + window.NoteText);

            // everything back to the start, then the share link pasted
            window.ResetSettings();
            WaitForPicture(window);
            Check(window.Current.Deres.ProfileId == "gameboy" && window.Current.Deres.Adjust.IsNeutral && window.NoteText == "Settings reset.", "Reset settings should start over");
            var data = new DataObject();
            data.SetText(link);
            window.Paste(data);
            WaitForPicture(window);
            Check(Looks.Link(window.Current) == link && window.ShownWith.ProfileId == "snes" && window.ShownWith.CustomCount == 64, "the pasted link should bring the look back");
            Check(window.NoteText == "Opened the share link.", "the bottom line: " + window.NoteText);

            // and the look file, dropped on the window
            window.ResetSettings();
            var drop = new DataObject(DataFormats.FileDrop, new[] { look });
            window.OpenData(drop, "nothing");
            WaitForPicture(window);
            Check(Looks.Link(window.Current) == link, "the dropped look file should bring the look back");

            // a link's screen effect and signal are used
            window.UseLook(Looks.FromLink("#s=" + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("{\"p\":\"nes\",\"fx\":[\"crt\"],\"sg\":[\"rf\"]}"))), "the share link");
            Check(window.NoteText == "Opened the share link.", "the bottom line: " + window.NoteText);
            Check(window.Current.Fx.Screen == Screens.Crt && window.CrtPanel.Visibility == Visibility.Visible, "the link's CRT screen should be on, its controls shown");
            Check(window.Current.Signal.Mode == SignalModes.Rf && window.SignalPanel.Visibility == Visibility.Visible, "the link's signal (RF) should be on, its controls shown");
        }

        // The machine that's picked, clicked again, starts over as on the website; a look opened
        // while the width box has the focus keeps its width when the box is left; Load palette
        // reads anything but a .png as palette text.
        static void SmallBehaviours()
        {
            MainWindow window = NewMain();
            WaitForPicture(window);
            Pick(window, "snes");
            window.ColoursSlider.Value = 7;
            DitherButton(window, DitherIds.Atkinson).IsChecked = true;
            WaitForPicture(window);
            RadioButton snes = window.Cards.Children.OfType<RadioButton>().First(c => (string)c.Tag == "snes");
            snes.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            WaitForPicture(window);
            Check(window.Current.Deres.CustomCount == null && window.Current.Deres.Dither == DitherIds.Floyd && window.ShownWith.Dither == DitherIds.Floyd,
                "clicking the Super Nintendo again should put back its 256 colours and Floyd–Steinberg");

            // the width box: focus in it, a look opened, the box left
            window.CustomButton.IsChecked = true;
            window.TypeWidth("123");
            WaitForPicture(window);
            Settings look = window.Current.Copy();
            look.Deres.CustomWidth = 77;
            window.UseLook(Looks.FromFile(Looks.FileText(look)), "a look");
            WaitForPicture(window);
            // what a box with the focus still shows (the window fills it in only when it hasn't the
            // focus, so as not to fight typing), then the box left
            window.WidthBox.Text = "123";
            window.LeaveWidthBox();
            WaitForPicture(window);
            Check(window.Current.Deres.CustomWidth == 77 && window.ShownWith.CustomWidth == 77, "leaving the width box shouldn't undo the look's width: " + window.Current.Deres.CustomWidth + ", the box says " + window.WidthBox.Text);

            // a .txt of colours, as the website reads any file but a .png
            string txt = Path.Combine(Work, "colours.txt");
            File.WriteAllText(txt, "#102030\n405060\n");
            window.LoadPalette(txt);
            WaitForPicture(window);
            Check(string.Join(",", window.Current.Deres.CustomPalette) == "#102030,#405060", "a .txt of colours should load as a palette");
            // a palette file with a number too big for a double reads as the website reads it
            string pal = Path.Combine(Work, "huge.pal");
            File.WriteAllText(pal, "JASC-PAL\n0100\n" + new string('9', 400) + "\n" + new string('9', 400) + " 0 0\n1 2 3\n");
            window.LoadPalette(pal);
            Check(string.Join(",", window.Current.Deres.CustomPalette) == "#000000,#010203", "the huge numbers should read as the website reads them: " + window.NoteText);
        }

        static void SurpriseAndReset()
        {
            MainWindow window = NewMain();
            WaitForPicture(window);
            int screensOn = 0;
            for (int seed = 1; seed <= 12; seed++)
            {
                window.Surprise(new Random(seed));
                DeresSettings d = window.Current.Deres;
                Profile p = Profiles.ById(d.ProfileId);
                Check(p != null && p.Id != Profiles.CustomId && Dither.ById(d.Dither) != null && (!Dither.ById(d.Dither).Native || p.NativeDither == d.Dither), "rolled " + d.ProfileId + " with " + d.Dither);
                Check(d.Adjust.Brightness >= -0.1 && d.Adjust.Brightness <= 0.1 && d.Adjust.Contrast >= 0.9 && d.Adjust.Contrast <= 1.2 && d.Adjust.Saturation >= 0.85 && d.Adjust.Saturation <= 1.25 &&
                    d.CustomCount == null, "rolled adjustments out of the website's range");
                Check(window.NoteText.StartsWith("Rolled: " + p.Name + ", "), "the bottom line: " + window.NoteText);
                // half the time the machine's own screen, else none
                string screen = window.Current.Fx.Screen;
                Check(screen == Screens.Off || screen == p.Screen, "rolled the " + screen + " screen for " + p.Id);
                Check(window.NoteText.EndsWith(screen == Screens.Off ? "no screen effect." : MainWindow.ScreenName(screen) + " screen."), "the bottom line: " + window.NoteText);
                if (screen != Screens.Off) screensOn++;
            }
            Check(screensOn > 0 && screensOn < 12, "twelve rolls should turn a screen on some of the time, not " + screensOn + " times");
            WaitForPicture(window);
            // the button asks first, then resets
            window.ResetButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Check((string)window.ResetButton.Content == "_Reset everything?" && !window.Current.Deres.Adjust.IsNeutral, "the first click should only ask");
            window.ResetButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Check(window.Current.Deres.Adjust.IsNeutral && window.Current.Deres.ProfileId == "gameboy" && (string)window.ResetButton.Content == "_Reset settings", "the second click should reset");
        }

        // waits for a task the window started, letting the window's thread run meanwhile
        static void Await(System.Threading.Tasks.Task task)
        {
            Pump(() => task.IsCompleted, 15000);
            Check(task.IsCompleted, "it didn't finish within 15 seconds");
            task.GetAwaiter().GetResult();
        }

        static T Await<T>(System.Threading.Tasks.Task<T> task)
        {
            Await((System.Threading.Tasks.Task)task);
            return task.Result;
        }
    }
}
