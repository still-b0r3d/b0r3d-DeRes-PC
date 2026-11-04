using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace B0r3dDeRes.Engine
{
    // A frame of an animation at its size: r, g, b a pixel, and which pixels show (null: all).
    sealed class AnimFrame
    {
        public byte[] Rgb, Alpha;
    }

    // How big an animation may be, in pixels: one frame, and every frame held at once.
    sealed class AnimationLimits
    {
        public long FramePixels, Budget, ExportPixels = long.MaxValue;

        // the website's: a phone has to hold them
        public static readonly AnimationLimits Website = new AnimationLimits { FramePixels = 1024 * 1024, Budget = 16L * 1024 * 1024, ExportPixels = 4096L * 4096 };

        // The PC's, set by measuring (see PLAN.md, stage 5): a frame up to 4096 × 4096 (16 times the
        // website's), and 128 MP held at once (8 times; about 400 MB of frames): a 48-frame signal
        // loop up to 6× on the NES. An animated GIF's own frames past it are made twice instead.
        public static readonly AnimationLimits Pc = new AnimationLimits { FramePixels = 4096L * 4096, Budget = 128L * 1024 * 1024 };
    }

    sealed class AnimationException : Exception
    {
        public AnimationException(string message) : base(message) { }
    }

    sealed class AnimationResult
    {
        public bool Apng;
        public int Width, Height, Frames;
    }

    // How big a GIF or APNG comes out with the settings there are (the website's animationFit).
    sealed class AnimationFit
    {
        public int? Multiplier;     // the Size it's made at: the one chosen, or the biggest smaller one that fits (null: none fits)
        public int Held;            // frames held at once while it's made
        public int? Next;           // the next Size up that doesn't fit (the smallest, when none fits); null when the chosen one fits
        public string Limit;        // why Next doesn't: "frame" (one frame is too big) or "frames" (all of them together)
    }

    // What a loop is made of: the Export section's settings (the website's AnimatedExportOptions).
    sealed class LoopOptions
    {
        public string Style = Animation.ScreenFx;
        public bool Apng;
        public int Multiplier = 4;
        public bool Tv;
        public FxSettings Fx;                               // the screen effect, when it goes into exports (null: none)
        public PictureSource Source;                        // the picture, for the A/B wipe's original (null: none)
        public Func<Rendered, int, SignalImage> Signal;     // a picture through the signal at an engine frame (null: off)
        public int LoopFrames = 16, LoopFps = 15;           // the signal loop's length and speed
        public bool SeeThrough;                             // keep the picture's see-through pixels
        public AnimationLimits Limits = AnimationLimits.Website;
        public Action<int, int, string> Progress;           // frames done, of how many (and what's being done, if not the usual)
        public CancellationToken Cancel;
    }

    // What an animated GIF's own frames go out with (the website's FramesExportOptions).
    sealed class FramesOptions
    {
        public bool Apng;
        public int Multiplier = 4;
        public bool Tv;
        public FxSettings Fx;
        public int[] DelaysMs;
        public int? LoopCount;                              // the GIF's own: 0 = forever, null = it plays once
        public Func<int, int, Rendered> Render;              // frame k through the machine, on lane `lane` (each lane its own engine)
        public int Lanes = 1;                               // frames made at once
        public Func<Rendered, int, SignalImage> Signal;     // frame k through the signal (null: off)
        public Func<int, double> Phase;                     // the screen effect's place at frame k (null: once round over the frames, as the website's)
        public bool Indexed, SharedPalette;                 // pixels are palette entries; and the palette is every frame's
        public bool SeeThrough;
        public long? Budget;                                // pixels a full-colour GIF may hold at once (else Limits.Budget)
        public AnimationLimits Limits = AnimationLimits.Website;
        public Action<int, int, string> Progress;
        public CancellationToken Cancel;
    }

    // Looping animations of the picture, as GIF or APNG (the website's core/animate.ts): the Screen
    // FX loop (the CRT's beam, the VHS tape's tracking, the composite's crawl; with the signal on,
    // the signal's own moving noise), the A/B wipe between the original and the picture, the dither
    // shimmer (the dither's pattern moving a step a frame), and an animated GIF's own frames, each
    // through the settings. With see-through pixels kept, a GIF gets a see-through entry and an
    // APNG goes r, g, b, a (or gets a see-through palette entry), and every frame is cleared after
    // it shows, so the one before never shows through. The same frames and file as the website's.
    static class Animation
    {
        public const string ScreenFx = "screen-fx", CompareWipe = "compare-wipe", DitherShimmer = "dither-shimmer", GifFrames = "gif-frames";
        public static readonly string[] Styles = { ScreenFx, CompareWipe, DitherShimmer };

        // the frames in each loop style, and how fast it plays (Screen FX: without the signal)
        public static void StyleLoop(string style, out int frames, out int fps)
        {
            if (style == CompareWipe)
            {
                frames = 16;
                fps = 10;
            }
            else if (style == DitherShimmer)
            {
                frames = 8;
                fps = 8;
            }
            else
            {
                frames = 12;
                fps = 10;
            }
        }

        // How many frames an animation holds at once (`signalLoop`: the Screen FX loop's length with
        // the signal on, else null). An animated GIF's own frames count as none: they're held only
        // while they fit, and are made twice past that.
        public static int HeldFrames(string kind, int? signalLoop)
        {
            if (kind == GifFrames) return 0;
            if (kind == ScreenFx && signalLoop != null) return signalLoop.Value;
            StyleLoop(kind, out int frames, out _);
            return frames;
        }

        // A GIF's sides are 16 bits. The website writes a broken GIF past this; the PC says it's too
        // big (both GIF and APNG, so they keep one Size).
        public const int MaxSide = 65535;

        // why an animation of `held` frames at width × height won't fit ("frame", "frames"), or null
        public static string Limit(int width, int height, int held, AnimationLimits limits)
        {
            long px = (long)width * height;
            if (px > limits.FramePixels || px > limits.ExportPixels || Math.Max(width, height) > MaxSide) return "frame";
            return px * held > limits.Budget ? "frames" : null;
        }

        // the biggest Size no bigger than `chosen` at which it fits; null when not even the smallest does
        public static int? Multiplier(Func<int, (int W, int H)> size, int chosen, int held, AnimationLimits limits)
        {
            int? best = null;
            foreach (int m in Export.Multipliers)
            {
                if (m > chosen) continue;
                (int w, int h) = size(m);
                if (Limit(w, h, held, limits) == null && (best == null || m > best)) best = m;
            }
            return best;
        }

        // The size a GIF or APNG of a picture (its machine resolution and pixel shape) comes out
        // at: the Size chosen, or the biggest that fits. `kind`: a loop style, or GifFrames.
        public static AnimationFit FitFor(int width, int height, int parX, int parY, bool tvChosen, int chosen, string kind, int? signalLoop, AnimationLimits limits)
        {
            bool tv = tvChosen && parX != parY;
            (int W, int H) Size(int m)
            {
                Export.Size(width, height, parX, parY, m, tv, out int w, out int h);
                return (w, h);
            }
            int held = HeldFrames(kind, signalLoop);
            int? multiplier = Multiplier(Size, chosen, held, limits);
            int? next = null;
            foreach (int m in Export.Multipliers)
                if (m <= chosen && (multiplier == null || m > multiplier))
                {
                    next = m;
                    break;
                }
            string limit = null;
            if (next != null)
            {
                (int w, int h) = Size(next.Value);
                limit = Limit(w, h, held, limits);
            }
            return new AnimationFit { Multiplier = multiplier, Held = held, Next = next, Limit = limit };
        }

        // an APNG plays a loop count's worth of times plus the first; a GIF without one plays once
        public static int ApngPlays(int? loopCount) => loopCount == null ? 1 : loopCount == 0 ? 0 : loopCount.Value + 1;

        // Whether a machine's pixels are its palette's colours, and whether that palette is the
        // same for every picture (so an animation's frames can share it).
        public static void PaletteKind(Profile p, DeresSettings s, out bool indexed, out bool shared)
        {
            indexed = Profiles.PixelsArePalette(p);
            shared = indexed && (p.Kind == Engine.PaletteKind.Fixed || p.Kind == Engine.PaletteKind.Cell && p.Mode != "gbc-tile" ||
                p.Kind == Engine.PaletteKind.Custom && s.CustomPalette != null && s.CustomPalette.Length > 0);
        }

        // the dither shimmer's eight starting points for the pattern
        static readonly int[][] ditherPhases = { new[] { 0, 0 }, new[] { 2, 1 }, new[] { 1, 3 }, new[] { 3, 2 }, new[] { 2, 2 }, new[] { 0, 3 }, new[] { 3, 1 }, new[] { 1, 0 } };

        // ---- the loops ----

        // A looping GIF or APNG of the picture `r` (made with `settings`) into `output`.
        // `renderDeres` makes it again with other settings (the dither shimmer's moving pattern).
        public static AnimationResult Loop(Stream output, Rendered r, LoopOptions o, DeresSettings settings, Func<DeresSettings, Rendered> renderDeres)
        {
            int m = Math.Max(1, o.Multiplier);
            Export.Size(r.Width, r.Height, r.Geometry.ParX, r.Geometry.ParY, m, o.Tv, out int width, out int height);
            if (Limit(width, height, 0, o.Limits) != null) throw new AnimationException("That size is too big for an animation. Pick a smaller size.");
            bool seeThrough = o.SeeThrough;
            Func<Rendered, int, SignalImage> signal = o.Signal;
            // every frame is held before it goes into the file (the website's limits always hold the
            // loops of a set length; the PC's bigger frames might not)
            int held = HeldFrames(o.Style, signal != null ? o.LoopFrames : (int?)null);
            if (o.Style != ScreenFx || signal == null)
                if ((long)width * height * held > o.Limits.Budget) throw new AnimationException(held + " frames at that size is too much to hold at once. Pick a smaller size.");
            // with the signal, the screen effect follows the PNG's rule (from 3× up)
            FxSettings signalFx = Export.WithFx(o.Fx, m) ? o.Fx : null;
            void Progress(int done, int total) => o.Progress?.Invoke(done, total, null);
            List<AnimFrame> Make(int count, Func<int, AnimFrame> frame)
            {
                var frames = new List<AnimFrame>();
                for (int f = 0; f < count; f++)
                {
                    o.Cancel.ThrowIfCancellationRequested();
                    frames.Add(frame(f));
                    Progress(f + 1, count);
                }
                return frames;
            }

            // the Screen FX loop with the signal: real moving noise, any loop length that fits
            if (o.Style == ScreenFx && signal != null)
            {
                int frameCount = o.LoopFrames, fps = o.LoopFps, step = Signal.EngineStep(fps);
                if ((long)width * height * frameCount > o.Limits.Budget)
                    throw new AnimationException(frameCount + " frames at that size is too much to hold at once. Pick fewer frames or a smaller size.");
                int delayMs = (int)JsMath.Round(1000.0 / fps);
                List<AnimFrame> frames = Make(frameCount, f => SignalFrame(signal(r, f * step), width, height, signalFx, (double)f / frameCount, r.Paper));
                return EncodeFrames(output, frames, o.Apng, width, height, delayMs, seeThrough, o.Cancel);
            }

            // the Screen FX loop
            if (o.Style == ScreenFx)
            {
                StyleLoop(ScreenFx, out int frameCount, out int fps);
                int delayMs = (int)JsMath.Round(1000.0 / fps);
                FxSettings activeFx = o.Fx != null && o.Fx.Screen != Screens.Off ? o.Fx : new FxSettings { Screen = Screens.Crt };
                List<AnimFrame> frames = Make(frameCount, f => FxFrame(r, width, height, activeFx, (double)f / frameCount));
                return EncodeFrames(output, frames, o.Apng, width, height, delayMs, seeThrough, o.Cancel);
            }

            // before and after: a wipe between the original and the picture
            if (o.Style == CompareWipe)
            {
                StyleLoop(CompareWipe, out int frameCount, out int fps);
                int delayMs = (int)JsMath.Round(1000.0 / fps);
                // the picture at the full size (with the signal: a new frame of it each time)
                int step = Signal.EngineStep(fps);
                Func<int, AnimFrame> deresAt = signal != null ? (Func<int, AnimFrame>)(f => SignalFrame(signal(r, f * step), width, height, signalFx, 0, r.Paper)) : null;
                AnimFrame deres = deresAt != null ? deresAt(0) : Export.WithFx(o.Fx, m) ? FxFrame(r, width, height, o.Fx, 0) : Enlarged(r, width, height);
                // the original at the full size (its see-through parts the same way: a pixel shows
                // from half covered)
                AnimFrame orig = o.Source != null
                    ? new AnimFrame { Rgb = SourceRgb(o.Source, width, height), Alpha = seeThrough ? SourceAlpha(o.Source, width, height) : null }
                    : new AnimFrame { Rgb = (byte[])deres.Rgb.Clone(), Alpha = deres.Alpha };
                var frames = new List<AnimFrame>();
                for (int f = 0; f < frameCount; f++)
                {
                    o.Cancel.ThrowIfCancellationRequested();
                    if (deresAt != null && f > 0) deres = deresAt(f);
                    int splitX;
                    if (f == 0 || f == 15) splitX = 0;                                         // all the original
                    else if (f >= 1 && f <= 7) splitX = (int)JsMath.Round((double)f / 8 * width);   // wiping across
                    else if (f >= 8 && f <= 9) splitX = width;                                 // all the picture
                    else splitX = (int)JsMath.Round((double)(15 - f) / 6 * width);             // wiping back
                    var rgb = new byte[(long)width * height * 3];
                    byte[] alpha = seeThrough ? new byte[(long)width * height] : null;
                    AnimFrame left = deres, right = orig;
                    EveryCore.For(0, height, y =>
                    {
                        for (int x = 0; x < width; x++)
                        {
                            long p = (long)y * width + x, idx = p * 3;
                            if (splitX > 0 && splitX < width && (x == splitX - 1 || x == splitX))
                            {
                                // the divider
                                rgb[idx] = rgb[idx + 1] = rgb[idx + 2] = 255;
                                if (alpha != null) alpha[p] = 255;
                            }
                            else
                            {
                                AnimFrame side = x < splitX ? left : right;
                                rgb[idx] = side.Rgb[idx];
                                rgb[idx + 1] = side.Rgb[idx + 1];
                                rgb[idx + 2] = side.Rgb[idx + 2];
                                if (alpha != null) alpha[p] = side.Alpha != null ? side.Alpha[p] : (byte)255;
                            }
                        }
                    });
                    frames.Add(new AnimFrame { Rgb = rgb, Alpha = alpha });
                    Progress(f + 1, frameCount);
                }
                return EncodeFrames(output, frames, o.Apng, width, height, delayMs, seeThrough, o.Cancel);
            }

            // the dither shimmer: the pattern moves a step each frame
            {
                StyleLoop(DitherShimmer, out int frameCount, out int fps);
                int delayMs = (int)JsMath.Round(1000.0 / fps);
                Rendered RenderAt(int f)
                {
                    if (renderDeres == null) return r;
                    DeresSettings s = settings.Clone();
                    s.DitherOffsetX = ditherPhases[f % ditherPhases.Length][0];
                    s.DitherOffsetY = ditherPhases[f % ditherPhases.Length][1];
                    return renderDeres(s);
                }
                if (signal != null)
                {
                    int step = Signal.EngineStep(fps);
                    List<AnimFrame> frames = Make(frameCount, f =>
                    {
                        Rendered fr = RenderAt(f);
                        return SignalFrame(signal(fr, f * step), width, height, signalFx, (double)f / frameCount, fr.Paper);
                    });
                    return EncodeFrames(output, frames, o.Apng, width, height, delayMs, seeThrough, o.Cancel);
                }
                if (Export.WithFx(o.Fx, m))
                {
                    List<AnimFrame> frames = Make(frameCount, f => FxFrame(RenderAt(f), width, height, o.Fx, (double)f / frameCount));
                    return EncodeFrames(output, frames, o.Apng, width, height, delayMs, seeThrough, o.Cancel);
                }

                // Indexed, with no screen effect: the machine's palette when its pixels are entries
                // of it, else exactly the colours the frames use, while they fit one. Kept
                // see-through pixels take one more entry; an APNG with a full palette goes r, g, b, a.
                var renders = new List<Rendered>();
                for (int f = 0; f < frameCount; f++)
                {
                    o.Cancel.ThrowIfCancellationRequested();
                    renders.Add(RenderAt(f));
                    Progress(f + 1, frameCount);
                }
                byte[] Shows(Rendered fr) => seeThrough ? fr.Alpha : null;
                byte[] palette = r.Palette;
                Func<Rendered, byte[]> entries = fr => fr.Indices;
                if (!r.Indexed)
                {
                    var census = new ColourCensus();
                    foreach (Rendered fr in renders) census.Add(fr.Rgb, Shows(fr));
                    byte[] exact = census.Palette();
                    palette = exact;
                    if (exact != null) entries = fr => Census.EntriesIn(exact, fr.Rgb, Shows(fr));
                }
                int colours = palette != null ? palette.Length / 3 : 0;
                if (palette != null && colours <= 256 && colours > 0 && !(seeThrough && o.Apng && colours == 256))
                {
                    var indexed = renders.Select(fr => (Indices: UpscaleIndices(entries(fr), fr.Width, fr.Height, width, height),
                        Alpha: seeThrough && fr.Alpha != null ? UpscaleIndices(fr.Alpha, fr.Width, fr.Height, width, height) : null)).ToList();
                    if (o.Apng)
                    {
                        var apngFrames = indexed.Select(f =>
                        {
                            if (f.Alpha != null) for (long i = 0; i < f.Indices.LongLength; i++) if (f.Alpha[i] == 0) f.Indices[i] = (byte)colours;
                            return new ApngFrame { DelayMs = delayMs, Indices = f.Indices };
                        }).ToList();
                        byte[] apngPalette = seeThrough ? palette.Concat(new byte[3]).ToArray() : palette;
                        var writer = new ApngWriter(output, width, height, apngFrames.Count, 0, apngPalette, seeThrough ? colours : -1);
                        writer.AddAll(apngFrames);
                        writer.Finish();
                        return new AnimationResult { Apng = true, Width = width, Height = height, Frames = frameCount };
                    }
                    var gifFrames = indexed.Select(f =>
                    {
                        if (!seeThrough) return new GifFrame { Width = width, Height = height, DelayMs = delayMs, Indices = f.Indices };
                        GifColours.WithTransparentEntry(palette, f.Indices, f.Alpha ?? Solid(f.Indices.Length), out byte[] pal, out byte[] idx, out int t);
                        return new GifFrame { Width = width, Height = height, DelayMs = delayMs, Indices = idx, Palette = pal, TransparentIndex = t };
                    }).ToList();
                    GifWriter.Write(output, gifFrames, 0, gifFrames[0].Palette ?? palette);
                    return new AnimationResult { Width = width, Height = height, Frames = frameCount };
                }

                // full colour, for the dither shimmer without the screen effect
                return EncodeFrames(output, renders.Select(fr => Enlarged(fr, width, height)).ToList(), o.Apng, width, height, delayMs, seeThrough, o.Cancel);
            }
        }

        static byte[] Solid(int n)
        {
            var a = new byte[n];
            for (int i = 0; i < n; i++) a[i] = 255;
            return a;
        }

        // full-colour frames as a looping APNG or GIF
        static AnimationResult EncodeFrames(Stream output, List<AnimFrame> frames, bool apng, int width, int height, int delayMs, bool seeThrough, CancellationToken cancel)
        {
            if (apng)
            {
                var writer = new ApngWriter(output, width, height, frames.Count);
                for (int start = 0; start < frames.Count; start += 8)
                {
                    cancel.ThrowIfCancellationRequested();
                    writer.AddAll(frames.Skip(start).Take(8).Select(f => ApngFrameOf(f, delayMs, seeThrough)).ToList());
                }
                writer.Finish();
                return new AnimationResult { Apng = true, Width = width, Height = height, Frames = frames.Count };
            }
            GifColours.WriteRgb(output, frames.Select(f => new AnimFrame { Rgb = f.Rgb, Alpha = seeThrough ? f.Alpha : null }).ToList(), width, height,
                Enumerable.Repeat(delayMs, frames.Count).ToArray(), 0, null, cancel);
            return new AnimationResult { Width = width, Height = height, Frames = frames.Count };
        }

        // an APNG frame from a full-colour one: r, g, b, a when see-through pixels are kept
        static ApngFrame ApngFrameOf(AnimFrame f, int delayMs, bool seeThrough) =>
            seeThrough ? new ApngFrame { DelayMs = delayMs, Rgba = RgbaOf(f) } : new ApngFrame { DelayMs = delayMs, Rgb = f.Rgb };

        // r, g, b, a a pixel (no mask: every pixel shows)
        static byte[] RgbaOf(AnimFrame f)
        {
            long n = f.Rgb.LongLength / 3;
            var output = new byte[n * 4];
            for (long i = 0; i < n; i++)
            {
                output[i * 4] = f.Rgb[i * 3];
                output[i * 4 + 1] = f.Rgb[i * 3 + 1];
                output[i * 4 + 2] = f.Rgb[i * 3 + 2];
                output[i * 4 + 3] = f.Alpha != null ? f.Alpha[i] : (byte)255;
            }
            return output;
        }

        // ---- an animated GIF's own frames ----

        // Every frame of an animated GIF through the settings, as a GIF or APNG with the GIF's own
        // delays and loop count, frames going into the file as they're made, so any number fit. A
        // full-colour GIF needs every frame for its shared palette: it holds them while they fit the
        // budget, and past it makes them twice (once for their colours, once to write).
        public static AnimationResult FramesOf(Stream output, FramesOptions o)
        {
            int total = o.DelaysMs.Length;
            if (total == 0) throw new AnimationException("There are no frames to export.");
            int m = Math.Max(1, o.Multiplier);
            Rendered first = o.Render(0, 0);
            Export.Size(first.Width, first.Height, first.Geometry.ParX, first.Geometry.ParY, m, o.Tv, out int width, out int height);
            if (Limit(width, height, 0, o.Limits) != null) throw new AnimationException("That size is too big for an animation. Pick a smaller size.");
            FxSettings fx = Export.WithFx(o.Fx, m) ? o.Fx : null;
            bool indexed = o.Indexed && first.Indexed && o.Signal == null && fx == null;
            bool seeThrough = o.SeeThrough;
            int lanes = Math.Max(1, o.Lanes);
            void Progress(int done) => o.Progress?.Invoke(done, total, null);

            // frame k at the export size, full colour
            AnimFrame RgbFrame(Rendered r, int k)
            {
                double phase = o.Phase != null ? o.Phase(k) : (double)k / total;
                if (o.Signal != null) return SignalFrame(o.Signal(r, k), width, height, fx, phase, r.Paper);
                if (fx != null) return FxFrame(r, width, height, fx, phase);
                return Enlarged(r, width, height);
            }
            Rendered FrameAt(int k, int lane) => k == 0 ? first : o.Render(k, lane);
            // which of frame r's pixels show at the export size (null: all of them)
            byte[] AlphaOf(Rendered r) => seeThrough && r.Alpha != null ? UpscaleIndices(r.Alpha, r.Width, r.Height, width, height) : null;

            if (o.Apng)
            {
                // kept see-through pixels in a shared palette take one more entry; a full palette has
                // no room, so those frames go r, g, b, a instead
                int colours = first.Palette.Length / 3;
                bool shared = indexed && o.SharedPalette && !(seeThrough && colours >= 256);
                byte[] palette = shared && seeThrough ? first.Palette.Concat(new byte[3]).ToArray() : first.Palette;
                var writer = new ApngWriter(output, width, height, total, ApngPlays(o.LoopCount), shared ? palette : null, shared && seeThrough ? colours : -1);
                int done = 0;
                foreach (ApngFrame[] window in Windows(total, lanes, o.Cancel, (k, lane) =>
                {
                    Rendered r = FrameAt(k, lane);
                    int delayMs = o.DelaysMs[k];
                    if (!shared) return ApngFrameOf(RgbFrame(r, k), delayMs, seeThrough);
                    byte[] indices = UpscaleIndices(r.Indices, r.Width, r.Height, width, height);
                    byte[] alpha = AlphaOf(r);
                    if (alpha != null) for (long i = 0; i < indices.LongLength; i++) if (alpha[i] == 0) indices[i] = (byte)colours;
                    return new ApngFrame { DelayMs = delayMs, Indices = indices };
                }))
                {
                    writer.AddAll(window);
                    done += window.Length;
                    Progress(done);
                }
                writer.Finish();
                return new AnimationResult { Apng = true, Width = width, Height = height, Frames = total };
            }

            if (indexed)
            {
                // each frame with its own palette: a machine that picks its colours from the
                // picture can pick different ones a frame (those frames get a colour table of their own)
                var writer = new GifWriter(output, width, height, o.LoopCount);
                int done = 0;
                foreach (GifFrame[] window in Windows(total, lanes, o.Cancel, (k, lane) =>
                {
                    Rendered r = FrameAt(k, lane);
                    byte[] indices = UpscaleIndices(r.Indices, r.Width, r.Height, width, height);
                    if (!seeThrough) return new GifFrame { Width = width, Height = height, DelayMs = o.DelaysMs[k], Indices = indices, Palette = r.Palette };
                    GifColours.WithTransparentEntry(r.Palette, indices, AlphaOf(r) ?? Solid(indices.Length), out byte[] pal, out byte[] idx, out int t);
                    return new GifFrame { Width = width, Height = height, DelayMs = o.DelaysMs[k], Indices = idx, Palette = pal, TransparentIndex = t };
                }))
                {
                    writer.AddAll(window);
                    done += window.Length;
                    Progress(done);
                }
                writer.Finish();
                return new AnimationResult { Width = width, Height = height, Frames = total };
            }

            // Full colour: one palette for every frame (so still parts don't shimmer), which needs
            // every frame first. While they fit the budget they're held, and each is let go once
            // it's written. Kept see-through pixels get an entry of their own after up to 255
            // colours. Past the budget, every frame is made twice: the first time only its colours
            // are sampled (the same samples the held frames give), the second time it's written.
            long budget = o.Budget ?? o.Limits.Budget;
            byte[] colours2;
            Func<IEnumerable<AnimFrame[]>> second;
            if ((long)width * height * total > budget)
            {
                var samples = new PaletteSamples((long)total * width * height);
                int read = 0;
                foreach (AnimFrame[] window in Windows(total, lanes, o.Cancel, (k, lane) => RgbFrame(FrameAt(k, lane), k)))
                    foreach (AnimFrame f in window)
                    {
                        samples.Add(f.Rgb, width * height, seeThrough ? f.Alpha : null);
                        o.Progress?.Invoke(++read, total, "reading the colours");
                    }
                colours2 = samples.Palette(seeThrough ? 255 : 256);
                second = () => Windows(total, lanes, o.Cancel, (k, lane) => RgbFrame(FrameAt(k, lane), k));
            }
            else
            {
                var held = new List<AnimFrame>();
                foreach (AnimFrame[] window in Windows(total, lanes, o.Cancel, (k, lane) => RgbFrame(FrameAt(k, lane), k)))
                {
                    held.AddRange(window);
                    Progress(held.Count);
                }
                colours2 = GifColours.Palette(held.Select(f => f.Rgb).ToList(), width * height, seeThrough ? held.Select(f => f.Alpha).ToList() : null);
                second = () => HeldWindows(held);
            }
            int ti = -1;
            byte[] withEntry = seeThrough ? GifColours.TransparentAfter(colours2, out ti) : null;
            var nearest = new NearestColour(colours2);
            var shared2 = new GifWriter(output, width, height, o.LoopCount, withEntry ?? colours2);
            int written = 0;
            foreach (AnimFrame[] window in second())
            {
                o.Cancel.ThrowIfCancellationRequested();
                var batch = new GifFrame[window.Length];
                for (int i = 0; i < window.Length; i++)
                {
                    int k = written + i;
                    AnimFrame f = window[i];
                    byte[] indices = nearest.Map(f.Rgb);
                    if (ti >= 0 && f.Alpha != null) for (long p = 0; p < indices.LongLength; p++) if (f.Alpha[p] == 0) indices[p] = (byte)ti;
                    batch[i] = new GifFrame { Width = width, Height = height, DelayMs = o.DelaysMs[k], Indices = indices, TransparentIndex = ti };
                    window[i] = null;
                }
                shared2.AddAll(batch);
                written += window.Length;
                if ((long)width * height * total > budget) Progress(written);
            }
            shared2.Finish();
            return new AnimationResult { Width = width, Height = height, Frames = total };
        }

        // the held frames, a window at a time, each let go once it's handed over
        static IEnumerable<AnimFrame[]> HeldWindows(List<AnimFrame> held)
        {
            for (int start = 0; start < held.Count; start += 8)
            {
                AnimFrame[] window = held.Skip(start).Take(8).ToArray();
                for (int i = start; i < start + window.Length; i++) held[i] = null;
                yield return window;
            }
        }

        // Frames 0 … count-1 in order, `lanes` of them made at once (lane i of a window on its own
        // engine): what's made doesn't depend on how many lanes there are.
        static IEnumerable<T[]> Windows<T>(int count, int lanes, CancellationToken cancel, Func<int, int, T> make)
        {
            int size = Math.Max(lanes, 1);
            // (a window of at least a few frames, so their compressing goes on every core too)
            if (size < 4) size = Math.Min(4, Math.Max(1, count));
            for (int start = 0; start < count; start += size)
            {
                cancel.ThrowIfCancellationRequested();
                int n = Math.Min(size, count - start);
                var made = new T[n];
                if (lanes <= 1) for (int i = 0; i < n; i++) made[i] = make(start + i, 0);
                else
                {
                    // lane j makes frames j, j + lanes, … of the window, one after another
                    EveryCore.For(0, Math.Min(lanes, n), lane =>
                    {
                        for (int i = lane; i < n; i += lanes) made[i] = make(start + i, lane);
                    });
                }
                yield return made;
            }
        }

        // ---- frames ----

        // a renderer's rows into a frame, on every core
        static AnimFrame Draw(RowRenderer render, bool withAlpha)
        {
            int w = render.Width, h = render.Height;
            var rgb = new byte[(long)w * h * 3];
            byte[] alpha = withAlpha ? new byte[(long)w * h] : null;
            EveryCore.For(0, h, () => (new byte[w * 3], new byte[w]), (y, state, buffers) =>
            {
                render.Row(y, buffers.Item1, buffers.Item2);
                Buffer.BlockCopy(buffers.Item1, 0, rgb, y * w * 3, w * 3);
                if (alpha != null) Buffer.BlockCopy(buffers.Item2, 0, alpha, y * w, w);
                return buffers;
            });
            return new AnimFrame { Rgb = rgb, Alpha = alpha };
        }

        // a picture at the export size through the screen effect
        public static AnimFrame FxFrame(Rendered r, int width, int height, FxSettings fx, double phase) =>
            Draw(new FxRenderer(r.Rgb, r.Width, r.Height, r.Paper, r.Alpha, width, height, fx, phase), r.Alpha != null);

        // the signal at the export size, through the screen effect when there is one
        public static AnimFrame SignalFrame(SignalImage img, int width, int height, FxSettings fx, double phase, byte[] paper) =>
            Draw(Signal.Renderer(img, width, height, fx, phase, paper), img.Alpha != null);

        // a picture enlarged to the export size, nearest neighbour
        public static AnimFrame Enlarged(Rendered r, int width, int height) =>
            new AnimFrame { Rgb = UpscaleRgb(r.Rgb, r.Width, r.Height, width, height), Alpha = r.Alpha != null ? UpscaleIndices(r.Alpha, r.Width, r.Height, width, height) : null };

        static int[] Columns(int srcW, int dstW)
        {
            var column = new int[dstW];
            for (int x = 0; x < dstW; x++) column[x] = (int)Math.Min(srcW - 1, (long)x * srcW / dstW);
            return column;
        }

        static byte[] UpscaleRgb(byte[] src, int srcW, int srcH, int dstW, int dstH)
        {
            var output = new byte[(long)dstW * dstH * 3];
            int[] column = Columns(srcW, dstW);
            EveryCore.For(0, dstH, y =>
            {
                long sy = Math.Min(srcH - 1, (long)y * srcH / dstH) * srcW * 3, o = (long)y * dstW * 3;
                for (int x = 0; x < dstW; x++, o += 3)
                {
                    long s = sy + column[x] * 3;
                    output[o] = src[s];
                    output[o + 1] = src[s + 1];
                    output[o + 2] = src[s + 2];
                }
            });
            return output;
        }

        static byte[] UpscaleIndices(byte[] src, int srcW, int srcH, int dstW, int dstH)
        {
            var output = new byte[(long)dstW * dstH];
            int[] column = Columns(srcW, dstW);
            EveryCore.For(0, dstH, y =>
            {
                long sy = Math.Min(srcH - 1, (long)y * srcH / dstH) * srcW, o = (long)y * dstW;
                for (int x = 0; x < dstW; x++) output[o + x] = src[sy + column[x]];
            });
            return output;
        }

        // the original photo at the export size, nearest neighbour (the website's resampleRgbaToRgb)
        static byte[] SourceRgb(PictureSource src, int dstW, int dstH)
        {
            var output = new byte[(long)dstW * dstH * 3];
            int[] column = Columns(src.Width, dstW);
            EveryCore.For(0, dstH, y =>
            {
                long row = Math.Min(src.Height - 1, (long)y * src.Height / dstH) * src.Width * 4, o = (long)y * dstW * 3;
                for (int x = 0; x < dstW; x++, o += 3)
                {
                    long s = row + (long)column[x] * 4;
                    output[o] = src.Bgra[s + 2];
                    output[o + 1] = src.Bgra[s + 1];
                    output[o + 2] = src.Bgra[s];
                }
            });
            return output;
        }

        // which of the original's pixels show (from half covered) at the export size
        static byte[] SourceAlpha(PictureSource src, int dstW, int dstH)
        {
            var output = new byte[(long)dstW * dstH];
            int[] column = Columns(src.Width, dstW);
            EveryCore.For(0, dstH, y =>
            {
                long row = Math.Min(src.Height - 1, (long)y * src.Height / dstH) * src.Width * 4, o = (long)y * dstW;
                for (int x = 0; x < dstW; x++) output[o + x] = src.Bgra[row + (long)column[x] * 4 + 3] >= 128 ? (byte)255 : (byte)0;
            });
            return output;
        }
    }
}
