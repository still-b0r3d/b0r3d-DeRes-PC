using System;
using System.IO;
using System.Security;
using System.Text;
using System.Threading;

namespace B0r3dDeRes
{
    // Where b0r3d DeRes keeps its settings: one folder, "b0r3dDeRes data" next to the exe, or the
    // folder given with --data. b0r3d DeRes writes nothing anywhere else: no AppData, no registry,
    // no Temp. In the folder it only ever touches names of its own (b0r3dDeRes.ini and the
    // "b0r3dDeRes drag" folder), so --data can point at a folder that holds other things.
    static class DataFolder
    {
        public const string DefaultName = "b0r3dDeRes data";

        public static string Path { get; private set; }

        // why settings can't be kept here at all (a temporary or read-only place), or null
        public static string WhyNotKept { get; private set; }

        // why the last save failed although settings can be kept (a file held for a moment by a
        // sync app or antivirus); the next save tries again
        public static string LastProblem { get; private set; }

        public static bool CanKeep => WhyNotKept == null;

        public static string ExeFolder => System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(typeof(DataFolder).Assembly.Location));

        // `exeFolder` is for the tests; the program always uses its own folder
        public static void Choose(string fromCommandLine, string exeFolder = null)
        {
            exeFolder = exeFolder ?? ExeFolder;
            WhyNotKept = null;
            LastProblem = null;
            if (!string.IsNullOrWhiteSpace(fromCommandLine))
            {
                try
                {
                    string asked = Environment.ExpandEnvironmentVariables(fromCommandLine.Trim().Trim('"'));
                    Path = System.IO.Path.GetFullPath(System.IO.Path.Combine(exeFolder, asked));
                }
                catch (Exception e) when (e is ArgumentException || e is NotSupportedException || e is PathTooLongException || e is SecurityException)
                {
                    Path = System.IO.Path.Combine(exeFolder, DefaultName);
                    WhyNotKept = "The folder given with --data (" + fromCommandLine + ") isn't a usable folder, so settings aren't kept.";
                }
                return;
            }
            Path = System.IO.Path.Combine(exeFolder, DefaultName);
            if (IsTemporary(exeFolder))
                WhyNotKept = "b0r3d DeRes is running from a temporary place (a zip, an email attachment or a download folder inside AppData), so settings aren't kept. Move b0r3dDeRes.exe into a folder of your own.";
        }

        // Is the folder inside Windows' Temp folder or AppData? Opening an exe inside a zip runs a
        // copy from Temp, and email and chat programs keep attachments in caches inside AppData.
        internal static bool IsTemporary(string folder)
        {
            try
            {
                string here = WithSlash(folder);
                foreach (string place in new[] { System.IO.Path.GetTempPath(), Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) })
                    if (!string.IsNullOrEmpty(place) && here.StartsWith(WithSlash(place), StringComparison.OrdinalIgnoreCase)) return true;
                return false;
            }
            catch (Exception e) when (e is ArgumentException || e is NotSupportedException || e is PathTooLongException || e is SecurityException)
            {
                return false;
            }
        }

        static string WithSlash(string folder)
        {
            return System.IO.Path.GetFullPath(folder).TrimEnd('\\') + "\\";
        }

        // At start: makes the folder and checks it can be written to, so a read-only place says so
        // at once instead of losing the settings when the program closes.
        public static void CheckWritable()
        {
            if (!CanKeep) return;
            string probe = System.IO.Path.Combine(Path, "b0r3dDeRes.ini." + Guid.NewGuid().ToString("N").Substring(0, 8) + ".probe");
            try
            {
                Directory.CreateDirectory(Path);
                File.WriteAllBytes(probe, new byte[0]);
                File.Delete(probe);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is NotSupportedException || e is SecurityException)
            {
                WhyNotKept = "Settings can't be kept in " + Path + " (" + e.Message.TrimEnd('.') + "). Move b0r3dDeRes.exe into a folder you can save in, or start it with --data <folder>.";
            }
        }

        // Writes a file in the data folder, the whole file or nothing, trying three times in case a
        // sync app or antivirus has it for a moment. False if it couldn't (LastProblem says why).
        public static bool TryWrite(string name, string text)
        {
            if (!CanKeep) return false;
            byte[] bytes = new UTF8Encoding(false).GetBytes(text);
            Exception last = null;
            for (int attempt = 0; attempt < 3; attempt++)
            {
                if (attempt > 0) Thread.Sleep(100);
                try
                {
                    Directory.CreateDirectory(Path);
                    Exports.Write(System.IO.Path.Combine(Path, name), bytes);
                    LastProblem = null;
                    return true;
                }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is NotSupportedException || e is SecurityException)
                {
                    last = e;
                }
            }
            LastProblem = "Settings couldn't be saved just now: " + last.Message;
            return false;
        }

        // for the tests: start over with settings allowed again
        internal static void Reset(string path)
        {
            Path = path;
            WhyNotKept = null;
            LastProblem = null;
        }
    }
}
