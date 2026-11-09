using Avalonia.Input;
using Avalonia.Media;
using Drawie.Numerics;
using PixiEditor.ChangeableDocument.Enums;
using PixiEditor.ChangeableDocument.Helpers;
using PixiEditor.Models.Commands.Attributes.Commands;
using PixiEditor.Models.Handlers;
using PixiEditor.Models.Handlers.Tools;
using PixiEditor.Models.Input;
using PixiEditor.UI.Common.Localization;
using PixiEditor.ViewModels.Tools.ToolSettings.Toolbars;
using DrawieColor = Drawie.Backend.Core.ColorsImpl.Color;

namespace PixiEditor.ViewModels.Tools.Tools;

/// <summary>Outlines a shape, a colour area, a selection or a whole layer with one click.</summary>
[Command.Tool(Key = Key.F)]
internal class OutlineToolViewModel : ToolViewModel, IOutlineToolHandler
{
    public override string ToolNameLocalizationKey => "OUTLINE_TOOL";
    public override LocalizedString Tooltip => new("OUTLINE_TOOL_TOOLTIP", Shortcut);
    public override string DefaultIcon => PixiPerfectIcons.Outline;
    public override Type[]? SupportedLayerTypes { get; } = { typeof(IRasterLayerHandler) };
    public override Type LayerTypeToCreateOnEmptyUse { get; } = null;
    public override bool UsesColor => true;

    [Settings.Size("SHADE_THICKNESS_LABEL", 1d)]
    public double Thickness => GetValue<double>();

    [Settings.Enum("OUTLINE_POSITION_LABEL", OutlinePosition.Outside)]
    public OutlinePosition Position => GetValue<OutlinePosition>();

    [Settings.Enum("OUTLINE_CORNERS_LABEL", OutlineCorners.Round)]
    public OutlineCorners Corners => GetValue<OutlineCorners>();

    [Settings.Percent("OUTLINE_SOFTNESS_LABEL", 0f)]
    public float Softness => GetValue<float>();

    [Settings.Enum("SHADE_FALLOFF_LABEL", ShadeFalloff.Smooth)]
    public ShadeFalloff Falloff => GetValue<ShadeFalloff>();

    [Settings.Size("SHADE_BANDS_LABEL", 2d)]
    public double Bands => GetValue<double>();

    [Settings.Enum("DITHER_MODE_LABEL", OrderedDither.Bayer4)]
    public OrderedDither Pattern => GetValue<OrderedDither>();

    [Settings.Enum("OUTLINE_TARGET_LABEL", OutlineTarget.Shape)]
    public OutlineTarget Target => GetValue<OutlineTarget>();

    [Settings.Percent("TOLERANCE_LABEL")]
    public float Tolerance => GetValue<float>();

    [Settings.Enum("SHADE_BLEND_LABEL", BlendMode.Normal)]
    public BlendMode OutlineBlendMode => GetValue<BlendMode>();

    [Settings.Percent("SHADE_STRENGTH_LABEL", 1f)]
    public float Strength => GetValue<float>();

    [Settings.Bool("SHADE_USE_MAIN_COLOR_LABEL", true)]
    public bool UseMainColor => GetValue<bool>();

    [Settings.Color("OUTLINE_COLOR_LABEL")]
    public IBrush OutlineColor => GetValue<IBrush>();

    public DrawieColor CustomColor => OutlineColor is ISolidColorBrush solid
        ? new DrawieColor(solid.Color.R, solid.Color.G, solid.Color.B, solid.Color.A)
        : new DrawieColor(0, 0, 0);

    public OutlineToolViewModel()
    {
        Toolbar = ToolbarFactory.Create(this);
        ActionDisplay = new LocalizedString("OUTLINE_TOOL_ACTION_DISPLAY");
        Cursor = Cursors.PreciseCursor;

        ShowSettingWhen(nameof(Bands), () => Falloff == ShadeFalloff.Bands, nameof(Falloff));
        ShowSettingWhen(nameof(Pattern), () => Falloff == ShadeFalloff.Dithered, nameof(Falloff));
        ShowSettingWhen(nameof(Tolerance), () => Target == OutlineTarget.ColorArea, nameof(Target));
        ShowSettingWhen(nameof(OutlineColor), () => !UseMainColor, nameof(UseMainColor));
    }

    public override void UseTool(VecD pos)
    {
        ViewModelMain.Current?.DocumentManagerSubViewModel.ActiveDocument?.Tools.UseOutlineTool();
    }
}
