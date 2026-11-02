using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;

namespace B0r3dDeRes
{
    // Help → About: the version and build, the maker, where the settings live, and the licences.
    public partial class AboutWindow : Window
    {
        public AboutWindow()
        {
            InitializeComponent();
            VersionText.Text = Program.VersionText;
            Licences.Text = LicenceText();
            ShowFolder();
            ShowCup();
            SourceInitialized += (s, e) => Theme.TitleBar(this);
        }

        // b0r3d DeRes's own licence, then the third-party notices (fdlibm), as built into the exe
        internal static string LicenceText()
        {
            return "b0r3d DeRes\r\n========\r\n\r\n" + Resource("B0r3dDeRes.LICENSE") + "\r\n\r\n" + Resource("B0r3dDeRes.THIRD-PARTY-NOTICES.txt");
        }

        static string Resource(string name)
        {
            using (Stream stream = typeof(AboutWindow).Assembly.GetManifestResourceStream(name))
            {
                if (stream == null) return "(" + name + " is missing from this build)";
                using (var reader = new StreamReader(stream)) return reader.ReadToEnd().Replace("\r\n", "\n").Replace("\n", "\r\n");
            }
        }

        void ShowFolder()
        {
            bool made = Directory.Exists(DataFolder.Path);
            string text = DataFolder.Path;
            if (!DataFolder.CanKeep) text += "\n" + DataFolder.WhyNotKept;
            else if (!made) text += "\n(made the first time there's a setting to keep)";
            FolderText.Text = text;
            OpenFolder.IsEnabled = made;
        }

        void ShowCup() => CupPicture.Source = Cup.For(64, VisualTreeHelper.GetDpi(this).DpiScaleX);

        protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
        {
            base.OnDpiChanged(oldDpi, newDpi);
            ShowCup();
        }

        void OnOpenFolder(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo("explorer.exe", "\"" + DataFolder.Path + "\"") { UseShellExecute = true });
            }
            catch (Exception x) when (x is Win32Exception || x is InvalidOperationException)
            {
                MessageBox.Show(this, "Couldn't open the folder:\n\n" + x.Message, Program.Name, MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        void OnMaker(object sender, RoutedEventArgs e) => MainWindow.OpenInBrowser(this, Program.MakerUrl);
    }
}
