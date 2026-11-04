using System;
using System.IO;
using System.Text;

namespace B0r3dDeRes.Video
{
    // The edit lists of an MP4 or MOV file, which Windows' video system doesn't apply: where the first
    // video track and the first sound track start showing in their own media. Encoders that reorder
    // frames start the picture a couple of frames in, and AAC starts its sound after a run-in of
    // samples; the edit lists say so, and players line the two up by them. Without them a phone's
    // video plays its sound out of step with its picture.
    static class Mp4Edits
    {
        // How much later, in 100 ns steps, Windows' times for each track run than the times the file
        // means. False (and no shift) for a file that isn't MP4 or MOV, or has no edit lists.
        public static bool Read(string path, out long videoShift, out long soundShift)
        {
            videoShift = soundShift = 0;
            try
            {
                using (var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    byte[] moov = FindTop(file, "moov");
                    if (moov == null) return false;
                    uint movieScale = 0;
                    bool video = false, sound = false;
                    foreach (var (type, start, length) in Boxes(moov, 0, moov.Length))
                    {
                        if (type == "mvhd") movieScale = TimeScale(moov, start);
                        else if (type == "trak")
                        {
                            Track(moov, start, length, out string handler, out long shift100ns, movieScale);
                            if (handler == "vide" && !video)
                            {
                                video = true;
                                videoShift = shift100ns;
                            }
                            else if (handler == "soun" && !sound)
                            {
                                sound = true;
                                soundShift = shift100ns;
                            }
                        }
                    }
                    return video;
                }
            }
            catch (Exception x) when (x is IOException || x is UnauthorizedAccessException || x is ArgumentException || x is IndexOutOfRangeException || x is OverflowException)
            {
                videoShift = soundShift = 0;
                return false;
            }
        }

        // a top-level box's insides (not past 64 MB), or null
        static byte[] FindTop(Stream file, string wanted)
        {
            long at = 0, end = file.Length;
            var head = new byte[16];
            while (at + 8 <= end)
            {
                file.Position = at;
                if (file.Read(head, 0, 8) < 8) return null;
                long size = BigEndian(head, 0, 4);
                string type = Encoding.ASCII.GetString(head, 4, 4);
                int header = 8;
                if (size == 1)
                {
                    if (file.Read(head, 8, 8) < 8) return null;
                    size = BigEndian(head, 8, 8);
                    header = 16;
                }
                else if (size == 0) size = end - at;
                if (size < header) return null;
                if (type == wanted)
                {
                    long length = size - header;
                    if (length > 64 << 20) return null;
                    var inside = new byte[length];
                    file.Position = at + header;
                    int read = 0;
                    while (read < length)
                    {
                        int n = file.Read(inside, read, (int)length - read);
                        if (n <= 0) return null;
                        read += n;
                    }
                    return inside;
                }
                at += size;
            }
            return null;
        }

        static System.Collections.Generic.IEnumerable<(string Type, int Start, int Length)> Boxes(byte[] b, int from, int to)
        {
            int at = from;
            while (at + 8 <= to)
            {
                long size = BigEndian(b, at, 4);
                string type = Encoding.ASCII.GetString(b, at + 4, 4);
                int header = 8;
                if (size == 1)
                {
                    size = BigEndian(b, at + 8, 8);
                    header = 16;
                }
                else if (size == 0) size = to - at;
                if (size < header || at + size > to) yield break;
                yield return (type, at + header, (int)size - header);
                at += (int)size;
            }
        }

        // mvhd's and mdhd's time scale (after the version, flags and two times of 4 or 8 bytes)
        static uint TimeScale(byte[] b, int start) => (uint)BigEndian(b, start + (b[start] == 1 ? 20 : 12), 4);

        // a track's handler (vide, soun…) and how far its edits shift it
        static void Track(byte[] b, int start, int length, out string handler, out long shift, uint movieScale)
        {
            handler = null;
            shift = 0;
            uint mediaScale = 0;
            long mediaStart = 0, emptyBefore = 0;
            bool edits = false;
            foreach (var (type, s, l) in Boxes(b, start, start + length))
            {
                if (type == "mdia")
                    foreach (var (mt, ms, ml) in Boxes(b, s, s + l))
                    {
                        if (mt == "mdhd") mediaScale = TimeScale(b, ms);
                        else if (mt == "hdlr" && ml >= 12) handler = Encoding.ASCII.GetString(b, ms + 8, 4);
                    }
                else if (type == "edts")
                    foreach (var (et, es, el) in Boxes(b, s, s + l))
                    {
                        if (et != "elst" || el < 8) continue;
                        bool wide = b[es] == 1;
                        long count = BigEndian(b, es + 4, 4);
                        int at = es + 8, entry = wide ? 20 : 12;
                        for (long k = 0; k < count && at + entry <= es + el; k++, at += entry)
                        {
                            long duration = BigEndian(b, at, wide ? 8 : 4);
                            long time = wide ? (long)BigEndian(b, at + 8, 8) : (int)(uint)BigEndian(b, at + 4, 4);
                            if (time == -1)
                            {
                                // an empty edit: the track starts this much later
                                emptyBefore += duration;
                                continue;
                            }
                            mediaStart = time;
                            edits = true;
                            break;
                        }
                    }
            }
            if (!edits || mediaScale == 0) return;
            shift = (long)Math.Round(mediaStart * 1e7 / mediaScale - (movieScale > 0 ? emptyBefore * 1e7 / movieScale : 0));
        }

        static long BigEndian(byte[] b, int at, int bytes)
        {
            ulong v = 0;
            for (int i = 0; i < bytes; i++) v = (v << 8) | b[at + i];
            return (long)v;
        }
    }
}
