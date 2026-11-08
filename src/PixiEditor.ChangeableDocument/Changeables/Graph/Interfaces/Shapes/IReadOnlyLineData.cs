using Drawie.Numerics;

namespace PixiEditor.ChangeableDocument.Changeables.Graph.Interfaces.Shapes;

public interface IReadOnlyLineData : IReadOnlyShapeVectorData
{
    public VecD Start { get; }
    public VecD End { get; }
    public VecD TransformedStart { get; set; }
    public VecD TransformedEnd { get; set; }

    /// <summary>Points between the ends that bend the line; empty for a plain straight line.</summary>
    public IReadOnlyList<VecD> ControlPoints { get; }

    /// <summary>0 draws straight segments between the points, 1 a fully smooth curve.</summary>
    public double Smoothness { get; }
}
