using System;
using System.IO;
using System.Threading;
using B0r3dDeRes.Engine;
using B0r3dDeRes.Video;

namespace B0r3dDeRes
{
    // What the art of every frame goes out with.
    sealed class AsciiFramesOptions
    {
        public AsciiSettings Settings;
        public int Multiplier = 1;              // the picture's size (GIF, APNG, MP4)
        public LoadedPicture Picture;           // an animated GIF, or a video (from its start to its stop)
        public double Fps;                      // a video's frames a second out (its own, or a slower one)
        public bool Sound = true;               // an MP4 keeps the video's sound
        public string Title = "photo";          // the ANSImation's SAUCE title
        public long From, To;                   // a video's stretch, taken when the save starts
        public int Lanes = Math.Max(1, Math.Min(4, Environment.ProcessorCount / 3));
        public Action<int, int, string> Progress;
        public CancellationToken Cancel;
    }

    // The art of every frame of an animated GIF (its own frames and delays, as the website's
    // ANSImation, GIF and APNG take them) or of a video (from its start to its stop, each frame
    // out the one showing then, as a video's saves take them): as an ANSImation (each frame drawn
    // over the last), a GIF or APNG of the art's pictures (the website's encoder: exactly their
    // colours when there are 256 or fewer), or an MP4 of them with the video's sound. Frames
    // stream through a few at a time, so any length fits.
    static class AsciiFrames
    {
        // the frames: how many, frame k's art, and (a GIF) its delays
        sealed class Source : IDisposable
        {
            readonly AsciiFramesOptions o;
            readonly VideoExport.Frames video;
            public readonly int Count;
            public readonly double Fps;

            public Source(AsciiFramesOptions o, double fps)
            {
                this.o = o;
                Fps = fps;
                if (o.Picture.Animation != null) Count = o.Picture.Animation.Frames.Length;
                else
                {
                    VideoClip clip = o.Picture.Video ?? throw new AnimationException("This picture has no frames to animate.");
                    Count = VideoExport.FrameCount(o.From, o.To, fps);
                    video = new VideoExport.Frames(clip.File, o.From, fps);
                }
            }

            // (a GIF's frames aren't read as a canvas holds them, as on the website; a video's are opaque)
            public AsciiArt Art(int k) => o.Picture.Animation != null ? Ascii.Generate(o.Picture.Animation.Frames[k], o.Settings, false) : Ascii.Generate(video.Get(k), o.Settings);

            public void Dispose() => video?.Dispose();
        }

        public static int Ansimation(Stream output, AsciiFramesOptions o)
        {
            using (var source = new Source(o, o.Fps))
            {
                var writer = new Ascii.AnsimationWriter(output);
                for (int k = 0; k < source.Count; k++)
                {
                    o.Cancel.ThrowIfCancellationRequested();
                    writer.Add(source.Art(k));
                    o.Progress?.Invoke(k + 1, source.Count, null);
                }
                writer.Finish(new SauceInfo { Title = o.Title, Author = "", Group = "b0r3d.org" });
                return writer.Frames;
            }
        }

        public static AnimationResult Animation(Stream output, AsciiFramesOptions o, bool apng)
        {
            AnimatedGif gif = o.Picture.Animation;
            // a video's frames at the most a GIF or APNG shows (VideoExport.AnimationFps), its delays kept over the clip
            double fps = gif != null ? 0 : VideoExport.AnimationFps(o.Fps, apng);
            using (var source = new Source(o, fps))
            {
                int total = source.Count;
                int[] delays;
                int? loop;
                if (gif != null)
                {
                    delays = gif.DelaysMs;
                    loop = gif.LoopCount;
                }
                else
                {
                    delays = new int[total];
                    for (int k = 0; k < total; k++)
                        delays[k] = apng ? (int)(Math.Round((k + 1) * 1000 / fps) - Math.Round(k * 1000 / fps)) : (int)(Math.Round((k + 1) * 100 / fps) - Math.Round(k * 100 / fps)) * 10;
                    loop = 0;
                }
                AsciiArt first = source.Art(0);
                AsciiRender.Size(first.Width, first.Height, o.Multiplier, out int w, out int h);
                return RgbAnimation.Write(output, new RgbAnimationOptions
                {
                    Apng = apng, Width = w, Height = h, DelaysMs = delays, LoopCount = loop, Lanes = o.Lanes, Limits = AnimationLimits.Pc, Progress = o.Progress, Cancel = o.Cancel,
                    Frame = (k, lane) =>
                    {
                        AsciiArt art = k == 0 ? first : source.Art(k);
                        if (art.Width != first.Width || art.Height != first.Height) throw new AnimationException("Every frame of an animation has to be the same size.");
                        return AsciiRender.Rgb(art, o.Multiplier);
                    },
                });
            }
        }

        public static AnimationResult Mp4(Stream output, AsciiFramesOptions o)
        {
            VideoClip clip = o.Picture.Video ?? throw new AnimationException("Only a video goes out as an MP4.");
            using (var source = new Source(o, o.Fps))
            {
                AsciiArt first = source.Art(0);
                AsciiRender.Size(first.Width, first.Height, o.Multiplier, out int w, out int h);
                var video = new VideoExportOptions { File = clip.File, From = o.From, To = o.To, Fps = o.Fps, Sound = o.Sound, Lanes = o.Lanes, Progress = o.Progress, Cancel = o.Cancel };
                return VideoExport.Mp4Of(output, video, w, h, (k, lane) => new AnimFrame { Rgb = AsciiRender.Rgb(k == 0 ? first : source.Art(k), o.Multiplier) });
            }
        }
    }
}
