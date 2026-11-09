using PixiEditor.ChangeableDocument.Helpers;

namespace PixiEditor.Models.Handlers.Tools;

internal interface ILiquifyToolHandler : IBrushToolHandler
{
    public double ToolSize { get; }
    public LiquifyMode Mode { get; }
    public float Strength { get; }
    public bool Smooth { get; }
    public bool KeepEdges { get; }
    public double EdgeWidth { get; }
}
