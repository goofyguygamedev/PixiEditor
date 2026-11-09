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

/// <summary>Drags out a gradient, by default from the primary to the secondary colour.</summary>
[Command.Tool(Key = Key.D)]
internal class GradientFillToolViewModel : ToolViewModel, IGradientFillToolHandler
{
    public override string ToolNameLocalizationKey => "GRADIENT_FILL_TOOL";
    public override LocalizedString Tooltip => new("GRADIENT_FILL_TOOL_TOOLTIP", Shortcut);
    public override string DefaultIcon => PixiPerfectIcons.Gradient;
    public override Type[]? SupportedLayerTypes { get; } = { typeof(IRasterLayerHandler) };
    public override Type LayerTypeToCreateOnEmptyUse { get; } = null;
    public override bool UsesColor => true;

    [Settings.Enum("GRADIENT_SHAPE_LABEL", GradientShape.Linear)]
    public GradientShape Shape => GetValue<GradientShape>();

    [Settings.Enum("SHADE_FALLOFF_LABEL", ShadeFalloff.Dithered)]
    public ShadeFalloff Falloff => GetValue<ShadeFalloff>();

    [Settings.Size("GRADIENT_STEPS_LABEL", 2d, Min = 2)]
    public double Steps => GetValue<double>();

    [Settings.Enum("DITHER_MODE_LABEL", OrderedDither.Bayer4)]
    public OrderedDither Pattern => GetValue<OrderedDither>();

    [Settings.Bool("GRADIENT_REVERSE_LABEL", false)]
    public bool Reverse => GetValue<bool>();

    [Settings.Bool("GRADIENT_TRANSPARENT_LABEL", false)]
    public bool FadeToTransparent => GetValue<bool>();

    [Settings.Enum("SHADE_BLEND_LABEL", BlendMode.Normal)]
    public BlendMode GradientBlendMode => GetValue<BlendMode>();

    [Settings.Percent("SHADE_STRENGTH_LABEL", 1f)]
    public float Strength => GetValue<float>();

    [Settings.Enum("GRADIENT_AREA_LABEL", GradientArea.Layer)]
    public GradientArea Area => GetValue<GradientArea>();

    [Settings.Percent("TOLERANCE_LABEL")]
    public float Tolerance => GetValue<float>();

    [Settings.Bool("GRADIENT_USE_MAIN_COLORS_LABEL", true)]
    public bool UseMainColors => GetValue<bool>();

    [Settings.Color("GRADIENT_FROM_LABEL")]
    public IBrush FromColor => GetValue<IBrush>();

    [Settings.Color("GRADIENT_TO_LABEL")]
    public IBrush ToColor => GetValue<IBrush>();

    public DrawieColor CustomFrom => ToDrawieColor(FromColor);

    public DrawieColor CustomTo => ToDrawieColor(ToColor);

    public GradientFillToolViewModel()
    {
        Toolbar = ToolbarFactory.Create(this);
        ActionDisplay = new LocalizedString("GRADIENT_FILL_TOOL_ACTION_DISPLAY");
        Cursor = Cursors.PreciseCursor;

        ShowSettingWhen(nameof(Steps), () => Falloff is ShadeFalloff.Bands or ShadeFalloff.Dithered, nameof(Falloff));
        ShowSettingWhen(nameof(Pattern), () => Falloff == ShadeFalloff.Dithered, nameof(Falloff));
        ShowSettingWhen(nameof(Tolerance), () => Area == GradientArea.ColorArea, nameof(Area));
        ShowSettingWhen(nameof(FromColor), () => !UseMainColors, nameof(UseMainColors));
        ShowSettingWhen(nameof(ToColor), () => !UseMainColors && !FadeToTransparent, nameof(UseMainColors), nameof(FadeToTransparent));
    }

    public override void UseTool(VecD pos)
    {
        ViewModelMain.Current?.DocumentManagerSubViewModel.ActiveDocument?.Tools.UseGradientFillTool();
    }

    private static DrawieColor ToDrawieColor(IBrush brush) => brush is ISolidColorBrush solid
        ? new DrawieColor(solid.Color.R, solid.Color.G, solid.Color.B, solid.Color.A)
        : new DrawieColor(0, 0, 0);
}
