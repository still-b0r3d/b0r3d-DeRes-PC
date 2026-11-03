using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace B0r3dDeRes
{
    // One per screen while picking what to DeRes: the screen as it was a moment ago, dimmed, to
    // drag a box on. A box takes what's in it, a click takes that whole screen, Enter takes every
    // screen as one picture, and Esc or a right-click cancels. The picture only ever lives in
    // memory. (b0r3d QR's SnipWindow.cs.)
    sealed class SnipWindow : Window
    {
        public enum Choice { Cancel, Box, Screen, Everything }

        public readonly Monitors.Monitor Monitor;
        public readonly BitmapSource Shot;   // the screen in real pixels

        // what was chosen, with the box in Shot's pixels (the whole shot unless it's a box)
        public event Action<SnipWindow, Choice, Int32Rect> Chosen;

        const double ClickSize = 6;   // a drag shorter than this is a click

        readonly Grid root = new Grid { Background = Brushes.Black };
        readonly Path veil;
        readonly Rectangle outline;
        Point? start;
        bool done;

        public SnipWindow(Monitors.Monitor monitor, BitmapSource shot, bool showHint)
        {
            Monitor = monitor;
            Shot = shot;
            Title = "b0r3d DeRes: DeRes the screen";
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Topmost = true;
            Background = Brushes.Black;
            Cursor = Cursors.Cross;
            UseLayoutRounding = true;

            var picture = new Image { Source = shot, Stretch = Stretch.Fill };
            RenderOptions.SetBitmapScalingMode(picture, BitmapScalingMode.NearestNeighbor);
            veil = new Path { Fill = new SolidColorBrush(Color.FromArgb(0x8C, 0, 0, 0)), IsHitTestVisible = false };
            outline = new Rectangle { Stroke = new SolidColorBrush(Color.FromRgb(0x00, 0xFF, 0x9D)), StrokeThickness = 2, Visibility = Visibility.Collapsed, IsHitTestVisible = false };
            var canvas = new Canvas { IsHitTestVisible = false };
            canvas.Children.Add(veil);
            canvas.Children.Add(outline);
            root.Children.Add(picture);
            root.Children.Add(canvas);
            if (showHint)
            {
                root.Children.Add(new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(0xE6, 0x13, 0x13, 0x13)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(0x3C, 0x3C, 0x3C)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(16, 9, 16, 9),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(0, 28, 0, 0),
                    IsHitTestVisible = false,
                    Child = new TextBlock
                    {
                        Text = "Drag a box around what to DeRes  ·  click for this whole screen  ·  Enter: every screen  ·  Esc: cancel",
                        Foreground = new SolidColorBrush(Color.FromRgb(0xE6, 0xE6, 0xE6)),
                        FontFamily = new FontFamily("Segoe UI"),
                        FontSize = 14,
                    },
                });
            }
            Content = root;

            SizeChanged += (s, e) => ShowBox(null);
            root.MouseLeftButtonDown += (s, e) => { start = e.GetPosition(root); root.CaptureMouse(); e.Handled = true; };
            root.MouseMove += (s, e) => { if (start.HasValue) ShowBox(Between(start.Value, e.GetPosition(root))); };
            root.MouseLeftButtonUp += (s, e) =>
            {
                if (!start.HasValue) return;
                Point from = start.Value;
                start = null;
                root.ReleaseMouseCapture();
                Drag(from, e.GetPosition(root));
                e.Handled = true;
            };
            root.MouseRightButtonUp += (s, e) => Finish(Choice.Cancel, Whole);
            PreviewKeyDown += (s, e) =>
            {
                if (e.Key == Key.Escape) Finish(Choice.Cancel, Whole);
                else if (e.Key == Key.Enter) Finish(Choice.Everything, Whole);
            };
        }

        Int32Rect Whole => new Int32Rect(0, 0, Shot.PixelWidth, Shot.PixelHeight);

        public bool IsClosed { get; private set; }

        // closed some other way (Alt+F4): the same as Esc, so b0r3d DeRes always comes back (once
        // the closing is over; after a choice it's already done, and Finish does nothing)
        protected override void OnClosed(EventArgs e)
        {
            IsClosed = true;
            base.OnClosed(e);
            Dispatcher.BeginInvoke(new Action(() => Finish(Choice.Cancel, Whole)));
        }

        // a drag (or a click) from one point to another, in the window's own units
        internal void Drag(Point from, Point to)
        {
            Rect box = Between(from, to);
            if (box.Width < ClickSize && box.Height < ClickSize) Finish(Choice.Screen, Whole);
            else Finish(Choice.Box, ToPixels(box, new Size(root.ActualWidth, root.ActualHeight), Shot.PixelWidth, Shot.PixelHeight));
        }

        // A box in the window's units as real pixels of the shot. The window covers the screen
        // exactly, so the shot's size over the window's size is the screen's scaling, whatever
        // WPF makes of it.
        internal static Int32Rect ToPixels(Rect box, Size window, int pixelWidth, int pixelHeight)
        {
            double sx = window.Width > 0 ? pixelWidth / window.Width : 1, sy = window.Height > 0 ? pixelHeight / window.Height : 1;
            int x0 = (int)Math.Max(0, Math.Floor(box.Left * sx)), y0 = (int)Math.Max(0, Math.Floor(box.Top * sy));
            int x1 = (int)Math.Min(pixelWidth, Math.Ceiling(box.Right * sx)), y1 = (int)Math.Min(pixelHeight, Math.Ceiling(box.Bottom * sy));
            return new Int32Rect(x0, y0, Math.Max(1, x1 - x0), Math.Max(1, y1 - y0));
        }

        // the tests' screenshot of a box being drawn
        internal void ShowBoxForShots(Rect box) => ShowBox(box);

        static Rect Between(Point a, Point b) => new Rect(new Point(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y)), new Point(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y)));

        // dims everything but the box
        void ShowBox(Rect? box)
        {
            var all = new Rect(0, 0, root.ActualWidth, root.ActualHeight);
            var veilShape = new GeometryGroup { FillRule = FillRule.EvenOdd };
            veilShape.Children.Add(new RectangleGeometry(all));
            if (box.HasValue && box.Value.Width > 0 && box.Value.Height > 0)
            {
                veilShape.Children.Add(new RectangleGeometry(box.Value));
                Canvas.SetLeft(outline, box.Value.X - 1);
                Canvas.SetTop(outline, box.Value.Y - 1);
                outline.Width = box.Value.Width + 2;
                outline.Height = box.Value.Height + 2;
                outline.Visibility = Visibility.Visible;
            }
            else
            {
                outline.Visibility = Visibility.Collapsed;
            }
            veil.Data = veilShape;
        }

        void Finish(Choice choice, Int32Rect box)
        {
            if (done) return;
            done = true;
            Chosen?.Invoke(this, choice, box);
        }
    }
}
