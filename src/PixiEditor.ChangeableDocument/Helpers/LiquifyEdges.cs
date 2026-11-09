using System.Runtime.InteropServices;
using Drawie.Numerics;

namespace PixiEditor.ChangeableDocument.Helpers;

/// <summary>
/// Carries the thin lines of an image through a liquify stroke as lines, so an outline stays the
/// width it was drawn however the warp stretches or squeezes the area around it.
/// </summary>
/// <remarks>
/// A warp scales everything it moves, so a stretched outline gets thicker and a squeezed one breaks
/// up. Here the thin strokes are taken out of the warp: their centrelines become particles that ride
/// the same motion as the field, the warped image is rendered as if the strokes were not there, and
/// the strokes are then redrawn through the moved particles at their original width. Line pixels the
/// stroke has not moved are never redrawn, so everything outside the brush stays exactly as it was.
/// </remarks>
internal sealed class LiquifyEdges
{
    private const int NoOwner = -1;

    /// <summary>Iterations used to solve where content ends up under a backward-mapped motion.</summary>
    private const int FixedPointSteps = 4;

    // Clockwise from north, the order the thinning test needs.
    private static readonly int[] RingX = [0, 1, 1, 1, 0, -1, -1, -1];
    private static readonly int[] RingY = [-1, -1, 0, 1, 1, 1, 0, -1];

    private readonly byte[] source;
    private readonly uint[] colors;
    private readonly int width;
    private readonly int height;
    private readonly int maxThickness;
    private readonly int searchRadius;
    private readonly bool[] thin;
    private readonly int[] owner;
    private readonly Particle[] particles;
    private readonly (int A, int B)[] links;

    /// <param name="source">The original image as RGBA, the size of the field.</param>
    /// <param name="maxThickness">The widest feature, in pixels, that counts as a line.</param>
    public LiquifyEdges(byte[] source, int width, int height, int maxThickness)
    {
        this.source = source;
        this.width = width;
        this.height = height;
        this.maxThickness = Math.Max(1, maxThickness);
        searchRadius = this.maxThickness + 1;
        colors = MemoryMarshal.Cast<byte, uint>(source).ToArray();
        owner = new int[colors.Length];
        Array.Fill(owner, NoOwner);

        thin = FindThinPixels();
        List<int> thinPixels = new();
        for (int i = 0; i < thin.Length; i++)
        {
            if (thin[i])
            {
                thinPixels.Add(i);
            }
        }

        bool[] skeleton = Skeletonize(thinPixels);
        particles = CreateParticles(thinPixels, skeleton);
        links = FindLinks(thinPixels, skeleton);
    }

    /// <summary>The share of the motion a point at this distance from the brush centre receives.</summary>
    public static double BrushWeight(double distanceSquared, double radiusSquared)
    {
        double falloff = 1 - (distanceSquared / radiusSquared);
        return falloff * falloff;
    }

    /// <summary>Moves the lines by one brush stamp.</summary>
    /// <param name="reach">Beyond this distance from the centre nothing can be affected.</param>
    /// <param name="touched">Told about every canvas position the lines now reach.</param>
    public void Advance(VecD center, double radius, double reach, Func<VecD, double, VecD> motion, Action<int, int> touched)
    {
        double radiusSquared = radius * radius;
        double reachSquared = reach * reach;

        for (int i = 0; i < particles.Length; i++)
        {
            ref Particle particle = ref particles[i];
            VecD start = new(particle.X, particle.Y);
            if ((start - center).LengthSquared >= reachSquared)
            {
                continue;
            }

            // The field stores where each output pixel reads from, so the motion is defined at the
            // destination. Solving for it keeps the lines exactly on the content they belong to.
            VecD delta = default;
            for (int step = 0; step < FixedPointSteps; step++)
            {
                VecD fromCenter = start + delta - center;
                double distanceSquared = fromCenter.LengthSquared;
                delta = distanceSquared >= radiusSquared
                    ? default
                    : motion(fromCenter, BrushWeight(distanceSquared, radiusSquared));
            }

            if (delta.X == 0 && delta.Y == 0)
            {
                continue;
            }

            particle.X = start.X + delta.X;
            particle.Y = start.Y + delta.Y;
            particle.Moved = Round(particle.X) != particle.HomeX || Round(particle.Y) != particle.HomeY;

            int margin = particle.Thickness + 1;
            foreach (VecD point in new[] { start, new VecD(particle.X, particle.Y) })
            {
                touched(Round(point.X) - margin, Round(point.Y) - margin);
                touched(Round(point.X) + margin, Round(point.Y) + margin);
            }
        }
    }

    /// <summary>Whether this pixel is part of a line that has not moved, and so simply stays.</summary>
    public bool IsPinned(int x, int y)
    {
        int line = owner[(y * width) + x];
        return line != NoOwner && !particles[line].Moved;
    }

    /// <summary>The source pixel the warped image shows for a sample, with the lines taken out.</summary>
    public (int X, int Y) ResolveSample(double sampleX, double sampleY)
    {
        int x = Round(sampleX);
        int y = Round(sampleY);
        if (x < 0 || y < 0 || x >= width || y >= height || owner[(y * width) + x] == NoOwner)
        {
            return (x, y);
        }

        // Nearest to the exact sample rather than to the pixel, so a line that gets stretched
        // across its width splits between the colours on its two sides at the right place.
        (int X, int Y) best = (x, y);
        double bestDistance = double.MaxValue;
        for (int candidateY = Math.Max(0, y - searchRadius); candidateY <= Math.Min(height - 1, y + searchRadius); candidateY++)
        {
            for (int candidateX = Math.Max(0, x - searchRadius); candidateX <= Math.Min(width - 1, x + searchRadius); candidateX++)
            {
                if (thin[(candidateY * width) + candidateX])
                {
                    continue;
                }

                double differenceX = candidateX - sampleX;
                double differenceY = candidateY - sampleY;
                double distance = (differenceX * differenceX) + (differenceY * differenceY);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = (candidateX, candidateY);
                }
            }
        }

        return best;
    }

    /// <summary>Draws every moved line over the warped image in <paramref name="destination"/>.</summary>
    public void Draw(byte[] destination, RectI area)
    {
        int[] drawn = new int[area.Width * area.Height];

        foreach ((int a, int b) in links)
        {
            if (particles[a].Moved || particles[b].Moved)
            {
                Connect(drawn, area, a, b);
            }
        }

        for (int i = 0; i < particles.Length; i++)
        {
            if (particles[i].Moved && !particles[i].IsLinked)
            {
                Plot(drawn, area, Round(particles[i].X), Round(particles[i].Y), i, particles[i].WidensHorizontally);
            }
        }

        RemoveCorners(drawn, area);

        for (int i = 0; i < drawn.Length; i++)
        {
            if (drawn[i] != 0)
            {
                ref Particle particle = ref particles[drawn[i] - 1];
                Buffer.BlockCopy(source, ((particle.HomeY * width) + particle.HomeX) * 4, destination, i * 4, 4);
            }
        }
    }

    private static int Round(double value) => (int)Math.Floor(value + 0.5);

    private bool IsFilled(int index) => colors[index] >> 24 != 0;

    /// <summary>Filled pixels that no solid square of their own colour covers.</summary>
    private bool[] FindThinPixels()
    {
        int side = maxThickness + 1;
        int[] squares = new int[colors.Length];
        bool[] solid = new bool[colors.Length];

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int index = (y * width) + x;
                if (!IsFilled(index))
                {
                    continue;
                }

                uint color = colors[index];
                int run = 1;
                if (x > 0 && y > 0 && colors[index - 1] == color && colors[index - width] == color
                    && colors[index - width - 1] == color)
                {
                    run += Math.Min(squares[index - 1], Math.Min(squares[index - width], squares[index - width - 1]));
                }

                squares[index] = run;
                if (run < side)
                {
                    continue;
                }

                for (int markY = y - side + 1; markY <= y; markY++)
                {
                    Array.Fill(solid, true, (markY * width) + x - side + 1, side);
                }
            }
        }

        bool[] candidates = new bool[colors.Length];
        for (int i = 0; i < candidates.Length; i++)
        {
            candidates[i] = IsFilled(i) && !solid[i];
        }

        // A pixel with no solid area anywhere near it is texture, like a dither field, not a line
        // running through something. There is nothing to rebuild underneath it, so it just warps.
        bool[] result = new bool[colors.Length];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int index = (y * width) + x;
                result[index] = candidates[index] && HasSolidNearby(candidates, x, y);
            }
        }

        return result;
    }

    private bool HasSolidNearby(bool[] candidates, int x, int y)
    {
        for (int nearY = Math.Max(0, y - searchRadius); nearY <= Math.Min(height - 1, y + searchRadius); nearY++)
        {
            for (int nearX = Math.Max(0, x - searchRadius); nearX <= Math.Min(width - 1, x + searchRadius); nearX++)
            {
                if (!candidates[(nearY * width) + nearX])
                {
                    return true;
                }
            }
        }

        return false;
    }

    private bool IsSameStroke(bool[] mask, uint color, int x, int y)
    {
        if (x < 0 || y < 0 || x >= width || y >= height)
        {
            return false;
        }

        int index = (y * width) + x;
        return mask[index] && colors[index] == color;
    }

    /// <summary>Zhang-Suen thinning, one colour at a time, down to one-pixel centrelines.</summary>
    private bool[] Skeletonize(List<int> thinPixels)
    {
        bool[] skeleton = (bool[])thin.Clone();
        List<int> removals = new();
        bool changed;
        do
        {
            changed = RemoveBoundary(skeleton, thinPixels, removals, true);
            changed |= RemoveBoundary(skeleton, thinPixels, removals, false);
        }
        while (changed);

        return skeleton;
    }

    private bool RemoveBoundary(bool[] skeleton, List<int> thinPixels, List<int> removals, bool firstPass)
    {
        removals.Clear();
        Span<bool> ring = stackalloc bool[8];

        foreach (int index in thinPixels)
        {
            if (!skeleton[index])
            {
                continue;
            }

            int x = index % width;
            int y = index / width;
            int neighbours = 0;
            for (int i = 0; i < 8; i++)
            {
                ring[i] = IsSameStroke(skeleton, colors[index], x + RingX[i], y + RingY[i]);
                neighbours += ring[i] ? 1 : 0;
            }

            int transitions = 0;
            for (int i = 0; i < 8; i++)
            {
                transitions += !ring[i] && ring[(i + 1) % 8] ? 1 : 0;
            }

            if (neighbours is < 2 or > 6 || transitions != 1)
            {
                continue;
            }

            bool north = ring[0], east = ring[2], south = ring[4], west = ring[6];
            bool remove = firstPass
                ? !(north && east && south) && !(east && south && west)
                : !(north && east && west) && !(north && south && west);
            if (remove)
            {
                removals.Add(index);
            }
        }

        foreach (int index in removals)
        {
            skeleton[index] = false;
        }

        return removals.Count > 0;
    }

    private Particle[] CreateParticles(List<int> thinPixels, bool[] skeleton)
    {
        List<Particle> created = new();
        Queue<int> pending = new();

        foreach (int index in thinPixels)
        {
            if (skeleton[index])
            {
                owner[index] = created.Count;
                created.Add(CreateParticle(index));
                pending.Enqueue(index);
            }
        }

        SpreadOwners(pending);

        // Thinning can erase a small blob entirely, which must not make it vanish from the image.
        foreach (int index in thinPixels)
        {
            if (owner[index] == NoOwner)
            {
                skeleton[index] = true;
                owner[index] = created.Count;
                created.Add(CreateParticle(index));
                pending.Enqueue(index);
                SpreadOwners(pending);
            }
        }

        return created.ToArray();
    }

    private void SpreadOwners(Queue<int> pending)
    {
        while (pending.Count > 0)
        {
            int current = pending.Dequeue();
            int x = current % width;
            int y = current / width;
            for (int i = 0; i < 8; i++)
            {
                int nextX = x + RingX[i];
                int nextY = y + RingY[i];
                if (IsSameStroke(thin, colors[current], nextX, nextY) && owner[(nextY * width) + nextX] == NoOwner)
                {
                    owner[(nextY * width) + nextX] = owner[current];
                    pending.Enqueue((nextY * width) + nextX);
                }
            }
        }
    }

    private Particle CreateParticle(int index)
    {
        int x = index % width;
        int y = index / width;
        int runAcross = MeasureRun(x, y, 1, 0);
        int runDown = MeasureRun(x, y, 0, 1);
        int thickness = Math.Clamp(Math.Min(runAcross, runDown), 1, maxThickness);
        bool horizontal = runAcross <= runDown;

        return new Particle
        {
            HomeX = x,
            HomeY = y,
            X = x,
            Y = y,
            Thickness = thickness,
            WidensHorizontally = horizontal,
            Anchor = FindAnchor(x, y, thickness, horizontal)
        };
    }

    private int MeasureRun(int x, int y, int stepX, int stepY)
    {
        uint color = colors[(y * width) + x];
        int run = 1;
        foreach (int direction in new[] { -1, 1 })
        {
            for (int i = 1; i <= maxThickness; i++)
            {
                int nextX = x + (stepX * i * direction);
                int nextY = y + (stepY * i * direction);
                if (nextX < 0 || nextY < 0 || nextX >= width || nextY >= height || colors[(nextY * width) + nextX] != color)
                {
                    break;
                }

                run++;
            }
        }

        return run;
    }

    /// <summary>
    /// How far a stroke starts before its centreline pixel. A centreline of an even-width stroke has
    /// to lean to one side, and this finds the side the stroke actually occupies.
    /// </summary>
    private int FindAnchor(int x, int y, int thickness, bool horizontal)
    {
        uint color = colors[(y * width) + x];
        int best = 0;
        int bestScore = int.MinValue;

        for (int offset = 1 - thickness; offset <= 0; offset++)
        {
            int covered = 0;
            for (int i = 0; i < thickness; i++)
            {
                int along = offset + i;
                covered += IsSameStroke(thin, color, x + (horizontal ? along : 0), y + (horizontal ? 0 : along)) ? 1 : 0;
            }

            int score = (covered * 1000) - Math.Abs((2 * offset) + thickness - 1);
            if (score > bestScore)
            {
                bestScore = score;
                best = offset;
            }
        }

        return best;
    }

    private (int A, int B)[] FindLinks(List<int> thinPixels, bool[] skeleton)
    {
        List<(int A, int B)> found = new();

        foreach (int index in thinPixels)
        {
            if (!skeleton[index])
            {
                continue;
            }

            int x = index % width;
            int y = index / width;
            uint color = colors[index];
            bool right = IsSameStroke(skeleton, color, x + 1, y);
            bool left = IsSameStroke(skeleton, color, x - 1, y);
            bool below = IsSameStroke(skeleton, color, x, y + 1);

            if (right)
            {
                AddLink(found, owner[index], owner[index + 1]);
            }

            if (below)
            {
                AddLink(found, owner[index], owner[index + width]);
            }

            // A diagonal is only a real connection when no pair of straight steps already makes it.
            if (!right && !below && IsSameStroke(skeleton, color, x + 1, y + 1))
            {
                AddLink(found, owner[index], owner[index + width + 1]);
            }

            if (!left && !below && IsSameStroke(skeleton, color, x - 1, y + 1))
            {
                AddLink(found, owner[index], owner[index + width - 1]);
            }
        }

        return found.ToArray();
    }

    private void AddLink(List<(int A, int B)> found, int a, int b)
    {
        found.Add((a, b));
        particles[a].IsLinked = true;
        particles[b].IsLinked = true;
    }

    private void Connect(int[] drawn, RectI area, int a, int b)
    {
        int x = Round(particles[a].X);
        int y = Round(particles[a].Y);
        int endX = Round(particles[b].X);
        int endY = Round(particles[b].Y);
        int deltaX = Math.Abs(endX - x);
        int deltaY = -Math.Abs(endY - y);
        int stepX = x < endX ? 1 : -1;
        int stepY = y < endY ? 1 : -1;
        int error = deltaX + deltaY;

        // Between the two ends the line belongs to whichever of them moved, which is what lets the
        // corner clean-up below touch it while leaving the original art alone.
        int between = particles[a].Moved ? a : b;

        // Width goes across the way the line runs now, which a twirl may have turned.
        bool horizontal = deltaX == -deltaY ? particles[between].WidensHorizontally : deltaX < -deltaY;
        Plot(drawn, area, x, y, a, horizontal);

        while (x != endX || y != endY)
        {
            int doubled = error * 2;
            if (doubled >= deltaY)
            {
                error += deltaY;
                x += stepX;
            }

            if (doubled <= deltaX)
            {
                error += deltaX;
                y += stepY;
            }

            Plot(drawn, area, x, y, x == endX && y == endY ? b : between, horizontal);
        }
    }

    private void Plot(int[] drawn, RectI area, int x, int y, int id, bool horizontal)
    {
        ref Particle particle = ref particles[id];
        for (int i = 0; i < particle.Thickness; i++)
        {
            int along = particle.Anchor + i;
            int cellX = x + (horizontal ? along : 0) - area.X;
            int cellY = y + (horizontal ? 0 : along) - area.Y;
            if (cellX >= 0 && cellY >= 0 && cellX < area.Width && cellY < area.Height)
            {
                drawn[(cellY * area.Width) + cellX] = id + 1;
            }
        }
    }

    /// <summary>
    /// Takes out the corner pixels where two moved segments meet at a right angle, which would
    /// otherwise read as a line one pixel thicker than the original at every step.
    /// </summary>
    private void RemoveCorners(int[] drawn, RectI area)
    {
        for (int y = 0; y < area.Height; y++)
        {
            for (int x = 0; x < area.Width; x++)
            {
                int cell = (y * area.Width) + x;
                if (drawn[cell] == 0)
                {
                    continue;
                }

                ref Particle particle = ref particles[drawn[cell] - 1];
                if (particle.Thickness != 1 || !particle.Moved)
                {
                    continue;
                }

                uint color = colors[(particle.HomeY * width) + particle.HomeX];
                int neighbours = 0;
                for (int i = 0; i < 8; i++)
                {
                    neighbours += IsDrawn(drawn, area, x + RingX[i], y + RingY[i], color) ? 1 : 0;
                }

                bool horizontal = IsDrawn(drawn, area, x - 1, y, color) || IsDrawn(drawn, area, x + 1, y, color);
                bool vertical = IsDrawn(drawn, area, x, y - 1, color) || IsDrawn(drawn, area, x, y + 1, color);
                if (neighbours == 2 && horizontal && vertical)
                {
                    drawn[cell] = 0;
                }
            }
        }
    }

    private bool IsDrawn(int[] drawn, RectI area, int x, int y, uint color)
    {
        if (x < 0 || y < 0 || x >= area.Width || y >= area.Height)
        {
            return false;
        }

        int id = drawn[(y * area.Width) + x];
        return id != 0 && colors[(particles[id - 1].HomeY * width) + particles[id - 1].HomeX] == color;
    }

    private struct Particle
    {
        public int HomeX;
        public int HomeY;
        public double X;
        public double Y;
        public int Thickness;
        public int Anchor;
        public bool WidensHorizontally;
        public bool IsLinked;
        public bool Moved;
    }
}
