using System.Runtime.InteropServices;
using ChunkyImageLib.Operations;
using Drawie.Backend.Core;
using Drawie.Backend.Core.ColorsImpl;
using Drawie.Backend.Core.Surfaces;
using Drawie.Backend.Core.Surfaces.ImageData;
using Drawie.Backend.Core.Surfaces.PaintImpl;
using Drawie.Backend.Core.Vector;
using Drawie.Numerics;
using PixiEditor.ChangeableDocument.Changes.Selection;
using PixiEditor.ChangeableDocument.Helpers;
using PixiEditor.ChangeableDocument.Rendering;
using BlendMode = PixiEditor.ChangeableDocument.Enums.BlendMode;
using DrawingBlendMode = Drawie.Backend.Core.Surfaces.BlendMode;

namespace PixiEditor.ChangeableDocument.Changes.Drawing;

/// <summary>Everything the Shade tool lets the artist choose.</summary>
public readonly record struct ShadeParameters(
    EdgeShadeShape Shape,
    double Strength,
    BlendMode BlendMode,
    Color Color,
    double Tolerance);

/// <summary>
/// Shades one region of a layer along its edge: the current selection if there is one, otherwise the
/// connected area of like colours under the pointer.
/// </summary>
/// <remarks>
/// The original pixels are read once, when the change starts, and every update re-shades from them.
/// That is what lets the angle and the other settings be adjusted live without the shade building up
/// on itself.
/// </remarks>
internal class ShadeRegion_UpdateableChange : UpdateableChange
{
    private static readonly ImageInfo ReadbackInfo =
        new(0, 0, ColorType.Rgba8888, AlphaType.Premul, ColorSpace.CreateSrgb());

    private readonly Guid memberGuid;
    private readonly bool drawOnMask;
    private readonly int frame;
    private VecI clickPos;
    private ShadeParameters parameters;

    private Surface? source;
    private byte[]? straightPixels;
    private int[]? selectionRegion;
    private int width;
    private int height;

    private Surface? patch;
    private RectI patchBounds;
    private CommittedChunkStorage? storedChunks;

    [GenerateUpdateableChangeActions]
    public ShadeRegion_UpdateableChange(Guid memberGuid, VecI clickPos, ShadeParameters parameters, bool drawOnMask, int frame)
    {
        this.memberGuid = memberGuid;
        this.clickPos = clickPos;
        this.parameters = parameters;
        this.drawOnMask = drawOnMask;
        this.frame = frame;
    }

    [UpdateChangeMethod]
    public void Update(VecI clickPos, ShadeParameters parameters)
    {
        this.clickPos = clickPos;
        this.parameters = parameters;
    }

    public override bool InitializeAndValidate(Document target)
    {
        if (!DrawingChangeHelper.IsValidForDrawing(target, memberGuid, drawOnMask, frame))
            return false;

        width = target.Size.X;
        height = target.Size.Y;

        ChunkyImage image = DrawingChangeHelper.GetTargetImageOrThrow(target, memberGuid, drawOnMask, frame)!;
        source = new Surface(ReadbackInfo.WithSize(target.Size));
        image.DrawCommittedRegionOn(new RectD(0, 0, width, height), ChunkResolution.Full,
            source.DrawingSurface.Canvas, VecI.Zero);

        using (Pixmap pixmap = source.PeekPixels())
        {
            straightPixels = MemoryMarshal.AsBytes(pixmap.GetPixelSpan<uint>()).ToArray();
        }

        EdgeShader.Unpremultiply(straightPixels);

        if (!target.Selection.SelectionPath.IsEmpty)
        {
            selectionRegion = EdgeShader.RegionFromMask(
                SelectionMaskHelper.Rasterize(target.Selection.SelectionPath, target.Size), straightPixels);
        }

        return true;
    }

    public override OneOf<None, IChangeInfo, List<IChangeInfo>> ApplyTemporarily(Document target)
    {
        ChunkyImage image = DrawingChangeHelper.GetTargetImageOrThrow(target, memberGuid, drawOnMask, frame)!;
        AffectedArea previous = image.FindAffectedArea();

        image.CancelChanges();
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
            RebuildPatch();
            ReleaseSource();
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
        ReleaseSource();
    }

    private void ReleaseSource()
    {
        source?.Dispose();
        source = null;
        straightPixels = null;
        selectionRegion = null;
    }

    private void RebuildPatch()
    {
        patch?.Dispose();
        patch = null;

        if (source is null || straightPixels is null)
            return;

        int[] region = selectionRegion
                       ?? EdgeShader.FloodRegion(straightPixels, width, height, clickPos, parameters.Tolerance);

        float[] amounts = EdgeShader.Compute(region, width, height, parameters.Shape);
        RectI? bounds = EdgeShader.FindBounds(amounts, width, height);
        if (bounds is null)
            return;

        Color color = parameters.Color;
        byte[] overlayBytes = EdgeShader.BuildOverlay(amounts, color.R, color.G, color.B, color.A, parameters.Strength);

        VecI size = new(width, height);
        using Surface overlay = new(ReadbackInfo.WithSize(size));
        overlay.DrawBytes(size, overlayBytes, ColorType.Rgba8888, AlphaType.Unpremul);

        // Blended here, against the original pixels, and replaced into the layer afterwards. Handing
        // the blend mode to the layer's draw instead would have it applied on top of whatever is
        // already queued there rather than on the artwork.
        patchBounds = bounds.Value;
        patch = new Surface(ReadbackInfo.WithSize(patchBounds.Size));
        Canvas canvas = patch.DrawingSurface.Canvas;
        using Paint copy = new() { BlendMode = DrawingBlendMode.Src };
        using Paint blend = new() { BlendMode = RenderContext.GetDrawingBlendMode(parameters.BlendMode) };
        canvas.DrawSurface(source.DrawingSurface, -patchBounds.X, -patchBounds.Y, copy);
        canvas.DrawSurface(overlay.DrawingSurface, -patchBounds.X, -patchBounds.Y, blend);
    }

    private void EnqueuePatch(ChunkyImage image)
    {
        if (patch is null)
            return;

        using Paint replace = new() { BlendMode = DrawingBlendMode.Src };
        image.EnqueueDrawImage(patchBounds.Pos, patch, replace);
    }
}
