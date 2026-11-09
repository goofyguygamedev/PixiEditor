using Drawie.Backend.Core.ColorsImpl;
using Drawie.Backend.Core.Surfaces;
using Drawie.Backend.Core.Surfaces.PaintImpl;
using Drawie.Numerics;
using PixiEditor.ChangeableDocument.Changeables.Graph.Nodes.FilterNodes;
using PixiEditor.ChangeableDocument.Rendering;

namespace PixiEditor.ChangeableDocument.Changeables.Graph.Nodes.Effects;

/// <summary>
/// Adds a glow around the content, in the content's own colours unless a colour is set.
/// </summary>
/// <remarks>
/// A glow is a blurred copy of the source screened back over it. That differs from
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

    // Neither CreateColorFilter nor CreateBlendMode takes ownership of what it is handed, so the
    // parts have to outlive the filter built from them and be disposed alongside it.
    private readonly List<ImageFilter> ownedImageFilters = new();
    private ColorFilter? ownedColorFilter;

    public GlowNode()
    {
        // Transparent means "no tint", which is the default: see CreateSourceColoredGlow.
        Color = CreateInput(ColorPropertyName, "COLOR", Colors.Transparent);
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

        DisposeFilters();

        lastGlowFilter = glowColor.A == 0
            ? CreateSourceColoredGlow(sigmaX, sigmaY, brightness)
            : CreateTintedGlow(sigmaX, sigmaY, glowColor, brightness);

        if (lastGlowFilter is null)
            return null;

        // Screen the glow under the untouched source so the content stays readable and the glow
        // only ever adds light.
        lastBlendFilter = ImageFilter.CreateBlendMode(BlendMode.Screen, lastGlowFilter, null);
        return lastBlendFilter ?? lastGlowFilter;
    }

    /// <summary>
    /// A glow in the colours already present in the artwork: the source blurred, nothing more.
    /// </summary>
    /// <remarks>
    /// This is what an unset colour means, and it is the default, because a glow is light coming
    /// off the thing that is glowing - red pixels should throw a red halo without having to be told
    /// to, and artwork of several colours should glow in all of them. A drop shadow cannot do this:
    /// it keeps only the silhouette and fills it with one flat colour, which is why the glow used
    /// to come out white over everything.
    /// </remarks>
    private ImageFilter? CreateSourceColoredGlow(float sigmaX, float sigmaY, double brightness)
    {
        ImageFilter? blur = ImageFilter.CreateBlur(sigmaX, sigmaY);
        if (blur is null || Math.Abs(brightness - 1) < 0.001)
        {
            return blur;
        }

        // Brightness scales alpha, the same channel it scales for a tinted glow: alpha is how much
        // of the blurred copy the screen blend lets through, so it is the intensity of the light.
        // Scaling the colours instead would wash the hues out towards white as it was turned up.
        ownedColorFilter = ColorFilter.CreateColorMatrix(new ColorMatrix(
            (1, 0, 0, 0, 0),
            (0, 1, 0, 0, 0),
            (0, 0, 1, 0, 0),
            (0, 0, 0, (float)brightness, 0)));

        ImageFilter? scaled = ImageFilter.CreateColorFilter(ownedColorFilter, blur);
        if (scaled is null)
        {
            return blur; // Brightness is not worth losing the glow over.
        }

        ownedImageFilters.Add(blur);
        return scaled;
    }

    /// <summary>A glow forced to one colour, for when the artwork's own colours are not wanted.</summary>
    private static ImageFilter? CreateTintedGlow(
        float sigmaX, float sigmaY, Color glowColor, double brightness)
    {
        // Alpha is the only channel that survives the drop-shadow tint, so it carries the intensity.
        byte alpha = (byte)Math.Clamp(glowColor.A * brightness, 0, 255);
        Color tinted = Drawie.Backend.Core.ColorsImpl.Color.FromRgba(
            glowColor.R, glowColor.G, glowColor.B, alpha);

        // Null input means "the source", so this is the source blurred and tinted.
        return ImageFilter.CreateDropShadow(0, 0, sigmaX, sigmaY, tinted, null);
    }

    private void DisposeFilters()
    {
        lastBlendFilter?.Dispose();
        lastBlendFilter = null;
        lastGlowFilter?.Dispose();
        lastGlowFilter = null;

        foreach (ImageFilter owned in ownedImageFilters)
        {
            owned.Dispose();
        }

        ownedImageFilters.Clear();

        ownedColorFilter?.Dispose();
        ownedColorFilter = null;
    }

    public override Node CreateCopy() => new GlowNode();

    public override void Dispose()
    {
        base.Dispose();
        DisposeFilters();
    }
}
