using System;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media.Imaging;

namespace B0r3dDeRes
{
    // Getting a picture out of the program: files, the clipboard and drag and drop.
    static class Exports
    {
        // Writes the whole file or nothing: into a temporary file of its own beside it first (a
        // name nobody else uses), then into place. With `elsewhere`, a file at `path` by then (one
        // that came while this one was made) is never written over: this one goes where
        // `elsewhere` says instead. With `readBack`, `write` can read what it wrote (to check it
        // before the file takes its name). The path it went to.
        public static void Write(string path, byte[] bytes) => Write(path, stream => stream.Write(bytes, 0, bytes.Length));

        public static string Write(string path, Action<Stream> write, Func<string> elsewhere = null, bool readBack = false)
        {
            string full = Path.GetFullPath(path);
            string tmp = Path.Combine(Path.GetDirectoryName(full), "." + Path.GetFileName(full) + "." + Guid.NewGuid().ToString("N").Substring(0, 8) + ".tmp");
            try
            {
                using (var stream = new FileStream(tmp, FileMode.CreateNew, readBack ? FileAccess.ReadWrite : FileAccess.Write, FileShare.None, 1 << 16)) write(stream);
                if (elsewhere != null)
                    while (true)
                        try
                        {
                            File.Move(tmp, full);
                            return full;
                        }
                        catch (IOException) when (File.Exists(full))
                        {
                            full = Path.GetFullPath(elsewhere());
                        }
                if (!File.Exists(full))
                {
                    File.Move(tmp, full);
                    return full;
                }
                // Over a file that's there (the Save dialog asked): it's moved aside, this one into
                // its place, then it's deleted; put back if this one can't go there. Not Windows'
                // ReplaceFile, which carries the old file's own hidden streams onto the new one (a
                // download's web address among them: no clean copy may take that on).
                string aside = Path.Combine(Path.GetDirectoryName(full), "." + Path.GetFileName(full) + "." + Guid.NewGuid().ToString("N").Substring(0, 8) + ".old");
                File.Move(full, aside);
                try
                {
                    File.Move(tmp, full);
                }
                catch
                {
                    try { File.Move(aside, full); } catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { }
                    throw;
                }
                try
                {
                    File.SetAttributes(aside, FileAttributes.Normal);
                    File.Delete(aside);
                }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
                {
                    // (something else has it open: it stays beside the new one, its name starting with a dot)
                }
                return full;
            }
            catch
            {
                try { File.Delete(tmp); } catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { }
                throw;
            }
        }

        // What Copy and drag hand to other programs: the picture (for Word, Paint and email, when
        // it isn't too big to hand over that way), the PNG file's bytes (for programs that take
        // PNG), and when dragging, a PNG file to drop into folders. It's never sent to Windows'
        // cloud clipboard: pictures stay on this PC.
        public static DataObject DataFor(BitmapSource picture, byte[] png, string fileName, bool withFile)
        {
            var data = new DataObject();
            if (picture != null) data.SetImage(picture);
            data.SetData("PNG", new MemoryStream(png), false);
            KeepOffTheCloud(data);
            if (withFile)
            {
                string file = DragFile(png, fileName);
                if (file != null) data.SetFileDropList(new StringCollection { file });
            }
            return data;
        }

        // Windows' cloud clipboard leaves alone what says it can't be uploaded.
        public static void KeepOffTheCloud(IDataObject data)
        {
            data.SetData("CanUploadToCloudClipboard", new MemoryStream(BitConverter.GetBytes(0)), false);
        }

        // Dragged PNGs wait in a folder of b0r3d DeRes's own inside the data folder until it closes.
        public const string DragFolderName = "b0r3dDeRes drag";

        public static string DragFolder => Path.Combine(DataFolder.Path, DragFolderName);

        static string DragFile(byte[] png, string fileName)
        {
            if (!DataFolder.CanKeep) return null;
            try
            {
                Directory.CreateDirectory(DragFolder);
                string path = Path.Combine(DragFolder, fileName);
                File.WriteAllBytes(path, png);
                return path;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is NotSupportedException)
            {
                return null;
            }
        }

        // Empties the drag folder (on start and on close): only the PNG files in it, then the
        // folder itself once it's empty. Never anything else, never a whole tree.
        public static void ClearDragFolder()
        {
            try
            {
                if (!Directory.Exists(DragFolder)) return;
                foreach (string file in Directory.GetFiles(DragFolder).Where(f => string.Equals(Path.GetExtension(f), ".png", StringComparison.OrdinalIgnoreCase)))
                {
                    try { File.Delete(file); }
                    catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { }  // still being dropped somewhere; next time
                }
                if (!Directory.EnumerateFileSystemEntries(DragFolder).Any()) Directory.Delete(DragFolder, false);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
            }
        }
    }
}
