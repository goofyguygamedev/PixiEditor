using System.Linq;
using PixiEditor.ChangeableDocument.Changeables.Graph.Interfaces.Shapes;
using Drawie.Backend.Core.Numerics;
using Drawie.Backend.Core.Surfaces;
using Drawie.Backend.Core.Surfaces.PaintImpl;
using Drawie.Backend.Core.Vector;
using Drawie.Numerics;

namespace PixiEditor.ChangeableDocument.Changeables.Graph.Nodes.Shapes.Data;

public class LineVectorData : ShapeVectorData, IReadOnlyLineData
{
    public VecD Start { get; set; }
    public VecD End { get; set; }

    /// <summary>
    /// Points between <see cref="Start"/> and <see cref="End"/> that bend the line. Empty means a
    /// plain straight line, which keeps the original two-point behaviour untouched.
    /// </summary>
    public List<VecD> ControlPoints { get; set; } = new();

    /// <summary>
    /// How rounded the corners at each point are. 0 draws straight segments between the points,
    /// 1 is a full Catmull-Rom curve through them.
    /// </summary>
    public double Smoothness { get; set; } = 1.0;

    // Explicit, because the interface exposes the points read-only while this class needs a
    // mutable list for building, serializing and transforming them.
    IReadOnlyList<VecD> IReadOnlyLineData.ControlPoints => ControlPoints;

    /// <summary>True when there is at least one intermediate point, so the shape is not a plain line.</summary>
    public bool HasCurve => ControlPoints.Count > 0;

    /// <summary>Start, the intermediate points, then End, in draw order.</summary>
    public List<VecD> AllPoints()
    {
        List<VecD> points = new(ControlPoints.Count + 2) { Start };
        points.AddRange(ControlPoints);
        points.Add(End);
        return points;
    }

    public VecD TransformedStart
    {
        get => TransformationMatrix.MapPoint(Start);
        set => Start = TransformationMatrix.Invert().MapPoint(value);
    }

    public VecD TransformedEnd
    {
        get => TransformationMatrix.MapPoint(End);
        set => End = TransformationMatrix.Invert().MapPoint(value);
    }

    public override RectD GeometryAABB
    {
        get
        {
            if (HasCurve)
            {
                // A Catmull-Rom curve can overshoot its control points, so the real path bounds
                // are needed rather than the bounding box of the points.
                using VectorPath curvePath = ToPath();
                return curvePath.TightBounds.Inflate(StrokeWidth / 2);
            }

            var dir = (End - Start).Normalize();
            var cross = new VecD(-dir.Y, dir.X);
            VecD offset = cross * StrokeWidth / 2;

            VecD topLeft = Start + offset;
            VecD bottomRight = End - offset;
            VecD bottomLeft = Start - offset;
            VecD topRight = End + offset;

            ShapeCorners corners = new ShapeCorners()
            {
                TopLeft = topLeft, BottomRight = bottomRight, BottomLeft = bottomLeft, TopRight = topRight
            };

            return corners.AABBBounds;
        }
    }

    public override RectD VisualAABB => GeometryAABB;

    public override ShapeCorners TransformationCorners => new ShapeCorners(GeometryAABB)
        .WithMatrix(TransformationMatrix);


    public LineVectorData(VecD startPos, VecD endPos)
    {
        Start = startPos;
        End = endPos;

        Fill = false;
    }

    public override void RasterizeGeometry(Canvas canvas)
    {
        Rasterize(canvas, false);
    }

    public override void RasterizeTransformed(Canvas canvas)
    {
        Rasterize(canvas, true);
    }

    private void Rasterize(Canvas canvas, bool applyTransform)
    {
        int num = 0;
        if (applyTransform)
        {
            num = canvas.Save();
            ApplyTransformTo(canvas);
        }

        using Paint paint = new Paint() { IsAntiAliased = true };

        paint.SetPaintable(Stroke);
        paint.Style = PaintStyle.Stroke;
        paint.StrokeWidth = StrokeWidth;

        if (HasCurve)
        {
            using VectorPath path = ToPath();
            canvas.DrawPath(path, paint);
        }
        else
        {
            canvas.DrawLine(Start, End, paint);
        }

        if (applyTransform)
        {
            canvas.RestoreToCount(num);
        }
    }

    public override bool IsValid()
    {
        return Start != End || HasCurve;
    }

    protected override int GetSpecificHash()
    {
        HashCode hash = new();
        hash.Add(Start);
        hash.Add(End);
        hash.Add(Smoothness);
        foreach (VecD point in ControlPoints)
        {
            hash.Add(point);
        }

        return hash.ToHashCode();
    }

    public override VectorPath ToPath(bool transformed = false)
    {
        VectorPath path = new VectorPath();

        if (!HasCurve)
        {
            path.MoveTo((VecF)Start);
            path.LineTo((VecF)End);
        }
        else
        {
            AppendCurve(path);
        }

        if (transformed)
        {
            path.Transform(TransformationMatrix);
        }

        return path;
    }

    private void AppendCurve(VectorPath path) =>
        LineCurveHelper.AppendTo(path, AllPoints(), Smoothness);

    protected bool Equals(LineVectorData other)
    {
        return base.Equals(other) && Start.Equals(other.Start) && End.Equals(other.End)
               && Smoothness.Equals(other.Smoothness) && ControlPoints.SequenceEqual(other.ControlPoints);
    }

    public override bool Equals(object? obj)
    {
        if (obj is null)
        {
            return false;
        }

        if (ReferenceEquals(this, obj))
        {
            return true;
        }

        if (obj.GetType() != GetType())
        {
            return false;
        }

        return Equals((LineVectorData)obj);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(base.GetHashCode(), Start, End, Smoothness, ControlPoints.Count);
    }
}
