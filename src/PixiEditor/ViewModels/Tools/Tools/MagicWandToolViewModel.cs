using Avalonia.Input;
using PixiEditor.ChangeableDocument.Enums;
using PixiEditor.Models.Commands.Attributes.Commands;
using PixiEditor.Models.Handlers.Tools;
using PixiEditor.Models.Tools;
using Drawie.Numerics;
using PixiEditor.UI.Common.Localization;
using PixiEditor.ViewModels.Tools.ToolSettings.Toolbars;

namespace PixiEditor.ViewModels.Tools.Tools;

[Command.Tool(Key = Key.W)]
internal class MagicWandToolViewModel : ToolViewModel, IMagicWandToolHandler
{
    public override LocalizedString Tooltip => new LocalizedString("MAGIC_WAND_TOOL_TOOLTIP", Shortcut);
    private LocalizedString defaultActionDisplay => new LocalizedString("MAGIC_WAND_ACTION_DISPLAY");
    public override string ToolNameLocalizationKey => "MAGIC_WAND_TOOL";
    public override Type[]? SupportedLayerTypes { get; } = null;
    private SelectionMode KeyModifierSelectionMode = SelectionMode.New;
    public SelectionMode ResultingSelectionMode => KeyModifierSelectionMode != SelectionMode.New ? KeyModifierSelectionMode : SelectMode;
    [Settings.Enum("MODE_LABEL")]
    public SelectionMode SelectMode => GetValue<SelectionMode>();

    [Settings.Enum("SCOPE_LABEL")]
    public DocumentScope DocumentScope => GetValue<DocumentScope>();

    [Settings.Percent("TOLERANCE_LABEL", ExposedByDefault = true)]
    public float Tolerance => GetValue<float>();

    [Settings.Bool("CONTIGUOUS_LABEL", true, ExposedByDefault = true)]
    public bool Contiguous => GetValue<bool>();

    private bool altOverridesContiguous;

    /// <summary>Alt forces a non-contiguous select, i.e. all of that colour in the layer.</summary>
    public bool ResultingContiguous => !altOverridesContiguous && Contiguous;

    // Keep Alt instead of letting it transiently switch to the color picker.
    public override bool HandlesAltModifier => true;

    public override string DefaultIcon => PixiPerfectIcons.MagicWand;

    public MagicWandToolViewModel()
    {
        Toolbar = ToolbarFactory.Create(this);
        ActionDisplay = defaultActionDisplay;
    }

    public override Type LayerTypeToCreateOnEmptyUse { get; } = null;

    public override void UseTool(VecD pos)
    {
        ViewModelMain.Current?.DocumentManagerSubViewModel.ActiveDocument?.Tools.UseMagicWandTool();
    }

    public override void KeyChanged(bool ctrlIsDown, bool shiftIsDown, bool altIsDown, Key argsKey)
    {
        // Alt is independent of the add/subtract modifiers, so it is tracked separately rather
        // than as another branch of the chain below.
        altOverridesContiguous = altIsDown;

        if (altIsDown)
        {
            ActionDisplay = new LocalizedString("MAGIC_WAND_ACTION_DISPLAY_ALT");
            KeyModifierSelectionMode = shiftIsDown
                ? SelectionMode.Add
                : ctrlIsDown
                    ? SelectionMode.Subtract
                    : SelectionMode.New;
            return;
        }

        if (shiftIsDown)
        {
            ActionDisplay = new LocalizedString("MAGIC_WAND_ACTION_DISPLAY_SHIFT");
            KeyModifierSelectionMode = SelectionMode.Add;
        }
        else if (ctrlIsDown)
        {
            ActionDisplay = new LocalizedString("MAGIC_WAND_ACTION_DISPLAY_CTRL");
            KeyModifierSelectionMode = SelectionMode.Subtract;
        }
        else
        {
            ActionDisplay = defaultActionDisplay;
            KeyModifierSelectionMode = SelectionMode.New;
        }
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
