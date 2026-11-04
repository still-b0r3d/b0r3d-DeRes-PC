using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using B0r3dDeRes.Engine;
using B0r3dDeRes.Video;

namespace B0r3dDeRes
{
    // An open video: the file, the frame that's the picture (paused there), and the stretch a save
    // takes (its start and stop).
    sealed class VideoClip : IDisposable
    {
        public readonly VideoFile File;
        public long Time;               // the paused frame's time (100 ns steps from the first frame)
        public PictureSource Source;    // the paused frame
        public long From, To;           // the stretch saved
        // a reader kept for going to a time and stepping, used one call at a time (none once it's closed)
        VideoReader scrub;
        readonly object scrubLock = new object();
        volatile bool closed;

        public VideoClip(VideoFile file, VideoFrame first)
        {
            File = file;
            Time = first.Time;
            Source = new PictureSource(first.Bgra, first.Width, first.Height);
            From = 0;
            To = file.Duration;
        }

        // the frame's number at the video's own speed (for names and the original's cache)
        public int FrameNumber(long time) => (int)Math.Round(time / (double)VideoFile.Second * File.Fps);

        // the reader for moving about, made when first needed (null once the clip's closed)
        VideoReader Scrub()
        {
            if (closed) return null;
            return scrub ?? (scrub = File.Frames());
        }

        // The frame showing at `time` (off the window's thread); null when it's no longer wanted by
        // the time its turn comes (a later move overtook it) or the clip's closed.
        public VideoFrame FrameAt(long time, Func<bool> stillWanted = null)
        {
            lock (scrubLock)
            {
                if (stillWanted?.Invoke() == false) return null;
                return Scrub()?.FrameAt(Math.Max(0, Math.Min(File.Duration - 1, time)));
            }
        }

        // the frame after the one at `time`, or null at the end
        public VideoFrame After(long time)
        {
            lock (scrubLock)
            {
                VideoReader r = Scrub();
                VideoFrame at = r?.FrameAt(time);
                if (at == null) return null;
                // FrameAt kept the frame after it: that's next
                return r.Next();
            }
        }

        // the frame before the one at `time`, or null at the start
        public VideoFrame Before(long time)
        {
            if (time <= 0) return null;
            lock (scrubLock)
            {
                VideoFrame f = Scrub()?.FrameAt(time - VideoReader.Grace - 1);
                return f != null && f.Time < time ? f : null;
            }
        }

        // closed: a move still under way finishes first, then its reader goes (off the window's thread)
        public void Dispose()
        {
            closed = true;
            Task.Run(() =>
            {
                lock (scrubLock)
                {
                    scrub?.Dispose();
                    scrub = null;
                }
            });
        }
    }

    // A frame made while a video plays, and where it falls.
    sealed class VideoPlayed
    {
        public long Time;
        public PictureSource Source;
        public PlayedFrame View;
    }

    // The video's timeline under the picture: Play / Pause (with sound), a frame back and on, the
    // time slider and the time, and the start and stop of what's saved. Playing, the frames are
    // read and made through the look on threads of their own, a few ahead, and each is shown when
    // the sound reaches it (frames that would come late are left out); paused, the frame on screen
    // is the picture, made in full by the window.
    sealed class VideoPlayer
    {
        readonly Button playButton, backButton, onButton, startButton, stopButton, wholeButton;
        readonly Slider slider;
        readonly TextBlock timeText, rangeText;
        readonly CheckBox soundBox;
        readonly FrameworkElement bar;
        readonly Func<Func<VideoFrame, long, PlayedFrame>> makeFor;   // what makes a frame through the look at the playing size (any thread), with the settings as they are
        readonly Action<VideoPlayed> show;
        readonly Action started;
        readonly Action<long, PictureSource> paused;        // stopped on this frame (its time and picture), or moved to it
        readonly Action<string> problem;
        readonly Action rangeChanged;

        VideoClip clip;
        bool quiet;
        bool dragging;
        bool resume;        // it was playing when a move began (or Play came meanwhile): it plays on from where the move lands
        Run run;
        int moving;         // the latest move (slider, step): an older one finishing late is let go
        int landed;         // the latest move that's landed (or come to nothing)
        Window window;
        // playing on after a move a moment later, so a key held on the slider doesn't start and
        // stop the sound at every step; and a play made again for new settings at most this often
        readonly DispatcherTimer resumeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        readonly DispatcherTimer restartTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };

        public VideoPlayer(Button play, Button back, Button on, Slider slider, TextBlock timeText, Button start, Button stop, Button whole, TextBlock rangeText, CheckBox soundBox,
            FrameworkElement bar, Func<Func<VideoFrame, long, PlayedFrame>> makeFor, Action<VideoPlayed> show, Action started, Action<long, PictureSource> paused, Action<string> problem, Action rangeChanged)
        {
            playButton = play;
            backButton = back;
            onButton = on;
            this.slider = slider;
            this.timeText = timeText;
            startButton = start;
            stopButton = stop;
            wholeButton = whole;
            this.rangeText = rangeText;
            this.soundBox = soundBox;
            this.bar = bar;
            this.makeFor = makeFor;
            this.show = show;
            this.started = started;
            this.paused = paused;
            this.problem = problem;
            this.rangeChanged = rangeChanged;
            play.Click += (s, e) => Toggle();
            back.Click += (s, e) => Step(-1);
            on.Click += (s, e) => Step(1);
            start.Click += (s, e) => SetStart();
            stop.Click += (s, e) => SetStop();
            whole.Click += (s, e) => Whole();
            slider.ValueChanged += (s, e) =>
            {
                if (!quiet) MoveTo((long)Math.Round(slider.Value * VideoFile.Second));
            };
            slider.PreviewMouseLeftButtonDown += (s, e) => Grab();
            slider.PreviewMouseLeftButtonUp += (s, e) => LetGo();
            slider.LostMouseCapture += (s, e) => LetGo();
            resumeTimer.Tick += (s, e) => Resume();
            restartTimer.Tick += (s, e) =>
            {
                restartTimer.Stop();
                if (!Playing) return;
                Halt();
                Play();
            };
            ShowButton();
            bar.Visibility = Visibility.Collapsed;
        }

        public VideoClip Clip => clip;
        public bool Playing => run != null;
        public bool Holding => dragging;
        public long Time => clip?.Time ?? 0;

        // the speakers (for the tests: a pretend pair)
        internal static Func<SoundOut> OpenSound = SoundOut.Open;

        // (for the tests) the time of the frame on screen while it plays
        internal long? PlayingAt => run?.Shown?.Time;

        // a new video (null: none, the bar hides)
        public void SetVideo(VideoClip next)
        {
            HaltForGood();
            // (a move still under way for the last one comes to nothing)
            landed = moving;
            clip = next;
            bar.Visibility = next != null ? Visibility.Visible : Visibility.Collapsed;
            if (next == null) return;
            Update();
        }

        public void Toggle()
        {
            if (Playing || resume) Pause();
            else Play();
        }

        // ---- playing

        // What one play is doing: its threads, its sound, its frames waiting to be shown. Its clock
        // is the sound's samples played while there's sound to play; once the sound's all played
        // (it ended before the picture, or couldn't go on) or stops moving, a stopwatch carries on
        // from where it got to, so the picture always reaches the end.
        sealed class Run
        {
            public readonly CancellationTokenSource Stop = new CancellationTokenSource();
            public SoundOut Sound;                  // (let go, under the run's lock, when it stops)
            public readonly long Start;             // where it started
            public readonly Stopwatch Clock = new Stopwatch();
            public readonly Queue<VideoPlayed> Ready = new Queue<VideoPlayed>();
            public volatile bool Ended;             // the frames are all read
            public volatile bool SoundStarted, SoundDone;   // the sound's begun going out; it's all gone out (or couldn't)
            public VideoPlayed Shown;
            public string Problem;
            public DispatcherTimer Timer;
            public long LastNow;                    // the clock at the last tick, and how long it's stood there
            public readonly Stopwatch Still = Stopwatch.StartNew();
            public bool Watching;                   // that's watched once the sound's begun (not while it's still on its way)
            bool detached;
            long clockFrom;

            public Run(long start)
            {
                Start = start;
                clockFrom = start;
            }

            // where playing has got to
            public long Now
            {
                get
                {
                    lock (this)
                        return Sound != null && !detached ? Start + Sound.Played * VideoFile.Second / VideoReader.SampleRate
                            : clockFrom + Clock.ElapsedTicks * VideoFile.Second / Stopwatch.Frequency;
                }
            }

            // the sound's gone out and all of it has played
            public bool SoundRanOut
            {
                get
                {
                    lock (this) return SoundDone && (Sound == null || Sound.Ahead == 0);
                }
            }

            // the stopwatch takes over from where the sound got to
            public void Detach()
            {
                lock (this)
                {
                    if (detached || Sound == null) return;
                    clockFrom = Start + Sound.Played * VideoFile.Second / VideoReader.SampleRate;
                    detached = true;
                    Clock.Restart();
                }
            }

            // a stretch of sound out to the speakers (false: the play's stopped)
            public bool Queue(short[] samples, int offset, int count)
            {
                lock (this)
                {
                    if (Sound == null || Stop.IsCancellationRequested) return false;
                    Sound.Queue(samples, offset, count);
                    SoundStarted = true;
                    return true;
                }
            }

            // waits while a quarter of a second is queued ahead (false: the play's stopped)
            public bool Room()
            {
                while (true)
                {
                    long ahead;
                    lock (this)
                    {
                        if (Sound == null || Stop.IsCancellationRequested) return false;
                        ahead = Sound.Ahead;
                    }
                    if (ahead <= VideoReader.SampleRate / 4) return true;
                    Thread.Sleep(10);
                }
            }
        }

        public void Play()
        {
            if (Playing || clip == null) return;
            // a move under way: it plays from where that lands
            if (landed != moving)
            {
                resume = true;
                ShowButton();
                return;
            }
            resume = false;
            resumeTimer.Stop();
            VideoClip c = clip;
            long start = c.Time;
            // at the end of the stretch, it starts over from its start
            if (start + c.File.FrameLength >= c.To) start = c.From;
            var r = new Run(start);
            run = r;
            ShowButton();
            started();
            if (soundBox.IsChecked == true && c.File.HasSound) r.Sound = OpenSound();
            Func<VideoFrame, long, PlayedFrame> make = makeFor();
            // (threads of their own for as long as it plays, leaving the shared ones to the frames' making)
            Task.Factory.StartNew(() => Frames(r, c, make), TaskCreationOptions.LongRunning);
            if (r.Sound != null) Task.Factory.StartNew(() => Sound(r, c), TaskCreationOptions.LongRunning);
            else r.Clock.Start();
            r.Timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(8) };
            r.Timer.Tick += (s, e) => Tick(r);
            r.Timer.Start();
        }

        // Reads the frames from where it starts and makes each through the look, a few ahead. One
        // already past its time is left out, unless nothing's gone up for a quarter of a second (a
        // video that reads slower than it plays still moves); one more than a second behind jumps
        // the reading on to where playing has got to.
        void Frames(Run r, VideoClip c, Func<VideoFrame, long, PlayedFrame> make)
        {
            try
            {
                using (VideoReader reader = c.File.Frames())
                {
                    reader.Seek(r.Start);
                    long frameLength = c.File.FrameLength, from = r.Start;
                    var sinceMade = Stopwatch.StartNew();
                    bool jumped = false;
                    while (!r.Stop.IsCancellationRequested)
                    {
                        // (the frames over before where it starts or jumped to are passed over unmade)
                        VideoFrame f = reader.Next(from);
                        if (f == null || f.Time >= c.To) break;
                        if (!jumped && r.Shown != null && f.Time + frameLength < r.Now - VideoFile.Second)
                        {
                            from = r.Now + VideoFile.Second / 2;
                            if (from >= c.To) break;
                            reader.Seek(from);
                            jumped = true;
                            continue;
                        }
                        // room for it: at most three waiting
                        while (!r.Stop.IsCancellationRequested)
                        {
                            lock (r.Ready)
                                if (r.Ready.Count < 3) break;
                            Thread.Sleep(4);
                        }
                        if (r.Stop.IsCancellationRequested) break;
                        bool late = f.Time + frameLength < r.Now && r.Shown != null && sinceMade.ElapsedMilliseconds < 250 && !jumped;
                        if (late) continue;
                        jumped = false;
                        sinceMade.Restart();
                        PlayedFrame view = make(f, f.Time);
                        lock (r.Ready) r.Ready.Enqueue(new VideoPlayed { Time = f.Time, Source = new PictureSource(f.Bgra, f.Width, f.Height), View = view });
                    }
                }
            }
            catch (Exception x)
            {
                r.Problem = x is OutOfMemoryException ? "there isn't enough memory free to play it at this size" : x.Message;
            }
            finally
            {
                r.Ended = true;
            }
        }

        // Feeds the sound to the speakers a quarter of a second ahead, each stretch where its own
        // time puts it (silence for a gap, or before a sound that starts late), cut to the stretch played.
        void Sound(Run r, VideoClip c)
        {
            try
            {
                using (VideoReader reader = c.File.Sound())
                {
                    // (from the start it's read as it comes: see VideoExport.Mp4)
                    if (r.Start > 0) reader.SeekSound(r.Start);
                    // in samples from where it starts: the stop, and how far it's queued
                    long end = (c.To - r.Start) * VideoReader.SampleRate / VideoFile.Second, queued = 0;
                    while (queued < end && r.Room())
                    {
                        short[] samples = reader.NextSound(out long time);
                        if (samples == null) break;
                        long at = (long)Math.Round((time - r.Start) / (double)VideoFile.Second * VideoReader.SampleRate);
                        if (Math.Abs(at - queued) <= VideoExport.SoundSlack) at = queued;
                        long count = samples.Length / 2;
                        while (at > queued)
                        {
                            // silence up to it, a tenth of a second at a time
                            long gap = Math.Min(Math.Min(at, end) - queued, VideoReader.SampleRate / 10);
                            if (gap <= 0 || !r.Room() || !r.Queue(new short[gap * 2], 0, (int)gap * 2)) break;
                            queued += gap;
                        }
                        if (queued >= end || at > queued) continue;
                        // what's before the start, or queued already, is left out, and what's past the stop
                        long skip = queued - at, take = Math.Min(count - skip, end - queued);
                        if (take <= 0) continue;
                        if (!r.Queue(samples, (int)skip * 2, (int)take * 2)) break;
                        queued += take;
                    }
                }
            }
            catch (Exception)
            {
                // the picture plays on without the rest of its sound
            }
            finally
            {
                r.SoundDone = true;
            }
        }

        // shows the frame that's due, and ends at the end
        void Tick(Run r)
        {
            if (run != r) return;
            // the sound's all played, or has stopped moving for a while: the stopwatch carries on
            if (r.SoundRanOut) r.Detach();
            long now = r.Now;
            if (now != r.LastNow || !r.Watching && r.SoundStarted)
            {
                r.Watching |= r.SoundStarted;
                r.LastNow = now;
                r.Still.Restart();
            }
            else if (r.Watching && r.Still.ElapsedMilliseconds > 1500) r.Detach();
            VideoPlayed due = null;
            lock (r.Ready)
                while (r.Ready.Count > 0 && r.Ready.Peek().Time <= now + VideoReader.Grace)
                {
                    due = r.Ready.Dequeue();
                }
            if (due != null)
            {
                r.Shown = due;
                show(due);
                Update(due.Time);
            }
            // (ended first: a last frame put in meanwhile is still shown)
            bool ended = r.Ended, empty;
            lock (r.Ready) empty = r.Ready.Count == 0;
            if (ended && empty)
            {
                if (r.Problem != null) problem("Couldn't play it: " + r.Problem + ".");
                Pause();
            }
        }

        void Stop(Run r)
        {
            r.Timer?.Stop();
            r.Stop.Cancel();
            lock (r)
            {
                r.Sound?.Dispose();
                r.Sound = null;
            }
        }

        public void Pause()
        {
            // (a play waiting for a move to land doesn't come)
            resume = false;
            resumeTimer.Stop();
            Run r = run;
            if (r == null)
            {
                ShowButton();
                return;
            }
            Stop(r);
            run = null;
            ShowButton();
            if (r.Shown != null)
            {
                clip.Time = r.Shown.Time;
                clip.Source = r.Shown.Source;
            }
            Update();
            paused(clip.Time, clip.Source);
        }

        // stops quietly (another picture is on its way), keeping the frame on screen as the picture
        public void Halt()
        {
            Run r = run;
            if (r == null) return;
            Stop(r);
            run = null;
            if (r.Shown != null && clip != null)
            {
                clip.Time = r.Shown.Time;
                clip.Source = r.Shown.Source;
            }
            ShowButton();
            Update();
        }

        // stops where it is for good (another picture is opening, or the window's closing): no move
        // under way plays it on
        public void HaltForGood()
        {
            resume = false;
            resumeTimer.Stop();
            restartTimer.Stop();
            Halt();
        }

        // the settings changed while it plays: the frames are made again from where it is (once a
        // slider's been still a moment)
        public void Invalidate()
        {
            if (!Playing) return;
            restartTimer.Stop();
            restartTimer.Start();
        }

        // ---- moving about

        // To the frame at `time` (the slider), off the window's thread; the latest move wins, and
        // one overtaken before its turn comes isn't made at all. Playing, it stops for the move and
        // plays on from where it lands (once the slider's let go of).
        async void MoveTo(long time)
        {
            if (clip == null) return;
            VideoClip c = clip;
            int ticket = ++moving;
            if (Playing)
            {
                resume = true;
                Halt();
            }
            Update(time);
            VideoFrame f;
            try
            {
                f = await Task.Run(() => c.FrameAt(time, () => Volatile.Read(ref moving) == ticket));
            }
            catch (VideoException x)
            {
                if (ticket != moving) return;
                landed = ticket;
                problem("Couldn't go there: " + x.Message);
                ResumeSoon();
                return;
            }
            if (ticket != moving || c != clip) return;
            landed = ticket;
            if (f != null)
            {
                c.Time = f.Time;
                c.Source = new PictureSource(f.Bgra, f.Width, f.Height);
                if (!dragging) Update();
                paused(c.Time, c.Source);
            }
            ResumeSoon();
        }

        // plays on after a move, if it was playing when the move began (or Play came meanwhile)
        void Resume()
        {
            resumeTimer.Stop();
            if (!resume || dragging || clip == null || landed != moving) return;
            resume = false;
            Play();
        }

        // the same a moment from now, unless another move comes first
        void ResumeSoon()
        {
            resumeTimer.Stop();
            if (resume && !dragging) resumeTimer.Start();
        }

        // a frame back (-1) or on (1)
        public async void Step(int direction)
        {
            if (clip == null) return;
            if (Playing) Pause();
            resume = false;
            VideoClip c = clip;
            int ticket = ++moving;
            long from = c.Time;
            VideoFrame f;
            try
            {
                f = await Task.Run(() => direction < 0 ? c.Before(from) : c.After(from));
            }
            catch (VideoException x)
            {
                if (ticket != moving) return;
                landed = ticket;
                problem("Couldn't step: " + x.Message);
                return;
            }
            if (ticket != moving || c != clip) return;
            landed = ticket;
            if (f == null) return;
            c.Time = f.Time;
            c.Source = new PictureSource(f.Bgra, f.Width, f.Height);
            Update();
            paused(c.Time, c.Source);
        }

        internal void Grab()
        {
            if (clip == null || dragging) return;
            dragging = true;
            if (Playing)
            {
                resume = true;
                Halt();
            }
            window = Window.GetWindow(slider);
            if (window != null) window.PreviewMouseLeftButtonUp += OnLetGo;
        }

        void OnLetGo(object sender, MouseButtonEventArgs e) => LetGo();

        // The slider let go of: once the move under way lands (if one is), it's the picture, and a
        // play that the drag stopped goes on from there. (Its screen effect waited for the slider
        // to be let go of, so the picture's made again.)
        internal void LetGo()
        {
            if (window != null) window.PreviewMouseLeftButtonUp -= OnLetGo;
            window = null;
            if (!dragging) return;
            dragging = false;
            if (landed != moving) return;
            Update();
            if (resume) Resume();
            else paused(clip.Time, clip.Source);
        }

        // ---- the stretch saved

        // the frame on screen: the one playing, or the paused one
        long OnScreen => run?.Shown?.Time ?? clip.Time;

        void SetStart()
        {
            if (clip == null) return;
            clip.From = OnScreen;
            if (clip.To <= clip.From) clip.To = clip.File.Duration;
            Update(Playing ? OnScreen : (long?)null);
            rangeChanged();
        }

        void SetStop()
        {
            if (clip == null) return;
            // to the end of the frame on screen
            clip.To = Math.Min(clip.File.Duration, OnScreen + clip.File.FrameLength);
            if (clip.From >= clip.To) clip.From = 0;
            Update(Playing ? OnScreen : (long?)null);
            rangeChanged();
        }

        void Whole()
        {
            if (clip == null) return;
            clip.From = 0;
            clip.To = clip.File.Duration;
            Update();
            rangeChanged();
        }

        // ---- the bar

        // (playing on after a move counts as playing)
        void ShowButton()
        {
            bool playing = Playing || resume;
            playButton.Content = playing ? "❚❚ Pause" : "▶ Play";
            playButton.ToolTip = playing ? "Stop on the frame shown: it's the picture then" : "Play the video through the look, with its sound";
            AutomationProperties.SetName(playButton, playing ? "Pause" : "Play");
        }

        // "0:12.40", or "1:02:03.04" past an hour (in whole hundredths first, so 59.996 s reads 1:00.00)
        public static string Clock(long time)
        {
            long hundredths = (long)Math.Round(Math.Max(0, time) / (double)(VideoFile.Second / 100));
            long hours = hundredths / 360000, minutes = hundredths / 6000 % 60;
            string s = (hundredths / 100 % 60).ToString("00", CultureInfo.InvariantCulture) + "." + (hundredths % 100).ToString("00", CultureInfo.InvariantCulture);
            return hours > 0 ? hours + ":" + minutes.ToString("00", CultureInfo.InvariantCulture) + ":" + s : minutes + ":" + s;
        }

        // the slider and the lines, at `time` (or the paused frame's)
        void Update(long? time = null)
        {
            if (clip == null) return;
            long t = time ?? clip.Time, length = clip.File.Duration;
            string text = Clock(t) + " / " + Clock(length);
            if (timeText.Text != text) timeText.Text = text;
            timeText.MinWidth = (Clock(length).Length * 2 + 3) * 7.5;
            quiet = true;
            try
            {
                slider.Maximum = Math.Max(0.001, (length - 1) / (double)VideoFile.Second);
                slider.SmallChange = 1 / Math.Max(1, clip.File.Fps);
                slider.LargeChange = 1;
                if (!dragging) slider.Value = t / (double)VideoFile.Second;
            }
            finally
            {
                quiet = false;
            }
            bool whole = clip.From <= 0 && clip.To >= length;
            rangeText.Text = whole ? "Saves all " + Seconds(length) : "Saves " + Clock(clip.From) + " to " + Clock(clip.To) + " (" + Seconds(clip.To - clip.From) + ")";
            wholeButton.IsEnabled = !whole;
        }

        static string Seconds(long time) => (time / (double)VideoFile.Second).ToString(time < 100 * VideoFile.Second ? "0.0" : "0", CultureInfo.InvariantCulture) + " s";
    }
}
