using Drawie.Backend.Core.Numerics;
using Drawie.Numerics;
using PixiEditor.Helpers;

namespace PixiEditor.Tests;

/// <summary>
/// Guards the rule that keeps the selection's transform box from writing changes nobody asked for.
/// The box raises its corners-changed event when it is shown, not only when it is dragged, and a
/// change opened from that event takes over the next undo.
/// </summary>
public class SelectionTransformTests
{
    private static readonly RectD Selection = new(10, 10, 30, 40);

    [Fact]
    public void DraggingTheBoxSomewhereElseIsARealChange()
    {
        ShapeCorners dragged = new(new RectD(10, 10, 60, 40));

        Assert.True(SelectionTransform.IsRealChange(Selection, dragged, showingBox: false));
    }

    [Fact]
    public void CornersMatchingTheSelectionAreNot()
    {
        // What showing the box produces: the corners it was just given, which are the selection's
        // own bounds. Writing these would cost an undo step and change nothing.
        ShapeCorners same = new(Selection);

        Assert.False(SelectionTransform.IsRealChange(Selection, same, showingBox: false));
    }

    [Fact]
    public void NothingCountsWhileTheBoxIsBeingShown()
    {
        ShapeCorners elsewhere = new(new RectD(0, 0, 100, 100));

        Assert.False(SelectionTransform.IsRealChange(Selection, elsewhere, showingBox: true));
    }

    [Fact]
    public void WithoutASelectionThereIsNothingToResize()
    {
        ShapeCorners corners = new(new RectD(0, 0, 10, 10));

        Assert.False(SelectionTransform.IsRealChange(null, corners, showingBox: false));
    }

    [Fact]
    public void AFlatSelectionIsNotResizable()
    {
        RectD flat = new(10, 10, 0, 40);
        ShapeCorners corners = new(new RectD(10, 10, 20, 40));

        Assert.False(SelectionTransform.IsRealChange(flat, corners, showingBox: false));
    }

    [Fact]
    public void DegenerateCornersAreRejected()
    {
        // The transform view model refuses to show these, so acting on them would write a
        // selection the box never displayed.
        ShapeCorners degenerate = new(new RectD(10, 10, 0, 0));

        Assert.False(SelectionTransform.IsRealChange(Selection, degenerate, showingBox: false));
    }

    [Fact]
    public void AOnePixelDragStillCounts()
    {
        ShapeCorners nudged = new(new RectD(10, 10, 31, 40));

        Assert.True(SelectionTransform.IsRealChange(Selection, nudged, showingBox: false));
    }
}
