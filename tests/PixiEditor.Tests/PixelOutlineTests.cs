using Drawie.Numerics;
using PixiEditor.ChangeableDocument.Helpers;

namespace PixiEditor.Tests;

/// <summary>Covers the width, corners and fade of outlines.</summary>
public class PixelOutlineTests
{
    private const int Size = 9;

    private static bool[] Center()
    {
        bool[] shape = new bool[Size * Size];
        shape[(4 * Size) + 4] = true;
        return shape;
    }

    private static OutlineStyle Style(int thickness, OutlineCorners corners = OutlineCorners.Round,
        OutlinePosition position = OutlinePosition.Outside, double softness = 0, ShadeFalloff falloff = ShadeFalloff.Smooth, int bands = 2) =>
        new(thickness, position, corners, softness, falloff, bands, OrderedDither.Bayer4);

    private static float At(float[] amounts, int x, int y) => amounts[(y * Size) + x];

    [Fact]
    public void AOnePixelRoundOutlineIsTheFourNeighbours()
    {
        float[] amounts = PixelOutline.Compute(Center(), Size, Size, Style(1));

        Assert.Equal(1f, At(amounts, 3, 4));
        Assert.Equal(1f, At(amounts, 4, 3));
        Assert.Equal(0f, At(amounts, 3, 3));
        Assert.Equal(0f, At(amounts, 4, 4));
    }

    [Fact]
    public void SquareCornersFillTheDiagonals()
    {
        Assert.Equal(1f, At(PixelOutline.Compute(Center(), Size, Size, Style(1, OutlineCorners.Square)), 3, 3));
    }

    [Fact]
    public void DiamondStopsAtTheStepDistance()
    {
        float[] amounts = PixelOutline.Compute(Center(), Size, Size, Style(2, OutlineCorners.Diamond));

        Assert.Equal(1f, At(amounts, 2, 4));
        Assert.Equal(1f, At(amounts, 3, 3));
        Assert.Equal(0f, At(amounts, 2, 3));
    }

    [Fact]
    public void RoundMatchesTheRealDistance()
    {
        float[] amounts = PixelOutline.Compute(Center(), Size, Size, Style(2));

        Assert.Equal(1f, At(amounts, 3, 3));
        Assert.Equal(1f, At(amounts, 2, 4));
        Assert.Equal(0f, At(amounts, 2, 3));
    }

    [Fact]
    public void InsideOutlinesStayWithinTheShape()
    {
        bool[] shape = new bool[Size * Size];
        for (int y = 2; y <= 6; y++)
        {
            for (int x = 2; x <= 6; x++)
            {
                shape[(y * Size) + x] = true;
            }
        }

        float[] amounts = PixelOutline.Compute(shape, Size, Size, Style(1, position: OutlinePosition.Inside));

        Assert.Equal(1f, At(amounts, 2, 4));
        Assert.Equal(0f, At(amounts, 3, 4));
        Assert.Equal(0f, At(amounts, 1, 4));
    }

    [Fact]
    public void CenteredSplitsTheWidthAcrossTheEdge()
    {
        bool[] shape = new bool[Size * Size];
        for (int x = 0; x <= 4; x++)
        {
            for (int y = 0; y < Size; y++)
            {
                shape[(y * Size) + x] = true;
            }
        }

        float[] amounts = PixelOutline.Compute(shape, Size, Size, Style(2, position: OutlinePosition.Centered));

        Assert.Equal(1f, At(amounts, 4, 4));
        Assert.Equal(1f, At(amounts, 5, 4));
        Assert.Equal(0f, At(amounts, 3, 4));
        Assert.Equal(0f, At(amounts, 6, 4));
    }

    [Fact]
    public void TheCanvasBorderIsNotAnEdge()
    {
        bool[] all = Enumerable.Repeat(true, Size * Size).ToArray();

        Assert.All(PixelOutline.Compute(all, Size, Size, Style(2, position: OutlinePosition.Inside)), a => Assert.Equal(0f, a));
    }

    [Fact]
    public void SoftnessFadesTheOuterPart()
    {
        float[] amounts = PixelOutline.Compute(Center(), Size, Size, Style(4, softness: 0.5));

        Assert.Equal(1f, At(amounts, 3, 4));
        Assert.True(At(amounts, 0, 4) < At(amounts, 1, 4));
        Assert.True(At(amounts, 1, 4) < 1f);
    }

    [Fact]
    public void DitheredSoftnessOnlyProducesWholePixels()
    {
        float[] amounts = PixelOutline.Compute(Center(), Size, Size, Style(4, softness: 1, falloff: ShadeFalloff.Dithered));

        Assert.All(amounts, a => Assert.True(a is 0f or 1f));
        Assert.Contains(amounts, a => a == 1f);
    }

    [Fact]
    public void FloodingCountsDiagonalContactAndIgnoresColour()
    {
        byte[] rgba =
        [
            255, 0, 0, 255, 0, 0, 0, 0,
            0, 0, 0, 0, 0, 255, 0, 255,
        ];

        bool[] shape = PixelOutline.FloodFilled(rgba, 2, 2, new VecI(0, 0));

        Assert.True(shape[0]);
        Assert.True(shape[3]);
        Assert.False(shape[1]);
    }

    [Fact]
    public void ClickingEmptySpaceFindsNoShape()
    {
        Assert.DoesNotContain(true, PixelOutline.FloodFilled(new byte[16], 2, 2, new VecI(0, 0)));
    }
}
