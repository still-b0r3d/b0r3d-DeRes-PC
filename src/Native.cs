using System;
using System.Runtime.InteropServices;

namespace B0r3dDeRes
{
    // The few Windows calls WPF doesn't wrap.
    static class Native
    {
        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        // a dark or light title bar (attribute 20 since Windows 10 2004, 19 before that)
        // Windows' fade when a window shows or hides, off while b0r3d DeRes steps aside for a
        // picture of the screen (so it's gone at once) and on again after
        public static void Transitions(System.Windows.Window window, bool on)
        {
            IntPtr hwnd = new System.Windows.Interop.WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return;
            int disabled = on ? 0 : 1;
            try
            {
                DwmSetWindowAttribute(hwnd, 3, ref disabled, sizeof(int));  // DWMWA_TRANSITIONS_FORCEDISABLED
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
        }

        public static void DarkTitleBar(IntPtr hwnd, bool dark)
        {
            if (hwnd == IntPtr.Zero) return;
            int on = dark ? 1 : 0;
            try
            {
                if (DwmSetWindowAttribute(hwnd, 20, ref on, sizeof(int)) != 0) DwmSetWindowAttribute(hwnd, 19, ref on, sizeof(int));
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
        }

        [DllImport("user32.dll")]
        static extern IntPtr GetThreadDpiAwarenessContext();

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool AreDpiAwarenessContextsEqual(IntPtr a, IntPtr b);

        [DllImport("user32.dll")]
        static extern int GetAwarenessFromDpiAwarenessContext(IntPtr context);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool GetFileInformationByHandle(Microsoft.Win32.SafeHandles.SafeFileHandle file, out FileInformation information);

        // (BY_HANDLE_FILE_INFORMATION: its times are FILETIMEs, two DWORDs each, so 4-byte packing)
        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        struct FileInformation
        {
            public uint Attributes;
            public long Created, Accessed, Written;
            public uint Volume, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
        }

        // Which file a path is: its volume's serial number and its number on that volume (the same
        // for every spelling of its path: a network drive's, a junction's, a hard link's), or null
        // when it can't be opened.
        public static (uint Volume, ulong Index)? FileIdentity(string path)
        {
            try
            {
                using (var file = new System.IO.FileStream(path, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.ReadWrite | System.IO.FileShare.Delete, 1))
                    if (GetFileInformationByHandle(file.SafeFileHandle, out FileInformation info)) return (info.Volume, ((ulong)info.IndexHigh << 32) | info.IndexLow);
            }
            catch (Exception x) when (x is System.IO.IOException || x is UnauthorizedAccessException || x is ArgumentException || x is NotSupportedException || x is System.Security.SecurityException) { }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
            return null;
        }

        static readonly IntPtr PerMonitorV2 = new IntPtr(-4);

        // how this thread follows the screens' scaling: "per-monitor-v2" when the manifest did its job
        public static string DpiAwareness()
        {
            try
            {
                IntPtr context = GetThreadDpiAwarenessContext();
                if (AreDpiAwarenessContextsEqual(context, PerMonitorV2)) return "per-monitor-v2";
                switch (GetAwarenessFromDpiAwarenessContext(context))
                {
                    case 0: return "unaware";
                    case 1: return "system";
                    case 2: return "per-monitor";
                    default: return "unknown";
                }
            }
            catch (EntryPointNotFoundException)
            {
                return "unknown";
            }
        }
    }
}
