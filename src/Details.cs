using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace UsageLLMs
{
    internal sealed class Details : FloatingWindow
    {
        private const int CardWidth = 256;
        private const int TailLength = 9;
        private const int NormalHeight = 165;
        private const int MessageTop = 42;
        private const int QuotaMessageTop = 156;
        private const int ContentPadding = 16;
        private const int ContentWidth = CardWidth - 2 * ContentPadding;
        private const TextFormatFlags TextStyle = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix;
        private const TextFormatFlags MessageStyle = TextStyle | TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl;
        private readonly Font heading = new Font("Segoe UI Semibold", 12.5f, FontStyle.Regular);
        private readonly Font label = new Font("Segoe UI", 9.75f, FontStyle.Regular);
        private readonly Font value = new Font("Segoe UI Semibold", 10.5f, FontStyle.Regular);
        private readonly Font small = new Font("Segoe UI", 9, FontStyle.Regular);
        private GraphicsPath outline;
        private RectangleF body;
        private Edge drawnSide;
        private int drawnHeight;
        private int drawnAnchor;
        private Usage reading;
        private bool stale;
        private string title = "Uso do Codex";
        private string notice;
        private int noticeHeight;
        private int cardHeight = NormalHeight;
        private Color shortColor = Palette.Green;
        private Color weeklyColor = Palette.Blue;

        public Details() : base(CardWidth, NormalHeight, 14)
        {
            AccessibleName = "Detalhes do uso do Codex";
        }

        public void Update(string provider, Usage data, bool old, string message, Placement settings)
        {
            title = provider == "Codex" ? "Uso do Codex" : provider;
            AccessibleName = title;
            AccessibleDescription = Describe(provider, data, message);
            reading = data;
            stale = old;
            notice = message;
            shortColor = settings.ShortColor;
            weeklyColor = settings.WeeklyColor;
            if (notice == null)
                cardHeight = HasQuota(data) ? NormalHeight : MessageTop + ContentPadding;
            else
            {
                var measured = TextRenderer.MeasureText(notice, small,
                    new Size(Pixel(ContentWidth), int.MaxValue), MessageStyle);
                noticeHeight = (int)Math.Ceiling(measured.Height * 96.0 / Pixel(96));
                int textTop = HasQuota(data) ? QuotaMessageTop : MessageTop;
                cardHeight = textTop + noticeHeight + ContentPadding;
            }
        }

        public Size MeasureFor(Edge widgetEdge)
        {
            bool lateral = DockFace.IsLateral(widgetEdge);
            return new Size(Pixel(CardWidth + (lateral ? TailLength : 0)),
                Pixel(cardHeight + (lateral ? 0 : TailLength)));
        }

        public void PlaceAt(Edge widgetEdge, Size size, Point location, Point providerCenter)
        {
            Edge side = widgetEdge;
            bool lateral = DockFace.IsLateral(widgetEdge);
            int width = CardWidth + (lateral ? TailLength : 0);
            int height = cardHeight + (lateral ? 0 : TailLength);
            int anchor = lateral
                ? (int)Math.Round((providerCenter.Y - location.Y) * (double)height / size.Height)
                : (int)Math.Round((providerCenter.X - location.X) * (double)width / size.Width);

            if (outline == null || drawnSide != side || drawnHeight != cardHeight || drawnAnchor != anchor)
            {
                float x = side == Edge.Left ? TailLength : 0;
                float y = side == Edge.Top ? TailLength : 0;
                body = new RectangleF(x + 0.5f, y + 0.5f, CardWidth - 1, cardHeight - 1);
                if (outline != null)
                    outline.Dispose();
                outline = BalloonOutline(body, side, anchor);
                using (var nativeShape = (GraphicsPath)outline.Clone())
                    SetShapedSurface(size, nativeShape, width, height);
                drawnSide = side;
                drawnHeight = cardHeight;
                drawnAnchor = anchor;
            }
            Location = location;
            if (Visible)
                Invalidate();
        }

        private static GraphicsPath BalloonOutline(RectangleF body, Edge side, int anchor)
        {
            const float radius = 14;
            const float diameter = 2 * radius;
            float x = body.Left;
            float y = body.Top;
            float right = body.Right;
            float bottom = body.Bottom;
            var path = new GraphicsPath();
            AddSide(path, new PointF(x + radius, y), new PointF(right - radius, y), side == Edge.Top, anchor);
            path.AddArc(right - diameter, y, diameter, diameter, 270, 90);
            AddSide(path, new PointF(right, y + radius), new PointF(right, bottom - radius), side == Edge.Right, anchor);
            path.AddArc(right - diameter, bottom - diameter, diameter, diameter, 0, 90);
            AddSide(path, new PointF(right - radius, bottom), new PointF(x + radius, bottom), side == Edge.Bottom, anchor);
            path.AddArc(x, bottom - diameter, diameter, diameter, 90, 90);
            AddSide(path, new PointF(x, bottom - radius), new PointF(x, y + radius), side == Edge.Left, anchor);
            path.AddArc(x, y, diameter, diameter, 180, 90);
            path.CloseFigure();
            return path;
        }

        private static void AddSide(GraphicsPath path, PointF from, PointF to, bool tail, int anchor)
        {
            if (!tail)
            {
                path.AddLine(from, to);
                return;
            }

            const float half = 9;
            float dx = Math.Sign(to.X - from.X);
            float dy = Math.Sign(to.Y - from.Y);
            float length = (to.X - from.X) * dx + (to.Y - from.Y) * dy;
            float requested = dx != 0 ? (anchor - from.X) * dx : (anchor - from.Y) * dy;
            float center = Math.Max(half + 2, Math.Min(length - half - 2, requested));
            PointF left = TailPoint(from, dx, dy, center - half, 0);
            PointF right = TailPoint(from, dx, dy, center + half, 0);
            PointF tip = TailPoint(from, dx, dy, center, TailLength);
            path.AddLine(from, left);
            path.AddBezier(left, TailPoint(from, dx, dy, center - 5, 0),
                TailPoint(from, dx, dy, center - 2, TailLength), tip);
            path.AddBezier(tip, TailPoint(from, dx, dy, center + 2, TailLength),
                TailPoint(from, dx, dy, center + 5, 0), right);
            path.AddLine(right, to);
        }

        private static PointF TailPoint(PointF start, float dx, float dy, float along, float outward)
        {
            return new PointF(start.X + dx * along + dy * outward, start.Y + dy * along - dx * outward);
        }

        public static bool HasQuota(Usage data)
        {
            return data != null && (data.FiveHours != null || data.Weekly != null);
        }

        public static string Describe(string provider, Usage data, string message)
        {
            string quotas = HasQuota(data)
                ? " · 5 horas " + Palette.Percent(data.FiveHours) + " · Semanal " + Palette.Percent(data.Weekly)
                : "";
            return provider + quotas + (message == null ? "" : " · " + message);
        }

        protected override void OnPaint(PaintEventArgs args)
        {
            base.OnPaint(args);
            if (outline == null)
                return;

            var g = args.Graphics;
            using (var line = new Pen(Palette.Outline, 1.25f))
                g.DrawPath(line, outline);
            g.ResetTransform();
            Write(g, title, heading, Palette.Text, ContentPadding, 11, ContentWidth, 27);

            bool showQuota = HasQuota(reading);
            if (showQuota)
            {
                Row(g, 46, "5 horas", reading.FiveHours, shortColor);
                Row(g, 101, "Semanal", reading.Weekly, weeklyColor);
            }
            if (notice != null)
                Write(g, notice, small, showQuota ? Palette.Yellow : Palette.Muted, ContentPadding,
                    showQuota ? QuotaMessageTop : MessageTop, ContentWidth, noticeHeight + 1, MessageStyle);
        }

        private void Row(Graphics g, int y, string name, Quota quota, Color baseColor)
        {
            Color color = Palette.Level(quota, baseColor, stale);
            Write(g, name, label, Palette.Text, ContentPadding, y, 150, 19);
            Write(g, Palette.Percent(quota), value, color, ContentPadding + 155, y - 1, ContentWidth - 155, 20,
                TextStyle | TextFormatFlags.SingleLine | TextFormatFlags.Right);
            DrawBar(g, y + 22, ContentWidth, Palette.Track);
            if (quota != null && quota.Used > 0)
                DrawBar(g, y + 22, ContentWidth * (float)quota.Used / 100, color);

            string reset = "Sem leitura para esta janela";
            if (quota != null)
            {
                if (!quota.Reset.HasValue)
                    reset = "Horário de renovação indisponível";
                else if (quota.Reset.Value <= DateTimeOffset.Now)
                    reset = "Aguardando confirmação da renovação";
                else
                    reset = quota.Reset.Value.ToLocalTime().ToString("'Renova em' dd/MM 'às' HH:mm", Palette.Portuguese);
            }
            Write(g, reset, small, Palette.Muted, ContentPadding, y + 34, ContentWidth, 18);
        }

        private void DrawBar(Graphics g, float y, float width, Color color)
        {
            float height = Pixel(5);
            float deviceWidth = Pixel(width);
            if (deviceWidth <= 0)
                return;
            using (var shape = Rounded(Pixel(body.X + ContentPadding), Pixel(body.Y + y),
                deviceWidth, height, Math.Min(deviceWidth, height) / 2))
            using (var brush = new SolidBrush(color))
                g.FillPath(brush, shape);
        }

        private void Write(Graphics g, string text, Font font, Color color, float x, float y,
            float width, float height, TextFormatFlags flags = TextStyle | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis)
        {
            var bounds = new Rectangle(Pixel(body.X + x), Pixel(body.Y + y), Pixel(width), Pixel(height));
            TextRenderer.DrawText(g, text, font, bounds, color, flags | TextFormatFlags.PreserveGraphicsClipping);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (outline != null)
                    outline.Dispose();
                heading.Dispose();
                label.Dispose();
                value.Dispose();
                small.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
