using Avalonia.Input;
using Avalonia.Media;
using Drawie.Numerics;
using PixiEditor.ChangeableDocument.Changeables.Graph.Nodes;
using PixiEditor.ChangeableDocument.Enums;
using PixiEditor.ChangeableDocument.Helpers;
using PixiEditor.Models.Commands.Attributes.Commands;
using PixiEditor.Models.Handlers;
using PixiEditor.Models.Handlers.Tools;
using PixiEditor.Models.Input;
using PixiEditor.UI.Common.Localization;
using PixiEditor.ViewModels.Tools.ToolSettings.Settings;
using PixiEditor.ViewModels.Tools.ToolSettings.Toolbars;
using DrawieColor = Drawie.Backend.Core.ColorsImpl.Color;

namespace PixiEditor.ViewModels.Tools.Tools;

[Command.Tool(Key = Key.J)]
internal class ShadeToolViewModel : ToolViewModel, IShadeToolHandler
{
    public override string ToolNameLocalizationKey => "SHADE_TOOL";
    public override LocalizedString Tooltip => new("SHADE_TOOL_TOOLTIP", Shortcut);
    public override string DefaultIcon => PixiPerfectIcons.StarHalf;
    public override Type[]? SupportedLayerTypes { get; } = { typeof(IRasterLayerHandler) };
    public override Type LayerTypeToCreateOnEmptyUse { get; } = null;
    public override bool UsesColor => true;

    [Settings.Float("SHADE_ANGLE_LABEL", 315f, 0f, 360f)]
    public float Angle => GetValue<float>();

    [Settings.Float("SHADE_SPREAD_LABEL", 90f, 0f, 360f)]
    public float Spread => GetValue<float>();

    [Settings.Size("SHADE_THICKNESS_LABEL", 3d)]
    public double Thickness => GetValue<double>();

    [Settings.Enum("SHADE_FALLOFF_LABEL", ShadeFalloff.Hard)]
    public ShadeFalloff Falloff => GetValue<ShadeFalloff>();

    [Settings.Size("SHADE_BANDS_LABEL", 2d)]
    public double Bands => GetValue<double>();

    [Settings.Enum("SHADE_BLEND_LABEL", BlendMode.Multiply)]
    public BlendMode ShadeBlendMode => GetValue<BlendMode>();

    [Settings.Percent("SHADE_STRENGTH_LABEL", 0.5f)]
    public float Strength => GetValue<float>();

    [Settings.Percent("TOLERANCE_LABEL")]
    public float Tolerance => GetValue<float>();

    [Settings.Bool("SHADE_USE_MAIN_COLOR_LABEL", true)]
    public bool UseMainColor => GetValue<bool>();

    [Settings.Color("SHADE_COLOR_LABEL")]
    public IBrush ShadeColor => GetValue<IBrush>();

    public DrawieColor CustomColor => ShadeColor is ISolidColorBrush solid
        ? new DrawieColor(solid.Color.R, solid.Color.G, solid.Color.B, solid.Color.A)
        : new DrawieColor(0, 0, 0);

    public ShadeToolViewModel()
    {
        Toolbar = ToolbarFactory.Create(this);
        ActionDisplay = new LocalizedString("SHADE_TOOL_ACTION_DISPLAY");
        Cursor = Cursors.PreciseCursor;

        ShowSettingWhen(nameof(Bands), () => Falloff == ShadeFalloff.Bands, nameof(Falloff));
        ShowSettingWhen(nameof(ShadeColor), () => !UseMainColor, nameof(UseMainColor));
    }

    public void SetAngle(float angle)
    {
        Toolbar.GetSetting(nameof(Angle))!.Value = angle;
    }

    public override void UseTool(VecD pos)
    {
        ViewModelMain.Current?.DocumentManagerSubViewModel.ActiveDocument?.Tools.UseShadeTool();
    }
}
