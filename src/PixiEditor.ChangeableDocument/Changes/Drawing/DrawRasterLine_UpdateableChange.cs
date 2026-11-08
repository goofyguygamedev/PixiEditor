using Drawie.Backend.Core.ColorsImpl.Paintables;
using Drawie.Backend.Core.Surfaces;
using Drawie.Backend.Core.Surfaces.PaintImpl;
using Drawie.Backend.Core.Vector;
using Drawie.Numerics;

namespace PixiEditor.ChangeableDocument.Changes.Drawing;

internal class DrawRasterLine_UpdateableChange : InterruptableUpdateableChange
{
    private readonly Guid memberGuid;
    private VecD from;
    private VecD to;
    private float strokeWidth;
    private Paintable paintable;
    private StrokeCap caps;
    private readonly bool drawOnMask;
    private CommittedChunkStorage? savedChunks;
    private int frame;
    private bool antiAliasing;
    private Paint paint;

    /// <summary>
    /// When the line has curve points, the already-built spline to stroke instead of the straight
    /// <see cref="from"/>-to-<see cref="to"/> segment. This change takes ownership of it and
    /// disposes it, so a caller must hand over a fresh path and not reuse it afterwards.
    /// </summary>
    private VectorPath? curve;

    [GenerateUpdateableChangeActions]
    public DrawRasterLine_UpdateableChange
    (Guid memberGuid, VecD from, VecD to, float strokeWidth, Paintable paintable, StrokeCap caps, bool antiAliasing,
        bool drawOnMask, int frame, VectorPath? curve)
    {
        this.memberGuid = memberGuid;
        this.from = from;
        this.to = to;
        this.strokeWidth = strokeWidth;
        this.paintable = paintable;
        this.caps = caps;
        this.drawOnMask = drawOnMask;
        this.frame = frame;
        this.antiAliasing = antiAliasing;
        this.curve = curve;

        paint = new Paint()
        {
            StrokeWidth = strokeWidth, StrokeCap = caps, IsAntiAliased = antiAliasing, BlendMode = BlendMode.SrcOver
        };
        paint.SetPaintable(paintable);
    }

    [UpdateChangeMethod]
    public void Update(VecD from, VecD to, float strokeWidth, Paintable paintable, StrokeCap caps, VectorPath? curve)
    {
        this.from = from;
        this.to = to;
        this.paintable = paintable;
        this.caps = caps;
        this.strokeWidth = strokeWidth;

        if (!ReferenceEquals(this.curve, curve))
        {
            this.curve?.Dispose();
            this.curve = curve;
        }

        paint.SetPaintable(paintable);
        paint.StrokeWidth = strokeWidth;
        paint.StrokeCap = caps;
    }

    public override bool InitializeAndValidate(Document target)
    {
        return DrawingChangeHelper.IsValidForDrawing(target, memberGuid, drawOnMask, frame);
    }

    private AffectedArea CommonApply(Document target)
    {
        var image = DrawingChangeHelper.GetTargetImageOrThrow(target, memberGuid, drawOnMask, frame);
        var oldAffected = image.FindAffectedArea();
        image.CancelChanges();
        if (from != to || curve is not null)
        {
            DrawingChangeHelper.ApplyClipsSymmetriesEtc(target, image, memberGuid, drawOnMask);
            if (strokeWidth == 0)
            {
                image.CancelChanges();
            }
            else if (curve is not null)
            {
                // Bresenham has no curved equivalent, so a curved line always strokes the path -
                // at width 1 without anti-aliasing that still lands on whole pixels.
                image.EnqueueDrawPath(curve, paintable, strokeWidth, caps, BlendMode.SrcOver,
                    PaintStyle.Stroke, antiAliasing);
            }
            else if (Math.Abs(strokeWidth - 1) < 0.01f && !antiAliasing)
            {
                image.EnqueueDrawBresenhamLine((VecI)from, (VecI)to, paintable, BlendMode.SrcOver);
            }
            else
            {
                image.EnqueueDrawSkiaLine(from, to, paint);
            }
        }

        var totalAffected = image.FindAffectedArea();
        totalAffected.UnionWith(oldAffected);
        return totalAffected;
    }

    public override OneOf<None, IChangeInfo, List<IChangeInfo>> ApplyTemporarily(Document target)
    {
        return DrawingChangeHelper.CreateAreaChangeInfo(memberGuid, CommonApply(target), drawOnMask);
    }

    public override OneOf<None, IChangeInfo, List<IChangeInfo>> Apply(Document target, bool firstApply,
        out bool ignoreInUndo)
    {
        if (from == to && curve is null)
        {
            ignoreInUndo = true;
            return new None();
        }

        var image = DrawingChangeHelper.GetTargetImageOrThrow(target, memberGuid, drawOnMask, frame);
        var affected = CommonApply(target);
        if (savedChunks is not null)
            throw new InvalidOperationException("Trying to save chunks while there are saved chunks already");
        savedChunks = new CommittedChunkStorage(image, image.FindAffectedArea().Chunks);
        image.CommitChanges();

        ignoreInUndo = false;
        return DrawingChangeHelper.CreateAreaChangeInfo(memberGuid, affected, drawOnMask);
    }

    public override OneOf<None, IChangeInfo, List<IChangeInfo>> Revert(Document target)
    {
        var affected = DrawingChangeHelper.ApplyStoredChunksDisposeAndSetToNull
            (target, memberGuid, drawOnMask, frame, ref savedChunks);
        return DrawingChangeHelper.CreateAreaChangeInfo(memberGuid, affected, drawOnMask);
    }

    public override void Dispose()
    {
        savedChunks?.Dispose();
        paint?.Dispose();
        curve?.Dispose();
        curve = null;
    }
}
