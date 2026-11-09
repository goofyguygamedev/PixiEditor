using Drawie.Numerics;
using PixiEditor.ChangeableDocument.Helpers;

namespace PixiEditor.Tests;

/// <summary>Covers Keep Edges: that lines ride the warp but stay the width they were drawn.</summary>
public class LiquifyEdgeTests
{
    private const int Size = 31;
    private const int LineX = 15;

    /// <summary>Orange to the left of a vertical black line, transparent to the right.</summary>
    private static byte[] OutlinedFill(int lineWidth = 1)
    {
        byte[] image = new byte[Size * Size * 4];
        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < LineX + lineWidth; x++)
            {
                int offset = ((y * Size) + x) * 4;
                bool line = x >= LineX;
                image[offset] = line ? (byte)0 : (byte)255;
                image[offset + 1] = line ? (byte)0 : (byte)128;
                image[offset + 2] = 0;
                image[offset + 3] = 255;
            }
        }

        return image;
    }

    private static byte[] RenderAll(LiquifyField field, byte[] source)
    {
        byte[] output = new byte[source.Length];
        field.Render(source, output, new RectI(0, 0, Size, Size), false);
        return output;
    }

    private static LiquifyField KeepingEdges(byte[] source, int width = 2)
    {
        LiquifyField field = new(Size, Size);
        field.KeepEdges(source, width);
        return field;
    }

    private static bool IsBlack(byte[] image, int x, int y)
    {
        int offset = ((y * Size) + x) * 4;
        return image[offset + 3] == 255 && image[offset] == 0 && image[offset + 1] == 0;
    }

    private static int BlackInRow(byte[] image, int y) =>
        Enumerable.Range(0, Size).Count(x => IsBlack(image, x, y));

    private static bool HasTwoByTwoBlock(byte[] image)
    {
        for (int y = 0; y < Size - 1; y++)
        {
            for (int x = 0; x < Size - 1; x++)
            {
                if (IsBlack(image, x, y) && IsBlack(image, x + 1, y) && IsBlack(image, x, y + 1) && IsBlack(image, x + 1, y + 1))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>Every black pixel, joined through 8 connected steps, is one piece.</summary>
    private static bool LineIsUnbroken(byte[] image)
    {
        List<(int X, int Y)> black = new();
        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                if (IsBlack(image, x, y))
                {
                    black.Add((x, y));
                }
            }
        }

        HashSet<(int X, int Y)> seen = [black[0]];
        Queue<(int X, int Y)> pending = new([black[0]]);
        while (pending.Count > 0)
        {
            (int x, int y) = pending.Dequeue();
            foreach ((int X, int Y) next in black.Where(p => Math.Abs(p.X - x) <= 1 && Math.Abs(p.Y - y) <= 1))
            {
                if (seen.Add(next))
                {
                    pending.Enqueue(next);
                }
            }
        }

        return seen.Count == black.Count;
    }

    [Fact]
    public void AnUntouchedFieldStillRendersTheOriginal()
    {
        byte[] source = OutlinedFill();

        Assert.Equal(source, RenderAll(KeepingEdges(source), source));
    }

    [Fact]
    public void PushingCarriesTheLineAndLeavesNothingBehind()
    {
        byte[] source = OutlinedFill();
        LiquifyField field = KeepingEdges(source);

        field.Push(new VecD(LineX, 15), new VecD(LineX + 4, 15), 10, 1);
        byte[] output = RenderAll(field, source);

        Assert.False(IsBlack(output, LineX, 15));
        Assert.True(Enumerable.Range(LineX + 3, 3).Any(x => IsBlack(output, x, 15)));
    }

    [Fact]
    public void APushedLineStaysOnePixelWideAndUnbroken()
    {
        byte[] source = OutlinedFill();
        LiquifyField field = KeepingEdges(source);

        field.Push(new VecD(LineX, 15), new VecD(LineX + 5, 15), 10, 1);
        byte[] output = RenderAll(field, source);

        Assert.False(HasTwoByTwoBlock(output));
        Assert.True(LineIsUnbroken(output));
        Assert.All(Enumerable.Range(0, Size), y => Assert.True(BlackInRow(output, y) >= 1));
    }

    [Fact]
    public void StretchingAcrossALineThickensItOnlyWithoutKeepEdges()
    {
        byte[] source = OutlinedFill();
        LiquifyField plain = new(Size, Size);
        LiquifyField kept = KeepingEdges(source);

        for (int i = 0; i < 12; i++)
        {
            plain.Apply(LiquifyMode.Expand, new VecD(LineX - 1, 15), 13, 1);
            kept.Apply(LiquifyMode.Expand, new VecD(LineX - 1, 15), 13, 1);
        }

        Assert.True(BlackInRow(RenderAll(plain, source), 15) > 1);
        Assert.Equal(1, BlackInRow(RenderAll(kept, source), 15));
    }

    [Fact]
    public void ATwoPixelLineStaysTwoPixelsWide()
    {
        byte[] source = OutlinedFill(2);
        LiquifyField field = KeepingEdges(source);

        for (int i = 0; i < 12; i++)
        {
            field.Apply(LiquifyMode.Expand, new VecD(LineX - 4, 15), 13, 1);
        }

        Assert.Equal(2, BlackInRow(RenderAll(field, source), 15));
    }

    [Fact]
    public void PinchingDoesNotBreakTheLine()
    {
        byte[] source = OutlinedFill();
        LiquifyField field = KeepingEdges(source);

        for (int i = 0; i < 15; i++)
        {
            field.Apply(LiquifyMode.Pinch, new VecD(LineX, 15), 10, 1);
        }

        Assert.True(LineIsUnbroken(RenderAll(field, source)));
    }

    [Fact]
    public void EverythingOutsideTheBrushIsUntouched()
    {
        byte[] source = OutlinedFill();
        LiquifyField field = KeepingEdges(source);

        field.Push(new VecD(LineX, 15), new VecD(LineX + 3, 15), 5, 1);
        byte[] output = RenderAll(field, source);

        foreach (int y in new[] { 0, 1, 2, 3, 27, 28, 29, 30 })
        {
            Assert.True(IsBlack(output, LineX, y));
            Assert.Equal(1, BlackInRow(output, y));
        }
    }

    [Fact]
    public void NoNewColoursAppear()
    {
        byte[] source = OutlinedFill();
        LiquifyField field = KeepingEdges(source);

        field.Push(new VecD(LineX, 15), new VecD(LineX + 5, 12), 10, 1);
        field.Apply(LiquifyMode.TwirlClockwise, new VecD(LineX, 15), 8, 1);
        byte[] output = RenderAll(field, source);

        for (int i = 0; i < output.Length; i += 4)
        {
            Assert.True(output[i] is 0 or 255);
            Assert.True(output[i + 1] is 0 or 128);
        }
    }

    [Fact]
    public void ALineTurnedByATwirlKeepsItsWidth()
    {
        byte[] source = OutlinedFill(2);
        LiquifyField field = KeepingEdges(source);

        for (int i = 0; i < 6; i++)
        {
            field.Apply(LiquifyMode.TwirlClockwise, new VecD(LineX, 15), 12, 1);
        }

        byte[] output = RenderAll(field, source);
        Assert.False(HasThreeByThreeBlock(output));
    }

    [Fact]
    public void ADitherFieldWarpsTheSameAsWithoutKeepEdges()
    {
        byte[] source = new byte[Size * Size * 4];
        for (int i = 0; i < Size * Size; i++)
        {
            source[i * 4] = ((i % Size) + (i / Size)) % 2 == 0 ? (byte)255 : (byte)0;
            source[(i * 4) + 3] = 255;
        }

        LiquifyField plain = new(Size, Size);
        LiquifyField kept = KeepingEdges(source);
        plain.Push(new VecD(10, 15), new VecD(16, 15), 8, 1);
        kept.Push(new VecD(10, 15), new VecD(16, 15), 8, 1);

        Assert.Equal(RenderAll(plain, source), RenderAll(kept, source));
    }

    private static bool HasThreeByThreeBlock(byte[] image)
    {
        for (int y = 0; y < Size - 2; y++)
        {
            for (int x = 0; x < Size - 2; x++)
            {
                bool solid = true;
                for (int i = 0; i < 9; i++)
                {
                    solid &= IsBlack(image, x + (i % 3), y + (i / 3));
                }

                if (solid)
                {
                    return true;
                }
            }
        }

        return false;
    }
}
