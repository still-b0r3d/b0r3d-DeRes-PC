using System;
using System.Collections.Generic;
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

        // for the tests: the settings as they are now, and the picture that's open
        internal Settings Current => settings;
        internal LoadedPicture OpenedPicture => picture;

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
            View.ZoomChanged += FxSoon;

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
                var colourStrip = new Image { Source = Strip(Profiles.PaletteBytes(p)), Stretch = Stretch.Fill, Height = 6 };
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
                card.Click += OnMachineAgain;
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
            BuildScreens();
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
                BackKeep.IsChecked = d.Background == Backgrounds.Transparent;
                KeepNote.Text = Words.KeepNote;
                KeepNote.Visibility = d.Background == Backgrounds.Transparent ? Visibility.Visible : Visibility.Collapsed;

                foreach (RadioButton card in Cards.Children) card.IsChecked = (string)card.Tag == p.Id;
                ShowCustomStrip();
                AboutLine.Text = Words.About(p);
                // Range, pool and Custom machines pick their colours from the picture; the slider can
                // ask for fewer, never more than the machine had.
                int? maxColours = Profiles.MaxColourCount(p);
                PaletteTools.Visibility = maxColours != null ? Visibility.Visible : Visibility.Collapsed;
                if (maxColours != null)
                {
                    int count = Profiles.ColourCount(p, d.CustomCount).Value;
                    ColoursSlider.Maximum = maxColours.Value;
                    ColoursSlider.Value = count;
                    ColoursValue.Text = count + " colours";
                    ColoursPanel.Visibility = p.Kind == PaletteKind.Custom && d.CustomPalette != null ? Visibility.Collapsed : Visibility.Visible;
                }
                GrabPaletteButton.IsEnabled = GrabMenu.IsEnabled = picture != null;

                BrightnessSlider.Value = JsMath.Round(d.Adjust.Brightness * 100);
                ContrastSlider.Value = JsMath.Round(d.Adjust.Contrast * 100);
                SaturationSlider.Value = JsMath.Round(d.Adjust.Saturation * 100);
                GammaSlider.Value = JsMath.Round(d.Adjust.Gamma * 100);
                BrightnessValue.Text = Words.SignedPercent(d.Adjust.Brightness);
                ContrastValue.Text = Words.Percent(d.Adjust.Contrast);
                SaturationValue.Text = Words.Percent(d.Adjust.Saturation);
                GammaValue.Text = Words.Gamma(d.Adjust.Gamma);

                foreach (RadioButton item in Dithers.Children)
                {
                    DitherInfo info = Dither.ById((string)item.Tag);
                    // a machine's own dither only on that machine; greyed out where it would change nothing
                    item.Visibility = !info.Native || p.NativeDither == info.Id ? Visibility.Visible : Visibility.Collapsed;
                    item.IsEnabled = Profiles.DitherApplies(p, info.Id);
                    item.IsChecked = d.Dither == info.Id && item.IsEnabled;
                }
                string ditherNote = Profiles.DitherNote(p);
                DitherNote.Text = ditherNote ?? "";
                DitherNote.Visibility = ditherNote != null ? Visibility.Visible : Visibility.Collapsed;

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
                    SizeLine.Text = Words.SizeLine(ew, eh, p, d, Export.WithFx(settings.ExportScreen, settings.Multiplier));
                }
                else SizeLine.Text = "";
                SyncScreen();

                // the machine's own file (.scr, .koa or .chr), and why there's none where that needs saying
                string format = p.EmulatorFormats.FirstOrDefault();
                EmulatorButton.Visibility = format != null ? Visibility.Visible : Visibility.Collapsed;
                EmulatorButton.Content = format != null ? Words.EmulatorButton(p, format) : null;
                EmulatorButton.IsEnabled = EmulatorMenu.IsEnabled = format != null && picture != null;
                ExportNote.Text = p.ExportNote ?? "";
                ExportNote.Visibility = p.ExportNote != null ? Visibility.Visible : Visibility.Collapsed;
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

        // A setting that changes no picture (the export's Size, its screen box): the controls
        // follow, and the settings are kept a moment later.
        void Kept()
        {
            Sync();
            saveTimer.Stop();
            saveTimer.Start();
        }

        // ---------- the controls' events ----------

        void OnMachine(object sender, RoutedEventArgs e)
        {
            if (Quiet) return;
            Profile p = Profiles.ById((string)((RadioButton)sender).Tag);
            if (p == null || p.Id == settings.Deres.ProfileId) return;
            // each machine starts with the dither and the screen that suit it (while a screen is
            // on; the VHS tape always brings its own), and the Colours slider at its own count, as
            // on the website
            settings.Deres.ProfileId = p.Id;
            settings.Deres.Dither = p.Dither;
            settings.Deres.CustomCount = null;
            settings.Fx.Screen = p.Id == "vhs" ? Screens.Vhs : settings.Fx.Screen == Screens.Off ? Screens.Off : p.Screen;
            Changed();
        }

        // The machine that's picked, clicked again: it starts over with its own dither and count,
        // as the website picks a machine whenever its card is clicked.
        void OnMachineAgain(object sender, RoutedEventArgs e)
        {
            if (Quiet || (string)((RadioButton)sender).Tag != settings.Deres.ProfileId) return;
            Profile p = CurrentProfile;
            if (settings.Deres.Dither == p.Dither && settings.Deres.CustomCount == null) return;
            settings.Deres.Dither = p.Dither;
            settings.Deres.CustomCount = null;
            Changed();
        }

        void OnColours(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (Quiet) return;
            settings.Deres.CustomCount = (int)JsMath.Round(ColoursSlider.Value);
            Changed();
        }

        // the Custom machine's card shows its loaded or grabbed palette
        void ShowCustomStrip()
        {
            Profile custom = Profiles.ById(Profiles.CustomId);
            string[] palette = settings.Deres.CustomPalette;
            string key = palette == null ? "" : string.Join(",", palette);
            if (key == customStripKey) return;
            customStripKey = key;
            foreach (RadioButton card in Cards.Children)
                if ((string)card.Tag == custom.Id && ((StackPanel)card.Content).Children[2] is Border swatch && swatch.Child is Image strip)
                    strip.Source = Strip(Profiles.PaletteBytes(custom, palette));
        }

        string customStripKey;

        static BitmapSource Strip(byte[] colours)
        {
            BitmapSource strip = BitmapSource.Create(colours.Length / 3, 1, 96, 96, PixelFormats.Rgb24, null, colours, colours.Length);
            strip.Freeze();
            return strip;
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
            Kept();
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
        // whether the width box was typed in since it was last filled in (only then is it taken)
        bool widthEdited;

        void OnWidthTyped(object sender, TextChangedEventArgs e)
        {
            if (Quiet || !WidthBox.IsKeyboardFocusWithin) return;
            widthEdited = true;
            WidthLimits(out int min, out int max);
            if (int.TryParse(WidthBox.Text.Trim(), out int w) && w >= min && w <= max && w != settings.Deres.CustomWidth)
            {
                settings.Deres.CustomWidth = w;
                Changed();
            }
        }

        void OnWidthLeft(object sender, KeyboardFocusChangedEventArgs e) => TidyWidth();

        // for the tests: the width box left
        internal void LeaveWidthBox() => TidyWidth();

        // for the tests: a width typed into the box, then Enter
        internal void TypeWidth(string text)
        {
            WidthBox.Text = text;
            widthEdited = true;
            TidyWidth();
        }

        void OnWidthKey(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;
            TidyWidth();
            e.Handled = true;
        }

        // Left, or Enter: what was typed is taken (in range), and the box shows the width there is.
        // Untouched, it only catches up: a look opened meanwhile keeps its own width.
        void TidyWidth()
        {
            bool edited = widthEdited;
            widthEdited = false;
            WidthLimits(out int min, out int max);
            if (edited && double.TryParse(WidthBox.Text.Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double typed) && !double.IsNaN(typed) && !double.IsInfinity(typed))
                settings.Deres.CustomWidth = ClampWidth(typed, min, max);
            quiet = true;
            WidthBox.Text = (PlannedGeometry()?.Width ?? settings.Deres.CustomWidth ?? CurrentProfile.Width).ToString();
            quiet = false;
            if (edited) Changed();
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
                string[] folders = files.Where(Directory.Exists).ToArray();
                if (existing.Length == 0 && folders.Length == 0)
                {
                    Problem(nothing);
                    return;
                }
                // a look file or a palette file sets the look (with a picture beside it, before it opens)
                string look = existing.FirstOrDefault(f => string.Equals(Path.GetExtension(f), ".json", StringComparison.OrdinalIgnoreCase));
                string palette = existing.FirstOrDefault(IsPaletteFile);
                if (look != null) OpenLook(look);
                if (palette != null) LoadPalette(palette);
                string[] pictures = existing.Where(Pictures.LooksLikePicture).ToArray();
                // a folder or several pictures: a batch
                if (folders.Length > 0 || pictures.Length > 1)
                {
                    OpenBatch(folders.Concat(pictures).ToArray());
                    return;
                }
                if (pictures.Length == 0 && (look != null || palette != null)) return;
                OpenFile(pictures.FirstOrDefault() ?? existing[0]);
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
            Paste(data);
        }

        // a copied picture opens; a copied share link sets its look
        internal void Paste(IDataObject data)
        {
            if (!Pictures.CanRead(data) && LinkIn(data) is Dictionary<string, object> link) UseLook(link, "the share link");
            else OpenData(data, NothingCopied);
        }

        const string NothingCopied = "Nothing to open on the clipboard: copy a picture, a picture file or a share link first.";

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
            // kept see-through: a checkerboard behind, as on the website
            View.SetChecker(s.Background == Backgrounds.Transparent && from.Source.HasAlpha);
            View.Show(r.Rgb, r.Width, r.Height, PixelRatio(r), r.Alpha);
            View.SetOriginal(original);
            FxSoon();
            DragThumb.Source = Sharing.Small(r);
            ShowTileFit(r.TileFit);
            ShowStage();
            ShowReadout();
            ShowZoom();
            CommandManager.InvalidateRequerySuggested();
        }

        // the tile meter: how many different 8×8 tiles, against what the machine could hold
        void ShowTileFit(HardwareFitReport report)
        {
            TileFit.Visibility = report != null ? Visibility.Visible : Visibility.Collapsed;
            if (report == null) return;
            TileFitText.Text = report.Summary;
            TileFitText.SetResourceReference(TextBlock.ForegroundProperty, report.TilesFit ? "Good" : "Bad");
            TileFit.SetResourceReference(Border.BorderBrushProperty, report.TilesFit ? "Good" : "Bad");
        }

        // how wide a pixel is shown: its shape on a TV when that's chosen, else square
        double PixelRatio(Rendered r) => settings.TvPixels && !r.Geometry.SquarePixels ? (double)r.Geometry.ParX / r.Geometry.ParY : 1;

        void ShowReadout()
        {
            if (Opening) return;
            string text = shown != null ? Words.Readout(shown) : "";
            if (Rendering && !busyTimer.IsEnabled && shown != null) text += " · working…";
            Readout.Text = text;
            Readout.ToolTip = text.Length > 0 ? text : null;   // (all of it, should a narrow window cut it short)
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
                if (!Pictures.CanRead(data) && LinkIn(data) == null) return;
                e.Handled = true;
                Paste(data);
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

        string FileName(Rendered r, int multiplier) =>
            Export.FileName(Export.BaseName(shownFrom?.Name ?? "photo"), r.Profile.Id, r.Width, r.Height, multiplier, settings.TvPixels && !r.Geometry.SquarePixels,
                Export.WithFx(settings.ExportScreen, multiplier));

        async void OnSavePng(object sender, ExecutedRoutedEventArgs e)
        {
            Rendered r = await Latest();
            if (r == null) return;
            int m = settings.Multiplier;
            bool tv = settings.TvPixels && !r.Geometry.SquarePixels;
            string name = FileName(r, m);
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
            CouldntSave(path, await SavePng(path, r, m, tv, settings.ExportScreen?.Clone()));
        }

        internal bool Saving { get; private set; }

        // a save that went wrong, said in a box (only from a menu or a button: the tests call the
        // save methods themselves, which give back what went wrong instead)
        void CouldntSave(string path, string problem)
        {
            if (problem != null) MessageBox.Show(this, "Couldn't save " + Path.GetFileName(path) + ":\n\n" + problem, Program.Name, MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        // the PNG at `path` (with the screen effect `fx` from 3× up, null for none); what went wrong, or null
        internal async Task<string> SavePng(string path, Rendered r, int m, bool tv, FxSettings fx = null)
        {
            Saving = true;
            Flash("Saving " + Path.GetFileName(path) + "…", "Dim");
            int width = 0, height = 0;
            string problem = null;
            try
            {
                await Task.Run(() => Exports.Write(path, stream => Export.WritePng(stream, r, m, tv, fx, out width, out height)));
            }
            catch (Exception x) when (x is IOException || x is UnauthorizedAccessException || x is NotSupportedException || x is System.Security.SecurityException)
            {
                problem = x.Message;
            }
            catch (OutOfMemoryException)
            {
                problem = "there isn't enough memory free for a picture that big";
            }
            finally
            {
                Saving = false;
            }
            if (problem == null) Flash("Saved " + Path.GetFileName(path) + " (" + width + "×" + height + ").", "Good");
            return problem;
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
            public FxSettings Fx;   // the screen effect, or null
            public string Name;
            public byte[] Png;
            public BitmapSource Picture;

            // the PNG and the picture; safe on any thread
            public void Make(Rendered r)
            {
                Png = Export.PngBytes(r, Multiplier, Tv, Fx);
                Picture = Export.WithFx(Fx, Multiplier) ? Sharing.FromPng(Png) : Sharing.Picture(r, Multiplier, Tv);
            }
        }

        internal Shared ToShare(Rendered r)
        {
            var s = new Shared { Tv = settings.TvPixels && !r.Geometry.SquarePixels, Multiplier = 1, Fx = settings.ExportScreen?.Clone() };
            foreach (int m in Export.Multipliers)
            {
                if (m > settings.Multiplier) break;
                Export.Size(r.Width, r.Height, r.Geometry.ParX, r.Geometry.ParY, m, s.Tv, out int w, out int h);
                if ((long)w * h <= MaxShared) s.Multiplier = m;
            }
            Export.Size(r.Width, r.Height, r.Geometry.ParX, r.Geometry.ParY, s.Multiplier, s.Tv, out s.Width, out s.Height);
            s.Name = FileName(r, s.Multiplier);
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
        Task<Shared> dragReady;   // what a drag hands over, made from the moment the mouse goes down

        void OnDragOutDown(object sender, MouseButtonEventArgs e)
        {
            if (shown == null) return;
            dragStart = e.GetPosition(this);
            dragArmed = true;
            // the picture on screen, as the small picture shows it (a drag can't wait for one being
            // made), made off this thread: with the screen effect a big one takes a moment
            Rendered r = shown;
            Shared share = ToShare(r);
            dragReady = Task.Run(() =>
            {
                share.Make(r);
                return share;
            });
        }

        void OnDragOutMove(object sender, MouseEventArgs e)
        {
            if (!dragArmed) return;
            if (e.LeftButton != MouseButtonState.Pressed || shown == null)
            {
                dragArmed = false;
                DragOut.Cursor = Cursors.Hand;
                return;
            }
            Point now = e.GetPosition(this);
            if (Math.Abs(now.X - dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(now.Y - dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            // not made yet: the drag starts with a move once it is
            if (!dragReady.IsCompleted)
            {
                DragOut.Cursor = Cursors.Wait;
                return;
            }
            dragArmed = false;
            DragOut.Cursor = Cursors.Hand;
            Shared share;
            DataObject data;
            try
            {
                share = dragReady.GetAwaiter().GetResult();
                data = Exports.DataFor(share.Picture, share.Png, share.Name, true);
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

        // ---------- the machine's own file (.scr, .koa, .chr) ----------

        async void OnSaveEmulator(object sender, RoutedEventArgs e)
        {
            Profile p = CurrentProfile;
            string format = p.EmulatorFormats.FirstOrDefault();
            if (format == null || picture == null) return;
            Rendered r = await Latest();
            if (r == null) return;
            string name = Export.EmulatorFileName(Export.BaseName(shownFrom?.Name ?? "photo"), p.Id, format);
            string path;
            try
            {
                path = FileDialogs.Save(this, "Save the picture for an emulator", Words.EmulatorType(format), "." + format, name);
            }
            catch (COMException x)
            {
                MessageBox.Show(this, "Windows' Save dialog didn't open:\n\n" + x.Message, Program.Name, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (path != null) CouldntSave(path, await SaveEmulator(path, format));
        }

        // The machine's own file of the picture as it is now. A screen file (.scr, .koa) can't
        // hold see-through pixels: with them kept it's made as if on black. Tiles (.chr) keep
        // them as colour 0. What went wrong, or null.
        internal async Task<string> SaveEmulator(string path, string format)
        {
            DeresSettings s = settings.Deres.Clone();
            if (s.Background == Backgrounds.Transparent && format != "chr") s.Background = Backgrounds.Black;
            LoadedPicture pic = picture;
            if (pic == null) return "there's no picture";
            Saving = true;
            string problem = null;
            EmulatorFile file = null;
            try
            {
                file = await Task.Run(() => Make(pic, s).Item1.EmulatorFile);
                if (file == null || file.Format != format) problem = "this machine has no ." + format + " file";
                else await Task.Run(() => Exports.Write(path, file.Data));
            }
            catch (Exception x) when (x is IOException || x is UnauthorizedAccessException || x is NotSupportedException || x is System.Security.SecurityException ||
                x is InvalidOperationException || x is ArgumentException)
            {
                problem = x.Message;
            }
            catch (OutOfMemoryException)
            {
                problem = "there isn't enough memory free to make the picture";
            }
            finally
            {
                Saving = false;
            }
            if (problem == null) Flash("Saved " + Path.GetFileName(path) + " (" + file.Data.Length.ToString("N0", System.Globalization.CultureInfo.InvariantCulture) + " bytes).", "Good");
            return problem;
        }

        // ---------- palettes ----------

        void OnLoadPalette(object sender, RoutedEventArgs e)
        {
            string path;
            try
            {
                path = FileDialogs.OpenPalette(this);
            }
            catch (COMException x)
            {
                MessageBox.Show(this, "Windows' Open dialog didn't open:\n\n" + x.Message, Program.Name, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (path != null) LoadPalette(path);
        }

        static bool IsPaletteFile(string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            return ext == ".hex" || ext == ".pal" || ext == ".gpl";
        }

        // A palette file (.hex, .pal, .gpl, or a picture's colours) becomes the Custom machine's.
        internal void LoadPalette(string path)
        {
            string name = Pictures.NameOf(path);
            string[] colours;
            try
            {
                // a .png is a palette picture; anything else is read as palette text, as on the website
                if (string.Equals(Path.GetExtension(path), ".png", StringComparison.OrdinalIgnoreCase)) colours = Pictures.PaletteColours(path);
                else
                {
                    if (new FileInfo(path).Length > 16 << 20) throw new PictureException("it's too big for a palette file");
                    colours = PaletteFile.ParseText(System.Text.Encoding.UTF8.GetString(Pictures.ReadFile(path)));
                }
            }
            catch (PictureException x)
            {
                Problem("Couldn't load the palette " + name + ": " + x.Message + ".");
                return;
            }
            catch (Exception x) when (x is IOException || x is UnauthorizedAccessException || x is ArgumentException || x is NotSupportedException)
            {
                Problem("Couldn't load the palette " + name + ": " + x.Message.TrimEnd('.') + ".");
                return;
            }
            if (colours.Length == 0)
            {
                Problem("No valid colours found in " + name + ".");
                return;
            }
            UsePalette(colours);
            Flash("Loaded palette \"" + name + "\" (" + colours.Length + " colours).", "Good");
        }

        void UsePalette(string[] colours)
        {
            DeresSettings d = settings.Deres;
            d.ProfileId = Profiles.CustomId;
            d.CustomPalette = colours;
            d.CustomCount = colours.Length;
            Changed();
        }

        async void OnGrabPalette(object sender, RoutedEventArgs e)
        {
            string[] colours = await GrabPalette();
            if (colours == null) return;
            // as the website hands over a .hex of it: keep it as a file too, if wanted
            string name = Export.BaseName(picture?.Name ?? "photo") + "-palette.hex";
            string path;
            try
            {
                path = FileDialogs.SaveAs(this, "Keep the grabbed palette as a file", PaletteTypes, 0, name, out int type);
                string problem = path != null ? await SavePalette(path, type, colours) : null;
                CouldntSave(path, problem);
                if (path != null && problem == null)
                {
                    Flash("Grabbed " + colours.Length + " colours and saved " + Path.GetFileName(path) + ".", "Good");
                    return;
                }
            }
            catch (COMException)
            {
                // no dialog: the palette is in use all the same
            }
            Flash("Grabbed " + colours.Length + " colours. Save palette… in the File menu keeps them as a file.", "Good");
        }

        // The picture's own colours (as many as the machine picks, else 16) become the Custom
        // machine's palette: the whole photo's pixels, by the website's median cut.
        internal async Task<string[]> GrabPalette()
        {
            LoadedPicture pic = picture;
            if (pic == null)
            {
                Problem("Open a picture first to grab its palette.");
                return null;
            }
            int count = Profiles.ColourCount(CurrentProfile, settings.Deres.CustomCount) ?? 16;
            byte[] raw = await Task.Run(() => Quantize.Grab(pic.Source, count));
            if (raw.Length == 0) return null;
            string[] colours = Enumerable.Range(0, raw.Length / 3).Select(i => Colour.RgbToHex(raw[i * 3], raw[i * 3 + 1], raw[i * 3 + 2])).ToArray();
            UsePalette(colours);
            return colours;
        }

        static readonly FileDialogs.FileType[] PaletteTypes =
        {
            new FileDialogs.FileType("Palette", ".hex"), new FileDialogs.FileType("JASC palette", ".pal"),
            new FileDialogs.FileType("GIMP palette", ".gpl"), new FileDialogs.FileType("Palette picture", ".png"),
        };

        // the colours of the picture on screen: its machine's palette, or the colours it makes (at most 256)
        string[] ShownPalette()
        {
            if (shown == null) return null;
            byte[] palette = shown.Indexed ? shown.Palette : Census.Palette(shown.Rgb, shown.Alpha);
            return palette == null ? null : Enumerable.Range(0, palette.Length / 3).Select(i => Colour.RgbToHex(palette[i * 3], palette[i * 3 + 1], palette[i * 3 + 2])).ToArray();
        }

        async void OnSavePalette(object sender, RoutedEventArgs e)
        {
            Rendered r = await Latest();
            if (r == null)
            {
                Problem("Open a picture first: the palette is the colours of the picture.");
                return;
            }
            string[] colours = ShownPalette();
            if (colours == null)
            {
                Problem("This picture has more than 256 colours, so there's no palette to save.");
                return;
            }
            string name = Export.BaseName(shownFrom?.Name ?? "photo") + "-" + r.Profile.Id + "-palette.hex";
            string path;
            int type;
            try
            {
                path = FileDialogs.SaveAs(this, "Save the palette", PaletteTypes, 0, name, out type);
            }
            catch (COMException x)
            {
                MessageBox.Show(this, "Windows' Save dialog didn't open:\n\n" + x.Message, Program.Name, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (path == null) return;
            string problem = await SavePalette(path, type, colours);
            CouldntSave(path, problem);
            if (problem == null) Flash("Saved " + Path.GetFileName(path) + " (" + colours.Length + " colours).", "Good");
        }

        // the colours as a .hex, .pal, .gpl or palette PNG (by PaletteTypes); what went wrong, or null
        internal async Task<string> SavePalette(string path, int type, string[] colours)
        {
            try
            {
                await Task.Run(() =>
                {
                    if (type == 3) Exports.Write(path, PaletteFile.Png(colours));
                    else Exports.Write(path, System.Text.Encoding.UTF8.GetBytes(type == 1 ? PaletteFile.Jasc(colours) : type == 2 ? PaletteFile.Gpl(colours) : PaletteFile.Hex(colours)));
                });
                return null;
            }
            catch (Exception x) when (x is IOException || x is UnauthorizedAccessException || x is NotSupportedException || x is System.Security.SecurityException)
            {
                return x.Message;
            }
        }

        // ---------- looks: share links and look files ----------

        void OnCopyLink(object sender, RoutedEventArgs e)
        {
            string link = Looks.Link(settings);
            try
            {
                var data = new DataObject();
                data.SetText(link, TextDataFormat.UnicodeText);
                Exports.KeepOffTheCloud(data);
                Clipboard.SetDataObject(data, true);
                Flash("Share link copied to the clipboard.", "Good");
            }
            catch (ExternalException)
            {
                Problem("Couldn't copy: another program is using the clipboard. Try again.");
            }
        }

        // a share link in copied text, by the website's own reading (null: none)
        static Dictionary<string, object> LinkIn(IDataObject data)
        {
            try
            {
                if (data == null || !data.GetDataPresent(DataFormats.UnicodeText)) return null;
                string text = data.GetData(DataFormats.UnicodeText) as string;
                return text != null && text.Length < 100000 && text.Contains("#s=") ? Looks.FromLink(text) : null;
            }
            catch (Exception x) when (x is ExternalException || x is OutOfMemoryException)
            {
                return null;
            }
        }

        // The look in a share link or a look file, checked as the website checks a link, over the
        // settings there are. Whatever it holds that comes with a later version is said.
        internal void UseLook(Dictionary<string, object> loaded, string what)
        {
            Settings next = Looks.Apply(loaded, settings, out string later);
            settings.Deres = next.Deres;
            settings.Fx = next.Fx;
            settings.TvPixels = next.TvPixels;
            settings.Multiplier = next.Multiplier;
            settings.ExportFx = next.ExportFx;
            Changed();
            Flash(later == null ? "Opened " + what + "." : "Opened " + what + ". " + char.ToUpperInvariant(later[0]) + later.Substring(1) +
                (later.Contains(" and ") ? " come" : " comes") + " with a later version of b0r3d DeRes.", later == null ? "Good" : "Warn");
        }

        void OnOpenLook(object sender, RoutedEventArgs e)
        {
            string path;
            try
            {
                path = FileDialogs.OpenLook(this);
            }
            catch (COMException x)
            {
                MessageBox.Show(this, "Windows' Open dialog didn't open:\n\n" + x.Message, Program.Name, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (path != null) OpenLook(path);
        }

        internal void OpenLook(string path)
        {
            string name = Pictures.NameOf(path);
            Dictionary<string, object> loaded;
            try
            {
                if (new FileInfo(path).Length > 1 << 20) throw new PictureException("it's too big for a look file");
                loaded = Looks.FromFile(System.Text.Encoding.UTF8.GetString(Pictures.ReadFile(path)));
            }
            catch (PictureException x)
            {
                Problem("Couldn't open " + name + ": " + x.Message + ".");
                return;
            }
            catch (Exception x) when (x is IOException || x is UnauthorizedAccessException || x is ArgumentException || x is NotSupportedException)
            {
                Problem("Couldn't open " + name + ": " + x.Message.TrimEnd('.') + ".");
                return;
            }
            if (loaded == null)
            {
                Problem("Couldn't open " + name + ": it isn't a b0r3d DeRes look.");
                return;
            }
            UseLook(loaded, "the look " + name);
        }

        async void OnSaveLook(object sender, RoutedEventArgs e)
        {
            string path;
            try
            {
                path = FileDialogs.Save(this, "Save the look", "Look", ".json", settings.Deres.ProfileId + "-look.json");
            }
            catch (COMException x)
            {
                MessageBox.Show(this, "Windows' Save dialog didn't open:\n\n" + x.Message, Program.Name, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (path != null) CouldntSave(path, await SaveLook(path));
        }

        // the look file at `path`; what went wrong, or null
        internal async Task<string> SaveLook(string path)
        {
            string text = Looks.FileText(settings);
            try
            {
                await Task.Run(() => Exports.Write(path, new System.Text.UTF8Encoding(false).GetBytes(text)));
                Flash("Saved the look as " + Path.GetFileName(path) + ".", "Good");
                return null;
            }
            catch (Exception x) when (x is IOException || x is UnauthorizedAccessException || x is NotSupportedException || x is System.Security.SecurityException)
            {
                return x.Message;
            }
        }

        // ---------- Surprise me and Reset settings ----------

        static readonly Random dice = new Random();

        void OnSurprise(object sender, RoutedEventArgs e) => Surprise(dice);

        // A random machine (not Custom), dither (its own one too), adjustments, and half the time
        // the machine's own screen, as the website's button rolls them.
        internal void Surprise(Random random)
        {
            Profile[] eligible = Profiles.All.Where(x => x.Id != Profiles.CustomId).ToArray();
            Profile p = eligible[random.Next(eligible.Length)];
            var dithers = Dither.All.Where(x => !x.Native).Select(x => x.Id).ToList();
            if (p.NativeDither != null) dithers.Add(p.NativeDither);
            string dither = dithers[random.Next(dithers.Count)];
            DeresSettings d = settings.Deres;
            d.ProfileId = p.Id;
            d.CustomCount = null;
            d.Dither = dither;
            d.Adjust = new Adjust
            {
                Brightness = JsMath.Round((random.NextDouble() * 0.2 - 0.1) * 100) / 100,
                Contrast = JsMath.Round((0.9 + random.NextDouble() * 0.3) * 100) / 100,
                Saturation = JsMath.Round((0.85 + random.NextDouble() * 0.4) * 100) / 100,
                Gamma = d.Adjust.Gamma,
            };
            settings.Fx.Screen = random.NextDouble() > 0.5 ? p.Screen : Screens.Off;
            Changed();
            Flash("Rolled: " + p.Name + ", " + Dither.ById(dither).Name + " dither, " +
                (settings.Fx.Screen == Screens.Off ? "no screen effect." : ScreenName(settings.Fx.Screen) + " screen."), "Dim");
        }

        DispatcherTimer resetTimer;

        // two clicks, as on the website: the first asks, the second (within 4 seconds) resets
        void OnResetButton(object sender, RoutedEventArgs e)
        {
            if (resetTimer == null)
            {
                resetTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
                resetTimer.Tick += (s, a) => CalmReset();
            }
            if (!resetTimer.IsEnabled)
            {
                ResetButton.Content = "_Reset everything?";
                ResetButton.SetResourceReference(ForegroundProperty, "Bad");
                resetTimer.Start();
                return;
            }
            CalmReset();
            ResetSettings();
        }

        void CalmReset()
        {
            resetTimer?.Stop();
            ResetButton.Content = "_Reset settings";
            ResetButton.ClearValue(ForegroundProperty);
        }

        void OnResetMenu(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show(this, "Put every setting back to how b0r3d DeRes starts? The picture stays, and so do light or dark (View → Look) and the window.", Program.Name,
                MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes) ResetSettings();
        }

        // Every setting back to how a first start has them (palettes grabbed or loaded go too); the
        // picture stays, and so do the program's look and the window.
        internal void ResetSettings()
        {
            Settings fresh = Settings.Defaults();
            settings.Deres = fresh.Deres;
            settings.Fx = fresh.Fx;
            settings.TvPixels = fresh.TvPixels;
            settings.Multiplier = fresh.Multiplier;
            settings.ExportFx = fresh.ExportFx;
            Changed();
            saveTimer.Stop();
            SaveSettings();
            Flash("Settings reset.", "Dim");
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
            // a batch under way finishes the pictures it has started (never half a file), then
            // the window closes
            if (batch != null && batch.Busy)
            {
                e.Cancel = true;
                batch.StopThenCloseOwner();
                Flash("Stopping the batch: the pictures under way finish, then b0r3d DeRes closes.", "Warn");
                return;
            }
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
