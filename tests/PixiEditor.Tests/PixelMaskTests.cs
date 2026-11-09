using Drawie.Numerics;
using PixiEditor.ChangeableDocument.Helpers;

namespace PixiEditor.Tests;

/// <summary>Covers the mask operations behind Grow, Shrink and Border selection.</summary>
public class PixelMaskTests
{
    private const int Size = 5;

    private static bool[] Center()
    {
        bool[] mask = new bool[Size * Size];
        mask[(2 * Size) + 2] = true;
        return mask;
    }

    private static bool At(bool[] mask, int x, int y) => mask[(y * Size) + x];

    [Fact]
    public void GrowingAddsTheFourNeighbours()
    {
        bool[] grown = PixelMask.Modify(Center(), Size, Size, SelectionModification.Grow, 1);

        Assert.True(At(grown, 2, 2));
        Assert.True(At(grown, 1, 2));
        Assert.True(At(grown, 3, 2));
        Assert.True(At(grown, 2, 1));
        Assert.True(At(grown, 2, 3));
        Assert.False(At(grown, 1, 1));
    }

    [Fact]
    public void ShrinkingUndoesAGrow()
    {
        bool[] grown = PixelMask.Modify(Center(), Size, Size, SelectionModification.Grow, 1);

        Assert.Equal(Center(), PixelMask.Modify(grown, Size, Size, SelectionModification.Shrink, 1));
    }

    [Fact]
    public void ShrinkingEverythingStopsAtTheCanvasBorder()
    {
        bool[] all = Enumerable.Repeat(true, Size * Size).ToArray();

        bool[] shrunk = PixelMask.Modify(all, Size, Size, SelectionModification.Shrink, 1);

        Assert.False(At(shrunk, 0, 2));
        Assert.True(At(shrunk, 1, 2));
    }

    [Fact]
    public void BorderIsOnlyTheRingOutside()
    {
        bool[] border = PixelMask.Modify(Center(), Size, Size, SelectionModification.Border, 1);

        Assert.False(At(border, 2, 2));
        Assert.True(At(border, 1, 2));
    }

    [Fact]
    public void ASinglePixelTracesToItsFourCorners()
    {
        List<List<VecI>> outlines = PixelMask.TraceOutlines(Center(), Size, Size);

        List<VecI> outline = Assert.Single(outlines);
        Assert.Equal(4, outline.Count);
        Assert.Contains(new VecI(2, 2), outline);
        Assert.Contains(new VecI(3, 3), outline);
    }

    [Fact]
    public void ARowOfPixelsHasNoInternalCorners()
    {
        bool[] mask = new bool[Size * Size];
        for (int x = 0; x < Size; x++)
        {
            mask[(2 * Size) + x] = true;
        }

        Assert.Equal(4, Assert.Single(PixelMask.TraceOutlines(mask, Size, Size)).Count);
    }

    [Fact]
    public void AHoleGetsItsOwnOutline()
    {
        bool[] ring = PixelMask.Modify(Center(), Size, Size, SelectionModification.Grow, 2);
        ring[(2 * Size) + 2] = false;

        Assert.Equal(2, PixelMask.TraceOutlines(ring, Size, Size).Count);
    }

    [Fact]
    public void PixelsTouchingAtACornerStayAsSeparateLoops()
    {
        bool[] mask = new bool[Size * Size];
        mask[(1 * Size) + 1] = true;
        mask[(2 * Size) + 2] = true;

        List<List<VecI>> outlines = PixelMask.TraceOutlines(mask, Size, Size);

        Assert.Equal(2, outlines.Count);
        Assert.All(outlines, outline => Assert.Equal(4, outline.Count));
    }

    [Fact]
    public void AnEmptyMaskHasNoOutlines()
    {
        Assert.Empty(PixelMask.TraceOutlines(new bool[Size * Size], Size, Size));
    }
}
