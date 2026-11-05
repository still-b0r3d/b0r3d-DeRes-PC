using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using B0r3dDeRes.Engine;

namespace B0r3dDeRes
{
    // Characters the VGA font lacks (the katakana ramp, and whatever a typed-in ramp brings),
    // drawn as a browser borrows them: with Windows' own fonts (Courier New, then Consolas, then
    // whatever font Windows picks for the character), its baseline where the VGA font's is, in
    // its cell, smoothed. On the website the browser does the same, so these look alike but aren't
    // pixel for pixel. WPF draws on the window's thread: AsciiRender.Prepare calls this there.
    static class AsciiGlyphs
    {
        static readonly Typeface typeface = new Typeface(new FontFamily("Courier New, Consolas"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

        public static void Install() => AsciiRender.Fallback = Draw;

        // how much of each pixel of a 8m × 16m cell the character covers (0-255)
        static byte[] Draw(int c, int m)
        {
            if (Application.Current != null && !Application.Current.Dispatcher.CheckAccess()) throw new InvalidOperationException("Characters are drawn on the window's thread.");
            int w = AsciiRender.CellWidth * m, h = AsciiRender.CellHeight * m;
            string text = c >= 0x10000 ? char.ConvertFromUtf32(c) : ((char)c).ToString();
            var visual = new DrawingVisual();
            using (DrawingContext dc = visual.RenderOpen())
            {
                dc.DrawRectangle(Brushes.Black, null, new Rect(0, 0, w, h));
                var formatted = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, h, Brushes.White, 1.0);
                // the VGA font's baseline: 12 of its 16 rows down
                dc.PushClip(new RectangleGeometry(new Rect(0, 0, w, h)));
                dc.DrawText(formatted, new Point(0, 12.0 * m - formatted.Baseline));
                dc.Pop();
            }
            var bitmap = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            var bgra = new byte[w * h * 4];
            bitmap.CopyPixels(bgra, w * 4, 0);
            var cover = new byte[w * h];
            for (int i = 0; i < cover.Length; i++) cover[i] = bgra[i * 4 + 2];
            return cover;
        }
    }
}
