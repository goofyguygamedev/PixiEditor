using PixiEditor.ChangeableDocument.Helpers;
using BlendMode = PixiEditor.ChangeableDocument.Enums.BlendMode;

namespace PixiEditor.ChangeableDocument.Changes.Drawing;

/// <summary>
/// Corrects the colours of a layer, or of just the selection when there is one, and can be re-tuned
/// live so the window's sliders can be dragged against the canvas before anything is committed.
/// </summary>
internal class AdjustColors_UpdateableChange : LiveOverlayChange
{
    private ColorAdjustments adjustments;

    [GenerateUpdateableChangeActions]
    public AdjustColors_UpdateableChange(Guid memberGuid, ColorAdjustments adjustments, bool drawOnMask, int frame)
        : base(memberGuid, drawOnMask, frame)
    {
        this.adjustments = adjustments;
    }

    [UpdateChangeMethod]
    public void Update(ColorAdjustments adjustments)
    {
        this.adjustments = adjustments;
    }

    /// <summary>A correction changes the pixels that are there; it does not lay anything over them.</summary>
    protected override bool OverlayReplacesPixels => true;

    protected override byte[]? BuildOverlay(out BlendMode blendMode)
    {
        blendMode = BlendMode.Normal;

        if (PixelAdjust.IsNeutral(adjustments))
        {
            return null;
        }

        return PixelAdjust.Apply(Pixels, adjustments, Selection);
    }
}
