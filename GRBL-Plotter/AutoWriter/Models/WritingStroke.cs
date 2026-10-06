using System.Collections.Generic;

namespace GrblPlotter.AutoWriter.Models
{
    /// <summary>
    /// A single pen-down stroke described by an ordered polyline in the machine work frame.
    /// The caller keeps the document order (page -> line -> character -> stroke); this model
    /// only carries one stroke's point sequence and never re-orders it.
    ///
    /// This is a new AutoWriter model. It is deliberately free of device, control or settings
    /// references so that stroke preparation and G-code generation can be tested offline.
    /// </summary>
    public sealed class WritingStroke
    {
        public WritingStroke()
        {
            Points = new List<WritingPoint>();
        }

        public WritingStroke(IEnumerable<WritingPoint> points)
        {
            Points = new List<WritingPoint>(points ?? new WritingPoint[0]);
        }

        /// <summary>Ordered polyline points. The first point is the pen-down position.</summary>
        public List<WritingPoint> Points { get; private set; }
    }

    /// <summary>
    /// A point in machine work coordinates (mm). X increases to the right; Y follows the
    /// machine axis mapping. Values are always millimetres regardless of the UI culture.
    /// </summary>
    public struct WritingPoint
    {
        public readonly double X;
        public readonly double Y;

        public WritingPoint(double x, double y)
        {
            X = x;
            Y = y;
        }
    }
}
