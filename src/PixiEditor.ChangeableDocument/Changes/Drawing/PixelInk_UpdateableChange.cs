using PixiEditor.ChangeableDocument.Changeables.Graph.Palettes;
using ChunkyImageLib.Operations;
using Drawie.Backend.Core.ColorsImpl;
using Drawie.Backend.Core.Surfaces;
using Drawie.Numerics;
using PixiEditor.ChangeableDocument.Helpers;

namespace PixiEditor.ChangeableDocument.Changes.Drawing;

/// <summary>How a <see cref="PixelInk_UpdateableChange"/> stroke decides each pixel's colour.</summary>
public enum PixelInkMode
{
    /// <summary>Paints the colour only where an ordered dither pattern allows it.</summary>
    Dither,

    /// <summary>Moves each pixel one step along the palette, towards its end.</summary>
    RampForward,

    /// <summary>Moves each pixel one step along the palette, towards its start.</summary>
    RampBackward,

    /// <summary>Copies each pixel from a fixed offset away.</summary>
    Clone
}

/// <summary>A hard-edged pixel stroke where each pixel's colour depends on where or what it is.</summary>
/// <remarks>
/// Each pixel is decided once per stroke. Going back over an area does not re-shade it, which is what
/// lets a stroke be scrubbed back and forth without running off the end of the ramp.
/// </remarks>
internal class PixelInk_UpdateableChange : UpdateableChange
{
    private readonly Guid memberGuid;
    private readonly PixelInkMode mode;
    private readonly int size;
    private readonly Color color;
    private readonly double density;
    private readonly Color[] palette;
    private readonly VecI cloneOffset;
    private readonly bool drawOnMask;
    private readonly int frame;

    private readonly Dictionary<VecI, Color> painted = new();
    private readonly List<VecI> pending = new();
    private VecI lastPosition;
    private CommittedChunkStorage? storedChunks;

    [GenerateUpdateableChangeActions]
    public PixelInk_UpdateableChange(Guid memberGuid, VecI pos, PixelInkMode mode, int size, Color color, double density,
        Palette palette, VecI cloneOffset, bool drawOnMask, int frame)
    {
        this.cloneOffset = cloneOffset;
        this.memberGuid = memberGuid;
        this.mode = mode;
        this.size = Math.Max(1, size);
        this.color = color;
        this.density = density;
        this.palette = palette.ToArray();
        this.drawOnMask = drawOnMask;
        this.frame = frame;
        lastPosition = pos;
        pending.Add(pos);
    }

    [UpdateChangeMethod]
    public void Update(VecI pos)
    {
        pending.AddRange(BresenhamLineHelper.GetBresenhamLine(lastPosition, pos));
        lastPosition = pos;
    }

    public override bool InitializeAndValidate(Document target)
    {
        if (!DrawingChangeHelper.IsValidForDrawing(target, memberGuid, drawOnMask, frame))
            return false;

        ChunkyImage image = DrawingChangeHelper.GetTargetImageOrThrow(target, memberGuid, drawOnMask, frame)!;
        image.SetBlendMode(BlendMode.SrcOver);
        DrawingChangeHelper.ApplyClipsSymmetriesEtc(target, image, memberGuid, drawOnMask);
        return true;
    }

    public override OneOf<None, IChangeInfo, List<IChangeInfo>> ApplyTemporarily(Document target)
    {
        ChunkyImage image = DrawingChangeHelper.GetTargetImageOrThrow(target, memberGuid, drawOnMask, frame)!;
        int queued = image.QueueLength;

        foreach ((VecI pixel, Color result) in TakeNewPixels(image))
        {
            image.EnqueueDrawPixel(pixel, result, BlendMode.Src);
        }

        return DrawingChangeHelper.CreateAreaChangeInfo(memberGuid, image.FindAffectedArea(queued), drawOnMask);
    }

    public override OneOf<None, IChangeInfo, List<IChangeInfo>> Apply(Document target, bool firstApply, out bool ignoreInUndo)
    {
        ChunkyImage image = DrawingChangeHelper.GetTargetImageOrThrow(target, memberGuid, drawOnMask, frame)!;

        if (firstApply)
        {
            foreach ((VecI pixel, Color result) in TakeNewPixels(image))
            {
                image.EnqueueDrawPixel(pixel, result, BlendMode.Src);
            }
        }
        else
        {
            image.SetBlendMode(BlendMode.SrcOver);
            DrawingChangeHelper.ApplyClipsSymmetriesEtc(target, image, memberGuid, drawOnMask);
            foreach ((VecI pixel, Color result) in painted)
            {
                image.EnqueueDrawPixel(pixel, result, BlendMode.Src);
            }
        }

        AffectedArea affected = image.FindAffectedArea();
        ignoreInUndo = painted.Count == 0;
        storedChunks = new CommittedChunkStorage(image, affected.Chunks);
        image.CommitChanges();
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
    }

    private List<(VecI Pixel, Color Result)> TakeNewPixels(ChunkyImage image)
    {
        List<(VecI, Color)> results = new();
        HashSet<VecI> seen = new();

        foreach (VecI center in pending)
        {
            foreach (VecI pixel in GetFootprint(center))
            {
                if (painted.ContainsKey(pixel) || !seen.Add(pixel))
                {
                    continue;
                }

                if (TryGetColor(image, pixel, out Color result))
                {
                    painted[pixel] = result;
                    results.Add((pixel, result));
                }
            }
        }

        pending.Clear();
        return results;
    }

    private IEnumerable<VecI> GetFootprint(VecI center)
    {
        int low = -(size - 1) / 2;
        int high = low + size - 1;
        double radius = size / 2d;
        double middle = (low + high) / 2d;

        for (int y = low; y <= high; y++)
        {
            for (int x = low; x <= high; x++)
            {
                double dx = x - middle, dy = y - middle;
                if ((dx * dx) + (dy * dy) <= radius * radius)
                {
                    yield return center + new VecI(x, y);
                }
            }
        }
    }

    private bool TryGetColor(ChunkyImage image, VecI pixel, out Color result)
    {
        result = color;

        if (mode == PixelInkMode.Dither)
        {
            return Ditherer.Threshold(DitherMode.Bayer4, pixel.X, pixel.Y) < density;
        }

        if (mode == PixelInkMode.Clone)
        {
            result = image.GetCommittedPixel(pixel + cloneOffset);
            return result.A != 0;
        }

        Color current = image.GetCommittedPixel(pixel);
        if (current.A == 0)
        {
            return false;
        }

        int index = Array.FindIndex(palette, entry => entry.R == current.R && entry.G == current.G && entry.B == current.B);
        if (index < 0)
        {
            return false;
        }

        int stepped = index + (mode == PixelInkMode.RampForward ? 1 : -1);
        if (stepped < 0 || stepped >= palette.Length)
        {
            return false;
        }

        result = palette[stepped].WithAlpha(current.A);
        return true;
    }
}
