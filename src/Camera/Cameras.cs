using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace B0r3dDeRes.Camera
{
    // What can keep the camera from starting or sending pictures, as the website tells it
    enum Trouble { NoCamera, Denied, InUse, Failed }

    sealed class CameraException : Exception
    {
        public readonly Trouble Trouble;

        // `detail`: Windows' side of it, for the line under the message (or null)
        public CameraException(Trouble trouble, string detail) : base(detail ?? "")
        {
            Trouble = trouble;
        }

        // what went wrong, for the window (a denied camera with Windows' own place to allow it)
        public string Words
        {
            get
            {
                switch (Trouble)
                {
                    case Trouble.NoCamera: return "b0r3d DeRes can't find a camera on this PC.";
                    case Trouble.Denied:
                        return "Windows isn't letting b0r3d DeRes use the camera. To allow it: Settings > Privacy & security > Camera, then turn on \"Camera access\" " +
                               "and \"Let desktop apps access your camera\". On Windows 10: Settings > Privacy > Camera, \"Allow desktop apps to access your camera\".";
                    case Trouble.InUse: return "Another app is using the camera. Close it, then try again.";
                    default: return "The camera didn't start. Try again, or open a picture instead.";
                }
            }
        }
    }

    // a camera Windows lists: its name, and its device path to find it again
    sealed class CameraInfo
    {
        public string Name;
        public string Id;
    }

    // one picture from a camera: 4 bytes a pixel (blue, green, red, unused), the top row first
    sealed class Frame
    {
        public int Width, Height;
        public byte[] Pixels = new byte[0];

        // makes room for a picture of this size (keeping the bytes when it's the same size)
        public void Size(int width, int height)
        {
            Width = width;
            Height = height;
            if (Pixels.Length != width * height * 4) Pixels = new byte[width * height * 4];
        }
    }

    // The cameras there are, and the pictures from one: Windows' own (Webcams), or pretend ones in the
    // self-test and the tests. Only ever used on the camera's own thread (CameraLive), which lets go of
    // it (Dispose) when it ends; only Interrupt comes from another.
    interface ICameras : IDisposable
    {
        List<CameraInfo> Find();
        IFrameSource Open(CameraInfo camera);
    }

    interface IFrameSource : IDisposable
    {
        // waits for the camera's next picture and puts it in `into`; false when there was none this time
        bool Next(Frame into);

        // From the thread pool, while Next may be waiting: stops the camera's pictures at once, so that a
        // Next waiting for one ends straight away and Next throws from then on. Dispose, on the camera's
        // thread, then lets go of the camera (only then is it shut down); after Dispose this does nothing.
        void Interrupt();
    }

    // Windows' own cameras, through Media Foundation. Listing them starts none: a camera is only
    // started by Open, and Dispose of what Open gave stops it.
    sealed class Webcams : ICameras
    {
        public List<CameraInfo> Find()
        {
            var list = new List<CameraInfo>();
            foreach (IMFActivate camera in Listed())
            {
                try
                {
                    list.Add(new CameraInfo { Name = Text(camera, MF.FriendlyName), Id = Text(camera, MF.SymbolicLink) });
                }
                finally
                {
                    MF.Release(camera);
                }
            }
            Name(list);
            return list;
        }

        // names to choose by: "Camera 2" for one without, "USB Camera (2)" for a second one called the same
        internal static void Name(List<CameraInfo> list)
        {
            var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < list.Count; i++)
            {
                string name = string.IsNullOrWhiteSpace(list[i].Name) ? "Camera " + (i + 1) : list[i].Name.Trim();
                seen[name] = seen.TryGetValue(name, out int times) ? times + 1 : 1;
                list[i].Name = seen[name] > 1 ? name + " (" + seen[name] + ")" : name;
            }
        }

        public IFrameSource Open(CameraInfo camera)
        {
            List<IMFActivate> all = Listed();
            int at = all.FindIndex(a => camera.Id.Length > 0 && Text(a, MF.SymbolicLink) == camera.Id);
            IMFActivate chosen = at >= 0 ? all[at] : null;
            foreach (IMFActivate other in all) if (other != chosen) MF.Release(other);
            if (chosen == null) throw new CameraException(Trouble.NoCamera, "Windows doesn't list " + camera.Name + " any more");
            Guid id = MF.MediaSourceId;
            int hr = chosen.ActivateObject(ref id, out object made);   // the camera starts (its light comes on)
            var source = made as IMFMediaSource;
            if (hr != 0 || source == null)
            {
                MF.Release(made);
                chosen.ShutdownObject();
                MF.Release(chosen);
                throw Problem(hr != 0 ? hr : unchecked((int)0x80004002), "Windows didn't start " + camera.Name);   // (E_NOINTERFACE)
            }
            return new SourceFrames(source, chosen);   // it lets go of both if it can't read the camera
        }

        // (Media Foundation stays up, as it does for the videos)
        public void Dispose()
        {
        }

        // every video camera Windows lists, none started (each one the caller's to let go of)
        static List<IMFActivate> Listed()
        {
            try
            {
                Video.Mf.Start();
            }
            catch (Video.VideoException x)
            {
                throw new CameraException(Trouble.Failed, x.Message);
            }
            IMFAttributes query = null;
            IntPtr array = IntPtr.Zero;
            var all = new List<IMFActivate>();
            try
            {
                Ok(Native(() => MF.MFCreateAttributes(out query, 1)), "Windows didn't list its cameras");
                Guid key = MF.SourceType, video = MF.VideoCapture;
                Ok(query.SetGUID(ref key, ref video), "Windows didn't list its cameras");
                int count = 0;
                Ok(Native(() => MF.MFEnumDeviceSources(query, out array, out count)), "Windows didn't list its cameras");
                for (int i = 0; i < count; i++)
                {
                    IntPtr one = Marshal.ReadIntPtr(array, i * IntPtr.Size);
                    if (one == IntPtr.Zero) continue;
                    try
                    {
                        object camera = Marshal.GetObjectForIUnknown(one);
                        if (camera is IMFActivate activate) all.Add(activate);
                        else MF.Release(camera);
                    }
                    finally
                    {
                        Marshal.Release(one);   // the list's own hold on it; the wrapper keeps its own
                    }
                }
            }
            finally
            {
                if (array != IntPtr.Zero) Marshal.FreeCoTaskMem(array);
                MF.Release(query);
            }
            return all;
        }

        static string Text(IMFActivate camera, Guid key) => camera.GetAllocatedString(ref key, out string text, out _) == 0 ? text ?? "" : "";

        // a Media Foundation call; Windows without its media features (Windows N without the Media
        // Feature Pack, Windows Server without Media Foundation) has no such DLLs
        internal static int Native(Func<int> call)
        {
            try
            {
                return call();
            }
            catch (Exception x) when (x is DllNotFoundException || x is EntryPointNotFoundException)
            {
                throw new CameraException(Trouble.Failed, "Windows' media features aren't on this PC (Windows N needs the Media Feature Pack)");
            }
        }

        static void Ok(int hr, string doing)
        {
            if (hr != 0) throw Problem(hr, doing);
        }

        // What Windows' answer means here; Windows' code is kept for the detail
        // line (the codes are mferror.h's and winerror.h's)
        internal static CameraException Problem(int hr, string doing)
        {
            Trouble trouble;
            switch (unchecked((uint)hr))
            {
                case 0x80070005:   // E_ACCESSDENIED: Windows' camera privacy settings say no
                    trouble = Trouble.Denied;
                    break;
                case 0xC00D3704:   // MF_E_HW_MFT_FAILED_START_STREAMING: another program has the camera
                case 0xC00D3EA3:   // MF_E_VIDEO_RECORDING_DEVICE_PREEMPTED: another app took it
                case 0x80070020:   // ERROR_SHARING_VIOLATION
                case 0x800700AA:   // ERROR_BUSY
                case 0x80070964:   // ERROR_DEVICE_IN_USE
                    trouble = Trouble.InUse;
                    break;
                case 0xC00D3EA2:   // MF_E_VIDEO_RECORDING_DEVICE_INVALIDATED: unplugged or switched off
                case 0xC00DABE0:   // MF_E_NO_CAPTURE_DEVICES_AVAILABLE
                case 0x8007048F:   // ERROR_DEVICE_NOT_CONNECTED
                    trouble = Trouble.NoCamera;
                    break;
                default:
                    trouble = Trouble.Failed;
                    break;
            }
            return new CameraException(trouble, doing + " (error 0x" + hr.ToString("X8") + ")");
        }
    }

    // A started camera's pictures (or, in the tests, a video file's) as Media Foundation turns them
    // into RGB32. Of the kinds of picture the camera sends, the nearest to the size wanted (the
    // website asks a browser for 1920 × 1440) at 15 a second or more, and at that size the nearest to
    // 30 a second. Lets go of the camera when disposed.
    sealed class SourceFrames : IFrameSource
    {
        // what the website asks a browser for (its ideal size)
        public const int WantWidth = 1920, WantHeight = 1440;

        IMFMediaSource source;
        IMFActivate activate;      // what started the camera (none for a file)
        IMFSourceReader reader;
        int width, height, stride; // the picture's size, and how far apart its rows are when the buffer doesn't say
        readonly object gate = new object();   // Interrupt (from the thread pool) and Dispose, one at a time
        volatile bool reading;     // ReadSample is under way
        Timer lastResort;          // from a stop on: shuts the camera down if ReadSample is still waiting (see Interrupt)

        // how long after a stop a reader still waiting for a picture is given before the camera is shut
        // down anyway (see Interrupt; the tests shorten it)
        internal static int ShutDownAfterMs = 5000;

        internal int Width => width;
        internal int Height => height;

        public SourceFrames(IMFMediaSource source, IMFActivate activate, int wantWidth = WantWidth, int wantHeight = WantHeight)
        {
            this.source = source;
            this.activate = activate;
            IMFAttributes settings = null;
            try
            {
                Check(MF.MFCreateAttributes(out settings, 1), "Media Foundation didn't start the camera's reader");
                // the camera's YUV or JPEG pictures turned into RGB by Windows' advanced video processor, as
                // the videos' frames are (its colours are the video reader's; the plain one's are a few levels off)
                Guid key = MF.AdvancedVideoProcessing;
                Check(settings.SetUINT32(ref key, 1), "Media Foundation didn't start the camera's reader");
                Check(Webcams.Native(() => MF.MFCreateSourceReaderFromMediaSource(source, settings, out reader)), "Media Foundation didn't start the camera's reader");
                Choose(wantWidth, wantHeight);
            }
            catch
            {
                Dispose();
                throw;
            }
            finally
            {
                MF.Release(settings);
            }
        }

        // a kind of picture the camera sends: its place in the camera's list, its size and rate
        internal struct Kind
        {
            public int Index, Width, Height;
            public double Rate;
        }

        // The order to try them in: 15 pictures a second or more first, then the nearest in size to
        // the size wanted, then the nearest to 30 a second, as Chrome picks for the website: 60 would
        // only double the decoding and copying. Of two as near, the faster.
        internal static IEnumerable<Kind> InOrder(IEnumerable<Kind> kinds, int wantWidth = WantWidth, int wantHeight = WantHeight) =>
            kinds.OrderBy(k => k.Rate >= 15 ? 0 : 1).ThenBy(k => Math.Abs(Math.Log(Math.Max(1, (double)k.Width * k.Height) / ((double)wantWidth * wantHeight))))
                 .ThenBy(k => Math.Abs(k.Rate - 30)).ThenByDescending(k => k.Rate);

        // the camera's own kind of picture to read, turned into RGB32
        void Choose(int wantWidth, int wantHeight)
        {
            var kinds = new List<Kind>();
            for (int i = 0; i < 1000; i++)
            {
                if (reader.GetNativeMediaType(MF.FirstVideoStream, i, out IMFMediaType type) != 0) break;   // MF_E_NO_MORE_TYPES
                try
                {
                    SizeOf(type, out int w, out int h);
                    kinds.Add(new Kind { Index = i, Width = w, Height = h, Rate = RateOf(type) });
                }
                finally
                {
                    MF.Release(type);
                }
            }
            int first = 0;   // the first refusal, which says why when every kind is refused
            foreach (Kind kind in InOrder(kinds, wantWidth, wantHeight))
            {
                int hr = Use(kind.Index);
                if (hr == 0) return;
                if (first == 0) first = hr;
            }
            throw first != 0 ? Webcams.Problem(first, "Media Foundation can't turn the camera's pictures into RGB")
                             : new CameraException(Trouble.Failed, "the camera sends no pictures Media Foundation can turn into RGB");
        }

        // the camera's kind `index`, then the same as RGB32: Media Foundation fills in the size and rate
        int Use(int index)
        {
            IMFMediaType native = null, rgb = null;
            try
            {
                int hr = reader.GetNativeMediaType(MF.FirstVideoStream, index, out native);
                if (hr == 0) hr = reader.SetCurrentMediaType(MF.FirstVideoStream, IntPtr.Zero, native);
                if (hr == 0) hr = MF.MFCreateMediaType(out rgb);
                Guid major = MF.MajorType, video = MF.Video, sub = MF.Subtype, rgb32 = MF.Rgb32;
                if (hr == 0) hr = rgb.SetGUID(ref major, ref video);
                if (hr == 0) hr = rgb.SetGUID(ref sub, ref rgb32);
                if (hr == 0) hr = reader.SetCurrentMediaType(MF.FirstVideoStream, IntPtr.Zero, rgb);
                if (hr == 0 && !ReadSize()) hr = unchecked((int)0xC00D36B4);   // MF_E_INVALIDMEDIATYPE
                return hr;
            }
            finally
            {
                MF.Release(native);
                MF.Release(rgb);
            }
        }

        // the picture's size (and its rows' distance, when the type says) from the kind being read
        bool ReadSize()
        {
            IMFMediaType current = null;
            try
            {
                if (reader.GetCurrentMediaType(MF.FirstVideoStream, out current) != 0) return false;
                Guid key = MF.Subtype;
                if (current.GetGUID(ref key, out Guid sub) != 0 || sub != MF.Rgb32) return false;
                SizeOf(current, out width, out height);
                Guid strideKey = MF.DefaultStride;
                stride = current.GetUINT32(ref strideKey, out int rows) == 0 && Math.Abs(rows) >= width * 4 ? rows : width * 4;
                return width > 0 && height > 0 && (long)width * height <= 8192L * 8192;
            }
            finally
            {
                MF.Release(current);
            }
        }

        static void SizeOf(IMFMediaType type, out int width, out int height)
        {
            Guid key = MF.FrameSize;
            if (type.GetUINT64(ref key, out long size) != 0) size = 0;
            width = (int)(size >> 32);
            height = (int)(size & 0xFFFFFFFF);
        }

        // pictures a second
        static double RateOf(IMFMediaType type)
        {
            Guid key = MF.FrameRate;
            if (type.GetUINT64(ref key, out long rate) != 0) return 0;
            long per = rate & 0xFFFFFFFF;
            return per == 0 ? 0 : (rate >> 32) / (double)per;
        }

        public bool Next(Frame into)
        {
            IMFSample sample = null;
            try
            {
                int hr, flags;
                reading = true;
                try
                {
                    hr = reader.ReadSample(MF.FirstVideoStream, 0, out _, out flags, out _, out sample);
                }
                finally
                {
                    reading = false;
                }
                if (hr != 0) throw Webcams.Problem(hr, "the camera stopped sending pictures");
                if ((flags & (MF.ReadError | MF.EndOfStream)) != 0) throw new CameraException(Trouble.Failed, "the camera stopped sending pictures");
                if ((flags & MF.CurrentTypeChanged) != 0 && !ReadSize()) throw new CameraException(Trouble.Failed, "the camera's pictures changed to a kind b0r3d DeRes can't read");
                if (sample == null) return false;   // a gap in the pictures, with nothing in it
                Copy(sample, width, height, stride, into);
                return true;
            }
            finally
            {
                MF.Release(sample);   // at once: the camera has only a few pictures to fill
            }
        }

        // One picture into `into`, the top row first. A buffer that knows its rows (IMF2DBuffer) says
        // where the top row is and how far the next one is, upwards for a picture kept bottom row first;
        // otherwise the kind's rows' distance says, and below 0 the bottom row comes first.
        internal static void Copy(IMFSample sample, int width, int height, int stride, Frame into)
        {
            IMFMediaBuffer buffer = null;
            try
            {
                Check(sample.ConvertToContiguousBuffer(out buffer), "a picture from the camera couldn't be read");
                into.Size(width, height);
                int row = width * 4;
                if (buffer is IMF2DBuffer rows && rows.Lock2D(out IntPtr top, out int pitch) == 0)
                {
                    try
                    {
                        if (Math.Abs(pitch) < row) throw new CameraException(Trouble.Failed, "a picture from the camera was narrower than its kind says");
                        for (int y = 0; y < height; y++) Marshal.Copy(top + y * pitch, into.Pixels, y * row, row);
                    }
                    finally
                    {
                        rows.Unlock2D();
                    }
                    return;
                }
                Check(buffer.Lock(out IntPtr start, out _, out int length), "a picture from the camera couldn't be read");
                try
                {
                    if (length < (long)Math.Abs(stride) * (height - 1) + row) throw new CameraException(Trouble.Failed, "a picture from the camera was cut short");
                    IntPtr first = stride < 0 ? start + -stride * (height - 1) : start;
                    for (int y = 0; y < height; y++) Marshal.Copy(first + y * stride, into.Pixels, y * row, row);
                }
                finally
                {
                    buffer.Unlock();
                }
            }
            finally
            {
                MF.Release(buffer);
            }
        }

        static void Check(int hr, string doing)
        {
            if (hr != 0) throw Webcams.Problem(hr, doing);
        }

        // Stops the camera while ReadSample may be waiting for a picture it hasn't sent. Shutting the
        // camera down doesn't end that wait (the reader goes on waiting for good), but an error from the
        // camera does: the reader hears it, and ReadSample returns it at once, as it does from then on.
        // The reader hears it on a thread of Media Foundation's own, which a busy PC can keep waiting, so
        // the camera isn't shut down here: shutting it down empties its events, and an error the reader
        // hasn't heard yet would go with them. The camera's thread shuts it down instead (Dispose) as soon
        // as Next has thrown, and its light goes off. Only if ReadSample is still waiting ShutDownAfterMs
        // later (the reader never heard it: Windows' trouble) is the camera shut down from here, so that its
        // light goes off at least; its thread may then wait for good. From the thread pool, as the camera's
        // objects are the MTA's; never while Dispose lets them go.
        public void Interrupt()
        {
            lock (gate)
            {
                if (source == null || lastResort != null) return;   // let go of, or stopped already
                Guid none = Guid.Empty;
                source.QueueEvent(MF.ErrorEvent, ref none, MF.ShutDown, IntPtr.Zero);
                lastResort = new Timer(_ => ShutDownIfStillWaiting(), null, ShutDownAfterMs, Timeout.Infinite);
            }
        }

        // the last resort, ShutDownAfterMs after a stop, on a thread of the thread pool's
        void ShutDownIfStillWaiting()
        {
            try
            {
                lock (gate)
                    if (source != null && reading) source.Shutdown();
            }
            catch (Exception)
            {
                // stopped as far as Windows lets it be
            }
        }

        // Lets the camera go: the reader (which shuts the camera down as it goes), the camera itself and
        // what started it. Its light goes off.
        public void Dispose()
        {
            lock (gate)
            {
                lastResort?.Dispose();
                lastResort = null;
                MF.Release(reader);
                reader = null;
                if (source != null)
                {
                    source.Shutdown();
                    MF.Release(source);
                    source = null;
                }
                if (activate != null)
                {
                    activate.ShutdownObject();
                    MF.Release(activate);
                    activate = null;
                }
            }
        }
    }

    // A pretend camera holding up pictures one after another, the last one for as long as it's read:
    // the self-test's. It has nothing to do with Windows' cameras.
    sealed class PictureCameras : ICameras
    {
        readonly string name;
        readonly Frame[] pictures;
        int shown, holding;
        volatile bool disposed;

        public PictureCameras(string name, params BitmapSource[] pictures)
        {
            this.name = name;
            this.pictures = pictures.Select(FrameOf).ToArray();
        }

        public PictureCameras(string name, params Frame[] pictures)
        {
            this.name = name;
            this.pictures = pictures;
        }

        public int Shown => Volatile.Read(ref shown);

        // the pretend camera and the cameras both let go of
        public bool LetGo => disposed && Volatile.Read(ref holding) == 0;

        public List<CameraInfo> Find() => new List<CameraInfo> { new CameraInfo { Name = name, Id = "pictures" } };

        public IFrameSource Open(CameraInfo camera) => new Held(this);

        public void Dispose() => disposed = true;

        sealed class Held : IFrameSource
        {
            readonly PictureCameras cameras;
            bool done;
            volatile bool stopped;

            public Held(PictureCameras cameras)
            {
                this.cameras = cameras;
                Interlocked.Increment(ref cameras.holding);
            }

            public bool Next(Frame into)
            {
                if (stopped) throw new CameraException(Trouble.Failed, "the pretend camera was stopped");
                Thread.Sleep(30);   // about as often as a camera sends them
                Frame picture = cameras.pictures[Math.Min(Interlocked.Increment(ref cameras.shown) - 1, cameras.pictures.Length - 1)];
                into.Size(picture.Width, picture.Height);
                Buffer.BlockCopy(picture.Pixels, 0, into.Pixels, 0, picture.Pixels.Length);
                return true;
            }

            public void Interrupt() => stopped = true;

            public void Dispose()
            {
                if (done) return;
                done = true;
                Interlocked.Decrement(ref cameras.holding);
            }
        }

        // a picture as a camera's frame
        internal static Frame FrameOf(BitmapSource picture)
        {
            var bgr = new FormatConvertedBitmap(picture, PixelFormats.Bgr32, null, 0);
            var frame = new Frame();
            frame.Size(bgr.PixelWidth, bgr.PixelHeight);
            bgr.CopyPixels(frame.Pixels, frame.Width * 4, 0);
            return frame;
        }
    }
}
