using Avalonia.Input;
using PixiEditor.ChangeableDocument.Actions.Generated;
using PixiEditor.ChangeableDocument.Actions.Undo;
using Drawie.Backend.Core.ColorsImpl;
using PixiEditor.ChangeableDocument.Changes.Drawing.FloodFill;
using PixiEditor.Extensions.CommonApi.Palettes;
using PixiEditor.Models.DocumentModels.UpdateableChangeExecutors.Features;
using PixiEditor.Models.Handlers;
using PixiEditor.Models.Handlers.Tools;
using PixiEditor.Models.Tools;
using Drawie.Numerics;
using PixiEditor.Models.Controllers.InputDevice;

namespace PixiEditor.Models.DocumentModels.UpdateableChangeExecutors;
#nullable enable

/// <summary>
/// Drives the Flood Fill tool. The fill stays live after the mouse is released, so the colour, the
/// tolerance and the other settings can be changed with the result on screen instead of having to
/// fill again to see what they do.
/// </summary>
/// <remarks>
/// Enter and Escape keep it, filling somewhere else keeps it and starts another, and undo while it
/// is live throws it away.
/// </remarks>
internal class FloodFillToolExecutor : UpdateableChangeExecutor, IMidChangeUndoableExecutor
{
    private IFloodFillToolHandler? tool;
    private IColorsHandler? colors;
    private Guid memberGuid;
    private bool drawOnMask;
    private int frame;
    private VecI lastPos;
    private bool finished;
    private (VecI Pos, FillAreaParameters Parameters)? lastSent;

    public override ExecutorType Type => ExecutorType.ToolLinked;

    /// <summary>
    /// Only while the button is down. The fill stays open after that so its settings can be changed,
    /// and a tool sitting in that state must not hold the rest of the editor up.
    /// </summary>
    public override bool BlocksOtherActions => controller!.LeftMousePressed;

    public bool CanUndo => true;

    public bool CanRedo => false;

    public override ExecutionState Start()
    {
        tool = GetHandler<IFloodFillToolHandler>();
        colors = GetHandler<IColorsHandler>();
        IStructureMemberHandler? member = document!.SelectedStructureMember;

        if (tool is null || colors is null || member is null)
            return ExecutionState.Error;

        drawOnMask = member is not ILayerHandler layer || layer.ShouldDrawOnMask;
        if (drawOnMask && !member.HasMaskBindable)
            return ExecutionState.Error;
        if (!drawOnMask && member is not ILayerHandler)
            return ExecutionState.Error;
        if (member.IsLockedBindable)
            return ExecutionState.Error;

        memberGuid = member.Id;
        frame = document.AnimationHandler.ActiveFrameBindable;
        lastPos = controller!.LastPixelPosition;

        Color color = colors.PrimaryColor;
        colors.AddSwatch(new PaletteColor(color.R, color.G, color.B));

        Send();
        return ExecutionState.Success;
    }

    public override void OnPixelPositionChange(VecI pos, MouseOnCanvasEventArgs args)
    {
        // Dragging keeps filling areas as it crosses them, as it always has. Moving the pointer
        // afterwards must not, or the live fill would wander off the area that was clicked.
        if (!controller!.LeftMousePressed)
            return;

        lastPos = pos;
        Send();
    }

    public override void OnSettingsChanged(string name, object value)
    {
        Send();
    }

    public override void OnColorChanged(Color color, bool primary)
    {
        if (primary)
            Send();
    }

    public override void OnConvertedKeyDown(Key key)
    {
        if (key == Key.Enter)
            Finish();
    }

    public override void ForceStop()
    {
        internals!.ActionAccumulator.AddFinishedActions(new EndFillArea_Action());
    }

    public void OnMidChangeUndo()
    {
        // Two calls, not one: the accumulator closes the packet after each, and undo refuses to
        // run while a packet is still open - so sending both at once would silently keep the change.
        internals!.ActionAccumulator.AddFinishedActions(new EndFillArea_Action());
        internals.ActionAccumulator.AddFinishedActions(new Undo_Action());
        onEnded!(this);
    }

    public void OnMidChangeRedo()
    {
    }

    public bool IsFeatureEnabled<T>()
    {
        return typeof(T) == typeof(IMidChangeUndoableExecutor);
    }

    private void Finish()
    {
        if (finished)
            return;

        finished = true;
        ForceStop();
        onEnded!(this);
    }

    private void Send()
    {
        if (tool is null || colors is null)
            return;

        FillAreaParameters parameters = new(
            colors.PrimaryColor,
            tool.ConsiderAllLayers,
            tool.Tolerance,
            tool.FillMode,
            tool.ResultingContiguous);

        if (lastSent == (lastPos, parameters))
            return;

        lastSent = (lastPos, parameters);
        internals!.ActionAccumulator.AddActions(
            new FillArea_Action(memberGuid, lastPos, parameters, drawOnMask, frame));
    }
}
