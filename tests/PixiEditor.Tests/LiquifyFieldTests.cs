using Drawie.Numerics;
using PixiEditor.ChangeableDocument.Helpers;

namespace PixiEditor.Tests;

/// <summary>
/// Covers the warp behind the Liquify tool: that pixels go where the stroke sends them, and that
/// nearest sampling never invents a colour.
/// </summary>
public class LiquifyFieldTests
{
    private const int Size = 21;

    /// <summary>A transparent image with a single 3x3 white block centred on the given point.</summary>
    private static byte[] BlockAt(int centerX, int centerY)
    {
        byte[] image = new byte[Size * Size * 4];
        for (int y = centerY - 1; y <= centerY + 1; y++)
        {
            for (int x = centerX - 1; x <= centerX + 1; x++)
            {
                int offset = ((y * Size) + x) * 4;
                image[offset] = image[offset + 1] = image[offset + 2] = image[offset + 3] = 255;
            }
        }

        return image;
    }

    private static byte[] RenderAll(LiquifyField field, byte[] source, bool smooth = false)
    {
        byte[] output = new byte[source.Length];
        field.Render(source, output, new RectI(0, 0, Size, Size), smooth);
        return output;
    }

    private static byte AlphaAt(byte[] image, int x, int y) => image[(((y * Size) + x) * 4) + 3];

    [Fact]
    public void AnUntouchedFieldRendersTheOriginal()
    {
        byte[] source = BlockAt(10, 10);

        Assert.Equal(source, RenderAll(new LiquifyField(Size, Size), source));
    }

    [Fact]
    public void AnUntouchedFieldHasNoDirtyArea()
    {
        Assert.Null(new LiquifyField(Size, Size).DirtyBounds);
    }

    [Fact]
    public void PushingMovesThePixelsTowardsTheStroke()
    {
        byte[] source = BlockAt(6, 10);
        LiquifyField field = new(Size, Size);

        field.Push(new VecD(6, 10), new VecD(10, 10), 8, 1);
        byte[] output = RenderAll(field, source);

        Assert.Equal(255, AlphaAt(output, 10, 10));
        Assert.Equal(0, AlphaAt(output, 5, 10));
    }

    [Fact]
    public void PushingLeavesPixelsOutsideTheBrushAlone()
    {
        byte[] source = BlockAt(3, 3);
        LiquifyField field = new(Size, Size);

        field.Push(new VecD(15, 15), new VecD(17, 15), 3, 1);

        Assert.Equal(source, RenderAll(field, source));
    }

    [Fact]
    public void ZeroStrengthChangesNothing()
    {
        byte[] source = BlockAt(10, 10);
        LiquifyField field = new(Size, Size);

        field.Push(new VecD(8, 10), new VecD(12, 10), 6, 0);

        Assert.Equal(source, RenderAll(field, source));
    }

    [Theory]
    [InlineData(LiquifyMode.TwirlClockwise)]
    [InlineData(LiquifyMode.TwirlCounterClockwise)]
    [InlineData(LiquifyMode.Pinch)]
    [InlineData(LiquifyMode.Expand)]
    public void TheCentreOfTheBrushStaysPut(LiquifyMode mode)
    {
        byte[] source = BlockAt(10, 10);
        LiquifyField field = new(Size, Size);

        for (int i = 0; i < 10; i++)
        {
            field.Apply(mode, new VecD(10, 10), 8, 1);
        }

        Assert.Equal(255, AlphaAt(RenderAll(field, source), 10, 10));
    }

    [Fact]
    public void ExpandingGrowsAShapeAndPinchingShrinksIt()
    {
        byte[] source = BlockAt(10, 10);
        LiquifyField expanded = new(Size, Size);
        LiquifyField pinched = new(Size, Size);

        for (int i = 0; i < 12; i++)
        {
            expanded.Apply(LiquifyMode.Expand, new VecD(10, 10), 8, 1);
            pinched.Apply(LiquifyMode.Pinch, new VecD(10, 10), 8, 1);
        }

        int original = CountOpaque(source);
        Assert.True(CountOpaque(RenderAll(expanded, source)) > original);
        Assert.True(CountOpaque(RenderAll(pinched, source)) < original);
    }

    [Fact]
    public void NearestSamplingNeverInventsAColour()
    {
        byte[] source = BlockAt(10, 10);
        LiquifyField field = new(Size, Size);

        field.Apply(LiquifyMode.TwirlClockwise, new VecD(10, 10), 8, 1);
        field.Push(new VecD(7, 7), new VecD(13, 12), 7, 0.7);

        Assert.All(RenderAll(field, source), value => Assert.True(value is 0 or 255));
    }

    [Fact]
    public void SmoothSamplingBlendsBetweenPixels()
    {
        byte[] source = BlockAt(10, 10);
        LiquifyField field = new(Size, Size);

        field.Push(new VecD(10, 10), new VecD(10.5, 10), 8, 1);

        Assert.Contains(RenderAll(field, source, smooth: true), value => value is > 0 and < 255);
    }

    [Fact]
    public void PullingFromOutsideTheCanvasGivesTransparency()
    {
        byte[] source = new byte[Size * Size * 4];
        Array.Fill(source, (byte)255);
        LiquifyField field = new(Size, Size);

        field.Push(new VecD(1, 10), new VecD(8, 10), 8, 1);

        Assert.Equal(0, AlphaAt(RenderAll(field, source), 1, 10));
    }

    private static int CountOpaque(byte[] image)
    {
        int count = 0;
        for (int i = 3; i < image.Length; i += 4)
        {
            if (image[i] != 0)
            {
                count++;
            }
        }

        return count;
    }
}
