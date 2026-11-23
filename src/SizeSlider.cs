using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace UsageLLMs
{
    internal sealed class SizeSlider : TrackBar
    {
        private const int GetThumbRect = 0x0400 + 25;
        private const int GetChannelRect = 0x0400 + 26;
        private int notifiedValue;

        public SizeSlider()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            AutoSize = false;
            TickStyle = TickStyle.None;
            Cursor = Cursors.Hand;
            BackColor = Palette.Surface;
            MouseEnter += (sender, args) => Invalidate();
            MouseLeave += (sender, args) => Invalidate();
            GotFocus += (sender, args) => Invalidate();
            LostFocus += (sender, args) => Invalidate();
        }

        protected override void OnValueChanged(EventArgs args)
        {
            notifiedValue = Value;
            base.OnValueChanged(args);
            Invalidate();
        }

        protected override void OnHandleCreated(EventArgs args)
        {
            base.OnHandleCreated(args);
            notifiedValue = Value;
        }

        protected override void WndProc(ref Message message)
        {
            const int KeyDown = 0x0100;
            const int TimerTick = 0x0113;
            const int MouseMove = 0x0200;
            const int LeftButtonDown = 0x0201;
            const int LeftButtonUp = 0x0202;
            const int MouseWheel = 0x020A;
            bool trackInput = message.Msg == KeyDown || message.Msg == LeftButtonDown ||
                message.Msg == LeftButtonUp || message.Msg == MouseWheel ||
                (message.Msg == MouseMove && (message.WParam.ToInt64() & 1) != 0) ||
                (message.Msg == TimerTick && Capture);

            base.WndProc(ref message);
            // A user-painted native trackbar can move without a reflected scroll notification.
            if (trackInput && IsHandleCreated && Value != notifiedValue)
                OnValueChanged(EventArgs.Empty);
        }

        protected override void OnPaint(PaintEventArgs args)
        {
            Rect thumb, channel;
            SendMessage(Handle, GetThumbRect, IntPtr.Zero, out thumb);
            SendMessage(Handle, GetChannelRect, IntPtr.Zero, out channel);

            var g = args.Graphics;
            g.Clear(BackColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;

            float radius = Height * 0.215f;
            float y = (thumb.Top + thumb.Bottom) / 2f;
            float x = (thumb.Left + thumb.Right) / 2f;
            float from = Math.Min(channel.Left + radius, x);
            float to = Math.Max(channel.Right - radius, x);

            using (var track = new Pen(Palette.Track, Height * 0.115f)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round
            })
            {
                g.DrawLine(track, from, y, to, y);
                if (Value > Minimum)
                {
                    track.Color = Palette.Green;
                    g.DrawLine(track, from, y, x, y);
                }
            }

            if (Focused || ClientRectangle.Contains(PointToClient(MousePosition)))
            {
                float haloRadius = radius + Height * 0.10f;
                using (var halo = new SolidBrush(Color.FromArgb(Focused ? 58 : 32, Palette.Green)))
                    g.FillEllipse(halo, x - haloRadius, y - haloRadius, 2 * haloRadius, 2 * haloRadius);
            }
            using (var grip = new SolidBrush(Palette.Text))
                g.FillEllipse(grip, x - radius, y - radius, 2 * radius, 2 * radius);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Rect { public int Left, Top, Right, Bottom; }

        [DllImport("user32.dll", EntryPoint = "SendMessageW")]
        private static extern IntPtr SendMessage(IntPtr handle, int message, IntPtr parameter, out Rect result);
    }
}
