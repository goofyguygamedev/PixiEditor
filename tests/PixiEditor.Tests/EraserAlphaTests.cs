using ChunkyImageLib;
using ChunkyImageLib.DataHolders;
using Drawie.Backend.Core.ColorsImpl;
using Drawie.Backend.Core.Surfaces;
using Drawie.Backend.Core.Surfaces.PaintImpl;
using Drawie.Numerics;
using PixiEditor.ChangeableDocument.Changeables.Brushes;
using PixiEditor.ChangeableDocument.Rendering.ContextData;

namespace PixiEditor.Tests;

/// <summary>
/// Covers the eraser taking only part of the alpha away, driven by the alpha of the primary colour.
/// The strength lands on the whole stroke at once rather than on each stamp, so these tests pin down
/// both the maths that works it out and the compositing that applies it.
/// </summary>
public class EraserAlphaTests : PixiEditorTest
{
    private const int Size = 8;

    [Fact]
    public void AnEraseWithoutAStrengthKeepsTheBrushOpacity()
    {
        Assert.Equal(1d, BrushEngine.ResolveOpacity(1d, erasing: true, eraseStrength: null));
    }

    [Fact]
    public void AnEraseStrengthScalesTheBrushOpacity()
    {
        Assert.Equal(0.5d, BrushEngine.ResolveOpacity(1d, erasing: true, eraseStrength: 0.5d));
        Assert.Equal(0.25d, BrushEngine.ResolveOpacity(0.5d, erasing: true, eraseStrength: 0.5d));
    }

    [Fact]
    public void APaintingStrokeIgnoresTheEraseStrength()
    {
        Assert.Equal(1d, BrushEngine.ResolveOpacity(1d, erasing: false, eraseStrength: 0.25d));
    }

    [Theory]
    [InlineData(-1d, 0d)]
    [InlineData(2d, 1d)]
    public void AnOutOfRangeStrengthIsClamped(double strength, double expected)
    {
        Assert.Equal(expected, BrushEngine.ResolveOpacity(1d, erasing: true, strength));
    }

    [Fact]
    public void EditorDataErasesFullyUnlessToldOtherwise()
    {
        Assert.Null(new EditorData(Colors.White, Colors.Black).EraseStrength);
        Assert.Equal(0.5d, new EditorData(Colors.White, Colors.Black, 0.5d).EraseStrength);
    }

    [Fact]
    public void AFullStrengthEraseClearsThePixels()
    {
        Assert.Equal(0, Erase(1d).A);
    }

    [Fact]
    public void AHalfStrengthEraseLeavesHalfTheAlpha()
    {
        Color erased = Erase(0.5d);

        Assert.InRange(erased.A, 120, 136);
        Assert.Equal(255, erased.R);
    }

    [Fact]
    public void AZeroStrengthEraseChangesNothing()
    {
        Assert.Equal(255, Erase(0d).A);
    }

    [Fact]
    public void ErasingTwiceOverTheSameSpotTakesMoreAlphaAway()
    {
        // Two separate strokes should build up, even though the stamps within one stroke do not.
        using ChunkyImage image = WhiteImage();
        EraseLeftHalf(image, 0.5d);
        EraseLeftHalf(image, 0.5d);

        Assert.InRange(image.GetCommittedPixel(new VecI(1, 4)).A, 54, 74);
    }

    [Fact]
    public void OverlappingStampsInOneStrokeDoNotBuildUp()
    {
        // The stamps of a stroke overlap heavily, because the default brush spacing is zero. They
        // gather in one buffer that is composited once, so two of them erase no more than one.
        using ChunkyImage image = WhiteImage();
        image.SetBlendMode(BlendMode.DstOut);
        image.SetOpacity(0.5d);
        image.EnqueueDrawRectangle(Stamp(new VecD(2, 4)));
        image.EnqueueDrawRectangle(Stamp(new VecD(3, 4)));
        image.CommitChanges();

        Assert.InRange(image.GetCommittedPixel(new VecI(2, 4)).A, 120, 136);
        Assert.InRange(image.GetCommittedPixel(new VecI(3, 4)).A, 120, 136);
    }

    [Fact]
    public void APartialEraseLeavesThePixelsOutsideTheStampAlone()
    {
        using ChunkyImage image = WhiteImage();
        EraseLeftHalf(image, 0.5d);

        Assert.Equal(255, image.GetCommittedPixel(new VecI(6, 4)).A);
    }

    private static Color Erase(double strength)
    {
        using ChunkyImage image = WhiteImage();
        EraseLeftHalf(image, strength);
        return image.GetCommittedPixel(new VecI(1, 4));
    }

    private static void EraseLeftHalf(ChunkyImage image, double strength)
    {
        image.SetBlendMode(BlendMode.DstOut);
        image.SetOpacity(BrushEngine.ResolveOpacity(1d, erasing: true, strength));
        image.EnqueueDrawRectangle(new ShapeData(new VecD(2, 4), new VecD(4, 8), 0, 0, 0, Colors.White,
            Colors.White));
        image.CommitChanges();
    }

    private static ShapeData Stamp(VecD center) =>
        new(center, new VecD(2, 2), 0, 0, 0, Colors.White, Colors.White);

    private static ChunkyImage WhiteImage()
    {
        ChunkyImage image = new(new VecI(Size, Size));
        using Paint white = new() { Color = Colors.White, BlendMode = BlendMode.Src };
        image.EnqueueDrawPaint(white);
        image.CommitChanges();
        return image;
    }
}
