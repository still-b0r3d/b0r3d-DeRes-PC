using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Media.Imaging;
using B0r3dDeRes.Engine;
using B0r3dDeRes.Video;

namespace B0r3dDeRes.Tests
{
    // Videos read through Windows' own video system: the clips in tests\video (made by
    // make-video-clips.sh), each with a red bar along its top, a white flash on frame 30 and a beep
    // from 1.000 s to 1.100 s.
    static partial class TestMain
    {
        static string Clip(string name) => Path.Combine(RepoFolder, "tests", "video", name);

        static void NeedVideo()
        {
            if (!Mf.Available) throw new SkipException("this Windows has no video parts");
        }

        // the average colour of part of a frame (from x0, y0 up to x1, y1)
        static (double R, double G, double B) Average(VideoFrame f, int x0, int y0, int x1, int y1)
        {
            double r = 0, g = 0, b = 0;
            int n = 0;
            for (int y = y0; y < y1; y++)
                for (int x = x0; x < x1; x++)
                {
                    long o = ((long)y * f.Width + x) * 4;
                    b += f.Bgra[o];
                    g += f.Bgra[o + 1];
                    r += f.Bgra[o + 2];
                    n++;
                }
            return (r / n, g / n, b / n);
        }

        // a frame as a PNG (for a look when a test fails, with DERES_DUMP set)
        static void SaveFrame(VideoFrame f, string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(f.Width, f.Height, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null, f.Bgra, f.Width * 4)));
            using (var file = File.Create(path)) encoder.Save(file);
        }

        static bool RedAlongTheTop(VideoFrame f)
        {
            var top = Average(f, f.Width / 4, 1, f.Width * 3 / 4, Math.Max(2, f.Height / 8 - 2));
            return top.R > 170 && top.G < 90 && top.B < 90;
        }

        static bool White(VideoFrame f)
        {
            var all = Average(f, 0, 0, f.Width, f.Height);
            return all.R > 225 && all.G > 225 && all.B > 225;
        }

        // every frame of a clip in order, and every sound sample (48 kHz stereo) with the time it starts
        static List<VideoFrame> AllFrames(VideoFile v)
        {
            var frames = new List<VideoFrame>();
            using (VideoReader r = v.Frames())
                for (VideoFrame f; (f = r.Next()) != null;) frames.Add(f);
            return frames;
        }

        static short[] AllSound(VideoFile v, out long start)
        {
            var all = new List<short>();
            start = 0;
            bool first = true;
            using (VideoReader r = v.Sound())
                for (short[] s; (s = r.NextSound(out long t)) != null;)
                {
                    if (first) start = t;
                    first = false;
                    all.AddRange(s);
                }
            return all.ToArray();
        }

        // when the beep starts, in seconds, or -1
        static double BeepAt(short[] sound, long start)
        {
            for (int i = 0; i + 1 < sound.Length; i += 2)
                if (Math.Abs((int)sound[i]) > 3000 || Math.Abs((int)sound[i + 1]) > 3000) return start / 1e7 + (i / 2) / 48000.0;
            return -1;
        }

        static void VideosOpen()
        {
            NeedVideo();
            var wrong = new List<string>();
            // name, upright size, frames a second, frames, sound, the flash's frame
            foreach (var (name, w, h, fps, frames, sound) in new[]
            {
                ("h264-aac.mp4", 160, 120, 30.0, 60, true), ("rotated.mp4", 96, 160, 30.0, 60, true), ("mono-44k.mp4", 128, 96, 25.0, 50, true),
                ("no-sound.mp4", 128, 72, 60.0, 60, false), ("h264-aac.mkv", 160, 120, 30.0, 60, true), ("mpeg2.mpg", 160, 120, 30.0, 60, true),
                ("wmv.wmv", 160, 120, 30.0, 60, true), ("mjpeg.avi", 160, 120, 30.0, 60, true),
            })
            {
                VideoFile v;
                try
                {
                    v = VideoFile.Open(Clip(name));
                }
                catch (VideoException x)
                {
                    wrong.Add(name + " didn't open: " + x.Message);
                    continue;
                }
                var why = new List<string>();
                if (v.Width != w || v.Height != h) why.Add("size " + v.Width + "×" + v.Height);
                if (Math.Abs(v.Fps - fps) > 0.01) why.Add(v.Fps.ToString("0.###") + " fps");
                if (v.HasSound != sound) why.Add(v.HasSound ? "sound" : "no sound");
                if (Math.Abs(v.Duration / 1e7 - frames / fps) > 0.1) why.Add("lasting " + (v.Duration / 1e7).ToString("0.###") + " s");
                List<VideoFrame> all = AllFrames(v);
                if (all.Count != frames) why.Add(all.Count + " frames");
                else
                {
                    // (the flash covers the bar too)
                    int fallen = all.FindIndex(f => !White(f) && !RedAlongTheTop(f));
                    if (fallen >= 0) why.Add("not upright (frame " + fallen + ")");
                    int flash = all.FindIndex(White);
                    if (flash != 30 || all.Skip(31).Any(White)) why.Add("the flash on frame " + flash);
                    for (int i = 0; i < all.Count; i++)
                        if (Math.Abs(all[i].Time / 1e7 - i / fps) > 0.002)
                        {
                            why.Add("frame " + i + " at " + (all[i].Time / 1e7).ToString("0.####") + " s");
                            break;
                        }
                }
                if (sound)
                {
                    short[] samples = AllSound(v, out long start);
                    double seconds = samples.Length / 2 / 48000.0, beep = BeepAt(samples, start);
                    if (Math.Abs(seconds - frames / fps) > 0.06) why.Add("sound lasting " + seconds.ToString("0.###") + " s");
                    if (Math.Abs(beep - 1) > 0.003) why.Add("the beep at " + beep.ToString("0.####") + " s");
                }
                if (why.Count > 0) wrong.Add(name + ": " + string.Join(", ", why));
            }
            Check(wrong.Count == 0, string.Join("; ", wrong));
        }

        static void VideoFramesByTime()
        {
            NeedVideo();
            VideoFile v = VideoFile.Open(Clip("h264-aac.mp4"));
            using (VideoReader r = v.Frames())
            {
                // the flash frame, from before and after it, and from a seek back
                foreach (double seconds in new[] { 1.0, 1.01, 0.5, 1.02, 1.9, 0.0, 1.0 })
                {
                    VideoFrame f = r.FrameAt((long)(seconds * 1e7));
                    int k = (int)Math.Round(f.Time / 1e7 * 30);
                    Check(k == (int)Math.Floor(seconds * 30 + 1e-9), "the frame at " + seconds + " s is frame " + k);
                    Check(White(f) == (k == 30), "the frame at " + seconds + " s is " + (White(f) ? "" : "not ") + "the flash");
                }
                // and on from there, in order
                VideoFrame next = r.Next();
                Check(next != null && Math.Abs(next.Time / 1e7 - 31 / 30.0) < 0.002, "the frame after the flash comes next");
            }
            // a variable frame rate: every frame at its own time (30 a second, then 10)
            VideoFile vfr = VideoFile.Open(Clip("vfr.mp4"));
            List<VideoFrame> frames = AllFrames(vfr);
            Check(frames.Count == 40, "the variable-rate clip has " + frames.Count + " frames, not 40");
            Check(frames.Take(30).Select((f, i) => Math.Abs(f.Time / 1e7 - i / 30.0) < 0.002).All(ok => ok) &&
                frames.Skip(30).Select((f, i) => Math.Abs(f.Time / 1e7 - (30 + 3 * i) / 30.0) < 0.002).All(ok => ok), "each frame of the variable-rate clip at its own time");
            // there a frame shows until the next one comes, whatever length Windows gives it: the
            // frame at 1.07 s is the 1 s one, and so is the one before the 1.1 s one
            using (VideoReader r = vfr.Frames())
            {
                VideoFrame at = r.FrameAt((long)(1.07 * VideoFile.Second));
                Check(at != null && Math.Abs(at.Time / 1e7 - 1.0) < 0.002, "the frame showing at 1.07 s of the variable-rate clip is at " + at?.Time / 1e7 + " s");
                VideoFrame next = r.Next();
                Check(next != null && Math.Abs(next.Time / 1e7 - 1.1) < 0.002, "the frame after it is at " + next?.Time / 1e7 + " s");
            }
            using (var clip = new VideoClip(vfr, frames[0]))
            {
                VideoFrame before = clip.Before((long)(1.1 * VideoFile.Second));
                Check(before != null && Math.Abs(before.Time / 1e7 - 1.0) < 0.002, "a frame back from 1.1 s of the variable-rate clip lands at " + before?.Time / 1e7 + " s");
            }
        }

        // A clip written here: grey with a red bar along the top, white on frame `flash`, a beep from 1.000 s.
        static void WriteClip(string path, int w, int h, double fps, int frames, bool sound, int flash = 30)
        {
            using (FileStream file = File.Create(path))
            using (var mp4 = new Mp4Writer(file, w, h, fps, sound))
            {
                long written = 0;   // sound samples written (each a left and a right)
                for (int k = 0; k < frames; k++)
                {
                    var bgra = new byte[w * h * 4];
                    for (int i = 0; i < bgra.Length; i += 4)
                    {
                        int y = i / 4 / w;
                        bool white = k == flash, red = y < h / 8;
                        bgra[i] = (byte)(white ? 255 : red ? 0 : 128);
                        bgra[i + 1] = (byte)(white ? 255 : red ? 0 : 128);
                        bgra[i + 2] = (byte)(white ? 255 : red ? 255 : 128);
                        bgra[i + 3] = 255;
                    }
                    long time = (long)Math.Round(k * 1e7 / fps), next = (long)Math.Round((k + 1) * 1e7 / fps);
                    mp4.AddFrame(bgra, time, next - time);
                    if (!sound) continue;
                    long until = (long)Math.Round((k + 1) / fps * 48000);
                    var samples = new short[(until - written) * 2];
                    for (long s = written; s < until; s++)
                    {
                        double t = s / 48000.0;
                        short v = (short)(t >= 1 && t < 1.1 ? 16000 * Math.Sin(2 * Math.PI * 1000 * (t - 1)) : 0);
                        samples[(s - written) * 2] = samples[(s - written) * 2 + 1] = v;
                    }
                    mp4.AddSound(samples, 0, samples.Length, written * VideoFile.Second / 48000);
                    written = until;
                }
                mp4.Finish();
            }
        }

        static void Mp4sWriteAndReadBack()
        {
            NeedVideo();
            var wrong = new List<string>();
            foreach (var (w, h, fps, frames, sound) in new[] { (160, 120, 30.0, 60, true), (96, 160, 30000 / 1001.0, 60, true), (128, 72, 60.0, 90, false), (200, 150, 12.5, 30, true) })
            {
                string path = Path.Combine(Work, "written-" + w + "x" + h + ".mp4");
                WriteClip(path, w, h, fps, frames, sound, Math.Min(30, frames - 5));
                string dump = Environment.GetEnvironmentVariable("DERES_DUMP");
                if (dump != null) File.Copy(path, Path.Combine(dump, Path.GetFileName(path)), true);
                VideoFile v = VideoFile.Open(path);
                List<VideoFrame> all = AllFrames(v);
                var why = new List<string>();
                if (v.Width != w || v.Height != h) why.Add("size " + v.Width + "×" + v.Height);
                if (Math.Abs(v.Fps - fps) > 0.001) why.Add(v.Fps + " fps");
                if (all.Count != frames) why.Add(all.Count + " frames");
                if (all.Any(f => !White(f) && !RedAlongTheTop(f))) why.Add("not upright");
                int flash = all.FindIndex(White);
                if (flash != Math.Min(30, frames - 5)) why.Add("the flash on frame " + flash);
                if (Math.Abs(v.Duration / 1e7 - frames / fps) > 0.002) why.Add("lasting " + (v.Duration / 1e7) + " s");
                if (v.HasSound != sound) why.Add(v.HasSound ? "sound" : "no sound");
                if (sound)
                {
                    short[] samples = AllSound(v, out long start);
                    double beep = BeepAt(samples, start), flashAt = all[flash].Time / 1e7;
                    // in step: the beep where it was written, and the flash where it was
                    if (Math.Abs(beep - 1) > 0.003) why.Add("the beep at " + beep.ToString("0.####") + " s");
                    if (Math.Abs(flashAt - Math.Min(30, frames - 5) / fps) > 0.002) why.Add("the flash at " + flashAt.ToString("0.####") + " s");
                }
                if (why.Count > 0) wrong.Add(w + "×" + h + " at " + fps.ToString("0.###") + ": " + string.Join(", ", why));
            }
            Check(wrong.Count == 0, string.Join("; ", wrong));
            // what an MP4 can't hold
            Check(!Mp4Writer.Fits(161, 120) && !Mp4Writer.Fits(160, 121) && Mp4Writer.Fits(4096, 2304) && Mp4Writer.Fits(2304, 4096) && !Mp4Writer.Fits(4096, 2306) && !Mp4Writer.Fits(0, 0),
                "even sides up to H.264's area");
        }

        static double Brightness(VideoFrame f)
        {
            var a = Average(f, 0, 0, f.Width, f.Height);
            return (a.R + a.G + a.B) / 3;
        }

        static void VideosSaveThroughTheLook()
        {
            NeedVideo();
            VideoFile v = VideoFile.Open(Clip("h264-aac.mp4"));
            VideoExportOptions Options(double fps) => new VideoExportOptions
            {
                File = v, From = 0, To = v.Duration, Fps = fps, Settings = new DeresSettings { ProfileId = "nes" }, Multiplier = 2,
                Signal = new SignalSettings { Mode = SignalModes.Rf }, Lanes = 2,
            };
            var wrong = new List<string>();
            // at its own speed and at a retro 15 a second: every frame through the NES on RF, the sound kept
            foreach (double fps in new[] { 30.0, 15.0 })
            {
                string path = Path.Combine(Work, "export-" + fps + ".mp4");
                AnimationResult result = null;
                Exports.Write(path, stream => result = VideoExport.Mp4(stream, Options(fps)));
                VideoFile back = VideoFile.Open(path);
                List<VideoFrame> frames = AllFrames(back);
                int expected = (int)Math.Round(2 * fps);
                var why = new List<string>();
                // the NES's 256 × 240 at 2×, its pixels as wide as tall here (raw)
                if (back.Width != 512 || back.Height != 480 || result.Width != 512) why.Add("size " + back.Width + "×" + back.Height);
                if (frames.Count != expected || result.Frames != expected) why.Add(frames.Count + " frames");
                if (Math.Abs(back.Fps - fps) > 0.001) why.Add(back.Fps + " fps");
                int flash = frames.FindIndex(f => Brightness(f) > 200);
                if (flash != (int)Math.Round(fps)) why.Add("the flash on frame " + flash);
                if (!back.HasSound) why.Add("no sound");
                else
                {
                    double beep = BeepAt(AllSound(back, out long start), start);
                    if (Math.Abs(beep - 1) > 0.003) why.Add("the beep at " + beep.ToString("0.####") + " s");
                }
                if (why.Count > 0) wrong.Add(fps + " fps: " + string.Join(", ", why));
            }
            // part of it, without its sound
            {
                VideoExportOptions o = Options(30);
                o.From = (long)(0.5 * VideoFile.Second);
                o.To = (long)(1.5 * VideoFile.Second);
                o.Sound = false;
                string path = Path.Combine(Work, "export-part.mp4");
                Exports.Write(path, stream => VideoExport.Mp4(stream, o));
                VideoFile back = VideoFile.Open(path);
                List<VideoFrame> frames = AllFrames(back);
                if (frames.Count != 30 || back.HasSound || frames.FindIndex(f => Brightness(f) > 200) != 15) wrong.Add("0.5 s to 1.5 s: " + frames.Count + " frames, the flash on frame " + frames.FindIndex(f => Brightness(f) > 200) + (back.HasSound ? ", sound" : ""));
            }
            // GIF and APNG of it
            foreach (bool apng in new[] { false, true })
            {
                string path = Path.Combine(Work, "export." + (apng ? "png" : "gif"));
                AnimationResult result = null;
                Exports.Write(path, stream => result = VideoExport.Animation(stream, Options(15), apng));
                using (FileStream file = File.OpenRead(path))
                {
                    int w, h, n;
                    bool ok = apng ? ApngInfo.Shape(file, out w, out h, out n) : GifInfo.Shape(file, out w, out h, out n);
                    if (!ok || w != 512 || h != 480 || n != 30) wrong.Add((apng ? "APNG" : "GIF") + ": " + w + "×" + h + ", " + n + " frames");
                }
            }
            // PNG frames, numbered
            {
                string folder = Path.Combine(Work, "frames");
                Directory.CreateDirectory(folder);
                VideoExportOptions o = Options(10);
                List<string> files = VideoExport.PngFrames(folder, "clip-nes", o, (p, write) => Exports.Write(p, write));
                if (files.Count != 20 || !File.Exists(Path.Combine(folder, "clip-nes-00001.png")) || !File.Exists(Path.Combine(folder, "clip-nes-00020.png")))
                    wrong.Add("PNG frames: " + files.Count + " (" + string.Join(", ", files.Take(2).Select(Path.GetFileName)) + ")");
            }
            // stopped part way: no file
            {
                string path = Path.Combine(Work, "stopped.mp4");
                var cancel = new System.Threading.CancellationTokenSource();
                VideoExportOptions o = Options(30);
                o.Cancel = cancel.Token;
                o.Progress = (done, total, note) => { if (done >= 8) cancel.Cancel(); };
                bool stopped = false;
                try
                {
                    Exports.Write(path, stream => VideoExport.Mp4(stream, o));
                }
                catch (OperationCanceledException)
                {
                    stopped = true;
                }
                if (!stopped || File.Exists(path) || Directory.GetFiles(Work, "stopped*").Length > 0) wrong.Add("Stop left " + string.Join(", ", Directory.GetFiles(Work, "stopped*").Select(Path.GetFileName)));
            }
            Check(wrong.Count == 0, string.Join("; ", wrong));
        }

        // Only by hand (DERES_MEASURE=hlg): how long an HLG frame of an iPhone's 4K takes to come
        // into normal colour, on every core and on one (playing needs it in well under 33 ms).
        static void MeasureHlg()
        {
            if (Environment.GetEnvironmentVariable("DERES_MEASURE") != "hlg") throw new SkipException("only by hand (DERES_MEASURE=hlg)");
            const int w = 3840, h = 2160;
            var luma = new ushort[w * h];
            var cb = new ushort[w / 2 * (h / 2)];
            var cr = new ushort[cb.Length];
            // a photo-like spread: mostly middle greys and soft colours, some shadows and highlights
            var random = new Random(1);
            for (int i = 0; i < luma.Length; i++) luma[i] = (ushort)Math.Max(64, Math.Min(940, 400 + (int)(random.NextDouble() * random.NextDouble() * 500) - 120));
            for (int i = 0; i < cb.Length; i++)
            {
                cb[i] = (ushort)(512 + random.Next(-90, 90));
                cr[i] = (ushort)(512 + random.Next(-90, 90));
            }
            Hdr.HlgToBgra(luma, cb, cr, w, h);
            var watch = System.Diagnostics.Stopwatch.StartNew();
            for (int k = 0; k < 5; k++) Hdr.HlgToBgra(luma, cb, cr, w, h);
            Console.WriteLine("      4K HLG frame, every core: " + watch.ElapsedMilliseconds / 5 + " ms");
            watch.Restart();
            for (int i = 0; i < luma.Length; i++) Hdr.Pixel(luma[i], cb[(i / w >> 1) * (w / 2) + (i % w >> 1)], cr[(i / w >> 1) * (w / 2) + (i % w >> 1)], out _, out _, out _);
            Console.WriteLine("      4K HLG frame, one core, its pixels alone: " + watch.ElapsedMilliseconds + " ms (" + (watch.Elapsed.TotalMilliseconds * 1e6 / luma.Length).ToString("0.0") + " ns a pixel)");
        }

        static void HdrVideosInNormalColour()
        {
            // the conversion itself, on the HDR clip's first frame (raw 10-bit planes, decoded by
            // ffmpeg), against the picture it was made from: no decoder needed, so it runs everywhere
            byte[] raw = File.ReadAllBytes(Clip("hlg-frame0.yuv"));
            int w = 160, h = 120, cw = w / 2, ch = h / 2;
            ushort Word(int i) => (ushort)(raw[i * 2] | raw[i * 2 + 1] << 8);
            var luma = new ushort[w * h];
            var cb = new ushort[cw * ch];
            var cr = new ushort[cw * ch];
            for (int i = 0; i < luma.Length; i++) luma[i] = Word(i);
            for (int i = 0; i < cb.Length; i++)
            {
                cb[i] = Word(luma.Length + i);
                cr[i] = Word(luma.Length + cb.Length + i);
            }
            byte[] bgra = Hdr.HlgToBgra(luma, cb, cr, w, h);
            var sdr = new FormatConvertedBitmap(BitmapFrame.Create(new Uri(Clip("hlg-frame0-sdr.png")), BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad), System.Windows.Media.PixelFormats.Bgra32, null, 0);
            var expected = new byte[w * h * 4];
            sdr.CopyPixels(expected, w * 4, 0);
            // the chart: grey steps (rows 15-39), colour patches (40-79), a grey ramp (80-119). Greys
            // come back as they were below the highlights' knee (any HLG conversion agrees on
            // greys); colours keep their hue (ffmpeg's converter, which made the clip, turns each
            // channel on its own where the standard goes by brightness, so saturated ones differ)
            double greyDiff = 0;
            int greys = 0;
            var wrongGreys = new List<string>();
            foreach (int y in Enumerable.Range(17, 21).Concat(Enumerable.Range(82, 36)))
                for (int x = 0; x < w; x++)
                {
                    int i = (y * w + x) * 4;
                    if (expected[i + 1] > 240) continue;
                    int d = Math.Max(Math.Abs(bgra[i] - expected[i]), Math.Max(Math.Abs(bgra[i + 1] - expected[i + 1]), Math.Abs(bgra[i + 2] - expected[i + 2])));
                    greyDiff += d;
                    greys++;
                    if (d > 6 && wrongGreys.Count < 4) wrongGreys.Add(expected[i + 1] + " came out " + bgra[i + 2] + "," + bgra[i + 1] + "," + bgra[i]);
                }
            Console.WriteLine("      HLG: greys within " + (greyDiff / greys).ToString("0.00") + " a pixel on average");
            Check(greyDiff / greys < 2.5 && wrongGreys.Count == 0, "greys from the HLG frame are off by " + (greyDiff / greys).ToString("0.00") + " on average: " + string.Join(", ", wrongGreys));
            var patches = new List<string>();
            for (int k = 0; k < 8; k++)
            {
                int i = (60 * w + k * 20 + 10) * 4;
                int[] got = { bgra[i + 2], bgra[i + 1], bgra[i] }, want = { expected[i + 2], expected[i + 1], expected[i] };
                patches.Add(string.Join(",", want) + " came out " + string.Join(",", got));
                // the channels keep their order (the hue), and the strongest stays near (pure blue,
                // the darkest colour, differs most between the two ways: 255 comes back as 210)
                bool order = true;
                for (int a = 0; a < 3; a++)
                    for (int b = 0; b < 3; b++)
                        if (want[a] > want[b] + 10 && got[a] < got[b] - 3) order = false;
                int strongest = Array.IndexOf(want, want.Max());
                Check(order && Math.Abs(got[strongest] - want[strongest]) <= 50, "the colour " + string.Join(",", want) + " came out " + string.Join(",", got));
            }
            Console.WriteLine("      HLG colours: " + string.Join("; ", patches));
            // black stays black, reference white is white, and an HLG grey ramp goes up
            Hdr.Pixel(64, 512, 512, out byte r0, out byte g0, out byte b0);
            Hdr.Pixel(64 + (int)Math.Round(0.75 * 876), 512, 512, out byte r1, out byte g1, out byte b1);
            Check(r0 == 0 && g0 == 0 && b0 == 0 && r1 >= 245 && g1 >= 245 && b1 >= 245 && Math.Abs(r1 - g1) <= 1 && Math.Abs(g1 - b1) <= 1, "black and reference white");
            byte last = 0;
            for (int y = 64; y <= 940; y += 4)
            {
                Hdr.Pixel(y, 512, 512, out byte gr, out _, out _);
                Check(gr >= last, "an HLG grey ramp only goes up (" + y + ")");
                last = gr;
            }
            // and the whole clip, where Windows has an HEVC decoder (not GitHub's machine)
            VideoFile v;
            try
            {
                v = VideoFile.Open(Clip("hlg.mp4"));
            }
            catch (VideoException x) when (x.Message.Contains("HEVC"))
            {
                throw new SkipException("the conversion is right; the whole clip needs Microsoft's HEVC extension: " + x.Message);
            }
            Check(v.Hlg && v.Width == w && v.Height == h, "the HDR clip opens as HLG, " + v.Width + "×" + v.Height);
            List<VideoFrame> frames = AllFrames(v);
            Check(frames.Count == 60 && frames.All(f => White(f) || RedAlongTheTop(f)) && frames.FindIndex(White) == 30, "the HDR clip's 60 frames upright, the flash on frame 30");
            Check(frames[0].Bgra.SequenceEqual(bgra), "Windows' decoder gives the same first frame as ffmpeg's");
            // marked as a phone marks portrait video: DeRes turns HDR frames itself (Windows' video
            // processor isn't used for them), the way Windows turns the others (the SDR clip above),
            // a quarter counter-clockwise for this marking, as ffmpeg and browsers do
            VideoFile turned = VideoFile.Open(Clip("hlg-rotated.mp4"));
            Check(turned.Hlg && turned.Width == h && turned.Height == w, "the turned HDR clip opens " + turned.Width + "×" + turned.Height);
            List<VideoFrame> upright = AllFrames(turned);
            Check(upright.Count == 60 && upright.FindIndex(White) == 30, "the turned HDR clip has its 60 frames, the flash on frame 30");
            bool same = true;
            for (int y = 0; y < h && same; y++)
                for (int x = 0; x < w && same; x++)
                {
                    long from = ((long)y * w + x) * 4, to = ((long)(w - 1 - x) * h + y) * 4;
                    for (int c = 0; c < 4; c++) same &= frames[0].Bgra[from + c] == upright[0].Bgra[to + c];
                }
            Check(same, "the turned HDR clip's first frame is the plain one turned a quarter counter-clockwise");
        }
    }
}
