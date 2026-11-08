using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using B0r3dDeRes.Camera;
using B0r3dDeRes.Engine;
using CameraFrame = B0r3dDeRes.Camera.Frame;

namespace B0r3dDeRes.Tests
{
    // The live camera. No test ever starts a real camera (only whoever uses this PC turns its webcam
    // on): Windows' cameras are only listed; the camera's Media Foundation reader is tried on video
    // files, which go through the same code a camera's pictures do; and the window's camera runs on
    // pretend cameras through its real loop.
    static partial class TestMain
    {
        // ---------- pretend cameras ----------

        // A camera's picture (blue, green, red, spare): a gradient across and down, with a bar that
        // moves with `k`, so frames and their left and right can be told apart
        static CameraFrame CameraPicture(int width, int height, int k)
        {
            var frame = new CameraFrame();
            frame.Size(width, height);
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    int i = (y * width + x) * 4;
                    bool bar = (x + k * 7) % width < width / 8;
                    frame.Pixels[i] = (byte)(bar ? 255 : x * 255 / Math.Max(1, width - 1));
                    frame.Pixels[i + 1] = (byte)(y * 255 / Math.Max(1, height - 1));
                    frame.Pixels[i + 2] = (byte)(bar ? 40 : 255 - x * 255 / Math.Max(1, width - 1));
                    frame.Pixels[i + 3] = 0;   // (the spare byte: never taken for see-through)
                }
            return frame;
        }

        // a pretend camera: a moving picture (or `Still`, the same one), trouble when it's started or
        // after `FailAfter` pictures, `OpenMs` to start; one session's at a time, as a real one
        sealed class FakeCamera
        {
            public string Name;
            public int Width = 320, Height = 240;
            public bool Still;
            public Exception OpenTrouble, ReadTrouble;
            public int FailAfter, OpenMs;
            public int Starts, Opened, Shown, Closed;
            public int Holding;
        }

        sealed class FakeCameras : ICameras
        {
            public readonly FakeCamera[] All;
            public bool Disposed;

            public FakeCameras(params FakeCamera[] cameras)
            {
                All = cameras;
            }

            public List<CameraInfo> Find() => All.Select((c, i) => new CameraInfo { Name = c.Name, Id = "fake " + i }).ToList();

            public IFrameSource Open(CameraInfo camera)
            {
                FakeCamera fake = All[int.Parse(camera.Id.Substring(5))];
                Interlocked.Increment(ref fake.Starts);
                if (fake.OpenMs > 0) Thread.Sleep(fake.OpenMs);   // (a camera that takes a while to start)
                if (fake.OpenTrouble != null) throw fake.OpenTrouble;
                if (Interlocked.Increment(ref fake.Holding) > 1)
                {
                    Interlocked.Decrement(ref fake.Holding);
                    throw Webcams.Problem(unchecked((int)0xC00D3704), "Windows didn't start " + fake.Name);
                }
                fake.Opened++;
                return new Source(fake);
            }

            public void Dispose() => Disposed = true;

            sealed class Source : IFrameSource
            {
                readonly FakeCamera fake;
                readonly ManualResetEvent stopped = new ManualResetEvent(false);
                bool done;

                public Source(FakeCamera fake)
                {
                    this.fake = fake;
                }

                // a picture every 20 ms or so; once it's been stopped, what Media Foundation says of a
                // camera that's been shut down
                public bool Next(CameraFrame into)
                {
                    if (stopped.WaitOne(20)) throw Webcams.Problem(unchecked((int)0xC00D3E85), "the camera stopped sending pictures");   // MF_E_SHUTDOWN
                    if (fake.ReadTrouble != null && fake.Shown >= fake.FailAfter) throw fake.ReadTrouble;
                    CameraFrame picture = CameraPicture(fake.Width, fake.Height, fake.Still ? 0 : fake.Shown);
                    fake.Shown++;
                    into.Size(picture.Width, picture.Height);
                    Buffer.BlockCopy(picture.Pixels, 0, into.Pixels, 0, picture.Pixels.Length);
                    return true;
                }

                public void Interrupt() => stopped.Set();

                public void Dispose()
                {
                    if (done) return;
                    done = true;
                    fake.Closed++;
                    Interlocked.Decrement(ref fake.Holding);
                }
            }
        }

        // ---------- the sums (the website's ui/camera-plan.test.ts) ----------

        static void CameraSums()
        {
            void Size(int vw, int vh, string id, bool custom, int? width, int ew, int eh)
            {
                LivePlan.InputSize(vw, vh, Profiles.ById(id), custom, width, Fits.Fill, out int w, out int h);
                Check(w == ew && h == eh, id + (custom ? " " + width + " wide" : "") + " from " + vw + "×" + vh + " grabs " + w + "×" + h + ", not " + ew + "×" + eh);
            }
            // Game Boy from a 1920×1080 camera: the middle 1200×1080 is shown at 160×144, so 0.4 of the frame
            Size(1920, 1080, "gameboy", false, null, 768, 432);
            // never more than the camera gives, or past 960 on the long side (1280 for a wide custom size)
            Size(640, 480, "pc98", false, null, 640, 480);
            LivePlan.InputSize(3840, 2160, Profiles.ById("vga"), false, null, Fits.Fill, out int bw, out int bh);
            Check(Math.Max(bw, bh) == 960, "a 4K camera on VGA grabs " + bw + "×" + bh);
            Size(1920, 1080, "gameboy", true, 640, 1280, 720);
            LivePlan.InputSize(1920, 1080, Profiles.ById("gameboy"), true, 200, Fits.Fill, out int cw, out _);
            Check(cw <= 960, "a custom width of 200 grabs " + cw + " wide");

            // the screen effect while live: stepped down to fit, never below 2×; none stays none
            Check(LivePlan.FxScale(0, 320, 200, 1, 1, false) == 0 && LivePlan.FxScale(3, 160, 144, 1, 1, false) == 3 && LivePlan.FxScale(8, 640, 480, 1, 1, false) == 2,
                "the live screen effect's scales");
            int s6 = LivePlan.FxScale(6, 320, 200, 1, 1, false);
            Check(320 * s6 * 200 * s6 <= LivePlan.FxPixels, "VGA's live effect at " + s6 + "× is past the budget");

            // a clip: 30 frames held as the PC holds an animated GIF's (so a camera's whole frames fit)
            LivePlan.ClipSize(1920, 1080, Pictures.GifBudget, out int fw, out int fh);
            Check(fw == 1920 && fh == 1080, "a 1080p camera's clip is held at " + fw + "×" + fh);
            LivePlan.ClipSize(1280, 720, 16.0 * 1024 * 1024, out int ww, out int wh);
            Check((long)ww * wh * LivePlan.ClipFrames <= 16L * 1024 * 1024 && Math.Abs((double)ww / wh - 16.0 / 9) < 0.05, "with the website's budget, 1280×720 is held at " + ww + "×" + wh);

            // names by the local time
            var at = new DateTime(2026, 10, 22, 21, 40, 5);
            Check(LivePlan.Name("camera", at) == "camera-2026-10-22-2140-05" && LivePlan.Name("camera-clip", at) == "camera-clip-2026-10-22-2140-05", "the names: " + LivePlan.Name("camera", at));

            // the frame rate: the frames of the last second, and slow after 2 s under 8
            var m = new FpsMeter();
            m.Reset(0);
            for (int t = 0; t <= 1000; t += 40) m.Frame(t);
            Check(m.Fps(1000) == 25 && !m.Slow(1000), "25 a second: " + m.Fps(1000));
            for (int t = 1250; t <= 4000; t += 250) m.Frame(t);
            Check(m.Fps(4000) == 4 && m.Slow(4000), "4 a second for 2.75 s: " + m.Fps(4000) + ", slow " + m.Slow(4000));
            var never = new FpsMeter();
            never.Reset(100);
            Check(!never.Slow(1500) && never.Slow(2200), "a look that never shows a frame is slow after 2 s");
            Check(Words.SlowLive(5) == "This look is slow live (about 5 frames a second). Snap to see it in full." && Words.SlowLive(0).Contains("about 1 frame a second"), "the slow line");
            Check(Words.RecordingLeft(30, 100) == "Recording 3…" && Words.RecordingLeft(1, 100) == "Recording 1…", "the countdown");
        }

        // Grab: each pixel the average of the camera pixels it covers, mirrored when asked; never see-through
        static void CameraGrab()
        {
            var frame = new CameraFrame();
            frame.Size(4, 2);
            // the left half dark, the right half light; the spare byte anything
            for (int y = 0; y < 2; y++)
                for (int x = 0; x < 4; x++)
                {
                    int i = (y * 4 + x) * 4;
                    frame.Pixels[i] = frame.Pixels[i + 1] = frame.Pixels[i + 2] = (byte)(x < 2 ? 10 + x * 10 : 200 + y);
                    frame.Pixels[i + 3] = 7;
                }
            PictureSource small = LivePlan.Grab(frame, 2, 1, false, 4, 2);
            Check(small.Width == 2 && small.Height == 1 && small.OrigW == 4 && small.OrigH == 2 && !small.HasAlpha, "the grab's size and source");
            // (10 + 20 + 10 + 20) / 4 = 15; (200 + 200 + 201 + 201) / 4 = 200.5, rounded to 201
            Check(small.Bgra[0] == 15 && small.Bgra[4] == 201 && small.Bgra[3] == 255 && small.Bgra[7] == 255, "the averages: " + small.Bgra[0] + ", " + small.Bgra[4]);
            PictureSource mirrored = LivePlan.Grab(frame, 2, 1, true);
            Check(mirrored.Bgra[0] == 201 && mirrored.Bgra[4] == 15, "mirrored, the light half comes first");
            PictureSource whole = LivePlan.Grab(frame, 4, 2, true);
            // (the top row: 10, 20, 200, 200 mirrored is 200, 200, 20, 10)
            Check(whole.Bgra[0] == 200 && whole.Bgra[4 * 2] == 20 && whole.Bgra[4 * 3] == 10 && whole.Bgra[3] == 255 && whole.Bgra[4 * 4] == 201, "a whole frame mirrored, pixel for pixel");
        }

        // Windows' cameras are listed (none started), named to choose by, and each answer worded
        static void CamerasListed()
        {
            var webcams = new Webcams();
            List<CameraInfo> list;
            try
            {
                list = webcams.Find();
            }
            catch (CameraException x) when (x.Trouble == Trouble.Failed)
            {
                throw new SkipException("Windows' media features (Media Foundation) aren't on this machine: " + x.Message);
            }
            finally
            {
                webcams.Dispose();
            }
            Check(list.All(c => c.Name.Trim().Length > 0 && c.Id.Length > 0), "a camera without a name or a device path: " + string.Join(", ", list.Select(c => "\"" + c.Name + "\"")));
            Console.WriteLine("      (Windows lists " + (list.Count == 1 ? "1 camera" : list.Count + " cameras") + "; none was started)");

            var named = new List<CameraInfo> { new CameraInfo { Name = "USB Camera" }, new CameraInfo { Name = "usb camera " }, new CameraInfo { Name = " " }, new CameraInfo { Name = null } };
            Webcams.Name(named);
            Check(named.Select(c => c.Name).SequenceEqual(new[] { "USB Camera", "usb camera (2)", "Camera 3", "Camera 4" }), "the names: " + string.Join(", ", named.Select(c => c.Name)));

            // each of Windows' answers, in the words the window shows
            Check(Webcams.Problem(unchecked((int)0x80070005), "x").Trouble == Trouble.Denied && Webcams.Problem(unchecked((int)0xC00D3704), "x").Trouble == Trouble.InUse &&
                  Webcams.Problem(unchecked((int)0xC00D3EA2), "x").Trouble == Trouble.NoCamera && Webcams.Problem(1, "x").Trouble == Trouble.Failed, "Windows' answers");
            Check(new CameraException(Trouble.Denied, null).Words.Contains("Let desktop apps access your camera") && new CameraException(Trouble.InUse, null).Words.StartsWith("Another app is using the camera") &&
                  new CameraException(Trouble.NoCamera, null).Words == "b0r3d DeRes can't find a camera on this PC." && new CameraException(Trouble.Failed, null).Words.StartsWith("The camera didn't start"), "the words");
            Check(Webcams.Problem(unchecked((int)0xC00D3704), "Windows didn't start X").Message.Contains("0xC00D3704"), "Windows' code is kept for the detail");

            // the kind of picture asked of a camera: 15 a second or more, nearest the website's 1920×1440, then nearest 30 a second
            var kinds = new[]
            {
                new SourceFrames.Kind { Index = 0, Width = 640, Height = 480, Rate = 30 },
                new SourceFrames.Kind { Index = 1, Width = 1920, Height = 1080, Rate = 5 },
                new SourceFrames.Kind { Index = 2, Width = 1920, Height = 1080, Rate = 30 },
                new SourceFrames.Kind { Index = 3, Width = 1920, Height = 1080, Rate = 60 },
                new SourceFrames.Kind { Index = 4, Width = 1280, Height = 720, Rate = 30 },
            };
            Check(SourceFrames.InOrder(kinds).First().Index == 2, "the camera's kind picked: " + SourceFrames.InOrder(kinds).First().Index);
        }

        // ---------- Media Foundation's camera reader, on video files ----------

        [DllImport("mfplat.dll")] static extern int MFCreateFile(int access, int open, int flags, [MarshalAs(UnmanagedType.LPWStr)] string path, out IntPtr stream);
        [DllImport("mfplat.dll")] static extern int MFCreateSourceResolver(out IMFSourceResolver resolver);

        // Windows' way from a file to a media source (only the tests open files with Media Foundation this way)
        [ComImport, Guid("fbe5a32d-a497-4b61-bb85-97b1a848a6e3"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMFSourceResolver
        {
            [PreserveSig] int CreateObjectFromURL([MarshalAs(UnmanagedType.LPWStr)] string url, int flags, IntPtr props, out int objectType, [MarshalAs(UnmanagedType.IUnknown)] out object made);
            [PreserveSig] int CreateObjectFromByteStream(IntPtr stream, [MarshalAs(UnmanagedType.LPWStr)] string url, int flags, IntPtr props, out int objectType, [MarshalAs(UnmanagedType.IUnknown)] out object made);
        }

        // a video file's media source, as a started camera's would be (null: Media Foundation here can't read it)
        static IMFMediaSource OpenVideoSource(string file)
        {
            Check(MFCreateFile(1, 0, 0, file, out IntPtr stream) == 0, "Media Foundation didn't open " + file);   // MF_ACCESSMODE_READ, MF_OPENMODE_FAIL_IF_NOT_EXIST
            Check(MFCreateSourceResolver(out IMFSourceResolver resolver) == 0, "no source resolver");
            int hr = resolver.CreateObjectFromByteStream(stream, file, 1, IntPtr.Zero, out _, out object made);   // MF_RESOLUTION_MEDIASOURCE
            Marshal.Release(stream);
            MF.Release(resolver);
            return hr == 0 ? made as IMFMediaSource : null;
        }

        // The reader a webcam's pictures go through, on MJPEG (as webcams send it) and H.264 files:
        // the same frames, the right way up, as the video reader makes of them; let go of afterwards.
        static void CameraReaderOnVideo()
        {
            if (!Video.Mf.Available) throw new SkipException("Windows' media features (Media Foundation) aren't on this machine");
            var read = new List<string>();
            foreach (string name in new[] { "mjpeg.avi", "no-sound.mp4" })
            {
                string file = Clip(name);
                // the same file through the video reader (a Windows that can't read it at all is skipped)
                Video.VideoFile video;
                Video.VideoFrame first;
                try
                {
                    video = Video.VideoFile.Open(file);
                    using (Video.VideoReader reader = video.Frames()) first = reader.Next();
                }
                catch (Video.VideoException)
                {
                    continue;
                }
                IMFMediaSource source = VideoReaderThread(() => OpenVideoSource(file));
                Check(source != null, "Media Foundation made no source of " + name);
                SourceFrames frames = null;
                try
                {
                    frames = VideoReaderThread(() => new SourceFrames(source, null));
                    var frame = new CameraFrame();
                    int got = 0;
                    for (int i = 0; i < 6 && got < 3; i++)
                        if (VideoReaderThread(() => frames.Next(frame))) got++;
                    Check(got >= 3, name + ": only " + got + " pictures read");
                    Check(frame.Width == video.Width && frame.Height == video.Height, name + " read at " + frame.Width + "×" + frame.Height + ", not " + video.Width + "×" + video.Height);
                    read.Add(name + " " + frame.Width + "×" + frame.Height);
                }
                finally
                {
                    if (frames != null) VideoReaderThread(() => { frames.Dispose(); return 0; });
                }
                // the first picture, against the video reader's (the same decoder; the colour conversion may round apart)
                IMFMediaSource again = VideoReaderThread(() => OpenVideoSource(file));
                var one = new CameraFrame();
                SourceFrames firstFrames = VideoReaderThread(() => new SourceFrames(again, null));
                try
                {
                    Check(VideoReaderThread(() => firstFrames.Next(one)), name + ": no first picture");
                }
                finally
                {
                    VideoReaderThread(() => { firstFrames.Dispose(); return 0; });
                }
                long diff = 0;
                for (int i = 0; i < one.Pixels.Length; i += 4)
                    for (int c = 0; c < 3; c++) diff += Math.Abs(one.Pixels[i + c] - first.Bgra[i + c]);
                double mean = (double)diff / (one.Pixels.Length / 4 * 3);
                Check(mean < 4, name + "'s first picture differs from the video reader's by " + mean.ToString("0.0") + " levels on average (upside down or mirrored?)");
            }
            if (read.Count == 0) throw new SkipException("Media Foundation here reads neither test clip");
            Console.WriteLine("      (" + string.Join(", ", read) + ")");
        }

        // Media Foundation's objects are the MTA's: these run on the video reader's own thread, as the camera's run on the camera's
        static T VideoReaderThread<T>(Func<T> call) => Video.VideoReader.Mta(call);

        // ---------- the camera's own thread ----------

        static void CameraLiveLoop()
        {
            var still = new FakeCamera { Name = "Desk camera", Width = 160, Height = 120, Still = true };
            var other = new FakeCamera { Name = "Other camera", Width = 64, Height = 48 };
            var cameras = new FakeCameras(still, other);
            var live = new CameraLive(cameras, Dispatcher.CurrentDispatcher) { Prefer = "nobody" };
            CameraLive locked = null, preferring = null;
            try
            {
                CameraThread(live, cameras, still, other, out locked, out preferring);
            }
            finally
            {
                // (whatever failed, no session is left holding a camera for the tests after)
                live.Stop();
                locked?.Stop();
                preferring?.Stop();
                Pump(() => !live.Running && (locked == null || !locked.Running) && (preferring == null || !preferring.Running), 5000);
            }
        }

        static void CameraThread(CameraLive live, FakeCameras cameras, FakeCamera still, FakeCamera other, out CameraLive locked, out CameraLive preferring)
        {
            locked = preferring = null;
            List<CameraInfo> listed = null;
            var started = new List<string>();
            int news = 0, ended = 0;
            PictureSource[] clip = null;
            CameraException failed = null;
            live.Listed = l => listed = l;
            live.Started = (c, w, h) => started.Add(c.Name + " " + w + "x" + h);
            live.NewPicture = () => news++;
            live.Clip = frames => clip = frames;
            live.Failed = x => failed = x;
            live.Ended = () => ended++;
            live.Start();
            Pump(() => started.Count > 0 && news > 0, 5000);
            Check(listed != null && listed.Count == 2 && started.SequenceEqual(new[] { "Desk camera 160x120" }) && failed == null, "the first camera listed and started: " + string.Join(", ", started) + " " + failed?.Words);
            // the newest picture, a copy taken in turn; then none until the camera sends another
            var mine = new CameraFrame();
            Check(live.TakeNewest(mine) && mine.Width == 160 && mine.Height == 120, "the newest picture taken");
            CameraFrame expected = CameraPicture(160, 120, 0);
            Check(mine.Pixels.SequenceEqual(expected.Pixels), "the picture taken is the camera's");
            // a snap: the newest picture whole, mirrored as asked (still there after it was taken)
            PictureSource snap = live.Snap(true);
            Check(snap != null && snap.Width == 160 && snap.Bgra[0] == expected.Pixels[159 * 4] && snap.Bgra[1] == expected.Pixels[159 * 4 + 1] && snap.Bgra[3] == 255, "the snap is the newest picture, mirrored");

            // a clip: 30 shots, ten a second, on time
            var watch = System.Diagnostics.Stopwatch.StartNew();
            live.Shoot(80, 60, false);
            Check(live.Shooting, "a clip being shot says so");
            Pump(() => clip != null, 8000);
            Check(clip != null && clip.Length == LivePlan.ClipFrames && clip.All(f => f.Width == 80 && f.Height == 60 && f.OrigW == 160 && f.OrigH == 120), "the clip's 30 frames at 80×60: " + clip?.Length);
            Check(watch.ElapsedMilliseconds >= 2700 && watch.ElapsedMilliseconds <= 5000, "30 shots ten a second took " + watch.ElapsedMilliseconds + " ms");
            Check(!live.Shooting && live.Shots == -1, "the clip is done");

            // another camera: the first is let go of first
            live.Choose(1);
            Pump(() => started.Count == 2, 5000);
            Check(started.Last() == "Other camera 64x48" && still.Closed == 1 && still.Holding == 0, "the other camera: " + string.Join(", ", started) + ", the first let go " + still.Closed);

            // trouble while reading: said, and the camera let go of
            other.ReadTrouble = Webcams.Problem(unchecked((int)0xC00D3EA2), "the camera was unplugged");
            other.FailAfter = other.Shown + 2;
            Pump(() => failed != null, 5000);
            Check(failed != null && failed.Trouble == Trouble.NoCamera && other.Holding == 0, "an unplugged camera says so and is let go of: " + failed?.Trouble);

            // Stop: the thread ends, everything let go of
            live.Stop();
            Pump(() => ended > 0, 5000);
            Check(ended == 1 && cameras.Disposed && still.Holding == 0 && other.Holding == 0 && !live.Running, "stopped: ended " + ended + ", let go " + cameras.Disposed);

            // a camera that won't start: said; and the one used last starts first
            var denied = new FakeCameras(new FakeCamera { Name = "Locked camera", OpenTrouble = Webcams.Problem(unchecked((int)0x80070005), "Windows didn't start it") });
            failed = null;
            locked = new CameraLive(denied, Dispatcher.CurrentDispatcher);
            locked.Failed = x => failed = x;
            locked.Start();
            Pump(() => failed != null, 5000);
            Check(failed != null && failed.Trouble == Trouble.Denied, "a camera Windows' privacy settings keep: " + failed?.Trouble);
            locked.Stop();
            var two = new FakeCameras(new FakeCamera { Name = "First" }, new FakeCamera { Name = "Second" });
            preferring = new CameraLive(two, Dispatcher.CurrentDispatcher) { Prefer = "Second" };
            string first = null;
            preferring.Started = (c, w, h) => first = first ?? c.Name;
            preferring.Start();
            Pump(() => first != null, 5000);
            preferring.Stop();
            Check(first == "Second" && two.All[0].Opened == 0, "the camera used last starts first: " + first);
        }

        // ---------- the window's camera ----------

        // the window's camera on, on a pretend one, and its first live frames shown
        static void CameraOnIn(MainWindow window, params FakeCamera[] cameras)
        {
            window.Cameras = () => new FakeCameras(cameras);
            window.StartCamera();
            Pump(() => window.LiveFrames >= 3, 10000);
            Check(window.CameraShows && window.LiveFrames >= 3, "the camera's live frames didn't come: " + window.LiveFrames + " (" + window.CameraLineText + ")");
            LayOut(window.Root, MainWidth, MainHeight);
        }

        // what the window makes of a camera's still picture: the engine's own picture of the grab
        static Rendered EngineOf(MainWindow window, CameraFrame picture, bool mirror)
        {
            DeresSettings s = window.Current.Deres;
            Profile p = Profiles.ById(s.ProfileId);
            LivePlan.InputSize(picture.Width, picture.Height, p, s.Custom, s.CustomWidth, s.Fit, out int w, out int h);
            var d = new Deres();
            d.SetSource(LivePlan.Grab(picture, w, h, mirror, picture.Width, picture.Height));
            return d.Render(s);
        }

        static bool CanRun(MainWindow window, RoutedCommand command) => command.CanExecute(null, window.Root);

        // a window with its sample, for a test; whatever it ends in, its camera is off and it's closed
        static void InAWindow(Action<MainWindow> test)
        {
            MainWindow window = NewMain();
            bool closed = false;
            window.Closed += (s, e) => closed = true;
            try
            {
                WaitForPicture(window);
                test(window);
            }
            finally
            {
                window.StopCamera(false);
                if (!closed) window.Close();
            }
        }

        static void CameraInTheWindow() => InAWindow(window =>
        {
            var cam = new FakeCamera { Name = "Desk camera", Width = 640, Height = 480, Still = true };
            CameraOnIn(window, cam);
            // the live frame is the engine's picture of the camera's, grabbed small and mirrored (a PC's camera faces you)
            Rendered live = window.LiveOnScreen;
            Rendered engine = EngineOf(window, CameraPicture(640, 480, 0), true);
            Check(live != null && live.Width == 160 && live.Height == 144 && live.Rgb.SequenceEqual(engine.Rgb), "the live frame isn't the engine's picture of the camera's");
            Check(window.PictureInfo.Text == "Desk camera · 640×480" && window.Readout.Text.EndsWith(" fps") && window.Readout.Text.StartsWith(Words.Readout(live)), "the lines while live: " + window.PictureInfo.Text + " / " + window.Readout.Text);
            Check(window.CameraLineText == Words.CameraStaysHere || window.CameraLineText.StartsWith("This look is slow live"), "the camera's line: " + window.CameraLineText);
            // the Image section swaps to the camera's buttons; saving waits for a snap
            Check(window.CameraPanel.Visibility == Visibility.Visible && window.OpenButton.Visibility != Visibility.Visible && window.CameraButton.Visibility != Visibility.Visible &&
                  window.SampleLine.Visibility != Visibility.Visible && window.SnapButton.IsEnabled && window.RecordButton.IsEnabled && window.FlipButton.Visibility != Visibility.Visible, "the Image section while live");
            Check(window.LiveNote.Visibility == Visibility.Visible && !window.SaveGifButton.IsEnabled && !window.SaveApngButton.IsEnabled && !CanRun(window, Commands.SavePng) &&
                  !CanRun(window, Commands.CopyPicture) && CanRun(window, Commands.ZoomIn) && !CanRun(window, Commands.Compare), "saving and comparing wait for a snap; zooming doesn't");
            Check(window.DragOut.Visibility != Visibility.Visible, "no dragging out a live frame");

            // another machine and a screen effect, live: the effect drawn smaller, never below 2×
            window.Cards.Children.OfType<RadioButton>().First(c => (string)c.Tag == "nes").IsChecked = true;
            int was = window.LiveFrames;
            Pump(() => window.LiveFrames > was + 2 && window.LiveOnScreen?.Profile.Id == "nes", 10000);
            Check(window.LiveOnScreen.Profile.Id == "nes" && window.LiveOnScreen.Rgb.SequenceEqual(EngineOf(window, CameraPicture(640, 480, 0), true).Rgb), "picking the NES while live");
            Check(window.Shown.Profile.Id == "gameboy", "the still picture isn't made while the camera is on");

            // Stop: the camera let go of, the picture back, made with the settings of now
            window.StopCamera();
            Check(!window.CameraOn && window.CameraButton.Visibility == Visibility.Visible && window.CameraPanel.Visibility != Visibility.Visible && window.LiveNote.Visibility != Visibility.Visible, "the buttons after Stop");
            Pump(() => !window.Rendering && window.Shown?.Profile.Id == "nes" && cam.Holding == 0, 10000);
            Check(cam.Holding == 0 && cam.Closed == 1, "the camera wasn't let go of");
            Check(window.Shown.Profile.Id == "nes" && window.PictureInfo.Text == "rukus.png · 256×256" && !window.Readout.Text.Contains("fps") && CanRun(window, Commands.SavePng), "the picture back after Stop: " + window.PictureInfo.Text + " / " + window.Readout.Text);
        });

        static void CameraSnapAndClip() => InAWindow(window =>
        {
            var cam = new FakeCamera { Name = "Desk camera", Width = 320, Height = 240, Still = true };
            CameraOnIn(window, cam);
            CameraFrame picture = CameraPicture(320, 240, 0);
            window.SnapCamera();
            Check(!window.CameraOn, "a snap turns the camera off");
            WaitForPicture(window);
            LoadedPicture snap = window.OpenedPicture;
            Check(snap.Name.StartsWith("camera-") && snap.Name.EndsWith(".png") && snap.Source.Width == 320 && snap.Source.Height == 240 && snap.Animation == null && snap.FileBytes == null,
                "the snap opened as " + snap.Name + " " + snap.Source.Width + "×" + snap.Source.Height);
            Check(snap.Source.Bgra.SequenceEqual(LivePlan.Grab(picture, 320, 240, true).Bgra), "the snap is the camera's picture, mirrored as it was shown");
            Pump(() => cam.Holding == 0, 5000);
            Check(cam.Holding == 0, "the camera wasn't let go of after the snap");

            // Mirror off, then a clip: 30 frames, 100 ms each, looping, opened like an animated GIF
            window.MirrorButton.IsChecked = false;
            window.MirrorButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Check(!window.Current.CameraMirror, "Mirror off");
            var moving = new FakeCamera { Name = "Desk camera", Width = 320, Height = 240 };
            CameraOnIn(window, moving);
            window.RecordCamera();
            Check(!window.SnapButton.IsEnabled && !window.RecordButton.IsEnabled && ((string)window.RecordButton.Content).StartsWith("Recording "), "recording: " + window.RecordButton.Content);
            Pump(() => !window.CameraOn, 10000);
            Check(!window.CameraOn, "the clip never came");
            WaitForPicture(window);
            LoadedPicture clip = window.OpenedPicture;
            AnimatedGif gif = clip.Animation;
            Check(gif != null && clip.Name.StartsWith("camera-clip-") && gif.Frames.Length == 30 && gif.DelaysMs.All(d => d == 100) && gif.LoopCount == 0 && gif.Width == 320 && gif.Height == 240,
                "the clip opened as " + clip.Name + ": " + gif?.Frames.Length + " frames of " + gif?.Width + "×" + gif?.Height);
            Check(gif.Frames.Select(f => Convert.ToBase64String(f.Bgra)).Distinct().Count() > 10, "the clip's frames are the camera's moving ones");
            // (unmirrored: each frame is one of the camera's pictures as it sent it)
            var sent = Enumerable.Range(0, moving.Shown + 1).Select(k => Convert.ToBase64String(LivePlan.Grab(CameraPicture(320, 240, k), 320, 240, false).Bgra)).ToHashSet();
            Check(gif.Frames.All(f => sent.Contains(Convert.ToBase64String(f.Bgra))), "a clip frame isn't one of the camera's pictures");
            Check(window.PlayBar.Visibility == Visibility.Visible && window.PictureInfo.Text.Contains("30 frames"), "the clip plays like a GIF: " + window.PictureInfo.Text);
        });

        static void CameraTurnsOff() => InAWindow(window =>
        {
            // another tab
            var cam = new FakeCamera { Name = "Desk camera" };
            CameraOnIn(window, cam);
            window.Tabs.SelectedItem = window.AsciiTab;
            Check(!window.CameraOn, "the ASCII tab didn't turn the camera off");
            window.Tabs.SelectedItem = window.DeresTab;
            Pump(() => cam.Holding == 0, 5000);
            Check(cam.Holding == 0, "the camera wasn't let go of after another tab");
            // opening a picture
            CameraOnIn(window, cam);
            window.OpenSample("cat-nap");
            Check(!window.CameraOn, "opening a picture didn't turn the camera off");
            WaitForPicture(window);
            Check(window.OpenedPicture.Name == "cat-nap.jpg", "the picture opened: " + window.OpenedPicture.Name);
            // a camera Windows keeps: said, under the button too, and nothing left on
            var locked = new FakeCamera { Name = "Locked", OpenTrouble = Webcams.Problem(unchecked((int)0x80070005), "Windows didn't start it") };
            window.Cameras = () => new FakeCameras(locked);
            window.StartCamera();
            Pump(() => !window.CameraOn, 5000);
            Check(!window.CameraOn && window.CameraLineText.StartsWith("Windows isn't letting b0r3d DeRes use the camera") && window.CameraButton.Visibility == Visibility.Visible,
                "a refused camera: " + window.CameraLineText);
            // two cameras: Flip shows and starts the other; the one used last is kept for next time
            var a = new FakeCamera { Name = "Camera A", Width = 320, Height = 240 };
            var b = new FakeCamera { Name = "Camera B", Width = 200, Height = 150 };
            CameraOnIn(window, a, b);
            Check(window.FlipButton.Visibility == Visibility.Visible, "Flip shows with two cameras");
            window.FlipCamera();
            Pump(() => window.PictureInfo.Text == "Camera B · 200×150" && window.LiveFrames > 0 && a.Holding == 0, 10000);
            Check(window.PictureInfo.Text == "Camera B · 200×150" && a.Holding == 0 && window.Current.CameraName == "Camera B", "Flip: " + window.PictureInfo.Text);
            window.StopCamera();
            Pump(() => b.Holding == 0, 5000);
            CameraOnIn(window, a, b);
            Check(window.PictureInfo.Text == "Camera B · 200×150", "the camera used last starts first: " + window.PictureInfo.Text);
            // Reset settings forgets the camera's choices, and leaves it on
            window.ResetSettings();
            Check(window.CameraOn && window.Current.CameraName == null && window.Current.CameraMirror, "Reset settings and the camera");
            window.Close();
            Pump(() => a.Holding == 0 && b.Holding == 0, 5000);
            Check(!window.CameraOn && a.Holding == 0 && b.Holding == 0, "closing the window let the camera go");
        });

        // The camera started again while the last one's live frame is still being made (a slow
        // look): its pictures come all the same, rather than the first going unheard for good
        static void CameraQuickRestart() => InAWindow(window =>
        {
            var cam = new FakeCamera { Name = "Desk camera" };
            CameraOnIn(window, cam);
            window.SlowLiveMs = 1000;
            Pump(() => false, 300);
            Check(window.LiveMaking, "a slow live frame should be under way");
            window.StopCamera();
            window.StartCamera();
            window.SlowLiveMs = 0;
            Pump(() => window.LiveFrames >= 3, 10000);
            Check(window.CameraShows && window.LiveFrames >= 3, "the camera started again stayed frozen: " + window.LiveFrames + " frames (" + window.CameraLineText + ")");
        });

        // While live, nothing plays under it: Play or pause is off and does nothing; and Use camera
        // waits while a picture opens (it would open under the live view, its play bar with it)
        static void CameraNothingPlaysUnder() => InAWindow(window =>
        {
            window.Cameras = () => new FakeCameras(new FakeCamera { Name = "Desk camera" });
            window.OpenSample("rukus-gif");
            Check(window.Opening && !window.CameraButton.IsEnabled && !window.CameraMenu.IsEnabled, "Use camera should wait while a picture opens");
            window.StartCamera();
            Check(!window.CameraOn, "the camera came on while a picture was opening");
            WaitForPicture(window);
            Check(window.CameraButton.IsEnabled && window.CameraMenu.IsEnabled && window.PlayMenu.IsEnabled, "Use camera and Play or pause once the GIF is open");
            CameraOnIn(window, new FakeCamera { Name = "Desk camera" });
            Check(!window.PlayMenu.IsEnabled && window.PlayBar.Visibility != Visibility.Visible, "Play or pause while live");
            window.PlayMenu.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Pump(() => false, 200);
            Check(System.Windows.Automation.AutomationProperties.GetName(window.PlayButton) != "Pause", "the GIF played under the live view");
        });

        // Flip while a camera is still starting goes on from that one, not from the first
        static void CameraFlipWhileStarting() => InAWindow(window =>
        {
            var a = new FakeCamera { Name = "Camera A" };
            var b = new FakeCamera { Name = "Camera B", OpenMs = 400 };
            var c = new FakeCamera { Name = "Camera C" };
            window.Current.CameraName = "Camera B";
            window.Cameras = () => new FakeCameras(a, b, c);
            window.StartCamera();
            // B, the one used last, starting: Flip goes on to C, not to B again
            Pump(() => Volatile.Read(ref b.Starts) > 0 && window.FlipButton.Visibility == Visibility.Visible, 5000);
            window.FlipCamera();
            Pump(() => window.PictureInfo.Text.StartsWith("Camera C ·"), 10000);
            Check(window.PictureInfo.Text.StartsWith("Camera C ·") && b.Holding == 0, "Flip while Camera B started: " + window.PictureInfo.Text);
            // two Flips in a row, the second while the first's camera starts: C, A, then B starting, then C
            window.FlipCamera();
            Pump(() => window.PictureInfo.Text.StartsWith("Camera A ·"), 10000);
            int starts = Volatile.Read(ref b.Starts);
            window.FlipCamera();
            Pump(() => Volatile.Read(ref b.Starts) > starts, 5000);
            window.FlipCamera();
            Pump(() => window.PictureInfo.Text.StartsWith("Camera C ·"), 10000);
            Check(window.PictureInfo.Text.StartsWith("Camera C ·") && a.Holding == 0 && b.Holding == 0, "two Flips in a row, from A: " + window.PictureInfo.Text);
        });

        // A camera that won't start: the words, and under them Windows' own side of it
        static void CameraTroubleDetail() => InAWindow(window =>
        {
            var n = new FakeCamera { Name = "Desk camera", OpenTrouble = new CameraException(Trouble.Failed, "Windows' media features aren't on this PC (Windows N needs the Media Feature Pack)") };
            window.Cameras = () => new FakeCameras(n);
            window.StartCamera();
            Pump(() => !window.CameraOn, 5000);
            Check(window.CameraLineText == "The camera didn't start. Try again, or open a picture instead.\nWindows' media features aren't on this PC (Windows N needs the Media Feature Pack).",
                "Windows' reason under the words: " + window.CameraLineText);
        });

        // Closing while a loop is made: the window waits for it to stop, the camera turns off at once
        static void CameraOffWhileClosing()
        {
            MainWindow window = NewMain();
            bool closed = false;
            window.Closed += (s, e) => closed = true;
            var cam = new FakeCamera { Name = "Desk camera" };
            string folder = Path.Combine(Work, "camera-closing");
            Fresh(folder);
            try
            {
                WaitForPicture(window);
                // a long loop: 48 frames at 8× through a CRT
                window.ScreenButtons.Children.OfType<RadioButton>().First(r => (string)r.Tag == Screens.Crt).IsChecked = true;
                window.LoopLengths.Children.OfType<RadioButton>().First(r => (int)r.Tag == 48).IsChecked = true;
                window.Sizes.Children.OfType<RadioButton>().First(r => (int)r.Tag == 8).IsChecked = true;
                var saving = window.SaveAnimation(Path.Combine(folder, "loop.gif"), false);
                Pump(() => window.Animating, 2000);
                CameraOnIn(window, cam);
                Check(window.Animating, "the loop was made before the camera came on");
                window.Close();
                Check(!closed && !window.CameraOn, "closing while a loop is made: the window should wait for it (" + (closed ? "closed" : "open") + "), the camera not (" + (window.CameraOn ? "on" : "off") + ")");
                Pump(() => closed, 10000);
                Await(saving);
                Pump(() => cam.Holding == 0, 5000);
                Check(closed && cam.Holding == 0, "the window closed once the loop stopped, and the camera was let go of");
            }
            finally
            {
                window.StopCamera(false);
                if (!closed) window.Close();
                TryDelete(folder);
            }
        }
    }
}
