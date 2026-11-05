using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using B0r3dDeRes.Engine;
using B0r3dDeRes.Video;

namespace B0r3dDeRes.Tests
{
    // The ASCII / ANSI studio's tab: its controls make the website's art, its saves are the
    // engine's files, a GIF plays and goes out as the website's GIF and ANSImation, a video goes out
    // as an MP4 with its sound, a GIF and an ANSImation, the modem types, and the settings are kept.
    static partial class TestMain
    {
        // the studio's tab, its art made
        static MainWindow AsciiWindow(string sample = null)
        {
            MainWindow window = NewMain();
            WaitForPicture(window);
            if (sample != null)
            {
                window.OpenSample(sample);
                WaitForPicture(window);
            }
            window.Tabs.SelectedItem = window.AsciiTab;
            WaitForAscii(window);
            return window;
        }

        static void WaitForAscii(MainWindow window)
        {
            Pump(() => !window.Opening && !window.AsciiRendering && window.AsciiShown != null && window.AsciiShown.Settings.Key == window.Current.Ascii.Key, 15000);
            Check(window.AsciiShown != null, "no art was made within 15 seconds");
            LayOut(window.Root, MainWidth, MainHeight);
        }

        static void ClickSegment(UniformGrid buttons, object tag)
        {
            RadioButton b = buttons.Children.OfType<RadioButton>().First(x => Equals(x.Tag, tag));
            b.IsChecked = true;
            b.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        }

        // the art on screen is the engine's of the picture with the settings the window has, and so is the preview's picture
        static void ArtIsTheEngines(MainWindow window, string what)
        {
            WaitForAscii(window);
            AsciiArt art = window.AsciiShown;
            AsciiArt want = Ascii.Generate(window.OpenedPicture.SourceAt(window.OpenedPicture.Animation != null ? window.AsciiGifPlayer.Frame : 0), window.Current.Ascii, window.OpenedPicture.Animation == null);
            Check(ArtSha(art) == ArtSha(want), what + ": the art isn't the engine's");
            Check(window.AsciiView.PicturePixels != null && window.AsciiView.PicturePixels.SequenceEqual(AsciiRender.Rgb(art, 1)), what + ": the preview isn't the art's picture");
            Check(window.AsciiReadout.Text == Words.AsciiReadout(art), what + ": the readout says " + window.AsciiReadout.Text);
        }

        static void AsciiStudioInTheWindow()
        {
            MainWindow window = AsciiWindow();
            // the Rukus sample as it starts: the website's very art of it
            AsciiCase start = WebsiteAscii.Cases.First(c => c.Input == "rukus" && c.Settings.Key == new AsciiSettings().Key);
            Check(ArtSha(window.AsciiShown) == start.Art, "the Rukus sample's art as the studio starts isn't the website's");
            Check(window.AsciiReadout.Text == "80×40 cells · " + start.Colours + " ANSI colours · half-block", "the readout: " + window.AsciiReadout.Text);
            Check(window.AsciiStageNote.Visibility != Visibility.Visible, "the stage says " + window.AsciiStageNote.Text);
            Check(window.AsciiPictureInfo.Text.StartsWith("rukus.png · 256×256"), "the picture line: " + window.AsciiPictureInfo.Text);
            ArtIsTheEngines(window, "as it starts");
            // each mode's button as the website's sets it: PETSCII and Teletext bring the C64's and Teletext's colours and 40 columns
            foreach (string mode in new[] { AsciiModes.Petscii, AsciiModes.Teletext, AsciiModes.Ramp, AsciiModes.HalfBlock })
            {
                // (the ramp and half blocks keep the colours and columns there are: the website's cases have ANSI 16 at 80)
                if (mode == AsciiModes.Ramp)
                {
                    ClickSegment(window.AsciiColourModes, AsciiColours.Ansi16);
                    ClickSegment(window.AsciiColumnPresets, 80);
                }
                ClickSegment(window.AsciiModeButtons, mode);
                WaitForAscii(window);
                AsciiSettings s = window.Current.Ascii;
                AsciiCase c = WebsiteAscii.Cases.FirstOrDefault(x => x.Input == "rukus" && x.Settings.Key == s.Key);
                Check(c != null && ArtSha(window.AsciiShown) == c.Art, mode + ": not the website's art of Rukus (" + s.Key + ")");
                Check(window.AsciiRampSection.Visibility == (mode == AsciiModes.Ramp ? Visibility.Visible : Visibility.Collapsed) &&
                    window.AsciiTeletextSection.Visibility == (mode == AsciiModes.Teletext ? Visibility.Visible : Visibility.Collapsed) &&
                    window.AsciiColumnsPanel.Visibility == (mode == AsciiModes.Petscii ? Visibility.Collapsed : Visibility.Visible) && window.AsciiC64Note.Visibility == (mode == AsciiModes.Petscii ? Visibility.Visible : Visibility.Collapsed),
                    mode + ": the sections that show");
                Check(window.AsciiSaveSeq.Visibility == (mode == AsciiModes.Petscii ? Visibility.Visible : Visibility.Collapsed) && window.AsciiSaveTxt.Visibility == (mode == AsciiModes.Teletext ? Visibility.Visible : Visibility.Collapsed) &&
                    window.AsciiSaveAns.Visibility == (mode == AsciiModes.Petscii || mode == AsciiModes.Teletext ? Visibility.Collapsed : Visibility.Visible), mode + ": the save buttons that show");
            }
            ClickSegment(window.AsciiModeButtons, AsciiModes.Teletext);
            Check(window.Current.Ascii.ColorMode == AsciiColours.Teletext && window.Current.Ascii.Columns == 40, "Teletext's button should bring its colours and 40 columns");
            // every other control, each making the engine's art
            ClickSegment(window.AsciiColourModes, AsciiColours.Ansi16);
            ClickSegment(window.AsciiColumnPresets, 132);
            Check(window.Current.Ascii.Columns == 132, "132 cols");
            ArtIsTheEngines(window, "132 columns");
            window.AsciiColumnsSlider.Value = 62;
            Check(window.Current.Ascii.Columns == 62 && window.AsciiColumnPresets.Children.OfType<RadioButton>().Single(b => b.IsChecked == true).Tag.Equals(0), "the slider at 62 should pick Custom");
            ArtIsTheEngines(window, "62 columns");
            window.AsciiAspectBox.IsChecked = false;
            ArtIsTheEngines(window, "square cells");
            window.AsciiAspectBox.IsChecked = true;
            ClickSegment(window.AsciiModeButtons, AsciiModes.Ramp);
            foreach (string ramp in Ascii.RampIds.Where(r => r != Ascii.CustomRamp))
            {
                ClickSegment(window.AsciiRamps, ramp);
                ArtIsTheEngines(window, "the " + ramp + " ramp");
            }
            window.TypeRamp(" .oO@" + (char)0xD83D);
            Check(window.Current.Ascii.RampPreset == Ascii.CustomRamp, "typing a ramp picks Custom");
            ArtIsTheEngines(window, "a typed ramp");
            // a ramp pasted with line breaks comes in on one line, as the website's box takes it
            var pasted = new DataObject();
            pasted.SetData(DataFormats.UnicodeText, " .o\r\nO@\n");
            var paste = new DataObjectPastingEventArgs(pasted, false, DataFormats.UnicodeText);
            window.AsciiCustomRamp.RaiseEvent(paste);
            Check(paste.DataObject.GetData(DataFormats.UnicodeText) as string == " .o O@", "a ramp pasted with line breaks: " + Json.Quote(paste.DataObject.GetData(DataFormats.UnicodeText) as string ?? "(none)"));
            window.AsciiInvertBox.IsChecked = true;
            ArtIsTheEngines(window, "inverted");
            foreach (string colours in AsciiColours.All)
            {
                ClickSegment(window.AsciiColourModes, colours);
                ArtIsTheEngines(window, colours);
            }
            window.AsciiContrastSlider.Value = 25;
            window.AsciiBrightnessSlider.Value = -15;
            window.AsciiGammaSlider.Value = 13;
            Check(window.Current.Ascii.Contrast == 25 && window.Current.Ascii.Brightness == -15 && window.Current.Ascii.Gamma == 1.3, "the adjustments: " + window.Current.Ascii.Key);
            Check(window.AsciiContrastValue.Text == "+25%" && window.AsciiBrightnessValue.Text == "-15%" && window.AsciiGammaValue.Text == "1.3×", "the adjustments' words");
            ArtIsTheEngines(window, "adjusted");
            ClickSegment(window.AsciiModeButtons, AsciiModes.Teletext);
            window.AsciiHeaderBox.IsChecked = false;
            window.AsciiSeparatedBox.IsChecked = true;
            Check(!window.Current.Ascii.TeletextHeader && window.Current.Ascii.TeletextSeparated, "Teletext's boxes");
            ArtIsTheEngines(window, "Teletext, separated, no header");
            // Reset settings: two clicks, then the studio as it starts (DeRes's settings stay)
            string deres = Looks.FileText(window.Current);
            window.AsciiResetButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Check(window.Current.Ascii.Mode == AsciiModes.Teletext, "one click reset it");
            window.AsciiResetButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Check(window.Current.Ascii.Key == new AsciiSettings().Key && window.Current.AsciiScale == 1 && Looks.FileText(window.Current) == deres, "Reset settings: " + window.Current.Ascii.Key);
            WaitForAscii(window);
            Check(ArtSha(window.AsciiShown) == start.Art, "after Reset, not the website's art as it starts");
            // the menus follow the tab
            Check(window.AsciiAnsMenu.Visibility == Visibility.Visible && window.BatchMenu.Visibility == Visibility.Collapsed && window.CompareMenu.Visibility == Visibility.Collapsed &&
                (string)window.CopyMenu.Header == "_Copy the art as text", "the menus on the studio's tab");
            window.Tabs.SelectedItem = window.DeresTab;
            Pump(() => false, 50);
            Check(window.AsciiAnsMenu.Visibility == Visibility.Collapsed && window.BatchMenu.Visibility == Visibility.Visible && (string)window.CopyMenu.Header == "_Copy picture", "the menus back on DeRes's tab");
            window.Close();
        }

        static void AsciiSavesInTheWindow()
        {
            MainWindow window = AsciiWindow("forest");
            string folder = Path.Combine(Work, "ascii-saves");
            Fresh(folder);
            AsciiArt art = window.AsciiShown;
            // the .ans: the art with a SAUCE record of today, titled with the picture's name
            string ans = Path.Combine(folder, "a.ans");
            Check(Await(window.SaveAsciiBytes(ans, art, MainWindow.AnsFile, (a, b) => b.Length + " bytes")) == null, "the .ans didn't save");
            byte[] saved = File.ReadAllBytes(ans);
            Check(saved.SequenceEqual(Ascii.AnsBytes(art, new SauceInfo { Title = "crystal-forest", Author = "", Group = "b0r3d.org" })), "the .ans isn't the engine's");
            Check(Sauce.Parse(saved).Title == "crystal-forest" && window.NoteText.StartsWith("Saved a.ans"), "the .ans's title, or the line: " + window.NoteText);
            // each copy button and menu item gives what it says: the engine's text, Markdown, ANSI escapes or HTML
            var copies = new Dictionary<string, Func<AsciiArt, string>> { { "text", a => Ascii.PlainText(a) }, { "markdown", a => Ascii.Markdown(a) }, { "ansi", a => Ascii.AnsiText(a) }, { "html", a => Ascii.Html(a) } };
            foreach (FrameworkElement copy in new FrameworkElement[] { window.AsciiCopyText, window.AsciiCopyMarkdown, window.AsciiCopyAnsi, window.AsciiCopyHtml, window.AsciiCopyMarkdownMenu, window.AsciiCopyAnsiMenu, window.AsciiCopyHtmlMenu })
            {
                string kind = copy.Tag as string;
                Check(kind != null && copies.ContainsKey(kind) && MainWindow.CopyText(art, kind, out _) == copies[kind](art), copy.Name + " copies something else (" + kind + ")");
            }
            // the PNG at each size, the art's pictures pixel for pixel, named as the website names it
            foreach (int m in AsciiRender.Scales)
            {
                string png = Path.Combine(folder, "a-" + m + ".png");
                Check(Await(window.SaveAsciiPng(png, art, m)) == null, "the PNG at " + m + "× didn't save");
                byte[] back = DecodeRgb(File.ReadAllBytes(png), out int w, out int h);
                AsciiRender.Size(art.Width, art.Height, m, out int ew, out int eh);
                Check(w == ew && h == eh && back.SequenceEqual(AsciiRender.Rgb(art, m)), "the PNG at " + m + "× isn't the art's picture");
            }
            Check(AsciiRender.PngName("crystal-forest", art, 2) == "crystal-forest-ansi-1280x" + (art.Height * 32) + "-2x.png", "the PNG's name");
            // PETSCII's .seq and .prg, Teletext's .txt
            ClickSegment(window.AsciiModeButtons, AsciiModes.Petscii);
            WaitForAscii(window);
            AsciiArt petscii = window.AsciiShown;
            string seq = Path.Combine(folder, "a.seq"), prg = Path.Combine(folder, "a.prg");
            Await(window.SaveAsciiBytes(seq, petscii, MainWindow.SeqFile, (a, b) => ""));
            Await(window.SaveAsciiBytes(prg, petscii, MainWindow.PrgFile, (a, b) => ""));
            Check(File.ReadAllBytes(seq).SequenceEqual(Ascii.PetsciiSeq(petscii)) && File.ReadAllBytes(prg).SequenceEqual(Ascii.PetsciiPrg(petscii)) && File.ReadAllBytes(prg).Length == 2 + 12 + 91 + 2000, "the .seq or .prg");
            ClickSegment(window.AsciiModeButtons, AsciiModes.Teletext);
            WaitForAscii(window);
            string txt = Path.Combine(folder, "a.txt");
            Await(window.SaveAsciiBytes(txt, window.AsciiShown, MainWindow.TxtFile, (a, b) => ""));
            Check(File.ReadAllBytes(txt).SequenceEqual(new UTF8Encoding(false).GetBytes(Ascii.TeletextPage(window.AsciiShown))) && File.ReadAllBytes(txt)[0] == (byte)'P', "the Teletext .txt (UTF-8, no byte-order mark)");
            // the PNG of Teletext's mosaics, at 4×, the engine's canvas drawing
            string tt = Path.Combine(folder, "tt.png");
            Await(window.SaveAsciiPng(tt, window.AsciiShown, 4));
            Check(DecodeRgb(File.ReadAllBytes(tt), out _, out _).SequenceEqual(AsciiRender.Rgb(window.AsciiShown, 4)), "the Teletext PNG");
            // nothing that moves: no GIF, APNG, ANSImation or MP4 buttons
            Check(window.AsciiSaveGif.Visibility == Visibility.Collapsed && window.AsciiSaveAnsimation.Visibility == Visibility.Collapsed && window.AsciiSaveMp4.Visibility == Visibility.Collapsed, "a still has animation buttons");
            window.Close();
        }

        // an ANSImation as the website's test file has it: its SAUCE record's title and date put to the fixtures' own
        static byte[] Dated(byte[] ans, string title)
        {
            var copy = (byte[])ans.Clone();
            int at = copy.Length - 128;
            for (int i = 0; i < 35; i++) copy[at + 7 + i] = i < title.Length ? (byte)title[i] : (byte)0x20;
            Encoding.ASCII.GetBytes(WebsiteAscii.Date, 0, 8, copy, at + 82);
            return copy;
        }

        static void AsciiGifInTheWindow()
        {
            MainWindow window = AsciiWindow("rukus-gif");
            Check(window.AsciiPlayBar.Visibility == Visibility.Visible && window.AsciiSaveGif.Visibility == Visibility.Visible && window.AsciiSaveAnsimation.Visibility == Visibility.Visible, "a GIF's play bar and buttons");
            Check(((string)window.AsciiSaveAnsimation.Content).Contains("(58 frames)"), "the ANSImation button: " + window.AsciiSaveAnsimation.Content);
            // it plays through the settings, and the frame it pauses on is the art
            window.AsciiGifPlayer.Play();
            Pump(() => window.AsciiGifPlayer.Frame >= 6, 20000);
            Check(window.AsciiGifPlayer.Frame >= 6, "the GIF didn't play");
            window.AsciiGifPlayer.Pause();
            int k = window.AsciiGifPlayer.Frame;
            WaitForAscii(window);
            Check(window.AsciiShownFrame == k && ArtSha(window.AsciiShown) == WebsiteAscii.Animations[0].FrameShas.Split(',')[k], "paused on frame " + (k + 1) + ": not the website's art of it");
            // the other tab opens on the same frame
            window.Tabs.SelectedItem = window.DeresTab;
            WaitForPicture(window);
            Check(window.ShownFrame == k, "DeRes's tab shows frame " + window.ShownFrame + ", not " + k);
            window.Tabs.SelectedItem = window.AsciiTab;
            WaitForAscii(window);
            // every frame: the website's GIF byte for byte, its APNG by its parts, its ANSImation byte for byte (the date aside)
            string folder = Path.Combine(Work, "ascii-gif");
            Fresh(folder);
            AsciiAnimation website = WebsiteAscii.Animations[0];
            AsciiGif gif = website.Gifs.First(g => !g.Apng && g.M == 1), apng = website.Gifs.First(g => g.Apng && g.M == 1);
            Check(window.AsciiFramesName(window.AsciiShown, window.OpenedPicture, MainWindow.AsciiFramesOut.Gif) == "rukus-breaks-out-ansi-640x480-1x.gif", "the GIF's name");
            string gifPath = Path.Combine(folder, "a.gif"), apngPath = Path.Combine(folder, "a.png"), ansPath = Path.Combine(folder, "a.ans");
            Check(Await(window.SaveAsciiFrames(gifPath, MainWindow.AsciiFramesOut.Gif, window.Current.Ascii, window.OpenedPicture)) == null, "the GIF didn't save");
            byte[] gifBytes = File.ReadAllBytes(gifPath);
            Check(gifBytes.Length == gif.Length && Sha(gifBytes) == gif.Sha, "the GIF isn't the website's (" + gifBytes.Length + " bytes, not " + gif.Length + ")");
            Check(window.NoteText.StartsWith("Saved a.gif (640×480, 58 frames)"), "the GIF's line: " + window.NoteText);
            Check(Await(window.SaveAsciiFrames(apngPath, MainWindow.AsciiFramesOut.Apng, window.Current.Ascii, window.OpenedPicture)) == null, "the APNG didn't save");
            string why = SameApng(File.ReadAllBytes(apngPath), apng.Png);
            Check(why == null, "the APNG isn't the website's: " + why);
            Check(Await(window.SaveAsciiFrames(ansPath, MainWindow.AsciiFramesOut.Ansimation, window.Current.Ascii, window.OpenedPicture)) == null, "the ANSImation didn't save");
            byte[] ansBytes = File.ReadAllBytes(ansPath);
            Check(ansBytes.Length == website.AnsimationLength && Sha(Dated(ansBytes, "gif-rukus")) == website.Ansimation && Sauce.Parse(ansBytes).Title == "rukus-breaks-out", "the ANSImation isn't the website's");
            Check(window.NoteText.Contains("58 frames"), "the ANSImation's line: " + window.NoteText);
            // a second save while one's made is refused; Stop leaves no file
            window.Current.AsciiScale = 4;
            var running = window.SaveAsciiFrames(Path.Combine(folder, "stopped.gif"), MainWindow.AsciiFramesOut.Gif, window.Current.Ascii, window.OpenedPicture);
            Check(Await(window.SaveAsciiFrames(Path.Combine(folder, "second.gif"), MainWindow.AsciiFramesOut.Gif, window.Current.Ascii, window.OpenedPicture)) != null, "a second save while one's made");
            window.StopAnimation();
            Pump(() => running.IsCompleted, 20000);
            Check(running.IsCompleted && !File.Exists(Path.Combine(folder, "stopped.gif")) && Directory.GetFiles(folder).All(f => !f.EndsWith(".tmp")), "Stop left a file");
            window.Close();
        }

        static void AsciiVideoInTheWindow()
        {
            if (!Mf.Available) throw new SkipException("Windows' video parts aren't here");
            MainWindow window = NewMain();
            WaitForPicture(window);
            window.OpenFile(Clip("h264-aac.mp4"));
            Pump(() => !window.Opening, 20000);
            WaitForPicture(window);
            window.Tabs.SelectedItem = window.AsciiTab;
            WaitForAscii(window);
            Check(window.AsciiVideoBar.Visibility == Visibility.Visible && window.AsciiSaveMp4.Visibility == Visibility.Visible && window.AsciiVideoRows.Visibility == Visibility.Visible, "a video's timeline and buttons");
            AsciiArt first = window.AsciiShown;
            Check(first.Width == 80 && first.Height == 30, "the art of a 160×120 video: " + first.Width + "×" + first.Height);
            // the frame at 1 s (the white flash) is the art then
            window.AsciiTimeSlider.Value = 1.0;
            Pump(() => window.OpenedPicture.Video.Time >= 9_900_000 && !window.AsciiRendering && window.AsciiShownTime == window.OpenedPicture.Video.Time, 15000);
            WaitForAscii(window);
            Check(window.AsciiShown.Fg.Take(30).All(v => v > 200), "the flash frame's art isn't bright");
            string folder = Path.Combine(Work, "ascii-video");
            Fresh(folder);
            // an MP4 of its art with its sound: every frame, the art's size, the beep at 1 s
            string mp4 = Path.Combine(folder, "a.mp4");
            Check(Await(window.SaveAsciiFrames(mp4, MainWindow.AsciiFramesOut.Mp4, window.Current.Ascii, window.OpenedPicture)) == null, "the MP4 didn't save: " + window.NoteText);
            VideoFile back = VideoFile.Open(mp4);
            List<VideoFrame> frames = AllFrames(back);
            Check(back.Width == 640 && back.Height == 480 && frames.Count == 60 && Math.Abs(back.Fps - 30) < 0.01, "the MP4: " + back.Width + "×" + back.Height + ", " + frames.Count + " frames at " + back.Fps);
            Check(frames.FindIndex(f => Brightness(f) > 150) == 30, "the flash isn't on frame 30 of the MP4");
            Check(back.HasSound && Math.Abs(BeepAt(AllSound(back, out long start), start) - 1) < 0.003, "the MP4's sound isn't in step");
            // a GIF and an ANSImation of its frames
            string gif = Path.Combine(folder, "a.gif"), ans = Path.Combine(folder, "a.ans");
            Check(Await(window.SaveAsciiFrames(gif, MainWindow.AsciiFramesOut.Gif, window.Current.Ascii, window.OpenedPicture)) == null, "the GIF didn't save");
            AnimatedGif opened = GifFrames.Open(File.ReadAllBytes(gif), 2048, AnimationLimits.Pc.Budget);
            Check(opened != null && opened.Frames.Length == 60 && opened.Width == 640 && opened.DelaysMs.Sum() >= 1990 && opened.DelaysMs.Sum() <= 2010, "the GIF of the video: " + (opened == null ? "none" : opened.Frames.Length + " frames, " + opened.DelaysMs.Sum() + " ms"));
            Check(Await(window.SaveAsciiFrames(ans, MainWindow.AsciiFramesOut.Ansimation, window.Current.Ascii, window.OpenedPicture)) == null, "the ANSImation didn't save");
            byte[] bytes = File.ReadAllBytes(ans);
            int homes = 0;
            for (int i = 0; i + 2 < bytes.Length; i++) if (bytes[i] == 0x1b && bytes[i + 1] == (byte)'[' && bytes[i + 2] == (byte)'H') homes++;
            Check(homes == 60 && Sauce.Parse(bytes).TInfo1 == 80 && Sauce.Parse(bytes).TInfo2 == 30, "the ANSImation of the video: " + homes + " frames");
            // a slower rate out: 15 a second
            window.Current.VideoRate = 15;
            string slow = Path.Combine(folder, "slow.ans");
            Await(window.SaveAsciiFrames(slow, MainWindow.AsciiFramesOut.Ansimation, window.Current.Ascii, window.OpenedPicture));
            byte[] slowBytes = File.ReadAllBytes(slow);
            int slowHomes = 0;
            for (int i = 0; i + 2 < slowBytes.Length; i++) if (slowBytes[i] == 0x1b && slowBytes[i + 1] == (byte)'[' && slowBytes[i + 2] == (byte)'H') slowHomes++;
            Check(slowHomes == 30, "at 15 a second, " + slowHomes + " frames");
            window.Close();
        }

        static void AsciiModemInTheWindow()
        {
            MainWindow window = AsciiWindow();
            AsciiModem modem = window.Modem;
            int total = window.AsciiShown.Width * window.AsciiShown.Height + window.AsciiShown.Height - 1;
            Check(modem.Sent == total && !modem.Typing && modem.ProgressText == "100% (" + total + "/" + total + ") · Instant", "the modem at Instant: " + modem.ProgressText);
            // 300 baud: new art is typed out, 30 characters a second
            window.AsciiBaud.SelectedIndex = Array.FindIndex(AsciiModem.Speeds, s => s.Id == "300");
            Check(modem.Typing && modem.Sent == 0 && (string)window.AsciiDialButton.Content == "Hang up", "picking 300 baud should dial");
            // what's been typed is the art's own picture, cell for cell (the first row's, here), the
            // cursor's cell black or its cyan block, and the rest still black
            void TypedSoFar(string when)
            {
                AsciiArt art = window.AsciiShown;
                byte[] whole = AsciiRender.Rgb(art, 1), shown = window.AsciiView.PicturePixels;
                int sent = modem.Sent, stride = art.Width * AsciiRender.CellWidth * 3, cw = AsciiRender.CellWidth * 3;
                for (int c = 0; c < art.Width; c++)
                {
                    bool same = true, black = true, cyan = true;
                    for (int y = 0; y < AsciiRender.CellHeight; y++)
                        for (int x = 0; x < cw; x++)
                        {
                            int o = y * stride + c * cw + x;
                            same &= shown[o] == whole[o];
                            black &= shown[o] == 0;
                            cyan &= shown[o] == (x % 3 == 0 ? 0 : 255);
                        }
                    Check(c < sent ? same : c == sent ? black || cyan : black, when + ": cell " + c + " of the first row, with " + sent + " sent");
                }
                Check(shown.Skip(AsciiRender.CellHeight * stride).All(v => v == 0), when + ": the rows not typed yet should be black");
            }
            modem.StepFor(1000);
            Check(modem.Sent == 30, "after a second at 300 baud, " + modem.Sent + " sent");
            TypedSoFar("after a second at 300 baud");
            Check(modem.ProgressText == "  1% (  30/" + total + ") · 300 baud", "the progress line: " + modem.ProgressText);
            // Hang up stops there; Dial up goes on from there (the cell the cursor was on typed, too)
            window.AsciiDialButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Check(!modem.Typing && modem.Sent == 30, "Hang up");
            window.AsciiDialButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            modem.StepFor(1000);
            Check(modem.Typing && modem.Sent == 60, "Dial up again: " + modem.Sent);
            TypedSoFar("dialled up again");
            // a change of settings types the new art from the start; Instant shows it whole
            ClickSegment(window.AsciiColourModes, AsciiColours.Truecolor);
            WaitForAscii(window);
            Check(modem.Typing && modem.Sent < 30, "new art should be typed from the start: " + modem.Sent);
            window.AsciiBaud.SelectedIndex = 0;
            Check(!modem.Typing && modem.Sent == total && window.AsciiView.PicturePixels.SequenceEqual(AsciiRender.Rgb(window.AsciiShown, 1)), "Instant shows it whole");
            // Redial at Instant goes at 9600
            window.AsciiRedialButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Check(modem.Typing && modem.Speed == "9600" && ((ComboBoxItem)window.AsciiBaud.SelectedItem).Tag.Equals("9600"), "Redial at Instant");
            modem.StepFor(100000);
            Check(!modem.Typing && modem.Sent == total, "typed to the end");
            window.Close();
            // a GIF playing stops the typing, and dialling pauses the GIF
            MainWindow gif = AsciiWindow("rukus-gif");
            gif.AsciiBaud.SelectedIndex = Array.FindIndex(AsciiModem.Speeds, s => s.Id == "2400");
            gif.AsciiGifPlayer.Play();
            Pump(() => gif.AsciiGifPlayer.Frame >= 2, 10000);
            Check(!gif.Modem.Typing, "the modem types while the GIF plays");
            gif.AsciiDialButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Check(gif.Modem.Typing && !gif.AsciiGifPlayer.Playing, "dialling should pause the GIF");
            gif.Close();
        }

        static void AsciiSettingsKept()
        {
            string path = Path.Combine(Work, "ascii-settings.ini");
            var s = new Settings();
            s.Ascii = new AsciiSettings
            {
                Mode = AsciiModes.Ramp, Columns = 62, RampPreset = Ascii.CustomRamp, CustomRamp = " \"\\" + (char)0xe9 + (char)0xD83D + (char)0xDE00 + (char)0xD83D + "x\t=", InvertRamp = true, ColorMode = AsciiColours.Amber,
                Contrast = -35, Brightness = 15, Gamma = 1.7, TwoToOneAspect = false, TeletextSeparated = true, TeletextHeader = false,
            };
            s.AsciiScale = 4;
            File.WriteAllText(path, s.ToText(), new UTF8Encoding(false));
            Settings back = Settings.Load(path);
            Check(back.Ascii.Key == s.Ascii.Key && back.AsciiScale == 4, "kept and read back: " + back.Ascii.Key);
            Check(File.ReadAllText(path).All(c => c < 128), "the settings file should be plain ASCII");
            // odd lines fall back field by field, as the website checks what it kept
            File.WriteAllText(path, "ascii_mode=nope\r\nascii_columns=999\r\nascii_gamma=0.1\r\nascii_custom_ramp=not json\r\nascii_colours=green\r\nascii_size=3\r\n");
            Settings odd = Settings.Load(path);
            Check(odd.Ascii.Mode == AsciiModes.HalfBlock && odd.Ascii.Columns == 200 && odd.Ascii.Gamma == 0.5 && odd.Ascii.CustomRamp == "" && odd.Ascii.ColorMode == AsciiColours.Green && odd.AsciiScale == 1, "odd lines: " + odd.Ascii.Key);
            // the window starts with them
            MainWindow window = NewMain(1, true, back);
            WaitForPicture(window);
            window.Tabs.SelectedItem = window.AsciiTab;
            WaitForAscii(window);
            Check(window.AsciiCustomRamp.Text == s.Ascii.CustomRamp && window.AsciiSizes.Children.OfType<RadioButton>().Single(b => b.IsChecked == true).Tag.Equals(4), "the window shows the kept settings");
            ArtIsTheEngines(window, "kept settings");
            window.Close();
        }
    }
}
