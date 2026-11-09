using Drawie.Backend.Core.ColorsImpl;
using Drawie.Backend.Core.Surfaces;
using Drawie.Numerics;
using PixiEditor.ChangeableDocument.Helpers;
using PixiEditor.ChangeableDocument.Rendering;
using BlendMode = PixiEditor.ChangeableDocument.Enums.BlendMode;
using DrawingBlendMode = Drawie.Backend.Core.Surfaces.BlendMode;

namespace PixiEditor.ChangeableDocument.Changeables.Graph.Nodes.Effects;

/// <summary>
/// Shades every area of like colours along the edges where it meets another colour.
/// </summary>
/// <remarks>
/// The automatic form of the Shade tool: rather than shading one chosen region, it finds them all,
/// so an outlined sprite picks up its shading as soon as the node is connected and keeps it as the
/// art changes. To shade with the editor's current colour, wire the Editor Info node's primary colour
/// into <see cref="Color"/>.
/// </remarks>
[NodeInfo("ShadeEdges")]
public class ShadeEdgesNode : PixelProcessNode
{
    /// <summary>The side that gets shaded, in degrees. 0 is right, 90 up.</summary>
    public InputProperty<double> Angle { get; }

    /// <summary>How far around that side the shade wraps. 360 shades a full ring.</summary>
    public InputProperty<double> Spread { get; }

    public InputProperty<int> Thickness { get; }
    public InputProperty<ShadeFalloff> Falloff { get; }
    public InputProperty<int> Bands { get; }
    public InputProperty<Color> Color { get; }
    public InputProperty<BlendMode> ShadeBlendMode { get; }
    public InputProperty<double> Strength { get; }

    /// <summary>How different two colours may be and still count as the same area.</summary>
    public InputProperty<double> Tolerance { get; }

    /// <summary>
    /// Only edges against this colour cast shade, and areas of this colour are not shaded themselves.
    /// Transparent means any other colour counts - set it to the outline colour to shade only
    /// against the outline.
    /// </summary>
    public InputProperty<Color> EdgeColor { get; }

    /// <summary>Whether the empty space around the art counts as an edge.</summary>
    public InputProperty<bool> TransparentIsEdge { get; }

    /// <summary>
    /// Areas smaller than this many pixels are left alone, so specks and dithering do not each get
    /// shaded as though they were shapes.
    /// </summary>
    public InputProperty<int> MinimumArea { get; }

    protected override bool DrawsBackground => true;

    protected override DrawingBlendMode ResultBlendMode => RenderContext.GetDrawingBlendMode(ShadeBlendMode.Value);

    public ShadeEdgesNode()
    {
        Angle = CreateInput("Angle", "SHADE_ANGLE_LABEL", 315.0);
        Spread = CreateInput("Spread", "SHADE_SPREAD_LABEL", 90.0)
            .WithRules(rules => rules.Min(0.0).Max(360.0));
        Thickness = CreateInput("Thickness", "SHADE_THICKNESS_LABEL", 3)
            .WithRules(rules => rules.Min(0).Max(128));
        Falloff = CreateInput("Falloff", "SHADE_FALLOFF_LABEL", ShadeFalloff.Hard);
        Bands = CreateInput("Bands", "SHADE_BANDS_LABEL", 2)
            .WithRules(rules => rules.Min(1).Max(16));
        Color = CreateInput("Color", "COLOR", Colors.Black);
        ShadeBlendMode = CreateInput("BlendMode", "SHADE_BLEND_LABEL", BlendMode.Multiply);
        Strength = CreateInput("Strength", "SHADE_STRENGTH_LABEL", 0.5)
            .WithRules(rules => rules.Min(0.0).Max(1.0));
        Tolerance = CreateInput("Tolerance", "TOLERANCE_LABEL", 0.0)
            .WithRules(rules => rules.Min(0.0).Max(1.0));
        EdgeColor = CreateInput("EdgeColor", "SHADE_EDGE_COLOR_LABEL", Colors.Transparent);
        TransparentIsEdge = CreateInput("TransparentIsEdge", "SHADE_TRANSPARENT_EDGE_LABEL", true);
        MinimumArea = CreateInput("MinimumArea", "SHADE_MINIMUM_AREA_LABEL", 4)
            .WithRules(rules => rules.Min(1));
    }

    private EdgeShadeShape GetShape(RenderContext context) => new(Angle.Value, Spread.Value,
        Math.Max(1, (int)Math.Round(Thickness.Value * context.ChunkResolution.Multiplier())), Falloff.Value, Bands.Value);

    protected override void AddSettingsTo(ref HashCode hash, RenderContext context)
    {
        hash.Add(GetShape(context));
        hash.Add(Color.Value);
        hash.Add(Strength.Value);
        hash.Add(Tolerance.Value);
        hash.Add(EdgeColor.Value);
        hash.Add(TransparentIsEdge.Value);
        hash.Add(MinimumArea.Value);
    }

    protected override byte[]? Process(byte[] rgba, VecI size, RenderContext context)
    {
        if (Thickness.Value <= 0 || Strength.Value <= 0)
        {
            return null;
        }

        double scale = context.ChunkResolution.Multiplier();
        int minimumArea = Math.Max(1, (int)Math.Round(MinimumArea.Value * scale * scale));
        float[] amounts = ComputeAmounts(rgba, size, GetShape(context), minimumArea);

        Color color = Color.Value;
        return EdgeShader.BuildOverlay(amounts, color.R, color.G, color.B, color.A, Strength.Value);
    }

    private float[] ComputeAmounts(byte[] rgba, VecI size, EdgeShadeShape shape, int minimumArea)
    {
        int pixelCount = size.X * size.Y;
        double tolerance = Tolerance.Value;
        int[] regions = EdgeShader.SegmentLikeColors(rgba, size.X, size.Y, tolerance, out int regionCount);

        Color edge = EdgeColor.Value;
        bool hasEdgeColor = edge.A > 0;
        bool transparentIsEdge = TransparentIsEdge.Value;

        int[] areas = new int[regionCount];
        bool[] shadeable = new bool[regionCount];
        Array.Fill(shadeable, true);

        bool[]? edgeMask = hasEdgeColor || !transparentIsEdge ? new bool[pixelCount] : null;

        for (int i = 0; i < pixelCount; i++)
        {
            bool isTransparent = regions[i] == EdgeShader.NoRegion;
            bool matchesEdge = !isTransparent && hasEdgeColor
                               && EdgeShader.ColorsMatch(rgba, i, edge.R, edge.G, edge.B, edge.A, tolerance);

            if (!isTransparent)
            {
                areas[regions[i]]++;
                if (matchesEdge)
                {
                    shadeable[regions[i]] = false;
                }
            }

            if (edgeMask is not null)
            {
                edgeMask[i] = isTransparent ? transparentIsEdge : !hasEdgeColor || matchesEdge;
            }
        }

        for (int region = 0; region < regionCount; region++)
        {
            shadeable[region] &= areas[region] >= minimumArea;
        }

        return EdgeShader.Compute(regions, size.X, size.Y, shape, edgeMask, shadeable);
    }

    public override Node CreateCopy()
    {
        return new ShadeEdgesNode();
    }
}
