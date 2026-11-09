using Drawie.Backend.Core.ColorsImpl;
using Drawie.Numerics;

namespace PixiEditor.ChangeableDocument.Helpers;

/// <summary>The form a gradient takes between its two points.</summary>
public enum GradientShape
{
    Linear,

    /// <summary>Runs out from the start point in both directions along the line.</summary>
    Reflected,
    Radial,
    Diamond,

    /// <summary>Sweeps once around the start point.</summary>
    Angular
}

/// <summary>Which pixels a gradient paints, before any selection is taken into account.</summary>
public enum GradientArea
{
    Layer,

    /// <summary>Only pixels that already hold something, so a sprite keeps its silhouette.</summary>
    FilledPixels,

    /// <summary>The connected area of like colours at the start point.</summary>
    ColorArea
}

/// <summary>The dither patterns that can be evaluated one pixel at a time.</summary>
public enum OrderedDither
{
    Checker,
    Bayer2,
    Bayer4,
    Bayer8
}

/// <summary>How a gradient looks, independent of where it is placed.</summary>
/// <param name="Falloff">How the colours step from one end to the other.</param>
/// <param name="Steps">Colours from end to end, for bands and dithering. 2 means only the end colours.</param>
/// <param name="Pattern">The dither pattern, for <see cref="ShadeFalloff.Dithered"/>.</param>
/// <param name="Reverse">Swaps which colour is at which end.</param>
public readonly record struct GradientStyle(
    GradientShape Shape,
    ShadeFalloff Falloff,
    int Steps,
    OrderedDither Pattern,
    bool Reverse);

/// <summary>Computes gradients on plain arrays, so the behaviour can be tested without a canvas.</summary>
public static class PixelGradient
{
    /// <summary>The threshold, between 0 and 1, of a dither pattern at a pixel.</summary>
    public static float Threshold(OrderedDither pattern, int x, int y)
    {
        DitherMode mode = pattern switch
        {
            OrderedDither.Checker => DitherMode.Checker,
            OrderedDither.Bayer2 => DitherMode.Bayer2,
            OrderedDither.Bayer8 => DitherMode.Bayer8,
            _ => DitherMode.Bayer4
        };

        return Ditherer.Threshold(mode, x, y);
    }

    /// <summary>How far along a gradient a point is, from 0 to 1. The line must have some length.</summary>
    public static double Position(GradientShape shape, VecD start, VecD end, double x, double y)
    {
        VecD line = end - start;
        double lengthSquared = line.LengthSquared;
        double relativeX = x - start.X;
        double relativeY = y - start.Y;

        // Measured in units of the line, along it and across it.
        double along = ((relativeX * line.X) + (relativeY * line.Y)) / lengthSquared;
        double across = ((relativeY * line.X) - (relativeX * line.Y)) / lengthSquared;

        double position = shape switch
        {
            GradientShape.Reflected => Math.Abs(along),
            GradientShape.Radial => Math.Sqrt((along * along) + (across * across)),
            GradientShape.Diamond => Math.Abs(along) + Math.Abs(across),
            GradientShape.Angular => WrapTurn(Math.Atan2(across, along) / (2 * Math.PI)),
            _ => along
        };

        return Math.Clamp(position, 0, 1);
    }

    /// <summary>Applies the falloff to a position, which is where banding and dithering happen.</summary>
    public static double Quantize(double position, GradientStyle style, int x, int y)
    {
        int steps = Math.Max(2, style.Steps);
        switch (style.Falloff)
        {
            case ShadeFalloff.Hard:
                return position < 0.5 ? 0 : 1;
            case ShadeFalloff.Bands:
                return Math.Min(Math.Floor(position * steps), steps - 1) / (steps - 1);
            case ShadeFalloff.Dithered:
                double scaled = position * (steps - 1);
                double lower = Math.Floor(scaled);
                double level = scaled - lower > Threshold(style.Pattern, x, y) ? lower + 1 : lower;
                return Math.Min(level, steps - 1) / (steps - 1);
            default:
                return position;
        }
    }

    /// <summary>The gradient as straight-alpha RGBA, or null if the line has no length.</summary>
    /// <param name="mask">Which pixels get painted. Null means all of them.</param>
    /// <param name="strength">0 to 1, scales the alpha of everything painted.</param>
    public static byte[]? Render(int width, int height, VecD start, VecD end, GradientStyle style, Color from, Color to,
        double strength, bool[]? mask)
    {
        if ((end - start).LengthSquared <= 0)
        {
            return null;
        }

        byte[] output = new byte[width * height * 4];

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int index = (y * width) + x;
                if (mask is not null && !mask[index])
                {
                    continue;
                }

                double position = Position(style.Shape, start, end, x + 0.5, y + 0.5);
                double amount = Quantize(style.Reverse ? 1 - position : position, style, x, y);

                int offset = index * 4;
                output[offset] = Mix(from.R, to.R, amount);
                output[offset + 1] = Mix(from.G, to.G, amount);
                output[offset + 2] = Mix(from.B, to.B, amount);
                output[offset + 3] = (byte)Math.Clamp(Math.Round(Mix(from.A, to.A, amount) * strength), 0, 255);
            }
        }

        return output;
    }

    private static double WrapTurn(double turn) => turn < 0 ? turn + 1 : turn;

    private static byte Mix(byte from, byte to, double amount) =>
        (byte)Math.Clamp(Math.Round(from + ((to - from) * amount)), 0, 255);
}
