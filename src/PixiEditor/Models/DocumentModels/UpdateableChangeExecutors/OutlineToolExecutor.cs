using Avalonia.Input;
using Drawie.Backend.Core.ColorsImpl;
using Drawie.Numerics;
using PixiEditor.ChangeableDocument.Actions.Generated;
using PixiEditor.ChangeableDocument.Actions.Undo;
using PixiEditor.ChangeableDocument.Changes.Drawing;
using PixiEditor.ChangeableDocument.Helpers;
using PixiEditor.Models.DocumentModels.UpdateableChangeExecutors.Features;
using PixiEditor.Models.Handlers;
using PixiEditor.Models.Handlers.Tools;
using PixiEditor.Models.Tools;

namespace PixiEditor.Models.DocumentModels.UpdateableChangeExecutors;
#nullable enable

/// <summary>
/// Drives the Outline tool. The outline stays live after the click, so the toolbar settings can be
/// adjusted on the canvas. Clicking another shape applies the last one and starts a new outline, and
/// undo throws the live one away.
/// </summary>
internal class OutlineToolExecutor : UpdateableChangeExecutor, IMidChangeUndoableExecutor, IReaimableExecutor
{
    private IOutlineToolHandler? handler;
    private IColorsHandler? colors;
    private Guid memberGuid;
    private int frame;
    private bool hasSelection;
    private VecI clickPosition;
    private OutlineParameters? lastSent;

    public override ExecutorType Type => ExecutorType.ToolLinked;

    public bool CanUndo => true;
    public bool CanRedo => false;

    public override ExecutionState Start()
    {
        handler = GetHandler<IOutlineToolHandler>();
        colors = GetHandler<IColorsHandler>();

        if (handler is null || colors is null || document!.SelectedStructureMember is not ILayerHandler layer)
            return ExecutionState.Error;

        if (layer.IsLockedBindable || layer.ShouldDrawOnMask)
            return ExecutionState.Error;

        memberGuid = layer.Id;
        frame = document.AnimationHandler.ActiveFrameBindable;
        hasSelection = !document.SelectionPathBindable.IsEmpty;
        clickPosition = controller!.LastPixelPosition;

        SendParameters();
        return ExecutionState.Success;
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
        internals!.ActionAccumulator.AddFinishedActions(new EndOutlineShape_Action());
    }

    public void OnMidChangeUndo()
    {
        // Two calls, not one: the accumulator closes the packet after each, and undo refuses to
        // run while a packet is still open - so sending both at once would silently keep the change.
        internals!.ActionAccumulator.AddFinishedActions(new EndOutlineShape_Action());
        internals.ActionAccumulator.AddFinishedActions(new Undo_Action());
        onEnded!(this);
    }

    public void OnMidChangeRedo()
    {
    }

    public bool IsFeatureEnabled<T>()
    {
        // With a selection or the whole layer another click has nothing new to outline.
        if (typeof(T) == typeof(IReaimableExecutor))
            return hasSelection || handler?.Target == OutlineTarget.WholeLayer;

        return typeof(T) == typeof(IMidChangeUndoableExecutor);
    }

    private void SendParameters()
    {
        OutlineParameters parameters = new(
            new OutlineStyle((int)Math.Round(handler!.Thickness), handler.Position, handler.Corners, handler.Softness,
                handler.Falloff, (int)Math.Round(handler.Bands), handler.Pattern),
            handler.Target,
            handler.UseMainColor ? colors!.PrimaryColor : handler.CustomColor,
            handler.OutlineBlendMode,
            handler.Strength,
            handler.Tolerance);

        if (parameters == lastSent)
            return;

        lastSent = parameters;
        internals!.ActionAccumulator.AddActions(new OutlineShape_Action(memberGuid, clickPosition, parameters, false, frame));
    }
}
