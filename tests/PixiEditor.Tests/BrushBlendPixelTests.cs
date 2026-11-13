using Avalonia.Headless.XUnit;
using Drawie.Backend.Core;using Drawie.Backend.Core.ColorsImpl;
using Drawie.Backend.Core.ColorsImpl.Paintables;
using Drawie.Backend.Core.Surfaces;
using Drawie.Backend.Core.Surfaces.ImageData;
using Drawie.Numerics;
using ChunkyImageLib;
using PixiEditor.ChangeableDocument;
using PixiEditor.ChangeableDocument.Actions;
using PixiEditor.ChangeableDocument.Actions.Generated;
using PixiEditor.ChangeableDocument.Actions.Undo;
using PixiEditor.ChangeableDocument.Changeables.Graph.Interfaces;
using PixiEditor.ChangeableDocument.Changeables.Graph.Nodes;
using PixiEditor.ChangeableDocument.Enums;
using PixiEditor.ChangeableDocument.Changeables.Brushes;
using PixiEditor.ChangeableDocument.Changeables.Graph;
using PixiEditor.ChangeableDocument.Changeables.Graph.Nodes.Brushes;
using PixiEditor.ChangeableDocument.Changeables.Graph.Nodes.Shapes.Data;
using PixiEditor.ChangeableDocument.Rendering.ContextData;

namespace PixiEditor.Tests;

/// <summary>
/// Puts real strokes through the brush engine and reads the pixels back. Reading the code was not
/// enough to catch the blend mode being dropped on the way down, so this checks the only thing
/// that matters: that picking a mode changes what ends up on the layer.
/// </summary>
public class BrushBlendPixelTests : FullPixiEditorTest
{
    private static readonly Color Backdrop = new(200, 200, 200);
    private static readonly Color Paint = new(128, 0, 0);

    [AvaloniaFact]
    public void MultiplyDarkensWhatIsUnderneathAndNormalDoesNot()
    {
        Color normal = StrokeOnGrey(BrushBlendMode.Normal);
        Color multiply = StrokeOnGrey(BrushBlendMode.Multiply);

        Assert.NotEqual(normal, multiply);
        Assert.True(multiply.R < normal.R, $"multiply gave {multiply}, normal gave {normal}");
    }

    [AvaloniaFact]
    public void EveryModeIsDistinguishableFromNormal()
    {
        Color normal = StrokeOnGrey(BrushBlendMode.Normal);

        BrushBlendMode[] shouldDiffer =
        [
            BrushBlendMode.Multiply, BrushBlendMode.Screen, BrushBlendMode.Add,
            BrushBlendMode.Lighten, BrushBlendMode.Difference
        ];

        string[] same = shouldDiffer
            .Where(mode => StrokeOnGrey(mode) == normal)
            .Select(mode => mode.ToString())
            .ToArray();

        Assert.Empty(same);
    }

    /// <summary>Paints one dab of <see cref="Paint"/> onto a grey layer and reads the centre pixel.</summary>
    private static Color StrokeOnGrey(BrushBlendMode mode)
    {
        using Surface backdrop = new(new VecI(32, 32));
        backdrop.DrawingSurface.Canvas.Clear(Backdrop);

        using ChunkyImage image = new(backdrop);

        NodeGraph graph = new();
        BrushOutputNode brush = new();
        graph.AddNode(brush);
        graph.CustomOutputNode = brush;
        brush.VectorShape.NonOverridenValue = new RectangleVectorData(new VecD(0, 0), new VecD(8, 8))
        {
            Fill = true, FillPaintable = new ColorPaintable(Paint)
        };
        brush.Fill.NonOverridenValue = new ColorPaintable(Paint);

        BrushData data = new(graph, brush.Id) { StrokeWidth = 8, BlendMode = mode };

        BrushEngine engine = new();
        engine.ExecuteBrush(image, data, new VecD(16, 16), 0, ColorSpace.CreateSrgb(), SamplingOptions.Default,
            default, default, new EditorData(Paint, Colors.White));

        image.CommitChanges();

        return image.GetCommittedPixel(new VecI(16, 16));
    }

    [AvaloniaFact]
    public void TheModeSurvivesTheWholeStrokePipeline()
    {
        // The engine honouring the mode is not enough - it has to still be set by the time the
        // change hands it over. That is where it was being dropped, and this is the test that
        // would have caught it.
        Color normal = StrokeThroughTheDocument(BrushBlendMode.Normal);
        Color multiply = StrokeThroughTheDocument(BrushBlendMode.Multiply);

        Assert.NotEqual(normal, multiply);
        Assert.True(multiply.R < normal.R, $"multiply gave {multiply}, normal gave {normal}");
    }

    private static Color StrokeThroughTheDocument(BrushBlendMode mode)
    {
        DocumentChangeTracker tracker = new();
        Guid output = Guid.NewGuid();
        Guid layer = Guid.NewGuid();

        Run(tracker,
            new CreateNode_Action(typeof(OutputNode), output, Guid.Empty),
            new ResizeCanvas_Action(new VecI(32, 32), ResizeAnchor.TopLeft),
            new CreateStructureMember_Action(output, layer, typeof(ImageLayerNode)),
            new ChangeBoundary_Action());

        Run(tracker,
            new FloodFill_Action(layer, new VecI(0, 0), Backdrop, false, 0f, FloodFillMode.Overlay, false, true, 0),
            new ChangeBoundary_Action());

        BrushData data = BrushOfSquares(mode);
        EditorData editorData = new(Paint, Colors.White);

        Run(tracker,
            new LineBasedPen_Action(layer, new VecD(16, 16), 8f, false, data, false, 0,
                default, default, editorData),
            new EndLineBasedPen_Action(),
            new ChangeBoundary_Action());

        IReadOnlyImageNode node = (IReadOnlyImageNode)tracker.Document.FindMemberOrThrow(layer);
        return node.GetLayerImageAtFrame(0).GetCommittedPixel(new VecI(16, 16));
    }

    private static void Run(DocumentChangeTracker tracker, params IAction[] actions) =>
        tracker.ProcessAll(actions.Select(action => (ActionSource.User, action)).ToList());

    /// <summary>A minimal brush: one filled square, which is all the engine needs to stamp.</summary>
    private static BrushData BrushOfSquares(BrushBlendMode mode)
    {
        NodeGraph graph = new();
        BrushOutputNode brush = new();
        graph.AddNode(brush);
        graph.CustomOutputNode = brush;
        brush.VectorShape.NonOverridenValue = new RectangleVectorData(new VecD(0, 0), new VecD(8, 8))
        {
            Fill = true, FillPaintable = new ColorPaintable(Paint)
        };
        brush.Fill.NonOverridenValue = new ColorPaintable(Paint);

        return new BrushData(graph, brush.Id) { StrokeWidth = 8, BlendMode = mode };
    }

    [AvaloniaFact]
    public void ThePixelPerfectPenBlendsToo()
    {
        // It never touches the brush engine - it writes pixels itself - so it needs the mode
        // handed to it separately. Pixel art tool sets leave this pen on, which is why the
        // setting looked dead while every other test of it passed.
        Color normal = PixelPerfectStroke(BrushBlendMode.Normal);
        Color multiply = PixelPerfectStroke(BrushBlendMode.Multiply);

        Assert.NotEqual(normal, multiply);
        Assert.True(multiply.R < normal.R, $"multiply gave {multiply}, normal gave {normal}");
    }

    private static Color PixelPerfectStroke(BrushBlendMode mode)
    {
        (DocumentChangeTracker tracker, Guid layer) = GreyCanvas();

        Run(tracker,
            new PixelPerfectPen_Action(layer, new VecI(15, 16), Paint, false, 0, mode),
            new PixelPerfectPen_Action(layer, new VecI(16, 16), Paint, false, 0, mode),
            new PixelPerfectPen_Action(layer, new VecI(17, 16), Paint, false, 0, mode),
            new EndPixelPerfectPen_Action(),
            new ChangeBoundary_Action());

        IReadOnlyImageNode node = (IReadOnlyImageNode)tracker.Document.FindMemberOrThrow(layer);
        return node.GetLayerImageAtFrame(0).GetCommittedPixel(new VecI(16, 16));
    }

    private static (DocumentChangeTracker Tracker, Guid Layer) GreyCanvas()
    {
        DocumentChangeTracker tracker = new();
        Guid output = Guid.NewGuid();
        Guid layer = Guid.NewGuid();

        Run(tracker,
            new CreateNode_Action(typeof(OutputNode), output, Guid.Empty),
            new ResizeCanvas_Action(new VecI(32, 32), ResizeAnchor.TopLeft),
            new CreateStructureMember_Action(output, layer, typeof(ImageLayerNode)),
            new ChangeBoundary_Action());

        Run(tracker,
            new FloodFill_Action(layer, new VecI(0, 0), Backdrop, false, 0f, FloodFillMode.Overlay, false, true, 0),
            new ChangeBoundary_Action());

        return (tracker, layer);
    }
}
