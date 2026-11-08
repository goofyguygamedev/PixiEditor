using Avalonia.Input;
using PixiEditor.ChangeableDocument.Changeables.Graph.Nodes;
using PixiEditor.Models.Commands.Attributes.Commands;
using PixiEditor.Models.Handlers;
using PixiEditor.Models.Handlers.Tools;
using Drawie.Numerics;
using PixiEditor.UI.Common.Localization;
using PixiEditor.ViewModels.Tools.ToolSettings.Toolbars;

namespace PixiEditor.ViewModels.Tools.Tools;

[Command.Tool(Key = Key.L, CommonToolType = "Line")]
internal class RasterLineToolViewModel : ShapeTool, ILineToolHandler
{
    private string defaultActionDisplay = "LINE_TOOL_ACTION_DISPLAY_DEFAULT";

    public override string ToolNameLocalizationKey => "LINE_TOOL";
    public override LocalizedString Tooltip => new LocalizedString("LINE_TOOL_TOOLTIP", Shortcut);

    public override Type[]? SupportedLayerTypes { get; } = { typeof(IRasterLayerHandler) };
    public override string DefaultIcon => PixiPerfectIcons.LowresLine;

    [Settings.Inherited] public double ToolSize => GetValue<double>();

    /// <summary>
    /// Number of draggable points between the ends. 0 keeps the plain two-point line.
    /// </summary>
    /// <remarks>
    /// Defaults to 2 to match Paint.NET's line tool, whose four points (two ends plus two middle
    /// handles) are what makes a line bendable without having to go and turn anything on first.
    /// They are seeded on the line itself, so a line drawn and left alone is still straight.
    /// </remarks>
    [Settings.Size("LINE_CURVE_POINTS", 2, Min = 0)]
    public double CurvePoints => GetValue<double>();

    /// <summary>0 is a smooth curve through the points, 1 makes every point a sharp corner.</summary>
    [Settings.Percent("LINE_CURVE_SHARPNESS", 0f)]
    public float CurveSharpness => GetValue<float>();

    /// <summary>Constrains the line to pixel-art isometric angles at whole-step lengths.</summary>
    [Settings.Bool("LINE_ISOMETRIC_LOCK", false, ExposedByDefault = true)]
    public bool IsometricLock => GetValue<bool>();

    public int CurvePointCount =>
        (int)Math.Clamp(Math.Round(CurvePoints), 0, LineToolbarHelper.MaxCurvePoints);

    public double CurveSmoothness => 1d - Math.Clamp(CurveSharpness, 0f, 1f);

    public bool Snap { get; private set; }

    public override Type LayerTypeToCreateOnEmptyUse { get; } = typeof(ImageLayerNode);

    public RasterLineToolViewModel()
    {
        ActionDisplay = defaultActionDisplay;
        Toolbar = ToolbarFactory.Create<RasterLineToolViewModel, ShapeToolbar>(this);
        var strokeSetting = Toolbar.GetSetting(nameof(ShapeToolbar.ToolSize));
        if (strokeSetting != null)
        {
            strokeSetting.Value = 1d;
        }

        LineToolbarHelper.ConfigureCurveSettings(Toolbar);
    }

    public override void KeyChanged(bool ctrlIsDown, bool shiftIsDown, bool altIsDown, Key argsKey)
    {
        DrawFromCenter = ctrlIsDown;

        if (shiftIsDown)
        {
            ActionDisplay = "LINE_TOOL_ACTION_DISPLAY_SHIFT";
            Snap = true;
        }
        else
        {
            ActionDisplay = defaultActionDisplay;
            Snap = false;
        }
    }

    public override void UseTool(VecD pos)
    {
        ViewModelMain.Current?.DocumentManagerSubViewModel.ActiveDocument?.Tools.UseRasterLineTool();
    }

    protected override void OnSelected(bool restoring)
    {
        if (restoring) return;

        var document = ViewModelMain.Current?.DocumentManagerSubViewModel.ActiveDocument;
        document.Tools.UseRasterLineTool();
    }
}
