using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using B0r3dDeRes.Engine;

namespace B0r3dDeRes.Tests
{
    // One picture the website's engine made: what it started from and its settings, then what came out.
    sealed class RenderCase
    {
        public readonly string Input, Profile, Fit, Background, Dither, Palette, Indices, Rgb, Words;
        public readonly int CustomWidth, Width, Height, Used;
        public readonly double Detail, Brightness, Contrast, Saturation, Gamma;
        public readonly double[] Src;
        public readonly int[] Dst;

        public RenderCase(string input, string profile, string fit, int customWidth, double detail, string background, double brightness, double contrast,
            double saturation, double gamma, string dither, int width, int height, int used, string palette, string indices, string rgb, double[] src, int[] dst, string words)
        {
            Input = input; Profile = profile; Fit = fit; CustomWidth = customWidth; Detail = detail; Background = background;
            Brightness = brightness; Contrast = contrast; Saturation = saturation; Gamma = gamma; Dither = dither;
            Width = width; Height = height; Used = used; Palette = palette; Indices = indices; Rgb = rgb; Src = src; Dst = dst; Words = words;
        }

        // the Colours slider (-1: not moved), Custom's palette ("" for none, else "#rrggbb,…")
        public int Count = -1;
        public string CustomPalette = "";
        // what else came out: pixels its palette's entries or not, which show (a hash, "" for all),
        // the paper colour, the tile meter (its line, "" for none) and the machine's own file
        public bool Indexed, TilesFit, ColoursFit;
        public string Alpha, Paper, Tiles, EmuFormat, EmuName, EmuSha;
        public int UniqueTiles, UniqueColours, EmuLength;
        // made with the signal on (CGA composite dithers to its colours), or null
        public SignalSettings Signal;

        public DeresSettings Settings() => Engine.Signal.ForRender(new DeresSettings
        {
            ProfileId = Profile,
            Fit = Fit,
            Custom = CustomWidth >= 0,
            CustomWidth = CustomWidth >= 0 ? CustomWidth : (int?)null,
            Detail = Detail,
            Background = Background,
            Adjust = new Adjust { Brightness = Brightness, Contrast = Contrast, Saturation = Saturation, Gamma = Gamma },
            Dither = Dither,
            CustomCount = Count >= 0 ? Count : (int?)null,
            CustomPalette = CustomPalette.Length > 0 ? CustomPalette.Split(',') : null,
        }, Signal);

        public override string ToString() => Input + " " + Profile + " " + Dither + (CustomWidth >= 0 ? " width " + CustomWidth : " " + Fit) + " detail " + Detail + " " + Background +
            (Brightness != 0 || Contrast != 1 || Saturation != 1 || Gamma != 1 ? " adjust " + Brightness + "/" + Contrast + "/" + Saturation + "/" + Gamma : "") +
            (Count >= 0 ? " colours " + Count : "") + (CustomPalette.Length > 0 ? " palette of " + CustomPalette.Split(',').Length : "");
    }

    // One PNG the website exported from a picture above, by its parts.
    sealed class ExportCase
    {
        public readonly int Render, Multiplier, Width, Height, Depth, ColourType, RowBytes;
        public readonly bool Tv;
        public readonly string Name, Chunks, Palette, Rows;
        public string Trns = "";    // the see-through entries, hex ("" for none)
        public FxSettings Fx;       // the screen effect asked for (null: none)
        // a wallpaper: its id and size ("" for a plain export)
        public string Wallpaper = "";
        public int TargetW, TargetH;

        public ExportCase(int render, int multiplier, bool tv, string name, string chunks, int width, int height, int depth, int colourType, string palette, string rows, int rowBytes)
        {
            Render = render; Multiplier = multiplier; Tv = tv; Name = name; Chunks = chunks; Width = width; Height = height;
            Depth = depth; ColourType = colourType; Palette = palette; Rows = rows; RowBytes = rowBytes;
        }
    }

    sealed class ProfileCase
    {
        public readonly string Id, Name, Origin, About, Colours, Dither, Screen;
        public readonly int Width, Height, ParX, ParY;
        public readonly bool Mono;
        // how it gets its colours, its limits, and what the controls ask of it (-1 / "" for none)
        public string Kind, Pool, Mode, Formats, ExportNote, NativeDither, Representative, PaletteBytes, DitherApplies, DitherNote, Counts;
        public int Bits, Count, MaxTiles, MaxColours, MaxCount, Total;
        public bool Limits, PixelsArePalette;
        public double EdgeBoost;

        public ProfileCase(string id, string name, string origin, string about, int width, int height, int parX, int parY, string colours, bool mono, string dither, string screen)
        {
            Id = id; Name = name; Origin = origin; About = about; Width = width; Height = height; ParX = parX; ParY = parY;
            Colours = colours; Mono = mono; Dither = dither; Screen = screen;
        }
    }

    // The engine against the website's own: the test files in WebsiteCases.cs and website\*.png come
    // from its engine, run in Node (see the comment at the top of WebsiteCases.cs).
    static partial class TestMain
    {
        static string Sha(byte[] bytes)
        {
            using (var sha = SHA256.Create()) return string.Concat(sha.ComputeHash(bytes).Select(b => b.ToString("x2")));
        }

        static string FloatsSha(float[] values)
        {
            var bytes = new byte[values.Length * 4];
            Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
            return Sha(bytes);
        }

        static string Hex(byte[] bytes) => string.Concat(bytes.Select(b => b.ToString("x2")));

        static readonly Dictionary<string, PictureSource> inputs = new Dictionary<string, PictureSource>();

        // a picture the website's engine started from, decoded exactly (they're plain PNGs)
        static PictureSource Input(string name)
        {
            lock (inputs)
            {
                if (!inputs.TryGetValue(name, out PictureSource source))
                {
                    string file = Path.Combine(TestsFolder, "website", name + ".png");
                    if (!File.Exists(file)) throw new SkipException(file + " isn't there (build the tests first)");
                    source = Pictures.FromBytes(File.ReadAllBytes(file), name + ".png").Source;
                    inputs[name] = source;
                }
                return source;
            }
        }

        // An engine for each test picture (or for each run of its cases, so they go on every
        // core: an engine is for one thread at a time, and two never share anything).
        static readonly Dictionary<string, Deres> engines = new Dictionary<string, Deres>();

        static Rendered Render(RenderCase c, string engine = null)
        {
            Deres d;
            lock (engines)
            {
                string key = engine ?? c.Input;
                if (!engines.TryGetValue(key, out d))
                {
                    d = new Deres();
                    d.SetSource(Input(c.Input));
                    engines[key] = d;
                }
            }
            lock (d) return d.Render(c.Settings());
        }

        // the website's pictures made here, kept for the export, screen and wallpaper checks
        static readonly System.Collections.Concurrent.ConcurrentDictionary<int, Rendered> made = new System.Collections.Concurrent.ConcurrentDictionary<int, Rendered>();

        static Rendered Made(int k) => made.GetOrAdd(k, i => Render(Website.Renders[i]));

        static void MathsLikeTheBrowser()
        {
            byte[] pairs = Convert.FromBase64String(Website.CubeRoots);
            int count = pairs.Length / 16, wrong = 0;
            string first = null;
            for (int i = 0; i < count; i++)
            {
                double x = BitConverter.ToDouble(pairs, i * 16), want = BitConverter.ToDouble(pairs, i * 16 + 8), got = JsMath.Cbrt(x);
                if (BitConverter.DoubleToInt64Bits(want) == BitConverter.DoubleToInt64Bits(got)) continue;
                wrong++;
                first = first ?? "cbrt(" + x.ToString("R") + ") = " + got.ToString("R") + ", Node says " + want.ToString("R");
            }
            Check(count > 5000 && wrong == 0, wrong + " of " + count + " cube roots differ from Node's, e.g. " + first);

            // powers: exact where they should be, and within a last bit of .NET's anywhere else
            Check(JsMath.Pow(2, 10) == 1024 && JsMath.Pow(10, 2) == 100 && JsMath.Pow(0.5, 2) == 0.25 && JsMath.Pow(9, 0.5) == 3, "whole powers aren't exact");
            Check(JsMath.Pow(0, 0.7) == 0 && JsMath.Pow(1, 0.3) == 1 && JsMath.Pow(0.3, 0) == 1 && JsMath.Pow(0.3, 1) == 0.3, "the special cases are wrong");
            Check(double.IsNaN(JsMath.Pow(-2, 0.5)) && double.IsNaN(JsMath.Pow(double.NaN, 2)), "powers that have no answer should be NaN");
            var random = new Random(2026);
            for (int i = 0; i < 200000; i++)
            {
                double x = random.NextDouble() * (i % 3 == 0 ? 1e-3 : 2), y = random.NextDouble() * 5 - 2.5;
                double mine = JsMath.Pow(x, y), net = Math.Pow(x, y);
                long apart = Math.Abs(BitConverter.DoubleToInt64Bits(mine) - BitConverter.DoubleToInt64Bits(net));
                Check(apart <= 1, "pow(" + x.ToString("R") + ", " + y.ToString("R") + ") = " + mine.ToString("R") + ", .NET says " + net.ToString("R"));
            }
            Check(JsMath.Round(2.5) == 3 && JsMath.Round(-2.5) == -2 && JsMath.Round(0.49999999999999994) == 0 && JsMath.Round(1.5) == 2 && JsMath.Round(-0.6) == -1,
                "rounding isn't JavaScript's (halves up)");
        }

        static void TablesLikeTheWebsite()
        {
            Check(FloatsSha(Colour.ByteToLinear) == Website.ByteToLinear, "the sRGB bytes in linear light differ from the website's");
            Check(Sha(Colour.LinearToByteTable) == Website.LinearToByte, "linear light to sRGB bytes differs from the website's");
            Check(FloatsSha(Colour.SrgbCurveTable) == Website.SrgbCurve, "the linear-to-sRGB curve differs from the website's");
            Check(FloatsSha(Colour.LinearCurveTable) == Website.LinearCurve, "the sRGB-to-linear curve differs from the website's");
            Check(FloatsSha(YliluomaPlanner.LinToSrgbTable) == Website.LinToSrgb, "Yliluoma's table differs from the website's");
            Check(FloatsSha(FxRenderer.BeamTable) == Website.Beam, "the screen effect's beam differs from the website's");
            Check(FloatsSha(Analog.CosTable) == Website.DctCosines, "the deep fry's cosines differ from the website's");
        }

        // sin, cos and exp, number for number, as V8 works them out in Node (fdlibm's)
        static void TrigLikeTheBrowser()
        {
            foreach (var (name, pairs, fn) in new (string, string, Func<double, double>)[] { ("sin", Website.Sines, JsMath.Sin), ("cos", Website.Cosines, JsMath.Cos), ("exp", Website.Exps, JsMath.Exp) })
            {
                byte[] bytes = Convert.FromBase64String(pairs);
                int count = bytes.Length / 16, wrong = 0;
                string first = null;
                for (int i = 0; i < count; i++)
                {
                    double x = BitConverter.ToDouble(bytes, i * 16), want = BitConverter.ToDouble(bytes, i * 16 + 8), got = fn(x);
                    if (BitConverter.DoubleToInt64Bits(want) == BitConverter.DoubleToInt64Bits(got)) continue;
                    wrong++;
                    first = first ?? name + "(" + x.ToString("R") + ") = " + got.ToString("R") + ", Node says " + want.ToString("R");
                }
                Check(count > 5000 && wrong == 0, wrong + " of " + count + " differ from Node's, e.g. " + first);
            }
        }

        // the screen effect, made by the website at many sizes: the same pixels, a row at a time
        // and on every core
        static void ScreensLikeTheWebsite()
        {
            var pictures = Website.Screens.Select(s => s.Render).Distinct().ToDictionary(k => k, Made);
            var found = new System.Collections.Concurrent.ConcurrentBag<(int At, string Why)>();
            System.Threading.Tasks.Parallel.For(0, Website.Screens.Length, at =>
            {
                var s = Website.Screens[at];
                Rendered r = pictures[s.Render];
                var screen = new FxRenderer(r.Rgb, r.Width, r.Height, r.Paper, r.Alpha, s.Width, s.Height, s.Fx, s.Phase);
                var rgb = new byte[s.Width * s.Height * 3];
                var alpha = new byte[s.Width * s.Height];
                var row = new byte[s.Width * 3];
                var shows = new byte[s.Width];
                for (int y = 0; y < s.Height; y++)
                {
                    screen.Row(y, row, shows);
                    Buffer.BlockCopy(row, 0, rgb, y * s.Width * 3, row.Length);
                    Buffer.BlockCopy(shows, 0, alpha, y * s.Width, shows.Length);
                }
                if (Sha(rgb) != s.Rgb || Sha(alpha) != s.Alpha)
                    found.Add((at, Website.Renders[s.Render] + " on " + s.Fx.Screen + " (" + s.Fx.Mask + ") at " + s.Width + "×" + s.Height + (s.Phase != 0 ? " phase " + s.Phase : "") +
                        (Sha(rgb) != s.Rgb ? ": colours" : ": which show")));
                // the rows on every core at once come out the same
                if (at % 40 == 0)
                {
                    byte[] rgba = screen.Rgba();
                    for (int i = 0; i < alpha.Length; i++)
                        if (rgba[i * 4] != rgb[i * 3] || rgba[i * 4 + 1] != rgb[i * 3 + 1] || rgba[i * 4 + 2] != rgb[i * 3 + 2] || rgba[i * 4 + 3] != alpha[i])
                        {
                            found.Add((at, "case " + at + " differs made on every core"));
                            break;
                        }
                }
            });
            List<string> wrong = found.OrderBy(f => f.At).Select(f => f.Why).ToList();
            Check(wrong.Count == 0, wrong.Count + " of " + Website.Screens.Length + " differ: " + string.Join("; ", wrong.Take(6)));
        }

        static void WallpapersLikeTheWebsite()
        {
            var found = new System.Collections.Concurrent.ConcurrentBag<string>();
            System.Threading.Tasks.Parallel.For(0, Website.Wallpapers.Length, at =>
            {
                ExportCase e = Website.Wallpapers[at];
                RenderCase c = Website.Renders[e.Render];
                Rendered r = Made(e.Render);
                byte[] png;
                using (var bytes = new MemoryStream())
                {
                    // (rows stored, not squeezed, but for every fourth: the rows are what's compared)
                    Engine.Png.Stored = at % 4 != 0;
                    try
                    {
                        Export.WriteWallpaper(bytes, r, e.TargetW, e.TargetH, e.Tv && !r.Geometry.SquarePixels, e.Fx);
                    }
                    finally
                    {
                        Engine.Png.Stored = false;
                    }
                    png = bytes.ToArray();
                }
                PngParts(png, out string chunks, out int width, out int height, out int depth, out int colourType, out byte[] palette, out byte[] rows, out byte[] trns);
                var why = new List<string>();
                if (chunks != e.Chunks) why.Add("chunks " + chunks);
                if (width != e.Width || height != e.Height) why.Add(width + "×" + height);
                if (depth != e.Depth || colourType != e.ColourType) why.Add("depth " + depth + " type " + colourType);
                if (rows.Length != e.RowBytes || Sha(rows) != e.Rows) why.Add("rows");
                string name = Export.WallpaperName(Export.BaseName(c.Input + ".jpg"), c.Profile, e.Wallpaper);
                if (name != e.Name) why.Add("named " + name);
                if (why.Count > 0) found.Add(e.Name + (e.Fx != null ? " with " + e.Fx.Screen : "") + ": " + string.Join(", ", why));
            });
            List<string> wrong = found.OrderBy(w => w, StringComparer.Ordinal).ToList();
            // the last of the checks that keep pictures: they go, so the tests after have the memory
            made.Clear();
            lock (engines) engines.Clear();
            Check(wrong.Count == 0, wrong.Count + " of " + Website.Wallpapers.Length + " differ: " + string.Join("; ", wrong.Take(6)));
            Check(Export.Wallpapers.Select(w => w.Id + " " + w.Width + "x" + w.Height).SequenceEqual(new[] { "phone 1080x2400", "win95 1024x768", "desktop-1080p 1920x1080" }), "the website's three wallpapers");
        }

        static void GammaTablesLikeTheWebsite()
        {
            Check(Website.GammaTables.Length == 211, "expected the 211 stops of the slider, got " + Website.GammaTables.Length);
            var wrong = Website.GammaTables.Where(t => FloatsSha(Adjust.GammaTable(t.Gamma)) != t.Table).Select(t => t.Gamma.ToString("0.00")).ToList();
            Check(wrong.Count == 0, "the gamma tables differ at " + string.Join(", ", wrong.Take(10)));
        }

        static readonly Dictionary<PaletteKind, string> kinds = new Dictionary<PaletteKind, string>
        {
            { PaletteKind.Fixed, "fixed" }, { PaletteKind.Range, "range" }, { PaletteKind.Direct, "direct" }, { PaletteKind.Custom, "custom" },
            { PaletteKind.Pool, "pool" }, { PaletteKind.Cell, "cell" }, { PaletteKind.Ham, "ham" }, { PaletteKind.N64, "n64" }, { PaletteKind.Analog, "analog" },
        };

        static void MachinesLikeTheWebsite()
        {
            Check(Profiles.All.Select(p => p.Id).SequenceEqual(Website.Profiles.Select(p => p.Id)), "the machines aren't the website's, in its order: " + string.Join(", ", Profiles.All.Select(p => p.Id)));
            foreach (ProfileCase w in Website.Profiles)
            {
                Profile p = Profiles.ById(w.Id);
                Check(p.Name == w.Name && p.Origin == w.Origin && p.About == w.About, p.Id + "'s words differ: " + p.Name + " / " + p.Origin + " / " + p.About);
                Check(p.Width == w.Width && (w.Height < 0 ? p.AutoHeight : !p.AutoHeight && p.Height == w.Height) && p.ParX == w.ParX && p.ParY == w.ParY, p.Id + "'s size or pixel shape differs");
                Check(kinds[p.Kind] == w.Kind, p.Id + " gets its colours as " + kinds[p.Kind] + ", not " + w.Kind);
                Check(string.Join(",", p.Colours ?? new string[0]) == w.Colours && string.Join(",", p.Pool ?? new string[0]) == w.Pool, p.Id + "'s colours differ");
                Check(p.BitsPerChannel == w.Bits && p.Count == w.Count && (p.Mode ?? "") == w.Mode, p.Id + "'s range, count or cells differ");
                Check((p.MaxTiles ?? -1) == w.MaxTiles && (p.MaxColours ?? -1) == w.MaxColours && string.Join(",", p.EmulatorFormats) == w.Formats && p.HasLimits == w.Limits,
                    p.Id + "'s limits differ");
                Check((p.ExportNote ?? "") == w.ExportNote, p.Id + "'s note under the export buttons differs");
                Check(p.Mono == w.Mono && p.Dither == w.Dither && (p.NativeDither ?? "") == w.NativeDither && p.EdgeBoost == w.EdgeBoost && p.Screen == w.Screen,
                    p.Id + "'s grey levels, dithers, sharpening or screen differ");
                Check(string.Join(",", Profiles.RepresentativePalette(p)) == w.Representative, p.Id + "'s card colours differ: " + string.Join(",", Profiles.RepresentativePalette(p)));
                Check(Hex(Profiles.PaletteBytes(p)) == w.PaletteBytes, p.Id + "'s palette bytes differ");
                Check((Profiles.MaxColourCount(p) ?? -1) == w.MaxCount && (Profiles.ColourTotal(p, Profiles.PaletteBytes(p)) ?? -1) == w.Total && Profiles.PixelsArePalette(p) == w.PixelsArePalette,
                    p.Id + "'s colour counts differ");
                Check(string.Join("", Dither.All.Select(d => Profiles.DitherApplies(p, d.Id) ? "1" : "0")) == w.DitherApplies && (Profiles.DitherNote(p) ?? "") == w.DitherNote,
                    p.Id + "'s dithers that apply, or its note, differ");
                Check(string.Join(",", new int?[] { null, 1, 2, 7, 300 }.Select(c => Profiles.ColourCount(p, c) ?? -1)) == w.Counts, p.Id + "'s Colours slider counts differ");
            }
        }

        static void DithersLikeTheWebsite()
        {
            Check(Dither.All.Length == Website.Dithers.Length, "the dithers are " + string.Join(", ", Dither.All.Select(d => d.Id)));
            for (int i = 0; i < Website.Dithers.Length; i++)
                Check(Dither.All[i].Id == Website.Dithers[i].Id && Dither.All[i].Name == Website.Dithers[i].Name && Dither.All[i].Hint == Website.Dithers[i].Hint && Dither.All[i].Native == Website.Dithers[i].Native,
                    "dither " + (i + 1) + " is " + Dither.All[i].Id + " (" + Dither.All[i].Name + ", " + Dither.All[i].Hint + "), not the website's " + Website.Dithers[i].Id);
        }

        static void PicturesLikeTheWebsite()
        {
            // runs of 40 cases on every core, each run on its engine; the pictures the later
            // checks need are kept
            var keep = new HashSet<int>(Website.Exports.Select(e => e.Render).Concat(Website.Screens.Select(s => s.Render)).Concat(Website.Wallpapers.Select(w => w.Render))
                .Concat(Website.Signals.Select(s => s.Render)));
            var found = new System.Collections.Concurrent.ConcurrentBag<(int At, string Why)>();
            var took = new System.Collections.Concurrent.ConcurrentBag<(long Ms, string Case)>();
            var runs = Website.Renders.Select((c, i) => (Case: c, At: i)).GroupBy(x => x.Case.Input + "#" + x.At / 40).ToList();
            System.Threading.Tasks.Parallel.ForEach(runs, run =>
            {
                foreach (var (c, at) in run)
                {
                    var watch = System.Diagnostics.Stopwatch.StartNew();
                    Rendered r = Render(c, run.Key);
                    if (keep.Contains(at)) made[at] = r;
                    string problem = Compare(c, r);
                    if (problem != null) found.Add((at, problem));
                    took.Add((watch.ElapsedMilliseconds, c.ToString()));
                }
            });
            // with DERES_SLOW set, the slowest pictures (to keep the run within its 90 seconds)
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DERES_SLOW")))
                foreach (var t in took.OrderByDescending(t => t.Ms).Take(20)) Console.WriteLine("      " + t.Ms + " ms  " + t.Case);
            List<string> wrong = found.OrderBy(f => f.At).Select(f => f.Why).ToList();
            Check(wrong.Count == 0, wrong.Count + " of " + Website.Renders.Length + " differ: " + string.Join("; ", wrong.Take(8)));
        }

        // what differs between a picture made here and the website's, or null
        static string Compare(RenderCase c, Rendered r)
        {
            var why = new List<string>();
            if (r.Width != c.Width || r.Height != c.Height) why.Add("size " + r.Width + "×" + r.Height + ", not " + c.Width + "×" + c.Height);
            else
            {
                if (Hex(r.Palette) != c.Palette) why.Add("palette");
                if (Sha(r.Indices) != c.Indices) why.Add("pixels (" + Differences(c, r) + ")");
                if (Sha(r.Rgb) != c.Rgb) why.Add("colours");
                if (r.Used != c.Used) why.Add(r.Used + " colours used, not " + c.Used);
                if (r.Indexed != c.Indexed) why.Add(r.Indexed ? "indexed" : "not indexed");
                if ((r.Alpha == null ? "" : Sha(r.Alpha)) != c.Alpha) why.Add(r.Alpha == null ? "nothing see-through" : "see-through pixels");
                if (Hex(r.Paper) != c.Paper) why.Add("paper " + Hex(r.Paper));
                HardwareFitReport t = r.TileFit;
                if ((t?.Summary ?? "") != c.Tiles || (t?.UniqueTiles ?? -1) != c.UniqueTiles || (t?.UniqueColours ?? -1) != c.UniqueColours ||
                    (t?.TilesFit ?? true) != c.TilesFit || (t?.ColoursFit ?? true) != c.ColoursFit) why.Add("tile meter \"" + t?.Summary + "\"");
                EmulatorFile e = r.EmulatorFile;
                if ((e?.Format ?? "") != c.EmuFormat || (e?.DefaultName ?? "") != c.EmuName || (e == null ? -1 : e.Data.Length) != c.EmuLength || (e == null ? "" : Sha(e.Data)) != c.EmuSha)
                    why.Add("machine file " + e?.DefaultName + " (" + (e?.Data.Length ?? 0) + " bytes)");
            }
            return why.Count > 0 ? c + ": " + string.Join(", ", why) : null;
        }

        // with the website's pixels dumped (make-test-files.mjs --dump), how many and where; else nothing
        static string Differences(RenderCase c, Rendered r)
        {
            string dump = Environment.GetEnvironmentVariable("DERES_DUMP");
            if (string.IsNullOrEmpty(dump)) return "set DERES_DUMP to the --dump folder to see where";
            int index = Array.IndexOf(Website.Renders, c);
            string file = Path.Combine(dump, index + "-" + c.Input + "-" + c.Profile + "-" + c.Dither + ".indices");
            if (!File.Exists(file)) return "no dump for it";
            byte[] want = File.ReadAllBytes(file);
            int n = 0, firstAt = -1;
            for (int i = 0; i < want.Length && i < r.Indices.Length; i++)
                if (want[i] != r.Indices[i])
                {
                    n++;
                    if (firstAt < 0) firstAt = i;
                }
            return n + " of " + want.Length + " differ, the first at (" + (firstAt % r.Width) + ", " + (firstAt / r.Width) + ")";
        }

        static void GeometryLikeTheWebsite()
        {
            var wrong = new List<string>();
            foreach (RenderCase c in Website.Renders)
            {
                PictureSource source = Input(c.Input);
                DeresSettings s = c.Settings();
                Geometry g = Geometry.Plan(source.Width, source.Height, source.Width, source.Height, Profiles.ById(c.Profile), s.Custom, s.CustomWidth, s.Fit);
                bool same = g.Src.X == c.Src[0] && g.Src.Y == c.Src[1] && g.Src.W == c.Src[2] && g.Src.H == c.Src[3] &&
                    g.Dst.X == c.Dst[0] && g.Dst.Y == c.Dst[1] && g.Dst.W == c.Dst[2] && g.Dst.H == c.Dst[3] && g.Width == c.Width && g.Height == c.Height;
                if (!same) wrong.Add(c + ": src " + g.Src.X + "," + g.Src.Y + " " + g.Src.W + "×" + g.Src.H + " dst " + g.Dst.X + "," + g.Dst.Y + " " + g.Dst.W + "×" + g.Dst.H);
            }
            Check(wrong.Count == 0, wrong.Count + " framed differently: " + string.Join("; ", wrong.Take(5)));
        }

        static void ResolutionWordsLikeTheWebsite()
        {
            var wrong = new List<string>();
            foreach (RenderCase c in Website.Renders)
            {
                PictureSource source = Input(c.Input);
                DeresSettings s = c.Settings();
                Profile p = Profiles.ById(c.Profile);
                Geometry g = Geometry.Plan(source.Width, source.Height, source.Width, source.Height, p, s.Custom, s.CustomWidth, s.Fit);
                g.Coverage(source.Width, source.Height, source.Width, source.Height, out double x, out double y);
                string words = Words.Resolution(p, s.Custom, g, x, y, true);
                if (words != c.Words) wrong.Add("\"" + words + "\" for \"" + c.Words + "\"");
            }
            Check(wrong.Count == 0, wrong.Count + " differ, e.g. " + string.Join("; ", wrong.Take(3)));
        }

        // a PNG's parts: its chunks (each kind once, in order), header, palette and unpacked rows
        static void PngParts(byte[] png, out string chunks, out int width, out int height, out int depth, out int colourType, out byte[] palette, out byte[] rows) =>
            PngParts(png, out chunks, out width, out height, out depth, out colourType, out palette, out rows, out _);

        static void PngParts(byte[] png, out string chunks, out int width, out int height, out int depth, out int colourType, out byte[] palette, out byte[] rows, out byte[] trns)
        {
            var kinds = new List<string>();
            var idat = new MemoryStream();
            width = height = depth = colourType = 0;
            palette = null;
            trns = null;
            for (int o = 8; o < png.Length;)
            {
                int len = (png[o] << 24) | (png[o + 1] << 16) | (png[o + 2] << 8) | png[o + 3];
                string type = Encoding.ASCII.GetString(png, o + 4, 4);
                if (!kinds.Contains(type)) kinds.Add(type);
                if (type == "IHDR")
                {
                    width = (png[o + 8] << 24) | (png[o + 9] << 16) | (png[o + 10] << 8) | png[o + 11];
                    height = (png[o + 12] << 24) | (png[o + 13] << 16) | (png[o + 14] << 8) | png[o + 15];
                    depth = png[o + 16];
                    colourType = png[o + 17];
                }
                if (type == "PLTE") palette = png.Skip(o + 8).Take(len).ToArray();
                if (type == "tRNS") trns = png.Skip(o + 8).Take(len).ToArray();
                if (type == "IDAT") idat.Write(png, o + 8, len);
                o += 12 + len;
            }
            chunks = string.Join(",", kinds);
            // zlib: two bytes of header, deflate, then four of check
            byte[] z = idat.ToArray();
            using (var inflate = new DeflateStream(new MemoryStream(z, 2, z.Length - 6), CompressionMode.Decompress))
            {
                var output = new MemoryStream();
                inflate.CopyTo(output);
                rows = output.ToArray();
            }
        }

        static void PngsLikeTheWebsite()
        {
            // the pictures (kept from above), then their PNGs on every core
            var pictures = Website.Exports.Select(e => e.Render).Distinct().ToDictionary(k => k, Made);
            var wrong = new System.Collections.Concurrent.ConcurrentBag<string>();
            System.Threading.Tasks.Parallel.For(0, Website.Exports.Length, at =>
            {
                ExportCase e = Website.Exports[at];
                RenderCase c = Website.Renders[e.Render];
                Rendered r = pictures[e.Render];
                bool tv = e.Tv && !r.Geometry.SquarePixels;
                // (rows stored, not squeezed, but for every tenth: the rows are what's compared)
                Engine.Png.Stored = at % 10 != 0;
                byte[] png;
                try
                {
                    png = Export.PngBytes(r, e.Multiplier, tv, e.Fx);
                }
                finally
                {
                    Engine.Png.Stored = false;
                }
                PngParts(png, out string chunks, out int width, out int height, out int depth, out int colourType, out byte[] palette, out byte[] rows, out byte[] trns);
                var why = new List<string>();
                if (chunks != e.Chunks) why.Add("chunks " + chunks);
                if (width != e.Width || height != e.Height) why.Add(width + "×" + height);
                if (depth != e.Depth || colourType != e.ColourType) why.Add("depth " + depth + " type " + colourType);
                if ((palette == null ? "" : Hex(palette)) != e.Palette) why.Add("palette");
                if ((trns == null ? "" : Hex(trns)) != e.Trns) why.Add("see-through entries");
                if (rows.Length != e.RowBytes || Sha(rows) != e.Rows) why.Add("rows");
                string name = Export.FileName(Export.BaseName(c.Input + ".jpg"), c.Profile, r.Width, r.Height, e.Multiplier, tv, Export.WithFx(e.Fx, e.Multiplier));
                if (name != e.Name) why.Add("named " + name);
                if (why.Count > 0) wrong.Add(e.Name + ": " + string.Join(", ", why));
            });
            Check(wrong.Count == 0, wrong.Count + " of " + Website.Exports.Length + " differ: " + string.Join("; ", wrong.Take(6)));
        }

        static void SizesLikeTheWebsite()
        {
            foreach (var s in Website.Sizes)
            {
                Profile p = Profiles.ById(s.Machine);
                Export.Size(p.Width, p.AutoHeight ? 1000 : p.Height, p.ParX, p.ParY, s.Multiplier, s.Tv, out int w, out int h);
                Check(w == s.Width && h == s.Height, s.Machine + " at " + s.Multiplier + "×" + (s.Tv ? " as on a TV" : "") + " is " + w + "×" + h + ", not " + s.Width + "×" + s.Height);
            }
        }

        static void NamesLikeTheWebsite()
        {
            foreach (var n in Website.BaseNames)
                Check(Export.BaseName(n.Name) == n.Base, "\"" + n.Name + "\" becomes \"" + Export.BaseName(n.Name) + "\", not the website's \"" + n.Base + "\"");
            Check(Export.FileName("photo", "nes", 256, 240, 4, false) == "photo-nes-256x240-4x.png", "the plain name");
            Check(Export.FileName("photo", "cga-1", 320, 200, 3, true) == "photo-cga-1-320x200-3x-tv.png", "the TV name");
        }

        // a big picture on all the cores, then on one: the same floats, so the same picture
        static void SameOnEveryCore()
        {
            const int w = 3001, h = 2003;
            var bgra = new byte[w * h * 4];
            var random = new Random(77);
            random.NextBytes(bgra);
            for (int i = 3; i < bgra.Length; i += 64) bgra[i] = 255;
            var picture = new PictureSource(bgra, w, h);
            var s = new DeresSettings { ProfileId = "web-safe", Dither = DitherIds.Floyd, Custom = true, CustomWidth = 777 };
            try
            {
                Resample.Cores = null;
                float[] many = Resample.Area(picture, new RectD(0.5, 1.25, w - 1, h - 2.5), 777, 518);
                Resample.Cores = 1;
                float[] one = Resample.Area(picture, new RectD(0.5, 1.25, w - 1, h - 2.5), 777, 518);
                Check(many.SequenceEqual(one), "the resize differs between all the cores and one");
                var d = new Deres();
                d.SetSource(picture);
                byte[] a = d.Render(s).Indices;
                Resample.Cores = null;
                var d2 = new Deres();
                d2.SetSource(picture);
                Check(a.SequenceEqual(d2.Render(s).Indices), "the picture differs between all the cores and one");
            }
            finally
            {
                Resample.Cores = null;
            }
        }
    }
}
