using ChunkyImageLib.DataHolders;
using Drawie.Backend.Core.Numerics;
using Drawie.Numerics;
using PixiEditor.ChangeableDocument.Actions;
using PixiEditor.ChangeableDocument.Actions.Generated;
using PixiEditor.ChangeableDocument.Changes.Drawing;
using PixiEditor.ChangeableDocument.Helpers;
using PixiEditor.Models.Handlers;
using PixiEditor.Models.Handlers.Tools;
using PixiEditor.Models.Tools;

namespace PixiEditor.Models.DocumentModels.UpdateableChangeExecutors;
#nullable enable

/// <summary>
/// Drives the Shape tool, which draws whichever shape the toolbar is set to. Dragging sets the box
/// the shape fills, and the shape can then be scaled and rotated like any other shape tool before it
/// is applied.
/// </summary>
internal class RasterShapeToolExecutor : DrawableShapeToolExecutor<IRasterShapeToolHandler>
{
    public override ExecutorType Type => ExecutorType.ToolLinked;

    protected override DocumentTransformMode TransformMode => DocumentTransformMode.Scale_Rotate_NoShear_NoPerspective;

    protected override bool UseGlobalUndo => false;

    protected override bool ShowApplyButton => true;

    protected override void DrawShape(VecD currentPos, double rotationRad, bool firstDraw)
    {
        RectI rect;
        VecI startPos = (VecI)Snap(startDrawingPos, currentPos).Floor();
        if (firstDraw)
            rect = new RectI((VecI)currentPos, VecI.Zero);
        else
            rect = RectI.FromTwoPixels(startPos, (VecI)currentPos);

        lastRect = (RectD)rect;
        lastRadians = rotationRad;

        internals!.ActionAccumulator.AddActions(new DrawRasterShape_Action(memberId, BuildData(), drawOnMask,
            document!.AnimationHandler.ActiveFrameBindable));
    }

    protected override IAction SettingsChangedAction(string name, object value) =>
        new DrawRasterShape_Action(memberId, BuildData(), drawOnMask, document!.AnimationHandler.ActiveFrameBindable);

    protected override IAction TransformMovedAction(ShapeData data, ShapeCorners corners)
    {
        lastRect = RectD.FromCenterAndSize(data.Center, data.Size);
        lastRadians = corners.RectRotation;

        return new DrawRasterShape_Action(memberId, BuildData(), drawOnMask,
            document!.AnimationHandler.ActiveFrameBindable);
    }

    protected override bool CanEditShape(IStructureMemberHandler layer) => true;

    protected override IAction EndDrawAction() => new EndDrawRasterShape_Action();

    private ShapeDrawData BuildData() =>
        new(toolViewModel!.Shape,
            lastRect,
            lastRadians,
            (int)Math.Round(toolViewModel.Points),
            toolViewModel.InnerRatio,
            toolViewModel.CornerRadius,
            (float)StrokeWidth,
            StrokePaintable,
            FillPaintable,
            toolbar.AntiAliasing);
}
