using BlendMode = PixiEditor.ChangeableDocument.Enums.BlendMode;
using ChunkyImageLib.DataHolders;
using Drawie.Backend.Core.Surfaces;
using ChunkyImageLib;
using Drawie.Backend.Core;
using Drawie.Backend.Core.ColorsImpl;
using Drawie.Backend.Core.Numerics;
using Drawie.Backend.Core.Surfaces.ImageData;
using Drawie.Backend.Core.Surfaces.PaintImpl;
using Drawie.Backend.Core.Vector;
using Drawie.Numerics;
using PixiEditor.ChangeableDocument;
using PixiEditor.ChangeableDocument.Actions;
using PixiEditor.ChangeableDocument.Actions.Generated;
using PixiEditor.ChangeableDocument.Actions.Undo;
using PixiEditor.ChangeableDocument.Changeables.Animations;
using PixiEditor.ChangeableDocument.Changeables.Graph;
using PixiEditor.ChangeableDocument.Changeables.Graph.Interfaces;
using PixiEditor.ChangeableDocument.Changeables.Graph.Nodes;
using PixiEditor.ChangeableDocument.Changeables.Graph.Nodes.Effects;
using PixiEditor.ChangeableDocument.Changes.Drawing;
using PixiEditor.ChangeableDocument.Enums;
using PixiEditor.ChangeableDocument.Helpers;
using PixiEditor.ChangeableDocument.Rendering;

namespace PixiEditor.Tests;

/// <summary>
/// Runs the Shade and Liquify changes through the real document pipeline, on real layers, including
/// undo and redo. The array tests cover the maths; these cover the part they cannot - that pixels
/// are read from and written back to a layer correctly.
/// </summary>
public class ShadeAndLiquifyPipelineTests : PixiEditorTest
{
    private const int CanvasSize = 16;

    private static readonly Color Orange = new(255, 128, 0);

    private static readonly EdgeShadeShape RightSide = new(0, 0, 2, ShadeFalloff.Hard, 1);

    private static void Run(DocumentChangeTracker tracker, params IAction[] actions)
    {
        tracker.ProcessAll(actions.Select(action => (ActionSource.User, action)).ToList());
    }

    /// <summary>A canvas with an orange 8x8 block at (4,4) on an otherwise transparent layer.</summary>
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

    private static Color PixelAt(DocumentChangeTracker tracker, Guid layer, int x, int y)
    {
        var node = (IReadOnlyImageNode)tracker.Document.FindMemberOrThrow(layer);
        return node.GetLayerImageAtFrame(0).GetCommittedPixel(new VecI(x, y));
    }

    private static ShadeParameters BlackMultiply(EdgeShadeShape shape, double strength = 1) =>
        new(shape, strength, BlendMode.Multiply, new Color(0, 0, 0), 0);

    private static void Shade(DocumentChangeTracker tracker, Guid layer, ShadeParameters parameters, int x = 5, int y = 5)
    {
        Run(tracker,
            new ShadeRegion_Action(layer, new VecI(x, y), parameters, false, 0),
            new EndShadeRegion_Action(),
            new ChangeBoundary_Action());
    }

    [Fact]
    public void ShadingASelectionDarkensOnlyTheChosenSide()
    {
        var (tracker, layer) = CreateBlock();

        Shade(tracker, layer, BlackMultiply(RightSide));

        Assert.Equal(new Color(0, 0, 0), PixelAt(tracker, layer, 11, 8));
        Assert.Equal(new Color(0, 0, 0), PixelAt(tracker, layer, 10, 8));
        Assert.Equal(Orange, PixelAt(tracker, layer, 9, 8));
        Assert.Equal(Orange, PixelAt(tracker, layer, 4, 8));
    }

    [Fact]
    public void ShadingWithoutASelectionUsesTheColourAreaUnderThePointer()
    {
        var (tracker, layer) = CreateBlock();
        Run(tracker, new ClearSelection_Action(), new ChangeBoundary_Action());

        Shade(tracker, layer, BlackMultiply(RightSide), 6, 6);

        Assert.Equal(new Color(0, 0, 0), PixelAt(tracker, layer, 11, 8));
        Assert.Equal(Orange, PixelAt(tracker, layer, 9, 8));
    }

    [Fact]
    public void ClickingEmptySpaceWithoutASelectionChangesNothing()
    {
        var (tracker, layer) = CreateBlock();
        Run(tracker, new ClearSelection_Action(), new ChangeBoundary_Action());

        Shade(tracker, layer, BlackMultiply(RightSide), 0, 0);

        Assert.Equal(Orange, PixelAt(tracker, layer, 11, 8));
    }

    [Fact]
    public void StrengthScalesTheShade()
    {
        var (tracker, layer) = CreateBlock();

        Shade(tracker, layer, BlackMultiply(RightSide, 0.5));

        Color shaded = PixelAt(tracker, layer, 11, 8);
        Assert.InRange(shaded.R, 120, 135);
        Assert.Equal(255, shaded.A);
    }

    [Fact]
    public void AdjustingALiveShadeReplacesItRatherThanBuildingUp()
    {
        var (tracker, layer) = CreateBlock();

        Run(tracker,
            new ShadeRegion_Action(layer, new VecI(5, 5), BlackMultiply(RightSide), false, 0),
            new ShadeRegion_Action(layer, new VecI(5, 5), BlackMultiply(RightSide with { Angle = 180 }), false, 0),
            new EndShadeRegion_Action(),
            new ChangeBoundary_Action());

        Assert.Equal(Orange, PixelAt(tracker, layer, 11, 8));
        Assert.Equal(new Color(0, 0, 0), PixelAt(tracker, layer, 4, 8));
    }

    [Fact]
    public void ShadingUndoesAndRedoes()
    {
        var (tracker, layer) = CreateBlock();
        Shade(tracker, layer, BlackMultiply(RightSide));

        Run(tracker, new Undo_Action());
        Assert.Equal(Orange, PixelAt(tracker, layer, 11, 8));

        Run(tracker, new Redo_Action());
        Assert.Equal(new Color(0, 0, 0), PixelAt(tracker, layer, 11, 8));
    }

    [Fact]
    public void PushingMovesPixelsAcrossTheLayerAndUndoRestoresThem()
    {
        var (tracker, layer) = CreateBlock();
        Run(tracker, new ClearSelection_Action(), new ChangeBoundary_Action());
        Assert.Equal(0, PixelAt(tracker, layer, 13, 8).A);

        Liquify(tracker, layer, new VecD(9.5, 8.5), new VecD(13.5, 8.5));

        Assert.Equal(255, PixelAt(tracker, layer, 13, 8).A);
        Assert.Equal(Orange.R, PixelAt(tracker, layer, 13, 8).R);

        Run(tracker, new Undo_Action());
        Assert.Equal(0, PixelAt(tracker, layer, 13, 8).A);

        Run(tracker, new Redo_Action());
        Assert.Equal(255, PixelAt(tracker, layer, 13, 8).A);
    }

    [Fact]
    public void LiquifyStaysInsideTheSelection()
    {
        var (tracker, layer) = CreateBlock();

        // The selection from CreateBlock is still active, and (13,8) is outside it.
        Liquify(tracker, layer, new VecD(9.5, 8.5), new VecD(13.5, 8.5));

        Assert.Equal(0, PixelAt(tracker, layer, 13, 8).A);
    }

    private static void Liquify(DocumentChangeTracker tracker, Guid layer, VecD from, VecD to)
    {
        Run(tracker,
            new Liquify_Action(layer, from, LiquifyMode.Push, 8, 1, false, 0, false, 0),
            new Liquify_Action(layer, to, LiquifyMode.Push, 8, 1, false, 0, false, 0),
            new EndLiquify_Action(),
            new ChangeBoundary_Action());
    }

    [Fact]
    public void TheNodeShadesAgainstTheEdgeAndLeavesTheRestAlone()
    {
        using var context = Drawie.Backend.Core.Bridge.DrawingBackendApi.Current.RenderingDispatcher.EnsureContext();

        ShadeEdgesNode node = new();
        node.Background.NonOverridenValue = new Painter((_, canvas) =>
        {
            using Paint fill = new() { Color = Orange, Style = PaintStyle.Fill };
            canvas.DrawRect(new RectD(4, 4, 8, 8), fill);
        });
        node.Angle.NonOverridenValue = 0d;
        node.Spread.NonOverridenValue = 0d;
        node.Thickness.NonOverridenValue = 2;
        node.Strength.NonOverridenValue = 1d;

        VecI size = new(CanvasSize, CanvasSize);
        using Texture output = Texture.ForProcessing(size, ColorSpace.CreateSrgb());
        RenderContext renderContext = new(output.DrawingSurface.Canvas, new KeyFrameTime(0, 0), ChunkResolution.Full,
            size, size, ColorSpace.CreateSrgb(), SamplingOptions.Default, new NodeGraph());

        node.RenderPreview(output.DrawingSurface, renderContext, string.Empty);

        Color shaded = output.GetSrgbPixel(new VecI(11, 8));
        Color untouched = output.GetSrgbPixel(new VecI(9, 8));

        Assert.True(shaded.R < 5, $"expected the right edge to be shaded, got {shaded}");
        Assert.True(untouched.R > 250, $"expected the middle to be untouched, got {untouched}");
    }
}
