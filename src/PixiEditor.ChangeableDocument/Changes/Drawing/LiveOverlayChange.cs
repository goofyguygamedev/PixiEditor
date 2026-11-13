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

    // Kept between updates rather than built per update: a live change rebuilds on every pointer
    // move, and allocating and freeing canvas-sized GPU surfaces that often is most of the cost.
    private Surface? overlay;
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

    /// <summary>
    /// True when the overlay stands in for the layer's pixels rather than sitting over them, as a
    /// colour correction does: it carries the original pixels everywhere it changed nothing, so it
    /// replaces the area outright instead of being blended against what is underneath.
    /// </summary>
    protected virtual bool OverlayReplacesPixels => false;

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
        overlay?.Dispose();
        overlay = null;
        ReleaseSource();
    }

    /// <summary>
    /// Drops the patch, which is also how <see cref="Apply"/> is told that nothing was painted.
    /// </summary>
    private void DiscardPatch()
    {
        patch?.Dispose();
        patch = null;
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
        if (source is null)
        {
            DiscardPatch();
            return;
        }

        byte[]? overlayBytes = BuildOverlay(out BlendMode blendMode);
        if (overlayBytes is null || FindPaintedBounds(overlayBytes) is not { } bounds)
        {
            DiscardPatch();
            return;
        }

        VecI size = new(Width, Height);
        overlay ??= new Surface(ReadbackInfo.WithSize(size));

        // Src, so the bytes replace the surface rather than blending into the last update's.
        overlay.DrawBytes(size, overlayBytes, ColorType.Rgba8888, AlphaType.Unpremul);

        // Blended here, against the original pixels, and replaced into the layer afterwards. Handing
        // the blend mode to the layer draw would apply it over whatever is already queued there.
        if (patch is null || patch.Size != bounds.Size)
        {
            patch?.Dispose();
            patch = new Surface(ReadbackInfo.WithSize(bounds.Size));
        }

        patchBounds = bounds;
        Canvas canvas = patch.DrawingSurface.Canvas;
        using Paint copy = new() { BlendMode = DrawingBlendMode.Src };
        using Paint blend = new()
        {
            BlendMode = OverlayReplacesPixels ? DrawingBlendMode.Src : RenderContext.GetDrawingBlendMode(blendMode)
        };
        canvas.DrawSurface(source.DrawingSurface, -patchBounds.X, -patchBounds.Y, copy);
        canvas.DrawSurface(overlay.DrawingSurface, -patchBounds.X, -patchBounds.Y, blend);
    }

    /// <summary>
    /// The smallest rectangle holding every pixel the overlay paints, or null if it paints none.
    /// </summary>
    /// <remarks>
    /// A row at a time across the cores, each row keeping its own extent, because this reads every
    /// pixel of the canvas again for every pointer move of a live change.
    /// </remarks>
    private RectI? FindPaintedBounds(byte[] overlay)
    {
        int width = Width;
        int[] rowMin = new int[Height];
        int[] rowMax = new int[Height];

        Parallel.For(0, Height, y =>
        {
            int first = -1;
            int last = -1;
            int rowOffset = y * width * 4;

            for (int x = 0; x < width; x++)
            {
                if (overlay[rowOffset + (x * 4) + 3] == 0)
                {
                    continue;
                }

                if (first < 0)
                {
                    first = x;
                }

                last = x;
            }

            rowMin[y] = first;
            rowMax[y] = last;
        });

        int minX = width, minY = Height, maxX = -1, maxY = -1;
        for (int y = 0; y < Height; y++)
        {
            if (rowMin[y] < 0)
            {
                continue;
            }

            minX = Math.Min(minX, rowMin[y]);
            maxX = Math.Max(maxX, rowMax[y]);
            minY = Math.Min(minY, y);
            maxY = y;
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
