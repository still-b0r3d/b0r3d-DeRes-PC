using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using B0r3dDeRes.Engine;

namespace B0r3dDeRes.Tests
{
    // b0r3dDeResTests.exe --shots <folder> ascii: the ASCII / ANSI studio, in light and dark (the
    // Rukus sample as it starts, at 100 % and 150 %; the crystal forest as a truecolour ramp with
    // the ramps open; the cat as PETSCII; the forest as Teletext with gaps; the Export section; the
    // katakana ramp in Windows' font; green CRT under the scanlines; the modem typing; the GIF
    // sample playing, its Export; a video on its timeline).
    static partial class TestMain
    {
        static void AsciiShots(string folder, Look look)
        {
            Theme.Set(look);
            string name = look.ToString().ToLowerInvariant();
            void Shot(MainWindow w, string shot, double scale = 1)
            {
                LayOut(w.Root, MainWidth, MainHeight);
                Pump(() => false, 60);
                LayOut(w.Root, MainWidth, MainHeight);
                Shoot(w.Root, MainWidth, MainHeight, scale, Path.Combine(folder, shot + "-" + name + (scale != 1 ? "-" + (int)(scale * 100) : "") + ".png"));
            }
            MainWindow Studio(string sample, double scale = 1)
            {
                MainWindow w = NewMain(scale);
                WaitForPicture(w);
                if (sample != null)
                {
                    w.OpenSample(sample);
                    WaitForPicture(w);
                }
                w.Tabs.SelectedItem = w.AsciiTab;
                WaitForAscii(w);
                return w;
            }
            void Sections(MainWindow w, Expander open)
            {
                foreach (Expander e in new[] { w.AsciiImageSection, w.AsciiModeSection, w.AsciiRampSection, w.AsciiTeletextSection, w.AsciiColourSection, w.AsciiAdjustSection, w.AsciiExportSection })
                    e.IsExpanded = e == open;
            }

            foreach (double scale in new[] { 1.0, 1.5 })
            {
                MainWindow start = Studio(null, scale);
                Shot(start, "ascii-start", scale);
                start.Close();
            }

            MainWindow ramp = Studio("forest");
            ClickSegment(ramp.AsciiModeButtons, AsciiModes.Ramp);
            ClickSegment(ramp.AsciiColourModes, AsciiColours.Truecolor);
            ClickSegment(ramp.AsciiColumnPresets, 132);
            WaitForAscii(ramp);
            Sections(ramp, ramp.AsciiRampSection);
            ramp.AsciiModeSection.IsExpanded = true;
            Shot(ramp, "ascii-ramp");
            ramp.Close();

            MainWindow petscii = Studio("cat-nap");
            ClickSegment(petscii.AsciiModeButtons, AsciiModes.Petscii);
            WaitForAscii(petscii);
            Sections(petscii, petscii.AsciiModeSection);
            Shot(petscii, "ascii-petscii");
            petscii.Close();

            MainWindow teletext = Studio("forest");
            ClickSegment(teletext.AsciiModeButtons, AsciiModes.Teletext);
            teletext.AsciiSeparatedBox.IsChecked = true;
            WaitForAscii(teletext);
            Sections(teletext, teletext.AsciiTeletextSection);
            teletext.AsciiView.ZoomTo(2);
            Shot(teletext, "ascii-teletext");
            teletext.Close();

            MainWindow export = Studio(null);
            Sections(export, export.AsciiExportSection);
            Shot(export, "ascii-export");
            export.Close();

            MainWindow katakana = Studio("cat-nap");
            ClickSegment(katakana.AsciiModeButtons, AsciiModes.Ramp);
            ClickSegment(katakana.AsciiRamps, "katakana");
            ClickSegment(katakana.AsciiColourModes, AsciiColours.Green);
            WaitForAscii(katakana);
            katakana.SetAsciiScanlines(true);
            Sections(katakana, katakana.AsciiColourSection);
            katakana.AsciiView.ZoomTo(1);
            Shot(katakana, "ascii-katakana-green-scanlines");
            katakana.Close();

            MainWindow modem = Studio("forest");
            modem.AsciiBaud.SelectedIndex = Array.FindIndex(AsciiModem.Speeds, s => s.Id == "2400");
            modem.Modem.StepFor(1400);
            Sections(modem, modem.AsciiImageSection);
            Shot(modem, "ascii-modem");
            modem.Close();

            MainWindow gif = Studio("rukus-gif");
            gif.AsciiGifPlayer.Play();
            Pump(() => gif.AsciiGifPlayer.Frame >= 8, 20000);
            Shot(gif, "ascii-gif-playing");
            gif.AsciiGifPlayer.Pause();
            WaitForAscii(gif);
            Sections(gif, gif.AsciiExportSection);
            ScrollAscii(gif, gif.AsciiSaveAns);
            Shot(gif, "ascii-gif-export");
            gif.Close();

            if (Video.Mf.Available)
            {
                string clip = Path.Combine(Work, "cat-nap-pan.mp4");
                if (!File.Exists(clip)) PanClip(clip);
                MainWindow video = NewMain();
                WaitForPicture(video);
                video.OpenFile(clip);
                Pump(() => !video.Opening, 20000);
                WaitForPicture(video);
                video.Tabs.SelectedItem = video.AsciiTab;
                WaitForAscii(video);
                ClickSegment(video.AsciiColourModes, AsciiColours.Truecolor);
                WaitForAscii(video);
                Sections(video, video.AsciiExportSection);
                ScrollAscii(video, video.AsciiSizes);
                Shot(video, "ascii-video");
                video.Close();
            }
        }

        // scrolls the studio's settings so `element` is near the top
        static void ScrollAscii(MainWindow window, FrameworkElement element)
        {
            LayOut(window.Root, MainWidth, MainHeight);
            double y = element.TranslatePoint(new Point(0, 0), (UIElement)window.AsciiScroll.Content).Y;
            window.AsciiScroll.ScrollToVerticalOffset(Math.Max(0, y - 120));
            LayOut(window.Root, MainWidth, MainHeight);
            Pump(() => false, 60);
            LayOut(window.Root, MainWidth, MainHeight);
        }
    }
}
