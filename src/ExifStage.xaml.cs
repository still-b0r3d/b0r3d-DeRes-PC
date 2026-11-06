using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using B0r3dDeRes.Exif;
using ImageMetadata = B0r3dDeRes.Exif.ImageMetadata;

namespace B0r3dDeRes
{
    // a batch's row in the scrubber: its file, what reading it found, and how cleaning it went
    public sealed class ExifRow : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        public string Path { get; set; }
        public string Name { get; set; }
        internal string Original;      // the file it was added as (an injected copy can take its place), never written over
        internal bool IsVideo;
        // what reading it found (read on a worker; only the summary is kept, not the file)
        internal bool Read;
        internal string FormatId = "unknown";
        internal long Bytes;
        internal bool HasGps;
        internal long TrailerBytes;
        internal string Problem;
        // the clean copy
        internal SelfCheckReport Report;
        internal string CleanName;
        internal string Error;
        internal bool Cleaning;

        public string Format => Read ? (Problem != null ? "?" : JsText.Upper(FormatId)) : "…";
        public string Size => Read ? JsText.Fixed(Bytes / 1024.0, 1) + " KB" : "";
        public string Gps => Read && Problem == null ? (HasGps ? "📍 Yes" : "No") : "";
        public bool GpsAlert => HasGps;
        public string Trailer => Read && Problem == null ? (TrailerBytes > 0 ? JsText.Fixed(TrailerBytes / 1024.0, 0) + " KB" : "No") : "";
        public bool TrailerAlert => TrailerBytes > 0;
        // the website's batchStatus: from the file's own self-check
        public string Status => Error != null ? "Error" : Cleaning ? "Cleaning…" : !Read ? "Reading…" : Problem != null ? "Can't read" : Report == null ? "Queued" : Report.IsClean ? "✓ Cleaned" : "⚠ Not clean";
        public string StatusTip => Error ?? Problem ?? (Report != null && !Report.IsClean ? Report.Summary : CleanName);
        public bool Pending => Error == null && Problem == null && Report == null;

        internal void Changed()
        {
            foreach (string p in new[] { nameof(Format), nameof(Size), nameof(Gps), nameof(GpsAlert), nameof(Trailer), nameof(TrailerAlert), nameof(Status), nameof(StatusTip), nameof(Pending) })
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));
        }
    }

    // The EXIF tools' inspector (the website's ui/scrub-stage.ts), on whichever of the two tabs shows.
    public partial class ExifStage : UserControl
    {
        ImageMetadata meta;
        bool allTags;
        string filter = "";
        readonly DispatcherTimer copiedTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };

        // shows a line in the tab's status (set by the window)
        internal Action<string> OnStatus = text => { };
        // a batch's row picked
        internal Action<int> RowPicked = i => { };

        // the All Tags list shows at most this many rows at once
        internal const int MaxTableRows = 500;

        internal ImageMetadata Shown => meta;
        internal bool AllTagsShown => allTags;
        internal string FilterText => filter;
        internal IEnumerable<string> TableRows => Table.Children.OfType<Grid>().Select(g => string.Join(" | ", g.Children.OfType<TextBlock>().Select(t => t.Text)));

        public ExifStage()
        {
            InitializeComponent();
            copiedTimer.Tick += (s, e) =>
            {
                copiedTimer.Stop();
                CopyGpsButton.Content = "Copy coordinates";
            };
            BatchList.SelectionChanged += (s, e) =>
            {
                if (quietRows || BatchList.SelectedIndex < 0) return;
                RowPicked(BatchList.SelectedIndex);
            };
        }

        static Visibility Shows(bool on) => on ? Visibility.Visible : Visibility.Collapsed;

        public void ShowMessage(string text)
        {
            Message.Text = text;
            Message.Visibility = Visibility.Visible;
            Body.Visibility = Visibility.Collapsed;
            meta = null;
        }

        bool quietRows;

        // the batch's rows (more than none), the one picked, and what's going on with them
        internal void ShowBatch(IList<ExifRow> rows, int picked, string progress)
        {
            BatchPanel.Visibility = Shows(rows != null && rows.Count > 0);
            if (rows == null || rows.Count == 0) return;
            quietRows = true;
            if (!ReferenceEquals(BatchList.ItemsSource, rows)) BatchList.ItemsSource = rows;
            BatchList.SelectedIndex = picked;
            quietRows = false;
            if (picked >= 0 && picked < rows.Count) BatchList.ScrollIntoView(rows[picked]);
            SyncBatchSummary(rows);
            BatchProgress.Text = progress ?? "";
            BatchProgress.Visibility = Shows(!string.IsNullOrEmpty(progress));
        }

        internal void SyncBatchSummary(IList<ExifRow> rows)
        {
            int gps = rows.Count(r => r.HasGps), trailers = rows.Count(r => r.TrailerBytes > 0), reading = rows.Count(r => !r.Read);
            BatchSummary.Text = rows.Count + " files queued · " + gps + " with GPS · " + trailers + " with trailers" + (reading > 0 ? " (" + reading + " still being read)" : "") + ". Click any row to inspect.";
        }

        internal void HideBatch() => BatchPanel.Visibility = Visibility.Collapsed;

        // a file's metadata; `preview` is null when Windows can't show the picture
        internal void ShowFile(ImageMetadata m, BitmapSource preview, bool previewWanted = true)
        {
            meta = m;
            Message.Visibility = Visibility.Collapsed;
            Body.Visibility = Visibility.Visible;
            PreviewFrame.Visibility = Shows(preview != null);
            NoPreview.Visibility = Shows(preview == null && previewWanted);
            Preview.Source = preview;
            CheckBanner.Visibility = Visibility.Collapsed;
            Narrative.Text = ExifReader.Narrative(m);

            GpsAlert.Visibility = Shows(m.Gps != null);
            if (m.Gps != null) GpsText.Text = ExifReader.GpsDms(m.Gps) + " (" + ExifReader.GpsDecimal(m.Gps) + ")";
            CopyGpsButton.Content = "Copy coordinates";

            BitmapSource thumb = m.HasThumbnail && m.Thumbnail != null ? Decode(m.Thumbnail.Data) : null;
            ThumbAlert.Visibility = Shows(m.HasThumbnail && m.Thumbnail != null);
            Thumbnail.Source = thumb;
            Thumbnail.Visibility = Shows(thumb != null);

            TrailerAlert.Visibility = Shows(m.TrailerBytes > 0);
            if (m.TrailerBytes > 0)
                TrailerText.Text = JsText.Grouped(m.TrailerBytes) + (m.Video != null
                    ? " bytes of trailer data follow the video's last box (a phone's own data, like Samsung's). These will be safely stripped."
                    : " bytes of trailer data follow the image stream (e.g. motion photo MP4 video, Samsung trailer, or Ultra HDR gain map). These will be safely stripped.");

            // AI markers: only what the file says about AI
            AiMarkerReport ai = m.AiDetection;
            AiAlert.Visibility = Shows(ai != null && ai.Detected);
            if (ai != null && ai.Detected)
            {
                AiTitle.Text = ai.Kind == "edited" ? "🤖 AI Editing Marker Found" : "🤖 AI Generation Marker Found";
                AiText.Text = ai.Summary + (!string.IsNullOrEmpty(ai.PromptOrParams) ? " Prompt: \"" + ai.PromptOrParams + "\"" : "");
                AiEvidence.Text = string.Join(" · ", ai.Evidence);
            }
            // Content Credentials that say nothing about AI
            ContentCredentials cred = ai?.Credentials;
            C2paNote.Visibility = Shows(cred != null && cred.AiClaim == null);
            if (cred != null && cred.AiClaim == null)
                C2paText.Text = "This file carries Content Credentials (C2PA) in its " + cred.Where + (!string.IsNullOrEmpty(cred.Generator) ? ", written by " + cred.Generator : "") +
                    ": a signed record of where the picture came from. Nothing in them says AI made it.";
            RenderTable();
        }

        // a JPEG thumbnail (or a video's cover picture), or null when Windows can't read it
        static BitmapSource Decode(byte[] bytes)
        {
            try
            {
                BitmapDecoder decoder = BitmapDecoder.Create(new MemoryStream(bytes, false), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                BitmapSource frame = decoder.Frames[0];
                if (frame.CanFreeze) frame.Freeze();
                return frame;
            }
            catch (Exception x) when (x is NotSupportedException || x is FileFormatException || x is ArgumentException || x is IOException || x is ExternalException || x is InvalidOperationException || x is OverflowException)
            {
                return null;
            }
        }

        // the Verified Clean check of the file just saved
        internal void ShowSelfCheck(SelfCheckReport r, bool video)
        {
            CheckBanner.Visibility = Visibility.Visible;
            CheckBanner.SetResourceReference(Border.BorderBrushProperty, r.IsClean ? "Good" : "Bad");
            CheckBadge.SetResourceReference(TextBlock.ForegroundProperty, r.IsClean ? "Good" : "Bad");
            CheckBadge.Text = SelfCheckBadge(r);
            CheckSummary.Text = r.Summary;
            string saved = r.BytesSaved > 0 ? " · Saved " + JsText.Grouped(r.BytesSaved) + " bytes" : "";
            CheckStats.Text = video
                ? "Original: " + Megabytes(r.OriginalSize) + " → Clean: " + Megabytes(r.ScrubbedSize) + saved + " · " + (r.SamplesUntouched == true ? "The same picture and sound, byte for byte" : "The picture or sound changed")
                : "Original: " + JsText.Fixed(r.OriginalSize / 1024.0, 1) + " KB → Clean: " + JsText.Fixed(r.ScrubbedSize / 1024.0, 1) + " KB" + saved + " · Bit-identical pixels";
        }

        static string Megabytes(long bytes) => bytes < 1024 * 1024 ? JsText.Fixed(bytes / 1024.0, 1) + " KB" : JsText.Fixed(bytes / 1048576.0, 1) + " MB";

        // the website's selfCheckBadge: it says clean only when the check found nothing left
        internal static string SelfCheckBadge(SelfCheckReport r) => r.IsClean ? "✓ Verified Clean" : "⚠ Not clean: metadata left";

        void OnSummary(object sender, RoutedEventArgs e) => SwitchView(false);

        void OnAllTags(object sender, RoutedEventArgs e) => SwitchView(true);

        internal void SwitchView(bool tags)
        {
            allTags = tags;
            SummaryButton.IsChecked = !tags;
            TagsButton.IsChecked = tags;
            Filter.Visibility = Shows(tags);
            if (tags && IsVisible) Filter.Focus();
            RenderTable();
        }

        internal void SetFilter(string text) => Filter.Text = text;

        void OnFilter(object sender, TextChangedEventArgs e)
        {
            filter = Filter.Text;
            RenderTable();
        }

        void OnCopyGps(object sender, RoutedEventArgs e) => CopyGps();

        // the coordinates Copy coordinates hands over: two numbers, for a map
        internal string GpsCopyText => meta?.Gps == null ? null : ExifReader.GpsDecimal(meta.Gps);

        // what goes onto the clipboard: never Windows' cloud clipboard or its history (Win+V), a
        // place stays on this PC and goes when something else is copied
        internal static DataObject GpsData(string text)
        {
            var data = new DataObject();
            data.SetText(text, TextDataFormat.UnicodeText);
            Exports.KeepOffTheCloud(data);
            data.SetData("CanIncludeInClipboardHistory", new MemoryStream(BitConverter.GetBytes(0)), false);
            return data;
        }

        // if the clipboard won't take them, they're said
        internal bool CopyGps()
        {
            string text = GpsCopyText;
            if (text == null) return false;
            try
            {
                Clipboard.SetDataObject(GpsData(text), true);
                CopyGpsButton.Content = "Copied!";
                copiedTimer.Stop();
                copiedTimer.Start();
                return true;
            }
            catch (ExternalException)
            {
                OnStatus("Couldn't copy. The coordinates are " + text);
                return false;
            }
        }

        // ---- the Metadata Inspector's table ----

        TextBlock Cell(string text, int column, bool header, bool muted)
        {
            var t = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(10, 5, 10, 5) };
            if (header)
            {
                t.FontWeight = FontWeights.SemiBold;
                t.SetResourceReference(TextBlock.ForegroundProperty, "Dim");
            }
            else if (muted) t.SetResourceReference(TextBlock.ForegroundProperty, "Dim");
            Grid.SetColumn(t, column);
            return t;
        }

        void AddRow(GridLength[] widths, bool header, params (string Text, bool Muted)[] cells)
        {
            var g = new Grid();
            foreach (GridLength w in widths) g.ColumnDefinitions.Add(new ColumnDefinition { Width = w });
            for (int i = 0; i < cells.Length; i++) g.Children.Add(Cell(cells[i].Text, i, header, cells[i].Muted));
            if (Table.Children.Count > 0)
            {
                var line = new Border { BorderThickness = new Thickness(0, 1, 0, 0) };
                line.SetResourceReference(Border.BorderBrushProperty, "Line");
                Table.Children.Add(line);
            }
            Table.Children.Add(g);
        }

        static readonly GridLength[] two = { new GridLength(190), new GridLength(1, GridUnitType.Star) };
        static readonly GridLength[] four = { new GridLength(84), new GridLength(200), new GridLength(84), new GridLength(1, GridUnitType.Star) };

        static bool IsNone(string v) => v == "None" || v == "No" || v == "0 bytes";

        // a tag's value for the list (a damaged file's value the browser can't make reads as that here)
        static string Value(ParsedExifTag t)
        {
            try
            {
                return t.DisplayValue;
            }
            catch (ExifRangeException)
            {
                return "(past the end of the file: damaged)";
            }
        }

        internal void RenderTable()
        {
            Table.Children.Clear();
            if (meta == null) return;
            if (meta.Format == "unknown")
            {
                // nothing was read, so no "None" that would read as "checked, none"
                AddRow(two, true, ("Field", false), ("Value", false));
                AddRow(two, false, ("Format", false), (meta.Video != null ? "Can't read this kind of video" : "Can't read this kind of file", true));
                return;
            }
            if (allTags)
            {
                string q = JsText.Lower(JsText.Trim(filter));
                List<ParsedExifTag> filtered = q.Length == 0 ? meta.AllTags : meta.AllTags.Where(t =>
                    JsText.Lower(t.Name).Contains(q) || JsText.Lower(t.TagHex).Contains(q) || JsText.Lower(t.Category).Contains(q) || JsText.Lower(Value(t)).Contains(q)).ToList();
                AddRow(four, true, ("Tag", false), ("Name", false), ("Category", false), ("Value", false));
                foreach (ParsedExifTag t in filtered.Take(MaxTableRows)) AddRow(four, false, (t.TagHex, false), (t.Name, false), (t.Category, true), (Value(t), false));
                if (filtered.Count == 0) AddRow(two, false, (q.Length > 0 ? "No tags matching \"" + q + "\"" : "No raw EXIF/TIFF tags recorded.", true));
                if (filtered.Count > MaxTableRows) AddRow(two, false, ("Showing " + MaxTableRows + " of " + filtered.Count + " tags. Type in the filter to find the rest.", true));
                return;
            }
            string gps = meta.Gps != null ? ExifReader.GpsDms(meta.Gps) + " (" + ExifReader.GpsDecimal(meta.Gps) + ")" : "None";
            string N(string s) => string.IsNullOrEmpty(s) ? "None" : s;
            bool T(double? x) => x.HasValue && x.Value != 0 && !double.IsNaN(x.Value);
            var rows = new List<(string, string)>();
            GifDetails g = meta.Gif;
            VideoDetails v = meta.Video;
            if (g != null)
            {
                // a GIF has no camera fields at all: what it does hold
                rows.Add(("Format", "GIF (" + g.Version + ")"));
                rows.Add(("Size", g.Width + "×" + g.Height));
                rows.Add(("Frames", g.Frames.ToString()));
                rows.Add(("Loop", g.LoopCount == null ? "Plays once" : g.LoopCount == 0 ? "Forever" : g.LoopCount + " more time" + (g.LoopCount == 1 ? "" : "s")));
                rows.Add(("GPS Coordinates (XMP)", gps));
                rows.Add(("Comments", g.Comments > 0 ? g.Comments.ToString() : "None"));
                rows.Add(("XMP", meta.XmpText != null ? JsText.Grouped(meta.XmpText.Length) + " characters" : "None"));
                rows.Add(("Colour Profile", meta.HasIccProfile ? "Colour profile present" : "None"));
            }
            else if (v != null)
            {
                // a video (the PC's): what phones write, and what the picture is
                rows.Add(("Format", meta.Format == "mov" ? "MOV (QuickTime)" : "MP4" + (v.Brand.Trim().Length > 0 ? " (" + v.Brand.Trim() + ")" : "")));
                rows.Add(("Camera Make", N(meta.Make)));
                rows.Add(("Camera Model", N(meta.Model)));
                rows.Add(("Software", N(meta.Software)));
                rows.Add(("Date & Time", N(meta.DateTime)));
                rows.Add(("GPS Coordinates", gps));
                rows.Add(("Length", v.Seconds > 0 ? VideoMeta.Length(v.Seconds) : "None"));
                rows.Add(("Picture", v.Width > 0 ? v.Width + "×" + v.Height + (v.Rotation != 0 ? ", turned " + v.Rotation + "° to show it" : "") : "None"));
                rows.Add(("Timed Metadata Tracks", v.TimedMetadata.Count > 0 ? string.Join(", ", v.TimedMetadata) : "None"));
                rows.Add(("Artist / Copyright", N(string.Join(" / ", new[] { meta.Artist, meta.Copyright }.Where(s => !string.IsNullOrEmpty(s))))));
                rows.Add(("Embedded Thumbnail", meta.HasThumbnail ? "Yes (a cover picture)" : "No"));
            }
            else
            {
                rows.Add(("Format", JsText.Upper(meta.Format)));
                rows.Add(("Camera Make", N(meta.Make)));
                rows.Add(("Camera Model", N(meta.Model)));
                rows.Add(("Lens", N(meta.LensModel)));
                rows.Add(("Date & Time", N(meta.DateTime)));
                rows.Add(("Orientation", T(meta.Orientation) ? JsText.Num(meta.Orientation.Value) : "Standard (1)"));
                rows.Add(("GPS Coordinates", gps));
                rows.Add(("Shutter Speed", N(meta.ExposureTime)));
                rows.Add(("Aperture", T(meta.FNumber) ? "f/" + JsText.Num(meta.FNumber.Value) : "None"));
                rows.Add(("ISO", T(meta.Iso) ? JsText.Num(meta.Iso.Value) : "None"));
                rows.Add(("Focal Length", T(meta.FocalLength) ? JsText.Num(meta.FocalLength.Value) + " mm" : "None"));
                rows.Add(("Software", N(meta.Software)));
                rows.Add(("Artist / Copyright", N(string.Join(" / ", new[] { meta.Artist, meta.Copyright }.Where(s => !string.IsNullOrEmpty(s))))));
                rows.Add(("Colour Profile", meta.HasIccProfile ? "Colour profile present" : "None"));
                rows.Add(("Embedded Thumbnail", meta.HasThumbnail ? "Yes (may show the uncropped original)" : "No"));
            }
            rows.Add(("Trailing Bytes", meta.TrailerBytes > 0 ? JsText.Grouped(meta.TrailerBytes) + " bytes" : "0 bytes"));
            rows.Add(("Total Raw Tags", meta.RawTagCount + " metadata fields"));
            AddRow(two, true, ("Field", false), ("Value", false));
            foreach (var (k, value) in rows) AddRow(two, false, (k, false), (value, IsNone(value)));
        }
    }
}
