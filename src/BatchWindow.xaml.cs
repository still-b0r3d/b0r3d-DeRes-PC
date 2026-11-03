using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using B0r3dDeRes.Engine;

namespace B0r3dDeRes
{
    // what to do with a file that's already in the folder
    enum Clash { Number, Replace, Skip }

    // The batch: a folder or a pile of pictures through the look the main window has now, into a
    // folder: a PNG or a wallpaper each (and the machine's own file, when asked), several pictures
    // at once on every core. Every file is written whole, then read back from the disk.
    public partial class BatchWindow : Window
    {
        readonly MainWindow owner;
        readonly List<string> files = new List<string>();
        readonly HashSet<string> known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string into;            // the folder the files go into
        bool intoChosen;        // picked by hand (else it follows the first picture's folder)
        bool busy, stopping, closeWhenDone, closeOwnerWhenDone;
        string madeIn;

        internal Task Running { get; private set; } = Task.CompletedTask;
        internal bool Busy => busy;
        internal IReadOnlyList<string> Files => files;
        internal string Into => into;
        // for the tests: "12 made, 0 skipped, 0 failed"
        internal string LastOutcome { get; private set; } = "";
        // for the tests: how many pictures are made at once (null: by the cores)
        internal int? AtOnce;

        internal BatchWindow(MainWindow owner)
        {
            InitializeComponent();
            this.owner = owner;
            SourceInitialized += (s, e) => Theme.TitleBar(this);
            foreach (Wallpaper w in owner.WallpaperChoices) WallpaperBox.Items.Add(new ComboBoxItem { Content = w.Name + " (" + w.Width + "×" + w.Height + ")", Tag = w });
            WallpaperBox.SelectedIndex = 0;
            Closing += OnClosing;
            // the look can change in the main window meanwhile
            Activated += (s, e) => Refresh();
            Refresh();
        }

        // ---------- the pictures ----------

        // Pictures and folders to DeRes: a folder gives its own pictures (not those in the folders
        // inside it, nor online-only ones), in name order. A picture already in the list isn't
        // added again. False while a batch runs (nothing is added then).
        internal bool Add(IEnumerable<string> paths)
        {
            if (busy) return false;
            int before = files.Count, online = 0;
            var notes = new List<string>();
            foreach (string path in paths)
            {
                List<string> found;
                try
                {
                    if (Directory.Exists(path))
                    {
                        found = new List<string>();
                        foreach (string f in Directory.EnumerateFiles(path).Where(Pictures.LooksLikePicture).OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase))
                        {
                            if (Pictures.OnlyOnline(f)) online++;
                            else found.Add(f);
                        }
                    }
                    else found = File.Exists(path) && Pictures.LooksLikePicture(path) ? new List<string> { path } : new List<string>();
                }
                catch (Exception x) when (x is IOException || x is UnauthorizedAccessException || x is ArgumentException || x is NotSupportedException || x is System.Security.SecurityException)
                {
                    notes.Add("Couldn't look in " + Pictures.NameOf(path) + ": " + x.Message.TrimEnd('.') + ".");
                    continue;
                }
                foreach (string file in found)
                {
                    string full = Path.GetFullPath(file);
                    if (known.Add(full)) files.Add(full);
                }
            }
            if (!intoChosen && files.Count > 0) into = Path.Combine(Path.GetDirectoryName(files[0]), "DeRes");
            ClearOutcome();
            if (online > 0)
                notes.Add("Left out " + Count(online, "picture") + " kept only online (OneDrive and the like): in Explorer, Always keep on this device brings " + (online == 1 ? "it" : "them") + " in.");
            if (files.Count == before && notes.Count == 0) notes.Add("No new pictures in that.");
            StatusLine.Text = string.Join(" ", notes);
            Refresh();
            return true;
        }

        void OnChooseFolder(object sender, RoutedEventArgs e)
        {
            string folder = Ask(() => FileDialogs.PickFolder(this, "A folder of pictures to DeRes"), "folder");
            if (folder != null) Add(new[] { folder });
        }

        void OnChoosePictures(object sender, RoutedEventArgs e)
        {
            string[] chosen = Ask(() => FileDialogs.OpenPictures(this), "Open");
            if (chosen != null) Add(chosen);
        }

        void OnClear(object sender, RoutedEventArgs e)
        {
            if (busy) return;
            files.Clear();
            known.Clear();
            if (!intoChosen) into = null;
            ClearOutcome();
            Refresh();
        }

        void OnChooseInto(object sender, RoutedEventArgs e)
        {
            string folder = Ask(() => FileDialogs.PickFolder(this, "The folder for the files"), "folder");
            if (folder == null) return;
            SetInto(folder);
        }

        internal void SetInto(string folder)
        {
            into = folder;
            intoChosen = true;
            Refresh();
        }

        // a Windows dialog's answer, or null (cancelled, or it didn't open: said in a box)
        T Ask<T>(Func<T> dialog, string which) where T : class
        {
            try
            {
                return dialog();
            }
            catch (COMException x)
            {
                MessageBox.Show(this, "Windows' " + which + " dialog didn't open:\n\n" + x.Message, Program.Name, MessageBoxButton.OK, MessageBoxImage.Warning);
                return null;
            }
        }

        void OnDragOver(object sender, DragEventArgs e)
        {
            e.Effects = !busy && e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        void OnDrop(object sender, DragEventArgs e)
        {
            e.Handled = true;
            if (busy || !(e.Data.GetData(DataFormats.FileDrop) is string[] dropped)) return;
            Add(dropped);
        }

        void OnMakeChoice(object sender, RoutedEventArgs e) => Refresh();

        void OnWallpaperChoice(object sender, SelectionChangedEventArgs e) => Refresh();

        // ---------- what the window says ----------

        Wallpaper ChosenWallpaper => WallpaperChoice.IsChecked == true ? (WallpaperBox.SelectedItem as ComboBoxItem)?.Tag as Wallpaper : null;

        void Refresh()
        {
            // (the buttons' first Checked comes while the window is still being built)
            if (owner == null || MakeButton == null) return;
            Settings s = owner.Current;
            Profile p = Profiles.ById(s.Deres.ProfileId) ?? Profiles.Default;
            var look = new List<string> { p.Name };
            if (Profiles.DitherApplies(p, s.Deres.Dither)) look.Add(Dither.ById(s.Deres.Dither)?.Name + " dither");
            LookLine.Text = "Each picture goes through the look the main window has now: " + string.Join(", ", look) + ".";

            PicturesLine.Text = files.Count == 0 ? "No pictures yet." :
                Count(files.Count, "picture") + (FromOneFolder(out string folder) ? " from " + folder : "");
            IntoLine.Text = into ?? "(a folder called DeRes beside the pictures)";
            IntoLine.ToolTip = into;

            WallpaperBox.Visibility = WallpaperChoice.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            // (a custom width's pixels are square)
            bool tv = s.TvPixels && !s.Deres.Custom && !p.SquarePixels;
            FxSettings fx = s.ExportScreen;
            string screen = MainWindow.ScreenName(s.Fx.Screen) + " screen";
            Wallpaper w = ChosenWallpaper;
            bool withFx;
            if (w == null)
            {
                withFx = Export.WithFx(fx, s.Multiplier);
                MakeLine.Text = "At " + s.Multiplier + "×" + (tv ? ", as on a TV" : "") + (withFx ? ", with the " + screen : "") +
                    ": the Size, Pixel shape and screen effect set under Export.";
            }
            else
            {
                withFx = fx != null;
                MakeLine.Text = "Each enlarged by the biggest whole step that fits " + w.Width + "×" + w.Height + (tv ? ", as on a TV" : "") +
                    (withFx ? ", with the " + screen + " from 3× up" : "") + ", on its first colour.";
            }
            if (s.Fx.Screen != Screens.Off && !withFx)
                MakeLine.Text += " The " + screen + " isn't in the files: that takes With the screen effect, under Export, at 3× and up.";
            string format = p.EmulatorFormats.FirstOrDefault();
            MachineFileBox.Visibility = format != null ? Visibility.Visible : Visibility.Collapsed;
            if (format != null) MachineFileBox.Content = "Also the machine's own ." + format + " file (" + Words.EmulatorType(format) + ")";
            ShowButtons();
        }

        // the pictures all come from one folder (named), or not
        bool FromOneFolder(out string folder)
        {
            folder = files.Count > 0 ? Path.GetDirectoryName(files[0]) : null;
            string first = folder;
            return folder != null && files.All(f => string.Equals(Path.GetDirectoryName(f), first, StringComparison.OrdinalIgnoreCase));
        }

        void ShowButtons()
        {
            MakeButton.IsEnabled = !busy && files.Count > 0;
            StopButton.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
            FolderButton.IsEnabled = PicturesButton.IsEnabled = ClearButton.IsEnabled = IntoButton.IsEnabled = !busy;
            PngChoice.IsEnabled = WallpaperChoice.IsEnabled = WallpaperBox.IsEnabled = MachineFileBox.IsEnabled = !busy;
            NumberChoice.IsEnabled = ReplaceChoice.IsEnabled = SkipChoice.IsEnabled = !busy;
        }

        // ---------- making the files ----------

        // what every picture goes through: the look, taken when the batch starts; and how it's going
        sealed class Job
        {
            public Settings Settings;
            public Wallpaper Wallpaper;     // null: a PNG each
            public bool MachineFile;
            public Clash Clash;
            public string Into;
            public readonly HashSet<string> Taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            public int Made, Skipped, Done;
            public readonly List<string> Failed = new List<string>();
            public readonly HashSet<string> FailedPictures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            public readonly ConcurrentQueue<string> TooBig = new ConcurrentQueue<string>();
        }

        // one file a picture makes: its name (before any number) and bytes, and for a PNG its size
        sealed class Output
        {
            public string Name;
            public byte[] Bytes;
            public int Width, Height;
        }

        void OnMake(object sender, RoutedEventArgs e) => Make(NumberChoice.IsChecked == true ? Clash.Number : ReplaceChoice.IsChecked == true ? Clash.Replace : Clash.Skip);

        // starts the batch (the tests wait on Running)
        internal void Make(Clash clash)
        {
            if (busy || files.Count == 0) return;
            ClearOutcome();
            string folder = into ?? Path.Combine(Path.GetDirectoryName(files[0]), "DeRes");
            try
            {
                Directory.CreateDirectory(folder);
            }
            catch (Exception x) when (x is IOException || x is UnauthorizedAccessException || x is ArgumentException || x is NotSupportedException || x is System.Security.SecurityException)
            {
                StatusLine.Text = "Couldn't make the folder " + folder + ": " + x.Message;
                StatusLine.SetResourceReference(TextBlock.ForegroundProperty, "Bad");
                return;
            }
            var job = new Job
            {
                Settings = owner.Current.Copy(),
                Wallpaper = ChosenWallpaper,
                MachineFile = MachineFileBox.Visibility == Visibility.Visible && MachineFileBox.IsChecked == true,
                Clash = clash,
                Into = folder,
            };
            Running = Run(job, files.ToList());
            Running.ContinueWith(t => Dispatcher.BeginInvoke(new Action(() => throw new InvalidOperationException("The batch failed.", t.Exception.GetBaseException()))),
                TaskContinuationOptions.OnlyOnFaulted);
        }

        async Task Run(Job job, List<string> todo)
        {
            busy = true;
            stopping = false;
            ShowButtons();
            Progress.Maximum = todo.Count;
            Progress.Value = 0;
            Progress.Visibility = Visibility.Visible;
            ShowProgress(job, todo.Count, null);
            try
            {
                int atOnce = AtOnce ?? Math.Max(1, Math.Min(4, Environment.ProcessorCount / 3));
                using (var gate = new SemaphoreSlim(atOnce))
                {
                    var running = new List<Task>();
                    foreach (string file in todo)
                    {
                        await gate.WaitAsync();
                        if (stopping)
                        {
                            gate.Release();
                            break;
                        }
                        ShowProgress(job, todo.Count, file);
                        // (One catches whatever a picture throws, so none of these fault)
                        running.Add(Task.Run(() => One(job, file, false)).ContinueWith(t =>
                        {
                            gate.Release();
                            Dispatcher.BeginInvoke(new Action(() => ShowProgress(job, todo.Count, null)));
                        }));
                    }
                    await Task.WhenAll(running);
                }
                // the ones there wasn't memory for beside the others: once more, alone
                while (!stopping && job.TooBig.TryDequeue(out string big))
                {
                    ShowProgress(job, todo.Count, big);
                    await Task.Run(() => One(job, big, true));
                }
            }
            finally
            {
                busy = false;
                Progress.Visibility = Visibility.Collapsed;
                ShowButtons();
            }
            madeIn = job.Into;
            Finish(job, todo.Count);
            if (closeOwnerWhenDone) owner.Close();
            else if (closeWhenDone) Close();
        }

        void ShowProgress(Job job, int total, string now)
        {
            int done;
            lock (job) done = job.Done;
            Progress.Value = done;
            StatusLine.Text = (stopping ? "Stopping: the pictures under way finish first. " : "") + done.ToString("N0") + " of " + total.ToString("N0") + " done" +
                (now != null ? " · " + Pictures.NameOf(now) : "");
            StatusLine.ClearValue(TextBlock.ForegroundProperty);
        }

        // One picture, on a worker thread: opened, made, its files written and read back. Whatever
        // goes wrong is this picture's: listed, never the batch's end. Out of memory beside the
        // others, it waits to be made on its own (unless a file of it is written already).
        void One(Job job, string file, bool alone)
        {
            string name = Pictures.NameOf(file);
            bool wrote = false;
            try
            {
                LoadedPicture picture = Pictures.Load(file);
                var engine = new Deres();
                engine.SetSource(picture.Source);
                DeresSettings s = job.Settings.Deres;
                Rendered r = engine.Render(s);
                string stem = Export.BaseName(picture.Name);
                bool tv = job.Settings.TvPixels && !r.Geometry.SquarePixels;
                FxSettings fx = job.Settings.ExportScreen;
                var outputs = new List<Output>();
                using (var bytes = new MemoryStream())
                {
                    var png = new Output();
                    if (job.Wallpaper == null)
                    {
                        int m = job.Settings.Multiplier;
                        Export.WritePng(bytes, r, m, tv, fx, out png.Width, out png.Height);
                        png.Name = Export.FileName(stem, r.Profile.Id, r.Width, r.Height, m, tv, Export.WithFx(fx, m));
                    }
                    else
                    {
                        Export.WriteWallpaper(bytes, r, job.Wallpaper.Width, job.Wallpaper.Height, tv, fx);
                        png.Width = job.Wallpaper.Width;
                        png.Height = job.Wallpaper.Height;
                        png.Name = Export.WallpaperName(stem, r.Profile.Id, job.Wallpaper.Id);
                    }
                    png.Bytes = bytes.ToArray();
                    outputs.Add(png);
                }
                string missing = null;
                string format = r.Profile.EmulatorFormats.FirstOrDefault();
                if (job.MachineFile && format != null)
                {
                    // A screen file (.scr, .koa) can't hold see-through pixels: with them kept it's
                    // made on black, as Save for an emulator makes it. Tiles keep them as colour 0.
                    Rendered forFile = r;
                    if (format != "chr" && s.Background == Backgrounds.Transparent && picture.Source.HasAlpha)
                    {
                        DeresSettings onBlack = s.Clone();
                        onBlack.Background = Backgrounds.Black;
                        forFile = engine.Render(onBlack);
                    }
                    if (forFile.EmulatorFile != null) outputs.Add(new Output { Name = Export.EmulatorFileName(stem, r.Profile.Id, forFile.EmulatorFile.Format), Bytes = forFile.EmulatorFile.Data });
                    else missing = "its ." + format + " file didn't come out";
                }
                List<string> names = Reserve(job, outputs);
                for (int i = 0; i < outputs.Count; i++)
                {
                    if (names[i] == null) continue;
                    wrote = true;
                    string problem = WriteAndCheck(Path.Combine(job.Into, names[i]), outputs[i]);
                    if (problem != null) Fail(job, file, name + ": " + names[i] + " " + problem);
                    else lock (job) job.Made++;
                }
                if (missing != null) Fail(job, file, name + ": " + missing);
            }
            catch (Exception x)
            {
                Exception why = x is AggregateException all ? all.Flatten().InnerExceptions[0] : x;
                bool memory = why is OutOfMemoryException || why is PictureException p && p.OutOfMemory;
                if (memory && !alone && !wrote)
                {
                    job.TooBig.Enqueue(file);
                    return;
                }
                Fail(job, file, name + ": " + (memory ? "there isn't enough memory free for it" : why is PictureException ? why.Message : why.Message.TrimEnd('.')));
            }
            lock (job) job.Done++;
        }

        // Names for one picture's files, numbered together ("photo-…-4x (2).png" beside
        // "photo-… (2).scr"): a file this batch made is never written over, files already in the
        // folder are numbered past, replaced or skipped (null) as asked.
        static List<string> Reserve(Job job, List<Output> outputs)
        {
            lock (job)
            {
                bool Ours(string n) => job.Taken.Contains(n);
                bool There(string n) => File.Exists(Path.Combine(job.Into, n));
                int k = 1;
                while (outputs.Any(o => Ours(Numbered(o.Name, k)) || job.Clash == Clash.Number && There(Numbered(o.Name, k)))) k++;
                var names = new List<string>();
                foreach (Output o in outputs)
                {
                    string chosen = Numbered(o.Name, k);
                    if (job.Clash == Clash.Skip && There(chosen))
                    {
                        job.Skipped++;
                        names.Add(null);
                        continue;
                    }
                    job.Taken.Add(chosen);
                    names.Add(chosen);
                }
                return names;
            }
        }

        // "photo-nes-256x240-4x (2).png" (1: the name itself)
        static string Numbered(string fileName, int n) =>
            n == 1 ? fileName : Path.GetFileNameWithoutExtension(fileName) + " (" + n + ")" + Path.GetExtension(fileName);

        // A file written whole, then read back from the disk: the same bytes, and a PNG that
        // Windows reads as its size (its header: the pixels are the bytes checked). Why not, or null.
        static string WriteAndCheck(string path, Output output)
        {
            Exports.Write(path, output.Bytes);
            byte[] back = File.ReadAllBytes(path);
            if (back.Length != output.Bytes.Length || !back.SequenceEqual(output.Bytes)) return "came back from the disk different";
            if (output.Width == 0) return null;
            BitmapFrame frame = BitmapDecoder.Create(new MemoryStream(back, false), BitmapCreateOptions.PreservePixelFormat | BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.None).Frames[0];
            return frame.PixelWidth == output.Width && frame.PixelHeight == output.Height ? null : "didn't read back as a " + output.Width + "×" + output.Height + " PNG";
        }

        static void Fail(Job job, string picture, string why)
        {
            lock (job)
            {
                job.Failed.Add(why);
                job.FailedPictures.Add(picture);
            }
        }

        void OnStop(object sender, RoutedEventArgs e)
        {
            stopping = true;
            StatusLine.Text = "Stopping: the pictures under way finish first.";
        }

        // the main window is closing: the pictures under way finish, then it closes
        internal void StopThenCloseOwner()
        {
            stopping = closeOwnerWhenDone = true;
            StatusLine.Text = "Stopping: the pictures under way finish first, then b0r3d DeRes closes.";
        }

        void Finish(Job job, int total)
        {
            int done = job.Done, failed = job.FailedPictures.Count;
            var said = new StringBuilder();
            if (stopping) said.Append("Stopped after " + done.ToString("N0") + " of " + total.ToString("N0") + " pictures. ");
            said.Append(job.Made == 0 ? "No files were made" : "Made " + Count(job.Made, "file") + " in " + job.Into);
            said.Append(job.Made > 0 ? ", each read back from the disk." : ".");
            if (job.Skipped > 0) said.Append(" " + Count(job.Skipped, "file") + (job.Skipped == 1 ? " was" : " were") + " already there and left as " + (job.Skipped == 1 ? "it was." : "they were."));
            if (failed > 0) said.Append(" " + Count(failed, "picture") + " didn't come out right:");
            StatusLine.Text = said.ToString();
            StatusLine.SetResourceReference(TextBlock.ForegroundProperty, job.Failed.Count > 0 || job.Made == 0 ? "Warn" : "Good");
            OutcomeList.Items.Clear();
            foreach (string line in job.Failed.Take(30)) OutcomeList.Items.Add(new TextBlock { Text = line, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
            if (job.Failed.Count > 30) OutcomeList.Items.Add(new TextBlock { Text = "… and " + (job.Failed.Count - 30) + " more." });
            ShowFilesButton.Visibility = job.Made > 0 ? Visibility.Visible : Visibility.Collapsed;
            LastOutcome = job.Made + " made, " + job.Skipped + " skipped, " + failed + " failed";
        }

        static string Count(int n, string what) => n.ToString("N0") + " " + what + (n == 1 ? "" : "s");

        void ClearOutcome()
        {
            StatusLine.Text = "";
            StatusLine.ClearValue(TextBlock.ForegroundProperty);
            OutcomeList.Items.Clear();
            ShowFilesButton.Visibility = Visibility.Collapsed;
            LastOutcome = "";
        }

        // Explorer at the files
        void OnShowFiles(object sender, RoutedEventArgs e)
        {
            if (madeIn == null) return;
            try
            {
                Process.Start(new ProcessStartInfo("explorer.exe", "\"" + madeIn + "\"") { UseShellExecute = true });
            }
            catch (Exception x) when (x is Win32Exception || x is InvalidOperationException)
            {
                MessageBox.Show(this, "Couldn't open Explorer:\n\n" + x.Message, Program.Name, MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        void OnClose(object sender, RoutedEventArgs e) => Close();

        // closed while it works: it stops, and closes once the pictures under way are done
        void OnClosing(object sender, CancelEventArgs e)
        {
            if (!busy) return;
            e.Cancel = true;
            stopping = closeWhenDone = true;
            StatusLine.Text = "Stopping: the pictures under way finish first, then this window closes.";
        }
    }
}
