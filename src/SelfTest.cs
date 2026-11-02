using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using B0r3dDeRes.Engine;

namespace B0r3dDeRes
{
    // b0r3dDeRes.exe --selftest <file>: no window shown. Opens a sample from inside the exe, makes
    // the picture, saves it as a PNG the way File → Save PNG does and reads that back through
    // Windows, builds the windows in both looks with the picture in them, and writes what it found
    // to <file> as key=value lines; the exit code is 0 when it all worked. The tests run it on the
    // finished exe, on its own in a folder, to prove it works with nothing beside it.
    static class SelfTest
    {
        public static int Run(string reportPath)
        {
            var lines = new List<string>();
            bool ok = true;
            try
            {
                lines.Add("version=" + Program.FullVersion);
                lines.Add("dpi=" + Native.DpiAwareness());
                lines.Add("bits=" + (Environment.Is64BitProcess ? "64" : "32"));
                ok &= Environment.Is64BitProcess;

                // a sample from inside the exe, made into a Game Boy picture
                LoadedPicture rukus = Pictures.Sample("rukus");
                var engine = new Deres();
                engine.SetSource(rukus.Source);
                Rendered r = engine.Render(new DeresSettings());
                bool rendered = r.Width == 160 && r.Height == 144 && r.Used >= 2 && r.Used <= 4;
                lines.Add("render=" + (rendered ? "ok" : "failed"));
                ok &= rendered;

                // saved, then read back by Windows: the same pixels, and the palette kept
                byte[] png = Export.PngBytes(r, 3, false);
                BitmapFrame frame = BitmapDecoder.Create(new MemoryStream(png), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
                bool indexed = frame.Format == PixelFormats.Indexed2 && frame.Palette != null && frame.Palette.Colors.Count == 4;
                var back = new FormatConvertedBitmap(frame, PixelFormats.Rgb24, null, 0);
                var pixels = new byte[back.PixelWidth * back.PixelHeight * 3];
                back.CopyPixels(pixels, back.PixelWidth * 3, 0);
                bool same = back.PixelWidth == 480 && back.PixelHeight == 432;
                for (int y = 0; same && y < 432; y++)
                    for (int x = 0; same && x < 480; x++)
                    {
                        int s = ((y / 3) * 160 + x / 3) * 3, o = (y * 480 + x) * 3;
                        same = pixels[o] == r.Rgb[s] && pixels[o + 1] == r.Rgb[s + 1] && pixels[o + 2] == r.Rgb[s + 2];
                    }
                lines.Add("png=" + (same && indexed ? "ok" : "failed"));
                ok &= same && indexed;

                // the samples are all inside: each opens
                bool samples = Pictures.Samples.All(sample => Pictures.Sample(sample.Id).Source.Width > 0);
                lines.Add("samples=" + (samples ? "ok" : "failed"));
                ok &= samples;

                // the windows, built from inside this exe and laid out (never shown), in both looks:
                // their pages, styles and pictures all load
                var app = new App();
                app.InitializeComponent();
                // a window that throws ends the self-test with a line in the report, never a message box
                App.OnProblem = x =>
                {
                    lines.Add("error=a window threw " + x.GetType().Name + ": " + Flat(x.Message));
                    lines.Add("result=failed");
                    Environment.Exit(Report(reportPath, lines, false));
                };
                // (the window opens its picture off this thread and comes back to it)
                System.Threading.SynchronizationContext.SetSynchronizationContext(new System.Windows.Threading.DispatcherSynchronizationContext(System.Windows.Threading.Dispatcher.CurrentDispatcher));
                foreach (Look look in new[] { Look.Light, Look.Dark })
                {
                    Theme.Set(look);
                    var window = new MainWindow(new Settings(), null);
                    var until = DateTime.UtcNow.AddSeconds(20);
                    while ((window.Opening || window.Rendering || window.Shown == null) && DateTime.UtcNow < until) Pump();
                    window.Root.Measure(new Size(1280, 800));
                    window.Root.Arrange(new Rect(0, 0, 1280, 800));
                    window.Root.UpdateLayout();
                    var about = new AboutWindow();
                    about.Root.Measure(new Size(640, double.PositiveInfinity));
                    about.Root.Arrange(new Rect(about.Root.DesiredSize));
                    about.Root.UpdateLayout();
                    if (window.Shown == null || !window.View.HasPicture) throw new InvalidOperationException("the main window made no picture in the " + look + " look");
                }
                lines.Add("windows=ok");
            }
            catch (Exception e)
            {
                ok = false;
                string why = e.GetType().Name + ": " + e.Message;
                for (Exception inner = e.InnerException; inner != null; inner = inner.InnerException) why += " <- " + inner.GetType().Name + ": " + inner.Message;
                lines.Add("error=" + why.Replace('\r', ' ').Replace('\n', ' '));
            }
            lines.Add("result=" + (ok ? "ok" : "failed"));
            return Report(reportPath, lines, ok);
        }

        // lets the window's queued work run for a moment
        static void Pump()
        {
            var frame = new System.Windows.Threading.DispatcherFrame();
            System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() => frame.Continue = false));
            System.Windows.Threading.Dispatcher.PushFrame(frame);
            System.Threading.Thread.Sleep(5);
        }

        // a message on one line
        static string Flat(string text) => text.Replace('\r', ' ').Replace('\n', ' ');

        static int Report(string reportPath, List<string> lines, bool ok)
        {
            try
            {
                File.WriteAllLines(reportPath, lines);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is NotSupportedException || e is ArgumentException)
            {
                return 3;
            }
            return ok ? 0 : 1;
        }
    }
}
