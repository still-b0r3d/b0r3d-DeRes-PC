using System;
using System.Collections.Generic;
using System.Globalization;
using B0r3dDeRes.Engine;

namespace B0r3dDeRes
{
    // The lines the window shows about the picture, in the website's words (its ui/controls.ts and
    // ui/app.ts), so the PC and img.b0r3d.org read the same.
    static class Words
    {
        // a number to one place, as the website writes it: 4.5, 3, 12.3
        static string OneDp(double v) => (JsMath.Round(v * 10) / 10).ToString(CultureInfo.InvariantCulture);

        // a whole number, rounded as the website rounds (halves up)
        static string Whole(double v) => JsMath.Round(v).ToString(CultureInfo.InvariantCulture);

        // The line under Resolution: the machine's own size, a custom size against it, and how many
        // of the photo's pixels go into each of the machine's (no picture: the first part only).
        public static string Resolution(Profile p, bool custom, Geometry g, double coverX, double coverY, bool hasPicture)
        {
            string shape = p.SquarePixels ? "" : p.ParX < p.ParY ? ", each taller than wide" : ", each wider than tall";
            var parts = new List<string>
            {
                p.AutoHeight ? p.Name + ": " + p.Width + " pixels across, as long as the photo needs." : p.Name + ": " + p.Width + "×" + p.Height + " pixels" + shape + ".",
            };
            if (custom && g != null)
            {
                double r = (double)g.Width / p.Width;
                string against = g.Width == p.Width ? "the same width" : r < 1 ? Whole(r * 100) + "% of its width" : OneDp(r) + "× its width";
                parts.Add("Yours: " + g.Width + "×" + g.Height + ", " + against + ".");
            }
            if (hasPicture)
            {
                double mean = Math.Sqrt(coverX * coverY);
                parts.Add(mean < 1
                    ? "Your photo is smaller, so it’s enlarged " + OneDp(1 / mean) + "×."
                    : "Each pixel averages " + OneDp(coverX) + " × " + OneDp(coverY) + " of your photo’s pixels.");
            }
            return string.Join(" ", parts);
        }

        // "14 of 16 colours used · 320×200 · 5:6 pixels"
        public static string Readout(Rendered r)
        {
            int colours = r.Palette.Length / 3;
            string shape = r.Geometry.SquarePixels ? "square pixels" : r.Geometry.ParX + ":" + r.Geometry.ParY + " pixels";
            return r.Used + " of " + colours + " colours used · " + r.Width + "×" + r.Height + " · " + shape;
        }

        // "rukus.png · 256×256", and "· first of 58 frames" for a GIF's first picture
        public static string PictureInfo(LoadedPicture picture)
        {
            if (picture == null) return "No picture yet";
            string text = picture.Name + " · " + picture.Source.Width + "×" + picture.Source.Height;
            return picture.Frames > 1 ? text + " · first of " + picture.Frames + " frames" : text;
        }

        public static string Native(Profile p) => "Native " + p.Width + "×" + (p.AutoHeight ? "auto" : p.Height.ToString(CultureInfo.InvariantCulture));

        public static string About(Profile p) => p.Origin + ". " + p.About;

        public static string DetailHint(bool nothingToBringBack) => nothingToBringBack
            ? "Your photo isn’t shrunk here, so there’s nothing for it to bring back."
            : "Brings back thin lines (eyes, outlines, stripes) that shrinking blurs away. 0% is a plain average.";

        public static string ShapeNote(Geometry g) =>
            "This machine's pixels are " + g.ParX + ":" + g.ParY + " (width:height). \"As on a TV\" stretches the PNG to match.";

        // "640×576 px · 4-colour palette"
        public static string SizeLine(int width, int height, Profile p) => width + "×" + height + " px · " + p.Colours.Length + "-colour palette";

        // the sliders' values, as the website shows them
        public static string Percent(double v) => Whole(v * 100) + "%";
        public static string SignedPercent(double v) => (v > 0 ? "+" : "") + Whole(v * 100);
        public static string Gamma(double v) => v.ToString("0.00", CultureInfo.InvariantCulture);
    }
}
