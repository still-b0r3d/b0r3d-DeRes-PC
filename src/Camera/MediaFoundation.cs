using System;
using System.Runtime.InteropServices;

namespace B0r3dDeRes.Camera
{
    // The parts of Windows' Media Foundation the camera needs: listing the cameras, starting one and
    // reading its pictures (b0r3d QR's camera code). It's all inside Windows (mfplat, mf and
    // mfreadwrite), started the way the videos start it (Video.Mf.Start: without its network parts),
    // and nothing here takes an address: a camera is found by what Windows lists, and its pictures
    // come straight from it.
    static class MF
    {
        [DllImport("mfplat.dll")] public static extern int MFCreateAttributes(out IMFAttributes attributes, int size);
        [DllImport("mfplat.dll")] public static extern int MFCreateMediaType(out IMFMediaType type);
        [DllImport("mf.dll")] public static extern int MFEnumDeviceSources(IMFAttributes attributes, out IntPtr activates, out int count);
        [DllImport("mfreadwrite.dll")] public static extern int MFCreateSourceReaderFromMediaSource(IMFMediaSource source, IMFAttributes attributes, out IMFSourceReader reader);

        // the names of attributes and kinds of picture (from mfidl.h, mfapi.h and mfreadwrite.h)
        public static readonly Guid SourceType = new Guid("c60ac5fe-252a-478f-a0ef-bc8fa5f7cad3");        // MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE
        public static readonly Guid VideoCapture = new Guid("8ac3587a-4ae7-42d8-99e0-0a6013eef90f");      // ..._SOURCE_TYPE_VIDCAP_GUID
        public static readonly Guid FriendlyName = new Guid("60d0e559-52f8-4fa2-bbce-acdb34a8ec01");      // MF_DEVSOURCE_ATTRIBUTE_FRIENDLY_NAME
        public static readonly Guid SymbolicLink = new Guid("58f0aad8-22bf-4f8a-bb3d-d2c4978c6e2f");      // ..._SOURCE_TYPE_VIDCAP_SYMBOLIC_LINK
        public static readonly Guid AdvancedVideoProcessing = new Guid("0f81da2c-b537-4672-a8b2-a681b17307a3");   // MF_SOURCE_READER_ENABLE_ADVANCED_VIDEO_PROCESSING
        public static readonly Guid MajorType = new Guid("48eba18e-f8c9-4687-bf11-0a74c9f96a8f");         // MF_MT_MAJOR_TYPE
        public static readonly Guid Subtype = new Guid("f7e34c9a-42e8-4714-b74b-cb29d72c35e5");           // MF_MT_SUBTYPE
        public static readonly Guid FrameSize = new Guid("1652c33d-d6b2-4012-b834-72030849a37d");         // MF_MT_FRAME_SIZE
        public static readonly Guid FrameRate = new Guid("c459a2e8-3d2c-4e44-b132-fee5156c7bb0");         // MF_MT_FRAME_RATE
        public static readonly Guid DefaultStride = new Guid("644b4e48-1e02-4516-b0eb-c01ca9d49ac6");     // MF_MT_DEFAULT_STRIDE
        public static readonly Guid Video = new Guid("73646976-0000-0010-8000-00aa00389b71");             // MFMediaType_Video
        public static readonly Guid Rgb32 = new Guid("00000016-0000-0010-8000-00aa00389b71");             // MFVideoFormat_RGB32
        public static readonly Guid MediaSourceId = new Guid("279a808d-aec7-40c8-9c6b-a6b492c78a66");     // IMFMediaSource

        public const int FirstVideoStream = unchecked((int)0xFFFFFFFC);   // MF_SOURCE_READER_FIRST_VIDEO_STREAM

        // what ReadSample says about the stream (MF_SOURCE_READER_FLAG)
        public const int ReadError = 0x1, EndOfStream = 0x2, CurrentTypeChanged = 0x20;

        // a camera's error (MEError), and the one it's given when it's stopped (MF_E_SHUTDOWN)
        public const int ErrorEvent = 1;
        public const int ShutDown = unchecked((int)0xC00D3E85);

        // Lets go of a Media Foundation object at once, rather than when .NET gets round to it (a
        // camera's pictures held on to stop it sending more). Once for each time it was handed over:
        // the same object handed over twice (a sample's only buffer) is one wrapper, still in use.
        public static void Release(object com)
        {
            if (com != null && Marshal.IsComObject(com)) Marshal.ReleaseComObject(com);
        }
    }

    // Media Foundation's interfaces, as their vtables are: an interface that builds on another lists
    // the other's methods first. Every call returns its HRESULT ([PreserveSig]) so failures can be told
    // apart; the methods b0r3d DeRes never calls take plain pointers. (The videos' own, in Video.Mf,
    // are kept apart: these are b0r3d QR's, as its camera reads them.)

    [ComImport, Guid("2cd2d921-c447-44a7-a13c-4adabfc247e3"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMFAttributes
    {
        [PreserveSig] int GetItem([In] ref Guid key, IntPtr value);
        [PreserveSig] int GetItemType([In] ref Guid key, out int type);
        [PreserveSig] int CompareItem([In] ref Guid key, IntPtr value, out int same);
        [PreserveSig] int Compare(IMFAttributes theirs, int matchType, out int same);
        [PreserveSig] int GetUINT32([In] ref Guid key, out int value);
        [PreserveSig] int GetUINT64([In] ref Guid key, out long value);
        [PreserveSig] int GetDouble([In] ref Guid key, out double value);
        [PreserveSig] int GetGUID([In] ref Guid key, out Guid value);
        [PreserveSig] int GetStringLength([In] ref Guid key, out int length);
        [PreserveSig] int GetString([In] ref Guid key, IntPtr buffer, int size, out int length);
        [PreserveSig] int GetAllocatedString([In] ref Guid key, [MarshalAs(UnmanagedType.LPWStr)] out string value, out int length);
        [PreserveSig] int GetBlobSize([In] ref Guid key, out int size);
        [PreserveSig] int GetBlob([In] ref Guid key, IntPtr buffer, int size, out int written);
        [PreserveSig] int GetAllocatedBlob([In] ref Guid key, out IntPtr buffer, out int size);
        [PreserveSig] int GetUnknown([In] ref Guid key, [In] ref Guid iid, out IntPtr value);
        [PreserveSig] int SetItem([In] ref Guid key, IntPtr value);
        [PreserveSig] int DeleteItem([In] ref Guid key);
        [PreserveSig] int DeleteAllItems();
        [PreserveSig] int SetUINT32([In] ref Guid key, int value);
        [PreserveSig] int SetUINT64([In] ref Guid key, long value);
        [PreserveSig] int SetDouble([In] ref Guid key, double value);
        [PreserveSig] int SetGUID([In] ref Guid key, [In] ref Guid value);
        [PreserveSig] int SetString([In] ref Guid key, [MarshalAs(UnmanagedType.LPWStr)] string value);
        [PreserveSig] int SetBlob([In] ref Guid key, IntPtr buffer, int size);
        [PreserveSig] int SetUnknown([In] ref Guid key, IntPtr value);
        [PreserveSig] int LockStore();
        [PreserveSig] int UnlockStore();
        [PreserveSig] int GetCount(out int count);
        [PreserveSig] int GetItemByIndex(int index, out Guid key, IntPtr value);
        [PreserveSig] int CopyAllItems(IMFAttributes destination);
    }

    // a camera Windows lists, not started yet (IMFAttributes' 30 methods first)
    [ComImport, Guid("7fee9e9a-4a89-47a6-899c-b6a53a70fb67"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMFActivate
    {
        [PreserveSig] int GetItem([In] ref Guid key, IntPtr value);
        [PreserveSig] int GetItemType([In] ref Guid key, out int type);
        [PreserveSig] int CompareItem([In] ref Guid key, IntPtr value, out int same);
        [PreserveSig] int Compare(IMFAttributes theirs, int matchType, out int same);
        [PreserveSig] int GetUINT32([In] ref Guid key, out int value);
        [PreserveSig] int GetUINT64([In] ref Guid key, out long value);
        [PreserveSig] int GetDouble([In] ref Guid key, out double value);
        [PreserveSig] int GetGUID([In] ref Guid key, out Guid value);
        [PreserveSig] int GetStringLength([In] ref Guid key, out int length);
        [PreserveSig] int GetString([In] ref Guid key, IntPtr buffer, int size, out int length);
        [PreserveSig] int GetAllocatedString([In] ref Guid key, [MarshalAs(UnmanagedType.LPWStr)] out string value, out int length);
        [PreserveSig] int GetBlobSize([In] ref Guid key, out int size);
        [PreserveSig] int GetBlob([In] ref Guid key, IntPtr buffer, int size, out int written);
        [PreserveSig] int GetAllocatedBlob([In] ref Guid key, out IntPtr buffer, out int size);
        [PreserveSig] int GetUnknown([In] ref Guid key, [In] ref Guid iid, out IntPtr value);
        [PreserveSig] int SetItem([In] ref Guid key, IntPtr value);
        [PreserveSig] int DeleteItem([In] ref Guid key);
        [PreserveSig] int DeleteAllItems();
        [PreserveSig] int SetUINT32([In] ref Guid key, int value);
        [PreserveSig] int SetUINT64([In] ref Guid key, long value);
        [PreserveSig] int SetDouble([In] ref Guid key, double value);
        [PreserveSig] int SetGUID([In] ref Guid key, [In] ref Guid value);
        [PreserveSig] int SetString([In] ref Guid key, [MarshalAs(UnmanagedType.LPWStr)] string value);
        [PreserveSig] int SetBlob([In] ref Guid key, IntPtr buffer, int size);
        [PreserveSig] int SetUnknown([In] ref Guid key, IntPtr value);
        [PreserveSig] int LockStore();
        [PreserveSig] int UnlockStore();
        [PreserveSig] int GetCount(out int count);
        [PreserveSig] int GetItemByIndex(int index, out Guid key, IntPtr value);
        [PreserveSig] int CopyAllItems(IMFAttributes destination);
        [PreserveSig] int ActivateObject([In] ref Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object made);
        [PreserveSig] int ShutdownObject();
        [PreserveSig] int DetachObject();
    }

    // a kind of picture: its size, its rate, how it's stored (IMFAttributes' 30 methods first)
    [ComImport, Guid("44ae0fa8-ea31-4109-8d2e-4cae4997c555"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMFMediaType
    {
        [PreserveSig] int GetItem([In] ref Guid key, IntPtr value);
        [PreserveSig] int GetItemType([In] ref Guid key, out int type);
        [PreserveSig] int CompareItem([In] ref Guid key, IntPtr value, out int same);
        [PreserveSig] int Compare(IMFAttributes theirs, int matchType, out int same);
        [PreserveSig] int GetUINT32([In] ref Guid key, out int value);
        [PreserveSig] int GetUINT64([In] ref Guid key, out long value);
        [PreserveSig] int GetDouble([In] ref Guid key, out double value);
        [PreserveSig] int GetGUID([In] ref Guid key, out Guid value);
        [PreserveSig] int GetStringLength([In] ref Guid key, out int length);
        [PreserveSig] int GetString([In] ref Guid key, IntPtr buffer, int size, out int length);
        [PreserveSig] int GetAllocatedString([In] ref Guid key, [MarshalAs(UnmanagedType.LPWStr)] out string value, out int length);
        [PreserveSig] int GetBlobSize([In] ref Guid key, out int size);
        [PreserveSig] int GetBlob([In] ref Guid key, IntPtr buffer, int size, out int written);
        [PreserveSig] int GetAllocatedBlob([In] ref Guid key, out IntPtr buffer, out int size);
        [PreserveSig] int GetUnknown([In] ref Guid key, [In] ref Guid iid, out IntPtr value);
        [PreserveSig] int SetItem([In] ref Guid key, IntPtr value);
        [PreserveSig] int DeleteItem([In] ref Guid key);
        [PreserveSig] int DeleteAllItems();
        [PreserveSig] int SetUINT32([In] ref Guid key, int value);
        [PreserveSig] int SetUINT64([In] ref Guid key, long value);
        [PreserveSig] int SetDouble([In] ref Guid key, double value);
        [PreserveSig] int SetGUID([In] ref Guid key, [In] ref Guid value);
        [PreserveSig] int SetString([In] ref Guid key, [MarshalAs(UnmanagedType.LPWStr)] string value);
        [PreserveSig] int SetBlob([In] ref Guid key, IntPtr buffer, int size);
        [PreserveSig] int SetUnknown([In] ref Guid key, IntPtr value);
        [PreserveSig] int LockStore();
        [PreserveSig] int UnlockStore();
        [PreserveSig] int GetCount(out int count);
        [PreserveSig] int GetItemByIndex(int index, out Guid key, IntPtr value);
        [PreserveSig] int CopyAllItems(IMFAttributes destination);
        [PreserveSig] int GetMajorType(out Guid major);
        [PreserveSig] int IsCompressedFormat(out int compressed);
        [PreserveSig] int IsEqual(IMFMediaType other, out int flags);
        [PreserveSig] int GetRepresentation(Guid representation, out IntPtr value);
        [PreserveSig] int FreeRepresentation(Guid representation, IntPtr value);
    }

    // a started camera (IMFMediaEventGenerator's 4 methods first); only QueueEvent and Shutdown are used
    [ComImport, Guid("279a808d-aec7-40c8-9c6b-a6b492c78a66"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMFMediaSource
    {
        [PreserveSig] int GetEvent(int flags, out IntPtr mediaEvent);
        [PreserveSig] int BeginGetEvent(IntPtr callback, IntPtr state);
        [PreserveSig] int EndGetEvent(IntPtr result, out IntPtr mediaEvent);
        [PreserveSig] int QueueEvent(int type, [In] ref Guid extendedType, int status, IntPtr value);
        [PreserveSig] int GetCharacteristics(out int characteristics);
        [PreserveSig] int CreatePresentationDescriptor(out IntPtr descriptor);
        [PreserveSig] int Start(IntPtr descriptor, IntPtr timeFormat, IntPtr startPosition);
        [PreserveSig] int Stop();
        [PreserveSig] int Pause();
        [PreserveSig] int Shutdown();
    }

    // reads a started camera's pictures one at a time, turned into the kind asked for
    [ComImport, Guid("70ae66f2-c809-4e4f-8915-bdcb406b7993"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMFSourceReader
    {
        [PreserveSig] int GetStreamSelection(int stream, out int selected);
        [PreserveSig] int SetStreamSelection(int stream, int selected);
        [PreserveSig] int GetNativeMediaType(int stream, int index, out IMFMediaType type);
        [PreserveSig] int GetCurrentMediaType(int stream, out IMFMediaType type);
        [PreserveSig] int SetCurrentMediaType(int stream, IntPtr reserved, IMFMediaType type);
        [PreserveSig] int SetCurrentPosition([In] ref Guid timeFormat, IntPtr position);
        [PreserveSig] int ReadSample(int stream, int controlFlags, out int actualStream, out int streamFlags, out long timestamp, out IMFSample sample);
        [PreserveSig] int Flush(int stream);
        [PreserveSig] int GetServiceForStream(int stream, [In] ref Guid service, [In] ref Guid iid, out IntPtr value);
        [PreserveSig] int GetPresentationAttribute(int stream, [In] ref Guid key, IntPtr value);
    }

    // one picture from the camera (IMFAttributes' 30 methods first)
    [ComImport, Guid("c40a00f2-b93a-4d80-ae8c-5a1c634f58e4"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMFSample
    {
        [PreserveSig] int GetItem([In] ref Guid key, IntPtr value);
        [PreserveSig] int GetItemType([In] ref Guid key, out int type);
        [PreserveSig] int CompareItem([In] ref Guid key, IntPtr value, out int same);
        [PreserveSig] int Compare(IMFAttributes theirs, int matchType, out int same);
        [PreserveSig] int GetUINT32([In] ref Guid key, out int value);
        [PreserveSig] int GetUINT64([In] ref Guid key, out long value);
        [PreserveSig] int GetDouble([In] ref Guid key, out double value);
        [PreserveSig] int GetGUID([In] ref Guid key, out Guid value);
        [PreserveSig] int GetStringLength([In] ref Guid key, out int length);
        [PreserveSig] int GetString([In] ref Guid key, IntPtr buffer, int size, out int length);
        [PreserveSig] int GetAllocatedString([In] ref Guid key, [MarshalAs(UnmanagedType.LPWStr)] out string value, out int length);
        [PreserveSig] int GetBlobSize([In] ref Guid key, out int size);
        [PreserveSig] int GetBlob([In] ref Guid key, IntPtr buffer, int size, out int written);
        [PreserveSig] int GetAllocatedBlob([In] ref Guid key, out IntPtr buffer, out int size);
        [PreserveSig] int GetUnknown([In] ref Guid key, [In] ref Guid iid, out IntPtr value);
        [PreserveSig] int SetItem([In] ref Guid key, IntPtr value);
        [PreserveSig] int DeleteItem([In] ref Guid key);
        [PreserveSig] int DeleteAllItems();
        [PreserveSig] int SetUINT32([In] ref Guid key, int value);
        [PreserveSig] int SetUINT64([In] ref Guid key, long value);
        [PreserveSig] int SetDouble([In] ref Guid key, double value);
        [PreserveSig] int SetGUID([In] ref Guid key, [In] ref Guid value);
        [PreserveSig] int SetString([In] ref Guid key, [MarshalAs(UnmanagedType.LPWStr)] string value);
        [PreserveSig] int SetBlob([In] ref Guid key, IntPtr buffer, int size);
        [PreserveSig] int SetUnknown([In] ref Guid key, IntPtr value);
        [PreserveSig] int LockStore();
        [PreserveSig] int UnlockStore();
        [PreserveSig] int GetCount(out int count);
        [PreserveSig] int GetItemByIndex(int index, out Guid key, IntPtr value);
        [PreserveSig] int CopyAllItems(IMFAttributes destination);
        [PreserveSig] int GetSampleFlags(out int flags);
        [PreserveSig] int SetSampleFlags(int flags);
        [PreserveSig] int GetSampleTime(out long time);
        [PreserveSig] int SetSampleTime(long time);
        [PreserveSig] int GetSampleDuration(out long duration);
        [PreserveSig] int SetSampleDuration(long duration);
        [PreserveSig] int GetBufferCount(out int count);
        [PreserveSig] int GetBufferByIndex(int index, out IMFMediaBuffer buffer);
        [PreserveSig] int ConvertToContiguousBuffer(out IMFMediaBuffer buffer);
        [PreserveSig] int AddBuffer(IMFMediaBuffer buffer);
        [PreserveSig] int RemoveBufferByIndex(int index);
        [PreserveSig] int RemoveAllBuffers();
        [PreserveSig] int GetTotalLength(out int length);
        [PreserveSig] int CopyToBuffer(IMFMediaBuffer buffer);
    }

    // a picture's bytes, end to end
    [ComImport, Guid("045fa593-8799-42b8-bc8d-8968c6453507"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMFMediaBuffer
    {
        [PreserveSig] int Lock(out IntPtr bytes, out int maxLength, out int length);
        [PreserveSig] int Unlock();
        [PreserveSig] int GetCurrentLength(out int length);
        [PreserveSig] int SetCurrentLength(int length);
        [PreserveSig] int GetMaxLength(out int length);
    }

    // a picture's bytes as rows: where the top row starts and how far it is to the next (less than 0
    // when the picture is kept bottom row first)
    [ComImport, Guid("7dc9d5f9-9ed9-44ec-9bbf-0600bb589fbb"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMF2DBuffer
    {
        [PreserveSig] int Lock2D(out IntPtr scanline0, out int pitch);
        [PreserveSig] int Unlock2D();
        [PreserveSig] int GetScanline0AndPitch(out IntPtr scanline0, out int pitch);
        [PreserveSig] int IsContiguousFormat(out int contiguous);
        [PreserveSig] int GetContiguousLength(out int length);
        [PreserveSig] int ContiguousCopyTo(IntPtr destination, int size);
        [PreserveSig] int ContiguousCopyFrom(IntPtr source, int size);
    }
}
