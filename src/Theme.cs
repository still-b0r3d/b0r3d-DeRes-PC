using System;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Win32;

namespace B0r3dDeRes
{
    // Light or dark: like Windows' apps (the default), or always one of them; high contrast always
    // gets Windows' own colours. The colours live in Themes\Light.xaml, Dark.xaml and
    // HighContrast.xaml, and every style uses them through DynamicResource, so swapping the
    // dictionary repaints everything. Windows' setting is only read, never changed.
    static class Theme
    {
        public static Look Choice { get; private set; } = Look.Windows;
        public static bool IsDark { get; private set; }
        public static bool IsHighContrast { get; private set; }
        public static event Action Changed;

        static bool listening;

        public static void Start(Look look)
        {
            if (!listening)
            {
                SystemEvents.UserPreferenceChanged += OnWindowsChanged;
                SystemParameters.StaticPropertyChanged += OnSystemParameter;
                listening = true;
            }
            Set(look);
        }

        public static void Stop()
        {
            if (!listening) return;
            SystemEvents.UserPreferenceChanged -= OnWindowsChanged;
            SystemParameters.StaticPropertyChanged -= OnSystemParameter;
            listening = false;
        }

        public static void Set(Look look)
        {
            Choice = look;
            Apply();
        }

        static void Apply()
        {
            Application app = Application.Current;
            if (app == null) return;
            bool highContrast = SystemParameters.HighContrast;
            bool dark = !highContrast && (Choice == Look.Dark || Choice == Look.Windows && WindowsAppsAreDark());
            string name = highContrast ? "HighContrast" : dark ? "Dark" : "Light";
            // (LoadComponent only reads XAML built into the exe; a dictionary's Source could fetch some)
            var colours = (ResourceDictionary)Application.LoadComponent(new Uri("/b0r3dDeRes;component/Themes/" + name + ".xaml", UriKind.Relative));
            app.Resources.MergedDictionaries[0] = colours;
            IsDark = dark;
            IsHighContrast = highContrast;
            foreach (Window window in app.Windows) TitleBar(window);
            Changed?.Invoke();
        }

        // the title bar follows too; call it once the window has a handle (SourceInitialized)
        public static void TitleBar(Window window)
        {
            Native.DarkTitleBar(new WindowInteropHelper(window).Handle, IsDark);
        }

        static bool WindowsAppsAreDark()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    object light = key?.GetValue("AppsUseLightTheme");
                    return light is int value && value == 0;
                }
            }
            catch (Exception e) when (e is System.Security.SecurityException || e is UnauthorizedAccessException || e is System.IO.IOException)
            {
                return false;
            }
        }

        static void OnWindowsChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            // light/dark arrives as General; colours and high contrast as Color or Accessibility
            if (e.Category == UserPreferenceCategory.General || e.Category == UserPreferenceCategory.Color || e.Category == UserPreferenceCategory.Accessibility)
                Application.Current?.Dispatcher.BeginInvoke(new Action(Apply));
        }

        static void OnSystemParameter(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(SystemParameters.HighContrast))
                Application.Current?.Dispatcher.BeginInvoke(new Action(Apply));
        }
    }
}
