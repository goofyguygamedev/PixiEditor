using Drawie.Numerics;
using PixiEditor.ChangeableDocument.Helpers;

namespace PixiEditor.Tests;

/// <summary>Covers the outlines behind the Shape tool, on plain numbers.</summary>
public class ShapeGeometryTests
{
    private static readonly RectD Box = new(10, 20, 40, 60);

    private static VecD[] Outline(ShapeKind kind, int points = 5, double innerRatio = 0.5) =>
        ShapeGeometry.Outline(kind, Box, points, innerRatio);

    [Fact]
    public void ARectangleIsTheCornersOfTheBox()
    {
        VecD[] outline = Outline(ShapeKind.Rectangle);

        Assert.Equal(4, outline.Length);
        Assert.Contains(new VecD(10, 20), outline);
        Assert.Contains(new VecD(50, 20), outline);
        Assert.Contains(new VecD(50, 80), outline);
        Assert.Contains(new VecD(10, 80), outline);
    }

    [Fact]
    public void ATrianglePointsUpFromTheFullWidth()
    {
        VecD[] outline = Outline(ShapeKind.Triangle);

        Assert.Equal(3, outline.Length);
        Assert.Equal(new VecD(30, 20), outline[0]);
        Assert.Equal(new VecD(50, 80), outline[1]);
        Assert.Equal(new VecD(10, 80), outline[2]);
    }

    [Fact]
    public void ADiamondSitsOnTheEdgeMidpoints()
    {
        VecD[] outline = Outline(ShapeKind.Diamond);

        Assert.Equal([new VecD(30, 20), new VecD(50, 50), new VecD(30, 80), new VecD(10, 50)], outline);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(8)]
    public void APolygonHasOneCornerPerSide(int sides)
    {
        Assert.Equal(sides, Outline(ShapeKind.Polygon, sides).Length);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(6)]
    public void AStarHasTwoCornersPerPoint(int points)
    {
        Assert.Equal(points * 2, Outline(ShapeKind.Star, points).Length);
    }

    [Fact]
    public void FewerThanThreeSidesIsNotAShapeSoItIsClampedUp()
    {
        Assert.Equal(3, Outline(ShapeKind.Polygon, 1).Length);
        Assert.Equal(6, Outline(ShapeKind.Star, 0).Length);
    }

    [Fact]
    public void APolygonStartsAtTheTopAndTurnsClockwise()
    {
        VecD[] outline = Outline(ShapeKind.Polygon, 4);

        Assert.Equal(new VecD(30, 20), outline[0]);
        Assert.True(outline[1].X > outline[0].X, "the second corner should be to the right");
        Assert.True(outline[1].Y > outline[0].Y, "and below, since y runs downwards");
    }

    [Fact]
    public void AStarAlternatesBetweenFarAndNearCorners()
    {
        VecD center = Box.Center;
        VecD[] outline = Outline(ShapeKind.Star, 5, 0.4);

        for (int i = 0; i < outline.Length; i += 2)
        {
            double outer = (outline[i] - center).Length;
            double inner = (outline[i + 1] - center).Length;
            Assert.True(inner < outer, $"corner {i + 1} should sit closer to the centre than corner {i}");
        }
    }

    [Fact]
    public void AnInnerSizeOfOneMakesTheStarARegularPolygon()
    {
        RectD square = new(0, 0, 40, 40);
        VecD center = square.Center;
        double[] reaches = ShapeGeometry.Outline(ShapeKind.Star, square, 5, 1)
            .Select(corner => (corner - center).Length).ToArray();

        Assert.All(reaches, reach => Assert.Equal(reaches[0], reach, 6));
    }

    [Theory]
    [InlineData(ShapeKind.Rectangle)]
    [InlineData(ShapeKind.Triangle)]
    [InlineData(ShapeKind.RightTriangle)]
    [InlineData(ShapeKind.Diamond)]
    [InlineData(ShapeKind.Polygon)]
    [InlineData(ShapeKind.Star)]
    [InlineData(ShapeKind.Cross)]
    [InlineData(ShapeKind.Arrow)]
    public void NoShapeReachesOutsideItsBox(ShapeKind kind)
    {
        foreach (VecD corner in Outline(kind))
        {
            Assert.InRange(corner.X, Box.Left, Box.Right);
            Assert.InRange(corner.Y, Box.Top, Box.Bottom);
        }
    }

    [Theory]
    [InlineData(ShapeKind.Rectangle)]
    [InlineData(ShapeKind.Triangle)]
    [InlineData(ShapeKind.Diamond)]
    [InlineData(ShapeKind.Cross)]
    [InlineData(ShapeKind.Arrow)]
    public void EveryStraightShapeFillsItsBoxInBothDirections(ShapeKind kind)
    {
        VecD[] outline = Outline(kind);

        Assert.Equal(Box.Left, outline.Min(corner => corner.X), 6);
        Assert.Equal(Box.Right, outline.Max(corner => corner.X), 6);
        Assert.Equal(Box.Top, outline.Min(corner => corner.Y), 6);
        Assert.Equal(Box.Bottom, outline.Max(corner => corner.Y), 6);
    }

    [Fact]
    public void ACrossIsTwelveCornersAndTheArrowIsSeven()
    {
        Assert.Equal(12, Outline(ShapeKind.Cross).Length);
        Assert.Equal(7, Outline(ShapeKind.Arrow).Length);
    }

    [Fact]
    public void TheCurvedShapesHaveNoCorners()
    {
        Assert.Empty(Outline(ShapeKind.Ellipse));
        Assert.Empty(Outline(ShapeKind.Heart));
    }

    [Fact]
    public void OnlyTheEllipseAndTheHeartAreCurved()
    {
        Assert.True(ShapeGeometry.IsCurved(ShapeKind.Ellipse));
        Assert.True(ShapeGeometry.IsCurved(ShapeKind.Heart));
        Assert.False(ShapeGeometry.IsCurved(ShapeKind.Star));
    }

    [Fact]
    public void OnlyThePolygonAndTheStarUseThePointCount()
    {
        Assert.True(ShapeGeometry.UsesPoints(ShapeKind.Polygon));
        Assert.True(ShapeGeometry.UsesPoints(ShapeKind.Star));
        Assert.False(ShapeGeometry.UsesPoints(ShapeKind.Rectangle));
    }

    [Fact]
    public void AHeartIsCubicSegmentsStartingAndEndingAtItsBottomPoint()
    {
        VecD[] curve = ShapeGeometry.CurvedOutline(ShapeKind.Heart, Box);

        Assert.Equal(0, (curve.Length - 1) % 3);
        Assert.Equal(new VecD(30, 80), curve[0]);
        Assert.Equal(curve[0], curve[^1]);
    }

    [Fact]
    public void AHeartFillsItsBox()
    {
        VecD[] curve = ShapeGeometry.CurvedOutline(ShapeKind.Heart, Box);

        Assert.Equal(Box.Left, curve.Min(point => point.X), 6);
        Assert.Equal(Box.Right, curve.Max(point => point.X), 6);
        Assert.Equal(Box.Top, curve.Min(point => point.Y), 6);
        Assert.Equal(Box.Bottom, curve.Max(point => point.Y), 6);
    }

    [Fact]
    public void OnlyTheHeartHasCurvedSegments()
    {
        Assert.Empty(ShapeGeometry.CurvedOutline(ShapeKind.Ellipse, Box));
        Assert.Empty(ShapeGeometry.CurvedOutline(ShapeKind.Star, Box));
    }
}
