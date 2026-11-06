using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using B0r3dDeRes.Engine;
using B0r3dDeRes.Exif;
using ImageMetadata = B0r3dDeRes.Exif.ImageMetadata;
using B0r3dDeRes.Video;

namespace B0r3dDeRes
{
    // a file in the EXIF tools: the one open (the last one opened, in any tab), or a batch's row
    // being looked at, read whole
    sealed class ExifItem
    {
        public string Name, Path;     // Path: where it is (null: a sample, a pasted picture, the screen)
        public string Original;       // the file it came from first (an edited photo's original), never written over
        public ExifRow Row;           // the batch's row it is, if it's one
        public byte[] Bytes;          // a photo's (a video's are read from its file as it's cleaned)
        public bool Video;
        public long Size;
        public ImageMetadata Meta;
        public BitmapSource Preview;
        public bool PreviewWanted = true;
    }

    // what reading a batch's row found (worked out on a worker, put on the row by the window)
    sealed class RowFacts
    {
        public long Bytes, TrailerBytes;
        public string FormatId = "unknown", Problem;
        public bool HasGps;
    }

    // The EXIF Scrubber's and EXIF Injector's tabs (the website's ui/scrub-controls.ts,
    // inject-controls.ts and their part of app.ts): the inspector on the right of either, the
    // scrubber's options, a clean copy of the file open, a batch of any number of files and
    // folders cleaned into a folder or a ZIP (the PC's), and the injector's form. Videos too (MP4
    // and MOV: where they were filmed, and on what, taken out byte by byte). The originals are
    // never changed.
    public partial class MainWindow
    {
        internal bool ScrubActive => Tabs.SelectedItem == ScrubTab;
        internal bool InjectActive => Tabs.SelectedItem == InjectTab;
        internal bool ExifActive => ScrubActive || InjectActive;

        ExifStage exifStage;
        ExifItem exifFile;
        readonly ObservableCollection<ExifRow> exifBatch = new ObservableCollection<ExifRow>();
        int batchIndex;
        ExifItem batchShown;
        object stageShowing;           // what the inspector shows (it's drawn again only when that changes)
        CancellationTokenSource exifCancel;
        bool exifBusy, closeAfterExif;
        string batchProgress;
        int exifTicket, rowTicket;
        bool readingBatch;

        // for the tests
        internal ExifStage Inspector => exifStage;
        internal ExifItem ExifFile => exifFile;
        internal IList<ExifRow> ExifBatch => exifBatch;
        internal int BatchIndex => batchIndex;
        internal ExifItem ExifRowShown => batchShown;
        internal bool ExifBusy => exifBusy;
        internal bool ExifReading => readingBatch;
        internal Task ExifRunning { get; private set; } = Task.CompletedTask;

        void BuildExif()
        {
            exifStage = new ExifStage { OnStatus = text => ExifSay(text) };
            exifStage.RowPicked = PickRow;
            foreach (TextBlock line in new[] { ScrubSampleLine, InjectSampleLine })
            {
                line.Inlines.Add(new Run("Try the sample: "));
                var link = new Hyperlink(new Run("Rukus")) { Tag = "rukus" };
                link.Click += (s, e) => OpenSample((string)((Hyperlink)s).Tag);
                line.Inlines.Add(link);
            }
            foreach (var (tag, text) in new[] { ("", "Not set"), ("1", "Standard (1)"), ("2", "Mirror Horizontal (2)"), ("3", "Rotate 180° (3)"), ("4", "Mirror Vertical (4)"),
                ("5", "Mirror Horiz & Rotate 270° CW (5)"), ("6", "Rotate 90° CW (6)"), ("7", "Mirror Horiz & Rotate 90° CW (7)"), ("8", "Rotate 90° CCW (8)") })
                InjectOrientation.Items.Add(new ComboBoxItem { Content = text, Tag = tag });
            InjectOrientation.SelectedIndex = 0;
            InjectPreset.Items.Add(new ComboBoxItem { Content = "Pick a device…", Tag = "" });
            foreach (string group in DevicePresets.All.Select(p => p.Group).Distinct())
            {
                InjectPreset.Items.Add(new ComboBoxItem { Content = group, IsEnabled = false, FontWeight = FontWeights.SemiBold, Tag = "" });
                foreach (DevicePreset p in DevicePresets.All.Where(p => p.Group == group)) InjectPreset.Items.Add(new ComboBoxItem { Content = "    " + p.Label, Tag = p.Id });
            }
            InjectPreset.SelectedIndex = 0;
            foreach (Control c in InjectFields.Values)
                if (c is TextBox box) box.TextChanged += (s, e) => Valid((TextBox)s);
            PlaceExifStage();
            SyncExif();
        }

        // the inspector into the tab showing (the other one has none)
        void PlaceExifStage()
        {
            Border slot = InjectActive ? InjectStageSlot : ScrubStageSlot;
            if (slot.Child == exifStage) return;
            if (exifStage.Parent is Border old) old.Child = null;
            slot.Child = exifStage;
        }

        // the tab opening: whatever plays stops, the inspector comes over
        void ExifTabShown()
        {
            if (player.Playing) player.Pause();
            videoPlayer.Pause();
            if (asciiPlayer.Playing) asciiPlayer.Pause();
            asciiVideoPlayer.Pause();
            modem.HangUp();
            if (fullScreen) SetFullScreen(false);
            PlaceExifStage();
            SyncExif();
        }

        // ---- the file open ----

        // the picture just opened is the EXIF tools' file too (a single file replaces a batch, as on
        // the website, unless a batch is being cleaned)
        async void SetExifFrom(LoadedPicture p)
        {
            int ticket = ++exifTicket;
            if (!exifBusy) ClearBatchRows();
            ExifItem item;
            try
            {
                item = await Task.Run(() => ExifOf(p));
            }
            catch (OutOfMemoryException)
            {
                item = null;
            }
            if (ticket != exifTicket) return;
            exifFile = item;
            stageShowing = null;
            SyncExif();
        }

        static ExifItem ExifOf(LoadedPicture p)
        {
            var item = new ExifItem { Name = p.Name, Path = p.FilePath, Original = p.FilePath, Video = p.Video != null };
            if (item.Video)
            {
                item.Size = new FileInfo(p.FilePath).Length;
                item.Meta = VideoMetaOf(p.FilePath);
            }
            else
            {
                // a pasted picture or the screen: the PNG a browser would hand over (it says nothing)
                item.Bytes = p.FileBytes ?? PngOf(p.Source);
                item.Size = item.Bytes.Length;
                item.Meta = InspectPhoto(item.Bytes);
            }
            item.Preview = PreviewOf(p.Video != null ? p.Video.Source : p.Source);
            return item;
        }

        // a damaged file the browser's inspector would stop on reads as one it can't read
        static ImageMetadata InspectPhoto(byte[] bytes)
        {
            try
            {
                return ExifReader.Inspect(bytes);
            }
            catch (ExifRangeException)
            {
                return new ImageMetadata { Format = "unknown", AiDetection = AiMarkers.Detect(new List<string>(), null, null) };
            }
        }

        // a video's metadata: MP4 and MOV are read; other kinds of video aren't
        static ImageMetadata VideoMetaOf(string path)
        {
            try
            {
                return VideoMeta.Inspect(path);
            }
            catch (Exception x) when (x is IOException || x is UnauthorizedAccessException || x is ArgumentException || x is NotSupportedException)
            {
                return new ImageMetadata { Format = "unknown", Video = new VideoDetails(), AiDetection = AiMarkers.Detect(new List<string>(), null, null) };
            }
        }

        static byte[] PngOf(PictureSource s)
        {
            var bitmap = BitmapSource.Create(s.Width, s.Height, 96, 96, PixelFormats.Bgra32, null, s.Bgra, s.Width * 4);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            var bytes = new MemoryStream();
            encoder.Save(bytes);
            return bytes.ToArray();
        }

        // a picture small enough to show beside the details: whole blocks averaged, at most 800 px on its long side
        internal static BitmapSource PreviewOf(PictureSource s)
        {
            if (s == null || s.Width <= 0 || s.Height <= 0) return null;
            int k = Math.Max(1, (int)Math.Ceiling(Math.Max(s.Width, s.Height) / 800.0));
            int w = Math.Max(1, s.Width / k), h = Math.Max(1, s.Height / k);
            var bgra = new byte[w * h * 4];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int b = 0, g = 0, r = 0, a = 0;
                    for (int dy = 0; dy < k; dy++)
                    {
                        long row = ((long)(y * k + dy) * s.Width + x * k) * 4;
                        for (int dx = 0; dx < k; dx++, row += 4)
                        {
                            b += s.Bgra[row];
                            g += s.Bgra[row + 1];
                            r += s.Bgra[row + 2];
                            a += s.Bgra[row + 3];
                        }
                    }
                    int n = k * k, o = (y * w + x) * 4;
                    bgra[o] = (byte)(b / n);
                    bgra[o + 1] = (byte)(g / n);
                    bgra[o + 2] = (byte)(r / n);
                    bgra[o + 3] = (byte)(a / n);
                }
            BitmapSource preview = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, bgra, w * 4);
            preview.Freeze();
            return preview;
        }

        // a picture Windows couldn't open: the inspector still reads what it can (HEIC without its codec, a damaged PNG)
        void SetExifFromFailed(string name, byte[] bytes, string path)
        {
            ImageMetadata meta = InspectPhoto(bytes);
            if (meta.Format == "unknown") return;
            if (!exifBusy) ClearBatchRows();
            exifTicket++;
            exifFile = new ExifItem { Name = name, Path = path, Original = path, Bytes = bytes, Size = bytes.Length, Meta = meta };
            stageShowing = null;
            SyncExif();
        }

        // ---- the batch ----

        void ClearBatchRows()
        {
            exifBatch.Clear();
            batchShown = null;
            batchIndex = 0;
            batchProgress = null;
            rowTicket++;
            rowLoading = false;
        }

        void OnExifAddFiles(object sender, RoutedEventArgs e)
        {
            string[] chosen;
            try
            {
                chosen = FileDialogs.OpenPictures(this);
            }
            catch (COMException x)
            {
                MessageBox.Show(this, "Windows' Open dialog didn't open:\n\n" + x.Message, Program.Name, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (chosen != null) AddToBatch(chosen);
        }

        void OnExifAddFolder(object sender, RoutedEventArgs e)
        {
            string folder;
            try
            {
                folder = FileDialogs.PickFolder(this, "A folder of photos or videos to clean");
            }
            catch (COMException x)
            {
                MessageBox.Show(this, "Windows' folder dialog didn't open:\n\n" + x.Message, Program.Name, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (folder != null) AddToBatch(new[] { folder });
        }

        void OnExifClearBatch(object sender, RoutedEventArgs e) => ClearExifBatch();

        internal void ClearExifBatch()
        {
            if (exifBusy) return;
            ClearBatchRows();
            stageShowing = null;
            ScrubSay("");
            SyncExif();
        }

        static bool ExifTakes(string path) => Pictures.LooksLikePicture(path) || VideoFile.LooksLikeVideo(path);

        // Files and folders into the batch: a folder gives its own pictures and videos (not those in
        // folders inside it, nor online-only ones), in name order, as the DeRes batch does. One
        // already there isn't added again. They're read on a worker, row by row.
        internal void AddToBatch(IEnumerable<string> paths)
        {
            if (exifBusy)
            {
                ExifSay("Wait for the batch to finish (or Stop it) before adding more.");
                return;
            }
            var known = new HashSet<string>(exifBatch.Select(r => r.Path), StringComparer.OrdinalIgnoreCase);
            int before = exifBatch.Count, online = 0;
            var notes = new List<string>();
            foreach (string path in paths)
            {
                List<string> found;
                try
                {
                    if (Directory.Exists(path))
                    {
                        found = new List<string>();
                        foreach (string f in Directory.EnumerateFiles(path).Where(ExifTakes).OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase))
                        {
                            if (Pictures.OnlyOnline(f)) online++;
                            else found.Add(f);
                        }
                    }
                    else found = File.Exists(path) ? new List<string> { path } : new List<string>();
                }
                catch (Exception x) when (x is IOException || x is UnauthorizedAccessException || x is ArgumentException || x is NotSupportedException || x is System.Security.SecurityException)
                {
                    notes.Add("Couldn't look in " + Pictures.NameOf(path) + ": " + x.Message.TrimEnd('.') + ".");
                    continue;
                }
                foreach (string file in found)
                {
                    string full = Path.GetFullPath(file);
                    if (known.Add(full)) exifBatch.Add(new ExifRow { Path = full, Original = full, Name = Path.GetFileName(full), IsVideo = VideoFile.LooksLikeVideo(full) });
                }
            }
            if (online > 0) notes.Add("Left out " + online + " file" + (online == 1 ? "" : "s") + " kept only online (OneDrive and the like): in Explorer, Always keep on this device brings " + (online == 1 ? "it" : "them") + " in.");
            int added = exifBatch.Count - before;
            if (added == 0 && notes.Count == 0) notes.Add("No new photos or videos in that.");
            ExifSay(string.Join(" ", notes));
            if (before == 0 && exifBatch.Count > 0) PickRow(0);
            ReadBatch();
            SyncExif();
        }

        // every row not read yet, on a worker, one at a time (only what the table needs is kept;
        // a row whose file changed meanwhile, an injected copy, keeps what was found for that)
        async void ReadBatch()
        {
            if (readingBatch) return;
            readingBatch = true;
            try
            {
                while (true)
                {
                    ExifRow row = exifBatch.FirstOrDefault(r => !r.Read);
                    if (row == null) break;
                    string path = row.Path;
                    bool video = row.IsVideo;
                    RowFacts facts = await Task.Run(() => ReadFacts(path, video));
                    if (row.Path == path) Apply(row, facts);
                    row.Changed();
                    exifStage.SyncBatchSummary(exifBatch);
                    SyncScrubPanel();
                }
            }
            finally
            {
                readingBatch = false;
            }
        }

        static RowFacts ReadFacts(string path, bool video)
        {
            var f = new RowFacts();
            try
            {
                if (video)
                {
                    f.Bytes = new FileInfo(path).Length;
                    ImageMetadata m = VideoMetaOf(path);
                    f.FormatId = m.Format != "unknown" ? m.Format : Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
                    if (m.Format == "unknown") f.Problem = "the scrubber cleans MP4 and MOV videos";
                    f.HasGps = m.Gps != null;
                    f.TrailerBytes = m.TrailerBytes;
                }
                else FactsOf(f, Pictures.ReadFile(path));
            }
            catch (PictureException x)
            {
                f.Problem = x.Message;
            }
            catch (Exception x) when (x is IOException || x is UnauthorizedAccessException || x is OutOfMemoryException)
            {
                f.Problem = x.Message.TrimEnd('.');
            }
            return f;
        }

        static RowFacts FactsOf(RowFacts f, byte[] bytes)
        {
            ImageMetadata m = InspectPhoto(bytes);
            f.Bytes = bytes.Length;
            f.FormatId = m.Format;
            f.HasGps = m.Gps != null;
            f.TrailerBytes = m.TrailerBytes;
            return f;
        }

        static void Apply(ExifRow row, RowFacts f)
        {
            row.Bytes = f.Bytes;
            row.FormatId = f.FormatId;
            row.HasGps = f.HasGps;
            row.TrailerBytes = f.TrailerBytes;
            row.Problem = f.Problem;
            row.Read = true;
        }

        // a row being read whole for the inspector (the injector waits for it)
        bool rowLoading;
        internal bool ExifRowLoading => rowLoading;

        // a row picked: read whole for the inspector (and the injector)
        async void PickRow(int index)
        {
            if (index < 0 || index >= exifBatch.Count) return;
            batchIndex = index;
            int ticket = ++rowTicket;
            ExifRow row = exifBatch[index];
            rowLoading = true;
            SyncInjectPanel();
            ExifItem item;
            try
            {
                item = await Task.Run(() => LoadRow(row));
            }
            catch (OutOfMemoryException)
            {
                item = null;
            }
            if (ticket != rowTicket) return;
            rowLoading = false;
            batchShown = item;
            stageShowing = null;
            SyncExif();
        }

        static ExifItem LoadRow(ExifRow row)
        {
            var item = new ExifItem { Name = row.Name, Path = row.Path, Original = row.Original, Row = row, Video = row.IsVideo };
            try
            {
                if (row.IsVideo)
                {
                    item.Size = new FileInfo(row.Path).Length;
                    item.Meta = VideoMetaOf(row.Path);
                    item.Preview = FirstFrame(row.Path);
                }
                else
                {
                    item.Bytes = Pictures.ReadFile(row.Path);
                    item.Size = item.Bytes.Length;
                    item.Meta = InspectPhoto(item.Bytes);
                    try
                    {
                        item.Preview = PreviewOf(Pictures.FromBytes(item.Bytes, row.Name).Source);
                    }
                    catch (PictureException)
                    {
                        item.Preview = null;
                    }
                }
            }
            catch (Exception x) when (x is PictureException || x is IOException || x is UnauthorizedAccessException)
            {
                item.Meta = new ImageMetadata { Format = "unknown", AiDetection = AiMarkers.Detect(new List<string>(), null, null) };
                item.PreviewWanted = false;
            }
            return item;
        }

        static BitmapSource FirstFrame(string path)
        {
            if (!Mf.Available) return null;
            try
            {
                VideoFile file = VideoFile.Open(path);
                using (VideoReader reader = file.Frames())
                {
                    VideoFrame f = reader.Next();
                    return f == null ? null : PreviewOf(new PictureSource(f.Bgra, f.Width, f.Height));
                }
            }
            catch (VideoException)
            {
                return null;
            }
        }

        // ---- what both tabs show ----

        // the file the tools work on: a batch's row looked at, or the file open
        ExifItem ExifCurrent => exifBatch.Count > 0 ? batchShown : exifFile;

        internal void SyncExif()
        {
            if (exifStage == null) return;
            // the inspector
            if (exifBatch.Count > 0) exifStage.ShowBatch(exifBatch, batchIndex, batchProgress);
            else exifStage.HideBatch();
            ExifItem shown = ExifCurrent;
            if (!ReferenceEquals(shown, stageShowing))
            {
                stageShowing = shown;
                if (shown?.Meta != null) exifStage.ShowFile(shown.Meta, shown.Preview, shown.PreviewWanted);
                else if (exifBatch.Count > 0) exifStage.ShowFile(new ImageMetadata { Format = "unknown", AiDetection = AiMarkers.Detect(new List<string>(), null, null) }, null, false);
                else exifStage.ShowMessage("Choose a photo to inspect and scrub its metadata.");
            }
            SyncScrubPanel();
            SyncInjectPanel();
            SyncTab();
        }

        void SyncScrubPanel()
        {
            bool quietBefore = quiet;
            quiet = true;
            try
            {
                ScrubAllButton.IsChecked = !settings.ScrubLocationOnly;
                ScrubLocationButton.IsChecked = settings.ScrubLocationOnly;
                ScrubOrientationBox.IsChecked = settings.ScrubOrientation;
            }
            finally
            {
                quiet = quietBefore;
            }
            bool inBatch = exifBatch.Count > 0;
            if (inBatch)
            {
                bool videos = exifBatch.Any(r => r.IsVideo);
                string kb = JsText.Fixed(exifBatch.Sum(r => r.Bytes) / 1024.0, 1);
                int gps = exifBatch.Count(r => r.HasGps);
                ScrubPictureInfo.Text = exifBatch.Count + (videos ? " files" : " photos") + " in batch (" + kb + " KB)" + (gps > 0 ? " · " + gps + " with GPS" : "");
            }
            else if (exifFile != null) ScrubPictureInfo.Text = exifFile.Name + " · " + JsText.Fixed(exifFile.Size / 1024.0, 1) + " KB";
            else ScrubPictureInfo.Text = "No photo loaded.";
            ScrubSaveButton.Visibility = inBatch ? Visibility.Collapsed : Visibility.Visible;
            ScrubSaveButton.IsEnabled = exifFile != null && !exifBusy;
            ScrubSaveButton.Content = exifFile?.Video == true ? "_Scrub & save a clean video…" : "_Scrub & save a clean copy…";
            ScrubZipButton.Visibility = ScrubIntoButton.Visibility = ScrubClearBatchButton.Visibility = inBatch ? Visibility.Visible : Visibility.Collapsed;
            ScrubZipButton.IsEnabled = ScrubIntoButton.IsEnabled = ScrubClearBatchButton.IsEnabled = inBatch && !exifBusy;
            ScrubZipButton.Content = "Clean the batch into a _ZIP (" + exifBatch.Count + (exifBatch.Count == 1 ? " file" : " files") + ")…";
            ScrubStopButton.Visibility = exifBusy ? Visibility.Visible : Visibility.Collapsed;
            ScrubAddButton.IsEnabled = ScrubFolderAddButton.IsEnabled = !exifBusy;
        }

        void ScrubSay(string text, bool bad = false)
        {
            ScrubStatus.Text = text ?? "";
            ScrubStatus.SetResourceReference(TextBlock.ForegroundProperty, bad ? "Bad" : "Text");
        }

        void InjectSay(string text, bool bad = false)
        {
            InjectStatus.Text = text ?? "";
            InjectStatus.SetResourceReference(TextBlock.ForegroundProperty, bad ? "Bad" : "Text");
        }

        // words on the EXIF tab showing
        void ExifSay(string text, bool bad = false)
        {
            if (InjectActive) InjectSay(text, bad);
            else ScrubSay(text, bad);
        }

        // what Ctrl+S does on these tabs: what their save buttons do
        // (nothing while the window is still being built)
        internal bool ExifCanSave => exifStage != null && (InjectActive ? InjectSaveButton.IsEnabled : ScrubSaveButton.IsEnabled && ScrubSaveButton.Visibility == Visibility.Visible);

        void OnScrubMode(object sender, RoutedEventArgs e)
        {
            if (Quiet) return;
            settings.ScrubLocationOnly = ScrubLocationButton.IsChecked == true;
            KeepExifSettings();
        }

        void OnScrubOrientation(object sender, RoutedEventArgs e)
        {
            if (Quiet) return;
            settings.ScrubOrientation = ScrubOrientationBox.IsChecked == true;
            KeepExifSettings();
        }

        void KeepExifSettings()
        {
            saveTimer.Stop();
            saveTimer.Start();
        }

        internal ScrubOptions ScrubOptions => new ScrubOptions { Mode = settings.ScrubLocationOnly ? "location_only" : "all", PreserveOrientation = settings.ScrubOrientation };

        // the scrubber's options as they start
        internal void ResetExif()
        {
            Settings fresh = Settings.Defaults();
            settings.ScrubLocationOnly = fresh.ScrubLocationOnly;
            settings.ScrubOrientation = fresh.ScrubOrientation;
            SyncScrubPanel();
            saveTimer.Stop();
            SaveSettings();
            Flash("The Scrubber's options are reset.", "Dim");
        }

        void OnExifStop(object sender, RoutedEventArgs e) => StopExif();

        internal void StopExif() => exifCancel?.Cancel();

        // (for the tests: called on the worker as each of a batch's rows starts)
        internal Action<int> OnBatchStep;

        // the engine's words, for this PC (the website's say "in the browser")
        static string PcWords(string message) => message.Replace(" in the browser", "");

        // ---- a clean copy of the file open ----

        static string SameFileWords = "That's the original's own name: pick another, so the original stays as it is.";

        static bool SameFile(string a, string b)
        {
            if (a == null || b == null) return false;
            try
            {
                if (string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase)) return true;
            }
            catch (Exception x) when (x is ArgumentException || x is NotSupportedException || x is PathTooLongException)
            {
                return false;
            }
            // (another spelling of it: a network drive's, a junction's, a hard link's)
            var ia = Native.FileIdentity(a);
            if (ia == null) return false;
            var ib = Native.FileIdentity(b);
            return ib != null && ia.Value.Equals(ib.Value);
        }

        // whether a file at `path` is the open file, or an original it came from
        static bool IsOriginal(string path, ExifItem f) => SameFile(path, f.Path) || SameFile(path, f.Original);

        static string ChangedWords = "The file changed while the Save dialog was open, so nothing was saved: save again.";

        async void OnScrubSave(object sender, RoutedEventArgs e)
        {
            ExifItem f = exifFile;
            if (f == null || exifBusy || exifBatch.Count > 0) return;
            string ext;
            if (f.Video)
            {
                if (f.Meta.Format == "unknown")
                {
                    ScrubSay("Failed to scrub: only MP4 and MOV videos can be cleaned here.", true);
                    return;
                }
                ext = "." + f.Meta.Format;
            }
            else
            {
                // (the website's words for a kind it doesn't clean, before any dialog)
                if (!Scrubber.CanScrub(f.Meta.Format))
                {
                    try
                    {
                        Scrubber.Scrub(f.Bytes, ScrubOptions);
                    }
                    catch (ScrubException x)
                    {
                        ScrubSay("Failed to scrub: " + PcWords(x.Message), true);
                        return;
                    }
                    catch (ExifRangeException x)
                    {
                        ScrubSay("Failed to scrub: " + x.Message, true);
                        return;
                    }
                }
                ext = "." + Scrubber.Extension(f.Meta.Format);
            }
            string path;
            try
            {
                path = FileDialogs.Save(this, "Save the clean copy", f.Video ? (ext == ".mov" ? "QuickTime video" : "MP4 video") : JsText.Upper(f.Meta.Format) + " picture", ext, Export.BaseName(f.Name) + "-clean" + ext);
            }
            catch (COMException x)
            {
                MessageBox.Show(this, "Windows' Save dialog didn't open:\n\n" + x.Message, Program.Name, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (path != null) await ScrubSaveTo(path, f);
        }

        // The clean copy of the file open (`item`: the one the Save dialog was for), saved at
        // `path` and checked there (Verified Clean).
        internal async Task ScrubSaveTo(string path, ExifItem item = null)
        {
            ExifItem f = item ?? exifFile;
            if (f == null || exifBusy) return;
            if (f != exifFile || exifBatch.Count > 0)
            {
                ScrubSay(ChangedWords, true);
                return;
            }
            if (IsOriginal(path, f))
            {
                ScrubSay(SameFileWords, true);
                return;
            }
            ScrubOptions o = ScrubOptions;
            string name = Path.GetFileName(path);
            exifBusy = true;
            exifCancel = new CancellationTokenSource();
            CancellationToken cancel = exifCancel.Token;
            SyncExif();
            ScrubSay(f.Video ? "Cleaning the video…" : "Scrubbing metadata…");
            Task<SelfCheckReport> run = Task.Run(() => f.Video ? CleanVideo(f.Path, path, o, cancel, done => Dispatcher.BeginInvoke(new Action(() => ScrubSay("Cleaning the video… " + done + " %")))) : CleanPhoto(f, path, o));
            ExifRunning = run;
            try
            {
                SelfCheckReport report = await run;
                // (the check shows over the file it's of: another opened meanwhile has its own)
                if (ReferenceEquals(stageShowing, f)) exifStage.ShowSelfCheck(report, f.Video);
                ScrubSay(report.IsClean ? "Cleaned and saved " + name + "." : "Saved " + name + ", but it is not fully clean: " + string.Join(", ", report.Leftovers) + ".", !report.IsClean);
            }
            catch (OperationCanceledException)
            {
                ScrubSay("Stopped: nothing was saved.");
            }
            catch (ScrubException x)
            {
                ScrubSay("Failed to scrub: " + PcWords(x.Message), true);
            }
            catch (ExifRangeException x)
            {
                ScrubSay("Failed to scrub: " + x.Message, true);
            }
            catch (Exception x) when (x is IOException || x is UnauthorizedAccessException)
            {
                ScrubSay("Couldn't save " + name + ": " + x.Message.TrimEnd('.') + ".", true);
            }
            finally
            {
                exifBusy = false;
                exifCancel = null;
                SyncExif();
                if (closeAfterExif) Close();
            }
        }

        // a photo's clean copy, written and read back from the disk for its check
        static SelfCheckReport CleanPhoto(ExifItem f, string path, ScrubOptions o)
        {
            ScrubResult result = Scrubber.Scrub(f.Bytes, o);
            string written = Exports.Write(path, s => s.Write(result.Data, 0, result.Data.Length));
            return Scrubber.Verify(f.Meta, f.Bytes.Length, File.ReadAllBytes(written), o);
        }

        // A video's clean copy, streamed into its file and checked against the original byte for
        // byte before it takes its name: a stopped or failed check leaves no file.
        static SelfCheckReport CleanVideo(string source, string path, ScrubOptions o, CancellationToken cancel, Action<long> percent = null)
        {
            using (FileStream input = OpenSource(source))
            {
                VideoCleanPlan plan = PlanVideo(input, o, out ImageMetadata before);
                long last = -1;
                SelfCheckReport report = null;
                Exports.Write(path, s =>
                {
                    VideoMeta.Write(input, plan, s, done =>
                    {
                        long p = plan.Length > 0 ? done * 100 / plan.Length : 100;
                        if (p != last && percent != null) percent(last = p);
                    }, cancel);
                    s.Flush();
                    report = VideoMeta.Verify(before, input.Length, plan, input, s, o, cancel);
                }, null, true);
                return report;
            }
        }

        // A file to clean, held so nothing changes, replaces or deletes it while it's read; one
        // that can't be opened (gone, or being written) is its row's problem.
        static FileStream OpenSource(string path)
        {
            try
            {
                return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16);
            }
            catch (Exception x) when (x is IOException || x is UnauthorizedAccessException || x is ArgumentException || x is NotSupportedException)
            {
                throw new ScrubException("it couldn't be read: " + x.Message.TrimEnd('.'));
            }
        }

        // a row's name as the website would have it (a browser hands over a lone surrogate as
        // U+FFFD), short enough that its clean copy's name fits Windows' 255 characters
        static string BatchSource(string name)
        {
            name = JsText.WellFormed(name);
            string ext = Path.GetExtension(name);
            const int most = 200;
            if (name.Length - ext.Length <= most) return name;
            int cut = char.IsHighSurrogate(name[most - 1]) ? most - 1 : most;
            return name.Substring(0, cut) + ext;
        }

        // ---- the batch cleaned ----

        async void OnScrubInto(object sender, RoutedEventArgs e)
        {
            if (exifBusy || exifBatch.Count == 0) return;
            string folder;
            try
            {
                folder = FileDialogs.PickFolder(this, "The folder for the clean copies");
            }
            catch (COMException x)
            {
                MessageBox.Show(this, "Windows' folder dialog didn't open:\n\n" + x.Message, Program.Name, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (folder != null) await CleanBatchInto(folder);
        }

        async void OnScrubZip(object sender, RoutedEventArgs e)
        {
            if (exifBusy || exifBatch.Count == 0) return;
            string path;
            try
            {
                path = FileDialogs.Save(this, "Save the clean copies as a ZIP", "ZIP archive", ".zip", "b0r3d-clean-photos.zip");
            }
            catch (COMException x)
            {
                MessageBox.Show(this, "Windows' Save dialog didn't open:\n\n" + x.Message, Program.Name, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (path != null) await CleanBatchToZip(path);
        }

        static string Plural(int n, string what) => n + " " + what + (n == 1 ? "" : "s");

        string BatchWhat(int n) => Plural(n, exifBatch.Any(r => r.IsVideo) ? "file" : "photo");

        // A file's own problem said on its row, as the website's batch does for any error: it can't
        // be read or cleaned, its clean copy's name is too long there, it's too big. Stop, and
        // trouble writing (a full disk), go on up and end the batch.
        static void TryRow(ExifRow row, Action clean)
        {
            try
            {
                clean();
            }
            catch (Exception x) when (x is ScrubException || x is ExifRangeException || x is PictureException)
            {
                row.Error = PcWords(x.Message);
            }
            catch (PathTooLongException)
            {
                row.Error = "its clean copy's name is too long for that folder";
            }
            catch (OutOfMemoryException)
            {
                row.Error = "it's too big to clean here";
            }
            catch (Exception x) when (!(x is OperationCanceledException || x is IOException || x is UnauthorizedAccessException))
            {
                row.Error = "it couldn't be cleaned (" + x.GetType().Name + ": " + x.Message.TrimEnd('.') + ")";
            }
        }

        // the end of what a batch says: the website's words, then the rows not fully clean and
        // (the PC's) those that couldn't be cleaned at all, which the website leaves to the table
        string BatchEnd(string done)
        {
            int notClean = exifBatch.Count(r => r.Report != null && !r.Report.IsClean), failed = exifBatch.Count(r => r.Error != null);
            var buts = new List<string>();
            if (notClean > 0) buts.Add(notClean + " not fully clean");
            if (failed > 0) buts.Add(failed + " couldn't be cleaned");
            return buts.Count == 0 ? done + "." : done + ", but " + string.Join(" and ", buts) + ": see the table.";
        }

        bool AllClean => !exifBatch.Any(r => r.Report != null && !r.Report.IsClean);

        // a batch with nothing in it to clean leaves no ZIP, as on the website
        sealed class NothingToZip : Exception
        {
        }

        // Whether a file at `path` is one of the batch's own (an original, or a copy in its place):
        // only one of the same size as it (or not read yet, or edited) is opened to see.
        bool OneOfTheBatch(string path)
        {
            long size;
            try
            {
                var info = new FileInfo(path);
                if (!info.Exists) return false;
                size = info.Length;
            }
            catch (Exception x) when (x is IOException || x is UnauthorizedAccessException || x is ArgumentException || x is NotSupportedException)
            {
                return false;
            }
            return exifBatch.Any(r => ((!r.Read || r.Bytes == size) && SameFile(path, r.Path)) || (r.Original != r.Path && SameFile(path, r.Original)) ||
                string.Equals(Path.GetFullPath(path), r.Path, StringComparison.OrdinalIgnoreCase));
        }

        // a video's plan, or its row's problem
        static VideoCleanPlan PlanVideo(Stream input, ScrubOptions o, out ImageMetadata before)
        {
            try
            {
                return VideoMeta.Plan(input, o, out before);
            }
            catch (IOException x)
            {
                throw new ScrubException("this video can't be cleaned: " + x.Message.TrimEnd('.'));
            }
        }

        // Each of the batch's files cleaned into `folder` as photo-clean.jpg (numbered when a name is
        // taken, in the folder or in the batch: nothing there is written over), each checked (a
        // photo read back from the disk; a video before it takes its name). The originals are never
        // changed.
        internal async Task CleanBatchInto(string folder)
        {
            if (exifBusy || exifBatch.Count == 0) return;
            var used = new HashSet<string>(StringComparer.Ordinal);
            try
            {
                foreach (string f in Directory.EnumerateFileSystemEntries(folder)) used.Add(JsText.Lower(Path.GetFileName(f)));
            }
            catch (Exception x) when (x is IOException || x is UnauthorizedAccessException || x is ArgumentException || x is System.Security.SecurityException)
            {
                ScrubSay("Couldn't use that folder: " + x.Message.TrimEnd('.') + ".", true);
                return;
            }
            await RunBatch((rows, o, cancel, step) =>
            {
                for (int i = 0; i < rows.Count; i++)
                {
                    cancel.ThrowIfCancellationRequested();
                    ExifRow row = rows[i];
                    step(i, row);
                    string NextFree(string format) => Path.Combine(folder, Scrubber.BatchName(BatchSource(row.Name), format, used));
                    TryRow(row, () =>
                    {
                        if (row.IsVideo)
                        {
                            using (FileStream input = OpenSource(row.Path))
                            {
                                VideoCleanPlan plan = PlanVideo(input, o, out ImageMetadata before);
                                SelfCheckReport report = null;
                                string written = Exports.Write(NextFree(before.Format), s =>
                                {
                                    VideoMeta.Write(input, plan, s, null, cancel);
                                    s.Flush();
                                    report = VideoMeta.Verify(before, input.Length, plan, input, s, o, cancel);
                                }, () => NextFree(before.Format), true);
                                row.CleanName = Path.GetFileName(written);
                                row.Report = report;
                            }
                            return;
                        }
                        byte[] bytes = Pictures.ReadFile(row.Path);
                        ImageMetadata meta = ExifReader.Inspect(bytes);
                        ScrubResult result = Scrubber.Scrub(bytes, o);
                        string savedAt = Exports.Write(NextFree(result.Format), s => s.Write(result.Data, 0, result.Data.Length), () => NextFree(result.Format));
                        row.CleanName = Path.GetFileName(savedAt);
                        row.Report = Scrubber.Verify(meta, bytes.Length, File.ReadAllBytes(savedAt), o);
                    });
                }
            }, ok =>
            {
                long bytes = exifBatch.Where(r => r.Report != null).Sum(r => r.Report.ScrubbedSize);
                return ok == 0 ? "No files could be scrubbed." : BatchEnd((AllClean ? "Cleaned and saved " : "Saved ") + BatchWhat(ok) + " into " + Pictures.NameOf(folder) + " (" + JsText.Fixed(bytes / 1024.0, 1) + " KB)");
            });
        }

        // The batch's clean copies in one ZIP (the website's: stored, names unique whatever their
        // case, every date 1980-01-01), written as it goes; a photo checked before it goes in, a
        // video where it lies in the ZIP just after. A file that can't be cleaned is left out (said
        // on its row); a failure inside the ZIP, or Stop, leaves no ZIP at all.
        internal async Task CleanBatchToZip(string zipPath)
        {
            if (exifBusy || exifBatch.Count == 0) return;
            if (OneOfTheBatch(zipPath))
            {
                ScrubSay("That's one of the batch's own files: pick another name, so it stays as it is.", true);
                return;
            }
            string writtenAt = null;
            await RunBatch((rows, o, cancel, step) =>
            {
                var used = new HashSet<string>(StringComparer.Ordinal);
                try
                {
                    writtenAt = Exports.Write(zipPath, s =>
                    {
                        var zip = new ZipWriter(s);
                        for (int i = 0; i < rows.Count; i++)
                        {
                            cancel.ThrowIfCancellationRequested();
                            ExifRow row = rows[i];
                            step(i, row);
                            if (row.IsVideo)
                            {
                                FileStream input = null;
                                VideoCleanPlan plan = null;
                                ImageMetadata before = null;
                                TryRow(row, () =>
                                {
                                    input = OpenSource(row.Path);
                                    try
                                    {
                                        plan = PlanVideo(input, o, out before);
                                    }
                                    catch
                                    {
                                        input.Dispose();
                                        input = null;
                                        throw;
                                    }
                                });
                                if (input == null) continue;
                                using (input)
                                {
                                    string name = Scrubber.BatchName(BatchSource(row.Name), before.Format, used);
                                    zip.Add(name, plan.Length, entry => VideoMeta.Write(input, plan, entry, null, cancel));
                                    // its check, read back from the ZIP being written
                                    var written = zip.Written.Last();
                                    SelfCheckReport report = VideoMeta.Verify(before, input.Length, plan, input, new SubStream(s, written.DataOffset, written.Size), o, cancel);
                                    s.Position = s.Length;
                                    row.CleanName = name;
                                    row.Report = report;
                                }
                                continue;
                            }
                            ScrubResult result = null;
                            ImageMetadata meta = null;
                            byte[] bytes = null;
                            TryRow(row, () =>
                            {
                                bytes = Pictures.ReadFile(row.Path);
                                meta = ExifReader.Inspect(bytes);
                                result = Scrubber.Scrub(bytes, o);
                            });
                            if (result == null) continue;
                            string entryName = Scrubber.BatchName(BatchSource(row.Name), result.Format, used);
                            row.Report = Scrubber.Verify(meta, bytes.Length, result.Data, o);
                            zip.Add(entryName, result.Data);
                            row.CleanName = entryName;
                        }
                        // (nothing in it could be cleaned: no ZIP at all, as on the website)
                        if (!zip.Written.Any()) throw new NothingToZip();
                        zip.Finish();
                    }, null, true);
                }
                catch (NothingToZip)
                {
                    writtenAt = null;
                }
            }, ok =>
            {
                if (ok == 0 || writtenAt == null) return "No files could be scrubbed.";
                long length = 0;
                try
                {
                    length = new FileInfo(writtenAt).Length;
                }
                catch (IOException)
                {
                }
                string name = Path.GetFileName(writtenAt), kb = JsText.Fixed(length / 1024.0, 1) + " KB";
                string clean = exifBatch.Any(r => r.IsVideo) ? (ok == 1 ? "file" : "files") : ok == 1 ? "photo" : "photos";
                return BatchEnd(AllClean ? "Bundled & saved " + name + " (" + ok + " clean " + clean + ", " + kb + ")" : "Saved " + name + " (" + BatchWhat(ok) + ", " + kb + ")");
            }, true);
        }

        // The batch's rows cleaned on a worker by `work` (it says which row it's on with `step`),
        // with Stop; `words` says how it went from how many came out. `whole`: one file for all of
        // them (a ZIP), so stopped or failed, none of them was saved.
        async Task RunBatch(Action<List<ExifRow>, ScrubOptions, CancellationToken, Action<int, ExifRow>> work, Func<int, string> words, bool whole = false)
        {
            exifBusy = true;
            exifCancel = new CancellationTokenSource();
            CancellationToken cancel = exifCancel.Token;
            ScrubOptions o = ScrubOptions;
            var rows = exifBatch.ToList();
            foreach (ExifRow r in rows)
            {
                r.Report = null;
                r.Error = null;
                r.CleanName = null;
                r.Changed();
            }
            SyncExif();
            var done = new TaskCompletionSource<bool>();
            ExifRunning = done.Task;
            ExifRow cleaning = null;
            void Step(int i, ExifRow row)
            {
                OnBatchStep?.Invoke(i);
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (cleaning != null)
                    {
                        cleaning.Cleaning = false;
                        cleaning.Changed();
                    }
                    cleaning = row;
                    row.Cleaning = true;
                    row.Changed();
                    batchProgress = "Cleaning " + (i + 1) + " of " + rows.Count + ": " + row.Name + "…";
                    exifStage.ShowBatch(exifBatch, batchIndex, batchProgress);
                    ScrubSay(batchProgress);
                }));
            }
            string failure = null;
            bool stopped = false, finished = false;
            try
            {
                // (the rows still being read are read first; Stop works while they are)
                while (readingBatch && !cancel.IsCancellationRequested) await Task.Delay(20);
                cancel.ThrowIfCancellationRequested();
                await Task.Run(() => work(rows, o, cancel, Step));
                finished = true;
            }
            catch (OperationCanceledException)
            {
                stopped = true;
            }
            catch (Exception x) when (x is IOException || x is UnauthorizedAccessException || x is OutOfMemoryException)
            {
                failure = x.Message.TrimEnd('.');
            }
            catch (Exception x)
            {
                // (anything else: said, and the batch ends as if it had failed)
                failure = "something went wrong (" + x.GetType().Name + ": " + x.Message.TrimEnd('.') + ")";
            }
            finally
            {
                // (the last step's update first)
                await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Background);
                foreach (ExifRow r in rows)
                {
                    r.Cleaning = false;
                    // (one file for all of them, not made: none of them was saved)
                    if (whole && !finished)
                    {
                        r.Report = null;
                        r.CleanName = null;
                    }
                    r.Changed();
                }
                int ok = rows.Count(r => r.Report != null);
                batchProgress = null;
                exifStage.ShowBatch(exifBatch, batchIndex, null);
                if (stopped) ScrubSay(whole ? "Stopped: no ZIP was saved." : "Stopped: " + BatchWhat(ok) + " cleaned and checked before it stopped; nothing half-written or unchecked is left.");
                else if (failure != null) ScrubSay("Couldn't save: " + failure + ".", true);
                else ScrubSay(words(ok), rows.Any(r => r.Error != null || r.Report != null && !r.Report.IsClean));
                exifBusy = false;
                exifCancel = null;
                SyncExif();
                done.TrySetResult(true);
                if (closeAfterExif) Close();
            }
        }

        // ---- the injector ----

        Dictionary<string, Control> injectFields;

        Dictionary<string, Control> InjectFields => injectFields ?? (injectFields = new Dictionary<string, Control>
        {
            ["make"] = InjectMake, ["model"] = InjectModel, ["lens"] = InjectLens, ["artist"] = InjectArtist, ["copyright"] = InjectCopyright, ["software"] = InjectSoftware,
            ["orientation"] = InjectOrientation, ["aperture"] = InjectAperture, ["shutter"] = InjectShutter, ["iso"] = InjectIso, ["focal"] = InjectFocal,
            ["dateTime"] = InjectDateTime, ["description"] = InjectDescription, ["lat"] = InjectLat, ["lon"] = InjectLon, ["alt"] = InjectAlt,
        });

        // what the photo had when it was opened: unchanged fields are left alone
        InjectorFields loadedFields = new InjectorFields();
        byte[] loadedBytes;
        DevicePreset preset;

        internal InjectorFields LoadedFields => loadedFields;
        internal DevicePreset PresetPicked => preset;

        internal InjectorFields ReadForm()
        {
            var f = new InjectorFields();
            foreach (var kv in InjectFields)
                f[kv.Key] = kv.Value is TextBox t ? t.Text : (string)((ComboBoxItem)((ComboBox)kv.Value).SelectedItem)?.Tag ?? "";
            return f;
        }

        void SetField(string key, string value)
        {
            Control c = InjectFields[key];
            if (c is TextBox t) t.Text = value ?? "";
            else
            {
                var box = (ComboBox)c;
                box.SelectedItem = box.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == (value ?? "")) ?? box.Items[0];
            }
            Valid(c);
        }

        internal void FillForm(InjectorFields values)
        {
            foreach (string k in InjectorFields.Keys) SetField(k, values[k]);
            ForgetPreset();
        }

        void ForgetPreset()
        {
            preset = null;
            bool wasQuiet = quiet;
            quiet = true;
            InjectPreset.SelectedIndex = 0;
            quiet = wasQuiet;
        }

        // a field with a problem: the Bad colour round it
        static void Invalid(Control c) => c.SetResourceReference(Control.BorderBrushProperty, "Bad");

        static void Valid(Control c) => c.ClearValue(Control.BorderBrushProperty);

        void SyncInjectPanel()
        {
            ExifItem cur = ExifCurrent;
            if (cur == null || cur.Meta == null)
            {
                InjectPictureInfo.Text = "No photo loaded.";
                InjectFormatNote.Visibility = Visibility.Collapsed;
                InjectXmpBox.Visibility = Visibility.Collapsed;
                InjectSaveButton.IsEnabled = InjectResetButton.IsEnabled = false;
                // (the next photo shown fills the form again, the one shown before too)
                loadedBytes = null;
                return;
            }
            InjectPictureInfo.Text = cur.Name + " · " + JsText.Fixed(cur.Size / 1024.0, 1) + " KB";
            bool writable = !cur.Video && Injector.CanInject(cur.Meta.Format);
            // (not while a batch's row is being read: the form would be for the one before)
            InjectSaveButton.IsEnabled = InjectResetButton.IsEnabled = writable && !exifBusy && !rowLoading;
            InjectFormatNote.Visibility = writable ? Visibility.Collapsed : Visibility.Visible;
            InjectFormatNote.Text = cur.Meta.Format == "unknown"
                ? "The injector writes into JPEG and PNG files, and this kind of file can't be read here."
                : cur.Meta.Format == "gif"
                    ? "GIFs can't hold EXIF, so there's nothing to edit here."
                    : "The injector writes into JPEG and PNG files. This one is " + JsText.Upper(cur.Meta.Format) + ", so its " + (cur.Video ? "metadata" : "EXIF") + " can be read in the Scrubber but not changed here.";
            // a new photo: the form starts from what it has (switching tabs keeps the edits)
            if (!ReferenceEquals(cur.Bytes, loadedBytes) || cur.Bytes == null)
            {
                loadedBytes = cur.Bytes;
                loadedFields = writable ? Injector.ReadFields(cur.Bytes) : new InjectorFields();
                FillForm(loadedFields);
                string xmp = writable ? cur.Meta.XmpText : null;
                InjectXmpBox.Visibility = !string.IsNullOrEmpty(xmp) ? Visibility.Visible : Visibility.Collapsed;
                if (!string.IsNullOrEmpty(xmp))
                {
                    List<string> copies = Injector.XmpCopies(xmp);
                    InjectXmpBox.IsChecked = copies.Count > 0;
                    InjectXmpText.Text = copies.Count > 0
                        ? "The photo's XMP block keeps its own copy of the " + string.Join(", ", copies) + ", which would contradict what you inject."
                        : "The photo has an XMP block too (more metadata kept beside the EXIF).";
                }
            }
        }

        void OnInjectPreset(object sender, SelectionChangedEventArgs e)
        {
            if (Quiet || InjectPreset.SelectedItem == null) return;
            DevicePreset p = DevicePresets.ById((string)((ComboBoxItem)InjectPreset.SelectedItem).Tag);
            if (p == null) return;
            foreach (string k in DevicePresets.FieldKeys) SetField(k, p[k]);
            preset = p;
            InjectSay("Filled in as " + DevicePresets.ShortLabel(p) + ": camera, lens and exposure. Inject to save it.");
        }

        void OnInjectReset(object sender, RoutedEventArgs e) => FillForm(loadedFields);

        void OnInjectClear(object sender, RoutedEventArgs e) => FillForm(new InjectorFields());

        // the website's b0r3d Defaults (the software is this program, at its version)
        internal void InjectDefaults()
        {
            var f = new InjectorFields
            {
                ["make"] = "b0r3d optics", ["model"] = "Rukus-Cam 95 Deluxe", ["lens"] = "b0r3d Prime 28mm f/1.4 Deluxe", ["artist"] = "still_b0r3d",
                ["copyright"] = "(c) 2026 b0r3d.org - All rights reserved", ["software"] = "b0r3d DeRes v" + Program.Version, ["orientation"] = "1", ["aperture"] = "1.4",
                ["shutter"] = "1/250", ["iso"] = "100", ["focal"] = "28", ["dateTime"] = Injector.NormalizeDate(), ["description"] = "Rukus the Cat, Chief Executive Officer of b0r3d.org",
                ["lat"] = "41.878113", ["lon"] = "-87.629799", ["alt"] = "182",
            };
            FillForm(f);
        }

        void OnInjectDefaults(object sender, RoutedEventArgs e) => InjectDefaults();

        void OnInjectNow(object sender, RoutedEventArgs e) => SetField("dateTime", Injector.NormalizeDate());

        void OnInjectPlace(object sender, RoutedEventArgs e)
        {
            string[] p = ((string)((Button)sender).Tag).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            SetField("lat", p.Length > 0 ? p[0] : "");
            SetField("lon", p.Length > 1 ? p[1] : "");
            SetField("alt", p.Length > 2 ? p[2] : "");
        }

        // the form as edits and options, or null (the problem said, its field marked)
        internal (ExifEdits Edits, InjectOptions Options)? FormEdits()
        {
            InjectorFields now = ReadForm();
            ExifEdits edits = Injector.EditsFrom(now, loadedFields, out FieldError error);
            if (error != null)
            {
                Control c = InjectFields[error.Field];
                Invalid(c);
                c.Focus();
                InjectSay(error.Message, true);
                return null;
            }
            if (preset != null && DevicePresets.FormShows(preset, now)) DevicePresets.AddExtras(preset, edits);
            var options = new InjectOptions { RemoveXmp = InjectXmpBox.Visibility == Visibility.Visible && InjectXmpBox.IsChecked == true };
            if (edits.Count == 0 && !options.RemoveXmp)
            {
                InjectSay("Nothing has changed yet: every field still shows what the photo has.");
                return null;
            }
            return (edits, options);
        }

        async void OnInjectSave(object sender, RoutedEventArgs e)
        {
            ExifItem cur = ExifCurrent;
            if (cur?.Bytes == null || exifBusy || rowLoading) return;
            var form = FormEdits();
            if (form == null) return;
            byte[] made;
            try
            {
                made = Injector.Inject(cur.Bytes, form.Value.Edits, form.Value.Options);
            }
            catch (Exception x) when (x is InjectException || x is ExifRangeException)
            {
                InjectSay("Couldn't inject: " + x.Message, true);
                return;
            }
            string ext = "." + Scrubber.Extension(ExifReader.DetectFormat(made));
            // (not the photo's own name: after an injection that's its injected copy)
            string suggested = Export.BaseName(cur.Name) + "-injected" + ext;
            for (int k = 2; cur.Path != null && string.Equals(suggested, Path.GetFileName(cur.Path), StringComparison.OrdinalIgnoreCase); k++)
                suggested = Export.BaseName(cur.Name) + "-injected-" + k + ext;
            string path;
            try
            {
                path = FileDialogs.Save(this, "Save the photo with its new EXIF", JsText.Upper(ExifReader.DetectFormat(made)) + " picture", ext, suggested);
            }
            catch (COMException x)
            {
                MessageBox.Show(this, "Windows' Save dialog didn't open:\n\n" + x.Message, Program.Name, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (path != null) await InjectSaveTo(path, form.Value, cur);
        }

        // The form's edits written into a new copy at `path` (`item`: the photo the Save dialog
        // was for); the edited photo is then the one shown and edited (in a batch, in its row's
        // place), as on the website.
        internal async Task InjectSaveTo(string path, (ExifEdits Edits, InjectOptions Options)? form = null, ExifItem item = null)
        {
            ExifItem cur = item ?? ExifCurrent;
            if (cur?.Bytes == null || exifBusy) return;
            // (the form is the photo's shown: one picked or opened meanwhile has its own)
            if (cur != ExifCurrent || rowLoading || !ReferenceEquals(loadedBytes, cur.Bytes))
            {
                InjectSay(ChangedWords, true);
                return;
            }
            form = form ?? FormEdits();
            if (form == null) return;
            if (IsOriginal(path, cur))
            {
                InjectSay(SameFileWords, true);
                return;
            }
            byte[] made;
            try
            {
                made = Injector.Inject(cur.Bytes, form.Value.Edits, form.Value.Options);
            }
            catch (Exception x) when (x is InjectException || x is ExifRangeException)
            {
                InjectSay("Couldn't inject: " + x.Message, true);
                return;
            }
            string written;
            exifBusy = true;
            SyncExif();
            try
            {
                written = await Task.Run(() => Exports.Write(path, s => s.Write(made, 0, made.Length)));
            }
            catch (Exception x) when (x is IOException || x is UnauthorizedAccessException)
            {
                InjectSay("Couldn't save " + Path.GetFileName(path) + ": " + x.Message.TrimEnd('.') + ".", true);
                return;
            }
            finally
            {
                exifBusy = false;
                SyncExif();
                if (closeAfterExif) Close();
            }
            if (closeAfterExif) return;
            var edited = new ExifItem { Name = cur.Name, Path = written, Original = cur.Original ?? cur.Path, Row = cur.Row, Bytes = made, Size = made.Length, Meta = InspectPhoto(made), Preview = cur.Preview };
            if (cur.Row != null && exifBatch.Contains(cur.Row))
            {
                // its row now the edited photo (the original stays as it is)
                ExifRow row = cur.Row;
                row.Path = written;
                Apply(row, FactsOf(new RowFacts(), made));
                row.Report = null;
                row.Error = null;
                row.CleanName = null;
                row.Changed();
                if (batchShown == cur) batchShown = edited;
            }
            else if (exifFile == cur) exifFile = edited;
            stageShowing = null;
            SyncExif();
            int n = form.Value.Edits.Count;
            var parts = new List<string>();
            if (n > 0) parts.Add(n + " field" + (n == 1 ? "" : "s") + " changed");
            if (form.Value.Options.RemoveXmp) parts.Add("XMP block removed");
            InjectSay("Saved " + Path.GetFileName(written) + ": " + string.Join(", ", parts) + ".");
        }

        // ---- the menus and keys on the EXIF tabs ----

        // the File and Edit lines the tabs have, the others hidden
        void SyncExifMenus()
        {
            bool exif = ExifActive;
            Visibility Exif = exif ? Visibility.Visible : Visibility.Collapsed;
            ExifAddMenu.Visibility = ExifFolderMenu.Visibility = ExifSaveMenu.Visibility = ExifCopyGpsMenu.Visibility = Exif;
            ExifZipMenu.Visibility = ExifIntoMenu.Visibility = ExifClearMenu.Visibility = exif && ScrubActive && exifBatch.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            ExifSaveMenu.Visibility = exif && (InjectActive || exifBatch.Count == 0) ? Visibility.Visible : Visibility.Collapsed;
            ExifSaveMenu.Header = InjectActive ? "_Save with the new EXIF…" : "_Save a clean copy…";
            ExifCopyGpsMenu.IsEnabled = exifStage?.Shown?.Gps != null;
            // (the lines only these tabs hide come back on the others)
            foreach (UIElement m in new UIElement[] { SavePngMenu, CopyMenu, ZoomInMenu, ZoomOutMenu, FitMenu, OneMenu, FullScreenMenu, PlayMenu, ViewLine1, ViewLine2 })
                m.Visibility = exif ? Visibility.Collapsed : Visibility.Visible;
            if (!exif) return;
            foreach (MenuItem m in new[] { BatchMenu, OpenLookMenu, OpenPaletteMenu, SavePngMenu, SaveGifMenu, SaveApngMenu, SaveMp4Menu, SaveFramesMenu, WallpaperMenu, EmulatorMenu, SaveLookMenu, SavePaletteMenu,
                AsciiAnsMenu, AsciiAnsimationMenu, AsciiSeqMenu, AsciiPrgMenu, AsciiTxtMenu, CopyMenu, AsciiCopyMarkdownMenu, AsciiCopyAnsiMenu, AsciiCopyHtmlMenu, CopyLinkMenu, GrabMenu, SurpriseMenu,
                ZoomInMenu, ZoomOutMenu, FitMenu, OneMenu, CompareMenu, ScanlinesMenu, FullScreenMenu, PlayMenu })
                m.Visibility = Visibility.Collapsed;
            DeresEditLine.Visibility = Visibility.Collapsed;
            ViewLine1.Visibility = ViewLine2.Visibility = Visibility.Collapsed;
        }

        void OnExifSaveMenu(object sender, RoutedEventArgs e)
        {
            if (InjectActive) OnInjectSave(sender, e);
            else OnScrubSave(sender, e);
        }

        void OnExifCopyGps(object sender, RoutedEventArgs e) => exifStage.CopyGps();
    }

    // a stretch of another stream, read only (a video's entry inside a ZIP, for its check)
    sealed class SubStream : Stream
    {
        readonly Stream inner;
        readonly long start, length;
        long position;

        public SubStream(Stream inner, long start, long length)
        {
            this.inner = inner;
            this.start = start;
            this.length = length;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            long left = length - position;
            if (left <= 0) return 0;
            inner.Position = start + position;
            int n = inner.Read(buffer, offset, (int)Math.Min(count, left));
            position += n;
            return n;
        }

        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => length;

        // (never before its start: that's the ZIP's own header)
        public override long Position
        {
            get => position;
            set => position = value >= 0 ? value : throw new IOException("a place before the entry's start");
        }

        public override long Seek(long offset, SeekOrigin origin) => Position = origin == SeekOrigin.Begin ? offset : origin == SeekOrigin.Current ? position + offset : length + offset;
        public override void Flush() { }
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
