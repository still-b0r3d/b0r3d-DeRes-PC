using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using B0r3dDeRes.Engine;
using B0r3dDeRes.Video;

namespace B0r3dDeRes
{
    // The ASCII / ANSI studio's tab (the website's ui/ascii-controls.ts, ascii-stage.ts and its part
    // of app.ts): the settings in its sections, the art on the right (drawn as its picture, the
    // font's own letters, so the preview is the PNG), the readout, the dial-up modem, an animated
    // GIF's play bar and a video's timeline, the copies, and every file: .ans with SAUCE, an
    // ANSImation, .seq, .prg, Teletext .txt, the PNG, and a GIF's or a video's every frame as a GIF,
    // an APNG or (a video) an MP4 with its sound. The same art as the website's.
    public partial class MainWindow
    {
        internal bool AsciiActive => Tabs.SelectedItem == AsciiTab;

        Player asciiPlayer;
        VideoPlayer asciiVideoPlayer;
        AsciiModem modem;

        // the art on screen, paused (what copies and saves take): made with its settings, from a
        // picture, at its frame and time; and a frame's art while a GIF or video plays
        AsciiArt asciiShown, asciiPlaying;
        LoadedPicture asciiShownFrom;
        int asciiShownFrame, asciiPlayingFrame;
        long asciiShownTime, asciiPlayingTime;
        int asciiRenders;   // for the tests: how many have been shown

        internal AsciiArt AsciiShown => asciiShown;
        internal int AsciiShownFrame => asciiShownFrame;
        internal long AsciiShownTime => asciiShownTime;
        internal AsciiArt AsciiOnScreen => asciiPlaying ?? asciiShown;
        internal int AsciiRenders => asciiRenders;
        internal Player AsciiGifPlayer => asciiPlayer;
        internal VideoPlayer AsciiTimeline => asciiVideoPlayer;
        internal AsciiModem Modem => modem;
        internal bool AsciiPlays => asciiPlayer.Playing || asciiVideoPlayer.Playing;

        static readonly (string Id, string Text)[] asciiModeChoices =
        {
            (AsciiModes.HalfBlock, "Half-block (▀)"), (AsciiModes.Ramp, "Character ramp"), (AsciiModes.Petscii, "PETSCII (C64)"), (AsciiModes.Teletext, "Teletext (Ceefax)"),
        };

        static readonly (int Columns, string Text)[] asciiColumnChoices = { (40, "40 cols (C64/Ceefax)"), (80, "80 cols (BBS)"), (132, "132 cols"), (0, "Custom") };

        static readonly (string Id, string Text)[] asciiRampChoices =
        {
            ("standard", "Standard"), ("detailed", "Detailed"), ("blocks", "Blocks"), ("katakana", "Katakana"), ("binary", "Binary"), (Ascii.CustomRamp, "Custom"),
        };

        static readonly (string Id, string Text)[] asciiColourChoices =
        {
            (AsciiColours.Ansi16, "ANSI 16-Colour"), (AsciiColours.C64, "C64 VIC-II"), (AsciiColours.Teletext, "Teletext 8-Col"), (AsciiColours.Green, "Green CRT"),
            (AsciiColours.Amber, "Amber CRT"), (AsciiColours.Mono, "Mono"), (AsciiColours.Truecolor, "RGB Truecolor"),
        };

        void BuildAscii()
        {
            RadioButton Segment(string group, object tag, string text, RoutedEventHandler onClick, string name = null)
            {
                var button = new RadioButton { Style = (Style)FindResource("Segment"), GroupName = group, Tag = tag, Content = text, Padding = new Thickness(0), Margin = new Thickness(0, 0, 6, 6) };
                AutomationProperties.SetName(button, name ?? text);
                // (a click every time, as the website's buttons: the picked mode clicked again starts over)
                button.Click += onClick;
                return button;
            }
            foreach (var m in asciiModeChoices) AsciiModeButtons.Children.Add(Segment("AsciiMode", m.Id, m.Text, OnAsciiMode));
            foreach (var c in asciiColumnChoices) AsciiColumnPresets.Children.Add(Segment("AsciiColumns", c.Columns, c.Text, OnAsciiColumnPreset));
            foreach (var r in asciiRampChoices) AsciiRamps.Children.Add(Segment("AsciiRamp", r.Id, r.Text, OnAsciiRamp, r.Text + " ramp"));
            foreach (var c in asciiColourChoices) AsciiColourModes.Children.Add(Segment("AsciiColour", c.Id, c.Text, OnAsciiColour));
            foreach (int m in AsciiRender.Scales) AsciiSizes.Children.Add(Segment("AsciiSize", m, m + "×", OnAsciiSize, m + " times"));
            AsciiVideoRates.Children.Add(Segment("AsciiVideoRate", 0, "Own", OnAsciiVideoRate, "The video's own frame rate"));
            foreach (int fps in Settings.VideoRates) AsciiVideoRates.Children.Add(Segment("AsciiVideoRate", fps, fps.ToString(CultureInfo.InvariantCulture), OnAsciiVideoRate, fps + " frames a second"));
            foreach (var b in AsciiModem.Speeds) AsciiBaud.Items.Add(new ComboBoxItem { Content = b.Text, Tag = b.Id });
            AsciiBaud.SelectedIndex = 0;

            AsciiSampleLine.Inlines.Add(new Run("Try a picture: "));
            for (int i = 0; i < Pictures.Samples.Length; i++)
            {
                var sample = Pictures.Samples[i];
                if (sample.File.EndsWith(".gif", StringComparison.Ordinal))
                {
                    AsciiSampleLine.Inlines.Add(new LineBreak());
                    AsciiSampleLine.Inlines.Add(new Run("Try a GIF: "));
                }
                else if (i > 0) AsciiSampleLine.Inlines.Add(new Run(" · "));
                var link = new Hyperlink(new Run(sample.Label)) { Tag = sample.Id };
                link.Click += (s, e) => OpenSample((string)((Hyperlink)s).Tag);
                AsciiSampleLine.Inlines.Add(link);
            }

            asciiPlayer = new Player(AsciiPlayButton, AsciiFrameSlider, AsciiFrameCounter, AsciiPlayBar, PlayBudget, MakeAsciiPlayed, ShowAsciiPlayed, OnAsciiPlayStarted, OnAsciiPlayPaused, Problem, () => picture);
            asciiVideoPlayer = new VideoPlayer(AsciiVideoPlayButton, AsciiStepBackButton, AsciiStepOnButton, AsciiTimeSlider, AsciiVideoTime, AsciiSetStartButton, AsciiSetStopButton, AsciiWholeButton,
                AsciiRangeText, AsciiVideoSoundBox, AsciiVideoBar, AsciiVideoMaker, ShowAsciiVideoPlayed, OnAsciiPlayStarted, OnAsciiVideoPaused, Problem, () =>
                {
                    Sync();
                    SyncAscii();
                });
            DataObject.AddPastingHandler(AsciiCustomRamp, OnRampPasting);
            modem = new AsciiModem(AsciiView, AsciiDialButton, AsciiBaudProgress, () =>
            {
                // the modem never types while a GIF or a video plays
                asciiPlayer.Pause();
                asciiVideoPlayer.Pause();
            });
            AsciiView.SetBackground(Colors.Black);
            AsciiView.ZoomChanged += () => AsciiZoomText.Text = AsciiView.HasPicture ? AsciiView.ZoomText : "";
            AsciiGlyphs.Install();
            Tabs.SelectionChanged += OnTabChanged;
        }

        // ---- the controls, from the settings ----

        internal void SyncAscii()
        {
            quiet = true;
            try
            {
                AsciiSettings s = settings.Ascii;
                AsciiPictureInfo.Text = picture != null ? Words.PictureInfo(picture) : "No picture loaded.";
                foreach (RadioButton b in AsciiModeButtons.Children) b.IsChecked = (string)b.Tag == s.Mode;
                AsciiRampSection.Visibility = s.Mode == AsciiModes.Ramp ? Visibility.Visible : Visibility.Collapsed;
                AsciiTeletextSection.Visibility = s.Mode == AsciiModes.Teletext ? Visibility.Visible : Visibility.Collapsed;
                int columns = (int)s.Columns;
                bool preset = columns == 40 || columns == 80 || columns == 132;
                foreach (RadioButton b in AsciiColumnPresets.Children) b.IsChecked = (int)b.Tag == (preset ? columns : 0);
                AsciiColumnsSlider.Value = s.Columns;
                AsciiColumnsValue.Text = JsNumber.Text(s.Columns) + " cols";
                // PETSCII is always a 40×25 C64 screen
                AsciiColumnsPanel.Visibility = s.Mode != AsciiModes.Petscii ? Visibility.Visible : Visibility.Collapsed;
                AsciiC64Note.Visibility = s.Mode == AsciiModes.Petscii ? Visibility.Visible : Visibility.Collapsed;
                AsciiAspectBox.IsChecked = s.TwoToOneAspect;
                foreach (RadioButton b in AsciiRamps.Children) b.IsChecked = (string)b.Tag == s.RampPreset;
                if (!AsciiCustomRamp.IsKeyboardFocusWithin && !string.IsNullOrEmpty(s.CustomRamp)) AsciiCustomRamp.Text = s.CustomRamp;
                AsciiInvertBox.IsChecked = s.InvertRamp;
                AsciiHeaderBox.IsChecked = s.TeletextHeader;
                AsciiSeparatedBox.IsChecked = s.TeletextSeparated;
                foreach (RadioButton b in AsciiColourModes.Children) b.IsChecked = (string)b.Tag == s.ColorMode;
                AsciiContrastSlider.Value = s.Contrast;
                AsciiBrightnessSlider.Value = s.Brightness;
                AsciiGammaSlider.Value = JsMath.Round(s.Gamma * 10);
                AsciiContrastValue.Text = Words.AsciiPercent(s.Contrast);
                AsciiBrightnessValue.Text = Words.AsciiPercent(s.Brightness);
                AsciiGammaValue.Text = s.Gamma.ToString("0.0", CultureInfo.InvariantCulture) + "×";
                AsciiSaveSeq.Visibility = AsciiSavePrg.Visibility = AsciiSeqMenu.Visibility = AsciiPrgMenu.Visibility = s.Mode == AsciiModes.Petscii ? Visibility.Visible : Visibility.Collapsed;
                AsciiSaveTxt.Visibility = AsciiTxtMenu.Visibility = s.Mode == AsciiModes.Teletext ? Visibility.Visible : Visibility.Collapsed;
                AsciiSaveAns.Visibility = AsciiAnsMenu.Visibility = s.Mode != AsciiModes.Petscii && s.Mode != AsciiModes.Teletext ? Visibility.Visible : Visibility.Collapsed;
                SyncAsciiExport();
                SyncTab();
            }
            finally
            {
                quiet = false;
            }
            CommandManager.InvalidateRequerySuggested();
        }

        // the export's sizes, lines and buttons (the art changed size, or a save started or ended)
        void SyncAsciiExport()
        {
            bool wasQuiet = quiet;
            quiet = true;
            try
            {
                SyncAsciiExportNow();
            }
            finally
            {
                quiet = wasQuiet;
            }
        }

        void SyncAsciiExportNow()
        {
            foreach (RadioButton b in AsciiSizes.Children) b.IsChecked = (int)b.Tag == settings.AsciiScale;
            bool gif = picture?.Animation != null, video = picture?.Video != null, animated = gif || video;
            AsciiSettings s = settings.Ascii;
            bool ansimation = animated && s.Mode != AsciiModes.Petscii && s.Mode != AsciiModes.Teletext;
            AsciiSaveAnsimation.Visibility = AsciiAnsimationMenu.Visibility = ansimation ? Visibility.Visible : Visibility.Collapsed;
            AsciiSaveAnsimation.Content = gif ? "Save ANSImation .ANS (" + picture.Animation.Frames.Length + " frames)…" : "Save ANSImation .ANS (its frames)…";
            AsciiSaveGif.Visibility = AsciiSaveApng.Visibility = animated ? Visibility.Visible : Visibility.Collapsed;
            AsciiSaveMp4.Visibility = AsciiVideoRows.Visibility = video ? Visibility.Visible : Visibility.Collapsed;
            if (video)
            {
                double own = picture.Video.File.Fps;
                foreach (RadioButton b in AsciiVideoRates.Children)
                {
                    int fps = (int)b.Tag;
                    b.Visibility = fps == 0 || fps < own - 0.01 ? Visibility.Visible : Visibility.Collapsed;
                    b.IsChecked = fps == 0 ? !(settings.VideoRate > 0 && settings.VideoRate < own - 0.01) : fps == settings.VideoRate;
                    if (fps == 0) b.Content = "Own (" + Words.FrameRate(own) + ")";
                }
                AsciiKeepSound.IsChecked = settings.VideoSound;
                AsciiKeepSound.IsEnabled = picture.Video.File.HasSound;
                AsciiKeepSound.Content = picture.Video.File.HasSound ? "Keep the sound" : "Keep the sound (it has none)";
            }
            bool idle = animating == null;
            AsciiStopButton.Visibility = idle ? Visibility.Collapsed : Visibility.Visible;
            AsciiArt art = asciiShown;
            if (art == null || picture == null)
            {
                AsciiSizeLine.Text = "";
                AsciiAnimHint.Visibility = Visibility.Collapsed;
                AsciiSaveGif.IsEnabled = AsciiSaveApng.IsEnabled = AsciiSaveMp4.IsEnabled = false;
                SyncTab();
                return;
            }
            // the size line is the PNG's; animations have their own line by their buttons
            AsciiRender.Size(art.Width, art.Height, settings.AsciiScale, out int w, out int h);
            AsciiSizeLine.Text = w + "×" + h + " px";
            int? fit = AsciiAnimationScale(art, settings.AsciiScale);
            var hints = new List<string>();
            if (animated)
            {
                string hint = Words.AsciiAnimationHint(art, settings.AsciiScale, fit, AsciiAnimationNext(art, settings.AsciiScale, fit));
                if (hint.Length > 0) hints.Add(hint);
            }
            bool mp4Fits = false;
            if (video)
            {
                AsciiRender.Size(art.Width, art.Height, AsciiMp4Scale(art), out int mw, out int mh);
                mp4Fits = Mp4Writer.Fits(mw, mh);
                VideoClip clip = picture.Video;
                double fps = settings.VideoFps(clip.File.Fps);
                int frames = VideoExport.FrameCount(clip.From, clip.To, fps);
                hints.Add(mp4Fits
                    ? "MP4: " + mw + "×" + mh + (AsciiMp4Scale(art) < settings.AsciiScale ? " (" + AsciiMp4Scale(art) + "×: bigger is more than an MP4 holds)" : "") + ", " + frames.ToString("N0", CultureInfo.InvariantCulture) +
                      " frames at " + Words.FrameRate(fps) + " a second" + (clip.File.HasSound && settings.VideoSound ? ", with its sound." : ", no sound.")
                    : "Even 1× is more than an MP4 holds (4096×2304): pick fewer columns.");
            }
            AsciiAnimHint.Text = string.Join("\n", hints);
            AsciiAnimHint.Visibility = hints.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            AsciiSaveGif.IsEnabled = AsciiSaveApng.IsEnabled = animated && fit != null && idle;
            AsciiSaveMp4.IsEnabled = video && mp4Fits && idle;
            AsciiSaveAnsimation.IsEnabled = idle;
            SyncTab();
        }

        // the biggest size no bigger than `chosen` an animation of the art's pictures can be made at (the PC's limits); null when none
        static int? AsciiAnimationScale(AsciiArt art, int chosen)
        {
            int? best = null;
            foreach (int m in AsciiRender.Scales)
            {
                if (m > chosen) continue;
                AsciiRender.Size(art.Width, art.Height, m, out int w, out int h);
                if (Animation.Limit(w, h, 0, AnimationLimits.Pc) == null) best = m;
            }
            return best;
        }

        // the next size up that doesn't fit (the smallest, when none does); null when the chosen one fits
        static int? AsciiAnimationNext(AsciiArt art, int chosen, int? fit)
        {
            foreach (int m in AsciiRender.Scales) if (m <= chosen && (fit == null || m > fit)) return m;
            return null;
        }

        // the size an MP4 of the art goes out at: the chosen one, or the biggest an MP4 holds
        int AsciiMp4Scale(AsciiArt art)
        {
            int best = 1;
            foreach (int m in AsciiRender.Scales)
            {
                if (m > settings.AsciiScale) continue;
                AsciiRender.Size(art.Width, art.Height, m, out int w, out int h);
                if (Mp4Writer.Fits(w, h)) best = m;
            }
            return best;
        }

        // the menus follow the tab: each tab's own lines show, and the shared ones act on it
        void SyncTab()
        {
            bool ascii = AsciiActive;
            Visibility DeresOnly = ascii ? Visibility.Collapsed : Visibility.Visible, AsciiOnly = ascii ? Visibility.Visible : Visibility.Collapsed;
            foreach (MenuItem m in new[] { BatchMenu, OpenLookMenu, OpenPaletteMenu, SaveFramesMenu, WallpaperMenu, EmulatorMenu, SaveLookMenu, SavePaletteMenu, CopyLinkMenu, GrabMenu, SurpriseMenu, CompareMenu })
                m.Visibility = DeresOnly;
            if (!ascii)
            {
                SaveFramesMenu.Visibility = picture?.Video != null ? Visibility.Visible : Visibility.Collapsed;
                foreach (MenuItem m in new[] { AsciiAnsMenu, AsciiAnsimationMenu, AsciiSeqMenu, AsciiPrgMenu, AsciiTxtMenu }) m.Visibility = Visibility.Collapsed;
            }
            foreach (MenuItem m in new[] { AsciiCopyMarkdownMenu, AsciiCopyAnsiMenu, AsciiCopyHtmlMenu, ScanlinesMenu }) m.Visibility = AsciiOnly;
            DeresEditLine.Visibility = DeresOnly;
            CopyMenu.Header = ascii ? "_Copy the art as text" : "_Copy picture";
            if (ascii)
            {
                bool animated = picture?.Animation != null || picture?.Video != null;
                SaveGifMenu.Visibility = SaveApngMenu.Visibility = animated ? Visibility.Visible : Visibility.Collapsed;
                SaveGifMenu.IsEnabled = SaveApngMenu.IsEnabled = AsciiSaveGif.IsEnabled;
                SaveMp4Menu.Visibility = picture?.Video != null ? Visibility.Visible : Visibility.Collapsed;
                SaveMp4Menu.IsEnabled = AsciiSaveMp4.IsEnabled;
            }
            else
            {
                SaveGifMenu.Visibility = SaveApngMenu.Visibility = Visibility.Visible;
                SaveMp4Menu.Visibility = picture?.Video != null ? Visibility.Visible : Visibility.Collapsed;
            }
            // the EXIF tools' tabs have lines of their own, and none of these
            SyncExifMenus();
        }

        // ---- the controls' events ----

        void AsciiChanged()
        {
            SyncAscii();
            RenderAsciiSoon();
            saveTimer.Stop();
            saveTimer.Start();
        }

        void OnAsciiMode(object sender, RoutedEventArgs e)
        {
            if (Quiet) return;
            string id = (string)((RadioButton)sender).Tag;
            AsciiSettings s = settings.Ascii;
            // PETSCII and Teletext bring their own colours and the 40 columns
            s.Mode = id;
            if (id == AsciiModes.Petscii)
            {
                s.ColorMode = AsciiColours.C64;
                s.Columns = 40;
            }
            else if (id == AsciiModes.Teletext)
            {
                s.ColorMode = AsciiColours.Teletext;
                s.Columns = 40;
            }
            AsciiChanged();
        }

        void OnAsciiColumnPreset(object sender, RoutedEventArgs e)
        {
            if (Quiet) return;
            int columns = (int)((RadioButton)sender).Tag;
            if (columns > 0) settings.Ascii.Columns = columns;
            AsciiChanged();
        }

        void OnAsciiColumns(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (Quiet) return;
            settings.Ascii.Columns = JsMath.Round(AsciiColumnsSlider.Value);
            AsciiChanged();
        }

        void OnAsciiAspect(object sender, RoutedEventArgs e)
        {
            if (Quiet) return;
            settings.Ascii.TwoToOneAspect = AsciiAspectBox.IsChecked == true;
            AsciiChanged();
        }

        void OnAsciiRamp(object sender, RoutedEventArgs e)
        {
            if (Quiet) return;
            settings.Ascii.RampPreset = (string)((RadioButton)sender).Tag;
            AsciiChanged();
        }

        void OnAsciiCustomRamp(object sender, TextChangedEventArgs e)
        {
            if (Quiet || !AsciiCustomRamp.IsKeyboardFocusWithin) return;
            settings.Ascii.CustomRamp = AsciiSettings.CutRamp(AsciiCustomRamp.Text);
            settings.Ascii.RampPreset = Ascii.CustomRamp;
            AsciiChanged();
        }

        // Text pasted into the ramp's box, as the website's one-line box takes it: its line breaks
        // spaces, those at its end dropped (Windows' one-line box would keep only its first line).
        void OnRampPasting(object sender, DataObjectPastingEventArgs e)
        {
            if (!e.SourceDataObject.GetDataPresent(DataFormats.UnicodeText, true) || !(e.SourceDataObject.GetData(DataFormats.UnicodeText, true) is string text)) return;
            string line = AsciiSettings.OneLine(text);
            if (line == text) return;
            var data = new DataObject();
            data.SetData(DataFormats.UnicodeText, line);
            e.DataObject = data;
        }

        // for the tests: a ramp typed into the box
        internal void TypeRamp(string text)
        {
            settings.Ascii.CustomRamp = AsciiSettings.CutRamp(text);
            settings.Ascii.RampPreset = Ascii.CustomRamp;
            quiet = true;
            AsciiCustomRamp.Text = text;
            quiet = false;
            AsciiChanged();
        }

        void OnAsciiInvert(object sender, RoutedEventArgs e)
        {
            if (Quiet) return;
            settings.Ascii.InvertRamp = AsciiInvertBox.IsChecked == true;
            AsciiChanged();
        }

        void OnAsciiTeletext(object sender, RoutedEventArgs e)
        {
            if (Quiet) return;
            settings.Ascii.TeletextHeader = AsciiHeaderBox.IsChecked == true;
            settings.Ascii.TeletextSeparated = AsciiSeparatedBox.IsChecked == true;
            AsciiChanged();
        }

        void OnAsciiColour(object sender, RoutedEventArgs e)
        {
            if (Quiet) return;
            settings.Ascii.ColorMode = (string)((RadioButton)sender).Tag;
            AsciiChanged();
        }

        void OnAsciiAdjust(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (Quiet) return;
            // the website's steps: 5 points of contrast and brightness, a tenth of gamma
            AsciiSettings s = settings.Ascii;
            if (sender == AsciiContrastSlider) s.Contrast = JsMath.Round(AsciiContrastSlider.Value);
            else if (sender == AsciiBrightnessSlider) s.Brightness = JsMath.Round(AsciiBrightnessSlider.Value);
            else if (sender == AsciiGammaSlider) s.Gamma = JsMath.Round(AsciiGammaSlider.Value) / 10;
            AsciiChanged();
        }

        void OnAsciiSize(object sender, RoutedEventArgs e)
        {
            if (Quiet) return;
            settings.AsciiScale = (int)((RadioButton)sender).Tag;
            SyncAsciiExport();
            saveTimer.Stop();
            saveTimer.Start();
        }

        void OnAsciiVideoRate(object sender, RoutedEventArgs e)
        {
            if (Quiet) return;
            settings.VideoRate = (int)((RadioButton)sender).Tag;
            Kept();
            SyncAsciiExport();
        }

        void OnAsciiKeepSound(object sender, RoutedEventArgs e)
        {
            if (Quiet) return;
            settings.VideoSound = AsciiKeepSound.IsChecked == true;
            Kept();
            SyncAsciiExport();
        }

        DispatcherTimerHolder asciiResetTimer;

        // two clicks, as on the website: the first asks, the second (within 4 seconds) resets
        void OnAsciiResetButton(object sender, RoutedEventArgs e)
        {
            if (asciiResetTimer == null) asciiResetTimer = new DispatcherTimerHolder(TimeSpan.FromSeconds(4), CalmAsciiReset);
            if (!asciiResetTimer.Running)
            {
                AsciiResetButton.Content = "Reset everythin_g?";
                AsciiResetButton.SetResourceReference(ForegroundProperty, "Bad");
                asciiResetTimer.Start();
                return;
            }
            CalmAsciiReset();
            ResetAscii();
        }

        void CalmAsciiReset()
        {
            asciiResetTimer?.Stop();
            AsciiResetButton.Content = "Reset settin_gs";
            AsciiResetButton.ClearValue(ForegroundProperty);
        }

        // every one of the studio's settings back to how it starts, a video's frame rate and Keep the
        // sound too (shared with DeRes, whose rows follow when its tab shows); the picture stays
        internal void ResetAscii()
        {
            Settings fresh = Settings.Defaults();
            settings.Ascii = fresh.Ascii;
            settings.AsciiScale = fresh.AsciiScale;
            settings.VideoRate = fresh.VideoRate;
            settings.VideoSound = fresh.VideoSound;
            quiet = true;
            AsciiCustomRamp.Text = "";
            quiet = false;
            AsciiChanged();
            saveTimer.Stop();
            SaveSettings();
            Flash("Settings reset.", "Dim");
        }

        void OnAsciiScanlines(object sender, RoutedEventArgs e) => SetAsciiScanlines(AsciiScanlinesButton.IsChecked == true);

        void OnScanlinesMenu(object sender, RoutedEventArgs e) => SetAsciiScanlines(AsciiScanlines.Visibility != Visibility.Visible);

        internal void SetAsciiScanlines(bool on)
        {
            AsciiScanlines.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            AsciiScanlinesButton.IsChecked = on;
            ScanlinesMenu.IsChecked = on;
        }

        void OnAsciiBaud(object sender, SelectionChangedEventArgs e)
        {
            e.Handled = true;   // (not the tabs' change)
            if (modem == null || !(AsciiBaud.SelectedItem is ComboBoxItem item)) return;
            modem.SetSpeed((string)item.Tag);
        }

        void OnAsciiDial(object sender, RoutedEventArgs e)
        {
            modem.ToggleDial();
            ShowBaud();
        }

        void OnAsciiRedial(object sender, RoutedEventArgs e)
        {
            modem.Redial();
            ShowBaud();
        }

        // the speed box follows the modem (dialling at "Instant" goes at 9600)
        void ShowBaud()
        {
            int index = Array.FindIndex(AsciiModem.Speeds, b => b.Id == modem.Speed);
            if (index >= 0 && AsciiBaud.SelectedIndex != index) AsciiBaud.SelectedIndex = index;
        }

        // ---- switching tabs ----

        void OnTabChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.OriginalSource != Tabs || !ready) return;
            if (ExifActive)
            {
                ExifTabShown();
                Sync();
                SyncAscii();
                return;
            }
            bool ascii = AsciiActive;
            // whatever plays on the tab left stops where it is: that frame is the picture on the other
            if (ascii)
            {
                // (Pause also stops a video that's to play on once a move lands)
                if (player.Playing) player.Pause();
                videoPlayer.Pause();
                if (fullScreen) SetFullScreen(false);
                asciiPlayer.SetFrame(frame);
                if (picture?.Video != null) asciiVideoPlayer.SetVideo(picture.Video);
                if (!AsciiShows(picture, frame, frameTime)) RenderAsciiSoon();
                else ShowAsciiStage();
            }
            else
            {
                if (asciiPlayer.Playing) asciiPlayer.Pause();
                asciiVideoPlayer.Pause();
                modem.HangUp();
                if (fullScreen) SetFullScreen(false);
                player.SetFrame(frame);
                if (picture?.Video != null) videoPlayer.SetVideo(picture.Video);
                if (picture != null && (shownFrom != picture || shownFrame != frame || shownTime != frameTime)) RenderSoon();
            }
            Sync();
            SyncAscii();
        }

        // whether the art on screen is the picture's at this frame with the settings as they are
        bool AsciiShows(LoadedPicture pic, int at, long time) =>
            asciiShown != null && asciiShownFrom == pic && asciiShownFrame == at && asciiShownTime == time && asciiShown.Settings.Key == settings.Ascii.Key;

        // ---- making the art ----

        AsciiSettings asciiWanted;
        LoadedPicture asciiWantedPicture;
        int asciiWantedFrame;
        long asciiWantedTime;
        bool asciiWantedInstant, asciiRendering;
        string asciiPlayKey = "";

        internal bool AsciiRendering => asciiRendering || asciiWanted != null;

        // The art of the paused frame (or the picture) with the settings as they are, off the
        // window's thread; whatever the settings are when it's done is made next. `instant`: shown
        // whole even with a modem speed picked (a frame picked on the play bar isn't typed out).
        void RenderAsciiSoon(bool instant = false)
        {
            if (picture == null)
            {
                ShowAsciiStage("Choose a photo to start.");
                return;
            }
            // frames played with older settings are no good
            if (settings.Ascii.Key != asciiPlayKey)
            {
                asciiPlayKey = settings.Ascii.Key;
                asciiPlayer.Invalidate();
            }
            if (asciiPlayer.Playing) return;
            if (asciiVideoPlayer.Playing)
            {
                asciiVideoPlayer.Invalidate();
                return;
            }
            asciiWanted = settings.Ascii.Clone();
            asciiWantedPicture = picture;
            asciiWantedFrame = frame;
            asciiWantedTime = frameTime;
            asciiWantedInstant = instant;
            if (!asciiRendering) AsciiLoop();
        }

        async void AsciiLoop()
        {
            asciiRendering = true;
            try
            {
                while (asciiWanted != null)
                {
                    AsciiSettings s = asciiWanted;
                    LoadedPicture pic = asciiWantedPicture;
                    int at = asciiWantedFrame;
                    long time = asciiWantedTime;
                    bool instant = asciiWantedInstant;
                    asciiWanted = null;
                    // (a video's paused frame, taken now: moving on changes it)
                    PictureSource source = pic.SourceAt(at);
                    bool asCanvas = pic.Animation == null;
                    AsciiArt art = null;
                    string problem = null;
                    try
                    {
                        art = await Task.Run(() => Ascii.Generate(source, s, asCanvas));
                    }
                    catch (OutOfMemoryException)
                    {
                        problem = "There isn't enough memory free to make the art.";
                    }
                    catch (Exception x) when (x is InvalidOperationException || x is ArgumentException)
                    {
                        problem = "Error rendering ASCII: " + x.Message;
                    }
                    if (problem != null)
                    {
                        if (asciiWanted == null) ShowAsciiStage(problem, true);
                        continue;
                    }
                    if (at == frame && time == frameTime || asciiWanted == null) ShowAsciiArt(art, pic, at, time, instant);
                }
            }
            finally
            {
                asciiRendering = false;
            }
        }

        // the art made: it's the art on screen (unless a GIF or video plays: it waits for the pause)
        void ShowAsciiArt(AsciiArt art, LoadedPicture from, int at, long time, bool instant)
        {
            bool resized = asciiShown == null || asciiShown.Width != art.Width || asciiShown.Height != art.Height;
            asciiShown = art;
            asciiShownFrom = from;
            asciiShownFrame = at;
            asciiShownTime = time;
            asciiRenders++;
            if (AsciiPlays || asciiPlayer.Holding)
            {
                CommandManager.InvalidateRequerySuggested();
                return;
            }
            asciiPlaying = null;
            PutArt(art, instant, false);
            ShowAsciiStage();
            ShowAsciiReadout(art);
            if (resized) SyncAsciiExport();
            CommandManager.InvalidateRequerySuggested();
        }

        // the art on screen becomes the paused art, left on screen as it is
        void AdoptAscii(AsciiArt art, LoadedPicture from, int at, long time)
        {
            asciiShown = art;
            asciiShownFrom = from;
            asciiShownFrame = at;
            asciiShownTime = time;
            asciiPlaying = null;
            asciiRenders++;
            ShowAsciiReadout(art);
            SyncAsciiExport();
            CommandManager.InvalidateRequerySuggested();
        }

        // the art as its picture onto the stage (typed out by the modem when it's dialling)
        void PutArt(AsciiArt art, bool instant, bool frame)
        {
            AsciiRender.Prepare(art.Chars, new[] { 1 });
            byte[] rgb = AsciiRender.Rgb(art, 1);
            if (frame) modem.ShowFrame(art, rgb);
            else modem.Show(art, rgb, instant);
            AsciiZoomText.Text = AsciiView.ZoomText;
        }

        // "80×30 cells · 9 ANSI colours · half-block"
        void ShowAsciiReadout(AsciiArt art)
        {
            string text = art != null ? Words.AsciiReadout(art) : "";
            AsciiReadout.Text = text;
            AsciiReadout.ToolTip = text.Length > 0 ? text : null;
        }

        void ShowAsciiStage(string message = null, bool problem = false)
        {
            if (message != null) modem.HangUp();
            AsciiStageNote.Text = message ?? "";
            AsciiStageNote.Foreground = problem ? (Brush)FindResource("Bad") : new SolidColorBrush(Color.FromRgb(0x9d, 0x9d, 0x9d));
            AsciiStageNote.Visibility = message != null ? Visibility.Visible : Visibility.Collapsed;
            AsciiView.Visibility = message != null ? Visibility.Hidden : Visibility.Visible;
        }

        // ---- a GIF playing ----

        Task<PlayedFrame> MakeAsciiPlayed(int k)
        {
            LoadedPicture pic = picture;
            if (pic?.Animation == null) return Task.FromException<PlayedFrame>(new InvalidOperationException("There's no animated GIF open"));
            AsciiSettings s = settings.Ascii.Clone();
            return Task.Run(() => new PlayedFrame { Art = Ascii.Generate(pic.SourceAt(k), s, false) });
        }

        void ShowAsciiPlayed(PlayedFrame view, int k)
        {
            AsciiArt art = view.Art;
            bool resized = AsciiOnScreen == null || AsciiOnScreen.Width != art.Width || AsciiOnScreen.Height != art.Height;
            asciiPlaying = art;
            asciiPlayingFrame = k;
            PutArt(art, true, true);
            ShowAsciiStage();
            // (the readout waits while it plays: its colour count changes every frame)
            if (resized) ShowAsciiReadout(art);
        }

        void OnAsciiPlayStarted()
        {
            modem.SetPlaying(true);
            CommandManager.InvalidateRequerySuggested();
        }

        // paused on frame k (or the slider let go there): it's the picture now
        void OnAsciiPlayPaused(int k)
        {
            modem.SetPlaying(false);
            frame = k;
            if (asciiPlaying != null && asciiPlayingFrame == k && asciiPlaying.Settings.Key == settings.Ascii.Key)
            {
                // the frame on screen is the paused one: it's the art now, as it is (the modem may be typing it)
                AdoptAscii(asciiPlaying, picture, k, frameTime);
                return;
            }
            RenderAsciiSoon(true);
            CommandManager.InvalidateRequerySuggested();
        }

        // ---- a video playing ----

        Func<VideoFrame, long, PlayedFrame> AsciiVideoMaker()
        {
            AsciiSettings s = settings.Ascii.Clone();
            return (f, time) => new PlayedFrame { Art = Ascii.Generate(new PictureSource(f.Bgra, f.Width, f.Height), s) };
        }

        void ShowAsciiVideoPlayed(VideoPlayed v)
        {
            if (picture?.Video == null) return;
            asciiPlayingTime = v.Time;
            ShowAsciiPlayed(v.View, picture.Video.FrameNumber(v.Time));
        }

        void OnAsciiVideoPaused(long time, PictureSource source)
        {
            if (picture?.Video == null) return;
            modem.SetPlaying(false);
            picture.Source = source;
            frameTime = time;
            frame = picture.Video.FrameNumber(time);
            VideoFrameMoved(true);
            CommandManager.InvalidateRequerySuggested();
        }

        // A video's paused frame changed on one tab's timeline (`ascii`: the studio's): the tab
        // showing makes it. A move that lands after its tab was left is the frame now, so the
        // timeline showing is put to it too (never the one the move came from: that one may be
        // about to play on from there).
        internal void VideoFrameMoved(bool ascii)
        {
            if (AsciiActive)
            {
                if (!ascii && !asciiVideoPlayer.Playing && !asciiVideoPlayer.Holding) asciiVideoPlayer.SetVideo(picture.Video);
                RenderAsciiSoon(true);
            }
            else
            {
                if (ascii && !videoPlayer.Playing && !videoPlayer.Holding) videoPlayer.SetVideo(picture.Video);
                RenderSoon();
            }
        }

        // ---- getting the art out ----

        // The art as it is now, for a save: a GIF or video playing pauses first (the frame on screen
        // goes out), and one being made with the latest settings is waited for.
        async Task<AsciiArt> AsciiLatest()
        {
            if (asciiPlayer.Playing) asciiPlayer.Pause();
            asciiVideoPlayer.Pause();
            while (AsciiRendering) await Task.Delay(20);
            return asciiShown;
        }

        // the start of a file's name: the picture's, and a GIF's or video's frame after the first ("photo-frame12"), and " of frame 12" for the bottom line
        string AsciiBase(AsciiArt art, out string of)
        {
            LoadedPicture pic = art == asciiPlaying ? picture : asciiShownFrom ?? picture;
            int at = art == asciiPlaying ? asciiPlayingFrame : asciiShownFrame;
            bool animated = pic?.Animation != null || pic?.Video != null;
            of = animated && at > 0 ? " of frame " + (at + 1) : "";
            return Export.FrameBase(Export.BaseName(pic?.Name ?? "photo"), animated, at);
        }

        string PictureBase => Export.BaseName((asciiShownFrom ?? picture)?.Name ?? "photo");

        SauceInfo SauceFor() => new SauceInfo { Title = PictureBase, Author = "", Group = "b0r3d.org" };

        void OnAsciiCopy(object sender, RoutedEventArgs e) => CopyAscii((string)((FrameworkElement)sender).Tag);

        void OnAsciiCopyMenu(object sender, RoutedEventArgs e) => CopyAscii((string)((MenuItem)sender).Tag);

        // The art on screen (a GIF's frame playing too) as plain text, Markdown, ANSI escapes or
        // HTML, onto the clipboard (never Windows' cloud clipboard).
        internal void CopyAscii(string kind)
        {
            AsciiArt art = AsciiOnScreen;
            if (art == null) return;
            AsciiBase(art, out string of);
            string text = CopyText(art, kind, out string what);
            try
            {
                var data = new DataObject();
                data.SetText(text, TextDataFormat.UnicodeText);
                Exports.KeepOffTheCloud(data);
                Clipboard.SetDataObject(data, true);
                Flash("Copied " + what + of + " to the clipboard.", "Good");
            }
            catch (ExternalException)
            {
                Problem("Couldn't copy: another program is using the clipboard. Try again.");
            }
        }

        // the art as a copy gives it ("text", "markdown", "ansi" or "html"), and what that's called
        internal static string CopyText(AsciiArt art, string kind, out string what)
        {
            switch (kind)
            {
                case "markdown":
                    what = "Markdown code block";
                    return Ascii.Markdown(art);
                case "ansi":
                    what = "ANSI escape sequence text";
                    return Ascii.AnsiText(art);
                case "html":
                    what = "HTML <pre> block";
                    return Ascii.Html(art);
                default:
                    what = "plain text";
                    return Ascii.PlainText(art);
            }
        }

        // a file for the art: asks where (the website's name), then writes what `make` gives
        async void SaveAsciiFile(string title, string typeName, string extension, Func<AsciiArt, string, byte[]> make, Func<AsciiArt, byte[], string> said)
        {
            AsciiArt art = await AsciiLatest();
            if (art == null) return;
            string name = AsciiBase(art, out _) + extension;
            string path;
            try
            {
                path = FileDialogs.Save(this, title, typeName, extension, name);
            }
            catch (COMException x)
            {
                MessageBox.Show(this, "Windows' Save dialog didn't open:\n\n" + x.Message, Program.Name, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (path != null) CouldntSave(path, await SaveAsciiBytes(path, art, make, said));
        }

        // the art's file at `path`; what went wrong, or null
        internal async Task<string> SaveAsciiBytes(string path, AsciiArt art, Func<AsciiArt, string, byte[]> make, Func<AsciiArt, byte[], string> said)
        {
            byte[] bytes;
            try
            {
                bytes = make(art, PictureBase);
                await Task.Run(() => Exports.Write(path, bytes));
            }
            catch (Exception x) when (x is IOException || x is UnauthorizedAccessException || x is NotSupportedException || x is System.Security.SecurityException || x is InvalidOperationException)
            {
                return x.Message;
            }
            Flash("Saved " + Path.GetFileName(path) + " (" + said(art, bytes) + ").", "Good");
            return null;
        }

        static string Bytes(long n) => n.ToString("N0", CultureInfo.InvariantCulture) + " bytes";

        // the .ans (the art's own name for its title; no author: the person made it)
        internal static byte[] AnsFile(AsciiArt art, string title) => Ascii.AnsBytes(art, new SauceInfo { Title = title, Author = "", Group = "b0r3d.org" });

        void OnAsciiSaveAns(object sender, RoutedEventArgs e) =>
            SaveAsciiFile("Save the art as an ANSI file", "ANSI art", ".ans", AnsFile, (a, b) => Bytes(b.Length) + " with SAUCE record");

        // PETSCII's .seq and .prg, and Teletext's .txt (UTF-8, no byte-order mark, as the website's)
        internal static byte[] SeqFile(AsciiArt art, string title) => Ascii.PetsciiSeq(art);
        internal static byte[] PrgFile(AsciiArt art, string title) => Ascii.PetsciiPrg(art);
        internal static byte[] TxtFile(AsciiArt art, string title) => new System.Text.UTF8Encoding(false).GetBytes(Ascii.TeletextPage(art));

        void OnAsciiSaveSeq(object sender, RoutedEventArgs e) =>
            SaveAsciiFile("Save the PETSCII as a C64 .seq", "PETSCII stream", ".seq", SeqFile, (a, b) => Bytes(b.Length) + " PETSCII stream");

        void OnAsciiSavePrg(object sender, RoutedEventArgs e) =>
            SaveAsciiFile("Save the PETSCII as a C64 program", "C64 program", ".prg", PrgFile, (a, b) => Bytes(b.Length) + ": on a C64, LOAD it and type RUN");

        void OnAsciiSaveTxt(object sender, RoutedEventArgs e) =>
            SaveAsciiFile("Save the Teletext as text", "Text", ".txt", TxtFile, (a, b) => "the Teletext art as plain text");

        void OnAsciiAnsMenu(object sender, RoutedEventArgs e) => OnAsciiSaveAns(sender, e);
        void OnAsciiSeqMenu(object sender, RoutedEventArgs e) => OnAsciiSaveSeq(sender, e);
        void OnAsciiPrgMenu(object sender, RoutedEventArgs e) => OnAsciiSavePrg(sender, e);
        void OnAsciiTxtMenu(object sender, RoutedEventArgs e) => OnAsciiSaveTxt(sender, e);

        // ---- the PNG ----

        async void AskAndSaveAsciiPng()
        {
            AsciiArt art = await AsciiLatest();
            if (art == null) return;
            int m = settings.AsciiScale;
            string name = AsciiRender.PngName(AsciiBase(art, out _), art, m);
            string path;
            try
            {
                path = FileDialogs.Save(this, "Save the art's picture as a PNG", "PNG picture", ".png", name);
            }
            catch (COMException x)
            {
                MessageBox.Show(this, "Windows' Save dialog didn't open:\n\n" + x.Message, Program.Name, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (path != null) CouldntSave(path, await SaveAsciiPng(path, art, m));
        }

        // the art's picture at `m`× as a PNG at `path`; what went wrong, or null
        internal async Task<string> SaveAsciiPng(string path, AsciiArt art, int m)
        {
            Saving = true;
            Flash("Making ASCII PNG (" + m + "×)…", "Dim");
            AsciiRender.Prepare(art.Chars, new[] { m });
            int w = 0, h = 0;
            string problem = null;
            try
            {
                await Task.Run(() => Exports.Write(path, stream => AsciiRender.WritePng(stream, art, m, out w, out h)));
            }
            catch (Exception x) when (x is IOException || x is UnauthorizedAccessException || x is NotSupportedException || x is System.Security.SecurityException)
            {
                problem = x.Message;
            }
            catch (OutOfMemoryException)
            {
                problem = "there isn't enough memory free for a picture that big";
            }
            finally
            {
                Saving = false;
            }
            if (problem == null) Flash("Saved " + Path.GetFileName(path) + " (" + w + "×" + h + ").", "Good");
            return problem;
        }

        // ---- every frame: GIF, APNG, ANSImation and (a video) MP4 ----

        void OnAsciiSaveGif(object sender, RoutedEventArgs e) => AskAndSaveAsciiFrames(AsciiFramesOut.Gif);
        void OnAsciiSaveApng(object sender, RoutedEventArgs e) => AskAndSaveAsciiFrames(AsciiFramesOut.Apng);
        void OnAsciiSaveAnsimation(object sender, RoutedEventArgs e) => AskAndSaveAsciiFrames(AsciiFramesOut.Ansimation);
        void OnAsciiSaveMp4(object sender, RoutedEventArgs e) => AskAndSaveAsciiFrames(AsciiFramesOut.Mp4);
        void OnAsciiAnsimationMenu(object sender, RoutedEventArgs e) => OnAsciiSaveAnsimation(sender, e);

        internal enum AsciiFramesOut { Gif, Apng, Ansimation, Mp4 }

        async void AskAndSaveAsciiFrames(AsciiFramesOut kind)
        {
            if (animating != null || askingForAnimation) return;
            askingForAnimation = true;
            try
            {
                AsciiArt art = await AsciiLatest();
                LoadedPicture pic = asciiShownFrom;
                if (art == null || pic == null || pic.Animation == null && pic.Video == null) return;
                string name = AsciiFramesName(art, pic, kind);
                string path;
                try
                {
                    path = kind == AsciiFramesOut.Mp4 ? FileDialogs.Save(this, "Save the art of every frame as an MP4", "MP4 video", ".mp4", name)
                        : kind == AsciiFramesOut.Ansimation ? FileDialogs.Save(this, "Save every frame as an ANSImation", "ANSI art", ".ans", name)
                        : FileDialogs.Save(this, kind == AsciiFramesOut.Apng ? "Save every frame as an APNG" : "Save every frame as a GIF", kind == AsciiFramesOut.Apng ? "Animated PNG" : "GIF animation",
                            kind == AsciiFramesOut.Apng ? ".png" : ".gif", name);
                }
                catch (COMException x)
                {
                    MessageBox.Show(this, "Windows' Save dialog didn't open:\n\n" + x.Message, Program.Name, MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                if (path != null) CouldntSave(path, await SaveAsciiFrames(path, kind, art.Settings, pic));
            }
            finally
            {
                askingForAnimation = false;
            }
        }

        // the website's names: photo-ansi-640x480-1x.gif, …-animated.png, photo-animated.ans; and photo-ansi-640x480-1x.mp4
        internal string AsciiFramesName(AsciiArt art, LoadedPicture pic, AsciiFramesOut kind)
        {
            string stem = Export.BaseName(pic?.Name ?? "photo");
            switch (kind)
            {
                case AsciiFramesOut.Ansimation: return stem + "-animated.ans";
                case AsciiFramesOut.Mp4: return Path.ChangeExtension(AsciiRender.AnimationName(stem, art, AsciiMp4Scale(art), false), ".mp4");
                default: return AsciiRender.AnimationName(stem, art, AsciiAnimationScale(art, settings.AsciiScale) ?? 1, kind == AsciiFramesOut.Apng);
            }
        }

        // Every frame of the GIF (or the video, from its start to its stop) through the settings `s`
        // as `kind` at `path`: made off the window's thread, a few at once, its progress in the
        // bottom line, Stop leaving no file. What went wrong, or null (stopped too).
        internal async Task<string> SaveAsciiFrames(string path, AsciiFramesOut kind, AsciiSettings s, LoadedPicture pic)
        {
            if (pic == null || pic.Animation == null && pic.Video == null) return "there's nothing that moves";
            if (animating != null) return "something else is being made: wait for it, or Stop it";
            AsciiArt first = asciiShown ?? Ascii.Generate(pic.SourceAt(0), s, pic.Animation == null);
            // (an ANSImation is text: it has no picture to fit)
            int m = kind == AsciiFramesOut.Ansimation ? 1 : kind == AsciiFramesOut.Mp4 ? AsciiMp4Scale(first) : AsciiAnimationScale(first, settings.AsciiScale) ?? 0;
            if (m == 0) return "even 1× is too big for an animation: pick fewer columns";
            AsciiRender.Prepare(AsciiRender.CharactersOf(s), new[] { m });
            var cancel = new CancellationTokenSource();
            animating = cancel;
            Saving = true;
            SyncAscii();
            Sync();
            string making = "Making " + (kind == AsciiFramesOut.Ansimation ? "the ANSImation" : kind == AsciiFramesOut.Mp4 ? Path.GetFileName(path) : "the " + (kind == AsciiFramesOut.Apng ? "APNG" : "GIF")) + "…";
            Flash(making, "Dim");
            var clock = Stopwatch.StartNew();
            void Progress(int done, int total, string note) => Dispatcher.BeginInvoke(new Action(() =>
            {
                if (animating != cancel || cancel.IsCancellationRequested) return;
                string left = "";
                if (pic.Video != null && done > 0 && done < total && clock.Elapsed.TotalSeconds > 2)
                {
                    double seconds = clock.Elapsed.TotalSeconds / done * (total - done);
                    left = ", about " + (seconds < 60 ? Math.Max(1, (int)Math.Round(seconds)) + " s" : VideoPlayer.Clock((long)(seconds * VideoFile.Second)).Split('.')[0]) + " left";
                }
                Flash(making + " " + (note != null ? note + ", " : "") + "frame " + done.ToString("N0", CultureInfo.InvariantCulture) + " of " + total.ToString("N0", CultureInfo.InvariantCulture) + left +
                    (done == total && note == null && kind != AsciiFramesOut.Ansimation ? ", putting them together" : ""), "Dim");
            }));
            AnimationResult result = null;
            int ansFrames = 0;
            long ansBytes = 0;
            string problem = null;
            bool stopped = false;
            var o = new AsciiFramesOptions
            {
                Settings = s.Clone(), Multiplier = m, Picture = pic, Fps = pic.Video != null ? settings.VideoFps(pic.Video.File.Fps) : 0, Sound = settings.VideoSound,
                From = pic.Video?.From ?? 0, To = pic.Video?.To ?? 0,
                Title = Export.BaseName(pic.Name ?? "photo"), Progress = Progress, Cancel = cancel.Token,
            };
            try
            {
                await Task.Run(() => Exports.Write(path, stream =>
                {
                    if (kind == AsciiFramesOut.Ansimation)
                    {
                        var counted = new CountingStream(stream);
                        ansFrames = AsciiFrames.Ansimation(counted, o);
                        ansBytes = counted.Count;
                    }
                    else if (kind == AsciiFramesOut.Mp4) result = AsciiFrames.Mp4(stream, o);
                    else result = AsciiFrames.Animation(stream, o, kind == AsciiFramesOut.Apng);
                    // Stop while the frames were put together: still no file
                    cancel.Token.ThrowIfCancellationRequested();
                }));
            }
            catch (OperationCanceledException)
            {
                stopped = true;
            }
            catch (Exception x) when (x is AnimationException || x is VideoException || x is IOException || x is UnauthorizedAccessException || x is NotSupportedException || x is System.Security.SecurityException || x is InvalidOperationException)
            {
                problem = x.Message.TrimEnd('.');
            }
            catch (OutOfMemoryException)
            {
                problem = "there isn't enough memory free for frames that big: pick a smaller Size";
            }
            finally
            {
                animating = null;
                Saving = false;
                SyncAscii();
                Sync();
            }
            if (closeAfterAnimation)
            {
                _ = Dispatcher.BeginInvoke(new Action(Close));
                return null;
            }
            if (stopped) Flash("Stopped: " + Path.GetFileName(path) + " wasn't saved.", "Warn");
            else if (problem == null)
                Flash(kind == AsciiFramesOut.Ansimation ? "Saved " + Path.GetFileName(path) + " (" + ansFrames.ToString("N0", CultureInfo.InvariantCulture) + " frames, " + Bytes(ansBytes) + " with SAUCE record)."
                    : "Saved " + Path.GetFileName(path) + " (" + result.Width + "×" + result.Height + ", " + result.Frames.ToString("N0", CultureInfo.InvariantCulture) + " frames).", "Good");
            return problem;
        }

        // a stream that passes bytes on and counts them
        sealed class CountingStream : Stream
        {
            readonly Stream inner;
            public long Count;
            public CountingStream(Stream inner) => this.inner = inner;
            public override void Write(byte[] buffer, int offset, int count)
            {
                inner.Write(buffer, offset, count);
                Count += count;
            }
            public override void WriteByte(byte value)
            {
                inner.WriteByte(value);
                Count++;
            }
            public override bool CanRead => false;
            public override bool CanSeek => false;
            public override bool CanWrite => true;
            public override long Length => Count;
            public override long Position { get => Count; set => throw new NotSupportedException(); }
            public override void Flush() => inner.Flush();
            public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
        }
    }

    // a timer that does one thing once after a while (the two-step Reset button's)
    sealed class DispatcherTimerHolder
    {
        readonly System.Windows.Threading.DispatcherTimer timer;
        public DispatcherTimerHolder(TimeSpan after, Action then)
        {
            timer = new System.Windows.Threading.DispatcherTimer { Interval = after };
            timer.Tick += (s, e) => then();
        }
        public bool Running => timer.IsEnabled;
        public void Start() => timer.Start();
        public void Stop() => timer.Stop();
    }
}
