using System.Runtime.InteropServices;
using Drawie.Backend.Core;
using Drawie.Backend.Core.ColorsImpl;
using Drawie.Backend.Core.Surfaces;
using Drawie.Backend.Core.Surfaces.ImageData;
using Drawie.Backend.Core.Surfaces.PaintImpl;
using Drawie.Backend.Core.Vector;
using Drawie.Numerics;
using PixiEditor.ChangeableDocument.Helpers;

namespace PixiEditor.ChangeableDocument.Changes.Selection;

/// <summary>Turns selection paths into per-pixel masks and back.</summary>
internal static class SelectionMaskHelper
{
    private static readonly ImageInfo MaskInfo =
        new(0, 0, ColorType.Rgba8888, AlphaType.Premul, ColorSpace.CreateSrgb());

    /// <summary>Which pixels of a canvas of <paramref name="size"/> the path covers.</summary>
    public static bool[] Rasterize(VectorPath path, VecI size)
    {
        using Surface surface = new(MaskInfo.WithSize(size));
        using Paint fill = new() { Color = Colors.White, IsAntiAliased = false, Style = PaintStyle.Fill };
        surface.DrawingSurface.Canvas.DrawPath(path, fill);

        using Pixmap pixmap = surface.PeekPixels();
        Span<byte> pixels = MemoryMarshal.AsBytes(pixmap.GetPixelSpan<uint>());

        bool[] mask = new bool[size.X * size.Y];
        for (int i = 0; i < mask.Length; i++)
        {
            mask[i] = pixels[(i * 4) + 3] > 127;
        }

        return mask;
    }

    /// <summary>A selection path whose edges follow the mask's outline exactly.</summary>
    public static VectorPath ToPath(bool[] mask, VecI size)
    {
        VectorPath path = new() { FillType = PathFillType.EvenOdd };

        foreach (List<VecI> outline in PixelMask.TraceOutlines(mask, size.X, size.Y))
        {
            path.MoveTo(outline[0]);
            for (int i = 1; i < outline.Count; i++)
            {
                path.LineTo(outline[i]);
            }

            path.Close();
        }

        return path;
    }
}

internal class ModifySelection_Change : Change
{
    private readonly SelectionModification modification;
    private readonly int amount;

    private VectorPath? originalSelection;
    private VectorPath? modifiedSelection;

    [GenerateMakeChangeAction]
    public ModifySelection_Change(SelectionModification modification, int amount)
    {
        this.modification = modification;
        this.amount = amount;
    }

    public override bool InitializeAndValidate(Document target)
    {
        if (amount < 1 || target.Selection.SelectionPath.IsEmpty)
            return false;

        originalSelection = new VectorPath(target.Selection.SelectionPath) { FillType = PathFillType.EvenOdd };

        bool[] mask = SelectionMaskHelper.Rasterize(originalSelection, target.Size);
        bool[] modified = PixelMask.Modify(mask, target.Size.X, target.Size.Y, modification, amount);
        modifiedSelection = SelectionMaskHelper.ToPath(modified, target.Size);
        return true;
    }

    public override OneOf<None, IChangeInfo, List<IChangeInfo>> Apply(Document target, bool firstApply, out bool ignoreInUndo)
    {
        ignoreInUndo = false;
        return SetSelection(target, modifiedSelection!);
    }

    public override OneOf<None, IChangeInfo, List<IChangeInfo>> Revert(Document target)
    {
        return SetSelection(target, originalSelection!);
    }

    private static Selection_ChangeInfo SetSelection(Document target, VectorPath path)
    {
        target.Selection.SelectionPath = new VectorPath(path) { FillType = PathFillType.EvenOdd };
        return new Selection_ChangeInfo(new VectorPath(path) { FillType = PathFillType.EvenOdd });
    }

    public override void Dispose()
    {
        originalSelection?.Dispose();
        modifiedSelection?.Dispose();
    }
}
