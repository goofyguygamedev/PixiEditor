using PixiEditor.Models.Handlers;
using PixiEditor.Models.Handlers.Tools;
using PixiEditor.Models.Tools;
using Drawie.Numerics;
using PixiEditor.Models.Controllers.InputDevice;
using PixiEditor.Extensions.CommonApi.Palettes;

namespace PixiEditor.Models.DocumentModels.UpdateableChangeExecutors;

#nullable enable
internal class ColorPickerToolExecutor : UpdateableChangeExecutor
{
    private bool includeReference;
    private bool includeCanvas;
    private DocumentScope scope;
    private IColorsHandler? colorsViewModel;
    private IWindowHandler? windowHandler;
    private Guid? sceneTextureKey;
    private IColorPickerHandler? tool;

    public override ExecutionState Start()
    {
        colorsViewModel = GetHandler<IColorsHandler>();
        tool = GetHandler<IColorPickerHandler>();

        if (colorsViewModel is null || tool is null)
            return ExecutionState.Error;

        scope = tool.Mode;
        includeReference = tool.PickFromReferenceLayer && document!.ReferenceLayerHandler.ReferenceTexture is not null && document!.ReferenceLayerHandler.IsVisible;
        includeCanvas = tool.PickFromCanvas;

        windowHandler = GetHandler<IWindowHandler>();

        IViewport? viewport = windowHandler.ActiveWindow as IViewport;
        string? customOutput = viewport?.RenderOutputName;
        customOutput = customOutput == "DEFAULT" ? null : customOutput;
        sceneTextureKey = viewport?.SceneTextureKey;

        colorsViewModel.PrimaryColor = document.PickColor(controller.LastPrecisePosition, scope, includeReference, includeCanvas, document.AnimationHandler.ActiveFrameBindable, document.ReferenceLayerHandler.IsTopMost, customOutput, sceneTextureKey);
        return ExecutionState.Success;
    }

    public override void OnPrecisePositionChange(MouseOnCanvasEventArgs args)
    {
        if (!includeReference)
            return;

        string? customOutput = (windowHandler?.ActiveWindow as IViewport)?.RenderOutputName;
        customOutput = customOutput == "DEFAULT" ? null : customOutput;

        colorsViewModel.PrimaryColor = document.PickColor(args.Point.PositionOnCanvas, scope, includeReference, includeCanvas, document.AnimationHandler.ActiveFrameBindable, document.ReferenceLayerHandler.IsTopMost, customOutput, sceneTextureKey);
    }

    public override void OnPixelPositionChange(VecI pos, MouseOnCanvasEventArgs args)
    {
        string? customOutput = (windowHandler?.ActiveWindow as IViewport)?.RenderOutputName;
        customOutput = customOutput == "DEFAULT" ? null : customOutput;

        colorsViewModel.PrimaryColor = document.PickColor(pos, scope, includeReference, includeCanvas, document.AnimationHandler.ActiveFrameBindable, document.ReferenceLayerHandler.IsTopMost, customOutput, sceneTextureKey);
    }

    public override void OnLeftMouseButtonUp(VecD argsPositionOnCanvas)
    {
        if (ShouldAddToPalette())
        {
            AddPickedColorToPalette();
        }

        onEnded?.Invoke(this);
    }

    /// <summary>
    /// Whether this click should also put the color in the document's palette.
    /// </summary>
    /// <remarks>
    /// Only when the color picker is the tool the artist chose. Alt is also what transiently
    /// borrows the picker from another tool, and alt-picking a color while drawing should not
    /// quietly grow the palette on every click.
    /// </remarks>
    private bool ShouldAddToPalette() =>
        controller!.AltPressed && tool is { IsTransient: false } && document is not null;

    private void AddPickedColorToPalette()
    {
        PaletteColor picked = new(colorsViewModel!.PrimaryColor.R, colorsViewModel.PrimaryColor.G,
            colorsViewModel.PrimaryColor.B);

        // A palette is a set, and a duplicate swatch is only something to tidy up later.
        if (document!.Palette.Contains(picked))
        {
            return;
        }

        document.Palette.Add(picked);
    }

    public override void ForceStop()
    {
        
    }
}
