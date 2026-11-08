using Drawie.Backend.Core.ColorsImpl;
using Drawie.Backend.Core.Surfaces;
using Drawie.Backend.Core.Surfaces.PaintImpl;
using Drawie.Numerics;
using PixiEditor.ChangeableDocument.Changeables.Graph.Nodes.FilterNodes;
using PixiEditor.ChangeableDocument.Rendering;

namespace PixiEditor.ChangeableDocument.Changeables.Graph.Nodes.Effects;

/// <summary>
/// Adds a colored glow around the content.
/// </summary>
/// <remarks>
/// A glow is a blurred, tinted copy of the source screened back over it. That differs from
/// <see cref="FilterNodes.ShadowNode"/> with a zero offset, which draws the blur *behind* the
/// content and so reads as a dark halo rather than emitted light.
/// </remarks>
[NodeInfo("Glow")]
public class GlowNode : FilterNode
{
    public const string ColorPropertyName = "Color";
    public const string BrightnessPropertyName = "Brightness";
    public const string RadiusPropertyName = "Radius";

    public InputProperty<Color> Color { get; }
    public InputProperty<double> Brightness { get; }
    public InputProperty<VecD> Radius { get; }

    private ImageFilter? lastGlowFilter;
    private ImageFilter? lastBlendFilter;

    public GlowNode()
    {
        Color = CreateInput(ColorPropertyName, "COLOR", Colors.White);
        Brightness = CreateInput(BrightnessPropertyName, "BRIGHTNESS", 1.0)
            .WithRules(rules => rules.Min(0d).Max(4d));
        Radius = CreateInput(RadiusPropertyName, "RADIUS", new VecD(8, 8));
    }

    protected override ImageFilter? GetImageFilter(RenderContext context)
    {
        VecD radius = Radius.Value;
        float sigmaX = (float)Math.Max(0, radius.X);
        float sigmaY = (float)Math.Max(0, radius.Y);

        // A zero radius would blur nothing, leaving a hard-edged tinted copy rather than a glow.
        if (sigmaX <= 0 && sigmaY <= 0)
            return null;

        Color glowColor = Color.Value;
        double brightness = Math.Max(0, Brightness.Value);

        // Brightness scales how much light the glow contributes. Alpha is the only channel that
        // survives the drop-shadow tint, so it carries the intensity.
        byte alpha = (byte)Math.Clamp(glowColor.A * brightness, 0, 255);
        Color tinted = Drawie.Backend.Core.ColorsImpl.Color.FromRgba(
            glowColor.R, glowColor.G, glowColor.B, alpha);

        DisposeFilters();

        // Null input means "the source", so this is the source blurred and tinted.
        lastGlowFilter = ImageFilter.CreateDropShadow(0, 0, sigmaX, sigmaY, tinted, null);
        if (lastGlowFilter is null)
            return null;

        // Screen the glow under the untouched source so the content stays readable and the glow
        // only ever adds light.
        lastBlendFilter = ImageFilter.CreateBlendMode(BlendMode.Screen, lastGlowFilter, null);
        return lastBlendFilter ?? lastGlowFilter;
    }

    private void DisposeFilters()
    {
        lastBlendFilter?.Dispose();
        lastBlendFilter = null;
        lastGlowFilter?.Dispose();
        lastGlowFilter = null;
    }

    public override Node CreateCopy() => new GlowNode();

    public override void Dispose()
    {
        base.Dispose();
        DisposeFilters();
    }
}
