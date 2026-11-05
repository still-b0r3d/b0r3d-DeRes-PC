using System;
using System.Globalization;
using System.Text;

namespace B0r3dDeRes.Engine
{
    // A SAUCE record's fields (null: the usual).
    sealed class SauceInfo
    {
        public string Title, Author, Group;
        public string Date;          // YYYYMMDD (null: today)
        public int? DataType;        // 1: characters
        public int? FileType;        // 1: ANSi
        public int? TInfo1;          // columns
        public int? TInfo2;          // rows
        public int? TFlags;
        public string FontName;      // "IBM VGA"
        public long FileSize;        // (read back: the size of what's before it)
    }

    // SAUCE (Standard Architecture for Universal Comment Extensions) v00: the 128-byte record at
    // the end of BBS art files (.ans), after an end-of-file byte (Ctrl-Z). (The website's core/sauce.ts.)
    static class Sauce
    {
        public const int RecordSize = 128;
        public const byte Eof = 0x1a;

        // text as UTF-8, cut to `length` bytes and padded with spaces
        static void Put(byte[] record, int at, string text, int length)
        {
            for (int i = 0; i < length; i++) record[at + i] = 0x20;
            if (string.IsNullOrEmpty(text)) return;
            byte[] raw = new UTF8Encoding(false).GetBytes(text);
            Buffer.BlockCopy(raw, 0, record, at, Math.Min(raw.Length, length));
        }

        // today, as the record keeps dates (the PC's own clock and time zone, as the website's browser's)
        public static string Today() => DateTime.Now.ToString("yyyyMMdd", CultureInfo.InvariantCulture);

        // the 128-byte record for a file of `fileSize` bytes
        public static byte[] Record(long fileSize, SauceInfo info = null)
        {
            info = info ?? new SauceInfo();
            var record = new byte[RecordSize];
            Encoding.ASCII.GetBytes("SAUCE00", 0, 7, record, 0);
            Put(record, 7, info.Title ?? "", 35);
            // no author unless one is given: the person made the art
            Put(record, 42, info.Author ?? "", 20);
            Put(record, 62, info.Group ?? "b0r3d.org", 20);
            string date = info.Date != null && info.Date.Length == 8 ? info.Date : Today();
            byte[] dateBytes = new UTF8Encoding(false).GetBytes(date);
            Buffer.BlockCopy(dateBytes, 0, record, 82, Math.Min(dateBytes.Length, RecordSize - 82));
            uint size = (uint)fileSize;
            record[90] = (byte)size;
            record[91] = (byte)(size >> 8);
            record[92] = (byte)(size >> 16);
            record[93] = (byte)(size >> 24);
            record[94] = (byte)(info.DataType ?? 1);
            record[95] = (byte)(info.FileType ?? 1);
            int t1 = info.TInfo1 ?? 80, t2 = info.TInfo2 ?? 0;
            record[96] = (byte)t1;
            record[97] = (byte)(t1 >> 8);
            record[98] = (byte)t2;
            record[99] = (byte)(t2 >> 8);
            // TInfo3, TInfo4 and the comment count stay 0
            record[105] = (byte)(info.TFlags ?? 0);
            // the font's name, zero-padded
            if (!string.IsNullOrEmpty(info.FontName))
            {
                byte[] font = new UTF8Encoding(false).GetBytes(info.FontName);
                Buffer.BlockCopy(font, 0, record, 106, Math.Min(font.Length, 22));
            }
            return record;
        }

        // the data, the end-of-file byte and the record
        public static byte[] Attach(byte[] data, SauceInfo info = null)
        {
            byte[] record = Record(data.Length, info);
            var output = new byte[data.Length + 1 + RecordSize];
            Buffer.BlockCopy(data, 0, output, 0, data.Length);
            output[data.Length] = Eof;
            Buffer.BlockCopy(record, 0, output, data.Length + 1, RecordSize);
            return output;
        }

        // the record at the end of a file, read back; null when there's none
        public static SauceInfo Parse(byte[] data)
        {
            if (data.Length < RecordSize) return null;
            int at = data.Length - RecordSize;
            if (Encoding.ASCII.GetString(data, at, 5) != "SAUCE") return null;
            string Text(int from, int length) => Encoding.GetEncoding(1252).GetString(data, at + from, length);
            return new SauceInfo
            {
                Title = Text(7, 35).TrimEnd(),
                Author = Text(42, 20).TrimEnd(),
                Group = Text(62, 20).TrimEnd(),
                Date = Text(82, 8),
                FileSize = BitConverter.ToUInt32(data, at + 90),
                DataType = data[at + 94],
                FileType = data[at + 95],
                TInfo1 = BitConverter.ToUInt16(data, at + 96),
                TInfo2 = BitConverter.ToUInt16(data, at + 98),
                TFlags = data[at + 105],
                FontName = Text(106, 22).TrimEnd('\0').TrimEnd(),
            };
        }
    }
}
