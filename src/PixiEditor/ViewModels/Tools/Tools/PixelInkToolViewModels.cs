using Avalonia.Input;
using Drawie.Numerics;
using PixiEditor.Models.Commands.Attributes.Commands;
using PixiEditor.Models.Handlers.Tools;
using PixiEditor.UI.Common.Localization;
using PixiEditor.ViewModels.Tools.ToolSettings.Toolbars;

namespace PixiEditor.ViewModels.Tools.Tools;

/// <summary>Paints the main colour through a Bayer pattern, for hand-placed dithered shading.</summary>
[Command.Tool(Key = Key.Y)]
internal class DitherPenToolViewModel : BrushBasedToolViewModel, IPixelInkToolHandler
{
    public override string ToolNameLocalizationKey => "DITHER_PEN_TOOL";
    public override LocalizedString Tooltip => new("DITHER_PEN_TOOL_TOOLTIP", Shortcut);
    public override string DefaultIcon => PixiPerfectIcons.Grid;
    public override bool IsErasable => false;
    public bool UsesPaletteRamp => false;

    [Settings.Percent("DITHER_DENSITY_LABEL", 0.5f)]
    public float Density => GetValue<float>();

    public DitherPenToolViewModel()
    {
        ActionDisplay = new LocalizedString("DITHER_PEN_TOOL_ACTION_DISPLAY");
        Toolbar.GetSetting(nameof(BrushToolbar.Brush))!.IsExposed = false;
        Toolbar.GetSetting(nameof(BrushToolbar.StabilizationMode))!.IsExposed = false;
    }

    protected override Toolbar CreateToolbar() => ToolbarFactory.Create<DitherPenToolViewModel, BrushToolbar>(this);

    public override void UseTool(VecD pos)
    {
        EnsureBrushShapeSettings();
        ViewModelMain.Current?.DocumentManagerSubViewModel.ActiveDocument?.Tools.UsePixelInkTool(this);
    }
}

/// <summary>
/// Moves pixels along the palette as it passes over them, shading with the colours already chosen.
/// </summary>
[Command.Tool(Key = Key.I)]
internal class RampShadeToolViewModel : BrushBasedToolViewModel, IPixelInkToolHandler
{
    public override string ToolNameLocalizationKey => "RAMP_SHADE_TOOL";
    public override LocalizedString Tooltip => new("RAMP_SHADE_TOOL_TOOLTIP", Shortcut);
    public override string DefaultIcon => PixiPerfectIcons.Gradient;
    public override bool UsesColor => false;
    public override bool IsErasable => false;
    public bool UsesPaletteRamp => true;
    public float Density => 1f;

    public RampShadeToolViewModel()
    {
        ActionDisplay = new LocalizedString("RAMP_SHADE_TOOL_ACTION_DISPLAY");
        Toolbar.GetSetting(nameof(BrushToolbar.Brush))!.IsExposed = false;
        Toolbar.GetSetting(nameof(BrushToolbar.StabilizationMode))!.IsExposed = false;
    }

    protected override Toolbar CreateToolbar() => ToolbarFactory.Create<RampShadeToolViewModel, BrushToolbar>(this);

    public override void UseTool(VecD pos)
    {
        EnsureBrushShapeSettings();
        ViewModelMain.Current?.DocumentManagerSubViewModel.ActiveDocument?.Tools.UsePixelInkTool(this);
    }
}
