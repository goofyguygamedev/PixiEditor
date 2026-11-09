using Drawie.Numerics;

namespace PixiEditor.ChangeableDocument.Helpers;

/// <summary>Ways an existing selection can be reshaped.</summary>
public enum SelectionModification
{
    /// <summary>Takes in the pixels around the selection.</summary>
    Grow,

    /// <summary>Gives up the pixels along the selection's edge.</summary>
    Shrink,

    /// <summary>Selects only the ring of pixels just outside the selection.</summary>
    Border
}

/// <summary>Grid operations on a per-pixel on/off mask.</summary>
public static class PixelMask
{
    /// <summary>The mask after <paramref name="modification"/>, applied <paramref name="amount"/> pixels deep.</summary>
    public static bool[] Modify(bool[] mask, int width, int height, SelectionModification modification, int amount)
    {
        switch (modification)
        {
            case SelectionModification.Grow:
                return Dilate(mask, width, height, amount);
            case SelectionModification.Shrink:
                return Erode(mask, width, height, amount);
            default:
                bool[] grown = Dilate(mask, width, height, amount);
                for (int i = 0; i < grown.Length; i++)
                {
                    grown[i] &= !mask[i];
                }

                return grown;
        }
    }

    /// <summary>
    /// The closed outlines around the masked pixels, each as a list of its corners.
    /// </summary>
    /// <remarks>
    /// Selections are stored as paths and drawn by stroking them. Building one from a rectangle per
    /// pixel or per row would fill correctly but draw the marching ants along every internal seam, so
    /// only the true boundary is traced here.
    /// </remarks>
    public static List<List<VecI>> TraceOutlines(bool[] mask, int width, int height)
    {
        Dictionary<VecI, List<VecI>> outgoing = new();

        void AddEdge(VecI from, VecI to)
        {
            if (!outgoing.TryGetValue(from, out List<VecI>? targets))
            {
                targets = new List<VecI>(1);
                outgoing[from] = targets;
            }

            targets.Add(to);
        }

        bool IsSet(int x, int y) => x >= 0 && y >= 0 && x < width && y < height && mask[(y * width) + x];

        // Every boundary edge runs clockwise around its pixel, so edges chain up into closed loops.
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (!mask[(y * width) + x])
                {
                    continue;
                }

                if (!IsSet(x, y - 1)) AddEdge(new VecI(x, y), new VecI(x + 1, y));
                if (!IsSet(x + 1, y)) AddEdge(new VecI(x + 1, y), new VecI(x + 1, y + 1));
                if (!IsSet(x, y + 1)) AddEdge(new VecI(x + 1, y + 1), new VecI(x, y + 1));
                if (!IsSet(x - 1, y)) AddEdge(new VecI(x, y + 1), new VecI(x, y));
            }
        }

        List<List<VecI>> outlines = new();

        while (outgoing.Count > 0)
        {
            VecI start = outgoing.Keys.First();
            List<VecI> corners = new();
            VecI current = start;
            VecI direction = default;

            do
            {
                VecI next = TakeNext(outgoing, current, direction);
                VecI newDirection = next - current;
                if (newDirection != direction)
                {
                    corners.Add(current);
                }

                direction = newDirection;
                current = next;
            }
            while (current != start);

            outlines.Add(corners);
        }

        return outlines;
    }

    private static VecI TakeNext(Dictionary<VecI, List<VecI>> outgoing, VecI from, VecI incoming)
    {
        List<VecI> targets = outgoing[from];
        int chosen = 0;

        // Two pixels touching only at a corner leave two ways out of that vertex. Always turning the
        // same way keeps each pixel's loop separate rather than pinching them into a figure of eight.
        if (targets.Count > 1)
        {
            VecI clockwise = new(-incoming.Y, incoming.X);
            for (int i = 0; i < targets.Count; i++)
            {
                if (targets[i] - from == clockwise)
                {
                    chosen = i;
                    break;
                }
            }
        }

        VecI next = targets[chosen];
        targets.RemoveAt(chosen);
        if (targets.Count == 0)
        {
            outgoing.Remove(from);
        }

        return next;
    }

    private static bool[] Dilate(bool[] mask, int width, int height, int amount)
    {
        bool[] current = (bool[])mask.Clone();
        for (int pass = 0; pass < amount; pass++)
        {
            bool[] next = (bool[])current.Clone();
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int index = (y * width) + x;
                    if (current[index])
                    {
                        continue;
                    }

                    next[index] = (x > 0 && current[index - 1])
                                  || (x < width - 1 && current[index + 1])
                                  || (y > 0 && current[index - width])
                                  || (y < height - 1 && current[index + width]);
                }
            }

            current = next;
        }

        return current;
    }

    private static bool[] Erode(bool[] mask, int width, int height, int amount)
    {
        bool[] current = (bool[])mask.Clone();
        for (int pass = 0; pass < amount; pass++)
        {
            bool[] next = (bool[])current.Clone();
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int index = (y * width) + x;
                    if (!current[index])
                    {
                        continue;
                    }

                    // The canvas border counts as outside, so shrinking a select-all does shrink it.
                    next[index] = x > 0 && current[index - 1]
                                  && x < width - 1 && current[index + 1]
                                  && y > 0 && current[index - width]
                                  && y < height - 1 && current[index + width];
                }
            }

            current = next;
        }

        return current;
    }
}
