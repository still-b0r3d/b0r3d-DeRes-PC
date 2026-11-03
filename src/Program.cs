using System;
using System.Reflection;
using System.Windows;

namespace B0r3dDeRes
{
    static class Program
    {
        public const string Name = "b0r3d DeRes";
        public const string Maker = "b0r3d.org";
        public const string MakerUrl = "https://b0r3d.org";
        public const string WebsiteUrl = "https://img.b0r3d.org";
        // the signal engine's own page (under the Signal, "ntsc-rs")
        public const string SignalEngineUrl = "https://github.com/valadaptive/ntsc-rs";

        // "0.1 build abc1234", the exe's Product version: the version, then the commit it was built
        // from ("dev" for a build made anywhere but GitHub). build.cmd passes the commit in.
        public static readonly string FullVersion =
            typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0 build dev";

        public static string Version => FullVersion.Split(' ')[0];

        public static string Build
        {
            get
            {
                int at = FullVersion.LastIndexOf(" build ", StringComparison.Ordinal);
                return at < 0 ? "dev" : FullVersion.Substring(at + " build ".Length);
            }
        }

        // as it's shown: "v0.1 · Build: abc1234"
        public static string VersionText => "v" + Version + " · Build: " + Build;

        [STAThread]
        static int Main(string[] args)
        {
            CommandLine line = CommandLine.Parse(args);
            if (line.Problem != null)
            {
                MessageBox.Show(line.Problem + "\n\n" + CommandLine.Usage, Name, MessageBoxButton.OK, MessageBoxImage.Warning);
                return 2;
            }
            DataFolder.Choose(line.DataFolder);
            if (line.SelfTestReport != null) return SelfTest.Run(line.SelfTestReport);
            DataFolder.CheckWritable();

            var app = new App();
            app.InitializeComponent();
            Settings settings = Settings.Load();
            Theme.Start(settings.Look);
            int code = app.Run(new MainWindow(settings, line.Picture));
            Theme.Stop();
            return code;
        }
    }
}
