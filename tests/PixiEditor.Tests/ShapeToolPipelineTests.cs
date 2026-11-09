using Drawie.Backend.Core.ColorsImpl;
using Drawie.Backend.Core.ColorsImpl.Paintables;
using Drawie.Numerics;
using ChunkyImageLib.DataHolders;
using PixiEditor.ChangeableDocument;
using PixiEditor.ChangeableDocument.Actions;
using PixiEditor.ChangeableDocument.Actions.Generated;
using PixiEditor.ChangeableDocument.Actions.Undo;
using PixiEditor.ChangeableDocument.Changeables.Graph.Interfaces;
using PixiEditor.ChangeableDocument.Changeables.Graph.Nodes;
using PixiEditor.ChangeableDocument.Changes.Drawing;
using PixiEditor.ChangeableDocument.Enums;
using PixiEditor.ChangeableDocument.Helpers;

namespace PixiEditor.Tests;

/// <summary>Draws each shape onto a real layer, so the rasterized result is checked, not just the maths.</summary>
public class ShapeToolPipelineTests : PixiEditorTest
{
    private const int CanvasSize = 32;

    private static readonly Color Orange = new(255, 128, 0);

    /// <summary>The shape is drawn into the middle, leaving a margin so the corners can be checked.</summary>
    private static readonly RectD Box = new(8, 8, 16, 16);

    private static void Run(DocumentChangeTracker tracker, params IAction[] actions)
    {
        tracker.ProcessAll(actions.Select(action => (ActionSource.User, action)).ToList());
    }

    private static (DocumentChangeTracker Tracker, Guid Layer) CreateLayer()
    {
        DocumentChangeTracker tracker = new();
        Guid layer = Guid.NewGuid();
        Guid output = Guid.NewGuid();

        Run(tracker,
            new CreateNode_Action(typeof(OutputNode), output, Guid.Empty),
            new ResizeCanvas_Action(new VecI(CanvasSize, CanvasSize), ResizeAnchor.TopLeft),
            new CreateStructureMember_Action(output, layer, typeof(ImageLayerNode)),
            new ChangeBoundary_Action());

        return (tracker, layer);
    }

    private static Color PixelAt(DocumentChangeTracker tracker, Guid layer, int x, int y)
    {
        var node = (IReadOnlyImageNode)tracker.Document.FindMemberOrThrow(layer);
        return node.GetLayerImageAtFrame(0).GetCommittedPixel(new VecI(x, y));
    }

    private static ShapeDrawData Filled(ShapeKind kind, double angle = 0, int points = 5, double innerRatio = 0.5) =>
        new(kind, Box, angle, points, innerRatio, 0, 0, new ColorPaintable(Colors.Transparent),
            new ColorPaintable(Orange), false);

    private static void Draw(DocumentChangeTracker tracker, Guid layer, ShapeDrawData data)
    {
        Run(tracker,
            new DrawRasterShape_Action(layer, data, false, 0),
            new EndDrawRasterShape_Action(),
            new ChangeBoundary_Action());
    }

    /// <summary>How many pixels of the canvas the shape covered.</summary>
    private static int Covered(DocumentChangeTracker tracker, Guid layer)
    {
        int count = 0;
        for (int y = 0; y < CanvasSize; y++)
        {
            for (int x = 0; x < CanvasSize; x++)
            {
                if (PixelAt(tracker, layer, x, y).A != 0)
                {
                    count++;
                }
            }
        }

        return count;
    }

    [Theory]
    [InlineData(ShapeKind.Rectangle)]
    [InlineData(ShapeKind.Ellipse)]
    [InlineData(ShapeKind.Triangle)]
    [InlineData(ShapeKind.RightTriangle)]
    [InlineData(ShapeKind.Diamond)]
    [InlineData(ShapeKind.Polygon)]
    [InlineData(ShapeKind.Star)]
    [InlineData(ShapeKind.Heart)]
    [InlineData(ShapeKind.Cross)]
    [InlineData(ShapeKind.Arrow)]
    public void EveryShapeFillsItsMiddleAndStaysInsideItsBox(ShapeKind kind)
    {
        var (tracker, layer) = CreateLayer();

        Draw(tracker, layer, Filled(kind));

        Assert.Equal(Orange, PixelAt(tracker, layer, 16, 16));
        foreach ((int x, int y) in new[] { (0, 0), (31, 0), (0, 31), (31, 31), (7, 16), (24, 16) })
        {
            Assert.Equal(0, PixelAt(tracker, layer, x, y).A);
        }
    }

    [Fact]
    public void ARectangleFillsItsCornersButAnEllipseDoesNot()
    {
        var (rectTracker, rectLayer) = CreateLayer();
        var (ovalTracker, ovalLayer) = CreateLayer();

        Draw(rectTracker, rectLayer, Filled(ShapeKind.Rectangle));
        Draw(ovalTracker, ovalLayer, Filled(ShapeKind.Ellipse));

        Assert.Equal(Orange, PixelAt(rectTracker, rectLayer, 9, 9));
        Assert.Equal(0, PixelAt(ovalTracker, ovalLayer, 9, 9).A);
        Assert.True(Covered(ovalTracker, ovalLayer) < Covered(rectTracker, rectLayer));
    }

    [Fact]
    public void ATriangleIsEmptyAtTheTopCornersAndFilledAlongTheBottom()
    {
        var (tracker, layer) = CreateLayer();

        Draw(tracker, layer, Filled(ShapeKind.Triangle));

        Assert.Equal(0, PixelAt(tracker, layer, 9, 9).A);
        Assert.Equal(0, PixelAt(tracker, layer, 22, 9).A);
        Assert.Equal(Orange, PixelAt(tracker, layer, 16, 22));
    }

    [Fact]
    public void ACrossIsEmptyInItsCornersButFilledOnAllFourArms()
    {
        var (tracker, layer) = CreateLayer();

        Draw(tracker, layer, Filled(ShapeKind.Cross));

        Assert.Equal(0, PixelAt(tracker, layer, 9, 9).A);
        Assert.Equal(Orange, PixelAt(tracker, layer, 16, 9));
        Assert.Equal(Orange, PixelAt(tracker, layer, 16, 22));
        Assert.Equal(Orange, PixelAt(tracker, layer, 9, 16));
        Assert.Equal(Orange, PixelAt(tracker, layer, 22, 16));
    }

    [Fact]
    public void AStarCoversLessThanThePolygonItIsBuiltFrom()
    {
        var (starTracker, starLayer) = CreateLayer();
        var (polyTracker, polyLayer) = CreateLayer();

        Draw(starTracker, starLayer, Filled(ShapeKind.Star));
        Draw(polyTracker, polyLayer, Filled(ShapeKind.Polygon));

        Assert.True(Covered(starTracker, starLayer) < Covered(polyTracker, polyLayer));
    }

    [Fact]
    public void ABiggerInnerSizeMakesAFatterStar()
    {
        var (thinTracker, thinLayer) = CreateLayer();
        var (fatTracker, fatLayer) = CreateLayer();

        Draw(thinTracker, thinLayer, Filled(ShapeKind.Star, innerRatio: 0.2));
        Draw(fatTracker, fatLayer, Filled(ShapeKind.Star, innerRatio: 0.9));

        Assert.True(Covered(fatTracker, fatLayer) > Covered(thinTracker, thinLayer));
    }

    [Fact]
    public void AHeartHasItsDipAtTheTopAndItsPointAtTheBottom()
    {
        var (tracker, layer) = CreateLayer();

        Draw(tracker, layer, Filled(ShapeKind.Heart));

        Assert.Equal(0, PixelAt(tracker, layer, 16, 8).A);
        Assert.Equal(Orange, PixelAt(tracker, layer, 12, 11));
        Assert.Equal(Orange, PixelAt(tracker, layer, 20, 11));
        Assert.Equal(0, PixelAt(tracker, layer, 9, 22).A);
    }

    [Fact]
    public void RotatingAShapeTurnsItAboutItsCentre()
    {
        var (uprightTracker, uprightLayer) = CreateLayer();
        var (turnedTracker, turnedLayer) = CreateLayer();

        Draw(uprightTracker, uprightLayer, Filled(ShapeKind.Triangle));
        Draw(turnedTracker, turnedLayer, Filled(ShapeKind.Triangle, angle: Math.PI));

        // Turned upside down the base is at the top and the apex at the bottom, so a point out to
        // the side swaps from being inside the shape to being outside it.
        Assert.Equal(Orange, PixelAt(uprightTracker, uprightLayer, 11, 21));
        Assert.Equal(0, PixelAt(uprightTracker, uprightLayer, 11, 11).A);
        Assert.Equal(Orange, PixelAt(turnedTracker, turnedLayer, 11, 11));
        Assert.Equal(0, PixelAt(turnedTracker, turnedLayer, 11, 21).A);
    }

    [Fact]
    public void AnEmptyBoxDrawsNothing()
    {
        var (tracker, layer) = CreateLayer();

        Draw(tracker, layer, Filled(ShapeKind.Star) with { Bounds = new RectD(8, 8, 0, 0) });

        Assert.Equal(0, Covered(tracker, layer));
    }

    [Fact]
    public void AShapeUndoesAndRedoes()
    {
        var (tracker, layer) = CreateLayer();
        Draw(tracker, layer, Filled(ShapeKind.Star));

        Run(tracker, new Undo_Action());
        Assert.Equal(0, Covered(tracker, layer));

        Run(tracker, new Redo_Action());
        Assert.Equal(Orange, PixelAt(tracker, layer, 16, 16));
    }

    [Fact]
    public void AdjustingALiveShapeReplacesItRatherThanDrawingBoth()
    {
        var (tracker, layer) = CreateLayer();

        Run(tracker,
            new DrawRasterShape_Action(layer, Filled(ShapeKind.Rectangle), false, 0),
            new DrawRasterShape_Action(layer, Filled(ShapeKind.Ellipse), false, 0),
            new EndDrawRasterShape_Action(),
            new ChangeBoundary_Action());

        // The rectangle filled this corner; the ellipse that replaced it does not.
        Assert.Equal(0, PixelAt(tracker, layer, 9, 9).A);
        Assert.Equal(Orange, PixelAt(tracker, layer, 16, 16));
    }

    [Fact]
    public void AStrokeOnlyShapeIsHollow()
    {
        var (tracker, layer) = CreateLayer();

        Draw(tracker, layer, new ShapeDrawData(ShapeKind.Diamond, Box, 0, 5, 0.5, 0, 1,
            new ColorPaintable(Orange), new ColorPaintable(Colors.Transparent), false));

        Assert.Equal(0, PixelAt(tracker, layer, 16, 16).A);
        Assert.Equal(Orange, PixelAt(tracker, layer, 16, 8));
    }
}
