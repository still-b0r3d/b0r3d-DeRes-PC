using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using B0r3dDeRes.Engine;
using Geometry = B0r3dDeRes.Engine.Geometry;

namespace B0r3dDeRes
{
    // Animated GIFs and animations: the play bar and what it plays, and GIF and APNG exports (the
    // Export section's Animation loop: an animated GIF's own frames, the Screen FX loop, the A/B
    // wipe and the dither shimmer), as on the website, with the PC's bigger limits.
    public partial class MainWindow
    {
        // the frames kept while a GIF plays, in pixels (each its screen effect at the playing size):
        // 64 MP, 256 MB
        const long PlayBudget = 64L * 1024 * 1024;

        // with an animated GIF open, its animation is its own frames unless another loop is picked
        // (back to its frames for every GIF opened)
        bool gifFramesChosen = true;

        // the Animation loop's buttons, in the website's order and words
        static readonly (string Id, string Text, string Name)[] animChoices =
        {
            (Animation.GifFrames, "GIF frames", "GIF frames, the GIF's own"), (Animation.ScreenFx, "Screen FX", "Screen FX loop"),
            (Animation.CompareWipe, "A/B Wipe", "A/B wipe, before and after"), (Animation.DitherShimmer, "Shimmer", "Shimmer, the dither moving"),
        };

        void BuildAnimation()
        {
            RadioButton Segment(string group, object tag, string text, string name, RoutedEventHandler checkedHandler)
            {
                var button = new RadioButton { Style = (Style)FindResource("Segment"), GroupName = group, Tag = tag, Content = text, Padding = new Thickness(0), Margin = new Thickness(0, 0, 6, 6) };
                AutomationProperties.SetName(button, name);
                button.Checked += checkedHandler;
                return button;
            }
            foreach (var choice in animChoices) AnimStyles.Children.Add(Segment("AnimStyle", choice.Id, choice.Text, choice.Name, OnAnimStyle));
            foreach (int n in Settings.LoopFrameCounts) LoopLengths.Children.Add(Segment("LoopLength", n, n + " frames", "Loop of " + n + " frames", OnLoopLength));
            foreach (int fps in Settings.LoopSpeeds) LoopSpeeds.Children.Add(Segment("LoopSpeed", fps, fps + " fps", "Loop at " + fps + " frames a second", OnLoopSpeed));
        }

        // what the animation is made of: the GIF's own frames, or a loop style
        internal string AnimationKind => picture?.Animation != null && gifFramesChosen ? Animation.GifFrames : settings.AnimationStyle;

        // the size a GIF or APNG of the picture comes out at, with the PC's limits
        AnimationFit FitNow(Geometry g) =>
            Animation.FitFor(g.Width, g.Height, g.ParX, g.ParY, settings.TvPixels, settings.Multiplier, AnimationKind, settings.SignalOn != null ? settings.LoopFrames : (int?)null, AnimationLimits.Pc);

        // the Animation loop's controls and the line by its buttons, from the settings (inside Sync)
        void SyncAnimation(Profile p, Geometry g)
        {
            bool gif = picture?.Animation != null;
            string kind = AnimationKind;
            foreach (RadioButton b in AnimStyles.Children)
            {
                string id = (string)b.Tag;
                b.Visibility = id == Animation.GifFrames && !gif ? Visibility.Collapsed : Visibility.Visible;
                b.IsChecked = id == kind;
            }
            // the length and speed of the Screen FX loop with the signal on
            bool signalLoop = settings.SignalOn != null && kind == Animation.ScreenFx;
            LoopRows.Visibility = signalLoop ? Visibility.Visible : Visibility.Collapsed;
            foreach (RadioButton b in LoopLengths.Children) b.IsChecked = (int)b.Tag == settings.LoopFrames;
            foreach (RadioButton b in LoopSpeeds.Children) b.IsChecked = (int)b.Tag == settings.LoopFps;
            string hint = "";
            bool fits = false;
            if (g != null)
            {
                AnimationFit fit = FitNow(g);
                bool withFx = settings.ExportFx && settings.Fx.Screen != Screens.Off && settings.Multiplier >= Export.FxMinMultiplier;
                hint = Words.AnimationHint(fit, g, settings.TvPixels && !g.SquarePixels, settings.Multiplier, withFx, signalLoop, kind == Animation.ScreenFx && settings.SignalOn == null, settings.Deres.Custom);
                fits = fit.Multiplier != null;
            }
            AnimHint.Text = hint;
            AnimHint.Visibility = picture != null && hint.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            bool idle = animating == null;
            SaveGifButton.IsEnabled = SaveApngButton.IsEnabled = SaveGifMenu.IsEnabled = SaveApngMenu.IsEnabled = picture != null && fits && idle;
            StopAnimationButton.Visibility = idle ? Visibility.Collapsed : Visibility.Visible;
            PlayMenu.IsEnabled = player.Animated;
        }

        void OnAnimStyle(object sender, RoutedEventArgs e)
        {
            if (Quiet) return;
            string id = (string)((RadioButton)sender).Tag;
            gifFramesChosen = id == Animation.GifFrames;
            if (id != Animation.GifFrames) settings.AnimationStyle = id;
            Kept();
        }

        void OnLoopLength(object sender, RoutedEventArgs e)
        {
            if (Quiet) return;
            settings.LoopFrames = (int)((RadioButton)sender).Tag;
            Kept();
        }

        void OnLoopSpeed(object sender, RoutedEventArgs e)
        {
            if (Quiet) return;
            settings.LoopFps = (int)((RadioButton)sender).Tag;
            Kept();
        }

        void OnPlayMenu(object sender, RoutedEventArgs e) => player.Toggle();

        // ---- playing ----

        readonly Deres playEngine = new Deres();
        string playKey = "";

        // The preview's enlargement for the screen effect or the signal while a GIF plays (0 when
        // neither is on): the zoom's, as when it's still, or smaller so every frame is kept.
        double PlayingScale(Geometry g)
        {
            if (g == null || picture == null || settings.Fx.Screen == Screens.Off && settings.SignalOn == null) return 0;
            bool tv = settings.TvPixels && !g.SquarePixels;
            double ratio = tv ? (double)g.ParX / g.ParY : 1;
            int scale = ScreenScale(g.Width, g.Height, ratio, View.ZoomFor(g.Width, g.Height, ratio));
            return Player.PlayScale(scale, picture.FrameCount, s => PlaySize(g, s, tv), PlayBudget);
        }

        // the screen effect's size at an enlargement (in between whole sizes too, while a GIF plays)
        static (int W, int H) PlaySize(Geometry g, double s, bool tv) =>
            ((int)JsMath.Round(tv && g.ParX != g.ParY ? JsMath.Round(g.Width * s * g.ParX / g.ParY) : g.Width * s), (int)JsMath.Round(g.Height * s));

        // what the frames played are made with: when it changes, the ones made are dropped
        string PlayKeyNow()
        {
            Settings copy = settings.Copy();
            copy.Multiplier = 4;
            copy.ExportFx = false;
            copy.LoopFrames = 16;
            copy.LoopFps = 15;
            copy.AnimationStyle = Animation.ScreenFx;
            return Looks.FileText(copy) + "|" + PlayingScale(PlannedGeometry()).ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        }

        void CheckPlayKey()
        {
            if (picture?.Animation == null) return;
            string key = PlayKeyNow();
            if (key == playKey) return;
            playKey = key;
            player.Invalidate();
        }

        // frame k through the current settings, at the playing size, off the window's thread
        Task<PlayedFrame> MakePlayed(int k)
        {
            LoadedPicture pic = picture;
            AnimatedGif gif = pic?.Animation;
            if (gif == null) return Task.FromException<PlayedFrame>(new InvalidOperationException("There's no animated GIF open"));
            DeresSettings s = Signal.ForRender(settings.Deres.Clone(), settings.SignalOn);
            FxSettings fx = settings.Fx.Screen != Screens.Off ? settings.Fx.Clone() : null;
            SignalSettings signal = settings.SignalOn?.Clone();
            Geometry g = PlannedGeometry();
            double scale = PlayingScale(g);
            bool tv = settings.TvPixels && g != null && !g.SquarePixels;
            return Task.Run(() =>
            {
                lock (playEngine)
                {
                    PictureSource source = pic.SourceAt(k);
                    if (playEngine.Source != source) playEngine.SetSource(source);
                    Rendered r = playEngine.Render(s);
                    var view = new PlayedFrame { Picture = r };
                    if (scale > 0)
                    {
                        (int w, int h) = PlaySize(r.Geometry, scale, tv);
                        SignalImage through = signal != null ? Signal.Run(r, r.Profile.Id, signal, k * gif.SignalStep) : null;
                        RowRenderer render = through != null ? Signal.Renderer(through, w, h, fx, gif.Phase(k), r.Paper)
                            : new FxRenderer(r.Rgb, r.Width, r.Height, r.Paper, r.Alpha, w, h, fx, gif.Phase(k));
                        view.Screen = render.Rgba();
                        view.ScreenWidth = w;
                        view.ScreenHeight = h;
                    }
                    return view;
                }
            });
        }

        // a played frame on the stage, its readout under it
        void ShowPlayed(PlayedFrame view, int k)
        {
            Rendered r = view.Picture;
            onScreen = r;
            onScreenFrame = k;
            View.SetChecker(settings.Deres.Background == Backgrounds.Transparent && picture != null && picture.Source.HasAlpha);
            View.Show(r.Rgb, r.Width, r.Height, PixelRatio(r), r.Alpha);
            View.ShowScreen(view.Screen, view.ScreenWidth, view.ScreenHeight);
            // (the small picture under it drags out the frame playing)
            DragThumb.Source = Sharing.Small(r);
            ShowTileFit(r.TileFit);
            ShowStage();
            ShowReadout();
        }

        // while it plays there's no original to show for a moving frame: Compare comes back on the paused frame
        void OnPlayStarted()
        {
            View.SetComparing(false);
            View.Hold(false);
            View.SetOriginal(null);
            ShowCompare();
            System.Windows.Input.CommandManager.InvalidateRequerySuggested();
        }

        // paused on frame k (or the slider let go there): it's the picture now, made at the full size
        void OnPlayPaused(int k)
        {
            frame = k;
            RenderSoon();
            System.Windows.Input.CommandManager.InvalidateRequerySuggested();
        }

        // ---- saving GIF and APNG ----

        CancellationTokenSource animating;
        bool closeAfterAnimation;   // the window was closed while one was made: it closes once that's stopped
        internal bool Animating => animating != null;

        void OnSaveGif(object sender, RoutedEventArgs e) => AskAndSaveAnimation(false);
        void OnSaveApng(object sender, RoutedEventArgs e) => AskAndSaveAnimation(true);

        void OnStopAnimation(object sender, RoutedEventArgs e) => StopAnimation();

        internal void StopAnimation()
        {
            if (animating == null) return;
            animating.Cancel();
            Flash("Stopping…", "Dim");
        }

        bool askingForAnimation;   // a save's dialog is coming or open: a second click waits for it

        async void AskAndSaveAnimation(bool apng)
        {
            if (animating != null || askingForAnimation) return;
            askingForAnimation = true;
            try
            {
                Rendered r = await Latest();
                if (r == null) return;
                // what's saved is what's on screen now, whatever opens while the dialog is up
                LoadedPicture pic = shownFrom;
                string kind = AnimationKind;
                DeresSettings s = shownWith.Clone();
                string path;
                try
                {
                    path = FileDialogs.Save(this, apng ? "Save the animation as an APNG" : "Save the animation as a GIF", apng ? "Animated PNG" : "GIF animation", apng ? ".png" : ".gif", AnimationFileName(r, apng));
                }
                catch (COMException x)
                {
                    MessageBox.Show(this, "Windows' Save dialog didn't open:\n\n" + x.Message, Program.Name, MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                if (path != null) CouldntSave(path, await SaveAnimation(path, apng, r, pic, kind, s));
            }
            finally
            {
                askingForAnimation = false;
            }
        }

        // the website's names: photo-nes.gif for a GIF's own frames, photo-nes-screen-fx-loop.gif for a loop
        internal string AnimationFileName(Rendered r, bool apng)
        {
            string connection = SignalConnection(r);
            if (AnimationKind == Animation.GifFrames) return Export.FramesName(Export.BaseName(shownFrom?.Name ?? "photo"), r.Profile.Id, connection, apng);
            return Export.LoopName(BaseOf(r), r.Profile.Id, AnimationKind, connection, apng);
        }

        // The animation of the picture on screen as a GIF or APNG at `path`: at the Size chosen, or
        // the biggest that fits; frames made off the window's thread, its progress in the bottom
        // line, Stop leaving no file. What went wrong, or null (stopped too).
        internal Task<string> SaveAnimation(string path, bool apng) => SaveAnimation(path, apng, shown, shownFrom, AnimationKind, shownWith?.Clone());

        // the animation of picture `r` (made from `pic` with `s`), as `kind`
        async Task<string> SaveAnimation(string path, bool apng, Rendered r, LoadedPicture pic, string kind, DeresSettings s)
        {
            if (r == null || pic == null) return "there's no picture";
            if (animating != null) return "another animation is being made: wait for it, or Stop it";
            if (kind == Animation.GifFrames && pic.Animation == null) kind = settings.AnimationStyle;
            Geometry g = r.Geometry;
            AnimationFit fit = Animation.FitFor(g.Width, g.Height, g.ParX, g.ParY, settings.TvPixels, settings.Multiplier, kind, settings.SignalOn != null ? settings.LoopFrames : (int?)null, AnimationLimits.Pc);
            int m = fit.Multiplier ?? settings.Multiplier;
            bool tv = settings.TvPixels;
            FxSettings fx = settings.ExportScreen?.Clone();
            SignalSettings signal = settings.SignalOn?.Clone();
            int at = FrameOf(r);
            int loopFrames = settings.LoopFrames, loopFps = settings.LoopFps;
            string making = "Making " + Words.AnimationName(kind, signal != null) + " " + (apng ? "APNG" : "GIF") + "…";
            var cancel = new CancellationTokenSource();
            animating = cancel;
            Saving = true;
            Sync();
            Flash(making, "Dim");
            void Progress(int done, int total, string note) => Dispatcher.BeginInvoke(new Action(() =>
            {
                if (animating == cancel && !cancel.IsCancellationRequested)
                    Flash(making + " " + (note != null ? note + ", " : "") + "frame " + done + " of " + total + (done == total && note == null ? ", putting them together" : ""), "Dim");
            }));
            AnimationResult result = null;
            string problem = null;
            bool stopped = false;
            try
            {
                await Task.Run(() => Exports.Write(path, stream =>
                {
                    result = MakeAnimation(stream, pic, at, r, s, kind, apng, m, tv, fx, signal, loopFrames, loopFps, Progress, cancel.Token);
                    // Stop while the frames were put together: still no file
                    cancel.Token.ThrowIfCancellationRequested();
                }));
            }
            catch (OperationCanceledException)
            {
                stopped = true;
            }
            catch (AnimationException x)
            {
                problem = x.Message;
            }
            catch (Exception x) when (x is IOException || x is UnauthorizedAccessException || x is NotSupportedException || x is System.Security.SecurityException)
            {
                problem = x.Message;
            }
            catch (OutOfMemoryException)
            {
                problem = "there isn't enough memory free for an animation that big: pick a smaller Size";
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
            if (stopped) Flash("Stopped: " + Path.GetFileName(path) + " wasn't saved.", "Warn");
            else if (problem == null) Flash("Saved " + Path.GetFileName(path) + " (" + result.Width + "×" + result.Height + ", " + result.Frames + " frames).", "Good");
            return problem;
        }

        // An animation of a picture into `output`, on any thread (the window's saves and the batch):
        // an animated GIF's own frames (`kind` GifFrames: every frame through `s`, several at once,
        // each on its own engine), or a loop of `r`, its frame `at` (the dither shimmer makes it again
        // for each frame). `s`: the settings `r` was made with; `fx`: the screen effect when it goes
        // into exports; `signal`: the signal, when it's on.
        internal static AnimationResult MakeAnimation(Stream output, LoadedPicture pic, int at, Rendered r, DeresSettings s, string kind, bool apng, int multiplier, bool tv,
            FxSettings fx, SignalSettings signal, int loopFrames, int loopFps, Action<int, int, string> progress, CancellationToken cancel)
        {
            if (kind == Animation.GifFrames)
            {
                AnimatedGif gif = pic.Animation ?? throw new AnimationException("This picture has no frames to animate.");
                int lanes = Math.Max(1, Math.Min(4, Environment.ProcessorCount / 3));
                Deres[] engines = Enumerable.Range(0, lanes).Select(_ => new Deres()).ToArray();
                Animation.PaletteKind(r.Profile, s, out bool indexed, out bool shared);
                return Animation.FramesOf(output, new FramesOptions
                {
                    Apng = apng, Multiplier = multiplier, Tv = tv, Fx = fx, DelaysMs = gif.DelaysMs, LoopCount = gif.LoopCount, Lanes = lanes,
                    Render = (k, lane) =>
                    {
                        engines[lane].SetSource(gif.Frames[k]);
                        return engines[lane].Render(s);
                    },
                    Signal = signal != null ? (Func<Rendered, int, SignalImage>)((frameR, k) => Signal.Run(frameR, s.ProfileId, signal, k * gif.SignalStep)) : null,
                    Indexed = indexed, SharedPalette = shared, SeeThrough = s.Background == Backgrounds.Transparent && gif.HasAlpha,
                    Limits = AnimationLimits.Pc, Progress = progress, Cancel = cancel,
                });
            }
            PictureSource source = pic.SourceAt(at);
            var shimmer = new Deres();
            shimmer.SetSource(source);
            return Animation.Loop(output, r, new LoopOptions
            {
                Style = kind, Apng = apng, Multiplier = multiplier, Tv = tv, Fx = fx, Source = source, LoopFrames = loopFrames, LoopFps = loopFps,
                SeeThrough = s.Background == Backgrounds.Transparent && source.HasAlpha,
                Signal = signal != null ? (Func<Rendered, int, SignalImage>)((loopR, f) => Signal.Run(loopR, s.ProfileId, signal, f)) : null,
                Limits = AnimationLimits.Pc, Progress = progress, Cancel = cancel,
            }, s, sub => shimmer.Render(sub));
        }
    }
}
