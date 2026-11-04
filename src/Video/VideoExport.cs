using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using B0r3dDeRes.Engine;

namespace B0r3dDeRes.Video
{
    // What a video goes out with: its stretch, the frame rate, the look, the Size, the sound.
    sealed class VideoExportOptions
    {
        public VideoFile File;
        public long From, To;               // the stretch saved (100 ns steps from its first frame)
        public double Fps;                  // frames a second out: the video's own, or a slower one
        public DeresSettings Settings;
        public int Multiplier = 1;
        public bool Tv;
        public FxSettings Fx;               // the screen effect, when it goes into exports (null: none)
        public SignalSettings Signal;       // the signal (null: off)
        public bool Sound = true;
        public int Lanes = 1;               // frames made at once, each on its own engine
        public AnimationLimits Limits = AnimationLimits.Pc;
        public Action<int, int, string> Progress;
        public CancellationToken Cancel;

        // the same, at another frame rate
        public VideoExportOptions AtFps(double fps)
        {
            var copy = (VideoExportOptions)MemberwiseClone();
            copy.Fps = fps;
            return copy;
        }
    }

    // A video through the look, frame by frame, from its start to its stop: as an MP4 with its
    // sound, as a GIF or APNG, or as numbered PNGs. The frames stream through a few at a time (so
    // the disk sets how long a video can be, not memory), several made at once on every core, then
    // put back in order. Each frame out is the one showing at its time, so a slower frame rate
    // drops frames and the sound keeps its own speed.
    static class VideoExport
    {
        // the frames out from `from` to `to` at `fps`, and frame k's time
        public static int FrameCount(long from, long to, double fps) => Math.Max(1, (int)Math.Ceiling((to - from) / (double)VideoFile.Second * fps - 1e-6));
        public static long TimeOf(int k, long from, double fps) => from + (long)Math.Round(k * VideoFile.Second / fps);

        // The Signal's frame number for a frame at `time`, going out at `fps`: the video's frames in
        // order, stepped as an animated GIF's are (the noise moving at 30 fields a second's pace).
        public static int SignalFrame(long time, double fps) => (int)Math.Round(time / (double)VideoFile.Second * fps) * Engine.Signal.EngineStep(fps);

        // where the screen effect's moving parts are at `time` (its loop takes 1.2 s, as the Screen FX loop's 12 frames at 10 a second)
        public static double PhaseAt(long time)
        {
            double cycles = time / (double)VideoFile.Second / 1.2;
            return cycles - Math.Floor(cycles);
        }

        // ---- the frames, in order

        // The source frame showing at each time asked for, from one reader going forwards: frames
        // asked out of order within a window come from what's been read; an earlier one than
        // that starts the reader again (a GIF made twice reads the video twice).
        sealed class Frames : IDisposable
        {
            readonly VideoFile file;
            readonly long from;
            readonly double fps;
            VideoReader reader;
            readonly Dictionary<int, PictureSource> made = new Dictionary<int, PictureSource>();
            int next;                       // the next frame out not yet made
            VideoFrame current, ahead;

            public Frames(VideoFile file, long from, double fps)
            {
                this.file = file;
                this.from = from;
                this.fps = fps;
            }

            void Restart()
            {
                reader?.Dispose();
                reader = null;
                reader = file.Frames();
                reader.Seek(from);
                made.Clear();
                next = 0;
                current = null;
                ahead = reader.Next(from + VideoReader.Grace);
            }

            public PictureSource Get(int k)
            {
                lock (made)
                {
                    if (made.TryGetValue(k, out PictureSource kept)) return kept;
                    if (reader == null || k < next) Restart();
                    while (next <= k)
                    {
                        long time = TimeOf(next, from, fps);
                        // (frames over by then, at a slower rate out, are passed over unmade)
                        while (ahead != null && ahead.Time <= time + VideoReader.Grace)
                        {
                            current = ahead;
                            ahead = reader.Next(time + VideoReader.Grace);
                        }
                        VideoFrame f = current ?? ahead;
                        if (f == null) throw new VideoException("The video ended before its stop.");
                        made[next] = new PictureSource(f.Bgra, f.Width, f.Height);
                        // what was made a while back won't be asked for again (a window is at most 16)
                        made.Remove(next - 64);
                        next++;
                    }
                    return made[k];
                }
            }

            public void Dispose() => reader?.Dispose();
        }

        // each lane's own engine and the frame out k through the look at the export size, full colour
        sealed class Maker : IDisposable
        {
            readonly VideoExportOptions o;
            readonly Frames frames;
            readonly Deres[] engines;
            public readonly int Width, Height;
            public readonly FxSettings Fx;
            readonly Rendered first;

            public Maker(VideoExportOptions o)
            {
                this.o = o;
                frames = new Frames(o.File, o.From, o.Fps);
                try
                {
                    engines = Enumerable.Range(0, Math.Max(1, o.Lanes)).Select(_ => new Deres()).ToArray();
                    first = Render(0, 0);
                    Export.Size(first.Width, first.Height, first.Geometry.ParX, first.Geometry.ParY, o.Multiplier, o.Tv, out Width, out Height);
                    Fx = Export.WithFx(o.Fx, o.Multiplier) ? o.Fx : null;
                }
                catch
                {
                    // (its reader holds the file and a decoder)
                    frames.Dispose();
                    throw;
                }
            }

            public Rendered Render(int k, int lane)
            {
                if (k == 0 && first != null) return first;
                Deres engine = engines[lane];
                engine.SetSource(frames.Get(k));
                return engine.Render(o.Settings);
            }

            // frame k at the export size: through the signal, the screen effect, or enlarged
            public AnimFrame Frame(int k, int lane)
            {
                Rendered r = Render(k, lane);
                long time = TimeOf(k, o.From, o.Fps);
                double phase = PhaseAt(time);
                if (o.Signal != null) return Engine.Animation.SignalFrame(Engine.Signal.Run(r, o.Settings.ProfileId, o.Signal, SignalFrame(time, o.Fps)), Width, Height, Fx, phase, r.Paper);
                if (Fx != null) return Engine.Animation.FxFrame(r, Width, Height, Fx, phase);
                return Engine.Animation.Enlarged(r, Width, Height);
            }

            public void Dispose() => frames.Dispose();
        }

        // frames 0 … count-1 in order, `lanes` of them made at once
        static IEnumerable<T[]> Windows<T>(int count, int lanes, CancellationToken cancel, Func<int, int, T> make)
        {
            int size = Math.Max(4, lanes);
            for (int start = 0; start < count; start += size)
            {
                cancel.ThrowIfCancellationRequested();
                int n = Math.Min(size, count - start);
                var window = new T[n];
                EveryCore.For(0, Math.Min(Math.Max(1, lanes), n), lane =>
                {
                    for (int i = lane; i < n; i += Math.Max(1, lanes)) window[i] = make(start + i, lane);
                });
                yield return window;
            }
        }

        // the size an MP4 of these frames comes out at: even sides (a side one short gets its last
        // row or column again)
        public static void Mp4Size(int width, int height, out int w, out int h)
        {
            w = width + (width & 1);
            h = height + (height & 1);
        }

        // ---- MP4

        public static AnimationResult Mp4(Stream output, VideoExportOptions o)
        {
            int total = FrameCount(o.From, o.To, o.Fps);
            using (var maker = new Maker(o))
            {
                Mp4Size(maker.Width, maker.Height, out int w, out int h);
                if (!Mp4Writer.Fits(w, h))
                    throw new AnimationException("At this Size the frames are " + maker.Width + "×" + maker.Height + ", more than an MP4 can hold (4096×2304). Pick a smaller Size, or save PNG frames.");
                bool sound = o.Sound && o.File.HasSound;
                VideoReader soundReader = null;
                try
                {
                    if (sound)
                    {
                        soundReader = o.File.Sound();
                        // (from its start it's read as it comes: some Windows time the sound otherwise
                        // after going somewhere, an MP4's edit list then applied after all)
                        if (o.From > 0) soundReader.SeekSound(o.From);
                    }
                    using (var mp4 = new Mp4Writer(output, w, h, o.Fps, sound))
                    {
                        long soundEnd = o.To - o.From;   // the sound stops with the picture
                        long soundWritten = 0;           // samples (a left and a right each) written
                        short[] pending = null;
                        long pendingAt = 0, soundNext = long.MinValue;
                        int done = 0;
                        foreach (AnimFrame[] window in Windows(total, o.Lanes, o.Cancel, maker.Frame))
                        {
                            foreach (AnimFrame f in window)
                            {
                                long time = TimeOf(done, o.From, o.Fps) - o.From, end = TimeOf(done + 1, o.From, o.Fps) - o.From;
                                mp4.AddFrame(Bgra(f, maker.Width, maker.Height, w, h), time, end - time);
                                // the sound up to this frame's end, from where it was read to
                                if (soundReader != null) WriteSound(mp4, soundReader, Math.Min(end, soundEnd), ref soundWritten, ref pending, ref pendingAt, ref soundNext, o.From);
                                done++;
                            }
                            o.Progress?.Invoke(done, total, null);
                        }
                        o.Cancel.ThrowIfCancellationRequested();
                        o.Progress?.Invoke(total, total, "putting it together");
                        mp4.Finish();
                    }
                }
                finally
                {
                    soundReader?.Dispose();
                }
                return new AnimationResult { Width = w, Height = h, Frames = total };
            }
        }

        // How far past the end of the stretch of sound before a stretch's own time must be to leave
        // a gap (silence) there; nearer than that, or before it, the stretch carries straight on.
        public const long SoundSlack = VideoReader.SampleRate * 4 / 1000;

        // Where a stretch of sound goes, in samples after the start: right after the one before
        // (`next`), unless its own time leaves a gap after that (the first goes by its time). The
        // sound's own samples are continuous, while the times wobble at joins (containers keep
        // them to the millisecond) and some Windows give the first two stretches the same time
        // (GitHub's Windows Server does): going by them would cut or repeat sound.
        public static long SoundPlace(long time, long from, long next)
        {
            long labelled = (long)Math.Round((time - from) / (double)VideoFile.Second * VideoReader.SampleRate);
            return next == long.MinValue || labelled > next + SoundSlack ? labelled : next;
        }

        // The sound from where it's been written to up to `until` (the file's own time after its
        // start), as 48 kHz samples, a stretch at a time; what was read past it waits for the next
        // frame (at `pendingAt`). `next`: where the stretch after the last one read goes on.
        static void WriteSound(Mp4Writer mp4, VideoReader reader, long until, ref long written, ref short[] pending, ref long pendingAt, ref long next, long from)
        {
            long wantSamples = until * VideoReader.SampleRate / VideoFile.Second;
            while (written < wantSamples)
            {
                if (pending == null)
                {
                    pending = reader.NextSound(out long t);
                    if (pending == null)
                    {
                        // the sound ended first: silence for the rest
                        var silence = new short[(wantSamples - written) * 2];
                        mp4.AddSound(silence, 0, silence.Length, written * VideoFile.Second / VideoReader.SampleRate);
                        written = wantSamples;
                        return;
                    }
                    pendingAt = SoundPlace(t, from, next);
                    next = pendingAt + pending.Length / 2;
                }
                long at = pendingAt, count = pending.Length / 2;
                if (at + count <= written)
                {
                    // all before the start
                    pending = null;
                    continue;
                }
                if (at > written)
                {
                    // a gap before it: silence
                    long gap = Math.Min(at, wantSamples) - written;
                    var silence = new short[gap * 2];
                    mp4.AddSound(silence, 0, silence.Length, written * VideoFile.Second / VideoReader.SampleRate);
                    written += gap;
                    if (written >= wantSamples) return;
                }
                long skip = written - at, take = Math.Min(count - skip, wantSamples - written);
                mp4.AddSound(pending, (int)(skip * 2), (int)(take * 2), written * VideoFile.Second / VideoReader.SampleRate);
                written += take;
                if (skip + take >= count) pending = null;
                else
                {
                    // the rest waits: drop what's been written from the front
                    var rest = new short[(count - skip - take) * 2];
                    Array.Copy(pending, (skip + take) * 2, rest, 0, rest.Length);
                    pending = rest;
                    pendingAt += skip + take;
                }
            }
        }

        // a frame out as b, g, r, a at the MP4's size (a short side gets its last row or column again)
        static byte[] Bgra(AnimFrame f, int width, int height, int w, int h)
        {
            var bgra = new byte[(long)w * h * 4];
            EveryCore.For(0, h, y =>
            {
                int sy = Math.Min(height - 1, y);
                long o = (long)y * w * 4;
                for (int x = 0; x < w; x++, o += 4)
                {
                    long s = ((long)sy * width + Math.Min(width - 1, x)) * 3;
                    bgra[o] = f.Rgb[s + 2];
                    bgra[o + 1] = f.Rgb[s + 1];
                    bgra[o + 2] = f.Rgb[s];
                    bgra[o + 3] = 255;
                }
            });
            return bgra;
        }

        // ---- GIF and APNG (through the writers animated GIFs use, the video's frames for the GIF's)

        // The frame rate a GIF or APNG of a video goes at: its own, or where that's faster than
        // browsers show (they hold a frame shorter than 2 hundredths, or 10 ms, for a tenth of a
        // second) a GIF goes at 50 a second at most, an APNG at 60. A rate a little over that goes
        // at the most (a frame left out now and then); a faster one at a whole part of itself, so
        // the frames left out are even (60 becomes 30 for a GIF).
        public static double AnimationFps(double fps, bool apng)
        {
            double most = apng ? 60 : 50;
            if (fps <= most * 1.05) return Math.Min(fps, most);
            return fps / Math.Ceiling(fps / most);
        }

        public static AnimationResult Animation(Stream output, VideoExportOptions o, bool apng)
        {
            double fps = AnimationFps(o.Fps, apng);
            VideoExportOptions at = o.AtFps(fps);
            int total = FrameCount(at.From, at.To, fps);
            // each frame's delay in milliseconds, kept over the whole clip to a GIF's hundredths (an APNG's milliseconds)
            var delays = new int[total];
            for (int k = 0; k < total; k++)
                delays[k] = apng ? (int)(Math.Round((k + 1) * 1000 / fps) - Math.Round(k * 1000 / fps)) : (int)(Math.Round((k + 1) * 100 / fps) - Math.Round(k * 100 / fps)) * 10;
            using (var maker = new Maker(at))
            {
                Engine.Animation.PaletteKind(Profiles.ById(at.Settings.ProfileId) ?? Profiles.Default, at.Settings, out bool indexed, out bool shared);
                return Engine.Animation.FramesOf(output, new FramesOptions
                {
                    Apng = apng, Multiplier = at.Multiplier, Tv = at.Tv, Fx = at.Fx, DelaysMs = delays, LoopCount = 0, Lanes = Math.Max(1, at.Lanes),
                    Render = maker.Render,
                    Signal = at.Signal != null ? (Func<Rendered, int, SignalImage>)((r, k) => Engine.Signal.Run(r, at.Settings.ProfileId, at.Signal, SignalFrame(TimeOf(k, at.From, fps), fps))) : null,
                    // the screen effect's moving parts at the video's pace, as in the MP4 and the preview
                    Phase = k => PhaseAt(TimeOf(k, at.From, fps)),
                    Indexed = indexed, SharedPalette = shared, SeeThrough = false,
                    Limits = at.Limits, Progress = at.Progress, Cancel = at.Cancel,
                });
            }
        }

        // ---- PNG frames: base-00001.png on, into `folder`; the files written

        public static List<string> PngFrames(string folder, string baseName, VideoExportOptions o, Func<string, Action<Stream>, string> write)
        {
            int total = FrameCount(o.From, o.To, o.Fps);
            int digits = Math.Max(5, total.ToString().Length);
            var written = new List<string>();
            using (var maker = new Maker(o))
            {
                int done = 0;
                foreach (Rendered[] window in Windows(total, o.Lanes, o.Cancel, maker.Render))
                {
                    foreach (Rendered r in window)
                    {
                        long time = TimeOf(done, o.From, o.Fps);
                        SignalImage signal = o.Signal != null ? Engine.Signal.Run(r, o.Settings.ProfileId, o.Signal, SignalFrame(time, o.Fps)) : null;
                        string path = Path.Combine(folder, baseName + "-" + (done + 1).ToString("D" + digits) + ".png");
                        written.Add(write(path, stream => Export.WritePng(stream, r, o.Multiplier, o.Tv, o.Fx, out _, out _, signal, PhaseAt(time))));
                        done++;
                    }
                    o.Progress?.Invoke(done, total, null);
                }
            }
            return written;
        }
    }
}
