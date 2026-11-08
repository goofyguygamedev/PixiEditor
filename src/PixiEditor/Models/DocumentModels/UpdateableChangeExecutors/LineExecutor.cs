using Avalonia.Media;
using Drawie.Backend.Core.ColorsImpl.Paintables;
using PixiEditor.ChangeableDocument.Actions;
using PixiEditor.ChangeableDocument.Changeables.Graph.Interfaces.Shapes;
using PixiEditor.ChangeableDocument.Changeables.Graph.Nodes;
using PixiEditor.Extensions.CommonApi.Palettes;
using PixiEditor.Helpers.Extensions;
using PixiEditor.Models.Handlers;
using PixiEditor.Models.Handlers.Toolbars;
using PixiEditor.Models.Handlers.Tools;
using PixiEditor.Models.Tools;
using System.Linq;
using Drawie.Backend.Core.Vector;
using Drawie.Numerics;
using PixiEditor.ChangeableDocument.Changeables.Graph.Nodes.Shapes.Data;
using PixiEditor.Helpers;
using PixiEditor.ViewModels.Document.TransformOverlays;
using Color = Drawie.Backend.Core.ColorsImpl.Color;

namespace PixiEditor.Models.DocumentModels.UpdateableChangeExecutors;

internal abstract class LineExecutor<T> : SimpleShapeToolExecutor where T : ILineToolHandler
{
    public override ExecutorType Type => ExecutorType.ToolLinked;

    protected Paintable StrokePaintable => toolbar!.StrokeBrush.ToPaintable();
    protected double StrokeWidth => toolViewModel!.ToolSize;
    protected abstract bool UseGlobalUndo { get; }
    protected abstract bool ShowApplyButton { get; }

    /// <summary>How many curve points the toolbar is asking for right now.</summary>
    protected int CurvePointCount => toolViewModel?.CurvePointCount ?? 0;

    protected double CurveSmoothness => toolViewModel?.CurveSmoothness ?? 1d;

    /// <summary>Whether the toolbar is asking for isometric-locked angles right now.</summary>
    protected bool IsometricLock => toolViewModel?.IsometricLock ?? false;

    /// <summary>
    /// The curve points currently on the overlay - the single source of truth while a line is being
    /// edited, since that is what the user drags.
    /// </summary>
    protected IReadOnlyList<VecD> ControlPoints =>
        document?.LineToolOverlayHandler.ControlPoints ?? [];

    protected bool drawOnMask;

    protected VecD curPos;
    protected bool startedDrawing = false;
    private T? toolViewModel;
    private IColorsHandler? colorsVM;
    protected IShapeToolbar? toolbar;
    private bool ignoreNextColorChange = false;
    private VecD lastStartPos;

    private UndoStack<LineVectorData>? localUndoStack;

    public override bool CanUndo => !UseGlobalUndo && ActiveMode == ShapeToolMode.Transform && localUndoStack is { UndoCount: > 0 };
    public override bool CanRedo => !UseGlobalUndo && ActiveMode == ShapeToolMode.Transform && localUndoStack is { RedoCount: > 0 };

    public override ExecutionState Start()
    {
        if (base.Start() == ExecutionState.Error)
            return ExecutionState.Error;

        colorsVM = GetHandler<IColorsHandler>();
        toolViewModel = GetHandler<T>();
        IStructureMemberHandler? member = document?.SelectedStructureMember;
        toolbar = (IShapeToolbar)toolViewModel?.Toolbar;
        if (colorsVM is null || toolViewModel is null || member is null)
            return ExecutionState.Error;

        drawOnMask = member is not ILayerHandler layer || layer.ShouldDrawOnMask;
        if (drawOnMask && !member.HasMaskBindable)
            return ExecutionState.Error;
        if (!drawOnMask && member is not ILayerHandler)
            return ExecutionState.Error;

        if (member.IsLockedBindable)
            return ExecutionState.Error;
        
        localUndoStack = new UndoStack<LineVectorData>();

        if (ActiveMode == ShapeToolMode.Drawing)
        {
            if (toolbar.SyncWithPrimaryColor)
            {
                toolbar.StrokeBrush = new SolidColorBrush(colorsVM.PrimaryColor.ToColor());
                ignoreNextColorChange = colorsVM.ColorsTempSwapped;
            }

            document.LineToolOverlayHandler.Hide();
            // No curve points while the line is still being dragged out: they would have nowhere
            // sensible to sit, and the handles are hidden anyway. They get seeded on mouse up.
            document.LineToolOverlayHandler.Smoothness = CurveSmoothness;
            document.LineToolOverlayHandler.IsometricLock = IsometricLock;
            document.LineToolOverlayHandler.Show(startDrawingPos, startDrawingPos, false, AddToUndo);
            document.LineToolOverlayHandler.ShowHandles = false;
            document.LineToolOverlayHandler.IsSizeBoxEnabled = true;

            return ExecutionState.Success;
        }

        if (member is IVectorLayerHandler)
        {
            var node = (VectorLayerNode)internals.Tracker.Document.FindMember(member.Id);
            
            if(node is null)
                return ExecutionState.Error;

            if (node.EmbeddedShapeData is not IReadOnlyLineData data)
            {
                ActiveMode = ShapeToolMode.Preview;
                return ExecutionState.Success;
            }

            toolbar.StrokeBrush = data.Stroke.ToBrush();

            if (!InitShapeData(data))
            {
                ActiveMode = ShapeToolMode.Preview;
                return ExecutionState.Success;
            }

            ActiveMode = ShapeToolMode.Transform;

            // Reopening an existing line restores the curve it was saved with, so its handles come
            // back where the artist left them instead of snapping to the straight line.
            document.LineToolOverlayHandler.Smoothness = data.Smoothness;
            document.LineToolOverlayHandler.IsometricLock = IsometricLock;
            document.LineToolOverlayHandler.Show(
                data.TransformedStart, data.TransformedEnd, false, AddToUndo, data.ControlPoints);
            SyncControlPoints(data.TransformedStart, data.TransformedEnd);
        }
        else
        {
            ActiveMode = ShapeToolMode.Preview;
        }

        return ExecutionState.Success;
    }

    protected abstract bool InitShapeData(IReadOnlyLineData? data);
    protected abstract IAction DrawLine(VecD pos);
    protected abstract IAction TransformOverlayMoved(VecD start, VecD end);
    protected abstract IAction[] SettingsChange(string name, object value);
    protected abstract IAction EndDraw();

    protected override void PrecisePositionChangeDrawingMode(VecD pos)
    {
        startedDrawing = true;

        VecD endPos = pos;
        VecD snapped = endPos;
        string snapX = "";
        string snapY = "";

        VecD startPos = startDrawingPos;
        if (AlignToPixels)
        {
            startPos = startPos.Floor() + new VecD(0.5);
        }

        if (toolViewModel!.IsometricLock)
        {
            // Isometric lock takes precedence over both the Shift 45-degree snap and the document's
            // own snapping. Either would pull the endpoint off the isometric lattice by a fraction
            // of a pixel, which is the single thing the lock exists to prevent - so while it is on,
            // it is the only constraint applied.
            endPos = AlignToPixels
                ? GeometryHelper.GetIsometricPositionAlignedWithPixelCenters(startDrawingPos, pos)
                : GeometryHelper.GetIsometricPosition(startDrawingPos, pos);

            snapped = endPos;
        }
        else if (toolViewModel.Snap)
        {
            if (AlignToPixels)
            {
                endPos = GeometryHelper.Get45IncrementedPositionAlignedWithPixelCenters(startDrawingPos, pos);
            }
            else
            {
                endPos = GeometryHelper.Get45IncrementedPosition(startDrawingPos, pos);
            }

            VecD directionConstraint = endPos - startDrawingPos;
            snapped =
                document!.SnappingHandler.SnappingController.GetSnapPoint(endPos, directionConstraint, out snapX,
                    out snapY);
        }
        else
        {
            if (AlignToPixels)
            {
                endPos = endPos.Floor() + new VecD(0.5);
            }
            snapped = document!.SnappingHandler.SnappingController.GetSnapPoint(endPos, out snapX, out snapY);
        }

        if (toolViewModel.DrawFromCenter)
        {
            VecD center = startDrawingPos;
            startDrawingPos = center + (center - snapped);
        }

        HighlightSnapping(snapX, snapY, !string.IsNullOrEmpty(snapX) || !string.IsNullOrEmpty(snapY) ? snapped : null);
        document!.LineToolOverlayHandler.LineEnd = snapped;

        curPos = snapped;

        var drawLineAction = DrawLine(curPos);
        internals!.ActionAccumulator.AddActions(drawLineAction);

        lastStartPos = startDrawingPos;
        startDrawingPos = startPos;
    }

    public override void OnLeftMouseButtonUp(VecD argsPositionOnCanvas)
    {
        if (!startedDrawing)
        {
            internals.ActionAccumulator.AddFinishedActions(EndDraw());
            AddMembersToSnapping();
            
            base.OnLeftMouseButtonUp(argsPositionOnCanvas);
            ActiveMode = ShapeToolMode.Preview;
            onEnded!(this);
            return;
        }

        AddToUndo((lastStartPos, curPos));
        document!.LineToolOverlayHandler.Hide();
        document!.LineToolOverlayHandler.Smoothness = CurveSmoothness;
        document!.LineToolOverlayHandler.IsometricLock = IsometricLock;
        document!.LineToolOverlayHandler.Show(lastStartPos, curPos, ShowApplyButton, AddToUndo);

        // The line has its final length now, so this is the first point at which curve handles can
        // be placed along it.
        SyncControlPoints(lastStartPos, curPos);
        base.OnLeftMouseButtonUp(argsPositionOnCanvas);
    }

    public override void OnLineOverlayMoved(VecD start, VecD end)
    {
        if (ActiveMode != ShapeToolMode.Transform)
            return;

        var moveOverlayAction = TransformOverlayMoved(start, end);
        internals!.ActionAccumulator.AddActions(moveOverlayAction);

        startDrawingPos = start;
        curPos = end;
    }

    public override void OnColorChanged(Color color, bool primary)
    {
        if (!primary || !toolbar!.SyncWithPrimaryColor || ActiveMode == ShapeToolMode.Preview || ignoreNextColorChange)
        {
            if (primary)
            {
                ignoreNextColorChange = false;
            }

            return;
        }

        ignoreNextColorChange = ActiveMode == ShapeToolMode.Drawing;
        toolbar!.StrokeBrush = new SolidColorBrush(color.ToColor());
        var colorChangedAction = SettingsChange(nameof(IShapeToolbar.StrokeBrush), color);
        internals!.ActionAccumulator.AddActions(colorChangedAction);
    }

    public override void OnSelectedObjectNudged(VecI distance)
    {
        if (ActiveMode != ShapeToolMode.Transform)
            return;

        document!.LineToolOverlayHandler.Nudge(distance);
        AddToUndo((document.LineToolOverlayHandler.LineStart, document.LineToolOverlayHandler.LineEnd));
    }

    public override void OnSettingsChanged(string name, object value)
    {
        // The curve settings change the geometry rather than just how it is painted, so the overlay
        // has to be brought up to date before the change is rebuilt from it.
        if (ActiveMode == ShapeToolMode.Transform && document is not null)
        {
            if (name == LineToolSettings.CurveSharpness)
            {
                document.LineToolOverlayHandler.Smoothness = CurveSmoothness;
            }
            else if (name == LineToolSettings.CurvePoints)
            {
                SyncControlPoints(
                    document.LineToolOverlayHandler.LineStart, document.LineToolOverlayHandler.LineEnd);
            }
            else if (name == LineToolSettings.IsometricLock)
            {
                // Only changes what later drags are allowed to do; the line already on screen stays
                // put, since moving someone's endpoint for them on a toggle would be worse than
                // leaving a line drawn before the lock was on slightly off-angle.
                document.LineToolOverlayHandler.IsometricLock = IsometricLock;
            }
        }

        // Only build the actions when they are going to be used. SettingsChange constructs a
        // fresh curve VectorPath that the action takes ownership of, so building a set that is then
        // dropped on the floor leaks one path per settings change outside Transform mode.
        if (ActiveMode != ShapeToolMode.Transform)
            return;

        internals!.ActionAccumulator.AddFinishedActions(SettingsChange(name, value));
    }

    public override void OnMidChangeUndo()
    {
        if (ActiveMode != ShapeToolMode.Transform || localUndoStack == null)
            return;

        var undone = localUndoStack?.Undo();
        if (undone is not null)
        {
            ApplyState(undone);
        }
    }

    public override void OnMidChangeRedo()
    {
        if (ActiveMode != ShapeToolMode.Transform || localUndoStack == null)
            return;

        var redone = localUndoStack?.Redo();
        if (redone is not null)
        {
            ApplyState(redone);
        }
    }

    public override void OnTransformApplied()
    {
        base.OnTransformApplied();
        var endDrawAction = EndDraw();
        internals!.ActionAccumulator.AddFinishedActions(endDrawAction);

        if (StrokePaintable is ColorPaintable colorPaintable)
        {
            colorsVM.AddSwatch(new PaletteColor(colorPaintable.Color.R, colorPaintable.Color.G, colorPaintable.Color.B));
        }
    }

    public override void ForceStop()
    {
        base.ForceStop();
        var endDrawAction = EndDraw();
        internals!.ActionAccumulator.AddFinishedActions(endDrawAction);
    }

    protected override void StopTransformMode()
    {
        document!.LineToolOverlayHandler.Hide();
    }

    protected override void StartMode(ShapeToolMode mode)
    {
        base.StartMode(mode);
        if (mode == ShapeToolMode.Transform)
        {
            IReadOnlyList<VecD> keptPoints = ControlPoints.ToList();

            document!.LineToolOverlayHandler.Hide();
            document!.LineToolOverlayHandler.Smoothness = CurveSmoothness;
            document!.LineToolOverlayHandler.IsometricLock = IsometricLock;
            document!.LineToolOverlayHandler.Show(
                lastStartPos, curPos, ShowApplyButton, AddToUndo, keptPoints);
            SyncControlPoints(lastStartPos, curPos);
        }
    }

    private void AddToUndo((VecD, VecD) newPos)
    {
        if (UseGlobalUndo)
        {
            internals!.ActionAccumulator.AddFinishedActions(EndDraw(), TransformOverlayMoved(newPos.Item1, newPos.Item2), EndDraw());
        }
        else
        {
            localUndoStack!.AddState(ConstructLineData(newPos.Item1, newPos.Item2));
        }
    }

    protected LineVectorData ConstructLineData(VecD start, VecD end)
    {
        return new LineVectorData(start, end)
        {
            StrokeWidth = (float)StrokeWidth,
            Stroke = StrokePaintable,
            ControlPoints = ControlPoints.ToList(),
            Smoothness = CurveSmoothness
        };
    }

    /// <summary>
    /// Builds the path a curved line strokes, or null when the line is not actually bent and the
    /// plain straight-line path should be used instead. The caller owns the returned path.
    /// </summary>
    /// <remarks>
    /// Having curve points is not the same as being curved. They are seeded on the line, so a line
    /// drawn and left alone has points that bend it by nothing at all - and since curve points now
    /// default to on, returning a path for that case would quietly move every straight raster line
    /// off Bresenham and onto path stroking, which draws a subtly different set of pixels. Checking
    /// whether the points bend the line keeps the common case byte-identical to before.
    /// </remarks>
    protected VectorPath? BuildCurvePath(VecD start, VecD end)
    {
        IReadOnlyList<VecD> points = ControlPoints;
        if (points.Count == 0 || LineCurveHelper.IsStraight(start, end, points))
            return null;

        List<VecD> allPoints = new(points.Count + 2) { start };
        allPoints.AddRange(points);
        allPoints.Add(end);

        return LineCurveHelper.Build(allPoints, CurveSmoothness);
    }

    /// <summary>
    /// Brings the overlay's curve points in line with the toolbar's requested count, spread as
    /// evenly as possible along the curve the line currently describes.
    /// </summary>
    /// <remarks>
    /// Respacing all of the points - not only the new ones - is what keeps the handles usable. The
    /// old behaviour pinned existing points where they were and placed only the additions evenly,
    /// so as soon as one point had been dragged the rest ended up bunched around it. Sampling the
    /// curve the points already describe keeps the line's shape while evening out the handles along
    /// it. It only runs when the count actually changes, so a hand-shaped curve is never respaced
    /// behind the artist's back.
    /// </remarks>
    private void SyncControlPoints(VecD start, VecD end)
    {
        if (document is null)
            return;

        int wanted = CurvePointCount;
        List<VecD> current = document.LineToolOverlayHandler.ControlPoints.ToList();

        if (current.Count == wanted)
            return;

        if (wanted == 0)
        {
            document.LineToolOverlayHandler.SetControlPoints([]);
            return;
        }

        document.LineToolOverlayHandler.SetControlPoints(
            LineCurveHelper.RedistributeAlongCurve(start, end, current, CurveSmoothness, wanted));
    }

    private void ApplyState(LineVectorData data)
    {
        toolbar!.StrokeBrush = data.Stroke.ToBrush();
        toolbar!.ToolSize = data.StrokeWidth;

        document!.LineToolOverlayHandler.Smoothness = data.Smoothness;
        document!.LineToolOverlayHandler.IsometricLock = IsometricLock;
        document!.LineToolOverlayHandler.Show(
            data.Start, data.End, ShowApplyButton, AddToUndo, data.ControlPoints);
    }
}
