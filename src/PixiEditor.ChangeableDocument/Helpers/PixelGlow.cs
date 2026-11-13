using Drawie.Backend.Core.ColorsImpl;

namespace PixiEditor.ChangeableDocument.Helpers;

/// <summary>The look of a glow.</summary>
/// <param name="Radius">How far the light reaches, in pixels.</param>
/// <param name="Strength">Scales the whole glow's alpha, 0 to 1.</param>
/// <param name="Falloff">How the light fades with distance. The same choices the gradient offers.</param>
/// <param name="Bands">Band count, for <see cref="ShadeFalloff.Bands"/>.</param>
/// <param name="Pattern">The dither pattern, for <see cref="ShadeFalloff.Dithered"/>.</param>
/// <param name="Inside">Whether the light also falls inwards from the edge instead of only out.</param>
public readonly record struct GlowStyle(
    double Radius,
    double Strength,
    ShadeFalloff Falloff,
    int Bands,
    OrderedDither Pattern,
    bool Inside);

/// <summary>
/// A glow on plain arrays, so it can band and dither. A blur cannot: its falloff is whatever the
/// Gaussian gives, and banding or dithering it needs every pixel to know how far from the edge it
/// is and where it sits in the pattern.
/// </summary>
public static class PixelGlow
{
    /// <summary>Cost of a diagonal step relative to a straight one, which keeps the light round.</summary>
    private const double Diagonal = 1.41421356237;

    private const double Unreachable = 1e12;

    /// <summary>
    /// The glow alone as straight-alpha RGBA, to be drawn over the artwork, or null if there is
    /// nothing to glow.
    /// </summary>
    /// <param name="tint">The glow's colour. An alpha of zero means each pixel glows in the colour of
    /// the nearest artwork it came from, so red pixels throw a red halo.</param>
    public static byte[]? Render(byte[] rgba, int width, int height, GlowStyle style, Color tint)
    {
        double radius = style.Radius;
        if (radius <= 0 || width <= 0 || height <= 0)
        {
            return null;
        }

        int count = width * height;
        double[] distance = new double[count];
        int[] nearest = new int[count];
        bool any = false;

        for (int i = 0; i < count; i++)
        {
            bool filled = rgba[(i * 4) + 3] != 0;
            distance[i] = filled ? 0 : Unreachable;
            nearest[i] = filled ? i : -1;
            any |= filled;
        }

        if (!any)
        {
            return null;
        }

        Spread(distance, nearest, width, height);

        byte[] glow = new byte[count * 4];
        double strength = Math.Clamp(style.Strength, 0, 1);

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int index = (y * width) + x;
                bool filled = rgba[(index * 4) + 3] != 0;

                // The light comes off the artwork, so it lands outside it. Inside only when asked,
                // and then the artwork's own pixels are what it shines on.
                if (filled && !style.Inside)
                {
                    continue;
                }

                double away = distance[index];
                if (away > radius)
                {
                    continue;
                }

                double position = 1 - (away / radius);
                double amount = PixelGradient.Quantize(position, style.Falloff, style.Bands, style.Pattern, x, y);
                if (amount <= 0)
                {
                    continue;
                }

                int source = nearest[index];
                Color color = tint.A != 0
                    ? tint
                    : new Color(rgba[source * 4], rgba[(source * 4) + 1], rgba[(source * 4) + 2]);

                int offset = index * 4;
                glow[offset] = color.R;
                glow[offset + 1] = color.G;
                glow[offset + 2] = color.B;
                glow[offset + 3] = (byte)Math.Clamp(Math.Round(amount * strength * color.A), 0, 255);
            }
        }

        return glow;
    }

    /// <summary>
    /// Distance to the nearest artwork pixel, and which pixel that was, in two passes over the
    /// image. Carrying the source along is what lets the glow take its colour from the artwork.
    /// </summary>
    private static void Spread(double[] distance, int[] nearest, int width, int height)
    {
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int index = (y * width) + x;
                Visit(distance, nearest, index, x > 0 ? index - 1 : -1, 1);
                Visit(distance, nearest, index, y > 0 ? index - width : -1, 1);
                Visit(distance, nearest, index, y > 0 && x > 0 ? index - width - 1 : -1, Diagonal);
                Visit(distance, nearest, index, y > 0 && x < width - 1 ? index - width + 1 : -1, Diagonal);
            }
        }

        for (int y = height - 1; y >= 0; y--)
        {
            for (int x = width - 1; x >= 0; x--)
            {
                int index = (y * width) + x;
                Visit(distance, nearest, index, x < width - 1 ? index + 1 : -1, 1);
                Visit(distance, nearest, index, y < height - 1 ? index + width : -1, 1);
                Visit(distance, nearest, index, y < height - 1 && x < width - 1 ? index + width + 1 : -1, Diagonal);
                Visit(distance, nearest, index, y < height - 1 && x > 0 ? index + width - 1 : -1, Diagonal);
            }
        }
    }

    private static void Visit(double[] distance, int[] nearest, int index, int from, double cost)
    {
        if (from < 0)
        {
            return;
        }

        double candidate = distance[from] + cost;
        if (candidate >= distance[index])
        {
            return;
        }

        distance[index] = candidate;
        nearest[index] = nearest[from];
    }
}
