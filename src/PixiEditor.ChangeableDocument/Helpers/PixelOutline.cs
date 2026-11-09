using Drawie.Numerics;

namespace PixiEditor.ChangeableDocument.Helpers;

/// <summary>Where an outline sits relative to the edge of its shape.</summary>
public enum OutlinePosition
{
    Outside,
    Inside,
    Centered
}

/// <summary>The form an outline takes as it gets thicker, which decides what its corners do.</summary>
public enum OutlineCorners
{
    Round,
    Square,

    /// <summary>Follows the four straight directions only, the classic one-pixel sprite outline.</summary>
    Diamond
}

/// <summary>What an outline is drawn around when no selection says otherwise.</summary>
public enum OutlineTarget
{
    /// <summary>Every filled pixel touching the clicked one, whatever its colour.</summary>
    Shape,

    /// <summary>The connected area of like colours at the clicked pixel.</summary>
    ColorArea,
    WholeLayer
}

/// <summary>The look of an outline.</summary>
/// <param name="Thickness">Width in pixels.</param>
/// <param name="Softness">
/// How much of the width fades, from 0 for a solid outline to 1 for one that fades across all of it.
/// </param>
/// <param name="Falloff">How the soft part fades.</param>
/// <param name="Bands">Band count, for <see cref="ShadeFalloff.Bands"/>.</param>
/// <param name="Pattern">The dither pattern, for <see cref="ShadeFalloff.Dithered"/>.</param>
public readonly record struct OutlineStyle(
    int Thickness,
    OutlinePosition Position,
    OutlineCorners Corners,
    double Softness,
    ShadeFalloff Falloff,
    int Bands,
    OrderedDither Pattern);

/// <summary>Computes outlines on plain arrays, so the behaviour can be tested without a canvas.</summary>
public static class PixelOutline
{
    /// <summary>Stands in for infinity in the distance transforms, large enough never to be reached.</summary>
    private const double Unreachable = 1e12;

    /// <summary>Every filled pixel connected to the given one, counting diagonal contact.</summary>
    public static bool[] FloodFilled(byte[] rgba, int width, int height, VecI position)
    {
        bool[] shape = new bool[width * height];
        if (position.X < 0 || position.Y < 0 || position.X >= width || position.Y >= height)
        {
            return shape;
        }

        int start = (position.Y * width) + position.X;
        if (rgba[(start * 4) + 3] == 0)
        {
            return shape;
        }

        Stack<int> pending = new();
        pending.Push(start);
        shape[start] = true;

        while (pending.Count > 0)
        {
            int current = pending.Pop();
            int x = current % width;
            int y = current / width;
            for (int nextY = Math.Max(0, y - 1); nextY <= Math.Min(height - 1, y + 1); nextY++)
            {
                for (int nextX = Math.Max(0, x - 1); nextX <= Math.Min(width - 1, x + 1); nextX++)
                {
                    int next = (nextY * width) + nextX;
                    if (!shape[next] && rgba[(next * 4) + 3] != 0)
                    {
                        shape[next] = true;
                        pending.Push(next);
                    }
                }
            }
        }

        return shape;
    }

    /// <summary>The amount of outline, 0 to 1, for every pixel.</summary>
    /// <remarks>The canvas border is never an edge: it is where the image stops, not a shape.</remarks>
    public static float[] Compute(bool[] shape, int width, int height, OutlineStyle style)
    {
        float[] amounts = new float[shape.Length];
        if (Array.IndexOf(shape, true) < 0)
        {
            return amounts;
        }

        int thickness = Math.Max(1, style.Thickness);
        (int outside, int inside) = style.Position switch
        {
            OutlinePosition.Inside => (0, thickness),
            OutlinePosition.Centered => ((thickness + 1) / 2, thickness / 2),
            _ => (thickness, 0)
        };

        if (outside > 0)
        {
            Paint(amounts, shape, width, height, outside, style);
        }

        if (inside > 0)
        {
            Paint(amounts, Array.ConvertAll(shape, filled => !filled), width, height, inside, style);
        }

        return amounts;
    }

    private static void Paint(float[] amounts, bool[] seeds, int width, int height, int thickness, OutlineStyle style)
    {
        double[] distances = style.Corners == OutlineCorners.Round
            ? Euclidean(seeds, width, height)
            : Chamfer(seeds, width, height, style.Corners == OutlineCorners.Square);

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int index = (y * width) + x;
                double distance = distances[index];
                if (distance > 0 && distance <= thickness)
                {
                    amounts[index] = Fade((distance - 0.5) / thickness, style, x, y);
                }
            }
        }
    }

    private static float Fade(double position, OutlineStyle style, int x, int y)
    {
        double softness = Math.Clamp(style.Softness, 0, 1);
        double faded = softness <= 0 ? 0 : Math.Clamp((position - (1 - softness)) / softness, 0, 1);
        if (faded <= 0)
        {
            return 1f;
        }

        switch (style.Falloff)
        {
            case ShadeFalloff.Bands:
                int bands = Math.Max(1, style.Bands);
                return (float)(1 - (Math.Floor(faded * bands) / bands));
            case ShadeFalloff.Smooth:
                return (float)(1 - faded);
            case ShadeFalloff.Dithered:
                return 1 - faded > PixelGradient.Threshold(style.Pattern, x, y) ? 1f : 0f;
            default:
                return 1f;
        }
    }

    /// <summary>Straight-line distance to the nearest seed, in linear time (Felzenszwalb).</summary>
    private static double[] Euclidean(bool[] seeds, int width, int height)
    {
        double[] squared = new double[seeds.Length];
        for (int i = 0; i < seeds.Length; i++)
        {
            squared[i] = seeds[i] ? 0 : Unreachable;
        }

        int longest = Math.Max(width, height);
        double[] line = new double[longest];
        double[] result = new double[longest];
        int[] parabolas = new int[longest];
        double[] boundaries = new double[longest + 1];

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                line[y] = squared[(y * width) + x];
            }

            Transform(line, result, parabolas, boundaries, height);
            for (int y = 0; y < height; y++)
            {
                squared[(y * width) + x] = result[y];
            }
        }

        for (int y = 0; y < height; y++)
        {
            Array.Copy(squared, y * width, line, 0, width);
            Transform(line, result, parabolas, boundaries, width);
            Array.Copy(result, 0, squared, y * width, width);
        }

        return Array.ConvertAll(squared, Math.Sqrt);
    }

    private static void Transform(double[] line, double[] result, int[] parabolas, double[] boundaries, int length)
    {
        int count = 0;
        parabolas[0] = 0;
        boundaries[0] = double.NegativeInfinity;
        boundaries[1] = double.PositiveInfinity;

        for (int q = 1; q < length; q++)
        {
            double intersection = Intersect(line, q, parabolas[count]);
            while (intersection <= boundaries[count])
            {
                count--;
                intersection = Intersect(line, q, parabolas[count]);
            }

            count++;
            parabolas[count] = q;
            boundaries[count] = intersection;
            boundaries[count + 1] = double.PositiveInfinity;
        }

        int segment = 0;
        for (int q = 0; q < length; q++)
        {
            while (boundaries[segment + 1] < q)
            {
                segment++;
            }

            int p = parabolas[segment];
            result[q] = ((q - p) * (q - p)) + line[p];
        }
    }

    private static double Intersect(double[] line, int q, int p) =>
        (line[q] + (q * q) - line[p] - (p * p)) / (2.0 * (q - p));

    /// <summary>Exact distance over 8 connected steps (square) or 4 connected steps (diamond).</summary>
    private static double[] Chamfer(bool[] seeds, int width, int height, bool diagonals)
    {
        double[] distances = new double[seeds.Length];
        for (int i = 0; i < seeds.Length; i++)
        {
            distances[i] = seeds[i] ? 0 : Unreachable;
        }

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int index = (y * width) + x;
                double best = distances[index];
                if (x > 0) best = Math.Min(best, distances[index - 1] + 1);
                if (y > 0) best = Math.Min(best, distances[index - width] + 1);
                if (diagonals && y > 0 && x > 0) best = Math.Min(best, distances[index - width - 1] + 1);
                if (diagonals && y > 0 && x < width - 1) best = Math.Min(best, distances[index - width + 1] + 1);
                distances[index] = best;
            }
        }

        for (int y = height - 1; y >= 0; y--)
        {
            for (int x = width - 1; x >= 0; x--)
            {
                int index = (y * width) + x;
                double best = distances[index];
                if (x < width - 1) best = Math.Min(best, distances[index + 1] + 1);
                if (y < height - 1) best = Math.Min(best, distances[index + width] + 1);
                if (diagonals && y < height - 1 && x < width - 1) best = Math.Min(best, distances[index + width + 1] + 1);
                if (diagonals && y < height - 1 && x > 0) best = Math.Min(best, distances[index + width - 1] + 1);
                distances[index] = best;
            }
        }

        return distances;
    }
}
