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
using PixiEditor.ChangeableDocument.Changes.Drawing.FloodFill;
using PixiEditor.ChangeableDocument.Enums;

namespace PixiEditor.Tests;

/// <summary>
/// Covers the fill staying open after the click, so its colour and tolerance can be changed with
/// the result on screen, and still leaving one undo step behind.
/// </summary>
public class FillAreaPipelineTests : PixiEditorTest
{
    private const int CanvasSize = 16;

    private static readonly Color Dark = new(100, 100, 100);
    private static readonly Color NearlyDark = new(110, 110, 110);
    private static readonly Color Red = new(255, 0, 0);
    private static readonly Color Blue = new(0, 0, 255);

    private static void Run(DocumentChangeTracker tracker, params IAction[] actions)
    {
        tracker.ProcessAll(actions.Select(action => (ActionSource.User, action)).ToList());
    }

    /// <summary>
    /// Two blocks side by side in colours ten apart, so tolerance decides whether a fill started in
    /// one of them crosses into the other.
    /// </summary>
    private static (DocumentChangeTracker Tracker, Guid Layer) CreateTwoBlocks()
    {
        DocumentChangeTracker tracker = new();
        Guid layer = Guid.NewGuid();
        Guid output = Guid.NewGuid();

        Run(tracker,
            new CreateNode_Action(typeof(OutputNode), output, Guid.Empty),
            new ResizeCanvas_Action(new VecI(CanvasSize, CanvasSize), ResizeAnchor.TopLeft),
            new CreateStructureMember_Action(output, layer, typeof(ImageLayerNode)),
            new ChangeBoundary_Action());

        Paint(tracker, layer, new RectD(2, 2, 6, 12), Dark, new VecI(3, 3));
        Paint(tracker, layer, new RectD(8, 2, 6, 12), NearlyDark, new VecI(9, 3));
        Run(tracker, new ClearSelection_Action(), new ChangeBoundary_Action());

        return (tracker, layer);
    }

    private static void Paint(DocumentChangeTracker tracker, Guid layer, RectD area, Color color, VecI seed)
    {
        using VectorPath path = new();
        path.AddRect(area);
        Run(tracker,
            new SetSelection_Action(path),
            new FloodFill_Action(layer, seed, color, false, 0f, FloodFillMode.Overlay, false, false, 0),
            new ChangeBoundary_Action());
    }

    private static Color PixelAt(DocumentChangeTracker tracker, Guid layer, int x, int y)
    {
        var node = (IReadOnlyImageNode)tracker.Document.FindMemberOrThrow(layer);
        return node.GetLayerImageAtFrame(0).GetCommittedPixel(new VecI(x, y));
    }

    /// <summary>What is on screen, which while the fill is still live is not yet what is committed.</summary>
    private static Color PreviewAt(DocumentChangeTracker tracker, Guid layer, int x, int y)
    {
        var node = (IReadOnlyImageNode)tracker.Document.FindMemberOrThrow(layer);
        return node.GetLayerImageAtFrame(0).GetMostUpToDatePixel(new VecI(x, y));
    }

    private static FillAreaParameters Fill(Color color, float tolerance = 0f, bool contiguous = true) =>
        new(color, false, tolerance, FloodFillMode.Overlay, contiguous);

    [Fact]
    public void RaisingTheToleranceWidensTheFillThatIsAlreadyOnScreen()
    {
        var (tracker, layer) = CreateTwoBlocks();

        Run(tracker, new FillArea_Action(layer, new VecI(4, 4), Fill(Red), false, 0));
        Assert.Equal(Red, PreviewAt(tracker, layer, 4, 4));
        Assert.Equal(NearlyDark, PreviewAt(tracker, layer, 10, 4));

        // Same fill, wider tolerance: it now reaches into the neighbouring block.
        Run(tracker, new FillArea_Action(layer, new VecI(4, 4), Fill(Red, 0.1f), false, 0));
        Assert.Equal(Red, PreviewAt(tracker, layer, 10, 4));

        Run(tracker, new EndFillArea_Action(), new ChangeBoundary_Action());
        Assert.Equal(Red, PixelAt(tracker, layer, 10, 4));
    }

    [Fact]
    public void LoweringTheToleranceNarrowsItAgainRatherThanLeavingTheWiderFill()
    {
        var (tracker, layer) = CreateTwoBlocks();

        Run(tracker,
            new FillArea_Action(layer, new VecI(4, 4), Fill(Red, 0.1f), false, 0),
            new FillArea_Action(layer, new VecI(4, 4), Fill(Red), false, 0),
            new EndFillArea_Action(),
            new ChangeBoundary_Action());

        Assert.Equal(Red, PixelAt(tracker, layer, 4, 4));
        Assert.Equal(NearlyDark, PixelAt(tracker, layer, 10, 4));
    }

    [Fact]
    public void ChangingTheColourRepaintsTheSameArea()
    {
        var (tracker, layer) = CreateTwoBlocks();

        Run(tracker,
            new FillArea_Action(layer, new VecI(4, 4), Fill(Red), false, 0),
            new FillArea_Action(layer, new VecI(4, 4), Fill(Blue), false, 0),
            new EndFillArea_Action(),
            new ChangeBoundary_Action());

        Assert.Equal(Blue, PixelAt(tracker, layer, 4, 4));
        Assert.Equal(NearlyDark, PixelAt(tracker, layer, 10, 4));
    }

    [Fact]
    public void DraggingOverAnotherAreaFillsThatToo()
    {
        var (tracker, layer) = CreateTwoBlocks();

        Run(tracker,
            new FillArea_Action(layer, new VecI(4, 4), Fill(Red), false, 0),
            new FillArea_Action(layer, new VecI(10, 4), Fill(Red), false, 0),
            new EndFillArea_Action(),
            new ChangeBoundary_Action());

        Assert.Equal(Red, PixelAt(tracker, layer, 4, 4));
        Assert.Equal(Red, PixelAt(tracker, layer, 10, 4));
    }

    [Fact]
    public void TheWholeFillIsOneUndoStepHoweverMuchItWasRetuned()
    {
        var (tracker, layer) = CreateTwoBlocks();

        Run(tracker,
            new FillArea_Action(layer, new VecI(4, 4), Fill(Red), false, 0),
            new FillArea_Action(layer, new VecI(4, 4), Fill(Red, 0.05f), false, 0),
            new FillArea_Action(layer, new VecI(4, 4), Fill(Blue, 0.1f), false, 0),
            new EndFillArea_Action(),
            new ChangeBoundary_Action());

        Run(tracker, new Undo_Action());

        Assert.Equal(Dark, PixelAt(tracker, layer, 4, 4));
        Assert.Equal(NearlyDark, PixelAt(tracker, layer, 10, 4));
    }

    [Fact]
    public void UndoingWhileItIsStillLiveThrowsItAway()
    {
        var (tracker, layer) = CreateTwoBlocks();

        Run(tracker, new FillArea_Action(layer, new VecI(4, 4), Fill(Red), false, 0));
        Assert.Equal(Red, PreviewAt(tracker, layer, 4, 4));

        // The boundary has to close the packet before the undo: the tracker will not undo into an
        // open one, which is why the executor sends these as two batches.
        Run(tracker, new EndFillArea_Action(), new ChangeBoundary_Action());
        Run(tracker, new Undo_Action());

        Assert.Equal(Dark, PixelAt(tracker, layer, 4, 4));
    }

    [Fact]
    public void ANonContiguousFillTakesBothBlocksWithEnoughTolerance()
    {
        var (tracker, layer) = CreateTwoBlocks();

        Run(tracker,
            new FillArea_Action(layer, new VecI(4, 4), Fill(Red, 0.1f, contiguous: false), false, 0),
            new EndFillArea_Action(),
            new ChangeBoundary_Action());

        Assert.Equal(Red, PixelAt(tracker, layer, 4, 4));
        Assert.Equal(Red, PixelAt(tracker, layer, 10, 4));
    }
}
