using Drawie.Numerics;

namespace PixiEditor.Helpers;

public static class GeometryHelper
{
    public static VecD Get45IncrementedPosition(VecD startPos, VecD curPos)
    {
        VecD[] positions =
        [
            curPos.ProjectOntoLine(startPos, startPos + new VecD(1, 1)),
            curPos.ProjectOntoLine(startPos, startPos + new VecD(1, -1)),
            curPos.ProjectOntoLine(startPos, startPos + new VecD(1, 0)),
            curPos.ProjectOntoLine(startPos, startPos + new VecD(0, 1))
        ];

        VecD closest = positions[0];
        double minDistSq = double.MaxValue;
        foreach (var pos in positions)
        {
            double distSq = (pos - curPos).LengthSquared;
            if (distSq < minDistSq)
            {
                minDistSq = distSq;
                closest = pos;
            }
        }

        return closest;
    }

    public static VecD Get45IncrementedPositionAlignedWithPixelCenters(VecD startPos, VecD curPos)
    {
        VecD snapped = Get45IncrementedPosition(startPos.Floor() + new VecD(0.5, 0.5), curPos); 
        
        // in the 45 degree case `snapped` can end up directly at the intersection of 4 pixels, which is ambiguous
        if ((snapped - snapped.Round()).LengthSquared < 0.01)
        {
            snapped -= (curPos - startPos).Signs() * 0.5;
        }
        
        return snapped.Floor() + new VecD(0.5, 0.5);
    }

    /// <summary>
    /// The directions an isometric-locked line may run in, as exact integer pixel steps.
    /// </summary>
    /// <remarks>
    /// 2:1 is the pixel-art isometric standard - two pixels across for every one down, which is
    /// what makes a tile twice as wide as it is tall and lets neighbouring tiles interlock. 1:2 is
    /// its steep counterpart, used for the upright faces, and the pure horizontal and vertical
    /// cover tile edges and wall seams. 1:1 (45 degrees) is deliberately absent: it is not an
    /// isometric angle, it sits exactly between 2:1 and 1:2 so it would be easy to hit by accident,
    /// and holding Shift already snaps to it.
    /// </remarks>
    private static readonly VecD[] isometricDirections =
    [
        new VecD(1, 0), new VecD(-1, 0),
        new VecD(0, 1), new VecD(0, -1),
        new VecD(2, 1), new VecD(2, -1), new VecD(-2, 1), new VecD(-2, -1),
        new VecD(1, 2), new VecD(1, -2), new VecD(-1, 2), new VecD(-1, -2)
    ];

    /// <summary>
    /// Snaps <paramref name="curPos"/> to the nearest isometric direction from
    /// <paramref name="startPos"/>, landing on a whole number of steps along it.
    /// </summary>
    /// <remarks>
    /// Rounding to whole steps is the point, not a detail: a 2:1 line only rasterizes into the even
    /// two-across-one-down staircase pixel art expects if its length is a multiple of the step. An
    /// endpoint snapped to the right angle but a fractional length draws a staircase with one odd
    /// tread at the end, which is exactly the artefact an isometric lock exists to prevent. The
    /// length is always at least one full step, so a short drag still produces a usable iso line
    /// rather than a single pixel.
    /// </remarks>
    public static VecD GetIsometricPosition(VecD startPos, VecD curPos)
    {
        VecD delta = curPos - startPos;
        if (delta.LengthSquared <= 0)
            return startPos;

        VecD best = startPos;
        double bestDistSq = double.MaxValue;

        foreach (VecD direction in isometricDirections)
        {
            double steps = (delta.X * direction.X + delta.Y * direction.Y) / direction.LengthSquared;

            // Every direction has its mirror in the list, so dropping the ones pointing away from
            // the cursor leaves the candidates in the quadrant the user is actually dragging into.
            if (steps <= 0)
                continue;

            VecD candidate = startPos + direction * Math.Max(1, Math.Round(steps));
            double distSq = (candidate - curPos).LengthSquared;
            if (distSq < bestDistSq)
            {
                bestDistSq = distSq;
                best = candidate;
            }
        }

        return best;
    }

    /// <summary>
    /// <see cref="GetIsometricPosition"/> for the raster tools, which work from pixel centres.
    /// </summary>
    /// <remarks>
    /// The anchor is moved to the centre of the pixel it falls in and the offsets added to it are
    /// whole pixels, so the result is a pixel centre too and the rasterizer cannot round the
    /// endpoint into the neighbouring pixel and break the staircase.
    /// </remarks>
    public static VecD GetIsometricPositionAlignedWithPixelCenters(VecD startPos, VecD curPos)
    {
        return GetIsometricPosition(startPos.Floor() + new VecD(0.5, 0.5), curPos);
    }
}
