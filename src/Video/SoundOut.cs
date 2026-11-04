using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace B0r3dDeRes.Video
{
    // Sound out of the speakers while a video plays, through Windows' plain wave output: 48 kHz
    // stereo, 16-bit, in stretches queued a little ahead. How much has played is the clock the
    // picture keeps to. On a PC with no sound device it doesn't open, and the picture plays alone.
    sealed class SoundOut : IDisposable
    {
        [StructLayout(LayoutKind.Sequential)]
        struct WaveFormat
        {
            public ushort Tag, Channels;
            public uint SamplesPerSecond, AverageBytesPerSecond;
            public ushort BlockAlign, BitsPerSample, Extra;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct WaveHeader
        {
            public IntPtr Data;
            public uint Length, Recorded;
            public IntPtr User;
            public uint Flags, Loops;
            public IntPtr Next, Reserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct Time
        {
            public uint Type, Value, Extra;
        }

        [DllImport("winmm.dll")] static extern int waveOutOpen(out IntPtr device, uint id, ref WaveFormat format, IntPtr callback, IntPtr instance, uint flags);
        [DllImport("winmm.dll")] static extern int waveOutPrepareHeader(IntPtr device, IntPtr header, int size);
        [DllImport("winmm.dll")] static extern int waveOutUnprepareHeader(IntPtr device, IntPtr header, int size);
        [DllImport("winmm.dll")] static extern int waveOutWrite(IntPtr device, IntPtr header, int size);
        [DllImport("winmm.dll")] static extern int waveOutGetPosition(IntPtr device, ref Time time, int size);
        [DllImport("winmm.dll")] static extern int waveOutReset(IntPtr device);
        [DllImport("winmm.dll")] static extern int waveOutClose(IntPtr device);

        const uint WaveMapper = 0xFFFFFFFF, Done = 1, TimeSamples = 2;
        static readonly int headerSize = Marshal.SizeOf(typeof(WaveHeader));

        IntPtr device;
        // the stretches queued: their header and samples, in unmanaged memory until played
        readonly Queue<(IntPtr Header, IntPtr Data, int Samples)> queued = new Queue<(IntPtr, IntPtr, int)>();
        long queuedSamples, freedSamples;

        SoundOut(IntPtr device) => this.device = device;

        // ---- for the tests: speakers that aren't there, playing what's queued at its speed (and
        // stopping when it runs out, as real ones do), keeping every sample they were given
        readonly bool pretend;
        readonly System.Diagnostics.Stopwatch pretendClock = new System.Diagnostics.Stopwatch();
        long pretendFrom;   // where it played on from, when its clock last started
        internal readonly List<short> Heard = new List<short>();

        SoundOut(bool pretend) => this.pretend = pretend;

        internal static SoundOut Pretend() => new SoundOut(true);

        long PretendPlayed()
        {
            lock (Heard)
            {
                long playing = pretendFrom + pretendClock.ElapsedTicks * VideoReader.SampleRate / System.Diagnostics.Stopwatch.Frequency;
                if (playing < queuedSamples) return playing;
                // run dry: it stands at the end of what it was given until more comes
                pretendFrom = queuedSamples;
                pretendClock.Restart();
                return queuedSamples;
            }
        }

        // the speakers, or null when there are none to play to
        public static SoundOut Open()
        {
            var format = new WaveFormat
            {
                Tag = 1, Channels = (ushort)VideoReader.Channels, SamplesPerSecond = VideoReader.SampleRate, BitsPerSample = 16,
                BlockAlign = (ushort)(VideoReader.Channels * 2), AverageBytesPerSecond = (uint)(VideoReader.SampleRate * VideoReader.Channels * 2),
            };
            try
            {
                return waveOutOpen(out IntPtr device, WaveMapper, ref format, IntPtr.Zero, IntPtr.Zero, 0) == 0 ? new SoundOut(device) : null;
            }
            catch (Exception x) when (x is DllNotFoundException || x is EntryPointNotFoundException)
            {
                return null;
            }
        }

        // a stretch of samples (left then right) after the ones queued
        public void Queue(short[] samples, int offset, int count)
        {
            if (pretend && count > 0)
            {
                lock (Heard)
                {
                    PretendPlayed();
                    for (int i = 0; i < count; i++) Heard.Add(samples[offset + i]);
                    queuedSamples += count / 2;
                    return;
                }
            }
            if (device == IntPtr.Zero || count <= 0) return;
            Free();
            IntPtr data = Marshal.AllocHGlobal(count * 2);
            Marshal.Copy(samples, offset, data, count);
            IntPtr header = Marshal.AllocHGlobal(headerSize);
            Marshal.StructureToPtr(new WaveHeader { Data = data, Length = (uint)(count * 2) }, header, false);
            if (waveOutPrepareHeader(device, header, headerSize) != 0 || waveOutWrite(device, header, headerSize) != 0)
            {
                waveOutUnprepareHeader(device, header, headerSize);
                Marshal.FreeHGlobal(header);
                Marshal.FreeHGlobal(data);
                return;
            }
            queued.Enqueue((header, data, count / 2));
            queuedSamples += count / 2;
        }

        // the stretches played to the end, let go
        void Free()
        {
            while (queued.Count > 0)
            {
                var (header, data, samples) = queued.Peek();
                var h = (WaveHeader)Marshal.PtrToStructure(header, typeof(WaveHeader));
                if ((h.Flags & Done) == 0) break;
                queued.Dequeue();
                waveOutUnprepareHeader(device, header, headerSize);
                Marshal.FreeHGlobal(header);
                Marshal.FreeHGlobal(data);
                freedSamples += samples;
            }
        }

        const uint TimeBytes = 4;

        // samples played since it opened (each a left and a right)
        public long Played
        {
            get
            {
                if (pretend) return PretendPlayed();
                var t = new Time { Type = TimeSamples };
                if (device == IntPtr.Zero || waveOutGetPosition(device, ref t, Marshal.SizeOf(typeof(Time))) != 0) return freedSamples;
                // (a device that counts in bytes says so)
                return t.Type == TimeSamples ? t.Value : t.Type == TimeBytes ? t.Value / (VideoReader.Channels * 2) : freedSamples;
            }
        }

        // samples queued and not yet played
        public long Ahead => Math.Max(0, queuedSamples - Played);

        public void Dispose()
        {
            if (device == IntPtr.Zero) return;
            waveOutReset(device);
            while (queued.Count > 0)
            {
                var (header, data, _) = queued.Dequeue();
                waveOutUnprepareHeader(device, header, headerSize);
                Marshal.FreeHGlobal(header);
                Marshal.FreeHGlobal(data);
            }
            waveOutClose(device);
            device = IntPtr.Zero;
        }
    }
}
