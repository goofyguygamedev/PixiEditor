using PixiEditor.ChangeableDocument.Changeables.Graph.Palettes;
using Drawie.Backend.Core.ColorsImpl;
using Drawie.Numerics;
using PixiEditor.ChangeableDocument.Actions.Generated;
using PixiEditor.ChangeableDocument.Changes.Drawing;
using PixiEditor.Helpers.Extensions;
using PixiEditor.Models.Controllers.InputDevice;
using PixiEditor.Models.Handlers;
using PixiEditor.Models.Handlers.Tools;
using PixiEditor.Models.Tools;

namespace PixiEditor.Models.DocumentModels.UpdateableChangeExecutors;
#nullable enable

/// <summary>Drives one stroke of a pixel ink tool. Ctrl reverses the direction of a palette ramp.</summary>
internal class PixelInkToolExecutor : UpdateableChangeExecutor
{
    private readonly IPixelInkToolHandler tool;

    private Guid memberGuid;
    private PixelInkMode mode;
    private int size;
    private Color color;
    private double density;
    private Palette palette = Palette.empty;
    private VecI cloneOffset;
    private bool drawOnMask;
    private int frame;

    public PixelInkToolExecutor(IPixelInkToolHandler tool)
    {
        this.tool = tool;
    }

    public override ExecutionState Start()
    {
        if (document!.SelectedStructureMember is not ILayerHandler layer || layer.IsLockedBindable)
            return ExecutionState.Error;

        drawOnMask = layer.ShouldDrawOnMask;
        if (drawOnMask && !layer.HasMaskBindable)
            return ExecutionState.Error;

        palette = new Palette(document.Palette.Select(entry => entry.ToColor()));
        if (tool.UsesPaletteRamp && palette.Count == 0)
            return ExecutionState.Error;

        memberGuid = layer.Id;
        frame = document.AnimationHandler.ActiveFrameBindable;
        size = Math.Max(1, (int)Math.Round(tool.ToolSize));
        density = tool.Density;
        color = GetHandler<IColorsHandler>().PrimaryColor;

        if (tool.IsCloneStamp)
        {
            if (tool.GetCloneOffset(controller!.LastPixelPosition) is not { } offset)
                return ExecutionState.Error;

            cloneOffset = offset;
        }

        mode = tool.IsCloneStamp ? PixelInkMode.Clone : !tool.UsesPaletteRamp
            ? PixelInkMode.Dither
            : controller!.CtrlPressed
                ? PixelInkMode.RampBackward
                : PixelInkMode.RampForward;

        Send(controller!.LastPixelPosition);
        return ExecutionState.Success;
    }

    public override void OnPixelPositionChange(VecI pos, MouseOnCanvasEventArgs args) => Send(pos);

    public override void OnLeftMouseButtonUp(VecD pos)
    {
        Finish();
        onEnded!(this);
    }

    public override void ForceStop() => Finish();

    private void Send(VecI position)
    {
        internals!.ActionAccumulator.AddActions(
            new PixelInk_Action(memberGuid, position, mode, size, color, density, palette, cloneOffset, drawOnMask, frame));
    }

    private void Finish()
    {
        internals!.ActionAccumulator.AddFinishedActions(new EndPixelInk_Action());
    }
}
