using Avalonia.Headless.XUnit;
using PixiEditor.UI.Common.Controls;
using PixiEditor.ViewModels;
using PixiEditor.ViewModels.Tools.ToolSettings.Settings;
using PixiEditor.ViewModels.Tools.ToolSettings.Toolbars;
using PixiEditor.ViewModels.Tools.Tools;

namespace PixiEditor.Tests;

/// <summary>
/// Covers what a tool size is allowed to be - whole pixels, never below the tool's minimum - and
/// the two ways the pen's size used to stop responding: a scroll that threw before it could apply,
/// and pixel perfect pinning the value while leaving the field looking editable.
/// </summary>
public class ToolSizeTests : FullPixiEditorTest
{
    [AvaloniaFact]
    public void ASizeIsAlwaysAWholeNumberOfPixels()
    {
        SizeSettingViewModel size = new("Test");

        size.Value = 3.4;
        Assert.Equal(3, size.Value);

        size.Value = 3.5;
        Assert.Equal(4, size.Value);
    }

    [AvaloniaFact]
    public void ASizeNeverDropsBelowItsMinimum()
    {
        SizeSettingViewModel size = new("Test");

        size.Value = 0.1;
        Assert.Equal(1, size.Value);

        size.Value = 0;
        Assert.Equal(1, size.Value);

        size.Value = -8;
        Assert.Equal(1, size.Value);
    }

    [AvaloniaFact]
    public void ASizeThatIsAllowedToBeZeroStillCanBe()
    {
        // Shape stroke width: zero is how a shape is drawn with no outline at all.
        SizeSettingViewModel stroke = new("Stroke", defaultValue: 0, min: 0);

        Assert.Equal(0, stroke.Value);

        stroke.Value = 2.6;
        Assert.Equal(3, stroke.Value);
    }

    [AvaloniaFact]
    public void TheDefaultValueIsNotClampedAwayWhileTheSettingIsBeingBuilt()
    {
        // The bounds are fields set in the same constructor, so order matters: with them still at
        // zero when the default lands, every default came back out as zero.
        SizeSettingViewModel size = new("Test", defaultValue: 16, min: 1, max: 64);

        Assert.Equal(16, size.Value);
    }

    [AvaloniaFact]
    public void AnUnboundedMaxComesBackOutOfTheSizeFieldUnchanged()
    {
        // It used to come back as int.MinValue, which made the field's own clamp throw and left
        // scrolling over it doing nothing.
        SizeInput input = new() { MaxSize = double.PositiveInfinity, MinSize = 1 };

        Assert.True(input.MaxSize > input.MinSize, $"max came back as {input.MaxSize}");
    }

    [AvaloniaFact]
    public void TheBrushSizeIsWholePixelsStartingAtOne()
    {
        SizeSettingViewModel size = PenSize();

        Assert.Equal(0, size.DecimalPlaces);
        Assert.Equal(1, size.Min);
    }

    [AvaloniaFact]
    public void PixelPerfectTakesTheSizeToOneWithoutLockingIt()
    {
        PenToolViewModel pen = Pen();
        SizeSettingViewModel size = PenSize();
        BoolSettingViewModel pixelPerfect =
            ((BrushToolbar)pen.Toolbar).GetSetting<BoolSettingViewModel>("PixelPerfectEnabled");

        size.Value = 8;
        pixelPerfect.Value = true;

        Assert.Equal(1, size.Value);
        Assert.True(size.IsEnabled, "the size field has to stay usable, or there is no way back");

        // The whole point: a size written while pixel perfect is on has to take effect. It used to
        // be swallowed, which is what made the pen look stuck at one pixel for good.
        size.Value = 6;

        Assert.Equal(6, size.Value);
        Assert.False(pixelPerfect.Value);

        pixelPerfect.Value = false;
    }

    private static PenToolViewModel Pen() =>
        ViewModelMain.Current!.ToolsSubViewModel.AllToolSets
            .SelectMany(set => set.Tools).OfType<PenToolViewModel>().First();

    private static SizeSettingViewModel PenSize() =>
        ((BrushToolbar)Pen().Toolbar).GetSetting<SizeSettingViewModel>("ToolSize");
}
