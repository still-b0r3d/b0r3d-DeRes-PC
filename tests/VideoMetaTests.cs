using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using B0r3dDeRes.Exif;
using B0r3dDeRes.Video;

namespace B0r3dDeRes.Tests
{
    // The scrubber's videos (the PC's: the website has none): what phones write about where and on
    // what a video was filmed, found, and taken out byte by byte with the picture and sound the same
    // bytes. The clips are make-video-meta.mjs's (Apple, Android and GoPro layouts on the test
    // clips) and ffmpeg's own metadata writer's (make-video-clips.sh); both scripts are kept
    // outside this repository.
    static partial class TestMain
    {
        static ImageMetadata InspectClip(string name) => VideoMeta.Inspect(Clip(name));

        static void VideoMetadataFound()
        {
            ImageMetadata m = InspectClip("meta-iphone.mov");
            Check(m.Format == "mov" && m.Make == "Apple" && m.Model == "iPhone 15 Pro" && m.Software == "17.4.1" && m.DateTime == "2024-05-01T12:00:00+0100",
                "the iPhone clip: " + m.Format + " " + m.Make + " " + m.Model + " " + m.Software + " " + m.DateTime);
            Check(m.Gps != null && Math.Abs(m.Gps.Latitude - 41.8781) < 1e-9 && Math.Abs(m.Gps.Longitude + 87.6298) < 1e-9 && m.Gps.Altitude == 182, "the iPhone clip's place: " + (m.Gps == null ? "none" : ExifReader.GpsDecimal(m.Gps)));
            Check(m.Video.TimedMetadata.SequenceEqual(new[] { "mebx" }) && m.Video.Rotation == 90 && !m.Video.MoovFirst && m.XmpText != null && m.XmpText.Contains("GPSLatitude"),
                "the iPhone clip's track, turn and XMP: " + string.Join(",", m.Video.TimedMetadata) + " " + m.Video.Rotation + " " + m.Video.MoovFirst);
            string narrative = ExifReader.Narrative(m);
            Check(narrative == "Filmed on a Apple iPhone 15 Pro on 2024-05-01T12:00:00+0100 at coordinates 41.878100, -87.629800 (2.0 s, 160×96, turned 90°).", "the iPhone clip's line: " + narrative);
            Check(m.AllTags.Any(t => t.Name == "com.apple.quicktime.location.ISO6709" && t.DisplayValue == "+41.8781-087.6298+182.000/") &&
                m.AllTags.Any(t => t.Name == "com.apple.quicktime.location.accuracy.horizontal" && t.DisplayValue == "4.5"), "the iPhone clip's keys in All Tags");

            m = InspectClip("meta-android.mp4");
            Check(m.Format == "mp4" && m.Make == "samsung" && m.Model == "SM-S918B" && m.Video.MoovFirst, "the Android clip: " + m.Make + " " + m.Model + " " + m.Video.MoovFirst);
            Check(m.Gps != null && Math.Abs(m.Gps.Latitude - 35.6983) < 1e-9 && Math.Abs(m.Gps.Longitude - 139.773) < 1e-9, "the Android clip's place");
            Check(m.AllTags.Any(t => t.TagHex == "loci" && t.DisplayValue.StartsWith("35.698", StringComparison.Ordinal) && t.DisplayValue.EndsWith("(Akihabara)", StringComparison.Ordinal)), "the Android clip's 3GPP place");
            Check(m.AllTags.Any(t => t.TagHex == "smta" && t.DisplayValue.Contains("SM-S918B")), "Samsung's smta");
            Check(m.TrailerBytes == 4 + 27 + 11 + 4 + 4 + 4, "the bytes after the last box: " + m.TrailerBytes);
            Check(m.AiDetection.Detected && m.AiDetection.Kind == "made" && m.AiDetection.Tool == "Veo" && m.AiDetection.Credentials?.Where == "MP4 uuid box", "the Android clip's credentials: " + m.AiDetection.Summary);

            m = InspectClip("meta-gopro.mp4");
            Check(m.Video.TimedMetadata.SequenceEqual(new[] { "gpmd" }) && m.AllTags.Any(t => t.Name.Contains("GoPro, with GPS")) && m.AllTags.Any(t => t.TagHex == "GPMF"), "the GoPro clip's track and user data");

            m = InspectClip("meta-ffmpeg.mov");
            Check(m.Make == "Apple" && m.Model == "iPhone 15 Pro" && m.Gps != null && Math.Abs(m.Gps.Latitude - 35.6983) < 1e-9 && m.Gps.Altitude == 10 && m.Video.Created == new DateTime(2024, 5, 1, 12, 0, 0, DateTimeKind.Utc),
                "ffmpeg's keys: " + m.Make + " " + m.Model + " " + m.Video.Created);
            // (ffmpeg's MP4 writer keeps the place as 3GPP's loci, to 1/65536 of a degree)
            m = InspectClip("meta-ffmpeg-udta.mp4");
            Check(m.Gps != null && Math.Abs(m.Gps.Latitude - 35.6983) < 1e-4 && Math.Abs(m.Gps.Longitude - 139.773) < 1e-4 && m.AllTags.Any(t => t.TagHex == "loci") &&
                m.AllTags.Any(t => t.DisplayValue == "Null Island trip"), "ffmpeg's user data: " + (m.Gps == null ? "no place" : ExifReader.GpsDecimal(m.Gps)));

            // the program's own MP4s say nothing (an export holds just the picture and sound)
            m = InspectClip("h264-aac.mp4");
            Check(m.Gps == null && string.IsNullOrEmpty(m.Make) && m.Video.TimedMetadata.Count == 0, "a plain clip");
        }

        // a clip cleaned into a file of its own: the report, and the clean file
        static SelfCheckReport CleanClip(string name, ScrubOptions o, string into)
        {
            using (var source = new FileStream(Clip(name), FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                VideoCleanPlan plan = VideoMeta.Plan(source, o, out ImageMetadata before);
                using (var output = new FileStream(into, FileMode.Create, FileAccess.ReadWrite))
                {
                    VideoMeta.Write(source, plan, output);
                    output.Flush();
                    return VideoMeta.Verify(before, source.Length, plan, source, output, o);
                }
            }
        }

        // Windows plays the same frames and sound from both
        static void SamePictureAndSound(string original, string clean)
        {
            if (!Mf.Available) return;
            VideoFile a = VideoFile.Open(original), b = VideoFile.Open(clean);
            List<VideoFrame> fa = AllFrames(a), fb = AllFrames(b);
            Check(fa.Count > 0 && fa.Count == fb.Count, Path.GetFileName(clean) + ": " + fb.Count + " frames, not " + fa.Count);
            for (int i = 0; i < fa.Count; i++)
                Check(fa[i].Width == fb[i].Width && fa[i].Height == fb[i].Height && fa[i].Time == fb[i].Time && fa[i].Bgra.SequenceEqual(fb[i].Bgra), Path.GetFileName(clean) + ": frame " + i + " differs");
            if (a.HasSound)
            {
                short[] sa = AllSound(a, out long ta), sb = AllSound(b, out long tb);
                Check(ta == tb && sa.SequenceEqual(sb), Path.GetFileName(clean) + ": the sound differs");
            }
        }

        static void VideoMetadataCleaned()
        {
            if (!Mf.Available) throw new SkipException("this Windows has no video parts");
            string dir = Path.Combine(Work, "clean-videos");
            Directory.CreateDirectory(dir);
            var all = new ScrubOptions { Mode = "all" };
            var location = new ScrubOptions { Mode = "location_only" };
            foreach (string name in new[] { "meta-iphone.mov", "meta-android.mp4", "meta-gopro.mp4", "meta-ffmpeg.mov", "meta-ffmpeg-udta.mp4", "h264-aac.mp4" })
            {
                ImageMetadata before = InspectClip(name);
                // all of it
                string clean = Path.Combine(dir, "all-" + name);
                SelfCheckReport r = CleanClip(name, all, clean);
                ImageMetadata after = VideoMeta.Inspect(clean);
                Check(r.IsClean && r.SamplesUntouched == true, name + " (all): " + r.Summary);
                Check(after.Gps == null && string.IsNullOrEmpty(after.Make) && string.IsNullOrEmpty(after.Model) && string.IsNullOrEmpty(after.DateTime) && string.IsNullOrEmpty(after.Software) &&
                    after.RawTagCount == 0 && after.TrailerBytes == 0 && after.Video.TimedMetadata.Count == 0 && after.XmpText == null && !after.AiDetection.Detected,
                    name + " (all) still has: " + string.Join(", ", after.AllTags.Select(t => t.Name)));
                Check(after.Video.Rotation == before.Video.Rotation && new FileInfo(clean).Length == new FileInfo(Clip(name)).Length - before.TrailerBytes, name + " (all): its turn or size changed");
                Check(r.Summary.StartsWith("Checked: 0 GPS coordinates, 0 camera tags, 0 trailing bytes found. Kept: the picture and sound, byte for byte", StringComparison.Ordinal), name + ": " + r.Summary);
                SamePictureAndSound(Clip(name), clean);
                // where only
                clean = Path.Combine(dir, "location-" + name);
                r = CleanClip(name, location, clean);
                after = VideoMeta.Inspect(clean);
                Check(r.IsClean && r.SamplesUntouched == true && after.Gps == null && after.TrailerBytes == 0, name + " (location): " + r.Summary);
                Check(after.Make == before.Make && after.Model == before.Model && after.DateTime == before.DateTime && after.Software == before.Software, name + " (location) lost its camera or date");
                Check(!after.AllTags.Any(t => t.Name.IndexOf("location", StringComparison.OrdinalIgnoreCase) >= 0 || t.TagHex == "loci" || t.TagHex == "©xyz"), name + " (location) still names a place: " +
                    string.Join(", ", after.AllTags.Where(t => t.Name.IndexOf("location", StringComparison.OrdinalIgnoreCase) >= 0).Select(t => t.Name)));
                SamePictureAndSound(Clip(name), clean);
            }
            // location only keeps what isn't a place: the iPhone's keys but its location ones, the
            // GoPro's serial, the Android clip's credentials
            ImageMetadata iphone = VideoMeta.Inspect(Path.Combine(dir, "location-meta-iphone.mov"));
            Check(iphone.AllTags.Any(t => t.Name == "com.apple.quicktime.content.identifier") && iphone.Video.TimedMetadata.Count == 0 && iphone.XmpText == null, "the iPhone clip, location only");
            Check(VideoMeta.Inspect(Path.Combine(dir, "location-meta-gopro.mp4")).AllTags.Any(t => t.TagHex == "GPMF"), "the GoPro clip, location only, kept its serial");
            Check(VideoMeta.Inspect(Path.Combine(dir, "location-meta-android.mp4")).AiDetection.Detected, "the Android clip, location only, kept its credentials");
        }

        // ---- clips changed by hand, for what a review found ----

        static uint ReadU32(byte[] b, int at) => (uint)(b[at] << 24 | b[at + 1] << 16 | b[at + 2] << 8 | b[at + 3]);

        static void WriteU32(byte[] b, int at, long v)
        {
            b[at] = (byte)(v >> 24);
            b[at + 1] = (byte)(v >> 16);
            b[at + 2] = (byte)(v >> 8);
            b[at + 3] = (byte)v;
        }

        static byte[] Latin1(string s) => Encoding.GetEncoding(28591).GetBytes(s);

        static byte[] Concat(params byte[][] parts) => parts.SelectMany(p => p).ToArray();

        static byte[] MakeBox(string type, params byte[][] parts)
        {
            byte[] box = Concat(new byte[4], Latin1(type), Concat(parts));
            WriteU32(box, 0, box.Length);
            return box;
        }

        // a QuickTime text atom: its length, the language (und), the text
        static byte[] QtText(string text) => Concat(new byte[] { (byte)(text.Length >> 8), (byte)text.Length, 0x55, 0xc4 }, Latin1(text));

        // a clip's top-level boxes (32-bit sizes, as the test clips have)
        static List<(string Type, int Start, int Size)> TopBoxes(byte[] b)
        {
            var boxes = new List<(string, int, int)>();
            for (int i = 0; i + 8 <= b.Length;)
            {
                int size = (int)ReadU32(b, i);
                if (size < 8 || i + size > b.Length) break;
                boxes.Add((Encoding.ASCII.GetString(b, i + 4, 4), i, size));
                i += size;
            }
            return boxes;
        }

        // `extra` put at the end of the last top-level `type` box, its size made bigger (what comes
        // after it moves, so only for a box nothing's index points past)
        static byte[] AddToBox(byte[] file, string type, byte[] extra)
        {
            var (_, start, size) = TopBoxes(file).Last(b => b.Type == type);
            byte[] output = Concat(file.Take(start + size).ToArray(), extra, file.Skip(start + size).ToArray());
            WriteU32(output, start, size + extra.Length);
            return output;
        }

        static int IndexOf(byte[] b, string text, int from = 0)
        {
            byte[] t = Latin1(text);
            for (int i = from; i + t.Length <= b.Length; i++)
            {
                int j = 0;
                while (j < t.Length && b[i + j] == t[j]) j++;
                if (j == t.Length) return i;
            }
            return -1;
        }

        static bool Has(byte[] b, string text) => IndexOf(b, text) >= 0;

        // a video cleaned in memory: the report, and the clean bytes
        static SelfCheckReport CleanBytes(byte[] input, ScrubOptions o, out byte[] output)
        {
            var source = new MemoryStream(input, false);
            VideoCleanPlan plan = VideoMeta.Plan(source, o, out ImageMetadata before);
            var clean = new MemoryStream();
            VideoMeta.Write(source, plan, clean);
            output = clean.ToArray();
            return VideoMeta.Verify(before, input.Length, plan, source, new MemoryStream(output, false), o);
        }

        // a video the scrubber won't clean: refused with words, never a crash or a hang
        static void Refused(byte[] input, string words, string what)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                VideoMeta.Plan(new MemoryStream(input, false), new ScrubOptions(), out _);
                throw new CheckException(what + " should be refused");
            }
            catch (IOException x)
            {
                Check(x.Message.Contains(words), what + ": " + x.Message);
            }
            Check(watch.ElapsedMilliseconds < 5000, what + ": took " + watch.ElapsedMilliseconds + " ms to refuse");
        }

        static void VideoCleanHardCases()
        {
            var all = new ScrubOptions();
            var location = new ScrubOptions { Mode = "location_only" };
            var both = new[] { all, location };
            byte[] plain = File.ReadAllBytes(Clip("h264-aac.mp4"));      // the index first, the picture data last
            byte[] pcm = File.ReadAllBytes(Clip("pcm-twos.mov"));        // the picture data first, the index last
            Check(TopBoxes(plain).Last().Type == "mdat" && TopBoxes(pcm).Last().Type == "moov", "the clips' layouts changed");
            byte[] nmea = Latin1("$GPRMC,120000,A,4152.687,N,08737.788,W,0.0,0.0,010524,,,A*6C\r\n");

            // a dashcam's GPS between the frames (in the picture data, in no track): zeros, in both modes
            byte[] dashcam = AddToBox(plain, "mdat", MakeBox("free", Latin1("GPS "), nmea));
            foreach (ScrubOptions o in both)
            {
                SelfCheckReport r = CleanBytes(dashcam, o, out byte[] clean);
                Check(r.IsClean && r.SamplesUntouched == true && !Has(clean, "$GPRMC") && !Has(clean, "freeGPS") && clean.Length == dashcam.Length, "a dashcam's GPS block (" + o.Mode + "): " + r.Summary);
            }
            // the check reads the copy apart from the cleaning: a place written into a zeroed stretch is found
            {
                var source = new MemoryStream(dashcam, false);
                VideoCleanPlan plan = VideoMeta.Plan(source, all, out ImageMetadata before);
                var written = new MemoryStream();
                VideoMeta.Write(source, plan, written);
                byte[] tampered = written.ToArray();
                nmea.CopyTo(tampered, IndexOf(dashcam, "$GPRMC"));
                SelfCheckReport r = VideoMeta.Verify(before, dashcam.Length, plan, source, new MemoryStream(tampered, false), all);
                Check(!r.IsClean && r.Leftovers.Any(l => l.StartsWith("a place written in it (", StringComparison.Ordinal)), "a place left in a zeroed stretch should be found: " + r.Summary);
            }

            // in the index: an unknown box with a place in it, a dashcam's 'gps ' box, Samsung's @xyz
            // beside a make and model; and a free box at the top a dashcam filled with NMEA
            string place = "+41.8781-087.6298+182.000/";
            byte[] extras = AddToBox(pcm, "moov", Concat(MakeBox("xyzw", Latin1(place)), MakeBox("gps ", new byte[16]),
                MakeBox("udta", MakeBox("@xyz", QtText(place)), MakeBox("©mak", QtText("Samsung")), MakeBox("©mod", QtText("SM-S918B")))));
            extras = Concat(extras, MakeBox("free", nmea));
            ImageMetadata seen = VideoMeta.Inspect(new MemoryStream(extras, false));
            Check(seen.AllTags.Any(t => t.TagHex == "xyzw" && t.DisplayValue.EndsWith("with a place in it", StringComparison.Ordinal)) && seen.AllTags.Any(t => t.TagHex == "@xyz" && t.Name == "Location (Samsung)") &&
                seen.Gps != null && seen.Make == "Samsung", "the boxes added: " + string.Join(", ", seen.AllTags.Select(t => t.TagHex)));
            foreach (ScrubOptions o in both)
            {
                SelfCheckReport r = CleanBytes(extras, o, out byte[] clean);
                ImageMetadata after = VideoMeta.Inspect(new MemoryStream(clean, false));
                Check(r.IsClean && !Has(clean, "+41.8781") && !Has(clean, "$GPRMC") && after.Gps == null && !after.AllTags.Any(t => t.TagHex == "gps " || t.TagHex == "xyzw" || t.TagHex == "@xyz"),
                    "boxes with a place (" + o.Mode + "): " + r.Summary + " / " + string.Join(", ", after.AllTags.Select(t => t.TagHex)));
                Check(o == all ? string.IsNullOrEmpty(after.Make) && string.IsNullOrEmpty(after.Model) : after.Make == "Samsung" && after.Model == "SM-S918B", "the camera (" + o.Mode + "): " + after.Make + " " + after.Model);
            }

            // all mode takes the names and makers out of the index too (the handlers', the encoder's,
            // the vendor's): only the picture and sound say anything
            foreach (byte[] clip in new[] { plain, pcm })
            {
                SelfCheckReport r = CleanBytes(clip, all, out byte[] clean);
                var moov = TopBoxes(clean).First(b => b.Type == "moov");
                byte[] index = clean.Skip(moov.Start).Take(moov.Size).ToArray();
                Check(r.IsClean && !Has(index, "Handler") && !Has(index, "Lavc") && !Has(index, "appl"), "the index still names its makers: " + Encoding.ASCII.GetString(index.Select(c => c >= 32 && c < 127 ? c : (byte)'.').ToArray()));
            }

            // bytes after the video that look like a box: still the trailer, cut
            byte[] loc = Latin1("LOC:+41.8781-087.6298/");
            byte[] trailing = Concat(pcm, loc);
            Check(VideoMeta.Inspect(new MemoryStream(trailing, false)).TrailerBytes == loc.Length, "a trailer like a box should be the trailer");
            {
                SelfCheckReport r = CleanBytes(trailing, location, out byte[] clean);
                Check(r.IsClean && clean.Length == pcm.Length, "the trailer like a box, cut: " + r.Summary);
            }

            // old QuickTime sound: its chunks sized from the sound description, as players size them,
            // so the picture data stays byte for byte (with the sample size 1 older writers put too)
            foreach (string name in new[] { "pcm-twos.mov", "pcm-24.mov" })
                foreach (bool one in new[] { false, true })
                {
                    byte[] clip = File.ReadAllBytes(Clip(name));
                    if (one) WriteU32(clip, IndexOf(clip, "stsz", IndexOf(clip, "stsz") + 4) + 8, 1);
                    SelfCheckReport r = CleanBytes(clip, all, out byte[] clean);
                    var mdat = TopBoxes(clip).First(b => b.Type == "mdat");
                    Check(r.IsClean && clean.Skip(mdat.Start).Take(mdat.Size).SequenceEqual(clip.Skip(mdat.Start).Take(mdat.Size)), name + (one ? " (sample size 1)" : "") + ": its picture data changed: " + r.Summary);
                }

            // a drone's caption track says where it flew: gone in both modes, its captions zeroed
            ImageMetadata dji = InspectClip("meta-dji.mp4");
            Check(dji.AllTags.Any(t => t.Name.StartsWith("Text track", StringComparison.Ordinal)) && dji.Video.Tracks == 3, "the drone clip's caption track");
            string dir = Path.Combine(Work, "clean-videos");
            Directory.CreateDirectory(dir);
            foreach (ScrubOptions o in both)
            {
                string into = Path.Combine(dir, o.Mode + "-meta-dji.mp4");
                SelfCheckReport r = CleanClip("meta-dji.mp4", o, into);
                Check(r.IsClean && r.SamplesUntouched == true && !Has(File.ReadAllBytes(into), "GPS (") && VideoMeta.Inspect(into).Video.Tracks == 2, "the drone's captions (" + o.Mode + "): " + r.Summary);
                SamePictureAndSound(Clip("meta-dji.mp4"), into);
            }

            // refused, quickly: a second index, a fragment, an index pointing outside the picture data,
            // a damaged header, a sample table that would list four billion samples
            Refused(Concat(pcm, pcm.Skip(TopBoxes(pcm).Last().Start).ToArray()), "two indexes", "a second moov");
            Refused(Concat(pcm, MakeBox("moof", new byte[8])), "fragmented", "a moof");
            byte[] cut = (byte[])plain.Clone();
            WriteU32(cut, TopBoxes(plain).Last().Start, 4);
            Refused(cut, "outside its picture data", "picture data with a size too small to be a box");
            Check(VideoMeta.Inspect(new MemoryStream(cut, false)).TrailerBytes > 0, "the bytes after the index read as a trailer");
            byte[] shortTkhd = (byte[])pcm.Clone();
            int tkhd = IndexOf(shortTkhd, "tkhd") - 4;
            long tkhdSize = ReadU32(shortTkhd, tkhd);
            WriteU32(shortTkhd, tkhd, 12);
            WriteU32(shortTkhd, tkhd + 12, tkhdSize - 12);
            Latin1("free").CopyTo(shortTkhd, tkhd + 16);
            Check(VideoMeta.Inspect(new MemoryStream(shortTkhd, false)).Video != null, "a short tkhd should still be read");
            Refused(shortTkhd, "damaged (tkhd)", "a tkhd too short for its dates");
            byte[] noField = (byte[])pcm.Clone();
            Latin1("stz2").CopyTo(noField, IndexOf(noField, "stsz"));
            Refused(noField, "damaged (stz2)", "an stz2 with no field size");
            // four billion samples of one size, two billion a chunk: worked out a chunk at a time (no
            // sample by sample), and a chunk past the file's end is damage
            byte[] many = (byte[])pcm.Clone();
            WriteU32(many, IndexOf(many, "stsz", IndexOf(many, "stsz") + 4) + 12, 0xffffffffL);
            WriteU32(many, IndexOf(many, "stsc", IndexOf(many, "stsc") + 4) + 16, 0x7fffffffL);
            Refused(many, "damaged (a chunk's size)", "two billion samples a chunk");
        }

        static void VideoCleanGuards()
        {
            // a timed metadata track whose chunk lies in the picture's bytes: refused, nothing written
            byte[] bytes = File.ReadAllBytes(Clip("meta-iphone.mov"));
            int stco = -1;
            for (int i = bytes.Length - 8; i > 0 && stco < 0; i--)
                if (bytes[i] == 's' && bytes[i + 1] == 't' && bytes[i + 2] == 'c' && bytes[i + 3] == 'o') stco = i;
            // (the mebx track is the last; its one chunk offset moved to the start of the picture data)
            int mdat = -1;
            for (int i = 0; i < bytes.Length - 4 && mdat < 0; i++)
                if (bytes[i] == 'm' && bytes[i + 1] == 'd' && bytes[i + 2] == 'a' && bytes[i + 3] == 't') mdat = i;
            int to = mdat + 4;
            bytes[stco + 12] = (byte)(to >> 24);
            bytes[stco + 13] = (byte)(to >> 16);
            bytes[stco + 14] = (byte)(to >> 8);
            bytes[stco + 15] = (byte)to;
            try
            {
                VideoMeta.Plan(new MemoryStream(bytes), new ScrubOptions(), out _);
                throw new CheckException("a metadata track over the picture's bytes should be refused");
            }
            catch (IOException x)
            {
                Check(x.Message.Contains("share bytes"), "refused: " + x.Message);
            }
            // not a video at all
            foreach (byte[] junk in new[] { new byte[0], new byte[] { 1, 2, 3 }, File.ReadAllBytes(Path.Combine(ExifFolder, "phone.heic")), File.ReadAllBytes(Path.Combine(ExifFolder, "phone.jpg")) })
                try
                {
                    VideoMeta.Inspect(new MemoryStream(junk));
                    throw new CheckException("a file that isn't a video read as one");
                }
                catch (IOException)
                {
                    // (an EndOfStreamException is one too)
                }
            // a clean video into a ZIP as it's written: the entry is the clean file
            using (var source = new FileStream(Clip("meta-android.mp4"), FileMode.Open, FileAccess.Read))
            {
                VideoCleanPlan plan = VideoMeta.Plan(source, new ScrubOptions(), out _);
                var zipped = new MemoryStream();
                using (var zip = new ZipWriter(zipped))
                {
                    zip.Add("one.txt", new byte[] { 1 });
                    zip.Add("meta-android-clean.mp4", plan.Length, s => VideoMeta.Write(source, plan, s));
                }
                var expected = new MemoryStream();
                VideoMeta.Write(source, plan, expected);
                byte[] z = zipped.ToArray(), e = expected.ToArray();
                // the second entry's data: after its 30-byte header and name
                int at = 30 + "one.txt".Length + 1 + 30 + "meta-android-clean.mp4".Length;
                Check(z.Skip(at).Take(e.Length).SequenceEqual(e), "the ZIP's video entry isn't the clean file");
                uint crc = Engine.Png.Crc(0xffffffff, e, 0, e.Length) ^ 0xffffffff;
                int local = 30 + "one.txt".Length + 1;
                Check(BitConverter.ToUInt32(z, local + 14) == crc && BitConverter.ToUInt32(z, local + 18) == e.Length, "the ZIP's video entry has the wrong CRC or size");
            }
        }
    }
}
