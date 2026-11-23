using System.Drawing;
using System.Drawing.Drawing2D;

namespace UsageLLMs
{
    internal static class DockFace
    {
        public const int Width = 68;
        public const int LabelTop = 29;
        public const int LabelHeight = 19;
        public const int LabelWidth = 56;

        public static bool IsLateral(Edge edge)
        {
            return edge == Edge.Left || edge == Edge.Right;
        }

        public static int CellStep(Edge edge, bool showLabels)
        {
            return showLabels && IsLateral(edge) ? 106 : 70;
        }

        public static int Depth(Edge edge, bool showLabels)
        {
            return Width + (showLabels && !IsLateral(edge) ? 28 : 0);
        }

        public static int Length(Edge edge, int cells, bool showLabels)
        {
            return 100 + CellStep(edge, showLabels) * (cells - 1) + (showLabels && IsLateral(edge) ? 30 : 0);
        }

        public static GraphicsPath Create(Edge edge, int cells, bool showLabels, out PointF[] centers)
        {
            int extension = Length(edge, cells, showLabels) - 100;
            int depth = Depth(edge, showLabels);
            int extraDepth = depth - Width;
            int shoulder = 43 + extraDepth / 2;
            // Start at the screen edge, flare into the body, then return to the same edge.
            var path = new GraphicsPath();
            path.AddBezier(depth, 0, depth, 10, 56 + extraDepth, 12, shoulder, 12);
            path.AddBezier(shoulder, 12, 19, 12, 3, 26, 3, 47);
            path.AddLine(3, 47, 3, 53 + extension);
            path.AddBezier(3, 53 + extension, 3, 74 + extension, 19, 88 + extension, shoulder, 88 + extension);
            path.AddBezier(shoulder, 88 + extension, 56 + extraDepth, 88 + extension, depth, 90 + extension, depth, 100 + extension);
            path.CloseFigure();

            centers = new PointF[cells];
            for (int index = 0; index < cells; index++)
                centers[index] = new PointF(39 + (edge == Edge.Top ? extraDepth : 0),
                    50 + index * CellStep(edge, showLabels));
            using (var orientation = Orientation(edge, depth))
            {
                orientation.TransformPoints(centers);
                path.Transform(orientation);
            }
            return path;
        }

        private static Matrix Orientation(Edge edge, int depth)
        {
            switch (edge)
            {
                case Edge.Left: return new Matrix(-1, 0, 0, 1, depth, 0);
                case Edge.Top: return new Matrix(0, -1, 1, 0, 0, depth);
                case Edge.Bottom: return new Matrix(0, 1, 1, 0, 0, 0);
                default: return new Matrix();
            }
        }
    }
}
