using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Drawing;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace UsageLLMs
{
    internal enum Edge { Left, Right, Top, Bottom }

    internal sealed class Placement
    {
        private static readonly string Settings =
            Path.Combine(Environment.GetEnvironmentVariable("LOCALAPPDATA") ??
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CodexUsage", "position.txt");

        public Edge Edge = Edge.Right;
        public double Alignment = 50;
        public string Monitor;
        public const int MinSize = 52;
        public const int MaxSize = 86;
        public bool ShortBlue;
        public bool ShortOutside;
        public int WidgetSize = 68;
        private int enabledProviders = Providers.AllEnabled;
        public Color ShortColor { get { return ShortBlue ? Palette.Blue : Palette.Green; } }
        public Color WeeklyColor { get { return ShortBlue ? Palette.Green : Palette.Blue; } }

        public bool ProviderEnabled(Provider provider)
        {
            return (enabledProviders & (1 << (int)provider)) != 0;
        }

        public void SetProviderEnabled(Provider provider, bool enabled)
        {
            int bit = 1 << (int)provider;
            enabledProviders = enabled ? enabledProviders | bit : enabledProviders & ~bit;
        }

        public Forms.Screen Screen
        {
            get
            {
                return Forms.Screen.AllScreens.FirstOrDefault(screen => screen.DeviceName == Monitor)
                    ?? Forms.Screen.PrimaryScreen;
            }
        }

        public static Placement Load()
        {
            var placement = new Placement();
            try
            {
                var parts = File.ReadAllText(Settings).Split('|');
                Edge edge;
                double alignment;
                if (parts.Length >= 3 && Enum.TryParse(parts[0], out edge) &&
                    Enum.IsDefined(typeof(Edge), edge) &&
                    Double.TryParse(parts[1], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out alignment) &&
                    alignment >= 0 && alignment <= 100)
                {
                    placement.Edge = edge;
                    placement.Alignment = alignment;
                    placement.Monitor = parts[2];
                }
                bool shortBlue;
                if (parts.Length == 7 && !Boolean.TryParse(parts[3], out shortBlue))
                {
                    placement.ShortBlue = parts[3] == "2";
                    Boolean.TryParse(parts[5], out placement.ShortOutside);
                    if (parts[6] == "Small")
                        placement.WidgetSize = 54;
                    if (parts[6] == "Large")
                        placement.WidgetSize = MaxSize;
                }
                else if (parts.Length == 6 || parts.Length == 7)
                {
                    Boolean.TryParse(parts[3], out placement.ShortBlue);
                    Boolean.TryParse(parts[4], out placement.ShortOutside);
                    int size;
                    if (Int32.TryParse(parts[5], NumberStyles.None, CultureInfo.InvariantCulture, out size))
                        placement.WidgetSize = Clamp(size, MinSize, MaxSize);
                    int enabled;
                    if (parts.Length == 7 && Int32.TryParse(parts[6], NumberStyles.None, CultureInfo.InvariantCulture, out enabled)
                        && enabled <= Providers.AllEnabled)
                        placement.enabledProviders = enabled;
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return placement;
        }

        public string Serialize()
        {
            return String.Join("|", Edge.ToString(), Alignment.ToString("0.######", CultureInfo.InvariantCulture),
                Monitor, ShortBlue.ToString(), ShortOutside.ToString(), WidgetSize.ToString(CultureInfo.InvariantCulture),
                enabledProviders.ToString(CultureInfo.InvariantCulture));
        }

        public static void Save(string snapshot)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Settings));
            string pending = Settings + ".tmp";
            File.WriteAllText(pending, snapshot);
            if (File.Exists(Settings))
                File.Replace(pending, Settings, null);
            else
                File.Move(pending, Settings);
        }

        public Point Locate(Size size)
        {
            Point start, end;
            Segment(Screen.WorkingArea, size, Edge, out start, out end);
            return new Point(start.X + (int)Math.Round((end.X - start.X) * Alignment / 100),
                start.Y + (int)Math.Round((end.Y - start.Y) * Alignment / 100));
        }

        public Point DragTo(Point pointer, Size lateralSize)
        {
            var screen = Forms.Screen.FromPoint(pointer);
            var area = screen.WorkingArea;
            Point best = Point.Empty;
            double bestDistance = Double.MaxValue;
            Edge previousEdge = Edge;
            foreach (var edge in Edges)
            {
                Size size = DockFace.IsLateral(edge) ? lateralSize : new Size(lateralSize.Height, lateralSize.Width);
                var desired = new Point(pointer.X - size.Width / 2, pointer.Y - size.Height / 2);
                Point start, end;
                Segment(area, size, edge, out start, out end);
                var candidate = new Point(Clamp(desired.X, start.X, end.X), Clamp(desired.Y, start.Y, end.Y));
                double dx = (double)desired.X - candidate.X;
                double dy = (double)desired.Y - candidate.Y;
                double distance = dx * dx + dy * dy;
                if (distance > bestDistance || (distance == bestDistance && edge != previousEdge))
                    continue;

                bestDistance = distance;
                best = candidate;
                Edge = edge;
                int span = end.X - start.X + end.Y - start.Y;
                Alignment = span == 0 ? 50 : 100.0 * (best.X - start.X + best.Y - start.Y) / span;
            }
            Monitor = screen.DeviceName;
            return best;
        }

        private static readonly Edge[] Edges = { Edge.Left, Edge.Right, Edge.Top, Edge.Bottom };

        private static void Segment(Rectangle area, Size size, Edge edge, out Point start, out Point end)
        {
            int xSpan = Math.Max(0, area.Width - size.Width);
            int ySpan = Math.Max(0, area.Height - size.Height);
            int xMargin = Math.Min(xSpan / 2, (int)Math.Round(area.Width * 0.10));
            int yMargin = Math.Min(ySpan / 2, (int)Math.Round(area.Height * 0.10));
            if (edge == Edge.Top || edge == Edge.Bottom)
            {
                int y = area.Top + (edge == Edge.Top ? 0 : ySpan);
                start = new Point(area.Left + xMargin, y);
                end = new Point(area.Left + xSpan - xMargin, y);
            }
            else
            {
                int x = area.Left + (edge == Edge.Left ? 0 : xSpan);
                start = new Point(x, area.Top + yMargin);
                end = new Point(x, area.Top + ySpan - yMargin);
            }
        }

        private static int Clamp(int value, int min, int max) { return Math.Max(min, Math.Min(max, value)); }
    }

    internal static class Startup
    {
        private const string Run = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string Name = "Usage LLMs";
        private static string Command { get { return "\"" + Forms.Application.ExecutablePath + "\""; } }

        public static bool Enabled
        {
            get
            {
                using (var key = Registry.CurrentUser.OpenSubKey(Run))
                    return key != null && String.Equals(key.GetValue(Name) as string, Command, StringComparison.OrdinalIgnoreCase);
            }
        }

        public static void Set(bool enabled)
        {
            using (var key = Registry.CurrentUser.CreateSubKey(Run))
            {
                if (enabled)
                    key.SetValue(Name, Command, RegistryValueKind.String);
                else
                    key.DeleteValue(Name, false);
            }
        }
    }

    internal static class Program
    {
        [STAThread]
        public static int Main(string[] args)
        {
            if (args.Length > 0)
            {
                if (args.Length != 1 || (args[0] != "--enable-startup" && args[0] != "--disable-startup"))
                    return 2;
                try
                {
                    Startup.Set(args[0] == "--enable-startup");
                    return 0;
                }
                catch (UnauthorizedAccessException) { return 1; }
                catch (System.Security.SecurityException) { return 1; }
                catch (IOException) { return 1; }
            }

            bool first;
            using (var instance = new Mutex(true, @"Local\CodexUsage.EdgeWidget", out first))
            {
                if (!first)
                    return 0;
                Forms.Application.EnableVisualStyles();
                Forms.Application.SetCompatibleTextRenderingDefault(false);
                using (var widget = new Widget())
                    Forms.Application.Run(widget);
            }
            return 0;
        }
    }
}
