using Avalonia.Input;
using PixiEditor.ChangeableDocument.Enums;
using PixiEditor.Models.Commands.Attributes.Commands;
using PixiEditor.Models.Handlers.Tools;
using PixiEditor.Models.Position;
using Drawie.Numerics;
using PixiEditor.UI.Common.Localization;
using PixiEditor.ViewModels.Tools.ToolSettings.Toolbars;

namespace PixiEditor.ViewModels.Tools.Tools;

[Command.Tool(Key = Key.V)]
internal class SelectToolViewModel : ToolViewModel, ISelectToolHandler
{
    private string defaultActionDisplay = "SELECT_TOOL_ACTION_DISPLAY_DEFAULT";
    public override string ToolNameLocalizationKey => "SELECT_TOOL_NAME";

    public override string DefaultIcon => PixiPerfectIcons.RectangleSelection;

    public SelectToolViewModel()
    {
        ActionDisplay = defaultActionDisplay;
        Toolbar = ToolbarFactory.Create(this);
        Cursor = new Cursor(StandardCursorType.Cross);
    }

    private SelectionMode KeyModifierselectionMode = SelectionMode.New;
    public SelectionMode ResultingSelectionMode => KeyModifierselectionMode != SelectionMode.New ? KeyModifierselectionMode : SelectMode;

    public override Type LayerTypeToCreateOnEmptyUse { get; } = null;

    public override void KeyChanged(bool ctrlIsDown, bool shiftIsDown, bool altIsDown, Key argsKey)
    {
        if (shiftIsDown)
        {
            ActionDisplay = new LocalizedString("SELECT_TOOL_ACTION_DISPLAY_SHIFT");
            KeyModifierselectionMode = SelectionMode.Add;
        }
        else if (ctrlIsDown)
        {
            ActionDisplay = new LocalizedString("SELECT_TOOL_ACTION_DISPLAY_CTRL");
            KeyModifierselectionMode = SelectionMode.Subtract;
        }
        else
        {
            ActionDisplay = defaultActionDisplay;
            KeyModifierselectionMode = SelectionMode.New;
        }
    }

    [Settings.Enum("MODE_LABEL")]
    public SelectionMode SelectMode => GetValue<SelectionMode>();

    [Settings.Enum("SHAPE_LABEL")]
    public SelectionShape SelectShape => GetValue<SelectionShape>();

    //TODO: was Pixel
    public override Type[]? SupportedLayerTypes { get; } = null;

    public override LocalizedString Tooltip => new LocalizedString("SELECT_TOOL_TOOLTIP", Shortcut);

    public override void UseTool(VecD pos)
    {
        ViewModelMain.Current?.DocumentManagerSubViewModel.ActiveDocument?.Tools.UseSelectTool();
    }

    /// <summary>
    /// Brings up the selection's transform box, so an existing selection can be resized by its
    /// handles as soon as a select tool is picked up.
    /// </summary>
    protected override void OnSelected(bool restoring)
    {
        ShowSelectionTransformBox();
    }

    // An undo stops the running executor, which takes the box with it. Putting it back here means
    // stepping through history leaves the handles on whatever selection that step restored.
    public override void OnPostUndoInlet() => ShowSelectionTransformBox();

    public override void OnPostRedoInlet() => ShowSelectionTransformBox();

    private static void ShowSelectionTransformBox() =>
        ViewModelMain.Current?.DocumentManagerSubViewModel.ActiveDocument?.Tools.UseTransformSelection();

    protected override void OnDeselecting(bool transient)
    {
        base.OnDeselecting(transient);

        if (!transient)
        {
            ViewModelMain.Current?.DocumentManagerSubViewModel.ActiveDocument?.Operations
                .TryStopToolLinkedExecutor();
        }
    }
}
