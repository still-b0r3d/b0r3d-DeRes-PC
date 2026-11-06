using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using B0r3dDeRes.Engine;

namespace B0r3dDeRes.Exif
{
    // What to change: null for a field means keep what the photo has; Remove() (an empty text, or
    // Removed for a number) means take the tag out; a value means write it.
    sealed class ExifEdits
    {
        public Edit<string> Make, Model, LensModel, Artist, Copyright, Software, Description, LensMake;
        public Edit<string> DateTime;          // "YYYY:MM:DD HH:MM:SS" (other spellings go through ParseDate)
        public Edit<double> Orientation;       // 1-8
        public Edit<double> Iso, FNumber, FocalLength, FocalLength35;
        public Edit<(double Num, double Den)> ExposureTime;
        public Edit<string> ExposureText;      // a shutter speed as typed (instead of ExposureTime)
        public bool ForgetCamera;              // drop the maker's data, serial numbers, owner name and lens range
        public Edit<(double Latitude, double Longitude, double? Altitude)> Gps;
        public Edit<byte[]> Thumbnail;

        // how many fields are changed (the website's Object.keys(edits).length)
        public int Count
        {
            get
            {
                int n = 0;
                foreach (object e in new object[] { Make, Model, LensModel, Artist, Copyright, Software, Description, LensMake, DateTime, Orientation, Iso, FNumber, FocalLength, FocalLength35, Gps, Thumbnail })
                    if (e != null) n++;
                if (ExposureTime != null || ExposureText != null) n++;
                if (ForgetCamera) n++;
                return n;
            }
        }
    }

    // one field's change: a value, or Removed
    sealed class Edit<T>
    {
        public readonly bool Removed;
        public readonly T Value;

        Edit(bool removed, T value)
        {
            Removed = removed;
            Value = value;
        }

        public static Edit<T> To(T value) => new Edit<T>(false, value);
        public static readonly Edit<T> Remove = new Edit<T>(true, default(T));
    }

    sealed class InjectOptions
    {
        public bool RemoveXmp;   // drop the XMP block too (it can hold its own copy of the place, date and camera)
    }

    // the injector's form, as text (the website's InjectorFields)
    sealed class InjectorFields
    {
        public static readonly string[] Keys = { "make", "model", "lens", "artist", "copyright", "software", "orientation", "aperture", "shutter", "iso", "focal", "dateTime", "description", "lat", "lon", "alt" };

        readonly Dictionary<string, string> values = Keys.ToDictionary(k => k, k => "");

        public string this[string key]
        {
            get => values[key];
            set => values[key] = value ?? "";
        }

        public InjectorFields Copy()
        {
            var f = new InjectorFields();
            foreach (string k in Keys) f[k] = this[k];
            return f;
        }

        public bool SameAs(InjectorFields other) => Keys.All(k => this[k] == other[k]);
    }

    sealed class FieldError
    {
        public string Field, Message;
    }

    // a value thrown out by the injector, in the website's words
    sealed class InjectException : Exception
    {
        public InjectException(string message) : base(message) { }
    }

    // A JavaScript Map of tags: in the order they first came, a set keeping a tag's place.
    sealed class TagMap
    {
        readonly List<int> order = new List<int>();
        readonly Dictionary<int, InjectEntry> map = new Dictionary<int, InjectEntry>();

        public TagMap() { }

        public TagMap(TagMap from)
        {
            order.AddRange(from.order);
            foreach (var kv in from.map) map[kv.Key] = kv.Value;
        }

        public void Set(int tag, InjectEntry e)
        {
            if (!map.ContainsKey(tag)) order.Add(tag);
            map[tag] = e;
        }

        public void Delete(int tag)
        {
            if (map.Remove(tag)) order.Remove(tag);
        }

        public void Clear()
        {
            order.Clear();
            map.Clear();
        }

        public bool Has(int tag) => map.ContainsKey(tag);
        public InjectEntry Get(int tag) => map.TryGetValue(tag, out InjectEntry e) ? e : null;
        public int Count => order.Count;
        public IEnumerable<KeyValuePair<int, InjectEntry>> Entries => order.Select(t => new KeyValuePair<int, InjectEntry>(t, map[t]));
    }

    sealed class InjectEntry
    {
        public int Type;
        public long Count;
        public byte[] Bytes;   // the value's bytes, in the block's byte order
    }

    // The website's inject.ts: a JPEG's or PNG's EXIF edited in place, byte by byte, the pixels
    // untouched. Only the fields asked for change; every other tag the photo has is copied across
    // as it was, in its own byte order.
    static class Injector
    {
        // ---- dates, shutter speeds and other input ----

        static string Pad2(double n) => JsText.Pad(JsText.Num(n), 2);

        static string ExifDateOf(DateTime d) =>
            d.Year + ":" + Pad2(d.Month) + ":" + Pad2(d.Day) + " " + Pad2(d.Hour) + ":" + Pad2(d.Minute) + ":" + Pad2(d.Second);

        static readonly Regex date = new Regex("^([0-9]{4})[:\\-/.]([0-9]{1,2})[:\\-/.]([0-9]{1,2})(?:[ Tt]([0-9]{1,2}):([0-9]{2})(?::([0-9]{2}))?(?:\\.[0-9]+)?)?" + JsText.SpaceClass + "*(?:[Zz]|[+-][0-9]{2}:?[0-9]{2})?\\z", RegexOptions.CultureInvariant);

        // A date the way people type it (2026:10:08 14:30:00, 2026-10-08 14:30, 2026-10-08T14:30:00Z,
        // 2026-10-08) as EXIF's "YYYY:MM:DD HH:MM:SS", or null when it isn't a real date and time.
        public static string ParseDate(string text)
        {
            Match m = date.Match(JsText.Trim(text));
            if (!m.Success) return null;
            double G(int i, double otherwise) => m.Groups[i].Success ? JsText.ToNumber(m.Groups[i].Value) : otherwise;
            double y = G(1, 0), mo = G(2, 0), d = G(3, 0), h = G(4, 0), mi = G(5, 0), s = G(6, 0);
            if (y < 1000 || mo < 1 || mo > 12 || d < 1 || h > 23 || mi > 59 || s > 59) return null;
            if (d > System.DateTime.DaysInMonth((int)y, (int)mo)) return null;
            return JsText.Num(y) + ":" + Pad2(mo) + ":" + Pad2(d) + " " + Pad2(h) + ":" + Pad2(mi) + ":" + Pad2(s);
        }

        // EXIF's date for now (this PC's clock), or typed text (which must be a real date and time)
        public static string NormalizeDate(string text = null)
        {
            if (text == null) return ExifDateOf(System.DateTime.Now);
            return ParseDate(text) ?? throw new InjectException("Not a date and time: \"" + text + "\"");
        }

        public static string DateOf(DateTime d) => ExifDateOf(d);

        static double Gcd(double a, double b)
        {
            // (JavaScript's `while (b)`: NaN stops it too)
            while (b != 0 && !double.IsNaN(b))
            {
                double t = a % b;
                a = b;
                b = t;
            }
            return a;
        }

        // x as a fraction with the given denominator, reduced: 1.4 -> 7/5
        static (double, double) ToRational(double x, double den)
        {
            double num = JsMath.Round(x * den);
            double g = Gcd(num, den);
            if (g == 0 || double.IsNaN(g)) g = 1;
            return (num / g, den / g);
        }

        static readonly Regex shutterUnit = new Regex(JsText.SpaceClass + "*(?:[sS]|[sS][eE][cC]|\"|" + (char)0x2033 + ")\\z", RegexOptions.CultureInvariant);
        static readonly Regex shutterFraction = new Regex("^([0-9]+)" + JsText.SpaceClass + "*/" + JsText.SpaceClass + "*([0-9]+)\\z", RegexOptions.CultureInvariant);
        static readonly Regex shutterDecimal = new Regex("^[0-9]*\\.?[0-9]+\\z", RegexOptions.CultureInvariant);

        // A shutter speed as typed (1/250, 1/250s, 0.004, 2, 2s, 30") as an exact fraction of a
        // second, or null when it isn't one.
        public static (double Num, double Den)? ParseShutter(string text)
        {
            string t = shutterUnit.Replace(JsText.Trim(text), "", 1);
            Match frac = shutterFraction.Match(t);
            if (frac.Success)
            {
                double num = JsText.ToNumber(frac.Groups[1].Value), den = JsText.ToNumber(frac.Groups[2].Value);
                if (num <= 0 || den <= 0 || num > 0xffffffff || den > 0xffffffff) return null;
                return (num, den);
            }
            if (!shutterDecimal.IsMatch(t)) return null;
            double x = JsText.ToNumber(t);
            if (!(x > 0) || x > 86400) return null;
            if (x < 1)
            {
                double inv = 1 / x;
                if (Math.Abs(inv - JsMath.Round(inv)) < 1e-6 * inv) return (1, JsMath.Round(inv));
            }
            return ToRational(x, 10000);
        }

        // a shutter fraction the way a camera writes it: 1/250, 2, 0.8
        public static string FormatShutter((double Num, double Den) r)
        {
            double num = r.Num, den = r.Den;
            if (den == 0) return "";
            if (num == 1) return "1/" + JsText.Num(den);
            if (den == 1) return JsText.Num(num);
            if (num < den && den % num == 0) return "1/" + JsText.Num(den / num);
            return JsText.Num(JsText.FixedNumber(num / den, 4));
        }

        static string TrimNum(double x, int places) => JsText.Num(JsText.FixedNumber(x, places));

        // ---- the EXIF block as a set of tags ----

        sealed class Model
        {
            public bool Le;
            public TagMap Ifd0 = new TagMap(), Exif = new TagMap(), Gps = new TagMap(), Interop = new TagMap(), Ifd1 = new TagMap();
            public byte[] Thumbnail;
            public long? MakerNoteAt;   // where the MakerNote sat in the photo's block
        }

        const int Ifd0Exif = 0x8769, Ifd0Gps = 0x8825, ExifInterop = 0xa005, Ifd1ThumbAt = 0x0201, Ifd1ThumbLen = 0x0202, MakerNote = 0x927c;
        // more tags than this in one block means it's damaged
        const int MaxEntries = 1000;

        // a TIFF block as tags; anything that can't be read is left out
        static Model ParseBlock(byte[] tiff)
        {
            if (tiff.Length < 8) return null;
            bool le = tiff[0] == 0x49 && tiff[1] == 0x49;
            if (!le && !(tiff[0] == 0x4d && tiff[1] == 0x4d)) return null;
            long U16(long at) => at + 2 <= tiff.Length && at >= 0 ? (le ? tiff[at] | (tiff[at + 1] << 8) : (tiff[at] << 8) | tiff[at + 1]) : 0;
            long U32(long at) => at + 4 <= tiff.Length && at >= 0
                ? (le ? (uint)(tiff[at] | (tiff[at + 1] << 8) | (tiff[at + 2] << 16) | (tiff[at + 3] << 24)) : (uint)((tiff[at] << 24) | (tiff[at + 1] << 16) | (tiff[at + 2] << 8) | tiff[at + 3]))
                : 0;
            if (U16(2) != 42) return null;
            var model = new Model { Le = le };
            var offsets = new Dictionary<int, long>();

            long ReadIfd(long at, TagMap into, params int[] keepOffsetsOf)
            {
                if (at < 8 || at + 2 > tiff.Length) return 0;
                long n = Math.Min(U16(at), MaxEntries);
                for (long i = 0; i < n; i++)
                {
                    long e = at + 2 + i * 12;
                    if (e + 12 > tiff.Length) break;
                    int tag = (int)U16(e), type = (int)U16(e + 2);
                    long count = U32(e + 4);
                    int size = ExifReader.TypeSize(type);
                    if (size == 0) continue;
                    long total = count * size;
                    if (keepOffsetsOf.Contains(tag)) offsets[tag] = U32(e + 8);
                    long start = e + 8;
                    if (total > 4)
                    {
                        start = U32(e + 8);
                        if (start + total > tiff.Length) continue;
                        if (tag == MakerNote) model.MakerNoteAt = start;
                    }
                    var bytes = new byte[total];
                    Array.Copy(tiff, start, bytes, 0, total);
                    into.Set(tag, new InjectEntry { Type = type, Count = count, Bytes = bytes });
                }
                return U32(at + 2 + n * 12);
            }

            long ifd1At = ReadIfd(U32(4), model.Ifd0, Ifd0Exif, Ifd0Gps);
            if (offsets.ContainsKey(Ifd0Exif)) ReadIfd(offsets[Ifd0Exif], model.Exif, ExifInterop);
            if (offsets.ContainsKey(Ifd0Gps)) ReadIfd(offsets[Ifd0Gps], model.Gps);
            if (offsets.ContainsKey(ExifInterop)) ReadIfd(offsets[ExifInterop], model.Interop);
            if (ifd1At != 0)
            {
                ReadIfd(ifd1At, model.Ifd1, Ifd1ThumbAt, Ifd1ThumbLen);
                bool hasAt = offsets.TryGetValue(Ifd1ThumbAt, out long thumbAt), hasLen = offsets.TryGetValue(Ifd1ThumbLen, out long thumbLen);
                if (hasAt && hasLen && thumbLen > 0 && thumbAt + thumbLen <= tiff.Length)
                {
                    model.Thumbnail = new byte[thumbLen];
                    Array.Copy(tiff, thumbAt, model.Thumbnail, 0, thumbLen);
                }
                // a thumbnail kept as strips (not seen in phone photos) can't be moved safely, so that IFD goes
                else model.Ifd1.Clear();
            }
            // pointers are written fresh; SubIFDs (0x014a) point at blocks not copied
            foreach (int tag in new[] { Ifd0Exif, Ifd0Gps, 0x014a }) model.Ifd0.Delete(tag);
            model.Exif.Delete(ExifInterop);
            model.Ifd1.Delete(Ifd1ThumbAt);
            model.Ifd1.Delete(Ifd1ThumbLen);
            return model;
        }

        static InjectEntry Pointer() => new InjectEntry { Type = 4, Count = 1, Bytes = new byte[4] };

        // the TIFF block, or null when no tag is left
        static byte[] Serialize(Model model)
        {
            bool le = model.Le;
            var ifd1 = new TagMap(model.Ifd1);
            if (model.Thumbnail != null)
            {
                ifd1.Set(Ifd1ThumbAt, Pointer());
                ifd1.Set(Ifd1ThumbLen, Pointer());
            }
            else ifd1.Clear();
            var exif = new TagMap(model.Exif);
            if (model.Interop.Count > 0) exif.Set(ExifInterop, Pointer());
            var ifd0 = new TagMap(model.Ifd0);
            if (exif.Count > 0) ifd0.Set(Ifd0Exif, Pointer());
            if (model.Gps.Count > 0) ifd0.Set(Ifd0Gps, Pointer());
            if (ifd0.Count == 0 && ifd1.Count == 0) return null;

            var ifds = new List<(string Name, TagMap Tags)> { ("ifd0", ifd0), ("exif", exif), ("interop", model.Interop), ("gps", model.Gps), ("ifd1", ifd1) }
                .Where(d => d.Name == "ifd0" || d.Tags.Count > 0).ToList();

            // tables first, then the values that don't fit in their 4-byte slot
            var at = new Dictionary<string, long>();
            long cursor = 8;
            foreach (var d in ifds)
            {
                at[d.Name] = cursor;
                cursor += 2 + d.Tags.Count * 12 + 4;
            }
            long tablesEnd = cursor;
            var where = new List<(InjectEntry Entry, long At)>();
            var placed = new Dictionary<InjectEntry, long>();
            var big = new List<InjectEntry>();
            InjectEntry makerNote = null;
            foreach (var d in ifds)
                foreach (var kv in d.Tags.Entries)
                {
                    if (kv.Value.Bytes.Length <= 4) continue;
                    if (d.Name == "exif" && kv.Key == MakerNote) makerNote = kv.Value;
                    else big.Add(kv.Value);
                }
            long Align(long n) => n + n % 2;
            void Place(InjectEntry e, long p)
            {
                if (!placed.ContainsKey(e)) where.Add((e, p));
                else where[where.FindIndex(w => ReferenceEquals(w.Entry, e))] = (e, p);
                placed[e] = p;
            }
            // some makers (Canon, Sony …) point inside their MakerNote from the start of the block,
            // so it stays at the same place when it can
            long? mnAt = makerNote != null && model.MakerNoteAt.HasValue && model.MakerNoteAt.Value >= tablesEnd && model.MakerNoteAt.Value % 2 == 0 ? model.MakerNoteAt : null;
            var later = new List<InjectEntry>();
            foreach (InjectEntry e in big)
            {
                if (mnAt.HasValue && cursor + e.Bytes.Length > mnAt.Value)
                {
                    later.Add(e);
                    continue;
                }
                Place(e, cursor);
                cursor = Align(cursor + e.Bytes.Length);
            }
            if (makerNote != null)
            {
                long mnStart = mnAt ?? cursor;
                Place(makerNote, mnStart);
                cursor = Align(mnStart + makerNote.Bytes.Length);
            }
            foreach (InjectEntry e in later)
            {
                Place(e, cursor);
                cursor = Align(cursor + e.Bytes.Length);
            }
            long thumbAt = cursor;
            if (model.Thumbnail != null) cursor = Align(cursor + model.Thumbnail.Length);

            var output = new byte[cursor];
            void U16(long p, long v)
            {
                ushort x = (ushort)v;
                if (le)
                {
                    output[p] = (byte)x;
                    output[p + 1] = (byte)(x >> 8);
                }
                else
                {
                    output[p] = (byte)(x >> 8);
                    output[p + 1] = (byte)x;
                }
            }
            void U32(long p, long v)
            {
                uint x = (uint)v;
                if (le)
                {
                    output[p] = (byte)x;
                    output[p + 1] = (byte)(x >> 8);
                    output[p + 2] = (byte)(x >> 16);
                    output[p + 3] = (byte)(x >> 24);
                }
                else
                {
                    output[p] = (byte)(x >> 24);
                    output[p + 1] = (byte)(x >> 16);
                    output[p + 2] = (byte)(x >> 8);
                    output[p + 3] = (byte)x;
                }
            }
            output[0] = output[1] = (byte)(le ? 0x49 : 0x4d);
            U16(2, 42);
            U32(4, 8);
            long PointerTo(int tag)
            {
                string name = tag == Ifd0Exif ? "exif" : tag == Ifd0Gps ? "gps" : "interop";
                return at.TryGetValue(name, out long v) ? v : 0;
            }
            foreach (var d in ifds)
            {
                long start = at[d.Name];
                var tags = d.Tags.Entries.OrderBy(kv => kv.Key).ToList();
                U16(start, tags.Count);
                for (int i = 0; i < tags.Count; i++)
                {
                    int tag = tags[i].Key;
                    InjectEntry e = tags[i].Value;
                    long p = start + 2 + i * 12;
                    U16(p, tag);
                    U16(p + 2, e.Type);
                    U32(p + 4, e.Count);
                    if (d.Name == "ifd1" && tag == Ifd1ThumbAt) U32(p + 8, thumbAt);
                    else if (d.Name == "ifd1" && tag == Ifd1ThumbLen) U32(p + 8, model.Thumbnail.Length);
                    else if ((d.Name == "ifd0" && (tag == Ifd0Exif || tag == Ifd0Gps)) || (d.Name == "exif" && tag == ExifInterop)) U32(p + 8, PointerTo(tag));
                    else if (e.Bytes.Length <= 4) Array.Copy(e.Bytes, 0, output, p + 8, e.Bytes.Length);
                    else U32(p + 8, placed[e]);
                }
                long next = d.Name == "ifd0" && at.ContainsKey("ifd1") ? at["ifd1"] : 0;
                U32(start + 2 + tags.Count * 12, next);
            }
            foreach (var w in where) Array.Copy(w.Entry.Bytes, 0, output, w.At, w.Entry.Bytes.Length);
            if (model.Thumbnail != null) Array.Copy(model.Thumbnail, 0, output, thumbAt, model.Thumbnail.Length);
            return output;
        }

        // ---- values in the block's byte order ----

        // text as UTF-8 and a closing zero (a lone surrogate as U+FFFD, as TextEncoder writes it)
        static InjectEntry Ascii(string text)
        {
            byte[] raw = Utf8(text);
            var bytes = new byte[raw.Length + 1];
            Array.Copy(raw, bytes, raw.Length);
            return new InjectEntry { Type = 2, Count = bytes.Length, Bytes = bytes };
        }

        public static byte[] Utf8(string s)
        {
            var b = new List<byte>(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                int c = s[i];
                if (c >= 0xD800 && c <= 0xDBFF && i + 1 < s.Length && s[i + 1] >= 0xDC00 && s[i + 1] <= 0xDFFF)
                {
                    c = 0x10000 + ((c - 0xD800) << 10) + (s[i + 1] - 0xDC00);
                    i++;
                }
                else if (c >= 0xD800 && c <= 0xDFFF) c = 0xFFFD;
                if (c < 0x80) b.Add((byte)c);
                else if (c < 0x800)
                {
                    b.Add((byte)(0xC0 | (c >> 6)));
                    b.Add((byte)(0x80 | (c & 0x3F)));
                }
                else if (c < 0x10000)
                {
                    b.Add((byte)(0xE0 | (c >> 12)));
                    b.Add((byte)(0x80 | ((c >> 6) & 0x3F)));
                    b.Add((byte)(0x80 | (c & 0x3F)));
                }
                else
                {
                    b.Add((byte)(0xF0 | (c >> 18)));
                    b.Add((byte)(0x80 | ((c >> 12) & 0x3F)));
                    b.Add((byte)(0x80 | ((c >> 6) & 0x3F)));
                    b.Add((byte)(0x80 | (c & 0x3F)));
                }
            }
            return b.ToArray();
        }

        // JavaScript's ToUint32 / ToUint16 (what a DataView writes)
        static uint ToUint32(double x)
        {
            if (double.IsNaN(x) || double.IsInfinity(x)) return 0;
            double t = Math.Truncate(x) % 4294967296.0;
            if (t < 0) t += 4294967296.0;
            return (uint)t;
        }

        static InjectEntry Short(double n, bool le)
        {
            ushort v = (ushort)(ToUint32(n) & 0xffff);
            return new InjectEntry { Type = 3, Count = 1, Bytes = le ? new[] { (byte)v, (byte)(v >> 8) } : new[] { (byte)(v >> 8), (byte)v } };
        }

        static InjectEntry Rationals(bool le, params (double Num, double Den)[] values)
        {
            var bytes = new byte[values.Length * 8];
            for (int i = 0; i < values.Length; i++)
            {
                Put32(bytes, i * 8, ToUint32(values[i].Num), le);
                Put32(bytes, i * 8 + 4, ToUint32(values[i].Den), le);
            }
            return new InjectEntry { Type = 5, Count = values.Length, Bytes = bytes };
        }

        static void Put32(byte[] b, int at, uint v, bool le)
        {
            if (le)
            {
                b[at] = (byte)v;
                b[at + 1] = (byte)(v >> 8);
                b[at + 2] = (byte)(v >> 16);
                b[at + 3] = (byte)(v >> 24);
            }
            else
            {
                b[at] = (byte)(v >> 24);
                b[at + 1] = (byte)(v >> 16);
                b[at + 2] = (byte)(v >> 8);
                b[at + 3] = (byte)v;
            }
        }

        // decimal degrees as degrees, minutes and seconds (to 1/10000 of a second)
        static InjectEntry Dms(double value, bool le)
        {
            double tenThousandths = JsMath.Round(Math.Abs(value) * 3600 * 10000);
            double deg = Math.Floor(tenThousandths / 36000000);
            double min = Math.Floor(tenThousandths % 36000000 / 600000);
            double sec = tenThousandths % 600000;
            (double, double) s = sec % 10000 == 0 ? (sec / 10000, 1) : ToRational(sec / 10000, 10000);
            return Rationals(le, (deg, 1), (min, 1), s);
        }

        // tags worked out from another tag: they'd contradict an edit, so they go with it
        static readonly int[] derivedIso = { 0x8830, 0x8832, 0x8833, 0x8834, 0x8835 }, derivedFNumber = { 0x9202 }, derivedExposure = { 0x9201 }, derivedFocal = { 0xa405 },
            derivedDateExif = { 0x9010, 0x9011, 0x9012, 0x9290, 0x9291, 0x9292 }, derivedDateGps = { 0x0007, 0x001d };

        static void Drop(TagMap ifd, IEnumerable<int> tags)
        {
            foreach (int t in tags) ifd.Delete(t);
        }

        static void Apply(Model model, ExifEdits edits)
        {
            bool le = model.Le;
            bool exifWasEmpty = model.Exif.Count == 0;
            void Text(TagMap ifd, int tag, Edit<string> value)
            {
                if (value == null) return;
                if (value.Removed || JsText.Trim(value.Value ?? "") == "") ifd.Delete(tag);
                else ifd.Set(tag, Ascii(JsText.Trim(value.Value)));
            }
            Text(model.Ifd0, 0x010f, edits.Make);
            Text(model.Ifd0, 0x0110, edits.Model);
            Text(model.Ifd0, 0x0131, edits.Software);
            Text(model.Ifd0, 0x013b, edits.Artist);
            Text(model.Ifd0, 0x8298, edits.Copyright);
            Text(model.Ifd0, 0x010e, edits.Description);
            Text(model.Exif, 0xa434, edits.LensModel);
            Text(model.Exif, 0xa433, edits.LensMake);
            if (edits.ForgetCamera)
            {
                Drop(model.Exif, new[] { MakerNote, 0xa430, 0xa431, 0xa432, 0xa435 });
                model.MakerNoteAt = null;
            }
            if (edits.Orientation != null)
            {
                if (edits.Orientation.Removed) model.Ifd0.Delete(0x0112);
                else model.Ifd0.Set(0x0112, Short(edits.Orientation.Value, le));
            }
            if (edits.DateTime != null)
            {
                Drop(model.Exif, derivedDateExif);
                Drop(model.Gps, derivedDateGps);
                if (edits.DateTime.Removed || JsText.Trim(edits.DateTime.Value ?? "") == "")
                {
                    model.Ifd0.Delete(0x0132);
                    Drop(model.Exif, new[] { 0x9003, 0x9004 });
                }
                else
                {
                    string dt = NormalizeDate(edits.DateTime.Value);
                    model.Ifd0.Set(0x0132, Ascii(dt));
                    model.Exif.Set(0x9003, Ascii(dt));
                    model.Exif.Set(0x9004, Ascii(dt));
                }
            }
            void Rational(int tag, Edit<double> value, int[] derived, double den)
            {
                if (value == null) return;
                Drop(model.Exif, derived);
                if (value.Removed) model.Exif.Delete(tag);
                else model.Exif.Set(tag, Rationals(le, ToRational(value.Value, den)));
            }
            if (edits.Iso != null)
            {
                Drop(model.Exif, derivedIso);
                if (edits.Iso.Removed) model.Exif.Delete(0x8827);
                else model.Exif.Set(0x8827, Short(JsMath.Round(edits.Iso.Value), le));
            }
            Rational(0x829d, edits.FNumber, derivedFNumber, 100);
            // thousandths: phones write focal lengths like 6.765 mm
            Rational(0x920a, edits.FocalLength, derivedFocal, 1000);
            // after the focal length, which drops the old 35 mm figure with it
            if (edits.FocalLength35 != null)
            {
                if (edits.FocalLength35.Removed) model.Exif.Delete(0xa405);
                else model.Exif.Set(0xa405, Short(JsMath.Round(edits.FocalLength35.Value), le));
            }
            if (edits.ExposureTime != null || edits.ExposureText != null)
            {
                bool removed = edits.ExposureTime != null ? edits.ExposureTime.Removed : edits.ExposureText.Removed;
                (double, double)? value = null;
                if (!removed)
                {
                    value = edits.ExposureTime != null ? edits.ExposureTime.Value : ParseShutter(edits.ExposureText.Value);
                    if (value == null) throw new InjectException("Not a shutter speed: \"" + edits.ExposureText.Value + "\"");
                }
                Drop(model.Exif, derivedExposure);
                if (value == null) model.Exif.Delete(0x829a);
                else model.Exif.Set(0x829a, Rationals(le, value.Value));
            }
            if (edits.Gps != null)
            {
                // a new place replaces every GPS tag: the old block's destination, direction and
                // clock describe where the photo really was
                model.Gps.Clear();
                if (!edits.Gps.Removed)
                {
                    var (latitude, longitude, altitude) = edits.Gps.Value;
                    if (!(Math.Abs(latitude) <= 90) || !(Math.Abs(longitude) <= 180)) throw new InjectException("Not a place on Earth: " + JsText.Num(latitude) + ", " + JsText.Num(longitude));
                    model.Gps.Set(0x0000, new InjectEntry { Type = 1, Count = 4, Bytes = new byte[] { 2, 3, 0, 0 } });
                    model.Gps.Set(0x0001, Ascii(latitude >= 0 ? "N" : "S"));
                    model.Gps.Set(0x0002, Dms(latitude, le));
                    model.Gps.Set(0x0003, Ascii(longitude >= 0 ? "E" : "W"));
                    model.Gps.Set(0x0004, Dms(longitude, le));
                    if (altitude.HasValue && !double.IsNaN(altitude.Value) && !double.IsInfinity(altitude.Value))
                    {
                        model.Gps.Set(0x0005, new InjectEntry { Type = 1, Count = 1, Bytes = new byte[] { (byte)(altitude.Value < 0 ? 1 : 0) } });
                        model.Gps.Set(0x0006, Rationals(le, ToRational(Math.Abs(altitude.Value), 100)));
                    }
                }
            }
            if (edits.Thumbnail != null)
            {
                if (!edits.Thumbnail.Removed && edits.Thumbnail.Value != null && edits.Thumbnail.Value.Length > 0)
                {
                    model.Thumbnail = edits.Thumbnail.Value;
                    model.Ifd1.Set(0x0103, Short(6, le));   // JPEG
                }
                else
                {
                    model.Thumbnail = null;
                    model.Ifd1.Clear();
                }
            }
            // a brand-new Exif block says which Exif version it follows
            if (exifWasEmpty && model.Exif.Count > 0 && !model.Exif.Has(0x9000))
                model.Exif.Set(0x9000, new InjectEntry { Type = 7, Count = 4, Bytes = new byte[] { 0x30, 0x32, 0x33, 0x32 } });
        }

        // a TIFF block holding just these fields (for tests and new files)
        public static byte[] BuildTiff(ExifEdits edits)
        {
            var model = new Model { Le = true };
            Apply(model, edits);
            return Serialize(model) ?? throw new InjectException("There are no EXIF fields to write");
        }

        // ---- finding and replacing the block in a file ----

        sealed class Segment
        {
            public int Marker;
            public int Start, Length;
        }

        // the JPEG's segments before its first scan, and where the scan (and the rest) starts
        static List<Segment> SplitJpeg(byte[] jpeg, out int tail)
        {
            var segments = new List<Segment>();
            int pos = 2;
            while (pos + 1 < jpeg.Length)
            {
                if (jpeg[pos] != 0xff) break;
                int marker = jpeg[pos + 1];
                if (marker == 0xff)
                {
                    pos++;   // a fill byte
                    continue;
                }
                if (marker == 0xda || marker == 0xd9) break;
                if (marker == 0x01 || (marker >= 0xd0 && marker <= 0xd7))
                {
                    segments.Add(new Segment { Marker = marker, Start = pos, Length = 2 });
                    pos += 2;
                    continue;
                }
                if (pos + 4 > jpeg.Length) break;
                int len = (jpeg[pos + 2] << 8) | jpeg[pos + 3];
                if (len < 2 || pos + 2 + len > jpeg.Length) break;
                segments.Add(new Segment { Marker = marker, Start = pos, Length = 2 + len });
                pos += 2 + len;
            }
            tail = pos;
            return segments;
        }

        static bool IsExifApp1(byte[] f, Segment s) => s.Marker == 0xe1 && s.Length >= 10 && ExifReader.StartsWith(new Bytes(f, s.Start + s.Length), s.Start + 4, "Exif\0\0");

        static bool IsXmpApp1(byte[] f, Segment s)
        {
            var b = new Bytes(f, s.Start + s.Length);
            return s.Marker == 0xe1 && (ExifReader.StartsWith(b, s.Start + 4, ExifReader.XmpSig) || ExifReader.StartsWith(b, s.Start + 4, ExifReader.XmpExtensionSig));
        }

        sealed class Chunk
        {
            public string Type;
            public int Start, Length;
        }

        static List<Chunk> SplitPng(byte[] png, out int tail)
        {
            var chunks = new List<Chunk>();
            long pos = 8;
            while (pos + 12 <= png.Length)
            {
                long len = (uint)((png[pos] << 24) | (png[pos + 1] << 16) | (png[pos + 2] << 8) | png[pos + 3]);
                long end = pos + 12 + len;
                if (end > png.Length) break;
                string type = JsText.Chars(png, (int)pos + 4, 4);
                chunks.Add(new Chunk { Type = type, Start = (int)pos, Length = (int)(end - pos) });
                pos = end;
                if (type == "IEND") break;
            }
            tail = (int)pos;
            return chunks;
        }

        static bool IsPngXmp(byte[] f, Chunk c) => c.Type == "iTXt" && ExifReader.StartsWith(new Bytes(f, c.Start + c.Length), c.Start + 8, ExifReader.PngXmpKeyword + "\0");

        // the photo's EXIF TIFF block (JPEG APP1 or PNG eXIf), or null
        static byte[] FindExifBlock(byte[] bytes)
        {
            string format = ExifReader.DetectFormat(bytes);
            if (format == "jpeg")
            {
                Segment seg = SplitJpeg(bytes, out _).FirstOrDefault(s => IsExifApp1(bytes, s));
                if (seg == null) return null;
                var block = new byte[seg.Length - 10];
                Array.Copy(bytes, seg.Start + 10, block, 0, block.Length);
                return block;
            }
            if (format == "png")
            {
                Chunk chunk = SplitPng(bytes, out _).FirstOrDefault(c => c.Type == "eXIf");
                if (chunk == null) return null;
                var block = new byte[chunk.Length - 12];
                Array.Copy(bytes, chunk.Start + 8, block, 0, block.Length);
                return block;
            }
            return null;
        }

        static byte[] EditedTiff(byte[] bytes, ExifEdits edits)
        {
            byte[] old = FindExifBlock(bytes);
            Model model = (old != null ? ParseBlock(old) : null) ?? new Model { Le = true };
            Apply(model, edits);
            return Serialize(model);
        }

        // a JPEG's EXIF edited; the scan data and anything after it are untouched
        public static byte[] InjectJpeg(byte[] jpeg, ExifEdits edits, InjectOptions options = null)
        {
            if (jpeg.Length < 2 || jpeg[0] != 0xff || jpeg[1] != 0xd8) throw new InjectException("Not a valid JPEG image: missing SOI marker (0xFFD8)");
            options = options ?? new InjectOptions();
            byte[] tiff = EditedTiff(jpeg, edits);
            byte[] app1 = null;
            if (tiff != null)
            {
                long len = 2 + 6 + tiff.Length;
                if (len > 0xffff) throw new InjectException("The EXIF would be " + (long)Math.Ceiling(len / 1024.0) + " KB, but a JPEG can hold at most 64 KB of it.");
                app1 = new byte[2 + len];
                app1[0] = 0xff;
                app1[1] = 0xe1;
                app1[2] = (byte)(len >> 8);
                app1[3] = (byte)len;
                app1[4] = 0x45; app1[5] = 0x78; app1[6] = 0x69; app1[7] = 0x66;
                Array.Copy(tiff, 0, app1, 10, tiff.Length);
            }
            List<Segment> segments = SplitJpeg(jpeg, out int tail);
            var output = new MemoryStream(jpeg.Length + (app1?.Length ?? 0));
            output.WriteByte(0xff);
            output.WriteByte(0xd8);
            bool placed = false;
            bool hadExif = segments.Any(s => IsExifApp1(jpeg, s));
            for (int i = 0; i < segments.Count; i++)
            {
                Segment s = segments[i];
                if (IsExifApp1(jpeg, s))
                {
                    if (!placed && app1 != null) output.Write(app1, 0, app1.Length);
                    placed = true;
                    continue;
                }
                if (options.RemoveXmp && IsXmpApp1(jpeg, s)) continue;
                output.Write(jpeg, s.Start, s.Length);
                // a new block goes after the JFIF header(s), which must come first
                bool nextIsApp0 = i + 1 < segments.Count && segments[i + 1].Marker == 0xe0;
                if (!hadExif && !placed && app1 != null && s.Marker == 0xe0 && !nextIsApp0)
                {
                    output.Write(app1, 0, app1.Length);
                    placed = true;
                }
            }
            byte[] head = output.ToArray();
            if (!placed && app1 != null)
            {
                // right after SOI
                var withApp1 = new byte[head.Length + app1.Length];
                withApp1[0] = 0xff;
                withApp1[1] = 0xd8;
                Array.Copy(app1, 0, withApp1, 2, app1.Length);
                Array.Copy(head, 2, withApp1, 2 + app1.Length, head.Length - 2);
                head = withApp1;
            }
            var result = new byte[head.Length + jpeg.Length - tail];
            Array.Copy(head, result, head.Length);
            Array.Copy(jpeg, tail, result, head.Length, jpeg.Length - tail);
            return result;
        }

        // a PNG's eXIf chunk edited; the pixels, colour profile and other chunks are untouched
        public static byte[] InjectPng(byte[] png, ExifEdits edits, InjectOptions options = null)
        {
            byte[] signature = { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a };
            if (png.Length < 8 || signature.Where((b, i) => png[i] != b).Any()) throw new InjectException("Not a valid PNG image: missing PNG 8-byte signature");
            options = options ?? new InjectOptions();
            byte[] tiff = EditedTiff(png, edits);
            byte[] exifChunk = null;
            if (tiff != null)
            {
                exifChunk = new byte[12 + tiff.Length];
                Put32(exifChunk, 0, (uint)tiff.Length, false);
                exifChunk[4] = 0x65; exifChunk[5] = 0x58; exifChunk[6] = 0x49; exifChunk[7] = 0x66;
                Array.Copy(tiff, 0, exifChunk, 8, tiff.Length);
                uint crc = Png.Crc(0xffffffff, exifChunk, 4, 4 + tiff.Length) ^ 0xffffffff;
                Put32(exifChunk, 8 + tiff.Length, crc, false);
            }
            List<Chunk> chunks = SplitPng(png, out int tail);
            bool hadExif = chunks.Any(c => c.Type == "eXIf");
            var output = new MemoryStream(png.Length + (exifChunk?.Length ?? 0));
            output.Write(png, 0, 8);
            bool placed = false;
            foreach (Chunk c in chunks)
            {
                if (c.Type == "eXIf")
                {
                    if (!placed && exifChunk != null) output.Write(exifChunk, 0, exifChunk.Length);
                    placed = true;
                    continue;
                }
                if (options.RemoveXmp && IsPngXmp(png, c)) continue;
                output.Write(png, c.Start, c.Length);
                // eXIf has to come before the picture data; right after IHDR is safe
                if (!hadExif && !placed && exifChunk != null && c.Type == "IHDR")
                {
                    output.Write(exifChunk, 0, exifChunk.Length);
                    placed = true;
                }
            }
            output.Write(png, tail, png.Length - tail);
            return output.ToArray();
        }

        // a JPEG's or PNG's EXIF edited; other kinds are refused
        public static byte[] Inject(byte[] bytes, ExifEdits edits, InjectOptions options = null)
        {
            string format = ExifReader.DetectFormat(bytes);
            if (format == "jpeg") return InjectJpeg(bytes, edits, options);
            if (format == "png") return InjectPng(bytes, edits, options);
            throw new InjectException("EXIF injection is supported for JPEG and PNG images (got " + JsText.Upper(format) + ")");
        }

        public static bool CanInject(string format) => format == "jpeg" || format == "png";

        // ---- the injector's form ----

        static string EntryText(InjectEntry e)
        {
            if (e == null || e.Type != 2) return "";
            int end = Array.IndexOf(e.Bytes, (byte)0);
            int count = end < 0 ? e.Bytes.Length : end;
            return JsText.Trim(JsText.Utf8Fatal(e.Bytes, 0, count) ?? JsText.Latin1(e.Bytes, 0, count));
        }

        static List<double> EntryNumbers(InjectEntry e, bool le)
        {
            var output = new List<double>();
            if (e == null) return output;
            for (long i = 0; i < e.Count; i++)
            {
                if (e.Type == 3 && (i + 1) * 2 <= e.Bytes.Length) output.Add(le ? e.Bytes[i * 2] | (e.Bytes[i * 2 + 1] << 8) : (e.Bytes[i * 2] << 8) | e.Bytes[i * 2 + 1]);
                else if (e.Type == 4 && (i + 1) * 4 <= e.Bytes.Length) output.Add(Get32(e.Bytes, i * 4, le));
                else if (e.Type == 1) output.Add(i < e.Bytes.Length ? e.Bytes[i] : double.NaN);
            }
            return output;
        }

        static uint Get32(byte[] b, long at, bool le) => le
            ? (uint)(b[at] | (b[at + 1] << 8) | (b[at + 2] << 16) | (b[at + 3] << 24))
            : (uint)((b[at] << 24) | (b[at + 1] << 16) | (b[at + 2] << 8) | b[at + 3]);

        static List<(double Num, double Den)> EntryRationals(InjectEntry e, bool le)
        {
            var output = new List<(double, double)>();
            if (e == null || (e.Type != 5 && e.Type != 10)) return output;
            for (int i = 0; (i + 1) * 8 <= e.Bytes.Length; i++)
            {
                uint n = Get32(e.Bytes, i * 8, le), d = Get32(e.Bytes, i * 8 + 4, le);
                output.Add(e.Type == 5 ? ((double)n, (double)d) : ((double)(int)n, (double)(int)d));
            }
            return output;
        }

        // the photo's own values for the form, exactly as stored (nothing rounded for show)
        public static InjectorFields ReadFields(byte[] bytes)
        {
            byte[] block = FindExifBlock(bytes);
            Model model = block != null ? ParseBlock(block) : null;
            var fields = new InjectorFields();
            if (model == null) return fields;
            bool le = model.Le;
            double Ratio((double Num, double Den)? r) => r.HasValue && r.Value.Den != 0 && !double.IsNaN(r.Value.Den) ? r.Value.Num / r.Value.Den : double.NaN;
            string Decimal(double x, int places) => !double.IsNaN(x) && !double.IsInfinity(x) ? TrimNum(x, places) : "";
            (double, double)? FirstOf(List<(double, double)> list) => list.Count > 0 ? list[0] : ((double, double)?)null;

            fields["make"] = EntryText(model.Ifd0.Get(0x010f));
            fields["model"] = EntryText(model.Ifd0.Get(0x0110));
            fields["artist"] = EntryText(model.Ifd0.Get(0x013b));
            fields["copyright"] = EntryText(model.Ifd0.Get(0x8298));
            fields["software"] = EntryText(model.Ifd0.Get(0x0131));
            fields["description"] = EntryText(model.Ifd0.Get(0x010e));
            fields["lens"] = EntryText(model.Exif.Get(0xa434));
            string dt = EntryText(model.Ifd0.Get(0x0132));
            fields["dateTime"] = dt.Length > 0 ? dt : EntryText(model.Exif.Get(0x9003));
            List<double> o = EntryNumbers(model.Ifd0.Get(0x0112), le);
            fields["orientation"] = o.Count > 0 && o[0] >= 1 && o[0] <= 8 ? JsText.Num(o[0]) : "";
            List<double> iso = EntryNumbers(model.Exif.Get(0x8827), le);
            fields["iso"] = iso.Count > 0 && iso[0] != 0 && !double.IsNaN(iso[0]) ? JsText.Num(iso[0]) : "";
            fields["aperture"] = Decimal(Ratio(FirstOf(EntryRationals(model.Exif.Get(0x829d), le))), 2);
            fields["focal"] = Decimal(Ratio(FirstOf(EntryRationals(model.Exif.Get(0x920a), le))), 3);
            var exposure = FirstOf(EntryRationals(model.Exif.Get(0x829a), le));
            fields["shutter"] = exposure.HasValue ? FormatShutter(exposure.Value) : "";

            List<(double, double)> lat = EntryRationals(model.Gps.Get(0x0002), le), lon = EntryRationals(model.Gps.Get(0x0004), le);
            if (lat.Count >= 3 && lon.Count >= 3)
            {
                double Deg(List<(double, double)> r) => Ratio(r[0]) + Ratio(r[1]) / 60 + Ratio(r[2]) / 3600;
                double latSign = JsText.Upper(EntryText(model.Gps.Get(0x0001))) == "S" ? -1 : 1;
                double lonSign = JsText.Upper(EntryText(model.Gps.Get(0x0003))) == "W" ? -1 : 1;
                fields["lat"] = Decimal(latSign * Deg(lat), 6);
                fields["lon"] = Decimal(lonSign * Deg(lon), 6);
                double alt = Ratio(FirstOf(EntryRationals(model.Gps.Get(0x0006), le)));
                if (!double.IsNaN(alt) && !double.IsInfinity(alt))
                {
                    List<double> below = EntryNumbers(model.Gps.Get(0x0005), le);
                    fields["alt"] = Decimal(below.Count > 0 && below[0] == 1 ? -alt : alt, 2);
                }
            }
            return fields;
        }

        static readonly Regex number = new Regex("^[-+]?([0-9]+\\.?[0-9]*|\\.[0-9]+)\\z", RegexOptions.CultureInvariant);

        // The form as edits: a field still showing what the photo has is left alone (nothing
        // rounded or rewritten), a changed one is checked and written, a cleared one removed.
        public static ExifEdits EditsFrom(InjectorFields now, InjectorFields loaded, out FieldError error)
        {
            var edits = new ExifEdits();
            error = null;
            bool Changed(string k) => JsText.Trim(now[k]) != JsText.Trim(loaded[k]);
            double? Num(string k)
            {
                string t = JsText.Trim(now[k]);
                return number.IsMatch(t) ? JsText.ToNumber(t) : (double?)null;
            }
            Edit<string> TextEdit(string k)
            {
                string t = JsText.Trim(now[k]);
                return t.Length > 0 ? Edit<string>.To(t) : Edit<string>.Remove;
            }
            FieldError Fail(string field, string message) => new FieldError { Field = field, Message = message };

            if (Changed("make")) edits.Make = TextEdit("make");
            if (Changed("model")) edits.Model = TextEdit("model");
            if (Changed("lens")) edits.LensModel = TextEdit("lens");
            if (Changed("artist")) edits.Artist = TextEdit("artist");
            if (Changed("copyright")) edits.Copyright = TextEdit("copyright");
            if (Changed("software")) edits.Software = TextEdit("software");
            if (Changed("description")) edits.Description = TextEdit("description");
            if (Changed("orientation")) edits.Orientation = now["orientation"].Length > 0 ? Edit<double>.To(JsText.ToNumber(now["orientation"])) : Edit<double>.Remove;

            if (Changed("dateTime"))
            {
                if (JsText.Trim(now["dateTime"]).Length == 0) edits.DateTime = Edit<string>.Remove;
                else
                {
                    string dt = ParseDate(now["dateTime"]);
                    if (dt == null)
                    {
                        error = Fail("dateTime", "Date & time: use YYYY:MM:DD HH:MM:SS, like 2026:10:08 14:30:00.");
                        return edits;
                    }
                    edits.DateTime = Edit<string>.To(dt);
                }
            }
            if (Changed("aperture"))
            {
                if (JsText.Trim(now["aperture"]).Length == 0) edits.FNumber = Edit<double>.Remove;
                else
                {
                    double? f = Num("aperture");
                    if (f == null || !(f > 0) || f > 1000)
                    {
                        error = Fail("aperture", "Aperture: an f-number like 1.4 or 2.8.");
                        return edits;
                    }
                    edits.FNumber = Edit<double>.To(f.Value);
                }
            }
            if (Changed("shutter"))
            {
                if (JsText.Trim(now["shutter"]).Length == 0) edits.ExposureTime = Edit<(double, double)>.Remove;
                else
                {
                    var s = ParseShutter(now["shutter"]);
                    if (s == null)
                    {
                        error = Fail("shutter", "Shutter speed: a time in seconds like 1/250, 0.5 or 2.");
                        return edits;
                    }
                    edits.ExposureTime = Edit<(double, double)>.To(s.Value);
                }
            }
            if (Changed("iso"))
            {
                if (JsText.Trim(now["iso"]).Length == 0) edits.Iso = Edit<double>.Remove;
                else
                {
                    double? iso = Num("iso");
                    if (iso == null || iso != Math.Floor(iso.Value) || iso < 1 || iso > 65535)
                    {
                        error = Fail("iso", "ISO: a whole number from 1 to 65535.");
                        return edits;
                    }
                    edits.Iso = Edit<double>.To(iso.Value);
                }
            }
            if (Changed("focal"))
            {
                if (JsText.Trim(now["focal"]).Length == 0) edits.FocalLength = Edit<double>.Remove;
                else
                {
                    double? mm = Num("focal");
                    if (mm == null || !(mm > 0) || mm > 10000)
                    {
                        error = Fail("focal", "Focal length: millimetres, like 28 or 4.25.");
                        return edits;
                    }
                    edits.FocalLength = Edit<double>.To(mm.Value);
                }
            }
            if (Changed("lat") || Changed("lon") || Changed("alt"))
            {
                bool hasLat = JsText.Trim(now["lat"]).Length > 0, hasLon = JsText.Trim(now["lon"]).Length > 0;
                if (!hasLat && !hasLon)
                {
                    if (JsText.Trim(now["alt"]).Length > 0)
                    {
                        error = Fail("alt", "Altitude needs a latitude and longitude too, or clear it.");
                        return edits;
                    }
                    edits.Gps = Edit<(double, double, double?)>.Remove;
                }
                else if (!hasLat || !hasLon)
                {
                    error = Fail(hasLat ? "lon" : "lat", "GPS: fill in both latitude and longitude, or clear both.");
                    return edits;
                }
                else
                {
                    double? lat = Num("lat"), lon = Num("lon");
                    if (lat == null || lat < -90 || lat > 90)
                    {
                        error = Fail("lat", "Latitude: a number from -90 to 90.");
                        return edits;
                    }
                    if (lon == null || lon < -180 || lon > 180)
                    {
                        error = Fail("lon", "Longitude: a number from -180 to 180.");
                        return edits;
                    }
                    double? altitude = null;
                    if (JsText.Trim(now["alt"]).Length > 0)
                    {
                        double? a = Num("alt");
                        if (a == null || a < -11000 || a > 100000)
                        {
                            error = Fail("alt", "Altitude: metres, like 182 or -5.");
                            return edits;
                        }
                        altitude = a;
                    }
                    edits.Gps = Edit<(double, double, double?)>.To((lat.Value, lon.Value, altitude));
                }
            }
            return edits;
        }

        static readonly Regex xmpLocation = new Regex("GPSLatitude|GPSLongitude", RegexOptions.CultureInvariant),
            xmpDate = new Regex("CreateDate|DateTimeOriginal|DateCreated|ModifyDate", RegexOptions.CultureInvariant),
            xmpCamera = new Regex("tiff:Make|tiff:Model|LensModel|aux:Lens|SerialNumber", RegexOptions.CultureInvariant),
            xmpCreator = new Regex("dc:creator|dc:rights|Iptc4xmpCore:Creator", RegexOptions.CultureInvariant);

        // what an XMP block holds its own copy of, in words ("location", "date" …)
        public static List<string> XmpCopies(string xmp)
        {
            var found = new List<string>();
            if (xmpLocation.IsMatch(xmp)) found.Add("location");
            if (xmpDate.IsMatch(xmp)) found.Add("date");
            if (xmpCamera.IsMatch(xmp)) found.Add("camera");
            if (xmpCreator.IsMatch(xmp)) found.Add("creator");
            return found;
        }
    }
}
