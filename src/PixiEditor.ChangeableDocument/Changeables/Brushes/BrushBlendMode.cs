using System.ComponentModel;
using DrawingApiBlendMode = Drawie.Backend.Core.Surfaces.BlendMode;

namespace PixiEditor.ChangeableDocument.Changeables.Brushes;

/// <summary>
/// How a brush stroke lands on the layer underneath it. Chosen in the toolbar, so it overrides
/// whatever the brush's own graph asks for.
/// </summary>
public enum BrushBlendMode
{
    [Description("BRUSH_BLEND_NORMAL")]
    Normal,

    [Description("BRUSH_BLEND_OVERWRITE")]
    Overwrite,

    [Description("BRUSH_BLEND_MULTIPLY")]
    Multiply,

    [Description("BRUSH_BLEND_SCREEN")]
    Screen,

    [Description("BRUSH_BLEND_ADD")]
    Add,

    [Description("BRUSH_BLEND_DARKEN")]
    Darken,

    [Description("BRUSH_BLEND_LIGHTEN")]
    Lighten,

    [Description("BRUSH_BLEND_DIFFERENCE")]
    Difference
}

/// <summary>
/// Turns a brush blend mode into the two blend modes the engine actually uses. There are two,
/// because a stroke is combined with the layer at two different moments.
/// </summary>
/// <remarks>
/// The image blend mode joins the finished stroke to the layer, once. The stamp blend mode joins
/// each dab to whatever the stroke has built up so far. Most modes belong at the image level: put
/// Multiply on the stamps instead and every place the brush crossed its own path would multiply
/// twice and come out darker than the rest of the line.
///
/// Overwrite is the exception. Replacing pixels twice is the same as replacing them once, so it is
/// safe on the stamps - and it has to be there, because at the image level the engine reads Src as
/// "this stroke carries the whole layer" and seeds the stroke with a copy of what is underneath.
/// </remarks>
public static class BrushBlendModes
{
    public static DrawingApiBlendMode ToImageBlendMode(this BrushBlendMode mode, DrawingApiBlendMode brushDefault) =>
        mode switch
        {
            BrushBlendMode.Multiply => DrawingApiBlendMode.Multiply,
            BrushBlendMode.Screen => DrawingApiBlendMode.Screen,
            BrushBlendMode.Add => DrawingApiBlendMode.Plus,
            BrushBlendMode.Darken => DrawingApiBlendMode.Darken,
            BrushBlendMode.Lighten => DrawingApiBlendMode.Lighten,
            BrushBlendMode.Difference => DrawingApiBlendMode.Difference,
            _ => brushDefault
        };

    /// <summary>
    /// For the pixel perfect pen, which bypasses the brush engine and writes pixels itself. It has
    /// only one blend point, so Overwrite has to land there instead of on a stamp.
    /// </summary>
    public static DrawingApiBlendMode ToPixelBlendMode(this BrushBlendMode mode) =>
        mode == BrushBlendMode.Overwrite
            ? DrawingApiBlendMode.Src
            : mode.ToImageBlendMode(DrawingApiBlendMode.SrcOver);

    public static DrawingApiBlendMode ToStampBlendMode(this BrushBlendMode mode, DrawingApiBlendMode brushDefault) =>
        mode switch
        {
            BrushBlendMode.Overwrite => DrawingApiBlendMode.Src,
            _ => brushDefault
        };
}
