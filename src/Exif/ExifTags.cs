using System;
using System.Collections.Generic;
using System.Linq;
using B0r3dDeRes.Engine;

namespace B0r3dDeRes.Exif
{
    // A tag's value as the inspector reads it (the website's TagValue: text, one number, several
    // numbers with rationals divided out and signed types signed, the bytes of an UNDEFINED tag,
    // or nothing). Long lists come in cut short; the tag's count says how many it really holds.
    sealed class TagValue
    {
        public enum Kinds { None, Text, Number, Numbers, Bytes }

        public readonly Kinds Kind;
        public readonly string Text;
        public readonly double Number;
        public readonly double[] Numbers;
        public readonly byte[] Bytes;

        TagValue(Kinds kind, string text = null, double number = 0, double[] numbers = null, byte[] bytes = null)
        {
            Kind = kind;
            Text = text;
            Number = number;
            Numbers = numbers;
            Bytes = bytes;
        }

        public static readonly TagValue None = new TagValue(Kinds.None);
        public static TagValue Of(string text) => new TagValue(Kinds.Text, text: text);
        public static TagValue Of(double number) => new TagValue(Kinds.Number, number: number);
        public static TagValue Of(double[] numbers) => new TagValue(Kinds.Numbers, numbers: numbers);
        public static TagValue OfBytes(byte[] bytes) => new TagValue(Kinds.Bytes, bytes: bytes);
    }

    // The website's exif-dictionary.ts: tag names (TIFF 6.0, Exif 2.32 and the Windows XP tags) and
    // how each value is shown in the inspector's All Tags list.
    static class ExifTags
    {
        public const string Ifd0 = "IFD0", Exif = "Exif", Gps = "GPS", Interop = "Interop", Ifd1 = "IFD1";

        sealed class Def
        {
            public readonly string Name;
            public readonly Func<TagValue, double, string> Format;

            public Def(string name, Func<TagValue, double, string> format = null)
            {
                Name = name;
                Format = format;
            }
        }

        // a number without float noise: 72, 0.3333, 41.2
        static string Num(double n)
        {
            if (double.IsNaN(n) || double.IsInfinity(n)) return JsText.Num(n);
            return n == Math.Floor(n) ? JsText.Num(n) : JsText.Num(JsText.FixedNumber(n, 4));
        }

        static double First(TagValue v)
        {
            switch (v.Kind)
            {
                case TagValue.Kinds.Numbers: return v.Numbers.Length > 0 ? v.Numbers[0] : double.NaN;
                case TagValue.Kinds.Bytes: return v.Bytes.Length > 0 ? v.Bytes[0] : double.NaN;
                case TagValue.Kinds.Number: return v.Number;
                case TagValue.Kinds.Text: return JsText.ToNumber(v.Text);
                default: return double.NaN;
            }
        }

        // a number into a byte, as a Uint8Array takes it
        internal static byte ToUint8(double x)
        {
            if (double.IsNaN(x) || double.IsInfinity(x)) return 0;
            double t = Math.Truncate(x) % 256;
            if (t < 0) t += 256;
            return (byte)t;
        }

        static byte[] BytesOf(TagValue v)
        {
            switch (v.Kind)
            {
                case TagValue.Kinds.Bytes: return v.Bytes;
                case TagValue.Kinds.Numbers: return v.Numbers.Select(ToUint8).ToArray();
                case TagValue.Kinds.Number: return new[] { ToUint8(v.Number) };
                default: return new byte[0];
            }
        }

        // JavaScript's ToInt32 (for `x & 1`)
        static int ToInt32(double x)
        {
            if (double.IsNaN(x) || double.IsInfinity(x)) return 0;
            double t = Math.Truncate(x) % 4294967296.0;
            if (t < 0) t += 4294967296.0;
            return unchecked((int)(uint)t);
        }

        static Func<TagValue, double, string> EnumOf(params (int Key, string Text)[] map) => (v, count) =>
        {
            double n = First(v);
            foreach (var m in map)
                if (n == m.Key) return m.Text;
            return JsText.Num(n);
        };

        // seconds as a camera shows them: 1/250s, 0.8s, 2s
        public static string FormatExposure(double seconds)
        {
            if (!(seconds > 0)) return JsText.Num(seconds);
            if (seconds < 1)
            {
                double inv = 1 / seconds;
                // 1/3, 1/250 ... but 0.8s stays 0.8s rather than becoming "1/1s"
                if (Math.Abs(inv - JsMath.Round(inv)) < 0.01 * inv) return "1/" + JsText.Num(JsMath.Round(inv)) + "s";
                return Num(JsText.FixedNumber(seconds, 3)) + "s";
            }
            return Num(JsText.FixedNumber(seconds, 1)) + "s";
        }

        // degrees, minutes, seconds: 41° 52' 41.21"
        static string Dms(TagValue v, double count)
        {
            if (v.Kind != TagValue.Kinds.Numbers || v.Numbers.Length < 3) return Generic(v, 1);
            return Num(v.Numbers[0]) + "° " + Num(v.Numbers[1]) + "' " + Num(JsText.FixedNumber(v.Numbers[2], 2)) + "\"";
        }

        // Exif version bytes "0232" -> 2.32
        static string Version(TagValue v, double count)
        {
            byte[] b = BytesOf(v);
            string text = JsText.Chars(b, 0, Math.Min(4, b.Length));
            if (text.Length != 4 || !text.All(c => c >= '0' && c <= '9')) return Generic(v, b.Length);
            return JsText.Num(JsText.ToNumber(text.Substring(0, 2))) + "." + text.Substring(2);
        }

        // UTF-16 text, guessing the byte order when the file doesn't say
        static string Utf16(byte[] bytes, bool? littleEndian)
        {
            bool le;
            if (littleEndian.HasValue) le = littleEndian.Value;
            else
            {
                // mostly-Latin text has a zero in every other byte; which half holds the zeros gives the byte order
                int evenZeros = 0, oddZeros = 0;
                for (int i = 0; i + 1 < bytes.Length; i += 2)
                {
                    if (bytes[i] == 0) evenZeros++;
                    if (bytes[i + 1] == 0) oddZeros++;
                }
                le = oddZeros >= evenZeros;
            }
            int n = bytes.Length - bytes.Length % 2;
            return JsText.NoTrailingZeros(JsText.Utf16(bytes, 0, n, le));
        }

        // UserComment and GPSProcessingMethod: 8 bytes naming the character set, then the text
        public static string DecodeCharsetText(TagValue v)
        {
            byte[] b = BytesOf(v);
            if (b.Length < 8) return Printable(b) ?? b.Length + " bytes";
            string head = JsText.NoTrailingZeros(JsText.Chars(b, 0, 8));
            var body = new byte[b.Length - 8];
            Array.Copy(b, 8, body, 0, body.Length);
            if (head == "UNICODE") return JsText.Trim(Utf16(body, null));
            if (head == "JIS") return body.Length + " bytes of JIS text";
            // ASCII, or the all-zero "undefined" set that cameras fill with spaces
            return JsText.Trim(JsText.NoTrailingZeros(JsText.Utf8(body)));
        }

        // short runs of plain text inside UNDEFINED bytes, else null
        static string Printable(byte[] b)
        {
            int end = Array.IndexOf(b, (byte)0);
            if (end < 0) end = b.Length;
            if (end == 0 || end > 64) return null;
            for (int i = 0; i < end; i++)
                if (b[i] < 0x20 || b[i] > 0x7e) return null;
            for (int i = end; i < b.Length; i++)
                if (b[i] != 0) return null;
            return JsText.Chars(b, 0, end);
        }

        // any value, when the tag has no formatter of its own
        static string Generic(TagValue v, double count)
        {
            switch (v.Kind)
            {
                case TagValue.Kinds.None: return "";
                case TagValue.Kinds.Text: return JsText.Trim(v.Text);
                case TagValue.Kinds.Number: return Num(v.Number);
                case TagValue.Kinds.Bytes: return Printable(v.Bytes) ?? JsText.Num(count) + " bytes";
                default:
                    string shown = string.Join(", ", v.Numbers.Take(16).Select(Num));
                    return count > 16 ? shown + " … (" + JsText.Num(count) + " values)" : shown;
            }
        }

        // Windows XP tags: UTF-16LE text in BYTE values
        static string XpText(TagValue v, double count) => Utf16(BytesOf(v), true);

        static Func<TagValue, double, string> Pointer(string what) => (v, count) => what + " at byte " + JsText.Num(First(v));

        static Func<TagValue, double, string> Counted(string unit) => (v, count) => JsText.Num(count) + " " + unit;

        static string Fixed2(double x) => Num(JsText.FixedNumber(x, 2));

        static readonly Dictionary<int, Def> ifd0 = new Dictionary<int, Def>
        {
            [0x00fe] = new Def("NewSubfileType"),
            [0x00ff] = new Def("SubfileType"),
            [0x0100] = new Def("ImageWidth"),
            [0x0101] = new Def("ImageLength"),
            [0x0102] = new Def("BitsPerSample"),
            [0x0103] = new Def("Compression"),
            [0x0106] = new Def("PhotometricInterpretation"),
            [0x010e] = new Def("ImageDescription"),
            [0x010f] = new Def("Make"),
            [0x0110] = new Def("Model"),
            [0x0111] = new Def("StripOffsets"),
            [0x0112] = new Def("Orientation", (v, count) =>
            {
                double n = First(v);
                string text = null;
                if (n == 1) text = "Horizontal (normal)";
                else if (n == 2) text = "Mirror horizontal";
                else if (n == 3) text = "Rotate 180°";
                else if (n == 4) text = "Mirror vertical";
                else if (n == 5) text = "Mirror horizontal and rotate 270° CW";
                else if (n == 6) text = "Rotate 90° CW";
                else if (n == 7) text = "Mirror horizontal and rotate 90° CW";
                else if (n == 8) text = "Rotate 270° CW";
                return text != null ? JsText.Num(n) + " (" + text + ")" : JsText.Num(n);
            }),
            [0x0115] = new Def("SamplesPerPixel"),
            [0x0116] = new Def("RowsPerStrip"),
            [0x0117] = new Def("StripByteCounts"),
            [0x011a] = new Def("XResolution"),
            [0x011b] = new Def("YResolution"),
            [0x011c] = new Def("PlanarConfiguration"),
            [0x0128] = new Def("ResolutionUnit", EnumOf((1, "None"), (2, "Inches"), (3, "Centimeters"))),
            [0x0131] = new Def("Software"),
            [0x0132] = new Def("DateTime"),
            [0x013b] = new Def("Artist"),
            [0x013e] = new Def("WhitePoint"),
            [0x013f] = new Def("PrimaryChromaticities"),
            [0x0211] = new Def("YCbCrCoefficients"),
            [0x0212] = new Def("YCbCrSubSampling"),
            [0x0213] = new Def("YCbCrPositioning", EnumOf((1, "Centered"), (2, "Co-sited"))),
            [0x0214] = new Def("ReferenceBlackWhite"),
            [0x02bc] = new Def("XMP (in EXIF)", Counted("bytes")),
            [0x4746] = new Def("Rating"),
            [0x4749] = new Def("RatingPercent"),
            [0x8298] = new Def("Copyright"),
            [0x83bb] = new Def("IPTC (in EXIF)", Counted("values")),
            [0x8769] = new Def("ExifIFDPointer", Pointer("Exif block")),
            [0x8773] = new Def("ICC Profile (in EXIF)", Counted("bytes")),
            [0x8825] = new Def("GPSInfoIFDPointer", Pointer("GPS block")),
            [0x9c9b] = new Def("XPTitle", XpText),
            [0x9c9c] = new Def("XPComment", XpText),
            [0x9c9d] = new Def("XPAuthor", XpText),
            [0x9c9e] = new Def("XPKeywords", XpText),
            [0x9c9f] = new Def("XPSubject", XpText),
            [0xc4a5] = new Def("PrintIM", Counted("bytes")),
            [0xea1c] = new Def("Padding", Counted("bytes")),
        };

        static readonly Dictionary<int, Def> exif = new Dictionary<int, Def>
        {
            [0x829a] = new Def("ExposureTime", (v, count) => FormatExposure(First(v))),
            [0x829d] = new Def("FNumber", (v, count) => "f/" + Fixed2(First(v))),
            [0x8822] = new Def("ExposureProgram", EnumOf((0, "Not defined"), (1, "Manual"), (2, "Normal program"), (3, "Aperture priority"), (4, "Shutter priority"),
                (5, "Creative program"), (6, "Action program"), (7, "Portrait mode"), (8, "Landscape mode"))),
            [0x8827] = new Def("ISOSpeedRatings", (v, count) => "ISO " + JsText.Num(First(v))),
            [0x8830] = new Def("SensitivityType"),
            [0x8832] = new Def("RecommendedExposureIndex"),
            [0x8833] = new Def("ISOSpeed"),
            [0x9000] = new Def("ExifVersion", Version),
            [0x9003] = new Def("DateTimeOriginal"),
            [0x9004] = new Def("DateTimeDigitized"),
            [0x9010] = new Def("OffsetTime"),
            [0x9011] = new Def("OffsetTimeOriginal"),
            [0x9012] = new Def("OffsetTimeDigitized"),
            [0x9101] = new Def("ComponentsConfiguration", (v, count) =>
            {
                string[] names = { "-", "Y", "Cb", "Cr", "R", "G", "B" };
                byte[] b = BytesOf(v);
                return string.Join(", ", b.Take(4).Select(c => c < names.Length ? names[c] : JsText.Num(c)));
            }),
            [0x9102] = new Def("CompressedBitsPerPixel"),
            // APEX values: shutter Tv = -log2(seconds), aperture Av = 2·log2(f-number)
            [0x9201] = new Def("ShutterSpeedValue", (v, count) => FormatExposure(Math.Pow(2, -First(v)))),
            [0x9202] = new Def("ApertureValue", (v, count) => "f/" + JsText.Fixed(Math.Pow(Math.Sqrt(2), First(v)), 1)),
            [0x9203] = new Def("BrightnessValue", (v, count) => Num(First(v)) + " EV"),
            [0x9204] = new Def("ExposureBiasValue", (v, count) =>
            {
                double n = First(v);
                return (n > 0 ? "+" : "") + Fixed2(n) + " EV";
            }),
            [0x9205] = new Def("MaxApertureValue", (v, count) => "f/" + JsText.Fixed(Math.Pow(Math.Sqrt(2), First(v)), 1)),
            [0x9206] = new Def("SubjectDistance", (v, count) => Fixed2(First(v)) + " m"),
            [0x9207] = new Def("MeteringMode", EnumOf((0, "Unknown"), (1, "Average"), (2, "Center-weighted average"), (3, "Spot"), (4, "Multi-spot"),
                (5, "Multi-segment / Pattern"), (6, "Partial"), (255, "Other"))),
            [0x9208] = new Def("LightSource"),
            [0x9209] = new Def("Flash", (v, count) => (ToInt32(First(v)) & 1) == 1 ? "Flash fired" : "Flash did not fire"),
            [0x920a] = new Def("FocalLength", (v, count) => Fixed2(First(v)) + " mm"),
            [0x9214] = new Def("SubjectArea"),
            [0x927c] = new Def("MakerNote", (v, count) => JsText.Num(count) + " bytes (the camera maker's own data)"),
            [0x9286] = new Def("UserComment", (v, count) => DecodeCharsetText(v)),
            [0x9290] = new Def("SubSecTime"),
            [0x9291] = new Def("SubSecTimeOriginal"),
            [0x9292] = new Def("SubSecTimeDigitized"),
            [0xa000] = new Def("FlashpixVersion", Version),
            [0xa001] = new Def("ColorSpace", EnumOf((1, "sRGB"), (65535, "Uncalibrated"))),
            [0xa002] = new Def("PixelXDimension", (v, count) => JsText.Num(First(v)) + " px"),
            [0xa003] = new Def("PixelYDimension", (v, count) => JsText.Num(First(v)) + " px"),
            [0xa005] = new Def("InteroperabilityIFDPointer", Pointer("Interop block")),
            [0xa20e] = new Def("FocalPlaneXResolution"),
            [0xa20f] = new Def("FocalPlaneYResolution"),
            [0xa210] = new Def("FocalPlaneResolutionUnit", EnumOf((2, "Inches"), (3, "Centimeters"))),
            [0xa217] = new Def("SensingMethod"),
            [0xa300] = new Def("FileSource", EnumOf((1, "Film scanner"), (2, "Reflection print scanner"), (3, "Digital camera"))),
            [0xa301] = new Def("SceneType", EnumOf((1, "Directly photographed"))),
            [0xa302] = new Def("CFAPattern"),
            [0xa401] = new Def("CustomRendered"),
            [0xa402] = new Def("ExposureMode", EnumOf((0, "Auto"), (1, "Manual"), (2, "Auto bracket"))),
            [0xa403] = new Def("WhiteBalance", EnumOf((0, "Auto"), (1, "Manual"))),
            [0xa404] = new Def("DigitalZoomRatio"),
            [0xa405] = new Def("FocalLengthIn35mmFilm", (v, count) => JsText.Num(First(v)) + " mm"),
            [0xa406] = new Def("SceneCaptureType", EnumOf((0, "Standard"), (1, "Landscape"), (2, "Portrait"), (3, "Night"))),
            [0xa407] = new Def("GainControl"),
            [0xa408] = new Def("Contrast"),
            [0xa409] = new Def("Saturation"),
            [0xa40a] = new Def("Sharpness"),
            [0xa420] = new Def("ImageUniqueID"),
            [0xa430] = new Def("CameraOwnerName"),
            [0xa431] = new Def("BodySerialNumber"),
            [0xa432] = new Def("LensSpecification", (v, count) =>
            {
                if (v.Kind != TagValue.Kinds.Numbers || v.Numbers.Length < 4) return Generic(v, 1);
                double[] x = v.Numbers;
                // 0 means "unknown" here (the file stores 0/0)
                string Part(double a, double b, Func<string, string> unit) =>
                    !Truthy(a) && !Truthy(b) ? "" : a == b || !Truthy(b) ? unit(Num(a)) : unit(Num(a) + "–" + Num(b));
                string focal = Part(x[0], x[1], n => n + " mm");
                string f = Part(x[2], x[3], n => "f/" + n);
                string both = string.Join(" ", new[] { focal, f }.Where(s => s.Length > 0));
                return both.Length > 0 ? both : Generic(v, 4);
            }),
            [0xa433] = new Def("LensMake"),
            [0xa434] = new Def("LensModel"),
            [0xa435] = new Def("LensSerialNumber"),
            [0xa460] = new Def("CompositeImage"),
            [0xea1c] = new Def("Padding", Counted("bytes")),
            [0xea1d] = new Def("OffsetSchema"),
        };

        static bool Truthy(double x) => x != 0 && !double.IsNaN(x);

        static readonly Dictionary<int, Def> gps = new Dictionary<int, Def>
        {
            [0x0000] = new Def("GPSVersionID", (v, count) => string.Join(".", BytesOf(v).Select(b => b.ToString()))),
            [0x0001] = new Def("GPSLatitudeRef"),
            [0x0002] = new Def("GPSLatitude", Dms),
            [0x0003] = new Def("GPSLongitudeRef"),
            [0x0004] = new Def("GPSLongitude", Dms),
            [0x0005] = new Def("GPSAltitudeRef", (v, count) => First(v) == 1 ? "Below sea level" : "Above sea level"),
            [0x0006] = new Def("GPSAltitude", (v, count) => JsText.Fixed(First(v), 1) + " m"),
            [0x0007] = new Def("GPSTimeStamp", (v, count) =>
            {
                if (v.Kind != TagValue.Kinds.Numbers || v.Numbers.Length < 3) return Generic(v, 1);
                string Pad(double n) => JsText.Pad(JsText.Num(Math.Floor(n)), 2);
                return Pad(v.Numbers[0]) + ":" + Pad(v.Numbers[1]) + ":" + Pad(v.Numbers[2]) + " UTC";
            }),
            [0x0008] = new Def("GPSSatellites"),
            [0x0009] = new Def("GPSStatus"),
            [0x000a] = new Def("GPSMeasureMode"),
            [0x000b] = new Def("GPSDOP"),
            [0x000c] = new Def("GPSSpeedRef"),
            [0x000d] = new Def("GPSSpeed"),
            [0x000e] = new Def("GPSTrackRef"),
            [0x000f] = new Def("GPSTrack", (v, count) => Num(First(v)) + "°"),
            [0x0010] = new Def("GPSImgDirectionRef"),
            [0x0011] = new Def("GPSImgDirection", (v, count) => Num(First(v)) + "°"),
            [0x0012] = new Def("GPSMapDatum"),
            [0x0013] = new Def("GPSDestLatitudeRef"),
            [0x0014] = new Def("GPSDestLatitude", Dms),
            [0x0015] = new Def("GPSDestLongitudeRef"),
            [0x0016] = new Def("GPSDestLongitude", Dms),
            [0x0017] = new Def("GPSDestBearingRef"),
            [0x0018] = new Def("GPSDestBearing", (v, count) => Num(First(v)) + "°"),
            [0x0019] = new Def("GPSDestDistanceRef"),
            [0x001a] = new Def("GPSDestDistance"),
            [0x001b] = new Def("GPSProcessingMethod", (v, count) => DecodeCharsetText(v)),
            [0x001c] = new Def("GPSAreaInformation", (v, count) => DecodeCharsetText(v)),
            [0x001d] = new Def("GPSDateStamp"),
            [0x001e] = new Def("GPSDifferential"),
            [0x001f] = new Def("GPSHPositioningError", (v, count) => Num(First(v)) + " m"),
        };

        static readonly Dictionary<int, Def> interop = new Dictionary<int, Def>
        {
            [0x0001] = new Def("InteroperabilityIndex"),
            [0x0002] = new Def("InteroperabilityVersion", Version),
        };

        static readonly Dictionary<int, Def> ifd1 = new Dictionary<int, Def>
        {
            [0x0100] = new Def("ThumbnailImageWidth"),
            [0x0101] = new Def("ThumbnailImageHeight"),
            [0x0103] = new Def("ThumbnailCompression", EnumOf((1, "Uncompressed"), (6, "JPEG"))),
            [0x011a] = new Def("XResolution"),
            [0x011b] = new Def("YResolution"),
            [0x0128] = new Def("ResolutionUnit", EnumOf((1, "None"), (2, "Inches"), (3, "Centimeters"))),
            [0x0201] = new Def("JPEGInterchangeFormat", Pointer("Thumbnail")),
            [0x0202] = new Def("JPEGInterchangeFormatLength", (v, count) => JsText.Num(First(v)) + " bytes"),
        };

        static Dictionary<int, Def> DictFor(string category)
        {
            switch (category)
            {
                case Ifd0: return ifd0;
                case Exif: return exif;
                case Gps: return gps;
                case Interop: return interop;
                default: return ifd1;
            }
        }

        // a tag's name in its block, or "Unknown Tag (0x1234)"
        public static string Name(string category, int tagId) =>
            DictFor(category).TryGetValue(tagId, out Def def) && def.Name.Length > 0 ? def.Name : "Unknown Tag (0x" + JsText.Hex(tagId, 4) + ")";

        // long text is cut here in the table (the filter still finds what's shown)
        const int MaxShownChars = 500;

        // a tag's value as the list shows it
        public static string Format(string category, int tagId, TagValue value, double count)
        {
            DictFor(category).TryGetValue(tagId, out Def def);
            string text;
            try
            {
                text = def?.Format != null ? def.Format(value, count) : Generic(value, count);
            }
            catch (Exception x) when (x is ArgumentException || x is IndexOutOfRangeException || x is OverflowException)
            {
                text = Generic(value, count);
            }
            return text.Length > MaxShownChars ? text.Substring(0, MaxShownChars) + "…" : text;
        }
    }
}
