using PixiEditor.ChangeableDocument.Helpers;

namespace PixiEditor.Tests;

/// <summary>Covers the colour correction behind the layer panel's adjust window.</summary>
public class PixelAdjustTests
{
    private static readonly ColorAdjustments Neutral = new(0, 0, 0, 0);

    private static byte[] Pixel(byte r, byte g, byte b, byte a = 255) => [r, g, b, a];

    private static (byte R, byte G, byte B, byte A) Read(byte[] rgba, int pixel = 0) =>
        (rgba[pixel * 4], rgba[(pixel * 4) + 1], rgba[(pixel * 4) + 2], rgba[(pixel * 4) + 3]);

    [Fact]
    public void DoingNothingChangesNothing()
    {
        byte[] image = Pixel(10, 120, 240);

        Assert.True(PixelAdjust.IsNeutral(Neutral));
        Assert.Equal(image, PixelAdjust.Apply(image, Neutral));
    }

    [Fact]
    public void TurningTheHueHalfWayRoundTurnsRedToCyan()
    {
        var (r, g, b, _) = Read(PixelAdjust.Apply(Pixel(255, 0, 0), Neutral with { Hue = 180 }));

        Assert.InRange(r, 0, 2);
        Assert.InRange(g, 253, 255);
        Assert.InRange(b, 253, 255);
    }

    [Fact]
    public void TurningTheHueAllTheWayRoundComesBackToTheSameColour()
    {
        Assert.Equal(Pixel(200, 40, 90), PixelAdjust.Apply(Pixel(200, 40, 90), Neutral with { Hue = 360 }));
    }

    [Fact]
    public void DrainingTheColourLeavesGreyOfTheSameLightness()
    {
        var (r, g, b, _) = PixelAdjust.Apply(Pixel(255, 0, 0), Neutral with { Saturation = -1 }) is var output
            ? Read(output)
            : default;

        Assert.Equal(r, g);
        Assert.Equal(g, b);
        Assert.InRange(r, 126, 129);
    }

    [Fact]
    public void PushingTheColourUpSaturatesWithoutChangingTheHue()
    {
        var (r, g, b, _) = Read(PixelAdjust.Apply(Pixel(200, 150, 150), Neutral with { Saturation = 1 }));

        Assert.True(r > 200, $"expected the strong channel to climb, got {r}");
        Assert.Equal(g, b);
        Assert.True(g < 150, $"expected the weak channels to fall, got {g}");
    }

    [Theory]
    [InlineData(1, 255)]
    [InlineData(-1, 0)]
    public void LightnessReachesWhiteAndBlack(double lightness, byte expected)
    {
        var (r, g, b, _) = Read(PixelAdjust.Apply(Pixel(120, 60, 30), Neutral with { Lightness = lightness }));

        Assert.Equal(expected, r);
        Assert.Equal(expected, g);
        Assert.Equal(expected, b);
    }

    [Fact]
    public void ContrastPushesValuesApartFromMidGrey()
    {
        byte[] output = PixelAdjust.Apply([64, 192, 128, 255], Neutral with { Contrast = 1 });

        Assert.Equal(0, output[0]);
        Assert.Equal(255, output[1]);
        Assert.InRange(output[2], 127, 129);
    }

    [Fact]
    public void FlatteningTheContrastLeavesOneMidTone()
    {
        byte[] output = PixelAdjust.Apply([10, 200, 90, 255], Neutral with { Contrast = -1 });

        Assert.Equal(output[0], output[1]);
        Assert.InRange(output[0], 127, 128);
    }

    [Fact]
    public void AlphaIsNeverTouched()
    {
        byte[] output = PixelAdjust.Apply(Pixel(200, 100, 50, 77), Neutral with { Lightness = 1, Hue = 90 });

        Assert.Equal(77, output[3]);
    }

    [Fact]
    public void FullyTransparentPixelsAreLeftAsTheyAre()
    {
        byte[] image = Pixel(200, 100, 50, 0);

        Assert.Equal(image, PixelAdjust.Apply(image, Neutral with { Hue = 120, Lightness = 1 }));
    }

    [Fact]
    public void OnlyTheMaskedPixelsChange()
    {
        byte[] image = [255, 0, 0, 255, 255, 0, 0, 255];

        byte[] output = PixelAdjust.Apply(image, Neutral with { Saturation = -1 }, [true, false]);

        Assert.Equal(output[0], output[1]);
        Assert.Equal(255, output[4]);
        Assert.Equal(0, output[5]);
    }

    [Fact]
    public void TheOriginalImageIsNotModified()
    {
        byte[] image = Pixel(10, 20, 30);

        PixelAdjust.Apply(image, Neutral with { Lightness = 1 });

        Assert.Equal(Pixel(10, 20, 30), image);
    }
}
