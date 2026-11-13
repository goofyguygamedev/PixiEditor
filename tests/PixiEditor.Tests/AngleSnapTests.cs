using Drawie.Numerics;
using PixiEditor.Helpers;

namespace PixiEditor.Tests;

/// <summary>
/// Covers the angle lock the gradient uses while Shift is held, both for the first drag and for a
/// handle dragged afterwards.
/// </summary>
public class AngleSnapTests
{
    private const double Step = 15;

    private static readonly VecD Origin = new(10, 10);

    private static VecD At(double degrees, double length)
    {
        double radians = degrees * Math.PI / 180;
        return Origin + new VecD(Math.Cos(radians) * length, Math.Sin(radians) * length);
    }

    private static double AngleOf(VecD point)
    {
        VecD line = point - Origin;
        double degrees = Math.Atan2(line.Y, line.X) * 180 / Math.PI;
        return degrees < 0 ? degrees + 360 : degrees;
    }

    [Theory]
    [InlineData(0)]
    [InlineData(15)]
    [InlineData(30)]
    [InlineData(45)]
    [InlineData(90)]
    [InlineData(135)]
    [InlineData(180)]
    public void AnAngleAlreadyOnAStepIsLeftWhereItIs(double degrees)
    {
        VecD point = At(degrees, 40);

        VecD snapped = GeometryHelper.GetAngleSnappedPosition(Origin, point, Step);

        Assert.True((snapped - point).Length < 0.001, $"{degrees} degrees moved to {AngleOf(snapped)}");
    }

    [Theory]
    [InlineData(7, 0)]
    [InlineData(11, 15)]
    [InlineData(37, 30)]
    [InlineData(38, 45)]
    [InlineData(-7, 0)]
    [InlineData(100, 105)]
    public void AnAngleBetweenStepsGoesToTheNearestOne(double degrees, double expected)
    {
        VecD snapped = GeometryHelper.GetAngleSnappedPosition(Origin, At(degrees, 40), Step);

        double actual = AngleOf(snapped);
        double wrapped = expected < 0 ? expected + 360 : expected;
        Assert.True(Math.Abs(actual - wrapped) < 0.001, $"{degrees} degrees snapped to {actual}, expected {wrapped}");
    }

    [Fact]
    public void TheLengthIsKept()
    {
        VecD snapped = GeometryHelper.GetAngleSnappedPosition(Origin, At(37, 40), Step);

        Assert.Equal(40, (snapped - Origin).Length, 3);
    }

    [Fact]
    public void SnappingTwiceChangesNothingTheSecondTime()
    {
        // The gradient hands the snapped point back to its overlay, which reports the move again,
        // so a second pass has to be a no-op or the two would never settle.
        VecD once = GeometryHelper.GetAngleSnappedPosition(Origin, At(37, 40), Step);
        VecD twice = GeometryHelper.GetAngleSnappedPosition(Origin, once, Step);

        Assert.True((twice - once).Length < 0.001, $"{once} became {twice}");
    }

    [Fact]
    public void APointOnTheOriginIsLeftAlone()
    {
        Assert.Equal(Origin, GeometryHelper.GetAngleSnappedPosition(Origin, Origin, Step));
    }

    [Fact]
    public void NoStepMeansNoSnapping()
    {
        VecD point = At(37, 40);

        Assert.Equal(point, GeometryHelper.GetAngleSnappedPosition(Origin, point, 0));
    }

    [Fact]
    public void FortyFiveDegreeStepsStillWork()
    {
        VecD snapped = GeometryHelper.GetAngleSnappedPosition(Origin, At(37, 40), 45);

        Assert.True(Math.Abs(AngleOf(snapped) - 45) < 0.001, $"snapped to {AngleOf(snapped)}");
    }
}
