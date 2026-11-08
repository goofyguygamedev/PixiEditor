using Drawie.Backend.Core.Numerics;
using Drawie.Backend.Core.Vector;
using Drawie.Numerics;

namespace PixiEditor.ChangeableDocument.Changeables.Graph.Nodes.Shapes.Data;

/// <summary>
/// Builds the curved path a multi-point line draws through. Shared by the vector line's shape data
/// and the raster line change so both bend identically - a curve that previewed one way and
/// rasterized another would be worse than no curve at all.
/// </summary>
public static class LineCurveHelper
{
    /// <summary>
    /// Appends a Catmull-Rom spline through <paramref name="points"/>, converted to cubic beziers.
    /// Each point's tangent comes from its neighbours scaled by <paramref name="smoothness"/>, so a
    /// smoothness of 0 collapses the controls onto the segment endpoints and yields a straight
    /// polyline, and 1 is a fully rounded curve.
    /// </summary>
    public static void AppendTo(VectorPath path, IReadOnlyList<VecD> points, double smoothness)
    {
        if (points.Count == 0)
            return;

        path.MoveTo((VecF)points[0]);

        if (points.Count == 1)
            return;

        double tension = Math.Clamp(smoothness, 0, 1);

        for (int i = 0; i < points.Count - 1; i++)
        {
            VecD previous = points[Math.Max(0, i - 1)];
            VecD from = points[i];
            VecD to = points[i + 1];
            VecD next = points[Math.Min(points.Count - 1, i + 2)];

            VecD control1 = from + (to - previous) * (tension / 6d);
            VecD control2 = to - (next - from) * (tension / 6d);

            path.CubicTo((VecF)control1, (VecF)control2, (VecF)to);
        }
    }

    /// <summary>
    /// Builds a fresh path through <paramref name="points"/>. The caller owns the returned path.
    /// </summary>
    public static VectorPath Build(IReadOnlyList<VecD> points, double smoothness)
    {
        VectorPath path = new();
        AppendTo(path, points, smoothness);
        return path;
    }

    /// <summary>
    /// Spaces <paramref name="count"/> points evenly along the straight line from
    /// <paramref name="start"/> to <paramref name="end"/>. Seeding the handles on the line means
    /// turning curve points on doesn't move the line until one is actually dragged.
    /// </summary>
    public static List<VecD> SeedAlongLine(VecD start, VecD end, int count)
    {
        List<VecD> points = new(Math.Max(0, count));
        for (int i = 1; i <= count; i++)
        {
            double t = i / (double)(count + 1);
            points.Add(start + (end - start) * t);
        }

        return points;
    }

    /// <summary>
    /// How far off the straight line a curve point may sit while the line still counts as straight.
    /// </summary>
    /// <remarks>
    /// A quarter of a pixel, because pixel coverage cannot change until a point is about half a
    /// pixel off the line, so anything under this cannot alter which pixels the line draws on.
    /// </remarks>
    public const double StraightTolerance = 0.25;

    /// <summary>
    /// Whether every one of <paramref name="points"/> sits on the straight line from
    /// <paramref name="start"/> to <paramref name="end"/>, within
    /// <see cref="StraightTolerance"/>.
    /// </summary>
    /// <remarks>
    /// This is what lets curve points be on by default without costing anything. Curve points are
    /// seeded on the line, and a line with curve points can only be drawn by stroking a path, which
    /// gives a slightly different result from the Bresenham run a plain straight line gets. Asking
    /// whether the points actually bend the line means an untouched line still rasterizes exactly
    /// the way it did before it had handles.
    /// </remarks>
    public static bool IsStraight(VecD start, VecD end, IReadOnlyList<VecD> points)
    {
        if (points is null || points.Count == 0)
            return true;

        VecD along = end - start;
        double length = along.Length;

        foreach (VecD point in points)
        {
            VecD offset = point - start;

            // Degenerate line: there is no direction to be collinear with, so measure straight to
            // the shared endpoint instead.
            double distance = length <= 0
                ? offset.Length
                : Math.Abs((along.X * offset.Y) - (along.Y * offset.X)) / length;

            if (distance > StraightTolerance)
                return false;
        }

        return true;
    }

    /// <summary>How finely each curve segment is flattened before measuring it.</summary>
    private const int FlattenStepsPerSegment = 24;

    /// <summary>
    /// Places <paramref name="count"/> points at even intervals along the curve that currently runs
    /// from <paramref name="start"/> through <paramref name="existing"/> to <paramref name="end"/>.
    /// </summary>
    /// <remarks>
    /// Spacing is measured along the curve rather than in parameter space, so points stay visually
    /// even where the curve bends tightly instead of bunching up on the straighter stretches. The
    /// shape is kept because the samples are taken from the existing curve: changing the number of
    /// points respaces the handles without redrawing the line the artist already shaped. With no
    /// existing points the curve is the straight line, so this reduces to
    /// <see cref="SeedAlongLine"/>.
    /// </remarks>
    public static List<VecD> RedistributeAlongCurve(VecD start, VecD end, IReadOnlyList<VecD> existing,
        double smoothness, int count)
    {
        if (count <= 0)
            return new List<VecD>();

        if (existing is null || existing.Count == 0)
            return SeedAlongLine(start, end, count);

        List<VecD> through = new(existing.Count + 2) { start };
        through.AddRange(existing);
        through.Add(end);

        List<VecD> flattened = Flatten(through, smoothness);

        // Cumulative length at each flattened point, so a target distance can be looked up directly.
        List<double> lengths = new(flattened.Count) { 0 };
        for (int i = 1; i < flattened.Count; i++)
        {
            lengths.Add(lengths[i - 1] + (flattened[i] - flattened[i - 1]).Length);
        }

        double total = lengths[^1];
        if (total <= 0)
            return SeedAlongLine(start, end, count);

        List<VecD> result = new(count);
        int cursor = 1;
        for (int i = 1; i <= count; i++)
        {
            double target = total * i / (count + 1);

            // The targets ascend, so the walk never has to start over.
            while (cursor < lengths.Count - 1 && lengths[cursor] < target)
            {
                cursor++;
            }

            double segmentLength = lengths[cursor] - lengths[cursor - 1];
            double t = segmentLength <= 0 ? 0 : (target - lengths[cursor - 1]) / segmentLength;
            result.Add(flattened[cursor - 1] + (flattened[cursor] - flattened[cursor - 1]) * t);
        }

        return result;
    }

    /// <summary>
    /// Walks the same Catmull-Rom segments <see cref="AppendTo"/> emits and samples them into a
    /// polyline, so the curve can be measured without a path-measuring backend.
    /// </summary>
    private static List<VecD> Flatten(IReadOnlyList<VecD> points, double smoothness)
    {
        List<VecD> flattened = new(((points.Count - 1) * FlattenStepsPerSegment) + 1) { points[0] };

        if (points.Count < 2)
            return flattened;

        double tension = Math.Clamp(smoothness, 0, 1);

        for (int i = 0; i < points.Count - 1; i++)
        {
            VecD previous = points[Math.Max(0, i - 1)];
            VecD from = points[i];
            VecD to = points[i + 1];
            VecD next = points[Math.Min(points.Count - 1, i + 2)];

            VecD control1 = from + (to - previous) * (tension / 6d);
            VecD control2 = to - (next - from) * (tension / 6d);

            for (int step = 1; step <= FlattenStepsPerSegment; step++)
            {
                flattened.Add(CubicAt(from, control1, control2, to, step / (double)FlattenStepsPerSegment));
            }
        }

        return flattened;
    }

    private static VecD CubicAt(VecD from, VecD control1, VecD control2, VecD to, double t)
    {
        double inverse = 1 - t;
        double a = inverse * inverse * inverse;
        double b = 3 * inverse * inverse * t;
        double c = 3 * inverse * t * t;
        double d = t * t * t;

        return new VecD(
            (from.X * a) + (control1.X * b) + (control2.X * c) + (to.X * d),
            (from.Y * a) + (control1.Y * b) + (control2.Y * c) + (to.Y * d));
    }
}
