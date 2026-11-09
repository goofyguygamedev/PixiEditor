using ChunkyImageLib.Operations;
using Drawie.Backend.Core;
using Drawie.Backend.Core.Surfaces;
using Drawie.Backend.Core.Surfaces.PaintImpl;
using Drawie.Numerics;
using PixiEditor.ChangeableDocument.Changeables.Graph.Nodes;
using PixiEditor.ChangeableDocument.Changes.Drawing;

namespace PixiEditor.ChangeableDocument.Changes.Root;

/// <summary>
/// Slides every raster layer across the canvas, wrapping what leaves one side around to the other.
/// </summary>
/// <remarks>
/// The standard way of making a tile seamless: offset by half the canvas and the seams end up in the
/// middle, where they can be painted out. Offsetting by half again puts everything back.
/// </remarks>
internal class OffsetCanvasWrapped_Change : Change
{
    private readonly VecI offset;
    private readonly int frame;

    private Dictionary<(Guid Member, bool IsMask), CommittedChunkStorage>? savedChunks;

    [GenerateMakeChangeAction]
    public OffsetCanvasWrapped_Change(VecI offset, int frame)
    {
        this.offset = offset;
        this.frame = frame;
    }

    public override bool InitializeAndValidate(Document target) => target.Size.X > 0 && target.Size.Y > 0;

    public override OneOf<None, IChangeInfo, List<IChangeInfo>> Apply(Document target, bool firstApply, out bool ignoreInUndo)
    {
        if (savedChunks is not null)
            throw new InvalidOperationException();

        savedChunks = new();
        List<IChangeInfo> infos = new();

        target.ForEveryMember(member =>
        {
            if (member is ImageLayerNode layer && layer.GetLayerImageAtFrame(frame) is { } image)
            {
                OffsetImage(target.Size, image, member.Id, false, infos);
            }

            if (member.EmbeddedMask is { } mask)
            {
                OffsetImage(target.Size, mask, member.Id, true, infos);
            }
        });

        ignoreInUndo = savedChunks.Count == 0;
        return infos;
    }

    public override OneOf<None, IChangeInfo, List<IChangeInfo>> Revert(Document target)
    {
        if (savedChunks is null)
            throw new InvalidOperationException();

        List<IChangeInfo> infos = new();

        foreach (((Guid member, bool isMask), CommittedChunkStorage storage) in savedChunks)
        {
            CommittedChunkStorage? toRestore = storage;
            AffectedArea area = DrawingChangeHelper.ApplyStoredChunksDisposeAndSetToNull(target, member, isMask, frame, ref toRestore);
            infos.Add(DrawingChangeHelper.CreateAreaChangeInfo(member, area, isMask).AsT1);
        }

        savedChunks = null;
        return infos;
    }

    public override void Dispose()
    {
        if (savedChunks is null)
            return;

        foreach (CommittedChunkStorage storage in savedChunks.Values)
        {
            storage.Dispose();
        }
    }

    private void OffsetImage(VecI size, ChunkyImage image, Guid member, bool isMask, List<IChangeInfo> infos)
    {
        using Surface original = new(size);
        image.DrawCommittedRegionOn(new RectD(0, 0, size.X, size.Y), ChunkResolution.Full,
            original.DrawingSurface.Canvas, VecI.Zero);

        int shiftX = ((offset.X % size.X) + size.X) % size.X;
        int shiftY = ((offset.Y % size.Y) + size.Y) % size.Y;

        using Surface wrapped = new(size);
        using Paint replace = new() { BlendMode = BlendMode.Src };
        Canvas canvas = wrapped.DrawingSurface.Canvas;
        canvas.DrawSurface(original.DrawingSurface, shiftX, shiftY, replace);
        canvas.DrawSurface(original.DrawingSurface, shiftX - size.X, shiftY, replace);
        canvas.DrawSurface(original.DrawingSurface, shiftX, shiftY - size.Y, replace);
        canvas.DrawSurface(original.DrawingSurface, shiftX - size.X, shiftY - size.Y, replace);

        image.EnqueueDrawImage(VecI.Zero, wrapped, replace);

        AffectedArea area = image.FindAffectedArea();
        savedChunks![(member, isMask)] = new CommittedChunkStorage(image, area.Chunks);
        image.CommitChanges();
        infos.Add(DrawingChangeHelper.CreateAreaChangeInfo(member, area, isMask).AsT1);
    }
}
