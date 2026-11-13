using Avalonia.Headless.XUnit;
using Drawie.Numerics;
using PixiEditor.Helpers;
using PixiEditor.Views.Dialogs;

namespace PixiEditor.Tests;

/// <summary>Covers the canvas a sprite sheet needs, and that the dialog reports the same.</summary>
public class SpriteSheetLayoutTests : FullPixiEditorTest
{
    [AvaloniaFact]
    public void FourFramesThirtyTwoWideMakeAStripOneTwentyEightTall()
    {
        Assert.Equal(new VecI(32, 128), SpriteSheetLayout.CanvasFor(new VecI(32, 32), 4, 1));
    }

    [AvaloniaFact]
    public void ColumnsLayTheFramesAcrossInstead()
    {
        Assert.Equal(new VecI(64, 64), SpriteSheetLayout.CanvasFor(new VecI(32, 32), 4, 2));
        Assert.Equal(new VecI(128, 32), SpriteSheetLayout.CanvasFor(new VecI(32, 32), 4, 4));
    }

    [AvaloniaFact]
    public void ARaggedLastRowStillGetsItsFullRow()
    {
        // Five frames two across is three rows, the last half empty. The sheet is read back by
        // multiplying out from the frame size, so the row has to be there.
        Assert.Equal(new VecI(64, 96), SpriteSheetLayout.CanvasFor(new VecI(32, 32), 5, 2));
    }

    [AvaloniaFact]
    public void NonsenseNumbersAreHeldToSomethingUsable()
    {
        Assert.Equal(new VecI(1, 1), SpriteSheetLayout.CanvasFor(new VecI(0, -4), 0, 0));

        // More columns than frames is one row, not a row of empty cells.
        Assert.Equal(new VecI(96, 32), SpriteSheetLayout.CanvasFor(new VecI(32, 32), 3, 10));
    }

    [AvaloniaFact]
    public void TheDialogReportsTheCanvasItWouldMake()
    {
        SpriteSheetSetupPopup popup = new(new VecI(32, 32)) { FrameCount = 4, Columns = 1 };

        Assert.Equal(new VecI(32, 128), popup.CanvasSize);
        Assert.Contains("32 x 128", popup.ResultDescription);
    }
}
