using Avalonia.Input;
using Drawie.Numerics;
using PixiEditor.Models.Commands.Attributes.Commands;
using PixiEditor.Models.Handlers.Tools;
using PixiEditor.UI.Common.Localization;
using PixiEditor.ViewModels.Tools.ToolSettings.Toolbars;

namespace PixiEditor.ViewModels.Tools.Tools;

/// <summary>Alt-click picks where to copy from; painting then copies from there, pixel for pixel.</summary>
[Command.Tool(Key = Key.S)]
internal class CloneStampToolViewModel : BrushBasedToolViewModel, IPixelInkToolHandler
{
    private VecI? source;
    private VecI? cloneOffset;
    private bool altIsDown;

    public override string ToolNameLocalizationKey => "CLONE_STAMP_TOOL";
    public override LocalizedString Tooltip => new("CLONE_STAMP_TOOL_TOOLTIP", Shortcut);
    public override string DefaultIcon => PixiPerfectIcons.Layers;
    public override bool UsesColor => false;
    public override bool IsErasable => false;
    public override bool HandlesAltModifier => true;
    public bool UsesPaletteRamp => false;
    public bool IsCloneStamp => true;
    public float Density => 1f;

    public CloneStampToolViewModel()
    {
        ActionDisplay = new LocalizedString("CLONE_STAMP_TOOL_ACTION_DISPLAY");
        Toolbar.GetSetting(nameof(BrushToolbar.Brush))!.IsExposed = false;
        Toolbar.GetSetting(nameof(BrushToolbar.StabilizationMode))!.IsExposed = false;
    }

    protected override Toolbar CreateToolbar() => ToolbarFactory.Create<CloneStampToolViewModel, BrushToolbar>(this);

    public override void KeyChanged(bool ctrlIsDown, bool shiftIsDown, bool altIsDown, Key argsKey)
    {
        this.altIsDown = altIsDown;
    }

    /// <summary>
    /// How far from a painted pixel its colour is read. Fixed by the first stroke after a source is
    /// picked, so that later strokes carry on from the same place rather than starting over.
    /// </summary>
    public VecI? GetCloneOffset(VecI pressPosition)
    {
        if (source is null)
            return null;

        cloneOffset ??= source.Value - pressPosition;
        return cloneOffset;
    }

    public override void UseTool(VecD pos)
    {
        if (altIsDown)
        {
            source = (VecI)pos.Floor();
            cloneOffset = null;
            return;
        }

        EnsureBrushShapeSettings();
        ViewModelMain.Current?.DocumentManagerSubViewModel.ActiveDocument?.Tools.UsePixelInkTool(this);
    }
}
