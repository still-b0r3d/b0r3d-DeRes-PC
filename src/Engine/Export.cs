using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace B0r3dDeRes.Engine
{
    // A wallpaper: a size the picture is enlarged into (by the biggest whole number that fits),
    // centred on its backdrop colour.
    sealed class Wallpaper
    {
        public string Id, Name;
        public int Width, Height;
    }

    // Exports: the picture enlarged by a whole number, nearest neighbour, so every pixel stays a
    // crisp block. Without the screen effect it's an indexed PNG holding the machine's exact
    // palette (open it in a pixel-art editor and the palette is there); a look that makes its own
    // colours (the PlayStation, HAM, the N64 filter, the analog looks) holds exactly the colours it
    // uses instead, or goes full colour past 256 of them. With the screen effect it's a full-colour
    // PNG. Pixels kept see-through stay so: the indexed PNG gets one more palette entry that's
    // see-through (or goes full colour with alpha when the palette already has 256), the
    // full-colour PNG gets alpha. A wallpaper stays solid: its see-through pixels take its
    // backdrop colour. (The website's core/export.ts and core/filename.ts.)
    static class Export
    {
        // the export sizes offered (× the machine's resolution)
        public static readonly int[] Multipliers = { 1, 2, 3, 4, 6, 8, 12, 16 };

        // screen effects only make it into exports from this size up
        public const int FxMinMultiplier = 3;

        // whether an export at `multiplier` has the screen effect (null or off: none)
        public static bool WithFx(FxSettings fx, int multiplier) => fx != null && fx.Screen != Screens.Off && multiplier >= FxMinMultiplier;

        // the website's three (by its ids, which go in the file names)
        public static readonly Wallpaper[] Wallpapers =
        {
            new Wallpaper { Id = "phone", Name = "Phone", Width = 1080, Height = 2400 },
            new Wallpaper { Id = "win95", Name = "b0r3d 95", Width = 1024, Height = 768 },
            new Wallpaper { Id = "desktop-1080p", Name = "Full HD", Width = 1920, Height = 1080 },
        };

        // the size of an export at `multiplier`, stretched to the machine's pixel shape when `tv`
        public static void Size(int width, int height, int parX, int parY, int multiplier, bool tv, out int w, out int h)
        {
            w = tv && parX != parY ? (int)JsMath.Round((double)width * multiplier * parX / parY) : width * multiplier;
            h = height * multiplier;
        }

        // The PNG, written to `output`, with the screen effect `fx` from 3× up (null: none), and
        // through the analog signal when there is one (`signal`, always kept: full colour). TV
        // pixels only stretch when the pixels aren't square.
        public static void WritePng(Stream output, Rendered r, int multiplier, bool tv, FxSettings fx, out int width, out int height, SignalImage signal = null)
        {
            int m = Math.Max(1, multiplier);
            Size(r.Width, r.Height, r.Geometry.ParX, r.Geometry.ParY, m, tv, out width, out height);
            int w = width;
            byte[] alpha = r.Alpha;
            if (signal != null)
            {
                RowRenderer render = Signal.Renderer(signal, width, height, WithFx(fx, m) ? fx : null, 0, r.Paper);
                Engine.Png.WriteTruecolour(output, width, height, signal.Alpha != null, Bands(render, signal.Alpha != null, null));
                return;
            }
            if (WithFx(fx, m))
            {
                var screen = new FxRenderer(r.Rgb, r.Width, r.Height, r.Paper, alpha, width, height, fx);
                Engine.Png.WriteTruecolour(output, width, height, alpha != null, Bands(screen, alpha != null, null));
                return;
            }
            // which picture column each output column shows
            var column = new int[w];
            for (int x = 0; x < w; x++) column[x] = (int)Math.Min(r.Width - 1, Math.Floor((double)x * r.Width / w));
            // the pixels as palette entries: the machine's own palette (every entry, in its order)
            // when its pixels are entries of it, else exactly the colours that show
            byte[] palette = r.Indexed ? r.Palette : Census.Palette(r.Rgb, alpha);
            byte[] indices = r.Indexed ? r.Indices : palette != null ? Census.EntriesIn(palette, r.Rgb, alpha) : null;
            int colours = palette != null ? palette.Length / 3 : 0;

            // full colour: too many colours for a palette, or a full one with no room for a see-through entry
            if (palette == null || colours > 256 || colours == 0 || (alpha != null && colours == 256))
            {
                Engine.Png.WriteTruecolour(output, width, height, alpha != null, (y, row) =>
                {
                    int sy = y / m * r.Width;
                    int channels = alpha != null ? 4 : 3;
                    for (int x = 0; x < w; x++)
                    {
                        int s = sy + column[x], o = x * channels;
                        row[o] = r.Rgb[s * 3];
                        row[o + 1] = r.Rgb[s * 3 + 1];
                        row[o + 2] = r.Rgb[s * 3 + 2];
                        if (alpha != null) row[o + 3] = alpha[s];
                    }
                });
                return;
            }

            // see-through pixels point at one more entry, after the machine's colours
            byte[] entries = alpha != null ? palette.Concat(new byte[3]).ToArray() : palette;
            Engine.Png.WriteIndexed(output, width, height, entries, (y, row) =>
            {
                int sy = y / m * r.Width;
                for (int x = 0; x < w; x++)
                {
                    int s = sy + column[x];
                    row[x] = alpha != null && alpha[s] == 0 ? (byte)colours : indices[s];
                }
            }, alpha != null ? colours : (int?)null);
        }

        public static byte[] PngBytes(Rendered r, int multiplier, bool tv, FxSettings fx = null, SignalImage signal = null)
        {
            using (var bytes = new MemoryStream())
            {
                WritePng(bytes, r, multiplier, tv, fx, out _, out _, signal);
                return bytes.ToArray();
            }
        }

        // A screen effect's (or the signal's) rows for the PNG writer, made a band at a time on
        // every core: r, g, b (and a) a pixel, or with `backdrop`, solid, its hidden pixels in that colour.
        static Action<int, byte[]> Bands(RowRenderer screen, bool withAlpha, byte[] backdrop)
        {
            const int band = 64;
            byte[][] rows = null;
            int first = 0;
            return (y, row) =>
            {
                if (rows == null || y < first || y >= first + rows.Length)
                {
                    first = y;
                    rows = screen.Rows(y, Math.Min(band, screen.Height - y), withAlpha, backdrop);
                }
                Buffer.BlockCopy(rows[y - first], 0, row, 0, row.Length);
            };
        }

        // A wallpaper of `width` × `height`: the picture enlarged by the biggest whole number that
        // fits (as on a TV when `tv`), with the screen effect from 3× up and through the signal
        // when there is one, centred on the backdrop (its palette's first colour); a picture too
        // big even at 1× shows its middle.
        public static void WriteWallpaper(Stream output, Rendered r, int width, int height, bool tv, FxSettings fx, SignalImage signal = null)
        {
            int best = 1;
            for (int m = 1; m <= 32; m++)
            {
                Size(r.Width, r.Height, r.Geometry.ParX, r.Geometry.ParY, m, tv, out int sw, out int sh);
                if (sw <= width && sh <= height) best = m;
                else break;
            }
            Size(r.Width, r.Height, r.Geometry.ParX, r.Geometry.ParY, best, tv, out int scaledW, out int scaledH);
            int offsetX = (int)Math.Floor((width - scaledW) / 2.0), offsetY = (int)Math.Floor((height - scaledH) / 2.0);
            byte[] back = r.Palette.Length >= 3 ? new[] { r.Palette[0], r.Palette[1], r.Palette[2] } : new byte[3];
            int w = width;

            // where each wallpaper column falls in the picture as enlarged (-1: the backdrop)
            var at = new int[w];
            for (int x = 0; x < w; x++) at[x] = x < offsetX || x >= offsetX + scaledW ? -1 : x - offsetX;

            if (WithFx(fx, best) || signal != null)
            {
                RowRenderer screen = signal != null ? Signal.Renderer(signal, scaledW, scaledH, WithFx(fx, best) ? fx : null, 0, r.Paper)
                    : new FxRenderer(r.Rgb, r.Width, r.Height, r.Paper, r.Alpha, scaledW, scaledH, fx);
                Action<int, byte[]> rows = Bands(screen, false, back);
                var shown = new byte[scaledW * 3];
                Engine.Png.WriteTruecolour(output, width, height, false, (y, row) =>
                {
                    int sy = y - offsetY;
                    if (sy >= 0 && sy < scaledH) rows(sy, shown);
                    for (int x = 0; x < w; x++)
                    {
                        int s = sy >= 0 && sy < scaledH ? at[x] : -1;
                        byte[] from = s < 0 ? back : shown;
                        int o = s < 0 ? 0 : s * 3;
                        row[x * 3] = from[o];
                        row[x * 3 + 1] = from[o + 1];
                        row[x * 3 + 2] = from[o + 2];
                    }
                });
                return;
            }

            var column = new int[Math.Max(0, scaledW)];
            for (int x = 0; x < scaledW; x++) column[x] = (int)Math.Min(r.Width - 1, Math.Floor((double)x * r.Width / scaledW));
            byte[] alpha = r.Alpha;
            Engine.Png.WriteTruecolour(output, width, height, false, (y, row) =>
            {
                bool inside = y >= offsetY && y < offsetY + scaledH;
                int sy = inside ? (y - offsetY) / best * r.Width : 0;
                for (int x = 0; x < w; x++)
                {
                    int s = inside && at[x] >= 0 ? sy + column[at[x]] : -1;
                    bool hidden = s < 0 || alpha != null && alpha[s] == 0;
                    row[x * 3] = hidden ? back[0] : r.Rgb[s * 3];
                    row[x * 3 + 1] = hidden ? back[1] : r.Rgb[s * 3 + 1];
                    row[x * 3 + 2] = hidden ? back[2] : r.Rgb[s * 3 + 2];
                }
            });
        }

        // what's inside, in the name: photo-nes-phone-wallpaper.png
        public static string WallpaperName(string baseName, string profileId, string wallpaperId) => baseName + "-" + profileId + "-" + wallpaperId + "-wallpaper.png";

        // A file name's stem, safe for a file name ("IMG_2041.HEIC" -> "img-2041").
        public static string BaseName(string fileName)
        {
            string stem = Regex.Replace(fileName ?? "", @"\.[^./\\]*$", "");
            // JavaScript lowers a dotted capital I (U+0130) to i and a dot above (which then goes
            // like the other accents), and the Kelvin and Angstrom signs (U+212A, U+212B) to k and
            // a ringed a; .NET Framework leaves those three as they are.
            string clean = stem.Replace(((char)0x130).ToString(), "i" + (char)0x307).Replace((char)0x212A, 'k').Replace((char)0x212B, (char)0xE5)
                .ToLowerInvariant().Normalize(NormalizationForm.FormKD);
            // then the accents go (the combining marks, U+0300 to U+036F)
            clean = new string(clean.Where(c => c < 0x300 || c > 0x36F).ToArray());
            clean = Regex.Replace(clean, "[^a-z0-9]+", "-");
            clean = Regex.Replace(clean, "^-+|-+$", "");
            if (clean.Length > 40) clean = clean.Substring(0, 40);
            clean = Regex.Replace(clean, "-+$", "");
            return clean.Length > 0 ? clean : "photo";
        }

        // what's inside, in the name: photo-nes-256x240-4x.png, photo-nes-256x240-4x-tv-rf-fx.png
        // (`signal`: the connection the signal went over, "rf", "composite", "vhs-sp"…)
        public static string FileName(string baseName, string profileId, int width, int height, int multiplier, bool tv, bool fx = false, string signal = null) =>
            baseName + "-" + profileId + "-" + width.ToString(CultureInfo.InvariantCulture) + "x" + height.ToString(CultureInfo.InvariantCulture) + "-" +
            multiplier.ToString(CultureInfo.InvariantCulture) + "x" + (tv ? "-tv" : "") + (signal != null ? "-" + signal : "") + (fx ? "-fx" : "") + ".png";

        // a machine's own file: photo-zx-spectrum.scr
        public static string EmulatorFileName(string baseName, string profileId, string format) => baseName + "-" + profileId + "." + format;
    }
}
