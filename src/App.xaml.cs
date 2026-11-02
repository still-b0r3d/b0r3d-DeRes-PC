using System;
using System.Windows;
using System.Windows.Threading;

namespace B0r3dDeRes
{
    public partial class App : Application
    {
        // The tests set this: a window that throws becomes a FAIL line instead of a message box.
        internal static Action<Exception> OnProblem;

        public App()
        {
            DispatcherUnhandledException += OnUnhandled;
        }

        void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            e.Handled = true;
            if (OnProblem != null)
            {
                OnProblem(e.Exception);
                return;
            }
            MessageBox.Show("Something went wrong:\n\n" + e.Exception.Message, Program.Name, MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
