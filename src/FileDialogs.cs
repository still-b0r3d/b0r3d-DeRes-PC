using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace B0r3dDeRes
{
    // Windows' own Save, Open and folder dialogs, called directly instead of through WPF, because
    // WPF on .NET Framework can't tell them not to add files to Windows' Recent items (kept in
    // AppData and the registry). Windows still remembers the last folder each dialog was in for
    // b0r3d DeRes; that memory is Windows' own.
    static class FileDialogs
    {
        const uint OverwritePrompt = 0x2, StrictFileTypes = 0x4, PickFolders = 0x20, ForceFileSystem = 0x40, AllowMultiSelect = 0x200,
            PathMustExist = 0x800, FileMustExist = 0x1000, NoReadOnlyReturn = 0x8000, DontAddToRecent = 0x2000000;

        // asked of every Save dialog: Windows' own "replace it?" question, the file type's
        // extension kept, a real file, never a read-only one, and not added to Recent items
        internal const uint SaveOptions = OverwritePrompt | StrictFileTypes | ForceFileSystem | PathMustExist | NoReadOnlyReturn | DontAddToRecent;
        // Open: one picture that's there, not added to Recent items
        internal const uint OpenOptions = ForceFileSystem | PathMustExist | FileMustExist | DontAddToRecent;
        internal const uint NotRecent = DontAddToRecent;
        // a folder that's there, not added to Recent items
        internal const uint FolderOptions = PickFolders | ForceFileSystem | PathMustExist | DontAddToRecent;

        const int Cancelled = unchecked((int)0x800704C7);  // the user clicked Cancel
        const uint FileSystemPath = 0x80058000;

        // the chosen file, or null when cancelled
        public static string Save(Window owner, string title, string typeName, string extension, string fileName) =>
            SaveAs(owner, title, new[] { new FileType(typeName, extension) }, 0, fileName, out int ignored);

        // A file type in a dialog's list: "PNG picture" and ".png".
        public sealed class FileType
        {
            public readonly string Name, Extension;

            public FileType(string name, string extension)
            {
                Name = name;
                Extension = extension;
            }
        }

        // The chosen file and which of the types it is (the list in the dialog, starting with
        // `chosen`), or null when cancelled. The file always ends in its type's extension.
        public static string SaveAs(Window owner, string title, IList<FileType> types, int chosen, string fileName, out int typeIndex)
        {
            typeIndex = chosen;
            IFileSaveDialog dialog = PrepareSave(title, types, chosen, fileName);
            try
            {
                int result = dialog.Show(Handle(owner));
                if (result == Cancelled) return null;
                if (result < 0) Marshal.ThrowExceptionForHR(result);
                dialog.GetFileTypeIndex(out uint picked);  // counted from 1
                dialog.GetResult(out IShellItem item);
                string path = PathOf(item);
                typeIndex = picked >= 1 && picked <= types.Count ? (int)picked - 1 : chosen;
                // the file's own extension has the last word should the two disagree (several types
                // can share one, like the text kinds' .txt, so the dialog's choice stands when it fits)
                string extension = System.IO.Path.GetExtension(path);
                if (!string.Equals(extension, types[typeIndex].Extension, StringComparison.OrdinalIgnoreCase))
                    for (int i = 0; i < types.Count; i++)
                        if (string.Equals(extension, types[i].Extension, StringComparison.OrdinalIgnoreCase))
                        {
                            typeIndex = i;
                            break;
                        }
                return path;
            }
            finally
            {
                Marshal.ReleaseComObject(dialog);
            }
        }

        // the chosen picture, or null when cancelled
        public static string OpenPicture(Window owner) => Open(owner, PrepareOpen());

        // a palette file (.hex, .pal, .gpl or a palette picture), or null when cancelled
        public static string OpenPalette(Window owner) => Open(owner, PrepareOpen("Load a palette",
            new FilterSpec { Name = "Palettes (.hex, .pal, .gpl, .png)", Spec = "*.hex;*.pal;*.gpl;*.png" },
            new FilterSpec { Name = "All files", Spec = "*.*" }));

        // a look file, or null when cancelled
        public static string OpenLook(Window owner) => Open(owner, PrepareOpen("Open a look",
            new FilterSpec { Name = "Looks (.json)", Spec = "*.json" },
            new FilterSpec { Name = "All files", Spec = "*.*" }));

        // several pictures for a batch, or null when cancelled
        public static string[] OpenPictures(Window owner)
        {
            IFileOpenDialog dialog = PrepareOpen("Pictures to DeRes",
                new FilterSpec { Name = "Pictures", Spec = Pictures.DialogPattern },
                new FilterSpec { Name = "All files", Spec = "*.*" });
            dialog.GetOptions(out uint options);
            dialog.SetOptions(options | AllowMultiSelect);
            try
            {
                int result = dialog.Show(Handle(owner));
                if (result == Cancelled) return null;
                if (result < 0) Marshal.ThrowExceptionForHR(result);
                dialog.GetResults(out IShellItemArray items);
                try
                {
                    items.GetCount(out uint count);
                    var paths = new List<string>();
                    for (uint i = 0; i < count; i++)
                    {
                        items.GetItemAt(i, out IShellItem item);
                        paths.Add(PathOf(item));
                    }
                    return paths.ToArray();
                }
                finally
                {
                    Marshal.ReleaseComObject(items);
                }
            }
            finally
            {
                Marshal.ReleaseComObject(dialog);
            }
        }

        // a folder, or null when cancelled
        public static string PickFolder(Window owner, string title) => Open(owner, PrepareFolder(title));

        internal static IFileOpenDialog PrepareFolder(string title)
        {
            var dialog = (IFileOpenDialog)new FileOpenDialogObject();
            dialog.GetOptions(out uint options);
            dialog.SetOptions(options | FolderOptions);
            dialog.SetTitle(title);
            return dialog;
        }

        static string Open(Window owner, IFileOpenDialog dialog)
        {
            try
            {
                int result = dialog.Show(Handle(owner));
                if (result == Cancelled) return null;
                if (result < 0) Marshal.ThrowExceptionForHR(result);
                dialog.GetResult(out IShellItem item);
                return PathOf(item);
            }
            finally
            {
                Marshal.ReleaseComObject(dialog);
            }
        }

        // the dialogs, set up but not shown (the tests check their options this way)
        internal static IFileSaveDialog PrepareSave(string title, string typeName, string extension, string fileName) =>
            PrepareSave(title, new[] { new FileType(typeName, extension) }, 0, fileName);

        internal static IFileSaveDialog PrepareSave(string title, IList<FileType> types, int chosen, string fileName)
        {
            var dialog = (IFileSaveDialog)new FileSaveDialogObject();
            dialog.GetOptions(out uint options);
            dialog.SetOptions(options | SaveOptions);
            dialog.SetTitle(title);
            var filters = new FilterSpec[types.Count];
            for (int i = 0; i < types.Count; i++) filters[i] = new FilterSpec { Name = types[i].Name + " (*" + types[i].Extension + ")", Spec = "*" + types[i].Extension };
            dialog.SetFileTypes((uint)filters.Length, filters);
            dialog.SetFileTypeIndex((uint)chosen + 1);
            dialog.SetDefaultExtension(types[chosen].Extension.TrimStart('.'));
            dialog.SetFileName(fileName);
            return dialog;
        }

        internal static IFileOpenDialog PrepareOpen() => PrepareOpen("Open a picture",
            new FilterSpec { Name = "Pictures", Spec = Pictures.DialogPattern },
            new FilterSpec { Name = "All files", Spec = "*.*" });

        internal static IFileOpenDialog PrepareOpen(string title, params FilterSpec[] filters)
        {
            var dialog = (IFileOpenDialog)new FileOpenDialogObject();
            dialog.GetOptions(out uint options);
            dialog.SetOptions(options | OpenOptions);
            dialog.SetTitle(title);
            dialog.SetFileTypes((uint)filters.Length, filters);
            return dialog;
        }

        static IntPtr Handle(Window owner) => owner == null ? IntPtr.Zero : new WindowInteropHelper(owner).Handle;

        static string PathOf(IShellItem item)
        {
            try
            {
                item.GetDisplayName(FileSystemPath, out IntPtr name);
                try { return Marshal.PtrToStringUni(name); }
                finally { Marshal.FreeCoTaskMem(name); }
            }
            finally
            {
                Marshal.ReleaseComObject(item);
            }
        }

        [ComImport, Guid("C0B4E2F3-BA21-4773-8DBA-335EC946EB8B")]
        class FileSaveDialogObject
        {
        }

        [ComImport, Guid("DC1C5A9C-E88A-4DDE-A5A1-60F82A20AEF7")]
        class FileOpenDialogObject
        {
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct FilterSpec
        {
            [MarshalAs(UnmanagedType.LPWStr)] public string Name;
            [MarshalAs(UnmanagedType.LPWStr)] public string Spec;
        }

        // IFileSaveDialog, with IModalWindow's and IFileDialog's methods first, in Windows' order
        [ComImport, Guid("84BCCD23-5FDE-4CDB-AEA4-AF64B83D78AB"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        internal interface IFileSaveDialog
        {
            [PreserveSig] int Show(IntPtr parent);
            void SetFileTypes(uint count, [MarshalAs(UnmanagedType.LPArray)] FilterSpec[] filters);
            void SetFileTypeIndex(uint index);
            void GetFileTypeIndex(out uint index);
            void Advise(IntPtr events, out uint cookie);
            void Unadvise(uint cookie);
            void SetOptions(uint options);
            void GetOptions(out uint options);
            void SetDefaultFolder(IShellItem item);
            void SetFolder(IShellItem item);
            void GetFolder(out IShellItem item);
            void GetCurrentSelection(out IShellItem item);
            void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string name);
            void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string name);
            void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string title);
            void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string text);
            void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string label);
            void GetResult(out IShellItem item);
            void AddPlace(IShellItem item, int place);
            void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string extension);
            void Close(int result);
            void SetClientGuid(ref Guid guid);
            void ClearClientData();
            void SetFilter(IntPtr filter);
            void SetSaveAsItem(IShellItem item);
            void SetProperties(IntPtr store);
            void SetCollectedProperties(IntPtr list, int appendDefault);
            void GetProperties(out IntPtr store);
            void ApplyProperties(IShellItem item, IntPtr store, IntPtr window, IntPtr sink);
        }

        // IFileOpenDialog: the same IModalWindow and IFileDialog methods, then its own two
        [ComImport, Guid("D57C7288-D4AD-4768-BE02-9D969532D960"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        internal interface IFileOpenDialog
        {
            [PreserveSig] int Show(IntPtr parent);
            void SetFileTypes(uint count, [MarshalAs(UnmanagedType.LPArray)] FilterSpec[] filters);
            void SetFileTypeIndex(uint index);
            void GetFileTypeIndex(out uint index);
            void Advise(IntPtr events, out uint cookie);
            void Unadvise(uint cookie);
            void SetOptions(uint options);
            void GetOptions(out uint options);
            void SetDefaultFolder(IShellItem item);
            void SetFolder(IShellItem item);
            void GetFolder(out IShellItem item);
            void GetCurrentSelection(out IShellItem item);
            void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string name);
            void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string name);
            void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string title);
            void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string text);
            void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string label);
            void GetResult(out IShellItem item);
            void AddPlace(IShellItem item, int place);
            void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string extension);
            void Close(int result);
            void SetClientGuid(ref Guid guid);
            void ClearClientData();
            void SetFilter(IntPtr filter);
            void GetResults(out IShellItemArray items);
            void GetSelectedItems(out IShellItemArray items);
        }

        [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        internal interface IShellItem
        {
            void BindToHandler(IntPtr context, ref Guid handler, ref Guid interfaceId, out IntPtr result);
            void GetParent(out IShellItem parent);
            void GetDisplayName(uint form, out IntPtr name);
            void GetAttributes(uint mask, out uint attributes);
            void Compare(IShellItem other, uint hint, out int order);
        }

        [ComImport, Guid("B63EA76D-1F85-456F-A19C-48159EFA858B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        internal interface IShellItemArray
        {
            void BindToHandler(IntPtr context, ref Guid handler, ref Guid interfaceId, out IntPtr result);
            void GetPropertyStore(int flags, ref Guid interfaceId, out IntPtr result);
            void GetPropertyDescriptionList(IntPtr keyType, ref Guid interfaceId, out IntPtr result);
            void GetAttributes(int how, uint mask, out uint attributes);
            void GetCount(out uint count);
            void GetItemAt(uint index, out IShellItem item);
            void EnumItems(out IntPtr items);
        }
    }
}
