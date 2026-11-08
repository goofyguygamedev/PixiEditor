using PixiEditor.ChangeableDocument.Actions;
using PixiEditor.ChangeableDocument.Actions.Generated;
using PixiEditor.ChangeableDocument.Changeables.Graph.Interfaces.Shapes;
using Drawie.Backend.Core.Surfaces.PaintImpl;
using PixiEditor.Models.Handlers.Tools;
using Drawie.Numerics;

namespace PixiEditor.Models.DocumentModels.UpdateableChangeExecutors;
#nullable enable
internal class RasterLineToolExecutor : LineExecutor<ILineToolHandler>
{
    protected override bool UseGlobalUndo => false;
    protected override bool ShowApplyButton => true;

    protected override bool InitShapeData(IReadOnlyLineData? data)
    {
        return false;
    }

    protected override IAction DrawLine(VecD pos) => BuildAction(startDrawingPos, pos);

    protected override IAction TransformOverlayMoved(VecD start, VecD end) => BuildAction(start, end);

    protected override IAction[] SettingsChange(string name, object value) =>
        [BuildAction(startDrawingPos, curPos)];

    /// <summary>
    /// Builds the draw action, handing over a freshly built curve path whenever the line has curve
    /// points. The change takes ownership of that path, so it is never reused or disposed here.
    /// </summary>
    private DrawRasterLine_Action BuildAction(VecD start, VecD end)
    {
        VecD pixelStart = ToPixelPos(start);
        VecD pixelEnd = ToPixelPos(end);

        return new DrawRasterLine_Action(memberId, pixelStart, pixelEnd,
            (float)StrokeWidth, StrokePaintable, StrokeCap.Butt, toolbar.AntiAliasing, drawOnMask,
            document!.AnimationHandler.ActiveFrameBindable, BuildCurvePath(pixelStart, pixelEnd));
    }

    private VecD ToPixelPos(VecD pos)
    {
        return pos;
    }

    protected override IAction EndDraw()
    {
        return new EndDrawRasterLine_Action();
    }
}
