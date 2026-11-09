using Avalonia.Input;
using Drawie.Numerics;
using PixiEditor.ChangeableDocument.Helpers;
using PixiEditor.Models.Commands.Attributes.Commands;
using PixiEditor.Models.Handlers.Tools;
using PixiEditor.UI.Common.Localization;
using PixiEditor.ViewModels.Tools.ToolSettings.Toolbars;

namespace PixiEditor.ViewModels.Tools.Tools;

/// <summary>
/// Pushes, twirls, pinches and expands existing pixels.
/// </summary>
/// <remarks>
/// A brush tool only so that it gets the brush size setting, the size shortcuts and the circle under
/// the cursor. It does not paint with the brush, so the brush and stabilizer pickers are hidden.
/// </remarks>
[Command.Tool(Key = Key.K)]
internal class LiquifyToolViewModel : BrushBasedToolViewModel, ILiquifyToolHandler
{
    public override string ToolNameLocalizationKey => "LIQUIFY_TOOL";
    public override LocalizedString Tooltip => new("LIQUIFY_TOOL_TOOLTIP", Shortcut);
    public override string DefaultIcon => PixiPerfectIcons.Smudge;
    public override bool UsesColor => false;
    public override bool IsErasable => false;

    [Settings.Enum("LIQUIFY_MODE_LABEL", LiquifyMode.Push)]
    public LiquifyMode Mode => GetValue<LiquifyMode>();

    [Settings.Percent("LIQUIFY_STRENGTH_LABEL", 0.7f)]
    public float Strength => GetValue<float>();

    [Settings.Bool("LIQUIFY_SMOOTH_LABEL", false)]
    public bool Smooth => GetValue<bool>();

    [Settings.Bool("LIQUIFY_KEEP_EDGES_LABEL", false)]
    public bool KeepEdges => GetValue<bool>();

    [Settings.Size("LIQUIFY_EDGE_WIDTH_LABEL", 2d)]
    public double EdgeWidth => GetValue<double>();

    public LiquifyToolViewModel()
    {
        ActionDisplay = new LocalizedString("LIQUIFY_TOOL_ACTION_DISPLAY");

        Toolbar.GetSetting(nameof(BrushToolbar.Brush))!.IsExposed = false;
        Toolbar.GetSetting(nameof(BrushToolbar.StabilizationMode))!.IsExposed = false;
        ShowSettingWhen(nameof(EdgeWidth), () => KeepEdges, nameof(KeepEdges));
    }

    protected override Toolbar CreateToolbar()
    {
        return ToolbarFactory.Create<LiquifyToolViewModel, BrushToolbar>(this);
    }

    public override void UseTool(VecD pos)
    {
        EnsureBrushShapeSettings();
        ViewModelMain.Current?.DocumentManagerSubViewModel.ActiveDocument?.Tools.UseLiquifyTool();
    }
}
