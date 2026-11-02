using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using B0r3dDeRes.Engine;

namespace B0r3dDeRes
{
    // What went wrong opening a picture, worded for people.
    sealed class PictureException : Exception
    {
        public PictureException(string message) : base(message) { }
    }

    // An opened picture: the whole photo for the engine, and what the window says about it.
    sealed class LoadedPicture
    {
        public PictureSource Source;
        public string Name;         // "photo.jpg", or "image.png" for a pasted picture, as the website names those
        public int Frames = 1;      // images in the file (a GIF can hold many; this version shows the first)
    }

    // Pictures in: files (through Windows' own picture readers), the clipboard, drops and the
    // samples inside the exe. Every picture comes out upright and in sRGB, the way browsers show
    // it: turned by its EXIF orientation, and with its colour profile applied, except that a
    // profile that is sRGB already leaves the pixels as they are (browsers leave those alone,
    // where Windows would nudge them by a level or so).
    static class Pictures
    {
        // The biggest picture opened. A 48 MP phone photo takes about 200 MB; this is a camera's
        // 150 MP, about 600 MB, set so a big picture never runs the PC out of memory.
        public const long MaxPixels = 150000000;

        static readonly string[] Extensions =
        {
            ".png", ".jpg", ".jpeg", ".jpe", ".jfif", ".bmp", ".dib", ".gif", ".tif", ".tiff", ".ico",
            ".webp", ".heic", ".heif", ".hif", ".avif", ".jxl", ".jxr", ".wdp", ".hdp",
            ".dng", ".cr2", ".cr3", ".nef", ".arw", ".orf", ".rw2", ".raf", ".srw", ".pef",
        };

        public static string DialogPattern => string.Join(";", Extensions.Select(e => "*" + e));

        public static bool LooksLikePicture(string path) => Extensions.Contains(Path.GetExtension(path).ToLowerInvariant());

        // the samples, by the website's ids, with their file names
        public static readonly (string Id, string Label, string File, string Menu)[] Samples =
        {
            ("rukus", "Rukus", "rukus.png", "_Rukus"),
            ("forest", "Crystal forest", "crystal-forest.jpg", "Crystal _forest"),
            ("cat-nap", "Cat nap", "cat-nap.jpg", "Cat _nap"),
        };

        public static LoadedPicture Sample(string id)
        {
            var sample = Samples.First(s => s.Id == id);
            using (Stream stream = typeof(Pictures).Assembly.GetManifestResourceStream("B0r3dDeRes.samples." + sample.File))
            {
                if (stream == null) throw new PictureException("the sample " + sample.Label + " is missing from this build");
                var bytes = new MemoryStream();
                stream.CopyTo(bytes);
                return FromBytes(bytes.ToArray(), sample.File);
            }
        }

        // a file's name for saying what happened to it, even when the path is no proper path
        public static string NameOf(string path)
        {
            try
            {
                return Path.GetFileName(path);
            }
            catch (ArgumentException)
            {
                return path;
            }
        }

        // A picture file. Safe on any thread.
        public static LoadedPicture Load(string path) => FromBytes(ReadFile(path), NameOf(path));

        // A file's bytes, or a PictureException saying why not. Safe on any thread.
        public static byte[] ReadFile(string path)
        {
            byte[] bytes;
            try
            {
                if (Directory.Exists(path)) throw new PictureException("it's a folder (folders of pictures come in a later version)");
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    if (stream.Length > int.MaxValue) throw new PictureException("it's too big to open");
                    bytes = new byte[stream.Length];
                    int read = 0;
                    while (read < bytes.Length)
                    {
                        int n = stream.Read(bytes, read, bytes.Length - read);
                        if (n == 0) break;
                        read += n;
                    }
                }
            }
            catch (FileNotFoundException) { throw new PictureException("it isn't there any more"); }
            catch (DirectoryNotFoundException) { throw new PictureException("it isn't there any more"); }
            catch (PathTooLongException) { throw new PictureException("its path is too long for Windows"); }
            catch (UnauthorizedAccessException) { throw new PictureException("Windows won't let b0r3d DeRes open it"); }
            catch (IOException e) { throw new PictureException(e.Message.TrimEnd('.')); }
            // a name Windows doesn't take for a file ("*.png", "a:b.png", a path ending in a quote)
            catch (Exception e) when (e is ArgumentException || e is NotSupportedException || e is System.Security.SecurityException)
            {
                throw new PictureException("that isn't a file Windows can open");
            }
            return bytes;
        }

        // A palette picture's different colours (see-through or not) in reading order, at most
        // 256, from its pixels as the file has them (no colour profile, as the website reads one).
        public static string[] PaletteColours(string path)
        {
            byte[] bytes = ReadFile(path);
            try
            {
                BitmapDecoder decoder = BitmapDecoder.Create(new MemoryStream(bytes, false), BitmapCreateOptions.IgnoreColorProfile | BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.None);
                BitmapFrame frame = decoder.Frames[0];
                if ((long)frame.PixelWidth * frame.PixelHeight > MaxPixels) throw new PictureException("it's too big for a palette");
                byte[] bgra = Pixels(frame, out int width, out int height);
                return PaletteFile.FromPixels(bgra, width, height);
            }
            catch (PictureException) { throw; }
            catch (OutOfMemoryException) { throw new PictureException("it's too big to open with the memory this PC has free"); }
            catch (Exception e) when (e is NotSupportedException || e is FileFormatException || e is ArgumentException || e is IOException || e is ExternalException ||
                e is InvalidOperationException || e is OverflowException)
            {
                throw new PictureException(Damaged);
            }
        }

        // A picture from its file's bytes. Safe on any thread.
        public static LoadedPicture FromBytes(byte[] bytes, string name)
        {
            string ext = Path.GetExtension(name).ToLowerInvariant();
            if (ext == ".pdf") throw new PictureException("it's a PDF: b0r3d DeRes opens pictures");
            try
            {
                BitmapDecoder decoder = BitmapDecoder.Create(new MemoryStream(bytes, false), BitmapCreateOptions.IgnoreColorProfile | BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.None);
                BitmapFrame frame;
                if (decoder is IconBitmapDecoder) frame = decoder.Frames.OrderByDescending(f => (long)f.PixelWidth * f.PixelHeight).First();
                else frame = decoder.Frames[0];
                int frames = decoder is GifBitmapDecoder ? decoder.Frames.Count : 1;
                long pixels = (long)frame.PixelWidth * frame.PixelHeight;
                if (pixels > MaxPixels)
                    throw new PictureException("it's " + (pixels / 1e6).ToString("0") + " megapixels, and the most b0r3d DeRes opens is " + (MaxPixels / 1000000) + " megapixels");
                BitmapSource source = InSrgb(frame);
                byte[] bgra;
                int width, height;
                try
                {
                    bgra = Pixels(source, out width, out height);
                }
                catch (Exception e) when (source != frame && (e is ExternalException || e is InvalidOperationException || e is ArgumentException || e is NotSupportedException))
                {
                    // a colour profile Windows can't use after all: the pixels as they are, as browsers do
                    bgra = Pixels(frame, out width, out height);
                }
                if (decoder is GifBitmapDecoder) bgra = OnTheScreen(decoder, frame, bgra, ref width, ref height);
                bgra = Upright(bgra, ref width, ref height, Orientation(frame));
                return new LoadedPicture { Source = new PictureSource(bgra, width, height), Name = name, Frames = frames };
            }
            catch (PictureException) { throw; }
            catch (NotSupportedException) { throw new PictureException(WhyNot(ext)); }
            catch (FileFormatException) { throw new PictureException("it isn't a picture, or it's damaged"); }
            catch (ArgumentException) { throw new PictureException("it isn't a picture, or it's damaged"); }
            catch (IOException e) { throw new PictureException(e.Message.TrimEnd('.')); }
            catch (OutOfMemoryException) { throw new PictureException("it's too big to open with the memory this PC has free"); }
            // Windows' picture readers can fail in other ways too (a HEIC photo without the HEVC
            // extension, a broken TIFF): still just this picture's problem
            catch (Exception e) when (e is ExternalException || e is InvalidOperationException || e is OverflowException)
            {
                string why = WhyNot(ext);
                throw new PictureException(why != Unreadable && why != Damaged ? why : "Windows couldn't read it (" + e.Message.TrimEnd('.') + ")");
            }
        }

        const string Unreadable = "Windows can't open this kind of picture", Damaged = "it isn't a picture, or it's damaged";

        // why Windows can't open a kind of picture: most need one of Microsoft's free extensions
        static string WhyNot(string ext)
        {
            switch (ext)
            {
                case ".heic":
                case ".heif":
                case ".hif":
                    return "HEIC photos need Microsoft's free HEIF Image Extensions and HEVC Video Extensions (in the Microsoft Store)";
                case ".avif": return "AVIF pictures need Microsoft's free AV1 Video Extension (in the Microsoft Store)";
                case ".webp": return "WebP pictures need Microsoft's free WebP Image Extensions (in the Microsoft Store)";
                case ".jxl": return "JPEG XL pictures need Microsoft's free JPEG XL Image Extension (in the Microsoft Store)";
                case ".dng":
                case ".cr2":
                case ".cr3":
                case ".nef":
                case ".arw":
                case ".orf":
                case ".rw2":
                case ".raf":
                case ".srw":
                case ".pef":
                    return "camera RAW photos need Microsoft's free Raw Image Extension (in the Microsoft Store)";
                // the kinds Windows always reads: this one is broken, or isn't what its name says
                case ".png":
                case ".jpg":
                case ".jpeg":
                case ".jpe":
                case ".jfif":
                case ".gif":
                case ".bmp":
                case ".dib":
                case ".tif":
                case ".tiff":
                case ".ico":
                    return Damaged;
                default: return Unreadable;
            }
        }

        // The frame in sRGB: as it is when it has no colour profile or an sRGB one, else turned
        // into sRGB through its profile.
        static BitmapSource InSrgb(BitmapFrame frame)
        {
            ColorContext profile = null;
            try
            {
                profile = frame.ColorContexts?.FirstOrDefault();
            }
            catch (Exception e) when (e is NotSupportedException || e is InvalidOperationException || e is ExternalException)
            {
            }
            if (profile == null) return frame;
            // one Windows can't read is no use (browsers ignore a broken profile too), and an sRGB one changes nothing
            byte[] icc = ProfileBytes(profile);
            if (icc == null || ColourProfile.IsSrgb(icc)) return frame;
            try
            {
                return new ColorConvertedBitmap(frame, profile, new ColorContext(PixelFormats.Bgra32), PixelFormats.Bgra32);
            }
            catch (Exception e) when (e is ArgumentException || e is NotSupportedException || e is ExternalException || e is InvalidOperationException || e is NullReferenceException)
            {
                return frame;   // a profile Windows can't use: the pixels as they are, as for none
            }
        }

        static byte[] ProfileBytes(ColorContext context)
        {
            try
            {
                using (Stream stream = context.OpenProfileStream())
                {
                    var bytes = new MemoryStream();
                    stream.CopyTo(bytes);
                    return bytes.ToArray();
                }
            }
            // (WPF throws a NullReferenceException for a profile it couldn't read: that one's no use either)
            catch (Exception e) when (e is IOException || e is NotSupportedException || e is InvalidOperationException || e is ExternalException || e is NullReferenceException || e is ArgumentException)
            {
                return null;
            }
        }

        // the pixels as blue, green, red, alpha bytes, a band of rows at a time
        static byte[] Pixels(BitmapSource source, out int width, out int height)
        {
            var bgra = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
            width = bgra.PixelWidth;
            height = bgra.PixelHeight;
            int stride = width * 4;
            var pixels = new byte[(long)stride * height];
            int band = Math.Max(1, (8 << 20) / stride);
            var buffer = new byte[stride * Math.Min(band, height)];
            for (int top = 0; top < height; top += band)
            {
                int rows = Math.Min(band, height - top);
                bgra.CopyPixels(new Int32Rect(0, top, width, rows), buffer, stride, 0);
                Buffer.BlockCopy(buffer, 0, pixels, top * stride, rows * stride);
            }
            return pixels;
        }

        // A GIF's first picture where it sits on the GIF's own "screen" (usually all of it), the
        // rest see-through, as browsers show it.
        static byte[] OnTheScreen(BitmapDecoder decoder, BitmapFrame frame, byte[] bgra, ref int width, ref int height)
        {
            int screenW = width, screenH = height, left = 0, top = 0;
            try
            {
                if (decoder.Metadata is BitmapMetadata screen)
                {
                    screenW = Convert.ToInt32(screen.GetQuery("/logscrdesc/Width"));
                    screenH = Convert.ToInt32(screen.GetQuery("/logscrdesc/Height"));
                }
                if (frame.Metadata is BitmapMetadata image)
                {
                    left = Convert.ToInt32(image.GetQuery("/imgdesc/Left"));
                    top = Convert.ToInt32(image.GetQuery("/imgdesc/Top"));
                }
            }
            catch (Exception e) when (e is NotSupportedException || e is InvalidOperationException || e is ArgumentException || e is ExternalException || e is InvalidCastException || e is FormatException)
            {
                return bgra;
            }
            if (screenW <= 0 || screenH <= 0 || (long)screenW * screenH > MaxPixels) return bgra;
            if (screenW == width && screenH == height && left == 0 && top == 0) return bgra;
            var canvas = new byte[(long)screenW * screenH * 4];
            for (int y = 0; y < height; y++)
            {
                int sy = top + y;
                if (sy < 0 || sy >= screenH) continue;
                int from = Math.Max(0, -left), to = Math.Min(width, screenW - left);
                if (to > from) Buffer.BlockCopy(bgra, (y * width + from) * 4, canvas, (sy * screenW + left + from) * 4, (to - from) * 4);
            }
            width = screenW;
            height = screenH;
            return canvas;
        }

        // the EXIF orientation (1 to 8), or 1 when there's none
        static int Orientation(BitmapFrame frame)
        {
            try
            {
                if (frame.Metadata is BitmapMetadata m && m.ContainsQuery("System.Photo.Orientation") && m.GetQuery("System.Photo.Orientation") is ushort o && o >= 1 && o <= 8) return o;
            }
            catch (Exception e) when (e is NotSupportedException || e is InvalidOperationException || e is ArgumentException || e is ExternalException)
            {
                // no metadata to read: it's the right way up
            }
            return 1;
        }

        // Turns the pixels the right way up. Phones save photos sideways with a note of how to
        // turn them (EXIF's orientation), which browsers follow.
        internal static byte[] Upright(byte[] bgra, ref int width, ref int height, int orientation)
        {
            if (orientation <= 1 || orientation > 8) return bgra;
            int w = width, h = height;
            bool swap = orientation >= 5;
            int ow = swap ? h : w, oh = swap ? w : h;
            var output = new byte[bgra.LongLength];
            System.Threading.Tasks.Parallel.For(0, oh, y =>
            {
                long o = (long)y * ow * 4;
                for (int x = 0; x < ow; x++, o += 4)
                {
                    // where output pixel (x, y) comes from in the stored picture
                    int sx, sy;
                    switch (orientation)
                    {
                        case 2: sx = w - 1 - x; sy = y; break;                 // mirrored
                        case 3: sx = w - 1 - x; sy = h - 1 - y; break;         // upside down
                        case 4: sx = x; sy = h - 1 - y; break;                 // mirrored upside down
                        case 5: sx = y; sy = x; break;                         // mirrored, turned left
                        case 6: sx = y; sy = h - 1 - x; break;                 // turned right (a phone held upright)
                        case 7: sx = w - 1 - y; sy = h - 1 - x; break;         // mirrored, turned right
                        default: sx = w - 1 - y; sy = x; break;                // 8: turned left
                    }
                    long s = ((long)sy * w + sx) * 4;
                    output[o] = bgra[s];
                    output[o + 1] = bgra[s + 1];
                    output[o + 2] = bgra[s + 2];
                    output[o + 3] = bgra[s + 3];
                }
            });
            width = ow;
            height = oh;
            return output;
        }

        // ---- copied and dropped pictures ----

        // Is there a picture in what was copied or dropped: files, or a picture?
        public static bool CanRead(IDataObject data)
        {
            if (data == null) return false;
            try
            {
                return data.GetDataPresent(DataFormats.FileDrop) || data.GetDataPresent("PNG") || data.GetDataPresent(DataFormats.Bitmap) || data.GetDataPresent(DataFormats.Dib);
            }
            catch (COMException)
            {
                return false;
            }
        }

        // What was copied or dropped: the picture files in it, or a picture, taken from it here
        // (it's the window's thread's to read) and opened by `open` on any thread. A PNG comes
        // first if there is one: it keeps see-through parts, which plain bitmaps lose.
        public static void From(IDataObject data, out string[] files, out Func<LoadedPicture> open)
        {
            files = null;
            open = null;
            if (data.GetDataPresent(DataFormats.FileDrop) && data.GetData(DataFormats.FileDrop) is string[] paths && paths.Length > 0)
            {
                files = paths;
                return;
            }
            Func<LoadedPicture> plain = PlainPicture(data);
            if (data.GetDataPresent("PNG") && data.GetData("PNG") is MemoryStream png)
            {
                byte[] bytes = png.ToArray();
                open = () =>
                {
                    try
                    {
                        return FromBytes(bytes, "image.png");
                    }
                    catch (PictureException) when (plain != null)
                    {
                        return plain();  // not a usable PNG after all: the plain bitmap
                    }
                };
                return;
            }
            open = plain;
        }

        static Func<LoadedPicture> PlainPicture(IDataObject data)
        {
            if (!data.GetDataPresent(DataFormats.Bitmap) || !(data.GetData(DataFormats.Bitmap) is BitmapSource bitmap)) return null;
            if (bitmap.CanFreeze && !bitmap.IsFrozen) bitmap.Freeze();
            LoadedPicture Read(BitmapSource from)
            {
                byte[] bgra = Pixels(from, out int width, out int height);
                // a plain bitmap has no see-through parts, whatever its alpha bytes say
                for (long i = 3; i < bgra.LongLength; i += 4) bgra[i] = 255;
                return new LoadedPicture { Source = new PictureSource(bgra, width, height), Name = "image.png" };
            }
            if (bitmap.IsFrozen) return () => Read(bitmap);
            LoadedPicture now = Read(bitmap);  // one that can't be handed to another thread is read here
            return () => now;
        }
    }
}
