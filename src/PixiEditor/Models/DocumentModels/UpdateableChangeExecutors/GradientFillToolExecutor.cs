using Avalonia.Input;
using Drawie.Backend.Core.ColorsImpl;
using Drawie.Numerics;
using PixiEditor.ChangeableDocument.Actions.Generated;
using PixiEditor.ChangeableDocument.Actions.Undo;
using PixiEditor.ChangeableDocument.Changes.Drawing;
using PixiEditor.ChangeableDocument.Helpers;
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
internal class GradientFillToolExecutor : UpdateableChangeExecutor, IMidChangeUndoableExecutor, IReaimableExecutor
{
    private const double ShiftSnapRadians = Math.PI / 4;

    private IGradientFillToolHandler? handler;
    private IColorsHandler? colors;
    private Guid memberGuid;
    private int frame;

    private VecD dragOrigin;
    private VecD start;
    private VecD end;
    private bool dragging;
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
            ForceStop();
            onEnded!(this);
        }
    }

    public override void ForceStop()
    {
        internals!.ActionAccumulator.AddFinishedActions(new EndGradientFill_Action());
    }

    public void OnMidChangeUndo()
    {
        internals!.ActionAccumulator.AddFinishedActions(new EndGradientFill_Action(), new Undo_Action());
        onEnded!(this);
    }

    public void OnMidChangeRedo()
    {
    }

    public bool IsFeatureEnabled<T>()
    {
        return typeof(T) == typeof(IReaimableExecutor) || typeof(T) == typeof(IMidChangeUndoableExecutor);
    }

    private static VecD SnapAngle(VecD origin, VecD position)
    {
        VecD line = position - origin;
        double angle = Math.Round(Math.Atan2(line.Y, line.X) / ShiftSnapRadians) * ShiftSnapRadians;
        return origin + new VecD(Math.Cos(angle) * line.Length, Math.Sin(angle) * line.Length);
    }

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
