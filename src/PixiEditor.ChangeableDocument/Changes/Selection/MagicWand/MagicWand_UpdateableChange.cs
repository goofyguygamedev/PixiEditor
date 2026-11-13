using PixiEditor.ChangeableDocument.Enums;
using Drawie.Backend.Core.Vector;
using Drawie.Numerics;

namespace PixiEditor.ChangeableDocument.Changes.Selection.MagicWand;

internal class MagicWand_UpdateableChange : UpdateableChange
{
    private VectorPath? originalPath;
    private VectorPath path = new() { FillType = PathFillType.EvenOdd };

    /// <summary>
    /// Every point this selection was taken from, in order. A drag adds one per pixel it crosses,
    /// and keeping them means a change of tolerance can rebuild the whole selection from scratch
    /// instead of folding a fresh guess into the one already on screen.
    /// </summary>
    private readonly List<VecI> points = new();
    private readonly List<Guid> memberGuids;
    private readonly SelectionMode mode;
    private int frame;
    private double tolerance;
    private bool contiguous;
    private string renderOutput;

    [GenerateUpdateableChangeActions]
    public MagicWand_UpdateableChange(List<Guid> memberGuids, VecI point, SelectionMode mode, double tolerance, int frame, bool contiguous, string renderOutput)
    {
        path.MoveTo(point);
        this.mode = mode;
        this.memberGuids = memberGuids;
        points.Add(point);
        this.frame = frame;
        this.tolerance = tolerance;
        this.contiguous = contiguous;
        this.renderOutput = renderOutput;
    }

    public override bool InitializeAndValidate(Document target)
    {
        originalPath = new VectorPath(target.Selection.SelectionPath);
        return true;
    }

    /// <summary>
    /// Takes the tolerance and the contiguous flag as well as the point, which is what makes those
    /// two settings re-select while they are being changed. Everything else - the mode above all -
    /// is read once at the click: the modifier held then is the one the artist meant.
    /// </summary>
    [UpdateChangeMethod]
    public void Update(VecI point, double tolerance, bool contiguous)
    {
        this.tolerance = tolerance;
        this.contiguous = contiguous;

        // A settings change re-sends the last point, and a drag sends one for every pixel it
        // crosses, so only a point that moved is worth keeping.
        if (points[^1] != point)
        {
            points.Add(point);
        }
    }

    public override OneOf<None, IChangeInfo, List<IChangeInfo>> Apply(Document target, bool firstApply, out bool ignoreInUndo)
    {
        ignoreInUndo = false;
        return CommonApply(target);
    }

    public override OneOf<None, IChangeInfo, List<IChangeInfo>> ApplyTemporarily(Document target)
    {
        return CommonApply(target);
    }

    private Selection_ChangeInfo CommonApply(Document target)
    {
        HashSet<Guid> membersToReference = null;

        if (memberGuids != null)
        {
            membersToReference = new HashSet<Guid>();
            target.ForEveryReadonlyMember(member =>
            {
                if (memberGuids.Contains(member.Id))
                    membersToReference.Add(member.Id);
            });
        }

        VectorPath previousRegion = path;

        if (mode == SelectionMode.New)
        {
            // Replacing, so only the last point counts - which is what dragging always did, since
            // every point replaced the one before it.
            path = MagicWandHelper.DoMagicWandFloodFill(points[^1], membersToReference, tolerance,
                renderOutput, target, frame, contiguous);

            var copy = new VectorPath(path);
            copy.Close();

            var toDisposeNew = target.Selection.SelectionPath;
            target.Selection.SelectionPath = copy;
            toDisposeNew.Dispose();
        }
        else
        {
            // Adding or subtracting builds back up from the selection as it was before this change
            // started, never from the preview already on screen. Folding into the preview would
            // make each new tolerance pile onto the last one instead of replacing it.
            VectorPath combined = new VectorPath(originalPath!);

            foreach (VecI pointToSelect in points)
            {
                using VectorPath region = MagicWandHelper.DoMagicWandFloodFill(pointToSelect,
                    membersToReference, tolerance, renderOutput, target, frame, contiguous);

                VectorPath merged = combined.Op(region, mode.ToVectorPathOp());
                combined.Dispose();
                combined = merged;
            }

            path = MagicWandHelper.DoMagicWandFloodFill(points[^1], membersToReference, tolerance,
                renderOutput, target, frame, contiguous);

            var toDisposeOp = target.Selection.SelectionPath;
            target.Selection.SelectionPath = combined;
            toDisposeOp.Dispose();
        }

        previousRegion.Dispose();

        return new Selection_ChangeInfo(new VectorPath(target.Selection.SelectionPath));
    }

    public override OneOf<None, IChangeInfo, List<IChangeInfo>> Revert(Document target)
    {
        var toDispose = target.Selection.SelectionPath;
        target.Selection.SelectionPath = new VectorPath(originalPath!);
        toDispose.Dispose();
        return new Selection_ChangeInfo(new VectorPath(target.Selection.SelectionPath));
    }

    public override void Dispose()
    {
        path.Dispose();
        originalPath?.Dispose();
    }
}
