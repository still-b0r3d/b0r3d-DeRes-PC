using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace B0r3dDeRes
{
    // A name asked for (Save as preset): a box with a name to start from, a line that says what
    // the name would do as it's typed, Save and Cancel.
    sealed class NameWindow : Window
    {
        internal readonly TextBox Box;
        internal readonly TextBlock Note;
        internal readonly Button Ok;
        readonly Func<string, (bool Ok, string Note)> check;

        // `check` says whether a name can be taken and what it would do ("Replaces your preset of that name.")
        internal NameWindow(Window owner, string title, string question, string suggestion, Func<string, (bool Ok, string Note)> check)
        {
            this.check = check;
            // (a window never shown, as in the tests, can't own one)
            if (owner != null && owner.IsLoaded) Owner = owner;
            Icon = owner?.Icon;
            Title = title;
            Width = 380;
            SizeToContent = SizeToContent.Height;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;
            FontFamily = new FontFamily("Segoe UI");
            FontSize = 13;
            UseLayoutRounding = true;
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
            SetResourceReference(BackgroundProperty, "Back");
            SetResourceReference(ForegroundProperty, "Text");
            SourceInitialized += (s, e) => Theme.TitleBar(this);

            var root = new StackPanel();
            root.Children.Add(new TextBlock { Text = question, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });
            Box = new TextBox { Text = suggestion ?? "", MaxLength = 60 };
            AutomationProperties.SetName(Box, question);
            Box.TextChanged += (s, e) => Follow();
            root.Children.Add(Box);
            Note = new TextBlock { Style = (Style)FindResource("Hint") };
            root.Children.Add(Note);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
            Ok = new Button { Content = "_Save", IsDefault = true, MinWidth = 80, Margin = new Thickness(0, 0, 8, 0) };
            Ok.Click += (s, e) => DialogResult = true;
            var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 80 };
            buttons.Children.Add(Ok);
            buttons.Children.Add(cancel);
            root.Children.Add(buttons);
            var back = new Border { Padding = new Thickness(20, 18, 20, 16), Child = root };
            back.SetResourceReference(Border.BackgroundProperty, "Back");
            Content = back;
            Loaded += (s, e) =>
            {
                Box.Focus();
                Box.SelectAll();
            };
            Follow();
        }

        // the name as it would be taken
        internal string Answer => Box.Text.Trim();

        void Follow()
        {
            var (ok, note) = check(Answer);
            Ok.IsEnabled = ok;
            Note.Text = note ?? "";
            Note.Visibility = string.IsNullOrEmpty(note) ? Visibility.Collapsed : Visibility.Visible;
        }

        // the name given, or null for Cancel
        public static string Ask(Window owner, string title, string question, string suggestion, Func<string, (bool Ok, string Note)> check)
        {
            var window = new NameWindow(owner, title, question, suggestion, check);
            return window.ShowDialog() == true ? window.Answer : null;
        }
    }
}
