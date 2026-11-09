using System.Runtime.InteropServices;
using ChunkyImageLib.Operations;
using Drawie.Backend.Core;
using Drawie.Backend.Core.Surfaces;
using Drawie.Backend.Core.Surfaces.ImageData;
using Drawie.Backend.Core.Surfaces.PaintImpl;
using Drawie.Backend.Core.Vector;
using Drawie.Numerics;
using PixiEditor.ChangeableDocument.Changeables.Interfaces;
using PixiEditor.ChangeableDocument.Helpers;

namespace PixiEditor.ChangeableDocument.Changes.Drawing;

/// <summary>One liquify stroke: pushes, twirls, pinches or expands the pixels under the brush.</summary>
/// <remarks>
/// Works inside the selection if there is one, and deliberately skips the symmetry axes: mirroring a
/// warp would mean a second field running the other way, not a mirrored copy of the result.
/// </remarks>
internal class Liquify_UpdateableChange : UpdateableChange
{
    private static readonly ImageInfo ReadbackInfo =
        new(0, 0, ColorType.Rgba8888, AlphaType.Premul, ColorSpace.CreateSrgb());

    private readonly Guid memberGuid;
    private readonly LiquifyMode mode;
    private readonly double radius;
    private readonly double strength;
    private readonly bool smooth;
    private readonly int keepEdgeWidth;
    private readonly bool drawOnMask;
    private readonly int frame;
    private readonly List<VecD> pendingPoints = new();

    private LiquifyField? field;
    private byte[]? sourcePixels;
    private VecD lastPoint;

    private Surface? patch;
    private RectI patchBounds;
    private VectorPath? clip;
    private bool lockTransparency;
    private CommittedChunkStorage? storedChunks;

    [GenerateUpdateableChangeActions]
    public Liquify_UpdateableChange(Guid memberGuid, VecD pos, LiquifyMode mode, double radius, double strength,
        bool smooth, int keepEdgeWidth, bool drawOnMask, int frame)
    {
        this.memberGuid = memberGuid;
        this.mode = mode;
        this.radius = radius;
        this.strength = strength;
        this.smooth = smooth;
        this.keepEdgeWidth = keepEdgeWidth;
        this.drawOnMask = drawOnMask;
        this.frame = frame;
        lastPoint = ToFieldSpace(pos);
    }

    [UpdateChangeMethod]
    public void Update(VecD pos)
    {
        pendingPoints.Add(ToFieldSpace(pos));
    }

    public override bool InitializeAndValidate(Document target)
    {
        if (!DrawingChangeHelper.IsValidForDrawing(target, memberGuid, drawOnMask, frame))
            return false;

        ChunkyImage image = DrawingChangeHelper.GetTargetImageOrThrow(target, memberGuid, drawOnMask, frame)!;

        using Surface snapshot = new(ReadbackInfo.WithSize(target.Size));
        image.DrawCommittedRegionOn(new RectD(0, 0, target.Size.X, target.Size.Y), ChunkResolution.Full,
            snapshot.DrawingSurface.Canvas, VecI.Zero);

        using (Pixmap pixmap = snapshot.PeekPixels())
        {
            sourcePixels = MemoryMarshal.AsBytes(pixmap.GetPixelSpan<uint>()).ToArray();
        }

        field = new LiquifyField(target.Size.X, target.Size.Y);
        if (keepEdgeWidth > 0)
        {
            field.KeepEdges(sourcePixels, keepEdgeWidth);
        }

        if (!target.Selection.SelectionPath.IsEmpty)
        {
            clip = new VectorPath(target.Selection.SelectionPath);
        }

        lockTransparency = !drawOnMask
                           && target.FindMemberOrThrow(memberGuid) is ITransparencyLockable { LockTransparency: true };
        return true;
    }

    public override OneOf<None, IChangeInfo, List<IChangeInfo>> ApplyTemporarily(Document target)
    {
        ChunkyImage image = DrawingChangeHelper.GetTargetImageOrThrow(target, memberGuid, drawOnMask, frame)!;
        AffectedArea previous = image.FindAffectedArea();

        image.CancelChanges();
        ProcessPendingPoints();
        RebuildPatch();
        EnqueuePatch(image);

        AffectedArea affected = image.FindAffectedArea();
        affected.UnionWith(previous);
        return DrawingChangeHelper.CreateAreaChangeInfo(memberGuid, affected, drawOnMask);
    }

    public override OneOf<None, IChangeInfo, List<IChangeInfo>> Apply(Document target, bool firstApply, out bool ignoreInUndo)
    {
        ChunkyImage image = DrawingChangeHelper.GetTargetImageOrThrow(target, memberGuid, drawOnMask, frame)!;

        image.CancelChanges();
        if (firstApply)
        {
            ProcessPendingPoints();
            RebuildPatch();

            // Only the finished patch is needed from here on, and a whole canvas of source pixels
            // and offsets is not worth keeping for every stroke in the undo history.
            field = null;
            sourcePixels = null;
        }

        if (patch is null)
        {
            ignoreInUndo = true;
            return new None();
        }

        EnqueuePatch(image);

        AffectedArea affected = image.FindAffectedArea();
        storedChunks = new CommittedChunkStorage(image, affected.Chunks);
        image.CommitChanges();

        ignoreInUndo = false;
        return DrawingChangeHelper.CreateAreaChangeInfo(memberGuid, affected, drawOnMask);
    }

    public override OneOf<None, IChangeInfo, List<IChangeInfo>> Revert(Document target)
    {
        AffectedArea affected =
            DrawingChangeHelper.ApplyStoredChunksDisposeAndSetToNull(target, memberGuid, drawOnMask, frame, ref storedChunks);
        return DrawingChangeHelper.CreateAreaChangeInfo(memberGuid, affected, drawOnMask);
    }

    public override void Dispose()
    {
        storedChunks?.Dispose();
        patch?.Dispose();
        clip?.Dispose();
    }

    /// <summary>Pixel (x, y) is centred on (x + 0.5, y + 0.5) on the canvas, and on (x, y) in the field.</summary>
    private static VecD ToFieldSpace(VecD canvasPosition) => canvasPosition - new VecD(0.5);

    private void ProcessPendingPoints()
    {
        if (field is null)
            return;

        foreach (VecD point in pendingPoints)
        {
            if (mode == LiquifyMode.Push)
            {
                field.Push(lastPoint, point, radius, strength);
            }
            else
            {
                field.Apply(mode, point, radius, strength);
            }

            lastPoint = point;
        }

        pendingPoints.Clear();
    }

    private void RebuildPatch()
    {
        if (field?.DirtyBounds is not { } bounds || sourcePixels is null)
            return;

        byte[] rendered = new byte[bounds.Width * bounds.Height * 4];
        field.Render(sourcePixels, rendered, bounds, smooth);

        patch?.Dispose();
        patchBounds = bounds;
        patch = new Surface(ReadbackInfo.WithSize(bounds.Size));
        patch.DrawBytes(bounds.Size, rendered, ColorType.Rgba8888, AlphaType.Premul);
    }

    private void EnqueuePatch(ChunkyImage image)
    {
        if (patch is null)
            return;

        if (clip is not null)
            image.SetClippingPath(clip);
        if (lockTransparency)
            image.EnableLockTransparency();

        // Replaces rather than draws over, or pixels warped away would stay behind underneath.
        using Paint replace = new() { BlendMode = BlendMode.Src };
        image.EnqueueDrawImage(patchBounds.Pos, patch, replace);
    }
}
