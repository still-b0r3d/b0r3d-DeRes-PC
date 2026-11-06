using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using B0r3dDeRes.Exif;

namespace B0r3dDeRes.Tests
{
    // The EXIF Scrubber's and Injector's tabs in the window: the inspector shows the engine's
    // reading, the saves are the website's files byte for byte, the batch (into a folder or a
    // ZIP, Stop), the form, the menus and the kept options.
    static partial class TestMain
    {
        // the EXIF tools have a file, and the inspector shows it (`name`: that one)
        static void WaitForExif(MainWindow w, string name = null)
        {
            Pump(() => w.ExifFile != null && (name == null || w.ExifFile.Name == name) && w.Inspector.Shown != null, 15000);
            Check(w.ExifFile != null && (name == null || w.ExifFile.Name == name), "the EXIF tools never got " + (name ?? "a file") + " (they have " + (w.ExifFile?.Name ?? "none") + ")");
        }

        // a file opened, and the EXIF tools on it (that very file: one just saved may have its name)
        static void OpenForExif(MainWindow w, string path)
        {
            ExifItem before = w.ExifFile;
            string full = Path.GetFullPath(path);
            w.OpenFile(path);
            Pump(() => w.ExifFile != null && w.ExifFile != before && string.Equals(w.ExifFile.Path, full, StringComparison.OrdinalIgnoreCase) && w.Inspector.Shown != null, 15000);
            Check(w.ExifFile != before && string.Equals(w.ExifFile?.Path, full, StringComparison.OrdinalIgnoreCase), "the EXIF tools never got " + full + " (they have " + (w.ExifFile?.Path ?? "none") + ")");
        }

        // a task the window runs, run to its end (the window's queue pumped meanwhile)
        static void Finish(Task t, int ms = 30000)
        {
            Pump(() => t.IsCompleted, ms);
            Check(t.IsCompleted, "still running after " + ms / 1000 + " s");
            if (t.IsFaulted) throw t.Exception.InnerException;
        }

        // the website's case for one of the files
        static Dictionary<string, object> ExifCase(string name) => L(ExifExpected["cases"]).Select(D).First(c => S(c["name"]) == name);

        // what the inspector shows is what the website read from the file
        static bool ShowsTheWebsitesReading(MainWindow w, string name) => Sha(Encoding.UTF8.GetBytes(DumpMeta(w.Inspector.Shown))) == S(ExifCase(name)["dumpSha"]);

        // the inspector shows what the engine reads from these bytes
        static bool ShowsTheWebsitesReadingOf(MainWindow w, byte[] bytes) => DumpMeta(w.Inspector.Shown) == DumpMeta(ExifReader.Inspect(bytes));

        // the hash in a scrub or inject result ("ok jpeg 333 <sha> …", "ok 333 <sha>")
        static string ShaIn(string result, int at) => result.Split(' ')[at];

        static string ExifCopy(string name, string folder = "exif-files")
        {
            string dir = Path.Combine(Work, folder);
            Directory.CreateDirectory(dir);
            string to = Path.Combine(dir, name);
            File.Copy(Path.Combine(ExifFolder, name), to, true);
            return to;
        }

        static string Kb(string path) => JsText.Fixed(new FileInfo(path).Length / 1024.0, 1) + " KB";

        [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
        static extern bool CreateHardLink(string link, string existing, IntPtr security);

        static void ScrubberInTheWindow()
        {
            MainWindow w = NewMain();
            WaitForPicture(w);
            WaitForExif(w, "rukus.png");
            w.Tabs.SelectedItem = w.ScrubTab;
            Pump(() => false, 60);
            Check(w.ScrubStageSlot.Child == w.Inspector && w.InjectStageSlot.Child == null, "the inspector should be on the scrubber's tab");
            // the sample, as the website reads it
            Check(ShowsTheWebsitesReading(w, "rukus.png"), "the sample isn't read as the website reads it");
            Check(w.Inspector.Narrative.Text == ExifReader.Narrative(w.Inspector.Shown) && w.Inspector.Narrative.Text.StartsWith("Taken on a b0r3d optics Rukus-Cam 95 Deluxe on 1995:08:24", StringComparison.Ordinal),
                "the sample's line: " + w.Inspector.Narrative.Text);
            List<string> rows = w.Inspector.TableRows.ToList();
            Check(rows[0] == "Field | Value" && rows.Contains("Camera Make | b0r3d optics") && rows.Contains("Embedded Thumbnail | Yes (may show the uncropped original)") &&
                rows.Contains("GPS Coordinates | 41°52'41.2\" N, 87°37'47.3\" W (alt 182m) (41.878113, -87.629799)"), "the sample's summary: " + string.Join("; ", rows));
            Check(w.Inspector.GpsAlert.Visibility == Visibility.Visible && w.Inspector.ThumbAlert.Visibility == Visibility.Visible && w.Inspector.Thumbnail.Source != null && w.Inspector.Preview.Source != null,
                "the sample's GPS, thumbnail and picture");
            Check(w.ScrubPictureInfo.Text == "rukus.png · " + Kb(Path.Combine(ExifFolder, "rukus.png")), "the scrubber's line: " + w.ScrubPictureInfo.Text);
            // the other tab takes the inspector over
            w.Tabs.SelectedItem = w.InjectTab;
            Pump(() => false, 60);
            Check(w.InjectStageSlot.Child == w.Inspector && w.ScrubStageSlot.Child == null, "the inspector should move to the injector's tab");
            w.Tabs.SelectedItem = w.ScrubTab;
            Pump(() => false, 60);

            // a phone photo: its place, its thumbnail, every tag, and the filter
            string phone = ExifCopy("phone-thumb.jpg");
            string dir = Path.GetDirectoryName(phone);
            OpenForExif(w, phone);
            Check(ShowsTheWebsitesReading(w, "phone-thumb.jpg"), "the phone photo isn't read as the website reads it");
            Check(w.Inspector.GpsAlert.Visibility == Visibility.Visible && w.Inspector.GpsText.Text == "0°0'0.0\" N, 0°0'0.0\" E (0.000000, 0.000000)" && w.Inspector.ThumbAlert.Visibility == Visibility.Visible,
                "the phone photo's place and thumbnail: " + w.Inspector.GpsText.Text);
            w.Inspector.SwitchView(true);
            int tags = w.Inspector.Shown.AllTags.Count;
            Check(tags > 0 && w.Inspector.TableRows.Count() == tags + 1 && w.Inspector.TableRows.First() == "Tag | Name | Category | Value", "All Tags: " + w.Inspector.TableRows.Count() + " rows for " + tags + " tags");
            w.Inspector.SetFilter("GPSlat");
            List<string> found = w.Inspector.TableRows.Skip(1).ToList();
            Check(found.Count == 2 && found.All(r => r.Contains("GPSLatitude")), "the filter: " + string.Join("; ", found));
            w.Inspector.SetFilter("no such tag");
            Check(w.Inspector.TableRows.Last() == "No tags matching \"no such tag\"", "no match: " + w.Inspector.TableRows.Last());
            w.Inspector.SetFilter("");
            w.Inspector.SwitchView(false);
            Check(w.Inspector.TableRows.First() == "Field | Value", "back to the summary");
            // Copy coordinates: the two numbers, kept off Windows' cloud clipboard
            Check(w.Inspector.GpsCopyText == "0.000000, 0.000000", "the coordinates to copy: " + w.Inspector.GpsCopyText);
            DataObject gps = ExifStage.GpsData(w.Inspector.GpsCopyText);
            Check((string)gps.GetData(DataFormats.UnicodeText) == "0.000000, 0.000000" && gps.GetData("CanUploadToCloudClipboard") is MemoryStream cloud && BitConverter.ToInt32(cloud.ToArray(), 0) == 0 &&
                gps.GetData("CanIncludeInClipboardHistory") is MemoryStream history && BitConverter.ToInt32(history.ToArray(), 0) == 0, "the coordinates would go to Windows' cloud clipboard or its history");

            // its clean copies are the website's, byte for byte, in each mode, checked from the disk
            var scrub = D(ExifCase("phone-thumb.jpg")["scrub"]);
            string all = Path.Combine(dir, "phone-thumb-clean.jpg");
            Finish(w.ScrubSaveTo(all));
            Check(Sha(File.ReadAllBytes(all)) == ShaIn(S(scrub["all"]), 3), "the clean copy isn't the website's");
            Check(w.Inspector.CheckBanner.Visibility == Visibility.Visible && w.Inspector.CheckBadge.Text == "✓ Verified Clean" && w.ScrubStatus.Text == "Cleaned and saved phone-thumb-clean.jpg." &&
                w.Inspector.CheckStats.Text.EndsWith(" · Bit-identical pixels", StringComparison.Ordinal), "the check: " + w.Inspector.CheckBadge.Text + " / " + w.ScrubStatus.Text);
            w.ScrubOrientationBox.IsChecked = false;
            Check(!w.ScrubOptions.PreserveOrientation, "the orientation box should be off");
            string bare = Path.Combine(dir, "bare.jpg");
            Finish(w.ScrubSaveTo(bare));
            Check(Sha(File.ReadAllBytes(bare)) == ShaIn(S(scrub["bare"]), 3), "without the orientation, it isn't the website's");
            w.ScrubOrientationBox.IsChecked = true;
            w.ScrubLocationButton.IsChecked = true;
            Check(w.ScrubOptions.Mode == "location_only" && w.ScrubOptions.PreserveOrientation && w.ScrubAllButton.IsChecked == false, "location only should be on");
            string where = Path.Combine(dir, "where.jpg");
            Finish(w.ScrubSaveTo(where));
            Check(Sha(File.ReadAllBytes(where)) == ShaIn(S(scrub["location"]), 3), "location only, it isn't the website's");
            // saved over a file that's there (the dialog asked): none of its hidden streams (a
            // download's web address) carried onto the clean copy, and nothing left beside it
            string downloaded = Path.Combine(dir, "downloaded.jpg");
            File.WriteAllText(downloaded, "an older file");
            File.WriteAllText(@"\\?\" + downloaded + ":Zone.Identifier", "[ZoneTransfer]\r\nZoneId=3\r\nHostUrl=https://example.com/photo.jpg\r\n");
            Finish(w.ScrubSaveTo(downloaded));
            Check(Sha(File.ReadAllBytes(downloaded)) == ShaIn(S(scrub["location"]), 3) && !File.Exists(@"\\?\" + downloaded + ":Zone.Identifier") &&
                !Directory.GetFiles(dir).Any(f => Path.GetFileName(f).StartsWith(".", StringComparison.Ordinal)), "saved over a downloaded file: " + w.ScrubStatus.Text);
            // never over the original, under its own name or another (a hard link's: the same file)
            byte[] before = File.ReadAllBytes(phone);
            Finish(w.ScrubSaveTo(phone));
            Check(File.ReadAllBytes(phone).SequenceEqual(before) && w.ScrubStatus.Text == "That's the original's own name: pick another, so the original stays as it is.", "the original was written over: " + w.ScrubStatus.Text);
            string link = Path.Combine(dir, "same-file.jpg");
            if (File.Exists(link)) File.Delete(link);
            Check(CreateHardLink(link, phone, IntPtr.Zero), "couldn't make a hard link for the test");
            w.ScrubStatus.Text = "";
            Finish(w.ScrubSaveTo(link));
            Check(File.ReadAllBytes(phone).SequenceEqual(before) && w.ScrubStatus.Text.StartsWith("That's the original's own name", StringComparison.Ordinal), "the original was written over through a hard link: " + w.ScrubStatus.Text);
            File.Delete(link);
            w.ScrubAllButton.IsChecked = true;

            // a HEIC Windows can't show: read, no picture, and why it isn't scrubbed (in this PC's words)
            string heic = ExifCopy("phone.heic");
            OpenForExif(w, heic);
            Check(ShowsTheWebsitesReading(w, "phone.heic") && w.Inspector.NoPreview.Visibility == Visibility.Visible && w.Inspector.TableRows.Contains("Format | HEIC") && w.Inspector.GpsAlert.Visibility == Visibility.Visible, "the HEIC");
            string heicOut = Path.Combine(dir, "phone-clean.heic");
            Finish(w.ScrubSaveTo(heicOut));
            Check(w.ScrubStatus.Text == "Failed to scrub: HEIC files cannot be safely scrubbed at the byte level without re-encoding. Convert to JPEG or WebP to scrub metadata." && !File.Exists(heicOut),
                "the HEIC's words: " + w.ScrubStatus.Text);

            // a video: where it was filmed and on what, its clean copy checked, still turned the same
            OpenForExif(w, Clip("meta-iphone.mov"));
            rows = w.Inspector.TableRows.ToList();
            Check(rows.Contains("Format | MOV (QuickTime)") && rows.Contains("Camera Model | iPhone 15 Pro") && rows.Contains("Picture | 160×96, turned 90° to show it") && w.Inspector.Preview.Source != null &&
                (string)w.ScrubSaveButton.Content == "_Scrub & save a clean video…", "the video in the scrubber: " + string.Join("; ", rows));
            string clean = Path.Combine(dir, "meta-iphone-clean.mov");
            Finish(w.ScrubSaveTo(clean));
            ImageMetadata after = VideoMeta.Inspect(clean);
            Check(after.Gps == null && string.IsNullOrEmpty(after.Model) && after.Video.Rotation == 90 && w.Inspector.CheckBadge.Text == "✓ Verified Clean" &&
                w.Inspector.CheckStats.Text.EndsWith(" · The same picture and sound, byte for byte", StringComparison.Ordinal) && w.ScrubStatus.Text == "Cleaned and saved meta-iphone-clean.mov.",
                "the clean video: " + w.Inspector.CheckSummary.Text + " / " + w.ScrubStatus.Text);
            w.Close();
        }

        static void ExifBatchInTheWindow()
        {
            MainWindow w = NewMain();
            WaitForPicture(w);
            WaitForExif(w);
            w.Tabs.SelectedItem = w.ScrubTab;
            Pump(() => false, 60);
            // the website's batch, under the website's names (each in a folder of its own: two differ only in case)
            var spec = L(ExifExpected["batchZips"]).Select(D).ToList();
            string src = Path.Combine(Work, "exif-batch");
            Fresh(src);
            var paths = new List<string>();
            foreach (object f in L(spec[0]["files"]))
            {
                string folder = Path.Combine(src, paths.Count.ToString());
                Directory.CreateDirectory(folder);
                string to = Path.Combine(folder, S(L(f)[0]));
                File.Copy(Path.Combine(ExifFolder, S(L(f)[1])), to);
                paths.Add(to);
            }
            var shas = paths.ToDictionary(p => p, p => Sha(File.ReadAllBytes(p)));
            w.AddToBatch(paths);
            Pump(() => !w.ExifReading && w.ExifRowShown != null, 15000);
            Check(w.ExifBatch.Select(r => r.Name).SequenceEqual(paths.Select(Path.GetFileName)) && w.ExifBatch.All(r => r.Status == "Queued"),
                "the batch's rows: " + string.Join(", ", w.ExifBatch.Select(r => r.Name + " " + r.Status)));
            Check(w.ScrubZipButton.Visibility == Visibility.Visible && w.ScrubIntoButton.Visibility == Visibility.Visible && w.ScrubSaveButton.Visibility == Visibility.Collapsed &&
                w.Inspector.BatchPanel.Visibility == Visibility.Visible && (string)w.ScrubZipButton.Content == "Clean the batch into a _ZIP (8 files)…", "the batch's buttons");
            Check(w.ScrubPictureInfo.Text.StartsWith("8 photos in batch (", StringComparison.Ordinal) && w.Inspector.BatchSummary.Text.StartsWith("8 files queued · ", StringComparison.Ordinal),
                "the batch's lines: " + w.ScrubPictureInfo.Text + " / " + w.Inspector.BatchSummary.Text);
            w.Inspector.BatchList.SelectedIndex = 2;
            Pump(() => w.ExifRowShown?.Name == "rukus.png" && w.Inspector.Shown?.Make == "b0r3d optics", 10000);
            Check(w.Inspector.Shown?.Make == "b0r3d optics" && w.BatchIndex == 2, "the row picked should be inspected");
            // the ZIP, byte for byte the website's, in each mode (the HEIC left out, and said)
            foreach (var z in spec)
            {
                string mode = S(z["mode"]);
                (mode == "location" ? w.ScrubLocationButton : w.ScrubAllButton).IsChecked = true;
                string zip = Path.Combine(Work, "exif-" + mode + ".zip");
                Finish(w.CleanBatchToZip(zip));
                Check(File.Exists(zip) && Sha(File.ReadAllBytes(zip)) == S(z["sha"]), "the " + mode + " ZIP isn't the website's (" + (File.Exists(zip) ? new FileInfo(zip).Length + " bytes, not " + z["length"] : "none") + "): " + w.ScrubStatus.Text);
                ExifRow heicRow = w.ExifBatch.Single(r => r.Name == "phone.heic");
                Check(heicRow.Status == "Error" && heicRow.StatusTip.StartsWith("HEIC files cannot be safely scrubbed at the byte level without", StringComparison.Ordinal) &&
                    w.ExifBatch.Where(r => r != heicRow).All(r => r.Report != null && r.CleanName != null), "the rows after the ZIP: " + string.Join(", ", w.ExifBatch.Select(r => r.Name + " " + r.Status)));
                bool allClean = w.ExifBatch.All(r => r.Report == null || r.Report.IsClean);
                Check(w.ScrubStatus.Text == (allClean ? "Bundled & saved exif-" + mode + ".zip (7 clean photos, " + Kb(zip) + "), but 1 couldn't be cleaned: see the table."
                    : "Saved exif-" + mode + ".zip (7 photos, " + Kb(zip) + "), but " + w.ExifBatch.Count(r => r.Report != null && !r.Report.IsClean) + " not fully clean and 1 couldn't be cleaned: see the table."),
                    "the ZIP's words: " + w.ScrubStatus.Text);
            }
            w.ScrubAllButton.IsChecked = true;
            // into a folder: named as the ZIP names them, nothing there written over, the originals as they were
            string into = Path.Combine(Work, "exif-into");
            Fresh(into);
            File.WriteAllText(Path.Combine(into, "phone-clean.jpg"), "already here");
            Finish(w.CleanBatchInto(into));
            Check(File.ReadAllText(Path.Combine(into, "phone-clean.jpg")) == "already here", "a file already there was written over");
            var names = Directory.GetFiles(into).Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal).ToList();
            var want = new[] { "busy-clean.gif", "meta-clean.webp", "phone-clean-2.jpg", "phone-clean.jpg", "Phone-clean-3.jpg", "photo-clean.jpg", "rukus-clean.png", "Ünïcödé 写真-clean.png" }.OrderBy(n => n, StringComparer.Ordinal);
            Check(names.SequenceEqual(want), "the folder's files: " + string.Join(", ", names));
            Check(w.ExifBatch.Where(r => r.Report != null).All(r => File.Exists(Path.Combine(into, r.CleanName))) && w.ExifBatch[0].CleanName == "phone-clean-2.jpg", "the rows' clean names");
            Check(File.ReadAllBytes(Path.Combine(into, "phone-clean-2.jpg")).SequenceEqual(Scrubber.Scrub(File.ReadAllBytes(paths[0]), w.ScrubOptions).Data), "a clean copy isn't the engine's");
            Check(paths.All(p => Sha(File.ReadAllBytes(p)) == shas[p]), "an original changed");
            Check(w.ScrubStatus.Text.StartsWith("Cleaned and saved 7 photos into exif-into (", StringComparison.Ordinal) && w.ScrubStatus.Text.EndsWith(" KB), but 1 couldn't be cleaned: see the table.", StringComparison.Ordinal),
                "the folder's words: " + w.ScrubStatus.Text);
            // a video joins them, cleaned and checked
            w.AddToBatch(new[] { Clip("meta-android.mp4") });
            Pump(() => !w.ExifReading, 10000);
            Check(w.ExifBatch.Count == paths.Count + 1 && (string)w.ScrubZipButton.Content == "Clean the batch into a _ZIP (9 files)…" && w.ScrubPictureInfo.Text.StartsWith("9 files in batch (", StringComparison.Ordinal),
                "a video added: " + w.ScrubPictureInfo.Text);
            string intoVideo = Path.Combine(Work, "exif-into-video");
            Fresh(intoVideo);
            Finish(w.CleanBatchInto(intoVideo), 60000);
            ExifRow video = w.ExifBatch.Last();
            string cleanVideo = Path.Combine(intoVideo, "meta-android-clean.mp4");
            Check(video.Report?.IsClean == true && video.Report.SamplesUntouched == true && video.CleanName == "meta-android-clean.mp4" && File.Exists(cleanVideo) && VideoMeta.Inspect(cleanVideo).Gps == null,
                "the video in the batch: " + video.Status + " " + video.StatusTip);
            Check(w.ScrubStatus.Text.StartsWith("Cleaned and saved 8 files into exif-into-video (", StringComparison.Ordinal), "the words with a video: " + w.ScrubStatus.Text);

            // a ZIP named as one of the batch's own files: refused, the file as it was
            Finish(w.CleanBatchToZip(paths[2]));
            Check(w.ScrubStatus.Text == "That's one of the batch's own files: pick another name, so it stays as it is." && Sha(File.ReadAllBytes(paths[2])) == shas[paths[2]], "a ZIP over a batch's file: " + w.ScrubStatus.Text);
            // a video gone since it was added: its row says so, the rest go into the ZIP
            w.ClearExifBatch();
            string goneDir = Path.Combine(Work, "exif-gone");
            Fresh(goneDir);
            string goneVideo = Path.Combine(goneDir, "gone.mp4"), stays = Path.Combine(goneDir, "stays.jpg");
            File.Copy(Clip("meta-android.mp4"), goneVideo);
            File.Copy(Path.Combine(ExifFolder, "phone.jpg"), stays);
            w.AddToBatch(new[] { stays, goneVideo });
            Pump(() => !w.ExifReading && w.ExifRowShown != null, 10000);
            File.Delete(goneVideo);
            string goneZip = Path.Combine(Work, "exif-gone.zip");
            Finish(w.CleanBatchToZip(goneZip));
            Check(File.Exists(goneZip) && w.ExifBatch[1].Status == "Error" && w.ExifBatch[1].StatusTip.StartsWith("it couldn't be read: ", StringComparison.Ordinal) && w.ExifBatch[0].Status == "✓ Cleaned" &&
                w.ScrubStatus.Text.EndsWith("but 1 couldn't be cleaned: see the table.", StringComparison.Ordinal), "a video gone since it was added: " + w.ExifBatch[1].StatusTip + " / " + w.ScrubStatus.Text);

            // Clear batch, then Stop: what's cleaned and checked stays, nothing half-written or unchecked is left
            w.ClearExifBatch();
            Check(w.ExifBatch.Count == 0 && w.Inspector.BatchPanel.Visibility == Visibility.Collapsed && w.ScrubSaveButton.Visibility == Visibility.Visible && w.ScrubZipButton.Visibility == Visibility.Collapsed, "Clear batch");
            string many = Path.Combine(Work, "exif-many");
            Fresh(many);
            var copies = Enumerable.Range(1, 12).Select(i =>
            {
                string p = Path.Combine(many, "cat " + i.ToString("00") + ".jpg");
                File.Copy(Path.Combine(ExifFolder, "phone.jpg"), p);
                return p;
            }).ToList();
            w.AddToBatch(copies);
            Pump(() => !w.ExifReading, 15000);
            string stopped = Path.Combine(Work, "exif-stopped");
            Fresh(stopped);
            w.OnBatchStep = i =>
            {
                if (i == 3) w.StopExif();
            };
            Finish(w.CleanBatchInto(stopped));
            string[] left = Directory.GetFiles(stopped);
            Check(w.ScrubStatus.Text == "Stopped: 4 photos cleaned and checked before it stopped; nothing half-written or unchecked is left." && left.Length == 4 &&
                left.All(f => !Path.GetFileName(f).StartsWith(".", StringComparison.Ordinal)), "Stop: " + left.Length + " files, " + w.ScrubStatus.Text);
            Check(w.ExifBatch.Take(4).All(r => r.Status == "✓ Cleaned") && w.ExifBatch.Skip(4).All(r => r.Status == "Queued"), "the rows after Stop: " + string.Join(", ", w.ExifBatch.Select(r => r.Status)));
            // a ZIP stopped leaves no ZIP, and none of its rows says it was cleaned
            string stoppedZip = Path.Combine(Work, "stopped.zip");
            Finish(w.CleanBatchToZip(stoppedZip));
            Check(!File.Exists(stoppedZip) && !Directory.GetFiles(Work, ".stopped.zip.*").Any() && w.ScrubStatus.Text == "Stopped: no ZIP was saved." && w.ExifBatch.All(r => r.Status == "Queued"),
                "a stopped ZIP: " + w.ScrubStatus.Text);
            // a video stopped as it's cleaned leaves nothing either
            w.ClearExifBatch();
            w.AddToBatch(new[] { Clip("meta-iphone.mov") });
            Pump(() => !w.ExifReading, 10000);
            string stoppedVideo = Path.Combine(Work, "exif-stopped-video");
            Fresh(stoppedVideo);
            w.OnBatchStep = i => w.StopExif();
            Finish(w.CleanBatchInto(stoppedVideo));
            w.OnBatchStep = null;
            Check(Directory.GetFiles(stoppedVideo).Length == 0 && w.ScrubStatus.Text == "Stopped: 0 files cleaned and checked before it stopped; nothing half-written or unchecked is left.",
                "a stopped video: " + Directory.GetFiles(stoppedVideo).Length + " files, " + w.ScrubStatus.Text);

            // a single file opened is single again (the website's: it replaces the batch)
            OpenForExif(w, ExifCopy("bare-real.jpg"));
            Check(w.ExifBatch.Count == 0 && w.Inspector.BatchPanel.Visibility == Visibility.Collapsed && w.ScrubSaveButton.Visibility == Visibility.Visible, "one file after a batch");
            // two files and their folder dropped on the tab: the folder's files, each once
            w.OpenData(new DataObject(DataFormats.FileDrop, new[] { copies[0], copies[1], many }), "nothing");
            Pump(() => !w.ExifReading && w.ExifBatch.Count > 0, 15000);
            Check(w.ExifBatch.Select(r => r.Path).SequenceEqual(copies), "a drop of two files and their folder: " + w.ExifBatch.Count + " rows");
            w.Close();
        }

        static void InjectorInTheWindow()
        {
            MainWindow w = NewMain();
            WaitForPicture(w);
            WaitForExif(w, "rukus.png");
            w.Tabs.SelectedItem = w.InjectTab;
            Pump(() => false, 60);
            Check(w.InjectStageSlot.Child == w.Inspector && ShowsTheWebsitesReading(w, "rukus.png"), "the inspector on the injector's tab");
            // the sample's own fields, as the website reads them
            Check(FieldsJson(w.ReadForm()) == S(ExifCase("rukus.png")["fields"]), "the sample's form: " + FieldsJson(w.ReadForm()));
            Check(w.InjectPictureInfo.Text == "rukus.png · " + Kb(Path.Combine(ExifFolder, "rukus.png")) && w.InjectFormatNote.Visibility == Visibility.Collapsed && w.InjectSaveButton.IsEnabled,
                "the injector's line: " + w.InjectPictureInfo.Text);

            // a phone block: the form is the photo's, an Artist typed in saves the website's file byte for byte
            string phone = ExifCopy("phone-block.jpg");
            OpenForExif(w, phone);
            var inject = D(ExifCase("phone-block.jpg")["inject"]);
            Check(FieldsJson(w.ReadForm()) == S(ExifCase("phone-block.jpg")["fields"]), "the form isn't what the website reads: " + FieldsJson(w.ReadForm()));
            w.InjectArtist.Text = "me";
            string out1 = Path.Combine(Path.GetDirectoryName(phone), "phone-block-injected.jpg");
            Finish(w.InjectSaveTo(out1));
            Check(Sha(File.ReadAllBytes(out1)) == ShaIn(S(inject["artist"]), 2), "the injected photo isn't the website's");
            Check(w.InjectStatus.Text == "Saved phone-block-injected.jpg: 1 field changed." && w.ExifFile.Path == out1 && w.ReadForm()["artist"] == "me" && w.LoadedFields["artist"] == "me" &&
                ShowsTheWebsitesReadingOf(w, File.ReadAllBytes(out1)), "after the save: " + w.InjectStatus.Text);
            Check(File.ReadAllBytes(phone).SequenceEqual(File.ReadAllBytes(Path.Combine(ExifFolder, "phone-block.jpg"))), "the original changed");
            // a refused value: said, its field marked, nothing saved
            w.InjectShutter.Text = "abc";
            Check(w.FormEdits() == null && w.InjectStatus.Text == "Shutter speed: a time in seconds like 1/250, 0.5 or 2." &&
                w.InjectShutter.ReadLocalValue(Control.BorderBrushProperty) != DependencyProperty.UnsetValue, "a bad shutter speed: " + w.InjectStatus.Text);
            w.InjectShutter.Text = w.LoadedFields["shutter"];
            Check(w.InjectShutter.ReadLocalValue(Control.BorderBrushProperty) == DependencyProperty.UnsetValue, "the mark should go once it's typed in again");
            Check(w.FormEdits() == null && w.InjectStatus.Text == "Nothing has changed yet: every field still shows what the photo has.", "nothing changed: " + w.InjectStatus.Text);
            // a device preset: on the sample (every field it fills is new there) the website's file, its extras and all
            string sample = ExifCopy("rukus.png");
            OpenForExif(w, sample);
            ComboBoxItem iphone = w.InjectPreset.Items.OfType<ComboBoxItem>().First(i => (string)i.Tag == "iphone-15-pro");
            w.InjectPreset.SelectedItem = iphone;
            Check(w.PresetPicked?.Id == "iphone-15-pro" && w.ReadForm()["model"] == "iPhone 15 Pro" &&
                w.InjectStatus.Text == "Filled in as " + DevicePresets.ShortLabel(DevicePresets.ById("iphone-15-pro")) + ": camera, lens and exposure. Inject to save it.", "the preset: " + w.InjectStatus.Text);
            string out2 = Path.Combine(Path.GetDirectoryName(sample), "iphone.png");
            Finish(w.InjectSaveTo(out2));
            Check(Sha(File.ReadAllBytes(out2)) == ShaIn(S(D(ExifCase("rukus.png")["inject"])["iphone"]), 2), "the preset's photo isn't the website's");
            Check(w.PresetPicked == null && w.InjectPreset.SelectedIndex == 0, "the preset should be forgotten once the form starts from the new photo");
            // on the phone photo, its own f/1.78 is the preset's: left alone, as every unchanged field is
            OpenForExif(w, phone);
            w.InjectPreset.SelectedItem = iphone;
            var presetEdits = w.FormEdits();
            Check(presetEdits != null && presetEdits.Value.Edits.FNumber == null && presetEdits.Value.Edits.Make != null && presetEdits.Value.Edits.ForgetCamera, "the preset's edits: " + Canon(presetEdits.Value.Edits));
            string outPhone = Path.Combine(Path.GetDirectoryName(phone), "iphone.jpg");
            Finish(w.InjectSaveTo(outPhone));
            Check(File.ReadAllBytes(outPhone).SequenceEqual(Injector.Inject(File.ReadAllBytes(phone), presetEdits.Value.Edits)), "the preset's phone photo isn't the engine's");
            // the XMP box: ticked for a block with its own copy of the place; unticked, the block stays
            string xmp = ExifCopy("phone-block-xmp.jpg");
            var injectXmp = D(ExifCase("phone-block-xmp.jpg")["inject"]);
            OpenForExif(w, xmp);
            Check(w.InjectXmpBox.Visibility == Visibility.Visible && w.InjectXmpBox.IsChecked == true && w.InjectXmpText.Text.StartsWith("The photo's XMP block keeps its own copy of the location", StringComparison.Ordinal),
                "the XMP box: " + w.InjectXmpText.Text);
            w.InjectArtist.Text = "me";
            string out3 = Path.Combine(Path.GetDirectoryName(xmp), "xmp-injected.jpg");
            Finish(w.InjectSaveTo(out3));
            Check(Sha(File.ReadAllBytes(out3)) == ShaIn(S(injectXmp["noxmp"]), 2) && w.InjectStatus.Text == "Saved xmp-injected.jpg: 1 field changed, XMP block removed.", "XMP removed: " + w.InjectStatus.Text);
            OpenForExif(w, xmp);
            w.InjectXmpBox.IsChecked = false;
            w.InjectArtist.Text = "me";
            string out4 = Path.Combine(Path.GetDirectoryName(xmp), "xmp-kept.jpg");
            Finish(w.InjectSaveTo(out4));
            Check(Sha(File.ReadAllBytes(out4)) == ShaIn(S(injectXmp["artist"]), 2), "XMP kept: it isn't the website's");
            // b0r3d Defaults name this program
            w.InjectDefaults();
            InjectorFields d = w.ReadForm();
            Check(d["software"] == "b0r3d DeRes v" + Program.Version && d["make"] == "b0r3d optics" && Injector.ParseDate(d["dateTime"]) != null && w.PresetPicked == null, "the defaults: " + FieldsJson(d));
            // what it can't write into says so up front
            OpenForExif(w, ExifCopy("busy.gif"));
            Check(w.InjectFormatNote.Visibility == Visibility.Visible && !w.InjectSaveButton.IsEnabled && w.InjectFormatNote.Text == "GIFs can't hold EXIF, so there's nothing to edit here.", "the GIF's note: " + w.InjectFormatNote.Text);
            OpenForExif(w, ExifCopy("phone.heic"));
            Check(!w.InjectSaveButton.IsEnabled && w.InjectFormatNote.Text == "The injector writes into JPEG and PNG files. This one is HEIC, so its EXIF can be read in the Scrubber but not changed here.",
                "the HEIC's note: " + w.InjectFormatNote.Text);
            OpenForExif(w, Clip("meta-android.mp4"));
            Check(!w.InjectSaveButton.IsEnabled && w.InjectFormatNote.Text == "The injector writes into JPEG and PNG files. This one is MP4, so its metadata can be read in the Scrubber but not changed here.",
                "the video's note: " + w.InjectFormatNote.Text);
            Check(!Commands.SavePng.CanExecute(null, w), "Ctrl+S should do nothing for a video on the injector's tab");
            // a save for a photo no longer shown (another opened while the dialog was open): refused
            OpenForExif(w, ExifCopy("phone-block.jpg"));
            ExifItem stale = w.ExifFile;
            OpenForExif(w, ExifCopy("png-exif.png"));
            string never = Path.Combine(Work, "exif-files", "never.jpg");
            Finish(w.InjectSaveTo(never, null, stale));
            Check(!File.Exists(never) && w.InjectStatus.Text == "The file changed while the Save dialog was open, so nothing was saved: save again.", "a save for a photo no longer shown: " + w.InjectStatus.Text);
            // in a batch, the edited photo takes its row's place (and nothing saves while a row is read)
            string a = ExifCopy("phone-block.jpg", "exif-inject-batch"), b = ExifCopy("png-exif.png", "exif-inject-batch");
            w.AddToBatch(new[] { a, b });
            Pump(() => !w.ExifReading && w.ExifRowShown?.Name == "phone-block.jpg", 10000);
            w.Inspector.BatchList.SelectedIndex = 1;
            Check(w.ExifRowLoading && !w.InjectSaveButton.IsEnabled && !Commands.SavePng.CanExecute(null, w), "saving should wait while a row is read");
            Pump(() => w.ExifRowShown?.Name == "png-exif.png", 10000);
            Check(!w.ExifRowLoading && w.InjectSaveButton.IsEnabled, "saving should come back once the row is read");
            Check(FieldsJson(w.ReadForm()) == S(ExifCase("png-exif.png")["fields"]), "the row's form: " + FieldsJson(w.ReadForm()));
            w.InjectMake.Text = "RowCam";
            string out5 = Path.Combine(Path.GetDirectoryName(b), "png-injected.png");
            Finish(w.InjectSaveTo(out5));
            Check(w.ExifBatch.Count == 2 && w.ExifBatch[1].Path == out5 && w.Inspector.Shown.Make == "RowCam" && w.ExifBatch[0].Path == a && File.ReadAllBytes(b).SequenceEqual(File.ReadAllBytes(Path.Combine(ExifFolder, "png-exif.png"))),
                "the batch's row after an injection: " + w.ExifBatch[1].Path);
            w.Close();
        }

        static void ExifMenusInTheWindow()
        {
            MainWindow w = NewMain();
            WaitForPicture(w);
            WaitForExif(w);
            List<MenuItem> top = w.MenuBar.Items.OfType<MenuItem>().ToList();
            string Shown(MenuItem m) => string.Join(" | ", m.Items.OfType<MenuItem>().Where(l => l.Visibility == Visibility.Visible).Select(l => (string)l.Header));
            foreach (bool batch in new[] { false, true })
            {
                if (batch)
                {
                    w.AddToBatch(new[] { Path.Combine(ExifFolder, "phone.jpg"), Path.Combine(ExifFolder, "rukus.png") });
                    Pump(() => !w.ExifReading && w.ExifRowShown != null, 10000);
                }
                foreach (TabItem tab in new[] { w.ScrubTab, w.InjectTab })
                {
                    w.Tabs.SelectedItem = tab;
                    Pump(() => false, 60);
                    bool scrub = tab == w.ScrubTab;
                    string where = tab.Header + (batch ? " with a batch" : "");
                    string save = scrub ? (batch ? "Clean the batch into a _ZIP… | Clean the batch into a folde_r… | C_lear the batch" : "_Save a clean copy…") : "_Save with the new EXIF…";
                    Check(Shown(top[0]) == "_Open picture or video… | Open a sa_mple | DeRes the scree_n… | _Add photos or videos to the batch… | Add a _folder to the batch… | " + save + " | E_xit",
                        where + "'s File menu: " + Shown(top[0]));
                    Check(Shown(top[1]) == "_Copy coordinates | _Paste picture or link | _Reset settings…", where + "'s Edit menu: " + Shown(top[1]));
                    Check(Shown(top[2]) == "_Look", where + "'s View menu: " + Shown(top[2]));
                    // Ctrl+S saves the tab's copy (a batch has its own buttons), Ctrl+C copies the coordinates, there's no picture to zoom
                    Check(Commands.SavePng.CanExecute(null, w) == (!scrub || !batch) && Commands.CopyPicture.CanExecute(null, w) && !Commands.ZoomIn.CanExecute(null, w) &&
                        !Commands.FullScreen.CanExecute(null, w) && !Commands.Compare.CanExecute(null, w), where + ": the commands");
                }
            }
            // the other tabs get their own lines back
            foreach (TabItem tab in new[] { w.DeresTab, w.AsciiTab })
            {
                w.Tabs.SelectedItem = tab;
                Pump(() => false, 60);
                Check(w.SavePngMenu.Visibility == Visibility.Visible && w.ZoomInMenu.Visibility == Visibility.Visible && w.CopyMenu.Visibility == Visibility.Visible && w.ExifSaveMenu.Visibility == Visibility.Collapsed &&
                    w.ExifCopyGpsMenu.Visibility == Visibility.Collapsed && w.ExifAddMenu.Visibility == Visibility.Collapsed && w.ExifZipMenu.Visibility == Visibility.Collapsed, "back on " + tab.Header);
            }
            w.Close();
        }

        static void ExifSettingsKept()
        {
            string path = Path.Combine(Work, "exif-settings.ini");
            var s = new Settings { ScrubLocationOnly = true, ScrubOrientation = false };
            File.WriteAllText(path, s.ToText(), new UTF8Encoding(false));
            Settings back = Settings.Load(path);
            Check(back.ScrubLocationOnly && !back.ScrubOrientation && File.ReadAllText(path).Contains("scrub_mode=location_only\r\nscrub_orientation=off\r\n"), "kept and read back");
            // odd lines fall back
            File.WriteAllText(path, "scrub_mode=gps\r\nscrub_orientation=maybe\r\n");
            Settings odd = Settings.Load(path);
            Check(!odd.ScrubLocationOnly && odd.ScrubOrientation, "odd lines should fall back");
            // the window starts with them, and a click changes them
            MainWindow w = NewMain(1, true, back);
            WaitForPicture(w);
            WaitForExif(w);
            w.Tabs.SelectedItem = w.ScrubTab;
            Pump(() => false, 60);
            Check(w.ScrubLocationButton.IsChecked == true && w.ScrubAllButton.IsChecked == false && w.ScrubOrientationBox.IsChecked == false && w.ScrubOptions.Mode == "location_only" && !w.ScrubOptions.PreserveOrientation,
                "the window shows the kept options");
            w.ScrubAllButton.IsChecked = true;
            w.ScrubOrientationBox.IsChecked = true;
            Check(w.ScrubOptions.Mode == "all" && w.ScrubOptions.PreserveOrientation, "a click should change the options");
            w.Close();
        }
    }
}
