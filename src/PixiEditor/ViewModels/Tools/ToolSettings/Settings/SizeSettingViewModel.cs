namespace PixiEditor.ViewModels.Tools.ToolSettings.Settings;

internal sealed class SizeSettingViewModel : Setting<double>
{
    private bool isEnabled = true;
    private double min;
    private double max;
    private int decimalPlaces;
    private string unit = "px";
    
    public SizeSettingViewModel(string name, string label = null, double defaultValue = 1, double min = 1, double max = double.PositiveInfinity,
        int decimalPlaces = 0, string unit = "px")
        : base(name)
    {
        Label = label;

        // The bounds have to be in place before the default value is set: the value is held to
        // them on the way out, and with the fields still at zero the default would be clamped away.
        this.min = min;
        this.max = max;
        this.decimalPlaces = decimalPlaces;
        this.unit = unit;

        Value = defaultValue;
    }

    public bool IsEnabled
    {
        get => isEnabled;
        set
        {
            SetProperty(ref isEnabled, value);
        }
    }

    public double Min
    {
        get => min;
        set
        {
            SetProperty(ref min, value);
        }
    }

    public double Max
    {
        get => max;
        set
        {
            SetProperty(ref max, value);
        }
    }

    public int DecimalPlaces
    {
        get => decimalPlaces;
        set
        {
            SetProperty(ref decimalPlaces, value);
        }
    }

    public string Unit
    {
        get => unit;
        set
        {
            SetProperty(ref unit, value);
        }
    }

    /// <summary>
    /// Holds every value this setting reports to Min..Max and to the declared number of decimals.
    /// The input control already does this to what is typed, but a value can also arrive from a
    /// tool set default, an overwrite or a file saved before the bounds were what they are now, and
    /// those used to come straight back out - which is how a brush ended up at a size of 0.4.
    /// </summary>
    protected override object AdjustValue(object value)
    {
        double size;
        switch (value)
        {
            case double d: size = d; break;
            case float f: size = f; break;
            case int i: size = i; break;
            default: return value;
        }

        if (double.IsNaN(size))
            return Min;

        if (Max >= Min)
            size = Math.Clamp(size, Min, Max);

        if (double.IsFinite(size))
            size = Math.Round(size, Math.Clamp(DecimalPlaces, 0, 15), MidpointRounding.AwayFromZero);

        return size;
    }
}
