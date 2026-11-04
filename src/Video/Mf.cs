using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using STATSTG = System.Runtime.InteropServices.ComTypes.STATSTG;

namespace B0r3dDeRes.Video
{
    // Something about a video that can't be done, in words for the window.
    sealed class VideoException : Exception
    {
        public VideoException(string message) : base(message) { }
    }

    // Windows' own video system, Media Foundation, called directly: the few functions and
    // interfaces b0r3d DeRes reads and saves video with. Files are opened by the program and handed
    // over as streams of bytes, never by name or address, and the video system starts without its
    // network parts (MFSTARTUP_LITE), so nothing here ever goes online.
    static class Mf
    {
        const uint Version = 0x00020070, StartupLite = 1;

        [DllImport("mfplat.dll")] static extern int MFStartup(uint version, uint flags);
        [DllImport("mfplat.dll")] public static extern int MFCreateAttributes(out IMFAttributes attributes, uint initialSize);
        [DllImport("mfplat.dll")] public static extern int MFCreateMediaType(out IMFMediaType type);
        [DllImport("mfplat.dll")] public static extern int MFCreateSample(out IMFSample sample);
        [DllImport("mfplat.dll")] public static extern int MFCreateMemoryBuffer(uint maxLength, out IMFMediaBuffer buffer);
        [DllImport("mfplat.dll")] public static extern int MFCreateMFByteStreamOnStream(IStreamRaw stream, out IMFByteStream byteStream);
        [DllImport("mfreadwrite.dll")] public static extern int MFCreateSourceReaderFromByteStream(IMFByteStream byteStream, IMFAttributes attributes, out IMFSourceReader reader);
        // (always given no address: the bytes go to a stream of the program's own)
        [DllImport("mfreadwrite.dll")] public static extern int MFCreateSinkWriterFromURL([MarshalAs(UnmanagedType.LPWStr)] string url, IMFByteStream byteStream, IMFAttributes attributes, out IMFSinkWriter writer);

        static readonly object startLock = new object();
        static bool started;
        static string notStarted;

        // Starts Windows' video system once (it stays up until the program ends). Throws
        // VideoException when this Windows has none: the "N" editions sold in Europe come without it.
        public static void Start()
        {
            lock (startLock)
            {
                if (started) return;
                if (notStarted == null)
                {
                    try
                    {
                        int hr = MFStartup(Version, StartupLite);
                        if (hr >= 0)
                        {
                            started = true;
                            return;
                        }
                        notStarted = "Windows' video parts didn't start (" + Hr(hr) + ").";
                    }
                    catch (Exception x) when (x is DllNotFoundException || x is EntryPointNotFoundException || x is BadImageFormatException)
                    {
                        notStarted = "This Windows has no video parts.";
                    }
                    notStarted += " Videos need them: on the \"N\" editions of Windows, install Microsoft's free Media Feature Pack (Settings, Apps, Optional features).";
                }
                throw new VideoException(notStarted);
            }
        }

        public static bool Available
        {
            get
            {
                try
                {
                    Start();
                    return true;
                }
                catch (VideoException)
                {
                    return false;
                }
            }
        }

        // ---- results and attributes

        public const int NoMoreStreams = unchecked((int)0xC00D36B3);   // MF_E_INVALIDSTREAMNUMBER

        public static string Hr(int hr)
        {
            switch (unchecked((uint)hr))
            {
                case 0xC00D36B4: return "MF_E_INVALIDMEDIATYPE";
                case 0xC00D36BE: return "MF_E_INVALID_FILE_FORMAT";
                case 0xC00D36C4: return "MF_E_UNSUPPORTED_BYTESTREAM_TYPE";
                case 0xC00D36F0: return "MF_E_CANNOT_PARSE_BYTESTREAM";
                case 0xC00D5212: return "MF_E_TOPO_CODEC_NOT_FOUND";
                case 0xC00D6D61: return "MF_E_TRANSFORM_STREAM_CHANGE";
                case 0x8007000E: return "E_OUTOFMEMORY";
                default: return "0x" + hr.ToString("X8");
            }
        }

        public static void Check(int hr, string what)
        {
            if (hr == unchecked((int)0x8007000E)) throw new OutOfMemoryException();
            if (hr < 0) throw new VideoException(what + " (" + Hr(hr) + ")");
        }

        public static void Release(object o)
        {
            if (o != null && Marshal.IsComObject(o)) Marshal.ReleaseComObject(o);
        }

        public static IMFAttributes NewAttributes()
        {
            Check(MFCreateAttributes(out IMFAttributes a, 4), "Windows' video system has no room for settings");
            return a;
        }

        public static IMFMediaType NewType(Guid major, Guid sub)
        {
            Check(MFCreateMediaType(out IMFMediaType t), "Windows' video system has no room for a media type");
            SetGuid(t, G.MajorType, major);
            SetGuid(t, G.Subtype, sub);
            return t;
        }

        public static uint? U32(object o, Guid key) => ((IMFAttributes)o).GetUINT32(ref key, out uint v) >= 0 ? v : (uint?)null;
        public static ulong? U64(object o, Guid key) => ((IMFAttributes)o).GetUINT64(ref key, out ulong v) >= 0 ? v : (ulong?)null;
        public static Guid? GuidOf(object o, Guid key) => ((IMFAttributes)o).GetGUID(ref key, out Guid v) >= 0 ? v : (Guid?)null;

        public static byte[] Blob(object o, Guid key)
        {
            if (((IMFAttributes)o).GetAllocatedBlob(ref key, out IntPtr p, out uint n) < 0) return null;
            var b = new byte[n];
            Marshal.Copy(p, b, 0, (int)n);
            Marshal.FreeCoTaskMem(p);
            return b;
        }

        public static void SetU32(object o, Guid key, uint v) => Check(((IMFAttributes)o).SetUINT32(ref key, v), "a video setting didn't take");
        public static void SetU64(object o, Guid key, ulong v) => Check(((IMFAttributes)o).SetUINT64(ref key, v), "a video setting didn't take");
        public static void SetGuid(object o, Guid key, Guid v) => Check(((IMFAttributes)o).SetGUID(ref key, ref v), "a video setting didn't take");
        public static void SetPair(object o, Guid key, uint high, uint low) => SetU64(o, key, ((ulong)high << 32) | low);
    }

    [StructLayout(LayoutKind.Explicit, Size = 24)]
    struct PropVariant
    {
        [FieldOffset(0)] public ushort Type;
        [FieldOffset(8)] public long Long;
    }

    [ComImport, Guid("2cd2d921-c447-44a7-a13c-4adabfc247e3"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMFAttributes
    {
        [PreserveSig] int GetItem([In] ref Guid key, IntPtr value);
        [PreserveSig] int GetItemType([In] ref Guid key, out int type);
        [PreserveSig] int CompareItem([In] ref Guid key, IntPtr value, out int result);
        [PreserveSig] int Compare(IMFAttributes theirs, int matchType, out int result);
        [PreserveSig] int GetUINT32([In] ref Guid key, out uint value);
        [PreserveSig] int GetUINT64([In] ref Guid key, out ulong value);
        [PreserveSig] int GetDouble([In] ref Guid key, out double value);
        [PreserveSig] int GetGUID([In] ref Guid key, out Guid value);
        [PreserveSig] int GetStringLength([In] ref Guid key, out uint length);
        [PreserveSig] int GetString([In] ref Guid key, IntPtr value, uint size, out uint length);
        [PreserveSig] int GetAllocatedString([In] ref Guid key, out IntPtr value, out uint length);
        [PreserveSig] int GetBlobSize([In] ref Guid key, out uint size);
        [PreserveSig] int GetBlob([In] ref Guid key, IntPtr buffer, uint size, out uint blobSize);
        [PreserveSig] int GetAllocatedBlob([In] ref Guid key, out IntPtr buffer, out uint size);
        [PreserveSig] int GetUnknown([In] ref Guid key, [In] ref Guid riid, out IntPtr value);
        [PreserveSig] int SetItem([In] ref Guid key, IntPtr value);
        [PreserveSig] int DeleteItem([In] ref Guid key);
        [PreserveSig] int DeleteAllItems();
        [PreserveSig] int SetUINT32([In] ref Guid key, uint value);
        [PreserveSig] int SetUINT64([In] ref Guid key, ulong value);
        [PreserveSig] int SetDouble([In] ref Guid key, double value);
        [PreserveSig] int SetGUID([In] ref Guid key, [In] ref Guid value);
        [PreserveSig] int SetString([In] ref Guid key, [MarshalAs(UnmanagedType.LPWStr)] string value);
        [PreserveSig] int SetBlob([In] ref Guid key, IntPtr buffer, uint size);
        [PreserveSig] int SetUnknown([In] ref Guid key, [MarshalAs(UnmanagedType.IUnknown)] object value);
        [PreserveSig] int LockStore();
        [PreserveSig] int UnlockStore();
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetItemByIndex(uint index, out Guid key, IntPtr value);
        [PreserveSig] int CopyAllItems(IMFAttributes destination);
    }

    // The interfaces below derive from IMFAttributes: its 30 methods come first in each one's table
    // (placeholders here), and attributes are read through a cast to IMFAttributes.
    [ComImport, Guid("44ae0fa8-ea31-4109-8d2e-4cae4997c555"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMFMediaType
    {
        void _00(); void _01(); void _02(); void _03(); void _04(); void _05(); void _06(); void _07(); void _08(); void _09();
        void _10(); void _11(); void _12(); void _13(); void _14(); void _15(); void _16(); void _17(); void _18(); void _19();
        void _20(); void _21(); void _22(); void _23(); void _24(); void _25(); void _26(); void _27(); void _28(); void _29();
        [PreserveSig] int GetMajorType(out Guid type);
        [PreserveSig] int IsCompressedFormat(out int compressed);
    }

    [ComImport, Guid("c40a00f2-b93a-4d80-ae8c-5a1c634f58e4"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMFSample
    {
        void _00(); void _01(); void _02(); void _03(); void _04(); void _05(); void _06(); void _07(); void _08(); void _09();
        void _10(); void _11(); void _12(); void _13(); void _14(); void _15(); void _16(); void _17(); void _18(); void _19();
        void _20(); void _21(); void _22(); void _23(); void _24(); void _25(); void _26(); void _27(); void _28(); void _29();
        [PreserveSig] int GetSampleFlags(out uint flags);
        [PreserveSig] int SetSampleFlags(uint flags);
        [PreserveSig] int GetSampleTime(out long time);
        [PreserveSig] int SetSampleTime(long time);
        [PreserveSig] int GetSampleDuration(out long duration);
        [PreserveSig] int SetSampleDuration(long duration);
        [PreserveSig] int GetBufferCount(out uint count);
        [PreserveSig] int GetBufferByIndex(uint index, out IMFMediaBuffer buffer);
        [PreserveSig] int ConvertToContiguousBuffer(out IMFMediaBuffer buffer);
        [PreserveSig] int AddBuffer(IMFMediaBuffer buffer);
        [PreserveSig] int RemoveBufferByIndex(uint index);
        [PreserveSig] int RemoveAllBuffers();
        [PreserveSig] int GetTotalLength(out uint length);
        [PreserveSig] int CopyToBuffer(IMFMediaBuffer buffer);
    }

    [ComImport, Guid("045FA593-8799-42b8-BC8D-8968C6453507"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMFMediaBuffer
    {
        [PreserveSig] int Lock(out IntPtr buffer, out uint maxLength, out uint currentLength);
        [PreserveSig] int Unlock();
        [PreserveSig] int GetCurrentLength(out uint length);
        [PreserveSig] int SetCurrentLength(uint length);
        [PreserveSig] int GetMaxLength(out uint length);
    }

    [ComImport, Guid("7DC9D5F9-9ED9-44ec-9BBF-0600BB589FBB"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMF2DBuffer
    {
        [PreserveSig] int Lock2D(out IntPtr scanline0, out int pitch);
        [PreserveSig] int Unlock2D();
        [PreserveSig] int GetScanline0AndPitch(out IntPtr scanline0, out int pitch);
        [PreserveSig] int IsContiguousFormat(out int contiguous);
        [PreserveSig] int GetContiguousLength(out uint length);
    }

    // (only the start of its table: nothing else is called)
    [ComImport, Guid("ad4c1b00-4bf7-422f-9175-756693d9130d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMFByteStream
    {
        [PreserveSig] int GetCapabilities(out uint capabilities);
        [PreserveSig] int GetLength(out ulong length);
    }

    // (up to what's called: its attributes)
    [ComImport, Guid("bf94c121-5b05-4e6f-8000-ba598961414d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMFTransform
    {
        void _0(); void _1(); void _2(); void _3(); void _4();
        [PreserveSig] int GetAttributes(out IMFAttributes attributes);
    }

    [ComImport, Guid("70ae66f2-c809-4e4f-8915-bdcb406b7993"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMFSourceReader
    {
        [PreserveSig] int GetStreamSelection(uint stream, out int selected);
        [PreserveSig] int SetStreamSelection(uint stream, int selected);
        [PreserveSig] int GetNativeMediaType(uint stream, uint index, out IMFMediaType type);
        [PreserveSig] int GetCurrentMediaType(uint stream, out IMFMediaType type);
        [PreserveSig] int SetCurrentMediaType(uint stream, IntPtr reserved, IMFMediaType type);
        [PreserveSig] int SetCurrentPosition([In] ref Guid timeFormat, [In] ref PropVariant position);
        [PreserveSig] int ReadSample(uint stream, uint controlFlags, out uint actualStream, out uint streamFlags, out long timestamp, out IMFSample sample);
        [PreserveSig] int Flush(uint stream);
        [PreserveSig] int GetServiceForStream(uint stream, [In] ref Guid service, [In] ref Guid riid, out IntPtr obj);
        [PreserveSig] int GetPresentationAttribute(uint stream, [In] ref Guid attribute, out PropVariant value);
    }

    [ComImport, Guid("7b981cf0-560e-4116-9875-b099895f23d7"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMFSourceReaderEx
    {
        [PreserveSig] int GetStreamSelection(uint stream, out int selected);
        [PreserveSig] int SetStreamSelection(uint stream, int selected);
        [PreserveSig] int GetNativeMediaType(uint stream, uint index, out IMFMediaType type);
        [PreserveSig] int GetCurrentMediaType(uint stream, out IMFMediaType type);
        [PreserveSig] int SetCurrentMediaType(uint stream, IntPtr reserved, IMFMediaType type);
        [PreserveSig] int SetCurrentPosition([In] ref Guid timeFormat, [In] ref PropVariant position);
        [PreserveSig] int ReadSample(uint stream, uint controlFlags, out uint actualStream, out uint streamFlags, out long timestamp, out IMFSample sample);
        [PreserveSig] int Flush(uint stream);
        [PreserveSig] int GetServiceForStream(uint stream, [In] ref Guid service, [In] ref Guid riid, out IntPtr obj);
        [PreserveSig] int GetPresentationAttribute(uint stream, [In] ref Guid attribute, out PropVariant value);
        [PreserveSig] int SetNativeMediaType(uint stream, IMFMediaType type, out uint flags);
        [PreserveSig] int AddTransformForStream(uint stream, [MarshalAs(UnmanagedType.IUnknown)] object transform);
        [PreserveSig] int RemoveAllTransformsForStream(uint stream);
        [PreserveSig] int GetTransformForStream(uint stream, uint index, out Guid category, out IMFTransform transform);
    }

    [ComImport, Guid("3137f1cd-fe5e-4805-a5d8-fb477448cb3d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMFSinkWriter
    {
        [PreserveSig] int AddStream(IMFMediaType targetType, out uint stream);
        [PreserveSig] int SetInputMediaType(uint stream, IMFMediaType inputType, IMFAttributes encodingParameters);
        [PreserveSig] int BeginWriting();
        [PreserveSig] int WriteSample(uint stream, IMFSample sample);
        [PreserveSig] int SendStreamTick(uint stream, long timestamp);
        [PreserveSig] int PlaceMarker(uint stream, IntPtr context);
        [PreserveSig] int NotifyEndOfSegment(uint stream);
        [PreserveSig] int Flush(uint stream);
        [PreserveSig] int FinalizeWriter();   // IMFSinkWriter::Finalize
        [PreserveSig] int GetServiceForStream(uint stream, [In] ref Guid service, [In] ref Guid riid, out IntPtr obj);
    }

    // IStream, with raw pointers: how a stream of the program's own is handed to Windows' video system
    [ComImport, Guid("0000000c-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IStreamRaw
    {
        [PreserveSig] int Read(IntPtr pv, int cb, IntPtr read);
        [PreserveSig] int Write(IntPtr pv, int cb, IntPtr written);
        [PreserveSig] int Seek(long move, int origin, IntPtr position);
        [PreserveSig] int SetSize(long size);
        [PreserveSig] int CopyTo(IntPtr stream, long cb, IntPtr read, IntPtr written);
        [PreserveSig] int Commit(int flags);
        [PreserveSig] int Revert();
        [PreserveSig] int LockRegion(long offset, long cb, int lockType);
        [PreserveSig] int UnlockRegion(long offset, long cb, int lockType);
        [PreserveSig] int Stat(out STATSTG stat, int flags);
        [PreserveSig] int Clone(out IntPtr stream);
    }

    // A .NET stream (a file the program opened) as an IStream for Windows' video system.
    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    sealed class StreamForMf : IStreamRaw
    {
        const int NotImplemented = unchecked((int)0x80004001), Failed = unchecked((int)0x80004005), InvalidFunction = unchecked((int)0x80030001);
        readonly Stream stream;
        readonly bool writable;
        readonly object gate = new object();
        readonly byte[] buffer = new byte[1 << 20];

        public StreamForMf(Stream stream, bool writable)
        {
            this.stream = stream;
            this.writable = writable;
        }

        public int Read(IntPtr pv, int cb, IntPtr read)
        {
            try
            {
                lock (gate)
                {
                    int total = 0;
                    while (total < cb)
                    {
                        int n = stream.Read(buffer, 0, Math.Min(cb - total, buffer.Length));
                        if (n <= 0) break;
                        Marshal.Copy(buffer, 0, pv + total, n);
                        total += n;
                    }
                    if (read != IntPtr.Zero) Marshal.WriteInt32(read, total);
                    return total < cb ? 1 : 0;   // S_FALSE at the end
                }
            }
            catch (Exception)
            {
                return Failed;
            }
        }

        public int Write(IntPtr pv, int cb, IntPtr written)
        {
            if (!writable) return InvalidFunction;
            try
            {
                lock (gate)
                {
                    int total = 0;
                    while (total < cb)
                    {
                        int n = Math.Min(cb - total, buffer.Length);
                        Marshal.Copy(pv + total, buffer, 0, n);
                        stream.Write(buffer, 0, n);
                        total += n;
                    }
                    if (written != IntPtr.Zero) Marshal.WriteInt32(written, total);
                    return 0;
                }
            }
            catch (Exception)
            {
                return Failed;
            }
        }

        public int Seek(long move, int origin, IntPtr position)
        {
            try
            {
                lock (gate)
                {
                    long p = stream.Seek(move, (SeekOrigin)origin);
                    if (position != IntPtr.Zero) Marshal.WriteInt64(position, p);
                    return 0;
                }
            }
            catch (Exception)
            {
                return InvalidFunction;
            }
        }

        public int SetSize(long size)
        {
            if (!writable) return InvalidFunction;
            try
            {
                lock (gate) stream.SetLength(size);
                return 0;
            }
            catch (Exception)
            {
                return Failed;
            }
        }

        public int CopyTo(IntPtr target, long cb, IntPtr read, IntPtr written) => NotImplemented;

        public int Commit(int flags)
        {
            try
            {
                lock (gate) stream.Flush();
                return 0;
            }
            catch (Exception)
            {
                return Failed;
            }
        }

        public int Revert() => NotImplemented;
        public int LockRegion(long offset, long cb, int lockType) => InvalidFunction;
        public int UnlockRegion(long offset, long cb, int lockType) => InvalidFunction;

        public int Stat(out STATSTG stat, int flags)
        {
            stat = new STATSTG();
            try
            {
                lock (gate)
                {
                    stat.type = 2;   // a stream
                    stat.cbSize = stream.Length;
                    stat.grfMode = writable ? 2 : 0;
                }
                return 0;
            }
            catch (Exception)
            {
                return Failed;
            }
        }

        public int Clone(out IntPtr copy)
        {
            copy = IntPtr.Zero;
            return NotImplemented;
        }
    }

    // the attribute and type names Media Foundation's headers give these GUIDs
    static class G
    {
        static Guid FourCc(string s) => new Guid((uint)(s[0] | (s[1] << 8) | (s[2] << 16) | (s[3] << 24)), 0x0000, 0x0010, 0x80, 0x00, 0x00, 0xAA, 0x00, 0x38, 0x9B, 0x71);
        static Guid Numbered(uint n) => new Guid(n, 0x0000, 0x0010, 0x80, 0x00, 0x00, 0xAA, 0x00, 0x38, 0x9B, 0x71);

        public static readonly Guid EnableAdvancedVideoProcessing = new Guid("0f81da2c-b537-4672-a8b2-a681b17307a3");   // MF_SOURCE_READER_ENABLE_ADVANCED_VIDEO_PROCESSING
        public static readonly Guid HardwareTransforms = new Guid("a634a91c-822b-41b9-a494-4de4643612b0");              // MF_READWRITE_ENABLE_HARDWARE_TRANSFORMS
        public static readonly Guid DisableThrottling = new Guid("08b845d8-2b74-4afe-9d53-be16d2d5ae4f");               // MF_SINK_WRITER_DISABLE_THROTTLING
        public static readonly Guid ContainerType = new Guid("150ff23f-4abc-478b-ac4f-e1916fba1cca");                   // MF_TRANSCODE_CONTAINERTYPE
        public static readonly Guid Mpeg4Container = new Guid("dc6cd05d-b9d0-40ef-bd35-fa622c1ab28a");                  // MFTranscodeContainerType_MPEG4
        public static readonly Guid DisableFrameRateConversion = new Guid("2c0afa19-7a97-4d5a-9ee8-16d4fc518d8c");      // MF_XVP_DISABLE_FRC
        public static readonly Guid MajorType = new Guid("48eba18e-f8c9-4687-bf11-0a74c9f96a8f");
        public static readonly Guid Subtype = new Guid("f7e34c9a-42e8-4714-b74b-cb29d72c35e5");
        public static readonly Guid FrameSize = new Guid("1652c33d-d6b2-4012-b834-72030849a37d");
        public static readonly Guid FrameRate = new Guid("c459a2e8-3d2c-4e44-b132-fee5156c7bb0");
        public static readonly Guid PixelAspectRatio = new Guid("c6376a1e-8d0a-4027-be45-6d9a0ad39bb6");
        public static readonly Guid InterlaceMode = new Guid("e2724bb8-e676-4806-b4b2-a8d6efb44ccd");
        public static readonly Guid AverageBitrate = new Guid("20332624-fb0d-4d9e-bd0d-cbf6786c102e");
        public static readonly Guid DefaultStride = new Guid("644b4e48-1e02-4516-b0eb-c01ca9d49ac6");
        public static readonly Guid VideoRotation = new Guid("c380465d-2271-428c-9b83-ecea3b4a85c1");
        public static readonly Guid MinimumDisplayAperture = new Guid("d7388766-18fe-48c6-a177-ee894867c8c4");
        public static readonly Guid GeometricAperture = new Guid("66758743-7e5f-400d-980a-aa8596c85696");
        public static readonly Guid TransferFunction = new Guid("5fb0fce9-be5c-4935-a811-ec838f8eed93");
        public static readonly Guid VideoPrimaries = new Guid("dbfbe4d7-0740-4ee0-8192-850ab0e21935");
        public static readonly Guid YuvMatrix = new Guid("3e23d450-2c75-4d25-a00e-b91670d12327");
        public static readonly Guid NominalRange = new Guid("c21b8ee5-b956-4071-8daf-325edf5cab11");
        public static readonly Guid Mpeg2Profile = new Guid("ad76a80b-2d5c-4e0b-b375-64e520137036");
        public static readonly Guid AudioChannels = new Guid("37e48bf5-645e-4c5b-89de-ada9e29b696a");
        public static readonly Guid AudioSamplesPerSecond = new Guid("5faeeae7-0290-4c31-9e8a-c534f68d9dba");
        public static readonly Guid AudioBitsPerSample = new Guid("f2deb57f-40fa-4764-aa33-ed4f2d1ff669");
        public static readonly Guid AudioBlockAlignment = new Guid("322de230-9eeb-43bd-ab7a-ff412251541d");
        public static readonly Guid AudioAverageBytesPerSecond = new Guid("1aab75c8-cfef-451c-ab95-ac034b8e1731");
        public static readonly Guid Duration = new Guid("6c990d33-bb8e-477a-8598-0d5d96fcd88a");                        // MF_PD_DURATION
        public static readonly Guid VideoProcessorCategory = new Guid("302ea3fc-aa5f-47f9-9f7a-c2188bb16302");          // MFT_CATEGORY_VIDEO_PROCESSOR

        public static readonly Guid Video = Numbered(0x73646976), Audio = Numbered(0x73647561);
        public static readonly Guid Rgb32 = Numbered(22), P010 = FourCc("P010"), Nv12 = FourCc("NV12");
        public static readonly Guid H264 = FourCc("H264"), Hevc = FourCc("HEVC");
        public static readonly Guid Pcm = Numbered(1), Aac = Numbered(0x1610);

        // video formats by their codes, for saying which part of a file can't be read
        static readonly Dictionary<Guid, string> names = new Dictionary<Guid, string>
        {
            { H264, "H.264" }, { Hevc, "HEVC (H.265)" }, { FourCc("AV01"), "AV1" }, { FourCc("VP90"), "VP9" }, { FourCc("VP80"), "VP8" }, { FourCc("MJPG"), "Motion JPEG" },
            { FourCc("WMV3"), "Windows Media Video" }, { FourCc("WVC1"), "VC-1" }, { FourCc("MP4V"), "MPEG-4 Part 2" }, { new Guid("e06d8026-db46-11cf-b4d1-00805f6cbbea"), "MPEG-2" },
            { FourCc("apcn"), "ProRes" }, { FourCc("apch"), "ProRes" }, { FourCc("apcs"), "ProRes" }, { FourCc("apco"), "ProRes" }, { FourCc("ap4h"), "ProRes" },
            { Aac, "AAC" }, { Numbered(0x55), "MP3" }, { Numbered(0x50), "MPEG audio" }, { Numbered(0x161), "Windows Media Audio" }, { Numbered(0x704F), "Opus" },
            { Numbered(0xF1AC), "FLAC" }, { new Guid("8D2FD10B-5841-4a6b-8905-588FEC1ADED9"), "Vorbis" }, { new Guid("e06d802c-db46-11cf-b4d1-00805f6cbbea"), "Dolby Digital (AC-3)" },
            { Pcm, "PCM" }, { Numbered(3), "PCM (float)" },
        };

        public static string Name(Guid g)
        {
            if (names.TryGetValue(g, out string n)) return n;
            byte[] b = g.ToByteArray();
            bool printable = true;
            for (int i = 0; i < 4; i++) printable &= b[i] >= 32 && b[i] <= 126;
            return printable ? System.Text.Encoding.ASCII.GetString(b, 0, 4).Trim() : "an unknown kind";
        }
    }
}
