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
            DubsSlider.Value = s.Dubs;
            DubsValue.Text = Words.Dubs(s.Dubs);
            SyncPresets(s);
            int changed = s.Engine.Count;
            EngineNote.Text = changed == 1 ? "1 engine setting changed under Every setting." : changed + " engine settings changed under Every setting.";
            EngineNote.Visibility = changed > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        // The signal changed: the controls follow and the preview's signal is made again. On a
        // machine whose colours come from the signal (CGA composite) the picture is made again
        // too when those colours change, dithered to the new ones; Wear changes no colour, so it
        // only moves the signal, as on the website. A video playing makes its frames again.
        void SignalChanged()
        {
            Sync();
            if (videoPlayer.Playing) videoPlayer.Invalidate();
            else if (picture != null && !AnythingPlays && !SameColours(Signal.ForRender(settings.Deres, settings.SignalOn).FixedPalette, wantedColours)) RenderSoon();
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

        // ---- the tape's extras: copies, presets, and every one of the engine's settings ----

        void OnDubs(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (Quiet) return;
            settings.Signal.Dubs = (int)Math.Max(0, Math.Min(SignalSettings.MaxDubs, Math.Round(DubsSlider.Value)));
            SignalChanged();
        }

        // a preset in the list: its name and its whole Signal (never changed: it's cloned to be used)
        sealed class PresetChoice
        {
            public string Name;
            public SignalSettings Signal;
            public bool Own;
        }

        bool presetsListed;                  // the list holds your presets as they're kept
        (string Name, bool Own)? presetChosen;   // the preset picked or saved last

        // The presets list: "Pick a preset…", the built-in ones, then yours (or a line saying the
        // file can't be read). The one that's the Signal on now is picked: the one picked or saved
        // last while it still is, else the first that is (yours before a built-in one).
        void SyncPresets(SignalSettings s)
        {
            if (!presetsListed)
            {
                presetsListed = true;
                PresetBox.Items.Clear();
                PresetBox.Items.Add(new ComboBoxItem { Content = "Pick a preset…" });
                foreach (var (name, signal) in SignalPresets.BuiltIn) PresetBox.Items.Add(new ComboBoxItem { Content = name, Tag = new PresetChoice { Name = name, Signal = signal } });
                SignalPresets.Kept kept = SignalPresets.Read();
                if (kept.Problem != null || kept.Presets.Any()) PresetBox.Items.Add(new ComboBoxItem { Content = "Your own", IsEnabled = false, FontWeight = FontWeights.SemiBold });
                if (kept.Problem != null) PresetBox.Items.Add(new ComboBoxItem { Content = "(your presets file can't be read)", IsEnabled = false, ToolTip = SignalPresets.FileName + ": " + kept.Problem });
                foreach (SignalPresets.Preset p in kept.Presets) PresetBox.Items.Add(new ComboBoxItem { Content = p.Name, Tag = new PresetChoice { Name = p.Name, Signal = p.Signal, Own = true } });
            }
            var matches = PresetBox.Items.OfType<ComboBoxItem>().Where(i => i.Tag is PresetChoice c && c.Signal.SameAs(s)).ToList();
            ComboBoxItem on = matches.FirstOrDefault(i => i.Tag is PresetChoice c && presetChosen.HasValue && presetChosen.Value.Name == c.Name && presetChosen.Value.Own == c.Own)
                ?? matches.OrderBy(i => ((PresetChoice)i.Tag).Own ? 0 : 1).FirstOrDefault();
            PresetBox.SelectedItem = on ?? PresetBox.Items[0];
            presetOn = (PresetBox.SelectedItem as ComboBoxItem)?.Tag as PresetChoice;
            DeletePresetButton.IsEnabled = presetOn?.Own == true;
        }

        PresetChoice presetOn;

        // (for the tests) the preset that's the Signal on now, and whether it's one of yours
        internal string PresetOnName => presetOn?.Name;
        internal bool PresetOnIsOwn => presetOn?.Own == true;

        // (for the tests) the presets file read again, as when the window opens
        internal void ResetPresetList()
        {
            presetsListed = false;
            Sync();
        }

        void OnPreset(object sender, SelectionChangedEventArgs e)
        {
            if (Quiet || !(PresetBox.SelectedItem is ComboBoxItem item) || !(item.Tag is PresetChoice choice) || choice == presetOn) return;
            Use(choice);
        }

        // a preset's whole Signal in place of the one on now
        void Use(PresetChoice choice)
        {
            presetChosen = (choice.Name, choice.Own);
            settings.Signal = choice.Signal.Clone();
            SignalChanged();
        }

        // (for the tests) the preset of that name picked from the list (false: there's none)
        internal bool UsePreset(string name)
        {
            PresetChoice choice = PresetBox.Items.OfType<ComboBoxItem>().Select(i => i.Tag as PresetChoice).Where(c => c != null && c.Name == name).OrderBy(c => c.Own ? 0 : 1).FirstOrDefault();
            if (choice == null) return false;
            Use(choice);
            return true;
        }

        void OnSavePreset(object sender, RoutedEventArgs e)
        {
            if (!DataFolder.CanKeep)
            {
                Problem(DataFolder.WhyNotKept);
                return;
            }
            string unreadable = SignalPresets.Read().Problem;
            if (unreadable != null)
            {
                Problem(PresetsUnreadable(unreadable, "saved"));
                return;
            }
            string name = NameWindow.Ask(this, "Save as preset", "A name for this Signal, to pick it from the presets:", SuggestedPresetName(), PresetName);
            if (name != null) SavePreset(name);
        }

        // why nothing was saved or deleted: the presets file is there but can't be read (it's left as it is)
        static string PresetsUnreadable(string why, string what) =>
            "Your presets file can't be read (" + why + "), so nothing was " + what + ": mend " + SignalPresets.FileName + " in the settings folder, or move it away.";

        // whether a preset's name can be taken, and what it would do
        internal (bool Ok, string Note) PresetName(string name)
        {
            name = name.Trim();
            if (name.Length == 0) return (false, null);
            if (SignalPresets.BuiltIn.Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))) return (false, "A built-in preset has that name.");
            if (SignalPresets.Read().Presets.Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))) return (true, "Replaces your preset of that name.");
            return (true, null);
        }

        // "My VHS EP", "My VHS EP 2"…
        string SuggestedPresetName()
        {
            string mode = settings.Signal.Mode;
            string start = "My " + (mode != SignalModes.Auto ? signalChoices.FirstOrDefault(c => c.Id == mode).Text ?? "Signal" : "Signal");
            var own = SignalPresets.Read().Presets.ToList();
            string name = start;
            for (int n = 2; own.Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)); n++) name = start + " " + n;
            return name;
        }

        // The Signal on now kept as your preset `name` (one of yours of that name is replaced; the
        // file's other entries stay as they were). False, said in the bottom line, when it can't be.
        internal bool SavePreset(string name)
        {
            name = name.Trim();
            if (!PresetName(name).Ok) return false;
            SignalPresets.Kept kept = SignalPresets.Read();
            if (kept.Problem != null)
            {
                Problem(PresetsUnreadable(kept.Problem, "saved"));
                return false;
            }
            SignalPresets.Preset same = kept.Presets.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
            if (same != null)
            {
                same.Name = name;
                same.Signal = settings.Signal.Clone();
                same.Entry = null;
            }
            else kept.Entries.Add(new SignalPresets.Preset { Name = name, Signal = settings.Signal.Clone() });
            if (!SignalPresets.Keep(kept))
            {
                Problem("Couldn't keep the preset: " + (DataFolder.WhyNotKept ?? DataFolder.LastProblem));
                return false;
            }
            presetChosen = (name, true);
            presetsListed = false;
            Sync();
            Flash("Saved the preset “" + name + "”.", "Good");
            return true;
        }

        void OnDeletePreset(object sender, RoutedEventArgs e)
        {
            if (presetOn?.Own != true) return;
            string name = presetOn.Name;
            if (MessageBox.Show(this, "Delete your preset “" + name + "”? The Signal on now stays as it is.", Program.Name, MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes)
                DeletePreset(name);
        }

        // Your preset `name` deleted (the Signal on now stays as it is; the file's other entries
        // stay as they were). False, said in the bottom line, when it can't be.
        internal bool DeletePreset(string name)
        {
            SignalPresets.Kept kept = SignalPresets.Read();
            if (kept.Problem != null)
            {
                Problem(PresetsUnreadable(kept.Problem, "deleted"));
                return false;
            }
            if (kept.Entries.RemoveAll(e => e is SignalPresets.Preset p && p.Name == name) == 0)
            {
                // changed meanwhile (another b0r3d DeRes, or by hand): the list follows the file
                Problem("Your preset “" + name + "” isn't in your presets file any more.");
                presetsListed = false;
                Sync();
                return false;
            }
            if (!SignalPresets.Keep(kept))
            {
                Problem("Couldn't delete the preset: " + (DataFolder.WhyNotKept ?? DataFolder.LastProblem));
                return false;
            }
            if (presetChosen.HasValue && presetChosen.Value.Name == name && presetChosen.Value.Own) presetChosen = null;
            presetsListed = false;
            Sync();
            Flash("Deleted the preset “" + name + "”.", "Dim");
            return true;
        }

        void OnEverySetting(object sender, RoutedEventArgs e) => EverySetting().ShowDialog();

        // the window with every one of the engine's settings, for the picture on screen (not shown yet)
        internal TapeWindow EverySetting()
        {
            string Connection()
            {
                Profile p = CurrentProfile;
                return Signal.Connection(settings.Signal.Mode, Signal.Machine(p.Id, shown?.Width ?? p.Width)) ?? SignalModes.Composite;
            }
            return new TapeWindow(this, settings, () => Signal.EngineSettings(Connection(), settings.Signal, shown?.Height ?? CurrentProfile.Height), SignalChanged,
                () => "the " + CurrentProfile.Name + " over " + Signal.ConnectionNames[Connection()]);
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
