using PixiEditor.ChangeableDocument.Enums;
using PixiEditor.Models.DocumentModels.UpdateableChangeExecutors;
using PixiEditor.Models.Handlers.Tools;
using PixiEditor.Models.DocumentModels.UpdateableChangeExecutors.Features;
using PixiEditor.Models.Handlers;
using PixiEditor.Models.Tools;
using PixiEditor.ViewModels.Tools.Tools;

namespace PixiEditor.Models.DocumentModels.Public;

internal class DocumentToolsModule
{
    private IDocument Document { get; set; }
    private DocumentInternalParts Internals { get; set; }

    public DocumentToolsModule(IDocument doc, DocumentInternalParts internals)
    {
        this.Document = doc;
        this.Internals = internals;
    }

    public void UseSymmetry(SymmetryAxisDirection dir) =>
        Internals.ChangeController.TryStartExecutor(new SymmetryExecutor(dir));

    /// <summary>
    /// Puts the transform box around the selection so its handles can resize it. Forced past a
    /// tool-linked executor, because the select tool that asks for this is usually the one holding
    /// the session that just finished drawing the selection.
    /// </summary>
    public void UseTransformSelection()
    {
        bool force = Internals.ChangeController.GetCurrentExecutorType() == ExecutorType.ToolLinked;
        Internals.ChangeController.TryStartExecutor(new TransformSelectionExecutor(), force);
    }

    public void UseOpacitySlider() => Internals.ChangeController.TryStartExecutor<StructureMemberOpacityExecutor>();


    public void UsePenTool() => Internals.ChangeController.TryStartExecutor<PenToolExecutor>();

    public void UseEraserTool() => Internals.ChangeController.TryStartExecutor<EraserToolExecutor>();

    public void UseColorPickerTool() => Internals.ChangeController.TryStartExecutor<ColorPickerToolExecutor>();

    public void UseRasterRectangleTool()
    {
        bool force = Internals.ChangeController.GetCurrentExecutorType() == ExecutorType.ToolLinked;
        Internals.ChangeController.TryStartExecutor<RasterRectangleToolExecutor>(force);
    }

    public void UseRasterEllipseTool()
    {
        bool force = Internals.ChangeController.GetCurrentExecutorType() == ExecutorType.ToolLinked;
        Internals.ChangeController.TryStartExecutor<RasterEllipseToolExecutor>(force);
    }

    public void UseRasterLineTool()
    {
        bool force = Internals.ChangeController.GetCurrentExecutorType() == ExecutorType.ToolLinked;
        Internals.ChangeController.TryStartExecutor<RasterLineToolExecutor>(force);
    }

    public void UseVectorEllipseTool()
    {
        bool force = Internals.ChangeController.GetCurrentExecutorType() == ExecutorType.ToolLinked;
        Internals.ChangeController.TryStartExecutor<VectorEllipseToolExecutor>(force);
    }

    public void UseVectorRectangleTool()
    {
        bool force = Internals.ChangeController.GetCurrentExecutorType() == ExecutorType.ToolLinked;
        Internals.ChangeController.TryStartExecutor<VectorRectangleToolExecutor>(force);
    }

    public void UseVectorLineTool()
    {
        bool force = Internals.ChangeController.GetCurrentExecutorType() == ExecutorType.ToolLinked;
        Internals.ChangeController.TryStartExecutor<VectorLineToolExecutor>(force);
    }

    public void UseVectorPathTool()
    {
        bool force = Internals.ChangeController.GetCurrentExecutorType() == ExecutorType.ToolLinked;
        Internals.ChangeController.TryStartExecutor<VectorPathToolExecutor>(force);
    }

    public void UseSelectTool() => Internals.ChangeController.TryStartExecutor<SelectToolExecutor>();

    public void UseFloodFillTool() => Internals.ChangeController.TryStartExecutor<FloodFillToolExecutor>();

    public void UseLassoTool() => Internals.ChangeController.TryStartExecutor<LassoToolExecutor>();

    public void UseMagicWandTool() => Internals.ChangeController.TryStartExecutor<MagicWandToolExecutor>();

    public void UseLiquifyTool() => Internals.ChangeController.TryStartExecutor<LiquifyToolExecutor>();

    public void UsePixelInkTool(IPixelInkToolHandler tool) =>
        Internals.ChangeController.TryStartExecutor(new PixelInkToolExecutor(tool));

    public void UseShadeTool()
    {
        // A live shade on a selection takes the press itself, to re-aim.
        if (Internals.ChangeController.IsChangeOfTypeActive<IReaimableExecutor>())
            return;

        bool force = Internals.ChangeController.GetCurrentExecutorType() == ExecutorType.ToolLinked;
        Internals.ChangeController.TryStartExecutor<ShadeToolExecutor>(force);
    }

    public void UseRasterShapeTool() =>
        Internals.ChangeController.TryStartExecutor<RasterShapeToolExecutor>();

    public void UseGradientFillTool()
    {
        // A live gradient takes the press itself, to re-aim.
        if (Internals.ChangeController.IsChangeOfTypeActive<IReaimableExecutor>())
            return;

        bool force = Internals.ChangeController.GetCurrentExecutorType() == ExecutorType.ToolLinked;
        Internals.ChangeController.TryStartExecutor<GradientFillToolExecutor>(force);
    }

    public void UseOutlineTool()
    {
        if (Internals.ChangeController.IsChangeOfTypeActive<IReaimableExecutor>())
            return;

        bool force = Internals.ChangeController.GetCurrentExecutorType() == ExecutorType.ToolLinked;
        Internals.ChangeController.TryStartExecutor<OutlineToolExecutor>(force);
    }

    public void UseBrushBasedTool(BrushBasedToolViewModel brushBasedToolViewModel) =>
        Internals.ChangeController.TryStartExecutor(new BrushBasedExecutor(brushBasedToolViewModel));
    public void UseTextTool()
    {
        bool force = Internals.ChangeController.GetCurrentExecutorType() == ExecutorType.ToolLinked;
        Internals.ChangeController.TryStartExecutor<VectorTextToolExecutor>(force);
    }

    public bool TryStopActiveTool()
    {
        return Internals.ChangeController.TryStopActiveExecutor();
    }

}
