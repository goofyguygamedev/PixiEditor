using Avalonia.Headless.XUnit;
using PixiEditor.ChangeableDocument.Changeables.Brushes;
using PixiEditor.ViewModels;
using PixiEditor.ViewModels.Tools.ToolSettings.Settings;
using PixiEditor.ViewModels.Tools.ToolSettings.Toolbars;
using PixiEditor.ViewModels.Tools.Tools;
using DrawingApiBlendMode = Drawie.Backend.Core.Surfaces.BlendMode;

namespace PixiEditor.Tests;

/// <summary>
/// Covers the pen's blend mode: which of the engine's two blend points each mode lands on, and
/// that the setting is actually on the toolbar rather than only in the code.
/// </summary>
public class BrushBlendModeTests : FullPixiEditorTest
{
    private const DrawingApiBlendMode BrushDefault = DrawingApiBlendMode.SrcOver;

    [AvaloniaTheory]
    [InlineData(BrushBlendMode.Multiply, DrawingApiBlendMode.Multiply)]
    [InlineData(BrushBlendMode.Screen, DrawingApiBlendMode.Screen)]
    [InlineData(BrushBlendMode.Add, DrawingApiBlendMode.Plus)]
    [InlineData(BrushBlendMode.Darken, DrawingApiBlendMode.Darken)]
    [InlineData(BrushBlendMode.Lighten, DrawingApiBlendMode.Lighten)]
    [InlineData(BrushBlendMode.Difference, DrawingApiBlendMode.Difference)]
    public void TheseModesJoinTheWholeStrokeToTheLayerAtOnce(BrushBlendMode mode, DrawingApiBlendMode expected)
    {
        // At the image level, so crossing your own stroke does not blend twice.
        Assert.Equal(expected, mode.ToImageBlendMode(BrushDefault));
        Assert.Equal(BrushDefault, mode.ToStampBlendMode(BrushDefault));
    }

    [AvaloniaFact]
    public void OverwriteGoesOnTheStampsInstead()
    {
        // Src at the image level means something else entirely to the engine - "this stroke is the
        // whole layer" - so overwrite has to be done per dab. Replacing twice is still replacing.
        Assert.Equal(DrawingApiBlendMode.Src, BrushBlendMode.Overwrite.ToStampBlendMode(BrushDefault));
        Assert.Equal(BrushDefault, BrushBlendMode.Overwrite.ToImageBlendMode(BrushDefault));
    }

    [AvaloniaFact]
    public void NormalLeavesTheBrushsOwnGraphAlone()
    {
        Assert.Equal(BrushDefault, BrushBlendMode.Normal.ToImageBlendMode(BrushDefault));
        Assert.Equal(BrushDefault, BrushBlendMode.Normal.ToStampBlendMode(BrushDefault));
    }

    [AvaloniaFact]
    public void ThePenHasTheBlendModeInItsToolbar()
    {
        PenToolViewModel pen = ViewModelMain.Current!.ToolsSubViewModel.AllToolSets
            .SelectMany(set => set.Tools).OfType<PenToolViewModel>().First();

        var setting = ((BrushToolbar)pen.Toolbar).GetSetting<EnumSettingViewModel<BrushBlendMode>>("BlendMode");

        Assert.NotNull(setting);
        Assert.True(setting.IsExposed, "no use having it if it is not on the bar");
        Assert.Equal(BrushBlendMode.Normal, setting.Value);
    }

    [AvaloniaFact]
    public void TheBrushDataHandedToTheEngineCarriesIt()
    {
        PenToolViewModel pen = ViewModelMain.Current!.ToolsSubViewModel.AllToolSets
            .SelectMany(set => set.Tools).OfType<PenToolViewModel>().First();
        BrushToolbar toolbar = (BrushToolbar)pen.Toolbar;

        toolbar.BlendMode = BrushBlendMode.Multiply;

        Assert.Equal(BrushBlendMode.Multiply, toolbar.CreateBrushData().BlendMode);

        toolbar.BlendMode = BrushBlendMode.Normal;
    }
}
