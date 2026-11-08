using Drawie.Numerics;
using PixiEditor.ChangeableDocument.Changeables.Graph.Nodes.Shapes.Data;
using PixiEditor.Helpers;

namespace PixiEditor.Tests;

/// <summary>
/// Covers the two pure pieces of the line tool's geometry: the isometric angle lock and the even
/// respacing of curve points. Both are plain maths with no rendering backend involved, which is
/// what makes them worth testing here rather than by hand on a canvas.
/// </summary>
public class LineToolGeometryTests
{
    private const double Tolerance = 0.0001;

    [Fact]
    public void IsometricLockSnapsAShallowDragToExactlyTwoToOne()
    {
        VecD result = GeometryHelper.GetIsometricPosition(new VecD(0, 0), new VecD(10.3, 5.1));

        // The whole point of the lock: the result is a whole number of 2:1 steps, so the raster
        // staircase is an unbroken run of two-across-one-down treads.
        Assert.Equal(10, result.X, Tolerance);
        Assert.Equal(5, result.Y, Tolerance);
    }

    [Fact]
    public void IsometricLockSnapsASteepDragToOneToTwo()
    {
        VecD result = GeometryHelper.GetIsometricPosition(new VecD(0, 0), new VecD(5.2, 9.9));

        Assert.Equal(5, result.X, Tolerance);
        Assert.Equal(10, result.Y, Tolerance);
    }

    [Fact]
    public void IsometricLockKeepsVerticalAndHorizontalDrags()
    {
        VecD vertical = GeometryHelper.GetIsometricPosition(new VecD(0, 0), new VecD(0.4, 9));
        Assert.Equal(0, vertical.X, Tolerance);
        Assert.Equal(9, vertical.Y, Tolerance);

        VecD horizontal = GeometryHelper.GetIsometricPosition(new VecD(0, 0), new VecD(9, 0.4));
        Assert.Equal(9, horizontal.X, Tolerance);
        Assert.Equal(0, horizontal.Y, Tolerance);
    }

    [Fact]
    public void IsometricLockNeverProducesFortyFiveDegrees()
    {
        // 45 degrees is not an isometric angle, so a drag pointing straight at it has to resolve to
        // one of the two neighbouring iso slopes rather than passing through.
        VecD result = GeometryHelper.GetIsometricPosition(new VecD(0, 0), new VecD(10, 10));

        double ratio = Math.Abs(result.X) / Math.Abs(result.Y);
        Assert.True(Math.Abs(ratio - 2) < Tolerance || Math.Abs(ratio - 0.5) < Tolerance,
            $"expected a 2:1 or 1:2 slope, got {result.X}:{result.Y}");
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(-1, 1)]
    [InlineData(1, -1)]
    [InlineData(-1, -1)]
    public void IsometricLockFollowsTheDragIntoEveryQuadrant(int signX, int signY)
    {
        VecD cursor = new(10.3 * signX, 5.1 * signY);
        VecD result = GeometryHelper.GetIsometricPosition(new VecD(0, 0), cursor);

        Assert.Equal(10 * signX, result.X, Tolerance);
        Assert.Equal(5 * signY, result.Y, Tolerance);
    }

    [Fact]
    public void IsometricLockAlwaysMovesAtLeastOneWholeStep()
    {
        // A drag shorter than a step would otherwise round to zero and leave the endpoint on the
        // start, which reads as the tool having ignored the drag.
        VecD result = GeometryHelper.GetIsometricPosition(new VecD(0, 0), new VecD(0.1, 0.05));

        Assert.NotEqual(0, result.Length, Tolerance);
        Assert.Equal(1, result.X, Tolerance);
        Assert.Equal(0, result.Y, Tolerance);
    }

    [Fact]
    public void IsometricLockReturnsTheStartWhenThereIsNoDrag()
    {
        VecD start = new(4, 7);
        VecD result = GeometryHelper.GetIsometricPosition(start, start);

        Assert.Equal(start.X, result.X, Tolerance);
        Assert.Equal(start.Y, result.Y, Tolerance);
    }

    [Fact]
    public void PixelCenteredIsometricLockLandsOnAPixelCentre()
    {
        VecD result =
            GeometryHelper.GetIsometricPositionAlignedWithPixelCenters(new VecD(3.2, 4.8), new VecD(13.4, 9.6));

        Assert.Equal(13.5, result.X, Tolerance);
        Assert.Equal(9.5, result.Y, Tolerance);
    }

    [Fact]
    public void SeededPointsCountAsStraight()
    {
        // The default two points are seeded on the line, so a line nobody has bent has to still
        // report as straight - that is what keeps it on Bresenham rather than path stroking.
        VecD start = new(0, 0);
        VecD end = new(17, 5);
        List<VecD> seeded = LineCurveHelper.SeedAlongLine(start, end, 2);

        Assert.True(LineCurveHelper.IsStraight(start, end, seeded));
    }

    [Fact]
    public void NoPointsAtAllCountsAsStraight()
    {
        Assert.True(LineCurveHelper.IsStraight(new VecD(0, 0), new VecD(10, 10), []));
    }

    [Fact]
    public void ADraggedPointStopsCountingAsStraight()
    {
        VecD start = new(0, 0);
        VecD end = new(20, 0);

        Assert.False(LineCurveHelper.IsStraight(start, end, [new VecD(10, 3)]));
    }

    [Fact]
    public void APointNudgedLessThanAQuarterPixelStillCountsAsStraight()
    {
        // Below the tolerance the pixels drawn cannot change, so switching rasterization paths over
        // it would make the line flicker between two near-identical results while dragging.
        VecD start = new(0, 0);
        VecD end = new(20, 0);

        Assert.True(LineCurveHelper.IsStraight(start, end, [new VecD(10, 0.2)]));
    }

    [Fact]
    public void RedistributeWithNoExistingPointsSeedsEvenlyAlongTheStraightLine()
    {
        List<VecD> result =
            LineCurveHelper.RedistributeAlongCurve(new VecD(0, 0), new VecD(9, 0), [], 1d, 2);

        Assert.Equal(2, result.Count);
        Assert.Equal(3, result[0].X, Tolerance);
        Assert.Equal(6, result[1].X, Tolerance);
        Assert.Equal(0, result[0].Y, Tolerance);
        Assert.Equal(0, result[1].Y, Tolerance);
    }

    [Fact]
    public void RedistributeReturnsNothingForZeroPoints()
    {
        List<VecD> result =
            LineCurveHelper.RedistributeAlongCurve(new VecD(0, 0), new VecD(9, 0), [new VecD(4, 4)], 1d, 0);

        Assert.Empty(result);
    }

    [Fact]
    public void RedistributeSpacesPointsEvenlyAlongABentLine()
    {
        // Smoothness 0 collapses the spline onto its control polygon, so the expected positions can
        // be worked out by hand: a 0,0 -> 5,5 -> 10,0 dogleg, with the two new points a third and
        // two thirds of the way along it.
        List<VecD> result = LineCurveHelper.RedistributeAlongCurve(
            new VecD(0, 0), new VecD(10, 0), [new VecD(5, 5)], 0d, 2);

        Assert.Equal(2, result.Count);
        Assert.Equal(10d / 3d, result[0].X, 0.01);
        Assert.Equal(10d / 3d, result[0].Y, 0.01);
        Assert.Equal(20d / 3d, result[1].X, 0.01);
        Assert.Equal(10d / 3d, result[1].Y, 0.01);
    }

    [Fact]
    public void RedistributeKeepsSpacingEvenWhenReducingThePointCount()
    {
        // Three hand-dragged points bunched into the first half of the line, cut down to two. The
        // old behaviour kept the first two where they were, leaving both crowded near the start;
        // respacing has to put them at even distances along the curve instead.
        List<VecD> existing = [new VecD(1, 3), new VecD(2, 4), new VecD(3, 3)];
        List<VecD> result =
            LineCurveHelper.RedistributeAlongCurve(new VecD(0, 0), new VecD(20, 0), existing, 1d, 2);

        Assert.Equal(2, result.Count);

        double first = ArcLengthTo(new VecD(0, 0), new VecD(20, 0), existing, result[0]);
        double second = ArcLengthTo(new VecD(0, 0), new VecD(20, 0), existing, result[1]);
        double total = ArcLengthTo(new VecD(0, 0), new VecD(20, 0), existing, new VecD(20, 0));

        // A third and two thirds along, within the error of the flattening used to measure it.
        Assert.Equal(total / 3d, first, total * 0.02);
        Assert.Equal(total * 2d / 3d, second, total * 0.02);
    }

    /// <summary>
    /// Distance along the curve to the flattened sample nearest <paramref name="point"/>. Mirrors
    /// the measurement the helper itself does, which is the thing under test, so it is only used to
    /// check that the points come out evenly spaced rather than to check the absolute geometry.
    /// </summary>
    private static double ArcLengthTo(VecD start, VecD end, IReadOnlyList<VecD> existing, VecD point)
    {
        List<VecD> through = new(existing.Count + 2) { start };
        through.AddRange(existing);
        through.Add(end);

        // Re-walk the curve by sampling the same control polygon the helper flattens, at a much
        // finer step, and accumulate length up to the closest approach to the point.
        List<VecD> samples = SampleCurve(through, 1d, 400);

        double running = 0;
        double bestLength = 0;
        double bestDistSq = double.MaxValue;
        for (int i = 1; i < samples.Count; i++)
        {
            running += (samples[i] - samples[i - 1]).Length;
            double distSq = (samples[i] - point).LengthSquared;
            if (distSq < bestDistSq)
            {
                bestDistSq = distSq;
                bestLength = running;
            }
        }

        return bestLength;
    }

    private static List<VecD> SampleCurve(IReadOnlyList<VecD> points, double smoothness, int stepsPerSegment)
    {
        List<VecD> samples = new() { points[0] };
        double tension = Math.Clamp(smoothness, 0, 1);

        for (int i = 0; i < points.Count - 1; i++)
        {
            VecD previous = points[Math.Max(0, i - 1)];
            VecD from = points[i];
            VecD to = points[i + 1];
            VecD next = points[Math.Min(points.Count - 1, i + 2)];

            VecD control1 = from + (to - previous) * (tension / 6d);
            VecD control2 = to - (next - from) * (tension / 6d);

            for (int step = 1; step <= stepsPerSegment; step++)
            {
                double t = step / (double)stepsPerSegment;
                double inverse = 1 - t;
                double a = inverse * inverse * inverse;
                double b = 3 * inverse * inverse * t;
                double c = 3 * inverse * t * t;
                double d = t * t * t;

                samples.Add(new VecD(
                    (from.X * a) + (control1.X * b) + (control2.X * c) + (to.X * d),
                    (from.Y * a) + (control1.Y * b) + (control2.Y * c) + (to.Y * d)));
            }
        }

        return samples;
    }
}
