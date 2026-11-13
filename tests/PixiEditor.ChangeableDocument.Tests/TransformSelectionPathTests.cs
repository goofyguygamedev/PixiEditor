using Drawie.Backend.Core.Numerics;
using Drawie.Backend.Core.Vector;
using Drawie.Numerics;
using OneOf;
using OneOf.Types;
using PixiEditor.ChangeableDocument.Changeables;
using PixiEditor.ChangeableDocument.ChangeInfos;
using PixiEditor.ChangeableDocument.ChangeInfos.Drawing;
using PixiEditor.ChangeableDocument.Changes.Selection;
using PixiEditor.Tests;

namespace PixiEditor.ChangeableDocument.Tests;

/// <summary>
/// Covers resizing a selection by its transform handles. The change existed but nothing drove it,
/// so this is the first exercise of it.
/// </summary>
public class TransformSelectionPathTests : PixiEditorTest
{
    [Fact]
    public void CornersBecomeTheSelectionsNewBounds()
    {
        using Document document = CreateDocument(new VecI(100, 100), new RectD(10, 10, 20, 20));
        using TransformSelectionPath_UpdateableChange change =
            new(new ShapeCorners(new RectD(10, 10, 40, 60)));

        Assert.True(change.InitializeAndValidate(document));

        var applied = change.Apply(document, true, out bool ignoreInUndo);

        Assert.False(ignoreInUndo);
        Assert.Equal(new RectD(10, 10, 40, 60), document.Selection.SelectionPath.TightBounds);
        DisposeSelectionInfos(applied);
    }

    [Fact]
    public void RevertingPutsTheOriginalSelectionBack()
    {
        RectD original = new(10, 10, 20, 20);
        using Document document = CreateDocument(new VecI(100, 100), original);
        using TransformSelectionPath_UpdateableChange change =
            new(new ShapeCorners(new RectD(0, 0, 100, 100)));

        Assert.True(change.InitializeAndValidate(document));
        DisposeSelectionInfos(change.Apply(document, true, out _));

        var reverted = change.Revert(document);

        Assert.Equal(original, document.Selection.SelectionPath.TightBounds);
        DisposeSelectionInfos(reverted);
    }

    [Fact]
    public void EachUpdateMapsFromTheOriginalRatherThanTheLastOne()
    {
        // What makes a drag follow the pointer instead of running away: every update has to scale
        // the selection the drag started from, not the one the previous update produced.
        using Document document = CreateDocument(new VecI(200, 200), new RectD(0, 0, 10, 10));
        using TransformSelectionPath_UpdateableChange change =
            new(new ShapeCorners(new RectD(0, 0, 20, 20)));

        Assert.True(change.InitializeAndValidate(document));
        DisposeSelectionInfos(change.ApplyTemporarily(document));

        change.Update(new ShapeCorners(new RectD(0, 0, 40, 40)));
        DisposeSelectionInfos(change.ApplyTemporarily(document));

        change.Update(new ShapeCorners(new RectD(0, 0, 15, 15)));
        var applied = change.Apply(document, false, out _);

        Assert.Equal(new RectD(0, 0, 15, 15), document.Selection.SelectionPath.TightBounds);
        DisposeSelectionInfos(applied);
    }

    [Fact]
    public void AnEmptySelectionHasNothingToResize()
    {
        using Document document = new() { Size = new VecI(100, 100) };
        using TransformSelectionPath_UpdateableChange change =
            new(new ShapeCorners(new RectD(0, 0, 10, 10)));

        Assert.False(change.InitializeAndValidate(document));
    }

    private static Document CreateDocument(VecI size, RectD selectionBounds)
    {
        Document document = new() { Size = size };
        VectorPath selection = new() { FillType = PathFillType.EvenOdd };
        selection.AddRect(selectionBounds);

        document.Selection.SelectionPath.Dispose();
        document.Selection.SelectionPath = selection;
        return document;
    }

    private static void DisposeSelectionInfos(OneOf<None, IChangeInfo, List<IChangeInfo>> infos)
    {
        infos.Switch(
            static (None _) => { },
            static (IChangeInfo info) => DisposeSelectionInfo(info),
            static (List<IChangeInfo> changes) => changes.ForEach(DisposeSelectionInfo));
    }

    private static void DisposeSelectionInfo(IChangeInfo info)
    {
        if (info is Selection_ChangeInfo selection)
            selection.NewPath.Dispose();
    }
}
