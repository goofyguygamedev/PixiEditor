using PixiEditor.ChangeableDocument.Changeables.Graph.Palettes;
using System.Runtime.InteropServices;
using ChunkyImageLib.Operations;
using Drawie.Backend.Core;
using Drawie.Backend.Core.ColorsImpl;
using Drawie.Backend.Core.Surfaces;
using Drawie.Backend.Core.Surfaces.ImageData;
using Drawie.Backend.Core.Surfaces.PaintImpl;
using Drawie.Numerics;
using PixiEditor.ChangeableDocument.Helpers;

namespace PixiEditor.ChangeableDocument.Changes.Drawing;

/// <summary>
/// Remaps a layer onto the document's palette, with the chosen dither. Stays inside the selection
/// if there is one.
/// </summary>
internal class ReduceToPalette_Change : Change
{
    private static readonly ImageInfo ReadbackInfo =
        new(0, 0, ColorType.Rgba8888, AlphaType.Premul, ColorSpace.CreateSrgb());

    private readonly Guid memberGuid;
    private readonly Color[] palette;
    private readonly DitherMode mode;
    private readonly int frame;

    private CommittedChunkStorage? savedChunks;

    [GenerateMakeChangeAction]
    public ReduceToPalette_Change(Guid memberGuid, Palette palette, DitherMode mode, int frame)
    {
        this.memberGuid = memberGuid;
        this.palette = palette.ToArray();
        this.mode = mode;
        this.frame = frame;
    }

    public override bool InitializeAndValidate(Document target) =>
        palette.Length > 0 && DrawingChangeHelper.IsValidForDrawing(target, memberGuid, false, frame);

    public override OneOf<None, IChangeInfo, List<IChangeInfo>> Apply(Document target, bool firstApply, out bool ignoreInUndo)
    {
        ChunkyImage image = DrawingChangeHelper.GetTargetImageOrThrow(target, memberGuid, false, frame)!;
        VecI size = target.Size;

        using Surface source = new(ReadbackInfo.WithSize(size));
        image.DrawCommittedRegionOn(new RectD(0, 0, size.X, size.Y), ChunkResolution.Full, source.DrawingSurface.Canvas, VecI.Zero);

        byte[] pixels;
        using (Pixmap pixmap = source.PeekPixels())
        {
            pixels = MemoryMarshal.AsBytes(pixmap.GetPixelSpan<uint>()).ToArray();
        }

        EdgeShader.Unpremultiply(pixels);

        byte[] paletteRgb = new byte[palette.Length * 3];
        for (int i = 0; i < palette.Length; i++)
        {
            paletteRgb[i * 3] = palette[i].R;
            paletteRgb[(i * 3) + 1] = palette[i].G;
            paletteRgb[(i * 3) + 2] = palette[i].B;
        }

        byte[] reduced = Ditherer.Apply(pixels, size.X, size.Y, mode, paletteRgb, 2, 1);

        using Surface result = new(ReadbackInfo.WithSize(size));
        result.DrawBytes(size, reduced, ColorType.Rgba8888, AlphaType.Unpremul);

        if (!target.Selection.SelectionPath.IsEmpty)
            image.SetClippingPath(target.Selection.SelectionPath);

        using Paint replace = new() { BlendMode = BlendMode.Src };
        image.EnqueueDrawImage(VecI.Zero, result, replace);

        AffectedArea affected = image.FindAffectedArea();
        savedChunks = new CommittedChunkStorage(image, affected.Chunks);
        image.CommitChanges();

        ignoreInUndo = false;
        return DrawingChangeHelper.CreateAreaChangeInfo(memberGuid, affected, false);
    }

    public override OneOf<None, IChangeInfo, List<IChangeInfo>> Revert(Document target)
    {
        AffectedArea affected =
            DrawingChangeHelper.ApplyStoredChunksDisposeAndSetToNull(target, memberGuid, false, frame, ref savedChunks);
        return DrawingChangeHelper.CreateAreaChangeInfo(memberGuid, affected, false);
    }

    public override void Dispose()
    {
        savedChunks?.Dispose();
    }
}
