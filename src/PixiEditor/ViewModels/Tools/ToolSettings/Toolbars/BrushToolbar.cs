using PixiEditor.ChangeableDocument.Changeables.Brushes;
using PixiEditor.Models.BrushEngine;
using PixiEditor.Models.Handlers.Toolbars;
using PixiEditor.ViewModels.BrushSystem;
using PixiEditor.ViewModels.Tools.ToolSettings.Settings;

namespace PixiEditor.ViewModels.Tools.ToolSettings.Toolbars;

internal class BrushToolbar : Toolbar, IBrushToolbar
{
    public bool AntiAliasing
    {
        get => GetSetting<BoolSettingViewModel>(nameof(AntiAliasing)).Value;
        set => GetSetting<BoolSettingViewModel>(nameof(AntiAliasing)).Value = value;
    }
    
    public double ToolSize
    {
        get => GetSetting<SizeSettingViewModel>(nameof(ToolSize)).Value;
        set => GetSetting<SizeSettingViewModel>(nameof(ToolSize)).Value = value;
    }

    public Brush Brush
    {
        get => GetSetting<BrushSettingViewModel>(nameof(Brush)).Value?.Brush;
        set => GetSetting<BrushSettingViewModel>(nameof(Brush)).Value =
            value != null ? new BrushViewModel(value) : null;
    }

    public BrushBlendMode BlendMode
    {
        get => GetSetting<EnumSettingViewModel<BrushBlendMode>>(nameof(BlendMode)).Value;
        set => GetSetting<EnumSettingViewModel<BrushBlendMode>>(nameof(BlendMode)).Value = value;
    }

    public double Stabilization
    {
        get => GetSetting<SizeSettingViewModel>(nameof(Stabilization)).Value;
        set => GetSetting<SizeSettingViewModel>(nameof(Stabilization)).Value = value;
    }

    public StabilizationMode StabilizationMode
    {
        get => GetSetting<EnumSettingViewModel<StabilizationMode>>(nameof(StabilizationMode)).Value;
        set => GetSetting<EnumSettingViewModel<StabilizationMode>>(nameof(StabilizationMode)).Value = value;
    }

    public BrushData CreateBrushData()
    {
        Brush? brush = Brush;
        if (brush == null)
        {
            // Still carries the blend mode: it is the artist's choice, not the brush's, and it
            // should survive a moment with no brush picked.
            return new BrushData { BlendMode = BlendMode };
        }

        var pipe = Brush.Document.ShareGraph();
        var data = new BrushData(pipe.TryAccessData(), Brush.OutputNodeId)
        {
            AntiAliasing = AntiAliasing, StrokeWidth = (float)ToolSize, BlendMode = BlendMode,
        };

        pipe.Dispose();
        return data;
    }

    private BrushData lastBrushData;

    public BrushData LastBrushData
    {
        get => lastBrushData;
        private set => SetProperty(ref lastBrushData, value);
    }

    public override void OnLoadedSettings()
    {
        OnPropertyChanged(nameof(ToolSize));
        OnPropertyChanged(nameof(Brush));
        OnPropertyChanged(nameof(AntiAliasing));
        OnPropertyChanged(nameof(Stabilization));
        OnPropertyChanged(nameof(StabilizationMode));
        LastBrushData = CreateBrushData();

        OnPropertyChanged(nameof(LastBrushData));
    }

    public BrushToolbar()
    {
        AddSetting(new BoolSettingViewModel(nameof(AntiAliasing), "ANTI_ALIASING_SETTING") { IsExposed = false });
        // Whole pixels only, and at least one of them: a brush cannot paint a tenth of a pixel,
        // and a size of zero paints nothing at all.
        var setting = new SizeSettingViewModel(nameof(ToolSize), "TOOL_SIZE_LABEL");
        setting.ValueChanged += (_, _) => OnPropertyChanged(nameof(ToolSize));
        AddSetting(setting);
        AddSetting(new EnumSettingViewModel<BrushBlendMode>(nameof(BlendMode), "BRUSH_BLEND_MODE_SETTING"));
        AddSetting(new BrushSettingViewModel(nameof(Brush), "BRUSH_SETTING")
        {
            IsExposed = true, IsLabelVisible = false
        });
        AddSetting(new EnumSettingViewModel<StabilizationMode>(nameof(StabilizationMode), "STABILIZATION_MODE_SETTING")
        {
            IsExposed = true, PickerType = ListSettingPickerType.IconButtons, IsLabelVisible = false
        });
        var stabilizationSetting =
            new SizeSettingViewModel(nameof(Stabilization), "STABILIZATION_SETTING", 10, min: 0, max: 128)
            {
                IsExposed = false, IsLabelVisible = false
            };

        AddSetting(stabilizationSetting);

        foreach (var aSetting in Settings)
        {
            if (aSetting.Name is "Brush" or "AntiAliasing" or "ToolSize" or "BlendMode")
            {
                aSetting.ValueChanged += SettingOnValueChanged;
            }

            if (aSetting.Name == "Stabilization")
            {
                aSetting.ValueChanged += (_, _) => OnPropertyChanged(nameof(Stabilization));
            }

            if (aSetting.Name == "StabilizationMode")
            {
                aSetting.ValueChanged += (_, _) =>
                {
                    stabilizationSetting.IsExposed = StabilizationMode != StabilizationMode.None;
                    OnPropertyChanged(nameof(StabilizationMode));
                };
            }
        }
    }

    private void SettingOnValueChanged(object? sender, SettingValueChangedEventArgs<object> e)
    {
        LastBrushData = CreateBrushData();
    }
}
