using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Controls;
using B0r3dDeRes.Engine;
using B0r3dDeRes.Video;

namespace B0r3dDeRes.Tests
{
    // Videos in a batch: an MP4 each through the look, with its sound (none for one without), at
    // the Size or the biggest an MP4 holds; a GIF and an APNG of a video's frames at a slower
    // frame rate; a picture beside them as before; every file read back.
    static partial class TestMain
    {
        static void VideoBatches()
        {
            NeedVideo();
            MainWindow window = NewMain();
            WaitForPicture(window);
            Pick(window, "nes");
            PickSignal(window, SignalModes.Rf);
            void PickSize(int m) => window.Sizes.Children.OfType<RadioButton>().First(b => (int)b.Tag == m).IsChecked = true;
            PickSize(2);
            string folder = Path.Combine(Work, "video-batch");
            Fresh(folder);
            File.Copy(Clip("h264-aac.mp4"), Path.Combine(folder, "clip.mp4"));
            File.Copy(Clip("no-sound.mp4"), Path.Combine(folder, "quiet.mp4"));
            File.WriteAllBytes(Path.Combine(folder, "a.png"), Encode(new System.Windows.Media.Imaging.PngBitmapEncoder(), Quarters(40, 30)));
            string into = Path.Combine(folder, "DeRes");
            BatchWindow batch = window.OpenBatch(new[] { folder });
            Check(batch.PicturesLine.Text == "1 picture and 2 videos from " + folder &&
                batch.MakeLine.Text.EndsWith(" A video goes out whole as an MP4 with its sound, at 2× or the biggest Size an MP4 holds, at its own frame rate.", StringComparison.Ordinal),
                "the batch's lines: " + batch.PicturesLine.Text + " / " + batch.MakeLine.Text);
            var lap = System.Diagnostics.Stopwatch.StartNew();
            void MakeAll(string outcome)
            {
                batch.Make(Clash.Number);
                Pump(() => batch.Running.IsCompleted, 60000);
                // how long it took, in the output (a slow machine shows where its time goes)
                Console.WriteLine("      a batch made in " + (lap.ElapsedMilliseconds / 1000.0).ToString("0.0") + " s: " + batch.LastOutcome);
                lap.Restart();
                Check(batch.Running.IsCompleted && batch.LastOutcome == outcome,
                    "the batch came out " + batch.LastOutcome + ", not " + outcome + ": " + batch.StatusLine.Text + " " + string.Join(" ", batch.OutcomeList.Items.OfType<TextBlock>().Select(t => t.Text)));
            }

            // through RF at 2×: every frame (the flash on its own), 30 a second, the beep at 1 s
            MakeAll("3 made, 0 skipped, 0 failed");
            VideoFile clip = VideoFile.Open(Path.Combine(into, "clip-nes-rf.mp4"));
            List<VideoFrame> frames = AllFrames(clip);
            Check(clip.Width == 512 && clip.Height == 480 && frames.Count == 60 && Math.Abs(clip.Fps - 30) < 0.01 && clip.HasSound,
                "the MP4: " + clip.Width + "×" + clip.Height + ", " + frames.Count + " frames at " + clip.Fps + (clip.HasSound ? "" : ", no sound"));
            Check(Enumerable.Range(0, 60).All(i => Brightness(frames[i]) > 200 == (i == 30)), "the flash should be frame 30 alone");
            double beep = BeepAt(AllSound(clip, out long start), start);
            Check(Math.Abs(beep - 1.0) < 0.01, "the beep should be at 1 s, not " + beep);
            VideoFile quiet = VideoFile.Open(Path.Combine(into, "quiet-nes-rf.mp4"));
            Check(!quiet.HasSound && quiet.Width == 512, "the video without sound: " + (quiet.HasSound ? "has sound" : quiet.Width + " wide"));
            Check(File.Exists(Path.Combine(into, "a-nes-256x240-2x-rf.png")), "the picture's PNG: " + string.Join(", ", Directory.GetFiles(into).Select(Path.GetFileName)));

            // at 16× it's more than an MP4 holds: the biggest Size that does, at 10 a second (the signal
            // off; the batch read every frame back, so here its size and speed are enough)
            PickSignal(window, SignalModes.Off);
            PickSize(16);
            window.Current.VideoRate = 10;
            Click(batch.ClearButton);
            batch.Add(new[] { Path.Combine(folder, "clip.mp4") });
            Check(batch.MakeLine.Text.EndsWith(" A video goes out whole as an MP4 with its sound, at 16× or the biggest Size an MP4 holds, at 10 frames a second (or its own, if that's slower).", StringComparison.Ordinal),
                "the batch's line at 16×: " + batch.MakeLine.Text);
            MakeAll("1 made, 0 skipped, 0 failed");
            int m = Export.Multipliers.Where(k => k <= 16 && Mp4Writer.Fits(256 * k, 240 * k)).Max();
            VideoFile big = VideoFile.Open(Path.Combine(into, "clip-nes.mp4"));
            Check(m == 12 && big.Width == 256 * m && big.Height == 240 * m && Math.Abs(big.Duration / 1e7 - 2) < 0.01 && Math.Abs(big.Fps - 10) < 0.01 && big.HasSound,
                "at 16×: " + big.Width + "×" + big.Height + " (" + m + "×), " + big.Duration / 1e7 + " s at " + big.Fps);

            // a GIF and an APNG of its frames, at 2×
            PickSize(2);
            batch.GifChoice.IsChecked = true;
            Check(batch.MakeLine.Text.EndsWith(" A video goes out whole as a GIF of its frames, at 10 frames a second (or its own, if that's slower).", StringComparison.Ordinal), "the GIF's line: " + batch.MakeLine.Text);
            MakeAll("1 made, 0 skipped, 0 failed");
            batch.ApngChoice.IsChecked = true;
            MakeAll("1 made, 0 skipped, 0 failed");
            foreach (var (name, gif) in new[] { ("clip-nes.gif", true), ("clip-nes-animated.png", false) })
                using (FileStream f = File.OpenRead(Path.Combine(into, name)))
                {
                    int w, h, n;
                    bool whole = gif ? GifInfo.Shape(f, out w, out h, out n) : ApngInfo.Shape(f, out w, out h, out n);
                    Check(whole && w == 512 && h == 480 && n == 20, name + ": " + w + "×" + h + ", " + n + " frames");
                }
            batch.Close();
            window.Close();
        }
    }
}
