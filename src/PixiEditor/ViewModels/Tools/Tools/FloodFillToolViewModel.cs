using Avalonia.Input;
using PixiEditor.ChangeableDocument.Changeables.Graph.Nodes;
using PixiEditor.Models.Commands.Attributes.Commands;
using PixiEditor.Models.Handlers;
using PixiEditor.Models.Handlers.Tools;
using Drawie.Numerics;
using PixiEditor.ChangeableDocument.Enums;
using PixiEditor.UI.Common.Localization;
using PixiEditor.ViewModels.Tools.ToolSettings.Toolbars;

namespace PixiEditor.ViewModels.Tools.Tools;

[Command.Tool(Key = Key.G)]
internal class FloodFillToolViewModel : ToolViewModel, IFloodFillToolHandler
{
    private readonly string defaultActionDisplay = "FLOOD_FILL_TOOL_ACTION_DISPLAY_DEFAULT";

    public override string ToolNameLocalizationKey => "FLOOD_FILL_TOOL";

    public override Type[]? SupportedLayerTypes { get; } = { typeof(IRasterLayerHandler) };

    public override LocalizedString Tooltip => new("FLOOD_FILL_TOOL_TOOLTIP", Shortcut);

    public override bool UsesColor => true;

    public override bool IsErasable => true;

    public bool ConsiderAllLayers { get; private set; }

    [Settings.Percent("TOLERANCE_LABEL", ExposedByDefault = true)]
    public float Tolerance => GetValue<float>();

    [Settings.Enum("FLOOD_FILL_MODE_LABEL", FloodFillMode.Overlay, ExposedByDefault = true)]
    public FloodFillMode FillMode => GetValue<FloodFillMode>();

    [Settings.Bool("CONTIGUOUS_LABEL", true, ExposedByDefault = true)]
    public bool Contiguous => GetValue<bool>();

    private bool altOverridesContiguous;

    /// <summary>Alt forces a non-contiguous fill, i.e. all of that colour in the layer.</summary>
    public bool ResultingContiguous => !altOverridesContiguous && Contiguous;

    // Keep Alt instead of letting it transiently switch to the color picker.
    public override bool HandlesAltModifier => true;

    public override string DefaultIcon => PixiPerfectIcons.Bucket;

    public FloodFillToolViewModel()
    {
        Toolbar = ToolbarFactory.Create(this);
        ActionDisplay = defaultActionDisplay;
    }

    public override Type LayerTypeToCreateOnEmptyUse { get; } = typeof(ImageLayerNode);

    public override void KeyChanged(bool ctrlIsDown, bool shiftIsDown, bool altIsDown, Key argsKey)
    {
        // Alt and Ctrl are independent: one picks what counts as the same colour, the other picks
        // which layers are looked at.
        altOverridesContiguous = altIsDown;
        ConsiderAllLayers = ctrlIsDown;

        if (altIsDown)
        {
            ActionDisplay = "FLOOD_FILL_TOOL_ACTION_DISPLAY_ALT";
        }
        else if (ctrlIsDown)
        {
            ActionDisplay = "FLOOD_FILL_TOOL_ACTION_DISPLAY_CTRL";
        }
        else
        {
            ActionDisplay = defaultActionDisplay;
        }
    }

    public override void UseTool(VecD pos)
    {
        ViewModelMain.Current?.DocumentManagerSubViewModel.ActiveDocument?.Tools.UseFloodFillTool();
    }
}
