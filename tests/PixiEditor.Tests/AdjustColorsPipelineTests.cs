using ChunkyImageLib.DataHolders;
using Drawie.Backend.Core.ColorsImpl;
using Drawie.Backend.Core.Vector;
using Drawie.Numerics;
using PixiEditor.ChangeableDocument;
using PixiEditor.ChangeableDocument.Actions;
using PixiEditor.ChangeableDocument.Actions.Generated;
using PixiEditor.ChangeableDocument.Actions.Undo;
using PixiEditor.ChangeableDocument.Changeables.Graph.Interfaces;
using PixiEditor.ChangeableDocument.Changeables.Graph.Nodes;
using PixiEditor.ChangeableDocument.Enums;
using PixiEditor.ChangeableDocument.Helpers;

namespace PixiEditor.Tests;

/// <summary>
/// Runs the layer panel's colour correction over a real layer, which is the part the array tests
/// cannot cover: that it reads the layer, honours the selection and leaves one undo step.
/// </summary>
public class AdjustColorsPipelineTests : PixiEditorTest
{
    private const int CanvasSize = 16;

    private static readonly Color Orange = new(255, 128, 0);

    private static readonly ColorAdjustments Drain = new(0, -1, 0, 0);

    private static void Run(DocumentChangeTracker tracker, params IAction[] actions)
    {
        tracker.ProcessAll(actions.Select(action => (ActionSource.User, action)).ToList());
    }

    /// <summary>An orange 8x8 block at (4,4), with the selection left covering only its left half.</summary>
    private static (DocumentChangeTracker Tracker, Guid Layer) CreateBlock()
    {
        DocumentChangeTracker tracker = new();
        Guid layer = Guid.NewGuid();
        Guid output = Guid.NewGuid();

        Run(tracker,
            new CreateNode_Action(typeof(OutputNode), output, Guid.Empty),
            new ResizeCanvas_Action(new VecI(CanvasSize, CanvasSize), ResizeAnchor.TopLeft),
            new CreateStructureMember_Action(output, layer, typeof(ImageLayerNode)),
            new ChangeBoundary_Action());

        using VectorPath block = new();
        block.AddRect(new RectD(4, 4, 8, 8));
        Run(tracker,
            new SetSelection_Action(block),
            new FloodFill_Action(layer, new VecI(5, 5), Orange, false, 0f, FloodFillMode.Overlay, false, true, 0),
            new ChangeBoundary_Action());

        return (tracker, layer);
    }

    private static void Select(DocumentChangeTracker tracker, RectD area)
    {
        using VectorPath path = new();
        path.AddRect(area);
        Run(tracker, new SetSelection_Action(path), new ChangeBoundary_Action());
    }

    private static Color PixelAt(DocumentChangeTracker tracker, Guid layer, int x, int y)
    {
        var node = (IReadOnlyImageNode)tracker.Document.FindMemberOrThrow(layer);
        return node.GetLayerImageAtFrame(0).GetCommittedPixel(new VecI(x, y));
    }

    private static void Adjust(DocumentChangeTracker tracker, Guid layer, ColorAdjustments adjustments)
    {
        Run(tracker,
            new AdjustColors_Action(layer, adjustments, false, 0),
            new EndAdjustColors_Action(),
            new ChangeBoundary_Action());
    }

    [Fact]
    public void DrainingTheColourGreysTheWholeLayer()
    {
        var (tracker, layer) = CreateBlock();
        Run(tracker, new ClearSelection_Action(), new ChangeBoundary_Action());

        Adjust(tracker, layer, Drain);

        Color grey = PixelAt(tracker, layer, 5, 5);
        Assert.Equal(grey.R, grey.G);
        Assert.Equal(grey.G, grey.B);
        Assert.Equal(255, grey.A);
        Assert.Equal(grey, PixelAt(tracker, layer, 10, 10));
    }

    [Fact]
    public void WithASelectionOnlyThatAreaChanges()
    {
        var (tracker, layer) = CreateBlock();
        Select(tracker, new RectD(4, 4, 4, 8));

        Adjust(tracker, layer, Drain);

        Color inside = PixelAt(tracker, layer, 5, 5);
        Assert.Equal(inside.R, inside.B);
        Assert.Equal(Orange, PixelAt(tracker, layer, 10, 5));
    }

    [Fact]
    public void TheAreaOutsideTheLayerStaysTransparent()
    {
        var (tracker, layer) = CreateBlock();
        Run(tracker, new ClearSelection_Action(), new ChangeBoundary_Action());

        Adjust(tracker, layer, new ColorAdjustments(0, 0, 1, 0));

        Assert.Equal(0, PixelAt(tracker, layer, 0, 0).A);
    }

    [Fact]
    public void DraggingASliderReplacesTheLastValueRatherThanStacking()
    {
        var (tracker, layer) = CreateBlock();
        Run(tracker, new ClearSelection_Action(), new ChangeBoundary_Action());

        Run(tracker,
            new AdjustColors_Action(layer, new ColorAdjustments(90, 0, 0, 0), false, 0),
            new AdjustColors_Action(layer, new ColorAdjustments(180, 0, 0, 0), false, 0),
            new EndAdjustColors_Action(),
            new ChangeBoundary_Action());

        // One turn of 180 degrees, not 90 then another 180 on top of it.
        Color expected = new(0, 127, 255);
        Color actual = PixelAt(tracker, layer, 5, 5);
        Assert.InRange(actual.R, 0, 2);
        Assert.InRange(actual.G, expected.G - 2, expected.G + 2);
        Assert.InRange(actual.B, 253, 255);
    }

    [Fact]
    public void AdjustingUndoesAndRedoesInOneStep()
    {
        var (tracker, layer) = CreateBlock();
        Run(tracker, new ClearSelection_Action(), new ChangeBoundary_Action());
        Adjust(tracker, layer, Drain);

        Run(tracker, new Undo_Action());
        Assert.Equal(Orange, PixelAt(tracker, layer, 5, 5));

        Run(tracker, new Redo_Action());
        Assert.NotEqual(Orange, PixelAt(tracker, layer, 5, 5));
    }

    [Fact]
    public void AnAdjustmentThatChangesNothingLeavesTheLayerAlone()
    {
        var (tracker, layer) = CreateBlock();

        Adjust(tracker, layer, new ColorAdjustments(0, 0, 0, 0));

        Assert.Equal(Orange, PixelAt(tracker, layer, 5, 5));
    }
}
