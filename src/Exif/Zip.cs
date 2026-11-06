using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using B0r3dDeRes.Engine;

namespace B0r3dDeRes.Exif
{
    // A ZIP of cleaned files, the website's zip.ts: every file stored (not squeezed), its name in
    // UTF-8, and every entry dated 00:00 on 1980-01-01 (the earliest a ZIP holds), so the archive
    // says nothing about when or where it was made. Written straight to its file as it goes; the
    // same bytes as the website's for the same files. Past what a plain ZIP holds (a file or the
    // whole past 4 GB, or more than 65,535 files) it carries ZIP64's fields (the PC's).
    sealed class ZipWriter : IDisposable
    {
        const ushort ZipDate = (0 << 9) | (1 << 5) | 1;
        const uint Max32 = 0xFFFFFFFF;
        const ushort Max16 = 0xFFFF;

        sealed class Entry
        {
            public string Text;
            public byte[] Name;
            public uint Crc;
            public long Size, Offset, DataOffset;
        }

        readonly Stream output;
        readonly List<Entry> entries = new List<Entry>();
        // where ZIP64 starts: a size or offset this big, or this many files (lower only in the
        // tests, so they can read ZIP64's fields back without writing 4 GB)
        readonly long bigFrom;
        readonly int manyFrom;
        bool finished, adding;

        public ZipWriter(Stream output) : this(output, Max32, Max16)
        {
        }

        internal ZipWriter(Stream output, long bigFrom, int manyFrom)
        {
            this.output = output;
            this.bigFrom = bigFrom;
            this.manyFrom = manyFrom;
        }

        public int Count => entries.Count;

        // each file put in: its name, where its bytes start in the ZIP and how many there are
        public IEnumerable<(string Name, long DataOffset, long Size)> Written => entries.Select(e => (e.Text, e.DataOffset, e.Size));

        // the website's names are written with TextEncoder: UTF-8
        static byte[] NameBytes(string name) => Injector.Utf8(name);

        void U16(long v)
        {
            output.WriteByte((byte)v);
            output.WriteByte((byte)(v >> 8));
        }

        void U32(long v)
        {
            U16(v & 0xFFFF);
            U16((v >> 16) & 0xFFFF);
        }

        void U64(long v)
        {
            U32(v & 0xFFFFFFFF);
            U32((long)((ulong)v >> 32));
        }

        void LocalHeader(Entry e)
        {
            bool big = e.Size >= bigFrom;
            U32(0x04034b50);
            U16(big ? 45 : 20);     // version needed: 2.0 (4.5 for ZIP64)
            U16(0x0800);            // UTF-8 names
            U16(0);                 // stored
            U16(0);                 // time
            U16(ZipDate);
            U32(e.Crc);
            U32(big ? Max32 : e.Size);
            U32(big ? Max32 : e.Size);
            U16(e.Name.Length);
            U16(big ? 20 : 0);
            output.Write(e.Name, 0, e.Name.Length);
            if (big)
            {
                U16(0x0001);
                U16(16);
                U64(e.Size);
                U64(e.Size);
            }
        }

        // a file whose bytes are in memory
        public void Add(string name, byte[] data)
        {
            adding = true;
            var e = new Entry { Text = name, Name = NameBytes(name), Crc = Png.Crc(0xffffffff, data, 0, data.Length) ^ 0xffffffff, Size = data.Length, Offset = output.Position };
            LocalHeader(e);
            e.DataOffset = output.Position;
            output.Write(data, 0, data.Length);
            entries.Add(e);
            adding = false;
        }

        // A file of a known size written by `write` (a video): its CRC is worked out as it goes and
        // put into its header after (the stream must be seekable). `write` must write exactly
        // `size` bytes: more stops it as they come, fewer once it's done.
        public void Add(string name, long size, Action<Stream> write)
        {
            adding = true;
            var e = new Entry { Text = name, Name = NameBytes(name), Size = size, Offset = output.Position };
            LocalHeader(e);
            long dataStart = e.DataOffset = output.Position;
            var crc = new CrcStream(output, size);
            write(crc);
            if (crc.Written != size) throw new IOException("the clean file came out " + crc.Written + " bytes, not " + size);
            e.Crc = crc.Crc;
            long end = output.Position;
            output.Position = e.Offset + 14;
            U32(e.Crc);
            output.Position = end;
            if (output.Position != dataStart + size) throw new IOException("the ZIP's entry isn't where it should be");
            entries.Add(e);
            adding = false;
        }

        // the central directory and the end records
        public void Finish()
        {
            if (finished) return;
            finished = true;
            long cdStart = output.Position;
            foreach (Entry e in entries)
            {
                bool bigSize = e.Size >= bigFrom, bigOffset = e.Offset >= bigFrom;
                int extra = (bigSize ? 16 : 0) + (bigOffset ? 8 : 0);
                U32(0x02014b50);
                U16(bigSize || bigOffset ? 45 : 20);   // version made by
                U16(bigSize || bigOffset ? 45 : 20);   // version needed
                U16(0x0800);
                U16(0);
                U16(0);
                U16(ZipDate);
                U32(e.Crc);
                U32(bigSize ? Max32 : e.Size);
                U32(bigSize ? Max32 : e.Size);
                U16(e.Name.Length);
                U16(extra > 0 ? extra + 4 : 0);
                U16(0);     // comment
                U16(0);     // disk number start
                U16(0);     // internal attributes
                U32(0);     // external attributes
                U32(bigOffset ? Max32 : e.Offset);
                output.Write(e.Name, 0, e.Name.Length);
                if (extra > 0)
                {
                    U16(0x0001);
                    U16(extra);
                    if (bigSize)
                    {
                        U64(e.Size);
                        U64(e.Size);
                    }
                    if (bigOffset) U64(e.Offset);
                }
            }
            long cdSize = output.Position - cdStart;
            bool zip64 = entries.Count >= manyFrom || cdSize >= bigFrom || cdStart >= bigFrom;
            if (zip64)
            {
                long eocd64 = output.Position;
                U32(0x06064b50);
                U64(44);
                U16(45);
                U16(45);
                U32(0);
                U32(0);
                U64(entries.Count);
                U64(entries.Count);
                U64(cdSize);
                U64(cdStart);
                U32(0x07064b50);
                U32(0);
                U64(eocd64);
                U32(1);
            }
            U32(0x06054b50);
            U16(0);
            U16(0);
            U16(zip64 ? Max16 : entries.Count);
            U16(zip64 ? Max16 : entries.Count);
            U32(zip64 ? Max32 : cdSize);
            U32(zip64 ? Max32 : cdStart);
            U16(0);
        }

        // (a file that failed part way in leaves a broken ZIP: no directory is written over it)
        public void Dispose()
        {
            if (!adding) Finish();
        }

        // a whole ZIP in memory, as the website's createZip makes it
        public static byte[] Create(IEnumerable<(string Name, byte[] Data)> files)
        {
            var bytes = new MemoryStream();
            using (var zip = new ZipWriter(bytes))
                foreach (var f in files) zip.Add(f.Name, f.Data);
            return bytes.ToArray();
        }

        // a stream that passes bytes on, working out their CRC-32 and counting them (no more than
        // `limit` of them)
        sealed class CrcStream : Stream
        {
            readonly Stream inner;
            readonly long limit;
            uint crc = 0xffffffff;
            public long Written { get; private set; }
            public uint Crc => crc ^ 0xffffffff;

            public CrcStream(Stream inner, long limit)
            {
                this.inner = inner;
                this.limit = limit;
            }

            public override void Write(byte[] buffer, int offset, int count)
            {
                if (Written + count > limit) throw new IOException("the clean file came out longer than " + limit + " bytes");
                crc = Png.Crc(crc, buffer, offset, count);
                Written += count;
                inner.Write(buffer, offset, count);
            }

            public override bool CanRead => false;
            public override bool CanSeek => false;
            public override bool CanWrite => true;
            public override long Length => Written;
            public override long Position { get => Written; set => throw new NotSupportedException(); }
            public override void Flush() => inner.Flush();
            public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
        }
    }
}
