using Drawie.Backend.Core.ColorsImpl;
using Drawie.Backend.Core.Vector;
using Drawie.Numerics;
using ChunkyImageLib.DataHolders;
using PixiEditor.ChangeableDocument;
using PixiEditor.ChangeableDocument.Actions;
using PixiEditor.ChangeableDocument.Actions.Generated;
using PixiEditor.ChangeableDocument.Actions.Undo;
using PixiEditor.ChangeableDocument.Changeables.Graph.Interfaces;
using PixiEditor.ChangeableDocument.Changeables.Graph.Nodes;
using PixiEditor.ChangeableDocument.Changes.Drawing;
using PixiEditor.ChangeableDocument.Enums;
using PixiEditor.ChangeableDocument.Helpers;

namespace PixiEditor.Tests;

/// <summary>Runs the Gradient and Outline changes and Keep Edges on real layers, including undo.</summary>
public class GradientAndOutlinePipelineTests : PixiEditorTest
{
    private const int CanvasSize = 16;

    private static readonly Color Orange = new(255, 128, 0);
    private static readonly Color Black = new(0, 0, 0);
    private static readonly Color White = new(255, 255, 255);

    private static void Run(DocumentChangeTracker tracker, params IAction[] actions)
    {
        tracker.ProcessAll(actions.Select(action => (ActionSource.User, action)).ToList());
    }

    /// <summary>An orange 8x8 block at (4,4), with the selection already cleared.</summary>
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

        Select(tracker, new RectD(4, 4, 8, 8));
        Run(tracker,
            new FloodFill_Action(layer, new VecI(5, 5), Orange, false, 0f, FloodFillMode.Overlay, false, true, 0),
            new ClearSelection_Action(),
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

    private static GradientParameters HardGradient(GradientArea area = GradientArea.Layer) =>
        new(new VecD(0, 8), new VecD(16, 8),
            new GradientStyle(GradientShape.Linear, ShadeFalloff.Hard, 2, OrderedDither.Bayer4, false),
            Black, White, BlendMode.Normal, 1, area, 0);

    private static void Gradient(DocumentChangeTracker tracker, Guid layer, GradientParameters parameters)
    {
        Run(tracker,
            new GradientFill_Action(layer, parameters, false, 0),
            new EndGradientFill_Action(),
            new ChangeBoundary_Action());
    }

    private static OutlineParameters BlackOutline(OutlineTarget target = OutlineTarget.Shape) =>
        new(new OutlineStyle(1, OutlinePosition.Outside, OutlineCorners.Round, 0, ShadeFalloff.Smooth, 2, OrderedDither.Bayer4),
            target, Black, BlendMode.Normal, 1, 0);

    private static void Outline(DocumentChangeTracker tracker, Guid layer, OutlineParameters parameters, int x = 5, int y = 5)
    {
        Run(tracker,
            new OutlineShape_Action(layer, new VecI(x, y), parameters, false, 0),
            new EndOutlineShape_Action(),
            new ChangeBoundary_Action());
    }

    [Fact]
    public void AGradientPaintsBothEndsAndUndoes()
    {
        var (tracker, layer) = CreateBlock();

        Gradient(tracker, layer, HardGradient());

        Assert.Equal(Black, PixelAt(tracker, layer, 1, 8));
        Assert.Equal(White, PixelAt(tracker, layer, 14, 8));

        Run(tracker, new Undo_Action());
        Assert.Equal(0, PixelAt(tracker, layer, 1, 8).A);
        Assert.Equal(Orange, PixelAt(tracker, layer, 6, 8));

        Run(tracker, new Redo_Action());
        Assert.Equal(Black, PixelAt(tracker, layer, 1, 8));
    }

    [Fact]
    public void AGradientStaysInsideTheSelection()
    {
        var (tracker, layer) = CreateBlock();
        Select(tracker, new RectD(0, 0, 3, 16));

        Gradient(tracker, layer, HardGradient());

        Assert.Equal(Black, PixelAt(tracker, layer, 1, 8));
        Assert.Equal(0, PixelAt(tracker, layer, 14, 8).A);
    }

    [Fact]
    public void FilledPixelsKeepsTheSilhouette()
    {
        var (tracker, layer) = CreateBlock();

        Gradient(tracker, layer, HardGradient(GradientArea.FilledPixels));

        Assert.Equal(Black, PixelAt(tracker, layer, 5, 8));
        Assert.Equal(0, PixelAt(tracker, layer, 1, 8).A);
    }

    [Fact]
    public void AdjustingALiveGradientReplacesIt()
    {
        var (tracker, layer) = CreateBlock();
        GradientParameters first = HardGradient();
        GradientParameters flipped = first with { Style = first.Style with { Reverse = true } };

        Run(tracker,
            new GradientFill_Action(layer, first, false, 0),
            new GradientFill_Action(layer, flipped, false, 0),
            new EndGradientFill_Action(),
            new ChangeBoundary_Action());

        Assert.Equal(White, PixelAt(tracker, layer, 1, 8));
        Assert.Equal(Black, PixelAt(tracker, layer, 14, 8));
    }

    [Fact]
    public void OutliningAShapeDrawsAroundItAndUndoes()
    {
        var (tracker, layer) = CreateBlock();

        Outline(tracker, layer, BlackOutline());

        Assert.Equal(Black, PixelAt(tracker, layer, 3, 8));
        Assert.Equal(Black, PixelAt(tracker, layer, 12, 8));
        Assert.Equal(Orange, PixelAt(tracker, layer, 4, 8));
        Assert.Equal(0, PixelAt(tracker, layer, 2, 8).A);

        Run(tracker, new Undo_Action());
        Assert.Equal(0, PixelAt(tracker, layer, 3, 8).A);
    }

    [Fact]
    public void ClickingEmptySpaceOutlinesNothing()
    {
        var (tracker, layer) = CreateBlock();

        Outline(tracker, layer, BlackOutline(), 0, 0);

        Assert.Equal(0, PixelAt(tracker, layer, 3, 8).A);
    }

    [Fact]
    public void WholeLayerNeedsNoClickOnTheShape()
    {
        var (tracker, layer) = CreateBlock();

        Outline(tracker, layer, BlackOutline(OutlineTarget.WholeLayer), 0, 0);

        Assert.Equal(Black, PixelAt(tracker, layer, 3, 8));
    }

    [Fact]
    public void KeepEdgesWorksThroughTheLayer()
    {
        var (tracker, layer) = CreateBlock();
        Outline(tracker, layer, BlackOutline());

        Run(tracker,
            new Liquify_Action(layer, new VecD(12.5, 8.5), LiquifyMode.Push, 6, 1, false, 2, false, 0),
            new Liquify_Action(layer, new VecD(14.5, 8.5), LiquifyMode.Push, 6, 1, false, 2, false, 0),
            new EndLiquify_Action(),
            new ChangeBoundary_Action());

        Assert.Equal(Black, PixelAt(tracker, layer, 14, 8));
        Assert.NotEqual(Black, PixelAt(tracker, layer, 13, 8));
        Assert.NotEqual(Black, PixelAt(tracker, layer, 12, 8));
    }
}
