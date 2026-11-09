using Avalonia.Input;
using Drawie.Numerics;
using PixiEditor.ChangeableDocument.Changeables.Graph.Nodes;
using PixiEditor.ChangeableDocument.Helpers;
using PixiEditor.Models.Commands.Attributes.Commands;
using PixiEditor.Models.Handlers;
using PixiEditor.Models.Handlers.Tools;
using PixiEditor.UI.Common.Localization;
using PixiEditor.ViewModels.Tools.ToolSettings.Toolbars;

namespace PixiEditor.ViewModels.Tools.Tools;

/// <summary>
/// Draws a rectangle, ellipse, star, heart or any of the other shapes, chosen on the toolbar. It
/// replaces the separate rectangle and ellipse tools, which were one shape each.
/// </summary>
[Command.Tool(Key = Key.R, CommonToolType = "Shape")]
internal class RasterShapeToolViewModel : ShapeTool, IRasterShapeToolHandler
{
    private const string DefaultActionDisplay = "SHAPE_TOOL_ACTION_DISPLAY_DEFAULT";

    public override string ToolNameLocalizationKey => "SHAPE_TOOL";
    public override LocalizedString Tooltip => new("SHAPE_TOOL_TOOLTIP", Shortcut);
    public override string DefaultIcon => PixiPerfectIcons.Star;
    public override Type[]? SupportedLayerTypes { get; } = { typeof(IRasterLayerHandler) };
    public override Type LayerTypeToCreateOnEmptyUse { get; } = typeof(ImageLayerNode);

    [Settings.Enum("SHAPE_KIND_LABEL", ShapeKind.Rectangle)]
    public ShapeKind Shape => GetValue<ShapeKind>();

    [Settings.Size("SHAPE_POINTS_LABEL", 5d, Min = 3)]
    public double Points => GetValue<double>();

    [Settings.Percent("SHAPE_INNER_RATIO_LABEL", 0.5f)]
    public double InnerRatio => GetValue<float>();

    [Settings.Percent("RADIUS", 0f, Min = 0)]
    public float CornerRadius
    {
        get => GetValue<float>();
        set => SetValue(value);
    }

    public RasterShapeToolViewModel()
    {
        ActionDisplay = DefaultActionDisplay;
        Toolbar = ToolbarFactory.Create<RasterShapeToolViewModel, FillableShapeToolbar>(this);

        ShowSettingWhen(nameof(Points), () => ShapeGeometry.UsesPoints(Shape), nameof(Shape));
        ShowSettingWhen(nameof(InnerRatio), () => Shape == ShapeKind.Star, nameof(Shape));
        ShowSettingWhen(nameof(CornerRadius), () => Shape == ShapeKind.Rectangle, nameof(Shape));
    }

    public override void KeyChanged(bool ctrlIsDown, bool shiftIsDown, bool altIsDown, Key argsKey)
    {
        DrawFromCenter = ctrlIsDown;
        DrawEven = shiftIsDown;
        ActionDisplay = shiftIsDown ? "SHAPE_TOOL_ACTION_DISPLAY_SHIFT" : DefaultActionDisplay;
    }

    public override void UseTool(VecD pos)
    {
        ViewModelMain.Current?.DocumentManagerSubViewModel.ActiveDocument?.Tools.UseRasterShapeTool();
    }

    protected override void OnSelected(bool restoring)
    {
        if (restoring) return;

        ViewModelMain.Current?.DocumentManagerSubViewModel.ActiveDocument?.Tools.UseRasterShapeTool();
    }
}
