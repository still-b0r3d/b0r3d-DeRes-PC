using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using B0r3dDeRes.Engine;

namespace B0r3dDeRes.Tests
{
    // An APNG by its parts: every chunk in order, its header, frame count and plays, palette and
    // see-through entries (hex), each frame's control (hex, and "seqN" for an fdAT's number), and
    // the hash of every frame's unpacked rows together.
    sealed class ApngCase
    {
        public readonly string Chunks, Ihdr, Actl, Plte, Trns, Fctl, Rows;
        public readonly int RowBytes;

        public ApngCase(string chunks, string ihdr, string actl, string plte, string trns, string fctl, string rows, int rowBytes)
        {
            Chunks = chunks; Ihdr = ihdr; Actl = actl; Plte = plte; Trns = trns; Fctl = fctl; Rows = rows; RowBytes = rowBytes;
        }
    }

    // One animation the website made: a loop of a picture above (Render), or a GIF's own frames
    // (Gif, through Profile on Background), with its settings, then what came out.
    sealed class AnimCase
    {
        public int Render = -1, M, LoopFrames = 16, LoopFps = 15, Budget = -1, Width, Height, Frames, Length;
        public string Gif = "", Profile = "", Background = "", Style = Animation.GifFrames, Sha = "";
        public bool Apng, Tv, SeeThrough, Indexed, Shared;
        public FxSettings Fx;
        public SignalSettings Signal;
        public ApngCase Png;

        public override string ToString() => (Render >= 0 ? Website.Renders[Render].ToString() + " " + Style : Gif + " on " + Profile + " " + Background) + " " + (Apng ? "APNG" : "GIF") +
            " at " + M + "×" + (Tv ? " as on a TV" : "") + (Fx != null ? " " + Fx.Screen : "") + (Signal != null ? " through " + Signal.Mode : "") + (Budget >= 0 ? " made twice" : "");
    }

    // GIFs and loops against the website's own: its decoder, its GIF and APNG writers, its loops
    // and an animated GIF's frames through the settings (WebsiteCases.cs, from its engine in Node).
    static partial class TestMain
    {
        // ---- test inputs made the way the website's test-file maker makes them ----

        static Func<uint> Lcg(uint seed) => () => seed = unchecked(seed * 1103515245u + 12345u);

        static byte[] NoiseBytes(int n, int mod, uint seed)
        {
            Func<uint> next = Lcg(seed);
            var output = new byte[n];
            for (int i = 0; i < n; i++) output[i] = (byte)((next() >> 16) % (uint)mod);
            return output;
        }

        static byte[] RunBytes(int n, int mod, uint seed)
        {
            Func<uint> next = Lcg(seed);
            var output = new byte[n];
            int v = 0;
            for (int i = 0; i < n; i++)
            {
                if (((next() >> 16) & 15) == 0) v = (int)((next() >> 16) % (uint)mod);
                output[i] = (byte)v;
            }
            return output;
        }

        static byte[] TestBytes(string kind, int n, int mod, int seed) => kind == "runs" ? RunBytes(n, mod, (uint)seed) : NoiseBytes(n, mod, (uint)seed);

        static byte[] PalOf(int n, int seed) => NoiseBytes(n * 3, 256, (uint)seed);

        static byte[] Mask(int n, int seed) => NoiseBytes(n, 2, (uint)seed).Select(v => (byte)(v * 255)).ToArray();

        static int? LoopOf(string text) => text == "default" ? 0 : text == "none" ? (int?)null : int.Parse(text);

        static byte[] GifFile(string name)
        {
            string file = Path.Combine(TestsFolder, "website", name + ".gif");
            if (!File.Exists(file)) throw new SkipException(file + " isn't there (build the tests first)");
            return File.ReadAllBytes(file);
        }

        static string StopName(GifStop stop) => stop == GifStop.Damaged ? "damaged" : stop == GifStop.TooManyFrames ? "too-many-frames" : stop == GifStop.TooSlow ? "too-slow" : "";

        // a frame's b, g, r, a as the website's r, g, b, a
        static byte[] Rgba(byte[] bgra)
        {
            var rgba = new byte[bgra.Length];
            for (int i = 0; i < bgra.Length; i += 4)
            {
                rgba[i] = bgra[i + 2];
                rgba[i + 1] = bgra[i + 1];
                rgba[i + 2] = bgra[i];
                rgba[i + 3] = bgra[i + 3];
            }
            return rgba;
        }

        // what DeRes's decoder reads in a GIF, in the website's words
        static (string Error, int Count, string Version, int Width, int Height, int Loop, string Stop, string Frames) ReadGif(byte[] bytes, GifLimits limits)
        {
            int count = GifInfo.FrameCount(bytes);
            GifReader reader;
            try
            {
                reader = new GifReader(bytes, limits);
            }
            catch (GifException x)
            {
                return (x.Message, count, "", 0, 0, -1, "", "");
            }
            var frames = new List<string>();
            foreach (DecodedGifFrame f in reader.Frames())
                frames.Add(string.Join(" ", f.DelayMs, f.Disposal, f.Rect.X, f.Rect.Y, f.Rect.W, f.Rect.H, f.Interlaced ? 1 : 0, Sha(f.Rgba)));
            return ("", count, reader.Version, reader.Width, reader.Height, reader.LoopCount ?? -1, StopName(reader.Stop), string.Join("|", frames));
        }

        static void GifsReadLikeTheWebsite()
        {
            var wrong = new List<string>();
            foreach (var c in Website.GifDecodes)
            {
                var limits = new GifLimits();
                if (c.MaxPixels >= 0) limits.MaxPixels = c.MaxPixels;
                if (c.MaxFrames >= 0) limits.MaxFrames = c.MaxFrames;
                var got = ReadGif(GifFile(c.File), limits);
                var why = new List<string>();
                if (got.Error != c.Error) why.Add("\"" + got.Error + "\", not \"" + c.Error + "\"");
                if (got.Count != c.Count) why.Add(got.Count + " frames counted, not " + c.Count);
                if (got.Version != c.Version || got.Width != c.Width || got.Height != c.Height) why.Add("header " + got.Version + " " + got.Width + "×" + got.Height);
                if (got.Loop != c.Loop) why.Add("loop " + got.Loop);
                if (got.Stop != c.Stop) why.Add("stopped " + got.Stop);
                if (got.Frames != c.Frames)
                {
                    string[] mine = got.Frames.Split('|'), theirs = c.Frames.Split('|');
                    int at = Enumerable.Range(0, Math.Min(mine.Length, theirs.Length)).FirstOrDefault(i => mine[i] != theirs[i]);
                    why.Add(mine.Length + " frames for " + theirs.Length + ", frame " + at + ": " + (at < mine.Length ? mine[at] : "none") + " for " + (at < theirs.Length ? theirs[at] : "none"));
                }
                if (why.Count > 0) wrong.Add(c.File + (c.MaxPixels >= 0 || c.MaxFrames >= 0 ? " (limited)" : "") + ": " + string.Join(", ", why));
            }
            foreach (var c in Website.NotGifs)
            {
                byte[] bytes = Enumerable.Range(0, c.Hex.Length / 2).Select(i => Convert.ToByte(c.Hex.Substring(i * 2, 2), 16)).ToArray();
                var got = ReadGif(bytes, new GifLimits());
                if (got.Error != c.Error || got.Count != c.Count || got.Stop != c.Stop || got.Frames != c.Frames) wrong.Add(c.Hex + ": \"" + got.Error + "\" " + got.Count + " " + got.Stop);
                if (GifInfo.Shape(new MemoryStream(bytes), out _, out _, out _)) wrong.Add(c.Hex + ": read through as a whole GIF");
            }
            // read through without decoding (the batch's check of a GIF too big to hold): a whole
            // GIF's size and frames, and nothing whole once it's cut short
            foreach (var c in Website.GifDecodes.Where(d => d.MaxPixels < 0 && d.MaxFrames < 0 && d.Error == "" && d.Stop == ""))
            {
                byte[] bytes = GifFile(c.File);
                if (bytes[bytes.Length - 1] != 0x3b) continue;
                bool whole = GifInfo.Shape(new MemoryStream(bytes), out int w, out int h, out int n);
                if (!whole || w != c.Width || h != c.Height || n != c.Count) wrong.Add(c.File + " read through: " + whole + ", " + w + "×" + h + ", " + n + " frames");
                if (GifInfo.Shape(new MemoryStream(bytes, 0, bytes.Length - 1), out _, out _, out _)) wrong.Add(c.File + " without its end read through as whole");
            }
            Check(wrong.Count == 0, wrong.Count + " of " + (Website.GifDecodes.Length + Website.NotGifs.Length) + " differ: " + string.Join("; ", wrong.Take(5)));
        }

        static void GifsOpenLikeTheWebsite()
        {
            var wrong = new List<string>();
            foreach (var c in Website.GifOpens)
            {
                AnimatedGif a = GifFrames.Open(GifFile(c.File), c.MaxEdge, c.Budget);
                string what = c.File + " at most " + c.MaxEdge + " px, " + c.Budget + " held";
                if (a == null || !c.Ok)
                {
                    if ((a != null) != c.Ok) wrong.Add(what + ": " + (a == null ? "didn't open" : "opened, the website's didn't"));
                    continue;
                }
                var why = new List<string>();
                if (a.Width != c.Width || a.Height != c.Height || a.OrigW != c.OrigW || a.OrigH != c.OrigH) why.Add(a.Width + "×" + a.Height + " of " + a.OrigW + "×" + a.OrigH);
                if (a.Frames.Length != c.Frames || a.Total != c.Total) why.Add(a.Frames.Length + " of " + a.Total + " frames");
                if (a.HasAlpha != c.HasAlpha) why.Add("see-through " + a.HasAlpha);
                if ((a.LoopCount ?? -1) != c.Loop) why.Add("loop " + a.LoopCount);
                if (string.Join(",", a.DelaysMs) != c.Delays) why.Add("delays " + string.Join(",", a.DelaysMs));
                string[] shas = c.Shas.Split(',');
                int bad = Enumerable.Range(0, Math.Min(shas.Length, a.Frames.Length)).Count(k => Sha(Rgba(a.Frames[k].Bgra)) != shas[k]);
                if (bad > 0) why.Add(bad + " frames' pixels");
                if (a.Frames.Any(f => f.HasAlpha != c.HasAlpha)) why.Add("a frame's see-through flag");
                if (a.Frames.Any(f => f.OrigW != c.OrigW || f.OrigH != c.OrigH)) why.Add("a frame's own size");
                if (why.Count > 0) wrong.Add(what + ": " + string.Join(", ", why));
            }
            foreach (var p in Website.GifPlans)
            {
                GifFrames.Plan(p.W, p.H, p.Total, p.MaxEdge, p.Budget, out int w, out int h, out int keep);
                if (w != p.Width || h != p.Height || keep != p.Keep) wrong.Add(p.W + "×" + p.H + " × " + p.Total + ": " + w + "×" + h + " keeping " + keep + ", not " + p.Width + "×" + p.Height + " keeping " + p.Keep);
            }
            Check(wrong.Count == 0, wrong.Count + " differ: " + string.Join("; ", wrong.Take(5)));
        }

        static void GifAndApngWritersLikeTheWebsite()
        {
            var wrong = new List<string>();
            foreach (var c in Website.GifEncodes)
            {
                var frames = c.Frames.Split(';').Select(f =>
                {
                    string[] v = f.Split(' ');
                    int w = int.Parse(v[0]), h = int.Parse(v[1]), delay = int.Parse(v[2]), mod = int.Parse(v[4]), seed = int.Parse(v[5]), palN = int.Parse(v[6]), palSeed = int.Parse(v[7]), t = int.Parse(v[8]);
                    return new GifFrame { Width = w, Height = h, DelayMs = delay >= 0 ? delay : 100, Indices = TestBytes(v[3], w * h, mod, seed), Palette = palN >= 0 ? PalOf(palN, palSeed) : null, TransparentIndex = t };
                }).ToList();
                using (var bytes = new MemoryStream())
                {
                    GifWriter.Write(bytes, frames, LoopOf(c.Loop), c.GlobalN > 0 ? PalOf(c.GlobalN, c.GlobalSeed) : null);
                    byte[] gif = bytes.ToArray();
                    if (Sha(gif) != c.Sha || gif.Length != c.Length) wrong.Add("the GIF of " + c.Frames + " (" + gif.Length + " bytes for " + c.Length + ")");
                    // read through as the batch reads one back
                    bool whole = GifInfo.Shape(new MemoryStream(gif), out int w, out int h, out int n);
                    if (!whole || w != frames[0].Width || h != frames[0].Height || n != frames.Count || GifInfo.Shape(new MemoryStream(gif, 0, gif.Length / 2), out _, out _, out _))
                        wrong.Add("the GIF of " + c.Frames + " read through: " + whole + ", " + w + "×" + h + ", " + n + " frames");
                }
            }
            foreach (var c in Website.RgbGifs)
            {
                var specs = c.Frames.Split(';').Select(f => f.Split(' ')).ToList();
                var frames = specs.Select(v => new AnimFrame { Rgb = TestBytes(v[1], c.W * c.H * 3, 256, int.Parse(v[0])), Alpha = int.Parse(v[3]) >= 0 ? Mask(c.W * c.H, int.Parse(v[3])) : null }).ToList();
                using (var bytes = new MemoryStream())
                {
                    GifColours.WriteRgb(bytes, frames, c.W, c.H, specs.Select(v => int.Parse(v[2])).ToArray(), LoopOf(c.Loop), c.PalN > 0 ? PalOf(c.PalN, c.PalSeed) : null);
                    byte[] gif = bytes.ToArray();
                    if (Sha(gif) != c.Sha || gif.Length != c.Length) wrong.Add("the full-colour GIF of " + c.Frames + " (" + gif.Length + " bytes for " + c.Length + ")");
                }
            }
            foreach (var c in Website.SharedPalettes)
            {
                var rgbs = Enumerable.Range(0, c.Frames).Select(k => RunBytes(c.Pixels * 3, 256, (uint)(c.Seed + k * 10))).ToList();
                var alphas = c.AlphaSeed >= 0 ? Enumerable.Range(0, c.Frames).Select(k => Mask(c.Pixels, c.AlphaSeed + k)).ToList() : null;
                string got = Hex(GifColours.Palette(rgbs, c.Pixels, alphas));
                if (got != c.Palette) wrong.Add("the shared palette of " + c.Frames + " × " + c.Pixels + " pixels");
            }
            foreach (var c in Website.TransparentEntries)
            {
                byte[] alpha = NoiseBytes(c.Count, 3, (uint)(c.Seed + 200)).Select(v => v != 0 ? (byte)255 : (byte)0).ToArray();
                GifColours.WithTransparentEntry(PalOf(c.N, c.Seed), NoiseBytes(c.Count, c.N, (uint)(c.Seed + 100)), alpha, out byte[] pal, out byte[] indices, out int t);
                if (t != c.Index || Sha(indices) != c.Indices || Sha(pal) != c.Palette) wrong.Add("the see-through entry of " + c.N + " colours: " + t + " for " + c.Index);
            }
            foreach (var c in Website.Nearests)
            {
                var nearest = new NearestColour(PalOf(c.N, c.Seed));
                if (Sha(nearest.Map(NoiseBytes(c.Pixels * 3, 256, (uint)(c.Seed + 1)))) != c.Indices) wrong.Add("the nearest of " + c.N + " colours");
            }
            foreach (var c in Website.ApngEncodes)
            {
                var specs = c.Frames.Split(';').Select(f => f.Split(' ')).ToList();
                int n = c.W * c.H;
                using (var bytes = new MemoryStream())
                {
                    var writer = new ApngWriter(bytes, c.W, c.H, specs.Count, c.Plays >= 0 ? c.Plays : 0, c.Mode == "indexed" ? PalOf(c.Colours, c.PalSeed) : null, c.T);
                    foreach (string[] v in specs)
                    {
                        int seed = int.Parse(v[0]), delay = int.Parse(v[2]);
                        var f = new ApngFrame { DelayMs = delay };
                        if (c.Mode == "indexed") f.Indices = TestBytes(v[1], n, c.Colours, seed);
                        else if (c.Mode == "rgba") f.Rgba = TestBytes(v[1], n * 4, 256, seed);
                        else f.Rgb = TestBytes(v[1], n * 3, 256, seed);
                        writer.Add(f);
                    }
                    writer.Finish();
                    byte[] png = bytes.ToArray();
                    string why = SameApng(png, c.Png);
                    if (why != null) wrong.Add("the " + c.Mode + " APNG of " + c.Frames + ": " + why);
                    // read through as the batch reads one back
                    bool whole = ApngInfo.Shape(new MemoryStream(png), out int w, out int h, out int count);
                    if (!whole || w != c.W || h != c.H || count != specs.Count || ApngInfo.Shape(new MemoryStream(png, 0, png.Length - 12), out _, out _, out _))
                        wrong.Add("the " + c.Mode + " APNG of " + c.Frames + " read through: " + whole + ", " + w + "×" + h + ", " + count + " frames");
                    // its acTL saying a frame more than there are
                    byte[] miscounted = (byte[])png.Clone();
                    int actl = Enumerable.Range(8, png.Length - 12).First(i => png[i] == 'a' && png[i + 1] == 'c' && png[i + 2] == 'T' && png[i + 3] == 'L');
                    miscounted[actl + 7]++;
                    if (ApngInfo.Shape(new MemoryStream(miscounted), out _, out _, out _)) wrong.Add("the " + c.Mode + " APNG of " + c.Frames + " read through as whole with a frame too many said");
                }
            }
            Check(wrong.Count == 0, wrong.Count + " differ: " + string.Join("; ", wrong.Take(5)));
        }

        // what's different between an APNG made here and the website's (by its parts), or null
        static string SameApng(byte[] png, ApngCase want)
        {
            var kinds = new List<string>();
            var controls = new List<string>();
            var rows = new MemoryStream();
            string ihdr = "", actl = "", plte = "", trns = "";
            for (int o = 8; o < png.Length;)
            {
                int len = (png[o] << 24) | (png[o + 1] << 16) | (png[o + 2] << 8) | png[o + 3];
                string type = Encoding.ASCII.GetString(png, o + 4, 4);
                byte[] data = png.Skip(o + 8).Take(len).ToArray();
                kinds.Add(type);
                if (type == "IHDR") ihdr = Hex(data);
                if (type == "acTL") actl = Hex(data);
                if (type == "PLTE") plte = Hex(data);
                if (type == "tRNS") trns = Hex(data);
                if (type == "fcTL") controls.Add(Hex(data));
                if (type == "IDAT") Inflate(data, 0, rows);
                if (type == "fdAT")
                {
                    controls.Add("seq" + ((uint)((data[0] << 24) | (data[1] << 16) | (data[2] << 8) | data[3])));
                    Inflate(data, 4, rows);
                }
                o += 12 + len;
            }
            var why = new List<string>();
            if (string.Join(",", kinds) != want.Chunks) why.Add("chunks " + string.Join(",", kinds));
            if (ihdr != want.Ihdr) why.Add("header " + ihdr);
            if (actl != want.Actl) why.Add("frames/plays " + actl);
            if (plte != want.Plte || trns != want.Trns) why.Add("palette");
            if (string.Join(",", controls) != want.Fctl) why.Add("frame controls " + string.Join(",", controls).Substring(0, Math.Min(80, string.Join(",", controls).Length)));
            byte[] all = rows.ToArray();
            if (all.Length != want.RowBytes || Sha(all) != want.Rows) why.Add("rows");
            return why.Count > 0 ? string.Join(", ", why) : null;
        }

        // zlib data (after `skip` bytes) unpacked onto `into`
        static void Inflate(byte[] data, int skip, Stream into)
        {
            using (var z = new DeflateStream(new MemoryStream(data, skip + 2, data.Length - skip - 6), CompressionMode.Decompress)) z.CopyTo(into);
        }

        // ---- the loops ----

        static string SameAnimation(byte[] made, AnimationResult result, AnimCase c)
        {
            var why = new List<string>();
            if (result.Width != c.Width || result.Height != c.Height || result.Frames != c.Frames) why.Add(result.Width + "×" + result.Height + " × " + result.Frames + " frames");
            if (c.Apng)
            {
                string apng = SameApng(made, c.Png);
                if (apng != null) why.Add(apng);
            }
            else if (Sha(made) != c.Sha || made.Length != c.Length) why.Add("the GIF's bytes (" + made.Length + " for " + c.Length + ")");
            return why.Count > 0 ? string.Join(", ", why) : null;
        }

        static void LoopsLikeTheWebsite()
        {
            var found = new System.Collections.Concurrent.ConcurrentBag<(int At, string Why)>();
            // a few at a time, each with its own engine; each one's frames on every core too
            System.Threading.Tasks.Parallel.For(0, Website.Loops.Length, new System.Threading.Tasks.ParallelOptions { MaxDegreeOfParallelism = 3 }, at =>
            {
                AnimCase c = Website.Loops[at];
                RenderCase picture = Website.Renders[c.Render];
                var engine = new Deres();
                engine.SetSource(Input(picture.Input));
                DeresSettings s = picture.Settings();
                Rendered r = engine.Render(s);
                SignalSettings signal = c.Signal;
                var o = new LoopOptions
                {
                    Style = c.Style, Apng = c.Apng, Multiplier = c.M, Tv = c.Tv, Fx = c.Fx, Source = Input(picture.Input), LoopFrames = c.LoopFrames, LoopFps = c.LoopFps,
                    SeeThrough = c.SeeThrough, Limits = AnimationLimits.Website,
                    Signal = signal != null ? (Func<Rendered, int, SignalImage>)((pic, f) => Signal.Run(pic, picture.Profile, signal, f)) : null,
                };
                bool seeThrough = s.Background == Backgrounds.Transparent && engine.Source.HasAlpha;
                if (seeThrough != c.SeeThrough) found.Add((at, c + ": see-through " + seeThrough));
                using (var bytes = new MemoryStream())
                {
                    AnimationResult result = Animation.Loop(bytes, r, o, s, sub => engine.Render(sub));
                    string why = SameAnimation(bytes.ToArray(), result, c);
                    if (why != null) found.Add((at, c + ": " + why));
                }
            });
            List<string> wrong = found.OrderBy(f => f.At).Select(f => f.Why).ToList();
            Check(wrong.Count == 0, wrong.Count + " of " + Website.Loops.Length + " differ: " + string.Join("; ", wrong.Take(5)));
        }

        // ---- an animated GIF's own frames ----

        static readonly Dictionary<string, AnimatedGif> openedGifs = new Dictionary<string, AnimatedGif>();

        // a GIF the website's engine opened (at most 2048 px, its budget)
        static AnimatedGif OpenedGif(string name)
        {
            lock (openedGifs)
            {
                if (!openedGifs.TryGetValue(name, out AnimatedGif gif)) openedGifs[name] = gif = GifFrames.Open(GifFile(name), 2048, AnimationLimits.Website.Budget);
                return gif;
            }
        }

        // the settings the website's test-file maker makes a GIF's frames with
        static DeresSettings GifSettings(string profile, string background, SignalSettings signal)
        {
            Profile p = Profiles.ById(profile);
            return Signal.ForRender(new DeresSettings { ProfileId = profile, Background = background, Dither = p.Dither }, signal);
        }

        // `half` 0 or 1: every other case, from the first or the second (two parts share them);
        // the first half also a frame's PNG
        static void GifFramesLikeTheWebsite(int half)
        {
            var found = new System.Collections.Concurrent.ConcurrentBag<(int At, string Why)>();
            int[] cases = Enumerable.Range(0, Website.FramesExports.Length).Where(i => i % 2 == half).ToArray();
            System.Threading.Tasks.Parallel.ForEach(cases, new System.Threading.Tasks.ParallelOptions { MaxDegreeOfParallelism = 2 }, at =>
            {
                AnimCase c = Website.FramesExports[at];
                AnimatedGif gif = OpenedGif(c.Gif);
                DeresSettings s = GifSettings(c.Profile, c.Background, c.Signal);
                Animation.PaletteKind(Profiles.ById(c.Profile), s, out bool indexed, out bool shared);
                if (indexed != c.Indexed || shared != c.Shared) found.Add((at, c + ": indexed " + indexed + ", shared " + shared));
                // (two lanes, each its own engine: the file can't depend on how many)
                var engines = new[] { new Deres(), new Deres() };
                SignalSettings signal = c.Signal;
                int step = gif.SignalStep;
                var o = new FramesOptions
                {
                    Apng = c.Apng, Multiplier = c.M, Tv = c.Tv, Fx = c.Fx, DelaysMs = gif.DelaysMs, LoopCount = gif.LoopCount, Lanes = 2,
                    Render = (k, lane) =>
                    {
                        engines[lane].SetSource(gif.Frames[k]);
                        return engines[lane].Render(s);
                    },
                    Signal = signal != null ? (Func<Rendered, int, SignalImage>)((pic, k) => Signal.Run(pic, c.Profile, signal, k * step)) : null,
                    Indexed = indexed, SharedPalette = shared, SeeThrough = s.Background == Backgrounds.Transparent && gif.HasAlpha,
                    Budget = c.Budget >= 0 ? c.Budget : (long?)null, Limits = AnimationLimits.Website,
                };
                if (o.SeeThrough != c.SeeThrough) found.Add((at, c + ": see-through " + o.SeeThrough));
                using (var bytes = new MemoryStream())
                {
                    AnimationResult result = Animation.FramesOf(bytes, o);
                    string why = SameAnimation(bytes.ToArray(), result, c);
                    if (why != null) found.Add((at, c + ": " + why));
                }
            });
            if (Website.GifSignalStep != OpenedGif("gif-rukus").SignalStep) found.Add((-1, "the sample's signal step: " + OpenedGif("gif-rukus").SignalStep + ", not " + Website.GifSignalStep));
            // a frame's PNG: its place in the loop for the screen effect, its frame of the signal
            foreach (var p in Website.GifFramePngs)
            {
                if (half != 0) break;
                AnimatedGif gif = OpenedGif("gif-rukus");
                var engine = new Deres();
                engine.SetSource(gif.Frames[p.Frame]);
                Rendered r = engine.Render(GifSettings(p.Profile, Backgrounds.White, p.Signal));
                SignalImage through = p.Signal != null ? Signal.Run(r, p.Profile, p.Signal, p.Frame * gif.SignalStep) : null;
                byte[] png = Export.PngBytes(r, p.M, false, p.Fx, through, gif.Phase(p.Frame));
                PngParts(png, out string chunks, out int width, out int height, out int depth, out int colourType, out _, out byte[] rows);
                if (chunks != p.Png.Chunks || width != p.Png.Width || height != p.Png.Height || depth != p.Png.Depth || colourType != p.Png.ColourType || Sha(rows) != p.Png.Rows)
                    found.Add((1000, "frame " + p.Frame + "'s PNG on the " + p.Profile));
            }
            List<string> wrong = found.OrderBy(f => f.At).Select(f => f.Why).ToList();
            Check(wrong.Count == 0, wrong.Count + " differ: " + string.Join("; ", wrong.Take(5)));
        }

        // GIFs shrunk to be held: their frames know the GIF's own size, and frame 0 at a Custom width
        // and Detail comes out as the website's, its height, widest, Detail and resolution line
        // going by that size rather than the frames'.
        static void ShrunkGifsGoByTheirOwnSize()
        {
            Profile nes = Profiles.ById("nes");
            var wrong = new List<string>();
            foreach (var c in Website.ShrunkGifRenders)
            {
                AnimatedGif gif = GifFrames.Open(GifFile(c.File), 2048, c.Budget);
                Check(gif != null && gif.Width < gif.OrigW && gif.Frames.All(f => f.OrigW == gif.OrigW && f.OrigH == gif.OrigH), c.File + "'s frames should know the GIF's own size");
                PictureSource src = gif.Frames[0];
                var engine = new Deres();
                engine.SetSource(src);
                DeresSettings s = GifSettings("nes", Backgrounds.White, null);
                s.Custom = true;
                s.CustomWidth = c.CustomWidth;
                s.Detail = c.Detail;
                Rendered r = engine.Render(s);
                r.Geometry.Coverage(src.Width, src.Height, src.OrigW, src.OrigH, out double x, out double y);
                string words = Words.Resolution(nes, true, r.Geometry, x, y, true);
                Geometry.CustomWidthRange(src.Width, src.Height, src.OrigW, src.OrigH, out int min, out int max);
                if (r.Width != c.Width || r.Height != c.Height || Sha(r.Rgb) != c.Rgb) wrong.Add(c.File + " " + c.CustomWidth + " wide: " + r.Width + "×" + r.Height + (Sha(r.Rgb) != c.Rgb ? ", its pixels" : ""));
                if (words != c.Words) wrong.Add("\"" + words + "\" for \"" + c.Words + "\"");
                if (min != c.Min || max != c.Max) wrong.Add(c.File + "'s Custom widths " + min + " to " + max + ", not " + c.Min + " to " + c.Max);
            }
            Check(wrong.Count == 0, wrong.Count + " differ: " + string.Join("; ", wrong));
        }

        // Only by hand (DERES_MEASURE=vhs48-<size>|deepfry4|crt16|gif-open): the time and memory the
        // PC's limits were set by, printed. Each goes alone, within the 90 seconds.
        static void MeasureAnimations()
        {
            string which = Environment.GetEnvironmentVariable("DERES_MEASURE");
            if (string.IsNullOrEmpty(which)) throw new SkipException("only by hand (DERES_MEASURE)");
            var watch = System.Diagnostics.Stopwatch.StartNew();
            AnimationResult result;
            long bytes;
            if (which == "gif-open")
            {
                LoadedPicture gif = Pictures.FromBytes(GifFile("gif-rukus"), "rukus-breaks-out.gif");
                Console.WriteLine("      opened " + gif.FrameCount + " frames in " + watch.ElapsedMilliseconds + " ms");
                return;
            }
            using (var output = new MemoryStream())
            {
                if (which == "deepfry4")
                {
                    AnimatedGif gif = Pictures.FromBytes(GifFile("gif-rukus"), "rukus-breaks-out.gif").Animation;
                    var s = new DeresSettings { ProfileId = "deepfry", Dither = Profiles.ById("deepfry").Dither };
                    int lanes = Math.Max(1, Math.Min(4, Environment.ProcessorCount / 3));
                    Deres[] engines = Enumerable.Range(0, lanes).Select(_ => new Deres()).ToArray();
                    result = Animation.FramesOf(output, new FramesOptions
                    {
                        Multiplier = 4, DelaysMs = gif.DelaysMs, LoopCount = gif.LoopCount, Lanes = lanes, Limits = AnimationLimits.Pc,
                        Render = (k, lane) =>
                        {
                            engines[lane].SetSource(gif.Frames[k]);
                            return engines[lane].Render(s);
                        },
                    });
                }
                else
                {
                    var engine = new Deres();
                    engine.SetSource(Input("cat-nap"));
                    var s = new DeresSettings { ProfileId = "nes", Dither = Profiles.ById("nes").Dither };
                    Rendered r = engine.Render(s);
                    var signal = new SignalSettings { Mode = SignalModes.VhsEp };
                    bool crt = which == "crt16";
                    result = Animation.Loop(output, r, new LoopOptions
                    {
                        Style = Animation.ScreenFx, Multiplier = crt ? 16 : int.Parse(which.Substring(which.IndexOf('-') + 1)), Fx = crt ? new FxSettings { Screen = Screens.Crt } : null, LoopFrames = 48, LoopFps = 15,
                        Signal = crt ? null : (Func<Rendered, int, SignalImage>)((pic, f) => Signal.Run(pic, "nes", signal, f)), Limits = AnimationLimits.Pc,
                    }, s, sub => engine.Render(sub));
                }
                bytes = output.Length;
            }
            System.Diagnostics.Process me = System.Diagnostics.Process.GetCurrentProcess();
            me.Refresh();
            Console.WriteLine("      " + which + ": " + result.Frames + " frames of " + result.Width + "×" + result.Height + " in " + (watch.ElapsedMilliseconds / 1000.0).ToString("0.0") + " s, " +
                (bytes >> 20) + " MB, peak memory " + (me.PeakWorkingSet64 >> 20) + " MB on " + Environment.ProcessorCount + " cores");
        }

        static void AnimationSizesAndWordsLikeTheWebsite()
        {
            var wrong = new List<string>();
            foreach (var f in Website.AnimationFits)
            {
                AnimationFit fit = Animation.FitFor(f.W, f.H, f.ParX, f.ParY, f.Tv, f.M, f.Kind, f.SignalLoop >= 0 ? f.SignalLoop : (int?)null, AnimationLimits.Website);
                if ((fit.Multiplier ?? -1) != f.Multiplier || fit.Held != f.Held || (fit.Next ?? -1) != f.Next || (fit.Limit ?? "") != f.Limit)
                    wrong.Add(f.W + "×" + f.H + " " + f.Kind + " at " + f.M + "×: " + fit.Multiplier + "/" + fit.Held + "/" + fit.Next + "/" + fit.Limit);
            }
            foreach (var t in Website.FramesTexts)
            {
                string got = GifInfo.FramesText(t.File, t.Frames, t.Total, t.Ms, t.Frames >= 0);
                if (got != t.Text) wrong.Add("\"" + got + "\" for \"" + t.Text + "\"");
            }
            Check(Animation.ApngPlays(null) == 1 && Animation.ApngPlays(0) == 0 && Animation.ApngPlays(3) == 4, "an APNG's plays from a GIF's loop count");
            // no side past 65535, which a GIF can't say (the website's strips are among the fits above),
            // with the PC's limits too
            Check(Animation.Limit(16, 65535, 12, AnimationLimits.Pc) == null && Animation.Limit(16, 65536, 12, AnimationLimits.Pc) == "frame" && Animation.Limit(65536, 1, 0, AnimationLimits.Website) == "frame",
                "a side past 65535 is too big for an animation");
            AnimationFit tall = Animation.FitFor(1, 4096, 1, 1, false, 16, Animation.ScreenFx, null, AnimationLimits.Pc);
            Check(tall.Multiplier == 12 && tall.Next == 16 && tall.Limit == "frame", "a 1×4096 picture's loop at 16× would be 65,536 tall: " + tall.Multiplier + "/" + tall.Next + "/" + tall.Limit);
            Check(Signal.EngineStep(15) == 2 && Signal.EngineStep(30) == 1 && Signal.EngineStep(8) == 4 && Signal.EngineStep(1000 / 70.0) == 2, "the engine's step for a loop's speed");
            Check(wrong.Count == 0, wrong.Count + " differ: " + string.Join("; ", wrong.Take(5)));
        }
    }
}
