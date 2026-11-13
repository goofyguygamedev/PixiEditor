using System.Globalization;

namespace PixiEditor.Helpers.Converters;

/// <summary>
/// Takes the converter parameter off a double, floored at zero. Made for sizes that have to leave
/// room for something beside them, so an input that is not a number yet - an unset binding, or a
/// width before the first layout pass - means no constraint rather than a size of zero.
/// </summary>
internal class SubtractDoubleConverter : SingleInstanceConverter<SubtractDoubleConverter>
{
    public override object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not double from || double.IsNaN(from) || double.IsInfinity(from))
            return double.PositiveInfinity;

        double amount = parameter is null ? 0 : System.Convert.ToDouble(parameter, CultureInfo.InvariantCulture);
        return Math.Max(0, from - amount);
    }
}
