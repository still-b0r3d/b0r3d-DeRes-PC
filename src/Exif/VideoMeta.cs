using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace B0r3dDeRes.Exif
{
    // a video's own details, for the inspector
    sealed class VideoDetails
    {
        public string Brand;            // the ftyp major brand ("qt  ", "isom", "mp42" …)
        public double Seconds;          // its length
        public int Width, Height;       // the picture as stored
        public int Rotation;            // degrees the picture is turned to show it (0, 90, 180 or 270)
        public int Tracks;
        public List<string> TimedMetadata = new List<string>();   // timed metadata tracks, by their sample kind
        public bool MoovFirst;          // the index before the picture data (fast start)
        public DateTime? Created;       // the movie header's date (UTC), when it has one
    }

    // What a video's clean copy takes: patches over the original (blank boxes where metadata was,
    // zeros over timed metadata and dates) and where it ends (trailing bytes cut). Nothing else
    // in the file changes, so the picture and sound are the same bytes, and no offset in the file
    // moves: a box taken out becomes a 'free' box of the same size.
    sealed class VideoCleanPlan
    {
        public sealed class Patch
        {
            public long At, Length;
            public byte[] Bytes;   // null: zeros
        }

        public long Length;                                        // the clean file's length
        public List<Patch> Patches = new List<Patch>();
        public List<(long Start, long End)> Samples = new List<(long, long)>();   // the picture's and sound's bytes
        public int Removed;                                        // metadata items taken out
    }

    // MP4 and MOV metadata (the PC's, new with video): what phones write about where a video was
    // filmed and on what (QuickTime user data like ©xyz, 3GPP's loci, the keys Apple and Android
    // write in a meta box, XMP, Content Credentials, timed metadata tracks, the headers' dates), read
    // from the boxes without loading the picture data, and taken out byte by byte.
    static class VideoMeta
    {
        public static readonly string[] Extensions = { ".mp4", ".mov", ".m4v", ".3gp", ".3g2", ".qt" };

        public static bool LooksLikeVideo(string path)
        {
            try
            {
                return Extensions.Contains(Path.GetExtension(path).ToLowerInvariant());
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        // the most of a moov box (or a metadata box at the top) read into memory
        const long MaxBoxInMemory = 512L * 1024 * 1024;

        const string Xmp = "be7acfcb97a942e89c71999491e3afac", C2pa = "d8fec3d61b0e483c92975828877ec481";

        // timed metadata: Apple's, GoPro's, Google's camera motion, and plain text or XML metadata
        static readonly string[] timedKinds = { "mebx", "gpmd", "camm", "mett", "metx", "urim" };

        // the boxes a picture and sound need, at each level of the index (any other is taken out in
        // all mode: an unknown box may say where or on what)
        static readonly string[] trakKeeps = { "tkhd", "edts", "tref", "mdia", "tapt", "load", "txas", "clip", "matt", "kmat", "imap" };
        static readonly string[] mdiaKeeps = { "mdhd", "hdlr", "minf", "elng" };
        static readonly string[] minfKeeps = { "vmhd", "smhd", "nmhd", "gmhd", "hmhd", "sthd", "hdlr", "dinf", "stbl" };
        static readonly string[] stblKeeps = { "stsd", "stts", "ctts", "cslg", "stss", "stps", "sdtp", "stsc", "stsz", "stz2", "stco", "co64", "sgpd", "sbgp", "subs", "padb", "stsh", "stdp", "saiz", "saio" };

        const string Fragmented = "it's a fragmented MP4 (made in pieces), which the scrubber can't clean yet";

        // the most of an unknown box's bytes read to look for a place in it (a bigger one is taken
        // out in location mode too, unread)
        const int MaxLooked = 16 * 1024 * 1024;

        sealed class Box
        {
            public string Type;
            public long Start, Size;
            public int Header;       // bytes before its payload (8, 16 with a 64-bit size, +16 for a uuid)
            public int SizeFields;   // bytes of size and type (8 or 16)
            public string Uuid;      // a uuid box's own type, as hex
            public long End => Start + Size;
            public long Payload => Start + Header;
        }

        sealed class Track
        {
            public Box Trak;
            public string Handler = "", Kind = "";
            public int Width, Height, Rotation;
            public double Seconds;
            public byte[] Stsd;
            public List<(long Start, long End)> Chunks = new List<(long, long)>();
            // its names and makers in its headers (the handler's name, the encoder's name and vendor):
            // zeroed in all mode
            public List<(long At, long Length)> Names = new List<(long, long)>();
            // its chunks' sizes couldn't be worked out for sure (old QuickTime sound of a kind not known)
            public bool Uncertain;
            public bool Timed => Handler == "meta" || timedKinds.Contains(Kind);
            // the picture, the sound, or a picture beside it (depth): what a clean copy keeps
            public bool Picture => Handler == "vide" || Handler == "soun" || Handler == "auxv";
            public bool HoldsLocation => Kind == "gpmd" || Kind == "camm" || (Stsd != null && Contains(Stsd, "location"));
        }

        // one metadata thing found, and how it goes
        sealed class Item
        {
            public Box Box;              // the box it is (freed when it goes)
            public bool Location;        // it says where
            public bool Whole;           // in all mode the whole box goes (else it's inside one that does)
            public bool Counted = true;  // a field (a box that only holds fields isn't one)
            public string Key;           // a meta box's key, when it's one
        }

        // a meta box with keys, rebuilt without its location keys in location-only mode
        sealed class MetaBox
        {
            public Box Meta;
            public byte[] Buf;           // the bytes it was read from, and where they start in the file
            public long FileBase;
            public long ChildrenAt;
            public List<Box> Children = new List<Box>();
            public Box Keys, Ilst;
            public List<(byte[] Entry, string Name)> KeyEntries = new List<(byte[], string)>();
            public List<(Box Item, int Index, string Key)> Items = new List<(Box, int, string)>();
        }

        sealed class Parsed
        {
            public long Length, TrailerStart;
            public List<Box> Top = new List<Box>();
            public Box Moov;
            public byte[] MoovBytes;
            public int Moovs, Fragments;   // moov boxes, and moof boxes (a fragmented MP4)
            public List<Box> Mdats = new List<Box>();
            public List<Track> Tracks = new List<Track>();
            public List<Item> Items = new List<Item>();
            public List<MetaBox> Metas = new List<MetaBox>();
            public List<(long At, int Length)> Dates = new List<(long, int)>();   // creation and modification times in the headers
            public List<Box> Padding = new List<Box>();   // free and skip boxes (their bytes zeroed: an app may have left old metadata in one)
            public string Refuse;          // why it can't be cleaned safely (it can still be inspected)
            public ImageMetadata Meta;
            public ContentCredentials Credentials;

            public void Damaged(string where)
            {
                if (Refuse == null) Refuse = "part of it is damaged (" + where + "), so it can't be cleaned safely";
            }
        }

        // what a damaged box's reading throws (a field past its end, a length past the buffer)
        static bool IsDamage(Exception x) => x is IndexOutOfRangeException || x is ArgumentException || x is OverflowException;

        // ---------- reading ----------

        static string Fourcc(byte[] b, int at) => new string(new[] { (char)b[at], (char)b[at + 1], (char)b[at + 2], (char)b[at + 3] });

        static uint U32(byte[] b, long at) => (uint)((b[at] << 24) | (b[at + 1] << 16) | (b[at + 2] << 8) | b[at + 3]);

        static ulong U64(byte[] b, long at) => ((ulong)U32(b, at) << 32) | U32(b, at + 4);

        static int U16(byte[] b, long at) => (b[at] << 8) | b[at + 1];

        static bool Plausible(string type) => type.All(c => (c >= 0x20 && c <= 0x7e) || c == 0xa9);

        static bool Contains(byte[] b, string text)
        {
            for (int i = 0; i + text.Length <= b.Length; i++)
            {
                int j = 0;
                while (j < text.Length && b[i + j] == text[j]) j++;
                if (j == text.Length) return true;
            }
            return false;
        }

        // ---------- places in text ----------

        // how phones, cameras, drones and dashcams write where in text: GPS tag names, NMEA
        // sentences, a drone's captions ("GPS (8.6499, 53.1665, 18)"), a dashcam's GPS blocks, Apple's
        // location key (and ISO 6709 places: Iso6709At)
        static readonly string[] placeWords = { "location.ISO6709", "GPSLatitude", "GPSLongitude", "Latitude", "latitude", "$GPRMC", "$GPGGA", "$GPGLL", "$GNRMC", "$GNGGA", "freeGPS", "GPS (" };
        static readonly bool[] placeStarts = PlaceStarts();

        static bool[] PlaceStarts()
        {
            var starts = new bool[256];
            starts['+'] = starts['-'] = true;
            foreach (string w in placeWords) starts[w[0]] = true;
            return starts;
        }

        // the first sign of a place in b[start, end), or null
        static string FindPlace(byte[] b, long start, long end)
        {
            for (long i = start; i < end; i++)
            {
                byte c = b[i];
                if (!placeStarts[c]) continue;
                if (c == '+' || c == '-')
                {
                    int n = Iso6709At(b, i, end);
                    if (n > 0) return JsText.Latin1(b, (int)i, n);
                    continue;
                }
                foreach (string w in placeWords)
                {
                    if (c != w[0] || i + w.Length > end) continue;
                    int j = 1;
                    while (j < w.Length && b[i + j] == w[j]) j++;
                    if (j == w.Length) return w;
                }
            }
            return null;
        }

        // an ISO 6709 latitude and longitude at b[i] ("+41.8781-087.6298", or with minutes and
        // seconds, "+4152.687-08737.788"): its length, or 0
        static int Iso6709At(byte[] b, long i, long end)
        {
            long k = i;
            bool Part(int degrees)
            {
                if (k >= end || (b[k] != '+' && b[k] != '-')) return false;
                k++;
                long d = k;
                while (k < end && b[k] >= '0' && b[k] <= '9') k++;
                long whole = k - d;
                if (whole != degrees && whole != degrees + 2 && whole != degrees + 4) return false;
                if (k >= end || b[k] != '.') return false;
                k++;
                long f = k;
                while (k < end && b[k] >= '0' && b[k] <= '9') k++;
                return k > f;
            }
            if (!Part(2) || !Part(3)) return 0;
            return k - i >= 14 ? (int)(k - i) : 0;
        }

        // the check's scan of a clean copy, fed its bytes outside the picture and sound in order:
        // the end of each piece is kept, so a word split between two pieces is still seen
        sealed class PlaceScan
        {
            const int Keep = 96;
            readonly byte[] carry = new byte[Keep * 2];
            int kept;
            public string Found;

            public void Feed(byte[] b, int start, int count)
            {
                if (Found != null || count <= 0) return;
                int head = Math.Min(count, Keep);
                Array.Copy(b, start, carry, kept, head);
                Found = FindPlace(carry, 0, kept + head) ?? FindPlace(b, start, start + count);
                int tail = Math.Min(count, Keep);
                if (count >= Keep) Array.Copy(b, start + count - tail, carry, 0, tail);
                else
                {
                    // (a short piece: what was kept before, then it)
                    int from = Math.Max(0, kept + head - Keep);
                    Array.Copy(carry, from, carry, 0, kept + head - from);
                    tail = kept + head - from;
                }
                kept = tail;
            }

            // the picture or sound in between: nothing runs across it
            public void Break() => kept = 0;
        }

        // a × b, or -1 past what a long holds
        static long Mul(long a, long b) => a == 0 || b == 0 ? 0 : a < 0 || b < 0 || a > long.MaxValue / b ? -1 : a * b;

        static void ReadExactly(Stream s, long at, byte[] into, int count)
        {
            s.Position = at;
            int read = 0;
            while (read < count)
            {
                int n = s.Read(into, read, count - read);
                if (n == 0) throw new EndOfStreamException();
                read += n;
            }
        }

        static byte[] Read(Stream s, long at, long count)
        {
            if (count > MaxBoxInMemory) throw new IOException("a box of " + count + " bytes is too big to read");
            var b = new byte[count];
            ReadExactly(s, at, b, (int)count);
            return b;
        }

        // whether a file's first box says it's an MP4 or QuickTime file (and not a HEIF photo)
        public static string Detect(byte[] head)
        {
            if (head.Length < 12) return null;
            string type = Fourcc(head, 4);
            if (type == "ftyp")
            {
                string brand = Fourcc(head, 8);
                if (ExifReader.DetectFormat(head) != "unknown") return null;   // HEIC, AVIF
                return brand == "qt  " ? "mov" : "mp4";
            }
            return type == "moov" || type == "mdat" || type == "wide" || type == "free" || type == "skip" || type == "pnot" ? "mov" : null;
        }

        public static string Detect(Stream s)
        {
            var head = new byte[Math.Min(64, s.Length)];
            ReadExactly(s, 0, head, head.Length);
            return Detect(head);
        }

        // the boxes inside buf[start, end) (offsets in the file: fileBase + index)
        static List<Box> Children(byte[] buf, long fileBase, long start, long end)
        {
            var boxes = new List<Box>();
            long i = start;
            while (i + 8 <= end)
            {
                long size = U32(buf, i);
                int header = 8, sizeFields = 8;
                if (size == 1)
                {
                    if (i + 16 > end) break;
                    size = (long)U64(buf, i + 8);
                    header = sizeFields = 16;
                }
                else if (size == 0) size = end - i;
                if (size < header || i + size > end) break;
                var box = new Box { Type = Fourcc(buf, (int)i + 4), Start = fileBase + i, Size = size, Header = header, SizeFields = sizeFields };
                if (box.Type == "uuid")
                {
                    if (size < header + 16) break;
                    box.Uuid = string.Concat(Enumerable.Range(0, 16).Select(k => buf[i + header + k].ToString("x2")));
                    box.Header += 16;
                }
                boxes.Add(box);
                i += size;
            }
            return boxes;
        }

        // where a meta box's children start: QuickTime's has none of a full box's version and
        // flags (its hdlr comes straight away), ISO's does
        static long MetaChildren(byte[] buf, long payload, long end)
        {
            if (payload + 8 <= end && Fourcc(buf, (int)payload + 4) == "hdlr") return payload;
            return payload + 4;
        }

        static Parsed Parse(Stream s)
        {
            var p = new Parsed { Length = s.Length };
            long pos = 0;
            var head = new byte[32];
            while (pos + 8 <= p.Length)
            {
                ReadExactly(s, pos, head, (int)Math.Min(32, p.Length - pos));
                long size = U32(head, 0);
                string type = Fourcc(head, 4);
                int header = 8, sizeFields = 8;
                if (!Plausible(type)) break;
                // "to the end of the file", or past it (a file cut short): only the picture data, the
                // index or padding do that; other bytes there are what came after the video
                bool runsOn = type == "mdat" || type == "moov" || type == "free" || type == "skip";
                if (size == 1)
                {
                    if (pos + 16 > p.Length) break;
                    ulong big = U64(head, 8);
                    header = sizeFields = 16;
                    size = big > (ulong)(p.Length - pos) ? (runsOn ? p.Length - pos : -1) : (long)big;
                }
                else if (size == 0) size = runsOn ? p.Length - pos : -1;
                else if (size > p.Length - pos) size = runsOn ? p.Length - pos : -1;
                if (size < header) break;
                var box = new Box { Type = type, Start = pos, Size = size, Header = header, SizeFields = sizeFields };
                if (type == "uuid")
                {
                    if (size < header + 16) break;
                    var u = new byte[16];
                    ReadExactly(s, pos + header, u, 16);
                    box.Uuid = string.Concat(u.Select(x => x.ToString("x2")));
                    box.Header += 16;
                }
                p.Top.Add(box);
                pos += size;
            }
            p.TrailerStart = pos;
            if (p.Top.Count == 0 || Detect(s) == null) throw new IOException("it isn't an MP4 or MOV file");

            var meta = p.Meta = new ImageMetadata { Format = p.Top[0].Type == "ftyp" && FtypBrand(s, p.Top[0]) == "qt  " || p.Top[0].Type != "ftyp" ? "mov" : "mp4" };
            var video = meta.Video = new VideoDetails { Brand = p.Top[0].Type == "ftyp" ? FtypBrand(s, p.Top[0]) : "" };
            meta.TrailerBytes = p.Length - p.TrailerStart;
            var texts = new List<string>();

            long mdatAt = p.Top.FirstOrDefault(b => b.Type == "mdat")?.Start ?? long.MaxValue;
            foreach (Box box in p.Top)
            {
                try
                {
                    switch (box.Type)
                    {
                        case "moov":
                            p.Moovs++;
                            if (p.Moov != null)
                            {
                                // (a second index: which one a player reads isn't sure)
                                p.Refuse = p.Refuse ?? "it has two indexes (moov boxes), so it can't be cleaned safely";
                                break;
                            }
                            p.Moov = box;
                            video.MoovFirst = box.Start < mdatAt;
                            p.MoovBytes = Read(s, box.Start, box.Size);
                            ParseMoov(p, texts);
                            break;
                        case "mdat":
                            p.Mdats.Add(box);
                            break;
                        case "ftyp":
                            break;
                        case "moof":
                        case "mfra":
                        case "sidx":
                        case "ssix":
                        case "styp":
                        case "emsg":
                            if (box.Type == "moof") p.Fragments++;
                            p.Refuse = p.Refuse ?? Fragmented;
                            break;
                        case "uuid":
                        {
                            byte[] payload = ReadSome(s, box, out bool whole);
                            ReadUuid(p, box, payload, whole, texts, "uuid box");
                            break;
                        }
                        case "udta":
                        case "meta":
                            byte[] bytes = Read(s, box.Start, box.Size);
                            if (box.Type == "udta") ParseUdta(p, bytes, box.Start, box, texts);
                            else ParseMeta(p, bytes, box.Start, box, texts);
                            break;
                        case "free":
                        case "skip":
                        case "wide":
                            p.Padding.Add(box);
                            break;
                        default:
                        {
                            // anything else at the top (prft's clock time, pnot's preview, pdin, junk):
                            // not the video, so gone in all mode, and in location mode when it reads as a place
                            byte[] payload = ReadSome(s, box, out bool whole);
                            Unknown(p, box, payload, 0, payload.Length, whole, "Box");
                            break;
                        }
                    }
                }
                catch (Exception x) when (IsDamage(x))
                {
                    p.Damaged(Shown(box.Type));
                }
            }
            if (p.Moov == null) throw new IOException("it has no moov box (the video's index)");

            // what it says: GPS from the keys, the user data, 3GPP's loci, then XMP
            if (meta.Gps == null && !string.IsNullOrEmpty(meta.XmpText)) meta.Gps = ExifReader.XmpGps(meta.XmpText);
            if (string.IsNullOrEmpty(meta.DateTime) && video.Created.HasValue) meta.DateTime = Utc(video.Created.Value);
            var blocks = new List<string>();
            if (!string.IsNullOrEmpty(meta.XmpText)) blocks.Add(meta.XmpText);
            blocks.AddRange(texts);
            meta.AiDetection = AiMarkers.Detect(blocks, meta.Software, p.Credentials);
            meta.RawTagCount = p.Items.Count(i => i.Counted) + p.Dates.Select(d => d.At).Distinct().Count(at => Nonzero(p, at)) + p.Tracks.Count(t => !t.Picture);
            return p;
        }

        // a box's payload, read whole when it's no bigger than MaxLooked
        static byte[] ReadSome(Stream s, Box box, out bool whole)
        {
            long length = box.End - box.Payload;
            whole = length <= MaxLooked;
            return Read(s, box.Payload, Math.Max(0, Math.Min(length, MaxLooked)));
        }

        // A box the scrubber doesn't know (its bytes in buf[start, end)): it goes whole in all mode,
        // and in location mode too when it reads as a place, is named for GPS, or is too big to read.
        static void Unknown(Parsed p, Box box, byte[] buf, long start, long end, bool whole, string category, string name = null)
        {
            string place = FindPlace(buf, start, end);
            bool location = place != null || !whole || box.Type.ToLowerInvariant().Contains("gps");
            p.Items.Add(new Item { Box = box, Location = location, Whole = true });
            Row(p.Meta, Shown(box.Type), (name ?? "Unknown box") + " (" + Shown(box.Type) + ")", category, Words(box.End - box.Payload) + (place != null ? ", with a place in it" : ""));
        }

        // a box at a level of the index: padding, one the picture and sound need, or an unknown one
        static void Other(Parsed p, byte[] m, long fileBase, Box box, string[] keeps, string name)
        {
            if (box.Type == "free" || box.Type == "skip" || box.Type == "wide") p.Padding.Add(box);
            else if (!keeps.Contains(box.Type)) Unknown(p, box, m, box.Payload - fileBase, box.End - fileBase, true, "Track", name);
        }

        // a box's children; bytes after the last one that aren't zeros mean it's damaged
        static List<Box> Kids(Parsed p, byte[] m, long fileBase, Box box)
        {
            long start = box.Payload - fileBase, end = box.End - fileBase;
            List<Box> kids = Children(m, fileBase, start, end);
            long after = kids.Count > 0 ? kids[kids.Count - 1].End - fileBase : start;
            for (long i = after; i < end; i++)
                if (m[i] != 0)
                {
                    p.Damaged(Shown(box.Type));
                    break;
                }
            return kids;
        }

        // whether a header's date at `at` (in the moov) is set
        static bool Nonzero(Parsed p, long at)
        {
            var (start, length) = p.Dates.First(d => d.At == at);
            for (long i = 0; i < length; i++)
                if (p.MoovBytes[start - p.Moov.Start + i] != 0) return true;
            return false;
        }

        static string FtypBrand(Stream s, Box ftyp)
        {
            if (ftyp.Size < 12) return "";
            var b = new byte[4];
            ReadExactly(s, ftyp.Start + 8, b, 4);
            return Fourcc(b, 0);
        }

        static string Shown(string type) => new string(type.Select(c => c >= 0x20 && c <= 0x7e || c == 0xa9 ? c : '?').ToArray());

        static string Words(long bytes) => JsText.Grouped(bytes) + " bytes";

        static string Utc(DateTime d) => d.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + " UTC";

        static void AddItem(Parsed p, Box box, bool location, string tagHex, string category, string name, string shown, string key = null)
        {
            p.Items.Add(new Item { Box = box, Location = location, Whole = true, Key = key });
            Row(p.Meta, tagHex, name, category, shown);
        }

        static void Row(ImageMetadata meta, string tagHex, string name, string category, string shown)
        {
            if (meta.AllTags.Count < ExifReader.MaxListedTags) meta.AllTags.Add(new ParsedExifTag(0, tagHex, name, category, shown.Length > 500 ? shown.Substring(0, 500) + "…" : shown));
        }

        static readonly DateTime epoch1904 = new DateTime(1904, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        static DateTime? HeaderDate(ulong seconds)
        {
            if (seconds == 0) return null;
            try
            {
                return epoch1904.AddSeconds(seconds);
            }
            catch (ArgumentOutOfRangeException)
            {
                return null;
            }
        }

        static void ParseMoov(Parsed p, List<string> texts)
        {
            byte[] m = p.MoovBytes;
            long fileBase = p.Moov.Start;
            foreach (Box box in Kids(p, m, fileBase, p.Moov))
            {
                try
                {
                    switch (box.Type)
                    {
                        case "mvhd":
                        {
                            long b = box.Payload - fileBase, end = box.End - fileBase;
                            bool v1 = m[b] == 1;
                            // (its dates, time scale and length inside it, or it's damaged)
                            if (b + (v1 ? 32 : 20) > end)
                            {
                                p.Damaged("mvhd");
                                break;
                            }
                            ulong created = v1 ? U64(m, b + 4) : U32(m, b + 4);
                            ulong timescale = v1 ? U32(m, b + 20) : U32(m, b + 12);
                            ulong duration = v1 ? U64(m, b + 24) : U32(m, b + 16);
                            p.Dates.Add((box.Payload + 4, v1 ? 16 : 8));
                            p.Meta.Video.Created = HeaderDate(created);
                            if (timescale > 0) p.Meta.Video.Seconds = duration / (double)timescale;
                            if (p.Meta.Video.Created.HasValue) Row(p.Meta, "mvhd", "Created (movie header)", "Movie", Utc(p.Meta.Video.Created.Value));
                            ulong modified = v1 ? U64(m, b + 12) : U32(m, b + 8);
                            if (HeaderDate(modified) is DateTime mod) Row(p.Meta, "mvhd", "Modified (movie header)", "Movie", Utc(mod));
                            break;
                        }
                        case "trak":
                            ParseTrak(p, box);
                            break;
                        case "udta":
                            ParseUdta(p, m, fileBase, box, texts);
                            break;
                        case "meta":
                            ParseMeta(p, m, fileBase, box, texts);
                            break;
                        case "uuid":
                        {
                            var payload = new byte[box.End - box.Payload];
                            Array.Copy(m, box.Payload - fileBase, payload, 0, payload.Length);
                            ReadUuid(p, box, payload, true, texts, "uuid box");
                            break;
                        }
                        case "free":
                        case "skip":
                        case "wide":
                            p.Padding.Add(box);
                            break;
                        case "iods":
                            break;
                        case "mvex":
                            p.Refuse = p.Refuse ?? Fragmented;
                            break;
                        case "cmov":
                        case "rmra":
                            p.Refuse = p.Refuse ?? "its index is compressed, or it points to other files, so it can't be cleaned safely";
                            break;
                        default:
                            Unknown(p, box, m, box.Payload - fileBase, box.End - fileBase, true, "Movie", "Unknown box in the index");
                            break;
                    }
                }
                catch (Exception x) when (IsDamage(x))
                {
                    p.Damaged(Shown(box.Type));
                }
            }
            p.Meta.Video.Tracks = p.Tracks.Count;
            Track v = p.Tracks.FirstOrDefault(t => t.Handler == "vide");
            if (v != null)
            {
                p.Meta.Video.Width = v.Width;
                p.Meta.Video.Height = v.Height;
                p.Meta.Video.Rotation = v.Rotation;
            }
            // the tracks that aren't the picture or sound: metadata, captions (a drone's say where
            // it flew), timecodes (the time of day)
            foreach (Track t in p.Tracks.Where(t => !t.Picture))
            {
                string bytes = JsText.Grouped(t.Chunks.Sum(c => c.End - c.Start)) + " bytes of samples";
                if (t.Timed)
                {
                    p.Meta.Video.TimedMetadata.Add(t.Kind.Length > 0 ? t.Kind : t.Handler);
                    Row(p.Meta, t.Kind.Length > 0 ? Shown(t.Kind) : "trak", "Timed metadata track (" + TimedName(t.Kind) + ")", "Track", bytes + (t.HoldsLocation ? ", can hold a location" : ""));
                }
                else Row(p.Meta, t.Handler.Length > 0 ? Shown(t.Handler) : "trak", TrackName(t.Handler), "Track", bytes);
            }
        }

        static string TrackName(string handler)
        {
            switch (handler)
            {
                case "text":
                case "sbtl":
                case "subt":
                    return "Text track (captions; a drone's or dashcam's can say where)";
                case "tmcd": return "Timecode track (the time of day it was filmed)";
                case "hint": return "Hint track (for streaming)";
                case "clcp": return "Closed captions track";
                default: return "Track (" + (handler.Length > 0 ? Shown(handler) : "unknown") + ")";
            }
        }

        static string TimedName(string kind)
        {
            switch (kind)
            {
                case "mebx": return "Apple";
                case "gpmd": return "GoPro, with GPS";
                case "camm": return "camera motion";
                case "mett": return "text";
                case "metx": return "XML";
                default: return kind.Length > 0 ? Shown(kind) : "metadata";
            }
        }

        static void ParseTrak(Parsed p, Box trak)
        {
            byte[] m = p.MoovBytes;
            long fileBase = p.Moov.Start;
            var t = new Track { Trak = trak };
            p.Tracks.Add(t);
            Box stbl = null;
            foreach (Box box in Kids(p, m, fileBase, trak))
            {
                long b = box.Payload - fileBase, end = box.End - fileBase;
                switch (box.Type)
                {
                    case "tkhd":
                    {
                        bool v1 = m[b] == 1;
                        if (b + 4 + (v1 ? 16 : 8) > end)
                        {
                            p.Damaged("tkhd");
                            break;
                        }
                        p.Dates.Add((box.Payload + 4, v1 ? 16 : 8));
                        long matrix = b + (v1 ? 52 : 40);
                        if (matrix + 44 <= end)
                        {
                            int a = (int)U32(m, matrix), bb = (int)U32(m, matrix + 4);
                            t.Rotation = a == 0 && bb > 0 ? 90 : a < 0 && bb == 0 ? 180 : a == 0 && bb < 0 ? 270 : 0;
                            t.Width = (int)(U32(m, matrix + 36) >> 16);
                            t.Height = (int)(U32(m, matrix + 40) >> 16);
                        }
                        break;
                    }
                    case "mdia":
                        foreach (Box child in Kids(p, m, fileBase, box))
                        {
                            long cb = child.Payload - fileBase, cend = child.End - fileBase;
                            switch (child.Type)
                            {
                                case "mdhd":
                                {
                                    bool v1 = m[cb] == 1;
                                    if (cb + (v1 ? 32 : 20) > cend)
                                    {
                                        p.Damaged("mdhd");
                                        break;
                                    }
                                    p.Dates.Add((child.Payload + 4, v1 ? 16 : 8));
                                    ulong timescale = v1 ? U32(m, cb + 20) : U32(m, cb + 12), duration = v1 ? U64(m, cb + 24) : U32(m, cb + 16);
                                    if (timescale > 0) t.Seconds = duration / (double)timescale;
                                    break;
                                }
                                case "hdlr":
                                    // its kind; after it, the maker and the handler's name ("GoPro AVC",
                                    // QuickTime's "appl"), zeroed in all mode
                                    if (cb + 12 > cend) break;
                                    t.Handler = Fourcc(m, (int)cb + 8);
                                    if (cend > cb + 12) t.Names.Add((child.Payload + 12, cend - cb - 12));
                                    break;
                                case "minf":
                                    foreach (Box inner in Kids(p, m, fileBase, child))
                                    {
                                        long ib = inner.Payload - fileBase, iend = inner.End - fileBase;
                                        if (inner.Type == "stbl") stbl = inner;
                                        else if (inner.Type == "hdlr")
                                        {
                                            if (iend > ib + 12) t.Names.Add((inner.Payload + 12, iend - ib - 12));
                                        }
                                        else if (inner.Type == "dinf") DataReferences(p, m, fileBase, inner);
                                        else Other(p, m, fileBase, inner, minfKeeps, "Unknown box in a track");
                                    }
                                    break;
                                default:
                                    Other(p, m, fileBase, child, mdiaKeeps, "Unknown box in a track");
                                    break;
                            }
                        }
                        break;
                    case "udta":
                        ParseUdta(p, m, fileBase, box, null);
                        break;
                    case "meta":
                        ParseMeta(p, m, fileBase, box, null);
                        break;
                    case "uuid":
                    {
                        var payload = new byte[end - b];
                        Array.Copy(m, b, payload, 0, payload.Length);
                        ReadUuid(p, box, payload, true, null, "uuid box in a track");
                        break;
                    }
                    default:
                        Other(p, m, fileBase, box, trakKeeps, "Unknown box in a track");
                        break;
                }
            }
            if (stbl != null) ReadSampleTable(p, t, m, fileBase, stbl);
            else if (t.Picture) p.Damaged("a track with no sample table");
        }

        // A track's data references: each must say "in this file" (a flag on it); one pointing to
        // another file (QuickTime's alias, with its path) can't be cleaned here.
        static void DataReferences(Parsed p, byte[] m, long fileBase, Box dinf)
        {
            foreach (Box dref in Kids(p, m, fileBase, dinf))
            {
                if (dref.Type != "dref") continue;
                long b = dref.Payload - fileBase, end = dref.End - fileBase;
                if (b + 8 > end) continue;
                long n = U32(m, b + 4), at = b + 8;
                for (long i = 0; i < n && at + 12 <= end; i++)
                {
                    long size = U32(m, at);
                    if (size < 12 || at + size > end) break;
                    if ((U32(m, at + 8) & 1) == 0) p.Refuse = p.Refuse ?? "its picture or sound is kept in another file, so it can't be cleaned here";
                    at += size;
                }
            }
        }

        // where a track's samples are, chunk by chunk, as players work them out
        static void ReadSampleTable(Parsed p, Track t, byte[] m, long fileBase, Box stbl)
        {
            List<long> chunkOffsets = null;
            List<(long First, long PerChunk)> stsc = null;
            long fixedSize = 0, count = 0;
            List<long> sizes = null;
            bool oneTick = false;   // stts: every sample lasts one tick
            foreach (Box box in Kids(p, m, fileBase, stbl))
            {
                long b = box.Payload - fileBase, end = box.End - fileBase;
                switch (box.Type)
                {
                    case "stsd":
                        t.Stsd = new byte[end - b];
                        Array.Copy(m, b, t.Stsd, 0, t.Stsd.Length);
                        if (b + 16 <= end) t.Kind = Fourcc(m, (int)b + 12);
                        SampleNames(t, m, fileBase, b, end);
                        break;
                    case "stts":
                        if (b + 16 <= end) oneTick = U32(m, b + 4) == 1 && U32(m, b + 12) == 1;
                        break;
                    case "stco":
                    case "co64":
                    {
                        if (b + 8 > end)
                        {
                            p.Damaged(box.Type);
                            break;
                        }
                        long n = U32(m, b + 4);
                        int each = box.Type == "co64" ? 8 : 4;
                        chunkOffsets = new List<long>();
                        for (long i = 0; i < n && b + 8 + (i + 1) * each <= end; i++)
                        {
                            ulong offset = each == 8 ? U64(m, b + 8 + i * 8) : U32(m, b + 8 + i * 4);
                            if (offset > (ulong)p.Length)
                            {
                                p.Damaged(box.Type);
                                break;
                            }
                            chunkOffsets.Add((long)offset);
                        }
                        break;
                    }
                    case "stsc":
                    {
                        if (b + 8 > end)
                        {
                            p.Damaged("stsc");
                            break;
                        }
                        long n = U32(m, b + 4);
                        stsc = new List<(long, long)>();
                        for (long i = 0; i < n && b + 8 + (i + 1) * 12 <= end; i++) stsc.Add((U32(m, b + 8 + i * 12), U32(m, b + 12 + i * 12)));
                        break;
                    }
                    case "stsz":
                        if (b + 12 > end)
                        {
                            p.Damaged("stsz");
                            break;
                        }
                        fixedSize = U32(m, b + 4);
                        count = U32(m, b + 8);
                        if (fixedSize == 0)
                        {
                            sizes = new List<long>();
                            for (long i = 0; i < count && b + 12 + (i + 1) * 4 <= end; i++) sizes.Add(U32(m, b + 12 + i * 4));
                        }
                        break;
                    case "stz2":
                    {
                        int field = b + 12 <= end ? m[b + 7] : 0;
                        if (field != 4 && field != 8 && field != 16)
                        {
                            p.Damaged("stz2");
                            break;
                        }
                        count = U32(m, b + 8);
                        sizes = new List<long>();
                        for (long i = 0; i < count; i++)
                        {
                            long bit = i * field, at = b + 12 + bit / 8;
                            if (at + (field == 16 ? 2 : 1) > end) break;
                            sizes.Add(field == 16 ? U16(m, at) : field == 8 ? m[at] : (bit % 8 == 0 ? m[at] >> 4 : m[at] & 15));
                        }
                        break;
                    }
                    default:
                        Other(p, m, fileBase, box, stblKeeps, "Unknown box in a sample table");
                        break;
                }
            }
            if (chunkOffsets == null || stsc == null || stsc.Count == 0)
            {
                if (t.Picture && chunkOffsets != null && chunkOffsets.Count > 0) p.Damaged("a sample table");
                return;
            }
            // old QuickTime sound (a sample is one frame, one tick long): a chunk's bytes come from
            // the sound description (ffmpeg's and players' reading), not the sample size, which is 1
            bool frames = t.Handler == "soun" && oneTick;
            long perPacket = 0, packetBytes = 0, frameBytes = 0;
            if (frames) SoundFrames(t, out perPacket, out packetBytes, out frameBytes);
            long sample = 0;
            int run = 0;
            for (int c = 0; c < chunkOffsets.Count; c++)
            {
                long chunk = c + 1;
                while (run + 1 < stsc.Count && stsc[run + 1].First <= chunk) run++;
                long perChunk = stsc[run].First <= chunk ? stsc[run].PerChunk : 0;
                long samples = Math.Max(0, Math.Min(perChunk, count - sample));
                long bytes;
                if (frames && perPacket > 0 && packetBytes > 0 && Mul(samples, packetBytes) >= 0 && Mul(samples, packetBytes) % perPacket == 0) bytes = Mul(samples, packetBytes) / perPacket;
                else if (frames && frameBytes > 0) bytes = Mul(samples, frameBytes);
                else if (sizes != null)
                {
                    bytes = 0;
                    for (long k = 0; k < samples && sample + k < sizes.Count; k++) bytes += sizes[(int)(sample + k)];
                    if (frames) t.Uncertain = true;
                }
                else
                {
                    bytes = Mul(samples, fixedSize);
                    if (frames) t.Uncertain = true;
                }
                sample += samples;
                if (bytes < 0 || bytes > p.Length - chunkOffsets[c])
                {
                    p.Damaged("a chunk's size");
                    return;
                }
                if (bytes > 0) t.Chunks.Add((chunkOffsets[c], chunkOffsets[c] + bytes));
            }
        }

        // The sample descriptions' names and makers: a picture's encoder name (32 bytes in) and
        // vendor (QuickTime's "appl"), a sound's vendor. Zeroed in all mode; nothing plays them.
        static void SampleNames(Track t, byte[] m, long fileBase, long b, long end)
        {
            if (b + 8 > end) return;
            long n = U32(m, b + 4), at = b + 8;
            for (long i = 0; i < n && at + 8 <= end; i++)
            {
                long size = U32(m, at);
                if (size < 8 || at + size > end) break;
                bool picture = t.Handler == "vide" || t.Handler == "auxv";
                if (picture && size >= 86)
                {
                    t.Names.Add((fileBase + at + 20, 4));
                    t.Names.Add((fileBase + at + 50, 32));
                }
                else if (t.Handler == "soun" && size >= 28) t.Names.Add((fileBase + at + 20, 4));
                at += size;
            }
        }

        // An old QuickTime sound's frames, from its first sound description: version 1 and 2 say
        // how many frames make a packet and its bytes; plain PCM's frame bytes come from its format
        // and sample size (as ffmpeg reads them).
        static void SoundFrames(Track t, out long perPacket, out long packetBytes, out long frameBytes)
        {
            perPacket = packetBytes = frameBytes = 0;
            byte[] d = t.Stsd;
            if (d == null || d.Length < 8 + 36) return;
            const long e = 8;   // (after the version, flags and count)
            long size = U32(d, e);
            if (size < 36 || e + size > d.Length) return;
            string format = Fourcc(d, (int)e + 4);
            int version = U16(d, e + 16);
            long channels = U16(d, e + 24), bits = U16(d, e + 26);
            if (version == 1 && size >= 52)
            {
                perPacket = U32(d, e + 36);
                packetBytes = U32(d, e + 44);
            }
            else if (version == 2 && size >= 72)
            {
                channels = U32(d, e + 48);
                packetBytes = U32(d, e + 64);
                perPacket = U32(d, e + 68);
            }
            switch (format)
            {
                case "twos":
                case "sowt":
                case "raw ":
                case "NONE":
                    frameBytes = Math.Max(1, bits / 8) * channels;
                    break;
                case "in24":
                    frameBytes = 3 * channels;
                    break;
                case "in32":
                case "fl32":
                    frameBytes = 4 * channels;
                    break;
                case "fl64":
                    frameBytes = 8 * channels;
                    break;
                case "ulaw":
                case "alaw":
                    frameBytes = channels;
                    break;
            }
        }

        // QuickTime user data: ©xyz and its kin, 3GPP's loci, Samsung's smta, XMP_, a meta box
        static void ParseUdta(Parsed p, byte[] buf, long fileBase, Box udta, List<string> texts)
        {
            // (the box goes whole in all mode, with whatever's in it)
            p.Items.Add(new Item { Box = udta, Whole = true, Counted = false });
            foreach (Box box in Children(buf, fileBase, udta.Payload - fileBase, udta.End - fileBase))
            {
                long b = box.Payload - fileBase, end = box.End - fileBase;
                string type = box.Type;
                if (type == "free" || type == "skip")
                {
                    p.Padding.Add(box);
                    continue;
                }
                if (type == "meta")
                {
                    ParseMeta(p, buf, fileBase, box, texts, udta);
                    continue;
                }
                bool location = false;
                string shown, name = UdtaName(type);
                if (type == "@xyz")
                {
                    // Samsung's place, as ©xyz is written
                    string text = QuickTimeText(buf, b, end);
                    shown = text;
                    location = true;
                    var gps = Iso6709(text);
                    if (gps != null && p.Meta.Gps == null) p.Meta.Gps = gps;
                }
                else if (type[0] == (char)0xa9)
                {
                    string text = QuickTimeText(buf, b, end);
                    shown = text;
                    texts?.Add(text);
                    switch (type.Substring(1))
                    {
                        case "xyz":
                            location = true;
                            var gps = Iso6709(text);
                            if (gps != null && p.Meta.Gps == null) p.Meta.Gps = gps;
                            break;
                        case "mak": if (string.IsNullOrEmpty(p.Meta.Make)) p.Meta.Make = text; break;
                        case "mod": if (string.IsNullOrEmpty(p.Meta.Model)) p.Meta.Model = text; break;
                        case "swr": if (string.IsNullOrEmpty(p.Meta.Software)) p.Meta.Software = text; break;
                        case "day": if (string.IsNullOrEmpty(p.Meta.DateTime)) p.Meta.DateTime = text; break;
                        case "ART": if (string.IsNullOrEmpty(p.Meta.Artist)) p.Meta.Artist = text; break;
                        case "cpy": if (string.IsNullOrEmpty(p.Meta.Copyright)) p.Meta.Copyright = text; break;
                        case "des":
                        case "cmt": if (string.IsNullOrEmpty(p.Meta.Description)) p.Meta.Description = text; break;
                    }
                }
                else if (type == "loci")
                {
                    location = true;
                    var gps = Loci(buf, b, end, out string place);
                    shown = gps != null ? ExifReader.GpsDecimal(gps) + (place.Length > 0 ? " (" + place + ")" : "") : Words(end - b);
                    if (gps != null && p.Meta.Gps == null) p.Meta.Gps = gps;
                }
                else if (type == "XMP_")
                {
                    string xmp = JsText.Utf8(buf, (int)b, (int)(end - b));
                    p.Meta.XmpText = (p.Meta.XmpText ?? "") + xmp;
                    location = ExifReader.XmpHasLocation(xmp);
                    shown = JsText.Grouped(xmp.Length) + " characters";
                }
                else if (type == "smta")
                {
                    string model = SamsungModel(buf, b, end);
                    if (model != null && string.IsNullOrEmpty(p.Meta.Model)) p.Meta.Model = model;
                    shown = model != null ? "Samsung data, model " + model : "Samsung data, " + Words(end - b);
                }
                else if (type == "cprt" || type == "titl" || type == "auth" || type == "dscp" || type == "perf" || type == "gnre" || type == "albm" || type == "yrrc")
                {
                    // 3GPP's text boxes: a full box, a language, then the text
                    string text = end - b > 6 ? JsText.Trim(JsText.NoTrailingZeros(Text3gpp(buf, b + 6, end))) : "";
                    shown = text;
                    texts?.Add(text);
                    if (type == "auth" && string.IsNullOrEmpty(p.Meta.Artist)) p.Meta.Artist = text;
                    if (type == "cprt" && string.IsNullOrEmpty(p.Meta.Copyright)) p.Meta.Copyright = text;
                }
                else
                {
                    // a box only its maker reads: a place in it (or GPS in its name) makes it location
                    string place = FindPlace(buf, b, end);
                    location = place != null || type.ToLowerInvariant().Contains("gps");
                    shown = Words(end - b) + (place != null ? ", with a place in it" : "");
                }
                // (any box of user data with a place written in it says where: a comment, a title)
                if (!location && FindPlace(buf, b, end) != null) location = true;
                AddItem(p, box, location, Shown(type), "User data", name, shown);
            }
        }

        static string UdtaName(string type)
        {
            switch (type)
            {
                case "©xyz": return "Location (ISO 6709)";
                case "©mak": return "Make";
                case "©mod": return "Model";
                case "©swr": return "Software";
                case "©day": return "Date";
                case "©too": return "Encoder";
                case "©nam": return "Title";
                case "©cmt": return "Comment";
                case "©des": return "Description";
                case "©ART": return "Artist";
                case "©cpy": return "Copyright";
                case "loci": return "Location (3GPP)";
                case "XMP_": return "XMP Metadata Packet";
                case "smta": return "Samsung metadata";
                case "@xyz": return "Location (Samsung)";
                case "auth": return "Author";
                case "cprt": return "Copyright";
                case "titl": return "Title";
                case "dscp": return "Description";
                case "name": return "Track name";
                default: return "User data (" + Shown(type) + ")";
            }
        }

        // a QuickTime text atom: (a 16-bit length, a language, the text), the first of them
        static string QuickTimeText(byte[] b, long at, long end)
        {
            if (at + 4 > end) return "";
            int len = U16(b, at), lang = U16(b, at + 2);
            long start = at + 4, stop = Math.Min(end, start + len);
            if (len == 0 || stop <= start)
            {
                // some writers put the text straight in
                return JsText.Trim(JsText.NoTrailingZeros(JsText.Utf8Fatal(b, (int)at, (int)(end - at)) ?? JsText.Latin1(b, (int)at, (int)(end - at))));
            }
            int count = (int)(stop - start);
            // a Macintosh language code (below 0x400) means Mac text; else UTF-8
            string text = lang < 0x400 ? (JsText.Utf8Fatal(b, (int)start, count) ?? JsText.Latin1(b, (int)start, count)) : JsText.Utf8(b, (int)start, count);
            return JsText.Trim(JsText.NoTrailingZeros(text));
        }

        // 3GPP text: UTF-16 when it starts with a byte-order mark, else UTF-8, to a closing zero
        static string Text3gpp(byte[] b, long at, long end)
        {
            if (at + 2 <= end && b[at] == 0xfe && b[at + 1] == 0xff)
            {
                long stop = at + 2;
                while (stop + 1 < end && (b[stop] != 0 || b[stop + 1] != 0)) stop += 2;
                return JsText.Utf16(b, (int)at, (int)(stop - at), false);
            }
            long z = at;
            while (z < end && b[z] != 0) z++;
            return JsText.Utf8(b, (int)at, (int)(z - at));
        }

        static double Fixed1616(byte[] b, long at) => (int)U32(b, at) / 65536.0;

        // 3GPP's location: a full box, a language, the place's name, its role, longitude,
        // latitude and altitude (16.16), the astronomical body and notes
        static GpsCoordinates Loci(byte[] b, long at, long end, out string place)
        {
            place = "";
            long p = at + 6;
            if (p > end) return null;
            long z = p;
            bool utf16 = p + 2 <= end && b[p] == 0xfe && b[p + 1] == 0xff;
            if (utf16)
            {
                z = p + 2;
                while (z + 1 < end && (b[z] != 0 || b[z + 1] != 0)) z += 2;
                place = JsText.Utf16(b, (int)p, (int)(z - p), false);
                z += 2;
            }
            else
            {
                while (z < end && b[z] != 0) z++;
                place = JsText.Utf8(b, (int)p, (int)(z - p));
                z++;
            }
            long q = z + 1;   // the role
            if (q + 12 > end) return null;
            double lon = Fixed1616(b, q), lat = Fixed1616(b, q + 4), alt = Fixed1616(b, q + 8);
            if (Math.Abs(lat) > 90 || Math.Abs(lon) > 180) return null;
            return new GpsCoordinates { Latitude = lat, Longitude = lon, Altitude = alt, LatRef = lat < 0 ? "S" : "N", LonRef = lon < 0 ? "W" : "E" };
        }

        // Samsung's smta box: its mdln box names the model
        static string SamsungModel(byte[] b, long at, long end)
        {
            for (long i = at; i + 8 <= end; i++)
                if (b[i + 4] == (byte)'m' && b[i + 5] == (byte)'d' && b[i + 6] == (byte)'l' && b[i + 7] == (byte)'n')
                {
                    long size = U32(b, i);
                    if (size < 8 || i + size > end) return null;
                    string text = JsText.Trim(JsText.NoTrailingZeros(JsText.Utf8(b, (int)i + 8, (int)size - 8)));
                    return text.Length > 0 ? text : null;
                }
            return null;
        }

        // ISO 6709 ("+37.3349-122.0090+010.000/", or with minutes and seconds: "+3720.1-12201.7/")
        public static GpsCoordinates Iso6709(string text)
        {
            string s = text.Trim();
            var parts = new List<string>();
            int i = 0;
            while (i < s.Length && (s[i] == '+' || s[i] == '-') && parts.Count < 3)
            {
                int j = i + 1;
                while (j < s.Length && (char.IsDigit(s[j]) && s[j] < 128 || s[j] == '.')) j++;
                parts.Add(s.Substring(i, j - i));
                i = j;
            }
            if (parts.Count < 2) return null;
            double? Part(string t, int degreeDigits)
            {
                bool negative = t[0] == '-';
                string n = t.Substring(1);
                int dot = n.IndexOf('.');
                string whole = dot < 0 ? n : n.Substring(0, dot), frac = dot < 0 ? "" : n.Substring(dot);
                if (whole.Length == 0) return null;
                double v;
                if (whole.Length <= degreeDigits) v = Parse(whole + frac);
                else if (whole.Length == degreeDigits + 2) v = Parse(whole.Substring(0, degreeDigits)) + Parse(whole.Substring(degreeDigits) + frac) / 60;
                else if (whole.Length == degreeDigits + 4) v = Parse(whole.Substring(0, degreeDigits)) + Parse(whole.Substring(degreeDigits, 2)) / 60 + Parse(whole.Substring(degreeDigits + 2) + frac) / 3600;
                else return null;
                if (double.IsNaN(v)) return null;
                return negative ? -v : v;
            }
            double? lat = Part(parts[0], 2), lon = Part(parts[1], 3);
            if (lat == null || lon == null || Math.Abs(lat.Value) > 90 || Math.Abs(lon.Value) > 180) return null;
            var gps = new GpsCoordinates { Latitude = lat.Value, Longitude = lon.Value, LatRef = lat < 0 ? "S" : "N", LonRef = lon < 0 ? "W" : "E" };
            if (parts.Count == 3)
            {
                double alt = Parse(parts[2]);
                if (!double.IsNaN(alt)) gps.Altitude = alt;
            }
            return gps;
        }

        static double Parse(string t) => double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : double.NaN;

        // a meta box: its keys (Apple's and Android's mdta) and items, or iTunes-style items
        static void ParseMeta(Parsed p, byte[] buf, long fileBase, Box meta, List<string> texts, Box inUdta = null)
        {
            long payload = meta.Payload - fileBase, end = meta.End - fileBase;
            var mb = new MetaBox { Meta = meta, Buf = buf, FileBase = fileBase, ChildrenAt = MetaChildren(buf, payload, end) + fileBase };
            mb.Children = Children(buf, fileBase, mb.ChildrenAt - fileBase, end);
            mb.Keys = mb.Children.FirstOrDefault(c => c.Type == "keys");
            mb.Ilst = mb.Children.FirstOrDefault(c => c.Type == "ilst");
            if (mb.Keys != null)
            {
                long b = mb.Keys.Payload - fileBase, kend = mb.Keys.End - fileBase;
                long n = U32(buf, b + 4), at = b + 8;
                for (long i = 0; i < n && at + 8 <= kend; i++)
                {
                    long size = U32(buf, at);
                    if (size < 8 || at + size > kend) break;
                    var entry = new byte[size];
                    Array.Copy(buf, at, entry, 0, size);
                    mb.KeyEntries.Add((entry, JsText.Utf8(buf, (int)at + 8, (int)size - 8)));
                    at += size;
                }
            }
            bool any = false;
            if (mb.Ilst != null)
                foreach (Box item in Children(buf, fileBase, mb.Ilst.Payload - fileBase, mb.Ilst.End - fileBase))
                {
                    long ib = item.Start - fileBase;
                    int index = (int)U32(buf, ib + 4);
                    string key = mb.Keys != null && index >= 1 && index <= mb.KeyEntries.Count ? mb.KeyEntries[index - 1].Name : item.Type;
                    mb.Items.Add((item, index, key));
                    string value = ItemValue(p, buf, fileBase, item, key);
                    if (value != null && texts != null && !IsImage(buf, fileBase, item)) texts.Add(value);
                    bool location = key.IndexOf("location", StringComparison.OrdinalIgnoreCase) >= 0 || key == "©xyz" || key == "@xyz" ||
                        FindPlace(buf, item.Payload - fileBase, item.End - fileBase) != null;
                    p.Items.Add(new Item { Box = item, Location = location, Whole = false, Key = key });
                    Row(p.Meta, mb.Keys != null ? "key" : Shown(key), mb.Keys != null ? Shown(key) : ItemName(key), "Keys", value ?? "");
                    any = true;
                }
            p.Metas.Add(mb);
            // the meta box itself goes in all mode, its items with it; one with no item that's read
            // still counts as one thing
            p.Items.Add(new Item { Box = meta, Whole = true, Counted = !any });
            if (!any) Row(p.Meta, "meta", "Metadata box", inUdta != null ? "User data" : "Keys", Words(meta.Size));
        }

        static string ItemName(string key)
        {
            switch (key)
            {
                case "©xyz": return "Location (ISO 6709)";
                case "©too": return "Encoder";
                case "©nam": return "Title";
                case "©day": return "Date";
                case "©cmt": return "Comment";
                case "©ART": return "Artist";
                case "covr": return "Cover picture";
                default: return "Item (" + Shown(key) + ")";
            }
        }

        static bool IsImage(byte[] buf, long fileBase, Box item)
        {
            foreach (Box data in Children(buf, fileBase, item.Payload - fileBase, item.End - fileBase))
                if (data.Type == "data")
                {
                    long b = data.Payload - fileBase;
                    int type = (int)(U32(buf, b) & 0xffffff);
                    return type == 13 || type == 14 || type == 27;
                }
            return false;
        }

        // an item's value as text, and what it says about the video
        static string ItemValue(Parsed p, byte[] buf, long fileBase, Box item, string key)
        {
            foreach (Box data in Children(buf, fileBase, item.Payload - fileBase, item.End - fileBase))
            {
                if (data.Type != "data") continue;
                long b = data.Payload - fileBase, end = data.End - fileBase;
                if (b + 8 > end) return "";
                int type = (int)(U32(buf, b) & 0xffffff);
                long v = b + 8;
                int count = (int)(end - v);
                string text;
                switch (type)
                {
                    case 1:
                    case 4: text = JsText.Utf8(buf, (int)v, count); break;
                    case 2:
                    case 5: text = JsText.Utf16(buf, (int)v, count, false); break;
                    case 13:
                    case 14:
                    case 27:
                        if (!p.Meta.HasThumbnail)
                        {
                            p.Meta.HasThumbnail = true;
                            var picture = new byte[count];
                            Array.Copy(buf, v, picture, 0, count);
                            p.Meta.Thumbnail = new EmbeddedThumbnail { MimeType = type == 14 ? "image/png" : type == 27 ? "image/bmp" : "image/jpeg", Data = picture };
                        }
                        return Words(count) + " (a picture)";
                    case 21:
                    case 22:
                    case 65: case 66: case 67: case 74:
                    case 75: case 76: case 77: case 78:
                    {
                        bool signed = type == 21 || (type >= 65 && type <= 74);
                        if (count < 1 || count > 8) return Words(count);
                        long n = 0;
                        for (int k = 0; k < count; k++) n = (n << 8) | buf[v + k];
                        if (signed && count < 8 && (buf[v] & 0x80) != 0) n -= 1L << (count * 8);
                        text = n.ToString(CultureInfo.InvariantCulture);
                        break;
                    }
                    case 23:
                        if (count != 4) return Words(count);
                        text = JsText.Num(BitConverter.ToSingle(BitConverter.GetBytes(U32(buf, v)), 0));
                        break;
                    case 24:
                        if (count != 8) return Words(count);
                        text = JsText.Num(BitConverter.Int64BitsToDouble((long)U64(buf, v)));
                        break;
                    default: return Words(count);
                }
                text = JsText.Trim(JsText.NoTrailingZeros(text));
                string k2 = key.ToLowerInvariant();
                if (k2.EndsWith("location.iso6709", StringComparison.Ordinal) || key == "©xyz")
                {
                    var gps = Iso6709(text);
                    if (gps != null) p.Meta.Gps = gps;   // the keys come first
                }
                else if (k2 == "make" || k2.EndsWith(".make", StringComparison.Ordinal) || k2.EndsWith(".manufacturer", StringComparison.Ordinal)) p.Meta.Make = text;
                else if (k2 == "model" || k2.EndsWith(".model", StringComparison.Ordinal)) p.Meta.Model = text;
                else if (k2 == "software" || k2.EndsWith(".software", StringComparison.Ordinal)) p.Meta.Software = text;
                else if (k2.EndsWith("creationdate", StringComparison.Ordinal) || k2 == "©day" || k2 == "date") p.Meta.DateTime = text;
                else if (k2.EndsWith(".author", StringComparison.Ordinal) || k2 == "©art" || k2 == "artist") p.Meta.Artist = text;
                else if (k2.EndsWith(".copyright", StringComparison.Ordinal) || k2 == "©cpy" || k2 == "copyright") p.Meta.Copyright = text;
                else if (k2.EndsWith(".description", StringComparison.Ordinal) || k2 == "©des" || k2 == "description") p.Meta.Description = text;
                return text;
            }
            return null;
        }

        // A uuid box (`payload`: its bytes after the uuid, all of them when `whole`): XMP, Content
        // Credentials (a place in either makes it location), or one only its maker reads.
        static void ReadUuid(Parsed p, Box box, byte[] payload, bool whole, List<string> texts, string name)
        {
            bool place = !whole || FindPlace(payload, 0, payload.Length) != null;
            if (box.Uuid == Xmp && whole)
            {
                string xmp = JsText.Utf8(payload, 0, payload.Length);
                p.Meta.XmpText = (p.Meta.XmpText ?? "") + xmp;
                AddItem(p, box, ExifReader.XmpHasLocation(xmp) || place, "XMP", "XMP", "XMP Metadata Packet", JsText.Grouped(xmp.Length) + " characters");
            }
            else if (box.Uuid == C2pa && whole)
            {
                ContentCredentials found = AiMarkers.FromJumbf(payload, "MP4 uuid box");
                if (found != null && p.Credentials == null) p.Credentials = found;
                AddItem(p, box, place, "C2PA", "C2PA", "Content Credentials", Words(payload.Length) + (found?.Generator != null ? ", signed by " + found.Generator : ""));
            }
            else AddItem(p, box, place, "uuid", "Box", name + " (" + box.Uuid + ")", Words(box.End - box.Payload) + (place ? ", can hold a place" : ""));
        }

        // ---------- the inspector ----------

        // everything the inspector finds in a video file (the picture data isn't read)
        public static ImageMetadata Inspect(string path)
        {
            using (var s = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16))
                return Parse(s).Meta;
        }

        public static ImageMetadata Inspect(Stream s) => Parse(s).Meta;

        public static string Narrative(ImageMetadata meta)
        {
            VideoDetails v = meta.Video;
            var parts = new List<string>();
            string camera = string.Join(" ", new[] { meta.Make, meta.Model }.Where(x => !string.IsNullOrEmpty(x)));
            parts.Add(camera.Length > 0 ? "Filmed on a " + camera : "Camera details not recorded");
            if (!string.IsNullOrEmpty(meta.DateTime)) parts.Add("on " + meta.DateTime);
            parts.Add(meta.Gps != null ? "at coordinates " + ExifReader.GpsDecimal(meta.Gps) : "with no GPS location");
            var about = new List<string>();
            if (v.Seconds > 0) about.Add(Length(v.Seconds));
            if (v.Width > 0 && v.Height > 0) about.Add(v.Width + "×" + v.Height);
            if (v.Rotation != 0) about.Add("turned " + v.Rotation + "°");
            if (about.Count > 0) parts.Add("(" + string.Join(", ", about) + ")");
            return string.Join(" ", parts) + ".";
        }

        public static string Length(double seconds)
        {
            if (seconds < 60) return seconds.ToString(seconds < 10 ? "0.0" : "0", CultureInfo.InvariantCulture) + " s";
            var t = TimeSpan.FromSeconds(Math.Round(seconds));
            return t.TotalHours >= 1 ? ((int)t.TotalHours) + ":" + t.Minutes.ToString("00") + ":" + t.Seconds.ToString("00") : t.Minutes + ":" + t.Seconds.ToString("00");
        }

        // ---------- the clean copy ----------

        static readonly byte[] freeType = { (byte)'f', (byte)'r', (byte)'e', (byte)'e' };

        // the box made a 'free' box of the same size: its type changed, everything after its size zeros
        static void Free(VideoCleanPlan plan, Box box)
        {
            long at = box.Start + 4;
            plan.Patches.Add(new VideoCleanPlan.Patch { At = at, Length = 4, Bytes = freeType });
            long from = box.Start + box.SizeFields;
            if (box.End > from) plan.Patches.Add(new VideoCleanPlan.Patch { At = from, Length = box.End - from });
        }

        static bool Inside(Box inner, Box outer) => inner != outer && inner.Start >= outer.Start && inner.End <= outer.End;

        // What the clean copy of a video changes, for all metadata or only where it was filmed.
        // It keeps the picture and sound (in location mode also the timecode, hint tracks and
        // Apple's metadata that names no place); every other track goes, its box freed and its
        // samples zeroed with every byte of the picture data no kept track uses. A file it can't
        // be sure of is refused (an IOException with words).
        static VideoCleanPlan Plan(Parsed p, ScrubOptions options)
        {
            if (p.Refuse != null) throw new IOException(p.Refuse);
            bool all = !options.LocationOnly;
            var plan = new VideoCleanPlan { Length = p.TrailerStart };
            bool Keep(Track t) => t.Picture || !all && (t.Handler == "tmcd" || t.Handler == "hint" || t.Kind == "mebx" && !t.HoldsLocation);
            List<Track> kept = p.Tracks.Where(Keep).ToList(), gone = p.Tracks.Where(t => !Keep(t)).ToList();
            if (!kept.Any(t => t.Picture && t.Chunks.Count > 0)) throw new IOException("the scrubber can't find its picture or sound, so it can't be cleaned safely");
            // every sample where its index says: in the picture data, before where the copy ends
            bool InPictureData((long Start, long End) c) => c.End <= plan.Length && p.Mdats.Any(m => c.Start >= m.Payload && c.End <= m.End);
            if (!p.Tracks.All(t => t.Chunks.All(InPictureData))) throw new IOException("its index points outside its picture data, so it can't be cleaned safely");

            var freed = new List<Box>();
            void FreeOnce(Box box)
            {
                if (freed.Any(f => f == box || Inside(box, f))) return;
                freed.RemoveAll(f => Inside(f, box));
                freed.Add(box);
            }
            var rebuilt = new List<(MetaBox Box, List<Box> Gone)>();
            if (all)
            {
                foreach (Item item in p.Items.Where(i => i.Whole)) FreeOnce(item.Box);
                plan.Removed = p.Items.Count(i => i.Counted);
            }
            else
            {
                foreach (Item item in p.Items.Where(i => i.Location && i.Whole)) FreeOnce(item.Box);
                // a meta box with location items: built again without them (the space left a free
                // box), or freed whole when that can't be done
                foreach (MetaBox mb in p.Metas)
                {
                    var goneItems = mb.Items.Where(it => p.Items.Any(i => i.Box == it.Item && i.Location)).Select(it => it.Item).ToList();
                    if (goneItems.Count == 0) continue;
                    rebuilt.Add((mb, goneItems));
                }
                plan.Removed = p.Items.Count(i => i.Location && i.Counted);
            }
            foreach (Track t in gone) FreeOnce(t.Trak);
            plan.Removed += gone.Count;
            var rebuiltBytes = new List<(MetaBox Box, byte[] Bytes)>();
            foreach (var r in rebuilt)
            {
                if (freed.Any(f => f == r.Box.Meta || Inside(r.Box.Meta, f))) continue;
                byte[] bytes = RebuildMeta(r.Box, r.Gone);
                if (bytes != null) rebuiltBytes.Add((r.Box, bytes));
                else FreeOnce(r.Box.Meta);
            }
            bool Freed(Box box) => freed.Any(f => f == box || Inside(box, f));
            foreach (Box box in freed) Free(plan, box);
            foreach (var r in rebuiltBytes)
                if (!Freed(r.Box.Meta))
                    plan.Patches.Add(new VideoCleanPlan.Patch { At = r.Box.ChildrenAt, Length = r.Bytes.Length, Bytes = r.Bytes });
            bool InRebuilt(long at) => rebuiltBytes.Any(r => !Freed(r.Box.Meta) && at >= r.Box.ChildrenAt && at < r.Box.Meta.End);

            // free and skip boxes are padding: their bytes zeroed in both modes (an app may have left
            // old metadata in one, a dashcam its GPS)
            foreach (Box pad in p.Padding)
                if (!Freed(pad) && !InRebuilt(pad.Start) && pad.End > pad.Start + pad.SizeFields)
                    plan.Patches.Add(new VideoCleanPlan.Patch { At = pad.Start + pad.SizeFields, Length = pad.End - pad.Start - pad.SizeFields });

            // all mode: the headers' dates, and the names and makers in the kept tracks' headers
            if (all)
            {
                foreach (var d in p.Dates)
                    if (!gone.Any(t => d.At >= t.Trak.Start && d.At < t.Trak.End))
                        plan.Patches.Add(new VideoCleanPlan.Patch { At = d.At, Length = d.Length });
                foreach (Track t in kept)
                    foreach (var n in t.Names)
                        plan.Patches.Add(new VideoCleanPlan.Patch { At = n.At, Length = n.Length });
            }

            // the kept tracks' bytes, which nothing may touch (an old QuickTime sound whose chunks'
            // sizes aren't sure: up to where the next chunk starts)
            List<(long Start, long End)> samples = Merge(KeptBytes(p, kept));
            plan.Samples.AddRange(samples);
            // a gone track's bytes that are also a kept one's: zeroing them would change the
            // picture or sound, so such a file isn't cleaned (tracks sharing bytes means it's damaged)
            foreach (Track t in gone)
                foreach (var c in t.Chunks)
                    if (Overlaps(samples, c.Start, c.End)) throw new IOException("its tracks share bytes, so it can't be cleaned without touching the picture or sound");
            // the picture data no kept track uses (the gone tracks' samples, a dashcam's GPS between
            // frames): zeros; when a kept track's sizes aren't sure, just the gone tracks' samples
            if (!kept.Any(t => t.Uncertain))
                foreach (Box mdat in p.Mdats)
                {
                    long at = mdat.Payload, stop = Math.Min(mdat.End, plan.Length);
                    foreach (var s in samples)
                    {
                        if (s.End <= at) continue;
                        if (s.Start >= stop) break;
                        if (s.Start > at) plan.Patches.Add(new VideoCleanPlan.Patch { At = at, Length = s.Start - at });
                        at = Math.Max(at, s.End);
                    }
                    if (at < stop) plan.Patches.Add(new VideoCleanPlan.Patch { At = at, Length = stop - at });
                }
            else
                foreach (Track t in gone)
                    foreach (var c in t.Chunks) plan.Patches.Add(new VideoCleanPlan.Patch { At = c.Start, Length = c.End - c.Start });

            plan.Patches.Sort((a, b) => a.At.CompareTo(b.At));
            // nothing patched may land in the picture or sound
            int k = 0;
            foreach (var patch in plan.Patches)
            {
                while (k < samples.Count && samples[k].End <= patch.At) k++;
                if (k < samples.Count && samples[k].Start < patch.At + patch.Length) throw new IOException("its tracks share bytes, so it can't be cleaned without touching the picture or sound");
            }
            foreach (var patch in plan.Patches.Where(x => x.At + x.Length > plan.Length).ToList())
            {
                if (patch.At >= plan.Length) plan.Patches.Remove(patch);
                else patch.Length = plan.Length - patch.At;
            }
            return plan;
        }

        // The kept tracks' bytes. An old QuickTime sound whose chunks' sizes aren't sure keeps
        // everything up to the next chunk of any track (or the end of its picture data).
        static List<(long Start, long End)> KeptBytes(Parsed p, List<Track> kept)
        {
            var ranges = new List<(long Start, long End)>();
            List<long> starts = null;
            foreach (Track t in kept)
            {
                if (!t.Uncertain)
                {
                    ranges.AddRange(t.Chunks);
                    continue;
                }
                if (starts == null)
                {
                    starts = p.Tracks.SelectMany(x => x.Chunks.Select(c => c.Start)).Distinct().ToList();
                    starts.Sort();
                }
                foreach (var c in t.Chunks)
                {
                    long end = p.Mdats.Where(m => c.Start >= m.Payload && c.Start < m.End).Select(m => m.End).DefaultIfEmpty(c.End).First();
                    int i = starts.BinarySearch(c.Start);
                    i = i < 0 ? ~i : i + 1;
                    if (i < starts.Count) end = Math.Min(end, starts[i]);
                    ranges.Add((c.Start, Math.Max(end, c.End)));
                }
            }
            return ranges;
        }

        // ranges sorted, and those that touch joined
        static List<(long Start, long End)> Merge(List<(long Start, long End)> ranges)
        {
            ranges.Sort((a, b) => a.Start.CompareTo(b.Start));
            var merged = new List<(long Start, long End)>();
            foreach (var r in ranges)
            {
                if (merged.Count > 0 && r.Start <= merged[merged.Count - 1].End)
                {
                    var last = merged[merged.Count - 1];
                    merged[merged.Count - 1] = (last.Start, Math.Max(last.End, r.End));
                }
                else merged.Add(r);
            }
            return merged;
        }

        // whether [start, end) meets any of the sorted, joined ranges
        static bool Overlaps(List<(long Start, long End)> ranges, long start, long end)
        {
            int lo = 0, hi = ranges.Count - 1;
            while (lo <= hi)
            {
                int mid = (lo + hi) / 2;
                if (ranges[mid].End <= start) lo = mid + 1;
                else if (ranges[mid].Start >= end) hi = mid - 1;
                else return true;
            }
            return false;
        }

        // A meta box's children again without the given items: its keys renumbered, the ilst's items
        // with the new numbers, and a free box after them taking the space that's left.
        static byte[] RebuildMeta(MetaBox mb, List<Box> gone)
        {
            byte[] m = mb.Buf;
            long fileBase = mb.FileBase;
            long total = mb.Meta.End - mb.ChildrenAt;
            var keep = mb.Items.Where(it => !gone.Contains(it.Item)).ToList();
            var output = new MemoryStream();
            var newIndex = new Dictionary<int, int>();
            if (mb.Keys != null)
            {
                var usedKeys = new List<int>();
                foreach (var it in keep)
                    if (it.Index >= 1 && it.Index <= mb.KeyEntries.Count && !usedKeys.Contains(it.Index)) usedKeys.Add(it.Index);
                // the keys no item still uses go too (a location key with its item)
                var keptKeys = Enumerable.Range(1, mb.KeyEntries.Count).Where(k => usedKeys.Contains(k) || !mb.Items.Any(it => it.Index == k)).ToList();
                keptKeys.RemoveAll(k => mb.KeyEntries[k - 1].Name.IndexOf("location", StringComparison.OrdinalIgnoreCase) >= 0);
                for (int i = 0; i < keptKeys.Count; i++) newIndex[keptKeys[i]] = i + 1;
            }
            foreach (Box child in mb.Children)
            {
                if (child.Type == "free" || child.Type == "skip") continue;
                if (child == mb.Keys)
                {
                    var keys = new MemoryStream();
                    keys.Write(m, (int)(child.Payload - fileBase), 4);   // version and flags
                    WriteU32(keys, newIndex.Count);
                    foreach (var kv in newIndex.OrderBy(kv => kv.Value)) keys.Write(mb.KeyEntries[kv.Key - 1].Entry, 0, mb.KeyEntries[kv.Key - 1].Entry.Length);
                    WriteBox(output, "keys", keys.ToArray());
                }
                else if (child == mb.Ilst)
                {
                    var ilst = new MemoryStream();
                    foreach (var it in keep)
                    {
                        var item = new byte[it.Item.Size];
                        Array.Copy(m, it.Item.Start - fileBase, item, 0, item.Length);
                        if (mb.Keys != null)
                        {
                            if (!newIndex.TryGetValue(it.Index, out int to)) continue;
                            item[4] = (byte)(to >> 24);
                            item[5] = (byte)(to >> 16);
                            item[6] = (byte)(to >> 8);
                            item[7] = (byte)to;
                        }
                        ilst.Write(item, 0, item.Length);
                    }
                    WriteBox(output, "ilst", ilst.ToArray());
                }
                else output.Write(m, (int)(child.Start - fileBase), (int)child.Size);
            }
            long left = total - output.Length;
            if (left < 8) return null;   // (never: a whole item went, 8 bytes at least)
            WriteU32(output, left);
            output.Write(freeType, 0, 4);
            output.Write(new byte[left - 8], 0, (int)(left - 8));
            return output.ToArray();
        }

        static void WriteU32(Stream s, long v)
        {
            s.WriteByte((byte)(v >> 24));
            s.WriteByte((byte)(v >> 16));
            s.WriteByte((byte)(v >> 8));
            s.WriteByte((byte)v);
        }

        static void WriteBox(Stream s, string type, byte[] payload)
        {
            WriteU32(s, payload.Length + 8);
            foreach (char c in type) s.WriteByte((byte)c);
            s.Write(payload, 0, payload.Length);
        }

        // what a video's clean copy will be, from its file (it throws an IOException with words
        // for a file that can't be cleaned)
        public static VideoCleanPlan Plan(Stream s, ScrubOptions options, out ImageMetadata before)
        {
            Parsed p = Parse(s);
            before = p.Meta;
            return Plan(p, options);
        }

        // The clean copy written: the source's bytes with the patches over them, to the plan's
        // length, a megabyte at a time. `progress` hears how many bytes are done.
        public static void Write(Stream source, VideoCleanPlan plan, Stream output, Action<long> progress = null, CancellationToken cancel = default(CancellationToken))
        {
            var buffer = new byte[1 << 20];
            long pos = 0;
            int next = 0;
            source.Position = 0;
            while (pos < plan.Length)
            {
                cancel.ThrowIfCancellationRequested();
                int want = (int)Math.Min(buffer.Length, plan.Length - pos);
                int got = 0;
                while (got < want)
                {
                    int n = source.Read(buffer, got, want - got);
                    if (n == 0) throw new EndOfStreamException("the video got shorter while it was being cleaned");
                    got += n;
                }
                long blockEnd = pos + want;
                while (next < plan.Patches.Count && plan.Patches[next].At + plan.Patches[next].Length <= pos) next++;
                for (int k = next; k < plan.Patches.Count && plan.Patches[k].At < blockEnd; k++)
                {
                    VideoCleanPlan.Patch patch = plan.Patches[k];
                    long from = Math.Max(patch.At, pos), to = Math.Min(patch.At + patch.Length, blockEnd);
                    for (long i = from; i < to; i++) buffer[i - pos] = patch.Bytes != null ? patch.Bytes[i - patch.At] : (byte)0;
                }
                output.Write(buffer, 0, want);
                pos = blockEnd;
                progress?.Invoke(pos);
            }
        }

        // The Verified Clean check of a clean copy: it read again, and its every byte but the
        // patches' the source's (the picture and sound with them).
        public static SelfCheckReport Verify(ImageMetadata before, long originalSize, VideoCleanPlan plan, Stream source, Stream clean, ScrubOptions options, CancellationToken cancel = default(CancellationToken))
        {
            Parsed again = Parse(clean);
            ImageMetadata after = again.Meta;
            SelfCheckReport r = Scrubber.Verify(before, originalSize, after, clean.Length, options);
            // every kept byte inside the copy and the source's, every other byte the source's but the
            // patches, and (read apart from the parser) no place written anywhere outside the picture
            // and sound
            string place = null;
            bool untouched = plan.Samples.All(s => s.End <= plan.Length) && clean.Length == plan.Length && SameOutsidePatches(source, clean, plan, cancel, out place);
            r.SamplesUntouched = untouched;
            if (!untouched) r.Leftovers.Add("the picture or sound changed");
            if (place != null) r.Leftovers.Add("a place written in it (" + (place.Length > 40 ? place.Substring(0, 40) + "…" : place) + ")");
            if (again.Moovs != 1 || again.Fragments > 0) r.Leftovers.Add("its index changed");
            r.IsClean = r.Leftovers.Count == 0;
            r.TagsRemovedCount = plan.Removed;
            // a video's words for what was kept
            var summary = new List<string>();
            if (!r.IsClean) summary.Add("Still found: " + string.Join(", ", r.Leftovers) + ".");
            var kept = new List<string>();
            if (options.LocationOnly)
            {
                if (r.IsClean) summary.Add("Checked: 0 GPS coordinates, 0 trailing bytes found.");
                string camera = string.Join(" ", new[] { after.Make, after.Model }.Where(x => !string.IsNullOrEmpty(x)));
                if (camera.Length > 0) kept.Add("camera (" + camera + ")");
                if (!string.IsNullOrEmpty(after.DateTime)) kept.Add("date");
            }
            else if (r.IsClean) summary.Add("Checked: 0 GPS coordinates, 0 camera tags, 0 trailing bytes found.");
            if (untouched) kept.Add("the picture and sound, byte for byte");
            if (after.Video.Rotation != 0) kept.Add("its rotation (" + after.Video.Rotation + "°)");
            if (kept.Count > 0) summary.Add("Kept: " + string.Join(", ", kept) + ".");
            r.Summary = string.Join(" ", summary);
            return r;
        }

        // Every byte of the clean copy outside the patches is the source's. `place`: the first sign
        // of a place in the copy's bytes outside the picture and sound (the patched ones too), or null.
        static bool SameOutsidePatches(Stream source, Stream clean, VideoCleanPlan plan, CancellationToken cancel, out string place)
        {
            place = null;
            var a = new byte[1 << 20];
            var b = new byte[1 << 20];
            source.Position = 0;
            clean.Position = 0;
            var scan = new PlaceScan();
            List<(long Start, long End)> samples = plan.Samples;   // (sorted, joined)
            long pos = 0;
            int next = 0, si = 0;
            while (pos < plan.Length)
            {
                cancel.ThrowIfCancellationRequested();
                int want = (int)Math.Min(a.Length, plan.Length - pos);
                if (!Fill(source, a, want) || !Fill(clean, b, want)) return false;
                long blockEnd = pos + want;
                // the scan, before the patches are blanked for the comparing
                long at = pos;
                while (at < blockEnd && scan.Found == null)
                {
                    while (si < samples.Count && samples[si].End <= at) si++;
                    if (si < samples.Count && samples[si].Start <= at)
                    {
                        scan.Break();
                        at = Math.Min(blockEnd, samples[si].End);
                        continue;
                    }
                    long stop = si < samples.Count ? Math.Min(blockEnd, samples[si].Start) : blockEnd;
                    scan.Feed(b, (int)(at - pos), (int)(stop - at));
                    at = stop;
                }
                while (next < plan.Patches.Count && plan.Patches[next].At + plan.Patches[next].Length <= pos) next++;
                // the patched bytes aren't compared: blank them in both
                for (int k = next; k < plan.Patches.Count && plan.Patches[k].At < blockEnd; k++)
                {
                    VideoCleanPlan.Patch patch = plan.Patches[k];
                    long from = Math.Max(patch.At, pos), to = Math.Min(patch.At + patch.Length, blockEnd);
                    for (long i = from; i < to; i++) a[i - pos] = b[i - pos] = 0;
                }
                for (int i = 0; i < want; i++)
                    if (a[i] != b[i]) return false;
                pos = blockEnd;
            }
            place = scan.Found;
            return true;
        }

        static bool Fill(Stream s, byte[] into, int count)
        {
            int got = 0;
            while (got < count)
            {
                int n = s.Read(into, got, count - got);
                if (n == 0) return false;
                got += n;
            }
            return true;
        }

        // the size of a clean copy, before it's made
        public static long CleanLength(VideoCleanPlan plan) => plan.Length;
    }
}
