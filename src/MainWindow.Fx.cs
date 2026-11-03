using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using B0r3dDeRes.Engine;

namespace B0r3dDeRes
{
    // The screen effect: the Screen section, the preview's, and the wallpapers (which keep it from
    // 3× up, as PNGs do).
    public partial class MainWindow
    {
        // the Screen section's buttons, in the website's order and words
        static readonly (string Id, string Text, string Name)[] screenChoices =
        {
            (Screens.Off, "Off", "No screen effect"), (Screens.Crt, "CRT", "CRT"), (Screens.Lcd, "LCD", "LCD"), (Screens.Vhs, "VHS", "VHS"),
            (Screens.Composite, "Composite", "Composite"), (Screens.P1Green, "P1 Green", "P1 green phosphor"), (Screens.P3Amber, "P3 Amber", "P3 amber phosphor"),
            (Screens.P4White, "P4 White", "P4 white phosphor"),
        };

        // a screen's name in a sentence ("the CRT screen")
        internal static string ScreenName(string id) => screenChoices.FirstOrDefault(c => c.Id == id).Text ?? id;

        void BuildScreens()
        {
            foreach (var choice in screenChoices)
            {
                var button = new RadioButton { Style = (Style)FindResource("Segment"), GroupName = "Screen", Tag = choice.Id, Content = choice.Text, Padding = new Thickness(0), Margin = new Thickness(0, 0, 6, 6) };
                AutomationProperties.SetName(button, choice.Name);
                button.Checked += OnScreen;
                ScreenButtons.Children.Add(button);
            }
            BuildWallpapers();
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged += OnDisplaysChanged;
            Closed += (s, e) => Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= OnDisplaysChanged;
        }

        // the screens came and went (a TV turned on): their wallpapers follow
        void OnDisplaysChanged(object sender, EventArgs e) => Dispatcher.BeginInvoke(new Action(() =>
        {
            BuildWallpapers();
            Sync();
        }));

        // the website's three, then each of the PC's screens at its real size
        internal List<Wallpaper> WallpaperChoices { get; private set; } = new List<Wallpaper>();

        void BuildWallpapers()
        {
            var choices = new List<Wallpaper>(Export.Wallpapers);
            List<Monitors.Monitor> monitors;
            try
            {
                monitors = Monitors.All();
            }
            catch (Exception x) when (x is DllNotFoundException || x is EntryPointNotFoundException || x is ExternalException)
            {
                monitors = new List<Monitors.Monitor>();
            }
            foreach (Monitors.Monitor m in monitors)
                choices.Add(new Wallpaper { Id = m.Bounds.Width + "x" + m.Bounds.Height, Name = monitors.Count > 1 ? "Screen " + m.Number : "This screen", Width = m.Bounds.Width, Height = m.Bounds.Height });
            WallpaperChoices = choices;

            WallpaperButtons.Children.Clear();
            WallpaperMenu.Items.Clear();
            foreach (Wallpaper w in choices)
            {
                string size = w.Width + "×" + w.Height;
                var button = new Button { Content = w.Name + " · " + size, Tag = w, Margin = new Thickness(0, 0, 6, 6), Padding = new Thickness(6, 0, 6, 0), ToolTip = "Save a " + size + " wallpaper" };
                AutomationProperties.SetName(button, w.Name + " wallpaper, " + size);
                button.Click += OnWallpaper;
                WallpaperButtons.Children.Add(button);
                // (access keys: P, B, F, T, and a screen's number)
                string header = w.Name.StartsWith("Screen ", StringComparison.Ordinal) ? "Screen _" + w.Name.Substring(7) : "_" + w.Name;
                var item = new MenuItem { Header = header + " (" + size + ")…", Tag = w };
                item.Click += OnWallpaper;
                WallpaperMenu.Items.Add(item);
            }
        }

        // the Screen section and the export's box, from the settings (inside Sync)
        void SyncScreen()
        {
            FxSettings fx = settings.Fx;
            foreach (RadioButton button in ScreenButtons.Children) button.IsChecked = (string)button.Tag == fx.Screen;
            bool phosphor = fx.Screen == Screens.P1Green || fx.Screen == Screens.P3Amber || fx.Screen == Screens.P4White;
            bool crt = fx.Screen == Screens.Crt || fx.Screen == Screens.Vhs || fx.Screen == Screens.Composite || phosphor;
            CrtPanel.Visibility = crt ? Visibility.Visible : Visibility.Collapsed;
            MaskPanel.Visibility = fx.Screen == Screens.Crt ? Visibility.Visible : Visibility.Collapsed;
            BloomPanel.Visibility = fx.Screen == Screens.Crt || phosphor ? Visibility.Visible : Visibility.Collapsed;
            VhsPanel.Visibility = fx.Screen == Screens.Vhs ? Visibility.Visible : Visibility.Collapsed;
            LcdPanel.Visibility = fx.Screen == Screens.Lcd ? Visibility.Visible : Visibility.Collapsed;
            MaskNone.IsChecked = fx.Mask == "none";
            MaskShadow.IsChecked = fx.Mask == "shadow";
            MaskGrille.IsChecked = fx.Mask == "grille";
            void Show(Slider slider, TextBlock value, double v)
            {
                slider.Value = JsMath.Round(v * 100);
                value.Text = Words.Percent(v);
            }
            Show(ScanlinesSlider, ScanlinesValue, fx.Scanlines);
            Show(CurveSlider, CurveValue, fx.Curve);
            Show(BloomSlider, BloomValue, fx.Bloom);
            Show(TrackingSlider, TrackingValue, fx.VhsTracking);
            Show(GridSlider, GridValue, fx.Grid);
            Show(GhostSlider, GhostValue, fx.Ghost);
            OsdBox.IsChecked = fx.VhsOsd;

            ExportFxBox.IsChecked = settings.ExportFx;
            ExportFxBox.IsEnabled = fx.Screen != Screens.Off && settings.Multiplier >= Export.FxMinMultiplier;
            foreach (Button button in WallpaperButtons.Children) button.IsEnabled = picture != null;
            WallpaperMenu.IsEnabled = picture != null;
        }

        // the screen effect changed: the controls follow, the preview's is made again (the picture
        // under it stays as it is), and the settings are kept a moment later
        void FxChanged()
        {
            Sync();
            FxSoon();
            saveTimer.Stop();
            saveTimer.Start();
        }

        void OnScreen(object sender, RoutedEventArgs e)
        {
            if (Quiet) return;
            settings.Fx.Screen = (string)((RadioButton)sender).Tag;
            FxChanged();
        }

        void OnMask(object sender, RoutedEventArgs e)
        {
            if (Quiet) return;
            settings.Fx.Mask = (string)((RadioButton)sender).Tag;
            FxChanged();
        }

        void OnFxSlider(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (Quiet) return;
            // whole hundredths, as the website's 0.01 steps (0.05 for the tracking)
            FxSettings fx = settings.Fx;
            double v = JsMath.Round(((Slider)sender).Value) / 100.0;
            if (sender == ScanlinesSlider) fx.Scanlines = v;
            else if (sender == CurveSlider) fx.Curve = v;
            else if (sender == BloomSlider) fx.Bloom = v;
            else if (sender == TrackingSlider) fx.VhsTracking = v;
            else if (sender == GridSlider) fx.Grid = v;
            else if (sender == GhostSlider) fx.Ghost = v;
            FxChanged();
        }

        void OnOsd(object sender, RoutedEventArgs e)
        {
            if (Quiet) return;
            settings.Fx.VhsOsd = OsdBox.IsChecked == true;
            FxChanged();
        }

        void OnExportFx(object sender, RoutedEventArgs e)
        {
            if (Quiet) return;
            settings.ExportFx = ExportFxBox.IsChecked == true;
            Kept();
        }

        // ---- the preview's screen effect ----

        // Made off the window's thread at the zoom shown (whole screen pixels a machine pixel, at
        // least 3 so a scanline has room), on every core; the latest wanted is made next, and the
        // last one made stays on screen until then.
        const int MaxScreenScale = 64;
        const long MaxScreenPixels = 4096L * 4096;

        sealed class ScreenJob
        {
            public Rendered Picture;
            public FxSettings Fx;
            public int Width, Height;

            public bool Same(ScreenJob o) => o != null && o.Picture == Picture && o.Fx.SameAs(Fx) && o.Width == Width && o.Height == Height;
        }

        ScreenJob screenWanted, screenShown;
        bool screenRendering;

        // for the tests: a screen effect is being made, or waits to be
        internal bool ScreenRendering => screenRendering || screenWanted != null;

        // whole screen pixels a machine pixel for the preview's screen effect, at `zoom`
        internal static int ScreenScale(int width, int height, double ratio, double zoom)
        {
            int s = Math.Max(Export.FxMinMultiplier, Math.Min(MaxScreenScale, (int)Math.Round(zoom)));
            while (s > Export.FxMinMultiplier && width * s * ratio * height * s > MaxScreenPixels) s--;
            return s;
        }

        void FxSoon()
        {
            Rendered r = shown;
            if (r == null || settings.Fx.Screen == Screens.Off)
            {
                screenWanted = screenShown = null;
                View.ShowScreen(null, 0, 0);
                return;
            }
            double ratio = PixelRatio(r);
            bool tv = settings.TvPixels && !r.Geometry.SquarePixels;
            int scale = ScreenScale(r.Width, r.Height, ratio, View.ZoomFor(r.Width, r.Height, ratio));
            Export.Size(r.Width, r.Height, r.Geometry.ParX, r.Geometry.ParY, scale, tv, out int w, out int h);
            var job = new ScreenJob { Picture = r, Fx = settings.Fx.Clone(), Width = w, Height = h };
            if (job.Same(screenShown) && !screenRendering || job.Same(screenWanted)) return;
            screenWanted = job;
            if (!screenRendering) ScreenLoop();
        }

        async void ScreenLoop()
        {
            screenRendering = true;
            try
            {
                while (screenWanted != null)
                {
                    ScreenJob job = screenWanted;
                    screenWanted = null;
                    byte[] rgba;
                    try
                    {
                        Rendered r = job.Picture;
                        rgba = await Task.Run(() => new FxRenderer(r.Rgb, r.Width, r.Height, r.Paper, r.Alpha, job.Width, job.Height, job.Fx).Rgba());
                    }
                    catch (Exception x)
                    {
                        // no screen effect then: the picture itself shows, and the bottom line says why
                        if (job.Picture == shown)
                        {
                            screenShown = null;
                            View.ShowScreen(null, 0, 0);
                        }
                        if (screenWanted == null) Problem(x is OutOfMemoryException ? "There isn't enough memory free to show the screen effect." : "Couldn't show the screen effect: " + x.Message);
                        continue;
                    }
                    // a newer picture or the screen turned off meanwhile: this one's no longer wanted
                    if (job.Picture != shown || settings.Fx.Screen == Screens.Off) continue;
                    screenShown = job;
                    View.ShowScreen(rgba, job.Width, job.Height);
                }
            }
            finally
            {
                screenRendering = false;
            }
        }

        // ---- wallpapers ----

        async void OnWallpaper(object sender, RoutedEventArgs e)
        {
            var w = (Wallpaper)((FrameworkElement)sender).Tag;
            Rendered r = await Latest();
            if (r == null) return;
            string path;
            try
            {
                path = FileDialogs.Save(this, "Save the " + w.Name + " wallpaper (" + w.Width + "×" + w.Height + ")", "PNG picture", ".png", WallpaperName(r, w));
            }
            catch (COMException x)
            {
                MessageBox.Show(this, "Windows' Save dialog didn't open:\n\n" + x.Message, Program.Name, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (path != null) CouldntSave(path, await SaveWallpaper(path, r, w));
        }

        internal string WallpaperName(Rendered r, Wallpaper w) => Export.WallpaperName(Export.BaseName(shownFrom?.Name ?? "photo"), r.Profile.Id, w.Id);

        // the wallpaper at `path`; what went wrong, or null
        internal async Task<string> SaveWallpaper(string path, Rendered r, Wallpaper w)
        {
            Saving = true;
            Flash("Saving " + Path.GetFileName(path) + "…", "Dim");
            bool tv = settings.TvPixels && !r.Geometry.SquarePixels;
            FxSettings fx = settings.ExportScreen?.Clone();
            string problem = null;
            try
            {
                await Task.Run(() => Exports.Write(path, stream => Export.WriteWallpaper(stream, r, w.Width, w.Height, tv, fx)));
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
            if (problem == null) Flash("Saved " + Path.GetFileName(path) + " (" + w.Width + "×" + w.Height + ").", "Good");
            return problem;
        }
    }
}
