using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace B0r3dDeRes.Tests
{
    // Tests of the finished one-file exe in bin\: what Windows and a user see.
    static partial class TestMain
    {
        static void NeedTheExe()
        {
            if (!File.Exists(OneExe)) throw new SkipException("bin\\b0r3dDeRes.exe isn't built (run build.cmd)");
        }

        static void ExeVersion()
        {
            NeedTheExe();
            FileVersionInfo info = FileVersionInfo.GetVersionInfo(OneExe);
            Check(info.ProductVersion == Program.FullVersion, "the exe says " + info.ProductVersion + ", this build says " + Program.FullVersion);
            Check(Regex.IsMatch(Program.Version, @"^\d+\.\d+(\.\d+)?$"), "the version " + Program.Version + " isn't like 0.1 or 0.1.1");
            Check(Program.Build == "dev" || Regex.IsMatch(Program.Build, "^[0-9a-f]{7}$"), "the build stamp " + Program.Build + " is neither dev nor a short commit");
            // the version and the build apart, never "0.3+abc1234" (owner, 2026-11-01)
            Check(info.ProductVersion == Program.Version + " build " + Program.Build && Program.VersionText == "v" + Program.Version + " · Build: " + Program.Build && !Program.FullVersion.Contains("+"),
                "the version and the build should be shown apart: " + info.ProductVersion + " / " + Program.VersionText);
            string[] parts = Program.Version.Split('.');
            string fileVersion = string.Join(".", parts.Concat(Enumerable.Repeat("0", 4 - parts.Length)));
            Check(info.FileVersion == fileVersion, "the file version " + info.FileVersion + " should be " + fileVersion);
            Check(info.ProductName == "b0r3d DeRes" && info.FileDescription == "b0r3d DeRes", "the product and description: " + info.ProductName + " / " + info.FileDescription);
            Check(info.CompanyName == "b0r3d.org" && (info.LegalCopyright ?? "").Contains("b0r3d.org"), "the company and copyright: " + info.CompanyName + " / " + info.LegalCopyright);
        }

        // runs a copy of the exe alone in a folder with --selftest (no window), and reads its report
        static void ExeRunsOnItsOwn()
        {
            NeedTheExe();
            string alone = Path.Combine(Work, "alone");
            Fresh(alone);
            string exe = Path.Combine(alone, "b0r3dDeRes.exe");
            File.Copy(OneExe, exe);
            string report = Path.Combine(alone, "report.txt");
            var start = new ProcessStartInfo(exe, "--selftest \"" + report + "\"") { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = alone };
            using (Process run = Process.Start(start))
            {
                if (!run.WaitForExit(30000))
                {
                    run.Kill();
                    Check(false, "--selftest was still running after 30 seconds, so it was stopped");
                }
                Check(File.Exists(report), "--selftest wrote no report (exit code " + run.ExitCode + ")");
                Dictionary<string, string> found = File.ReadAllLines(report).Where(l => l.Contains("="))
                    .ToDictionary(l => l.Substring(0, l.IndexOf('=')), l => l.Substring(l.IndexOf('=') + 1));
                string all = string.Join(", ", found.Select(p => p.Key + "=" + p.Value));
                Check(run.ExitCode == 0 && found["result"] == "ok", "exit code " + run.ExitCode + ": " + all);
                Check(found["render"] == "ok" && found["png"] == "ok", "the picture or its PNG didn't come out right inside the exe: " + all);
                Check(found.TryGetValue("samples", out string samples) && samples == "ok", "the samples didn't open from inside the exe: " + all);
                Check(found.TryGetValue("windows", out string windows) && windows == "ok", "the windows didn't build from inside the exe: " + all);
                Check(found.TryGetValue("machines", out string machines) && machines == "ok" && found.TryGetValue("link", out string link) && link == "ok",
                    "every machine and a share link should work inside the exe: " + all);
                Check(found.TryGetValue("screens", out string screens) && screens == "ok", "every screen effect should work inside the exe: " + all);
                Check(SelfTest.RukusOverRf == Website.SelfTestSignal, "the self-test's frame over RF isn't the website's (make the test files again, and copy their hash into SelfTest.cs)");
                Check(found.TryGetValue("signal", out string signal) && signal == "ok", "the signal inside the exe should make the website's very frame: " + all);
                Check(SelfTest.RukusGif == Website.SelfTestGif, "the self-test's GIF isn't the website's (make the test files again, and copy its hash into SelfTest.cs)");
                Check(found.TryGetValue("gif", out string gif) && gif == "ok", "the GIF sample inside the exe should read as the website reads it and make the website's very GIF: " + all);
                Check(found["bits"] == "64", "the exe ran as a " + found["bits"] + "-bit program; only 64-bit does the website's sums to the last bit");
                Check(found["dpi"] == "per-monitor-v2", "the exe follows the screens' scaling as \"" + found["dpi"] + "\", not per-monitor-v2 (is the manifest in?)");
                Check(found["version"] == Program.FullVersion, "the exe says " + found["version"]);
            }
            string[] left = Directory.GetFileSystemEntries(alone).Select(Path.GetFileName).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToArray();
            Check(left.SequenceEqual(new[] { "b0r3dDeRes.exe", "report.txt" }), "the self-test left something behind: " + string.Join(", ", left));
        }

        // Reads the exe's metadata without running it, for every way it could go online: networking
        // and web types, XML that can fetch files, pictures and media loaded from addresses, calls
        // that take an address, native calls (only the Windows functions listed), COM objects (only
        // Windows' Save and Open dialogs), and web addresses in its strings and its XAML (only
        // b0r3d.org's and the website's, which open in the browser).
        static readonly string[] BannedSpaces = { "System.Net", "System.Xml", "System.Windows.Navigation", "System.Web", "System.ServiceModel", "System.Runtime.Remoting" };
        static readonly string[] BannedTypes = { "WebBrowser", "WebClient", "HttpClient", "Socket", "TcpClient", "UdpClient", "BitmapImage", "MediaElement", "MediaPlayer", "XamlReader", "XmlReader", "XmlDocument", "XmlUrlResolver" };
        static readonly Dictionary<string, string[]> AllowedNative = new Dictionary<string, string[]>
        {
            { "user32.dll", new[] { "GetThreadDpiAwarenessContext", "AreDpiAwarenessContextsEqual", "GetAwarenessFromDpiAwarenessContext",     // screen scaling
                "EnumDisplayMonitors", "GetMonitorInfo", "GetCursorPos", "SetWindowPos", "GetDC", "ReleaseDC" } },                                  // the screens, and pictures of them
            { "shcore.dll", new[] { "GetDpiForMonitor" } },                                                                                     // each screen's scaling
            { "gdi32.dll", new[] { "CreateCompatibleDC", "CreateCompatibleBitmap", "SelectObject", "BitBlt", "DeleteObject", "DeleteDC" } },  // a picture of the screen
            { "dwmapi.dll", new[] { "DwmSetWindowAttribute" } },                                                                                // title bars, no fade while stepping aside
        };
        static readonly string[] AllowedCom =
        {
            "C0B4E2F3-BA21-4773-8DBA-335EC946EB8B",  // Windows' Save dialog
            "DC1C5A9C-E88A-4DDE-A5A1-60F82A20AEF7",  // Windows' Open dialog
        };
        static readonly string[] AllowedAddresses =
        {
            "https://b0r3d.org",              // Help → b0r3d.org, opened in the browser
            "https://img.b0r3d.org",          // Help → the website, opened in the browser
            "https://img.b0r3d.org/",         // a share link's start, copied as text (never opened)
            "https://img.b0r3d.org/#s=",
            "https://github.com/valadaptive/ntsc-rs",   // the Signal's engine, opened in the browser
        };
        // an address: a scheme, then a host after the // ("https://" alone, in "Add https://", is none)
        const string Address = @"[A-Za-z][A-Za-z0-9+.\-]*://[A-Za-z0-9\[][^\s""'<>]*";
        // Calls into .NET that take an address (a Uri). Uri's own members only take one apart;
        // these two only read from inside the exe and refuse anything but a relative address.
        static readonly string[] AllowedUriTakers = { "System.Windows.Application.LoadComponent", "System.Windows.Application.GetResourceStream" };
        // XAML's own namespace names, which are names, never fetched
        const string XamlNamespaces = "http://schemas.microsoft.com/winfx/2006/xaml";

        static void ExeNeverGoesOnline()
        {
            NeedTheExe();
            using (FileStream stream = File.OpenRead(OneExe))
            using (var pe = new PEReader(stream))
            {
                MetadataReader md = pe.GetMetadataReader();
                var bad = new List<string>();
                foreach (TypeReferenceHandle handle in md.TypeReferences)
                {
                    TypeReference type = md.GetTypeReference(handle);
                    string space = md.GetString(type.Namespace), name = md.GetString(type.Name);
                    if (BannedSpaces.Any(s => space == s || space.StartsWith(s + ".", StringComparison.Ordinal)) || BannedTypes.Contains(name) || name.Contains("WebRequest") ||
                        space == "System.Windows.Controls" && name == "Frame")
                        bad.Add(space + "." + name);
                }
                foreach (AssemblyReferenceHandle handle in md.AssemblyReferences)
                {
                    string name = md.GetString(md.GetAssemblyReference(handle).Name);
                    if (BannedSpaces.Any(s => name.StartsWith(s, StringComparison.Ordinal))) bad.Add("the assembly " + name);
                }
                var typeNames = new TypeNames();
                foreach (MemberReferenceHandle handle in md.MemberReferences)
                {
                    MemberReference member = md.GetMemberReference(handle);
                    string name = md.GetString(member.Name);
                    if (name == "GetTypeFromProgID" || name == "GetTypeFromCLSID") bad.Add("COM by name (" + name + ")");
                    if (member.GetKind() != MemberReferenceKind.Method) continue;
                    string parent = ParentName(md, member.Parent, typeNames);
                    if (parent == "System.Uri" || AllowedUriTakers.Contains(parent + "." + name)) continue;
                    if (member.DecodeMethodSignature(typeNames, null).ParameterTypes.Any(p => p == "System.Uri" || p.StartsWith("System.Uri[", StringComparison.Ordinal)))
                        bad.Add("a call that takes an address (" + parent + "." + name + ")");
                }
                for (int row = 1; row <= md.GetTableRowCount(TableIndex.ModuleRef); row++)
                {
                    string dll = md.GetString(md.GetModuleReference(MetadataTokens.ModuleReferenceHandle(row)).Name).ToLowerInvariant();
                    if (!AllowedNative.ContainsKey(dll)) bad.Add("a native call into " + dll);
                }
                int nativeCalls = 0;
                foreach (MethodDefinitionHandle handle in md.MethodDefinitions)
                {
                    MethodDefinition method = md.GetMethodDefinition(handle);
                    MethodImport import = method.GetImport();
                    if (import.Module.IsNil) continue;
                    nativeCalls++;
                    string dll = md.GetString(md.GetModuleReference(import.Module).Name).ToLowerInvariant();
                    string entry = md.GetString(import.Name.IsNil ? method.Name : import.Name);
                    if (!AllowedNative.TryGetValue(dll, out string[] entries) || !entries.Contains(entry)) bad.Add("the native call " + dll + " " + entry);
                }
                Check(nativeCalls >= 4, "the scan saw only " + nativeCalls + " native calls (has the code moved?)");
                int baml = 0;
                foreach (string address in XamlAddresses(pe, md, ref baml))
                    if (!address.StartsWith(XamlNamespaces, StringComparison.Ordinal)) bad.Add("the address " + address + " in the XAML");
                Check(baml >= 6, "the scan saw only " + baml + " compiled XAML files (has the XAML moved?)");
                foreach (TypeDefinitionHandle handle in md.TypeDefinitions)
                {
                    TypeDefinition type = md.GetTypeDefinition(handle);
                    bool comClass = (type.Attributes & System.Reflection.TypeAttributes.Import) != 0 && (type.Attributes & System.Reflection.TypeAttributes.Interface) == 0;
                    if (!comClass) continue;
                    string guid = GuidOf(md, type);
                    if (guid == null || !AllowedCom.Contains(guid.ToUpperInvariant())) bad.Add("the COM object " + md.GetString(type.Name) + " {" + guid + "}");
                }
                foreach (string text in OwnStrings(pe, md))
                    foreach (Match address in Regex.Matches(text, Address))
                        if (!AllowedAddresses.Contains(address.Value)) bad.Add("the address " + address.Value);
                Check(bad.Count == 0, "the exe has " + string.Join(", ", bad.Distinct()));
                Check(md.TypeReferences.Count > 50, "the scan saw almost nothing; is this the right file?");
                Check(md.AssemblyReferences.All(h => new[] { "mscorlib", "System", "System.Core", "System.Numerics", "PresentationCore", "PresentationFramework", "WindowsBase", "System.Xaml" }
                    .Contains(md.GetString(md.GetAssemblyReference(h).Name))), "the exe needs more than .NET Framework itself: " +
                    string.Join(", ", md.AssemblyReferences.Select(h => md.GetString(md.GetAssemblyReference(h).Name))));
            }
        }

        // The strings b0r3d DeRes's own code uses (every ldstr in the methods of the B0r3dDeRes types).
        static IEnumerable<string> OwnStrings(PEReader pe, MetadataReader md)
        {
            foreach (TypeDefinitionHandle handle in md.TypeDefinitions)
            {
                TypeDefinition type = md.GetTypeDefinition(handle), outer = type;
                while (!outer.GetDeclaringType().IsNil) outer = md.GetTypeDefinition(outer.GetDeclaringType());
                if (!md.GetString(outer.Namespace).StartsWith("B0r3dDeRes", StringComparison.Ordinal)) continue;
                foreach (MethodDefinitionHandle methodHandle in type.GetMethods())
                {
                    MethodDefinition method = md.GetMethodDefinition(methodHandle);
                    if (method.RelativeVirtualAddress == 0) continue;
                    byte[] il = pe.GetMethodBody(method.RelativeVirtualAddress).GetILBytes();
                    for (int i = 0; i + 4 < il.Length; i++)
                    {
                        if (il[i] != 0x72 || il[i + 4] != 0x70) continue;  // ldstr, then a token into the strings
                        string text = null;
                        try { text = md.GetUserString(MetadataTokens.UserStringHandle(BitConverter.ToInt32(il, i + 1) & 0xFFFFFF)); }
                        catch (BadImageFormatException) { }  // bytes that only looked like an ldstr
                        if (text != null) yield return text;
                    }
                }
            }
        }

        // The web addresses in the compiled XAML (BAML) inside the exe's resources. BAML keeps its
        // strings as UTF-8, so the bytes read as text find them; an address can run on into the
        // next record's bytes, so callers compare starts, not whole strings.
        static IEnumerable<string> XamlAddresses(PEReader pe, MetadataReader md, ref int baml)
        {
            var found = new List<string>();
            DirectoryEntry resourcesDirectory = pe.PEHeaders.CorHeader.ResourcesDirectory;
            foreach (ManifestResourceHandle handle in md.ManifestResources)
            {
                ManifestResource resource = md.GetManifestResource(handle);
                if (!resource.Implementation.IsNil || !md.GetString(resource.Name).EndsWith(".g.resources", StringComparison.Ordinal)) continue;
                BlobReader blob = pe.GetSectionData(resourcesDirectory.RelativeVirtualAddress).GetReader();
                blob.Offset = (int)resource.Offset;
                byte[] bytes = blob.ReadBytes(blob.ReadInt32());
                using (var reader = new System.Resources.ResourceReader(new MemoryStream(bytes)))
                    foreach (System.Collections.DictionaryEntry entry in reader)
                    {
                        if (!((string)entry.Key).EndsWith(".baml", StringComparison.OrdinalIgnoreCase) || !(entry.Value is Stream stream)) continue;
                        baml++;
                        var copy = new MemoryStream();
                        stream.CopyTo(copy);
                        string text = System.Text.Encoding.GetEncoding(28591).GetString(copy.ToArray());
                        foreach (Match address in Regex.Matches(text, @"[A-Za-z][A-Za-z0-9+.\-]*://[!-~]*")) found.Add(address.Value);
                    }
            }
            return found;
        }

        // "System.Windows.Application" for the type a member belongs to
        static string ParentName(MetadataReader md, EntityHandle parent, TypeNames names)
        {
            switch (parent.Kind)
            {
                case HandleKind.TypeReference: return names.GetTypeFromReference(md, (TypeReferenceHandle)parent, 0);
                case HandleKind.TypeDefinition: return names.GetTypeFromDefinition(md, (TypeDefinitionHandle)parent, 0);
                case HandleKind.TypeSpecification: return md.GetTypeSpecification((TypeSpecificationHandle)parent).DecodeSignature(names, null);
                default: return parent.Kind.ToString();
            }
        }

        // Type names in a member's signature, enough to spot a Uri among its parameters (ref and
        // out ones too).
        sealed class TypeNames : ISignatureTypeProvider<string, object>
        {
            public string GetPrimitiveType(PrimitiveTypeCode code) => code.ToString();
            public string GetTypeFromDefinition(MetadataReader md, TypeDefinitionHandle handle, byte kind)
            {
                TypeDefinition type = md.GetTypeDefinition(handle);
                return md.GetString(type.Namespace) + "." + md.GetString(type.Name);
            }
            public string GetTypeFromReference(MetadataReader md, TypeReferenceHandle handle, byte kind)
            {
                TypeReference type = md.GetTypeReference(handle);
                return md.GetString(type.Namespace) + "." + md.GetString(type.Name);
            }
            public string GetTypeFromSpecification(MetadataReader md, object context, TypeSpecificationHandle handle, byte kind) =>
                md.GetTypeSpecification(handle).DecodeSignature(this, context);
            public string GetSZArrayType(string element) => element + "[]";
            public string GetArrayType(string element, ArrayShape shape) => element + "[,]";
            public string GetByReferenceType(string element) => element;
            public string GetPointerType(string element) => element + "*";
            public string GetPinnedType(string element) => element;
            public string GetModifiedType(string modifier, string unmodified, bool required) => unmodified;
            public string GetGenericInstantiation(string generic, System.Collections.Immutable.ImmutableArray<string> arguments) => generic + "<" + string.Join(",", arguments) + ">";
            public string GetGenericTypeParameter(object context, int index) => "!" + index;
            public string GetGenericMethodParameter(object context, int index) => "!!" + index;
            public string GetFunctionPointerType(MethodSignature<string> signature) => "method*";
        }

        // a type's [Guid("...")], read straight from the metadata
        static string GuidOf(MetadataReader md, TypeDefinition type)
        {
            foreach (CustomAttributeHandle handle in type.GetCustomAttributes())
            {
                CustomAttribute attribute = md.GetCustomAttribute(handle);
                if (attribute.Constructor.Kind != HandleKind.MemberReference) continue;
                MemberReference constructor = md.GetMemberReference((MemberReferenceHandle)attribute.Constructor);
                if (constructor.Parent.Kind != HandleKind.TypeReference) continue;
                if (md.GetString(md.GetTypeReference((TypeReferenceHandle)constructor.Parent).Name) != "GuidAttribute") continue;
                BlobReader value = md.GetBlobReader(attribute.Value);
                value.ReadUInt16();  // the prolog
                return value.ReadSerializedString();
            }
            return null;
        }

        // Windows' safety flags, and a 64-bit program (only 64-bit .NET does every sum the way the
        // website's JavaScript does)
        static void ExeSafetyFlags()
        {
            NeedTheExe();
            using (FileStream stream = File.OpenRead(OneExe))
            using (var pe = new PEReader(stream))
            {
                Check(pe.PEHeaders.CoffHeader.Machine == Machine.Amd64 && pe.PEHeaders.PEHeader.Magic == PEMagic.PE32Plus, "the exe isn't a 64-bit program (" + pe.PEHeaders.CoffHeader.Machine + ")");
                DllCharacteristics dll = pe.PEHeaders.PEHeader.DllCharacteristics;
                Check((dll & DllCharacteristics.DynamicBase) != 0, "address randomisation (ASLR) is off");
                Check((dll & DllCharacteristics.NxCompatible) != 0, "data execution prevention (DEP) is off");
                Check((dll & DllCharacteristics.HighEntropyVirtualAddressSpace) != 0, "high-entropy address randomisation is off");
                Check((pe.PEHeaders.CoffHeader.Characteristics & Characteristics.LargeAddressAware) != 0, "large addresses are off");
            }
        }

        // Windows' Save and Open dialogs are told not to add files to Recent items (set up, not shown)
        static void FileDialogsStayOutOfRecent()
        {
            FileDialogs.IFileSaveDialog save = FileDialogs.PrepareSave("Save the picture as a PNG", "PNG picture", ".png", "photo-nes-256x240-4x.png");
            try
            {
                save.GetOptions(out uint options);
                Check((options & FileDialogs.NotRecent) != 0, "the Save dialog would add files to Recent items (options " + options.ToString("X") + ")");
                Check((options & FileDialogs.SaveOptions) == FileDialogs.SaveOptions, "the Save dialog lost some of its options (" + options.ToString("X") + ")");
                save.GetFileName(out string name);
                Check(name == "photo-nes-256x240-4x.png", "the Save dialog's file name is " + name);
            }
            finally
            {
                Marshal.ReleaseComObject(save);
            }
            FileDialogs.IFileOpenDialog open = FileDialogs.PrepareOpen();
            try
            {
                open.GetOptions(out uint options);
                Check((options & FileDialogs.NotRecent) != 0, "the Open dialog would add to Recent items");
                Check((options & FileDialogs.OpenOptions) == FileDialogs.OpenOptions, "the Open dialog lost some of its options (" + options.ToString("X") + ")");
            }
            finally
            {
                Marshal.ReleaseComObject(open);
            }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern IntPtr LoadLibraryEx(string path, IntPtr file, uint flags);

        [DllImport("kernel32.dll")]
        static extern bool FreeLibrary(IntPtr module);

        [DllImport("kernel32.dll")]
        static extern IntPtr FindResource(IntPtr module, IntPtr name, IntPtr type);

        [DllImport("kernel32.dll")]
        static extern IntPtr LoadResource(IntPtr module, IntPtr info);

        [DllImport("kernel32.dll")]
        static extern IntPtr LockResource(IntPtr data);

        [DllImport("kernel32.dll")]
        static extern uint SizeofResource(IntPtr module, IntPtr info);

        delegate bool EnumNames(IntPtr module, IntPtr type, IntPtr name, IntPtr param);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        static extern bool EnumResourceNames(IntPtr module, IntPtr type, EnumNames callback, IntPtr param);

        [DllImport("user32.dll")]
        static extern IntPtr CreateIconFromResourceEx(byte[] bits, uint size, bool icon, uint version, int width, int height, uint flags);

        [DllImport("user32.dll")]
        static extern bool DestroyIcon(IntPtr icon);

        static readonly IntPtr RtIcon = (IntPtr)3, RtGroupIcon = (IntPtr)14;

        static byte[] ResourceBytes(IntPtr module, IntPtr name, IntPtr type)
        {
            IntPtr info = FindResource(module, name, type);
            if (info == IntPtr.Zero) return null;
            var bytes = new byte[SizeofResource(module, info)];
            Marshal.Copy(LockResource(LoadResource(module, info)), bytes, 0, bytes.Length);
            return bytes;
        }

        // The exe's own icon, as Windows reads it: every size there, small ones as bitmaps (not PNG),
        // and each one a picture of the cup rather than static.
        static void ExeIcon()
        {
            NeedTheExe();
            IntPtr module = LoadLibraryEx(OneExe, IntPtr.Zero, 0x2 | 0x20);  // as a data file, for its resources
            Check(module != IntPtr.Zero, "couldn't open the exe's resources");
            try
            {
                IntPtr group = IntPtr.Zero;
                EnumResourceNames(module, RtGroupIcon, (m, t, name, p) => { group = name; return false; }, IntPtr.Zero);
                Check(group != IntPtr.Zero, "the exe has no icon");
                byte[] dir = ResourceBytes(module, group, RtGroupIcon);
                int count = BitConverter.ToUInt16(dir, 4);
                var sizes = new List<int>();
                for (int i = 0; i < count; i++)
                {
                    int at = 6 + 14 * i;
                    int size = dir[at] == 0 ? 256 : dir[at];
                    sizes.Add(size);
                    byte[] frame = ResourceBytes(module, (IntPtr)BitConverter.ToUInt16(dir, at + 12), RtIcon);
                    Check(frame != null, size + " px is missing");
                    bool png = frame[0] == 0x89 && frame[1] == (byte)'P';
                    Check(png == (size >= 256), size + " px is stored as " + (png ? "a PNG" : "a bitmap"));
                    IntPtr icon = CreateIconFromResourceEx(frame, (uint)frame.Length, true, 0x00030000, size, size, 0);
                    Check(icon != IntPtr.Zero, "Windows couldn't read the " + size + " px picture");
                    try
                    {
                        BitmapSource picture = Imaging.CreateBitmapSourceFromHIcon(icon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                        string why = WhyNotAPicture(picture);
                        Check(why == null, size + " px: " + why);
                    }
                    finally
                    {
                        DestroyIcon(icon);
                    }
                }
                sizes.Sort();
                Check(sizes.SequenceEqual(new[] { 16, 20, 24, 32, 40, 48, 64, 256 }), "the icon's sizes are " + string.Join(", ", sizes));
            }
            finally
            {
                FreeLibrary(module);
            }
        }

        static void StaticIsCaught()
        {
            var random = new Random(12345);  // the same "random" static every run
            foreach (int size in new[] { 16, 32 })
            {
                var pixels = new byte[size * size * 4];
                random.NextBytes(pixels);
                BitmapSource noise = BitmapSource.Create(size, size, 96, 96, PixelFormats.Bgra32, null, pixels, size * 4);
                Check(WhyNotAPicture(noise) != null, size + " px of static passed as a picture");
            }
        }

        // .NET Framework draws an icon's PNG-packed pictures under 256 px as random static (LoafLock
        // saw it with b0r3d.org's rukus.ico). Static is see-through in random places, mostly half
        // see-through, and no two neighbouring pixels look alike; the cup is the opposite. Returns
        // why the picture looks like static, or null when it doesn't.
        static string WhyNotAPicture(BitmapSource picture)
        {
            var bgra = new FormatConvertedBitmap(picture, PixelFormats.Bgra32, null, 0);
            int w = bgra.PixelWidth, h = bgra.PixelHeight;
            var px = new byte[w * h * 4];
            bgra.CopyPixels(px, w * 4, 0);
            int Alpha(int x, int y) => px[(y * w + x) * 4 + 3];
            if (Alpha(0, 0) > 8 || Alpha(w - 1, 0) > 8 || Alpha(0, h - 1) > 8 || Alpha(w - 1, h - 1) > 8) return "the corners aren't see-through";
            int crisp = 0, pairs = 0;
            long diff = 0;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int i = (y * w + x) * 4;
                    if (px[i + 3] <= 8 || px[i + 3] >= 247) crisp++;
                    if (x + 1 < w && px[i + 3] >= 247 && px[i + 7] >= 247)
                    {
                        pairs++;
                        diff += Math.Abs(px[i] - px[i + 4]) + Math.Abs(px[i + 1] - px[i + 5]) + Math.Abs(px[i + 2] - px[i + 6]);
                    }
                }
            if (crisp * 10 < w * h * 6) return "only " + (100 * crisp / (w * h)) + "% of the pixels are solid or clear";
            if (pairs == 0) return "no solid pixels side by side";
            if (diff / pairs > 120) return "side-by-side pixels differ by " + (diff / pairs) + " on average";
            return null;
        }

        static void BuildCmdIsPlain()
        {
            string file = Path.Combine(RepoFolder, "build.cmd");
            if (!File.Exists(file)) throw new SkipException("not run from the repo");
            byte[] bytes = File.ReadAllBytes(file);
            Check(bytes.All(b => b < 128), "build.cmd has a character that isn't plain ASCII");
            for (int i = 0; i < bytes.Length; i++)
                if (bytes[i] == 10) Check(i > 0 && bytes[i - 1] == 13, "build.cmd has a line ending without CR (byte " + i + ")");
        }
    }
}
