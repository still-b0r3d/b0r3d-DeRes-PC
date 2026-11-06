using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using B0r3dDeRes.Engine;

namespace B0r3dDeRes.Exif
{
    sealed class ScrubOptions
    {
        // "all": every bit of metadata (camera, GPS, comments, trailers); "location_only": GPS and
        // trailers, the camera and settings kept
        public string Mode = "all";
        // keep the orientation tag, so phone photos stay upright in viewers
        public bool PreserveOrientation = true;

        public bool LocationOnly => Mode == "location_only";
    }

    // the Verified Clean check of a cleaned file
    sealed class SelfCheckReport
    {
        public string Format;
        public long OriginalSize, ScrubbedSize, BytesSaved;
        public int TagsRemovedCount;
        public bool GpsRemoved, CameraRemoved, TrailersRemoved, OrientationPreserved, IccPreserved, IsClean;
        public List<string> Leftovers = new List<string>();   // what the check still found, in plain words
        public string Summary;
        // a video's (the PC's): the picture and sound read back byte for byte
        public bool? SamplesUntouched;
    }

    sealed class ScrubResult
    {
        public byte[] Data;
        public string Format;
        public SelfCheckReport Report;
    }

    // a scrub the website refuses (HEIC, AVIF, other kinds), with its words
    sealed class ScrubException : Exception
    {
        public ScrubException(string message) : base(message) { }
    }

    // The website's scrub.ts: metadata taken out byte by byte, never through a picture encoder, so
    // the pixels are exactly the same. JPEG: Exif, XMP, Photoshop/IPTC, comments and MPF go, the
    // ICC profile stays, nothing after the end marker; the orientation is kept in a 36-byte APP1 of
    // its own. PNG: eXIf, text and tIME go, the rest stays, nothing after IEND. WebP: EXIF, XMP
    // and C2PA go, the VP8X flags say so, the RIFF size fits. GIF: comments, XMP, plain text and
    // application blocks go; colour tables, the loop block, frame timing, image data and the
    // colour profile stay byte for byte.
    static class Scrubber
    {
        // a TIFF block of 26 bytes holding only the orientation (little-endian)
        public static byte[] MinimalOrientationTiff(int orientation)
        {
            var t = new byte[26];
            t[0] = 0x49; t[1] = 0x49; t[2] = 0x2a; t[3] = 0x00;
            t[4] = 0x08;
            t[8] = 0x01;
            t[10] = 0x12; t[11] = 0x01;
            t[12] = 0x03;
            t[14] = 0x01;
            t[18] = (byte)(orientation & 0xff);
            t[19] = (byte)((orientation >> 8) & 0xff);
            return t;
        }

        // a 36-byte JPEG APP1 holding only the orientation
        public static byte[] MinimalOrientationApp1(int orientation)
        {
            var seg = new byte[36];
            seg[0] = 0xff; seg[1] = 0xe1; seg[2] = 0x00; seg[3] = 0x22;
            seg[4] = 0x45; seg[5] = 0x78; seg[6] = 0x69; seg[7] = 0x66;
            Array.Copy(MinimalOrientationTiff(orientation), 0, seg, 10, 26);
            return seg;
        }

        // a PNG eXIf chunk holding only the orientation
        public static byte[] MinimalOrientationPngChunk(int orientation)
        {
            byte[] tiff = MinimalOrientationTiff(orientation);
            var chunk = new byte[12 + tiff.Length];
            chunk[3] = (byte)tiff.Length;
            chunk[4] = 0x65; chunk[5] = 0x58; chunk[6] = 0x49; chunk[7] = 0x66;
            Array.Copy(tiff, 0, chunk, 8, tiff.Length);
            SetCrc(chunk, 4, 4 + tiff.Length, 8 + tiff.Length);
            return chunk;
        }

        // the CRC-32 of chunk[from, from + count) written big-endian at `at`
        static void SetCrc(byte[] chunk, int from, int count, int at)
        {
            uint crc = Png.Crc(0xffffffff, chunk, from, count) ^ 0xffffffff;
            chunk[at] = (byte)(crc >> 24);
            chunk[at + 1] = (byte)(crc >> 16);
            chunk[at + 2] = (byte)(crc >> 8);
            chunk[at + 3] = (byte)crc;
        }

        // the GPS block of a TIFF block zeroed where it lies, and its pointer taken out of IFD0;
        // the camera and orientation stay
        static void WipeGpsFromTiff(byte[] data, int tiffStart)
        {
            if (tiffStart + 8 > data.Length) return;
            bool isLe = data[tiffStart] == 0x49 && data[tiffStart + 1] == 0x49;
            bool isBe = data[tiffStart] == 0x4d && data[tiffStart + 1] == 0x4d;
            if (!isLe && !isBe) return;
            long U16(long off)
            {
                long p = tiffStart + off;
                if (p + 2 > data.Length) return 0;
                return isLe ? data[p] | (data[p + 1] << 8) : (data[p] << 8) | data[p + 1];
            }
            long U32(long off)
            {
                long p = tiffStart + off;
                if (p + 4 > data.Length) return 0;
                return isLe
                    ? (uint)(data[p] | (data[p + 1] << 8) | (data[p + 2] << 16) | (data[p + 3] << 24))
                    : (uint)((data[p] << 24) | (data[p + 1] << 16) | (data[p + 2] << 8) | data[p + 3]);
            }
            void Fill(long from, long to)
            {
                for (long i = Math.Max(0, from); i < Math.Min(to, data.Length); i++) data[i] = 0;
            }

            long ifd0Offset = U32(4);
            if (ifd0Offset == 0 || tiffStart + ifd0Offset + 2 > data.Length) return;
            long numEntries = U16(ifd0Offset);
            long gpsOffset = 0, gpsEntryOffset = 0;
            for (long i = 0; i < numEntries; i++)
            {
                long entryOff = ifd0Offset + 2 + i * 12;
                if (tiffStart + entryOff + 12 > data.Length) break;
                if (U16(entryOff) == 0x8825)
                {
                    gpsOffset = U32(entryOff + 8);
                    gpsEntryOffset = entryOff;
                    break;
                }
            }
            if (gpsOffset > 0 && tiffStart + gpsOffset + 2 <= data.Length)
            {
                long gpsEntries = U16(gpsOffset);
                int[] typeSizes = { 0, 1, 1, 2, 4, 8, 1, 1, 2, 4, 8 };
                for (long i = 0; i < gpsEntries; i++)
                {
                    long gEntry = gpsOffset + 2 + i * 12;
                    if (tiffStart + gEntry + 12 > data.Length) break;
                    long gType = U16(gEntry + 2), gCount = U32(gEntry + 4);
                    int typeSize = gType < typeSizes.Length ? typeSizes[gType] : 0;
                    long size = (typeSize == 0 ? 1 : typeSize) * gCount;
                    if (size > 4)
                    {
                        long p = tiffStart + U32(gEntry + 8);
                        long len = Math.Min(size, data.Length - p);
                        if (p >= 0 && len > 0) Fill(p, p + len);
                    }
                }
                // the GPS block's own table
                long dirLen = Math.Min(2 + gpsEntries * 12 + 4, data.Length - (tiffStart + gpsOffset));
                if (dirLen > 0) Fill(tiffStart + gpsOffset, tiffStart + gpsOffset + dirLen);
            }
            // the 0x8825 entry out of IFD0: the later entries and the next-IFD link move up one,
            // the count goes down, the freed space is zeroed
            if (gpsEntryOffset > 0)
            {
                long entryAt = tiffStart + gpsEntryOffset;
                long linkEnd = tiffStart + ifd0Offset + 2 + numEntries * 12 + 4;
                if (linkEnd <= data.Length)
                {
                    Array.Copy(data, entryAt + 12, data, entryAt, linkEnd - (entryAt + 12));
                    Fill(linkEnd - 12, linkEnd);
                    long n = numEntries - 1;
                    long countAt = tiffStart + ifd0Offset;
                    data[countAt] = (byte)(isLe ? n & 0xff : (n >> 8) & 0xff);
                    data[countAt + 1] = (byte)(isLe ? (n >> 8) & 0xff : n & 0xff);
                }
                else Fill(entryAt, Math.Min(entryAt + 12, data.Length));
            }
        }

        // where a JPEG's first scan starts, or its length if it has none
        static int JpegFirstScan(byte[] data)
        {
            int pos = 2;
            while (pos + 4 <= data.Length)
            {
                if (data[pos] != 0xff) return data.Length;
                int marker = data[pos + 1];
                if (marker == 0xff)
                {
                    pos++;
                    continue;
                }
                if (marker == 0xda) return pos;
                if (marker == 0xd8 || (marker >= 0xd0 && marker <= 0xd7))
                {
                    pos += 2;
                    continue;
                }
                int len = (data[pos + 2] << 8) | data[pos + 3];
                if (len < 2) return data.Length;
                pos += 2 + len;
            }
            return data.Length;
        }

        static bool KeepsOrientation(ScrubOptions o, ImageMetadata meta) =>
            o.Mode == "all" && o.PreserveOrientation && meta.Orientation.HasValue && meta.Orientation > 1 && meta.Orientation <= 8 && !double.IsNaN(meta.Orientation.Value);

        // (the orientation as the tag holds it: a whole number from 2 to 8, or a fraction cut down)
        static int OrientationOf(ImageMetadata meta) => (int)Math.Truncate(meta.Orientation.Value);

        // a JPEG's metadata out, byte by byte
        public static byte[] ScrubJpeg(byte[] data, ScrubOptions options = null)
        {
            if (data.Length < 4 || data[0] != 0xff || data[1] != 0xd8) throw new ScrubException("Not a valid JPEG image: missing SOI marker");
            options = options ?? new ScrubOptions();
            bool all = options.Mode != "location_only";
            // the orientation kept is the one viewers act on: metadata before the picture data
            ImageMetadata originalMeta = ExifReader.Inspect(new Bytes(data, JpegFirstScan(data)));
            var output = new MemoryStream(data.Length);
            void Append(byte[] bytes, int start, int count) => output.Write(bytes, start, count);

            Append(data, 0, 2);
            // a minimal APP1 where viewers look for it: after a leading JFIF APP0, or right after SOI
            bool orientationInjected = false;
            void InjectOrientation()
            {
                if (!orientationInjected && KeepsOrientation(options, originalMeta))
                {
                    byte[] seg = MinimalOrientationApp1(OrientationOf(originalMeta));
                    Append(seg, 0, seg.Length);
                    orientationInjected = true;
                }
            }
            bool leadingApp0 = data[2] == 0xff && data[3] == 0xe0;
            if (!leadingApp0) InjectOrientation();

            int pos = 2;
            bool inScan = false;
            while (pos < data.Length)
            {
                if (inScan)
                {
                    // the entropy-coded data to the next marker (a stuffed FF 00 and restart markers
                    // belong to it), then segments again: a progressive JPEG has more after this
                    int end = pos;
                    while (end < data.Length - 1)
                    {
                        int next = data[end + 1];
                        if (data[end] == 0xff && next != 0x00 && (next < 0xd0 || next > 0xd7)) break;
                        end++;
                    }
                    if (end >= data.Length - 1)
                    {
                        Append(data, pos, data.Length - pos);
                        break;
                    }
                    Append(data, pos, end - pos);
                    pos = end;
                    inScan = false;
                    continue;
                }
                if (data[pos] != 0xff)
                {
                    pos++;
                    continue;
                }
                int marker = pos + 1 < data.Length ? data[pos + 1] : -1;
                if (marker == 0xff || marker == 0x00)
                {
                    pos++;
                    continue;
                }
                if (marker == 0xd8)
                {
                    pos += 2;
                    continue;
                }
                if (marker == 0xd9)
                {
                    Append(data, pos, Math.Min(2, data.Length - pos));
                    break;
                }
                if (marker >= 0xd0 && marker <= 0xd7)
                {
                    Append(data, pos, Math.Min(2, data.Length - pos));
                    pos += 2;
                    continue;
                }
                if (pos + 4 > data.Length) break;
                int segLen = (data[pos + 2] << 8) | data[pos + 3];
                int segEnd = pos + 2 + segLen;
                if (segEnd > data.Length) break;
                if (marker == 0xda)
                {
                    Append(data, pos, segEnd - pos);
                    pos = segEnd;
                    inScan = true;
                    continue;
                }
                if (marker == 0xe0)
                {
                    Append(data, pos, segEnd - pos);
                    InjectOrientation();
                }
                else if (marker == 0xee) Append(data, pos, segEnd - pos);   // APP14 (Adobe)
                else if (marker == 0xe2)
                {
                    // APP2: kept for an ICC profile, dropped for MPF and anything else
                    if (segLen >= 14 && ExifReader.StartsWith(data, pos + 4, "ICC_PROFILE\0")) Append(data, pos, segEnd - pos);
                }
                else if (marker == 0xe1)
                {
                    // Exif or XMP: in location-only mode the Exif stays with its GPS wiped (XMP goes,
                    // it can hold the place too); in all mode every APP1 goes
                    if (!all && segLen >= 8 && data[pos + 4] == 0x45 && data[pos + 5] == 0x78 && data[pos + 6] == 0x69 && data[pos + 7] == 0x66 && data[pos + 8] == 0x00 && data[pos + 9] == 0x00)
                    {
                        var copy = new byte[segEnd - pos];
                        Array.Copy(data, pos, copy, 0, copy.Length);
                        WipeGpsFromTiff(copy, 10);
                        Append(copy, 0, copy.Length);
                    }
                }
                else if (marker == 0xed || marker == 0xfe || (marker >= 0xe3 && marker <= 0xef))
                {
                    // Photoshop/IPTC, comments, other APP segments: dropped
                }
                else Append(data, pos, segEnd - pos);   // DQT, DHT, SOF, DRI…: kept
                pos = segEnd;
            }
            return output.ToArray();
        }

        // a PNG's metadata out, byte by byte
        public static byte[] ScrubPng(byte[] data, ScrubOptions options = null)
        {
            if (data.Length < 8 || data[0] != 0x89 || data[1] != 0x50 || data[2] != 0x4e || data[3] != 0x47 || data[4] != 0x0d || data[5] != 0x0a || data[6] != 0x1a || data[7] != 0x0a)
                throw new ScrubException("Not a valid PNG image: signature mismatch");
            options = options ?? new ScrubOptions();
            bool all = options.Mode != "location_only";
            ImageMetadata originalMeta = ExifReader.Inspect(data);
            var output = new MemoryStream(data.Length);
            output.Write(data, 0, 8);
            bool orientationInjected = false;
            long pos = 8;
            while (pos + 8 <= data.Length)
            {
                long len = (uint)((data[pos] << 24) | (data[pos + 1] << 16) | (data[pos + 2] << 8) | data[pos + 3]);
                string type = JsText.Chars(data, (int)pos + 4, 4);
                long chunkEnd = pos + 8 + len + 4;
                if (chunkEnd > data.Length) break;
                int start = (int)pos, count = (int)(chunkEnd - pos);
                if (type == "IDAT")
                {
                    if (!orientationInjected && KeepsOrientation(options, originalMeta))
                    {
                        byte[] chunk = MinimalOrientationPngChunk(OrientationOf(originalMeta));
                        output.Write(chunk, 0, chunk.Length);
                        orientationInjected = true;
                    }
                    output.Write(data, start, count);
                }
                else if (type == "eXIf")
                {
                    if (!all)
                    {
                        var copy = new byte[count];
                        Array.Copy(data, start, copy, 0, count);
                        WipeGpsFromTiff(copy, 8);
                        SetCrc(copy, 4, 4 + (int)len, 8 + (int)len);
                        output.Write(copy, 0, count);
                    }
                }
                else if (type == "tEXt" || type == "zTXt" || type == "iTXt" || type == "tIME")
                {
                    // text and the time stamp: dropped
                }
                else if (type == "caBX")
                {
                    if (!all) output.Write(data, start, count);   // Content Credentials: dropped in all mode
                }
                else if (type == "IEND")
                {
                    output.Write(data, start, count);
                    break;   // anything after it is cut
                }
                else output.Write(data, start, count);   // IHDR, PLTE, iCCP, sRGB…: kept
                pos = chunkEnd;
            }
            return output.ToArray();
        }

        // a WebP's metadata out, byte by byte
        public static byte[] ScrubWebp(byte[] data)
        {
            if (data.Length < 12 || data[0] != 0x52 || data[1] != 0x49 || data[2] != 0x46 || data[3] != 0x46 || data[8] != 0x57 || data[9] != 0x45 || data[10] != 0x42 || data[11] != 0x50)
                throw new ScrubException("Not a valid WebP image: RIFF/WEBP header mismatch");
            var output = new MemoryStream(data.Length);
            output.Write(data, 0, 12);
            long pos = 12;
            // the RIFF end: anything after it is a trailer, and is cut
            long end = ExifReader.WebpRiffEnd(new Bytes(data));
            while (pos + 8 <= end)
            {
                string fourCC = JsText.Chars(data, (int)pos, 4);
                long size = (uint)(data[pos + 4] | (data[pos + 5] << 8) | (data[pos + 6] << 16) | (data[pos + 7] << 24));
                long chunkEnd = pos + 8 + size + size % 2;
                // a chunk's data must fit inside the RIFF; only its padding byte may be missing (and is put back)
                if (pos + 8 + size > end) break;
                byte[] chunk;
                if (chunkEnd <= data.Length)
                {
                    chunk = new byte[chunkEnd - pos];
                    Array.Copy(data, pos, chunk, 0, chunk.Length);
                }
                else
                {
                    chunk = new byte[8 + size + 1];
                    Array.Copy(data, pos, chunk, 0, 8 + size);
                }
                if (fourCC == "EXIF" || fourCC == "XMP " || fourCC == "C2PA")
                {
                    // dropped (as caBX in a PNG and APP11 in a JPEG)
                }
                else if (fourCC == "VP8X")
                {
                    // the flags no longer say EXIF (bit 3) or XMP (bit 2)
                    if (chunk.Length > 8) chunk[8] &= unchecked((byte)~(0x04 | 0x08));
                    output.Write(chunk, 0, chunk.Length);
                }
                else output.Write(chunk, 0, chunk.Length);
                pos = chunkEnd;
            }
            byte[] result = output.ToArray();
            uint riff = (uint)(result.Length - 8);
            result[4] = (byte)riff;
            result[5] = (byte)(riff >> 8);
            result[6] = (byte)(riff >> 16);
            result[7] = (byte)(riff >> 24);
            return result;
        }

        // A GIF's metadata out, byte by byte. Remove all keeps the header, colour tables, loop
        // block, graphic controls, image data and colour profile exactly, and drops comments, XMP,
        // plain text and every other application block; location only drops just the XMP that says
        // where. Anything after the end marker, or from damage on, is cut.
        public static byte[] ScrubGif(byte[] data, ScrubOptions options = null)
        {
            var b = new Bytes(data);
            if (ExifReader.DetectFormat(b) != "gif") throw new ScrubException("Not a valid GIF image: signature mismatch");
            bool all = (options?.Mode ?? "all") == "all";
            long n = data.Length;
            var output = new MemoryStream(data.Length);
            long p = 13;
            if ((data[10] & 0x80) != 0) p += 3 << ((data[10] & 7) + 1);
            output.Write(data, 0, (int)Math.Min(p, n));

            // the next drawn block after `at` (skipping other extensions): an image, plain text, or neither
            string NextDrawn(long at)
            {
                for (long q = at; q < n;)
                {
                    if (b.At(q) == 0x2c) return "image";
                    if (b.At(q) != 0x21) return null;
                    if (b.At(q + 1) == 0x01) return "text";
                    long end = ExifReader.GifSubBlocksEnd(b, q + 2);
                    if (end < 0) return null;
                    q = end;
                }
                return null;
            }

            while (p < n)
            {
                int block = data[p];
                if (block == 0x3b) break;
                if (block == 0x2c)
                {
                    if (p + 10 > n) break;
                    int flags = data[p + 9];
                    long q = p + 10;
                    if ((flags & 0x80) != 0) q += 3 << ((flags & 7) + 1);
                    long imageEnd = ExifReader.GifSubBlocksEnd(b, q + 1);
                    if (imageEnd < 0) break;
                    output.Write(data, (int)p, (int)(imageEnd - p));
                    p = imageEnd;
                    continue;
                }
                if (block != 0x21) break;
                int label = b[p + 1];
                long blockEnd = ExifReader.GifSubBlocksEnd(b, p + 2);
                if (blockEnd < 0) break;
                bool keep;
                if (label == 0xf9)
                {
                    // frame timing belongs to the drawn block after it; when that's plain text being
                    // dropped, the timing goes too (or it would move to the next frame)
                    keep = !(all && NextDrawn(blockEnd) == "text");
                }
                else if (label == 0xff && b.At(p + 2) == 11)
                {
                    string name = JsText.Chars(data, (int)(p + 3), (int)Math.Max(0, Math.Min(n, p + 14) - (p + 3)));
                    if (ExifReader.GifLoopApps.Contains(name) || name.StartsWith("ICCRGBG1", StringComparison.Ordinal)) keep = true;
                    else if (name == ExifReader.GifXmpApp) keep = !all && !ExifReader.XmpHasLocation(ExifReader.GifXmpText(b, p, blockEnd));
                    else keep = !all;
                }
                else keep = !all;   // comments, plain text, and anything unknown
                if (keep) output.Write(data, (int)p, (int)(blockEnd - p));
                p = blockEnd;
            }
            output.WriteByte(0x3b);
            return output.ToArray();
        }

        // The Verified Clean check: the cleaned file read again, and what it still holds.
        public static SelfCheckReport Verify(ImageMetadata originalMeta, long originalSize, ImageMetadata scrubbedMeta, long scrubbedSize, ScrubOptions options = null)
        {
            bool all = (options?.Mode ?? "all") != "location_only";
            var r = new SelfCheckReport { Format = scrubbedMeta.Format, OriginalSize = originalSize, ScrubbedSize = scrubbedSize, BytesSaved = originalSize - scrubbedSize };
            r.GpsRemoved = scrubbedMeta.Gps == null;
            r.CameraRemoved = string.IsNullOrEmpty(scrubbedMeta.Make) && string.IsNullOrEmpty(scrubbedMeta.Model);
            r.TrailersRemoved = scrubbedMeta.TrailerBytes == 0;
            r.IccPreserved = !originalMeta.HasIccProfile || scrubbedMeta.HasIccProfile;
            r.OrientationPreserved = !Truthy(originalMeta.Orientation) || originalMeta.Orientation <= 1 || scrubbedMeta.Orientation == originalMeta.Orientation;
            // in all mode the only field allowed to stay is the orientation written
            int keptOrientation = scrubbedMeta.Orientation.HasValue && scrubbedMeta.Orientation > 1 ? 1 : 0;
            int fieldsLeft = all ? Math.Max(0, scrubbedMeta.RawTagCount - keptOrientation) : 0;
            string camera = string.Join(" ", new[] { scrubbedMeta.Make, scrubbedMeta.Model }.Where(s => !string.IsNullOrEmpty(s)));
            if (!r.GpsRemoved) r.Leftovers.Add("GPS coordinates");
            if (all && !r.CameraRemoved) r.Leftovers.Add("camera tags (" + camera + ")");
            if (fieldsLeft > 0) r.Leftovers.Add(fieldsLeft + " metadata field" + (fieldsLeft == 1 ? "" : "s") + " in all");
            if (!r.TrailersRemoved) r.Leftovers.Add(JsText.Grouped(scrubbedMeta.TrailerBytes) + " trailing bytes");
            r.IsClean = r.Leftovers.Count == 0;

            var summary = new List<string>();
            if (!r.IsClean) summary.Add("Still found: " + string.Join(", ", r.Leftovers) + ".");
            var kept = new List<string>();
            if (all)
            {
                if (r.IsClean) summary.Add("Checked: 0 GPS coordinates, 0 camera tags, 0 trailing bytes found.");
                if (scrubbedMeta.Gif != null) kept.Add(scrubbedMeta.Gif.Frames > 1 ? "every frame, its timing and the loop" : "the picture");
                if (scrubbedMeta.HasIccProfile) kept.Add("colour profile");
                if (scrubbedMeta.Orientation.HasValue && scrubbedMeta.Orientation > 1) kept.Add("orientation");
                if (kept.Count > 0) summary.Add("Kept: " + string.Join(", ", kept) + ".");
                else if (r.IsClean) summary.Add("Clean pixel stream.");
            }
            else
            {
                if (r.IsClean) summary.Add("Checked: 0 GPS coordinates, 0 trailing bytes found.");
                if (camera.Length > 0) kept.Add("camera (" + camera + ")");
                if (!string.IsNullOrEmpty(scrubbedMeta.DateTime)) kept.Add("date");
                if (scrubbedMeta.Orientation.HasValue && scrubbedMeta.Orientation > 1) kept.Add("orientation");
                if (scrubbedMeta.HasIccProfile) kept.Add("colour profile");
                if (scrubbedMeta.Gif != null && scrubbedMeta.Gif.Comments > 0) kept.Add(scrubbedMeta.Gif.Comments == 1 ? "comment" : "comments");
                if (kept.Count > 0) summary.Add("Kept: " + string.Join(", ", kept) + ".");
            }
            r.TagsRemovedCount = Math.Max(0, originalMeta.RawTagCount - scrubbedMeta.RawTagCount);
            r.Summary = string.Join(" ", summary);
            return r;
        }

        static bool Truthy(double? x) => x.HasValue && x.Value != 0 && !double.IsNaN(x.Value);

        public static SelfCheckReport Verify(ImageMetadata originalMeta, long originalSize, byte[] scrubbed, ScrubOptions options = null) =>
            Verify(originalMeta, originalSize, ExifReader.Inspect(scrubbed), scrubbed.Length, options);

        // the kinds that can be cleaned here, by their format names
        public static bool CanScrub(string format) => format == "jpeg" || format == "png" || format == "webp" || format == "gif";

        // A file's metadata out, with the Verified Clean check of what came out.
        public static ScrubResult Scrub(byte[] data, ScrubOptions options = null)
        {
            string format = ExifReader.DetectFormat(data);
            ImageMetadata originalMeta = ExifReader.Inspect(data);
            byte[] scrubbed;
            if (format == "jpeg") scrubbed = ScrubJpeg(data, options);
            else if (format == "png") scrubbed = ScrubPng(data, options);
            else if (format == "webp") scrubbed = ScrubWebp(data);
            else if (format == "gif") scrubbed = ScrubGif(data, options);
            else if (format == "heic") throw new ScrubException("HEIC files cannot be safely scrubbed at the byte level in the browser without re-encoding. Convert to JPEG or WebP to scrub metadata.");
            else if (format == "avif") throw new ScrubException("AVIF files cannot be scrubbed here yet: only JPEG, PNG, WebP and GIF can. Convert to one of those to scrub metadata.");
            else throw new ScrubException("Unsupported image format. Supported formats: JPEG, PNG, WebP, GIF.");
            return new ScrubResult { Data = scrubbed, Format = format, Report = Verify(originalMeta, data.Length, scrubbed, options) };
        }

        // a format's file extension, as the website names clean files (jpeg as jpg)
        public static string Extension(string format) => format == "jpeg" ? "jpg" : format;

        static readonly Regex lastExtension = new Regex(@"\.[^/.]+\z", RegexOptions.CultureInvariant);

        // The batch's names for its clean files, as the website's ZIP names them: photo-clean.jpg,
        // and photo-clean-2.jpg when a name (whatever its case) is taken.
        public static string BatchName(string name, string format, HashSet<string> used)
        {
            string ext = Extension(format);
            string stem = lastExtension.Replace(name, "", 1);
            string clean = stem + "-clean." + ext;
            for (int k = 2; used.Contains(JsText.Lower(clean)); k++) clean = stem + "-clean-" + k + "." + ext;
            used.Add(JsText.Lower(clean));
            return clean;
        }
    }
}
