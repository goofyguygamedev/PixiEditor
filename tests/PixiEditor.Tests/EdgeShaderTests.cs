using Drawie.Numerics;
using PixiEditor.ChangeableDocument.Helpers;

namespace PixiEditor.Tests;

/// <summary>
/// Covers the region and shading maths behind the Shade tool and the Shade Edges node. Both sit on
/// plain arrays, so the behaviour that matters to an artist can be pinned down without a canvas.
/// </summary>
public class EdgeShaderTests
{
    private const int Size = 12;

    /// <summary>A 12x12 grid whose middle 8x8 is region 0, ringed by region 1.</summary>
    private static int[] BoxedRegion()
    {
        int[] regions = new int[Size * Size];
        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                regions[(y * Size) + x] = x is >= 2 and <= 9 && y is >= 2 and <= 9 ? 0 : 1;
            }
        }

        return regions;
    }

    /// <summary>Region 1 is excluded so the tests look only at the box itself.</summary>
    private static float[] ShadeBox(EdgeShadeShape shape) =>
        EdgeShader.Compute(BoxedRegion(), Size, Size, shape, shadeable: [true, false]);

    private static float At(float[] amounts, int x, int y) => amounts[(y * Size) + x];

    [Fact]
    public void AnAngleOfZeroShadesTheRightSideOnly()
    {
        float[] amounts = ShadeBox(new EdgeShadeShape(0, 0, 2, ShadeFalloff.Hard, 1));

        Assert.Equal(1f, At(amounts, 9, 5));
        Assert.Equal(1f, At(amounts, 8, 5));
        Assert.Equal(0f, At(amounts, 7, 5));
        Assert.Equal(0f, At(amounts, 2, 5));
    }

    [Fact]
    public void AnAngleOfNinetyMeansUp()
    {
        float[] amounts = ShadeBox(new EdgeShadeShape(90, 0, 1, ShadeFalloff.Hard, 1));

        Assert.Equal(1f, At(amounts, 5, 2));
        Assert.Equal(0f, At(amounts, 5, 9));
    }

    [Fact]
    public void AFullSpreadShadesARingAllTheWayAround()
    {
        float[] amounts = ShadeBox(new EdgeShadeShape(0, 360, 1, ShadeFalloff.Hard, 1));

        Assert.Equal(1f, At(amounts, 2, 5));
        Assert.Equal(1f, At(amounts, 9, 5));
        Assert.Equal(1f, At(amounts, 5, 2));
        Assert.Equal(1f, At(amounts, 5, 9));
        Assert.Equal(0f, At(amounts, 5, 5));
    }

    [Fact]
    public void BandsStepDownFromTheEdge()
    {
        float[] amounts = ShadeBox(new EdgeShadeShape(0, 0, 4, ShadeFalloff.Bands, 2));

        Assert.Equal(1f, At(amounts, 9, 5));
        Assert.Equal(1f, At(amounts, 8, 5));
        Assert.Equal(0.5f, At(amounts, 7, 5));
        Assert.Equal(0.5f, At(amounts, 6, 5));
        Assert.Equal(0f, At(amounts, 5, 5));
    }

    [Fact]
    public void TheCanvasBorderIsNotAnEdge()
    {
        int[] regions = new int[Size * Size];

        float[] amounts = EdgeShader.Compute(regions, Size, Size, new EdgeShadeShape(0, 360, 3, ShadeFalloff.Hard, 1));

        Assert.All(amounts, amount => Assert.Equal(0f, amount));
    }

    [Fact]
    public void OnlyPixelsInTheEdgeMaskCountAsAnEdge()
    {
        int[] regions = BoxedRegion();
        bool[] edgeMask = new bool[regions.Length];

        float[] amounts = EdgeShader.Compute(regions, Size, Size, new EdgeShadeShape(0, 0, 2, ShadeFalloff.Hard, 1),
            edgeMask, [true, false]);

        Assert.All(amounts, amount => Assert.Equal(0f, amount));
    }

    [Fact]
    public void AWalkPassesThroughAPatchThatIsNotAnEdge()
    {
        int[] regions = BoxedRegion();
        regions[(5 * Size) + 8] = 2; // a highlight inside the box, one pixel in from the right edge
        bool[] edgeMask = regions.Select(region => region == 1).ToArray();

        float[] amounts = EdgeShader.Compute(regions, Size, Size, new EdgeShadeShape(0, 0, 3, ShadeFalloff.Hard, 1),
            edgeMask, [true, false, false]);

        Assert.Equal(1f, At(amounts, 7, 5));
    }

    [Fact]
    public void SegmentingKeepsSeparateColoursApartAndSkipsTransparency()
    {
        byte[] rgba =
        [
            255, 0, 0, 255, 255, 0, 0, 255,
            0, 0, 255, 255, 0, 0, 0, 0,
        ];

        int[] regions = EdgeShader.SegmentLikeColors(rgba, 2, 2, 0, out int count);

        Assert.Equal(2, count);
        Assert.Equal(regions[0], regions[1]);
        Assert.NotEqual(regions[0], regions[2]);
        Assert.Equal(EdgeShader.NoRegion, regions[3]);
    }

    [Fact]
    public void ToleranceComparesAgainstTheSeedSoGradientsDoNotChain()
    {
        // Each neighbour is within tolerance of the last, but the far end is not within tolerance
        // of the start: a neighbour-to-neighbour comparison would merge the whole row.
        byte[] rgba =
        [
            100, 100, 100, 255,
            110, 110, 110, 255,
            120, 120, 120, 255,
        ];

        EdgeShader.SegmentLikeColors(rgba, 3, 1, 12 / 255d, out int count);

        Assert.Equal(2, count);
    }

    [Fact]
    public void FloodingOnlyTakesTheConnectedRegion()
    {
        byte[] rgba =
        [
            9, 9, 9, 255, 1, 1, 1, 255, 9, 9, 9, 255,
        ];

        int[] regions = EdgeShader.FloodRegion(rgba, 3, 1, new VecI(0, 0), 0);

        Assert.Equal(0, regions[0]);
        Assert.Equal(EdgeShader.NoRegion, regions[1]);
        Assert.Equal(EdgeShader.NoRegion, regions[2]);
    }

    [Fact]
    public void AMaskedRegionIgnoresEmptyPixels()
    {
        byte[] rgba = [5, 5, 5, 255, 0, 0, 0, 0];

        int[] regions = EdgeShader.RegionFromMask([true, true], rgba);

        Assert.Equal(0, regions[0]);
        Assert.Equal(EdgeShader.NoRegion, regions[1]);
    }

    [Fact]
    public void BoundsCoverExactlyTheShadedPixels()
    {
        float[] amounts = ShadeBox(new EdgeShadeShape(0, 0, 2, ShadeFalloff.Hard, 1));

        Assert.Equal(new RectI(8, 2, 2, 8), EdgeShader.FindBounds(amounts, Size, Size));
    }

    [Fact]
    public void NoShadeMeansNoBounds()
    {
        Assert.Null(EdgeShader.FindBounds(new float[4], 2, 2));
    }
}
