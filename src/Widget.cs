using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace UsageLLMs
{
    internal sealed class Widget : FloatingWindow
    {
        private const float ActivityGlowRadius = 17;
        private const string NoUsage = "—";
        private readonly Placement position = Placement.Load();
        private readonly CodexConnection connection = new CodexConnection();
        private readonly Timer poll = new Timer();
        private readonly Timer hover = new Timer { Interval = 220 };
        private readonly Timer pulse = new Timer { Interval = 33 };
        private readonly Stopwatch pulseAge = new Stopwatch();
        private readonly Timer activityPulse = new Timer { Interval = 100 };
        private readonly Stopwatch activityAge = new Stopwatch();
        private readonly Details details = new Details();
        private readonly Bitmap logo;
        private readonly Bitmap providerMarks;
        private ActivityMonitor activity;
        private int activityGeneration;
        private Provider[] visibleProviders;
        private int codexCell;
        private bool codexActive;
        private GraphicsPath activityGlowShape;
        private PathGradientBrush activityGlowBrush;
        private Usage reading;
        private string usageLabel = NoUsage;
        private Font usageLabelFont;
        private string error;
        private string preferenceError;
        private string pendingPlacement;
        private bool savingPlacement;
        private bool closeAfterSaving;
        private bool refreshing;
        private bool closing;
        private bool pressed;
        private bool dragging;
        private Rectangle dragThreshold;
        private Preferences preferences;
        private int faceSize = 68;
        private int shapedSize;
        private Edge shapedEdge;
        private PointF faceCenter;
        private PointF[] providerCenters;
        private int hoveredProvider = -1;
        private int failures;
        private readonly Stopwatch lastAttempt = new Stopwatch();

        public Widget() : base(68, 68, 19)
        {
            Text = "Usage LLMs";
            AccessibleName = Text;
            AccessibleRole = AccessibleRole.Indicator;
            Cursor = Cursors.Hand;
            logo = LoadBitmap("chatgpt-logo.png");
            providerMarks = LoadBitmap("provider-marks.png");
            LoadVisibleProviders();

            poll.Tick += (sender, args) =>
            {
                poll.Stop();
                RefreshUsage(false);
            };
            pulse.Tick += (sender, args) =>
            {
                if (pulseAge.ElapsedMilliseconds >= 540)
                    pulse.Stop();
                if (codexCell >= 0)
                    Invalidate(CellBounds(codexCell));
            };
            activityPulse.Tick += (sender, args) =>
            {
                float scale = faceSize / (float)DockFace.Width;
                float padding = ActivityGlowRadius + 2;
                int extent = Pixel(2 * padding * scale) + 2;
                Invalidate(new Rectangle(Pixel((faceCenter.X - padding) * scale),
                    Pixel((faceCenter.Y - padding) * scale), extent, extent));
            };
            hover.Tick += (sender, args) =>
            {
                hover.Stop();
                if (!pressed && preferences == null && hoveredProvider >= 0)
                    ShowDetails();
            };
            MouseEnter += (sender, args) => { if (!pressed) TrackHover(PointToClient(Cursor.Position)); };
            MouseLeave += (sender, args) =>
            {
                hover.Stop();
                details.Hide();
                hoveredProvider = -1;
            };
            KeyDown += (sender, args) =>
            {
                if (args.KeyCode == Keys.Enter)
                {
                    if (visibleProviders[0] == Provider.None)
                        OpenPreferences();
                    else
                        RefreshUsage(true);
                }
                if (args.KeyCode == Keys.Apps || (args.Shift && args.KeyCode == Keys.F10))
                    OpenPreferences();
            };
        }

        private static Bitmap LoadBitmap(string name)
        {
            using (var stream = typeof(Widget).Assembly.GetManifestResourceStream(name))
            using (var asset = new Bitmap(stream))
                return new Bitmap(asset);
        }

        protected override void OnMouseDown(MouseEventArgs args)
        {
            base.OnMouseDown(args);
            if (args.Button != MouseButtons.Left)
                return;
            hover.Stop();
            details.Hide();
            var pointer = PointToScreen(args.Location);
            var threshold = SystemInformation.DragSize;
            dragThreshold = new Rectangle(pointer.X - threshold.Width / 2, pointer.Y - threshold.Height / 2,
                threshold.Width, threshold.Height);
            pressed = true;
            Capture = true;
        }

        protected override void OnMouseMove(MouseEventArgs args)
        {
            base.OnMouseMove(args);
            if (pressed)
                MoveDrag(PointToScreen(args.Location));
            else
                TrackHover(args.Location);
        }

        private void MoveDrag(Point pointer)
        {
            if (!dragging && dragThreshold.Contains(pointer))
                return;
            dragging = true;
            var target = position.DragTo(pointer, FaceDimensions(faceSize, Edge.Right),
                FaceDimensions(faceSize, Edge.Top));
            UpdateShape();
            Location = target;
        }

        protected override void OnMouseUp(MouseEventArgs args)
        {
            base.OnMouseUp(args);
            if (args.Button == MouseButtons.Right && !pressed)
                OpenPreferences();
            if (args.Button != MouseButtons.Left || !pressed)
                return;

            MoveDrag(PointToScreen(args.Location));
            bool moved = FinishDrag();
            Capture = false;
            int cell = ProviderAt(args.Location);
            if (!moved && cell >= 0)
            {
                if (visibleProviders[cell] == Provider.Codex)
                    RefreshUsage(true);
                else if (visibleProviders[cell] == Provider.None)
                    OpenPreferences();
            }
            TrackHover(PointToClient(Cursor.Position));
        }

        protected override void OnMouseCaptureChanged(EventArgs args)
        {
            base.OnMouseCaptureChanged(args);
            if (pressed && !Capture)
                FinishDrag();
        }

        private bool FinishDrag()
        {
            pressed = false;
            if (!dragging)
                return false;
            dragging = false;
            SavePlacement();
            return true;
        }

        protected override void OnLoad(EventArgs args)
        {
            base.OnLoad(args);
            ApplySize();
        }

        protected override void OnShown(EventArgs args)
        {
            base.OnShown(args);
            ConfigureCodex();
        }

        private void ConfigureCodex()
        {
            if (codexCell >= 0)
            {
                if (activity == null)
                {
                    int generation = ++activityGeneration;
                    activity = new ActivityMonitor(active => ActivityChanged(generation, active));
                    RefreshUsage(false);
                }
                return;
            }

            poll.Stop();
            pulse.Stop();
            activityPulse.Stop();
            activityAge.Stop();
            codexActive = false;
            ReleaseActivityGlow();
            if (activity != null)
            {
                activity.Dispose();
                activity = null;
                activityGeneration++;
            }
        }

        private void ActivityChanged(int generation, bool active)
        {
            try
            {
                BeginInvoke((Action)(() =>
                {
                    if (!closing && codexCell >= 0 && generation == activityGeneration && codexActive != active)
                    {
                        codexActive = active;
                        if (active)
                        {
                            activityAge.Restart();
                            activityPulse.Start();
                        }
                        else
                        {
                            activityPulse.Stop();
                            activityAge.Stop();
                            ReleaseActivityGlow();
                        }
                        Render();
                    }
                }));
            }
            catch (InvalidOperationException) { }
        }

        protected override void WndProc(ref Message message)
        {
            base.WndProc(ref message);
            if ((message.Msg == 0x007E || message.Msg == 0x001A) && Visible && !closing)
                BeginInvoke((Action)DockToEdge);
        }

        private void DockToEdge()
        {
            UpdateShape();
            Location = position.Locate(Size);
            hover.Stop();
            details.Hide();
            hoveredProvider = -1;
        }

        private int ProviderAt(Point point)
        {
            if (providerCenters == null || !ClientRectangle.Contains(point) || !Region.IsVisible(point))
                return -1;
            bool lateral = DockFace.IsLateral(position.Edge);
            float scale = faceSize / (float)DockFace.Width;
            int first = Pixel((lateral ? providerCenters[0].Y : providerCenters[0].X) * scale);
            float index = ((lateral ? point.Y : point.X) - first) / (float)Pixel(DockFace.CellStep(position.Edge, ShowsUsage) * scale);
            return Math.Max(0, Math.Min(visibleProviders.Length - 1, (int)Math.Round(index)));
        }

        private void TrackHover(Point point)
        {
            if (preferences != null)
                return;
            int provider = ProviderAt(point);
            if (provider != hoveredProvider)
            {
                hoveredProvider = provider;
                hover.Stop();
                details.Hide();
            }
            if (provider >= 0 && !details.Visible && !hover.Enabled)
                hover.Start();
        }

        private void ShowDetails()
        {
            UpdateDetails();
            if (!details.Visible)
                details.Show(this);
        }

        private void UpdateDetails()
        {
            Provider provider = visibleProviders[hoveredProvider];
            string name = Providers.Name(provider);
            string message;
            switch (provider)
            {
                case Provider.None:
                    message = "Ative uma conta nas configurações para ver as cotas.";
                    break;
                case Provider.Codex:
                    message = preferenceError ?? error;
                    if (message == null && !Details.HasQuota(reading))
                        message = refreshing || reading == null ? "Consultando as cotas do Codex…"
                            : "A conta do Codex não retornou cotas de 5 horas ou semanais.";
                    break;
                default:
                    message = "Faça login no " + name + " para ver o consumo quando a integração estiver disponível.";
                    break;
            }
            bool isCodex = provider == Provider.Codex;
            details.Update(name, isCodex ? reading : null, isCodex && error != null, message, position);
            var anchor = RectangleToScreen(CellBounds(hoveredProvider));
            var size = details.MeasureFor(position.Edge);
            details.PlaceAt(position.Edge, size, PopupLocation(size, anchor, 2),
                new Point(anchor.Left + anchor.Width / 2, anchor.Top + anchor.Height / 2));
        }

        private Rectangle CellBounds(int provider)
        {
            if (providerCenters == null)
                return ClientRectangle;
            bool lateral = DockFace.IsLateral(position.Edge);
            float sizeScale = faceSize / (float)DockFace.Width;
            int span = Pixel(DockFace.CellStep(position.Edge, ShowsUsage) * sizeScale);
            var center = providerCenters[provider];
            int offset = Pixel((lateral ? center.Y : center.X) * sizeScale) - span / 2;
            return lateral ? new Rectangle(0, offset, Width, span) : new Rectangle(offset, 0, span, Height);
        }

        private Point PopupLocation(Size popup, Rectangle anchor, int gapDip = 7)
        {
            var area = position.Screen.WorkingArea;
            int gap = Pixel(gapDip);
            int x = anchor.Left + (anchor.Width - popup.Width) / 2;
            int y = anchor.Top + (anchor.Height - popup.Height) / 2;
            switch (position.Edge)
            {
                case Edge.Left: x = anchor.Right + gap; break;
                case Edge.Right: x = anchor.Left - popup.Width - gap; break;
                case Edge.Top: y = anchor.Bottom + gap; break;
                case Edge.Bottom: y = anchor.Top - popup.Height - gap; break;
            }
            return new Point(
                Math.Max(area.Left, Math.Min(x, area.Right - popup.Width)),
                Math.Max(area.Top, Math.Min(y, area.Bottom - popup.Height)));
        }

        private void OpenPreferences()
        {
            if (preferences != null)
                return;
            hover.Stop();
            details.Hide();
            var dialog = new Preferences(position, ApplyPreferences, ApplyAccounts, SaveStartup, Close);
            preferences = dialog;
            dialog.FormClosed += (sender, args) =>
            {
                preferences = null;
                if (!closing)
                    TrackHover(PointToClient(Cursor.Position));
            };
            var maxSize = FaceDimensions(Placement.MaxSize, position.Edge);
            var anchor = Rectangle.Union(Bounds, new Rectangle(position.Locate(maxSize), maxSize));
            dialog.Location = PopupLocation(dialog.Size, anchor);
            dialog.Show(this);
        }

        private void ApplyPreferences()
        {
            if (faceSize != position.WidgetSize)
                ApplySize();
            SavePlacement();
        }

        private void LoadVisibleProviders()
        {
            visibleProviders = Providers.All.Where(position.ProviderEnabled).ToArray();
            if (visibleProviders.Length == 0)
                visibleProviders = new[] { Provider.None };
            codexCell = Array.IndexOf(visibleProviders, Provider.Codex);
        }

        private void ApplyAccounts()
        {
            LoadVisibleProviders();
            shapedSize = 0;
            DockToEdge();
            ConfigureCodex();
            SavePlacement();
        }

        private void SaveStartup(bool enabled)
        {
            preferenceError = WritePreference(() => Startup.Set(enabled), "Não foi possível alterar a inicialização.");
            Render();
        }

        private async void SavePlacement()
        {
            pendingPlacement = position.Serialize();
            Render();
            if (savingPlacement)
                return;

            savingPlacement = true;
            do
            {
                string snapshot = pendingPlacement;
                pendingPlacement = null;
                string failure = await Task.Run(() => WritePreference(() => Placement.Save(snapshot),
                    "Não foi possível salvar as preferências."));
                if (!closing && pendingPlacement == null && preferenceError != failure)
                {
                    preferenceError = failure;
                    Render();
                }
            } while (!closing && pendingPlacement != null);
            savingPlacement = false;
            if (closeAfterSaving && !closing)
                Close();
        }

        private void ApplySize()
        {
            faceSize = position.WidgetSize;
            if (usageLabelFont != null)
                usageLabelFont.Dispose();
            float scale = faceSize / (float)DockFace.Width;
            usageLabelFont = new Font("Segoe UI Semibold", Pixel(9.5f * scale), FontStyle.Regular, GraphicsUnit.Pixel);
            DockToEdge();
        }

        private bool ShowsUsage { get { return visibleProviders[0] != Provider.None; } }

        private Size FaceDimensions(int size, Edge edge)
        {
            float scale = size / (float)DockFace.Width;
            int depth = Pixel(DockFace.Depth(edge, ShowsUsage) * scale);
            int length = Pixel(DockFace.Length(edge, visibleProviders.Length, ShowsUsage) * scale);
            return DockFace.IsLateral(edge) ? new Size(depth, length) : new Size(length, depth);
        }

        private void UpdateShape()
        {
            if (shapedSize == faceSize && shapedEdge == position.Edge)
                return;
            shapedSize = faceSize;
            shapedEdge = position.Edge;
            bool lateral = DockFace.IsLateral(shapedEdge);
            ReleaseActivityGlow();
            int length = DockFace.Length(shapedEdge, visibleProviders.Length, ShowsUsage);
            int depth = DockFace.Depth(shapedEdge, ShowsUsage);
            using (var shape = DockFace.Create(shapedEdge, visibleProviders.Length, ShowsUsage, out providerCenters))
            {
                if (codexCell >= 0)
                    faceCenter = providerCenters[codexCell];
                SetShapedSurface(FaceDimensions(faceSize, shapedEdge), shape,
                    lateral ? depth : length, lateral ? length : depth);
            }
            Invalidate();
        }

        private static string WritePreference(Action save, string failure)
        {
            try { save(); return null; }
            catch (IOException) { return failure; }
            catch (UnauthorizedAccessException) { return failure; }
            catch (SecurityException) { return failure; }
        }

        private async void RefreshUsage(bool manual)
        {
            if (codexCell < 0)
                return;
            if (manual)
            {
                if (!refreshing && lastAttempt.IsRunning && lastAttempt.ElapsedMilliseconds < 3000)
                    return;
                pulseAge.Restart();
                pulse.Start();
                Invalidate(CellBounds(codexCell));
            }
            if (refreshing)
                return;
            if (!manual && failures == 0 && lastAttempt.IsRunning && lastAttempt.ElapsedMilliseconds < 3000)
            {
                poll.Start();
                return;
            }

            lastAttempt.Restart();
            bool recovering = failures > 0;
            refreshing = true;
            try
            {
                reading = await connection.Fetch();
                error = null;
                failures = 0;
            }
            catch (UsageException failure)
            {
                if (failure.RequiresLogin)
                    reading = null;
                error = failure.Message;
                failures++;
            }
            catch (Exception) { error = "Não foi possível consultar o Codex."; failures++; }
            finally
            {
                refreshing = false;
                if (!closing && codexCell >= 0)
                {
                    Render();
                    if (!poll.Enabled || recovering || failures > 0)
                    {
                        int seconds = failures == 0 ? 30 : Math.Min(300, 60 << Math.Min(failures - 1, 3));
                        poll.Stop();
                        poll.Interval = seconds * 1000;
                        poll.Start();
                    }
                }
            }
        }

        private void Render()
        {
            var quota = reading == null ? null : reading.FiveHours ?? reading.Weekly;
            usageLabel = quota == null ? NoUsage : quota.Used.ToString("0", Palette.Portuguese) + "%";
            if (details.Visible && hoveredProvider >= 0)
                UpdateDetails();
            AccessibleDescription = String.Join(" · ", visibleProviders.Select(provider =>
                provider == Provider.Codex
                    ? Details.Describe("Codex", reading, preferenceError ?? error) + (codexActive ? " · em atividade" : "")
                    : Providers.Name(provider)));
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs args)
        {
            base.OnPaint(args);
            if (providerCenters == null)
                return;
            var g = args.Graphics;
            float scale = faceSize / (float)DockFace.Width;
            g.ScaleTransform(scale, scale);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            for (int index = 0; index < providerCenters.Length; index++)
            {
                if (!args.ClipRectangle.IntersectsWith(CellBounds(index)))
                    continue;
                Provider provider = visibleProviders[index];
                PointF center = providerCenters[index];
                var iconBounds = new RectangleF(center.X - 9, center.Y - 9, 18, 18);
                if (provider == Provider.Codex)
                {
                    var shortQuota = reading == null ? null : reading.FiveHours;
                    var weeklyQuota = reading == null ? null : reading.Weekly;
                    PaintRing(g, center, position.ShortOutside ? 25 : 19, shortQuota, position.ShortColor, true);
                    PaintRing(g, center, position.ShortOutside ? 19 : 25, weeklyQuota, position.WeeklyColor, true);
                    if (codexActive)
                        PaintActivityGlow(g);
                    g.DrawImage(logo, iconBounds);
                }
                else if (provider != Provider.None)
                {
                    PaintRing(g, center, 25, null, Palette.Blue, false);
                    if (provider != Provider.Grok)
                        PaintRing(g, center, 19, null, Palette.Green, false);
                    g.DrawImage(providerMarks, iconBounds, new RectangleF(((int)provider - 1) * 96, 0, 96, 96), GraphicsUnit.Pixel);
                }
                else
                    PaintSettings(g, center);
            }

            g.ResetTransform();
            for (int index = 0; index < providerCenters.Length; index++)
            {
                Provider provider = visibleProviders[index];
                if (provider == Provider.None || !args.ClipRectangle.IntersectsWith(CellBounds(index)))
                    continue;
                PointF center = providerCenters[index];
                var bounds = new Rectangle(Pixel((center.X - 16) * scale), Pixel((center.Y + DockFace.LabelTop) * scale),
                    Pixel(32 * scale), Pixel(DockFace.LabelHeight * scale));
                string text = provider == Provider.Codex ? usageLabel : NoUsage;
                Color color = text == NoUsage || error != null ? Palette.Muted : Palette.Text;
                TextRenderer.DrawText(g, text, usageLabelFont, bounds, color,
                    TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine |
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.PreserveGraphicsClipping);
            }
        }

        private static void PaintSettings(Graphics g, PointF center)
        {
            using (var pen = new Pen(Palette.Muted, 1.7f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                g.DrawEllipse(pen, center.X - 3, center.Y - 3, 6, 6);
                g.DrawEllipse(pen, center.X - 8, center.Y - 8, 16, 16);
                for (int index = 0; index < 8; index++)
                {
                    double angle = index * Math.PI / 4;
                    float x = (float)Math.Cos(angle);
                    float y = (float)Math.Sin(angle);
                    g.DrawLine(pen, center.X + 8 * x, center.Y + 8 * y, center.X + 11 * x, center.Y + 11 * y);
                }
            }
        }

        private void PaintActivityGlow(Graphics g)
        {
            const int period = 2600;
            double phase = activityAge.ElapsedMilliseconds % period / (double)period;
            double light = (1 + Math.Cos(2 * Math.PI * phase)) / 2;
            if (activityGlowBrush == null)
            {
                float radius = ActivityGlowRadius;
                activityGlowShape = new GraphicsPath();
                activityGlowShape.AddEllipse(faceCenter.X - radius, faceCenter.Y - radius, 2 * radius, 2 * radius);
                activityGlowBrush = new PathGradientBrush(activityGlowShape)
                {
                    SurroundColors = new[] { Color.FromArgb(0, Palette.Blue) },
                    FocusScales = new PointF(0.36f, 0.36f)
                };
            }
            activityGlowBrush.CenterColor = Color.FromArgb(88 + (int)(116 * light), Palette.Blue);
            g.FillPath(activityGlowBrush, activityGlowShape);
        }

        private void ReleaseActivityGlow()
        {
            if (activityGlowBrush == null)
                return;
            activityGlowBrush.Dispose();
            activityGlowShape.Dispose();
            activityGlowBrush = null;
            activityGlowShape = null;
        }

        private void PaintRing(Graphics g, PointF center, float radius, Quota quota, Color accent, bool live)
        {
            var bounds = new RectangleF(center.X - radius, center.Y - radius, radius * 2, radius * 2);
            using (var pen = new Pen(Palette.Track, 3.2f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                g.DrawEllipse(pen, bounds);
                if (quota != null && quota.Used > 0)
                {
                    pen.Color = Palette.Level(quota, accent, error != null);
                    if (quota.Used >= 100)
                        g.DrawEllipse(pen, bounds);
                    else
                        g.DrawArc(pen, bounds, -90, (float)(quota.Used * 360 / 100));
                }
                if (live && pulse.Enabled)
                {
                    double phase = Math.Min(1, pulseAge.ElapsedMilliseconds / 540.0);
                    pen.Width = 2.1f;
                    pen.Color = Color.FromArgb((int)(200 * Math.Sin(Math.PI * phase)), Palette.Text);
                    g.DrawArc(pen, bounds, -90 + (float)(phase * 360), 36);
                }
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs args)
        {
            base.OnFormClosing(args);
            if (args.Cancel)
                return;
            if (args.CloseReason == CloseReason.UserClosing && savingPlacement)
            {
                closeAfterSaving = true;
                args.Cancel = true;
                return;
            }
            closing = true;
            poll.Stop();
            hover.Stop();
            pulse.Stop();
            activityPulse.Stop();
            if (activity != null)
                activity.Dispose();
            connection.Dispose();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                logo.Dispose();
                providerMarks.Dispose();
                if (usageLabelFont != null)
                    usageLabelFont.Dispose();
                details.Dispose();
                poll.Dispose();
                hover.Dispose();
                pulse.Dispose();
                activityPulse.Dispose();
                ReleaseActivityGlow();
                if (activity != null)
                    activity.Dispose();
                connection.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    internal class FloatingWindow : Form
    {
        private readonly float scale;

        protected FloatingWindow(int width, int height, int radius)
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            ShowInTaskbar = false;
            TopMost = true;
            DoubleBuffered = true;
            BackColor = Palette.Surface;
            using (var graphics = Graphics.FromHwnd(IntPtr.Zero))
                scale = graphics.DpiX / 96f;
            SetSurfaceSize(width, height, radius);
        }

        protected void SetSurfaceSize(int width, int height, float radius)
        {
            ClientSize = new Size(Pixel(width), Pixel(height));
            // Control.Region owns the assigned region and disposes it when it is replaced.
            using (var shape = Rounded(0, 0, ClientSize.Width, ClientSize.Height, radius * scale))
                Region = new Region(shape);
        }

        protected void SetShapedSurface(Size size, GraphicsPath shape, int width, int height)
        {
            ClientSize = size;
            using (var scaleToWindow = new Matrix(size.Width / (float)width, 0, 0, size.Height / (float)height, 0, 0))
                shape.Transform(scaleToWindow);
            Region = new Region(shape);
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                var args = base.CreateParams;
                args.ExStyle |= 0x00000080; // WS_EX_TOOLWINDOW
                return args;
            }
        }

        protected int Pixel(float dip) { return (int)Math.Round(dip * scale); }

        protected override void OnPaint(PaintEventArgs args)
        {
            base.OnPaint(args);
            args.Graphics.ScaleTransform(scale, scale);
            args.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            args.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        }

        protected static GraphicsPath Rounded(float x, float y, float width, float height, float radius)
        {
            float diameter = radius * 2;
            var shape = new GraphicsPath();
            shape.AddArc(x, y, diameter, diameter, 180, 90);
            shape.AddArc(x + width - diameter, y, diameter, diameter, 270, 90);
            shape.AddArc(x + width - diameter, y + height - diameter, diameter, diameter, 0, 90);
            shape.AddArc(x, y + height - diameter, diameter, diameter, 90, 90);
            shape.CloseFigure();
            return shape;
        }
    }

    internal static class Palette
    {
        public static readonly CultureInfo Portuguese = CultureInfo.GetCultureInfo("pt-BR");
        public static readonly Color Surface = Color.Black;
        public static readonly Color Outline = Color.FromArgb(52, 60, 77);
        public static readonly Color Track = Color.FromArgb(48, 57, 73);
        public static readonly Color Text = Color.FromArgb(243, 245, 251);
        public static readonly Color Muted = Color.FromArgb(170, 180, 200);
        public static readonly Color Quiet = Color.FromArgb(119, 131, 153);
        public static readonly Color Green = Color.FromArgb(98, 222, 154);
        public static readonly Color Blue = Color.FromArgb(120, 200, 255);
        public static readonly Color Yellow = Color.FromArgb(250, 213, 92);
        public static readonly Color Red = Color.FromArgb(255, 104, 117);

        public static Color Level(Quota quota, Color normal, bool stale)
        {
            return stale || quota == null ? Quiet : UsageColor(quota.Used, normal);
        }

        public static Color UsageColor(double percent, Color normal)
        {
            return percent >= 76 ? Red : percent >= 50 ? Yellow : normal;
        }

        public static string Percent(Quota quota)
        {
            return quota == null ? "—" : quota.Used.ToString("0.#", Portuguese) + "%";
        }
    }
}
