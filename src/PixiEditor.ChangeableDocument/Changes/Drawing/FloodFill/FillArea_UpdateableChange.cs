using Drawie.Backend.Core.ColorsImpl;
using Drawie.Backend.Core.Surfaces.PaintImpl;
using Drawie.Backend.Core.Vector;
using Drawie.Numerics;
using PixiEditor.ChangeableDocument.Changeables.Graph.Nodes;
using PixiEditor.ChangeableDocument.Enums;
using BlendMode = Drawie.Backend.Core.Surfaces.BlendMode;

namespace PixiEditor.ChangeableDocument.Changes.Drawing.FloodFill;

/// <summary>Everything about a fill that the artist can still change after clicking.</summary>
public readonly record struct FillAreaParameters(
    Color Color,
    bool ReferenceAll,
    float Tolerance,
    FloodFillMode FillMode,
    bool Contiguous);

/// <summary>
/// Fills areas of a layer and stays open afterwards, so the colour, the tolerance and the rest can
/// be changed with the result on screen rather than by filling again and again.
/// </summary>
/// <remarks>
/// The clicked points are kept and every update fills all of them over again from the layer as it
/// was. That is what makes a tolerance change read as a correction of one fill rather than another
/// fill on top, and it costs little: a point whose area the previous points have already filled
/// finds the colour it wanted there and stops.
/// </remarks>
internal class FillArea_UpdateableChange : UpdateableChange
{
    private readonly Guid memberGuid;
    private readonly bool drawOnMask;
    private readonly int frame;
    private readonly List<VecI> points = new();

    private FillAreaParameters parameters;
    private CommittedChunkStorage? storedChunks;

    [GenerateUpdateableChangeActions]
    public FillArea_UpdateableChange(Guid memberGuid, VecI pos, FillAreaParameters parameters, bool drawOnMask,
        int frame)
    {
        this.memberGuid = memberGuid;
        this.drawOnMask = drawOnMask;
        this.frame = frame;
        this.parameters = parameters;
        points.Add(pos);
    }

    [UpdateChangeMethod]
    public void Update(VecI pos, FillAreaParameters parameters)
    {
        this.parameters = parameters;

        // Settings changes re-send the last point, and a drag sends a new one for every pixel it
        // crosses, so only a point that moved is worth keeping.
        if (points[^1] != pos)
        {
            points.Add(pos);
        }
    }

    public override bool InitializeAndValidate(Document target)
    {
        VecI first = points[0];
        if (first.X < 0 || first.Y < 0 || first.X >= target.Size.X || first.Y >= target.Size.Y)
            return false;

        return DrawingChangeHelper.IsValidForDrawing(target, memberGuid, drawOnMask, frame);
    }

    public override OneOf<None, IChangeInfo, List<IChangeInfo>> ApplyTemporarily(Document target)
    {
        ChunkyImage image = DrawingChangeHelper.GetTargetImageOrThrow(target, memberGuid, drawOnMask, frame);
        AffectedArea previous = image.FindAffectedArea();

        // Back to the committed layer first, so the fill is replaced rather than added to.
        image.CancelChanges();
        FillEveryPoint(target, image);

        AffectedArea affected = image.FindAffectedArea();
        affected.UnionWith(previous);
        return DrawingChangeHelper.CreateAreaChangeInfo(memberGuid, affected, drawOnMask);
    }

    public override OneOf<None, IChangeInfo, List<IChangeInfo>> Apply(Document target, bool firstApply,
        out bool ignoreInUndo)
    {
        ChunkyImage image = DrawingChangeHelper.GetTargetImageOrThrow(target, memberGuid, drawOnMask, frame);

        image.CancelChanges();
        FillEveryPoint(target, image);

        AffectedArea affected = image.FindAffectedArea();
        if (affected.Chunks.Count == 0)
        {
            image.CancelChanges();
            ignoreInUndo = true;
            return new None();
        }

        storedChunks = new CommittedChunkStorage(image, affected.Chunks);
        image.CommitChanges();

        ignoreInUndo = false;
        return DrawingChangeHelper.CreateAreaChangeInfo(memberGuid, affected, drawOnMask);
    }

    public override OneOf<None, IChangeInfo, List<IChangeInfo>> Revert(Document target)
    {
        AffectedArea affected = DrawingChangeHelper.ApplyStoredChunksDisposeAndSetToNull(target, memberGuid,
            drawOnMask, frame, ref storedChunks);
        return DrawingChangeHelper.CreateAreaChangeInfo(memberGuid, affected, drawOnMask);
    }

    public override void Dispose()
    {
        storedChunks?.Dispose();
    }

    private void FillEveryPoint(Document target, ChunkyImage image)
    {
        VectorPath? selection = target.Selection.SelectionPath.IsEmpty ? null : target.Selection.SelectionPath;

        HashSet<Guid> membersToReference = new();
        if (parameters.ReferenceAll)
        {
            target.ForEveryReadonlyMember(member => membersToReference.Add(member.Id));
        }
        else
        {
            membersToReference.Add(memberGuid);
        }

        bool lockTransparency = target.FindMember(memberGuid) is ImageLayerNode { LockTransparency: true };

        using Paint? paint = parameters.FillMode == FloodFillMode.Replace
            ? new Paint { BlendMode = BlendMode.Src }
            : null;

        foreach (VecI point in points)
        {
            var filled = FloodFillHelper.FloodFill(membersToReference, target, selection, point, parameters.Color,
                parameters.Tolerance, frame, lockTransparency, parameters.FillMode, parameters.Contiguous, null);

            // The image is told to copy what it is given. Handing it these chunks by reference
            // would be faster, but the draw happens later, off this thread, and a live fill rebuilds
            // on every change: the chunks would be freed under an operation still pointing at them,
            // which corrupts the heap rather than failing cleanly.
            foreach (var (chunkPos, chunk) in filled)
            {
                image.EnqueueDrawTexture(chunkPos * ChunkyImage.FullChunkSize, chunk.Surface, paint);
            }

            foreach (Chunk chunk in filled.Values)
            {
                chunk.Dispose();
            }
        }
    }
}
