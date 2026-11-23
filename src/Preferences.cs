using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Security;
using System.Windows.Forms;

namespace UsageLLMs
{
    internal sealed class Preferences : FloatingWindow
    {
        private static readonly Color ControlSurface = Color.FromArgb(34, 43, 58);
        private static readonly Color SelectedSurface = Color.FromArgb(29, 63, 57);
        private readonly Font titleFont = new Font("Segoe UI Semibold", 13, FontStyle.Regular);
        private readonly Font bodyFont = new Font("Segoe UI", 9, FontStyle.Regular);

        public Preferences(Placement settings, Action apply, Action accountsChanged, Action<bool> startupChanged, Action quit)
            : base(300, 284, 16)
        {
            Text = "Preferências do Usage LLMs";
            Font = bodyFont;

            SuspendLayout();
            AddLabel("Preferências", 18, 13, 264, 26, Palette.Text).Font = titleFont;
            var tabs = new[]
            {
                AddButton("Aparência", 18, 51, 84, 30),
                AddButton("Contas", 108, 51, 84, 30),
                AddButton("Geral", 198, 51, 84, 30)
            };
            var pages = new Panel[tabs.Length];
            for (int index = 0; index < tabs.Length; index++)
            {
                int tab = index;
                pages[index] = new Panel { BackColor = Palette.Surface };
                Place(pages[index], 18, 94, 264, 176);
                tabs[index].Click += (sender, args) => SelectTab(tabs, pages, tab);
            }
            BuildAppearance(pages[0], settings, apply);
            BuildAccounts(pages[1], settings, accountsChanged);
            BuildGeneral(pages[2], startupChanged, quit);
            SelectTab(tabs, pages, 0);
            ResumeLayout(false);
        }

        private static void SelectTab(Button[] tabs, Panel[] pages, int selected)
        {
            for (int index = 0; index < tabs.Length; index++)
            {
                bool active = index == selected;
                SelectChoice(tabs[index], active);
                pages[index].Visible = active;
            }
        }

        private void BuildAppearance(Panel page, Placement settings, Action apply)
        {
            var colorLabel = AddLabel("", 0, 0, 178, 30, Palette.Muted, page);
            var green = AddButton("", 196, 0, 29, 29, 14.5f, page);
            var blue = AddButton("", 235, 0, 29, 29, 14.5f, page);
            ConfigureSwatch(green, "Verde", Palette.Green);
            ConfigureSwatch(blue, "Azul", Palette.Blue);
            Action updateColor = () =>
            {
                colorLabel.Text = settings.ShortBlue ? "Cor de 5 h · Azul" : "Cor de 5 h · Verde";
                SelectSwatch(green, !settings.ShortBlue);
                SelectSwatch(blue, settings.ShortBlue);
            };
            green.Click += (sender, args) => { settings.ShortBlue = false; updateColor(); apply(); };
            blue.Click += (sender, args) => { settings.ShortBlue = true; updateColor(); apply(); };
            updateColor();

            AddLabel("Anel externo", 0, 44, 264, 19, Palette.Muted, page);
            var fiveHours = AddButton("5 horas", 0, 67, 127, 29, parent: page);
            var weekly = AddButton("Semanal", 137, 67, 127, 29, parent: page);
            Action updateOrder = () =>
            {
                SelectChoice(fiveHours, settings.ShortOutside);
                SelectChoice(weekly, !settings.ShortOutside);
            };
            fiveHours.Click += (sender, args) => { settings.ShortOutside = true; updateOrder(); apply(); };
            weekly.Click += (sender, args) => { settings.ShortOutside = false; updateOrder(); apply(); };
            updateOrder();

            var sizeLabel = AddLabel("", 0, 110, 264, 20, Palette.Muted, page);
            var slider = new SizeSlider
            {
                Minimum = Placement.MinSize, Maximum = Placement.MaxSize, Value = settings.WidgetSize,
                SmallChange = 1, LargeChange = 4, AccessibleName = "Tamanho do widget"
            };
            Action updateSize = () =>
            {
                string percent = (100.0 * slider.Value / Placement.MaxSize).ToString("0", Palette.Portuguese);
                sizeLabel.Text = "Tamanho · " + percent + "%";
                slider.AccessibleDescription = percent + "% do tamanho máximo";
            };
            updateSize();
            slider.ValueChanged += (sender, args) =>
            {
                settings.WidgetSize = slider.Value;
                updateSize();
                apply();
            };
            Place(slider, 0, 132, 264, 31, page);
        }

        private void BuildAccounts(Panel page, Placement settings, Action accountsChanged)
        {
            for (int index = 0; index < Providers.All.Length; index++)
            {
                Provider provider = Providers.All[index];
                string name = Providers.Name(provider);
                int y = index * 41;
                AddLabel(name, 0, y, 204, 32, Palette.Text, page);
                var toggle = new ToggleSwitch { AccessibleName = name, Checked = settings.ProviderEnabled(provider) };
                toggle.CheckedChanged += (sender, args) =>
                {
                    settings.SetProviderEnabled(provider, toggle.Checked);
                    accountsChanged();
                };
                Place(toggle, 220, y + 4, 44, 24, page);
            }
        }

        private void BuildGeneral(Panel page, Action<bool> startupChanged, Action quit)
        {
            AddLabel("Iniciar com o Windows", 0, 0, 204, 32, Palette.Text, page);
            var startup = new ToggleSwitch { AccessibleName = "Iniciar com o Windows" };
            try { startup.Checked = Startup.Enabled; }
            catch (SecurityException) { startup.Enabled = false; }
            catch (UnauthorizedAccessException) { startup.Enabled = false; }
            startup.CheckedChanged += (sender, args) => startupChanged(startup.Checked);
            Place(startup, 220, 4, 44, 24, page);

            var closeWidget = AddButton("Fechar widget", 0, 49, 264, 30, parent: page);
            closeWidget.Click += (sender, args) => quit();
        }

        protected override bool ShowWithoutActivation { get { return false; } }

        protected override void OnDeactivate(EventArgs args)
        {
            base.OnDeactivate(args);
            Close();
        }

        protected override bool ProcessCmdKey(ref Message message, Keys key)
        {
            if (key != Keys.Escape)
                return base.ProcessCmdKey(ref message, key);
            Close();
            return true;
        }

        private static void ConfigureSwatch(Button button, string name, Color color)
        {
            button.AccessibleName = "Cor de 5 horas · " + name;
            button.ForeColor = Palette.Surface;
            button.BackColor = color;
            button.FlatAppearance.MouseOverBackColor = color;
            button.FlatAppearance.MouseDownBackColor = color;
        }

        private static void SelectSwatch(Button button, bool active)
        {
            button.Text = active ? "✓" : "";
            button.AccessibleDescription = active ? "Selecionada" : "";
        }

        private static void SelectChoice(Button button, bool active)
        {
            button.BackColor = active ? SelectedSurface : ControlSurface;
            button.FlatAppearance.MouseOverBackColor = active ? SelectedSurface : Palette.Track;
            button.ForeColor = active ? Palette.Green : Palette.Text;
            button.AccessibleDescription = active ? "Selecionado" : "";
        }

        private Button AddButton(string text, int x, int y, int width, int height, float radius = 8, Control parent = null)
        {
            var button = new RoundedButton(Pixel(radius))
            {
                Text = text, FlatStyle = FlatStyle.Flat, UseVisualStyleBackColor = false,
                BackColor = ControlSurface, ForeColor = Palette.Text, Margin = Padding.Empty
            };
            button.FlatAppearance.BorderSize = 0;
            button.FlatAppearance.MouseOverBackColor = Palette.Track;
            Place(button, x, y, width, height, parent);
            return button;
        }

        private sealed class ToggleSwitch : CheckBox
        {
            public ToggleSwitch()
            {
                Cursor = Cursors.Hand;
                SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            }

            protected override void OnCheckedChanged(EventArgs args)
            {
                base.OnCheckedChanged(args);
                Invalidate();
            }

            protected override void OnPaint(PaintEventArgs args)
            {
                var g = args.Graphics;
                g.Clear(Parent.BackColor);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                float height = Height - 2;
                float inset = Height / 6f;
                float thumb = Height - 2 * inset;
                Color trackColor = !Enabled ? ControlSurface : Checked ? Palette.Green : Palette.Track;
                using (var track = Rounded(1, 1, Width - 2, height, height / 2))
                using (var fill = new SolidBrush(trackColor))
                using (var knob = new SolidBrush(Checked && Enabled ? Palette.Surface : Palette.Text))
                {
                    g.FillPath(fill, track);
                    float x = Checked ? Width - thumb - inset : inset;
                    g.FillEllipse(knob, x, inset, thumb, thumb);
                    if (Focused && ShowFocusCues)
                    {
                        using (var focus = new Pen(Palette.Text) { DashStyle = DashStyle.Dot })
                            g.DrawPath(focus, track);
                    }
                }
            }
        }

        private sealed class RoundedButton : Button
        {
            private readonly float radius;

            public RoundedButton(float radius) { this.radius = radius; }

            protected override void OnPaint(PaintEventArgs args)
            {
                var graphics = args.Graphics;
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.Clear(Parent.BackColor);
                bool over = ClientRectangle.Contains(PointToClient(MousePosition));
                Color fill = over ? FlatAppearance.MouseOverBackColor : BackColor;
                float corner = Math.Min(radius, Math.Min(Width, Height) / 2f);
                using (var shape = Rounded(0, 0, Width, Height, corner))
                using (var brush = new SolidBrush(fill))
                {
                    graphics.FillPath(brush, shape);
                    if (Focused && ShowFocusCues)
                    {
                        using (var focus = new Pen(ForeColor) { DashStyle = DashStyle.Dot })
                        using (var inset = Rounded(2, 2, Width - 4, Height - 4, corner - 2))
                            graphics.DrawPath(focus, inset);
                    }
                }
                TextRenderer.DrawText(graphics, Text, Font, ClientRectangle, ForeColor,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
        }

        private Label AddLabel(string text, int x, int y, int width, int height, Color color, Control parent = null)
        {
            var label = new Label { Text = text, ForeColor = color, TextAlign = ContentAlignment.MiddleLeft };
            Place(label, x, y, width, height, parent);
            return label;
        }

        private void Place(Control control, int x, int y, int width, int height, Control parent = null)
        {
            control.Bounds = new Rectangle(Pixel(x), Pixel(y), Pixel(width), Pixel(height));
            (parent ?? this).Controls.Add(control);
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing)
            {
                titleFont.Dispose();
                bodyFont.Dispose();
            }
        }
    }
}
