using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using B0r3dDeRes.Engine;

namespace B0r3dDeRes.Exif
{
    sealed class GpsCoordinates
    {
        public double Latitude, Longitude;
        public double? Altitude;
        public string LatRef, LonRef;
    }

    // a row of the All Tags list; its value is read and shown only when the list asks for it
    sealed class ParsedExifTag
    {
        public int TagId;
        public string TagHex, Name, Category;
        Func<string> show;
        string shown;

        public ParsedExifTag(int tagId, string tagHex, string name, string category, Func<string> show)
        {
            TagId = tagId;
            TagHex = tagHex;
            Name = name;
            Category = category;
            this.show = show;
        }

        public ParsedExifTag(int tagId, string tagHex, string name, string category, string shown)
        {
            TagId = tagId;
            TagHex = tagHex;
            Name = name;
            Category = category;
            this.shown = shown;
        }

        public string DisplayValue
        {
            get
            {
                if (shown == null)
                {
                    shown = show();
                    show = null;
                }
                return shown;
            }
        }
    }

    sealed class GifDetails
    {
        public string Version;
        public int Width, Height, Frames;
        public int? LoopCount;   // 0 forever, n n more times; null: no loop block (plays once)
        public int Comments;
    }

    sealed class EmbeddedThumbnail
    {
        public string MimeType;
        public byte[] Data;
    }

    // What the inspector found in a file: the website's ImageMetadata. (A video's, read by
    // VideoMeta, fills the same fields: Format is "mp4" or "mov" then.)
    sealed class ImageMetadata
    {
        public string Format;   // jpeg png webp heic avif gif unknown (and on the PC: mp4 mov)
        public string Make, Model, LensModel, DateTime, Software, Artist, Copyright, Description;
        public double? Orientation, Iso, FNumber, FocalLength;
        public string ExposureTime;
        public GpsCoordinates Gps;
        public bool HasIccProfile, HasThumbnail;
        public EmbeddedThumbnail Thumbnail;
        public long TrailerBytes;
        public int RawTagCount;
        public List<ParsedExifTag> AllTags = new List<ParsedExifTag>();
        public AiMarkerReport AiDetection;
        public string XmpText;
        public JsObject TextChunks;
        public List<string> ExifText;
        public GifDetails Gif;
        public VideoDetails Video;   // the PC's: a video's own details
    }

    // A plain JavaScript object used as a map of text to text: its keys enumerate as JavaScript's
    // do (whole-number keys first, in number order, then the rest in the order they came).
    sealed class JsObject
    {
        readonly List<string> order = new List<string>();
        readonly Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.Ordinal);

        public string Get(string key) => values.TryGetValue(key, out string v) ? v : null;

        public void Set(string key, string value)
        {
            if (!values.ContainsKey(key)) order.Add(key);
            values[key] = value;
        }

        static bool IsIndex(string key, out uint n)
        {
            n = 0;
            if (key.Length == 0 || key.Length > 10 || (key.Length > 1 && key[0] == '0')) return false;
            ulong v = 0;
            foreach (char c in key)
            {
                if (c < '0' || c > '9') return false;
                v = v * 10 + (ulong)(c - '0');
            }
            if (v > 4294967294) return false;
            n = (uint)v;
            return true;
        }

        public IEnumerable<KeyValuePair<string, string>> Entries
        {
            get
            {
                var indexes = new List<(uint N, string Key)>();
                var rest = new List<string>();
                foreach (string k in order)
                {
                    if (IsIndex(k, out uint n)) indexes.Add((n, k));
                    else rest.Add(k);
                }
                foreach (var i in indexes.OrderBy(i => i.N)) yield return new KeyValuePair<string, string>(i.Key, values[i.Key]);
                foreach (string k in rest) yield return new KeyValuePair<string, string>(k, values[k]);
            }
        }

        public int Count => order.Count;
    }

    // A DataView made past the end of the bytes it looks into: the browser throws a RangeError
    // there, and the PC says the same (only a damaged file gets here).
    sealed class ExifRangeException : Exception
    {
        public ExifRangeException(long offset) : base("Start offset " + offset + " is outside the bounds of the buffer") { }
    }

    // The bytes the inspector reads: the first Length of Array (a Uint8Array over Array's whole
    // buffer, as a subarray is in the browser: what lies past Length still counts for a DataView).
    struct Bytes
    {
        public readonly byte[] Array;
        public readonly int Length;

        public Bytes(byte[] array, int length)
        {
            Array = array;
            Length = length;
        }

        public Bytes(byte[] array) : this(array, array.Length) { }

        // data[i] in arithmetic: 0 where JavaScript reads undefined
        public int this[long i] => i >= 0 && i < Length ? Array[i] : 0;

        // data[i] compared: -1 where JavaScript reads undefined (equal to no byte)
        public int At(long i) => i >= 0 && i < Length ? Array[i] : -1;

        public byte[] Slice(long start, long end)
        {
            start = Math.Max(0, Math.Min(start, Length));
            end = Math.Max(start, Math.Min(end, Length));
            var b = new byte[end - start];
            System.Array.Copy(Array, start, b, 0, b.Length);
            return b;
        }
    }

    // The website's exif.ts: everything the inspector reads from a JPEG, PNG, WebP, HEIC, AVIF or
    // GIF, without any picture reader: camera, GPS (from EXIF, or XMP), dates, exposure, the
    // thumbnail, the bytes after the picture, every tag for the All Tags list, and its AI markers.
    static class ExifReader
    {
        // The All Tags list stops here (a damaged file can claim tens of thousands of tags; the
        // summary still counts them).
        public const int MaxListedTags = 2000;

        static void AddTag(ImageMetadata meta, ParsedExifTag tag)
        {
            if (meta.AllTags.Count < MaxListedTags) meta.AllTags.Add(tag);
        }

        // decimal GPS coordinates as degrees, minutes and seconds
        public static string GpsDms(GpsCoordinates coords)
        {
            string Coord(double val, string posRef, string negRef)
            {
                string reference = val >= 0 ? posRef : negRef;
                double abs = Math.Abs(val);
                double deg = Math.Floor(abs);
                double minFloat = (abs - deg) * 60;
                double min = Math.Floor(minFloat);
                string sec = JsText.Fixed((minFloat - min) * 60, 1);
                return JsText.Num(deg) + "°" + JsText.Num(min) + "'" + sec + "\" " + reference;
            }
            string lat = Coord(coords.Latitude, "N", "S"), lon = Coord(coords.Longitude, "E", "W");
            string alt = coords.Altitude.HasValue ? " (alt " + JsText.Num(JsMath.Round(coords.Altitude.Value)) + "m)" : "";
            return lat + ", " + lon + alt;
        }

        // decimal coordinates as plain numbers for copying (37.774900, -122.419400)
        public static string GpsDecimal(GpsCoordinates coords) => JsText.Fixed(coords.Latitude, 6) + ", " + JsText.Fixed(coords.Longitude, 6);

        static bool Truthy(double? x) => x.HasValue && x.Value != 0 && !double.IsNaN(x.Value);

        static bool Truthy(string s) => !string.IsNullOrEmpty(s);

        // a plain-English line about the file's metadata
        public static string Narrative(ImageMetadata meta)
        {
            if (meta.Video != null) return VideoMeta.Narrative(meta);
            // GIF, BMP, TIFF and the like: nothing was checked, so claim nothing
            if (meta.Format == "unknown") return "This kind of file can't be read here, so its details (camera, date, location) are unknown.";
            if (meta.Gif != null) return GifNarrative(meta, meta.Gif);
            var parts = new List<string>();
            if (Truthy(meta.Make) || Truthy(meta.Model)) parts.Add("Taken on a " + string.Join(" ", new[] { meta.Make, meta.Model }.Where(Truthy)));
            else parts.Add("Camera details not recorded");
            if (Truthy(meta.DateTime)) parts.Add("on " + meta.DateTime);
            parts.Add(meta.Gps != null ? "at coordinates " + GpsDecimal(meta.Gps) : "with no GPS location");
            var settings = new List<string>();
            if (Truthy(meta.FocalLength)) settings.Add(JsText.Num(meta.FocalLength.Value) + "mm");
            if (Truthy(meta.FNumber)) settings.Add("f/" + JsText.Num(meta.FNumber.Value));
            if (Truthy(meta.ExposureTime)) settings.Add(meta.ExposureTime);
            if (Truthy(meta.Iso)) settings.Add("ISO " + JsText.Num(meta.Iso.Value));
            if (settings.Count > 0) parts.Add("(" + string.Join(", ", settings) + ")");
            return string.Join(" ", parts) + ".";
        }

        // the kind of file, from its first bytes
        public static string DetectFormat(Bytes data)
        {
            if (data.Length < 12) return "unknown";
            if (StartsWith(data, 0, "GIF87a") || StartsWith(data, 0, "GIF89a")) return "gif";
            if (data[0] == 0xff && data[1] == 0xd8 && data[2] == 0xff) return "jpeg";
            if (data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4e && data[3] == 0x47 && data[4] == 0x0d && data[5] == 0x0a && data[6] == 0x1a && data[7] == 0x0a) return "png";
            if (data[0] == 0x52 && data[1] == 0x49 && data[2] == 0x46 && data[3] == 0x46 && data[8] == 0x57 && data[9] == 0x45 && data[10] == 0x42 && data[11] == 0x50) return "webp";
            if (data[4] == 0x66 && data[5] == 0x74 && data[6] == 0x79 && data[7] == 0x70)
            {
                // ftyp: the major brand, the minor version, then compatible brands to the end of the
                // box; AVIF files often name the generic HEIF brands too, so any AVIF brand is AVIF
                long boxEnd = Math.Min(data.Length, (uint)((data[0] << 24) | (data[1] << 16) | (data[2] << 8) | data[3]));
                var brands = new List<string> { JsText.Lower(JsText.Chars(data.Array, 8, 4)) };
                for (long p = 16; p + 4 <= boxEnd; p += 4) brands.Add(JsText.Lower(JsText.Chars(data.Array, (int)p, 4)));
                if (brands.Any(b => b == "avif" || b == "avis")) return "avif";
                if (new[] { "heic", "heix", "hevc", "heim", "heis", "mif1", "msf1" }.Contains(brands[0])) return "heic";
            }
            return "unknown";
        }

        public static string DetectFormat(byte[] data) => DetectFormat(new Bytes(data));

        // bytes a value of each TIFF type takes (1 BYTE … 12 DOUBLE, 13 IFD); 0 for any other
        public static int TypeSize(long type)
        {
            switch (type)
            {
                case 1: case 2: case 6: case 7: return 1;
                case 3: case 8: return 2;
                case 4: case 9: case 11: case 13: return 4;
                case 5: case 10: case 12: return 8;
                default: return 0;
            }
        }

        sealed class IfdEntry
        {
            public int Type;
            public long Count, ValOffset;
            public double Number;   // a number's value (Text is null)
            public Func<string> Text;   // an ASCII tag's text, read when asked for

            // String(entry.value)
            public string AsString() => Text != null ? Text() : JsText.Num(Number);

            // Number(entry.value)
            public double AsNumber() => Text != null ? JsText.ToNumber(Text()) : Number;
        }

        // an IFD's tags in the order they first came (a repeated tag keeps its place and takes the
        // later value, as a Map does)
        sealed class Ifd
        {
            readonly List<int> order = new List<int>();
            readonly Dictionary<int, IfdEntry> entries = new Dictionary<int, IfdEntry>();

            public void Set(int tag, IfdEntry e)
            {
                if (!entries.ContainsKey(tag)) order.Add(tag);
                entries[tag] = e;
            }

            public bool Has(int tag) => entries.ContainsKey(tag);
            public IfdEntry Get(int tag) => entries[tag];
            public int Size => order.Count;
            public IEnumerable<KeyValuePair<int, IfdEntry>> All => order.Select(t => new KeyValuePair<int, IfdEntry>(t, entries[t]));
        }

        // the website's TiffReader
        sealed class TiffReader
        {
            readonly Bytes data;
            readonly long baseOffset;
            readonly bool le;

            public TiffReader(Bytes data, long baseOffset)
            {
                this.data = data;
                this.baseOffset = baseOffset;
                le = data.At(baseOffset) == 0x49 && data.At(baseOffset + 1) == 0x49;
            }

            public long Uint16(double offset)
            {
                if (!Whole(offset)) return 0;
                long pos = baseOffset + (long)offset;
                if (pos + 2 > data.Length) return 0;
                return le ? data[pos] | (data[pos + 1] << 8) : (data[pos] << 8) | data[pos + 1];
            }

            public long Uint32(double offset)
            {
                if (!Whole(offset)) return 0;
                long pos = baseOffset + (long)offset;
                if (pos + 4 > data.Length) return 0;
                return le
                    ? (uint)(data[pos] | (data[pos + 1] << 8) | (data[pos + 2] << 16) | (data[pos + 3] << 24))
                    : (uint)((data[pos] << 24) | (data[pos + 1] << 16) | (data[pos + 2] << 8) | data[pos + 3]);
            }

            public double Rational(long offset)
            {
                long num = Uint32(offset), den = Uint32(offset + 4);
                if (den == 0) return 0;
                return num / (double)den;
            }

            public string String(long offset, long length)
            {
                long pos = baseOffset + offset;
                if (pos >= data.Length) return "";
                // text fields are short; a damaged count must not make this read megabytes
                long end = Math.Min(data.Length, pos + Math.Min(length, 1024));
                long stop = pos;
                while (stop < end && data[stop] != 0) stop++;
                int count = (int)Math.Max(0, stop - pos);
                // EXIF text is meant to be ASCII, but phones and editors write UTF-8; anything that
                // isn't valid UTF-8 is read byte by byte
                string text = JsText.Utf8Fatal(data.Array, (int)pos, count) ?? JsText.Latin1(data.Array, (int)pos, count);
                return JsText.Trim(text);
            }

            // Every value of a tag, for the All Tags list: text as a string, one number as a
            // number, several as numbers (rationals divided out, signed types signed), UNDEFINED as
            // bytes. Long lists stop at 64 numbers or maxBytes bytes.
            public TagValue Value(int type, long count, long valOffset, int maxBytes = 4096)
            {
                if (type == 2) return TagValue.Of(String(valOffset, count));
                int size = TypeSize(type);
                if (size == 0) size = 1;
                long pos = baseOffset + valOffset;
                long room = Math.Max(0, (long)Math.Floor((data.Length - pos) / (double)size));
                if (type == 1 || type == 6 || type == 7)
                {
                    long n = Math.Min(Math.Min(count, room), maxBytes);
                    byte[] bytes = data.Slice(pos, pos + n);
                    if (type == 7) return TagValue.OfBytes(bytes);
                    double[] nums = bytes.Select(b => (double)(type == 6 ? (sbyte)b : b)).ToArray();
                    return count == 1 ? (nums.Length > 0 ? TagValue.Of(nums[0]) : TagValue.None) : TagValue.Of(nums);
                }
                long k = Math.Min(Math.Min(count, room), 64);
                // new DataView(buffer, pos, n * size): past the buffer, the browser throws
                if (pos > data.Array.Length || pos < 0) throw new ExifRangeException(pos);
                var out_ = new List<double>();
                for (long i = 0; i < k; i++)
                {
                    long at = pos + i * size;
                    switch (type)
                    {
                        case 3: out_.Add(U16(at)); break;
                        case 8: out_.Add((short)U16(at)); break;
                        case 4: case 13: out_.Add(U32(at)); break;
                        case 9: out_.Add((int)U32(at)); break;
                        case 5:
                        {
                            uint den = U32(at + 4);
                            out_.Add(den == 0 ? 0 : U32(at) / (double)den);
                            break;
                        }
                        case 10:
                        {
                            int den = (int)U32(at + 4);
                            out_.Add(den == 0 ? 0 : (int)U32(at) / (double)den);
                            break;
                        }
                        case 11: out_.Add(BitConverter.ToSingle(BitConverter.GetBytes(U32(at)), 0)); break;
                        case 12:
                        {
                            ulong hi = U32(le ? at + 4 : at), lo = U32(le ? at : at + 4);
                            out_.Add(BitConverter.Int64BitsToDouble((long)((hi << 32) | lo)));
                            break;
                        }
                        default: return TagValue.Of(JsText.Num(count) + " values of an unknown type (" + type + ")");
                    }
                }
                return count == 1 ? (out_.Count > 0 ? TagValue.Of(out_[0]) : TagValue.None) : TagValue.Of(out_.ToArray());
            }

            ushort U16(long at) => (ushort)(le ? data.Array[at] | (data.Array[at + 1] << 8) : (data.Array[at] << 8) | data.Array[at + 1]);

            uint U32(long at) => le
                ? (uint)(data.Array[at] | (data.Array[at + 1] << 8) | (data.Array[at + 2] << 16) | (data.Array[at + 3] << 24))
                : (uint)((data.Array[at] << 24) | (data.Array[at + 1] << 16) | (data.Array[at + 2] << 8) | data.Array[at + 3]);

            public Ifd ReadIfd(double offsetValue)
            {
                var entries = new Ifd();
                // (a pointer that's no whole number reads nothing; a negative one nothing either)
                if (!Whole(offsetValue)) return entries;
                long offset = (long)offsetValue;
                if (offset + 2 > data.Length - baseOffset) return entries;
                long numTags = Uint16(offset);
                long cur = offset + 2;
                for (long i = 0; i < numTags; i++)
                {
                    if (cur + 12 > data.Length - baseOffset) break;
                    int tag = (int)Uint16(cur);
                    int type = (int)Uint16(cur + 2);
                    long count = Uint32(cur + 4);
                    int typeSize = TypeSize(type);
                    if (typeSize == 0) typeSize = 1;
                    long totalSize = count * typeSize;
                    var e = new IfdEntry { Type = type, Count = count };
                    if (totalSize <= 4)
                    {
                        e.ValOffset = cur + 8;
                        switch (type)
                        {
                            case 1: e.Number = data[baseOffset + cur + 8]; break;
                            case 2: break;
                            case 3: e.Number = Uint16(cur + 8); break;
                            case 4: e.Number = Uint32(cur + 8); break;
                            case 9: e.Number = (int)(uint)Uint32(cur + 8); break;
                            default: e.Number = Uint32(cur + 8); break;
                        }
                    }
                    else
                    {
                        e.ValOffset = Uint32(cur + 8);
                        switch (type)
                        {
                            case 2: break;
                            case 5:
                            case 10: e.Number = Rational(e.ValOffset); break;
                            default: e.Number = e.ValOffset; break;
                        }
                    }
                    if (type == 2)
                    {
                        // text is read only for the few tags shown, so a damaged block with
                        // thousands of long text tags costs nothing
                        long at = e.ValOffset;
                        e.Text = () => String(at, count);
                    }
                    entries.Set(tag, e);
                    cur += 12;
                }
                return entries;
            }

            public long NextIfdOffset(long offset)
            {
                long numTags = Uint16(offset);
                return Uint32(offset + 2 + numTags * 12);
            }
        }

        static bool Whole(double x) => x >= 0 && x == Math.Floor(x) && !double.IsInfinity(x);

        // the EXIF of a TIFF block
        public static void ParseTiffBlock(Bytes data, long tiffStart, ImageMetadata meta)
        {
            if (tiffStart + 8 > data.Length) return;
            bool isLe = data.At(tiffStart) == 0x49 && data.At(tiffStart + 1) == 0x49;
            bool isBe = data.At(tiffStart) == 0x4d && data.At(tiffStart + 1) == 0x4d;
            if (!isLe && !isBe) return;
            var reader = new TiffReader(data, tiffStart);
            if (reader.Uint16(2) != 42) return;
            long ifd0Offset = reader.Uint32(4);
            if (ifd0Offset == 0) return;

            void Record(Ifd ifd, string category)
            {
                foreach (var kv in ifd.All)
                {
                    if (meta.AllTags.Count >= MaxListedTags) return;
                    int tagId = kv.Key;
                    IfdEntry entry = kv.Value;
                    AddTag(meta, new ParsedExifTag(tagId, "0x" + JsText.Hex(tagId, 4), ExifTags.Name(category, tagId), category,
                        () => ExifTags.Format(category, tagId, reader.Value(entry.Type, entry.Count, entry.ValOffset), entry.Count)));
                }
            }
            // text an AI tool may leave its prompt in (Stable Diffusion writes UserComment); a
            // prompt with its settings can run to many KB, so up to 64 KB
            void KeepText(Ifd ifd, int tag, Func<TagValue, string> decode)
            {
                if (!ifd.Has(tag)) return;
                IfdEntry entry = ifd.Get(tag);
                string text = decode(reader.Value(entry.Type, entry.Count, entry.ValOffset, 65536));
                if (!string.IsNullOrEmpty(text)) (meta.ExifText ?? (meta.ExifText = new List<string>())).Add(text);
            }
            string AsText(TagValue v) => v.Kind == TagValue.Kinds.Text ? v.Text : "";
            string XpText(TagValue v)
            {
                if (v.Kind != TagValue.Kinds.Numbers) return "";
                byte[] b = v.Numbers.Select(ExifTags.ToUint8).ToArray();
                return JsText.NoTrailingZeros(JsText.Utf16(b, 0, b.Length, true));
            }

            Ifd ifd0 = reader.ReadIfd(ifd0Offset);
            meta.RawTagCount += ifd0.Size;
            Record(ifd0, ExifTags.Ifd0);
            KeepText(ifd0, 0x010e, AsText);
            KeepText(ifd0, 0x0131, AsText);
            KeepText(ifd0, 0x9c9c, XpText);

            if (ifd0.Has(0x010f)) meta.Make = ifd0.Get(0x010f).AsString();
            if (ifd0.Has(0x0110)) meta.Model = ifd0.Get(0x0110).AsString();
            if (ifd0.Has(0x0112)) meta.Orientation = ifd0.Get(0x0112).AsNumber();
            if (ifd0.Has(0x0132)) meta.DateTime = ifd0.Get(0x0132).AsString();
            if (ifd0.Has(0x0131)) meta.Software = ifd0.Get(0x0131).AsString();
            if (ifd0.Has(0x013b)) meta.Artist = ifd0.Get(0x013b).AsString();
            if (ifd0.Has(0x8298)) meta.Copyright = ifd0.Get(0x8298).AsString();
            if (ifd0.Has(0x010e)) meta.Description = ifd0.Get(0x010e).AsString();

            if (ifd0.Has(0x8769))
            {
                Ifd exif = reader.ReadIfd(ifd0.Get(0x8769).AsNumber());
                meta.RawTagCount += exif.Size;
                Record(exif, ExifTags.Exif);
                KeepText(exif, 0x9286, ExifTags.DecodeCharsetText);
                if (exif.Has(0xa005))
                {
                    double interopOffset = exif.Get(0xa005).AsNumber();
                    if (interopOffset > 0 && interopOffset < data.Length - tiffStart)
                    {
                        Ifd interop = reader.ReadIfd(interopOffset);
                        meta.RawTagCount += interop.Size;
                        Record(interop, ExifTags.Interop);
                    }
                }
                if (exif.Has(0x9003) && !Truthy(meta.DateTime)) meta.DateTime = exif.Get(0x9003).AsString();
                if (exif.Has(0xa434)) meta.LensModel = exif.Get(0xa434).AsString();
                if (exif.Has(0x8827)) meta.Iso = exif.Get(0x8827).AsNumber();
                if (exif.Has(0x829d)) meta.FNumber = JsText.FixedNumber(exif.Get(0x829d).AsNumber(), 1);
                if (exif.Has(0x920a)) meta.FocalLength = JsMath.Round(exif.Get(0x920a).AsNumber());
                if (exif.Has(0x829a))
                {
                    double exp = exif.Get(0x829a).AsNumber();
                    if (exp > 0 && exp < 1) meta.ExposureTime = "1/" + JsText.Num(JsMath.Round(1 / exp)) + "s";
                    else if (exp >= 1) meta.ExposureTime = JsText.Fixed(exp, 1) + "s";
                }
            }

            if (ifd0.Has(0x8825))
            {
                Ifd gps = reader.ReadIfd(ifd0.Get(0x8825).AsNumber());
                meta.RawTagCount += gps.Size;
                Record(gps, ExifTags.Gps);
                string latRef = gps.Has(0x0001) ? JsText.Trim(JsText.Upper(gps.Get(0x0001).AsString())) : "N";
                string lonRef = gps.Has(0x0003) ? JsText.Trim(JsText.Upper(gps.Get(0x0003).AsString())) : "W";
                double Degrees(int tag)
                {
                    long off = gps.Get(tag).ValOffset;
                    return reader.Rational(off) + reader.Rational(off + 8) / 60 + reader.Rational(off + 16) / 3600;
                }
                double latVal = 0, lonVal = 0;
                if (gps.Has(0x0002))
                {
                    latVal = Degrees(0x0002);
                    if (latRef == "S") latVal = -latVal;
                }
                if (gps.Has(0x0004))
                {
                    lonVal = Degrees(0x0004);
                    if (lonRef == "W") lonVal = -lonVal;
                }
                double? altitude = null;
                if (gps.Has(0x0006))
                {
                    double alt = gps.Get(0x0006).AsNumber();
                    double altRef = gps.Has(0x0005) ? gps.Get(0x0005).AsNumber() : 0;
                    if (altRef == 1) alt = -alt;
                    altitude = alt;
                }
                if (gps.Has(0x0002) && gps.Has(0x0004)) meta.Gps = new GpsCoordinates { Latitude = latVal, Longitude = lonVal, Altitude = altitude, LatRef = latRef, LonRef = lonRef };
            }

            // IFD1: the thumbnail (its offset follows IFD0)
            long ifd1Offset = reader.NextIfdOffset(ifd0Offset);
            if (ifd1Offset > 0 && ifd1Offset < data.Length - tiffStart)
            {
                Ifd ifd1 = reader.ReadIfd(ifd1Offset);
                meta.RawTagCount += ifd1.Size;
                Record(ifd1, ExifTags.Ifd1);
                if (ifd1.Has(0x0201) && ifd1.Has(0x0202))
                {
                    double thumbOffset = ifd1.Get(0x0201).AsNumber(), thumbLength = ifd1.Get(0x0202).AsNumber();
                    double absThumbStart = tiffStart + thumbOffset;
                    if (thumbLength > 0 && absThumbStart + thumbLength <= data.Length)
                    {
                        meta.HasThumbnail = true;
                        // (slice takes a fraction's whole part)
                        meta.Thumbnail = new EmbeddedThumbnail { MimeType = "image/jpeg", Data = data.Slice((long)Math.Truncate(absThumbStart), (long)Math.Truncate(absThumbStart + thumbLength)) };
                    }
                }
            }
        }

        // Everything the inspector finds in a file's bytes.
        public static ImageMetadata Inspect(byte[] data) => Inspect(new Bytes(data));

        public static ImageMetadata Inspect(Bytes data)
        {
            string format = DetectFormat(data);
            var meta = new ImageMetadata { Format = format };
            if (format == "jpeg") InspectJpeg(data, meta);
            else if (format == "png") InspectPng(data, meta);
            else if (format == "webp") InspectWebp(data, meta);
            else if (format == "heic" || format == "avif") InspectHeic(data, meta);   // both HEIF files, Exif kept the same way
            else if (format == "gif") InspectGif(data, meta);

            // a location kept only in XMP (no EXIF GPS) is still a location
            if (meta.Gps == null && !string.IsNullOrEmpty(meta.XmpText)) meta.Gps = XmpGps(meta.XmpText);

            var textBlocks = new List<string>();
            if (!string.IsNullOrEmpty(meta.XmpText)) textBlocks.Add(meta.XmpText);
            if (meta.TextChunks != null)
                foreach (var kv in meta.TextChunks.Entries) textBlocks.Add(kv.Key + ": " + kv.Value);
            if (meta.ExifText != null) textBlocks.AddRange(meta.ExifText);
            ContentCredentials credentials = AiMarkers.FindContentCredentials(data);
            meta.AiDetection = AiMarkers.Detect(textBlocks, meta.Software, credentials);
            if (credentials != null)
                AddTag(meta, new ParsedExifTag(0, "C2PA", "Content Credentials", "C2PA",
                    JsText.Grouped(credentials.Bytes) + " bytes in the " + credentials.Where + (Truthy(credentials.Generator) ? ", signed by " + credentials.Generator : "")));
            return meta;
        }

        // a text row in the All Tags list (XMP, PNG text, JUMBF data)
        static void TextRow(ImageMetadata meta, string tagHex, string name, string category, string shown) =>
            AddTag(meta, new ParsedExifTag(0, tagHex, name, category, shown));

        static string Short(string text) => text.Length > 200 ? text.Substring(0, 200) + "…" : text;

        // how JPEG APP1 segments (and PNG iTXt keywords) say they hold XMP
        public const string XmpSig = "http://ns.adobe.com/xap/1.0/\0";
        public const string XmpExtensionSig = "http://ns.adobe.com/xmp/extension/\0";
        public const string PngXmpKeyword = "XML:com.adobe.xmp";

        // whether the bytes at `at` spell out `text`
        public static bool StartsWith(Bytes data, long at, string text)
        {
            if (at + text.Length > data.Length) return false;
            for (int i = 0; i < text.Length; i++)
                if (data[at + i] != text[i]) return false;
            return true;
        }

        public static bool StartsWith(byte[] data, long at, string text) => StartsWith(new Bytes(data), at, text);

        static int IndexOf(byte[] b, byte value, int from)
        {
            if (from >= b.Length) return -1;
            return Array.IndexOf(b, value, Math.Max(0, from));
        }

        // A PNG text chunk: tEXt is Latin-1, iTXt UTF-8 (XMP and most non-English AI prompts);
        // compressed text is listed but not read.
        static void ReadPngText(string type, byte[] chunk, ImageMetadata meta)
        {
            int keyEnd = IndexOf(chunk, 0, 0);
            if (keyEnd <= 0) return;
            string keyword = JsText.Latin1(chunk, 0, keyEnd);
            string text = null;
            if (type == "tEXt") text = JsText.Latin1(chunk, keyEnd + 1, chunk.Length - keyEnd - 1);
            else if (type == "iTXt")
            {
                bool compressed = keyEnd + 1 < chunk.Length && chunk[keyEnd + 1] == 1;
                int langEnd = IndexOf(chunk, 0, keyEnd + 3);
                int transEnd = langEnd < 0 ? -1 : IndexOf(chunk, 0, langEnd + 1);
                if (!compressed && transEnd >= 0) text = JsText.Utf8(chunk, transEnd + 1, chunk.Length - transEnd - 1);
            }
            if (text == null)
            {
                TextRow(meta, type, "PNG Text (" + keyword + ")", "Text", JsText.Grouped(chunk.Length) + " bytes of compressed text (not read)");
                return;
            }
            if (type == "iTXt" && keyword == PngXmpKeyword)
            {
                meta.XmpText = (meta.XmpText ?? "") + text;
                TextRow(meta, "XMP", "XMP Metadata Packet", "XMP", JsText.Grouped(text.Length) + " characters");
                return;
            }
            JsObject chunks = meta.TextChunks ?? (meta.TextChunks = new JsObject());
            string had = chunks.Get(keyword);
            chunks.Set(keyword, !string.IsNullOrEmpty(had) ? had + "\n" + text : text);
            TextRow(meta, type, "PNG Text (" + keyword + ")", "Text", Short(text));
        }

        static void InspectJpeg(Bytes data, ImageMetadata meta)
        {
            long pos = 2;
            long eoiPos = -1;
            while (pos < data.Length)
            {
                if (data.At(pos) != 0xff)
                {
                    pos++;
                    continue;
                }
                int marker = data.At(pos + 1);
                if (marker == 0xd9)
                {
                    eoiPos = pos + 2;
                    break;
                }
                // fill bytes may come before any marker
                if (marker == 0xff)
                {
                    pos++;
                    continue;
                }
                if (marker == 0x00 || marker == 0xd8 || (marker >= 0xd0 && marker <= 0xd7))
                {
                    pos += 2;
                    continue;
                }
                if (marker == 0xda)
                {
                    // the start of a scan: its entropy-coded data runs to the next marker
                    long len = (data[pos + 2] << 8) | data[pos + 3];
                    pos += 2 + len;
                    while (pos < data.Length - 1)
                    {
                        if (data.At(pos) == 0xff && data.At(pos + 1) != 0x00 && !(data.At(pos + 1) >= 0xd0 && data.At(pos + 1) <= 0xd7)) break;
                        pos++;
                    }
                    continue;
                }
                long segLen = (data[pos + 2] << 8) | data[pos + 3];
                long segStart = pos + 4, segEnd = pos + 2 + segLen;
                // a damaged length: stop here rather than read past the file
                if (segLen < 2 || segEnd > data.Length) break;
                if (marker == 0xe1)
                {
                    if (data[segStart] == 0x45 && data[segStart + 1] == 0x78 && data[segStart + 2] == 0x69 && data[segStart + 3] == 0x66 && data.At(segStart + 4) == 0x00 && data.At(segStart + 5) == 0x00)
                        ParseTiffBlock(data, segStart + 6, meta);
                    else if (StartsWith(data, segStart, XmpSig))
                    {
                        string xmp = Utf8Between(data, segStart + XmpSig.Length, segEnd);
                        meta.XmpText = (meta.XmpText ?? "") + xmp;
                        meta.RawTagCount++;
                        TextRow(meta, "XMP", "XMP Metadata Packet", "XMP", JsText.Grouped(xmp.Length) + " characters");
                    }
                    else if (StartsWith(data, segStart, XmpExtensionSig))
                    {
                        // extended XMP: the rest of a packet too big for one segment
                        string xmp = Utf8Between(data, segStart + XmpExtensionSig.Length + 40, segEnd);
                        meta.XmpText = (meta.XmpText ?? "") + xmp;
                        meta.RawTagCount++;
                        TextRow(meta, "XMP", "Extended XMP Packet", "XMP", JsText.Grouped(xmp.Length) + " characters");
                    }
                }
                else if (marker == 0xeb)
                {
                    // APP11: JUMBF boxes (Content Credentials live here, so do JPEG XT extensions)
                    meta.RawTagCount++;
                    TextRow(meta, "APP11", "APP11 segment (JUMBF data)", "C2PA", JsText.Grouped(segLen) + " bytes");
                }
                else if (marker == 0xe2)
                {
                    // APP2: an ICC profile or MPF
                    if (data[segStart] == 0x49 && data[segStart + 1] == 0x43 && data[segStart + 2] == 0x43 && data[segStart + 3] == 0x5f) meta.HasIccProfile = true;
                }
                pos = segEnd;
            }
            if (eoiPos > 0 && eoiPos < data.Length) meta.TrailerBytes = data.Length - eoiPos;
        }

        // UTF-8 of data.subarray(start, end)
        static string Utf8Between(Bytes data, long start, long end)
        {
            start = Math.Max(0, Math.Min(start, data.Length));
            end = Math.Max(start, Math.Min(end, data.Length));
            return JsText.Utf8(data.Array, (int)start, (int)(end - start));
        }

        static long Uint32Be(Bytes data, long p) => (uint)((data[p] << 24) | (data[p + 1] << 16) | (data[p + 2] << 8) | data[p + 3]);

        static long Uint32Le(Bytes data, long p) => (uint)(data[p] | (data[p + 1] << 8) | (data[p + 2] << 16) | (data[p + 3] << 24));

        static void InspectPng(Bytes data, ImageMetadata meta)
        {
            long pos = 8, iendEnd = -1;
            while (pos + 8 <= data.Length)
            {
                // unsigned: a length with the top bit set must not send the walk backwards
                long len = Uint32Be(data, pos);
                string type = JsText.Chars(data.Array, (int)pos + 4, 4);
                long chunkData = pos + 8, nextChunk = chunkData + len + 4;
                if (nextChunk > data.Length)
                {
                    // a damaged chunk: whatever follows can't be read, so it counts as data after
                    // the picture (the scrubber drops it)
                    meta.TrailerBytes = data.Length - pos;
                    return;
                }
                if (type == "eXIf") ParseTiffBlock(data, chunkData, meta);
                else if (type == "iCCP") meta.HasIccProfile = true;
                else if (type == "tEXt" || type == "zTXt" || type == "iTXt")
                {
                    meta.RawTagCount++;
                    ReadPngText(type, data.Slice(chunkData, chunkData + len), meta);
                }
                else if (type == "caBX")
                {
                    meta.RawTagCount++;
                    TextRow(meta, "caBX", "caBX chunk (JUMBF data)", "C2PA", JsText.Grouped(len) + " bytes");
                }
                else if (type == "IEND")
                {
                    iendEnd = nextChunk;
                    break;
                }
                pos = nextChunk;
            }
            if (iendEnd > 0 && iendEnd < data.Length) meta.TrailerBytes = data.Length - iendEnd;
        }

        // where a WebP's RIFF data ends (its size field says), never past the file
        public static long WebpRiffEnd(Bytes data) => Math.Min(data.Length, 8 + Uint32Le(data, 4));

        static void InspectWebp(Bytes data, ImageMetadata meta)
        {
            long pos = 12;
            long end = WebpRiffEnd(data);
            // anything after the RIFF end is not part of the picture
            meta.TrailerBytes = data.Length - end;
            while (pos + 8 <= end)
            {
                string fourCC = JsText.Chars(data.Array, (int)pos, 4);
                long size = Uint32Le(data, pos + 4);
                long chunkData = pos + 8;
                long nextChunk = chunkData + size + size % 2;
                if (fourCC == "EXIF")
                {
                    long tiffOffset = chunkData;
                    // some WebP files put "Exif\0\0" first
                    if (data[chunkData] == 0x45 && data[chunkData + 1] == 0x78 && data[chunkData + 2] == 0x69 && data[chunkData + 3] == 0x66) tiffOffset += 6;
                    ParseTiffBlock(data, tiffOffset, meta);
                }
                else if (fourCC == "ICCP") meta.HasIccProfile = true;
                else if (fourCC == "XMP ")
                {
                    meta.RawTagCount++;
                    string xmp = Utf8Between(data, pos + 8, pos + 8 + size);
                    meta.XmpText = xmp;
                    TextRow(meta, "XMP", "XMP Metadata Packet", "XMP", JsText.Grouped(xmp.Length) + " characters");
                }
                else if (fourCC == "C2PA")
                {
                    meta.RawTagCount++;
                    TextRow(meta, "C2PA", "C2PA chunk (JUMBF data)", "C2PA", JsText.Grouped(size) + " bytes");
                }
                pos = nextChunk;
            }
        }

        static void InspectHeic(Bytes data, ImageMetadata meta)
        {
            // the first TIFF header inside the boxes that reads as EXIF
            for (long i = 0; i < data.Length - 8; i++)
            {
                if ((data[i] == 0x49 && data[i + 1] == 0x49 && data[i + 2] == 0x2a && data[i + 3] == 0x00) ||
                    (data[i] == 0x4d && data[i + 1] == 0x4d && data[i + 2] == 0x00 && data[i + 3] == 0x2a))
                {
                    ParseTiffBlock(data, i, meta);
                    if (meta.RawTagCount > 0) break;
                }
            }
        }

        // "A GIF with 58 frames that loops forever. GIFs don't record a camera or date; …"
        static string GifNarrative(ImageMetadata meta, GifDetails g)
        {
            string frames = g.Frames == 1 ? "one frame" : g.Frames + " frames";
            string loop = g.Frames < 2 ? "" : g.LoopCount == 0 ? " that loops forever" : g.LoopCount == null ? " that plays once" : " that plays " + (g.LoopCount.Value + 1) + " times";
            string where = meta.Gps != null ? "its XMP gives a location: " + GpsDecimal(meta.Gps) : "no location found";
            string comments = g.Comments > 0 ? ", and " + (g.Comments == 1 ? "a comment" : g.Comments + " comments") : "";
            return "A GIF with " + frames + loop + ". GIFs don't record a camera or date; " + where + comments + ".";
        }

        // the application blocks that hold a GIF's loop count, and the one that holds XMP
        public static readonly string[] GifLoopApps = { "NETSCAPE2.0", "ANIMEXTS1.0" };
        public const string GifXmpApp = "XMP DataXMP";

        // where a GIF's sub-blocks starting at `at` end (just past the 0 that ends them); -1 when the file ends first
        public static long GifSubBlocksEnd(Bytes data, long at)
        {
            long p = at;
            while (p < data.Length)
            {
                int len = data[p++];
                if (len == 0) return p;
                p += len;
            }
            return -1;
        }

        // the data of a run of sub-blocks, joined
        static byte[] GifSubBlockData(Bytes data, long at, long end)
        {
            var parts = new List<byte>();
            for (long p = at; p < end - 1;)
            {
                int len = data[p++];
                long stop = Math.Min(end - 1, p + len);
                for (long q = p; q < stop && q < data.Length; q++) parts.Add(data.Array[q]);
                p += len;
            }
            return parts.ToArray();
        }

        // The XMP packet in a GIF's "XMP DataXMP" block: raw text straight after the name, then a
        // 257-byte "magic trailer" (1, 255, 254 … 0). `at` is the block's 0x21, `end` its end.
        public static string GifXmpText(Bytes data, long at, long end)
        {
            long textStart = at + 14, trailer = end - 258;
            if (trailer >= textStart && data.At(trailer) == 1 && data.At(trailer + 1) == 0xff && data.At(end - 2) == 0 && data.At(end - 1) == 0)
                return Utf8Between(data, textStart, trailer);
            // not written the usual way: as ordinary sub-blocks
            byte[] joined = GifSubBlockData(data, at + 2, end);
            return joined.Length > 11 ? JsText.Utf8(joined, 11, joined.Length - 11) : "";
        }

        // a GIF comment's text: meant to be ASCII, often UTF-8, sometimes Latin-1
        static string GifText(byte[] bytes, int start = 0) =>
            JsText.Utf8Fatal(bytes, start, Math.Max(0, bytes.Length - start)) ?? JsText.Latin1(bytes, start, Math.Max(0, bytes.Length - start));

        static readonly Regex xmpLocation = new Regex("GPSLatitude|GPSLongitude|photoshop:City|photoshop:State|photoshop:Country|Iptc4xmpCore:Location|Iptc4xmpExt:Location", RegexOptions.CultureInvariant);

        // whether an XMP packet says where the picture was taken (GPS, or a place name)
        public static bool XmpHasLocation(string xmp) => xmpLocation.IsMatch(xmp);

        // JavaScript's \s, spelled out (.NET's is a different set). (And JavaScript's $ is .NET's
        // \z: .NET's $ also matches before a last line break.)
        public static readonly string Space = JsText.SpaceClass;

        static readonly Regex xmpCoordinate = new Regex("^" + Space + @"*(-?[0-9]+(?:\.[0-9]+)?)(?:,([0-9]+(?:\.[0-9]+)?))?(?:,([0-9]+(?:\.[0-9]+)?))?" + Space + @"*([NSEWnsew])?" + Space + @"*\z", RegexOptions.CultureInvariant);

        // one XMP GPS coordinate ("41,52.8N", "41,52,48N", or plain decimal degrees)
        static double? XmpCoordinate(string text, string neg)
        {
            Match m = xmpCoordinate.Match(text);
            if (!m.Success) return null;
            double first = JsText.ToNumber(m.Groups[1].Value);
            double value = Math.Abs(first) + (m.Groups[2].Success && m.Groups[2].Value.Length > 0 ? JsText.ToNumber(m.Groups[2].Value) / 60 : 0) +
                (m.Groups[3].Success && m.Groups[3].Value.Length > 0 ? JsText.ToNumber(m.Groups[3].Value) / 3600 : 0);
            bool negative = JsText.Upper(m.Groups[4].Success ? m.Groups[4].Value : "") == neg || first < 0;
            if (double.IsNaN(value) || double.IsInfinity(value)) return null;
            return negative ? -value : value;
        }

        // the GPS position in an XMP packet (exif:GPSLatitude and exif:GPSLongitude), if it has one
        public static GpsCoordinates XmpGps(string xmp)
        {
            string Field(string name)
            {
                Match m = Regex.Match(xmp, "exif:" + name + "(?:" + Space + "*=" + Space + "*\"([^\"]*)\"|>([^<]*)<)", RegexOptions.CultureInvariant);
                if (!m.Success) return null;
                return m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value;
            }
            string latText = Field("GPSLatitude"), lonText = Field("GPSLongitude");
            if (string.IsNullOrEmpty(latText) || string.IsNullOrEmpty(lonText)) return null;
            double? latitude = XmpCoordinate(latText, "S"), longitude = XmpCoordinate(lonText, "W");
            if (latitude == null || longitude == null) return null;
            var coords = new GpsCoordinates { Latitude = latitude.Value, Longitude = longitude.Value, LatRef = latitude < 0 ? "S" : "N", LonRef = longitude < 0 ? "W" : "E" };
            string alt = Field("GPSAltitude");
            if (!string.IsNullOrEmpty(alt))
            {
                string[] parts = alt.Split('/');
                double a = JsText.ToNumber(parts[0]);
                double b = parts.Length > 1 ? JsText.ToNumber(parts[1]) : double.NaN;
                double value = b != 0 && !double.IsNaN(b) ? a / b : a;
                if (!double.IsNaN(value) && !double.IsInfinity(value)) coords.Altitude = Field("GPSAltitudeRef") == "1" ? -value : value;
            }
            return coords;
        }

        // A GIF: no EXIF, but comments, plain text, XMP and other application blocks (a colour
        // profile, Photoshop or IPTC data) can ride along with the frames. The loop block is part
        // of the animation, not metadata.
        static void InspectGif(Bytes data, ImageMetadata meta)
        {
            long n = data.Length;
            string version = data[4] == 0x37 ? "87a" : "89a";
            int width = data[6] | (data[7] << 8), height = data[8] | (data[9] << 8);
            long p = 13;
            if ((data[10] & 0x80) != 0) p += 3 << ((data[10] & 7) + 1);
            var g = new GifDetails { Version = version, Width = width, Height = height };
            var texts = new List<string>();
            while (p < n)
            {
                int block = data[p];
                if (block == 0x3b)
                {
                    p++;
                    break;
                }
                if (block == 0x2c)
                {
                    // an image: descriptor, its own colour table, LZW size, data
                    if (p + 10 > n) break;
                    int flags = data[p + 9];
                    long q = p + 10;
                    if ((flags & 0x80) != 0) q += 3 << ((flags & 7) + 1);
                    long imageEnd = GifSubBlocksEnd(data, q + 1);
                    if (imageEnd < 0) break;
                    g.Frames++;
                    p = imageEnd;
                    continue;
                }
                if (block != 0x21) break;
                int label = data[p + 1];
                long end = GifSubBlocksEnd(data, p + 2);
                if (end < 0) break;
                if (label == 0xf9)
                {
                    // graphic control (delay, disposal, transparency): part of the animation
                }
                else if (label == 0xfe)
                {
                    string text = GifText(GifSubBlockData(data, p + 2, end));
                    g.Comments++;
                    texts.Add(text);
                    meta.RawTagCount++;
                    TextRow(meta, "Comment", "GIF Comment", "Text", Short(text));
                }
                else if (label == 0x01)
                {
                    // plain text: a 12-byte grid header, then the text to draw
                    string text = GifText(GifSubBlockData(data, p + 2, end), 12);
                    texts.Add(text);
                    meta.RawTagCount++;
                    TextRow(meta, "PlainText", "GIF Plain Text", "Text", Short(text));
                }
                else if (label == 0xff && data.At(p + 2) == 11)
                {
                    string name = JsText.Chars(data.Array, (int)(p + 3), (int)Math.Max(0, Math.Min(n, p + 14) - (p + 3)));
                    if (GifLoopApps.Contains(name))
                    {
                        if (g.LoopCount == null && data.At(p + 14) >= 3 && data.At(p + 15) == 1) g.LoopCount = data[p + 16] | (data[p + 17] << 8);
                    }
                    else if (name == GifXmpApp)
                    {
                        string xmp = GifXmpText(data, p, end);
                        meta.XmpText = (meta.XmpText ?? "") + xmp;
                        meta.RawTagCount++;
                        TextRow(meta, "XMP", "XMP Metadata Packet", "XMP", JsText.Grouped(xmp.Length) + " characters");
                    }
                    else
                    {
                        long bytes = end - p;
                        // a colour profile is part of how the picture looks (kept by the scrubber), so it isn't a field
                        bool icc = name.StartsWith("ICCRGBG1", StringComparison.Ordinal);
                        if (icc) meta.HasIccProfile = true;
                        else meta.RawTagCount++;
                        string what = icc ? "colour profile" : name.StartsWith("MGK8BIM", StringComparison.Ordinal) ? "Photoshop data" : name.StartsWith("MGKIPTC", StringComparison.Ordinal) ? "IPTC data" : "data";
                        string shownName = new string(name.Select(c => c >= 0x20 && c <= 0x7e ? c : '?').ToArray());
                        TextRow(meta, "App", "Application block (" + shownName + ")", "GIF", what + ", " + JsText.Grouped(bytes) + " bytes");
                    }
                }
                else
                {
                    meta.RawTagCount++;
                    TextRow(meta, "0x" + JsText.Hex(label, 2), "Unknown GIF extension", "GIF", JsText.Grouped(end - p) + " bytes");
                }
                p = end;
            }
            // after the end marker, or from damage on: not part of the picture
            meta.TrailerBytes = n - Math.Min(p, n);
            meta.Gif = g;
            if (texts.Count > 0)
            {
                meta.TextChunks = new JsObject();
                meta.TextChunks.Set("Comment", string.Join("\n", texts));
            }
            string loopText = g.LoopCount == null ? "No loop block (plays once)" : g.LoopCount == 0 ? "Forever" : g.LoopCount + " more time" + (g.LoopCount == 1 ? "" : "s");
            meta.AllTags.InsertRange(0, new[]
            {
                new ParsedExifTag(0, "Version", "GIF Version", "GIF", version),
                new ParsedExifTag(0, "Screen", "Size", "GIF", width + "×" + height),
                new ParsedExifTag(0, "Frames", "Frames", "GIF", g.Frames.ToString()),
                new ParsedExifTag(0, "Loop", "Loop Count", "GIF", loopText),
            });
        }
    }
}
