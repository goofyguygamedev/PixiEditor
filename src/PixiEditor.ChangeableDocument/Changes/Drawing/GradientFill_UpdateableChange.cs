using Drawie.Backend.Core.ColorsImpl;
using Drawie.Numerics;
using PixiEditor.ChangeableDocument.Helpers;
using BlendMode = PixiEditor.ChangeableDocument.Enums.BlendMode;

namespace PixiEditor.ChangeableDocument.Changes.Drawing;

/// <summary>Everything the Gradient tool lets the artist choose.</summary>
public readonly record struct GradientParameters(
    VecD Start,
    VecD End,
    GradientStyle Style,
    Color From,
    Color To,
    BlendMode BlendMode,
    double Strength,
    GradientArea Area,
    double Tolerance);

/// <summary>Paints a gradient on a layer, inside the selection if there is one.</summary>
internal class GradientFill_UpdateableChange : LiveOverlayChange
{
    private GradientParameters parameters;

    [GenerateUpdateableChangeActions]
    public GradientFill_UpdateableChange(Guid memberGuid, GradientParameters parameters, bool drawOnMask, int frame)
        : base(memberGuid, drawOnMask, frame)
    {
        this.parameters = parameters;
    }

    [UpdateChangeMethod]
    public void Update(GradientParameters parameters)
    {
        this.parameters = parameters;
    }

    protected override byte[]? BuildOverlay(out BlendMode blendMode)
    {
        blendMode = parameters.BlendMode;
        return PixelGradient.Render(Width, Height, parameters.Start, parameters.End, parameters.Style,
            parameters.From, parameters.To, parameters.Strength, BuildMask());
    }

    private bool[]? BuildMask()
    {
        bool[]? mask = parameters.Area switch
        {
            GradientArea.FilledPixels => FilledPixels(),
            GradientArea.ColorArea => ColorAreaAtStart(),
            _ => null
        };

        if (mask is null || Selection is null)
        {
            return mask ?? Selection;
        }

        for (int i = 0; i < mask.Length; i++)
        {
            mask[i] &= Selection[i];
        }

        return mask;
    }

    private bool[] FilledPixels()
    {
        bool[] mask = new bool[Width * Height];
        for (int i = 0; i < mask.Length; i++)
        {
            mask[i] = Pixels[(i * 4) + 3] != 0;
        }

        return mask;
    }

    private bool[] ColorAreaAtStart()
    {
        VecI start = new((int)Math.Floor(parameters.Start.X), (int)Math.Floor(parameters.Start.Y));
        int[] regions = EdgeShader.FloodRegion(Pixels, Width, Height, start, parameters.Tolerance);
        return Array.ConvertAll(regions, region => region != EdgeShader.NoRegion);
    }
}
