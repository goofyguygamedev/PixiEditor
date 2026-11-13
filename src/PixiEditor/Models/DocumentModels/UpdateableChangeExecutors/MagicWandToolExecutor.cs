using Avalonia.Input;
using Drawie.Numerics;
using PixiEditor.ChangeableDocument.Actions.Generated;
using PixiEditor.ChangeableDocument.Actions.Undo;
using PixiEditor.ChangeableDocument.Enums;
using PixiEditor.Models.Controllers.InputDevice;
using PixiEditor.Models.DocumentModels.UpdateableChangeExecutors.Features;
using PixiEditor.Models.Handlers.Tools;
using PixiEditor.Models.Tools;
using PixiEditor.ViewModels.SubViewModels;

namespace PixiEditor.Models.DocumentModels.UpdateableChangeExecutors;
#nullable enable

/// <summary>
/// Drives the Magic Wand. The selection stays live after the mouse is released, so the tolerance
/// and the contiguous setting can be changed with the result on screen instead of having to click
/// again to see what they do.
/// </summary>
/// <remarks>
/// Enter and Escape keep it, selecting somewhere else keeps it and starts another, and undo while
/// it is live throws it away - the same as the fill tool.
/// </remarks>
internal class MagicWandToolExecutor : UpdateableChangeExecutor, IMidChangeUndoableExecutor
{
    private IMagicWandToolHandler? magicWand;
    private bool considerRenderOutput;
    private List<Guid>? memberGuids;
    private SelectionMode mode;
    private string renderOutput = string.Empty;
    private VecI lastPos;
    private bool finished;
    private (VecI Pos, float Tolerance, bool Contiguous)? lastSent;

    public override ExecutorType Type => ExecutorType.ToolLinked;

    /// <summary>
    /// Only while the button is down. The selection stays open after that so its settings can be
    /// changed, and a tool sitting in that state must not hold the rest of the editor up.
    /// </summary>
    public override bool BlocksOtherActions => controller!.LeftMousePressed;

    public bool CanUndo => true;

    public bool CanRedo => false;

    public override ExecutionState Start()
    {
        magicWand = GetHandler<IMagicWandToolHandler>();
        renderOutput = GetHandler<WindowViewModel>().LastActiveViewport?.RenderOutputName ?? string.Empty;
        var members = document!.ExtractSelectedLayers(true).ToList();

        if (magicWand is null || members.Count == 0)
            return ExecutionState.Error;

        // Read once, at the click: both come from the modifiers held at that moment, and letting
        // them change underneath a live selection would turn an add into a replace halfway through.
        mode = magicWand.ResultingSelectionMode;

        memberGuids = members;
        considerRenderOutput = magicWand.DocumentScope == DocumentScope.Canvas;
        if (considerRenderOutput)
            memberGuids = null;

        lastPos = controller!.LastPixelPosition;

        Send();

        return ExecutionState.Success;
    }

    public override void OnPixelPositionChange(VecI pos, MouseOnCanvasEventArgs args)
    {
        // Dragging keeps re-selecting from wherever the pointer is, as it always has. Moving the
        // pointer after the release must not, or the live selection would wander off the spot that
        // was clicked.
        if (!controller!.LeftMousePressed)
            return;

        lastPos = pos;
        Send();
    }

    public override void OnSettingsChanged(string name, object value)
    {
        Send();
    }

    public override void OnLeftMouseButtonUp(VecD argsPositionOnCanvas)
    {
        // Deliberately nothing: the selection stays live so the tolerance can be tried out.
    }

    public override void OnConvertedKeyDown(Key key)
    {
        if (key == Key.Enter)
            Finish();
    }

    public override void ForceStop()
    {
        internals!.ActionAccumulator.AddFinishedActions(new EndMagicWand_Action());
    }

    public void OnMidChangeUndo()
    {
        // Two calls, not one: the accumulator closes the packet after each, and undo refuses to
        // run while a packet is still open - so sending both at once would silently keep the change.
        internals!.ActionAccumulator.AddFinishedActions(new EndMagicWand_Action());
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
        if (magicWand is null)
            return;

        float tolerance = (float)magicWand.Tolerance;
        bool contiguous = magicWand.ResultingContiguous;

        if (lastSent == (lastPos, tolerance, contiguous))
            return;

        lastSent = (lastPos, tolerance, contiguous);

        internals!.ActionAccumulator.AddActions(new MagicWand_Action(
            memberGuids, lastPos, mode, tolerance,
            document!.AnimationHandler.ActiveFrameBindable, contiguous, renderOutput));
    }
}
