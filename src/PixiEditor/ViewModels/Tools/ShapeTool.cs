using Avalonia.Input;
using PixiEditor.Models.Handlers.Tools;
using PixiEditor.ViewModels.Tools.ToolSettings.Toolbars;

namespace PixiEditor.ViewModels.Tools;

internal abstract class ShapeTool : ToolViewModel, IShapeToolHandler
{

    public override bool UsesColor => true;

    public override bool IsErasable => true;
    public bool DrawEven { get; protected set; }
    public bool DrawFromCenter { get; protected set; }

    /// <inheritdoc cref="IShapeToolHandler.ReopenableShape" />
    public ShapeReopenData? ReopenableShape { get; set; }

    /// <summary>
    /// Undo on a shape tool puts the shape back under the transform handles instead of just
    /// removing it, so it can be resized or recoloured and applied again. Restarting the tool is
    /// all it takes: the executor picks the remembered shape up as it starts.
    /// </summary>
    public override void OnPostUndoInlet()
    {
        if (IsActive)
        {
            OnSelected(false);
        }
    }

    public override void OnPostRedoInlet()
    {
        if (IsActive)
        {
            OnSelected(false);
        }
    }

    public ShapeTool()
    {
        Cursor = new Cursor(StandardCursorType.Cross);
        Toolbar = new FillableShapeToolbar();
    }

    protected override void OnDeselecting(bool transient)
    {
        if (!transient)
        {
            // Reopening only makes sense for the shape that is still the last thing done. Leave
            // the tool and that stops being true - undo could by then be undoing something else
            // entirely, and handing the old shape back would duplicate one still in the document.
            ReopenableShape = null;

            ViewModelMain.Current.DocumentManagerSubViewModel.ActiveDocument?.Operations.TryStopToolLinkedExecutor();
        }
    }
}
