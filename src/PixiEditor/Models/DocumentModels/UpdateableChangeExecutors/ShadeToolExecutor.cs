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
/// Drives the Shade tool. The shade stays live after the mouse is released, so the toolbar settings
/// can be adjusted and seen on the canvas before it is applied.
/// </summary>
/// <remarks>
/// With a selection, pressing again re-aims the same shade. Without one, each click shades a new
/// region and applies the last. Enter or switching tools applies; undo throws the live shade away.
/// </remarks>
internal class ShadeToolExecutor : UpdateableChangeExecutor, IMidChangeUndoableExecutor, IReaimableExecutor
{
    /// <summary>Pointer travel, in pixels, before a drag counts as aiming.</summary>
    private const double AimThreshold = 3;

    private const float ShiftSnapDegrees = 45;

    private IShadeToolHandler? handler;
    private IColorsHandler? colors;
    private Guid memberGuid;
    private int frame;
    private bool hasSelection;

    private VecI clickPosition;
    private VecD aimOrigin;
    private bool aiming;
    private ShadeParameters? lastSent;

    public override ExecutorType Type => ExecutorType.ToolLinked;

    public bool CanUndo => true;
    public bool CanRedo => false;

    public override ExecutionState Start()
    {
        handler = GetHandler<IShadeToolHandler>();
        colors = GetHandler<IColorsHandler>();

        if (handler is null || colors is null || document!.SelectedStructureMember is not ILayerHandler layer)
            return ExecutionState.Error;

        if (layer.IsLockedBindable || layer.ShouldDrawOnMask)
            return ExecutionState.Error;

        memberGuid = layer.Id;
        frame = document.AnimationHandler.ActiveFrameBindable;
        hasSelection = !document.SelectionPathBindable.IsEmpty;
        clickPosition = controller!.LastPixelPosition;
        aimOrigin = controller.LastPrecisePosition;
        aiming = controller.LeftMousePressed;

        SendParameters();
        return ExecutionState.Success;
    }

    public override void OnLeftMouseButtonDown(MouseOnCanvasEventArgs args)
    {
        aimOrigin = args.Point.PositionOnCanvas;
        aiming = true;
    }

    public override void OnPixelPositionChange(VecI pos, MouseOnCanvasEventArgs args)
    {
        if (!aiming || !controller!.LeftMousePressed)
            return;

        VecD drag = args.Point.PositionOnCanvas - aimOrigin;
        if (drag.Length < AimThreshold)
            return;

        // Screen Y runs downwards, and an angle of 90 means up.
        float angle = (float)(Math.Atan2(-drag.Y, drag.X) * 180 / Math.PI);
        angle = controller.ShiftPressed
            ? MathF.Round(angle / ShiftSnapDegrees) * ShiftSnapDegrees
            : MathF.Round(angle);

        handler!.SetAngle(((angle % 360) + 360) % 360);
        SendParameters();
    }

    public override void OnLeftMouseButtonUp(VecD pos)
    {
        aiming = false;
    }

    public override void OnSettingsChanged(string name, object value)
    {
        SendParameters();
    }

    public override void OnColorChanged(Color color, bool primary)
    {
        if (primary)
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
        internals!.ActionAccumulator.AddFinishedActions(new EndShadeRegion_Action());
    }

    public void OnMidChangeUndo()
    {
        internals!.ActionAccumulator.AddFinishedActions(new EndShadeRegion_Action(), new Undo_Action());
        onEnded!(this);
    }

    public void OnMidChangeRedo()
    {
    }

    public bool IsFeatureEnabled<T>()
    {
        if (typeof(T) == typeof(IReaimableExecutor))
            return hasSelection;

        return typeof(T) == typeof(IMidChangeUndoableExecutor);
    }

    private void SendParameters()
    {
        ShadeParameters parameters = new(
            new EdgeShadeShape(handler!.Angle, handler.Spread, (int)Math.Round(handler.Thickness), handler.Falloff,
                (int)Math.Round(handler.Bands)),
            handler.Strength,
            handler.ShadeBlendMode,
            handler.UseMainColor ? colors!.PrimaryColor : handler.CustomColor,
            handler.Tolerance);

        if (parameters == lastSent)
            return;

        lastSent = parameters;
        internals!.ActionAccumulator.AddActions(new ShadeRegion_Action(memberGuid, clickPosition, parameters, false, frame));
    }
}
