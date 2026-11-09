using Avalonia.Threading;
using Drawie.Numerics;
using PixiEditor.ChangeableDocument.Actions.Generated;
using PixiEditor.ChangeableDocument.Helpers;
using PixiEditor.Models.Controllers.InputDevice;
using PixiEditor.Models.Handlers;
using PixiEditor.Models.Handlers.Tools;
using PixiEditor.Models.Tools;

namespace PixiEditor.Models.DocumentModels.UpdateableChangeExecutors;
#nullable enable

/// <summary>Drives a single liquify stroke, from press to release.</summary>
internal class LiquifyToolExecutor : UpdateableChangeExecutor
{
    /// <summary>How often a twirl, pinch or expand re-applies while the pointer is held still.</summary>
    private static readonly TimeSpan HoldInterval = TimeSpan.FromMilliseconds(33);

    private Guid memberGuid;
    private LiquifyMode mode;
    private double radius;
    private double strength;
    private bool smooth;
    private int keepEdgeWidth;
    private bool drawOnMask;
    private int frame;
    private VecD lastPosition;
    private DispatcherTimer? holdTimer;

    public override ExecutionState Start()
    {
        ILiquifyToolHandler? tool = GetHandler<ILiquifyToolHandler>();

        if (tool is null || document!.SelectedStructureMember is not ILayerHandler layer)
            return ExecutionState.Error;

        if (layer.IsLockedBindable)
            return ExecutionState.Error;

        drawOnMask = layer.ShouldDrawOnMask;
        if (drawOnMask && !layer.HasMaskBindable)
            return ExecutionState.Error;

        memberGuid = layer.Id;
        frame = document.AnimationHandler.ActiveFrameBindable;
        mode = tool.Mode;
        radius = Math.Max(0.5, tool.ToolSize / 2);
        strength = tool.Strength;
        smooth = tool.Smooth;
        keepEdgeWidth = tool.KeepEdges ? (int)Math.Round(tool.EdgeWidth) : 0;
        lastPosition = controller!.LastPrecisePosition;

        Send(lastPosition);

        // A push only means anything while moving, but the others keep working on the spot, which
        // is how a slow twirl is built up.
        if (mode != LiquifyMode.Push)
        {
            holdTimer = new DispatcherTimer(HoldInterval, DispatcherPriority.Input, (_, _) => Send(lastPosition));
            holdTimer.Start();
        }

        return ExecutionState.Success;
    }

    public override void OnPrecisePositionChange(MouseOnCanvasEventArgs args)
    {
        lastPosition = args.Point.PositionOnCanvas;
        Send(lastPosition);
    }

    public override void OnLeftMouseButtonUp(VecD pos)
    {
        Finish();
        onEnded!(this);
    }

    public override void ForceStop()
    {
        Finish();
    }

    private void Send(VecD position)
    {
        internals!.ActionAccumulator.AddActions(
            new Liquify_Action(memberGuid, position, mode, radius, strength, smooth, keepEdgeWidth, drawOnMask, frame));
    }

    private void Finish()
    {
        holdTimer?.Stop();
        holdTimer = null;
        internals!.ActionAccumulator.AddFinishedActions(new EndLiquify_Action());
    }
}
