using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Threading;
using Microsoft.Win32;

namespace B0r3dDeRes.Tests
{
    // a test that can't run here (and says why) rather than one that failed
    sealed class SkipException : Exception
    {
        public SkipException(string why) : base(why) { }
    }

    sealed class CheckException : Exception
    {
        public CheckException(string why) : base(why) { }
    }

    // Runs every test and prints a PASS, FAIL or SKIP line for each. It never shows a dialog:
    // whatever goes wrong ends as a line, and a watchdog stops the run after 90 seconds.
    //   b0r3dDeResTests.exe                  run the tests
    //   b0r3dDeResTests.exe --shots <folder> draw the windows to PNG files (see Shots.cs)
    static partial class TestMain
    {
        static int passed, failed, skipped;
        static volatile string running = "starting";   // the test under way, for the watchdog

        // this exe is in bin\tests\; the program's exe is in bin\
        static readonly string TestsFolder = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
        static readonly string BinFolder = Path.GetDirectoryName(TestsFolder);
        static readonly string RepoFolder = Path.GetDirectoryName(BinFolder);
        static string OneExe => Path.Combine(BinFolder, "b0r3dDeRes.exe");

        // the tests' own scratch folder, next to this exe (never Temp: a test checks nothing lands there)
        static readonly string Work = Path.Combine(TestsFolder, "work");

        [STAThread]
        static int Main(string[] args)
        {
            AppDomain.CurrentDomain.UnhandledException += (s, e) => { Console.WriteLine("FAIL  crashed: " + e.ExceptionObject); Environment.Exit(3); };
            using (new Timer(_ => { Console.WriteLine("FAIL  still running after 90 seconds, so stopped (watchdog), during: " + running); Environment.Exit(4); }, null, 90000, Timeout.Infinite))
            {
                // the program's resources and styles, as when it runs; a window that throws is a FAIL line
                var app = new App();
                app.InitializeComponent();
                App.OnProblem = e => { Console.WriteLine("FAIL  a window threw: " + e); Environment.Exit(3); };
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
                Fresh(Work);
                DataFolder.Reset(Path.Combine(Work, "data"));

                if (args.Length == 2 && args[0] == "--shots")
                {
                    int shot = TakeShots(args[1]);
                    TryDelete(Work);
                    return shot;
                }
                if (args.Length > 0)
                {
                    Console.WriteLine("b0r3dDeResTests.exe [--shots <folder>]");
                    return 2;
                }

                List<string> footprint = Footprint();

                Run("version: the exe carries the version and the build", ExeVersion);
                Run("one file: the exe runs on its own, 64-bit, per-monitor DPI on, samples inside, and its windows load", ExeRunsOnItsOwn);
                Run("never goes online: no networking, web, XML, media, native or COM way out in the exe", ExeNeverGoesOnline);
                Run("safety flags: a 64-bit exe with Windows' address randomisation and DEP", ExeSafetyFlags);
                Run("file dialogs: Save and Open are told not to add to Recent items", FileDialogsStayOutOfRecent);
                Run("icon: the cup at every size, small ones as bitmaps, all of them pictures", ExeIcon);
                Run("icon check: tells static from a picture", StaticIsCaught);
                Run("build.cmd: plain ASCII with CRLF line endings", BuildCmdIsPlain);

                Run("maths: cube roots and powers as the browser works them out", MathsLikeTheBrowser);
                Run("maths: the colour tables are the website's, bit for bit", TablesLikeTheWebsite);
                Run("maths: the gamma table for every stop of the Gamma slider is the website's", GammaTablesLikeTheWebsite);
                Run("machines: the same " + Website.Profiles.Length + " machines as the website, word for word, colour for colour, limit for limit", MachinesLikeTheWebsite);
                Run("dithers: the same nine as the website, in its order and words, the machines' own marked", DithersLikeTheWebsite);
                Run("pictures: " + Website.Renders.Length + " made by the website's engine come out the same, pixel for pixel, tile meter and machine files too", PicturesLikeTheWebsite);
                Run("pictures: the Game Boy Color with no whole tile comes out black (the website stops)", TinyGameBoyColor);
                Run("grabber: the website's colours from every test picture", GrabsLikeTheWebsite);
                Run("share links: the website's very links, made from the same settings", LinksLikeTheWebsite);
                Run("share links: " + Website.Decodes.Length + " links read as the website reads them, odd and broken ones too", LinksReadLikeTheWebsite);
                Run("numbers: " + Website.NumberTexts.Length + " doubles written and read as JavaScript writes and reads them", NumbersLikeJavaScript);
                Run("palette files: read and written as the website does", PaletteFilesLikeTheWebsite);
                Run("look files: written, read back, and the website's own settings read as one", LookFilesRoundTrip);
                Run("odd text: nesting deeper than a stack and megabyte numbers in links and look files do no harm", OddTextDoesNoHarm);
                Run("geometry: crop, fit, stretch and custom widths frame the picture as the website does", GeometryLikeTheWebsite);
                Run("resolution line: the website's words for every picture above", ResolutionWordsLikeTheWebsite);
                Run("PNG: " + Website.Exports.Length + " exports with the website's header, palette and pixel rows", PngsLikeTheWebsite);
                Run("PNG: sizes, raw and as on a TV, for every machine and Size", SizesLikeTheWebsite);
                Run("file names: the website's names, photo-nes-256x240-4x.png", NamesLikeTheWebsite);
                Run("engine: the same pictures on one core as on all of them", SameOnEveryCore);
                Run("big photo: 48 MP opens whole and makes its picture in good time", BigPhoto);
                Run("big exports: 16× web-safe and a long receipt strip save in good time and read back", BigExports);

                Run("pictures: PNG, JPEG, BMP, GIF and TIFF open; a broken, missing or PDF file says why", ReadsPictureFiles);
                Run("pictures: all eight EXIF orientations come out upright", TurnsPhotosUpright);
                Run("pictures: other colour profiles are applied, sRGB ones leave the pixels as browsers do", AppliesColourProfiles);
                Run("pictures: Windows' JPEG reader is close to Chromium's on the sample photos", JpegsLikeTheBrowser);
                Run("pictures: a GIF opens at its first picture, on its own screen, with its frame count", GifsOpenAtTheirFirstPicture);
                Run("pictures: pasted PNGs and bitmaps, dropped files; folders, text and broken files refused", OpensCopiedAndDropped);

                Run("data folder: next to the exe, or where --data says; nothing kept in Temp or AppData", DataFolderPlaces);
                Run("data folder: --data into a folder of other things touches only b0r3d DeRes's own files", OtherFilesUntouched);
                Run("settings: saved and read back; garbled lines fall back field by field", SettingsRoundTrip);
                Run("settings: a folder that can't be written to is caught at start and said", SettingsWhereTheyCantBeKept);
                Run("settings: a save that fails once (a held file) doesn't stop the next", SaveFailsOnlyOnce);
                Run("command line: --data, --selftest, a picture, and unknown options", CommandLines);

                Run("look: the three colour sets match, and every colour used is in them", ThemesMatch);
                Run("window: builds in light and dark with no binding errors", WindowWithoutBindingErrors);
                Run("window: starts with the Rukus sample on the Game Boy, its lines in the website's words", WindowStartsWithTheSample);
                Run("preview: every machine pixel is exactly k × k screen pixels, at 100 % and 150 %", PreviewIsSharp);
                Run("preview: zoom in whole steps, move around, Fit, 1×, and a slider never moves it", ZoomAndMove);
                Run("preview: Compare's divider, and holding for the original", CompareAndHold);
                Run("window: every control makes the same picture as the engine, with the website's values", ControlsMakeTheSamePicture);
                Run("save: the PNG is the preview's pixels at the size chosen, the machine's palette, nothing else", SaveReadsBack);
                Run("copy and drag: a PNG, a plain picture while not too big, never the cloud clipboard, a named file", CopyAndDragData);
                Run("window: the settings and the look are kept, the picture never is", WindowKeepsSettings);
                Run("window: full screen shows just the picture, and Esc brings the window back", FullScreenAndBack);
                Run("window: Keep shows a checkerboard behind the see-through pixels, and the PNG keeps them", KeepShowsACheckerboard);
                Run("window: each machine offers its own dithers, greys out the rest, and says why", DithersFollowTheMachine);
                Run("window: the Colours slider picks fewer colours, and starts at each machine's own", ColoursSlider);
                Run("window: .scr, .koa and .chr saved as the machine makes them, see-through ones on black", MachineFiles);
                Run("window: palettes loaded, grabbed and saved in every kind", PalettesInAndOut);
                Run("window: looks saved, pasted as a link, dropped as a file, and what comes later said", LooksInAndOut);
                Run("window: a machine clicked again starts over, a look keeps its width, a .txt palette loads", SmallBehaviours);
                Run("window: Surprise me rolls as the website does, and Reset settings asks first", SurpriseAndReset);
                Run("keyboard: no two things share an access key, and every menu line has one", AccessKeysAreUnique);
                Run("look: the text boxes' right-click menu is b0r3d DeRes's own, so it's themed", TextMenusAreThemed);
                Run("about: the version, the build, the settings folder and every licence", AboutShowsItAll);

                Run("footprint: nothing written to AppData, Temp or the registry", () => FootprintUnchanged(footprint));

                Console.WriteLine();
                Console.WriteLine(passed + " passed, " + failed + " failed, " + skipped + " skipped");
                TryDelete(Work);
                return failed == 0 ? 0 : 1;
            }
        }

        static void Run(string name, Action test)
        {
            running = name;
            var watch = Stopwatch.StartNew();
            try
            {
                test();
                passed++;
                Console.WriteLine("PASS  " + name + Slow(watch));
            }
            catch (SkipException e)
            {
                skipped++;
                Console.WriteLine("SKIP  " + name + ": " + e.Message);
            }
            catch (CheckException e)
            {
                failed++;
                Console.WriteLine("FAIL  " + name + ": " + e.Message);
            }
            catch (Exception e)
            {
                failed++;
                Console.WriteLine("FAIL  " + name + ": " + e);
            }
        }

        // a test's time, when it took long enough to matter
        static string Slow(Stopwatch watch) => watch.ElapsedMilliseconds >= 1000 ? " (" + (watch.ElapsedMilliseconds / 1000.0).ToString("0.0") + " s)" : "";

        static void Check(bool ok, string why)
        {
            if (!ok) throw new CheckException(why);
        }

        // lets the window's timers and queued work run, until `done` or `ms` milliseconds pass
        static void Pump(Func<bool> done, int ms)
        {
            var watch = Stopwatch.StartNew();
            while (!done() && watch.ElapsedMilliseconds < ms)
            {
                var frame = new DispatcherFrame();
                Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
                Dispatcher.PushFrame(frame);
                Thread.Sleep(5);
            }
        }

        static void Fresh(string folder)
        {
            TryDelete(folder);
            Directory.CreateDirectory(folder);
        }

        static void TryDelete(string folder)
        {
            try
            {
                if (Directory.Exists(folder)) Directory.Delete(folder, true);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                // still in use; the next run clears it
            }
        }

        static string Short(string text) => text == null ? "(nothing)" : text.Length <= 40 ? text : text.Substring(0, 37) + "...";

        // Anything named after b0r3d (b0r3dDeRes, b0r3d DeRes, b0r3d.org: .NET would name settings
        // after the company) two levels into AppData and Temp, and in the registry's Software keys:
        // the program must add none of it (its folder is the only place it writes). What Windows
        // itself records (the Save dialog's last folder) isn't named after us, and the tests never
        // open the dialog; FileDialogsStayOutOfRecent checks its Recent-items option instead.
        static List<string> Footprint()
        {
            var seen = new List<string>();
            foreach (string folder in new[] { Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Path.GetTempPath() })
                FindOurs(folder, 2, seen);
            foreach (string key in new[] { "Software", @"Software\Classes" })
                using (RegistryKey software = Registry.CurrentUser.OpenSubKey(key))
                    if (software != null) seen.AddRange(software.GetSubKeyNames().Where(Ours).Select(k => @"HKCU\" + key + @"\" + k));
            seen.Sort(StringComparer.OrdinalIgnoreCase);
            return seen.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        static void FindOurs(string folder, int depth, List<string> seen)
        {
            try
            {
                foreach (string entry in Directory.EnumerateFileSystemEntries(folder))
                {
                    if (Ours(Path.GetFileName(entry))) seen.Add(entry);
                    else if (depth > 1 && Directory.Exists(entry) && (File.GetAttributes(entry) & FileAttributes.ReparsePoint) == 0) FindOurs(entry, depth - 1, seen);
                }
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is System.Security.SecurityException)
            {
            }
        }

        static bool Ours(string name) => name.IndexOf("b0r3d", StringComparison.OrdinalIgnoreCase) >= 0;

        static void FootprintUnchanged(List<string> before)
        {
            List<string> added = Footprint().Except(before, StringComparer.OrdinalIgnoreCase).ToList();
            Check(added.Count == 0, "new: " + string.Join(", ", added));
        }

    }
}
