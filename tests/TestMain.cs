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
    //   b0r3dDeResTests.exe                        run the tests
    //   b0r3dDeResTests.exe --part engine|animation|gifs|frames|signal|video|videosave|videobatch|ascii|window   run one part of them
    //                                              (build.cmd test runs the ten in turn, each
    //                                              within its 90 seconds on a 2-core CI machine too)
    //   b0r3dDeResTests.exe --shots <folder> light|dark|ascii   draw the windows to PNG files (see Shots.cs), a set at a time
    static partial class TestMain
    {
        static int passed, failed, skipped;
        static volatile string running = "starting";   // the test under way, for the watchdog
        static string only, part;                       // the part asked for (null: all), and the one the tests below are in

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
            // the watchdog, 90 seconds (by hand, DERES_WATCHDOG can make it shorter, for a soak run):
            // what's stuck, and any of Windows' video calls that haven't come back
            int watchdog = int.TryParse(Environment.GetEnvironmentVariable("DERES_WATCHDOG"), out int limit) && limit > 0 && limit < 90 ? limit : 90;
            void Stuck(object _)
            {
                string video = B0r3dDeRes.Video.VideoTrace.Now();
                Console.WriteLine("FAIL  still running after " + watchdog + " seconds, so stopped (watchdog), during: " + running + (video.Length > 0 ? "; Windows' video calls not back: " + video : ""));
                Environment.Exit(4);
            }
            using (new Timer(Stuck, null, watchdog * 1000, Timeout.Infinite))
            {
                // the program's resources and styles, as when it runs; a window that throws is a FAIL line
                var app = new App();
                app.InitializeComponent();
                // (the program ends when its main window closes; the tests close many and go on)
                app.ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown;
                App.OnProblem = e => { Console.WriteLine("FAIL  a window threw: " + e); Environment.Exit(3); };
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
                // (by hand, DERES_POOL=2: no more shared threads at the ready than a 2-core machine
                // has, as on GitHub's; .NET Framework counts every core even with the affinity set)
                if (int.TryParse(Environment.GetEnvironmentVariable("DERES_POOL"), out int pool) && pool > 0) ThreadPool.SetMinThreads(pool, pool);
                Fresh(Work);
                DataFolder.Reset(Path.Combine(Work, "data"));

                if (args.Length == 3 && args[0] == "--shots" && new[] { "light", "dark", "ascii" }.Contains(args[2]))
                {
                    int shot = TakeShots(args[1], args[2]);
                    TryDelete(Work);
                    return shot;
                }
                if (args.Length == 2 && args[0] == "--part" && new[] { "engine", "animation", "gifs", "frames", "signal", "video", "videosave", "videobatch", "ascii", "window" }.Contains(args[1])) only = args[1];
                else if (args.Length > 0)
                {
                    Console.WriteLine("b0r3dDeResTests.exe [--part engine|animation|gifs|frames|signal|video|videosave|videobatch|ascii|window] [--shots <folder> light|dark|ascii]");
                    return 2;
                }

                List<string> footprint = Footprint();

                part = "engine";
                Run("version: the exe carries the version and the build", ExeVersion);
                Run("one file: the exe runs on its own, 64-bit, per-monitor DPI on, samples inside, and its windows load", ExeRunsOnItsOwn);
                Run("never goes online: no networking, web, XML, media, native or COM way out in the exe", ExeNeverGoesOnline);
                Run("safety flags: a 64-bit exe with Windows' address randomisation and DEP", ExeSafetyFlags);
                Run("file dialogs: Save and Open are told not to add to Recent items", FileDialogsStayOutOfRecent);
                Run("icon: the cup at every size, small ones as bitmaps, all of them pictures", ExeIcon);
                Run("icon check: tells static from a picture", StaticIsCaught);
                Run("build.cmd: plain ASCII with CRLF line endings", BuildCmdIsPlain);

                Run("maths: cube roots and powers as the website's engine works them out in Node", MathsLikeTheBrowser);
                Run("maths: the colour tables, the beam and the deep fry's cosines are the website's, bit for bit", TablesLikeTheWebsite);
                Run("maths: sin, cos and exp as the website's engine works them out in Node, number for number", TrigLikeTheBrowser);
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
                Run("PNG: " + Website.Exports.Length + " exports with the website's header, palette and pixel rows, screen effects too", PngsLikeTheWebsite);
                Run("screens: " + Website.Screens.Length + " screen effects at many sizes come out as the website's, pixel for pixel", ScreensLikeTheWebsite);
                Run("signal: " + Website.Signals.Length + " frames through ntsc-rs come out as the website's, byte for byte: every machine's clock and timing, every connection, the wear, colour, sharpening, decoder, frames, interlaced and see-through pictures", SignalsLikeTheWebsite);
                Run("signal: " + (Website.SignalExports.Length + Website.SignalWallpapers.Length) + " PNGs and wallpapers through it as the website's, with the screen effect too, named for the connection", SignalPngsLikeTheWebsite);
                Run("signal: the settings files the website hands the engine, number for number (" + Website.EngineFiles.Length + ")", EngineSettingsLikeTheWebsite);
                Run("signal: CGA composite's colours from the signal, as the website's", ArtifactColoursLikeTheWebsite);
                Run("wallpapers: " + Website.Wallpapers.Length + " wallpapers, with and without the screen effect, as the website's", WallpapersLikeTheWebsite);
                Run("PNG: sizes, raw and as on a TV, for every machine and Size", SizesLikeTheWebsite);
                Run("file names: the website's names, photo-nes-256x240-4x.png", NamesLikeTheWebsite);
                Run("engine: the same pictures on one core as on all of them", SameOnEveryCore);
                Run("big photo: 48 MP opens whole and makes its picture in good time", BigPhoto);
                Run("big exports: 16× web-safe and a long receipt strip save in good time and read back", BigExports);

                Run("pictures: PNG, JPEG, BMP, GIF and TIFF open; a broken, missing or PDF file says why", ReadsPictureFiles);
                Run("pictures: all eight EXIF orientations come out upright", TurnsPhotosUpright);
                Run("pictures: other colour profiles are applied, sRGB ones leave the pixels as browsers do", AppliesColourProfiles);
                Run("pictures: Windows' JPEG reader is close to Chromium's on the sample photos", JpegsLikeTheBrowser);
                Run("pictures: an animated GIF opens at its first frame through DeRes's own decoder, on its own screen, with its frames; a one-frame GIF as a still", GifsOpenAtTheirFirstPicture);
                Run("pictures: pasted PNGs and bitmaps, a dropped picture opens, several or a folder go to the batch, broken files said", OpensCopiedAndDropped);

                Run("data folder: next to the exe, or where --data says; nothing kept in Temp or AppData", DataFolderPlaces);
                Run("data folder: --data into a folder of other things touches only b0r3d DeRes's own files", OtherFilesUntouched);
                Run("settings: saved and read back; garbled lines fall back field by field", SettingsRoundTrip);
                Run("settings: a folder that can't be written to is caught at start and said", SettingsWhereTheyCantBeKept);
                Run("settings: a save that fails once (a held file) doesn't stop the next", SaveFailsOnlyOnce);
                Run("command line: --data, --selftest, a picture, and unknown options", CommandLines);

                part = "animation";
                Run("GIFs: " + Website.GifDecodes.Length + " GIFs read frame by frame as the website's decoder reads them: disposals, interlacing, tables, clipping, LZW, delays, loops, damage", GifsReadLikeTheWebsite);
                Run("GIFs: opened as animations as the website opens them, shrunk and cut to a budget, stills left as stills", GifsOpenLikeTheWebsite);
                Run("GIF and APNG: the website's writers, byte for byte (GIF) and chunk for chunk (APNG), the shared palette and the see-through entry", GifAndApngWritersLikeTheWebsite);
                Run("loops: " + Website.Loops.Length + " Screen FX loops, A/B wipes and dither shimmers as the website's, with the screen effect, the signal and see-through pixels", LoopsLikeTheWebsite);
                Run("animations: the size they come out at, and the GIF's words, as the website's", AnimationSizesAndWordsLikeTheWebsite);

                part = "gifs";
                Run("GIF frames: every other one of " + Website.FramesExports.Length + " animated GIFs' own frames through a machine as the website's, made on two lanes; a frame's PNG", () => GifFramesLikeTheWebsite(0));
                Run("window: a GIF plays through the settings (and the signal), pauses on a frame that's then the picture, the slider goes to any frame, a frame's PNG says which", GifPlaysInTheWindow);
                Run("window: GIF and APNG of every loop and of a GIF's own frames, the engine's files, named as the website names them; the signal's loop, Stop, the line by the buttons, the style kept", AnimationsSaveInTheWindow);
                Run("batch: a GIF or an APNG each, an animated GIF as its own frames, a still as the loop picked, each made straight into its file and read back", BatchMakesGifs);

                part = "frames";
                Run("GIF frames: the other half of them, made on two lanes", () => GifFramesLikeTheWebsite(1));
                Run("GIFs: shrunk to be held, they go by their own size: Custom's height and widest, Detail and the resolution line as the website's", ShrunkGifsGoByTheirOwnSize);
                Run("window: GIFs shrunk to be held: Custom's height and widest and the resolution line by the GIF's own size", ShrunkGifInTheWindow);
                Run("batch: GIFs skipped (not made) or replaced, Stop while one's made leaves no file and counts nothing, a file that came to the name meanwhile left as it was", BatchGifsSkipReplaceStop);
                if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DERES_MEASURE"))) Run("measure: an animation at the PC's limits, its time and memory", MeasureAnimations);

                part = "signal";
                Run("signal: " + Website.EngineCases.Length + " frames with the engine's settings changed, every field mode and dubbed tapes, as the website's own engine makes them from the same settings, byte for byte", EveryEngineSettingLikeTheWebsite);
                Run("signal: ntsc-rs settings files read as the engine reads them (the defaults letter for letter), and refused where it refuses them", EngineFilesLikeTheWebsite);
                Run("window: Dub it, the presets (built in, and your own kept, replaced and deleted; a broken presets file left alone, a later version's entries kept), every one of the engine's settings in its window, ntsc-rs settings files in and out; links, look files and the settings file keep them", TapeExtrasInTheWindow);

                part = "video";
                Run("video: MP4, MKV, MPEG-2, WMV and AVI clips open upright at their own speed and length, every frame in order, the flash on its frame and the beep at 1 s, mono 44.1 kHz made 48 kHz stereo", VideosOpen);
                Run("video: the frame at any time, forwards and back, and a variable frame rate's frames each at its own time", VideoFramesByTime);
                Run("video: HLG HDR (an iPhone's) in normal colour, close to the picture it was made from; the whole clip where Windows can decode HEVC", HdrVideosInNormalColour);
                if (Environment.GetEnvironmentVariable("DERES_MEASURE") == "hlg") Run("measure: an HLG frame of 4K into normal colour", MeasureHlg);
                Run("video: MP4s written (H.264 and AAC) read back with every frame, upright, at their speed (29.97 too), the sound in step with the picture", Mp4sWriteAndReadBack);
                Run("window: a video opens with its timeline; a frame on and back and the slider make that frame the picture; start and stop; saved as MP4 at a Size and frame rate; it plays and pauses", VideoInTheWindow);
                Run("window: a video plays with its sound to its end when the sound stops first, the sound where its times put it, and on from where the slider's moved while it plays; the clock's words, GIF and APNG frame rates", VideoPlaysWithItsSound);

                part = "videosave";
                Run("video: a clip through the NES on RF saved as MP4 with its sound (its own speed and 15 a second, part of it without sound), GIF, APNG and PNG frames; Stop leaves no file", VideosSaveThroughTheLook);

                part = "videobatch";
                Run("batch: videos as MP4s through the look with their sound (none for one without), at the biggest Size an MP4 holds, a GIF and an APNG of a video's frames at 10 a second, a picture beside them, every file read back", VideoBatches);

                part = "ascii";
                Run("ASCII: " + WebsiteAscii.Cases.Length + " pictures' art as the website makes it, every mode, ramp, colour profile, column count, adjustment and Teletext option: the cells, colours and counts, and the text, Markdown, ANSI escapes, HTML, .ans, .seq, .prg and .txt byte for byte", AsciiLikeTheWebsite);
                Run("ASCII: the ramps, mosaics, palettes and C64 screen codes are the website's, and kept settings are checked as it checks them", AsciiTablesLikeTheWebsite);
                Run("ASCII: half-block pictures pixel for pixel, Teletext's as Chromium's canvas draws them, characters as the font's own letters, PNGs and names", AsciiPicturesLikeTheWebsite);
                Run("ASCII: the VGA font's " + WebsiteAscii.FontPoints.Split(',').Length + " letters as Chromium draws them, from the website's own font file; the quarter blocks it lacks", VgaFontLikeChromium);
                Run("ASCII: see-through pixels' colours as the website's canvas gives them back (65,536 of them), and the Rukus sample's art from its own file", CanvasLevelsLikeChromium);
                Run("ASCII: the GIF sample's every frame as art, its ANSImation byte for byte, GIFs byte for byte and APNGs by their parts", AsciiAnimationsLikeTheWebsite);
                Run("ASCII: SAUCE records, their fields and the title cut to 35 bytes", SauceRecords);
                Run("window: the ASCII studio makes the website's art of the Rukus sample in every mode, every control makes the engine's art, the menus follow the tab, and Reset settings", AsciiStudioInTheWindow);
                Run("window: the studio's .ans, PNGs at 1×, 2× and 4×, .seq, .prg and Teletext .txt are the engine's files", AsciiSavesInTheWindow);
                Run("window: the GIF sample plays as art and pauses on the website's art of the frame, the other tab opens there, and its every frame goes out as the website's GIF, APNG and ANSImation; Stop leaves no file", AsciiGifInTheWindow);
                Run("window: a video's art goes out as an MP4 with its sound in step, a GIF and an ANSImation, at its own speed or slower", AsciiVideoInTheWindow);
                Run("window: the dial-up modem types the art at its baud rate, hangs up and dials on, types new art from the start, and never while a GIF plays", AsciiModemInTheWindow);
                Run("settings: the studio's settings are kept (an odd typed-in ramp too), odd ones fall back, and the window starts with them", AsciiSettingsKept);

                part = "window";
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
                Run("window: looks saved, pasted as a link, dropped as a file, the screen and the signal in them", LooksInAndOut);
                Run("window: a machine clicked again starts over, a look keeps its width, a .txt palette loads", SmallBehaviours);
                Run("window: Surprise me rolls as the website does (the screen too), and Reset settings asks first", SurpriseAndReset);
                Run("screen: each screen's controls, and the preview's screen effect is the engine's, made at the zoom shown, pixel for pixel at 100 % and 150 %", ScreenSectionAndPreview);
                Run("screen: PNGs and copies with the screen effect from 3× up, named -fx, and the wallpapers, the PC's screens too", PngsAndWallpapersWithTheScreen);
                Run("analog looks: their words, no dithers, their own colours, in the window", AnalogLooksInTheWindow);
                Run("signal: its controls and words, the preview through it (and a screen over it) the engine's at the zoom shown, CGA composite's colours from it", SignalSectionAndPreview);
                Run("signal: PNGs, copies, wallpapers and batches through it, full colour, named -rf", SignalInPngsAndWallpapers);
                Run("batch: a folder through a look, every file read back, the broken one said, files numbered (a picture's together), skipped or replaced, .scr on black for kept parts, and closing waits for it", BatchMakesFiles);
                Run("DeRes the screen: the screens in real pixels, a box, a screen or all of them as the picture", DeresTheScreen);
                Run("keyboard: no two things share an access key, and every menu line has one", AccessKeysAreUnique);
                Run("look: the text boxes' right-click menu is b0r3d DeRes's own, so it's themed", TextMenusAreThemed);
                Run("about: the version, the build, the settings folder and every licence", AboutShowsItAll);

                part = only;   // (every part ends with it)
                Run("footprint: nothing written to AppData, Temp or the registry", () => FootprintUnchanged(footprint));

                Console.WriteLine();
                Console.WriteLine(passed + " passed, " + failed + " failed, " + skipped + " skipped");
                TryDelete(Work);
                return failed == 0 ? 0 : 1;
            }
        }

        // with DERES_ONLY set (by hand, never by build.cmd), only the tests whose names hold its text
        static readonly string onlyNamed = Environment.GetEnvironmentVariable("DERES_ONLY");

        static void Run(string name, Action test)
        {
            if (only != null && part != only) return;
            if (!string.IsNullOrEmpty(onlyNamed) && name.IndexOf(onlyNamed, StringComparison.OrdinalIgnoreCase) < 0 && !name.StartsWith("footprint", StringComparison.Ordinal)) return;
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
