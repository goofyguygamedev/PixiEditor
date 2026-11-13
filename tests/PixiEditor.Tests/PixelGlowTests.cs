using PixiEditor.ChangeableDocument.Helpers;
using Drawie.Backend.Core.ColorsImpl;

namespace PixiEditor.Tests;

/// <summary>
/// Covers the glow that can be banded and dithered, which is the thing a blurred copy cannot do.
/// </summary>
public class PixelGlowTests
{
    private const int Size = 21;

    private static readonly Color White = new(255, 255, 255);

    /// <summary>A single red pixel in the middle of an otherwise empty image.</summary>
    private static byte[] OneRedPixel()
    {
        byte[] image = new byte[Size * Size * 4];
        int middle = (((Size / 2) * Size) + (Size / 2)) * 4;
        image[middle] = 255;
        image[middle + 3] = 255;
        return image;
    }

    private static GlowStyle Style(double radius = 5, ShadeFalloff falloff = ShadeFalloff.Smooth, int bands = 3,
        bool inside = false) =>
        new(radius, 1, falloff, bands, OrderedDither.Bayer4, inside);

    private static (byte R, byte G, byte B, byte A) At(byte[] rgba, int x, int y)
    {
        int offset = ((y * Size) + x) * 4;
        return (rgba[offset], rgba[offset + 1], rgba[offset + 2], rgba[offset + 3]);
    }

    [Fact]
    public void NothingToGlowGivesNothing()
    {
        Assert.Null(PixelGlow.Render(new byte[Size * Size * 4], Size, Size, Style(), White));
    }

    [Fact]
    public void AZeroRadiusGivesNothing()
    {
        Assert.Null(PixelGlow.Render(OneRedPixel(), Size, Size, Style(radius: 0), White));
    }

    [Fact]
    public void TheLightFadesOutWithDistance()
    {
        byte[] glow = PixelGlow.Render(OneRedPixel(), Size, Size, Style(), White)!;

        byte near = At(glow, 11, 10).A;
        byte far = At(glow, 14, 10).A;

        Assert.True(near > far, $"expected {near} to be brighter than {far}");
        Assert.True(near > 0, "expected light next to the source");
    }

    [Fact]
    public void NoLightReachesBeyondTheRadius()
    {
        byte[] glow = PixelGlow.Render(OneRedPixel(), Size, Size, Style(radius: 4), White)!;

        Assert.Equal(0, At(glow, 15, 10).A);
        Assert.Equal(0, At(glow, 10, 2).A);
    }

    [Fact]
    public void TheArtworksOwnPixelsAreLeftClearUnlessItGlowsInwards()
    {
        byte[] outward = PixelGlow.Render(OneRedPixel(), Size, Size, Style(), White)!;
        Assert.Equal(0, At(outward, 10, 10).A);

        byte[] inward = PixelGlow.Render(OneRedPixel(), Size, Size, Style(inside: true), White)!;
        Assert.True(At(inward, 10, 10).A > 0, "expected the source pixel to be lit from the inside");
    }

    [Fact]
    public void AnUntintedGlowTakesTheColourOfTheArtworkItCameFrom()
    {
        byte[] glow = PixelGlow.Render(OneRedPixel(), Size, Size, Style(), new Color(0, 0, 0, 0))!;

        var (r, g, b, a) = At(glow, 12, 10);

        Assert.True(a > 0, "expected light");
        Assert.Equal(255, r);
        Assert.Equal(0, g);
        Assert.Equal(0, b);
    }

    [Fact]
    public void ATintOverridesTheArtworksColour()
    {
        byte[] glow = PixelGlow.Render(OneRedPixel(), Size, Size, Style(), new Color(0, 0, 255))!;

        var (r, _, b, _) = At(glow, 12, 10);

        Assert.Equal(0, r);
        Assert.Equal(255, b);
    }

    [Fact]
    public void BandsGiveOnlyAHandfulOfBrightnesses()
    {
        byte[] glow = PixelGlow.Render(OneRedPixel(), Size, Size,
            Style(radius: 8, falloff: ShadeFalloff.Bands, bands: 3), White)!;

        HashSet<byte> levels = new();
        for (int i = 3; i < glow.Length; i += 4)
        {
            if (glow[i] != 0)
            {
                levels.Add(glow[i]);
            }
        }

        // Three bands, of which the outermost is empty, so two visible brightnesses.
        Assert.True(levels.Count <= 3, $"expected at most three levels, got {levels.Count}");
        Assert.True(levels.Count >= 2, $"expected more than one level, got {levels.Count}");
    }

    [Fact]
    public void ADitheredGlowIsMadeOfOnOrOffPixels()
    {
        byte[] glow = PixelGlow.Render(OneRedPixel(), Size, Size,
            Style(radius: 8, falloff: ShadeFalloff.Dithered, bands: 2), White)!;

        for (int i = 3; i < glow.Length; i += 4)
        {
            Assert.True(glow[i] is 0 or 255, $"expected only on or off, got {glow[i]}");
        }

        Assert.Contains((byte)255, glow);
    }

    [Fact]
    public void AHardFalloffGivesOneFlatHalo()
    {
        byte[] glow = PixelGlow.Render(OneRedPixel(), Size, Size,
            Style(radius: 6, falloff: ShadeFalloff.Hard), White)!;

        for (int i = 3; i < glow.Length; i += 4)
        {
            Assert.True(glow[i] is 0 or 255, $"expected only on or off, got {glow[i]}");
        }
    }

    [Fact]
    public void StrengthScalesTheWholeGlow()
    {
        byte[] full = PixelGlow.Render(OneRedPixel(), Size, Size, Style(), White)!;
        byte[] half = PixelGlow.Render(OneRedPixel(), Size, Size,
            new GlowStyle(5, 0.5, ShadeFalloff.Smooth, 3, OrderedDither.Bayer4, false), White)!;

        Assert.InRange(At(half, 11, 10).A, (At(full, 11, 10).A / 2) - 2, (At(full, 11, 10).A / 2) + 2);
    }

    [Fact]
    public void TheGlowIsRoundRatherThanSquare()
    {
        byte[] glow = PixelGlow.Render(OneRedPixel(), Size, Size, Style(radius: 5), White)!;

        // A diagonal step costs more than a straight one, so the corner of the box is darker.
        Assert.True(At(glow, 13, 13).A < At(glow, 13, 10).A,
            "expected the diagonal to fade faster than the straight run");
    }
}
