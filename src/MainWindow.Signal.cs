using System;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using B0r3dDeRes.Engine;

namespace B0r3dDeRes
{
    // The Signal: its section, and the signal made from the picture on screen, once, for the
    // preview, saves, copies and wallpapers alike.
    public partial class MainWindow
    {
        // the Signal section's buttons, in the website's order and words (their spoken names start
        // with the words on them, so Voice Access finds them)
        static readonly (string Id, string Text, string Name)[] signalChoices =
        {
            (SignalModes.Off, "Off", "Off, no signal"), (SignalModes.Auto, "Machine's own", "Machine's own connection"), (SignalModes.Rf, "RF", "RF, the aerial socket"),
            (SignalModes.Composite, "Composite", "Composite video"), (SignalModes.VhsSp, "VHS SP", "VHS SP tape"), (SignalModes.VhsLp, "VHS LP", "VHS LP tape"),
            (SignalModes.VhsEp, "VHS EP", "VHS EP tape"),
        };

        void BuildSignals()
        {
            // Off, Machine's own (two columns), RF; then Composite and the three tape speeds
            int[] row = { 0, 0, 0, 1, 1, 1, 1 }, column = { 0, 1, 3, 0, 1, 2, 3 };
            for (int i = 0; i < signalChoices.Length; i++)
            {
                var choice = signalChoices[i];
                var button = new RadioButton { Style = (Style)FindResource("Segment"), GroupName = "Signal", Tag = choice.Id, Content = choice.Text, Padding = new Thickness(0), Margin = new Thickness(0, 0, 6, 6) };
                AutomationProperties.SetName(button, choice.Name);
                button.Checked += OnSignalMode;
                Grid.SetRow(button, row[i]);
                Grid.SetColumn(button, column[i]);
                if (i == 1) Grid.SetColumnSpan(button, 2);
                SignalButtons.Children.Add(button);
            }
        }

        // the connection the signal goes over now ("rf"…), or null when it's off
        string SignalConnection(Rendered r) => r != null ? Signal.Connection(settings.Signal.Mode, Signal.Machine(r.Profile.Id, r.Width)) : null;

        // the Signal section, from the settings (inside Sync)
        void SyncSignal(Profile p, Geometry g)
        {
            SignalSettings s = settings.Signal;
            foreach (RadioButton button in SignalButtons.Children) button.IsChecked = (string)button.Tag == s.Mode;
            SignalPanel.Visibility = s.Mode != SignalModes.Off ? Visibility.Visible : Visibility.Collapsed;
            WearSlider.Value = JsMath.Round(s.Wear * 100);
            WearValue.Text = Words.Percent(s.Wear);
            SignalColourSlider.Value = JsMath.Round(s.Colour * 100);
            SignalColourValue.Text = Words.Percent(s.Colour);
            SharpenSlider.Value = JsMath.Round(s.Sharpen * 100);
            SharpenValue.Text = Words.Percent(s.Sharpen);
            DecoderNotch.IsChecked = s.Decoder == SignalModes.Notch;
            DecoderComb.IsChecked = s.Decoder == SignalModes.Comb;
            SignalMachine machine = Signal.Machine(p.Id, g?.Width ?? p.Width);
            SignalNote.Text = Words.SignalNote(p, machine, Signal.Connection(s.Mode, machine));
        }

        // The signal changed: the controls follow and the preview's signal is made again. On a
        // machine whose colours come from the signal (CGA composite) the picture is made again
        // too when those colours change, dithered to the new ones; Wear changes no colour, so it
        // only moves the signal, as on the website.
        void SignalChanged()
        {
            Sync();
            if (picture != null && !player.Playing && !SameColours(Signal.ForRender(settings.Deres, settings.SignalOn).FixedPalette, wantedColours)) RenderSoon();
            else FxSoon();
            saveTimer.Stop();
            saveTimer.Start();
        }

        static bool SameColours(string[] a, string[] b) => a == null ? b == null : b != null && a.SequenceEqual(b);

        void OnSignalMode(object sender, RoutedEventArgs e)
        {
            if (Quiet) return;
            settings.Signal.Mode = (string)((RadioButton)sender).Tag;
            SignalChanged();
        }

        void OnSignalSlider(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (Quiet) return;
            // whole hundredths, in the website's 0.05 steps
            double v = JsMath.Round(((Slider)sender).Value) / 100.0;
            if (sender == WearSlider) settings.Signal.Wear = v;
            else if (sender == SignalColourSlider) settings.Signal.Colour = v;
            else if (sender == SharpenSlider) settings.Signal.Sharpen = v;
            SignalChanged();
        }

        void OnDecoder(object sender, RoutedEventArgs e)
        {
            if (Quiet) return;
            settings.Signal.Decoder = (string)((RadioButton)sender).Tag;
            SignalChanged();
        }

        void OnSignalEngine(object sender, RoutedEventArgs e) => OpenInBrowser(this, Program.SignalEngineUrl);

        void OnSignalNotices(object sender, RoutedEventArgs e) => Commands.About.Execute(null, this);

        // ---- the signal made from a picture ----

        // the last signal made: the same picture, settings and frame give the same one, so it's kept
        readonly object signalLock = new object();
        Rendered signalPicture;
        SignalSettings signalSettings;
        int signalFrame;
        SignalImage signalImage;

        // The picture through the signal (null with the signal off), on any thread: engine frame
        // `engineFrame` (an animated GIF's frame k is k × its step, so the noise moves at its speed).
        internal SignalImage SignalFor(Rendered r, SignalSettings s, int engineFrame = 0)
        {
            if (r == null || s == null || s.Mode == SignalModes.Off) return null;
            lock (signalLock)
                if (signalPicture == r && signalSettings.SameAs(s) && signalFrame == engineFrame) return signalImage;
            SignalImage made = Signal.Run(r, r.Profile.Id, s, engineFrame);
            lock (signalLock)
            {
                signalPicture = r;
                signalSettings = s.Clone();
                signalFrame = engineFrame;
                signalImage = made;
            }
            return made;
        }
    }
}
