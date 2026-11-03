using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace B0r3dDeRes
{
    // DeRes the screen: b0r3d DeRes steps aside, every screen is pictured and covered, and a box,
    // a whole screen or all of them becomes the picture. (b0r3d QR's Scan the screen.)
    public partial class MainWindow
    {
        List<SnipWindow> snips;     // set while the screens wait for a box
        bool steppedAside, batchAside;

        internal bool Snipping => snips != null;

        void WhenNotSnipping(object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = snips == null;
            e.Handled = true;
        }

        // steps aside at once (no fade), then pictures every screen
        void OnDeresScreen(object sender, ExecutedRoutedEventArgs e)
        {
            if (snips != null) return;
            if (fullScreen) SetFullScreen(false);
            snips = new List<SnipWindow>();
            CommandManager.InvalidateRequerySuggested();
            Native.Transitions(this, false);
            steppedAside = true;
            Hide();
            // the batch window steps aside too (Windows hides only this one)
            if (batch != null && batch.IsVisible)
            {
                Native.Transitions(batch, false);
                batch.Hide();
                batchAside = true;
            }
            var pause = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };  // for the desktop under this window to repaint
            pause.Tick += (s, a) =>
            {
                pause.Stop();
                ShowSnips();
            };
            pause.Start();
        }

        // (whatever goes wrong, b0r3d DeRes comes back: it's hidden, with nothing in the taskbar)
        void ShowSnips()
        {
            var shown = new List<SnipWindow>();
            try
            {
                foreach (Monitors.Monitor monitor in Monitors.All())
                    shown.Add(new SnipWindow(monitor, Monitors.Capture(monitor.Bounds), true));
                if (shown.Count == 0)
                {
                    snips = null;
                    ComeBack();
                    Problem("Windows says there are no screens to picture.");
                    return;
                }
                WatchSnips(shown);
                Point pointer = Monitors.Pointer();
                SnipWindow under = shown.FirstOrDefault(w => Contains(w.Monitor.Bounds, pointer)) ?? shown.FirstOrDefault(w => w.Monitor.Main) ?? shown[0];
                foreach (SnipWindow snip in shown)
                {
                    Monitors.Cover(snip, snip.Monitor.Bounds);
                    snip.Show();
                    Monitors.Cover(snip, snip.Monitor.Bounds);  // again: arriving on a screen with other scaling can resize it
                }
                under.Activate();
            }
            catch (Exception x)
            {
                if (snips != null && snips.Count > 0)
                {
                    OnSnipChosen(snips[0], SnipWindow.Choice.Cancel, Int32Rect.Empty);
                }
                else
                {
                    snips = null;
                    foreach (SnipWindow snip in shown) snip.Close();  // each holds a whole screen's picture
                    ComeBack();
                }
                Problem("Couldn't take a picture of the screen: " + x.Message);
            }
        }

        // The screens' windows: a choice on any of them (or one being closed) ends the wait for
        // all of them, and so does switching to another program, which they'd otherwise hide.
        internal void WatchSnips(List<SnipWindow> shown)
        {
            snips = shown;
            foreach (SnipWindow snip in shown) snip.Chosen += OnSnipChosen;
            if (Application.Current != null) Application.Current.Deactivated += OnAwayDuringSnip;
        }

        void OnAwayDuringSnip(object sender, EventArgs e)
        {
            if (snips != null && snips.Count > 0) OnSnipChosen(snips[0], SnipWindow.Choice.Cancel, Int32Rect.Empty);
        }

        static bool Contains(Int32Rect area, Point p) => p.X >= area.X && p.Y >= area.Y && p.X < area.X + area.Width && p.Y < area.Y + area.Height;

        internal void OnSnipChosen(SnipWindow from, SnipWindow.Choice choice, Int32Rect box)
        {
            List<SnipWindow> all = snips != null && snips.Count > 0 ? snips : new List<SnipWindow> { from };
            snips = null;
            if (Application.Current != null) Application.Current.Deactivated -= OnAwayDuringSnip;
            foreach (SnipWindow snip in all)
            {
                snip.Chosen -= OnSnipChosen;
                if (!snip.IsClosed) snip.Close();
            }
            ComeBack();
            CommandManager.InvalidateRequerySuggested();
            if (choice == SnipWindow.Choice.Cancel) return;

            BitmapSource shot;
            switch (choice)
            {
                case SnipWindow.Choice.Box:
                    var crop = new CroppedBitmap(from.Shot, box);
                    crop.Freeze();
                    shot = crop;
                    break;
                case SnipWindow.Choice.Screen:
                    shot = from.Shot;
                    break;
                default:
                    shot = all.Count == 1 ? all[0].Shot : Monitors.Stitch(all.Select(w => (w.Monitor, w.Shot)).ToList());
                    break;
            }
            Open(() => Pictures.FromScreen(shot, "screen.png"), "the picture of the screen");
        }

        // back after the screens were pictured (only shown again if it stepped aside for them)
        void ComeBack()
        {
            if (steppedAside)
            {
                steppedAside = false;
                Show();
            }
            Native.Transitions(this, true);
            if (batchAside)
            {
                batchAside = false;
                if (batch != null)
                {
                    batch.Show();
                    Native.Transitions(batch, true);
                }
            }
            if (IsVisible) Activate();
        }
    }
}
