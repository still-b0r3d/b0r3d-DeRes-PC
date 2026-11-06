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

        // the PC hadn't the memory free for it (a batch tries it again on its own)
        public bool OutOfMemory;
        // the file's bytes, when they were read (the EXIF tools still read a picture Windows can't show)
        public byte[] FileBytes;
        public string FilePath;
    }

    // An opened picture: the whole photo for the engine, and what the window says about it.
    sealed class LoadedPicture
    {
        public PictureSource Source;    // the picture (an animated GIF's first frame)
        public string Name;             // "photo.jpg", or "image.png" for a pasted picture, as the website names those
        public int Frames = 1;          // images in the file (a GIF can hold many)
        public AnimatedGif Animation;   // an animated GIF's frames, or null for a still
        public VideoClip Video;         // a video (its paused frame is the picture), or null
        // the file as it was opened, for the EXIF tools: its bytes (a picture's) and where it is
        // (null for a sample, a pasted picture or the screen)
        public byte[] FileBytes;
        public string FilePath;

        // frame k of an animated GIF (a still has only its picture; a video, its paused frame)
        public PictureSource SourceAt(int frame) => Animation != null ? Animation.Frames[Math.Max(0, Math.Min(Animation.Frames.Length - 1, frame))] : Video != null ? Video.Source : Source;

        public int FrameCount => Animation?.Frames.Length ?? 1;
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

        // An animated GIF's frames are held whole, at the GIF's own size, while all of them come
        // to no more than this many pixels (128 MP, 512 MB: 400 frames of 640×480, eight times the
        // website's room); past it they're shrunk to fit, down to 256 pixels on the long side, and
        // past that only the first frames are kept, as on the website. Its screen may be up to
        // 8192 × 8192, with up to 20,000 frames read in at most a minute. (A test shrinks a small
        // GIF with less.)
        internal static long GifBudget = 128L * 1024 * 1024;
        public static GifLimits GifLimits => new GifLimits { MaxPixels = 8192L * 8192, MaxFrames = 20000, MaxMs = 60000 };

        static readonly string[] Extensions =
        {
            ".png", ".jpg", ".jpeg", ".jpe", ".jfif", ".bmp", ".dib", ".gif", ".tif", ".tiff", ".ico",
            ".webp", ".heic", ".heif", ".hif", ".avif", ".jxl", ".jxr", ".wdp", ".hdp",
            ".dng", ".cr2", ".cr3", ".nef", ".arw", ".orf", ".rw2", ".raf", ".srw", ".pef",
        };

        public static string DialogPattern => string.Join(";", Extensions.Select(e => "*" + e));

        public static bool LooksLikePicture(string path) => Extensions.Contains(Path.GetExtension(path).ToLowerInvariant());

        // Online-only files (OneDrive and the like keep just a placeholder until one is opened):
        // a folder given to a batch leaves them alone rather than downloading every one.
        // (b0r3d QR's.)
        public static bool OnlyOnline(string path)
        {
            const int Offline = 0x1000, RecallOnOpen = 0x40000, RecallOnDataAccess = 0x400000;
            try
            {
                return ((int)File.GetAttributes(path) & (Offline | RecallOnOpen | RecallOnDataAccess)) != 0;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException || e is NotSupportedException)
            {
                return false;  // reading it will say what's wrong
            }
        }

        // the samples, by the website's ids, with their file names (the GIF last, on a line of its own)
        public static readonly (string Id, string Label, string File, string Menu)[] Samples =
        {
            ("rukus", "Rukus", "rukus.png", "_Rukus"),
            ("forest", "Crystal forest", "crystal-forest.jpg", "Crystal _forest"),
            ("cat-nap", "Cat nap", "cat-nap.jpg", "Cat _nap"),
            ("rukus-gif", "Rukus breaks out", "rukus-breaks-out.gif", "Rukus _breaks out (GIF)"),
        };

        public static LoadedPicture Sample(string id)
        {
            var sample = Samples.First(s => s.Id == id);
            using (Stream stream = typeof(Pictures).Assembly.GetManifestResourceStream("B0r3dDeRes.samples." + sample.File))
            {
                if (stream == null) throw new PictureException("the sample " + sample.Label + " is missing from this build");
                var bytes = new MemoryStream();
                stream.CopyTo(bytes);
                byte[] file = bytes.ToArray();
                LoadedPicture p = FromBytes(file, sample.File);
                p.FileBytes = file;
                return p;
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

        // A picture file (with its bytes, for the EXIF tools). Safe on any thread.
        public static LoadedPicture Load(string path)
        {
            byte[] bytes = ReadFile(path);
            try
            {
                LoadedPicture p = FromBytes(bytes, NameOf(path));
                p.FileBytes = bytes;
                p.FilePath = path;
                return p;
            }
            catch (PictureException x)
            {
                x.FileBytes = bytes;
                x.FilePath = path;
                throw;
            }
        }

        // A picture of the screen (or a box of it), named for what it is ("screen.png").
        public static LoadedPicture FromScreen(BitmapSource shot, string name)
        {
            var converted = new FormatConvertedBitmap(shot, PixelFormats.Bgr32, null, 0);
            int width = shot.PixelWidth, height = shot.PixelHeight;
            var bgra = new byte[(long)width * height * 4];
            converted.CopyPixels(bgra, width * 4, 0);
            for (long i = 3; i < bgra.Length; i += 4) bgra[i] = 255;
            return new LoadedPicture { Source = new PictureSource(bgra, width, height), Name = name };
        }

        // A file's bytes, or a PictureException saying why not. Safe on any thread.
        public static byte[] ReadFile(string path)
        {
            byte[] bytes;
            try
            {
                if (Directory.Exists(path)) throw new PictureException("it's a folder (File → Batch takes a folder of pictures)");
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
            catch (OutOfMemoryException) { throw new PictureException("it's too big to open with the memory this PC has free") { OutOfMemory = true }; }
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
            // An animated GIF: every frame through DeRes's own decoder, as the website opens one
            // (the frame count from its blocks first, so a still GIF costs nothing). One that won't
            // open that way opens as a still below.
            if (GifInfo.FrameCount(bytes) > 1)
            {
                AnimatedGif gif;
                try
                {
                    gif = GifFrames.Open(bytes, double.PositiveInfinity, GifBudget, GifLimits);
                }
                catch (OutOfMemoryException)
                {
                    throw new PictureException("it's too big to open with the memory this PC has free") { OutOfMemory = true };
                }
                if (gif != null) return new LoadedPicture { Source = gif.Frames[0], Name = name, Frames = gif.Total, Animation = gif };
            }
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
            catch (OutOfMemoryException) { throw new PictureException("it's too big to open with the memory this PC has free") { OutOfMemory = true }; }
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
                        LoadedPicture p = FromBytes(bytes, "image.png");
                        p.FileBytes = bytes;
                        return p;
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
