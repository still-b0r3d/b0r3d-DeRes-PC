using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using B0r3dDeRes.Engine;
using Geometry = B0r3dDeRes.Engine.Geometry;

namespace B0r3dDeRes.Tests
{
    // Opening pictures: the kinds Windows reads, turned upright, in sRGB, and the ways in.
    static partial class TestMain
    {
        // a picture in four quarters (red, green, blue, white), so turns and flips show
        static BitmapSource Quarters(int w, int h)
        {
            var px = new byte[w * h * 4];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int i = (y * w + x) * 4;
                    bool right = x >= w / 2, low = y >= h / 2;
                    byte r = (byte)(!right && !low || right && low ? 255 : 0), g = (byte)(right && !low || right && low ? 255 : 0), b = (byte)(!right && low || right && low ? 255 : 0);
                    px[i] = b;
                    px[i + 1] = g;
                    px[i + 2] = r;
                    px[i + 3] = 255;
                }
            BitmapSource source = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, px, w * 4);
            source.Freeze();
            return source;
        }

        // the colour in the middle of each quarter: "R G B W" for top left, top right, bottom left, bottom right
        static string QuarterColours(PictureSource p)
        {
            string At(int x, int y)
            {
                int i = (y * p.Width + x) * 4;
                int b = p.Bgra[i], g = p.Bgra[i + 1], r = p.Bgra[i + 2];
                return r > 200 && g > 200 && b > 200 ? "W" : r > 200 ? "R" : g > 200 ? "G" : b > 200 ? "B" : "?";
            }
            return At(p.Width / 4, p.Height / 4) + " " + At(p.Width * 3 / 4, p.Height / 4) + " " + At(p.Width / 4, p.Height * 3 / 4) + " " + At(p.Width * 3 / 4, p.Height * 3 / 4);
        }

        static byte[] Encode(BitmapEncoder encoder, BitmapSource source, BitmapMetadata metadata = null)
        {
            encoder.Frames.Add(BitmapFrame.Create(source, null, metadata, null));
            var bytes = new MemoryStream();
            encoder.Save(bytes);
            return bytes.ToArray();
        }

        static void ReadsPictureFiles()
        {
            BitmapSource quarters = Quarters(64, 48);
            var kinds = new (string Name, BitmapEncoder Encoder)[]
            {
                ("q.png", new PngBitmapEncoder()), ("q.jpg", new JpegBitmapEncoder { QualityLevel = 100 }), ("q.bmp", new BmpBitmapEncoder()),
                ("q.gif", new GifBitmapEncoder()), ("q.tif", new TiffBitmapEncoder()),
            };
            string folder = Path.Combine(Work, "kinds");
            Fresh(folder);
            foreach (var kind in kinds)
            {
                string file = Path.Combine(folder, kind.Name);
                File.WriteAllBytes(file, Encode(kind.Encoder, quarters));
                LoadedPicture p = Pictures.Load(file);
                Check(p.Source.Width == 64 && p.Source.Height == 48 && p.Name == kind.Name, kind.Name + " opened as " + p.Source.Width + "×" + p.Source.Height);
                Check(QuarterColours(p.Source) == "R G B W", kind.Name + " came out " + QuarterColours(p.Source));
                Check(!p.Source.HasAlpha, kind.Name + " has no see-through parts, yet it says it has");
            }

            // a TIFF's first page
            var tiff = new TiffBitmapEncoder();
            tiff.Frames.Add(BitmapFrame.Create(Quarters(30, 20)));
            tiff.Frames.Add(BitmapFrame.Create(Quarters(50, 50)));
            var tiffBytes = new MemoryStream();
            tiff.Save(tiffBytes);
            Check(Pictures.FromBytes(tiffBytes.ToArray(), "pages.tif").Source.Width == 30, "a TIFF should open at its first page");

            // what can't be opened says why, in words
            string Why(Action open)
            {
                try
                {
                    open();
                    return null;
                }
                catch (PictureException x)
                {
                    return x.Message;
                }
            }
            File.WriteAllText(Path.Combine(folder, "broken.png"), "not really a picture");
            Check(Why(() => Pictures.Load(Path.Combine(folder, "broken.png"))) == "it isn't a picture, or it's damaged", "a broken file: " + Why(() => Pictures.Load(Path.Combine(folder, "broken.png"))));
            Check(Why(() => Pictures.Load(Path.Combine(folder, "gone.png"))) == "it isn't there any more", "a missing file: " + Why(() => Pictures.Load(Path.Combine(folder, "gone.png"))));
            Check((Why(() => Pictures.FromBytes(Encoding.ASCII.GetBytes("%PDF-1.7"), "doc.pdf")) ?? "").Contains("PDF"), "a PDF should say it's a PDF");
            string heic = Why(() => Pictures.FromBytes(Encoding.ASCII.GetBytes("nonsense nonsense"), "phone.heic"));
            Check(heic != null && (heic.Contains("HEIF Image Extensions") || heic.Contains("damaged")), "a HEIC that won't open: " + heic);
        }

        // the eight EXIF orientations, as phones and cameras write them
        static void TurnsPhotosUpright()
        {
            // each orientation's picture as the camera stores it (turned, mirrored or both: 5 is
            // mirrored across its diagonal, 7 across the other), which the note turns upright
            BitmapSource upright = Quarters(64, 48);
            var turned = new Dictionary<int, Transform>
            {
                { 1, Transform.Identity }, { 2, new ScaleTransform(-1, 1) }, { 3, new RotateTransform(180) }, { 4, new ScaleTransform(1, -1) },
                { 5, new TransformGroup { Children = { new ScaleTransform(-1, 1), new RotateTransform(-90) } } },
                { 6, new RotateTransform(-90) }, { 7, new TransformGroup { Children = { new ScaleTransform(-1, 1), new RotateTransform(90) } } }, { 8, new RotateTransform(90) },
            };
            foreach (var pair in turned)
            {
                // stored: the upright picture with the turn undone, as the camera wrote it
                BitmapSource stored = new TransformedBitmap(upright, pair.Value);
                var metadata = new BitmapMetadata("jpg");
                metadata.SetQuery("/app1/ifd/{ushort=274}", (ushort)pair.Key);
                byte[] jpg = Encode(new JpegBitmapEncoder { QualityLevel = 100 }, stored, metadata);
                PictureSource p = Pictures.FromBytes(jpg, "turned-" + pair.Key + ".jpg").Source;
                Check(p.Width == 64 && p.Height == 48 && QuarterColours(p) == "R G B W", "orientation " + pair.Key + " came out " + p.Width + "×" + p.Height + " as " + QuarterColours(p));
            }
        }

        // ---- colour profiles ----

        static void Be32(Stream s, uint v)
        {
            s.WriteByte((byte)(v >> 24));
            s.WriteByte((byte)(v >> 16));
            s.WriteByte((byte)(v >> 8));
            s.WriteByte((byte)v);
        }

        // A v2 RGB display profile with red's and green's primaries swapped, and a 2.2 curve.
        static byte[] SwapProfile()
        {
            byte[] Xyz(double x, double y, double z)
            {
                var m = new MemoryStream();
                m.Write(Encoding.ASCII.GetBytes("XYZ \0\0\0\0"), 0, 8);
                foreach (double v in new[] { x, y, z }) Be32(m, (uint)(int)Math.Round(v * 65536));
                return m.ToArray();
            }
            byte[] curve = { (byte)'c', (byte)'u', (byte)'r', (byte)'v', 0, 0, 0, 0, 0, 0, 0, 1, 2, 51, 0, 0 };
            byte[] desc = new byte[] { (byte)'d', (byte)'e', (byte)'s', (byte)'c', 0, 0, 0, 0, 0, 0, 0, 5 }.Concat(Encoding.ASCII.GetBytes("swap\0")).Concat(new byte[79]).ToArray();
            var tags = new List<(string, byte[])>
            {
                ("desc", desc), ("wtpt", Xyz(0.9642, 1.0, 0.8249)), ("rXYZ", Xyz(0.3851, 0.7169, 0.0971)), ("gXYZ", Xyz(0.4361, 0.2225, 0.0139)),
                ("bXYZ", Xyz(0.1431, 0.0606, 0.7141)), ("rTRC", curve), ("gTRC", curve), ("bTRC", curve), ("cprt", Encoding.ASCII.GetBytes("text\0\0\0\0none\0\0\0\0")),
            };
            int at = 128 + 4 + tags.Count * 12;
            var table = new MemoryStream();
            var body = new MemoryStream();
            Be32(table, (uint)tags.Count);
            foreach (var (name, data) in tags)
            {
                while ((at + body.Length) % 4 != 0) body.WriteByte(0);
                table.Write(Encoding.ASCII.GetBytes(name), 0, 4);
                Be32(table, (uint)(at + body.Length));
                Be32(table, (uint)data.Length);
                body.Write(data, 0, data.Length);
            }
            var header = new MemoryStream();
            Be32(header, (uint)(128 + table.Length + body.Length));
            header.Write(Encoding.ASCII.GetBytes("none"), 0, 4);
            Be32(header, 0x02100000);
            header.Write(Encoding.ASCII.GetBytes("mntrRGB XYZ "), 0, 12);
            header.Write(new byte[12], 0, 12);
            header.Write(Encoding.ASCII.GetBytes("acsp"), 0, 4);
            header.Write(new byte[28], 0, 28);
            foreach (double v in new[] { 0.9642, 1.0, 0.8249 }) Be32(header, (uint)(int)Math.Round(v * 65536));
            header.Write(new byte[48], 0, 48);
            return header.ToArray().Concat(table.ToArray()).Concat(body.ToArray()).ToArray();
        }

        static uint Crc(byte[] bytes)
        {
            uint c = 0xffffffff;
            foreach (byte b in bytes)
            {
                c ^= b;
                for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xedb88320 ^ (c >> 1) : c >> 1;
            }
            return c ^ 0xffffffff;
        }

        static void Chunk(Stream png, string type, byte[] data)
        {
            Be32(png, (uint)data.Length);
            byte[] typed = Encoding.ASCII.GetBytes(type).Concat(data).ToArray();
            png.Write(typed, 0, typed.Length);
            Be32(png, Crc(typed));
        }

        static byte[] Zlib(byte[] data)
        {
            var m = new MemoryStream();
            m.WriteByte(0x78);
            m.WriteByte(0x9C);
            using (var z = new DeflateStream(m, CompressionLevel.Optimal, true)) z.Write(data, 0, data.Length);
            uint a = 1, b = 0;
            foreach (byte x in data)
            {
                a = (a + x) % 65521;
                b = (b + a) % 65521;
            }
            Be32(m, (b << 16) | a);
            return m.ToArray();
        }

        // a 4×1 PNG (red, green, blue, grey 128), with a colour profile in its iCCP chunk
        static byte[] ProfiledPng(byte[] icc)
        {
            var png = new MemoryStream();
            png.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, 0, 8);
            var ihdr = new MemoryStream();
            Be32(ihdr, 4);
            Be32(ihdr, 1);
            ihdr.Write(new byte[] { 8, 2, 0, 0, 0 }, 0, 5);
            Chunk(png, "IHDR", ihdr.ToArray());
            Chunk(png, "iCCP", Encoding.ASCII.GetBytes("test\0\0").Concat(Zlib(icc)).ToArray());
            Chunk(png, "IDAT", Zlib(new byte[] { 0, 255, 0, 0, 0, 255, 0, 0, 0, 255, 128, 128, 128 }));
            Chunk(png, "IEND", new byte[0]);
            return png.ToArray();
        }

        static void AppliesColourProfiles()
        {
            // a profile that isn't sRGB is applied: its "red" is sRGB's green
            byte[] swap = SwapProfile();
            Check(!ColourProfile.IsSrgb(swap), "the swapped profile passed as sRGB");
            PictureSource p = Pictures.FromBytes(ProfiledPng(swap), "swap.png").Source;
            byte[] px = p.Bgra;
            Check(px[2] < 30 && px[1] > 225 && px[0] < 30, "the profile's red should come out green, not " + px[2] + "," + px[1] + "," + px[0]);
            Check(px[6] > 225 && px[5] < 30 && px[4] < 30, "the profile's green should come out red, not " + px[6] + "," + px[5] + "," + px[4]);
            Check(Math.Abs(px[14] - 128) <= 4 && px[12] == px[14], "grey should stay grey, about as light: " + px[14] + "," + px[13] + "," + px[12]);

            // a broken profile (it says it has more tags than it holds) doesn't stop the picture
            // opening, as it doesn't in a browser
            byte[] broken = swap.Take(140).ToArray();
            broken[131] = 99;
            Check(!ColourProfile.IsSrgb(broken) && !ColourProfile.IsSrgb(new byte[] { 1, 2, 3 }), "a broken profile passed as sRGB");
            PictureSource despite = Pictures.FromBytes(ProfiledPng(broken), "broken-profile.png").Source;
            Check(despite.Width == 4 && despite.Height == 1, "a picture with a broken colour profile didn't open");

            // an sRGB profile leaves the pixels alone, as browsers do: the samples' two kinds of it
            foreach (string sample in new[] { "rukus", "forest" })
            {
                var name = Pictures.Samples.First(s => s.Id == sample).File;
                byte[] bytes;
                using (Stream stream = typeof(Pictures).Assembly.GetManifestResourceStream("B0r3dDeRes.samples." + name))
                {
                    var m = new MemoryStream();
                    stream.CopyTo(m);
                    bytes = m.ToArray();
                }
                BitmapFrame frame = BitmapDecoder.Create(new MemoryStream(bytes), BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad).Frames[0];
                var raw = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
                var rawPixels = new byte[raw.PixelWidth * raw.PixelHeight * 4];
                raw.CopyPixels(rawPixels, raw.PixelWidth * 4, 0);
                Check(Pictures.Sample(sample).Source.Bgra.SequenceEqual(rawPixels), name + "'s sRGB profile changed its pixels");
            }

            // and the Rukus sample opens as Chromium opened it for the website (its see-through
            // edges aside: browsers keep those premultiplied, which rounds them a little)
            PictureSource rukus = Pictures.Sample("rukus").Source, chromium = Input("rukus");
            int opaqueDiffer = 0;
            for (int i = 0; i < rukus.Bgra.Length; i += 4)
                if (rukus.Bgra[i + 3] == 255 && (rukus.Bgra[i] != chromium.Bgra[i] || rukus.Bgra[i + 1] != chromium.Bgra[i + 1] || rukus.Bgra[i + 2] != chromium.Bgra[i + 2])) opaqueDiffer++;
            Check(opaqueDiffer == 0, opaqueDiffer + " solid pixels of Rukus differ from the website's working copy");
        }

        // How Windows' JPEG reader compares with Chromium's on the two sample photos: close, not
        // the same (the pixel tests start from Chromium's, for that reason). Fails if they're far apart.
        static void JpegsLikeTheBrowser()
        {
            var notes = new List<string>();
            foreach (var (id, input) in new[] { ("cat-nap", "cat-nap"), ("forest", "crystal-forest") })
            {
                PictureSource mine = Pictures.Sample(id).Source, chrome = Input(input);
                Check(mine.Width == chrome.Width && mine.Height == chrome.Height, id + " opened at a different size");
                int differ = 0, most = 0;
                for (int i = 0; i < mine.Bgra.Length; i++)
                {
                    int d = Math.Abs(mine.Bgra[i] - chrome.Bgra[i]);
                    if (d > 0) differ++;
                    most = Math.Max(most, d);
                }
                notes.Add(id + " " + (100.0 * differ / mine.Bgra.Length).ToString("0.0") + "% of channels differ, by at most " + most);
                Check(most <= 12, id + ": Windows' JPEG reader is far from Chromium's (" + notes.Last() + ")");
            }
            Console.WriteLine("      " + string.Join("; ", notes));
        }

        // ---- GIFs ----

        // A GIF made by hand: a 10×8 screen, its first picture 4×3 at (3, 2), and a second
        // picture. The codes are written 3 bits wide, starting over every two pixels, so no
        // compressor is needed.
        static byte[] OffsetGif()
        {
            var gif = new MemoryStream();
            byte[] head = Encoding.ASCII.GetBytes("GIF89a");
            gif.Write(head, 0, head.Length);
            gif.Write(new byte[] { 10, 0, 8, 0, 0xF1, 0, 0 }, 0, 7);                 // screen 10×8, a 4-colour table
            gif.Write(new byte[] { 0, 0, 0, 255, 0, 0, 0, 255, 0, 0, 0, 255 }, 0, 12);  // black, red, green, blue
            foreach (var (left, top, w, h, colour) in new[] { (3, 2, 4, 3, 1), (0, 0, 10, 8, 2) })
            {
                gif.Write(new byte[] { 0x21, 0xF9, 4, 0, 10, 0, 0, 0 }, 0, 8);     // 0.1 s, no see-through colour
                gif.Write(new byte[] { 0x2C, (byte)left, 0, (byte)top, 0, (byte)w, 0, (byte)h, 0, 0 }, 0, 10);
                var codes = new List<int>();
                for (int i = 0; i < w * h; i++)
                {
                    if (i % 2 == 0) codes.Add(4);  // clear
                    codes.Add(colour);
                }
                codes.Add(5);  // end
                var data = new List<byte>();
                int bits = 0, have = 0;
                foreach (int code in codes)
                {
                    bits |= code << have;
                    have += 3;
                    while (have >= 8)
                    {
                        data.Add((byte)bits);
                        bits >>= 8;
                        have -= 8;
                    }
                }
                if (have > 0) data.Add((byte)bits);
                gif.WriteByte(2);  // the codes' starting size
                gif.WriteByte((byte)data.Count);
                gif.Write(data.ToArray(), 0, data.Count);
                gif.WriteByte(0);
            }
            gif.WriteByte(0x3B);
            return gif.ToArray();
        }

        static void GifsOpenAtTheirFirstPicture()
        {
            LoadedPicture p = Pictures.FromBytes(OffsetGif(), "moving.gif");
            Check(p.Frames == 2, "the GIF has 2 pictures, not " + p.Frames);
            Check(p.Source.Width == 10 && p.Source.Height == 8, "a GIF opens at its screen's size, not " + p.Source.Width + "×" + p.Source.Height);
            Check(p.Source.HasAlpha, "outside the first picture the screen is see-through");
            byte[] px = p.Source.Bgra;
            int At(int x, int y) => (y * 10 + x) * 4;
            Check(px[At(4, 3) + 2] == 255 && px[At(4, 3) + 3] == 255, "the first picture should be red where it is");
            Check(px[At(0, 0) + 3] == 0 && px[At(9, 7) + 3] == 0, "outside the first picture should be see-through");
            Check(Words.PictureInfo(p) == "moving.gif · 10×8 · 2 frames · 0.2 s", "the picture line: " + Words.PictureInfo(p));
            // its second frame covers the first, through DeRes's own decoder
            Check(p.Animation != null && p.Animation.Frames.Length == 2 && p.SourceAt(1).Bgra[At(0, 0) + 1] == 255 && p.SourceAt(1).Bgra[At(0, 0) + 3] == 255 && p.SourceAt(1).HasAlpha,
                "the second frame should be green all over (and count as see-through, as the first frame has see-through parts)");
            // a GIF with one picture opens as a still, through Windows
            LoadedPicture still = Pictures.FromBytes(GifFile("gif-still"), "still.gif");
            Check(still.Animation == null && still.Frames == 1 && Words.PictureInfo(still) == "still.gif · 3×2", "a one-frame GIF: " + Words.PictureInfo(still));
        }

        // ---- the clipboard and drops ----

        static void OpensCopiedAndDropped()
        {
            BitmapSource quarters = Quarters(40, 30);
            byte[] png = Encode(new PngBitmapEncoder(), quarters);
            MainWindow window = NewMain(1, false);

            var asPng = new DataObject();
            asPng.SetData("PNG", new MemoryStream(png), false);
            window.OpenData(asPng, "nothing");
            WaitForPicture(window);
            Check(window.PictureInfo.Text == "image.png · 40×30", "a pasted PNG: " + window.PictureInfo.Text);

            var asBitmap = new DataObject();
            asBitmap.SetImage(Quarters(24, 16));
            window.OpenData(asBitmap, "nothing");
            Pump(() => window.PictureInfo.Text.Contains("24×16"), 5000);
            WaitForPicture(window);
            Check(window.PictureInfo.Text == "image.png · 24×16", "a pasted bitmap: " + window.PictureInfo.Text);

            string folder = Path.Combine(Work, "dropped");
            Fresh(folder);
            File.WriteAllBytes(Path.Combine(folder, "notes.txt"), new byte[] { 1 });
            File.WriteAllBytes(Path.Combine(folder, "first.png"), png);
            File.WriteAllBytes(Path.Combine(folder, "second.png"), png);
            var files = new DataObject();
            files.SetFileDropList(new StringCollection { Path.Combine(folder, "notes.txt"), Path.Combine(folder, "first.png") });
            window.OpenData(files, "nothing");
            Pump(() => window.PictureInfo.Text.StartsWith("first.png", StringComparison.Ordinal), 5000);
            WaitForPicture(window);
            Check(window.PictureInfo.Text == "first.png · 40×30", "a dropped picture (with a note beside it) should open: " + window.PictureInfo.Text);

            // several pictures, or a folder: a batch, and the picture stays
            var several = new DataObject();
            several.SetFileDropList(new StringCollection { Path.Combine(folder, "notes.txt"), Path.Combine(folder, "second.png"), Path.Combine(folder, "first.png") });
            window.OpenData(several, "nothing");
            BatchWindow batch = window.BatchShown;
            Check(batch != null && batch.Files.Select(Path.GetFileName).SequenceEqual(new[] { "second.png", "first.png" }), "two dropped pictures should go to the batch, in their order");
            batch?.Close();
            var aFolder = new DataObject();
            aFolder.SetFileDropList(new StringCollection { folder });
            window.OpenData(aFolder, "nothing");
            batch = window.BatchShown;
            Check(batch != null && batch.Files.Select(Path.GetFileName).SequenceEqual(new[] { "first.png", "second.png" }), "a dropped folder should give the batch its pictures, by name, and not the note");
            Check(batch?.Into == Path.Combine(folder, "DeRes"), "the batch should go into a DeRes folder beside them: " + batch?.Into);
            batch?.Close();
            Check(window.PictureInfo.Text.StartsWith("first.png", StringComparison.Ordinal), "a dropped folder changed the picture");

            var text = new DataObject();
            text.SetText("hello");
            Check(!Pictures.CanRead(text), "text isn't a picture to open");

            File.WriteAllText(Path.Combine(folder, "bad.png"), "not a picture");
            window.OpenFile(Path.Combine(folder, "bad.png"));
            Pump(() => !window.Opening, 5000);
            Check(window.NoteText.StartsWith("Couldn't open bad.png: it isn't a picture", StringComparison.Ordinal), "a broken file should say why: " + window.NoteText);
            Check(window.PictureInfo.Text.StartsWith("first.png", StringComparison.Ordinal), "a broken file replaced the picture");

            // what a command line can hand over that isn't a file: said, never a crash or "Opening…" for good
            foreach (string odd in new[] { @"C:\My Pictures""", "*.png", "a:b.png", folder })
            {
                window.OpenFile(odd);
                Pump(() => !window.Opening, 5000);
                Check(!window.Opening && window.NoteText.StartsWith("Couldn't open ", StringComparison.Ordinal), "\"" + odd + "\" should say it couldn't open: " + window.NoteText);
                Check(window.PictureInfo.Text.StartsWith("first.png", StringComparison.Ordinal), "\"" + odd + "\" replaced the picture");
            }
            Check(window.NoteText.Contains("folder"), "a folder should say it's a folder: " + window.NoteText);
        }
    }
}
