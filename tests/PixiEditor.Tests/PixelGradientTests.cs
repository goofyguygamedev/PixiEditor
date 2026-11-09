using Drawie.Backend.Core.ColorsImpl;
using Drawie.Numerics;
using PixiEditor.ChangeableDocument.Helpers;

namespace PixiEditor.Tests;

/// <summary>Covers where a gradient lands and how its colours step.</summary>
public class PixelGradientTests
{
    private static readonly VecD Left = new(0, 0);
    private static readonly VecD Right = new(10, 0);
    private static readonly Color Black = new(0, 0, 0);
    private static readonly Color White = new(255, 255, 255);

    private static GradientStyle Style(ShadeFalloff falloff, int steps = 2, GradientShape shape = GradientShape.Linear) =>
        new(shape, falloff, steps, OrderedDither.Bayer4, false);

    private static byte[] Row(GradientStyle style) =>
        PixelGradient.Render(10, 1, Left, Right, style, Black, White, 1, null)!;

    [Theory]
    [InlineData(GradientShape.Linear, 5, 0, 0.5)]
    [InlineData(GradientShape.Linear, -5, 0, 0)]
    [InlineData(GradientShape.Reflected, -5, 0, 0.5)]
    [InlineData(GradientShape.Radial, 0, 5, 0.5)]
    [InlineData(GradientShape.Diamond, 3, 2, 0.5)]
    [InlineData(GradientShape.Angular, -5, 0, 0.5)]
    public void PositionsFollowTheShape(GradientShape shape, double x, double y, double expected)
    {
        Assert.Equal(expected, PixelGradient.Position(shape, Left, Right, x, y), 3);
    }

    [Fact]
    public void ALineWithNoLengthPaintsNothing()
    {
        Assert.Null(PixelGradient.Render(4, 4, Left, Left, Style(ShadeFalloff.Smooth), Black, White, 1, null));
    }

    [Fact]
    public void SmoothRunsFromOneColourToTheOther()
    {
        byte[] row = Row(Style(ShadeFalloff.Smooth));

        Assert.True(row[0] < 20);
        Assert.True(row[9 * 4] > 235);
        Assert.InRange(row[5 * 4], 130, 150);
    }

    [Fact]
    public void HardSplitsAtTheMiddle()
    {
        byte[] row = Row(Style(ShadeFalloff.Hard));

        Assert.Equal(0, row[4 * 4]);
        Assert.Equal(255, row[5 * 4]);
    }

    [Fact]
    public void BandsUseExactlyThatManyColours()
    {
        byte[] row = Row(Style(ShadeFalloff.Bands, 3));

        HashSet<byte> values = Enumerable.Range(0, 10).Select(i => row[i * 4]).ToHashSet();
        Assert.Equal(3, values.Count);
    }

    [Fact]
    public void TwoStepDitherUsesOnlyTheEndColoursButMixesThem()
    {
        byte[] output = PixelGradient.Render(10, 10, Left, Right, Style(ShadeFalloff.Dithered), Black, White, 1, null)!;

        Assert.All(Enumerable.Range(0, 100), i => Assert.True(output[i * 4] is 0 or 255));
        byte[] middleColumn = Enumerable.Range(0, 10).Select(y => output[((y * 10) + 5) * 4]).ToArray();
        Assert.Contains((byte)0, middleColumn);
        Assert.Contains((byte)255, middleColumn);
    }

    [Fact]
    public void MoreDitherStepsAddColoursBetweenTheEnds()
    {
        byte[] output = PixelGradient.Render(10, 10, Left, Right, Style(ShadeFalloff.Dithered, 3), Black, White, 1, null)!;

        Assert.Contains(Enumerable.Range(0, 100), i => output[i * 4] is > 100 and < 155);
    }

    [Fact]
    public void ReverseSwapsTheEnds()
    {
        byte[] row = Row(Style(ShadeFalloff.Hard) with { Reverse = true });

        Assert.Equal(255, row[0]);
        Assert.Equal(0, row[9 * 4]);
    }

    [Fact]
    public void TheMaskKeepsPixelsOut()
    {
        bool[] mask = new bool[10];
        mask[3] = true;

        byte[] output = PixelGradient.Render(10, 1, Left, Right, Style(ShadeFalloff.Smooth), Black, White, 1, mask)!;

        Assert.Equal(255, output[(3 * 4) + 3]);
        Assert.Equal(0, output[(4 * 4) + 3]);
    }

    [Fact]
    public void FadingToTransparentFadesTheAlphaOnly()
    {
        byte[] output = PixelGradient.Render(10, 1, Left, Right, Style(ShadeFalloff.Smooth), White, White.WithAlpha(0), 1, null)!;

        Assert.Equal(255, output[8 * 4]);
        Assert.True(output[(0 * 4) + 3] > output[(8 * 4) + 3]);
    }
}
