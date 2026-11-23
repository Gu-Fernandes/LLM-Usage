using System.Drawing;
using System.Drawing.Drawing2D;

namespace UsageLLMs
{
    internal static class DockFace
    {
        public const int Width = 68;
        public const int CellStep = 70;

        public static bool IsLateral(Edge edge)
        {
            return edge == Edge.Left || edge == Edge.Right;
        }

        public static int Length(int cells)
        {
            return 100 + CellStep * (cells - 1);
        }

        public static int LengthAt(int size, int cells)
        {
            return (int)System.Math.Round(size * (double)Length(cells) / Width);
        }

        public static GraphicsPath Create(Edge edge, int cells, out PointF[] centers)
        {
            int extension = Length(cells) - 100;
            // Start at the screen edge, flare into the body, then return to the same edge.
            var path = new GraphicsPath();
            path.AddBezier(68, 0, 68, 10, 56, 12, 43, 12);
            path.AddBezier(43, 12, 19, 12, 3, 26, 3, 47);
            path.AddLine(3, 47, 3, 53 + extension);
            path.AddBezier(3, 53 + extension, 3, 74 + extension, 19, 88 + extension, 43, 88 + extension);
            path.AddBezier(43, 88 + extension, 56, 88 + extension, 68, 90 + extension, 68, 100 + extension);
            path.CloseFigure();

            centers = new PointF[cells];
            for (int index = 0; index < cells; index++)
                centers[index] = new PointF(39, 50 + index * CellStep);
            using (var orientation = Orientation(edge))
            {
                orientation.TransformPoints(centers);
                path.Transform(orientation);
            }
            return path;
        }

        private static Matrix Orientation(Edge edge)
        {
            switch (edge)
            {
                case Edge.Left: return new Matrix(-1, 0, 0, 1, Width, 0);
                case Edge.Top: return new Matrix(0, -1, 1, 0, 0, Width);
                case Edge.Bottom: return new Matrix(0, 1, 1, 0, 0, 0);
                default: return new Matrix();
            }
        }
    }
}
