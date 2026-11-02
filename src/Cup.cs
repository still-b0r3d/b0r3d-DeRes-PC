using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace B0r3dDeRes
{
    // The b0r3d cup from the icon, at the picture that suits the screen: its hand-made 32 px cup at
    // 100 %, the 48 px one at 150 %, and so on, rather than one picture stretched.
    static class Cup
    {
        static BitmapFrame[] frames;

        public static ImageSource For(double size, double scale)
        {
            if (frames == null)
            {
                // (read from inside the exe as a stream: BitmapDecoder given an address could fetch one)
                using (Stream stream = Application.GetResourceStream(new Uri("/b0r3dDeRes;component/assets/b0r3dDeRes.ico", UriKind.Relative)).Stream)
                {
                    var icon = BitmapDecoder.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                    frames = icon.Frames.OrderBy(f => f.PixelWidth).ToArray();
                }
            }
            double wanted = size * scale;
            return frames.FirstOrDefault(f => f.PixelWidth >= wanted - 0.5) ?? frames.Last();
        }
    }
}
