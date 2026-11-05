using System;
using System.Diagnostics;
using System.Globalization;
using System.Windows.Controls;
using System.Windows.Threading;
using B0r3dDeRes.Engine;

namespace B0r3dDeRes
{
    // The ASCII studio's dial-up modem (the website's AsciiStage): it types the art out a
    // character at a time at a baud rate, a blinking cursor where the next one goes, as a BBS
    // came down a phone line. Every cell is a character, and the end of each row but the last is
    // one more (the line break). It never types while a GIF or a video plays: dialling pauses
    // them (onDial), and their playing starts it showing the art whole.
    sealed class AsciiModem
    {
        // the speeds, as the website lists them, and the characters a second each sends
        public static readonly (string Id, string Text, int PerSecond)[] Speeds =
        {
            ("instant", "Instant (Full)", 1000000), ("28800", "28.8k baud", 2880), ("14400", "14.4k baud", 1440), ("9600", "9600 baud", 960),
            ("2400", "2400 baud", 240), ("1200", "1200 baud", 120), ("300", "300 baud (Vintage)", 30),
        };

        const string Instant = "instant";

        readonly PixelView view;
        readonly Button dialButton;
        readonly TextBlock progress;
        readonly Action onDial;
        readonly DispatcherTimer timer;
        readonly Stopwatch clock = new Stopwatch();

        AsciiArt art;
        byte[] whole, typed;    // the art's picture at 1×, and what's been typed of it
        int width, height;
        int index;              // characters sent
        double last;            // when the last step was, in milliseconds
        bool stepped;           // a step has been (last says when)
        int cursorCell = -1;    // where the cursor's drawn (-1: nowhere)
        bool cursorShown;

        public string Speed { get; private set; } = Instant;
        public bool Typing { get; private set; }
        public int Sent => index;
        public int Total => art == null ? 0 : art.Width * art.Height + art.Height - 1;

        public AsciiModem(PixelView view, Button dialButton, TextBlock progress, Action onDial)
        {
            this.view = view;
            this.dialButton = dialButton;
            this.progress = progress;
            this.onDial = onDial;
            timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(15) };
            timer.Tick += (s, e) => Step();
        }

        // New art (a new picture or new settings): typed out when a speed is picked, else shown whole.
        public void Show(AsciiArt next, byte[] rgb, bool instant)
        {
            HangUp();
            Put(next, rgb);
            if (instant || Speed == Instant)
            {
                ShowWhole();
                index = Total;
            }
            else
            {
                index = 0;
                Dial();
            }
            UpdateProgress();
        }

        // A frame of a GIF or a video (playing, or picked on its bar): always shown whole, at once.
        public void ShowFrame(AsciiArt next, byte[] rgb)
        {
            HangUp();
            Put(next, rgb);
            ShowWhole();
            index = Total;
            UpdateProgress();
        }

        // a GIF or a video starts or stops playing: starting stops the typing (what it had typed shows whole)
        public void SetPlaying(bool on)
        {
            if (!on || !Typing) return;
            HangUp();
            if (art == null) return;
            ShowWhole();
            index = Total;
            UpdateProgress();
        }

        void Put(AsciiArt next, byte[] rgb)
        {
            art = next;
            whole = rgb;
            AsciiRender.Size(next.Width, next.Height, 1, out width, out height);
            typed = null;
            cursorCell = -1;
        }

        void ShowWhole()
        {
            typed = null;
            cursorCell = -1;
            view.Show(whole, width, height, 1);
        }

        public void SetSpeed(string id)
        {
            if (Array.FindIndex(Speeds, s => s.Id == id) < 0 || id == Speed) return;
            Speed = id;
            if (Speed == Instant)
            {
                HangUp();
                if (art != null) ShowWhole();
                index = Total;
                UpdateProgress();
            }
            else Redial();
        }

        // Dial up: types from where it got to (from the start once it's all there); Hang up stops there.
        public void ToggleDial()
        {
            if (Typing)
            {
                HangUp();
                return;
            }
            if (index >= Total) index = 0;
            if (Speed == Instant) Speed = "9600";
            Dial();
        }

        public void Redial()
        {
            HangUp();
            index = 0;
            if (Speed == Instant) Speed = "9600";
            Dial();
        }

        void Dial()
        {
            if (Typing || art == null || Total == 0) return;
            Typing = true;
            dialButton.Content = "Hang up";
            // what's been sent so far, on black
            typed = new byte[whole.Length];
            for (int p = 0; p < index; p++) Reveal(p);
            cursorCell = -1;
            stepped = false;
            clock.Restart();
            timer.Start();
            onDial();
            Draw();
        }

        // stops typing where it got to
        public void HangUp()
        {
            Typing = false;
            dialButton.Content = "Dial up";
            timer.Stop();
            stepped = false;
        }

        int PerSecond => Speeds[Array.FindIndex(Speeds, s => s.Id == Speed)].PerSecond;

        // a step of typing: as many characters as the speed sends in the time since the last (at least one)
        void Step()
        {
            if (!Typing) return;
            double now = clock.Elapsed.TotalMilliseconds;
            if (!stepped) last = now;
            stepped = true;
            double elapsed = Math.Max(0, (now - last) / 1000);
            last = now;
            int total = Total;
            int add = (int)Math.Max(1, JsMath.Round(PerSecond * elapsed));
            int to = Math.Min(total, index + add);
            // the cursor off first: the cell it's on may be one of those typed now
            HideCursor();
            for (int p = index; p < to; p++) Reveal(p);
            index = to;
            if (index >= total)
            {
                HangUp();
                ShowWhole();
            }
            else Draw();
            UpdateProgress();
        }

        // for the tests: a step as though `ms` milliseconds had gone by
        internal void StepFor(double ms)
        {
            if (!Typing) return;
            if (!stepped) last = clock.Elapsed.TotalMilliseconds;
            stepped = true;
            last -= ms;
            Step();
        }

        // character p of what's sent: its cell (the line breaks show nothing)
        void Reveal(int p)
        {
            int perRow = art.Width + 1, r = p / perRow, c = p % perRow;
            if (c >= art.Width) return;
            Copy(r * art.Width + c, whole);
        }

        // copies a cell's pixels from `source` into what's typed
        void Copy(int cell, byte[] source)
        {
            int cx = cell % art.Width, cy = cell / art.Width;
            for (int y = 0; y < AsciiRender.CellHeight; y++)
            {
                int row = ((cy * AsciiRender.CellHeight + y) * width + cx * AsciiRender.CellWidth) * 3;
                Buffer.BlockCopy(source, row, typed, row, AsciiRender.CellWidth * 3);
            }
        }

        // the cell the next character goes in (after a line break, the next row's first), or -1
        int NextCell()
        {
            if (index >= Total) return -1;
            int perRow = art.Width + 1, r = index / perRow, c = index % perRow;
            if (c >= art.Width) return r + 1 < art.Height ? (r + 1) * art.Width : -1;
            return r * art.Width + c;
        }

        // the cursor's cell back to what it is: black, as it's not been typed yet
        void HideCursor()
        {
            if (cursorCell >= 0 && cursorShown) Blank(cursorCell);
            cursorShown = false;
        }

        // what's typed, with the cursor (a cyan █ that blinks: on for 0.4 s, off for 0.4 s)
        void Draw()
        {
            if (typed == null) return;
            HideCursor();
            cursorCell = NextCell();
            cursorShown = cursorCell >= 0 && clock.ElapsedMilliseconds % 800 < 400;
            if (cursorShown) Cursor(cursorCell);
            view.Show(typed, width, height, 1);
        }

        void Blank(int cell)
        {
            int cx = cell % art.Width, cy = cell / art.Width;
            for (int y = 0; y < AsciiRender.CellHeight; y++)
                Array.Clear(typed, ((cy * AsciiRender.CellHeight + y) * width + cx * AsciiRender.CellWidth) * 3, AsciiRender.CellWidth * 3);
        }

        void Cursor(int cell)
        {
            int cx = cell % art.Width, cy = cell / art.Width;
            for (int y = 0; y < AsciiRender.CellHeight; y++)
                for (int x = 0; x < AsciiRender.CellWidth; x++)
                {
                    int o = ((cy * AsciiRender.CellHeight + y) * width + cx * AsciiRender.CellWidth + x) * 3;
                    typed[o] = 0;
                    typed[o + 1] = 255;
                    typed[o + 2] = 255;
                }
        }

        // "  42% (1234/2943) · 9600 baud", padded to its widest so the buttons beside it stay put
        void UpdateProgress()
        {
            int total = Total;
            if (total == 0)
            {
                progress.Text = "";
                return;
            }
            int pct = (int)JsMath.Round((double)index / total * 100);
            string label = Speed == Instant ? "Instant" : Speed + " baud";
            string count = index.ToString(CultureInfo.InvariantCulture).PadLeft(total.ToString(CultureInfo.InvariantCulture).Length);
            progress.Text = pct.ToString(CultureInfo.InvariantCulture).PadLeft(3) + "% (" + count + "/" + total + ") · " + label;
        }

        internal string ProgressText => progress.Text;
    }
}
