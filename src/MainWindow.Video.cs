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
using B0r3dDeRes.Engine;
using B0r3dDeRes.Video;
using Geometry = B0r3dDeRes.Engine.Geometry;

namespace B0r3dDeRes
{
    // Videos: opening one (its paused frame is the picture, so every control works on it), the
    // timeline under the picture, the frames made while it plays, and saving it through the look
    // as an MP4 with its sound, a GIF or APNG of its frames, or numbered PNGs.
    public partial class MainWindow
    {
        readonly Deres videoPlayEngine = new Deres();

        void BuildVideo()
        {
            videoPlayer = new VideoPlayer(VideoPlayButton, StepBackButton, StepOnButton, TimeSlider, VideoTime, SetStartButton, SetStopButton, WholeButton, RangeText, VideoSoundBox,
                VideoBar, VideoMaker, ShowVideoPlayed, OnPlayStarted, OnVideoPaused, Problem, () => Sync());
            RadioButton Rate(int fps, string text, string name)
            {
                var button = new RadioButton { Style = (Style)FindResource("Segment"), GroupName = "VideoRate", Tag = fps, Content = text, Padding = new Thickness(0), Margin = new Thickness(0, 0, 6, 6) };
                AutomationProperties.SetName(button, name);
                button.Checked += OnVideoRate;
                return button;
            }
            VideoRates.Children.Add(Rate(0, "Own", "The video's own frame rate"));
            foreach (int fps in Settings.VideoRates) VideoRates.Children.Add(Rate(fps, fps.ToString(CultureInfo.InvariantCulture), fps + " frames a second"));
        }

        internal VideoPlayer Timeline => videoPlayer;

        // A video, opened off the window's thread: its first frame is the picture.
        static LoadedPicture LoadVideo(string path)
        {
            try
            {
                VideoFile file = VideoFile.Open(path);
                VideoFrame first;
                using (VideoReader reader = file.Frames()) first = reader.Next();
                if (first == null) throw new PictureException("Windows found no frames in it");
                var clip = new VideoClip(file, first);
                return new LoadedPicture { Source = clip.Source, Name = Path.GetFileName(path), Video = clip, FilePath = path };
            }
            catch (VideoException x)
            {
                throw new PictureException(x.Message.TrimEnd('.'));
            }
        }

        // what makes a frame through the look at the playing size (on any thread), with the settings as they are now
        Func<VideoFrame, long, PlayedFrame> VideoMaker()
        {
            DeresSettings s = Signal.ForRender(settings.Deres.Clone(), settings.SignalOn);
            FxSettings fx = settings.Fx.Screen != Screens.Off ? settings.Fx.Clone() : null;
            SignalSettings signal = settings.SignalOn?.Clone();
            Geometry g = PlannedGeometry();
            double scale = PlayingScale(g);
            bool tv = settings.TvPixels && g != null && !g.SquarePixels;
            double fps = settings.VideoFps(picture?.Video?.File.Fps ?? 30);
            return (f, time) =>
            {
                lock (videoPlayEngine)
                {
                    videoPlayEngine.SetSource(new PictureSource(f.Bgra, f.Width, f.Height));
                    Rendered r = videoPlayEngine.Render(s);
                    var view = new PlayedFrame { Picture = r };
                    if (scale > 0)
                    {
                        (int w, int h) = PlaySize(r.Geometry, scale, tv);
                        double phase = VideoExport.PhaseAt(time);
                        SignalImage through = signal != null ? Signal.Run(r, r.Profile.Id, signal, VideoExport.SignalFrame(time, fps)) : null;
                        RowRenderer render = through != null ? Signal.Renderer(through, w, h, fx, phase, r.Paper) : new FxRenderer(r.Rgb, r.Width, r.Height, r.Paper, r.Alpha, w, h, fx, phase);
                        view.Screen = render.Rgba();
                        view.ScreenWidth = w;
                        view.ScreenHeight = h;
                    }
                    return view;
                }
            };
        }

        void ShowVideoPlayed(VideoPlayed v)
        {
            if (picture?.Video == null) return;
            onScreenTime = v.Time;
            ShowPlayed(v.View, picture.Video.FrameNumber(v.Time));
        }

        // paused on a frame (or moved to one): it's the picture now, made in full
        void OnVideoPaused(long time, PictureSource source)
        {
            if (picture?.Video == null) return;
            picture.Source = source;
            frameTime = time;
            frame = picture.Video.FrameNumber(time);
            // (the tab showing makes it: a move can land after its own tab was left)
            VideoFrameMoved(false);
            System.Windows.Input.CommandManager.InvalidateRequerySuggested();
        }

        // ---- the Export section's video rows (inside Sync)

        void SyncVideo(Profile p, Geometry g)
        {
            VideoClip clip = picture?.Video;
            bool video = clip != null;
            VideoRows.Visibility = video ? Visibility.Visible : Visibility.Collapsed;
            SaveMp4Button.Visibility = SaveFramesButton.Visibility = video ? Visibility.Visible : Visibility.Collapsed;
            // (the menu's lines are the studio's on its tab)
            if (!AsciiActive) SaveMp4Menu.Visibility = SaveFramesMenu.Visibility = video ? Visibility.Visible : Visibility.Collapsed;
            if (!video) return;
            double own = clip.File.Fps;
            foreach (RadioButton b in VideoRates.Children)
            {
                int fps = (int)b.Tag;
                // only rates slower than its own
                b.Visibility = fps == 0 || fps < own - 0.01 ? Visibility.Visible : Visibility.Collapsed;
                b.IsChecked = fps == 0 ? !(settings.VideoRate > 0 && settings.VideoRate < own - 0.01) : fps == settings.VideoRate;
                if (fps == 0) b.Content = "Own (" + Words.FrameRate(own) + ")";
            }
            VideoKeepSound.IsChecked = settings.VideoSound;
            VideoKeepSound.IsEnabled = clip.File.HasSound;
            VideoKeepSound.Content = clip.File.HasSound ? "Keep the sound" : "Keep the sound (it has none)";
            bool idle = animating == null;
            string hint = "";
            bool fits = false;
            if (g != null)
            {
                Export.Size(g.Width, g.Height, g.ParX, g.ParY, settings.Multiplier, settings.TvPixels && !g.SquarePixels, out int w, out int h);
                VideoExport.Mp4Size(w, h, out int mw, out int mh);
                double fps = settings.VideoFps(own);
                int frames = VideoExport.FrameCount(clip.From, clip.To, fps);
                fits = Mp4Writer.Fits(mw, mh);
                hint = fits
                    ? "MP4: " + mw + "×" + mh + ", " + frames.ToString("N0", CultureInfo.InvariantCulture) + " frames at " + Words.FrameRate(fps) + " a second" + (clip.File.HasSound && settings.VideoSound ? ", with its sound." : ", no sound.")
                    : "At this Size the frames are " + w + "×" + h + ": too big for an MP4 (4096×2304 at most). Pick a smaller Size, or save PNG frames.";
            }
            VideoHint.Text = hint;
            SaveMp4Button.IsEnabled = fits && idle;
            SaveFramesButton.IsEnabled = g != null && idle;
            if (!AsciiActive)
            {
                SaveMp4Menu.IsEnabled = SaveMp4Button.IsEnabled;
                SaveFramesMenu.IsEnabled = SaveFramesButton.IsEnabled;
            }
        }

        void OnVideoRate(object sender, RoutedEventArgs e)
        {
            if (Quiet) return;
            settings.VideoRate = (int)((RadioButton)sender).Tag;
            Kept();
        }

        void OnVideoKeepSound(object sender, RoutedEventArgs e)
        {
            if (Quiet) return;
            settings.VideoSound = VideoKeepSound.IsChecked == true;
            Kept();
        }

        // ---- saving a video

        void OnSaveMp4(object sender, RoutedEventArgs e)
        {
            if (AsciiActive) AskAndSaveAsciiFrames(AsciiFramesOut.Mp4);
            else AskAndSaveVideo(VideoOut.Mp4);
        }
        void OnSaveFrames(object sender, RoutedEventArgs e) => AskAndSaveVideo(VideoOut.Frames);

        internal enum VideoOut { Mp4, Gif, Apng, Frames }

        async void AskAndSaveVideo(VideoOut kind)
        {
            if (animating != null || askingForAnimation || picture?.Video == null) return;
            askingForAnimation = true;
            try
            {
                Rendered r = await Latest();
                if (r == null || shownFrom?.Video == null) return;
                string path;
                try
                {
                    path = kind == VideoOut.Frames ? FileDialogs.PickFolder(this, "Save the frames into a folder")
                        : FileDialogs.Save(this, "Save the video as an MP4", "MP4 video", ".mp4", VideoFileName(r, kind));
                }
                catch (COMException x)
                {
                    MessageBox.Show(this, "Windows' dialog didn't open:\n\n" + x.Message, Program.Name, MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                if (path != null) CouldntSave(path, await SaveVideo(path, kind));
            }
            finally
            {
                askingForAnimation = false;
            }
        }

        // the website's names: clip-nes-rf.mp4 (and .gif, -animated.png); clip-nes-rf for the frames' folder
        internal string VideoFileName(Rendered r, VideoOut kind)
        {
            string name = Export.FramesName(Export.BaseName(shownFrom?.Name ?? "video"), r.Profile.Id, SignalConnection(r), kind == VideoOut.Apng);
            if (kind == VideoOut.Mp4) return Path.ChangeExtension(name, ".mp4");
            if (kind == VideoOut.Frames) return Path.GetFileNameWithoutExtension(name);
            return name;
        }

        // The open video through the look as `kind` at `path` (a folder for PNG frames): from its
        // start to its stop, at the frame rate and Size chosen, made off the window's thread, its
        // progress and time left in the bottom line, Stop leaving no file. What went wrong, or null.
        internal async Task<string> SaveVideo(string path, VideoOut kind)
        {
            Rendered r = shown;
            LoadedPicture pic = shownFrom;
            if (r == null || pic?.Video == null) return "there's no video";
            if (animating != null) return "something else is being made: wait for it, or Stop it";
            VideoClip clip = pic.Video;
            // a GIF or APNG at the Size chosen, or the biggest that fits (as the line under Export says)
            Geometry g = r.Geometry;
            int m = kind == VideoOut.Gif || kind == VideoOut.Apng
                ? Animation.FitFor(g.Width, g.Height, g.ParX, g.ParY, settings.TvPixels, settings.Multiplier, Animation.GifFrames, settings.SignalOn != null ? settings.LoopFrames : (int?)null, AnimationLimits.Pc).Multiplier ?? settings.Multiplier
                : settings.Multiplier;
            var o = new VideoExportOptions
            {
                File = clip.File, From = clip.From, To = clip.To, Fps = settings.VideoFps(clip.File.Fps), Settings = shownWith.Clone(), Multiplier = m,
                Tv = settings.TvPixels, Fx = settings.ExportScreen?.Clone(), Signal = settings.SignalOn?.Clone(), Sound = settings.VideoSound,
                Lanes = Math.Max(1, Math.Min(4, Environment.ProcessorCount / 3)), Limits = AnimationLimits.Pc,
            };
            string what = kind == VideoOut.Frames ? "the PNG frames" : Path.GetFileName(path);
            var cancel = new CancellationTokenSource();
            animating = cancel;
            o.Cancel = cancel.Token;
            Saving = true;
            Sync();
            Flash("Making " + what + "…", "Dim");
            var clock = Stopwatch.StartNew();
            o.Progress = (done, total, note) => Dispatcher.BeginInvoke(new Action(() =>
            {
                if (animating != cancel || cancel.IsCancellationRequested) return;
                string left = "";
                if (done > 0 && done < total && clock.Elapsed.TotalSeconds > 2)
                {
                    double seconds = clock.Elapsed.TotalSeconds / done * (total - done);
                    left = ", about " + (seconds < 60 ? Math.Max(1, (int)Math.Round(seconds)) + " s" : VideoPlayer.Clock((long)(seconds * VideoFile.Second)).Split('.')[0]) + " left";
                }
                Flash("Making " + what + ": " + (note ?? "frame " + done.ToString("N0", CultureInfo.InvariantCulture) + " of " + total.ToString("N0", CultureInfo.InvariantCulture) + left), "Dim");
            }));
            AnimationResult result = null;
            int pngs = 0;
            string problem = null;
            bool stopped = false;
            try
            {
                await Task.Run(() =>
                {
                    switch (kind)
                    {
                        case VideoOut.Mp4:
                            Exports.Write(path, stream =>
                            {
                                result = VideoExport.Mp4(stream, o);
                                cancel.Token.ThrowIfCancellationRequested();
                            });
                            break;
                        case VideoOut.Frames:
                            pngs = VideoExport.PngFrames(path, VideoFileName(r, VideoOut.Frames), o, (p, write) => Exports.Write(p, write)).Count;
                            break;
                        default:
                            Exports.Write(path, stream =>
                            {
                                result = VideoExport.Animation(stream, o, kind == VideoOut.Apng);
                                cancel.Token.ThrowIfCancellationRequested();
                            });
                            break;
                    }
                });
            }
            catch (OperationCanceledException)
            {
                stopped = true;
            }
            catch (Exception x) when (x is AnimationException || x is VideoException || x is IOException || x is UnauthorizedAccessException || x is NotSupportedException || x is System.Security.SecurityException)
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
                Sync();
            }
            if (closeAfterAnimation)
            {
                _ = Dispatcher.BeginInvoke(new Action(Close));
                return null;
            }
            if (stopped) Flash("Stopped: " + (kind == VideoOut.Frames ? "the frames made so far are in the folder." : Path.GetFileName(path) + " wasn't saved."), "Warn");
            else if (problem == null)
                Flash(kind == VideoOut.Frames ? "Saved " + pngs.ToString("N0", CultureInfo.InvariantCulture) + " PNG frames into " + Path.GetFileName(path.TrimEnd('\\')) + "."
                    : "Saved " + Path.GetFileName(path) + " (" + result.Width + "×" + result.Height + ", " + result.Frames.ToString("N0", CultureInfo.InvariantCulture) + " frames).", "Good");
            return problem;
        }
    }
}
