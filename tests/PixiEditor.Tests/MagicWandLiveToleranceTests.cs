using Avalonia.Headless.XUnit;
using Drawie.Backend.Core.ColorsImpl;
using Drawie.Backend.Core.Numerics;
using Drawie.Backend.Core.Vector;
using Drawie.Numerics;
using PixiEditor.ChangeableDocument;
using PixiEditor.ChangeableDocument.Actions;
using PixiEditor.ChangeableDocument.Actions.Generated;
using PixiEditor.ChangeableDocument.Actions.Undo;
using PixiEditor.ChangeableDocument.Changeables.Graph.Nodes;
using PixiEditor.ChangeableDocument.Enums;

namespace PixiEditor.Tests;

/// <summary>
/// Covers the magic wand re-selecting while its tolerance is being changed. The update used to
/// carry only the clicked point, so a new tolerance reached the change but never the selection -
/// the result looked frozen at whatever the first click produced.
/// </summary>
public class MagicWandLiveToleranceTests : FullPixiEditorTest
{
    private const int CanvasSize = 16;

    /// <summary>A canvas of one colour with a block of a very slightly different one at 4,4.</summary>
    private static readonly Color Surrounding = new(255, 128, 0);
    private static readonly Color NearlyTheSame = new(255, 138, 0);

    [AvaloniaFact]
    public void RaisingTheToleranceWidensTheSelectionWithoutAnotherClick()
    {
        (DocumentChangeTracker tracker, Guid layer) = CanvasWithANearlyIdenticalBlock();
        VecI insideTheBlock = new(5, 5);

        Select(tracker, layer, insideTheBlock, tolerance: 0f);
        Assert.Equal(new VecI(4, 4), (VecI)SelectionSize(tracker));

        // The same click, a wider tolerance. This is the update that used to be dropped: the
        // surrounding colour is close enough now, so the selection should take in the whole canvas.
        Select(tracker, layer, insideTheBlock, tolerance: 1f);
        Assert.Equal(new VecI(CanvasSize, CanvasSize), (VecI)SelectionSize(tracker));

        // And back down again, which is what dragging the slider does.
        Select(tracker, layer, insideTheBlock, tolerance: 0f);
        Assert.Equal(new VecI(4, 4), (VecI)SelectionSize(tracker));

        Run(tracker, new EndMagicWand_Action(), new ChangeBoundary_Action());
    }

    private static void Select(DocumentChangeTracker tracker, Guid layer, VecI at, float tolerance) =>
        Run(tracker, new MagicWand_Action([layer], at, SelectionMode.New, tolerance, 0, true, string.Empty));

    private static VecD SelectionSize(DocumentChangeTracker tracker) =>
        tracker.Document.Selection.SelectionPath.TightBounds.Size;

    private static (DocumentChangeTracker Tracker, Guid Layer) CanvasWithANearlyIdenticalBlock()
    {
        DocumentChangeTracker tracker = new();
        Guid output = Guid.NewGuid();
        Guid layer = Guid.NewGuid();

        Run(tracker,
            new CreateNode_Action(typeof(OutputNode), output, Guid.Empty),
            new ResizeCanvas_Action(new VecI(CanvasSize, CanvasSize), ResizeAnchor.TopLeft),
            new CreateStructureMember_Action(output, layer, typeof(ImageLayerNode)),
            new ChangeBoundary_Action());

        Run(tracker,
            new FloodFill_Action(layer, new VecI(0, 0), Surrounding, false, 0f, FloodFillMode.Overlay, false, true, 0),
            new ChangeBoundary_Action());

        using VectorPath block = new();
        block.AddRect(new RectD(4, 4, 4, 4));

        Run(tracker,
            new SetSelection_Action(block),
            new FloodFill_Action(layer, new VecI(5, 5), NearlyTheSame, false, 0f, FloodFillMode.Overlay, false, true, 0),
            new ChangeBoundary_Action());

        // The wand has to work against the pixels, not against a selection left lying around.
        Run(tracker, new SetSelection_Action(new VectorPath()), new ChangeBoundary_Action());

        return (tracker, layer);
    }

    private static void Run(DocumentChangeTracker tracker, params IAction[] actions) =>
        tracker.ProcessAll(actions.Select(action => (ActionSource.User, action)).ToList());
}
