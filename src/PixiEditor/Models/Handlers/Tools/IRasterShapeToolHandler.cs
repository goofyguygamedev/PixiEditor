using PixiEditor.ChangeableDocument.Helpers;

namespace PixiEditor.Models.Handlers.Tools;

internal interface IRasterShapeToolHandler : IShapeToolHandler, ICornerRadiusTool
{
    public ShapeKind Shape { get; }
    public double Points { get; }
    public double InnerRatio { get; }
}
