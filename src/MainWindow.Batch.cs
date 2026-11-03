using System.Collections.Generic;
using System.Windows.Input;

namespace B0r3dDeRes
{
    // File → Batch: a folder or several pictures through the look there is now.
    public partial class MainWindow
    {
        BatchWindow batch;

        internal BatchWindow BatchShown => batch;

        void OnBatch(object sender, ExecutedRoutedEventArgs e) => OpenBatch(null);

        // the batch window (one at a time), with these pictures and folders added to it
        internal BatchWindow OpenBatch(IList<string> paths)
        {
            if (batch == null)
            {
                batch = new BatchWindow(this);
                batch.Closed += (s, e) => batch = null;
                // (the tests' windows are laid out without ever being shown)
                if (IsLoaded)
                {
                    batch.Owner = this;
                    batch.Show();
                }
            }
            else if (batch.IsVisible) batch.Activate();
            if (paths != null && !batch.Add(paths)) Flash("A batch is running: drop or choose the pictures again once it's done.", "Warn");
            return batch;
        }
    }
}
