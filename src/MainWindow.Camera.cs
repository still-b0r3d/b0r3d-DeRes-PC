using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using B0r3dDeRes.Camera;
using B0r3dDeRes.Engine;
using CameraFrame = B0r3dDeRes.Camera.Frame;
using Geometry = B0r3dDeRes.Engine.Geometry;

namespace B0r3dDeRes
{
    // The live camera (the website's): Use camera turns the preview into a live view through the
    // look, and every setting works on it as it comes. Each picture from the camera is grabbed small
    // (about 3 camera pixels for every machine pixel), made through an engine of its own off the
    // window's thread, and shown; pictures that come meanwhile are skipped, so a slow look never
    // queues up. Snap makes the camera's picture the picture; Record 3 s makes 30 of them, ten a
    // second, an animated GIF's frames. Either turns the camera off, as do Stop, another tab, opening
    // anything, the window minimized or closed. The camera is only ever started by the person (never
    // by a test or the self-test: they use pretend cameras), and its pictures stay in memory.
    public partial class MainWindow
    {
        // where cameras come from: Windows' own (the tests and the self-test give pretend ones)
        internal Func<ICameras> Cameras = () => new Webcams();

        CameraLive camera;                   // the camera on (or starting), or null
        List<CameraInfo> cameraList = new List<CameraInfo>();
        CameraInfo cameraNow;                // the one sending pictures (null while it starts)
        CameraInfo cameraWanted;             // the one starting, or picked last with Flip
        int cameraWidth, cameraHeight;
        readonly Deres liveEngine = new Deres();
        CameraFrame liveFrame = new CameraFrame();   // the camera's picture being made (taken from the camera's thread in turn)
        bool liveMaking;
        PictureSource liveSource;            // the last picture grabbed: it frames the controls while the camera is on
        int liveTick, liveFrames;
        readonly FpsMeter liveMeter = new FpsMeter();
        readonly Stopwatch liveClock = Stopwatch.StartNew();
        string clipName;
        DispatcherTimer clipTimer;

        // live frames per turn of the screen effect's moving parts (the website's LIVE_LOOP)
        const int LiveLoop = 32;

        // for the tests
        internal bool CameraOn => camera != null;
        internal bool CameraShows => camera != null && cameraNow != null;
        internal int LiveFrames => liveFrames;
        internal string CameraLineText => CameraLine.Text;
        internal Rendered LiveOnScreen => onScreen;   // the live frame (or a GIF's frame playing) on the stage
        internal bool LiveMaking => liveMaking;
        internal int SlowLiveMs;                      // a pretend slow look: each live frame takes this much longer

        // the picture the controls frame: the camera's while it's on, else the one open
        PictureSource PlannedSource => camera != null && liveSource != null ? liveSource : picture?.Source;

        internal bool DeresActive => Tabs.SelectedItem == DeresTab;

        void OnUseCamera(object sender, RoutedEventArgs e) => StartCamera();
        void OnStopCamera(object sender, RoutedEventArgs e) => StopCamera();
        void OnSnap(object sender, RoutedEventArgs e) => SnapCamera();
        void OnRecord(object sender, RoutedEventArgs e) => RecordCamera();
        void OnFlip(object sender, RoutedEventArgs e) => FlipCamera();

        void OnMirror(object sender, RoutedEventArgs e)
        {
            settings.CameraMirror = !settings.CameraMirror;
            Kept();
        }

        // The camera comes on (the one used last, else Windows' first): what plays stops where it
        // is, and the camera's pictures take the preview.
        internal void StartCamera()
        {
            // (not while a picture opens: it would open under the live view)
            if (camera != null || !DeresActive || Opening) return;
            if (fullScreen) SetFullScreen(false);
            player.Pause();
            videoPlayer.Pause();
            View.SetComparing(false);
            View.Hold(false);
            View.SetOriginal(null);
            ShowCompare();
            PlayBar.Visibility = VideoBar.Visibility = Visibility.Collapsed;
            var session = new CameraLive(Cameras(), Dispatcher) { Prefer = settings.CameraName };
            camera = session;
            cameraList = new List<CameraInfo>();
            cameraNow = cameraWanted = null;
            liveSource = null;
            onScreen = null;
            liveFrames = 0;
            liveTick = 0;
            session.Listed = list =>
            {
                if (camera != session) return;
                cameraList = list;
                Sync();
            };
            session.Starting = c =>
            {
                if (camera != session) return;
                cameraNow = null;
                cameraWanted = c;
                SayCamera(Words.CameraStarting(c.Name));
                Sync();
            };
            session.Started = (c, w, h) =>
            {
                if (camera != session) return;
                cameraNow = c;
                cameraWidth = w;
                cameraHeight = h;
                settings.CameraName = c.Name;
                liveMeter.Reset(liveClock.ElapsedMilliseconds);
                SayCamera(null);
                Kept();
            };
            session.NewPicture = () =>
            {
                if (camera == session && !liveMaking) MakeLive();
            };
            session.Clip = frames =>
            {
                if (camera == session) OpenClip(frames);
            };
            session.Failed = why =>
            {
                if (camera != session) return;
                StopCamera();
                SayCamera(Words.CameraTrouble(why.Words, why.Message));
                Problem(why.Words);
            };
            session.Start();
            SayCamera(Words.CameraStarting(null));
            ShowStage();
            Sync();
            CommandManager.InvalidateRequerySuggested();
        }

        // The camera off. `restore`: the picture open comes back (not when a snap or a clip is about
        // to replace it).
        internal void StopCamera(bool restore = true)
        {
            CameraLive session = camera;
            if (session == null) return;
            camera = null;   // (from here on, nothing the session says is heard)
            session.Stop();
            clipTimer?.Stop();
            cameraNow = null;
            liveSource = null;
            onScreen = null;
            SayCamera(null);
            View.ShowScreen(null, 0, 0);
            if (restore)
            {
                PlayBar.Visibility = picture?.Animation != null && picture.FrameCount > 1 ? Visibility.Visible : Visibility.Collapsed;
                VideoBar.Visibility = picture?.Video != null ? Visibility.Visible : Visibility.Collapsed;
                if (picture != null) RenderSoon();
                else
                {
                    View.Show(null, 0, 0, 1);
                    shown = null;
                }
            }
            Sync();
            ShowStage();
            ShowReadout();
            ShowZoom();
            CommandManager.InvalidateRequerySuggested();
        }

        // The camera's newest picture through the settings, at the live size, off the window's
        // thread; the next one as soon as this one's shown (pictures that came meanwhile are skipped).
        async void MakeLive()
        {
            CameraLive session = camera;
            if (session == null || liveMaking) return;
            liveMaking = true;
            try
            {
                while (camera == session && session.TakeNewest(liveFrame))
                {
                    CameraFrame f = liveFrame;   // (this window's until it takes the next)
                    DeresSettings s = Signal.ForRender(settings.Deres.Clone(), settings.SignalOn);
                    FxSettings fx = settings.Fx.Screen != Screens.Off ? settings.Fx.Clone() : null;
                    SignalSettings signal = settings.SignalOn?.Clone();
                    Profile p = Profiles.ById(s.ProfileId) ?? Profiles.Default;
                    LivePlan.InputSize(f.Width, f.Height, p, s.Custom, s.CustomWidth, s.Fit, out int gw, out int gh);
                    Geometry g = Geometry.Plan(gw, gh, f.Width, f.Height, p, s.Custom, s.CustomWidth, s.Fit);
                    bool tv = settings.TvPixels && !g.SquarePixels;
                    double ratio = tv ? (double)g.ParX / g.ParY : 1;
                    int scale = fx != null || signal != null ? LivePlan.FxScale(ScreenScale(g.Width, g.Height, ratio, View.ZoomFor(g.Width, g.Height, ratio)), g.Width, g.Height, g.ParX, g.ParY, tv) : 0;
                    bool mirror = settings.CameraMirror;
                    int tick = liveTick++, slow = SlowLiveMs;
                    (PlayedFrame view, PictureSource grabbed) = await Task.Run(() =>
                    {
                        if (slow > 0) System.Threading.Thread.Sleep(slow);
                        return MakeLiveFrame(f, gw, gh, mirror, s, fx, signal, scale, tv, tick);
                    });
                    if (camera != session) break;
                    bool resized = liveSource == null || liveSource.Width != grabbed.Width || liveSource.Height != grabbed.Height || liveSource.OrigW != grabbed.OrigW || liveSource.OrigH != grabbed.OrigH;
                    liveSource = grabbed;
                    if (resized) Sync();
                    ShowLive(view);
                }
            }
            catch (Exception x) when (x is OutOfMemoryException || x is InvalidOperationException || x is ArgumentException)
            {
                if (camera == session)
                {
                    StopCamera();
                    Problem("The live picture couldn't be made: " + x.Message);
                }
            }
            finally
            {
                liveMaking = false;
                // the camera started again while this frame was made: the new one's first picture
                // came meanwhile (and it says so only once until one is taken), so its frames start here
                if (camera != null && camera != session) MakeLive();
            }
        }

        // On a worker thread: the camera's picture grabbed at gw × gh, through the settings, and
        // through the screen effect or the signal at `scale` (0: neither)
        (PlayedFrame, PictureSource) MakeLiveFrame(CameraFrame f, int gw, int gh, bool mirror, DeresSettings s, FxSettings fx, SignalSettings signal, int scale, bool tv, int tick)
        {
            PictureSource grabbed = LivePlan.Grab(f, gw, gh, mirror, f.Width, f.Height);
            lock (liveEngine)
            {
                liveEngine.SetSource(grabbed);
                Rendered r = liveEngine.Render(s);
                var view = new PlayedFrame { Picture = r };
                if (scale > 0)
                {
                    (int w, int h) = PlaySize(r.Geometry, scale, tv);
                    double phase = (double)(tick % LiveLoop) / LiveLoop;
                    SignalImage through = signal != null ? Signal.Run(r, r.Profile.Id, signal, tick) : null;
                    RowRenderer render = through != null ? Signal.Renderer(through, w, h, fx, phase, r.Paper)
                        : new FxRenderer(r.Rgb, r.Width, r.Height, r.Paper, r.Alpha, w, h, fx, phase);
                    view.Screen = render.Rgba();
                    view.ScreenWidth = w;
                    view.ScreenHeight = h;
                }
                return (view, grabbed);
            }
        }

        // a live frame on the stage, its readout under it (with the frames a second)
        void ShowLive(PlayedFrame view)
        {
            Rendered r = view.Picture;
            onScreen = r;
            liveFrames++;
            double now = liveClock.ElapsedMilliseconds;
            liveMeter.Frame(now);
            View.SetChecker(false);
            View.Show(r.Rgb, r.Width, r.Height, PixelRatio(r), r.Alpha);
            View.ShowScreen(view.Screen, view.ScreenWidth, view.ScreenHeight);
            ShowTileFit(r.TileFit);
            ShowStage();
            ShowReadout();
            ShowZoom();
            if (camera != null && !camera.Shooting) SayCamera(liveMeter.Slow(now) ? Words.SlowLive(liveMeter.Fps(now)) : null);
        }

        // what the readout adds while the camera is on
        string LiveRate => camera != null && onScreen != null ? " · " + liveMeter.Fps(liveClock.ElapsedMilliseconds) + " fps" : "";

        // Snap: the camera's picture as it is now (mirrored as it's shown) becomes the picture, as if it
        // had been opened; the camera turns off.
        internal void SnapCamera()
        {
            CameraLive session = camera;
            if (session == null || session.Shooting || cameraNow == null) return;
            PictureSource shot = session.Snap(settings.CameraMirror);
            if (shot == null) return;
            string name = LivePlan.Name("camera", DateTime.Now) + ".png";
            StopCamera(false);
            Open(() => new LoadedPicture { Source = shot, Name = name }, name);
        }

        // Record 3 s: 30 of the camera's pictures, ten a second, at the size an animated GIF of 30
        // frames from it is held; they open like an animated GIF, and the camera turns off.
        internal void RecordCamera()
        {
            CameraLive session = camera;
            if (session == null || session.Shooting || cameraNow == null) return;
            LivePlan.ClipSize(cameraWidth, cameraHeight, Pictures.GifBudget, out int w, out int h);
            clipName = LivePlan.Name("camera-clip", DateTime.Now);
            session.Shoot(w, h, settings.CameraMirror);
            SayCamera(Words.Recording);
            if (clipTimer == null)
            {
                clipTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
                clipTimer.Tick += (s, e) => SyncCamera();
            }
            clipTimer.Start();
            SyncCamera();
        }

        // the clip's frames, as an opened animated GIF: 100 ms each, looping for ever
        void OpenClip(PictureSource[] frames)
        {
            string name = clipName ?? LivePlan.Name("camera-clip", DateTime.Now);
            StopCamera(false);
            if (frames.Length < 2) return;
            int[] delays = Enumerable.Repeat(LivePlan.ClipDelayMs, frames.Length).ToArray();
            var gif = new AnimatedGif
            {
                Width = frames[0].Width, Height = frames[0].Height, OrigW = frames[0].OrigW, OrigH = frames[0].OrigH,
                Frames = frames, DelaysMs = delays, LoopCount = 0, Total = frames.Length, HasAlpha = false,
                DurationMs = (double)LivePlan.ClipDelayMs * frames.Length, SignalStep = Signal.EngineStep(1000.0 / LivePlan.ClipDelayMs),
            };
            Open(() => new LoadedPicture { Source = frames[0], Name = name, Frames = frames.Length, Animation = gif }, name);
        }

        // Flip: the next camera, where there are two or more: on from the one showing, else the one
        // starting (or picked last), else the one the camera starts with (the one used last, else
        // the first)
        internal void FlipCamera()
        {
            if (camera == null || camera.Shooting || cameraList.Count < 2) return;
            CameraInfo from = cameraNow ?? cameraWanted ?? cameraList.Find(c => c.Name == camera.Prefer);
            int at = from != null ? cameraList.FindIndex(c => c.Id == from.Id) : 0;
            int next = (Math.Max(0, at) + 1) % cameraList.Count;
            cameraNow = null;
            cameraWanted = cameraList[next];
            camera.Choose(next);
            Sync();
        }

        // the line under the camera's buttons: `text`, or the usual one
        void SayCamera(string text) => CameraLine.Text = text ?? Words.CameraStaysHere;

        // the camera's buttons and lines, from what it's doing (part of Sync)
        void SyncCamera()
        {
            bool on = camera != null;
            Visibility Live = on ? Visibility.Visible : Visibility.Collapsed, Still = on ? Visibility.Collapsed : Visibility.Visible;
            OpenButton.Visibility = CameraButton.Visibility = SnipRow.Visibility = SampleLine.Visibility = DropHint.Visibility = Still;
            CameraPanel.Visibility = Live;
            // (Use camera waits while a picture opens)
            CameraButton.IsEnabled = CameraMenu.IsEnabled = !on && !Opening;
            int shots = camera?.Shots ?? -1;
            bool shooting = shots >= 0;
            bool started = on && cameraNow != null;
            FlipButton.Visibility = cameraList.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
            SnapButton.IsEnabled = RecordButton.IsEnabled = started && !shooting;
            FlipButton.IsEnabled = MirrorButton.IsEnabled = !shooting;
            MirrorButton.IsChecked = settings.CameraMirror;
            RecordButton.Content = shooting ? Words.RecordingLeft(LivePlan.ClipFrames - shots, LivePlan.ClipDelayMs) : "Record 3 s";
            if (!shooting) clipTimer?.Stop();
            if (string.IsNullOrEmpty(CameraLine.Text)) SayCamera(null);
            LiveNote.Visibility = Live;
            if (on)
            {
                // saving waits for a snap or a clip
                foreach (UIElement b in new UIElement[] { SaveGifButton, SaveApngButton, SaveMp4Button, SaveFramesButton, EmulatorButton, GrabPaletteButton })
                    b.IsEnabled = false;
                foreach (UIElement b in WallpaperButtons.Children) b.IsEnabled = false;
                foreach (UIElement m in new UIElement[] { SaveGifMenu, SaveApngMenu, SaveMp4Menu, SaveFramesMenu, WallpaperMenu, EmulatorMenu, GrabMenu })
                    m.IsEnabled = false;
                BackgroundPanel.Visibility = Visibility.Collapsed;
            }
        }
    }
}
