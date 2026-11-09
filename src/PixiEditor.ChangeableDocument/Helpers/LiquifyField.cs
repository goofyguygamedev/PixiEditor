using Drawie.Numerics;

namespace PixiEditor.ChangeableDocument.Helpers;

/// <summary>What a liquify stroke does to the pixels under it.</summary>
public enum LiquifyMode
{
    Push,
    TwirlClockwise,
    TwirlCounterClockwise,
    Pinch,
    Expand
}

/// <summary>
/// The warp built up over a liquify stroke: for every output pixel, where in the original image its
/// colour comes from.
/// </summary>
/// <remarks>
/// Strokes warp this map rather than the pixels themselves, and the image is always resampled once
/// from the untouched original. Dragging over the same spot repeatedly therefore keeps the pixels
/// crisp instead of degrading them a little further on every pass.
/// </remarks>
public sealed class LiquifyField
{
    /// <summary>Radians turned per twirl stamp at the centre of the brush, at full strength.</summary>
    private const double TwirlPerStamp = 0.25;

    /// <summary>Fraction of the distance to the centre moved per pinch stamp, at full strength.</summary>
    private const double PinchPerStamp = 0.15;

    private readonly int width;
    private readonly int height;
    private readonly float[] offsetX;
    private readonly float[] offsetY;
    private LiquifyEdges? edges;

    private int dirtyMinX;
    private int dirtyMinY;
    private int dirtyMaxX = -1;
    private int dirtyMaxY = -1;

    public LiquifyField(int width, int height)
    {
        this.width = width;
        this.height = height;
        offsetX = new float[width * height];
        offsetY = new float[width * height];
        dirtyMinX = width;
        dirtyMinY = height;
    }

    /// <summary>Everything the stroke has touched so far, or null if it has touched nothing.</summary>
    public RectI? DirtyBounds =>
        dirtyMaxX < dirtyMinX
            ? null
            : new RectI(dirtyMinX, dirtyMinY, dirtyMaxX - dirtyMinX + 1, dirtyMaxY - dirtyMinY + 1);

    /// <summary>
    /// Makes the warp keep thin lines at the width they were drawn. Call before the first stroke.
    /// </summary>
    /// <param name="source">The image that will be rendered, the size of the field.</param>
    /// <param name="maxThickness">The widest feature, in pixels, that counts as a line.</param>
    public void KeepEdges(byte[] source, int maxThickness)
    {
        edges = new LiquifyEdges(source, width, height, maxThickness);
    }

    /// <summary>Pushes the pixels under the brush along the line between two points.</summary>
    /// <param name="strength">0 to 1, where 1 keeps the pixels under the pointer.</param>
    public void Push(VecD from, VecD to, double radius, double strength)
    {
        VecD travel = to - from;
        double length = travel.Length;
        if (length <= 0 || radius <= 0)
        {
            return;
        }

        // Each step has to be small next to the brush, or the warp is too large a jump for the
        // composition in Stamp to stay accurate and the pixels tear.
        int steps = Math.Max(1, (int)Math.Ceiling(length / Math.Max(0.5, radius / 4)));
        VecD step = travel / steps;

        for (int i = 1; i <= steps; i++)
        {
            VecD center = from + (step * i);
            VecD motion = step * strength;
            Stamp(center, radius, Math.Ceiling(motion.Length) + 2, (_, weight) => motion * weight);
        }
    }

    /// <summary>Applies a twirl, pinch or expand once at <paramref name="center"/>.</summary>
    public void Apply(LiquifyMode mode, VecD center, double radius, double strength)
    {
        if (radius <= 0)
        {
            return;
        }

        switch (mode)
        {
            case LiquifyMode.TwirlClockwise:
                Twirl(center, radius, strength * TwirlPerStamp);
                break;
            case LiquifyMode.TwirlCounterClockwise:
                Twirl(center, radius, -strength * TwirlPerStamp);
                break;
            case LiquifyMode.Pinch:
                Scale(center, radius, -strength * PinchPerStamp);
                break;
            case LiquifyMode.Expand:
                Scale(center, radius, strength * PinchPerStamp);
                break;
        }
    }

    /// <summary>
    /// Writes the warped image into <paramref name="destination"/>, which holds exactly
    /// <paramref name="area"/>.
    /// </summary>
    /// <param name="source">The original image, as premultiplied RGBA, the size of the field.</param>
    /// <param name="smooth">
    /// Blends between source pixels. Off by default for pixel art, where nearest sampling keeps
    /// every colour on the palette.
    /// </param>
    public void Render(byte[] source, byte[] destination, RectI area, bool smooth)
    {
        for (int y = 0; y < area.Height; y++)
        {
            for (int x = 0; x < area.Width; x++)
            {
                int canvasX = area.X + x;
                int canvasY = area.Y + y;
                int index = (canvasY * width) + canvasX;
                double sampleX = canvasX + offsetX[index];
                double sampleY = canvasY + offsetY[index];
                int target = ((y * area.Width) + x) * 4;

                if (edges is not null && edges.IsPinned(canvasX, canvasY))
                {
                    CopyPixel(source, canvasX, canvasY, destination, target);
                }
                else if (smooth)
                {
                    SampleBilinear(source, sampleX, sampleY, destination, target);
                }
                else if (edges is not null)
                {
                    (int resolvedX, int resolvedY) = edges.ResolveSample(sampleX, sampleY);
                    CopyPixel(source, resolvedX, resolvedY, destination, target);
                }
                else
                {
                    CopyPixel(source, (int)Math.Floor(sampleX + 0.5), (int)Math.Floor(sampleY + 0.5), destination, target);
                }
            }
        }

        edges?.Draw(destination, area);
    }

    private void Twirl(VecD center, double radius, double angle)
    {
        // A large radius moves the rim a long way per stamp, so the sampling margin has to scale.
        Stamp(center, radius, (Math.Abs(angle) * radius) + 2, (fromCenter, weight) =>
        {
            double turn = angle * weight;
            double cos = Math.Cos(turn);
            double sin = Math.Sin(turn);
            return new VecD(
                (fromCenter.X * cos) - (fromCenter.Y * sin) - fromCenter.X,
                (fromCenter.X * sin) + (fromCenter.Y * cos) - fromCenter.Y);
        });
    }

    private void Scale(VecD center, double radius, double amount)
    {
        Stamp(center, radius, (Math.Abs(amount) * radius) + 2, (fromCenter, weight) => fromCenter * (amount * weight));
    }

    /// <summary>
    /// Moves the content under a circular brush by the given motion, on top of the warp so far.
    /// </summary>
    /// <param name="center">Brush centre, in pixel-centre coordinates.</param>
    /// <param name="margin">Furthest any pixel moves, so the snapshot of the old field covers it.</param>
    /// <param name="motion">How far content moves, from its offset to the centre and brush weight.</param>
    private void Stamp(VecD center, double radius, double margin, Func<VecD, double, VecD> motion)
    {
        double reach = radius + margin;
        int left = Math.Max(0, (int)Math.Floor(center.X - reach));
        int top = Math.Max(0, (int)Math.Floor(center.Y - reach));
        int right = Math.Min(width - 1, (int)Math.Ceiling(center.X + reach));
        int bottom = Math.Min(height - 1, (int)Math.Ceiling(center.Y + reach));
        if (right < left || bottom < top)
        {
            return;
        }

        int snapshotWidth = right - left + 1;
        int snapshotHeight = bottom - top + 1;
        float[] oldX = new float[snapshotWidth * snapshotHeight];
        float[] oldY = new float[snapshotWidth * snapshotHeight];
        for (int y = 0; y < snapshotHeight; y++)
        {
            Array.Copy(offsetX, ((top + y) * width) + left, oldX, y * snapshotWidth, snapshotWidth);
            Array.Copy(offsetY, ((top + y) * width) + left, oldY, y * snapshotWidth, snapshotWidth);
        }

        double radiusSquared = radius * radius;

        for (int y = top; y <= bottom; y++)
        {
            for (int x = left; x <= right; x++)
            {
                VecD fromCenter = new VecD(x, y) - center;
                double distanceSquared = fromCenter.LengthSquared;
                if (distanceSquared >= radiusSquared)
                {
                    continue;
                }

                VecD delta = motion(fromCenter, LiquifyEdges.BrushWeight(distanceSquared, radiusSquared));

                // Content arriving here left from (x,y) - delta, so this pixel now shows whatever
                // that point was showing before this stamp.
                (float carriedX, float carriedY) = SampleSnapshot(oldX, oldY, snapshotWidth, snapshotHeight,
                    x - delta.X - left, y - delta.Y - top);

                int index = (y * width) + x;
                offsetX[index] = carriedX - (float)delta.X;
                offsetY[index] = carriedY - (float)delta.Y;

                dirtyMinX = Math.Min(dirtyMinX, x);
                dirtyMaxX = Math.Max(dirtyMaxX, x);
                dirtyMinY = Math.Min(dirtyMinY, y);
                dirtyMaxY = Math.Max(dirtyMaxY, y);
            }
        }

        edges?.Advance(center, radius, reach, motion, MarkDirty);
    }

    private void MarkDirty(int x, int y)
    {
        x = Math.Clamp(x, 0, width - 1);
        y = Math.Clamp(y, 0, height - 1);
        dirtyMinX = Math.Min(dirtyMinX, x);
        dirtyMaxX = Math.Max(dirtyMaxX, x);
        dirtyMinY = Math.Min(dirtyMinY, y);
        dirtyMaxY = Math.Max(dirtyMaxY, y);
    }

    private static (float X, float Y) SampleSnapshot(float[] fieldX, float[] fieldY, int width, int height, double x, double y)
    {
        x = Math.Clamp(x, 0, width - 1);
        y = Math.Clamp(y, 0, height - 1);
        int left = (int)x;
        int top = (int)y;
        int right = Math.Min(left + 1, width - 1);
        int bottom = Math.Min(top + 1, height - 1);
        float tx = (float)(x - left);
        float ty = (float)(y - top);

        return (Interpolate(fieldX), Interpolate(fieldY));

        float Interpolate(float[] field)
        {
            float upper = field[(top * width) + left] + ((field[(top * width) + right] - field[(top * width) + left]) * tx);
            float lower = field[(bottom * width) + left] + ((field[(bottom * width) + right] - field[(bottom * width) + left]) * tx);
            return upper + ((lower - upper) * ty);
        }
    }

    private void CopyPixel(byte[] source, int x, int y, byte[] destination, int target)
    {
        if (x < 0 || y < 0 || x >= width || y >= height)
        {
            destination[target] = destination[target + 1] = destination[target + 2] = destination[target + 3] = 0;
            return;
        }

        Buffer.BlockCopy(source, ((y * width) + x) * 4, destination, target, 4);
    }

    private void SampleBilinear(byte[] source, double x, double y, byte[] destination, int target)
    {
        int left = (int)Math.Floor(x);
        int top = (int)Math.Floor(y);
        double tx = x - left;
        double ty = y - top;

        for (int channel = 0; channel < 4; channel++)
        {
            double upper = (Read(left, top, channel) * (1 - tx)) + (Read(left + 1, top, channel) * tx);
            double lower = (Read(left, top + 1, channel) * (1 - tx)) + (Read(left + 1, top + 1, channel) * tx);
            destination[target + channel] = (byte)Math.Clamp(Math.Round((upper * (1 - ty)) + (lower * ty)), 0, 255);
        }

        byte Read(int readX, int readY, int channel)
        {
            if (edges is not null)
            {
                (readX, readY) = edges.ResolveSample(readX, readY);
            }

            return readX < 0 || readY < 0 || readX >= width || readY >= height
                ? (byte)0
                : source[(((readY * width) + readX) * 4) + channel];
        }
    }
}
