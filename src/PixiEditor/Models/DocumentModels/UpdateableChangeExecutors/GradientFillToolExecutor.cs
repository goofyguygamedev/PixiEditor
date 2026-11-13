using Avalonia.Input;
using Drawie.Backend.Core.ColorsImpl;
using Drawie.Backend.Core.Numerics;
using Drawie.Numerics;
using PixiEditor.ChangeableDocument.Actions.Generated;
using PixiEditor.ChangeableDocument.Actions.Undo;
using PixiEditor.ChangeableDocument.Changes.Drawing;
using PixiEditor.ChangeableDocument.Helpers;
using PixiEditor.Helpers;
using PixiEditor.Models.Controllers.InputDevice;
using PixiEditor.Models.DocumentModels.UpdateableChangeExecutors.Features;
using PixiEditor.Models.Handlers;
using PixiEditor.Models.Handlers.Tools;
using PixiEditor.Models.Tools;

namespace PixiEditor.Models.DocumentModels.UpdateableChangeExecutors;
#nullable enable

/// <summary>
/// Drives the Gradient tool. The gradient stays live after the mouse is released, so its settings can
/// be adjusted on the canvas and another drag re-aims it. Enter or switching tools applies it, and
/// undo throws it away.
/// </summary>
internal class GradientFillToolExecutor : UpdateableChangeExecutor, IMidChangeUndoableExecutor, IReaimableExecutor,
    ITransformableExecutor
{
    /// <summary>Shift snaps the aim to 15 degree steps, which keeps the usual 30, 45 and 60 within reach.</summary>
    private const double ShiftSnapDegrees = 15;

    private IGradientFillToolHandler? handler;
    private IColorsHandler? colors;
    private Guid memberGuid;
    private int frame;

    private VecD dragOrigin;
    private VecD start;
    private VecD end;
    private bool dragging;
    private bool handlesShown;
    private bool finished;
    private GradientParameters? lastSent;

    public override ExecutorType Type => ExecutorType.ToolLinked;

    public bool CanUndo => true;
    public bool CanRedo => false;

    public override ExecutionState Start()
    {
        handler = GetHandler<IGradientFillToolHandler>();
        colors = GetHandler<IColorsHandler>();

        if (handler is null || colors is null || document!.SelectedStructureMember is not ILayerHandler layer)
            return ExecutionState.Error;

        if (layer.IsLockedBindable || layer.ShouldDrawOnMask)
            return ExecutionState.Error;

        memberGuid = layer.Id;
        frame = document.AnimationHandler.ActiveFrameBindable;
        dragOrigin = controller!.LastPrecisePosition;
        start = dragOrigin;
        end = dragOrigin;
        dragging = controller.LeftMousePressed;

        SendParameters();
        return ExecutionState.Success;
    }

    public override void OnLeftMouseButtonDown(MouseOnCanvasEventArgs args)
    {
        // The existing gradient is only replaced once the pointer moves, so a stray click keeps it.
        dragOrigin = args.Point.PositionOnCanvas;
        dragging = true;
    }

    public override void OnPrecisePositionChange(MouseOnCanvasEventArgs args)
    {
        if (!dragging || !controller!.LeftMousePressed)
            return;

        VecD position = args.Point.PositionOnCanvas;
        if ((position - dragOrigin).LengthSquared <= 0)
            return;

        start = dragOrigin;
        end = controller.ShiftPressed ? SnapAngle(start, position) : position;
        SendParameters();
    }

    public override void OnLeftMouseButtonUp(VecD pos)
    {
        dragging = false;
        ShowHandles();
    }

    /// <summary>
    /// Puts a handle on each end of the gradient once it has been drawn, so it can be pulled
    /// further out or turned round without starting again, and shows the Apply button.
    /// </summary>
    private void ShowHandles()
    {
        if (document is null || (end - start).LengthSquared <= 0)
            return;

        document.LineToolOverlayHandler.Hide();
        document.LineToolOverlayHandler.Show(start, end, true, AddToUndo);
        document.LineToolOverlayHandler.ShowHandles = true;
        handlesShown = true;
    }

    private void HideHandles()
    {
        if (!handlesShown || document is null)
            return;

        handlesShown = false;
        document.LineToolOverlayHandler.Hide();
    }

    /// <summary>
    /// The handles move one live change rather than leaving a step of their own, so there is nothing
    /// to add: the whole gradient is one undo step either way.
    /// </summary>
    private void AddToUndo((VecD Start, VecD End) line)
    {
    }

    public override void OnSettingsChanged(string name, object value)
    {
        SendParameters();
    }

    public override void OnColorChanged(Color color, bool primary)
    {
        SendParameters();
    }

    public override void OnConvertedKeyDown(Key key)
    {
        if (key == Key.Enter)
        {
            Finish();
        }
    }

    public override void ForceStop()
    {
        HideHandles();
        internals!.ActionAccumulator.AddFinishedActions(new EndGradientFill_Action());
    }

    public void OnMidChangeUndo()
    {
        HideHandles();
        // Two calls, not one: the accumulator closes the packet after each, and undo refuses to
        // run while a packet is still open - so sending both at once would silently keep the change.
        internals!.ActionAccumulator.AddFinishedActions(new EndGradientFill_Action());
        internals.ActionAccumulator.AddFinishedActions(new Undo_Action());
        onEnded!(this);
    }

    public void OnMidChangeRedo()
    {
    }

    public bool IsTransforming => handlesShown;

    public void OnTransformStarted()
    {
    }

    public void OnTransformChanged(ShapeCorners corners)
    {
    }

    /// <summary>The Apply button, and Enter, which is the same command.</summary>
    public void OnTransformApplied()
    {
        Finish();
    }

    public void OnLineOverlayMoved(VecD start, VecD end)
    {
        // A press that lands away from a handle is a fresh aim, and that drag owns the ends until
        // the button comes back up.
        if (dragging)
            return;

        bool startMoved = (start - this.start).LengthSquared > 0;
        bool endMoved = (end - this.end).LengthSquared > 0;

        // Shift locks the aim to the same steps as the first drag. Only when one end is moving:
        // when both are, the line is being carried along by its middle and its angle is already
        // fixed, so there is nothing to snap.
        if (controller!.ShiftPressed && startMoved != endMoved)
        {
            if (endMoved)
            {
                end = SnapAngle(start, end);
            }
            else
            {
                start = SnapAngle(end, start);
            }
        }

        this.start = start;
        this.end = end;

        // Handed back to the overlay so the handle sits where the gradient actually points rather
        // than where the pointer is. Setting these raises the moved event once more, which lands
        // here with nothing left to change and stops.
        document!.LineToolOverlayHandler.LineStart = start;
        document!.LineToolOverlayHandler.LineEnd = end;

        SendParameters();
    }

    public void OnSelectedObjectNudged(VecI distance)
    {
        document!.LineToolOverlayHandler.Nudge(distance);
    }

    public bool IsTransformingMember(Guid id) => false;

    public bool IsFeatureEnabled<T>()
    {
        return typeof(T) == typeof(IReaimableExecutor) || typeof(T) == typeof(IMidChangeUndoableExecutor) ||
               typeof(T) == typeof(ITransformableExecutor);
    }

    /// <summary>
    /// Keeps the gradient. Guarded because Enter and the Apply button are the same command and both
    /// arrive here, and a second ending would push another action at a change that is already gone.
    /// </summary>
    private void Finish()
    {
        if (finished)
            return;

        finished = true;
        ForceStop();
        onEnded!(this);
    }

    private static VecD SnapAngle(VecD origin, VecD position) =>
        GeometryHelper.GetAngleSnappedPosition(origin, position, ShiftSnapDegrees);

    private void SendParameters()
    {
        Color from = handler!.UseMainColors ? colors!.PrimaryColor : handler.CustomFrom;
        Color to = handler.UseMainColors ? colors!.SecondaryColor : handler.CustomTo;
        if (handler.FadeToTransparent)
        {
            // Keeps the colour of the start, or the fade would pass through the other colour.
            to = from.WithAlpha(0);
        }

        GradientParameters parameters = new(
            start,
            end,
            new GradientStyle(handler.Shape, handler.Falloff, (int)Math.Round(handler.Steps), handler.Pattern, handler.Reverse),
            from,
            to,
            handler.GradientBlendMode,
            handler.Strength,
            handler.Area,
            handler.Tolerance);

        if (parameters == lastSent)
            return;

        lastSent = parameters;
        internals!.ActionAccumulator.AddActions(new GradientFill_Action(memberGuid, parameters, false, frame));
    }
}
