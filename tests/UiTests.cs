using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using B0r3dDeRes.Engine;
using Geometry = B0r3dDeRes.Engine.Geometry;

namespace B0r3dDeRes.Tests
{
    // The windows, built as the program builds them and laid out without ever being shown.
    static partial class TestMain
    {
        const double MainWidth = 1280, MainHeight = 800;

        static MainWindow NewMain(double scale = 1, bool sample = true, Settings settings = null)
        {
            var window = new MainWindow(settings ?? new Settings(), null, sample) { ScaleOverride = scale };
            LayOut(window.Root, MainWidth, MainHeight);
            return window;
        }

        // measures and arranges a window's content at a size (height: as tall as it needs, if not given)
        static Size LayOut(FrameworkElement root, double width, double height = double.PositiveInfinity)
        {
            root.Measure(new Size(width, height));
            double h = double.IsInfinity(height) ? Math.Ceiling(root.DesiredSize.Height) : height;
            root.Arrange(new Rect(0, 0, width, h));
            root.UpdateLayout();
            return new Size(width, h);
        }

        // waits for the picture being opened and made, then lays the window out with it
        static void WaitForPicture(MainWindow window)
        {
            Pump(() => !window.Opening && !window.Rendering && window.Shown != null, 15000);
            Check(window.Shown != null, "no picture was made within 15 seconds");
            LayOut(window.Root, MainWidth, MainHeight);
        }

        // picks a machine on the Hardware list, as a click does, and waits for its picture
        static void Pick(MainWindow window, string id)
        {
            RadioButton card = window.Cards.Children.OfType<RadioButton>().First(c => (string)c.Tag == id);
            card.IsChecked = true;
            WaitForPicture(window);
            Check(window.Shown.Profile.Id == id, "picking " + id + " made a " + window.Shown.Profile.Id + " picture");
        }

        static void ThemesMatch()
        {
            string[] names = { "Light", "Dark", "HighContrast" };
            List<string>[] keys = names.Select(n =>
                new ResourceDictionary { Source = new Uri("/b0r3dDeRes;component/Themes/" + n + ".xaml", UriKind.Relative) }
                    .Keys.Cast<object>().Select(k => k.ToString()).OrderBy(k => k, StringComparer.Ordinal).ToList()).ToArray();
            for (int i = 1; i < names.Length; i++)
            {
                var missing = keys[0].Except(keys[i]).Concat(keys[i].Except(keys[0])).ToList();
                Check(missing.Count == 0, names[0] + " and " + names[i] + " differ in: " + string.Join(", ", missing));
            }

            // every colour named in the XAML and code is in the colour sets (or, for the few things
            // that aren't colours, like the text boxes' menu, in Controls.xaml)
            var controls = new ResourceDictionary { Source = new Uri("/b0r3dDeRes;component/Themes/Controls.xaml", UriKind.Relative) }
                .Keys.Cast<object>().Select(k => k.ToString()).ToList();
            string src = Path.Combine(RepoFolder, "src");
            if (Directory.Exists(src))
            {
                var used = new HashSet<string>();
                foreach (string file in Directory.EnumerateFiles(src, "*.xaml", SearchOption.AllDirectories))
                    foreach (Match m in Regex.Matches(File.ReadAllText(file), @"\{DynamicResource ([A-Za-z]+)\}")) used.Add(m.Groups[1].Value);
                foreach (string file in Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories))
                {
                    string code = File.ReadAllText(file);
                    foreach (Match m in Regex.Matches(code, @"SetResourceReference\([^,]+,\s*""([A-Za-z]+)""\)")) used.Add(m.Groups[1].Value);
                    foreach (Match m in Regex.Matches(code, @"Flash\([^;]*?""(Good|Bad|Warn|Dim)""\)")) used.Add(m.Groups[1].Value);
                    foreach (Match m in Regex.Matches(code, @"TryFindResource\(""([A-Za-z]+)""\)")) used.Add(m.Groups[1].Value);
                }
                var unknown = used.Where(k => !keys[0].Contains(k) && !controls.Contains(k)).ToList();
                Check(used.Count > 15, "found only " + used.Count + " colour names in src (has the code moved?)");
                Check(unknown.Count == 0, "used but in no colour set: " + string.Join(", ", unknown));
            }

            Theme.Set(Look.Dark);
            bool darkOk = Theme.IsHighContrast || ((SolidColorBrush)Application.Current.Resources["Back"]).Color == Color.FromRgb(0x13, 0x13, 0x13);
            Theme.Set(Look.Light);
            bool lightOk = Theme.IsHighContrast || ((SolidColorBrush)Application.Current.Resources["Back"]).Color == Colors.White;
            Check(darkOk && lightOk, "switching the look didn't swap the colours");
        }

        sealed class Collect : TraceListener
        {
            public readonly List<string> Lines = new List<string>();
            public override void Write(string message) { }
            public override void WriteLine(string message) => Lines.Add(message);
        }

        static void WindowWithoutBindingErrors()
        {
            var errors = new Collect();
            PresentationTraceSources.Refresh();
            PresentationTraceSources.DataBindingSource.Listeners.Add(errors);
            PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Warning;
            try
            {
                foreach (Look look in new[] { Look.Light, Look.Dark })
                {
                    Theme.Set(look);
                    MainWindow window = NewMain();
                    WaitForPicture(window);
                    window.ExportSection.IsExpanded = window.AdjustSection.IsExpanded = window.HardwareSection.IsExpanded = window.DitherSection.IsExpanded = true;
                    LayOut(window.Root, MainWidth, MainHeight);
                    var about = new AboutWindow();
                    LayOut(about.Root, 640);
                }
            }
            finally
            {
                PresentationTraceSources.DataBindingSource.Listeners.Remove(errors);
                Theme.Set(Look.Light);
            }
            Check(errors.Lines.Count == 0, errors.Lines.Count + " binding errors, first: " + errors.Lines.FirstOrDefault());
        }

        static IEnumerable<DependencyObject> LogicalDescendants(DependencyObject parent)
        {
            foreach (object child in LogicalTreeHelper.GetChildren(parent))
            {
                if (!(child is DependencyObject element)) continue;
                yield return element;
                foreach (DependencyObject below in LogicalDescendants(element)) yield return below;
            }
        }

        // the window starts as the website does: the Ruckus sample on the Game Boy, its lines filled in
        static void WindowStartsWithTheSample()
        {
            MainWindow window = NewMain();
            WaitForPicture(window);
            Rendered r = window.Shown;
            Check(r.Profile.Id == "gameboy" && r.Width == 160 && r.Height == 144, "it started with " + r.Profile.Id + " at " + r.Width + "×" + r.Height);
            Check(window.PictureInfo.Text == "rukus.png · 256×256", "the picture line: " + window.PictureInfo.Text);
            // (the tile meter's numbers are the website's: the pictures test checks them all)
            Check(window.Readout.Text == r.Used + " of 4 colours used · 160×144 · square pixels · " + r.TileFit.Summary &&
                System.Text.RegularExpressions.Regex.IsMatch(r.TileFit.Summary, @"^\d+ / 256 tiles \(fits in VRAM\)$"), "the readout: " + window.Readout.Text);
            Check(window.TileFit.Visibility == Visibility.Visible && window.TileFitText.Text == r.TileFit.Summary, "the tile meter should show under the machines");
            Check(window.ResolutionInfo.Text == "Game Boy: 160×144 pixels. Each pixel averages 1.6 × 1.6 of your photo’s pixels.", "the resolution line: " + window.ResolutionInfo.Text);
            Check(window.SizeLine.Text == "640×576 px · 4-colour palette", "the size line: " + window.SizeLine.Text);
            Check(window.StageNote.Visibility != Visibility.Visible, "the stage still shows a note: " + window.StageNote.Text);
            Check(window.BackgroundPanel.Visibility == Visibility.Visible, "Ruckus has see-through parts, so Transparent areas should show");
            Check(window.Cards.Children.OfType<RadioButton>().Count(c => c.IsChecked == true) == 1, "one machine should be picked");
            Check(window.Dithers.Children.OfType<RadioButton>().Single(d => d.IsChecked == true).Tag as string == "bayer4", "the Game Boy starts on Bayer 4×4");
            Pump(() => window.SaveButton.IsEnabled, 2000);
            Check(window.SaveButton.IsEnabled && window.CopyButton.IsEnabled, "Save and Copy aren't on with a picture");

            MainWindow empty = NewMain(1, false);
            Pump(() => false, 100);
            Check(empty.StageNote.Visibility == Visibility.Visible && empty.StageNote.Text == "Choose a picture to start.", "with no picture: " + empty.StageNote.Text);
            Check(!empty.SaveButton.IsEnabled && !empty.CopyButton.IsEnabled, "Save and Copy are on with nothing to save");
            Check(empty.PictureInfo.Text == "No picture yet", "the picture line with none: " + empty.PictureInfo.Text);
        }
    }
}
