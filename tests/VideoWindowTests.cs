using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using B0r3dDeRes.Engine;
using B0r3dDeRes.Video;

namespace B0r3dDeRes.Tests
{
    // A video in the window: its timeline, the paused frame as the picture, playing, the stretch
    // saved, and saving it as MP4 (with the controls' words).
    static partial class TestMain
    {
        // the picture the window should show for the video's paused frame, through its settings
        static byte[] VideoPicture(MainWindow window)
        {
            var d = new Deres();
            d.SetSource(window.OpenedPicture.Video.Source);
            return d.Render(Signal.ForRender(window.Current.Deres.Clone(), window.Current.SignalOn)).Rgb;
        }

        static void Click(Button b) => b.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

        static void VideoInTheWindow()
        {
            NeedVideo();
            MainWindow window = NewMain();
            WaitForPicture(window);
            Check(window.VideoBar.Visibility != Visibility.Visible && window.SaveMp4Button.Visibility != Visibility.Visible, "a picture has no timeline and no Save MP4");
            window.OpenFile(Clip("h264-aac.mp4"));
            Pump(() => !window.Opening, 20000);
            WaitForPicture(window);
            LoadedPicture pic = window.OpenedPicture;
            VideoClip clip = pic.Video;
            Check(clip != null && window.VideoBar.Visibility == Visibility.Visible && window.PlayBar.Visibility != Visibility.Visible && window.SaveMp4Button.Visibility == Visibility.Visible,
                "a video opens with its timeline and Save MP4");
            Check(window.VideoTime.Text == "0:00.00 / 0:02.00" && window.RangeText.Text == "Saves all 2.0 s", "the timeline's words: " + window.VideoTime.Text + " | " + window.RangeText.Text);
            Check(window.Shown.Rgb.SequenceEqual(VideoPicture(window)), "the picture is the video's first frame through the settings");
            Check(window.AnimationKind == Animation.GifFrames && window.AnimStyles.Children.OfType<RadioButton>().First(b => (string)b.Tag == Animation.GifFrames).Content as string == "Video",
                "a video's animation is its own frames, the button says Video");

            // a frame on and a frame back, and the slider to the flash at 1 s
            Pick(window, "nes");
            void WaitForTime(double seconds)
            {
                Pump(() => Math.Abs(clip.Time / 1e7 - seconds) < 0.002 && !window.Rendering && window.ShownFrame == clip.FrameNumber(clip.Time), 15000);
                WaitForPicture(window);
                Check(Math.Abs(clip.Time / 1e7 - seconds) < 0.002, "the paused frame should be at " + seconds + " s, not " + clip.Time / 1e7);
                Check(window.Shown.Rgb.SequenceEqual(VideoPicture(window)), "at " + seconds + " s the picture is that frame through the settings");
            }
            Click(window.StepOnButton);
            WaitForTime(1 / 30.0);
            Click(window.StepOnButton);
            WaitForTime(2 / 30.0);
            Click(window.StepBackButton);
            WaitForTime(1 / 30.0);
            window.TimeSlider.Value = 1.0;
            WaitForTime(1.0);
            Check(window.VideoTime.Text == "0:01.00 / 0:02.00", "the time at the flash: " + window.VideoTime.Text);

            // the stretch saved: from here, to a frame later
            Click(window.SetStartButton);
            window.TimeSlider.Value = 1.5;
            WaitForTime(1.5);
            Click(window.SetStopButton);
            Check(clip.From == (long)(1.0 * VideoFile.Second) && Math.Abs(clip.To / 1e7 - (1.5 + 1 / 30.0)) < 0.002 && window.RangeText.Text == "Saves 0:01.00 to 0:01.53 (0.5 s)",
                "start and stop: " + window.RangeText.Text);
            Check(window.VideoHint.Text.StartsWith("MP4: 1024×960, 16 frames at 30 a second, with its sound", StringComparison.Ordinal), "the MP4's line: " + window.VideoHint.Text);

            // saved as an MP4 at 2×, a slower frame rate, with its sound: the stretch, through the look
            window.Sizes.Children.OfType<RadioButton>().First(b => (int)b.Tag == 2).IsChecked = true;
            window.VideoRates.Children.OfType<RadioButton>().First(b => (int)b.Tag == 15).IsChecked = true;
            string path = Path.Combine(Work, "window.mp4");
            Task<string> saving = window.SaveVideo(path, MainWindow.VideoOut.Mp4);
            Pump(() => saving.IsCompleted, 60000);
            Check(saving.IsCompleted && saving.Result == null, "the MP4 should save: " + (saving.IsCompleted ? saving.Result : "still going"));
            VideoFile back = VideoFile.Open(path);
            var frames = AllFrames(back);
            Check(back.Width == 512 && back.Height == 480 && frames.Count == 8 && Math.Abs(back.Fps - 15) < 0.001 && back.HasSound,
                "the MP4: " + back.Width + "×" + back.Height + ", " + frames.Count + " frames at " + back.Fps + (back.HasSound ? "" : ", no sound"));
            Check(Brightness(frames[0]) > 200 && frames.Skip(1).All(f => Brightness(f) < 200), "its first frame is the flash (it starts at 1 s)");
            Check(window.NoteText == "Saved window.mp4 (512×480, 8 frames).", "the bottom line: " + window.NoteText);
            Check(window.VideoFileName(window.Shown, MainWindow.VideoOut.Mp4) == "h264-aac-nes.mp4" && window.VideoFileName(window.Shown, MainWindow.VideoOut.Gif) == "h264-aac-nes.gif",
                "the names: " + window.VideoFileName(window.Shown, MainWindow.VideoOut.Mp4));

            // playing: the frames go on with the clock; paused, the frame on screen is the picture
            Click(window.WholeButton);
            window.VideoSoundBox.IsChecked = false;
            window.TimeSlider.Value = 0.2;
            WaitForTime(0.2);
            window.Timeline.Play();
            Check(window.Timeline.Playing && !Commands.Compare.CanExecute(null, window) && window.VideoPlayButton.Content as string == "❚❚ Pause", "playing: Compare off, the button says Pause");
            Pump(() => false, 700);
            window.Timeline.Pause();
            Check(!window.Timeline.Playing && clip.Time > (long)(0.3 * VideoFile.Second), "it played on: paused at " + clip.Time / 1e7 + " s");
            WaitForTime(clip.Time / 1e7);
            Check(window.VideoPlayButton.Content as string == "▶ Play", "paused, the button says Play");

            // Reset settings puts a video's frame rate (15 a second, from the save) and Keep the sound back too
            window.VideoKeepSound.IsChecked = false;
            Check(window.Current.VideoRate == 15 && !window.Current.VideoSound, "before Reset settings: " + window.Current.VideoRate + (window.Current.VideoSound ? ", sound" : ", no sound"));
            window.ResetSettings();
            WaitForPicture(window);
            Check(window.Current.VideoRate == 0 && window.Current.VideoSound && window.VideoKeepSound.IsChecked == true &&
                window.VideoRates.Children.OfType<RadioButton>().First(b => (int)b.Tag == 0).IsChecked == true &&
                window.VideoHint.Text.Contains(" frames at 30 a second, with its sound."),
                "Reset settings should put a video's frame rate and Keep the sound back: " + window.Current.VideoRate + (window.Current.VideoSound ? ", sound; " : ", no sound; ") + window.VideoHint.Text);

            // a picture opened after it: the timeline goes
            window.OpenSample("rukus");
            WaitForPicture(window);
            Check(window.VideoBar.Visibility != Visibility.Visible && window.OpenedPicture.Video == null, "a picture after a video has no timeline");
            window.Close();
        }

        // Playing with its sound (to pretend speakers, here): a sound that ends before the picture
        // still plays to the end, the sound goes out where its own times put it, and the slider moved
        // while it plays goes on playing from there. The clock's words and a GIF's frame rate too.
        static void VideoPlaysWithItsSound()
        {
            NeedVideo();
            Check(VideoPlayer.Clock(0) == "0:00.00" && VideoPlayer.Clock(599_960_000) == "1:00.00" && VideoPlayer.Clock(37_230_400_000) == "1:02:03.04" && VideoPlayer.Clock(15_333_333) == "0:01.53",
                "the clock's words: " + VideoPlayer.Clock(599_960_000) + " / " + VideoPlayer.Clock(37_230_400_000));
            Check(VideoExport.AnimationFps(60, false) == 30 && Math.Abs(VideoExport.AnimationFps(59.94, false) - 29.97) < 1e-9 && VideoExport.AnimationFps(50, false) == 50 &&
                VideoExport.AnimationFps(50.3, false) == 50 && VideoExport.AnimationFps(30, false) == 30 && VideoExport.AnimationFps(60, true) == 60 &&
                VideoExport.AnimationFps(120, true) == 60 && VideoExport.AnimationFps(240, false) == 48,
                "a GIF's and an APNG's frame rates: " + VideoExport.AnimationFps(60, false) + " / " + VideoExport.AnimationFps(50.3, false) + " / " + VideoExport.AnimationFps(120, true));

            // where stretches of sound go: right after the one before, a wobbling time or the same time
            // twice (as Windows Server gives the first two) carrying on; a time past it is a gap
            long ms = VideoFile.Second / 1000;
            Check(VideoExport.SoundPlace(-213333, 0, long.MinValue) == -1024 && VideoExport.SoundPlace(-213333, 0, 0) == 0 && VideoExport.SoundPlace(20 * ms, 0, 1024) == 1024 &&
                VideoExport.SoundPlace(100 * ms, 0, 1024) == 4800, "where stretches of sound go: " + VideoExport.SoundPlace(-213333, 0, 0) + ", " + VideoExport.SoundPlace(100 * ms, 0, 1024));

            // 2 s of frames, the sound only for the first 1.2 s, with a beep at 0.5 s
            string path = Path.Combine(Work, "short-sound.mp4");
            using (FileStream file = File.Create(path))
            using (var mp4 = new Mp4Writer(file, 64, 48, 30, true))
            {
                long written = 0, soundEnd = 48000 * 12 / 10;
                for (int k = 0; k < 60; k++)
                {
                    var bgra = Enumerable.Repeat((byte)128, 64 * 48 * 4).ToArray();
                    long time = (long)Math.Round(k * 1e7 / 30), next = (long)Math.Round((k + 1) * 1e7 / 30);
                    mp4.AddFrame(bgra, time, next - time);
                    long until = Math.Min(soundEnd, (long)Math.Round((k + 1) / 30.0 * 48000));
                    if (until <= written) continue;
                    var samples = new short[(until - written) * 2];
                    for (long s = written; s < until; s++)
                    {
                        double t = s / 48000.0;
                        samples[(s - written) * 2] = samples[(s - written) * 2 + 1] = (short)(t >= 0.5 && t < 0.6 ? 16000 * Math.Sin(2 * Math.PI * 1000 * (t - 0.5)) : 0);
                    }
                    mp4.AddSound(samples, 0, samples.Length, written * VideoFile.Second / 48000);
                    written = until;
                }
                mp4.Finish();
            }

            SoundOut heard = null;
            VideoPlayer.OpenSound = () => heard = SoundOut.Pretend();
            try
            {
                MainWindow window = NewMain();
                WaitForPicture(window);
                window.OpenFile(path);
                Pump(() => !window.Opening, 20000);
                WaitForPicture(window);
                VideoClip clip = window.OpenedPicture.Video;
                Check(clip != null && clip.File.HasSound, "the clip with a short sound should open with its sound");
                window.VideoSoundBox.IsChecked = true;
                window.Timeline.Play();
                Pump(() => !window.Timeline.Playing, 8000);
                Check(!window.Timeline.Playing && clip.Time >= (long)(1.9 * VideoFile.Second), "with its sound over at 1.2 s it should still play to its end: " + (window.Timeline.Playing ? "still playing at " + window.Timeline.PlayingAt / 1e7 + " s" : "stopped at " + clip.Time / 1e7 + " s"));
                List<short> sound = heard.Heard;
                double beep = BeepAt(sound.ToArray(), 0);
                Check(Math.Abs(sound.Count / 2 / 48000.0 - 1.2) < 0.04 && Math.Abs(beep - 0.5) < 0.01, "the sound that went out: " + (sound.Count / 2 / 48000.0) + " s, the beep at " + beep + " s");

                // the slider moved while it plays: it plays on from there
                window.TimeSlider.Value = 0;
                Pump(() => clip.Time == 0 && !window.Rendering, 5000);
                window.Timeline.Play();
                Pump(() => window.Timeline.PlayingAt > 0, 3000);
                window.Timeline.Grab();
                window.TimeSlider.Value = 1.5;
                window.Timeline.LetGo();
                Pump(() => window.Timeline.Playing && window.Timeline.PlayingAt >= (long)(1.5 * VideoFile.Second), 5000);
                Check(window.Timeline.Playing && window.Timeline.PlayingAt >= (long)(1.5 * VideoFile.Second), "moved to 1.5 s while playing, it should play on from there: " +
                    (window.Timeline.Playing ? "playing at " + window.Timeline.PlayingAt / 1e7 + " s" : "stopped at " + clip.Time / 1e7 + " s"));
                window.Timeline.Pause();
                window.Close();
            }
            finally
            {
                VideoPlayer.OpenSound = SoundOut.Open;
            }
        }
    }
}
