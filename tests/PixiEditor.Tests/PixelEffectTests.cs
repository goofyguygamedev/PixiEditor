using PixiEditor.ChangeableDocument.Helpers;

namespace PixiEditor.Tests;

/// <summary>Covers the dithering and pixel effects on plain arrays.</summary>
public class PixelEffectTests
{
    private static byte[] Solid(int width, int height, byte r, byte g, byte b, byte a = 255)
    {
        byte[] image = new byte[width * height * 4];
        for (int i = 0; i < image.Length; i += 4)
        {
            image[i] = r;
            image[i + 1] = g;
            image[i + 2] = b;
            image[i + 3] = a;
        }

        return image;
    }

    [Fact]
    public void BayerTwoMatchesTheTextbookMatrix()
    {
        Assert.Equal(1 / 8f, Ditherer.Threshold(DitherMode.Bayer2, 0, 0));
        Assert.Equal(5 / 8f, Ditherer.Threshold(DitherMode.Bayer2, 1, 0));
        Assert.Equal(7 / 8f, Ditherer.Threshold(DitherMode.Bayer2, 0, 1));
        Assert.Equal(3 / 8f, Ditherer.Threshold(DitherMode.Bayer2, 1, 1));
    }

    [Theory]
    [InlineData(DitherMode.Bayer4, 16)]
    [InlineData(DitherMode.Bayer8, 64)]
    public void BayerUsesEveryThresholdExactlyOnce(DitherMode mode, int cells)
    {
        int side = (int)Math.Sqrt(cells);
        HashSet<float> thresholds = new();
        for (int y = 0; y < side; y++)
        {
            for (int x = 0; x < side; x++)
            {
                thresholds.Add(Ditherer.Threshold(mode, x, y));
            }
        }

        Assert.Equal(cells, thresholds.Count);
    }

    [Theory]
    [InlineData(DitherMode.None)]
    [InlineData(DitherMode.Bayer4)]
    [InlineData(DitherMode.FloydSteinberg)]
    [InlineData(DitherMode.Atkinson)]
    public void EveryOutputColourComesFromThePalette(DitherMode mode)
    {
        byte[] gradient = new byte[16 * 4];
        for (int i = 0; i < 16; i++)
        {
            gradient[i * 4] = gradient[(i * 4) + 1] = gradient[(i * 4) + 2] = (byte)(i * 17);
            gradient[(i * 4) + 3] = 255;
        }

        byte[] output = Ditherer.Apply(gradient, 4, 4, mode, [0, 0, 0, 255, 255, 255], 2, 1);

        for (int i = 0; i < output.Length; i += 4)
        {
            Assert.True(output[i] is 0 or 255);
        }
    }

    [Fact]
    public void MidGreyDithersToAMixOfBothColours()
    {
        byte[] output = Ditherer.Apply(Solid(4, 4, 128, 128, 128), 4, 4, DitherMode.Bayer4, [0, 0, 0, 255, 255, 255], 2, 1);

        Assert.Contains(output, value => value == 0);
        Assert.Contains(output, value => value == 255 );
    }

    [Fact]
    public void TransparentPixelsAreLeftAlone()
    {
        byte[] output = Ditherer.Apply(Solid(2, 2, 50, 60, 70, 0), 2, 2, DitherMode.Bayer4, [0, 0, 0], 2, 1);

        Assert.Equal(Solid(2, 2, 50, 60, 70, 0), output);
    }

    [Fact]
    public void LevelsDefaultsChangeNothing()
    {
        byte[] image = Solid(2, 2, 10, 128, 240);

        Assert.Equal(image, PixelEffects.Levels(image, 0, 1, 1, 0, 1));
    }

    [Fact]
    public void LevelsRemapsTheInputRange()
    {
        byte[] output = PixelEffects.Levels(Solid(1, 1, 128, 128, 128), 0.25, 0.75, 1, 0, 1);

        Assert.InRange(output[0], 125, 131);
    }

    [Fact]
    public void AutoRangeFindsTheExtremesAndIgnoresTransparency()
    {
        byte[] image = [20, 30, 40, 255, 200, 100, 90, 255, 255, 255, 255, 0];

        Assert.Equal(((byte)20, (byte)200), PixelEffects.FindRange(image));
    }

    [Fact]
    public void AStraightCurveChangesNothing()
    {
        byte[] image = Solid(2, 2, 13, 99, 201);

        Assert.Equal(image, PixelEffects.Curves(image, [0, 0.25, 0.5, 0.75, 1]));
    }

    [Fact]
    public void RaisingTheMidpointBrightensMidtones()
    {
        byte[] output = PixelEffects.Curves(Solid(1, 1, 128, 128, 128), [0, 0.3, 0.7, 0.9, 1]);

        Assert.True(output[0] > 150);
    }

    [Fact]
    public void PixelateFillsEachBlockWithOneColour()
    {
        byte[] image = Solid(2, 1, 0, 0, 0);
        image[4] = image[5] = image[6] = 200;

        byte[] output = PixelEffects.Pixelate(image, 2, 1, 2);

        Assert.Equal(100, output[0]);
        Assert.Equal(100, output[4]);
    }

    [Fact]
    public void AveragingDoesNotPullInTheColourOfTransparentPixels()
    {
        byte[] image = [255, 0, 0, 255, 0, 255, 0, 0];

        byte[] output = PixelEffects.Pixelate(image, 2, 1, 2);

        Assert.Equal(255, output[0]);
        Assert.Equal(0, output[1]);
        Assert.InRange(output[3], 126, 128);
    }

    [Fact]
    public void MotionBlurSmearsSideways()
    {
        byte[] image = new byte[5 * 4];
        image[(2 * 4) + 3] = 255;

        byte[] output = PixelEffects.MotionBlur(image, 5, 1, 0, 3);

        Assert.True(output[(1 * 4) + 3] > 0);
        Assert.True(output[(3 * 4) + 3] > 0);
        Assert.Equal(0, output[3]);
    }

    [Fact]
    public void VignetteDarkensCornersAndLeavesTheCentre()
    {
        byte[] output = PixelEffects.Vignette(Solid(5, 5, 200, 200, 200), 5, 5, 0.3, 0.2, 1);

        Assert.Equal(200, output[((2 * 5) + 2) * 4]);
        Assert.Equal(0, output[0]);
    }

    [Fact]
    public void ChromaticAberrationSplitsTheChannels()
    {
        byte[] image = new byte[3 * 4];
        image[4] = image[5] = image[6] = image[7] = 255;

        byte[] output = PixelEffects.ChromaticAberration(image, 3, 1, 1, 0);

        Assert.Equal(255, output[8]);
        Assert.Equal(255, output[2]);
    }

    [Fact]
    public void MedianRemovesASpeck()
    {
        byte[] image = Solid(3, 3, 10, 10, 10);
        image[4 * 4] = 255;

        Assert.Equal(10, PixelEffects.Median(image, 3, 3, 1)[4 * 4]);
    }
}
