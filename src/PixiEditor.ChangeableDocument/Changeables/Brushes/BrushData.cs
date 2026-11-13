using PixiEditor.ChangeableDocument.Changeables.Graph.Interfaces;

namespace PixiEditor.ChangeableDocument.Changeables.Brushes;

/// <remarks>
/// A record struct so copies can be made with <c>with</c>. Rebuilding one by hand and listing the
/// fields to keep is how the toolbar's blend mode went missing: it was simply left off the list.
/// </remarks>
public record struct BrushData
{
    public IReadOnlyNodeGraph BrushGraph { get; set; }
    public bool AntiAliasing { get; set; }
    public float StrokeWidth { get; set; }

    /// <summary>How the stroke combines with the layer. Normal unless the toolbar says otherwise.</summary>
    public BrushBlendMode BlendMode { get; set; }
    public Guid TargetBrushNodeId { get; set; }

    public BrushData(IReadOnlyNodeGraph brushGraph, Guid targetBrushNodeId)
    {
        BrushGraph = brushGraph;
        TargetBrushNodeId = targetBrushNodeId;
    }
}
