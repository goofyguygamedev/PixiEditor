namespace PixiEditor.ChangeableDocument.Helpers;

/// <summary>
/// A quick colour correction, as the layer panel's adjust window offers it.
/// </summary>
/// <param name="Hue">Degrees to turn the hue by, -180 to 180.</param>
/// <param name="Saturation">-1 drains all colour, 0 leaves it, 1 pushes it as far as it goes.</param>
/// <param name="Lightness">-1 goes to black, 0 leaves it, 1 goes to white.</param>
/// <param name="Contrast">-1 flattens everything to mid grey, 0 leaves it, 1 doubles the spread.</param>
public readonly record struct ColorAdjustments(double Hue, double Saturation, double Lightness, double Contrast);

/// <summary>
/// Colour correction on plain arrays, so it can be tested without a canvas and reused by anything
/// that holds straight-alpha RGBA.
/// </summary>
public static class PixelAdjust
{
    /// <summary>True when the settings would leave every pixel exactly as it is.</summary>
    public static bool IsNeutral(ColorAdjustments adjustments) =>
        Math.Abs(adjustments.Hue) < 0.001 && Math.Abs(adjustments.Saturation) < 0.001 &&
        Math.Abs(adjustments.Lightness) < 0.001 && Math.Abs(adjustments.Contrast) < 0.001;

    /// <summary>
    /// Straight-alpha RGBA in, straight-alpha RGBA out. Pixels the mask leaves out are copied
    /// across untouched, and alpha is never changed: an adjustment moves colour, not coverage.
    /// </summary>
    /// <param name="mask">One entry per pixel, or null for the whole image.</param>
    public static byte[] Apply(byte[] rgba, ColorAdjustments adjustments, bool[]? mask = null)
    {
        byte[] output = (byte[])rgba.Clone();
        if (IsNeutral(adjustments))
        {
            return output;
        }

        double hueTurn = adjustments.Hue / 360.0;
        double saturation = Math.Clamp(adjustments.Saturation, -1, 1);
        double lightness = Math.Clamp(adjustments.Lightness, -1, 1);
        double contrast = Math.Clamp(adjustments.Contrast, -1, 1);

        for (int pixel = 0, offset = 0; offset < rgba.Length; pixel++, offset += 4)
        {
            if (mask is not null && !mask[pixel])
            {
                continue;
            }

            // A fully transparent pixel has no colour anyone can see, and moving its hue would
            // only show up later if something made it opaque again.
            if (rgba[offset + 3] == 0)
            {
                continue;
            }

            (double hue, double sat, double light) =
                ToHsl(rgba[offset] / 255.0, rgba[offset + 1] / 255.0, rgba[offset + 2] / 255.0);

            hue = Wrap(hue + hueTurn);
            sat = Push(sat, saturation);
            light = Push(light, lightness);

            (double red, double green, double blue) = ToRgb(hue, sat, light);

            output[offset] = Byte(Contrast(red, contrast));
            output[offset + 1] = Byte(Contrast(green, contrast));
            output[offset + 2] = Byte(Contrast(blue, contrast));
        }

        return output;
    }

    /// <summary>
    /// Moves a 0-1 value by a -1 to 1 amount, so that 1 reaches the top and -1 reaches the bottom
    /// without ever overshooting. A plain multiply would clip everything bright to white at once.
    /// </summary>
    private static double Push(double value, double amount) =>
        amount >= 0 ? value + ((1 - value) * amount) : value * (1 + amount);

    private static double Contrast(double value, double amount) => ((value - 0.5) * (1 + amount)) + 0.5;

    private static double Wrap(double value) => value - Math.Floor(value);

    private static byte Byte(double value) => (byte)Math.Clamp(Math.Round(value * 255), 0, 255);

    private static (double Hue, double Saturation, double Lightness) ToHsl(double red, double green, double blue)
    {
        double max = Math.Max(red, Math.Max(green, blue));
        double min = Math.Min(red, Math.Min(green, blue));
        double lightness = (max + min) / 2;

        if (Math.Abs(max - min) < 1e-9)
        {
            return (0, 0, lightness);
        }

        double span = max - min;
        double saturation = lightness > 0.5 ? span / (2 - max - min) : span / (max + min);

        double hue;
        if (Math.Abs(max - red) < 1e-9)
        {
            hue = ((green - blue) / span) + (green < blue ? 6 : 0);
        }
        else if (Math.Abs(max - green) < 1e-9)
        {
            hue = ((blue - red) / span) + 2;
        }
        else
        {
            hue = ((red - green) / span) + 4;
        }

        return (hue / 6, saturation, lightness);
    }

    private static (double Red, double Green, double Blue) ToRgb(double hue, double saturation, double lightness)
    {
        if (saturation <= 0)
        {
            return (lightness, lightness, lightness);
        }

        double high = lightness < 0.5 ? lightness * (1 + saturation) : lightness + saturation - (lightness * saturation);
        double low = (2 * lightness) - high;

        return (Channel(low, high, hue + (1.0 / 3)), Channel(low, high, hue), Channel(low, high, hue - (1.0 / 3)));
    }

    private static double Channel(double low, double high, double hue)
    {
        hue = Wrap(hue);
        if (hue < 1.0 / 6)
        {
            return low + ((high - low) * 6 * hue);
        }

        if (hue < 1.0 / 2)
        {
            return high;
        }

        if (hue < 2.0 / 3)
        {
            return low + ((high - low) * ((2.0 / 3) - hue) * 6);
        }

        return low;
    }
}
