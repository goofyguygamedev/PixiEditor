using System.Runtime.InteropServices;
using ChunkyImageLib.Operations;
using Drawie.Backend.Core;
using Drawie.Backend.Core.Surfaces;
using Drawie.Backend.Core.Surfaces.ImageData;
using Drawie.Backend.Core.Surfaces.PaintImpl;
using Drawie.Numerics;
using PixiEditor.ChangeableDocument.Changes.Selection;
using PixiEditor.ChangeableDocument.Helpers;
using PixiEditor.ChangeableDocument.Rendering;
using BlendMode = PixiEditor.ChangeableDocument.Enums.BlendMode;
using DrawingBlendMode = Drawie.Backend.Core.Surfaces.BlendMode;

namespace PixiEditor.ChangeableDocument.Changes.Drawing;

/// <summary>
/// A change that composites a computed image over a layer and can be re-tuned live: the original
/// pixels are read once at the start and every update starts over from them.
/// </summary>
internal abstract class LiveOverlayChange : UpdateableChange
{
    private static readonly ImageInfo ReadbackInfo =
        new(0, 0, ColorType.Rgba8888, AlphaType.Premul, ColorSpace.CreateSrgb());

    private readonly Guid memberGuid;
    private readonly bool drawOnMask;
    private readonly int frame;

    private Surface? source;
    private Surface? patch;
    private RectI patchBounds;
    private CommittedChunkStorage? storedChunks;

    protected LiveOverlayChange(Guid memberGuid, bool drawOnMask, int frame)
    {
        this.memberGuid = memberGuid;
        this.drawOnMask = drawOnMask;
        this.frame = frame;
    }

    protected int Width { get; private set; }

    protected int Height { get; private set; }

    /// <summary>The layer as straight-alpha RGBA, as it was when the change started.</summary>
    protected byte[] Pixels { get; private set; } = [];

    /// <summary>The selection as a pixel mask, or null if nothing is selected.</summary>
    protected bool[]? Selection { get; private set; }

    /// <summary>The image to composite, as straight-alpha RGBA, or null for nothing.</summary>
    protected abstract byte[]? BuildOverlay(out BlendMode blendMode);

    public override bool InitializeAndValidate(Document target)
    {
        if (!DrawingChangeHelper.IsValidForDrawing(target, memberGuid, drawOnMask, frame))
            return false;

        Width = target.Size.X;
        Height = target.Size.Y;

        ChunkyImage image = DrawingChangeHelper.GetTargetImageOrThrow(target, memberGuid, drawOnMask, frame)!;
        source = new Surface(ReadbackInfo.WithSize(target.Size));
        image.DrawCommittedRegionOn(new RectD(0, 0, Width, Height), ChunkResolution.Full,
            source.DrawingSurface.Canvas, VecI.Zero);

        using (Pixmap pixmap = source.PeekPixels())
        {
            Pixels = MemoryMarshal.AsBytes(pixmap.GetPixelSpan<uint>()).ToArray();
        }

        EdgeShader.Unpremultiply(Pixels);

        if (!target.Selection.SelectionPath.IsEmpty)
        {
            Selection = SelectionMaskHelper.Rasterize(target.Selection.SelectionPath, target.Size);
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
        Pixels = [];
        Selection = null;
    }

    private void RebuildPatch()
    {
        patch?.Dispose();
        patch = null;

        if (source is null)
            return;

        byte[]? overlayBytes = BuildOverlay(out BlendMode blendMode);
        if (overlayBytes is null || FindPaintedBounds(overlayBytes) is not { } bounds)
            return;

        VecI size = new(Width, Height);
        using Surface overlay = new(ReadbackInfo.WithSize(size));
        overlay.DrawBytes(size, overlayBytes, ColorType.Rgba8888, AlphaType.Unpremul);

        // Blended here, against the original pixels, and replaced into the layer afterwards. Handing
        // the blend mode to the layer draw would apply it over whatever is already queued there.
        patchBounds = bounds;
        patch = new Surface(ReadbackInfo.WithSize(patchBounds.Size));
        Canvas canvas = patch.DrawingSurface.Canvas;
        using Paint copy = new() { BlendMode = DrawingBlendMode.Src };
        using Paint blend = new() { BlendMode = RenderContext.GetDrawingBlendMode(blendMode) };
        canvas.DrawSurface(source.DrawingSurface, -patchBounds.X, -patchBounds.Y, copy);
        canvas.DrawSurface(overlay.DrawingSurface, -patchBounds.X, -patchBounds.Y, blend);
    }

    private RectI? FindPaintedBounds(byte[] overlay)
    {
        int minX = Width, minY = Height, maxX = -1, maxY = -1;
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                if (overlay[(((y * Width) + x) * 4) + 3] == 0)
                {
                    continue;
                }

                minX = Math.Min(minX, x);
                maxX = Math.Max(maxX, x);
                minY = Math.Min(minY, y);
                maxY = Math.Max(maxY, y);
            }
        }

        return maxX < 0 ? null : new RectI(minX, minY, maxX - minX + 1, maxY - minY + 1);
    }

    private void EnqueuePatch(ChunkyImage image)
    {
        if (patch is null)
            return;

        using Paint replace = new() { BlendMode = DrawingBlendMode.Src };
        image.EnqueueDrawImage(patchBounds.Pos, patch, replace);
    }
}
