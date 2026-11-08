using System.Windows.Input;
using Avalonia;
using Avalonia.Input;
using PixiEditor.Models.Controllers.InputDevice;
using Drawie.Backend.Core.Numerics;
using Drawie.Backend.Core.Surfaces;
using Drawie.Backend.Core.Surfaces.PaintImpl;
using Drawie.Backend.Core.Vector;
using PixiEditor.ChangeableDocument.Changeables.Graph.Nodes.Shapes.Data;
using PixiEditor.Extensions.UI.Overlays;
using PixiEditor.Helpers;
using Drawie.Numerics;
using System.Linq;
using PixiEditor.Views.Overlays.Drawables;
using PixiEditor.Views.Overlays.Handles;
using PixiEditor.Views.Overlays.TransformOverlay;
using Colors = Drawie.Backend.Core.ColorsImpl.Colors;

namespace PixiEditor.Views.Overlays.LineToolOverlay;

internal class LineToolOverlay : Overlay
{
    public static readonly StyledProperty<VecD> LineStartProperty =
        AvaloniaProperty.Register<LineToolOverlay, VecD>(nameof(LineStart), defaultValue: VecD.Zero);

    public VecD LineStart
    {
        get => GetValue(LineStartProperty);
        set => SetValue(LineStartProperty, value);
    }

    public static readonly StyledProperty<VecD> LineEndProperty =
        AvaloniaProperty.Register<LineToolOverlay, VecD>(nameof(LineEnd), defaultValue: VecD.Zero);

    public VecD LineEnd
    {
        get => GetValue(LineEndProperty);
        set => SetValue(LineEndProperty, value);
    }

    public static readonly StyledProperty<ICommand?> ActionCompletedProperty =
        AvaloniaProperty.Register<LineToolOverlay, ICommand?>(nameof(ActionCompleted));

    public ICommand? ActionCompleted
    {
        get => GetValue(ActionCompletedProperty);
        set => SetValue(ActionCompletedProperty, value);
    }

    public static readonly StyledProperty<SnappingController> SnappingControllerProperty =
        AvaloniaProperty.Register<LineToolOverlay, SnappingController>(
            nameof(SnappingController));

    public SnappingController SnappingController
    {
        get => GetValue(SnappingControllerProperty);
        set => SetValue(SnappingControllerProperty, value);
    }

    public static readonly StyledProperty<bool> ShowHandlesProperty = AvaloniaProperty.Register<LineToolOverlay, bool>(
        nameof(ShowHandles), defaultValue: true);

    public bool ShowHandles
    {
        get => GetValue(ShowHandlesProperty);
        set => SetValue(ShowHandlesProperty, value);
    }

    public static readonly StyledProperty<bool> IsSizeBoxEnabledProperty =
        AvaloniaProperty.Register<LineToolOverlay, bool>(
            nameof(IsSizeBoxEnabled));

    public bool IsSizeBoxEnabled
    {
        get => GetValue(IsSizeBoxEnabledProperty);
        set => SetValue(IsSizeBoxEnabledProperty, value);
    }

    public static readonly StyledProperty<ICommand> AddToUndoCommandProperty = AvaloniaProperty.Register<LineToolOverlay, ICommand>(
        nameof(AddToUndoCommand));

    public ICommand AddToUndoCommand
    {
        get => GetValue(AddToUndoCommandProperty);
        set => SetValue(AddToUndoCommandProperty, value);
    }

    public static readonly StyledProperty<IReadOnlyList<VecD>> ControlPointsProperty =
        AvaloniaProperty.Register<LineToolOverlay, IReadOnlyList<VecD>>(
            nameof(ControlPoints), defaultValue: []);

    /// <summary>
    /// The draggable points between <see cref="LineStart"/> and <see cref="LineEnd"/> that bend the
    /// line. One handle is shown per point; an empty list is a plain straight line with no extra
    /// handles, which is the behaviour the tool had before curves existed.
    /// </summary>
    public IReadOnlyList<VecD> ControlPoints
    {
        get => GetValue(ControlPointsProperty);
        set => SetValue(ControlPointsProperty, value);
    }

    public static readonly StyledProperty<double> SmoothnessProperty =
        AvaloniaProperty.Register<LineToolOverlay, double>(nameof(Smoothness), defaultValue: 1d);

    /// <summary>Only used to draw the guide curve; the real geometry is built by the executor.</summary>
    public double Smoothness
    {
        get => GetValue(SmoothnessProperty);
        set => SetValue(SmoothnessProperty, value);
    }

    public static readonly StyledProperty<bool> IsometricLockProperty =
        AvaloniaProperty.Register<LineToolOverlay, bool>(nameof(IsometricLock));

    /// <summary>
    /// Constrains dragging either end to the pixel-art isometric angles, the same way the toolbar
    /// setting constrains drawing the line in the first place. Without it, a line drawn under the
    /// lock would come straight off its angle the moment an endpoint was nudged.
    /// </summary>
    public bool IsometricLock
    {
        get => GetValue(IsometricLockProperty);
        set => SetValue(IsometricLockProperty, value);
    }

    static LineToolOverlay()
    {
        LineStartProperty.Changed.Subscribe(RenderAffectingPropertyChanged);
        LineEndProperty.Changed.Subscribe(RenderAffectingPropertyChanged);
        SmoothnessProperty.Changed.Subscribe(SmoothnessChanged);
        ControlPointsProperty.Changed.Subscribe(ControlPointsChanged);
    }


    private Paint blackPaint = new Paint()
    {
        Color = Colors.Black, StrokeWidth = 1, Style = PaintStyle.Stroke, IsAntiAliased = true
    };

    /// <summary>Thin dashed hint of the curve, so the shape is readable while dragging a point.</summary>
    private Paint guidePaint = new Paint()
    {
        Color = Colors.White, StrokeWidth = 1, Style = PaintStyle.Stroke, IsAntiAliased = true
    };

    private VecD mouseDownPos = VecD.Zero;
    private VecD lineStartOnMouseDown = VecD.Zero;
    private VecD lineEndOnMouseDown = VecD.Zero;
    private List<VecD> controlPointsOnMouseDown = new();

    private VecD lastMousePos = VecD.Zero;

    private bool movedWhileMouseDown = false;

    private RectangleHandle startHandle;
    private RectangleHandle endHandle;
    private TransformHandle moveHandle;
    private readonly List<ControlPointHandle> controlHandles = new();

    private bool isDraggingHandle = false;
    private InfoBox infoBox;

    private VecD startPos;
    private VecD endPos;

    public LineToolOverlay()
    {
        Cursor = new Cursor(StandardCursorType.Arrow);

        startHandle = new AnchorHandle(this);
        startHandle.StrokePaint = blackPaint;
        startHandle.OnPress += OnHandlePress;
        startHandle.OnDrag += StartHandleOnDrag;
        startHandle.OnHover += (handle, _) => Refresh();
        startHandle.OnRelease += OnHandleRelease;
        startHandle.Cursor = new Cursor(StandardCursorType.Arrow);
        AddHandle(startHandle);

        endHandle = new AnchorHandle(this);
        endHandle.StrokePaint = blackPaint;
        endHandle.OnPress += OnHandlePress;
        endHandle.OnDrag += EndHandleOnDrag;
        endHandle.Cursor = new Cursor(StandardCursorType.Arrow);
        endHandle.OnHover += (handle, _) => Refresh();
        endHandle.OnRelease += OnHandleRelease;
        AddHandle(endHandle);

        moveHandle = new TransformHandle(this);
        moveHandle.StrokePaint = blackPaint;
        moveHandle.OnPress += OnHandlePress;
        moveHandle.OnDrag += MoveHandleOnDrag;
        moveHandle.OnRelease += OnHandleRelease;
        endHandle.Cursor = new Cursor(StandardCursorType.Arrow);
        moveHandle.OnHover += (handle, _) => Refresh();
        moveHandle.OnRelease += OnHandleRelease;
        AddHandle(moveHandle);

        infoBox = new InfoBox();

        SyncControlHandles();
    }

    /// <summary>
    /// Grows or shrinks the pool of curve handles to match <see cref="ControlPoints"/>. Handles are
    /// identified by their index in <see cref="controlHandles"/>, which is what the drag handler maps
    /// back to a point, so the two lists must stay the same length.
    /// </summary>
    private void SyncControlHandles()
    {
        int wanted = ControlPoints?.Count ?? 0;

        while (controlHandles.Count > wanted)
        {
            ControlPointHandle extra = controlHandles[^1];
            controlHandles.RemoveAt(controlHandles.Count - 1);
            RemoveHandle(extra);
        }

        while (controlHandles.Count < wanted)
        {
            ControlPointHandle handle = new(this)
            {
                StrokePaint = blackPaint, Cursor = new Cursor(StandardCursorType.Arrow)
            };

            handle.OnPress += OnHandlePress;
            handle.OnDrag += ControlHandleOnDrag;
            handle.OnHover += (_, _) => Refresh();
            handle.OnRelease += OnHandleRelease;

            controlHandles.Add(handle);
            AddHandle(handle);
        }
    }

    private void ControlHandleOnDrag(Handle source, OverlayPointerArgs args)
    {
        if (source is not ControlPointHandle handle)
            return;

        int index = controlHandles.IndexOf(handle);
        if (index < 0 || index >= controlPointsOnMouseDown.Count)
            return;

        VecD delta = args.Point - mouseDownPos;

        // Replaced wholesale rather than mutated in place: the bound view model only notifies on a
        // new list, and a silent mutation would leave the drawn line stale.
        List<VecD> updated = new(controlPointsOnMouseDown);
        updated[index] = SnapAndHighlight(controlPointsOnMouseDown[index] + delta);
        ControlPoints = updated;

        movedWhileMouseDown = true;
        isDraggingHandle = true;
        lastMousePos = args.Point;
        IsSizeBoxEnabled = true;
    }

    protected override void OnOverlayPointerMoved(OverlayPointerArgs args)
    {
        base.OnOverlayPointerMoved(args);

        lastMousePos = args.Point;
    }

    public override bool TestHit(VecD point)
    {
        return IsVisible;
    }

    private void OnHandleRelease(Handle obj, OverlayPointerArgs args)
    {
        if (SnappingController != null)
        {
            SnappingController.HighlightedXAxis = null;
            SnappingController.HighlightedYAxis = null;
            SnappingController.HighlightedPoint = null;
            Refresh();
        }

        isDraggingHandle = false;
        IsSizeBoxEnabled = false;
        
        AddToUndoCommand?.Execute((LineStart, LineEnd));
    }

    protected override void ZoomChanged(double newZoom)
    {
        blackPaint.StrokeWidth = 1 / (float)newZoom;
        guidePaint.StrokeWidth = 1 / (float)newZoom;
    }

    protected override void OnRenderOverlay(Canvas context, RectD canvasBounds)
    {
        VecD mappedStart = LineStart;
        VecD mappedEnd = LineEnd;

        startHandle.Position = mappedStart;
        endHandle.Position = mappedEnd;

        IReadOnlyList<VecD> points = ControlPoints ?? [];
        for (int i = 0; i < controlHandles.Count && i < points.Count; i++)
        {
            controlHandles[i].Position = points[i];
        }

        VecD center = (mappedStart + mappedEnd) / 2;
        VecD size = mappedEnd - mappedStart;

        moveHandle.Position = TransformHelper.GetHandlePos(new ShapeCorners(center, size), ZoomScale, moveHandle.Size);

        if (ShowHandles)
        {
            if (points.Count > 0)
                DrawCurveGuide(context, mappedStart, mappedEnd, points);

            startHandle.Draw(context);
            endHandle.Draw(context);

            foreach (ControlPointHandle handle in controlHandles)
            {
                handle.Draw(context);
            }

            moveHandle.Draw(context);
        }

        if (IsSizeBoxEnabled)
        {
            int toRestore = context.Save();
            var matrix = context.TotalMatrix;
            VecD pos = matrix.MapPoint(lastMousePos);
            context.SetMatrix(Matrix3X3.Identity);

            string length = $"L: {(mappedEnd - mappedStart).Length:0.#} px";
            infoBox.DrawInfo(context, length, pos);

            context.RestoreToCount(toRestore);
        }
    }

    protected override void OnOverlayPointerPressed(OverlayPointerArgs args)
    {
        if (args.PointerButton != MouseButton.Left)
            return;

        movedWhileMouseDown = false;
        mouseDownPos = args.Point;

        lineStartOnMouseDown = LineStart;
        lineEndOnMouseDown = LineEnd;
        controlPointsOnMouseDown = (ControlPoints ?? []).ToList();

        args.Pointer.Capture(this);
    }

    private void StartHandleOnDrag(Handle source, OverlayPointerArgs args)
    {
        VecD delta = args.Point - mouseDownPos;
        LineStart = ConstrainOrSnap(lineStartOnMouseDown + delta, LineEnd);
        movedWhileMouseDown = true;

        lastMousePos = args.Point;
        isDraggingHandle = true;
        IsSizeBoxEnabled = true;
    }

    private void EndHandleOnDrag(Handle source, OverlayPointerArgs args)
    {
        VecD delta = args.Point - mouseDownPos;
        VecD final = ConstrainOrSnap(lineEndOnMouseDown + delta, LineStart);

        LineEnd = final;
        movedWhileMouseDown = true;

        isDraggingHandle = true;
        lastMousePos = args.Point;
        IsSizeBoxEnabled = true;
    }

    /// <summary>
    /// Applies whichever constraint is in force to an endpoint being dragged: the isometric lattice
    /// measured from <paramref name="anchor"/>, the end that is staying put, or else the document's
    /// ordinary snapping.
    /// </summary>
    /// <remarks>
    /// The two are deliberately exclusive. Document snapping moves the endpoint onto another
    /// object's bounds, which is a fraction of a pixel off the lattice in the general case, and an
    /// isometric line that is a fraction of a pixel off is no longer isometric once rasterized.
    /// </remarks>
    private VecD ConstrainOrSnap(VecD position, VecD anchor)
    {
        if (!IsometricLock)
            return SnapAndHighlight(position);

        ClearSnapHighlight();
        return GeometryHelper.GetIsometricPosition(anchor, position);
    }

    private void ClearSnapHighlight()
    {
        if (SnappingController == null)
            return;

        SnappingController.HighlightedXAxis = null;
        SnappingController.HighlightedYAxis = null;
        SnappingController.HighlightedPoint = null;
    }

    private VecD SnapAndHighlight(VecD position)
    {
        VecD final = position;
        if (SnappingController != null)
        {
            double? x = SnappingController.SnapToHorizontal(position.X, out string snapAxisX);
            double? y = SnappingController.SnapToVertical(position.Y, out string snapAxisY);

            if (x.HasValue)
            {
                final = new VecD(x.Value, final.Y);
            }

            if (y.HasValue)
            {
                final = new VecD(final.X, y.Value);
            }

            SnappingController.HighlightedXAxis = snapAxisX;
            SnappingController.HighlightedYAxis = snapAxisY;
            SnappingController.HighlightedPoint = x != null || y != null ? final : null;
        }

        return final;
    }

    private void OnHandlePress(Handle source, OverlayPointerArgs args)
    {
        movedWhileMouseDown = false;
        mouseDownPos = args.Point;

        lineStartOnMouseDown = LineStart;
        lineEndOnMouseDown = LineEnd;
        controlPointsOnMouseDown = (ControlPoints ?? []).ToList();
    }

    private void MoveHandleOnDrag(Handle source, OverlayPointerArgs args)
    {
        var delta = args.Point - mouseDownPos;

        VecD mappedStart = lineStartOnMouseDown;
        VecD mappedEnd = lineEndOnMouseDown;

        ((string, string), VecD) snapDeltaResult = TrySnapLine(mappedStart, mappedEnd, delta, out VecD? snapSource);

        if (SnappingController != null)
        {
            SnappingController.HighlightedXAxis = snapDeltaResult.Item1.Item1;
            SnappingController.HighlightedYAxis = snapDeltaResult.Item1.Item2;
            SnappingController.HighlightedPoint = snapSource;
        }

        VecD totalDelta = delta + snapDeltaResult.Item2;

        // Control points ride along with the move, or dragging the line would reshape the curve.
        if (controlPointsOnMouseDown.Count > 0)
            ControlPoints = controlPointsOnMouseDown.Select(point => point + totalDelta).ToList();

        LineStart = lineStartOnMouseDown + totalDelta;
        LineEnd = lineEndOnMouseDown + totalDelta;

        movedWhileMouseDown = true;
    }

    protected override void OnOverlayPointerReleased(OverlayPointerArgs args)
    {
        IsSizeBoxEnabled = false;

        if (args.InitialPressMouseButton != MouseButton.Left)
            return;

        if (movedWhileMouseDown && ActionCompleted is not null && ActionCompleted.CanExecute(null))
            ActionCompleted.Execute(null);
    }

    private ((string, string), VecD) TrySnapLine(VecD originalStart, VecD originalEnd, VecD delta, out VecD? snapSource)
    {
        if (SnappingController == null)
        {
            snapSource = null;
            return ((string.Empty, string.Empty), delta);
        }

        VecD center = (originalStart + originalEnd) / 2f;
        VecD[] pointsToTest = new VecD[] { center + delta, originalStart + delta, originalEnd + delta, };

        VecD snapDelta =
            SnappingController.GetSnapDeltaForPoints(pointsToTest, out string snapAxisX, out string snapAxisY,
                out snapSource);

        if (snapSource != null)
        {
            snapSource += snapDelta;
        }

        return ((snapAxisX, snapAxisY), snapDelta);
    }


    /// <summary>
    /// Traces the same spline the shape data will build, so the handles visibly belong to the curve
    /// they are bending rather than floating next to a straight line.
    /// </summary>
    private void DrawCurveGuide(Canvas context, VecD start, VecD end, IReadOnlyList<VecD> controlPoints)
    {
        List<VecD> allPoints = new(controlPoints.Count + 2) { start };
        allPoints.AddRange(controlPoints);
        allPoints.Add(end);

        using VectorPath guide = LineCurveHelper.Build(allPoints, Smoothness);
        context.DrawPath(guide, guidePaint);
    }

    private static void RenderAffectingPropertyChanged(AvaloniaPropertyChangedEventArgs<VecD> e)
    {
        if (e.Sender is LineToolOverlay overlay)
        {
            overlay.Refresh();
        }
    }

    private static void SmoothnessChanged(AvaloniaPropertyChangedEventArgs<double> e)
    {
        if (e.Sender is LineToolOverlay overlay)
        {
            overlay.Refresh();
        }
    }

    private static void ControlPointsChanged(AvaloniaPropertyChangedEventArgs<IReadOnlyList<VecD>> e)
    {
        if (e.Sender is not LineToolOverlay overlay)
            return;

        overlay.SyncControlHandles();
        overlay.Refresh();
    }
}
