using System.Windows.Input;

namespace B0r3dDeRes
{
    // The menu commands with keyboard shortcuts. (Ctrl+C and Ctrl+V copy and paste pictures except
    // in a text box, where they copy and paste text: MainWindow's OnPreviewKeys.)
    static class Commands
    {
        public static readonly RoutedUICommand OpenPicture = Make("Open picture or video…", nameof(OpenPicture), new KeyGesture(Key.O, ModifierKeys.Control));
        public static readonly RoutedUICommand DeresScreen = Make("DeRes the screen…", nameof(DeresScreen), new KeyGesture(Key.D, ModifierKeys.Control | ModifierKeys.Shift));
        public static readonly RoutedUICommand Batch = Make("Batch…", nameof(Batch), new KeyGesture(Key.B, ModifierKeys.Control));
        public static readonly RoutedUICommand PastePicture = Make("Paste picture", nameof(PastePicture), new KeyGesture(Key.V, ModifierKeys.Control | ModifierKeys.Shift));
        public static readonly RoutedUICommand SavePng = Make("Save PNG…", nameof(SavePng), new KeyGesture(Key.S, ModifierKeys.Control));
        public static readonly RoutedUICommand CopyPicture = Make("Copy picture", nameof(CopyPicture), new KeyGesture(Key.C, ModifierKeys.Control | ModifierKeys.Shift));
        public static readonly RoutedUICommand ZoomIn = Make("Zoom in", nameof(ZoomIn), new KeyGesture(Key.OemPlus, ModifierKeys.Control), new KeyGesture(Key.OemPlus, ModifierKeys.Control | ModifierKeys.Shift), new KeyGesture(Key.Add, ModifierKeys.Control));
        public static readonly RoutedUICommand ZoomOut = Make("Zoom out", nameof(ZoomOut), new KeyGesture(Key.OemMinus, ModifierKeys.Control), new KeyGesture(Key.Subtract, ModifierKeys.Control));
        public static readonly RoutedUICommand ZoomFit = Make("Fit", nameof(ZoomFit), new KeyGesture(Key.D0, ModifierKeys.Control), new KeyGesture(Key.NumPad0, ModifierKeys.Control));
        public static readonly RoutedUICommand ZoomOne = Make("1 screen pixel a pixel", nameof(ZoomOne), new KeyGesture(Key.D1, ModifierKeys.Control), new KeyGesture(Key.NumPad1, ModifierKeys.Control));
        public static readonly RoutedUICommand Compare = Make("Compare", nameof(Compare));
        public static readonly RoutedUICommand FullScreen = Make("Full screen", nameof(FullScreen), new KeyGesture(Key.F11));
        public static readonly RoutedUICommand About = Make("About b0r3d DeRes", nameof(About), new KeyGesture(Key.F1));

        static RoutedUICommand Make(string text, string name, params KeyGesture[] keys)
        {
            var gestures = new InputGestureCollection();
            foreach (KeyGesture key in keys) if (key != null) gestures.Add(key);
            return new RoutedUICommand(text, name, typeof(Commands), gestures);
        }
    }
}
