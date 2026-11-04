using System;
using System.IO;
using System.Runtime.InteropServices;

namespace B0r3dDeRes.Video
{
    // An MP4 being written by Windows' own encoders: H.264 (its Baseline profile: no reordered
    // frames, which Windows' encoder would otherwise put a frame behind the sound) and AAC sound,
    // 48 kHz stereo, into a stream of the program's own. Frames go in as b, g, r, a, top row first.
    sealed class Mp4Writer : IDisposable
    {
        // H.264's largest frame (36,864 macroblocks: 4096 × 2304, or 2304 × 4096 standing up)
        public const long MaxArea = 4096L * 2304;
        public const int MaxSide = 4096 * 2;

        // whether an MP4 can hold frames of this size (even sides, H.264's area)
        public static bool Fits(int width, int height) => width >= 2 && height >= 2 && width % 2 == 0 && height % 2 == 0 && (long)width * height <= MaxArea && Math.Max(width, height) <= MaxSide;

        readonly IMFByteStream bytes;
        readonly IMFSinkWriter writer;
        readonly uint video, audio;
        readonly int width, height;
        readonly bool sound;
        bool finished;

        public Mp4Writer(Stream output, int width, int height, double fps, bool sound)
        {
            Mf.Start();
            if (!Fits(width, height)) throw new VideoException("An MP4 can't hold frames of " + width + "×" + height + " (they must be even, and at most " + 4096 + "×" + 2304 + ").");
            this.width = width;
            this.height = height;
            this.sound = sound;
            IMFAttributes attributes = null;
            try
            {
                Mf.Check(Mf.MFCreateMFByteStreamOnStream(new StreamForMf(output, true), out bytes), "Windows' video system can't write the file");
                attributes = Mf.NewAttributes();
                Mf.SetGuid(attributes, G.ContainerType, G.Mpeg4Container);
                // Windows' own software encoders, the same on every PC
                Mf.SetU32(attributes, G.HardwareTransforms, 0);
                Mf.Check(Mf.MFCreateSinkWriterFromURL(null, bytes, attributes, out writer), "Windows can't write an MP4 here");

                (uint rateTop, uint rateBottom) = Rate(fps);
                IMFMediaType h264 = Mf.NewType(G.Video, G.H264);
                IMFMediaType rgb = Mf.NewType(G.Video, G.Rgb32);
                try
                {
                    // about 0.25 bits a pixel a frame: room for the sharp edges of pixel art
                    double bits = Math.Max(1000000, Math.Min(100000000, (double)width * height * fps / 4));
                    Mf.SetU32(h264, G.AverageBitrate, (uint)bits);
                    Mf.SetU32(h264, G.Mpeg2Profile, 66);   // Baseline
                    foreach (IMFMediaType t in new[] { h264, rgb })
                    {
                        Mf.SetU32(t, G.InterlaceMode, 2);   // progressive
                        Mf.SetPair(t, G.FrameSize, (uint)width, (uint)height);
                        Mf.SetPair(t, G.FrameRate, rateTop, rateBottom);
                        Mf.SetPair(t, G.PixelAspectRatio, 1, 1);
                    }
                    // rows top first (else the frames come out upside down)
                    Mf.SetU32(rgb, G.DefaultStride, (uint)(width * 4));
                    Mf.Check(writer.AddStream(h264, out video), "Windows' H.264 encoder won't take this video");
                    Mf.Check(writer.SetInputMediaType(video, rgb, null), "Windows' H.264 encoder won't take frames of " + width + "×" + height);
                }
                finally
                {
                    Mf.Release(h264);
                    Mf.Release(rgb);
                }
                if (sound)
                {
                    IMFMediaType aac = Mf.NewType(G.Audio, G.Aac);
                    IMFMediaType pcm = Mf.NewType(G.Audio, G.Pcm);
                    try
                    {
                        foreach (IMFMediaType t in new[] { aac, pcm })
                        {
                            Mf.SetU32(t, G.AudioBitsPerSample, 16);
                            Mf.SetU32(t, G.AudioSamplesPerSecond, VideoReader.SampleRate);
                            Mf.SetU32(t, G.AudioChannels, VideoReader.Channels);
                        }
                        Mf.SetU32(aac, G.AudioAverageBytesPerSecond, 24000);   // 192 kbit/s
                        Mf.SetU32(pcm, G.AudioBlockAlignment, 4);
                        Mf.SetU32(pcm, G.AudioAverageBytesPerSecond, VideoReader.SampleRate * 4);
                        Mf.Check(writer.AddStream(aac, out audio), "Windows' AAC encoder won't take this sound");
                        Mf.Check(writer.SetInputMediaType(audio, pcm, null), "Windows' AAC encoder won't take this sound");
                    }
                    finally
                    {
                        Mf.Release(aac);
                        Mf.Release(pcm);
                    }
                }
                Mf.Check(writer.BeginWriting(), "Windows couldn't start the MP4");
            }
            catch
            {
                Dispose();
                throw;
            }
            finally
            {
                Mf.Release(attributes);
            }
        }

        // a frame rate as MP4 keeps it: NTSC's 1000/1001 ones exactly, others in thousandths
        static (uint, uint) Rate(double fps)
        {
            if (!(fps > 0)) fps = 30;
            double ntsc = fps * 1.001;
            if (Math.Abs(ntsc - Math.Round(ntsc)) < 0.001 && Math.Abs(fps - Math.Round(fps)) > 0.01) return ((uint)Math.Round(ntsc) * 1000, 1001);
            if (Math.Abs(fps - Math.Round(fps)) < 1e-6) return ((uint)Math.Round(fps), 1);
            return ((uint)Math.Round(fps * 1000), 1000);
        }

        // a frame (b, g, r, a, top row first, the writer's size) from `time` for `duration` (100 ns steps)
        public void AddFrame(byte[] bgra, long time, long duration)
        {
            if (bgra.LongLength != (long)width * height * 4) throw new ArgumentException("the frame isn't the video's size");
            int length = width * height * 4;
            Mf.Check(Mf.MFCreateMemoryBuffer((uint)length, out IMFMediaBuffer buffer), "Windows has no room for a frame");
            IMFSample sample = null;
            try
            {
                Mf.Check(buffer.Lock(out IntPtr p, out _, out _), "Windows has no room for a frame");
                Marshal.Copy(bgra, 0, p, length);
                buffer.Unlock();
                buffer.SetCurrentLength((uint)length);
                Mf.Check(Mf.MFCreateSample(out sample), "Windows has no room for a frame");
                sample.AddBuffer(buffer);
                sample.SetSampleTime(time);
                sample.SetSampleDuration(duration);
                Mf.Check(writer.WriteSample(video, sample), "Windows' H.264 encoder stopped");
            }
            finally
            {
                Mf.Release(sample);
                Mf.Release(buffer);
            }
        }

        // sound (16-bit, 48 kHz, left then right) from `time`
        public void AddSound(short[] samples, int offset, int count, long time)
        {
            if (!sound || count <= 0) return;
            int length = count * 2;
            Mf.Check(Mf.MFCreateMemoryBuffer((uint)length, out IMFMediaBuffer buffer), "Windows has no room for the sound");
            IMFSample sample = null;
            try
            {
                Mf.Check(buffer.Lock(out IntPtr p, out _, out _), "Windows has no room for the sound");
                Marshal.Copy(samples, offset, p, count);
                buffer.Unlock();
                buffer.SetCurrentLength((uint)length);
                Mf.Check(Mf.MFCreateSample(out sample), "Windows has no room for the sound");
                sample.AddBuffer(buffer);
                sample.SetSampleTime(time);
                sample.SetSampleDuration((long)count / 2 * VideoFile.Second / VideoReader.SampleRate);
                Mf.Check(writer.WriteSample(audio, sample), "Windows' AAC encoder stopped");
            }
            finally
            {
                Mf.Release(sample);
                Mf.Release(buffer);
            }
        }

        // writes what's left and the MP4's index; until then the file isn't a video
        public void Finish()
        {
            if (finished) return;
            Mf.Check(writer.FinalizeWriter(), "Windows couldn't finish the MP4");
            finished = true;
        }

        public void Dispose()
        {
            Mf.Release(writer);
            Mf.Release(bytes);
        }
    }
}
