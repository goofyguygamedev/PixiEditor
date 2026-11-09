using Drawie.Backend.Core.ColorsImpl;
using Drawie.Numerics;
using PixiEditor.ChangeableDocument.Helpers;
using BlendMode = PixiEditor.ChangeableDocument.Enums.BlendMode;

namespace PixiEditor.ChangeableDocument.Changes.Drawing;

/// <summary>Everything the Outline tool lets the artist choose.</summary>
public readonly record struct OutlineParameters(
    OutlineStyle Style,
    OutlineTarget Target,
    Color Color,
    BlendMode BlendMode,
    double Strength,
    double Tolerance);

/// <summary>
/// Outlines part of a layer: the filled pixels of the selection if there is one, otherwise whatever
/// the target setting picks out at the clicked pixel.
/// </summary>
internal class OutlineShape_UpdateableChange : LiveOverlayChange
{
    private VecI clickPos;
    private OutlineParameters parameters;

    [GenerateUpdateableChangeActions]
    public OutlineShape_UpdateableChange(Guid memberGuid, VecI clickPos, OutlineParameters parameters, bool drawOnMask, int frame)
        : base(memberGuid, drawOnMask, frame)
    {
        this.clickPos = clickPos;
        this.parameters = parameters;
    }

    [UpdateChangeMethod]
    public void Update(VecI clickPos, OutlineParameters parameters)
    {
        this.clickPos = clickPos;
        this.parameters = parameters;
    }

    protected override byte[]? BuildOverlay(out BlendMode blendMode)
    {
        blendMode = parameters.BlendMode;
        float[] amounts = PixelOutline.Compute(BuildShape(), Width, Height, parameters.Style);
        Color color = parameters.Color;
        return EdgeShader.BuildOverlay(amounts, color.R, color.G, color.B, color.A, parameters.Strength);
    }

    private bool[] BuildShape()
    {
        if (Selection is not null)
        {
            int[] selected = EdgeShader.RegionFromMask(Selection, Pixels);
            return Array.ConvertAll(selected, region => region != EdgeShader.NoRegion);
        }

        switch (parameters.Target)
        {
            case OutlineTarget.ColorArea:
                int[] regions = EdgeShader.FloodRegion(Pixels, Width, Height, clickPos, parameters.Tolerance);
                return Array.ConvertAll(regions, region => region != EdgeShader.NoRegion);
            case OutlineTarget.WholeLayer:
                bool[] filled = new bool[Width * Height];
                for (int i = 0; i < filled.Length; i++)
                {
                    filled[i] = Pixels[(i * 4) + 3] != 0;
                }

                return filled;
            default:
                return PixelOutline.FloodFilled(Pixels, Width, Height, clickPos);
        }
    }
}
