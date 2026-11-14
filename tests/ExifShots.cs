using System;
using System.IO;
using System.Linq;
using System.Windows.Controls;

namespace B0r3dDeRes.Tests
{
    // b0r3dDeResTests.exe --shots <folder> exif: the EXIF Scrubber and Injector, in light and dark
    // (the Ruckus sample as the tabs open, at 100 % and 150 %; a phone photo with its place, its
    // thumbnail and all its tags; its clean copy's Verified Clean; AI and Content Credentials; a
    // GIF; a HEIC Windows can't show; an iPhone video and its clean copy; a batch, cleaned; the
    // injector's form, a device preset, a refused value and a saved photo).
    static partial class TestMain
    {
        static void ExifShots(string folder, Look look)
        {
            Theme.Set(look);
            string name = look.ToString().ToLowerInvariant();
            string work = Path.Combine(Work, "exif-shots");
            Fresh(work);
            void Shot(MainWindow w, string shot, double scale = 1)
            {
                LayOut(w.Root, MainWidth, MainHeight);
                Pump(() => false, 80);
                LayOut(w.Root, MainWidth, MainHeight);
                Shoot(w.Root, MainWidth, MainHeight, scale, Path.Combine(folder, shot + "-" + name + (scale != 1 ? "-" + (int)(scale * 100) : "") + ".png"));
            }
            MainWindow Tab(TabItem tab, string file = null, double scale = 1)
            {
                MainWindow w = NewMain(scale);
                WaitForPicture(w);
                WaitForExif(w);
                w.Tabs.SelectedItem = tab == null ? w.ScrubTab : tab;
                if (file != null) OpenForExif(w, file);
                Pump(() => false, 60);
                return w;
            }

            foreach (double scale in new[] { 1.0, 1.5 })
            {
                MainWindow start = NewMain(scale);
                WaitForPicture(start);
                WaitForExif(start);
                start.Tabs.SelectedItem = start.ScrubTab;
                Shot(start, "exif-start", scale);
                start.Close();
            }

            MainWindow phone = Tab(null, Path.Combine(ExifFolder, "phone-icc-xmp.jpg"));
            Shot(phone, "exif-phone");
            phone.Inspector.SwitchView(true);
            Shot(phone, "exif-alltags");
            phone.Inspector.SetFilter("gps");
            Shot(phone, "exif-filter");
            phone.Inspector.SwitchView(false);
            Finish(phone.ScrubSaveTo(Path.Combine(work, "phone-clean.jpg")));
            Pump(() => !phone.ExifBusy, 10000);
            phone.ScrubSaveSection.IsExpanded = true;
            Shot(phone, "exif-clean");
            phone.Close();

            foreach (var (file, shot) in new[] { ("c2pa-firefly.png", "exif-c2pa-firefly"), ("c2pa-leica.png", "exif-c2pa-leica"), ("ai-sd.png", "exif-ai-sd"), ("busy.gif", "exif-gif"),
                ("phone.heic", "exif-heic"), ("phone-trailer.jpg", "exif-phone-trailer") })
            {
                MainWindow w = Tab(null, Path.Combine(ExifFolder, file));
                Shot(w, shot);
                w.Close();
            }

            MainWindow video = Tab(null, Clip("meta-iphone.mov"));
            Shot(video, "exif-video");
            Finish(video.ScrubSaveTo(Path.Combine(work, "meta-iphone-clean.mov")));
            Pump(() => !video.ExifBusy, 20000);
            Shot(video, "exif-video-clean");
            video.Close();

            MainWindow batch = Tab(null);
            batch.AddToBatch(new[] { "phone.jpg", "phone-icc-xmp.jpg", "rukus.png", "busy.gif", "phone.heic", "c2pa-firefly.png", "meta.webp", "dot.bmp" }.Select(f => Path.Combine(ExifFolder, f))
                .Concat(new[] { Clip("meta-android.mp4") }));
            Pump(() => !batch.ExifReading && batch.ExifRowShown != null, 20000);
            Shot(batch, "exif-batch");
            string into = Path.Combine(work, "clean");
            Directory.CreateDirectory(into);
            Finish(batch.CleanBatchInto(into));
            Pump(() => !batch.ExifBusy, 30000);
            Shot(batch, "exif-batch-clean");
            batch.Close();

            foreach (double scale in new[] { 1.0, 1.5 })
            {
                MainWindow inject = Tab(null, null, scale);
                inject.Tabs.SelectedItem = inject.InjectTab;
                Pump(() => false, 60);
                inject.InjectCameraSection.IsExpanded = true;
                Shot(inject, "inject-start", scale);
                inject.Close();
            }

            MainWindow preset = Tab(null, Path.Combine(ExifFolder, "phone-block-xmp.jpg"));
            preset.Tabs.SelectedItem = preset.InjectTab;
            Pump(() => false, 60);
            preset.InjectCameraSection.IsExpanded = true;
            preset.InjectLensSection.IsExpanded = true;
            preset.InjectPreset.SelectedItem = preset.InjectPreset.Items.OfType<ComboBoxItem>().First(i => (string)i.Tag == "iphone-15-pro");
            Shot(preset, "inject-preset");
            preset.InjectCameraSection.IsExpanded = false;
            preset.InjectShutter.Text = "abc";
            preset.FormEdits();
            Shot(preset, "inject-error");
            preset.InjectShutter.Text = "1/500";
            preset.InjectGpsSection.IsExpanded = true;
            preset.InjectLat.Text = "35.6983";
            preset.InjectLon.Text = "139.773";
            Finish(preset.InjectSaveTo(Path.Combine(work, "phone-injected.jpg")));
            Pump(() => false, 300);
            Shot(preset, "inject-saved");
            preset.Close();
        }
    }
}
