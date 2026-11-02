using System;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using B0r3dDeRes.Engine;
using Geometry = B0r3dDeRes.Engine.Geometry;

namespace B0r3dDeRes
{
    // Pictures for showing, as opposed to the ones that are made: the original photo framed the
    // way the machine frames it (for Compare and holding the picture), and the finished picture
    // enlarged for the clipboard.
    static class Original
    {
        // The photo framed as the machine's picture is: its part of the photo where the picture
        // is, black in the "fit" bars, and the chosen background under its see-through parts. At
        // most 2048 pixels on a side, and never more pixels than the photo has there; it's shown
        // smooth, at the picture's size.
        public static BitmapSource Framed(PictureSource source, Geometry g, string background)
        {
            double f = Math.Min(2048.0 / g.Width, 2048.0 / g.Height);
            f = Math.Min(f, Math.Max(1, Math.Max(g.Src.W / g.Dst.W, g.Src.H / g.Dst.H)));
            int w = Math.Max(1, (int)Math.Round(g.Width * f)), h = Math.Max(1, (int)Math.Round(g.Height * f));
            int dx = Math.Min(w - 1, (int)Math.Round(g.Dst.X * f)), dy = Math.Min(h - 1, (int)Math.Round(g.Dst.Y * f));
            int dw = Math.Max(1, Math.Min(w - dx, (int)Math.Round(g.Dst.W * f))), dh = Math.Max(1, Math.Min(h - dy, (int)Math.Round(g.Dst.H * f)));
            float[] premul = Resample.Area(source, g.Src, dw, dh);
            var bgr = new byte[w * h * 4];   // black, the bars' colour
            double under = background == Backgrounds.Black ? 0 : 1;
            for (int y = 0; y < dh; y++)
                for (int x = 0; x < dw; x++)
                {
                    int i = (y * dw + x) * 4, o = ((dy + y) * w + dx + x) * 4;
                    double a = premul[i + 3], bg = under * (1 - a);
                    bgr[o] = (byte)Colour.LinearToByte(premul[i + 2] + bg);
                    bgr[o + 1] = (byte)Colour.LinearToByte(premul[i + 1] + bg);
                    bgr[o + 2] = (byte)Colour.LinearToByte(premul[i] + bg);
                }
            BitmapSource picture = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgr32, null, bgr, w * 4);
            picture.Freeze();
            return picture;
        }
    }

    static class Sharing
    {
        // The finished picture at the export's size, as a plain picture (blocks of whole pixels,
        // stretched as on a TV when `tv`), for programs that don't take PNG.
        public static BitmapSource Picture(Rendered r, int multiplier, bool tv)
        {
            int m = Math.Max(1, multiplier);
            Export.Size(r.Width, r.Height, r.Geometry.ParX, r.Geometry.ParY, m, tv, out int w, out int h);
            var column = new int[w];
            for (int x = 0; x < w; x++) column[x] = (int)Math.Min(r.Width - 1, Math.Floor((double)x * r.Width / w));
            var bgr = new byte[(long)w * h * 4];
            for (int y = 0; y < h; y++)
            {
                int sy = y / m * r.Width;
                long o = (long)y * w * 4;
                for (int x = 0; x < w; x++, o += 4)
                {
                    int s = (sy + column[x]) * 3;
                    bgr[o] = r.Rgb[s + 2];
                    bgr[o + 1] = r.Rgb[s + 1];
                    bgr[o + 2] = r.Rgb[s];
                }
            }
            BitmapSource picture = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgr32, null, bgr, w * 4);
            picture.Freeze();
            return picture;
        }
    }
}
