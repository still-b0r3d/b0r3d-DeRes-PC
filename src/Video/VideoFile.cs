using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using B0r3dDeRes.Engine;

namespace B0r3dDeRes.Video
{
    // A frame of a video, upright: b, g, r, a a pixel (a always 255), the top row first.
    sealed class VideoFrame
    {
        public byte[] Bgra;
        public int Width, Height;
        public long Time;        // when it shows, in 100 ns steps from the start
        public long Duration;    // how long it shows (0: not said)
    }

    // A video file: what it holds, read by Windows' own video system. Each VideoReader made from it
    // opens the file again for itself, so several can read at once (the paused frame, the playing
    // frames, the sound, a save).
    sealed class VideoFile
    {
        public string Path, Name;
        public int Width, Height;        // the frame upright
        public double Fps;               // its frames a second, as the file says
        public long Duration;            // 100 ns steps
        public int Rotation;             // turned this many degrees to stand upright
        public bool Hlg;                 // HLG HDR (an iPhone's): turned into normal colour by DeRes itself
        public string VideoKind, SoundKind;
        public bool HasSound;
        // Windows' own times run this much later than the video's: where its first frame shows, and
        // each track's edit-list shift (an MP4's or MOV's, which Windows doesn't apply)
        internal long Start, VideoShift, SoundShift;

        public const long Second = 10000000;

        // one frame's length at its own speed
        public long FrameLength => Fps > 0 ? (long)Math.Round(Second / Fps) : Second / 30;

        // the file's frames counted at its own speed (a guess for one that varies)
        public long FrameCount => Math.Max(1, (long)Math.Round(Duration / (double)Second * (Fps > 0 ? Fps : 30)));

        static readonly string[] extensions = { ".mp4", ".m4v", ".mov", ".mkv", ".webm", ".avi", ".wmv", ".asf", ".mpg", ".mpeg", ".m2ts", ".mts", ".ts", ".3gp", ".3g2" };
        public static string[] Extensions => (string[])extensions.Clone();
        public static bool LooksLikeVideo(string path)
        {
            try
            {
                return Array.IndexOf(extensions, System.IO.Path.GetExtension(path ?? "").ToLowerInvariant()) >= 0;
            }
            catch (ArgumentException)
            {
                // no proper path: opening it will say so
                return false;
            }
        }

        // Opens a video and reads what it holds. Throws VideoException in words for the window when
        // it can't (Windows has no video parts, or no decoder for the file's picture).
        public static VideoFile Open(string path) => VideoReader.Mta(() => OpenHere(path));

        static VideoFile OpenHere(string path)
        {
            Mf.Start();
            if (!File.Exists(path)) throw new VideoException("The file isn't there any more.");
            var v = new VideoFile { Path = path, Name = System.IO.Path.GetFileName(path) };
            using (var probe = new VideoReader(v, VideoReader.Purpose.Look))
            {
                IMFSourceReader r = probe.Reader;
                int video = -1, sound = -1;
                IMFMediaType videoType = null;
                for (uint i = 0; ; i++)
                {
                    int hr = r.GetNativeMediaType(i, 0, out IMFMediaType t);
                    if (hr == Mf.NoMoreStreams) break;
                    if (hr < 0) break;
                    Guid major = Mf.GuidOf(t, G.MajorType) ?? Guid.Empty, sub = Mf.GuidOf(t, G.Subtype) ?? Guid.Empty;
                    if (major == G.Video && video < 0)
                    {
                        video = (int)i;
                        videoType = t;
                        v.VideoKind = G.Name(sub);
                        continue;
                    }
                    if (major == G.Audio && sound < 0)
                    {
                        sound = (int)i;
                        v.SoundKind = G.Name(sub);
                    }
                    Mf.Release(t);
                }
                if (video < 0) throw new VideoException("There's no picture in it, only sound.");
                try
                {
                    v.Rotation = (int)(Mf.U32(videoType, G.VideoRotation) ?? 0) % 360;
                    v.Hlg = Mf.U32(videoType, G.TransferFunction) == 16;   // MFVideoTransFunc_HLG
                    ulong? rate = Mf.U64(videoType, G.FrameRate);
                    if (rate != null && (rate.Value & 0xFFFFFFFF) != 0) v.Fps = (double)(rate.Value >> 32) / (rate.Value & 0xFFFFFFFF);
                }
                finally
                {
                    Mf.Release(videoType);
                }
                Guid duration = G.Duration;
                if (r.GetPresentationAttribute(0xFFFFFFFF, ref duration, out PropVariant d) >= 0) v.Duration = d.Long;
                v.HasSound = sound >= 0;
            }
            long videoShift = 0, soundShift = 0;
            string extension = System.IO.Path.GetExtension(path).ToLowerInvariant();
            if (extension == ".mp4" || extension == ".m4v" || extension == ".mov" || extension == ".3gp" || extension == ".3g2")
                Mp4Edits.Read(path, out videoShift, out soundShift);
            long rawDuration = v.Duration;
            // the picture's size, where it starts and its real frame rate come from a reader set up the
            // way frames are read (its times are Windows' own until the start is known)
            using (var reader = new VideoReader(v, VideoReader.Purpose.Frames))
            {
                v.Width = reader.FrameWidth;
                v.Height = reader.FrameHeight;
                if (reader.Fps > 0 && !(v.Fps > 0)) v.Fps = reader.Fps;
                // the first frame must decode: a file whose picture Windows can't decode stops here
                // (its frames' times only are read here: none are made)
                var times = new System.Collections.Generic.List<long>();
                for ((long Time, long Duration)? f; times.Count < 13 && (f = reader.NextTime()) != null;) times.Add(f.Value.Time);
                if (times.Count == 0) throw new VideoException("Windows found no frames in it.");
                // An edit list Windows did apply after all would put the first frame before the start.
                // Each track's is judged on its own: some Windows apply the picture's and not the
                // sound's (GitHub's Windows Server 2025 does), the sound's is judged below.
                if (times[0] - videoShift < -v.FrameLength / 2) videoShift = 0;
                v.VideoShift = videoShift;
                v.SoundShift = soundShift;
                v.Start = times[0] - videoShift;
                // the frame rate the frames keep (Windows says half of Matroska's)
                if (times.Count >= 3)
                {
                    // over the frames read (a file's times can be rounded to milliseconds)
                    double measured = Second * (times.Count - 1) / (double)Math.Max(1, times[times.Count - 1] - times[0]);
                    // Windows' rate when it's near; twice it when that's nearer (Matroska); else the measured one
                    if (v.Fps > 0 && Math.Abs(measured / (2 * v.Fps) - 1) < 0.02) v.Fps *= 2;
                    else if (!(v.Fps > 0) || Math.Abs(measured / v.Fps - 1) > 0.03) v.Fps = Snap(measured);
                }
                // how long it runs: to the end of its last frame (a file's own length can be a guess)
                reader.Seek(Math.Max(0, rawDuration - v.Start - 3 * Second));
                (long Time, long Duration)? last = null;
                for ((long Time, long Duration)? f; (f = reader.NextTime()) != null;) last = f;
                if (last != null) v.Duration = last.Value.Time + (last.Value.Duration > 0 ? last.Value.Duration : v.FrameLength);
                else v.Duration = Math.Max(v.FrameLength, rawDuration - v.Start);
            }
            if (v.HasSound)
                try
                {
                    using (var sound = new VideoReader(v, VideoReader.Purpose.Sound))
                    {
                        // the sound's edit list, applied by Windows after all, puts its first stretch
                        // well before its start (Windows' own time below 0)
                        if (v.SoundShift > 0 && sound.NextSound(out long first) != null && first + v.SoundShift + v.Start < -v.SoundShift / 2) v.SoundShift = 0;
                    }
                }
                catch (VideoException)
                {
                    // the picture opens without its sound
                    v.HasSound = false;
                }
            return v;
        }

        public VideoReader Frames() => new VideoReader(this, VideoReader.Purpose.Frames);
        public VideoReader Sound() => new VideoReader(this, VideoReader.Purpose.Sound);

        // a measured frame rate as the usual one it's near
        static double Snap(double fps)
        {
            foreach (double usual in new[] { 24000 / 1001.0, 24, 25, 30000 / 1001.0, 30, 48, 50, 60000 / 1001.0, 60, 100, 120, 15, 12, 10 })
                if (Math.Abs(fps / usual - 1) < 0.005) return usual;
            return Math.Round(fps, 3);
        }
    }

    // One reader of a video file: its frames in order from a time (upright, b, g, r, a), or its
    // sound (16-bit samples, 48 kHz stereo), on any one thread at a time.
    sealed class VideoReader : IDisposable
    {
        public enum Purpose { Look, Frames, Sound }

        const uint FirstVideo = 0xFFFFFFFC, FirstAudio = 0xFFFFFFFD, AllStreams = 0xFFFFFFFE;
        const uint ErrorFlag = 1, EndOfStream = 2, TypeChanged = 0x20, Tick = 0x100;
        public const int SampleRate = 48000, Channels = 2;
        // how far a frame's time may be past the time asked for and still be the one showing then
        public const long Grace = 10000;

        readonly VideoFile file;
        readonly Purpose purpose;
        FileStream stream;
        IMFByteStream bytes;
        internal IMFSourceReader Reader;
        public int FrameWidth, FrameHeight;
        public double Fps;
        int pitchWidth, pitchHeight;      // the decoder's frame (HLG) before it's cut to the picture and turned
        int cropX, cropY;
        bool hlg;                         // read as 10-bit and converted by DeRes (an HLG video whose decoder gives 10 bits)
        bool p010, p010Refused;
        // sound: the reader's own rate and channels when Windows wouldn't make it 48 kHz stereo
        int soundRate, soundChannels;
        bool convertSound;
        bool ended;

        public VideoReader(VideoFile file, Purpose purpose)
        {
            this.file = file;
            this.purpose = purpose;
            hlg = file.Hlg;
            try
            {
                Mta(SetUp);
            }
            catch (VideoException) when (p010Refused)
            {
                // marked HLG, but its decoder only gives 8 bits (some cameras' HLG): Windows' own conversion then
                hlg = false;
                Mta(SetUp);
            }
        }

        // Windows' video objects live on the free-threaded side: they're made and used there, so a
        // call from the window's thread goes over to the video thread, one of b0r3d DeRes's own that
        // stays for good, and waits for it there. Not a shared thread: on two cores those can all be
        // waiting on this very call. Nor a new one each time: Windows Server hangs on that.
        internal static T Mta<T>(Func<T> f)
        {
            if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA) return f();
            var done = new TaskCompletionSource<T>();
            videoCalls.Add(() =>
            {
                try
                {
                    done.SetResult(f());
                }
                catch (Exception x)
                {
                    done.SetException(x);
                }
            });
            return done.Task.GetAwaiter().GetResult();
        }

        // the calls waiting for the video thread, which is started with it
        static readonly System.Collections.Concurrent.BlockingCollection<Action> videoCalls = StartVideoThread();

        static System.Collections.Concurrent.BlockingCollection<Action> StartVideoThread()
        {
            var calls = new System.Collections.Concurrent.BlockingCollection<Action>();
            var t = new Thread(() =>
            {
                foreach (Action call in calls.GetConsumingEnumerable()) call();
            })
            { IsBackground = true, Name = "b0r3d DeRes video" };
            t.SetApartmentState(ApartmentState.MTA);
            t.Start();
            return calls;
        }

        internal static void Mta(Action a) => Mta(() =>
        {
            a();
            return true;
        });

        void SetUp()
        {
            try
            {
                stream = new FileStream(file.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16, FileOptions.RandomAccess);
            }
            catch (Exception x) when (x is IOException || x is UnauthorizedAccessException)
            {
                throw new VideoException("The file can't be opened: " + x.Message);
            }
            IMFAttributes attributes = null;
            try
            {
                Mf.Check(Mf.MFCreateMFByteStreamOnStream(new StreamForMf(stream, false), out bytes), "Windows' video system can't read the file");
                attributes = Mf.NewAttributes();
                // the video processor: frames upright, in full colour (not for HLG: DeRes converts that itself)
                if (purpose == Purpose.Frames && !hlg) Mf.SetU32(attributes, G.EnableAdvancedVideoProcessing, 1);
                int hr = Mf.MFCreateSourceReaderFromByteStream(bytes, attributes, out Reader);
                if (hr < 0) throw new VideoException("Windows can't read it as a video (" + Mf.Hr(hr) + ").");
                Reader.SetStreamSelection(AllStreams, 0);
                if (purpose == Purpose.Frames) SetUpFrames();
                else if (purpose == Purpose.Sound) SetUpSound();
            }
            catch
            {
                Dispose();
                throw;
            }
            finally
            {
                Mf.Release(attributes);
            }
        }

        void SetUpFrames()
        {
            Reader.SetStreamSelection(FirstVideo, 1);
            p010 = hlg;
            IMFMediaType want = Mf.NewType(G.Video, p010 ? G.P010 : G.Rgb32);
            int hr;
            try
            {
                hr = Reader.SetCurrentMediaType(FirstVideo, IntPtr.Zero, want);
            }
            finally
            {
                Mf.Release(want);
            }
            p010Refused = hr < 0 && p010;
            if (hr < 0) throw new VideoException(NoDecoder());
            if (!p010) DisableFrameRateConversion();
            ReadType();
        }

        // Windows' video processor evens out the frames' timing (and halves Matroska's rate) unless told not to
        void DisableFrameRateConversion()
        {
            if (!(Reader is IMFSourceReaderEx ex)) return;
            for (uint k = 0; k < 8; k++)
            {
                if (ex.GetTransformForStream(FirstVideo, k, out Guid category, out IMFTransform t) < 0 || t == null) break;
                try
                {
                    if (category == G.VideoProcessorCategory && t.GetAttributes(out IMFAttributes a) >= 0 && a != null)
                    {
                        Guid key = G.DisableFrameRateConversion;
                        a.SetUINT32(ref key, 1);
                        Mf.Release(a);
                    }
                }
                finally
                {
                    Mf.Release(t);
                }
            }
        }

        void ReadType()
        {
            Mf.Check(Reader.GetCurrentMediaType(FirstVideo, out IMFMediaType t), "Windows didn't say what its frames are");
            try
            {
                ulong size = Mf.U64(t, G.FrameSize) ?? 0;
                pitchWidth = (int)(size >> 32);
                pitchHeight = (int)(size & 0xFFFFFFFF);
                ulong? rate = Mf.U64(t, G.FrameRate);
                if (rate != null && (rate.Value & 0xFFFFFFFF) != 0) Fps = (double)(rate.Value >> 32) / (rate.Value & 0xFFFFFFFF);
                int w = pitchWidth, h = pitchHeight;
                cropX = cropY = 0;
                if (p010)
                {
                    // the picture inside the decoder's frame (1080 lines come as 1088)
                    byte[] area = Mf.Blob(t, G.MinimumDisplayAperture) ?? Mf.Blob(t, G.GeometricAperture);
                    if (area != null && area.Length >= 16)
                    {
                        int x = BitConverter.ToInt16(area, 2), y = BitConverter.ToInt16(area, 6), aw = BitConverter.ToInt32(area, 8), ah = BitConverter.ToInt32(area, 12);
                        if (x >= 0 && y >= 0 && aw > 0 && ah > 0 && x + aw <= w && y + ah <= h)
                        {
                            cropX = x;
                            cropY = y;
                            w = aw;
                            h = ah;
                        }
                    }
                    bool sideways = file.Rotation == 90 || file.Rotation == 270;
                    FrameWidth = sideways ? h : w;
                    FrameHeight = sideways ? w : h;
                }
                else
                {
                    FrameWidth = w;
                    FrameHeight = h;
                }
            }
            finally
            {
                Mf.Release(t);
            }
            if (FrameWidth <= 0 || FrameHeight <= 0) throw new VideoException("Windows said its frames have no size.");
        }

        void SetUpSound()
        {
            Reader.SetStreamSelection(FirstAudio, 1);
            IMFMediaType want = Mf.NewType(G.Audio, G.Pcm);
            int hr;
            try
            {
                Mf.SetU32(want, G.AudioBitsPerSample, 16);
                Mf.SetU32(want, G.AudioSamplesPerSecond, SampleRate);
                Mf.SetU32(want, G.AudioChannels, Channels);
                hr = Reader.SetCurrentMediaType(FirstAudio, IntPtr.Zero, want);
            }
            finally
            {
                Mf.Release(want);
            }
            if (hr < 0)
            {
                // the decoder's own rate and channels, made 48 kHz stereo here
                IMFMediaType plain = Mf.NewType(G.Audio, G.Pcm);
                try
                {
                    Mf.SetU32(plain, G.AudioBitsPerSample, 16);
                    hr = Reader.SetCurrentMediaType(FirstAudio, IntPtr.Zero, plain);
                }
                finally
                {
                    Mf.Release(plain);
                }
                if (hr < 0) throw new VideoException("Windows can't decode its sound (" + (file.SoundKind ?? "an unknown kind") + ").");
            }
            Mf.Check(Reader.GetCurrentMediaType(FirstAudio, out IMFMediaType t), "Windows didn't say what its sound is");
            try
            {
                soundRate = (int)(Mf.U32(t, G.AudioSamplesPerSecond) ?? SampleRate);
                soundChannels = (int)(Mf.U32(t, G.AudioChannels) ?? Channels);
                if ((Mf.U32(t, G.AudioBitsPerSample) ?? 16) != 16 || soundRate <= 0 || soundChannels <= 0) throw new VideoException("Windows gave its sound in an odd form.");
                convertSound = soundRate != SampleRate || soundChannels != Channels;
            }
            finally
            {
                Mf.Release(t);
            }
        }

        string NoDecoder()
        {
            string kind = file.VideoKind ?? "an unknown kind";
            if (kind.StartsWith("HEVC", StringComparison.Ordinal)) return "Its picture is HEVC (H.265): Windows needs Microsoft's HEVC Video Extensions (from the Microsoft Store) to read it.";
            if (kind == "AV1") return "Its picture is AV1: Windows needs Microsoft's AV1 Video Extension (from the Microsoft Store) to read it.";
            if (kind == "VP9" || kind == "VP8") return "Its picture is " + kind + ": Windows needs Microsoft's VP9 Video Extensions (from the Microsoft Store) to read it.";
            if (kind == "ProRes") return "Its picture is ProRes, which Windows has no decoder for.";
            return "Windows can't decode its picture (" + kind + ").";
        }

        // ---- frames

        // Windows' own time for a time in the video (0 = its first frame), and back
        long Shift => (purpose == Purpose.Sound ? file.SoundShift : file.VideoShift) + file.Start;
        long ToWindows(long time) => time + Shift;
        long FromWindows(long time) => time - Shift;

        // Goes to `time`: the next frame read is the first one at or after the keyframe before it.
        public void Seek(long time) => Mta(() => SeekHere(time));

        void SeekHere(long time)
        {
            var p = new PropVariant { Type = 20, Long = Math.Max(0, ToWindows(time)) };   // VT_I8
            Guid format = Guid.Empty;
            Mf.Check(Reader.SetCurrentPosition(ref format, ref p), "Windows couldn't go to that time in the video");
            ended = false;
            LetGoHeld();
        }

        // The frame read ahead, not made yet: a frame is only known to be over once the next one's
        // time is in (variable frame rates' times have gaps a frame's own length doesn't tell). Made
        // already when the picture's kind changed after it.
        IMFSample heldSample;
        VideoFrame heldFrame;
        long heldTime, heldDuration;

        bool Holding => heldSample != null || heldFrame != null;

        void LetGoHeld()
        {
            Mf.Release(heldSample);
            heldSample = null;
            heldFrame = null;
        }

        VideoFrame MakeHeld() => heldFrame ?? new VideoFrame { Bgra = Pixels(heldSample), Width = FrameWidth, Height = FrameHeight, Time = heldTime, Duration = heldDuration };

        // The next frame, or null after the last one. One that's over by `skipBefore` (the next
        // starts by then) is passed over unmade, so going far ahead costs no colour work.
        public VideoFrame Next() => Mta(() => NextHere());

        public VideoFrame Next(long skipBefore) => Mta(() => NextHere(skipBefore));

        VideoFrame NextHere(long skipBefore = long.MinValue)
        {
            while (true)
            {
                if (ended)
                {
                    // the last one: on to the end
                    if (!Holding) return null;
                    VideoFrame last = MakeHeld();
                    LetGoHeld();
                    return last;
                }
                IMFSample sample = ReadVideoSample(out long time, out bool changed);
                if (changed && heldSample != null)
                {
                    // made now, in the kind it came in
                    heldFrame = MakeHeld();
                    Mf.Release(heldSample);
                    heldSample = null;
                }
                if (changed) ReadType();
                if (sample == null) continue;
                long at = FromWindows(time);
                VideoFrame made = null;
                try
                {
                    sample.GetSampleDuration(out long duration);
                    if (Holding && at > skipBefore) made = MakeHeld();
                    LetGoHeld();
                    heldSample = sample;
                    heldTime = at;
                    heldDuration = duration;
                }
                catch
                {
                    Mf.Release(sample);
                    throw;
                }
                if (made != null) return made;
            }
        }

        // the next sample of the picture (null: none this time, or the end), whether its kind changed
        IMFSample ReadVideoSample(out long time, out bool changed)
        {
            int hr = Reader.ReadSample(FirstVideo, 0, out _, out uint flags, out time, out IMFSample sample);
            if (hr < 0)
            {
                Mf.Release(sample);
                throw new VideoException(hr == unchecked((int)0xC00D5212) || hr == unchecked((int)0xC00D36B4) ? NoDecoder() : "Windows couldn't read a frame (" + Mf.Hr(hr) + ").");
            }
            if ((flags & ErrorFlag) != 0)
            {
                Mf.Release(sample);
                throw new VideoException("The video is damaged: Windows stopped reading it.");
            }
            if ((flags & EndOfStream) != 0) ended = true;
            changed = (flags & TypeChanged) != 0;
            return sample;
        }

        // The next frame's time and length, read but not made (null after the last): for working
        // out a video's speed and length.
        public (long Time, long Duration)? NextTime() => Mta(() =>
        {
            LetGoHeld();
            while (!ended)
            {
                IMFSample sample = ReadVideoSample(out long time, out bool changed);
                if (changed) ReadType();
                if (sample == null) continue;
                try
                {
                    sample.GetSampleDuration(out long duration);
                    return ((long, long)?)(FromWindows(time), duration);
                }
                finally
                {
                    Mf.Release(sample);
                }
            }
            return null;
        });

        // the frame showing at `time`: the last one starting by then (with a millisecond's grace:
        // times in files are rounded), or the first one if none does; those before it pass unmade
        public VideoFrame FrameAt(long time) => Mta(() => FrameAtHere(time));

        VideoFrame FrameAtHere(long time)
        {
            SeekHere(time);
            return NextHere(time + Grace);
        }

        byte[] Pixels(IMFSample sample)
        {
            Mf.Check(sample.ConvertToContiguousBuffer(out IMFMediaBuffer buffer), "Windows couldn't hand over a frame");
            try
            {
                IntPtr top;
                int pitch;
                bool locked2d = false;
                var two = buffer as IMF2DBuffer;
                if (two != null && two.Lock2D(out top, out pitch) >= 0) locked2d = true;
                else
                {
                    Mf.Check(buffer.Lock(out top, out _, out _), "Windows couldn't hand over a frame");
                    pitch = p010 ? pitchWidth * 2 : pitchWidth * 4;
                }
                try
                {
                    // (Windows gives the turn counter-clockwise)
                    return p010 ? Hdr.HlgToBgra(top, pitch, pitchWidth, pitchHeight, cropX, cropY, (360 - file.Rotation) % 360, FrameWidth, FrameHeight) : Copy(top, pitch);
                }
                finally
                {
                    if (locked2d) two.Unlock2D();
                    else buffer.Unlock();
                }
            }
            finally
            {
                Mf.Release(buffer);
            }
        }

        byte[] Copy(IntPtr top, int pitch)
        {
            int w = FrameWidth, h = FrameHeight;
            var bgra = new byte[(long)w * h * 4];
            for (int y = 0; y < h; y++) Marshal.Copy(top + y * pitch, bgra, y * w * 4, w * 4);
            // RGB32 has no alpha: every pixel shows
            for (int i = 3; i < bgra.Length; i += 4) bgra[i] = 255;
            return bgra;
        }

        // ---- sound

        // The next stretch of sound: 16-bit samples, 48 kHz, left then right; null after the last.
        // `time`: when it starts.
        public short[] NextSound(out long time)
        {
            long at = 0;
            short[] samples = Mta(() => NextSoundHere(out at));
            time = at;
            return samples;
        }

        short[] NextSoundHere(out long time)
        {
            time = 0;
            while (!ended)
            {
                int hr = Reader.ReadSample(FirstAudio, 0, out _, out uint flags, out time, out IMFSample sample);
                if (hr < 0 || (flags & ErrorFlag) != 0)
                {
                    Mf.Release(sample);
                    throw new VideoException("Windows couldn't read its sound (" + Mf.Hr(hr) + ").");
                }
                if ((flags & EndOfStream) != 0) ended = true;
                if (sample == null) continue;
                time = FromWindows(time);
                try
                {
                    Mf.Check(sample.ConvertToContiguousBuffer(out IMFMediaBuffer buffer), "Windows couldn't hand over the sound");
                    try
                    {
                        Mf.Check(buffer.Lock(out IntPtr p, out _, out uint length), "Windows couldn't hand over the sound");
                        try
                        {
                            var samples = new short[length / 2];
                            Marshal.Copy(p, samples, 0, samples.Length);
                            return convertSound ? Resample(samples, soundRate, soundChannels) : samples;
                        }
                        finally
                        {
                            buffer.Unlock();
                        }
                    }
                    finally
                    {
                        Mf.Release(buffer);
                    }
                }
                finally
                {
                    Mf.Release(sample);
                }
            }
            return null;
        }

        double resampleAt;   // where the next output sample falls in the input, carried between stretches
        float[] resampleLast;

        // sound at another rate or in other channels, made 48 kHz stereo (linear between samples)
        short[] Resample(short[] input, int rate, int channels)
        {
            channels = Math.Max(1, channels);
            int frames = input.Length / channels;
            // each input frame as a stereo pair, after the last one of the stretch before
            int k0 = resampleLast != null ? 1 : 0;
            var left = new float[frames + k0];
            var right = new float[frames + k0];
            if (k0 == 1)
            {
                left[0] = resampleLast[0];
                right[0] = resampleLast[1];
            }
            for (int i = 0; i < frames; i++)
            {
                float l, r;
                if (channels == 1) l = r = input[i];
                else
                {
                    l = input[i * channels];
                    r = input[i * channels + 1];
                    // more than two: the centre (and the rest) mixed into both
                    for (int c = 2; c < channels; c++)
                    {
                        float v = input[i * channels + c] * 0.5f;
                        l += v;
                        r += v;
                    }
                }
                left[i + k0] = l;
                right[i + k0] = r;
            }
            int have = frames + k0;
            if (have == 0) return new short[0];
            double step = (double)rate / SampleRate;
            var output = new System.Collections.Generic.List<short>((int)Math.Min(int.MaxValue / 4, (long)frames * 2 * SampleRate / Math.Max(1, rate) + 4));
            double at = resampleAt;
            while (at + 1 < have)
            {
                int i = (int)at;
                double f = at - i;
                output.Add(Sixteen(left[i] + (left[i + 1] - left[i]) * f));
                output.Add(Sixteen(right[i] + (right[i + 1] - right[i]) * f));
                at += step;
            }
            resampleAt = at - (have - 1);
            // (kept as it was, mixed channels and all, for the next stretch to start from)
            resampleLast = new[] { left[have - 1], right[have - 1] };
            return output.ToArray();
        }

        static short Sixteen(double v) => (short)Math.Max(short.MinValue, Math.Min(short.MaxValue, Math.Round(v)));

        // the sound read from `time` on
        public void SeekSound(long time) => Mta(() =>
        {
            SeekHere(time);
            resampleAt = 0;
            resampleLast = null;
        });

        // (twice does no harm)
        public void Dispose() => Mta(() =>
        {
            LetGoHeld();
            Mf.Release(Reader);
            Mf.Release(bytes);
            stream?.Dispose();
            Reader = null;
            bytes = null;
            stream = null;
        });
    }
}
