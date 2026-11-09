using Drawie.Numerics;

namespace PixiEditor.Models.Handlers.Tools;

/// <summary>Shared by the tools that paint through a pixel ink.</summary>
internal interface IPixelInkToolHandler : IBrushToolHandler
{
    public double ToolSize { get; }

    /// <summary>Share of pixels a dither stroke fills, 0 to 1. Unused by the ramp.</summary>
    public float Density { get; }

    /// <summary>Whether this tool steps along the palette rather than dithering.</summary>
    public bool UsesPaletteRamp { get; }

    public bool IsCloneStamp => false;

    /// <summary>Where a clone reads from relative to the pixel, or null if no source has been picked.</summary>
    public VecI? GetCloneOffset(VecI pressPosition) => null;
}
