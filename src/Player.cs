using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using B0r3dDeRes.Engine;

namespace B0r3dDeRes
{
    // A frame of an animated GIF as the preview shows it while it plays: the picture through the
    // settings, and its screen effect or signal at the playing size (null: none).
    sealed class PlayedFrame
    {
        public Rendered Picture;
        public byte[] Screen;
        public int ScreenWidth, ScreenHeight;
        // the ASCII studio's frames: the art (no picture)
        public AsciiArt Art;

        // what keeping it costs, against the budget: the pixels it holds (the picture's own too);
        // a cell of art costs as much as 3.5 pixels (14 bytes against 4, as on the website)
        public long Size => (Screen != null ? (long)ScreenWidth * ScreenHeight : 0) + (Picture != null ? (long)Picture.Width * Picture.Height : 0) + (Art != null ? (long)Art.Cells * 14 / 4 : 0);
    }

    // Playing an animated GIF in the preview (the website's ui/player.ts). Each frame goes through
    // the current settings off the window's thread, a little ahead of the one on screen; the frames
    // made are kept for as long as the settings stay the same, so after the first pass it loops at
    // the GIF's own speed. Any change drops them. The kept frames stay inside a budget (the preview
    // plays smaller if need be, see PlayScale); past it, the rest are made again on each pass. The
    // slider goes to any frame: while it's held, it shows the frames already made (making the one
    // under it if need be), and dragging it while playing pauses until it's let go. Paused, the
    // frame on screen is the one the exports use.
    sealed class Player
    {
        readonly Button button;
        readonly Slider slider;
        readonly TextBlock counter;
        readonly FrameworkElement bar;
        readonly Func<int, Task<PlayedFrame>> make;   // frame k through the current settings
        readonly Action<PlayedFrame, int> show;        // puts frame k on the stage
        readonly Action started;
        readonly Action<int> paused;                   // playing stopped on this frame, or the slider moved to it (or was let go on it) while paused
        readonly Action<string> problem;
        readonly Func<object> picture;                 // changes with every new picture: frames made for an older one are dropped
        readonly long budget;
        readonly DispatcherTimer timer;
        readonly Stopwatch clock = Stopwatch.StartNew();

        int[] delays = new int[0];
        int current;
        bool playing, dragging, resumeAfterDrag, quiet;
        PlayedFrame[] cache = new PlayedFrame[0];
        long cachedSize;
        // a frame made when the cache was full, waiting to be shown
        int spareFrame = -1;
        PlayedFrame spareView;
        // a frame didn't fit: from then on, only the frame due next is made
        bool full;
        // bumped by every change, so frames made for older settings are dropped
        int epoch;
        bool inFlight;
        double shownAt;
        Window window;

        public Player(Button button, Slider slider, TextBlock counter, FrameworkElement bar, long budget, Func<int, Task<PlayedFrame>> make, Action<PlayedFrame, int> show,
            Action started, Action<int> paused, Action<string> problem, Func<object> picture)
        {
            this.button = button;
            this.slider = slider;
            this.counter = counter;
            this.bar = bar;
            this.budget = budget;
            this.make = make;
            this.show = show;
            this.started = started;
            this.paused = paused;
            this.problem = problem;
            this.picture = picture;
            timer = new DispatcherTimer(DispatcherPriority.Normal);
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                Pump();
            };
            button.Click += (s, e) => Toggle();
            slider.ValueChanged += (s, e) =>
            {
                if (!quiet) Seek(slider.Value);
            };
            slider.PreviewMouseLeftButtonDown += (s, e) => Grab();
            slider.PreviewMouseLeftButtonUp += (s, e) => LetGo();
            // the slider's thumb losing the mouse (Alt+Tab mid-drag) lets go too
            slider.LostMouseCapture += (s, e) => LetGo();
            ShowButton();
            bar.Visibility = Visibility.Collapsed;
        }

        // the frame on screen (the one exports use)
        public int Frame => current;
        public bool Playing => playing;
        // the slider is held down (the frames shown are the ones made at the playing size)
        public bool Holding => dragging;
        public bool Animated => delays.Length > 1;
        internal int Kept => Array.FindAll(cache, v => v != null).Length;

        double Now => clock.Elapsed.TotalMilliseconds;

        // frame k as made with the current settings, if it's kept (or waiting to be shown)
        PlayedFrame KeptFrame(int k) => (k >= 0 && k < cache.Length ? cache[k] : null) ?? (spareFrame == k ? spareView : null);

        // A new picture: the delays of its frames, or null for a still.
        public void SetAnimation(int[] delaysMs)
        {
            Stop();
            Unhook();
            dragging = false;
            resumeAfterDrag = false;
            delays = delaysMs != null && delaysMs.Length > 1 ? (int[])delaysMs.Clone() : new int[0];
            current = 0;
            Drop();
            bar.Visibility = delays.Length > 1 ? Visibility.Visible : Visibility.Collapsed;
            // room for the longest count ("58 / 58"), so the slider never changes width
            counter.MinWidth = (delays.Length.ToString().Length * 2 + 3) * 7.5;
            Update();
        }

        // The settings (or the preview's size) changed: the frames made so far are no good.
        public void Invalidate()
        {
            if (delays.Length < 2) return;
            Drop();
            Update();
            if (playing) Pump();
            else if (dragging) Ask();
        }

        public void Toggle()
        {
            if (playing) Pause();
            else Play();
        }

        public void Play()
        {
            if (playing || delays.Length < 2) return;
            playing = true;
            ShowButton();
            started();
            shownAt = Now;
            Pump();
        }

        public void Pause()
        {
            if (!playing) return;
            Stop();
            Update();
            paused(current);
        }

        // Stops quietly (a new picture is on its way), keeping the frame on screen.
        public void Halt()
        {
            Stop();
            Update();
        }

        // Goes to frame k, shown straight away if it has been made. Playing, it plays on from there;
        // held, the frame under the slider is made at the playing size if it hasn't been (a
        // full-size one for every step would come too late, after the slider moved on); paused,
        // it's the frame the exports use.
        public void Seek(double k)
        {
            int n = delays.Length;
            if (n < 2) return;
            current = (int)Math.Max(0, Math.Min(n - 1, JsMath.Round(k)));
            PlayedFrame view = KeptFrame(current);
            if (view != null) show(view, current);
            Update(view == null && dragging);
            if (playing)
            {
                shownAt = Now;
                Pump();
            }
            else if (dragging) Ask();
            else paused(current);
        }

        // Goes to frame k quietly while paused: nothing is shown or made (the window makes it itself).
        public void SetFrame(int k)
        {
            int n = delays.Length;
            if (n < 2 || dragging || playing) return;
            current = Math.Max(0, Math.Min(n - 1, k));
            Update();
        }

        // The slider is pressed: playing pauses while it's held, and goes on when it's let go
        // (anywhere: even off the slider, or without moving it).
        internal void Grab()
        {
            if (delays.Length < 2 || dragging) return;
            dragging = true;
            if (playing)
            {
                resumeAfterDrag = true;
                Stop();
                Update();
            }
            Unhook();
            window = Window.GetWindow(slider);
            if (window != null) window.PreviewMouseLeftButtonUp += OnLetGo;
        }

        void OnLetGo(object sender, MouseButtonEventArgs e) => LetGo();

        void Unhook()
        {
            if (window != null) window.PreviewMouseLeftButtonUp -= OnLetGo;
            window = null;
        }

        // The slider was let go: playing goes on, or the frame it's on is the one paused on.
        internal void LetGo()
        {
            Unhook();
            if (!dragging) return;
            dragging = false;
            if (resumeAfterDrag)
            {
                resumeAfterDrag = false;
                Play();
                return;
            }
            Update();
            paused(current);
        }

        void Stop()
        {
            playing = false;
            timer.Stop();
            ShowButton();
        }

        void ShowButton()
        {
            button.Content = playing ? "❚❚ _Pause" : "▶ _Play";
            button.ToolTip = playing ? "Stop on the frame shown: it's the picture then" : "Play the GIF through the look, at its own speed";
            AutomationProperties.SetName(button, playing ? "Pause" : "Play");
        }

        void Drop()
        {
            epoch++;
            cache = new PlayedFrame[delays.Length];
            cachedSize = 0;
            spareFrame = -1;
            spareView = null;
            full = false;
        }

        // Shows the next frame when it's due and made; asks for frames ahead.
        void Pump()
        {
            timer.Stop();
            if (!playing) return;
            int n = delays.Length, next = (current + 1) % n;
            PlayedFrame view = KeptFrame(next);
            double due = shownAt + delays[current], now = Now;
            if (view != null && now >= due)
            {
                current = next;
                if (spareView == view)
                {
                    spareFrame = -1;
                    spareView = null;
                }
                show(view, next);
                // keep to the GIF's timing, unless the frame came late
                shownAt = now - due < delays[next] ? due : now;
                Update();
                Ask();
                Schedule(shownAt + delays[next] - Now);
                return;
            }
            Update(view == null);
            Ask();
            if (view != null) Schedule(due - now);
            // otherwise the frame arriving calls Pump()
        }

        void Schedule(double ms)
        {
            timer.Stop();
            timer.Interval = TimeSpan.FromMilliseconds(Math.Max(0, ms));
            timer.Start();
        }

        // the frame to make next, or -1: playing, the next one ahead not yet made; held, the one under the slider
        int Wanted()
        {
            if (!playing) return dragging && KeptFrame(current) == null ? current : -1;
            int n = delays.Length;
            for (int i = 1; i <= n; i++)
            {
                int k = (current + i) % n;
                if (KeptFrame(k) != null) continue;
                // with no room left, only the frame that's due next is worth making
                if (full && i > 1) return -1;
                return k;
            }
            return -1;
        }

        // asks for the frame wanted next, if nothing's on its way
        async void Ask()
        {
            if (inFlight) return;
            int frame = Wanted();
            if (frame < 0) return;
            int asked = epoch;
            object pic = picture();
            inFlight = true;
            PlayedFrame view;
            try
            {
                view = await make(frame);
            }
            catch (Exception x)
            {
                inFlight = false;
                if (asked != epoch || pic != picture() || !playing) return;
                Pause();
                problem("Something went wrong: " + (x is OutOfMemoryException ? "there isn't enough memory free to play it at this size" : x.Message));
                return;
            }
            inFlight = false;
            bool fresh = asked == epoch && pic == picture();
            if (fresh) Keep(frame, view);
            if (playing)
            {
                Pump();
                return;
            }
            if (fresh && dragging && frame == current)
            {
                show(view, frame);
                Update();
            }
            Ask();
        }

        // keeps a frame while there's room in the budget, else holds it until it's shown
        void Keep(int frame, PlayedFrame view)
        {
            long size = view.Size;
            if (cachedSize + size <= budget)
            {
                cache[frame] = view;
                cachedSize += size;
            }
            else
            {
                full = true;
                spareFrame = frame;
                spareView = view;
            }
        }

        // "12 / 58", dimmed while the next frame is still being made; the slider follows
        void Update(bool waiting = false)
        {
            int n = delays.Length;
            string text = (current + 1) + " / " + n;
            if (counter.Text != text) counter.Text = text;
            counter.SetResourceReference(TextBlock.ForegroundProperty, waiting ? "Dim" : "Text");
            quiet = true;
            try
            {
                slider.Maximum = Math.Max(1, n - 1);
                if (slider.Value != current) slider.Value = current;
            }
            finally
            {
                quiet = false;
            }
        }

        // The preview's enlargement while a GIF plays: `scale` if every frame fits the budget at that
        // size, else the biggest that does, so the frames are kept and it loops smoothly. Whole
        // steps down to 2× (even scanlines), then tenths down to 1× (the picture's own size; the
        // scanlines fade out on the way). `size` gives a frame's size at an enlargement.
        public static double PlayScale(double scale, int frames, Func<double, (int W, int H)> size, long budget)
        {
            bool Fits(double s)
            {
                (int w, int h) = size(s);
                return (long)w * h * frames <= budget;
            }
            double at = scale;
            while (at > 2 && !Fits(at)) at--;
            if (Fits(at)) return at;
            for (int t = (int)JsMath.Round(at * 10) - 1; t > 10; t--) if (Fits(t / 10.0)) return t / 10.0;
            return 1;
        }
    }
}
