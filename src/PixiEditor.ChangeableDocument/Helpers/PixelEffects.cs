namespace PixiEditor.ChangeableDocument.Helpers;

/// <summary>Effects over straight-alpha RGBA pixel arrays.</summary>
public static class PixelEffects
{
    /// <summary>The darkest and lightest colour values among pixels that are not transparent.</summary>
    public static (byte Low, byte High) FindRange(byte[] rgba)
    {
        byte low = 255, high = 0;
        for (int i = 0; i < rgba.Length; i += 4)
        {
            if (rgba[i + 3] == 0)
            {
                continue;
            }

            for (int channel = 0; channel < 3; channel++)
            {
                low = Math.Min(low, rgba[i + channel]);
                high = Math.Max(high, rgba[i + channel]);
            }
        }

        return low > high ? ((byte)0, (byte)255) : (low, high);
    }

    /// <summary>Remaps the tonal range. All values are 0 to 1, except gamma where 1 is neutral.</summary>
    public static byte[] Levels(byte[] rgba, double inputBlack, double inputWhite, double gamma, double outputBlack, double outputWhite)
    {
        double span = Math.Max(1d / 255, inputWhite - inputBlack);
        double exponent = 1 / Math.Max(0.01, gamma);

        return ApplyLookup(rgba, value =>
        {
            double normalized = Math.Clamp((value - inputBlack) / span, 0, 1);
            return outputBlack + (Math.Pow(normalized, exponent) * (outputWhite - outputBlack));
        });
    }

    /// <summary>Applies a tone curve through evenly spaced output values, from shadows to highlights.</summary>
    public static byte[] Curves(byte[] rgba, double[] points)
    {
        double[] tangents = ComputeTangents(points);
        double step = 1d / (points.Length - 1);

        return ApplyLookup(rgba, value =>
        {
            int segment = Math.Min((int)(value / step), points.Length - 2);
            double t = (value - (segment * step)) / step;
            double t2 = t * t, t3 = t2 * t;
            return (((2 * t3) - (3 * t2) + 1) * points[segment])
                   + ((t3 - (2 * t2) + t) * tangents[segment] * step)
                   + (((-2 * t3) + (3 * t2)) * points[segment + 1])
                   + ((t3 - t2) * tangents[segment + 1] * step);
        });
    }

    public static byte[] Pixelate(byte[] rgba, int width, int height, int blockSize)
    {
        byte[] output = new byte[rgba.Length];
        blockSize = Math.Max(1, blockSize);

        for (int blockY = 0; blockY < height; blockY += blockSize)
        {
            for (int blockX = 0; blockX < width; blockX += blockSize)
            {
                int right = Math.Min(blockX + blockSize, width);
                int bottom = Math.Min(blockY + blockSize, height);
                Accumulator total = default;

                for (int y = blockY; y < bottom; y++)
                {
                    for (int x = blockX; x < right; x++)
                    {
                        total.Add(rgba, ((y * width) + x) * 4);
                    }
                }

                for (int y = blockY; y < bottom; y++)
                {
                    for (int x = blockX; x < right; x++)
                    {
                        total.Write(output, ((y * width) + x) * 4);
                    }
                }
            }
        }

        return output;
    }

    /// <summary>Smears along a straight line. <paramref name="angle"/> is in degrees.</summary>
    public static byte[] MotionBlur(byte[] rgba, int width, int height, double angle, int length)
    {
        double radians = angle * Math.PI / 180;
        double directionX = Math.Cos(radians), directionY = -Math.Sin(radians);

        return Sample(rgba, width, height, Math.Max(1, length), (x, y, t) =>
            (x + (directionX * (t - 0.5) * (length - 1)), y + (directionY * (t - 0.5) * (length - 1))));
    }

    /// <summary>Smears towards or away from a centre given as 0 to 1 across the canvas.</summary>
    public static byte[] ZoomBlur(byte[] rgba, int width, int height, double centerX, double centerY, double amount)
    {
        double originX = centerX * width, originY = centerY * height;
        int samples = Math.Clamp((int)(amount * Math.Max(width, height) / 2), 2, 64);

        return Sample(rgba, width, height, samples, (x, y, t) =>
        {
            double scale = 1 + ((t - 0.5) * amount);
            return (originX + ((x - originX) * scale), originY + ((y - originY) * scale));
        });
    }

    /// <summary>Smears around a centre given as 0 to 1 across the canvas, by a total angle in degrees.</summary>
    public static byte[] SpinBlur(byte[] rgba, int width, int height, double centerX, double centerY, double degrees)
    {
        double originX = centerX * width, originY = centerY * height;
        double total = degrees * Math.PI / 180;
        int samples = Math.Clamp((int)(Math.Abs(degrees) / 2), 2, 64);

        return Sample(rgba, width, height, samples, (x, y, t) =>
        {
            double turn = (t - 0.5) * total;
            double cos = Math.Cos(turn), sin = Math.Sin(turn);
            double fromX = x - originX, fromY = y - originY;
            return (originX + (fromX * cos) - (fromY * sin), originY + (fromX * sin) + (fromY * cos));
        });
    }

    /// <summary>Darkens towards the corners.</summary>
    /// <param name="radius">Where the darkening starts, as a fraction of centre-to-corner distance.</param>
    /// <param name="softness">Distance over which it reaches full strength, in the same units.</param>
    public static byte[] Vignette(byte[] rgba, int width, int height, double radius, double softness, double strength)
    {
        byte[] output = (byte[])rgba.Clone();
        double centerX = (width - 1) / 2d, centerY = (height - 1) / 2d;
        double corner = Math.Max(1, Math.Sqrt((centerX * centerX) + (centerY * centerY)));

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                double dx = x - centerX, dy = y - centerY;
                double distance = Math.Sqrt((dx * dx) + (dy * dy)) / corner;
                double amount = Math.Clamp((distance - radius) / Math.Max(0.001, softness), 0, 1) * strength;
                int offset = ((y * width) + x) * 4;
                for (int channel = 0; channel < 3; channel++)
                {
                    output[offset + channel] = (byte)Math.Round(rgba[offset + channel] * (1 - amount));
                }
            }
        }

        return output;
    }

    /// <summary>Slides the red channel one way and the blue the other, like a cheap lens.</summary>
    public static byte[] ChromaticAberration(byte[] rgba, int width, int height, int offsetX, int offsetY)
    {
        byte[] output = (byte[])rgba.Clone();

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int offset = ((y * width) + x) * 4;
                byte redAlpha = Read(rgba, width, height, x - offsetX, y - offsetY, 3);
                byte blueAlpha = Read(rgba, width, height, x + offsetX, y + offsetY, 3);
                output[offset] = redAlpha == 0 ? (byte)0 : Read(rgba, width, height, x - offsetX, y - offsetY, 0);
                output[offset + 2] = blueAlpha == 0 ? (byte)0 : Read(rgba, width, height, x + offsetX, y + offsetY, 2);
                output[offset + 3] = Math.Max(rgba[offset + 3], Math.Max(redAlpha, blueAlpha));
            }
        }

        return output;
    }

    /// <summary>Takes the median of each neighbourhood, removing specks while keeping edges.</summary>
    public static byte[] Median(byte[] rgba, int width, int height, int radius)
    {
        byte[] output = new byte[rgba.Length];
        int windowSide = (radius * 2) + 1;
        byte[][] windows = [new byte[windowSide * windowSide], new byte[windowSide * windowSide], new byte[windowSide * windowSide], new byte[windowSide * windowSide]];

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int count = 0;
                for (int windowY = Math.Max(0, y - radius); windowY <= Math.Min(height - 1, y + radius); windowY++)
                {
                    for (int windowX = Math.Max(0, x - radius); windowX <= Math.Min(width - 1, x + radius); windowX++)
                    {
                        int source = ((windowY * width) + windowX) * 4;
                        for (int channel = 0; channel < 4; channel++)
                        {
                            windows[channel][count] = rgba[source + channel];
                        }

                        count++;
                    }
                }

                int target = ((y * width) + x) * 4;
                for (int channel = 0; channel < 4; channel++)
                {
                    Array.Sort(windows[channel], 0, count);
                    output[target + channel] = windows[channel][count / 2];
                }
            }
        }

        return output;
    }

    private static byte[] ApplyLookup(byte[] rgba, Func<double, double> map)
    {
        byte[] table = new byte[256];
        for (int i = 0; i < 256; i++)
        {
            table[i] = (byte)Math.Clamp(Math.Round(map(i / 255d) * 255), 0, 255);
        }

        byte[] output = (byte[])rgba.Clone();
        for (int i = 0; i < output.Length; i += 4)
        {
            output[i] = table[output[i]];
            output[i + 1] = table[output[i + 1]];
            output[i + 2] = table[output[i + 2]];
        }

        return output;
    }

    /// <summary>Tangents that keep a rising curve rising, so it can never overshoot into a wobble.</summary>
    private static double[] ComputeTangents(double[] points)
    {
        double step = 1d / (points.Length - 1);
        double[] slopes = new double[points.Length - 1];
        for (int i = 0; i < slopes.Length; i++)
        {
            slopes[i] = (points[i + 1] - points[i]) / step;
        }

        double[] tangents = new double[points.Length];
        tangents[0] = slopes[0];
        tangents[^1] = slopes[^1];
        for (int i = 1; i < points.Length - 1; i++)
        {
            tangents[i] = slopes[i - 1] * slopes[i] <= 0 ? 0 : (slopes[i - 1] + slopes[i]) / 2;
        }

        return tangents;
    }

    private static byte[] Sample(byte[] rgba, int width, int height, int samples,
        Func<double, double, double, (double X, double Y)> position)
    {
        byte[] output = new byte[rgba.Length];

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                Accumulator total = default;
                for (int sample = 0; sample < samples; sample++)
                {
                    double t = samples == 1 ? 0.5 : sample / (double)(samples - 1);
                    (double sampleX, double sampleY) = position(x, y, t);
                    int readX = (int)Math.Round(sampleX);
                    int readY = (int)Math.Round(sampleY);
                    if (readX >= 0 && readY >= 0 && readX < width && readY < height)
                    {
                        total.Add(rgba, ((readY * width) + readX) * 4);
                    }
                    else
                    {
                        total.AddEmpty();
                    }
                }

                total.Write(output, ((y * width) + x) * 4);
            }
        }

        return output;
    }

    private static byte Read(byte[] rgba, int width, int height, int x, int y, int channel) =>
        x < 0 || y < 0 || x >= width || y >= height ? (byte)0 : rgba[(((y * width) + x) * 4) + channel];

    /// <summary>
    /// Averages pixels weighted by alpha, so that transparent pixels, whose colour means nothing,
    /// cannot bleed dark fringes into the result.
    /// </summary>
    private struct Accumulator
    {
        private long red, green, blue, alpha;
        private int count;

        public void Add(byte[] rgba, int offset)
        {
            byte pixelAlpha = rgba[offset + 3];
            red += rgba[offset] * pixelAlpha;
            green += rgba[offset + 1] * pixelAlpha;
            blue += rgba[offset + 2] * pixelAlpha;
            alpha += pixelAlpha;
            count++;
        }

        public void AddEmpty() => count++;

        public readonly void Write(byte[] output, int offset)
        {
            if (alpha == 0 || count == 0)
            {
                return;
            }

            output[offset] = (byte)Math.Min(255, red / alpha);
            output[offset + 1] = (byte)Math.Min(255, green / alpha);
            output[offset + 2] = (byte)Math.Min(255, blue / alpha);
            output[offset + 3] = (byte)(alpha / count);
        }
    }
}
