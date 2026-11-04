using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using B0r3dDeRes.Engine;

namespace B0r3dDeRes.Tests
{
    // The Signal's tape extras in the window: Dub it, the presets (built in, and your own kept in
    // the data folder), every one of the engine's settings in a window of its own, ntsc-rs
    // settings files in and out; and links, look files and the settings file keep what's changed.
    static partial class TestMain
    {
        static void TapeExtrasInTheWindow()
        {
            MainWindow window = NewMain();
            WaitForPicture(window);
            Pick(window, "nes");
            PickSignal(window, SignalModes.VhsSp);
            window.SignalAdvanced.IsExpanded = true;
            byte[] SamePreview(string what)
            {
                WaitForPicture(window);
                WaitForScreen(window);
                Rendered r = window.Shown;
                int scale = MainWindow.ScreenScale(r.Width, r.Height, 1, window.View.Zoom);
                byte[] want = Signal.Renderer(Signal.Run(r, r.Profile.Id, window.Current.Signal, 0), r.Width * scale, r.Height * scale, null, 0, r.Paper).Rgba();
                Check(window.View.HasScreen && window.View.ScreenPixels.SequenceEqual(want), what + ": the preview isn't the engine's signal");
                return want;
            }
            string Picked() => (window.PresetBox.SelectedItem as ComboBoxItem)?.Content as string;
            string[] Listed() => window.PresetBox.Items.OfType<ComboBoxItem>().Select(i => i.Content as string).ToArray();

            // Dub it: the tape copied three times, each copy through the signal again
            byte[] tape0 = SamePreview("VHS SP");
            Check(window.DubsValue.Text == "off" && window.EngineNote.Visibility != Visibility.Visible, "at first no copies and no engine setting changed: " + window.DubsValue.Text);
            window.DubsSlider.Value = 3;
            Check(window.Current.Signal.Dubs == 3 && window.DubsValue.Text == "3 times", "Dub it 3: " + window.Current.Signal.Dubs + " / " + window.DubsValue.Text);
            Check(!SamePreview("dubbed 3 times").SequenceEqual(tape0), "three copies look the same as the tape itself");
            window.DubsSlider.Value = 0;

            // the presets: one picked from the list is its whole Signal; changed, it's no longer that preset
            Check(Listed().SequenceEqual(new[] { "Pick a preset…" }.Concat(SignalPresets.BuiltIn.Select(p => p.Name))) && Picked() == "Pick a preset…" && window.PresetOnName == null,
                "the presets: " + string.Join(", ", Listed()));
            window.PresetBox.SelectedItem = window.PresetBox.Items.OfType<ComboBoxItem>().First(i => i.Content as string == "Copy of a copy");
            SignalSettings s = window.Current.Signal;
            Check(s.Mode == SignalModes.VhsLp && s.Wear == 0.6 && s.Dubs == 4 && s.EngineText == "{\"chroma_delay_vertical\":1}" && window.PresetOnName == "Copy of a copy" && Picked() == "Copy of a copy" &&
                !window.DeletePresetButton.IsEnabled && window.DubsValue.Text == "4 times" && window.EngineNote.Visibility == Visibility.Visible && window.EngineNote.Text == "1 engine setting changed under Every setting.",
                "Copy of a copy: " + s.Mode + " " + s.Wear + " dubs " + s.Dubs + " " + s.EngineText + " | " + window.EngineNote.Text);
            SamePreview("Copy of a copy");
            window.WearSlider.Value = 70;
            Check(window.PresetOnName == null && Picked() == "Pick a preset…", "a preset changed is still picked: " + Picked());

            // your own: kept in the data folder, listed under "Your own", picked when it's on
            Check(!window.PresetName("").Ok && !window.PresetName(" bad TRACKING ").Ok && window.PresetName("Bad tracking").Note == "A built-in preset has that name." &&
                window.PresetName("Mine").Ok && window.PresetName("Mine").Note == null, "the names a preset can have");
            Check(window.SavePreset("  Mine  "), "Save as preset");
            string kept = Path.Combine(DataFolder.Path, SignalPresets.FileName);
            List<SignalPresets.Preset> Own() => SignalPresets.Read().Presets.ToList();
            var own = Own();
            Check(File.Exists(kept) && own.Count == 1 && own[0].Name == "Mine" && own[0].Signal.SameAs(window.Current.Signal),
                "the preset kept and read back: " + own.Count);
            Check(Listed().Skip(1 + SignalPresets.BuiltIn.Length).SequenceEqual(new[] { "Your own", "Mine" }) && !((ComboBoxItem)window.PresetBox.Items[1 + SignalPresets.BuiltIn.Length]).IsEnabled &&
                window.PresetOnName == "Mine" && window.PresetOnIsOwn && window.DeletePresetButton.IsEnabled && window.NoteText == "Saved the preset “Mine”.",
                "yours in the list: " + string.Join(", ", Listed()) + " | " + window.NoteText);
            Check(window.PresetName("MINE").Ok && window.PresetName("MINE").Note == "Replaces your preset of that name.", "a name of yours again replaces it");
            // the name box says the same as it's typed (never shown here)
            var ask = new NameWindow(window, "Save as preset", "A name:", "Bad tracking", window.PresetName);
            Check(!ask.Ok.IsEnabled && ask.Note.Text == "A built-in preset has that name.", "the name box with a built-in name: " + ask.Note.Text);
            ask.Box.Text = " mine ";
            Check(ask.Ok.IsEnabled && ask.Note.Text == "Replaces your preset of that name." && ask.Answer == "mine", "the name box with yours: " + ask.Note.Text);
            ask.Close();
            // another Signal under the same name replaces it; picked again after a change
            window.DubsSlider.Value = 1;
            Check(window.SavePreset("mine") && (own = Own()).Count == 1 && own[0].Name == "mine" && own[0].Signal.Dubs == 1,
                "replaced: " + string.Join(", ", own.Select(p => p.Name + " " + p.Signal.Dubs)));
            window.DubsSlider.Value = 0;
            Check(window.PresetOnName == null && window.UsePreset("mine") && window.Current.Signal.Dubs == 1 && window.PresetOnIsOwn, "picked again: " + window.PresetOnName);
            // two of yours with the same Signal: the one saved or picked last is the one shown (and deleted)
            Check(window.SavePreset("twin") && window.PresetOnName == "twin" && window.UsePreset("mine") && window.PresetOnName == "mine" && window.UsePreset("twin") && window.PresetOnName == "twin",
                "two presets of the same Signal: " + window.PresetOnName);
            Check(window.DeletePreset("twin") && window.PresetOnName == "mine" && Own().Select(p => p.Name).SequenceEqual(new[] { "mine" }), "the twin deleted, the other shown: " + window.PresetOnName);
            // deleted: the list forgets it, the Signal stays
            Check(window.DeletePreset("mine") && Own().Count == 0 && window.PresetOnName == null && window.Current.Signal.Dubs == 1 && !window.DeletePresetButton.IsEnabled &&
                Listed().Length == 1 + SignalPresets.BuiltIn.Length, "deleted: " + string.Join(", ", Listed()));
            Check(!window.DeletePreset("mine") && window.NoteText == "Your preset “mine” isn't in your presets file any more.", "a preset gone from the file meanwhile: " + window.NoteText);
            // what this version can't read in the file stays as it was through a save; a file that
            // can't be read at all is never written over, and the list says so
            string theirs0 = "{\"presets\":[{\"name\":\"Theirs\",\"signal\":{\"mode\":\"rf\",\"future\":[1,{\"x\":true}]},\"note\":\"keep me\"},{\"something\":\"else\"}],\"version\":2}";
            File.WriteAllText(kept, theirs0);
            window.ResetPresetList();
            Check(Listed().Skip(1 + SignalPresets.BuiltIn.Length).SequenceEqual(new[] { "Your own", "Theirs" }), "a file of a later version: " + string.Join(", ", Listed()));
            Check(window.SavePreset("Mine"), "saving beside entries this version doesn't know");
            string after = File.ReadAllText(kept);
            Check(after.Contains("{\"name\":\"Theirs\",\"signal\":{\"mode\":\"rf\",\"future\":[1,{\"x\":true}]},\"note\":\"keep me\"}") && after.Contains("{\"something\":\"else\"}") &&
                after.Contains(",\"version\":2}") && Own().Select(p => p.Name).SequenceEqual(new[] { "Theirs", "Mine" }), "what the file had should stay as it was: " + after);
            File.WriteAllText(kept, "{\"presets\":[{\"name\":\"A\",");
            window.ResetPresetList();
            Check(Listed().Last() == "(your presets file can't be read)" && !window.SavePreset("Lost") && File.ReadAllText(kept) == "{\"presets\":[{\"name\":\"A\"," &&
                window.NoteText.StartsWith("Your presets file can't be read (it isn't JSON", StringComparison.Ordinal), "a broken presets file: " + string.Join(", ", Listed()) + " | " + window.NoteText);
            File.Delete(kept);
            window.ResetPresetList();

            // every one of the engine's settings, as the engine has them for this picture, changed in place
            PickSignal(window, SignalModes.VhsSp);
            window.DubsSlider.Value = 0;
            TapeWindow tape = window.EverySetting();
            int lines = window.Shown.Height;
            NtscSettings Effective() => Signal.EngineSettings(SignalModes.VhsSp, window.Current.Signal, lines);
            Check(tape.RowCount == NtscSettings.All.Length && tape.HeadingText.StartsWith("The Signal engine's settings for the NES over a VHS tape (SP).", StringComparison.Ordinal),
                "the window: " + tape.RowCount + " rows, " + tape.HeadingText);
            Check(tape.LabelOf("chroma_delay_vertical").StartsWith("• ", StringComparison.Ordinal) && !tape.LabelOf("snow_intensity").StartsWith("•", StringComparison.Ordinal), "the preset's change is marked");
            tape.ResetAll();
            Check(window.Current.Signal.Engine.Count == 0 && window.EngineNote.Visibility != Visibility.Visible && !tape.LabelOf("chroma_delay_vertical").StartsWith("•", StringComparison.Ordinal),
                "Reset all: " + window.Current.Signal.EngineText);
            NtscSettings now = Effective();
            foreach (NtscSetting setting in NtscSettings.All)
            {
                object v = now.Get(setting.Key);
                FrameworkElement c = tape.ControlOf(setting.Key);
                bool shows = c is CheckBox box ? box.IsChecked == (v is bool b && b)
                    : c is ComboBox menu ? menu.SelectedItem is ComboBoxItem item && (int)item.Tag == (int)(double)v
                    : c is TextBox text ? text.Text == ((long)(double)v).ToString(CultureInfo.InvariantCulture)
                    : c is Slider;
                Check(shows, setting.Key + " doesn't show " + v);
            }
            // the seed's box: let go of as it was, nothing changes; a new seed does; Reset all puts it back
            var seedBox = (TextBox)tape.ControlOf("random_seed");
            void LetGo() => seedBox.RaiseEvent(new KeyboardFocusChangedEventArgs(Keyboard.PrimaryDevice, 0, seedBox, null) { RoutedEvent = UIElement.LostKeyboardFocusEvent });
            LetGo();
            Check(!window.Current.Signal.Engine.ContainsKey("random_seed"), "letting go of the seed's box as it was changed the seed");
            seedBox.Text = " 1234 ";
            LetGo();
            Check(window.Current.Signal.Engine.TryGetValue("random_seed", out object seed) && seed is double sd && sd == 1234 && seedBox.Text == "1234" &&
                tape.LabelOf("random_seed").StartsWith("• ", StringComparison.Ordinal), "a seed typed in: " + seedBox.Text);
            seedBox.Text = "77";
            tape.ResetAll();
            LetGo();
            Check(seedBox.Text == "0" && window.Current.Signal.Engine.Count == 0, "Reset all should put the seed's box back for good: " + seedBox.Text + ", " + window.Current.Signal.EngineText);
            // small values show their own digits, not 0
            window.Current.Signal.Engine["snow_intensity"] = 0.00004;
            window.Current.Signal.Engine["vhs_chroma_loss"] = 0.000002;
            tape.Refresh();
            Check(tape.ValueOf("snow_intensity") == "0.00004" && tape.ValueOf("vhs_chroma_loss").EndsWith(" %", StringComparison.Ordinal) && tape.ValueOf("vhs_chroma_loss") != "0 %",
                "small values: " + tape.ValueOf("snow_intensity") + " / " + tape.ValueOf("vhs_chroma_loss"));
            tape.ResetAll();
            var snow = (Slider)tape.ControlOf("snow_intensity");
            snow.Value = snow.Maximum / 2;
            Check(window.Current.Signal.Engine.TryGetValue("snow_intensity", out object snowAt) && snowAt is double d && tape.LabelOf("snow_intensity").StartsWith("• ", StringComparison.Ordinal) &&
                (double)Effective().Get("snow_intensity") == Signal.JsonFloat(d), "a slider: " + snowAt);
            var head = (CheckBox)tape.ControlOf("head_switching");
            Check(head.IsChecked == true && tape.RowOn("head_switching_height"), "a VHS tape has head switching");
            head.IsChecked = false;
            Check(window.Current.Signal.Engine["head_switching"] is bool on && !on && !tape.RowOn("head_switching_height"), "a switch turned off greys out what's under it");
            var field = (ComboBox)tape.ControlOf("use_field");
            field.SelectedItem = field.Items.OfType<ComboBoxItem>().First(i => (int)i.Tag == 0);
            Check(window.Current.Signal.Engine["use_field"] is double f && f == 0 && window.EngineNote.Text == "3 engine settings changed under Every setting.", "a menu: " + window.EngineNote.Text);
            SamePreview("three engine settings changed");

            // ntsc-rs settings files: what Save writes reads back as these settings; one loaded
            // changes what it has otherwise than the connection (the scale stays the picture's own)
            Check(NtscSettings.FromFile(tape.FileText).SameAs(Effective()), "the settings file Save writes doesn't read back the same");
            string folder = Path.Combine(Work, "tape");
            Fresh(folder);
            NtscSettings theirs = NtscSettings.FromValues(NtscSettings.DefaultValues());
            theirs.Set("composite_noise_intensity", 0.1);
            theirs.Set("vhs_tape_speed", 3.0);
            theirs.Set("use_field", 1.0);
            string file = Path.Combine(folder, "theirs.json");
            File.WriteAllText(file, theirs.ToFile());
            Check(tape.LoadFile(file) == null, "an ntsc-rs settings file should load");
            string[] scaling = { "scale_settings", "bandwidth_scale", "vertical_scale", "scale_with_video_size" };
            SignalSettings plain = window.Current.Signal.Clone();
            plain.Engine.Clear();
            List<string> differ = theirs.Differences(Signal.EngineSettings(SignalModes.VhsSp, plain, lines)).Where(k => !scaling.Contains(k)).ToList();
            Check(window.Current.Signal.Engine.Keys.OrderBy(k => k, StringComparer.Ordinal).SequenceEqual(differ.OrderBy(k => k, StringComparer.Ordinal)) && differ.Count < NtscSettings.All.Length &&
                Effective().Differences(theirs).All(k => scaling.Contains(k)) && (double)window.Current.Signal.Engine["composite_noise_intensity"] == 0.1 &&
                window.Current.Signal.Engine.ContainsKey("vhs_tape_speed") && window.Current.Signal.Engine.ContainsKey("use_field"),
                "loaded: " + window.Current.Signal.EngineText + "; still differing in " + string.Join(", ", Effective().Differences(theirs)));
            SamePreview("an ntsc-rs settings file");
            string before = window.Current.Signal.EngineText;
            File.WriteAllText(file, "{\"_composite_preemphasis\":1.0,\"version\":1}");
            Check(tape.LoadFile(file) == "it's an ntscQT preset, not an ntsc-rs settings file" && window.Current.Signal.EngineText == before, "an ntscQT preset is refused and changes nothing");
            Check(tape.LoadFile(Path.Combine(folder, "missing.json")) != null && window.Current.Signal.EngineText == before, "a missing file is said and changes nothing");

            // the changes and the copies go with links, look files and the settings file
            window.DubsSlider.Value = 2;
            Settings live = window.Current;
            Check(Looks.Apply(Looks.FromLink(Looks.Link(live)), Settings.Defaults()).Signal.SameAs(live.Signal), "a share link lost the engine's settings or the copies");
            Check(Looks.Apply(Looks.FromFile(Looks.FileText(live)), Settings.Defaults()).Signal.SameAs(live.Signal), "a look file lost the engine's settings or the copies");
            string ini = Path.Combine(folder, "b0r3dDeRes.ini");
            File.WriteAllText(ini, live.ToText());
            Check(Settings.Load(ini).Signal.SameAs(live.Signal), "the settings file lost the engine's settings or the copies");
            Check(Looks.Apply(new Dictionary<string, object>(), live).Signal.SameAs(live.Signal), "a look file without a Signal should leave the one on now as it was, engine settings and copies too");
            tape.Close();
            window.Close();
        }
    }
}
