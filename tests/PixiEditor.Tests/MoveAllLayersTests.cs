using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Drawie.Backend.Core.ColorsImpl;
using Drawie.Backend.Core.Numerics;
using Drawie.Backend.Core.Surfaces.ImageData;
using Drawie.Backend.Core.Vector;
using Drawie.Numerics;
using PixiEditor.ChangeableDocument;
using PixiEditor.ChangeableDocument.Actions;
using PixiEditor.ChangeableDocument.Actions.Generated;using PixiEditor.ChangeableDocument.Actions.Undo;
using PixiEditor.ChangeableDocument.Changeables.Graph.Interfaces;
using PixiEditor.ChangeableDocument.Changeables.Graph.Nodes;
using PixiEditor.ChangeableDocument.Enums;
using PixiEditor.Models.DocumentModels.UpdateableChangeExecutors;
using PixiEditor.Models.Handlers;
using PixiEditor.ViewModels;
using PixiEditor.ViewModels.Document;

namespace PixiEditor.Tests;

/// <summary>
/// Covers Alt on the move tool: which layers a widened move is allowed to touch, and that moving
/// several layers in one go really does move all of them rather than only the first.
/// </summary>
public class MoveAllLayersTests : FullPixiEditorTest
{
    private const int CanvasSize = 16;
    private static readonly Color Orange = new(255, 128, 0);

    [AvaloniaFact]
    public void AWidenedMoveSkipsHiddenAndLockedLayers()
    {
        DocumentViewModel document = DocumentViewModel.Build(b => b.WithSize(CanvasSize, CanvasSize)
            .WithGraph(g => g.WithImageLayerNode("layer", new VecI(CanvasSize), ColorSpace.CreateSrgb(), out int id)
                .WithOutputNode(id, "Output")));

        IStructureMemberHandler layer = document.StructureHelper.GetAllMembers().OfType<ILayerHandler>().First();

        Assert.Single(TransformSelectedExecutor.MovableRasterLayers([layer]));

        layer.IsVisibleBindable = false;
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(TransformSelectedExecutor.MovableRasterLayers([layer]));

        layer.IsVisibleBindable = true;
        layer.IsLockedBindable = true;
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(TransformSelectedExecutor.MovableRasterLayers([layer]));
    }

    [AvaloniaFact]
    public void TheMoveToolKeepsAltForItself()
    {
        // Alt transiently switches to the colour picker for every tool that does not claim it, so
        // without this the Alt move is swapped out from under the drag before it can happen.
        IToolHandler move = ViewModelMain.Current!.ToolsSubViewModel.AllToolSets
            .SelectMany(set => set.Tools).First(tool => tool.ToolName == "Move");

        Assert.True(move.HandlesAltModifier);
    }

    [AvaloniaFact]
    public void MovingSeveralLayersAtOnceMovesEveryOneOfThem()
    {
        // What Alt asks the engine to do. The selection is a block in the top left of both layers;
        // the transform carries it four pixels right and down.
        (DocumentChangeTracker tracker, Guid first, Guid second) = TwoLayersWithABlock();

        RectD from = new(2, 2, 4, 4);
        RectD to = new(6, 6, 4, 4);
        Dictionary<Guid, ShapeCorners> members = new()
        {
            [first] = new ShapeCorners(from),
            [second] = new ShapeCorners(from),
        };

        Run(tracker,
            new TransformSelected_Action(new ShapeCorners(from), false, false, members, false, 0),
            new TransformSelected_Action(new ShapeCorners(to), false, false, members, false, 0),
            new EndTransformSelected_Action(),
            new ChangeBoundary_Action());

        foreach (Guid layer in new[] { first, second })
        {
            Assert.Equal(Orange, PixelAt(tracker, layer, 7, 7));
            Assert.Equal(0, PixelAt(tracker, layer, 3, 3).A);
        }
    }

    private static (DocumentChangeTracker Tracker, Guid First, Guid Second) TwoLayersWithABlock()
    {
        DocumentChangeTracker tracker = new();
        Guid output = Guid.NewGuid();
        Guid first = Guid.NewGuid();
        Guid second = Guid.NewGuid();

        Run(tracker,
            new CreateNode_Action(typeof(OutputNode), output, Guid.Empty),
            new ResizeCanvas_Action(new VecI(CanvasSize, CanvasSize), ResizeAnchor.TopLeft),
            new CreateStructureMember_Action(output, first, typeof(ImageLayerNode)),
            new CreateStructureMember_Action(output, second, typeof(ImageLayerNode)),
            new ChangeBoundary_Action());

        using VectorPath block = new();
        block.AddRect(new RectD(2, 2, 4, 4));

        Run(tracker, new SetSelection_Action(block), new ChangeBoundary_Action());

        foreach (Guid layer in new[] { first, second })
        {
            Run(tracker,
                new FloodFill_Action(layer, new VecI(3, 3), Orange, false, 0f, FloodFillMode.Overlay, false, true, 0),
                new ChangeBoundary_Action());
        }

        return (tracker, first, second);
    }

    private static void Run(DocumentChangeTracker tracker, params IAction[] actions) =>
        tracker.ProcessAll(actions.Select(action => (ActionSource.User, action)).ToList());

    private static Color PixelAt(DocumentChangeTracker tracker, Guid layer, int x, int y)
    {
        IReadOnlyImageNode node = (IReadOnlyImageNode)tracker.Document.FindMemberOrThrow(layer);
        return node.GetLayerImageAtFrame(0).GetCommittedPixel(new VecI(x, y));
    }
}
