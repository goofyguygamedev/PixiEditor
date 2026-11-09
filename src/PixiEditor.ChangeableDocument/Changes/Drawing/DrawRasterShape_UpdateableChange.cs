using Drawie.Backend.Core.ColorsImpl.Paintables;
using Drawie.Backend.Core.Numerics;
using Drawie.Backend.Core.Surfaces;
using Drawie.Backend.Core.Surfaces.PaintImpl;
using Drawie.Backend.Core.Vector;
using Drawie.Numerics;
using PixiEditor.ChangeableDocument.Helpers;

namespace PixiEditor.ChangeableDocument.Changes.Drawing;

/// <summary>Everything the Shape tool lets the artist choose.</summary>
/// <param name="Bounds">The box the shape is drawn into.</param>
/// <param name="Angle">Rotation about the centre of that box, in radians.</param>
/// <param name="Points">Sides for a polygon, or points for a star.</param>
/// <param name="InnerRatio">How far a star's inner points reach, from 0 to 1.</param>
/// <param name="CornerRadius">Rounds a rectangle's corners, in pixels.</param>
public readonly record struct ShapeDrawData(
    ShapeKind Kind,
    RectD Bounds,
    double Angle,
    int Points,
    double InnerRatio,
    double CornerRadius,
    float StrokeWidth,
    Paintable Stroke,
    Paintable Fill,
    bool AntiAliasing);

/// <summary>
/// Draws one of the Shape tool's shapes onto a layer, and keeps redrawing it from scratch while the
/// artist is still dragging or adjusting it.
/// </summary>
/// <remarks>
/// The straight-edged shapes and the heart go down as a path, which is what lets one change cover
/// every shape. A plain rectangle and a plain ellipse are handed to the operations the rectangle and
/// ellipse tools already use instead, because those are pixel-perfect: a path would land on the same
/// edges but hand its corners to the rasterizer, and at sprite sizes that shows. Both of those
/// operations rotate the shape themselves, so the path is only needed for the other shapes.
/// </remarks>
internal class DrawRasterShape_UpdateableChange : UpdateableChange
{
    private readonly Guid memberGuid;
    private readonly bool drawOnMask;
    private readonly int frame;
    private ShapeDrawData data;
    private CommittedChunkStorage? storedChunks;

    [GenerateUpdateableChangeActions]
    public DrawRasterShape_UpdateableChange(Guid memberGuid, ShapeDrawData data, bool drawOnMask, int frame)
    {
        this.memberGuid = memberGuid;
        this.data = data;
        this.drawOnMask = drawOnMask;
        this.frame = frame;
    }

    [UpdateChangeMethod]
    public void Update(ShapeDrawData data)
    {
        this.data = data;
    }

    public override bool InitializeAndValidate(Document target)
    {
        return DrawingChangeHelper.IsValidForDrawing(target, memberGuid, drawOnMask, frame);
    }

    /// <summary>The shape as a path, already rotated. Null when the shape has no area to fill.</summary>
    public static VectorPath? BuildPath(ShapeDrawData data)
    {
        if (data.Bounds.Width <= 0 || data.Bounds.Height <= 0)
        {
            return null;
        }

        VectorPath path = new();

        if (data.Kind == ShapeKind.Ellipse)
        {
            path.AddOval(data.Bounds);
        }
        else if (data.Kind == ShapeKind.Heart)
        {
            VecD[] curve = ShapeGeometry.CurvedOutline(ShapeKind.Heart, data.Bounds);
            path.MoveTo((VecF)curve[0]);
            for (int i = 1; i + 2 < curve.Length; i += 3)
            {
                path.CubicTo((VecF)curve[i], (VecF)curve[i + 1], (VecF)curve[i + 2]);
            }

            path.Close();
        }
        else
        {
            VecD[] outline = ShapeGeometry.Outline(data.Kind, data.Bounds, data.Points, data.InnerRatio);
            if (outline.Length == 0)
            {
                path.Dispose();
                return null;
            }

            path.MoveTo((VecF)outline[0]);
            for (int i = 1; i < outline.Length; i++)
            {
                path.LineTo((VecF)outline[i]);
            }

            path.Close();
        }

        if (data.Angle != 0)
        {
            VecD center = data.Bounds.Center;
            path.Transform(Matrix3X3.CreateRotation((float)data.Angle, (float)center.X, (float)center.Y));
        }

        return path;
    }

    private AffectedArea UpdateShape(Document target, ChunkyImage targetImage)
    {
        AffectedArea oldArea = targetImage.FindAffectedArea();
        targetImage.CancelChanges();

        if (data.Bounds.Width > 0 && data.Bounds.Height > 0)
        {
            DrawingChangeHelper.ApplyClipsSymmetriesEtc(target, targetImage, memberGuid, drawOnMask);
            Draw(targetImage);
        }

        AffectedArea area = targetImage.FindAffectedArea();
        area.UnionWith(oldArea);
        return area;
    }

    private void Draw(ChunkyImage targetImage)
    {
        if (data.Kind == ShapeKind.Rectangle)
        {
            targetImage.EnqueueDrawRectangle(new ShapeData(data.Bounds.Center, data.Bounds.Size, data.CornerRadius,
                data.Angle, data.StrokeWidth, data.Stroke, data.Fill) { AntiAliasing = data.AntiAliasing });
            return;
        }

        if (data.Kind == ShapeKind.Ellipse)
        {
            targetImage.EnqueueDrawEllipse(data.Bounds, data.Stroke, data.Fill, data.StrokeWidth, data.Angle,
                data.AntiAliasing);
            return;
        }

        using VectorPath? path = BuildPath(data);
        if (path is null)
        {
            return;
        }

        // Filled first, so a stroke of any width sits on top of its own fill rather than under it.
        if (data.Fill.AnythingVisible)
        {
            targetImage.EnqueueDrawPath(path, data.Fill, 0, StrokeCap.Butt, BlendMode.SrcOver, PaintStyle.Fill,
                data.AntiAliasing);
        }

        if (data.Stroke.AnythingVisible)
        {
            targetImage.EnqueueDrawPath(path, data.Stroke, data.StrokeWidth, StrokeCap.Butt, BlendMode.SrcOver,
                PaintStyle.Stroke, data.AntiAliasing);
        }
    }

    public override OneOf<None, IChangeInfo, List<IChangeInfo>> ApplyTemporarily(Document target)
    {
        ChunkyImage targetImage = DrawingChangeHelper.GetTargetImageOrThrow(target, memberGuid, drawOnMask, frame);
        AffectedArea area = UpdateShape(target, targetImage);
        return DrawingChangeHelper.CreateAreaChangeInfo(memberGuid, area, drawOnMask);
    }

    public override OneOf<None, IChangeInfo, List<IChangeInfo>> Apply(Document target, bool firstApply, out bool ignoreInUndo)
    {
        if (data.Bounds.Width <= 0 || data.Bounds.Height <= 0)
        {
            ignoreInUndo = true;
            return new None();
        }

        ChunkyImage targetImage = DrawingChangeHelper.GetTargetImageOrThrow(target, memberGuid, drawOnMask, frame);
        AffectedArea area = UpdateShape(target, targetImage);
        storedChunks = new CommittedChunkStorage(targetImage, area.Chunks);
        targetImage.CommitChanges();

        ignoreInUndo = false;
        return DrawingChangeHelper.CreateAreaChangeInfo(memberGuid, area, drawOnMask);
    }

    public override OneOf<None, IChangeInfo, List<IChangeInfo>> Revert(Document target)
    {
        AffectedArea area =
            DrawingChangeHelper.ApplyStoredChunksDisposeAndSetToNull(target, memberGuid, drawOnMask, frame, ref storedChunks);
        return DrawingChangeHelper.CreateAreaChangeInfo(memberGuid, area, drawOnMask);
    }

    public override void Dispose()
    {
        storedChunks?.Dispose();
    }
}
