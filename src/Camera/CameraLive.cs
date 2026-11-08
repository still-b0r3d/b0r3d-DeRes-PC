using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Windows.Threading;
using B0r3dDeRes.Engine;

namespace B0r3dDeRes.Camera
{
    // A camera on a thread of its own (b0r3d QR's camera loop): its pictures are kept for the window
    // as they come, newest only, and the window takes the newest whenever it's ready to make the next
    // live frame, so a slow look never builds up a queue. A clip's shots are taken here, ten a second,
    // on time. Stop (or another camera chosen) lets the camera go at once, even while it waits for a
    // picture, which is cut short from the thread pool; the camera is let go of on this same thread
    // whatever happens. Pictures live only in memory.
    sealed class CameraLive
    {
        internal static int EarlierSessionsMs = 10000;   // the longest a session waits for an earlier one's camera (the tests shorten it)

        readonly ICameras cameras;
        readonly Dispatcher ui;
        readonly AutoResetEvent wake = new AutoResetEvent(false);
        readonly object gate = new object();     // Stop and Choose against the camera being read
        Thread thread;
        volatile bool stopping;
        volatile int wanted;                     // the camera to read (its place in the list)
        IFrameSource current;                    // the camera being read, to cut short from another thread (under `gate`)
        int currentAt;                           // its place in the list
        IFrameSource stoppedOnPurpose;           // the last one cut short by Stop or Choose, whose trouble is no failure

        // The cameras of earlier sessions not let go of yet: one still starting when it was stopped
        // (Windows can't cut that short) is let go of as soon as it has started. A session started
        // again straight away waits for them, or Windows would say the camera is in use by another
        // application when it's b0r3d DeRes's own, just stopped.
        static readonly List<ManualResetEvent> notLetGo = new List<ManualResetEvent>();
        readonly ManualResetEvent letGo = new ManualResetEvent(false);
        ManualResetEvent[] earlier = new ManualResetEvent[0];

        // what the window hears, on its own thread
        public Action<List<CameraInfo>> Listed;   // the cameras Windows lists
        public Action<CameraInfo> Starting;       // a camera is being started
        public Action<CameraInfo, int, int> Started;   // its pictures are coming, this big
        public Action NewPicture;                 // a newer picture is there to take (once, until it's taken)
        public Action<PictureSource[]> Clip;      // a clip's frames, all of them; the camera goes on
        public Action<CameraException> Failed;    // why the camera can't be read
        public Action Ended;                      // the thread is done and the camera let go of

        // the newest picture (`latest`), and how many there have been; the window takes it in turn for one of its own
        Frame latest = new Frame();
        long pictures, taken;
        int newPosted;    // 1 while NewPicture is on its way to the window

        // a clip being shot: its frames' size, whether they're mirrored, the frames so far and when the next is due
        int clipW, clipH;
        bool clipMirror;
        List<PictureSource> clipFrames;
        long clipNext;
        readonly Stopwatch clock = Stopwatch.StartNew();   // Windows setting its time doesn't move it

        // the camera being read: its size
        public int Width { get; private set; }
        public int Height { get; private set; }

        // the camera to start with, by its name (the one used last), when Windows still lists it;
        // else the first
        public string Prefer;

        public CameraLive(ICameras cameras, Dispatcher ui)
        {
            this.cameras = cameras;
            this.ui = ui;
        }

        public bool Running => thread != null && thread.IsAlive;
        public bool Shooting
        {
            get
            {
                lock (gate) return clipFrames != null;
            }
        }

        // the camera comes on: the one Prefer names, else the first Windows lists
        public void Start()
        {
            if (thread != null) return;
            lock (notLetGo)
            {
                earlier = notLetGo.ToArray();
                notLetGo.Add(letGo);
            }
            thread = new Thread(Run) { IsBackground = true, Name = "b0r3d DeRes camera" };
            thread.SetApartmentState(ApartmentState.MTA);   // Media Foundation's objects work from any thread of the MTA
            thread.Start();
        }

        // another camera from the list: the one being read is let go of first, at once (a picture it's
        // waiting for isn't waited for)
        public void Choose(int index)
        {
            IFrameSource other;
            lock (gate)
            {
                wanted = index;
                clipFrames = null;   // a clip doesn't go on with another camera
                other = currentAt != index ? OnPurpose(current) : null;
            }
            wake.Set();
            CutShort(other);
        }

        // the camera stops at once, even while it waits for a picture (one still starting is let go
        // of as soon as it has started)
        public void Stop()
        {
            IFrameSource other;
            lock (gate)
            {
                stopping = true;
                clipFrames = null;
                other = OnPurpose(current);
            }
            wake.Set();
            CutShort(other);
        }

        // A copy of the newest picture into `into`; false when there's been none since the last taken.
        // (A copy: the newest stays here for a snap.)
        public bool TakeNewest(Frame into)
        {
            lock (gate)
            {
                Volatile.Write(ref newPosted, 0);
                if (taken == pictures) return false;
                taken = pictures;
                into.Size(latest.Width, latest.Height);
                Buffer.BlockCopy(latest.Pixels, 0, into.Pixels, 0, latest.Pixels.Length);
                return true;
            }
        }

        // The newest picture, whole, as the engine's picture (mirrored if asked); null before the first
        public PictureSource Snap(bool mirror)
        {
            lock (gate)
            {
                if (pictures == 0 || latest.Width == 0) return null;
                return LivePlan.Grab(latest, latest.Width, latest.Height, mirror);
            }
        }

        // Starts a clip: ClipFrames shots, ClipDelayMs apart, width × height (mirrored if asked); Clip has them all
        public void Shoot(int width, int height, bool mirror)
        {
            lock (gate)
            {
                clipW = width;
                clipH = height;
                clipMirror = mirror;
                clipFrames = new List<PictureSource>();
                clipNext = clock.ElapsedMilliseconds;
            }
        }

        // shots taken so far of the clip being shot (-1: none is)
        public int Shots
        {
            get
            {
                lock (gate) return clipFrames?.Count ?? -1;
            }
        }

        // the camera being read, to cut short on purpose, unless it's been cut short already (under `gate`)
        IFrameSource OnPurpose(IFrameSource camera)
        {
            if (camera == null || camera == stoppedOnPurpose) return null;
            stoppedOnPurpose = camera;
            return camera;
        }

        // Cuts short the camera's wait for a picture, from the thread pool: Media Foundation's objects
        // are the MTA's, as the camera's thread is, never the window's thread's. The camera's thread
        // lets go of the rest.
        static void CutShort(IFrameSource camera)
        {
            if (camera == null) return;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    camera.Interrupt();
                }
                catch (Exception)
                {
                    // stopped as far as Windows lets it be; the camera's thread lets go of the rest
                }
            });
        }

        void Post(Action call, DispatcherPriority priority = DispatcherPriority.Normal) => ui.BeginInvoke(priority, call);

        // Waits for the cameras of earlier sessions to be let go of: not past Stop, and not for ever (a
        // camera that never finishes starting is tried anyway, and Windows says whether it's in use). One
        // still not let go of when the time is up may never be (its thread can wait for good: see
        // SourceFrames.Interrupt), so it's dropped from the list: no later session waits for it again.
        void WaitForEarlierSessions()
        {
            var waited = Stopwatch.StartNew();
            foreach (ManualResetEvent other in earlier)
                while (!stopping && !other.WaitOne(0))
                {
                    long left = EarlierSessionsMs - waited.ElapsedMilliseconds;
                    if (left <= 0)
                    {
                        lock (notLetGo) notLetGo.RemoveAll(e => Array.IndexOf(earlier, e) >= 0 && !e.WaitOne(0));
                        return;
                    }
                    WaitHandle.WaitAny(new WaitHandle[] { other, wake }, (int)left);
                }
        }

        void Run()
        {
            IFrameSource source = null;
            try
            {
                List<CameraInfo> list = cameras.Find();
                Post(() => Listed?.Invoke(list));
                if (list.Count == 0) throw new CameraException(Trouble.NoCamera, null);
                int preferred = Prefer != null ? list.FindIndex(c => c.Name == Prefer) : -1;
                if (preferred >= 0) wanted = preferred;
                WaitForEarlierSessions();
                int open = -1;
                bool started = false;
                var back = new Frame();    // the picture being filled
                while (!stopping)
                {
                    int want = Math.Max(0, Math.Min(list.Count - 1, wanted));
                    if (want != open)
                    {
                        LetGo(ref source);
                        open = want;
                        started = false;
                        CameraInfo camera = list[want];
                        Post(() => Starting?.Invoke(camera));
                        try
                        {
                            source = cameras.Open(camera);   // (nothing cuts starting short: after Stop, it's let go of once started)
                            lock (gate)
                            {
                                current = source;
                                currentAt = want;
                            }
                        }
                        catch (Exception x)
                        {
                            CameraException why = Why(x);   // (another camera can still be chosen)
                            Post(() => Failed?.Invoke(why));
                        }
                        continue;
                    }
                    if (source == null)
                    {
                        wake.WaitOne();   // this one wouldn't start: wait for another choice, or Stop
                        continue;
                    }
                    bool got;
                    try
                    {
                        got = source.Next(back);
                    }
                    catch (Exception x)
                    {
                        bool meant;
                        lock (gate) meant = stoppedOnPurpose == source;
                        LetGo(ref source);
                        if (meant)
                        {
                            open = -1;   // stopped on purpose (Stop, another camera chosen): no message, and the one wanted starts
                            continue;
                        }
                        CameraException why = Why(x);
                        Post(() => Failed?.Invoke(why));
                        continue;
                    }
                    if (!got || stopping || wanted != open) continue;
                    if (!started)
                    {
                        started = true;
                        CameraInfo camera = list[open];
                        int w = back.Width, h = back.Height;
                        Width = w;
                        Height = h;
                        Post(() => Started?.Invoke(camera, w, h));
                    }
                    ShootIfDue(back);
                    Keep(ref back);
                }
            }
            catch (Exception x)
            {
                // whatever goes wrong, the camera still goes, and the window says so
                CameraException why = Why(x);
                Post(() => Failed?.Invoke(why));
            }
            finally
            {
                LetGo(ref source);
                letGo.Set();   // a session started again can start the camera now
                lock (notLetGo) notLetGo.Remove(letGo);
                try
                {
                    cameras.Dispose();
                }
                catch (Exception)
                {
                    // nothing left to let go of
                }
                Post(() => Ended?.Invoke());
            }
        }

        // the clip's next shot, if it's time: ten a second (if the camera fell behind, ten a second from here)
        void ShootIfDue(Frame frame)
        {
            int w, h;
            bool mirror;
            long now = clock.ElapsedMilliseconds;
            lock (gate)
            {
                if (clipFrames == null || now < clipNext) return;
                w = clipW;
                h = clipH;
                mirror = clipMirror;
                clipNext = now - clipNext > LivePlan.ClipDelayMs ? now + LivePlan.ClipDelayMs : clipNext + LivePlan.ClipDelayMs;
            }
            PictureSource shot = LivePlan.Grab(frame, w, h, mirror, frame.Width, frame.Height);
            PictureSource[] done = null;
            lock (gate)
            {
                if (clipFrames == null) return;   // stopped meanwhile
                clipFrames.Add(shot);
                if (clipFrames.Count >= LivePlan.ClipFrames)
                {
                    done = clipFrames.ToArray();
                    clipFrames = null;
                }
            }
            if (done != null) Post(() => Clip?.Invoke(done));
        }

        // The picture just read becomes the newest (the one it replaces is filled next); the window
        // hears there's one, unless it's still to take the last.
        void Keep(ref Frame back)
        {
            lock (gate)
            {
                Frame newest = back;
                back = latest;
                latest = newest;
                pictures++;
            }
            if (Interlocked.CompareExchange(ref newPosted, 1, 0) == 0) Post(() => NewPicture?.Invoke(), DispatcherPriority.Background);
        }

        // what went wrong (anything Windows threw that isn't one of the camera's own troubles:
        // "The camera didn't start.", with what it said)
        static CameraException Why(Exception x) => x as CameraException ?? new CameraException(Trouble.Failed, x.Message);

        void LetGo(ref IFrameSource source)
        {
            if (source == null) return;
            lock (gate)
            {
                // nothing cuts it short from here on (a cut already on its way finds it let go of, and does nothing)
                if (current == source) current = null;
                if (stoppedOnPurpose == source) stoppedOnPurpose = null;
            }
            try
            {
                source.Dispose();
            }
            catch (Exception)
            {
                // it's let go of as far as Windows lets it be
            }
            source = null;
        }
    }
}
