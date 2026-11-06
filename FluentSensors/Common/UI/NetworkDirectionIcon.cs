using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace FluentSensors.Common.UI
{
    public static class NetworkDirectionIcon
    {
        public static bool IsIcon(string label) => label is "\u2B07" or "\u2B06";

        public static Geometry Create(string label)
        {
            bool down = label == "\u2B07";
            var geometry = new PathGeometry();
            var stem = new PathFigure { StartPoint = new Point(5, down ? 1 : 11), IsClosed = false };
            stem.Segments.Add(new LineSegment { Point = new Point(5, down ? 11 : 1) });
            var head = new PathFigure { StartPoint = new Point(1, down ? 7 : 5), IsClosed = false };
            head.Segments.Add(new LineSegment { Point = new Point(5, down ? 11 : 1) });
            head.Segments.Add(new LineSegment { Point = new Point(9, down ? 7 : 5) });
            geometry.Figures.Add(stem);
            geometry.Figures.Add(head);
            return geometry;
        }
    }
}
