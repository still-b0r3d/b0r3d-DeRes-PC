using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using B0r3dDeRes.Engine;
using Geometry = B0r3dDeRes.Engine.Geometry;

namespace B0r3dDeRes.Tests
{
    static partial class TestMain
    {
        // A 48 MP photo (8000 × 6000, a phone's biggest): opened whole, made into a picture, then
        // made again as a slider moves and as the machine changes. Prints the times and memory.
        static void BigPhoto()
        {
            const int w = 8000, h = 6000;
            string file = Path.Combine(Work, "big.jpg");
            {
                var px = new byte[w * h * 4];
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        int i = (y * w + x) * 4;
                        px[i] = (byte)(x * 255 / w);
                        px[i + 1] = (byte)(y * 255 / h);
                        px[i + 2] = (byte)((x ^ y) & 255);
                        px[i + 3] = 255;
                    }
                BitmapSource source = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, px, w * 4);
                var jpg = new JpegBitmapEncoder { QualityLevel = 85 };
                jpg.Frames.Add(BitmapFrame.Create(source));
                using (FileStream stream = File.Create(file)) jpg.Save(stream);
            }
            GC.Collect();
            long before = Process.GetCurrentProcess().PrivateMemorySize64;
            var watch = Stopwatch.StartNew();
            LoadedPicture p = Pictures.Load(file);
            long open = watch.ElapsedMilliseconds;
            Check(p.Source.Width == w && p.Source.Height == h, "the big photo opened at " + p.Source.Width + "×" + p.Source.Height);
            var engine = new Deres();
            engine.SetSource(p.Source);
            watch.Restart();
            engine.Render(new DeresSettings { ProfileId = "nes", Dither = DitherIds.Floyd });
            long first = watch.ElapsedMilliseconds;
            watch.Restart();
            engine.Render(new DeresSettings { ProfileId = "nes", Dither = DitherIds.Floyd, Adjust = new Adjust { Contrast = 1.3 } });
            long slider = watch.ElapsedMilliseconds;
            watch.Restart();
            engine.Render(new DeresSettings { ProfileId = "web-safe", Dither = DitherIds.Yliluoma });
            long machine = watch.ElapsedMilliseconds;
            watch.Restart();
            Geometry g = Geometry.Plan(w, h, w, h, Profiles.ById("nes"), false, null, Fits.Fill);
            Original.Framed(p.Source, g, Backgrounds.White);
            long original = watch.ElapsedMilliseconds;
            long used = (Process.GetCurrentProcess().PrivateMemorySize64 - before) / (1024 * 1024);
            Console.WriteLine("      48 MP: opened in " + open + " ms, made in " + first + " ms, again after a slider in " + slider + " ms, on another machine in " + machine +
                " ms, the original for Compare in " + original + " ms; " + used + " MB more memory");
            Check(open < 10000 && first < 5000 && slider < 500 && machine < 5000, "too slow for a 48 MP photo (see the line above)");
            Check(used < 1500, "a 48 MP photo took " + used + " MB");
        }

        // Exports past the website's 16 MP: web-safe at 16× (10240 × 7680) and the receipt
        // printer's strip of the cat nap at 16× (6144 × 8192), saved and read back. Prints the times.
        static void BigExports()
        {
            var engine = new Deres();
            engine.SetSource(Input("cat-nap"));
            string folder = Path.Combine(Work, "big-exports");
            Fresh(folder);
            var notes = new System.Collections.Generic.List<string>();
            foreach (var (id, w, h) in new[] { ("web-safe", 10240, 7680), ("receipt", 6144, 8192) })
            {
                Rendered r = engine.Render(new DeresSettings { ProfileId = id, Dither = DitherIds.Floyd });
                string file = Path.Combine(folder, id + ".png");
                var watch = Stopwatch.StartNew();
                int width = 0, height = 0;
                Exports.Write(file, stream => Export.WritePng(stream, r, 16, false, out width, out height));
                long save = watch.ElapsedMilliseconds;
                Check(width == w && height == h, id + " at 16× is " + width + "×" + height + ", not " + w + "×" + h);
                // read back by Windows: its size, and a row from the middle against the picture
                watch.Restart();
                BitmapFrame frame = BitmapDecoder.Create(new Uri(file), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.None).Frames[0];
                Check(frame.PixelWidth == w && frame.PixelHeight == h, id + "'s PNG reads back at " + frame.PixelWidth + "×" + frame.PixelHeight);
                var row = new byte[w * 3];
                int y = h / 2 + 7;
                new FormatConvertedBitmap(frame, PixelFormats.Rgb24, null, 0).CopyPixels(new System.Windows.Int32Rect(0, y, w, 1), row, w * 3, 0);
                for (int x = 0; x < w; x += 3)
                {
                    int s = ((y / 16) * r.Width + x / 16) * 3;
                    Check(row[x * 3] == r.Rgb[s] && row[x * 3 + 1] == r.Rgb[s + 1] && row[x * 3 + 2] == r.Rgb[s + 2], id + "'s PNG differs from the picture at (" + x + ", " + y + ")");
                }
                notes.Add(id + " " + w + "×" + h + " saved in " + save + " ms (" + new FileInfo(file).Length / 1024 + " KB), read back in " + watch.ElapsedMilliseconds + " ms");
                Check(save < 15000, id + " at 16× took " + save + " ms to save");
            }
            Console.WriteLine("      " + string.Join("; ", notes));
        }
    }
}
