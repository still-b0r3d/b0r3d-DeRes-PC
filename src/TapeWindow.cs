using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using B0r3dDeRes.Engine;

namespace B0r3dDeRes
{
    // Every one of the Signal engine's settings (ntsc-rs's), as the tape site was to have them: each
    // shows what it is for the picture on screen over the connection picked, and what's changed
    // here stays on top of the connection's own until Reset all (Wear only moves the noises not
    // changed here). ntsc-rs settings files load and save here: one loaded changes what it has
    // otherwise than the connection, but for the scale, which b0r3d DeRes fits to each picture.
    sealed class TapeWindow : Window
    {
        readonly Settings settings;
        readonly Func<NtscSettings> effective;    // the engine's settings now
        readonly Action changed;                  // the Signal changed
        readonly Func<string> describe;           // "the NES over RF"
        readonly StackPanel list = new StackPanel();
        readonly TextBlock heading = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) };
        readonly Dictionary<string, Row> rows = new Dictionary<string, Row>();
        bool quiet;

        sealed class Row
        {
            public NtscSetting Setting;
            public FrameworkElement Line, Control;
            public TextBlock Label, Value;
        }

        const int Steps = 1000;

        public TapeWindow(Window owner, Settings settings, Func<NtscSettings> effective, Action changed, Func<string> describe)
        {
            this.settings = settings;
            this.effective = effective;
            this.changed = changed;
            this.describe = describe;
            // (a window never shown, as in the tests, can't own one)
            if (owner != null && owner.IsLoaded) Owner = owner;
            Icon = owner?.Icon;
            Title = "Every Signal setting · b0r3d DeRes";
            Width = 560;
            Height = 720;
            MinWidth = 420;
            MinHeight = 400;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;
            FontFamily = new FontFamily("Segoe UI");
            FontSize = 13;
            UseLayoutRounding = true;
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
            SetResourceReference(BackgroundProperty, "Back");
            SetResourceReference(ForegroundProperty, "Text");
            SourceInitialized += (s, e) => Theme.TitleBar(this);

            var root = new DockPanel();
            var buttons = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
            DockPanel.SetDock(buttons, Dock.Bottom);
            Button B(string text, string tip, RoutedEventHandler click)
            {
                var b = new Button { Content = text, ToolTip = tip, Margin = new Thickness(0, 0, 8, 6) };
                b.Click += click;
                buttons.Children.Add(b);
                return b;
            }
            B("_Reset all", "Every setting back to what the connection makes", (s, e) => ResetAll());
            B("_Load ntsc-rs settings…", "An ntsc-rs settings file (or preset): every setting in it", (s, e) => Load());
            B("_Save ntsc-rs settings…", "These settings as an ntsc-rs settings file", (s, e) => Save());
            Button close = B("Close", null, (s, e) => Close());
            close.IsCancel = true;
            root.Children.Add(buttons);
            DockPanel.SetDock(heading, Dock.Top);
            root.Children.Add(heading);
            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = list, Focusable = false };
            root.Children.Add(scroll);
            // (the colour behind it all, also in a picture of it)
            var back = new Border { Padding = new Thickness(18), Child = root };
            back.SetResourceReference(Border.BackgroundProperty, "Back");
            Content = back;
            Build();
            Refresh();
        }

        void Build()
        {
            foreach (NtscSetting s in NtscSettings.All)
            {
                var row = new Row { Setting = s, Label = new TextBlock { Text = s.Label, VerticalAlignment = VerticalAlignment.Center }, Value = new TextBlock { MinWidth = 64, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center, FontFamily = new FontFamily("Consolas") } };
                string tip = s.Tip + " (ntsc-rs: " + s.Key + ")";
                var line = new DockPanel { Margin = new Thickness(20 * s.Depth, s.Group == null ? 10 : 4, 0, 0), ToolTip = tip };
                switch (s.Kind)
                {
                    case SettingKind.Bool:
                    case SettingKind.Group:
                        {
                            var box = new CheckBox { Content = row.Label, FontWeight = s.Kind == SettingKind.Group ? FontWeights.SemiBold : FontWeights.Normal };
                            AutomationProperties.SetName(box, s.Label);
                            box.Checked += (o, e) => Change(s, true);
                            box.Unchecked += (o, e) => Change(s, false);
                            row.Control = box;
                            line.Children.Add(box);
                            break;
                        }
                    case SettingKind.Choice:
                        {
                            var combo = new ComboBox { MinWidth = 170 };
                            foreach (var c in s.Choices) combo.Items.Add(new ComboBoxItem { Content = c.Label, Tag = c.Value });
                            AutomationProperties.SetName(combo, s.Label);
                            combo.SelectionChanged += (o, e) =>
                            {
                                if (combo.SelectedItem is ComboBoxItem item) Change(s, (double)(int)item.Tag);
                            };
                            DockPanel.SetDock(combo, Dock.Right);
                            row.Control = combo;
                            line.Children.Add(combo);
                            line.Children.Add(row.Label);
                            break;
                        }
                    default:
                        {
                            var top = new DockPanel();
                            DockPanel.SetDock(row.Value, Dock.Right);
                            // (the seed shows in its box)
                            if (s.Key != "random_seed") top.Children.Add(row.Value);
                            top.Children.Add(row.Label);
                            var stack = new StackPanel();
                            stack.Children.Add(top);
                            if (s.Key == "random_seed")
                            {
                                var box = new TextBox { Width = 140, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 0) };
                                AutomationProperties.SetName(box, s.Label);
                                box.LostKeyboardFocus += (o, e) =>
                                {
                                    // a seed typed in, unless it's the one there already
                                    double seed = long.TryParse(box.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out long n) ? Math.Max(int.MinValue, Math.Min(int.MaxValue, n)) : double.NaN;
                                    if (!double.IsNaN(seed) && seed != (double)effective().Get(s.Key)) Change(s, seed);
                                    else Refresh(true);
                                };
                                box.KeyDown += (o, e) =>
                                {
                                    if (e.Key == System.Windows.Input.Key.Enter) System.Windows.Input.Keyboard.ClearFocus();
                                };
                                row.Control = box;
                                stack.Children.Add(box);
                            }
                            else
                            {
                                bool whole = s.Kind == SettingKind.Int;
                                var slider = new Slider { Minimum = whole ? s.Min : 0, Maximum = whole ? s.Max : Steps, SmallChange = 1, LargeChange = whole ? 1 : Steps / 20, IsSnapToTickEnabled = whole, TickFrequency = 1, Margin = new Thickness(0, 2, 0, 0) };
                                AutomationProperties.SetName(slider, s.Label);
                                slider.ValueChanged += (o, e) =>
                                {
                                    if (!quiet) Change(s, whole ? Math.Round(slider.Value) : FromSlider(s, slider.Value));
                                };
                                row.Control = slider;
                                stack.Children.Add(slider);
                            }
                            line.Children.Add(stack);
                            break;
                        }
                }
                row.Line = line;
                rows[s.Key] = row;
                list.Children.Add(line);
            }
        }

        // a slider's place (0 … 1000) as the setting's value, and back: in even steps, or by
        // ratio for the settings that go from a whisper to a lot (three decades, from 0)
        static double FromSlider(NtscSetting s, double position)
        {
            double t = position / Steps;
            double v = s.Logarithmic && s.Min >= 0 ? s.Min + (s.Max - s.Min) * (Math.Pow(1000, t) - 1) / 999 : s.Min + (s.Max - s.Min) * t;
            // four figures are plenty
            return v == 0 ? 0 : Math.Round(v, Math.Max(0, Math.Min(15, 3 - (int)Math.Floor(Math.Log10(Math.Abs(v))))));
        }

        static double ToSlider(NtscSetting s, double value)
        {
            double t = s.Max > s.Min ? (value - s.Min) / (s.Max - s.Min) : 0;
            if (s.Logarithmic && s.Min >= 0) t = Math.Log(1 + Math.Max(0, t) * 999) / Math.Log(1000);
            return Math.Max(0, Math.Min(1, t)) * Steps;
        }

        // a setting's value in words: a whole number, or a float in the fewest digits that are it
        // (a small one isn't rounded away to 0), as a percentage where the setting is one
        static string Shown(NtscSetting s, object v)
        {
            if (!(v is double d)) return "";
            if (s.Kind == SettingKind.Int) return ((long)d).ToString(CultureInfo.InvariantCulture);
            if (s.Kind == SettingKind.Percent) return NtscSettings.F32Text((float)(d * 100)) + " %";
            return NtscSettings.F32Text((float)d);
        }

        void Change(NtscSetting s, object value)
        {
            if (quiet) return;
            settings.Signal.Engine[s.Key] = value;
            changed();
            Refresh();
        }

        // Every row from the settings as they are now: its value, its control, and a dot when it's
        // changed here. A box being typed in keeps its text, unless `boxesToo` (Reset all, a file
        // loaded, the box let go of).
        internal void Refresh(bool boxesToo = false)
        {
            NtscSettings now = effective();
            heading.Text = "The Signal engine's settings for " + describe() + ". What you change here stays on top of the connection's own, marked •, until Reset all; Wear only moves the noises you haven't changed here.";
            quiet = true;
            try
            {
                foreach (Row row in rows.Values)
                {
                    NtscSetting s = row.Setting;
                    object v = now.Get(s.Key);
                    bool mine = settings.Signal.Engine.ContainsKey(s.Key);
                    row.Label.Text = (mine ? "• " : "") + s.Label;
                    row.Label.FontWeight = mine ? FontWeights.Bold : s.Kind == SettingKind.Group ? FontWeights.SemiBold : FontWeights.Normal;
                    row.Value.Text = Shown(s, v);
                    switch (row.Control)
                    {
                        case CheckBox box: box.IsChecked = v is bool b && b; break;
                        case ComboBox combo: combo.SelectedItem = combo.Items.OfType<ComboBoxItem>().FirstOrDefault(i => v is double d && (int)i.Tag == (int)d); break;
                        case TextBox text: if (boxesToo || !text.IsKeyboardFocusWithin) text.Text = Shown(s, v); break;
                        case Slider slider: slider.Value = s.Kind == SettingKind.Int ? (double)v : ToSlider(s, (double)v); break;
                    }
                    // what's under a switch that's off has no say
                    bool on = true;
                    for (NtscSetting g = s.Group; g != null; g = g.Group) on &= now.Get(g.Key) is bool gb && gb;
                    row.Line.IsEnabled = on;
                    row.Line.Opacity = on ? 1 : 0.5;
                }
            }
            finally
            {
                quiet = false;
            }
        }

        internal void ResetAll()
        {
            settings.Signal.Engine.Clear();
            changed();
            Refresh(true);
        }

        // the settings now as an ntsc-rs settings file
        internal string FileText => effective().ToFile();

        // for the tests: the rows, their controls and words
        internal int RowCount => rows.Count;
        internal string HeadingText => heading.Text;
        internal FrameworkElement ControlOf(string key) => rows[key].Control;
        internal string LabelOf(string key) => rows[key].Label.Text;
        internal string ValueOf(string key) => rows[key].Value.Text;
        internal bool RowOn(string key) => rows[key].Line.IsEnabled;

        // the settings that fit the engine to its picture's size and samples, which b0r3d DeRes sets
        // for each picture itself (a file's were for the video it was made on)
        static readonly string[] scaling = { "scale_settings", "bandwidth_scale", "vertical_scale", "scale_with_video_size" };

        // an ntsc-rs settings file: what it has that the connection doesn't make already
        void Load()
        {
            string path;
            try
            {
                path = FileDialogs.OpenEngineSettings(this);
            }
            catch (COMException x)
            {
                MessageBox.Show(this, "Windows' Open dialog didn't open:\n\n" + x.Message, Program.Name, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (path == null) return;
            string problem = LoadFile(path);
            if (problem != null) MessageBox.Show(this, "Couldn't load " + Path.GetFileName(path) + ": " + problem + ".", Program.Name, MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        // (for the tests too) what went wrong, or null
        internal string LoadFile(string path)
        {
            NtscSettings loaded;
            try
            {
                loaded = NtscSettings.FromFile(File.ReadAllText(path));
            }
            catch (FormatException x)
            {
                return x.Message;
            }
            catch (Exception x) when (x is IOException || x is UnauthorizedAccessException)
            {
                return x.Message.TrimEnd('.');
            }
            // in place of what was changed here: each setting the file has otherwise than the
            // connection makes it (so Wear, Sharpen and the decoder still move the rest), but for
            // the scale
            settings.Signal.Engine.Clear();
            foreach (string key in loaded.Differences(effective()))
                if (Array.IndexOf(scaling, key) < 0) settings.Signal.Engine[key] = Tidy(NtscSettings.Find(key), loaded.Get(key));
            changed();
            Refresh(true);
            return null;
        }

        // a float setting kept as its shortest digits (0.1, not 0.100000001490116) where they read back as the same float
        static object Tidy(NtscSetting s, object v)
        {
            if (!(v is double d) || s.Kind != SettingKind.Float && s.Kind != SettingKind.Percent) return v;
            return double.TryParse(NtscSettings.F32Text((float)d), NumberStyles.Float, CultureInfo.InvariantCulture, out double shortest) && Signal.JsonFloat(shortest) == (float)d ? shortest : d;
        }

        void Save()
        {
            string path;
            try
            {
                path = FileDialogs.Save(this, "Save ntsc-rs settings", "ntsc-rs settings", ".json", "deres-signal.json");
            }
            catch (COMException x)
            {
                MessageBox.Show(this, "Windows' Save dialog didn't open:\n\n" + x.Message, Program.Name, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (path == null) return;
            try
            {
                Exports.Write(path, System.Text.Encoding.UTF8.GetBytes(FileText));
            }
            catch (Exception x) when (x is IOException || x is UnauthorizedAccessException)
            {
                MessageBox.Show(this, "Couldn't save it: " + x.Message, Program.Name, MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }
}
