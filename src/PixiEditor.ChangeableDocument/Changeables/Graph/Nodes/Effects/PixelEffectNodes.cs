using Drawie.Backend.Core.ColorsImpl;
using Drawie.Backend.Core.Surfaces;
using Drawie.Numerics;
using PixiEditor.ChangeableDocument.Changeables.Graph.Palettes;
using PixiEditor.ChangeableDocument.Helpers;
using PixiEditor.ChangeableDocument.Rendering;

namespace PixiEditor.ChangeableDocument.Changeables.Graph.Nodes.Effects;

[NodeInfo("Dither")]
public class DitherNode : PixelProcessNode
{
    public InputProperty<DitherMode> Mode { get; }

    /// <summary>Values per colour channel, used when no palette is connected.</summary>
    public InputProperty<int> Levels { get; }

    /// <summary>The colours to reduce to. Empty falls back on <see cref="Levels"/>.</summary>
    public InputProperty<Palette> Colors { get; }

    public InputProperty<double> Amount { get; }

    public DitherNode()
    {
        Mode = CreateInput("Mode", "DITHER_MODE_LABEL", DitherMode.Bayer4);
        Levels = CreateInput("Levels", "DITHER_LEVELS_LABEL", 4).WithRules(rules => rules.Min(2).Max(256));
        Colors = CreateInput<Palette>("Colors", "COLORS", Palette.empty);
        Amount = CreateInput("Amount", "DITHER_AMOUNT_LABEL", 1.0).WithRules(rules => rules.Min(0.0).Max(1.0));
    }

    protected override void AddSettingsTo(ref HashCode hash, RenderContext context)
    {
        hash.Add(Mode.Value);
        hash.Add(Levels.Value);
        hash.Add(Colors.Value);
        hash.Add(Amount.Value);
    }

    protected override byte[]? Process(byte[] rgba, VecI size, RenderContext context)
    {
        byte[]? palette = null;
        if (Colors.Value is { Count: > 0 } colors)
        {
            palette = new byte[colors.Count * 3];
            for (int i = 0; i < colors.Count; i++)
            {
                palette[i * 3] = colors[i].R;
                palette[(i * 3) + 1] = colors[i].G;
                palette[(i * 3) + 2] = colors[i].B;
            }
        }

        return Ditherer.Apply(rgba, size.X, size.Y, Mode.Value, palette, Levels.Value, Amount.Value);
    }

    public override Node CreateCopy() => new DitherNode();
}

[NodeInfo("Levels")]
public class LevelsNode : PixelProcessNode
{
    public InputProperty<bool> Auto { get; }
    public InputProperty<double> InputBlack { get; }
    public InputProperty<double> InputWhite { get; }
    public InputProperty<double> Gamma { get; }
    public InputProperty<double> OutputBlack { get; }
    public InputProperty<double> OutputWhite { get; }

    public LevelsNode()
    {
        Auto = CreateInput("Auto", "LEVELS_AUTO_LABEL", false);
        InputBlack = CreateInput("InputBlack", "LEVELS_INPUT_BLACK_LABEL", 0.0).WithRules(rules => rules.Min(0.0).Max(1.0));
        InputWhite = CreateInput("InputWhite", "LEVELS_INPUT_WHITE_LABEL", 1.0).WithRules(rules => rules.Min(0.0).Max(1.0));
        Gamma = CreateInput("Gamma", "LEVELS_GAMMA_LABEL", 1.0).WithRules(rules => rules.Min(0.01).Max(10.0));
        OutputBlack = CreateInput("OutputBlack", "LEVELS_OUTPUT_BLACK_LABEL", 0.0).WithRules(rules => rules.Min(0.0).Max(1.0));
        OutputWhite = CreateInput("OutputWhite", "LEVELS_OUTPUT_WHITE_LABEL", 1.0).WithRules(rules => rules.Min(0.0).Max(1.0));
    }

    protected override void AddSettingsTo(ref HashCode hash, RenderContext context)
    {
        hash.Add(Auto.Value);
        hash.Add(InputBlack.Value);
        hash.Add(InputWhite.Value);
        hash.Add(Gamma.Value);
        hash.Add(OutputBlack.Value);
        hash.Add(OutputWhite.Value);
    }

    protected override byte[]? Process(byte[] rgba, VecI size, RenderContext context)
    {
        double black = InputBlack.Value;
        double white = InputWhite.Value;

        if (Auto.Value)
        {
            (byte low, byte high) = PixelEffects.FindRange(rgba);
            black = low / 255d;
            white = high / 255d;
        }

        return PixelEffects.Levels(rgba, black, white, Gamma.Value, OutputBlack.Value, OutputWhite.Value);
    }

    public override Node CreateCopy() => new LevelsNode();
}

[NodeInfo("Curves")]
public class CurvesNode : PixelProcessNode
{
    public InputProperty<double> Shadows { get; }
    public InputProperty<double> Darks { get; }
    public InputProperty<double> Midtones { get; }
    public InputProperty<double> Lights { get; }
    public InputProperty<double> Highlights { get; }

    public CurvesNode()
    {
        Shadows = CreatePoint("Shadows", "CURVES_SHADOWS_LABEL", 0.0);
        Darks = CreatePoint("Darks", "CURVES_DARKS_LABEL", 0.25);
        Midtones = CreatePoint("Midtones", "CURVES_MIDTONES_LABEL", 0.5);
        Lights = CreatePoint("Lights", "CURVES_LIGHTS_LABEL", 0.75);
        Highlights = CreatePoint("Highlights", "CURVES_HIGHLIGHTS_LABEL", 1.0);
    }

    private InputProperty<double> CreatePoint(string name, string label, double value) =>
        CreateInput(name, label, value).WithRules(rules => rules.Min(0.0).Max(1.0));

    protected override void AddSettingsTo(ref HashCode hash, RenderContext context)
    {
        hash.Add(Shadows.Value);
        hash.Add(Darks.Value);
        hash.Add(Midtones.Value);
        hash.Add(Lights.Value);
        hash.Add(Highlights.Value);
    }

    protected override byte[]? Process(byte[] rgba, VecI size, RenderContext context) =>
        PixelEffects.Curves(rgba, [Shadows.Value, Darks.Value, Midtones.Value, Lights.Value, Highlights.Value]);

    public override Node CreateCopy() => new CurvesNode();
}

[NodeInfo("Pixelate")]
public class PixelateNode : PixelProcessNode
{
    public InputProperty<int> BlockSize { get; }

    public PixelateNode()
    {
        BlockSize = CreateInput("BlockSize", "PIXELATE_BLOCK_SIZE_LABEL", 4).WithRules(rules => rules.Min(1).Max(512));
    }

    protected override void AddSettingsTo(ref HashCode hash, RenderContext context) => hash.Add(BlockSize.Value);

    protected override byte[]? Process(byte[] rgba, VecI size, RenderContext context) =>
        PixelEffects.Pixelate(rgba, size.X, size.Y, Math.Max(1, (int)Math.Round(BlockSize.Value * context.ChunkResolution.Multiplier())));

    public override Node CreateCopy() => new PixelateNode();
}

[NodeInfo("MotionBlur")]
public class MotionBlurNode : PixelProcessNode
{
    public InputProperty<double> Angle { get; }
    public InputProperty<int> Length { get; }

    public MotionBlurNode()
    {
        Angle = CreateInput("Angle", "SHADE_ANGLE_LABEL", 0.0);
        Length = CreateInput("Length", "BLUR_LENGTH_LABEL", 8).WithRules(rules => rules.Min(1).Max(256));
    }

    protected override void AddSettingsTo(ref HashCode hash, RenderContext context)
    {
        hash.Add(Angle.Value);
        hash.Add(Length.Value);
    }

    protected override byte[]? Process(byte[] rgba, VecI size, RenderContext context) =>
        PixelEffects.MotionBlur(rgba, size.X, size.Y, Angle.Value, Math.Max(1, (int)Math.Round(Length.Value * context.ChunkResolution.Multiplier())));

    public override Node CreateCopy() => new MotionBlurNode();
}

[NodeInfo("ZoomBlur")]
public class ZoomBlurNode : PixelProcessNode
{
    public InputProperty<VecD> Center { get; }
    public InputProperty<double> Amount { get; }

    public ZoomBlurNode()
    {
        Center = CreateInput("Center", "BLUR_CENTER_LABEL", new VecD(0.5, 0.5));
        Amount = CreateInput("Amount", "DITHER_AMOUNT_LABEL", 0.2).WithRules(rules => rules.Min(0.0).Max(2.0));
    }

    protected override void AddSettingsTo(ref HashCode hash, RenderContext context)
    {
        hash.Add(Center.Value);
        hash.Add(Amount.Value);
    }

    protected override byte[]? Process(byte[] rgba, VecI size, RenderContext context) =>
        PixelEffects.ZoomBlur(rgba, size.X, size.Y, Center.Value.X, Center.Value.Y, Amount.Value);

    public override Node CreateCopy() => new ZoomBlurNode();
}

[NodeInfo("SpinBlur")]
public class SpinBlurNode : PixelProcessNode
{
    public InputProperty<VecD> Center { get; }
    public InputProperty<double> Degrees { get; }

    public SpinBlurNode()
    {
        Center = CreateInput("Center", "BLUR_CENTER_LABEL", new VecD(0.5, 0.5));
        Degrees = CreateInput("Degrees", "BLUR_DEGREES_LABEL", 15.0).WithRules(rules => rules.Min(0.0).Max(360.0));
    }

    protected override void AddSettingsTo(ref HashCode hash, RenderContext context)
    {
        hash.Add(Center.Value);
        hash.Add(Degrees.Value);
    }

    protected override byte[]? Process(byte[] rgba, VecI size, RenderContext context) =>
        PixelEffects.SpinBlur(rgba, size.X, size.Y, Center.Value.X, Center.Value.Y, Degrees.Value);

    public override Node CreateCopy() => new SpinBlurNode();
}

[NodeInfo("PixelGlow")]
public class PixelGlowNode : PixelProcessNode
{
    public InputProperty<Color> Color { get; }

    public InputProperty<double> Radius { get; }

    public InputProperty<double> Strength { get; }

    public InputProperty<ShadeFalloff> Falloff { get; }

    public InputProperty<int> Bands { get; }

    public InputProperty<OrderedDither> Pattern { get; }

    public InputProperty<bool> Inside { get; }

    public PixelGlowNode()
    {
        Color = CreateInput("Color", "COLOR", Colors.Transparent);
        Radius = CreateInput("Radius", "RADIUS", 6.0).WithRules(rules => rules.Min(0.0).Max(256.0));
        Strength = CreateInput("Strength", "SHADE_STRENGTH_LABEL", 1.0).WithRules(rules => rules.Min(0.0).Max(1.0));
        Falloff = CreateInput("Falloff", "SHADE_FALLOFF_LABEL", ShadeFalloff.Bands);
        Bands = CreateInput("Bands", "SHADE_BANDS_LABEL", 3).WithRules(rules => rules.Min(2).Max(32));
        Pattern = CreateInput("Pattern", "DITHER_MODE_LABEL", OrderedDither.Bayer4);
        Inside = CreateInput("Inside", "GLOW_INSIDE_LABEL", false);
    }

    /// <summary>Screened over the artwork, so the glow only ever adds light to it.</summary>
    protected override bool DrawsBackground => true;

    protected override BlendMode ResultBlendMode => BlendMode.Screen;

    protected override void AddSettingsTo(ref HashCode hash, RenderContext context)
    {
        hash.Add(Color.Value);
        hash.Add(Radius.Value);
        hash.Add(Strength.Value);
        hash.Add(Falloff.Value);
        hash.Add(Bands.Value);
        hash.Add(Pattern.Value);
        hash.Add(Inside.Value);
    }

    protected override byte[]? Process(byte[] rgba, VecI size, RenderContext context)
    {
        GlowStyle style = new(Radius.Value, Strength.Value, Falloff.Value, Bands.Value,
            Pattern.Value, Inside.Value);

        return PixelGlow.Render(rgba, size.X, size.Y, style, Color.Value);
    }

    public override Node CreateCopy() => new PixelGlowNode();
}

[NodeInfo("Vignette")]
public class VignetteNode : PixelProcessNode
{
    public InputProperty<double> Radius { get; }
    public InputProperty<double> Softness { get; }
    public InputProperty<double> Strength { get; }

    public VignetteNode()
    {
        Radius = CreateInput("Radius", "VIGNETTE_RADIUS_LABEL", 0.6).WithRules(rules => rules.Min(0.0).Max(1.5));
        Softness = CreateInput("Softness", "VIGNETTE_SOFTNESS_LABEL", 0.4).WithRules(rules => rules.Min(0.0).Max(1.5));
        Strength = CreateInput("Strength", "SHADE_STRENGTH_LABEL", 0.6).WithRules(rules => rules.Min(0.0).Max(1.0));
    }

    protected override void AddSettingsTo(ref HashCode hash, RenderContext context)
    {
        hash.Add(Radius.Value);
        hash.Add(Softness.Value);
        hash.Add(Strength.Value);
    }

    protected override byte[]? Process(byte[] rgba, VecI size, RenderContext context) =>
        PixelEffects.Vignette(rgba, size.X, size.Y, Radius.Value, Softness.Value, Strength.Value);

    public override Node CreateCopy() => new VignetteNode();
}

[NodeInfo("ChromaticAberration")]
public class ChromaticAberrationNode : PixelProcessNode
{
    public InputProperty<VecI> Offset { get; }

    public ChromaticAberrationNode()
    {
        Offset = CreateInput("Offset", "CHROMATIC_OFFSET_LABEL", new VecI(1, 0));
    }

    protected override void AddSettingsTo(ref HashCode hash, RenderContext context) => hash.Add(Offset.Value);

    protected override byte[]? Process(byte[] rgba, VecI size, RenderContext context)
    {
        double scale = context.ChunkResolution.Multiplier();
        return PixelEffects.ChromaticAberration(rgba, size.X, size.Y,
            (int)Math.Round(Offset.Value.X * scale), (int)Math.Round(Offset.Value.Y * scale));
    }

    public override Node CreateCopy() => new ChromaticAberrationNode();
}

[NodeInfo("ReduceNoise")]
public class ReduceNoiseNode : PixelProcessNode
{
    public InputProperty<int> Radius { get; }

    public ReduceNoiseNode()
    {
        Radius = CreateInput("Radius", "VIGNETTE_RADIUS_LABEL", 1).WithRules(rules => rules.Min(1).Max(8));
    }

    protected override void AddSettingsTo(ref HashCode hash, RenderContext context) => hash.Add(Radius.Value);

    protected override byte[]? Process(byte[] rgba, VecI size, RenderContext context) =>
        PixelEffects.Median(rgba, size.X, size.Y, Radius.Value);

    public override Node CreateCopy() => new ReduceNoiseNode();
}
