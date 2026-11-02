using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Automation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using B0r3dDeRes.Engine;
using Geometry = B0r3dDeRes.Engine.Geometry;

namespace B0r3dDeRes
{
    // The window: the DeRes tab (the settings on the left, the picture on the right), the menus,
    // the bottom line, and keeping the settings. The picture is opened and made off the window's
    // thread; whatever the settings are when it's done is what's made next, so sliders never wait.
    public partial class MainWindow : Window
    {
        readonly Settings settings;
        readonly bool ready;
        bool quiet;          // the controls are being set from the settings: their events change nothing
        bool Quiet => quiet || !ready;
        bool maximized;      // the window's last state that wasn't minimized, for closing from the taskbar
        readonly DispatcherTimer noteTimer, saveTimer, busyTimer;

        LoadedPicture picture;
        int opening;         // which open is the latest; an older one finishing late is let go
        readonly Deres engine = new Deres();
        Rendered shown;      // the picture on screen
        DeresSettings shownWith;
        LoadedPicture shownFrom;   // the picture it was made from (one being opened may be newer)

        // for the tests: a pretend screen scaling (1.5 = 150 %)
        internal double? ScaleOverride
        {
            get => View.ScaleOverride;
            set => View.ScaleOverride = value;
        }

        // startWith: a picture to open; else the Rukus sample, as the website starts (`sample` false, for
        // the tests: nothing)
        internal MainWindow(Settings settings, string startWith, bool sample = true)
        {
            this.settings = settings;
            InitializeComponent();
            VersionRun.Text = Program.Name + " " + Program.VersionText;
            Width = settings.WindowWidth;
            Height = settings.WindowHeight;
            if (settings.Maximized) WindowState = WindowState.Maximized;
            maximized = settings.Maximized;
            StateChanged += (s, e) => { if (WindowState != WindowState.Minimized && !fullScreen) maximized = WindowState == WindowState.Maximized; };

            noteTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
            noteTimer.Tick += (s, e) => { noteTimer.Stop(); ShowNote(); };
            saveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            saveTimer.Tick += (s, e) => { saveTimer.Stop(); SaveSettings(); };
            busyTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            busyTimer.Tick += (s, e) => { busyTimer.Stop(); ShowReadout(); };

            Theme.Changed += OnTheme;
            SourceInitialized += (s, e) => Theme.TitleBar(this);
            Closing += OnClosing;
            // Windows signing out or shutting down closes no window: keep the settings and clear the drag folder then too
            if (Application.Current != null) Application.Current.SessionEnding += OnSessionEnding;
            Closed += (s, e) =>
            {
                Theme.Changed -= OnTheme;
                if (Application.Current != null) Application.Current.SessionEnding -= OnSessionEnding;
            };
            View.ZoomChanged += ShowZoom;

            Exports.ClearDragFolder();
            BuildLists();
            OnTheme();
            ShowNote();
            ShowCup();
            ready = true;
            Sync();
            ShowStage();
            if (startWith != null) OpenFile(startWith);
            else if (sample) OpenSample("rukus");
        }

        double Scale => ScaleOverride ?? VisualTreeHelper.GetDpi(this).DpiScaleX;

        void ShowCup() => CupPicture.Source = Cup.For(28, Scale);

        protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
        {
            base.OnDpiChanged(oldDpi, newDpi);
            ShowCup();
        }

        Profile CurrentProfile => Profiles.ById(settings.Deres.ProfileId) ?? Profiles.Default;

        // ---------- the lists: machines, dithers, sizes, samples ----------

        void BuildLists()
        {
            foreach (Profile p in Profiles.All)
            {
                byte[] colours = Profiles.PaletteBytes(p);
                BitmapSource strip = BitmapSource.Create(colours.Length / 3, 1, 96, 96, PixelFormats.Rgb24, null, colours, colours.Length);
                strip.Freeze();
                var colourStrip = new Image { Source = strip, Stretch = Stretch.Fill, Height = 6 };
                RenderOptions.SetBitmapScalingMode(colourStrip, BitmapScalingMode.NearestNeighbor);
                // (a hairline round it, so white shows on a light card)
                var swatch = new Border { Child = colourStrip, BorderThickness = new Thickness(1), Margin = new Thickness(0, 6, 0, 0) };
                swatch.SetResourceReference(Border.BorderBrushProperty, "Line");
                var card = new RadioButton
                {
                    Style = (Style)FindResource("Card"),
                    GroupName = "Machine",
                    Tag = p.Id,
                    ToolTip = Words.About(p),
                    Content = new StackPanel
                    {
                        Children =
                        {
                            new TextBlock { Text = p.Name, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis },
                            Dim(new TextBlock { Text = p.CardMeta, FontSize = 11.5, Margin = new Thickness(0, 1, 0, 0) }),
                            swatch,
                        },
                    },
                };
                AutomationProperties.SetName(card, p.Name);
                card.Checked += OnMachine;
                Cards.Children.Add(card);
            }
            foreach (DitherInfo d in Dither.All)
            {
                var item = new RadioButton
                {
                    Style = (Style)FindResource("DitherItem"),
                    GroupName = "Dither",
                    Tag = d.Id,
                    Content = new StackPanel
                    {
                        Children =
                        {
                            new TextBlock { Text = d.Name, FontWeight = FontWeights.SemiBold },
                            Dim(new TextBlock { Text = d.Hint, FontSize = 12 }),
                        },
                    },
                };
                AutomationProperties.SetName(item, d.Name);
                item.Checked += OnDither;
                Dithers.Children.Add(item);
            }
            foreach (int m in Export.Multipliers)
            {
                var size = new RadioButton { Style = (Style)FindResource("Segment"), GroupName = "Size", Tag = m, Content = m + "×", Margin = new Thickness(0, 0, 6, 6) };
                AutomationProperties.SetName(size, m + " times");
                size.Checked += OnSize;
                Sizes.Children.Add(size);
            }
            SampleLine.Inlines.Add(new Run("Try a picture: "));
            for (int i = 0; i < Pictures.Samples.Length; i++)
            {
                var sample = Pictures.Samples[i];
                if (i > 0) SampleLine.Inlines.Add(new Run(" · "));
                var link = new Hyperlink(new Run(sample.Label)) { Tag = sample.Id };
                link.Click += (s, e) => OpenSample((string)((Hyperlink)s).Tag);
                SampleLine.Inlines.Add(link);
                var item = new MenuItem { Header = sample.Menu, Tag = sample.Id };
                item.Click += (s, e) => OpenSample((string)((MenuItem)s).Tag);
                SamplesMenu.Items.Add(item);
            }
        }

        static TextBlock Dim(TextBlock text)
        {
            text.SetResourceReference(TextBlock.ForegroundProperty, "Dim");
            return text;
        }

        // ---------- the controls, from the settings ----------

        // Sets every control from the settings, and the lines that follow from them.
        internal void Sync()
        {
            quiet = true;
            try
            {
                DeresSettings d = settings.Deres;
                Profile p = CurrentProfile;
                Geometry g = PlannedGeometry();

                PictureInfo.Text = Words.PictureInfo(picture);
                NativeButton.Content = Words.Native(p);
                NativeButton.IsChecked = !d.Custom;
                CustomButton.IsChecked = d.Custom;
                WidthPanel.Visibility = d.Custom ? Visibility.Visible : Visibility.Collapsed;
                if (d.Custom)
                {
                    WidthLimits(out int min, out int max);
                    int w = g?.Width ?? ClampWidth(d.CustomWidth ?? p.Width, min, max);
                    if (!WidthBox.IsKeyboardFocusWithin) WidthBox.Text = w.ToString();
                    WidthHeight.Text = g != null ? "× " + g.Height + " px" : "px";
                    WidthSlider.Value = ToTravel(w, min, max);
                }
                double coverX = 0, coverY = 0;
                if (g != null) g.Coverage(picture.Source.Width, picture.Source.Height, picture.Source.Width, picture.Source.Height, out coverX, out coverY);
                ResolutionInfo.Text = Words.Resolution(p, d.Custom, g, coverX, coverY, g != null);
                FitPanel.Visibility = !d.Custom && !p.AutoHeight ? Visibility.Visible : Visibility.Collapsed;
                FitFill.IsChecked = d.Fit == Fits.Fill;
                FitFit.IsChecked = d.Fit == Fits.Fit;
                FitStretch.IsChecked = d.Fit == Fits.Stretch;
                DetailSlider.Value = JsMath.Round(d.Detail * 20);
                DetailValue.Text = Words.Percent(d.Detail);
                DetailHint.Text = Words.DetailHint(g != null && Engine.Detail.Amount(1, coverX, coverY) == 0);
                BackgroundPanel.Visibility = picture != null && picture.Source.HasAlpha ? Visibility.Visible : Visibility.Collapsed;
                BackWhite.IsChecked = d.Background == Backgrounds.White;
                BackBlack.IsChecked = d.Background == Backgrounds.Black;

                foreach (RadioButton card in Cards.Children) card.IsChecked = (string)card.Tag == p.Id;
                AboutLine.Text = Words.About(p);

                BrightnessSlider.Value = JsMath.Round(d.Adjust.Brightness * 100);
                ContrastSlider.Value = JsMath.Round(d.Adjust.Contrast * 100);
                SaturationSlider.Value = JsMath.Round(d.Adjust.Saturation * 100);
                GammaSlider.Value = JsMath.Round(d.Adjust.Gamma * 100);
                BrightnessValue.Text = Words.SignedPercent(d.Adjust.Brightness);
                ContrastValue.Text = Words.Percent(d.Adjust.Contrast);
                SaturationValue.Text = Words.Percent(d.Adjust.Saturation);
                GammaValue.Text = Words.Gamma(d.Adjust.Gamma);

                string dither = Dither.ById(d.Dither) != null ? d.Dither : p.Dither;
                foreach (RadioButton item in Dithers.Children) item.IsChecked = (string)item.Tag == dither;

                bool square = g != null ? g.SquarePixels : p.SquarePixels;
                ShapePanel.Visibility = square ? Visibility.Collapsed : Visibility.Visible;
                ShapeRaw.IsChecked = !settings.TvPixels;
                ShapeTv.IsChecked = settings.TvPixels;
                ShapeNote.Text = g != null && !square ? Words.ShapeNote(g) : "";
                bool tv = settings.TvPixels && !square;
                foreach (RadioButton size in Sizes.Children)
                {
                    int m = (int)size.Tag;
                    size.IsChecked = m == settings.Multiplier;
                    if (g != null)
                    {
                        Export.Size(g.Width, g.Height, g.ParX, g.ParY, m, tv, out int w, out int h);
                        size.ToolTip = w + "×" + h;
                    }
                }
                if (g != null)
                {
                    Export.Size(g.Width, g.Height, g.ParX, g.ParY, settings.Multiplier, tv, out int ew, out int eh);
                    SizeLine.Text = Words.SizeLine(ew, eh, p);
                }
                else SizeLine.Text = "";
            }
            finally
            {
                quiet = false;
            }
            CommandManager.InvalidateRequerySuggested();
        }

        // the framing the settings give the picture now (null with no picture)
        Geometry PlannedGeometry()
        {
            if (picture == null) return null;
            DeresSettings d = settings.Deres;
            PictureSource src = picture.Source;
            return Geometry.Plan(src.Width, src.Height, src.Width, src.Height, CurrentProfile, d.Custom, d.CustomWidth, d.Fit);
        }

        // A setting changed: the controls follow, the picture is made again, and the settings are
        // kept a moment later (once, after a slider stops).
        void Changed()
        {
            Sync();
            RenderSoon();
            saveTimer.Stop();
            saveTimer.Start();
        }

        // ---------- the controls' events ----------

        void OnMachine(object sender, RoutedEventArgs e)
        {
            if (Quiet) return;
            Profile p = Profiles.ById((string)((RadioButton)sender).Tag);
            if (p == null || p.Id == settings.Deres.ProfileId) return;
            // each machine starts with the dither that suits it, as on the website
            settings.Deres.ProfileId = p.Id;
            settings.Deres.Dither = p.Dither;
            Changed();
        }

        void OnDither(object sender, RoutedEventArgs e)
        {
            if (Quiet) return;
            settings.Deres.Dither = (string)((RadioButton)sender).Tag;
            Changed();
        }

        void OnResolution(object sender, RoutedEventArgs e)
        {
            if (Quiet) return;
            settings.Deres.Custom = sender == CustomButton;
            Changed();
        }

        void OnFraming(object sender, RoutedEventArgs e)
        {
            if (Quiet) return;
            settings.Deres.Fit = (string)((RadioButton)sender).Tag;
            Changed();
        }

        void OnDetail(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (Quiet) return;
            // a stop on the slider is the number the website's 0.05 steps give: 7 is 0.35
            settings.Deres.Detail = JsMath.Round(DetailSlider.Value) * 5 / 100.0;
            Changed();
        }

        void OnBackground(object sender, RoutedEventArgs e)
        {
            if (Quiet) return;
            settings.Deres.Background = (string)((RadioButton)sender).Tag;
            Changed();
        }

        void OnAdjust(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (Quiet) return;
            // whole hundredths, as the website's 0.01 steps: 137 is 1.37
            Adjust a = settings.Deres.Adjust;
            if (sender == BrightnessSlider) a.Brightness = JsMath.Round(BrightnessSlider.Value) / 100.0;
            else if (sender == ContrastSlider) a.Contrast = JsMath.Round(ContrastSlider.Value) / 100.0;
            else if (sender == SaturationSlider) a.Saturation = JsMath.Round(SaturationSlider.Value) / 100.0;
            else if (sender == GammaSlider) a.Gamma = JsMath.Round(GammaSlider.Value) / 100.0;
            Changed();
        }

        void OnAdjustReset(object sender, RoutedEventArgs e)
        {
            settings.Deres.Adjust = new Adjust();
            Changed();
        }

        void OnShape(object sender, RoutedEventArgs e)
        {
            if (Quiet) return;
            settings.TvPixels = sender == ShapeTv;
            Changed();
        }

        void OnSize(object sender, RoutedEventArgs e)
        {
            if (Quiet) return;
            settings.Multiplier = (int)((RadioButton)sender).Tag;
            Changed();
        }

        // ---- the custom width: a box for an exact number, and a slider in even ratios (20 -> 40
        // takes as much travel as 400 -> 800) ----

        const int WidthTravel = 1000;

        void WidthLimits(out int min, out int max)
        {
            if (picture == null)
            {
                min = Geometry.CustomMin;
                max = Geometry.CustomMax;
                return;
            }
            PictureSource src = picture.Source;
            Geometry.CustomWidthRange(src.Width, src.Height, src.Width, src.Height, out min, out max);
        }

        static int ToTravel(int w, int min, int max) => max > min ? (int)JsMath.Round(WidthTravel * Math.Log((double)w / min) / Math.Log((double)max / min)) : 0;

        static int FromTravel(double t, int min, int max) => (int)JsMath.Round(min * JsMath.Pow((double)max / min, t / WidthTravel));

        static int ClampWidth(double w, int min, int max) => (int)Math.Min(max, Math.Max(min, JsMath.Round(w)));

        // typing: every whole number in range shows at once; the box is tidied when it's left
        void OnWidthTyped(object sender, TextChangedEventArgs e)
        {
            if (Quiet || !WidthBox.IsKeyboardFocusWithin) return;
            WidthLimits(out int min, out int max);
            if (int.TryParse(WidthBox.Text.Trim(), out int w) && w >= min && w <= max && w != settings.Deres.CustomWidth)
            {
                settings.Deres.CustomWidth = w;
                Changed();
            }
        }

        void OnWidthLeft(object sender, KeyboardFocusChangedEventArgs e) => TidyWidth();

        // for the tests: a width typed into the box, then Enter
        internal void TypeWidth(string text)
        {
            WidthBox.Text = text;
            TidyWidth();
        }

        void OnWidthKey(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;
            TidyWidth();
            e.Handled = true;
        }

        void TidyWidth()
        {
            WidthLimits(out int min, out int max);
            if (double.TryParse(WidthBox.Text.Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double typed) && !double.IsNaN(typed) && !double.IsInfinity(typed))
                settings.Deres.CustomWidth = ClampWidth(typed, min, max);
            WidthBox.Text = (PlannedGeometry()?.Width ?? settings.Deres.CustomWidth ?? CurrentProfile.Width).ToString();
            Changed();
        }

        void OnWidthSlider(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (Quiet) return;
            WidthLimits(out int min, out int max);
            settings.Deres.CustomWidth = FromTravel(WidthSlider.Value, min, max);
            Changed();
        }

        // ---------- opening pictures ----------

        void OnOpenPicture(object sender, ExecutedRoutedEventArgs e)
        {
            string path;
            try
            {
                path = FileDialogs.OpenPicture(this);
            }
            catch (COMException x)
            {
                MessageBox.Show(this, "Windows' Open dialog didn't open:\n\n" + x.Message, Program.Name, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (path != null) OpenFile(path);
        }

        internal void OpenFile(string path) => Open(() => Pictures.Load(path), Pictures.NameOf(path));

        internal void OpenSample(string id) => Open(() => Pictures.Sample(id), Pictures.Samples.First(s => s.Id == id).File);

        // Opens a picture off the window's thread. Until it's open the old one stays (or the stage
        // says it's opening); a picture that can't be opened says why and changes nothing.
        internal async void Open(Func<LoadedPicture> load, string name)
        {
            int ticket = ++opening;
            Opening = true;
            if (picture == null) ShowStage("Opening…");
            else Readout.Text = "Opening…";
            LoadedPicture loaded = null;
            string problem = null;
            try
            {
                loaded = await Task.Run(load);
            }
            catch (PictureException x)
            {
                problem = x.Message;
            }
            catch (OutOfMemoryException)
            {
                problem = "it's too big to open with the memory this PC has free";
            }
            catch (Exception x)
            {
                // anything else Windows' readers throw is still just this picture's problem
                problem = "something went wrong (" + x.Message.TrimEnd('.') + ")";
            }
            if (ticket != opening) return;
            Opening = false;
            if (problem != null)
            {
                Problem("Couldn't open " + name + ": " + problem + ".");
                if (picture == null) ShowStage("Choose a picture to start.");
                else ShowReadout();
                return;
            }
            picture = loaded;
            Sync();
            RenderSoon();
        }

        internal bool Opening { get; private set; }

        // what was copied or dropped: the first picture file in it, or the picture itself
        internal void OpenData(IDataObject data, string nothing)
        {
            string[] files = null;
            Func<LoadedPicture> pasted = null;
            try
            {
                if (data != null) Pictures.From(data, out files, out pasted);
            }
            catch (Exception x) when (x is ExternalException || x is PictureException)
            {
                // the other program let go of it, or it's no picture: nothing to open
            }
            if (pasted != null)
            {
                Open(pasted, "the pasted picture");
                return;
            }
            if (files != null)
            {
                string[] existing = files.Where(f => File.Exists(f)).ToArray();
                if (existing.Length == 0)
                {
                    Problem(files.Any(Directory.Exists) ? "That's a folder: drop a picture. (Folders of pictures come in a later version.)" : nothing);
                    return;
                }
                string[] pictures = existing.Where(Pictures.LooksLikePicture).ToArray();
                string first = pictures.FirstOrDefault() ?? existing[0];
                OpenFile(first);
                if (pictures.Length > 1) Flash("Opened " + Pictures.NameOf(first) + ", the first of " + pictures.Length + " pictures.", "Dim");
                return;
            }
            Problem(nothing);
        }

        void OnPastePicture(object sender, ExecutedRoutedEventArgs e)
        {
            IDataObject data;
            try
            {
                data = Clipboard.GetDataObject();
            }
            catch (ExternalException)
            {
                Problem("Couldn't look at the clipboard: another program is using it. Try again.");
                return;
            }
            OpenData(data, NothingCopied);
        }

        const string NothingCopied = "Nothing to open on the clipboard: copy a picture, or a picture file, first.";

        // pictures or picture files dragged onto the window open; what nothing takes is refused
        void OnPreviewDragOver(object sender, DragEventArgs e)
        {
            if (draggingOut || !Pictures.CanRead(e.Data)) return;
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
        }

        void OnPreviewDrop(object sender, DragEventArgs e)
        {
            if (draggingOut || !Pictures.CanRead(e.Data)) return;
            e.Handled = true;
            OpenData(e.Data, "Nothing to open in what was dropped.");
        }

        // Text dragged in, or anything while the picture is being dragged out, is refused: the
        // window lets things be dropped on all of it, and a drop nobody answers can look like a
        // move to the program it came from, which then deletes its copy.
        void OnDragElsewhere(object sender, DragEventArgs e)
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
        }

        // ---------- making the picture ----------

        DeresSettings wanted;
        LoadedPicture wantedPicture;
        bool rendering;
        int renders;   // for the tests: how many have been shown

        internal bool Rendering => rendering || wanted != null;
        internal int Renders => renders;
        internal Rendered Shown => shown;
        internal DeresSettings ShownWith => shownWith;

        void RenderSoon()
        {
            if (picture == null) return;
            wanted = settings.Deres.Clone();
            wantedPicture = picture;
            if (!rendering) RenderLoop();
        }

        async void RenderLoop()
        {
            rendering = true;
            busyTimer.Start();
            try
            {
                while (wanted != null)
                {
                    DeresSettings s = wanted;
                    LoadedPicture pic = wantedPicture;
                    wanted = null;
                    Rendered r = null;
                    BitmapSource original = null;
                    string problem = null;
                    try
                    {
                        (r, original) = await Task.Run(() => Make(pic, s));
                    }
                    catch (OutOfMemoryException)
                    {
                        problem = "There isn't enough memory free to make this picture.";
                    }
                    catch (Exception x) when (x is InvalidOperationException || x is ArgumentException)
                    {
                        problem = "Something went wrong making the picture: " + x.Message;
                    }
                    // Shown even when newer settings are waiting (they're made next), as the
                    // website does: a slow picture still follows a slider as it moves.
                    if (problem != null)
                    {
                        if (wanted == null) Problem(problem);
                    }
                    else Show(r, original, s, pic);
                }
            }
            finally
            {
                rendering = false;
                busyTimer.Stop();
                ShowReadout();
            }
        }

        string originalKey;
        BitmapSource originalPicture;

        // on a worker thread, one at a time: the picture, and the original framed the same way
        (Rendered, BitmapSource) Make(LoadedPicture pic, DeresSettings s)
        {
            lock (engine)
            {
                if (engine.Source != pic.Source) engine.SetSource(pic.Source);
                Rendered r = engine.Render(s);
                Geometry g = r.Geometry;
                string key = string.Join("|", pic.GetHashCode(), g.Width, g.Height, g.Src.X, g.Src.Y, g.Src.W, g.Src.H, g.Dst.X, g.Dst.Y, g.Dst.W, g.Dst.H, s.Background);
                if (key != originalKey)
                {
                    originalPicture = Original.Framed(pic.Source, g, s.Background);
                    originalKey = key;
                }
                return (r, originalPicture);
            }
        }

        void Show(Rendered r, BitmapSource original, DeresSettings s, LoadedPicture from)
        {
            shown = r;
            shownWith = s;
            shownFrom = from;
            renders++;
            View.Show(r.Rgb, r.Width, r.Height, PixelRatio(r));
            View.SetOriginal(original);
            BitmapSource small = BitmapSource.Create(r.Width, r.Height, 96, 96, PixelFormats.Rgb24, null, r.Rgb, r.Width * 3);
            small.Freeze();
            DragThumb.Source = small;
            ShowStage();
            ShowReadout();
            ShowZoom();
            CommandManager.InvalidateRequerySuggested();
        }

        // how wide a pixel is shown: its shape on a TV when that's chosen, else square
        double PixelRatio(Rendered r) => settings.TvPixels && !r.Geometry.SquarePixels ? (double)r.Geometry.ParX / r.Geometry.ParY : 1;

        void ShowReadout()
        {
            if (Opening) return;
            string text = shown != null ? Words.Readout(shown) : "";
            if (Rendering && !busyTimer.IsEnabled && shown != null) text += " · working…";
            Readout.Text = text;
        }

        void ShowStage(string message = null)
        {
            if (message == null && shown == null && picture == null) message = "Choose a picture to start.";
            StageNote.Text = message ?? "";
            StageNote.Visibility = message != null ? Visibility.Visible : Visibility.Collapsed;
            HoldHint.Visibility = shown != null ? Visibility.Visible : Visibility.Hidden;
            DragOut.Visibility = shown != null ? Visibility.Visible : Visibility.Hidden;
        }

        void ShowZoom()
        {
            ZoomText.Text = View.HasPicture ? View.ZoomText : "";
            ShowCompare();
        }

        // ---------- the preview's view ----------

        void WhenThereIsAPicture(object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = shown != null;
            e.Handled = true;
        }

        void OnZoomIn(object sender, ExecutedRoutedEventArgs e) => View.ZoomStep(1);
        void OnZoomOut(object sender, ExecutedRoutedEventArgs e) => View.ZoomStep(-1);
        void OnZoomFit(object sender, ExecutedRoutedEventArgs e) => View.ZoomToFit();
        void OnZoomOne(object sender, ExecutedRoutedEventArgs e) => View.ZoomTo(1);

        void OnCompare(object sender, ExecutedRoutedEventArgs e)
        {
            View.SetComparing(!View.Comparing);
            ShowCompare();
        }

        void ShowCompare()
        {
            bool on = View.Comparing;
            CompareButton.IsChecked = on;
            CompareMenu.IsChecked = on;
            TagOriginal.Visibility = View.ShowingOriginal ? Visibility.Visible : Visibility.Collapsed;
            TagDeres.Visibility = on && !View.Holding ? Visibility.Visible : Visibility.Collapsed;
        }

        // ---- full screen: just the picture, on black ----

        bool fullScreen;
        WindowState stateBefore;
        object contentBefore;

        internal bool IsFullScreen => fullScreen;

        void OnFullScreen(object sender, ExecutedRoutedEventArgs e) => SetFullScreen(!fullScreen);

        internal void SetFullScreen(bool on)
        {
            if (on == fullScreen) return;
            fullScreen = on;
            if (on)
            {
                stateBefore = WindowState;
                contentBefore = Content;
                StageArea.Children.Remove(Stage);
                Stage.CornerRadius = new CornerRadius(0);
                Stage.Background = Brushes.Black;
                Content = Stage;
                View.SetBackground(Colors.Black);
                WindowStyle = WindowStyle.None;
                if (WindowState == WindowState.Maximized) WindowState = WindowState.Normal;  // (so Windows covers the taskbar too)
                WindowState = WindowState.Maximized;
            }
            else
            {
                Content = contentBefore;
                StageArea.Children.Insert(0, Stage);
                Stage.CornerRadius = new CornerRadius(8);
                Stage.SetResourceReference(Border.BackgroundProperty, "Stage");
                OnTheme();
                // through Normal, or a window maximized before keeps the borderless size over the taskbar
                WindowState = WindowState.Normal;
                WindowStyle = WindowStyle.SingleBorderWindow;
                WindowState = stateBefore;
            }
            FullScreenMenu.IsChecked = on;
            View.ZoomToFit();
            View.Focus();
        }

        // ---------- keys ----------

        // Ctrl+V with a picture copied, and Ctrl+C, work on pictures wherever the focus is, except
        // in a text box, where they're the text's. Space held shows the original, except on a
        // control that Space works.
        void OnPreviewKeys(object sender, KeyEventArgs e)
        {
            bool inText = Keyboard.FocusedElement is TextBox;
            if (e.Key == Key.Space && Keyboard.Modifiers == ModifierKeys.None && !TakesSpace(Keyboard.FocusedElement))
            {
                e.Handled = true;
                View.Hold(true);
                ShowCompare();
                return;
            }
            if (Keyboard.Modifiers != ModifierKeys.Control || inText) return;
            if (e.Key == Key.V)
            {
                IDataObject data;
                try
                {
                    data = Clipboard.GetDataObject();
                }
                catch (ExternalException)
                {
                    return;
                }
                if (!Pictures.CanRead(data)) return;
                e.Handled = true;
                OpenData(data, NothingCopied);
            }
            else if (e.Key == Key.C && shown != null)
            {
                e.Handled = true;
                Commands.CopyPicture.Execute(null, this);
            }
        }

        void OnPreviewKeysUp(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Space || !View.Holding) return;
            View.Hold(false);
            ShowCompare();
        }

        static bool TakesSpace(IInputElement focused) => focused is TextBoxBase || focused is ButtonBase || focused is Slider || focused is ComboBox || focused is MenuItem;

        // Esc leaves full screen (once menus and boxes have had their turn)
        void OnKeys(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape && fullScreen)
            {
                e.Handled = true;
                SetFullScreen(false);
            }
        }

        // ---------- getting the picture out ----------

        // the picture as it is now: waits for one being made with the latest settings
        async Task<Rendered> Latest()
        {
            while (Rendering) await Task.Delay(20);
            return shown;
        }

        string FileName(Rendered r) =>
            Export.FileName(Export.BaseName(shownFrom?.Name ?? "photo"), r.Profile.Id, r.Width, r.Height, settings.Multiplier, settings.TvPixels && !r.Geometry.SquarePixels);

        async void OnSavePng(object sender, ExecutedRoutedEventArgs e)
        {
            Rendered r = await Latest();
            if (r == null) return;
            int m = settings.Multiplier;
            bool tv = settings.TvPixels && !r.Geometry.SquarePixels;
            string name = FileName(r);
            string path;
            try
            {
                path = FileDialogs.Save(this, "Save the picture as a PNG", "PNG picture", ".png", name);
            }
            catch (COMException x)
            {
                MessageBox.Show(this, "Windows' Save dialog didn't open:\n\n" + x.Message, Program.Name, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (path == null) return;
            await SavePng(path, r, m, tv);
        }

        internal bool Saving { get; private set; }

        internal async Task SavePng(string path, Rendered r, int m, bool tv)
        {
            Saving = true;
            Flash("Saving " + Path.GetFileName(path) + "…", "Dim");
            int width = 0, height = 0;
            string problem = null;
            try
            {
                await Task.Run(() => Exports.Write(path, stream => Export.WritePng(stream, r, m, tv, out width, out height)));
            }
            catch (Exception x) when (x is IOException || x is UnauthorizedAccessException || x is NotSupportedException || x is System.Security.SecurityException)
            {
                problem = x.Message;
            }
            catch (OutOfMemoryException)
            {
                problem = "there isn't enough memory free for a picture that big";
            }
            Saving = false;
            if (problem != null) MessageBox.Show(this, "Couldn't save " + Path.GetFileName(path) + ":\n\n" + problem, Program.Name, MessageBoxButton.OK, MessageBoxImage.Warning);
            else Flash("Saved " + Path.GetFileName(path) + " (" + width + "×" + height + ").", "Good");
        }

        // Copy and drag hand over at most this many pixels, the website's own limit for any
        // export: past it they go at the biggest Size within it (made in a moment, where the
        // biggest exports take seconds), and the bottom line says so. Save PNG makes any size.
        internal const long MaxShared = 4096L * 4096;

        // what Copy and drag hand over: the PNG and the same picture plain (for Paint, Word and
        // email), at the Size chosen or the biggest within MaxShared, named for what it is
        internal sealed class Shared
        {
            public int Multiplier, Width, Height;
            public bool Tv;
            public string Name;
            public byte[] Png;
            public BitmapSource Picture;

            // the PNG and the picture; safe on any thread
            public void Make(Rendered r)
            {
                Png = Export.PngBytes(r, Multiplier, Tv);
                Picture = Sharing.Picture(r, Multiplier, Tv);
            }
        }

        internal Shared ToShare(Rendered r)
        {
            var s = new Shared { Tv = settings.TvPixels && !r.Geometry.SquarePixels, Multiplier = 1 };
            foreach (int m in Export.Multipliers)
            {
                if (m > settings.Multiplier) break;
                Export.Size(r.Width, r.Height, r.Geometry.ParX, r.Geometry.ParY, m, s.Tv, out int w, out int h);
                if ((long)w * h <= MaxShared) s.Multiplier = m;
            }
            Export.Size(r.Width, r.Height, r.Geometry.ParX, r.Geometry.ParY, s.Multiplier, s.Tv, out s.Width, out s.Height);
            s.Name = Export.FileName(Export.BaseName(shownFrom?.Name ?? "photo"), r.Profile.Id, r.Width, r.Height, s.Multiplier, s.Tv);
            return s;
        }

        // for the bottom line: "(640×576)", or at what Size it went and why
        string Handed(Shared s) => s.Multiplier == settings.Multiplier
            ? "(" + s.Width + "×" + s.Height + ")."
            : "at " + s.Multiplier + "× (" + s.Width + "×" + s.Height + "): bigger is more than can be handed over. Save PNG makes any size.";

        async void OnCopyPicture(object sender, ExecutedRoutedEventArgs e)
        {
            Rendered r = await Latest();
            if (r == null) return;
            Shared share = ToShare(r);
            try
            {
                await Task.Run(() => share.Make(r));
            }
            catch (OutOfMemoryException)
            {
                Problem("There isn't enough memory free to copy the picture.");
                return;
            }
            try
            {
                Clipboard.SetDataObject(Exports.DataFor(share.Picture, share.Png, share.Name, false), true);
                Flash("Copied the picture " + Handed(share), "Good");
            }
            catch (ExternalException)
            {
                Problem("Couldn't copy: another program is using the clipboard. Try again.");
            }
        }

        // ---- dragging the picture out, from the small picture under the preview ----

        Point dragStart;
        bool dragArmed, draggingOut;

        void OnDragOutDown(object sender, MouseButtonEventArgs e)
        {
            if (shown == null) return;
            dragStart = e.GetPosition(this);
            dragArmed = true;
        }

        void OnDragOutMove(object sender, MouseEventArgs e)
        {
            if (!dragArmed) return;
            if (e.LeftButton != MouseButtonState.Pressed || shown == null)
            {
                dragArmed = false;
                return;
            }
            Point now = e.GetPosition(this);
            if (Math.Abs(now.X - dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(now.Y - dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            dragArmed = false;
            // the picture on screen, as the small picture shows it (a drag can't wait for one being made)
            Shared share = ToShare(shown);
            DataObject data;
            try
            {
                data = DataToShare(shown, share, true);
            }
            catch (OutOfMemoryException)
            {
                Problem("There isn't enough memory free to drag the picture.");
                return;
            }
            draggingOut = true;  // dropped back on this window, it isn't a picture to open
            try
            {
                if (DragDrop.DoDragDrop(DragOut, data, DragDropEffects.Copy) != DragDropEffects.None && share.Multiplier != settings.Multiplier)
                    Flash("Dragged the picture " + Handed(share), "Dim");
            }
            catch (ExternalException)
            {
                // the program it was dropped on failed; there's nothing to undo here
            }
            finally
            {
                draggingOut = false;
            }
        }

        // what a drag hands over (and the tests check), made here and now
        internal DataObject DataToShare(Rendered r, Shared share, bool withFile)
        {
            share.Make(r);
            return Exports.DataFor(share.Picture, share.Png, share.Name, withFile);
        }

        // ---------- the look, notes and the rest ----------

        void OnTheme()
        {
            ShowLook();
            if (!fullScreen && TryFindResource("Stage") is SolidColorBrush stage) View.SetBackground(stage.Color);
        }

        void ShowLook()
        {
            LookWindows.IsChecked = Theme.Choice == Look.Windows;
            LookLight.IsChecked = Theme.Choice == Look.Light;
            LookDark.IsChecked = Theme.Choice == Look.Dark;
        }

        // the bottom line: why settings aren't kept, or why the last save of them failed
        void ShowNote()
        {
            string why = DataFolder.WhyNotKept ?? DataFolder.LastProblem ?? "";
            Note.Text = why;
            Note.ToolTip = why.Length > 0 ? why : null;
            Note.SetResourceReference(TextBlock.ForegroundProperty, "Warn");
        }

        // a few seconds of news in the bottom line ("Saved photo-nes-256x240-4x.png")
        internal void Flash(string text, string colour)
        {
            Note.Text = text;
            Note.ToolTip = text;
            Note.SetResourceReference(TextBlock.ForegroundProperty, colour);
            noteTimer.Stop();
            noteTimer.Start();
        }

        internal string NoteText => Note.Text;

        void Problem(string text) => Flash(text, "Bad");

        void OnLook(object sender, RoutedEventArgs e)
        {
            var look = (Look)Enum.Parse(typeof(Look), (string)((MenuItem)sender).Tag);
            settings.Look = look;
            Theme.Set(look);
            ShowLook();
            settings.Save();
            ShowNote();
        }

        void OnAbout(object sender, ExecutedRoutedEventArgs e)
        {
            new AboutWindow { Owner = this }.ShowDialog();
        }

        void OnMaker(object sender, RoutedEventArgs e) => OpenInBrowser(this, Program.MakerUrl);

        void OnWebsite(object sender, RoutedEventArgs e) => OpenInBrowser(this, Program.WebsiteUrl);

        // a link opens in your browser; b0r3d DeRes itself never goes online
        internal static void OpenInBrowser(Window owner, string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception x) when (x is Win32Exception || x is InvalidOperationException)
            {
                MessageBox.Show(owner, "Couldn't open " + url + ":\n\n" + x.Message, Program.Name, MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        void OnExit(object sender, RoutedEventArgs e) => Close();

        void OnSessionEnding(object sender, SessionEndingCancelEventArgs e)
        {
            SaveSettings();
            Exports.ClearDragFolder();
        }

        void OnClosing(object sender, CancelEventArgs e)
        {
            if (fullScreen) SetFullScreen(false);
            saveTimer.Stop();
            SaveSettings();
            Exports.ClearDragFolder();
        }

        // the look, the window's size and the settings (never the picture) into the data folder
        internal void SaveSettings()
        {
            settings.Look = Theme.Choice;
            settings.Maximized = WindowState == WindowState.Maximized || WindowState == WindowState.Minimized && maximized;
            Rect normal = WindowState == WindowState.Normal ? new Rect(Left, Top, ActualWidth, ActualHeight) : RestoreBounds;
            if (!fullScreen && !normal.IsEmpty && normal.Width > 0 && normal.Height > 0)
            {
                settings.WindowWidth = normal.Width;
                settings.WindowHeight = normal.Height;
            }
            settings.Save();
            ShowNote();
        }
    }
}
