namespace PixiEditor.Models.Handlers.Tools;

internal interface IEraserToolHandler : IBrushToolHandler
{
    public double ToolSize { get; }

    /// <summary>When set, the primary colour's alpha decides how much alpha the eraser takes away.</summary>
    public bool UseColorAlpha { get; }
}
