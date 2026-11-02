using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace B0r3dDeRes
{
    // The preview. The picture is drawn here at the screen's own pixels: at a zoom of k, every
    // machine pixel is exactly k × k screen pixels (k × its shape across, as on a TV), so it stays
    // a sharp block on any screen, the 150 % TV too. Fit picks the biggest whole k that shows it
    // all; only a picture too big for the screen at 1× is shrunk, smoothly. Drag to move around;
    // hold the picture (or Space) for the original, or turn Compare on and drag the divider.
    sealed class PixelView : FrameworkElement
    {
        // the picture: r, g, b a pixel, its size, and how wide its pixels are on a TV
        byte[] rgb;
        int pw, ph;
        double ratio = 1;   // pixel width : height as shown (1 for raw pixels)
        // the original photo framed the same way, for comparing (shown smooth)
        BitmapSource original;

        // zoom: screen pixels a machine pixel (down), and whether it follows the size (Fit)
        double zoom = 1;
        bool fit = true;
        // where the picture's top left is, in screen pixels from the view's top left
        double left, top;

        bool comparing, holding;
        double split = 0.5;   // the divider, 0-1 across the picture
        Color back = Colors.Black;

        WriteableBitmap screen;
        int[] canvas;

        // for the tests: a pretend screen scaling (1.5 = 150 %)
        internal double? ScaleOverride;

        public event Action ZoomChanged;

        public PixelView()
        {
            Focusable = true;
            ClipToBounds = true;
            RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.NearestNeighbor);
            SnapsToDevicePixels = true;
            Cursor = Cursors.Arrow;
        }

        double Scale => ScaleOverride ?? VisualTreeHelper.GetDpi(this).DpiScaleX;

        public bool HasPicture => rgb != null;
        public bool Fit => fit;
        public double Zoom => zoom;
        public bool Comparing => comparing;
        public bool Holding => holding;
        internal bool ShowingOriginal => original != null && (holding || comparing);
        internal double Split => split;

        // the view's size in screen pixels
        int ViewW => Math.Max(0, (int)Math.Round(ActualWidth * Scale));
        int ViewH => Math.Max(0, (int)Math.Round(ActualHeight * Scale));

        public void SetBackground(Color colour)
        {
            back = colour;
            InvalidateVisual();
        }

        // A new picture (null: none). The zoom and place stay when the size does, so moving a
        // slider never moves the picture.
        public void Show(byte[] pixels, int width, int height, double pixelRatio)
        {
            bool sameSize = rgb != null && width == pw && height == ph && pixelRatio == ratio;
            rgb = pixels;
            pw = width;
            ph = height;
            ratio = pixelRatio;
            if (pixels == null) original = null;
            if (!sameSize) Place(fit ? FitZoom() : zoom, null);
            InvalidateVisual();
        }

        public void SetOriginal(BitmapSource picture)
        {
            original = picture;
            InvalidateVisual();
        }

        // ---- zoom ----

        // the biggest whole zoom that shows all of it, or less than 1 when even 1× is too big
        public double FitZoom()
        {
            if (rgb == null || ViewW == 0 || ViewH == 0) return 1;
            double room = Math.Min(ViewW / (pw * ratio), (double)ViewH / ph);
            return room >= 1 ? Math.Floor(room) : room;
        }

        static readonly double[] Steps = { 1, 2, 3, 4, 5, 6, 8, 10, 12, 16, 20, 24, 32, 48, 64 };

        public void ZoomToFit()
        {
            fit = true;
            Place(FitZoom(), null);
        }

        public void ZoomTo(double z)
        {
            fit = false;
            Place(z, null);
        }

        // one step in (+1) or out (-1), about a point in the view (null: its middle)
        public void ZoomStep(int direction, Point? about = null)
        {
            if (rgb == null) return;
            double next = zoom;
            if (direction > 0)
            {
                next = zoom < 1 ? 1 : Steps[Steps.Length - 1];
                foreach (double s in Steps) if (s > zoom + 1e-9) { next = s; break; }
            }
            else
            {
                double least = Math.Min(1, FitZoom());
                next = least;
                for (int i = Steps.Length - 1; i >= 0; i--) if (Steps[i] < zoom - 1e-9) { next = Math.Max(least, Steps[i]); break; }
            }
            fit = false;
            Place(next, about);
        }

        // The zoom's word: "4×", "Fit 3×", "Fit 62%".
        public string ZoomText
        {
            get
            {
                string z = zoom >= 1 ? zoom.ToString("0") + "×" : Math.Round(zoom * 100) + "%";
                return fit ? "Fit " + z : z;
            }
        }

        // Sets the zoom, keeping the picture's point under `about` (screen pixels) where it was,
        // then keeps the picture in view.
        void Place(double z, Point? about)
        {
            double old = zoom;
            double cx = about?.X * Scale ?? ViewW / 2.0, cy = about?.Y * Scale ?? ViewH / 2.0;
            double fx = (cx - left) / (pw * ratio * old), fy = (cy - top) / (ph * old);
            zoom = z;
            if (about == null && fit)
            {
                left = (ViewW - DrawnW) / 2.0;
                top = (ViewH - DrawnH) / 2.0;
            }
            else
            {
                left = cx - fx * pw * ratio * zoom;
                top = cy - fy * ph * zoom;
            }
            Keep();
            InvalidateVisual();
            ZoomChanged?.Invoke();
        }

        // the picture's size on screen, in screen pixels
        double DrawnW => Math.Max(1, Math.Round(pw * ratio * zoom));
        double DrawnH => Math.Max(1, Math.Round(ph * zoom));

        // Whole screen pixels, and in view: centred when it's smaller than the view, never
        // dragged off it when it's bigger.
        void Keep()
        {
            left = DrawnW <= ViewW ? Math.Floor((ViewW - DrawnW) / 2) : Math.Round(Math.Min(0, Math.Max(ViewW - DrawnW, left)));
            top = DrawnH <= ViewH ? Math.Floor((ViewH - DrawnH) / 2) : Math.Round(Math.Min(0, Math.Max(ViewH - DrawnH, top)));
        }

        public bool CanMove => rgb != null && (DrawnW > ViewW || DrawnH > ViewH);

        public void Move(double dx, double dy)
        {
            left += dx * Scale;
            top += dy * Scale;
            Keep();
            InvalidateVisual();
        }

        protected override void OnRenderSizeChanged(SizeChangedInfo info)
        {
            base.OnRenderSizeChanged(info);
            if (fit) Place(FitZoom(), null);
            else
            {
                Keep();
                InvalidateVisual();
            }
        }

        protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
        {
            base.OnDpiChanged(oldDpi, newDpi);
            screen = null;
            if (fit) Place(FitZoom(), null);
            InvalidateVisual();
        }

        // ---- compare ----

        public void SetComparing(bool on)
        {
            comparing = on;
            InvalidateVisual();
        }

        public void Hold(bool on)
        {
            if (holding == on) return;
            holding = on;
            InvalidateVisual();
        }

        public void SetSplit(double at)
        {
            split = Math.Min(1, Math.Max(0, at));
            InvalidateVisual();
        }

        // where the divider is across the view, in device-independent pixels
        double DividerX => (left + DrawnW * split) / Scale;

        // ---- drawing ----

        protected override void OnRender(DrawingContext dc)
        {
            int vw = ViewW, vh = ViewH;
            if (vw == 0 || vh == 0) return;
            double scale = Scale;
            if (screen == null || screen.PixelWidth != vw || screen.PixelHeight != vh || Math.Abs(screen.DpiX - 96 * scale) > 0.01)
            {
                screen = new WriteableBitmap(vw, vh, 96 * scale, 96 * scale, PixelFormats.Bgr32, null);
                canvas = new int[vw * vh];
            }
            Paint(canvas, vw, vh);
            screen.WritePixels(new Int32Rect(0, 0, vw, vh), canvas, vw * 4, 0);
            dc.DrawImage(screen, new Rect(0, 0, vw / scale, vh / scale));
            if (!ShowingOriginal) return;

            // the original over the picture, all of it while held, else left of the divider
            var where = new Rect(left / scale, top / scale, DrawnW / scale, DrawnH / scale);
            var group = new DrawingGroup();
            RenderOptions.SetBitmapScalingMode(group, BitmapScalingMode.HighQuality);
            group.Children.Add(new ImageDrawing(original, where));
            if (!holding) group.ClipGeometry = new RectangleGeometry(new Rect(where.X, where.Y, Math.Max(0, DividerX - where.X), where.Height));
            dc.DrawDrawing(group);
            if (!holding)
            {
                var line = new Pen(new SolidColorBrush(Color.FromArgb(230, 255, 255, 255)), 2);
                double x = Math.Round(DividerX * scale) / scale;
                dc.DrawLine(line, new Point(x, where.Top), new Point(x, where.Bottom));
                var grip = new Rect(x - 7, where.Top + where.Height / 2 - 18, 14, 36);
                dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(230, 255, 255, 255)), new Pen(new SolidColorBrush(Color.FromArgb(160, 0, 0, 0)), 1), grip, 4, 4);
            }
        }

        // The view's screen pixels: the background, and the picture at its zoom, nearest
        // neighbour from 1× up (whole blocks), averaged below 1×.
        internal void Paint(int[] into, int vw, int vh)
        {
            int bg = (back.R << 16) | (back.G << 8) | back.B;
            for (int i = 0; i < into.Length; i++) into[i] = bg;
            if (rgb == null) return;
            int x0 = (int)left, y0 = (int)top, dw = (int)DrawnW, dh = (int)DrawnH;
            int fromX = Math.Max(0, x0), toX = Math.Min(vw, x0 + dw), fromY = Math.Max(0, y0), toY = Math.Min(vh, y0 + dh);
            if (toX <= fromX || toY <= fromY) return;
            if (zoom < 1)
            {
                PaintSmall(into, vw, x0, y0, dw, dh, fromX, toX, fromY, toY);
                return;
            }
            // which picture column each screen column shows
            var column = new int[toX - fromX];
            for (int x = fromX; x < toX; x++) column[x - fromX] = Math.Min(pw - 1, (int)Math.Floor((x - x0) * (double)pw / dw));
            var row = new int[toX - fromX];
            int last = -1;
            for (int y = fromY; y < toY; y++)
            {
                int sy = Math.Min(ph - 1, (int)Math.Floor((y - y0) * (double)ph / dh));
                if (sy != last)
                {
                    for (int i = 0; i < row.Length; i++)
                    {
                        int s = (sy * pw + column[i]) * 3;
                        row[i] = (rgb[s] << 16) | (rgb[s + 1] << 8) | rgb[s + 2];
                    }
                    last = sy;
                }
                Array.Copy(row, 0, into, y * vw + fromX, row.Length);
            }
        }

        // a picture bigger than the view at 1×: each screen pixel the average of what it covers
        void PaintSmall(int[] into, int vw, int x0, int y0, int dw, int dh, int fromX, int toX, int fromY, int toY)
        {
            double sx = (double)pw / dw, sy = (double)ph / dh;
            for (int y = fromY; y < toY; y++)
            {
                int ya = (int)((y - y0) * sy), yb = Math.Min(ph, Math.Max(ya + 1, (int)((y - y0 + 1) * sy)));
                for (int x = fromX; x < toX; x++)
                {
                    int xa = (int)((x - x0) * sx), xb = Math.Min(pw, Math.Max(xa + 1, (int)((x - x0 + 1) * sx)));
                    int r = 0, g = 0, b = 0, n = 0;
                    for (int yy = ya; yy < yb; yy++)
                        for (int xx = xa; xx < xb; xx++)
                        {
                            int s = (yy * pw + xx) * 3;
                            r += rgb[s];
                            g += rgb[s + 1];
                            b += rgb[s + 2];
                            n++;
                        }
                    into[y * vw + x] = ((r / n) << 16) | ((g / n) << 8) | (b / n);
                }
            }
        }

        // ---- the mouse: hold for the original, drag to move or to move the divider ----

        Point pressed;
        bool down, dragging, onDivider;

        bool NearDivider(Point p) => comparing && original != null && Math.Abs(p.X - DividerX) <= 8;

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            if (rgb == null) return;
            Focus();
            pressed = e.GetPosition(this);
            down = true;
            dragging = false;
            onDivider = NearDivider(pressed);
            CaptureMouse();
            if (onDivider) SetSplit((pressed.X - left / Scale) / (DrawnW / Scale));
            else if (original != null && !comparing) Hold(true);
            e.Handled = true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            Point now = e.GetPosition(this);
            if (!down)
            {
                Cursor = NearDivider(now) ? Cursors.SizeWE : CanMove ? Cursors.SizeAll : Cursors.Arrow;
                return;
            }
            if (onDivider)
            {
                SetSplit((now.X - left / Scale) / (DrawnW / Scale));
                return;
            }
            if (!dragging && (Math.Abs(now.X - pressed.X) >= SystemParameters.MinimumHorizontalDragDistance || Math.Abs(now.Y - pressed.Y) >= SystemParameters.MinimumVerticalDragDistance))
            {
                dragging = true;
                Hold(false);  // moving, not looking
            }
            if (dragging)
            {
                Move(now.X - pressed.X, now.Y - pressed.Y);
                pressed = now;
            }
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonUp(e);
            End();
        }

        protected override void OnLostMouseCapture(MouseEventArgs e)
        {
            base.OnLostMouseCapture(e);
            End();
        }

        void End()
        {
            if (!down) return;
            down = false;
            dragging = false;
            onDivider = false;
            if (IsMouseCaptured) ReleaseMouseCapture();
            Hold(false);
        }

        protected override void OnMouseWheel(MouseWheelEventArgs e)
        {
            base.OnMouseWheel(e);
            if (rgb == null) return;
            ZoomStep(e.Delta > 0 ? 1 : -1, e.GetPosition(this));
            e.Handled = true;
        }
    }
}
