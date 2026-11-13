namespace PixiEditor.Models.Handlers.Tools;

internal interface IShapeToolHandler : IToolHandler
{
    public bool DrawEven { get; }
    public bool DrawFromCenter { get; }

    /// <summary>
    /// The shape this tool last applied, kept so an undo can reopen it for adjustment. Null once
    /// it has been reopened, or when the last thing applied is no longer the top of the undo stack.
    /// </summary>
    public ShapeReopenData? ReopenableShape { get; set; }
}
