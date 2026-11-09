namespace PixiEditor.ChangeableDocument.Helpers;

/// <summary>The dithering patterns on offer.</summary>
public enum DitherMode
{
    /// <summary>Snaps to the nearest colour with no pattern at all.</summary>
    None,

    Checker,
    Bayer2,
    Bayer4,
    Bayer8,

    /// <summary>Error diffusion. Organic-looking, but noisier in motion than an ordered pattern.</summary>
    FloydSteinberg,

    /// <summary>Error diffusion that deliberately loses some error, keeping contrast and flat areas clean.</summary>
    Atkinson
}

/// <summary>Reduces an image to fewer colours, using a pattern to fake the ones it loses.</summary>
public static class Ditherer
{
    /// <summary>The ordered-dither threshold for a pixel, 0 to 1.</summary>
    public static float Threshold(DitherMode mode, int x, int y)
    {
        return mode switch
        {
            DitherMode.Checker => ((x + y) & 1) == 0 ? 0.25f : 0.75f,
            DitherMode.Bayer2 => Bayer(x, y, 1),
            DitherMode.Bayer4 => Bayer(x, y, 2),
            DitherMode.Bayer8 => Bayer(x, y, 3),
            _ => 0.5f
        };
    }

    /// <summary>The dithered image. Alpha passes through and transparent pixels are left alone.</summary>
    /// <param name="rgba">Straight-alpha RGBA.</param>
    /// <param name="paletteRgb">Three bytes per colour, or null to quantize to <paramref name="levels"/>.</param>
    /// <param name="levels">Values per channel, used when there is no palette.</param>
    /// <param name="amount">0 to 1: how strong the pattern is. 0 is a plain snap to nearest.</param>
    public static byte[] Apply(byte[] rgba, int width, int height, DitherMode mode, byte[]? paletteRgb, int levels, double amount)
    {
        levels = Math.Clamp(levels, 2, 256);
        return mode is DitherMode.FloydSteinberg or DitherMode.Atkinson
            ? Diffuse(rgba, width, height, mode, paletteRgb, levels, amount)
            : Ordered(rgba, width, height, mode, paletteRgb, levels, amount);
    }

    private static byte[] Ordered(byte[] rgba, int width, int height, DitherMode mode, byte[]? paletteRgb, int levels, double amount)
    {
        byte[] output = (byte[])rgba.Clone();
        double spread = mode == DitherMode.None ? 0 : GetSpread(paletteRgb, levels) * amount;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int offset = ((y * width) + x) * 4;
                if (rgba[offset + 3] == 0)
                {
                    continue;
                }

                double shift = (Threshold(mode, x, y) - 0.5) * spread;
                (byte r, byte g, byte b) = Quantize(rgba[offset] + shift, rgba[offset + 1] + shift, rgba[offset + 2] + shift,
                    paletteRgb, levels);
                output[offset] = r;
                output[offset + 1] = g;
                output[offset + 2] = b;
            }
        }

        return output;
    }

    private static byte[] Diffuse(byte[] rgba, int width, int height, DitherMode mode, byte[]? paletteRgb, int levels, double amount)
    {
        byte[] output = (byte[])rgba.Clone();
        double[] working = new double[width * height * 3];
        for (int i = 0; i < width * height; i++)
        {
            working[i * 3] = rgba[i * 4];
            working[(i * 3) + 1] = rgba[(i * 4) + 1];
            working[(i * 3) + 2] = rgba[(i * 4) + 2];
        }

        (int X, int Y, double Weight)[] spreadTo = mode == DitherMode.Atkinson
            ?
            [
                (1, 0, 1 / 8d), (2, 0, 1 / 8d), (-1, 1, 1 / 8d), (0, 1, 1 / 8d), (1, 1, 1 / 8d), (0, 2, 1 / 8d)
            ]
            :
            [
                (1, 0, 7 / 16d), (-1, 1, 3 / 16d), (0, 1, 5 / 16d), (1, 1, 1 / 16d)
            ];

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int index = (y * width) + x;
                if (rgba[(index * 4) + 3] == 0)
                {
                    continue;
                }

                double oldR = working[index * 3], oldG = working[(index * 3) + 1], oldB = working[(index * 3) + 2];
                (byte r, byte g, byte b) = Quantize(oldR, oldG, oldB, paletteRgb, levels);
                output[index * 4] = r;
                output[(index * 4) + 1] = g;
                output[(index * 4) + 2] = b;

                double errorR = (oldR - r) * amount, errorG = (oldG - g) * amount, errorB = (oldB - b) * amount;
                foreach ((int spreadX, int spreadY, double weight) in spreadTo)
                {
                    int targetX = x + spreadX;
                    int targetY = y + spreadY;
                    if (targetX < 0 || targetX >= width || targetY >= height)
                    {
                        continue;
                    }

                    int target = (targetY * width) + targetX;
                    if (rgba[(target * 4) + 3] == 0)
                    {
                        continue;
                    }

                    working[target * 3] += errorR * weight;
                    working[(target * 3) + 1] += errorG * weight;
                    working[(target * 3) + 2] += errorB * weight;
                }
            }
        }

        return output;
    }

    /// <summary>How far the pattern has to nudge a value to reach the next available colour.</summary>
    private static double GetSpread(byte[]? paletteRgb, int levels)
    {
        if (paletteRgb is null)
        {
            return 255d / (levels - 1);
        }

        // Roughly the gap between neighbouring colours, were the palette spread evenly through RGB.
        return 255d / Math.Max(1, Math.Cbrt(paletteRgb.Length / 3d));
    }

    private static (byte R, byte G, byte B) Quantize(double r, double g, double b, byte[]? paletteRgb, int levels)
    {
        if (paletteRgb is null || paletteRgb.Length == 0)
        {
            return (Step(r, levels), Step(g, levels), Step(b, levels));
        }

        int best = 0;
        double bestDistance = double.MaxValue;
        for (int i = 0; i < paletteRgb.Length; i += 3)
        {
            double dr = r - paletteRgb[i], dg = g - paletteRgb[i + 1], db = b - paletteRgb[i + 2];

            // Green weighted up and blue down, the usual cheap stand-in for how close colours look.
            double distance = (2 * dr * dr) + (4 * dg * dg) + (3 * db * db);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = i;
            }
        }

        return (paletteRgb[best], paletteRgb[best + 1], paletteRgb[best + 2]);
    }

    private static byte Step(double value, int levels)
    {
        double normalized = Math.Clamp(value / 255d, 0, 1);
        return (byte)Math.Round(Math.Round(normalized * (levels - 1)) / (levels - 1) * 255);
    }

    /// <summary>The classic recursive Bayer matrix, computed from the bits of the coordinates.</summary>
    private static float Bayer(int x, int y, int order)
    {
        int value = 0;
        for (int bit = 0; bit < order; bit++)
        {
            value = (value << 2) | ((((x ^ y) >> bit) & 1) << 1) | ((y >> bit) & 1);
        }

        return (value + 0.5f) / (1 << (order * 2));
    }
}
