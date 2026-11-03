using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using B0r3dDeRes.Engine;

namespace B0r3dDeRes.Tests
{
    // GIFs and animations in the window: the play bar, playing through the settings, the paused
    // frame as the picture, GIF and APNG saves, Stop, and the batch.
    static partial class TestMain
    {
        // the window's own frame k of the picture open, through the settings it shows
        static Rendered EngineFrame(MainWindow window, int k)
        {
            var engine = new Deres();
            engine.SetSource(window.OpenedPicture.SourceAt(k));
            return engine.Render(window.ShownWith);
        }

        static void PickAnimation(MainWindow window, string id) =>
            window.AnimStyles.Children.OfType<RadioButton>().First(b => (string)b.Tag == id).IsChecked = true;

        static void GifPlaysInTheWindow()
        {
            MainWindow window = NewMain();
            WaitForPicture(window);
            Check(window.PlayBar.Visibility != Visibility.Visible && !window.GifPlayer.Animated, "a still has no play bar");
            window.OpenSample("rukus-gif");
            WaitForPicture(window);
            LoadedPicture gif = window.OpenedPicture;
            Check(gif.Animation != null && gif.Animation.Frames.Length == 58 && window.PlayBar.Visibility == Visibility.Visible && window.FrameCounter.Text == "1 / 58",
                "the GIF sample opens with its play bar at 1 / 58: " + window.FrameCounter.Text);
            Check(window.PictureInfo.Text.StartsWith("rukus-breaks-out.gif · 512×384 · 58 frames · ", StringComparison.Ordinal), "the picture line: " + window.PictureInfo.Text);
            Check(window.ShownFrame == 0 && window.Shown.Rgb.SequenceEqual(EngineFrame(window, 0).Rgb), "the picture should be its first frame through the settings");
            Check(window.AnimationKind == Animation.GifFrames && window.AnimStyles.Children.OfType<RadioButton>().First(b => (string)b.Tag == Animation.GifFrames).Visibility == Visibility.Visible,
                "a GIF's animation is its own frames, the button shown");

            // playing: each frame through the settings, at the GIF's own speed; Compare is off
            Player player = window.GifPlayer;
            Pick(window, "nes");
            player.Play();
            Check(player.Playing && !Commands.Compare.CanExecute(null, window) && window.PlayButton.Content as string == "❚❚ _Pause", "playing: Compare off, the button says Pause");
            Pump(() => player.Frame >= 4, 15000);
            Check(player.Frame >= 4, "it didn't play on: frame " + player.Frame);
            Check(window.View.PicturePixels.SequenceEqual(EngineFrame(window, player.Frame).Rgb), "the frame playing should be frame " + player.Frame + " through the settings");
            // a change while it plays: the frames made before are dropped, the new ones follow it
            window.Dithers.Children.OfType<RadioButton>().First(b => (string)b.Tag == DitherIds.Floyd).IsChecked = true;
            int from = player.Frame;
            Pump(() => player.Frame != from && player.Frame != (from + 1) % 58, 15000);
            var floyd = window.Current.Deres.Clone();
            var check = new Deres();
            check.SetSource(gif.SourceAt(player.Frame));
            Check(window.View.PicturePixels.SequenceEqual(check.Render(floyd).Rgb), "after a change, the frames playing should be made with it");

            // the signal while it plays: each frame its own frame of the noise, at the playing size
            PickSignal(window, SignalModes.Rf);
            Pump(() => window.View.HasScreen && player.Frame > 2, 15000);
            {
                int k = player.Frame;
                Rendered r = EngineFrame(window, k);
                SignalImage through = Signal.Run(r, "nes", window.Current.Signal, k * gif.Animation.SignalStep);
                byte[] want = Signal.Renderer(through, window.View.ScreenWidth, window.View.ScreenHeight, null, gif.Animation.Phase(k), r.Paper).Rgba();
                Check(window.View.ScreenPixels.SequenceEqual(want), "the frame playing through the signal should be the engine's frame " + k * gif.Animation.SignalStep + " of it");
            }
            PickSignal(window, SignalModes.Off);

            // a picture that won't open while it plays: it stops, and the frame it stopped on is the picture
            string broken = Path.Combine(Work, "broken-while-playing.png");
            File.WriteAllText(broken, "not a picture");
            window.OpenFile(broken);
            Pump(() => !window.Opening, 10000);
            WaitForPicture(window);
            Check(!player.Playing && window.ShownFrame == player.Frame && window.OpenedPicture == gif && window.NoteText.StartsWith("Couldn't open broken-while-playing.png", StringComparison.Ordinal),
                "after a picture that won't open, the frame it stopped on should be the picture: frame " + window.ShownFrame + " for " + player.Frame);
            player.Play();
            Pump(() => player.Frame != window.ShownFrame, 10000);

            // paused: the frame on screen is the picture, made at the full size, Compare back
            player.Pause();
            int at = player.Frame;
            WaitForPicture(window);
            Check(window.ShownFrame == at && window.Shown.Rgb.SequenceEqual(EngineFrame(window, at).Rgb) && window.FrameCounter.Text == (at + 1) + " / 58" && window.PlayButton.Content as string == "▶ _Play",
                "paused on frame " + at + ": the picture should be that frame");
            Check(Commands.Compare.CanExecute(null, window), "Compare comes back on the paused frame");
            // the slider goes to any frame
            window.FrameSlider.Value = 20;
            WaitForPicture(window);
            Check(window.ShownFrame == 20 && player.Frame == 20 && window.FrameCounter.Text == "21 / 58", "the slider at 20: frame " + window.ShownFrame);
            // held, it shows frames made at the playing size, and lets go on the one under it
            player.Grab();
            window.FrameSlider.Value = 30;
            Pump(() => window.View.PicturePixels != null && window.View.PicturePixels.SequenceEqual(EngineFrame(window, 30).Rgb), 10000);
            player.LetGo();
            WaitForPicture(window);
            Check(window.ShownFrame == 30, "let go on frame 30: " + window.ShownFrame);

            // a frame's PNG says which frame it is, with its place in the loop for the screen effect
            PickScreen(window, Screens.Vhs);
            window.ExportFxBox.IsChecked = true;
            Rendered shown = window.Shown;
            MainWindow.Shared share = window.ToShare(shown);
            Check(share.Name == "rukus-breaks-out-frame31-nes-256x240-4x-fx.png", "a frame's PNG name: " + share.Name);
            string folder = Path.Combine(Work, "gif-frame");
            Fresh(folder);
            string path = Path.Combine(folder, share.Name);
            Check(Await(window.SavePng(path, shown, 4, false, window.Current.ExportScreen)) == null, "the frame's PNG wasn't saved");
            Check(File.ReadAllBytes(path).SequenceEqual(Export.PngBytes(shown, 4, false, window.Current.Fx, null, gif.Animation.Phase(30))), "the frame's PNG should have the screen effect at its place in the loop");
            Check(window.WallpaperName(shown, window.WallpaperChoices[0]) == "rukus-breaks-out-frame31-nes-phone-wallpaper.png", "a frame's wallpaper name");

            // a new picture: the play bar goes, the frame starts over
            window.OpenSample("rukus");
            WaitForPicture(window);
            Check(window.PlayBar.Visibility != Visibility.Visible && window.ShownFrame == 0 && !window.GifPlayer.Animated && window.AnimationKind == Animation.ScreenFx, "a still after the GIF");
        }

        // GIF and APNG saves: a GIF's own frames, every loop style, the signal's loop, the line by
        // the buttons, Stop; the style kept in the settings and in looks.
        static void AnimationsSaveInTheWindow()
        {
            MainWindow window = NewMain();
            WaitForPicture(window);
            string folder = Path.Combine(Work, "animations");
            Fresh(folder);

            // a loop of a still: the Screen FX loop (a CRT when no screen is on), named as the website names it
            Check(window.AnimationKind == Animation.ScreenFx && window.AnimStyles.Children.OfType<RadioButton>().First(b => (string)b.Tag == Animation.GifFrames).Visibility == Visibility.Collapsed,
                "a still's animation is the Screen FX loop; no GIF frames button");
            window.Sizes.Children.OfType<RadioButton>().First(b => (int)b.Tag == 2).IsChecked = true;
            Rendered r = window.Shown;
            foreach (string style in Animation.Styles)
            {
                PickAnimation(window, style);
                Check(window.Current.AnimationStyle == style && window.AnimationKind == style, "picked " + style);
                foreach (bool apng in new[] { false, true })
                {
                    string name = window.AnimationFileName(r, apng);
                    Check(name == "rukus-gameboy-" + style + (apng ? "-animated.png" : "-loop.gif"), "the " + style + " file's name: " + name);
                    string path = Path.Combine(folder, name);
                    Check(Await(window.SaveAnimation(path, apng)) == null && File.Exists(path), "the " + style + " " + (apng ? "APNG" : "GIF") + " wasn't saved");
                    var engine = new Deres();
                    engine.SetSource(window.OpenedPicture.Source);
                    using (var want = new MemoryStream())
                    {
                        Animation.Loop(want, r, new LoopOptions { Style = style, Apng = apng, Multiplier = 2, Source = window.OpenedPicture.Source, Limits = AnimationLimits.Pc }, window.ShownWith, sub => engine.Render(sub));
                        Check(File.ReadAllBytes(path).SequenceEqual(want.ToArray()), "the " + style + " " + (apng ? "APNG" : "GIF") + " should be the engine's");
                    }
                    Check(window.NoteText.StartsWith("Saved " + name + " (320×288, ", StringComparison.Ordinal), "the bottom line: " + window.NoteText);
                }
            }

            // the signal's loop: its length and speed, under Export
            PickAnimation(window, Animation.ScreenFx);
            PickSignal(window, SignalModes.VhsEp);
            Check(window.LoopRows.Visibility == Visibility.Visible, "the signal loop's length and speed should show");
            window.LoopLengths.Children.OfType<RadioButton>().First(b => (int)b.Tag == 48).IsChecked = true;
            window.LoopSpeeds.Children.OfType<RadioButton>().First(b => (int)b.Tag == 30).IsChecked = true;
            Check(window.Current.LoopFrames == 48 && window.Current.LoopFps == 30, "48 frames at 30 fps");
            window.Sizes.Children.OfType<RadioButton>().First(b => (int)b.Tag == 1).IsChecked = true;
            string loop = Path.Combine(folder, window.AnimationFileName(window.Shown, true));
            Check(Path.GetFileName(loop) == "rukus-gameboy-screen-fx-vhs-ep-animated.png", "the signal loop's name: " + Path.GetFileName(loop));
            Check(Await(window.SaveAnimation(loop, true)) == null, "the signal loop wasn't saved");
            byte[] bytes = File.ReadAllBytes(loop);
            int actl = Enumerable.Range(8, bytes.Length - 12).First(i => bytes[i] == 'a' && bytes[i + 1] == 'c' && bytes[i + 2] == 'T' && bytes[i + 3] == 'L');
            Check((bytes[actl + 4] << 24 | bytes[actl + 5] << 16 | bytes[actl + 6] << 8 | bytes[actl + 7]) == 48, "the signal loop should hold 48 frames");
            // Stop: nothing saved, said so
            window.Sizes.Children.OfType<RadioButton>().First(b => (int)b.Tag == 8).IsChecked = true;
            string stopped = Path.Combine(folder, "stopped.png");
            var task = window.SaveAnimation(stopped, true);
            Pump(() => window.Animating, 2000);
            // one at a time
            string second = Await(window.SaveAnimation(Path.Combine(folder, "second.gif"), false));
            Check(second != null && second.StartsWith("another animation is being made", StringComparison.Ordinal) && !File.Exists(Path.Combine(folder, "second.gif")),
                "a second animation while one is made should wait: " + second);
            window.StopAnimation();
            Check(Await(task) == null && !File.Exists(stopped) && !Directory.GetFiles(folder).Any(f => Path.GetFileName(f).StartsWith(".stopped", StringComparison.Ordinal)) && window.NoteText.StartsWith("Stopped:", StringComparison.Ordinal),
                "Stop should leave no file and say so: " + window.NoteText);
            // the line by the buttons when the Size chosen is too big for an animation: 48 frames of the signal's loop at 16×
            window.Sizes.Children.OfType<RadioButton>().First(b => (int)b.Tag == 16).IsChecked = true;
            Check(window.AnimHint.Visibility == Visibility.Visible && window.AnimHint.Text == "GIF and APNG come out at 8× (1280×1152 px): 12× and up are too big for 48 frames.", "the line by the buttons: " + window.AnimHint.Text);
            window.Sizes.Children.OfType<RadioButton>().First(b => (int)b.Tag == 4).IsChecked = true;
            Check(window.AnimHint.Visibility != Visibility.Visible, "no line when the Size fits: " + window.AnimHint.Text);
            PickSignal(window, SignalModes.Off);

            // the GIF's own frames, every one through the settings, its delays and loop kept
            window.OpenSample("rukus-gif");
            WaitForPicture(window);
            Pick(window, "snes");
            window.Sizes.Children.OfType<RadioButton>().First(b => (int)b.Tag == 1).IsChecked = true;
            string frames = Path.Combine(folder, window.AnimationFileName(window.Shown, false));
            Check(Path.GetFileName(frames) == "rukus-breaks-out-snes.gif", "the GIF frames' name: " + Path.GetFileName(frames));
            Check(Await(window.SaveAnimation(frames, false)) == null, "the GIF's frames weren't saved");
            AnimatedGif sample = window.OpenedPicture.Animation;
            var lanes = new[] { new Deres() };
            DeresSettings s = window.ShownWith;
            using (var want = new MemoryStream())
            {
                Animation.FramesOf(want, new FramesOptions
                {
                    Multiplier = 1, DelaysMs = sample.DelaysMs, LoopCount = sample.LoopCount, Indexed = true, SharedPalette = false, Limits = AnimationLimits.Pc,
                    Render = (k, lane) =>
                    {
                        lanes[0].SetSource(sample.Frames[k]);
                        return lanes[0].Render(s);
                    },
                });
                Check(File.ReadAllBytes(frames).SequenceEqual(want.ToArray()), "the GIF's frames should be the engine's");
            }
            var back = new GifReader(File.ReadAllBytes(frames));
            Check(back.Frames().Count() == 58 && back.Width == 256 && back.Height == 224 && back.LoopCount == 0, "the GIF should read back as 58 frames of 256×224 that loop");
            // another loop picked for a GIF, then a new GIF: its own frames again
            PickAnimation(window, Animation.CompareWipe);
            Check(window.AnimationKind == Animation.CompareWipe, "the A/B wipe of a GIF's frame");
            window.OpenSample("rukus-gif");
            WaitForPicture(window);
            Check(window.AnimationKind == Animation.GifFrames && window.Current.AnimationStyle == Animation.CompareWipe, "a GIF opened goes back to its own frames");

            // the loop style is kept, and goes into looks
            string ini = Path.Combine(folder, "b0r3dDeRes.ini");
            File.WriteAllText(ini, window.Current.ToText());
            Check(Settings.Load(ini).AnimationStyle == Animation.CompareWipe, "the loop style should be kept");
            Check(Looks.FileText(window.Current).Contains("\"animationStyle\": \"compare-wipe\""), "a look file should hold the loop style");
            Check(Looks.Apply(Looks.FromFile(Looks.FileText(window.Current)), Settings.Defaults()).AnimationStyle == Animation.CompareWipe, "a look file's loop style should be read back");
            File.WriteAllText(ini, "animation=nope\r\n");
            Check(Settings.Load(ini).AnimationStyle == Animation.ScreenFx, "an odd loop style should fall back to the Screen FX loop");
            window.ResetSettings();
            Check(window.Current.AnimationStyle == Animation.ScreenFx, "Reset settings: the Screen FX loop");
        }

        // GIFs shrunk to be held (here with less room than the PC's, so small ones are): Custom's
        // height and widest and the resolution line go by the GIF's own size, as the website's.
        static void ShrunkGifInTheWindow()
        {
            long budget = Pictures.GifBudget;
            try
            {
                MainWindow window = NewMain();
                WaitForPicture(window);
                Pick(window, "nes");
                window.CustomButton.IsChecked = true;
                string open = null;
                foreach (var c in Website.ShrunkGifRenders)
                {
                    if (c.File != open)
                    {
                        Pictures.GifBudget = (long)c.Budget;
                        window.OpenFile(Path.Combine(TestsFolder, "website", c.File + ".gif"));
                        Pump(() => !window.Opening, 10000);
                        WaitForPicture(window);
                        open = c.File;
                        LoadedPicture gif = window.OpenedPicture;
                        Check(gif.Animation != null && gif.Source.Width < gif.Animation.OrigW && gif.Source.OrigW == gif.Animation.OrigW &&
                            window.PictureInfo.Text.StartsWith(c.File + ".gif · " + gif.Animation.OrigW + "×" + gif.Animation.OrigH + " · ", StringComparison.Ordinal),
                            c.File + " should be shrunk to be held, its own size said: " + gif.Source.Width + " of " + gif.Source.OrigW + ", " + window.PictureInfo.Text);
                    }
                    window.DetailSlider.Value = JsMath.Round(c.Detail * 20);
                    // (a width past the widest is taken as the widest)
                    window.TypeWidth(c.CustomWidth.ToString());
                    Pump(() => !window.Rendering && window.ShownWith != null && window.ShownWith.CustomWidth == c.Width && window.ShownWith.Detail == c.Detail, 15000);
                    WaitForPicture(window);
                    Check(window.Shown.Width == c.Width && window.Shown.Height == c.Height && window.WidthHeight.Text == "× " + c.Height + " px" && window.Current.Deres.CustomWidth == c.Width &&
                        window.Shown.Rgb.SequenceEqual(EngineFrame(window, 0).Rgb),
                        c.File + " at " + c.CustomWidth + " wide: " + window.Shown.Width + "×" + window.Shown.Height + " (" + window.WidthHeight.Text + ", width " + window.Current.Deres.CustomWidth + "), not " +
                        c.Width + "×" + c.Height + " as the website's");
                    Check(window.ResolutionInfo.Text == c.Words, "the resolution line: \"" + window.ResolutionInfo.Text + "\", not \"" + c.Words + "\"");
                }
            }
            finally
            {
                Pictures.GifBudget = budget;
            }
        }

        // A batch of GIFs skipped, replaced and stopped: one already there is skipped (not made) or
        // replaced; Stop while one is made leaves no file and counts nothing, not even the machine
        // file it skipped; and a file that comes to the name while one is made is left as it was
        // (the new one takes the next number), but for Replace.
        static void BatchGifsSkipReplaceStop()
        {
            string came = Path.Combine(Work, "came");
            Fresh(came);
            string at = Path.Combine(came, "came.gif");
            string went = Exports.Write(at, stream =>
            {
                File.WriteAllText(at, "theirs");
                stream.WriteByte(7);
            }, () => Path.Combine(came, "came (2).gif"));
            Check(went == Path.Combine(came, "came (2).gif") && File.ReadAllText(at) == "theirs" && File.ReadAllBytes(went).SequenceEqual(new byte[] { 7 }),
                "a file that came to the name meanwhile should be left as it was: " + went);
            Exports.Write(at, stream => stream.WriteByte(8));
            Check(File.ReadAllBytes(at).SequenceEqual(new byte[] { 8 }) && Directory.GetFiles(came).Length == 2, "with nowhere else to go, it's replaced");

            MainWindow window = NewMain(1, false);
            string folder = Path.Combine(Work, "batch-gifs-stop");
            Fresh(folder);
            File.WriteAllBytes(Path.Combine(folder, "rukus-breaks-out.gif"), GifFile("gif-rukus"));
            window.Current.Deres.ProfileId = "zx-spectrum";
            window.Current.Deres.Dither = Profiles.ById("zx-spectrum").Dither;
            window.Current.Multiplier = 1;
            window.Sync();
            BatchWindow batch = window.OpenBatch(new[] { folder });
            batch.GifChoice.IsChecked = true;
            string into = Path.Combine(folder, "DeRes");
            void Make(Clash clash, string outcome)
            {
                batch.Make(clash);
                Pump(() => batch.Running.IsCompleted, 30000);
                Check(batch.LastOutcome == outcome, "the batch came out " + batch.LastOutcome + ", not " + outcome + ": " + batch.StatusLine.Text);
            }
            string Made() => string.Join(", ", Directory.GetFiles(into).Select(Path.GetFileName));
            Make(Clash.Number, "1 made, 0 skipped, 0 failed");
            string gif = Path.Combine(into, "rukus-breaks-out-zx-spectrum.gif");
            Check(File.Exists(gif), "made: " + Made());
            DateTime first = File.GetLastWriteTimeUtc(gif);
            Make(Clash.Skip, "0 made, 1 skipped, 0 failed");
            Check(File.GetLastWriteTimeUtc(gif) == first && Directory.GetFiles(into).Length == 1, "skipped: " + Made());
            File.WriteAllText(gif, "old");
            Make(Clash.Replace, "1 made, 0 skipped, 0 failed");
            Check(new FileInfo(gif).Length > 100 && Directory.GetFiles(into).Length == 1, "replaced: " + Made());

            // Stop while the GIF is made (big and through the signal, so it takes a while): no
            // file, and nothing counted, not the machine file it skipped either
            File.Delete(gif);
            string scr = Path.Combine(into, "rukus-breaks-out-zx-spectrum.scr");
            File.WriteAllText(scr, "there");
            window.Current.Multiplier = 8;
            window.Current.Signal.Mode = SignalModes.Rf;
            window.Sync();
            batch.MachineFileBox.IsChecked = true;
            Check(batch.MachineFileBox.Visibility == Visibility.Visible, "the ZX Spectrum's .scr should be offered");
            batch.Make(Clash.Skip);
            Pump(() => Directory.GetFiles(into, "*.tmp").Length > 0 || batch.Running.IsCompleted, 20000);
            Check(Directory.GetFiles(into, "*.tmp").Length > 0, "the GIF should be being made: " + batch.LastOutcome + ", " + Made());
            batch.StopButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Pump(() => batch.Running.IsCompleted, 30000);
            Check(batch.LastOutcome == "0 made, 0 skipped, 0 failed" && File.ReadAllText(scr) == "there" && Directory.GetFiles(into).Length == 1,
                "stopped while the GIF was made: " + batch.LastOutcome + ", " + Made() + ": " + batch.StatusLine.Text);
            batch.Close();
        }

        // A batch of GIFs and APNGs: an animated GIF as its own frames, a still as the loop picked.
        static void BatchMakesGifs()
        {
            MainWindow window = NewMain(1, false);
            string folder = Path.Combine(Work, "batch-gifs");
            Fresh(folder);
            File.WriteAllBytes(Path.Combine(folder, "a.png"), Encode(new System.Windows.Media.Imaging.PngBitmapEncoder(), Quarters(40, 30)));
            File.WriteAllBytes(Path.Combine(folder, "rukus-breaks-out.gif"), GifFile("gif-rukus"));
            window.Sync();
            BatchWindow batch = window.OpenBatch(new[] { folder });
            batch.GifChoice.IsChecked = true;
            Check(batch.MakeLine.Text.StartsWith("An animated GIF goes out as its own frames, any other picture as the Screen FX loop picked under Export.", StringComparison.Ordinal), "the batch's line: " + batch.MakeLine.Text);
            batch.Make(Clash.Number);
            Pump(() => batch.Running.IsCompleted, 40000);
            Check(batch.LastOutcome == "2 made, 0 skipped, 0 failed", "the GIF batch: " + batch.LastOutcome + ": " + batch.StatusLine.Text);
            string into = Path.Combine(folder, "DeRes");
            var gif = new GifReader(File.ReadAllBytes(Path.Combine(into, "rukus-breaks-out-gameboy.gif")));
            var loop = new GifReader(File.ReadAllBytes(Path.Combine(into, "a-gameboy-screen-fx-loop.gif")));
            Check(gif.Frames().Count() == 58 && gif.Width == 640 && loop.Frames().Count() == 12 && loop.Width == 640, "the GIFs: " + gif.Width + " wide, " + loop.Width + " wide");
            batch.ApngChoice.IsChecked = true;
            batch.Make(Clash.Number);
            Pump(() => batch.Running.IsCompleted, 40000);
            Check(batch.LastOutcome == "2 made, 0 skipped, 0 failed" && File.Exists(Path.Combine(into, "rukus-breaks-out-gameboy-animated.png")) && File.Exists(Path.Combine(into, "a-gameboy-screen-fx-animated.png")),
                "the APNG batch: " + batch.LastOutcome + ": " + string.Join(", ", Directory.GetFiles(into).Select(Path.GetFileName)));
            batch.Close();
        }
    }
}
