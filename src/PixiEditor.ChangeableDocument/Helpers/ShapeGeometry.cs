using Drawie.Numerics;

namespace PixiEditor.ChangeableDocument.Helpers;

/// <summary>The shapes the Shape tool can draw.</summary>
public enum ShapeKind
{
    Rectangle,
    Ellipse,
    Triangle,
    RightTriangle,
    Diamond,

    /// <summary>A regular polygon with as many sides as the point count asks for.</summary>
    Polygon,
    Star,
    Heart,

    /// <summary>A plus sign.</summary>
    Cross,
    Arrow
}

/// <summary>
/// Builds the outline of each shape, as points in the box the artist dragged out.
/// </summary>
/// <remarks>
/// Every shape is defined in a unit box running from (0,0) to (1,1) and then stretched onto that
/// box, so a shape always fills the drag exactly and the definitions stay readable. Y runs
/// downwards, as it does on the canvas, so y = 0 is the top.
///
/// Only the straight-edged shapes come out as points; the ellipse and the heart are curves, and
/// <see cref="CurvedOutline"/> gives them as cubic segments. Nothing here touches a canvas, so all
/// of it can be tested on plain numbers.
/// </remarks>
public static class ShapeGeometry
{
    /// <summary>Sides, or star points, when the tool offers no choice.</summary>
    public const int DefaultPoints = 5;

    /// <summary>How far a star's inner points sit from its centre, as a share of the outer ones.</summary>
    public const double DefaultInnerRatio = 0.5;

    private const double Third = 1.0 / 3;
    private const double TwoThirds = 2.0 / 3;

    /// <summary>A plus sign whose arms are a third of the box wide.</summary>
    private static readonly VecD[] CrossOutline =
    [
        new(Third, 0), new(TwoThirds, 0), new(TwoThirds, Third), new(1, Third),
        new(1, TwoThirds), new(TwoThirds, TwoThirds), new(TwoThirds, 1), new(Third, 1),
        new(Third, TwoThirds), new(0, TwoThirds), new(0, Third), new(Third, Third)
    ];

    /// <summary>An arrow pointing right, with a head two fifths of the box long.</summary>
    private static readonly VecD[] ArrowOutline =
    [
        new(0, 0.25), new(0.6, 0.25), new(0.6, 0), new(1, 0.5),
        new(0.6, 1), new(0.6, 0.75), new(0, 0.75)
    ];

    private static readonly VecD[] TriangleOutline = [new(0.5, 0), new(1, 1), new(0, 1)];

    private static readonly VecD[] RightTriangleOutline = [new(0, 0), new(0, 1), new(1, 1)];

    private static readonly VecD[] DiamondOutline = [new(0.5, 0), new(1, 0.5), new(0.5, 1), new(0, 0.5)];

    private static readonly VecD[] RectangleOutline = [new(0, 0), new(1, 0), new(1, 1), new(0, 1)];

    /// <summary>
    /// A heart, as cubic segments from the bottom point up the left side, across the two lobes and
    /// back down the right. Each segment is two control points and an end point.
    /// </summary>
    private static readonly VecD[] HeartCurve =
    [
        new(0.5, 1),
        new(0.15, 0.72), new(0, 0.52), new(0, 0.3),
        new(0, 0.08), new(0.17, 0), new(0.32, 0),
        new(0.42, 0), new(0.47, 0.06), new(0.5, 0.14),
        new(0.53, 0.06), new(0.58, 0), new(0.68, 0),
        new(0.83, 0), new(1, 0.08), new(1, 0.3),
        new(1, 0.52), new(0.85, 0.72), new(0.5, 1)
    ];

    /// <summary>Whether this shape is drawn as curves rather than as straight edges.</summary>
    public static bool IsCurved(ShapeKind kind) => kind is ShapeKind.Ellipse or ShapeKind.Heart;

    /// <summary>Whether the point count changes this shape.</summary>
    public static bool UsesPoints(ShapeKind kind) => kind is ShapeKind.Polygon or ShapeKind.Star;

    /// <summary>The corners of a straight-edged shape, in order, inside <paramref name="bounds"/>.</summary>
    /// <param name="points">Sides for a polygon, or points for a star. Clamped to at least 3.</param>
    /// <param name="innerRatio">How far a star's inner points reach, from 0 to 1.</param>
    /// <returns>An empty array for the curved shapes, which have no corners.</returns>
    public static VecD[] Outline(ShapeKind kind, RectD bounds, int points = DefaultPoints,
        double innerRatio = DefaultInnerRatio)
    {
        VecD[] unit = kind switch
        {
            ShapeKind.Rectangle => RectangleOutline,
            ShapeKind.Triangle => TriangleOutline,
            ShapeKind.RightTriangle => RightTriangleOutline,
            ShapeKind.Diamond => DiamondOutline,
            ShapeKind.Cross => CrossOutline,
            ShapeKind.Arrow => ArrowOutline,
            ShapeKind.Polygon => Radial(Math.Max(3, points), 1, 1),
            ShapeKind.Star => Radial(Math.Max(3, points), 2, Math.Clamp(innerRatio, 0, 1)),
            _ => []
        };

        VecD[] outline = new VecD[unit.Length];
        for (int i = 0; i < unit.Length; i++)
        {
            outline[i] = Place(unit[i], bounds);
        }

        return outline;
    }

    /// <summary>
    /// The cubic segments of a curved shape inside <paramref name="bounds"/>: a start point followed
    /// by groups of three, each two control points and an end point.
    /// </summary>
    /// <returns>An empty array for shapes that are not curved, and for the ellipse, which is an oval.</returns>
    public static VecD[] CurvedOutline(ShapeKind kind, RectD bounds)
    {
        if (kind != ShapeKind.Heart)
        {
            return [];
        }

        VecD[] curve = new VecD[HeartCurve.Length];
        for (int i = 0; i < HeartCurve.Length; i++)
        {
            curve[i] = Place(HeartCurve[i], bounds);
        }

        return curve;
    }

    /// <summary>
    /// Points spaced evenly around the box, starting at the top and going clockwise. With
    /// <paramref name="ringsPerStep"/> of 2 every second point is pulled in by
    /// <paramref name="innerRatio"/>, which is what makes a star rather than a polygon.
    /// </summary>
    private static VecD[] Radial(int points, int ringsPerStep, double innerRatio)
    {
        int count = points * ringsPerStep;
        VecD[] unit = new VecD[count];

        for (int i = 0; i < count; i++)
        {
            // Straight up is -90 degrees, because y runs downwards.
            double angle = (-Math.PI / 2) + (i * 2 * Math.PI / count);
            double reach = ringsPerStep == 2 && i % 2 == 1 ? innerRatio : 1;

            // The unit circle spans -1 to 1, so it is halved to land inside the unit box.
            unit[i] = new VecD(0.5 + (Math.Cos(angle) * reach * 0.5), 0.5 + (Math.Sin(angle) * reach * 0.5));
        }

        return unit;
    }

    private static VecD Place(VecD unit, RectD bounds) =>
        new(bounds.X + (unit.X * bounds.Width), bounds.Y + (unit.Y * bounds.Height));
}
