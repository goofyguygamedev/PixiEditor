using Drawie.Numerics;

namespace PixiEditor.ChangeableDocument.Helpers;

/// <summary>How the shade fades from the edge inwards.</summary>
public enum ShadeFalloff
{
    /// <summary>Full strength across the whole thickness.</summary>
    Hard,

    /// <summary>Steps down in flat bands, which is what hand-shaded pixel art looks like.</summary>
    Bands,

    /// <summary>A continuous fade. Introduces new colours, so it is not the pixel-art default.</summary>
    Smooth,

    /// <summary>The same fade as <see cref="Smooth"/>, expressed as an ordered dither.</summary>
    Dithered
}

/// <summary>The geometry of a shade: which side, how wide, how it fades.</summary>
/// <param name="Angle">
/// The side that gets shaded, in degrees: 0 is right, 90 up, 180 left, 270 down.
/// </param>
/// <param name="Spread">
/// How far around that side the shade wraps, in degrees. 0 is a single direction, 360 a ring.
/// </param>
/// <param name="Thickness">How far in from the edge the shade reaches, in pixels.</param>
/// <param name="Falloff">How the shade fades over that distance.</param>
/// <param name="Bands">Band count, for <see cref="ShadeFalloff.Bands"/>.</param>
public readonly record struct EdgeShadeShape(
    double Angle,
    double Spread,
    int Thickness,
    ShadeFalloff Falloff,
    int Bands);

/// <summary>
/// Works out how much shade each pixel of a region gets from how close it is to the region's edge
/// in a given direction.
/// </summary>
/// <remarks>
/// Each pixel walks outwards along the shading direction until it leaves its region. That is what
/// produces the crescent an artist would draw on a sphere: thick where the edge faces the angle,
/// tapering to nothing where the edge runs alongside it. A plain distance-to-edge would give an even
/// ring whatever the angle. Every pixel uses the same pre-computed walk, so the inner edge of the
/// shade is as clean as the outline it follows.
///
/// This class knows nothing about surfaces or rendering, so the tool and the graph node share it and
/// it can be tested on plain arrays.
/// </remarks>
public static class EdgeShader
{
    /// <summary>Region id of a pixel that belongs to no region.</summary>
    public const int NoRegion = -1;

    /// <summary>Fans are built from rays this many degrees apart.</summary>
    private const double RayStepDegrees = 15;

    private const int ParallelPixelThreshold = 40_000;

    private static readonly float[] Bayer4 =
    [
        0.5f / 16, 8.5f / 16, 2.5f / 16, 10.5f / 16,
        12.5f / 16, 4.5f / 16, 14.5f / 16, 6.5f / 16,
        3.5f / 16, 11.5f / 16, 1.5f / 16, 9.5f / 16,
        15.5f / 16, 7.5f / 16, 13.5f / 16, 5.5f / 16
    ];

    /// <summary>
    /// Splits an image into connected regions of like colours. Transparent pixels get
    /// <see cref="NoRegion"/>.
    /// </summary>
    /// <remarks>
    /// Pixels are compared with the colour the region started from rather than with their neighbour,
    /// or a soft gradient would chain together into one region however low the tolerance was.
    /// </remarks>
    /// <param name="rgba">Straight-alpha RGBA, four bytes per pixel.</param>
    /// <param name="tolerance">Allowed difference per channel, 0 to 1.</param>
    /// <param name="regionCount">How many regions were found.</param>
    public static int[] SegmentLikeColors(byte[] rgba, int width, int height, double tolerance, out int regionCount)
    {
        int[] regions = new int[width * height];
        Array.Fill(regions, NoRegion);
        int maxDifference = ToByteTolerance(tolerance);
        Stack<int> pending = new();
        regionCount = 0;

        for (int start = 0; start < regions.Length; start++)
        {
            if (regions[start] != NoRegion || rgba[(start * 4) + 3] == 0)
            {
                continue;
            }

            FloodInto(regions, regionCount, rgba, width, height, start, maxDifference, pending);
            regionCount++;
        }

        return regions;
    }

    /// <summary>
    /// The single connected region of like colours containing <paramref name="position"/>, as region
    /// 0. Everything else is <see cref="NoRegion"/>.
    /// </summary>
    public static int[] FloodRegion(byte[] rgba, int width, int height, VecI position, double tolerance)
    {
        int[] regions = new int[width * height];
        Array.Fill(regions, NoRegion);

        if (position.X < 0 || position.Y < 0 || position.X >= width || position.Y >= height)
        {
            return regions;
        }

        int start = (position.Y * width) + position.X;
        if (rgba[(start * 4) + 3] != 0)
        {
            FloodInto(regions, 0, rgba, width, height, start, ToByteTolerance(tolerance), new Stack<int>());
        }

        return regions;
    }

    /// <summary>A region covering the masked pixels that actually hold something.</summary>
    public static int[] RegionFromMask(bool[] mask, byte[] rgba)
    {
        int[] regions = new int[mask.Length];
        for (int i = 0; i < mask.Length; i++)
        {
            regions[i] = mask[i] && rgba[(i * 4) + 3] != 0 ? 0 : NoRegion;
        }

        return regions;
    }

    /// <summary>The amount of shade, 0 to 1, for every pixel.</summary>
    /// <param name="regions">Region id per pixel.</param>
    /// <param name="edgeMask">
    /// Which pixels count as an edge when a walk reaches them. Null means anything outside the
    /// region. A walk passes through a pixel that does not count and carries on looking, so a
    /// highlight inside a shape does not stop it finding the outline beyond.
    /// </param>
    /// <param name="shadeable">Per region id, whether it gets shaded at all. Null means all do.</param>
    /// <remarks>The canvas border is never an edge: it is where the image stops, not a shape.</remarks>
    public static float[] Compute(int[] regions, int width, int height, EdgeShadeShape shape,
        bool[]? edgeMask = null, bool[]? shadeable = null)
    {
        float[] amounts = new float[regions.Length];
        if (shape.Thickness < 1)
        {
            return amounts;
        }

        Ray[] rays = BuildRays(shape);

        void ShadeRow(int y)
        {
            for (int x = 0; x < width; x++)
            {
                int index = (y * width) + x;
                int region = regions[index];
                if (region == NoRegion || (shadeable is not null && !shadeable[region]))
                {
                    continue;
                }

                double distance = FindEdgeDistance(regions, width, height, x, y, region, rays, edgeMask);
                if (distance < 1)
                {
                    amounts[index] = ApplyFalloff(distance, shape, x, y);
                }
            }
        }

        if (regions.Length >= ParallelPixelThreshold)
        {
            Parallel.For(0, height, ShadeRow);
        }
        else
        {
            for (int y = 0; y < height; y++)
            {
                ShadeRow(y);
            }
        }

        return amounts;
    }

    /// <summary>
    /// Turns shade amounts into straight-alpha RGBA: the colour everywhere, with the amount as alpha.
    /// </summary>
    public static byte[] BuildOverlay(float[] amounts, byte r, byte g, byte b, byte a, double strength)
    {
        byte[] overlay = new byte[amounts.Length * 4];
        for (int i = 0; i < amounts.Length; i++)
        {
            if (amounts[i] <= 0)
            {
                continue;
            }

            int offset = i * 4;
            overlay[offset] = r;
            overlay[offset + 1] = g;
            overlay[offset + 2] = b;
            overlay[offset + 3] = (byte)Math.Clamp(amounts[i] * strength * a, 0, 255);
        }

        return overlay;
    }

    /// <summary>The smallest rectangle holding every shaded pixel, or null if there are none.</summary>
    public static RectI? FindBounds(float[] amounts, int width, int height)
    {
        int minX = width, minY = height, maxX = -1, maxY = -1;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (amounts[(y * width) + x] <= 0)
                {
                    continue;
                }

                minX = Math.Min(minX, x);
                maxX = Math.Max(maxX, x);
                minY = Math.Min(minY, y);
                maxY = Math.Max(maxY, y);
            }
        }

        return maxX < 0 ? null : new RectI(minX, minY, maxX - minX + 1, maxY - minY + 1);
    }

    /// <summary>Converts premultiplied RGBA to straight alpha, in place.</summary>
    public static void Unpremultiply(byte[] rgba)
    {
        for (int i = 0; i < rgba.Length; i += 4)
        {
            byte alpha = rgba[i + 3];
            if (alpha is 0 or 255)
            {
                continue;
            }

            rgba[i] = (byte)Math.Min(255, rgba[i] * 255 / alpha);
            rgba[i + 1] = (byte)Math.Min(255, rgba[i + 1] * 255 / alpha);
            rgba[i + 2] = (byte)Math.Min(255, rgba[i + 2] * 255 / alpha);
        }
    }

    /// <summary>Whether a pixel matches a colour within <paramref name="tolerance"/> on every channel.</summary>
    public static bool ColorsMatch(byte[] rgba, int index, byte r, byte g, byte b, byte a, double tolerance)
    {
        int maxDifference = ToByteTolerance(tolerance);
        int offset = index * 4;
        return Math.Abs(rgba[offset] - r) <= maxDifference
               && Math.Abs(rgba[offset + 1] - g) <= maxDifference
               && Math.Abs(rgba[offset + 2] - b) <= maxDifference
               && Math.Abs(rgba[offset + 3] - a) <= maxDifference;
    }

    private static int ToByteTolerance(double tolerance) => (int)Math.Round(Math.Clamp(tolerance, 0, 1) * 255);

    private static void FloodInto(int[] regions, int region, byte[] rgba, int width, int height, int start,
        int maxDifference, Stack<int> pending)
    {
        int seed = start * 4;
        byte r = rgba[seed], g = rgba[seed + 1], b = rgba[seed + 2], a = rgba[seed + 3];

        pending.Clear();
        pending.Push(start);
        regions[start] = region;

        while (pending.Count > 0)
        {
            int current = pending.Pop();
            int x = current % width;

            if (x > 0) Visit(current - 1);
            if (x < width - 1) Visit(current + 1);
            if (current >= width) Visit(current - width);
            if (current < (height - 1) * width) Visit(current + width);
        }

        void Visit(int index)
        {
            if (regions[index] != NoRegion)
            {
                return;
            }

            int offset = index * 4;
            if (rgba[offset + 3] != 0
                && Math.Abs(rgba[offset] - r) <= maxDifference
                && Math.Abs(rgba[offset + 1] - g) <= maxDifference
                && Math.Abs(rgba[offset + 2] - b) <= maxDifference
                && Math.Abs(rgba[offset + 3] - a) <= maxDifference)
            {
                regions[index] = region;
                pending.Push(index);
            }
        }
    }

    /// <returns>0 touching the edge, approaching 1 at full thickness, or above 1 if none was found.</returns>
    private static double FindEdgeDistance(int[] regions, int width, int height, int x, int y, int region,
        Ray[] rays, bool[]? edgeMask)
    {
        double best = 2;

        foreach (Ray ray in rays)
        {
            for (int step = 0; step < ray.DeltaX.Length; step++)
            {
                double distance = step / (double)ray.DeltaX.Length;
                if (distance >= best)
                {
                    break;
                }

                int sampleX = x + ray.DeltaX[step];
                int sampleY = y + ray.DeltaY[step];
                if (sampleX < 0 || sampleY < 0 || sampleX >= width || sampleY >= height)
                {
                    break;
                }

                int sample = (sampleY * width) + sampleX;
                if (regions[sample] == region)
                {
                    continue;
                }

                if (edgeMask is null || edgeMask[sample])
                {
                    best = distance;
                    break;
                }
            }
        }

        return best;
    }

    private static float ApplyFalloff(double distance, EdgeShadeShape shape, int x, int y)
    {
        double remaining = 1 - distance;
        switch (shape.Falloff)
        {
            case ShadeFalloff.Bands:
                int bands = Math.Max(1, shape.Bands);
                return (float)(1 - (Math.Floor(distance * bands) / bands));
            case ShadeFalloff.Smooth:
                return (float)remaining;
            case ShadeFalloff.Dithered:
                return remaining > Bayer4[((y & 3) * 4) + (x & 3)] ? 1f : 0f;
            default:
                return 1f;
        }
    }

    private static Ray[] BuildRays(EdgeShadeShape shape)
    {
        double spread = Math.Clamp(shape.Spread, 0, 360);
        List<Ray> rays = new();

        if (spread <= 0)
        {
            rays.Add(CreateRay(shape.Angle, shape.Thickness));
            return rays.ToArray();
        }

        int intervals = Math.Max(1, (int)Math.Ceiling(spread / RayStepDegrees));
        double step = spread / intervals;
        bool wrapsAround = spread >= 360;
        int rayCount = wrapsAround ? intervals : intervals + 1;

        // Rays shorten towards the ends of the fan so the shade tapers off instead of stopping dead.
        // The taper fades out as the fan approaches a full circle, where there are no ends to taper.
        double taper = 1 - (spread / 360);

        for (int i = 0; i < rayCount; i++)
        {
            double offset = (-spread / 2) + (i * step);
            double position = Math.Abs(offset) / (spread / 2);
            double scale = 1 - (taper * (1 - Math.Cos(position * Math.PI / 2)));
            int length = (int)Math.Round(shape.Thickness * scale);
            if (length >= 1)
            {
                rays.Add(CreateRay(shape.Angle + offset, length));
            }
        }

        return rays.ToArray();
    }

    private static Ray CreateRay(double angleDegrees, int length)
    {
        double radians = angleDegrees * Math.PI / 180;
        double directionX = Math.Cos(radians);

        // Screen Y runs downwards, and an angle of 90 should mean up.
        double directionY = -Math.Sin(radians);

        int[] deltaX = new int[length];
        int[] deltaY = new int[length];
        for (int i = 0; i < length; i++)
        {
            deltaX[i] = (int)Math.Round(directionX * (i + 1));
            deltaY[i] = (int)Math.Round(directionY * (i + 1));
        }

        return new Ray(deltaX, deltaY);
    }

    private readonly record struct Ray(int[] DeltaX, int[] DeltaY);
}
