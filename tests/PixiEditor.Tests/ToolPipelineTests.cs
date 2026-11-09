using System.Text.Json;
using Avalonia.Headless.XUnit;
using Avalonia.Platform;
using Drawie.Backend.Core.ColorsImpl;
using Drawie.Backend.Core.Vector;
using Drawie.Numerics;
using PixiEditor.ChangeableDocument;
using PixiEditor.ChangeableDocument.Actions;
using PixiEditor.ChangeableDocument.Actions.Generated;
using PixiEditor.ChangeableDocument.Actions.Undo;
using PixiEditor.ChangeableDocument.Changeables.Graph.Interfaces;
using PixiEditor.ChangeableDocument.Changeables.Graph.Nodes;
using PixiEditor.ChangeableDocument.Changeables.Graph.Nodes.Effects;
using PixiEditor.ChangeableDocument.Changeables.Graph.Nodes.FilterNodes;
using PixiEditor.ChangeableDocument.Changeables.Graph.Palettes;
using PixiEditor.ChangeableDocument.Changes.Drawing;
using PixiEditor.ChangeableDocument.Enums;
using PixiEditor.ChangeableDocument.Helpers;
using PixiEditor.Models.Config;
using PixiEditor.Models.Handlers;
using PixiEditor.Models.Handlers.Tools;
using PixiEditor.ViewModels;

namespace PixiEditor.Tests;

/// <summary>Runs the remaining new changes through the real document pipeline, on real layers.</summary>
public class ToolPipelineTests : FullPixiEditorTest
{
    private const int CanvasSize = 16;

    private static readonly Color Orange = new(255, 128, 0);
    private static readonly Color Dark = new(100, 50, 0);

    private static void Run(DocumentChangeTracker tracker, params IAction[] actions)
    {
        tracker.ProcessAll(actions.Select(action => (ActionSource.User, action)).ToList());
    }

    private static (DocumentChangeTracker Tracker, Guid Layer, Guid Output) CreateBlock()
    {
        DocumentChangeTracker tracker = new();
        Guid layer = Guid.NewGuid();
        Guid output = Guid.NewGuid();

        Run(tracker,
            new CreateNode_Action(typeof(OutputNode), output, Guid.Empty),
            new ResizeCanvas_Action(new VecI(CanvasSize, CanvasSize), ResizeAnchor.TopLeft),
            new CreateStructureMember_Action(output, layer, typeof(ImageLayerNode)),
            new ChangeBoundary_Action());

        using VectorPath block = new();
        block.AddRect(new RectD(4, 4, 8, 8));
        Run(tracker,
            new SetSelection_Action(block),
            new FloodFill_Action(layer, new VecI(5, 5), Orange, false, 0f, FloodFillMode.Overlay, false, true, 0),
            new ChangeBoundary_Action());

        return (tracker, layer, output);
    }

    private static Color PixelAt(DocumentChangeTracker tracker, Guid layer, int x, int y)
    {
        var node = (IReadOnlyImageNode)tracker.Document.FindMemberOrThrow(layer);
        return node.GetLayerImageAtFrame(0).GetCommittedPixel(new VecI(x, y));
    }

    private static void Ink(DocumentChangeTracker tracker, Guid layer, PixelInkMode mode, Palette palette, VecI offset, int x, int y)
    {
        Run(tracker,
            new PixelInk_Action(layer, new VecI(x, y), mode, 1, new Color(0, 0, 255), 1, palette, offset, false, 0),
            new EndPixelInk_Action(),
            new ChangeBoundary_Action());
    }

    [AvaloniaFact]
    public void TheNewToolsAreRegistered()
    {
        string[] names = ViewModelMain.Current!.ToolsSubViewModel.AllToolSets.SelectMany(set => set.Tools).Select(tool => tool.ToolName).ToArray();

        Assert.Contains("Liquify", names);
        Assert.Contains("Shade", names);
        Assert.Contains("DitherPen", names);
        Assert.Contains("RampShade", names);
        Assert.Contains("CloneStamp", names);
        Assert.Contains("GradientFill", names);
        Assert.Contains("Outline", names);
    }

    /// <summary>
    /// A built-in tool whose name matches a custom brush tool silently replaces it, because
    /// AddCustomTools skips any config entry whose name an existing tool already uses. Naming a new
    /// tool after a stock one therefore deletes the stock one instead of failing.
    /// </summary>
    [AvaloniaFact]
    public void NoBuiltInToolShadowsACustomBrushTool()
    {
        ToolsConfig config = ViewModelMain.Current!.Config.GetConfig<ToolsConfig>("ToolSetsConfig");

        foreach (ToolConfig custom in config.CustomTools)
        {
            IToolHandler? resolved = ViewModelMain.Current.ToolsSubViewModel.AllToolSets
                .SelectMany(set => set.Tools)
                .FirstOrDefault(tool => tool.ToolName == custom.ToolName);

            Assert.NotNull(resolved);

            // Checking the name alone is not enough: the tool that replaces a custom brush tool
            // takes its name, so only the type tells the two apart.
            Assert.True(resolved is IBrushToolHandler { IsCustomBrushTool: true },
                $"'{custom.ToolName}' resolved to {resolved!.GetType().Name}, which has replaced the custom brush tool of that name");
        }
    }

    [AvaloniaFact]
    public void TheStockGradientBrushAndTheGradientFillToolBothExist()
    {
        string[] resolved = ResolvedToolNames();

        Assert.Contains("Gradient", resolved);
        Assert.Contains("GradientFill", resolved);
    }

    [AvaloniaFact]
    public void EveryToolListedInAToolSetResolves()
    {
        ToolsConfig config = ViewModelMain.Current!.Config.GetConfig<ToolsConfig>("ToolSetsConfig");

        foreach (ToolSet set in config.ToolSets)
        {
            IToolSetHandler resolved = ViewModelMain.Current.ToolsSubViewModel.AllToolSets
                .Single(candidate => candidate.Name == set.Name);
            string[] names = resolved.Tools.Select(tool => tool.ToolName).ToArray();

            foreach (ToolConfig listed in set.Tools)
            {
                Assert.Contains(listed.ToolName, names);
            }
        }
    }

    [AvaloniaFact]
    public void NoToolSetListsTheSameToolTwice()
    {
        foreach (IToolSetHandler set in ViewModelMain.Current!.ToolsSubViewModel.AllToolSets)
        {
            string[] names = set.Tools.Select(tool => tool.ToolName).ToArray();

            Assert.Equal(names.Length, names.Distinct().Count());
        }
    }

    /// <summary>
    /// Duplicate keys are legal JSON and parse without complaint, so one silently wins and the other
    /// is lost. Adding a key that already exists has to be caught here.
    /// </summary>
    [AvaloniaFact]
    public void TheEnglishLocalizationHasNoDuplicateKeys()
    {
        using Stream stream = AssetLoader.Open(new Uri("avares://PixiEditor/Data/Localization/Languages/en.json"));
        using JsonDocument document = JsonDocument.Parse(stream);

        List<string> keys = document.RootElement.EnumerateObject().Select(property => property.Name).ToList();
        string[] duplicates = keys.GroupBy(key => key).Where(group => group.Count() > 1).Select(group => group.Key).ToArray();

        Assert.Empty(duplicates);
    }

    private static string[] ResolvedToolNames() =>
        ViewModelMain.Current!.ToolsSubViewModel.AllToolSets
            .SelectMany(set => set.Tools)
            .Select(tool => tool.ToolName)
            .Distinct()
            .ToArray();

    [AvaloniaFact]
    public void GrowingTheSelectionTakesInTheNextPixel()
    {
        var (tracker, _, _) = CreateBlock();
        Assert.False(tracker.Document.Selection.SelectionPath.Contains(12.5f, 8.5f));

        Run(tracker, new ModifySelection_Action(SelectionModification.Grow, 1), new ChangeBoundary_Action());
        Assert.True(tracker.Document.Selection.SelectionPath.Contains(12.5f, 8.5f));

        Run(tracker, new Undo_Action());
        Assert.False(tracker.Document.Selection.SelectionPath.Contains(12.5f, 8.5f));
    }

    [AvaloniaFact]
    public void OffsettingWrapsPixelsAroundTheCanvas()
    {
        var (tracker, layer, _) = CreateBlock();

        Run(tracker, new OffsetCanvasWrapped_Action(new VecI(8, 0), 0), new ChangeBoundary_Action());

        Assert.Equal(255, PixelAt(tracker, layer, 0, 8).A);
        Assert.Equal(0, PixelAt(tracker, layer, 8, 8).A);

        Run(tracker, new Undo_Action());
        Assert.Equal(255, PixelAt(tracker, layer, 8, 8).A);
    }

    [AvaloniaFact]
    public void ReducingSnapsTheLayerToThePalette()
    {
        var (tracker, layer, _) = CreateBlock();

        Run(tracker, new ClearSelection_Action(),
            new ReduceToPalette_Action(layer, new Palette(new[] { new Color(250, 130, 10), new Color(0, 0, 0) }), DitherMode.None, 0),
            new ChangeBoundary_Action());

        Assert.Equal(new Color(250, 130, 10), PixelAt(tracker, layer, 8, 8));
    }

    [AvaloniaFact]
    public void ARampStrokeStepsAlongThePaletteOncePerPixel()
    {
        var (tracker, layer, _) = CreateBlock();
        Palette palette = new(new[] { Orange, Dark });

        Run(tracker,
            new PixelInk_Action(layer, new VecI(8, 8), PixelInkMode.RampForward, 1, Colors.Black, 1, palette, VecI.Zero, false, 0),
            new PixelInk_Action(layer, new VecI(8, 8), PixelInkMode.RampForward, 1, Colors.Black, 1, palette, VecI.Zero, false, 0),
            new EndPixelInk_Action(),
            new ChangeBoundary_Action());

        Assert.Equal(Dark, PixelAt(tracker, layer, 8, 8));
    }

    [AvaloniaFact]
    public void ARampLeavesColoursNotOnThePaletteAlone()
    {
        var (tracker, layer, _) = CreateBlock();

        Ink(tracker, layer, PixelInkMode.RampForward, new Palette(new[] { Dark, new Color(1, 2, 3) }), VecI.Zero, 8, 8);

        Assert.Equal(Orange, PixelAt(tracker, layer, 8, 8));
    }

    [AvaloniaFact]
    public void TheCloneStampCopiesFromTheOffset()
    {
        var (tracker, layer, _) = CreateBlock();
        Run(tracker, new ClearSelection_Action(), new ChangeBoundary_Action());

        Ink(tracker, layer, PixelInkMode.Clone, Palette.empty, new VecI(6, 0), 2, 8);

        Assert.Equal(Orange, PixelAt(tracker, layer, 2, 8));
    }

    [AvaloniaFact]
    public void AdjustmentsConnectToTheLayersFilterSocket()
    {
        var (tracker, layer, _) = CreateBlock();
        Guid filter = Guid.NewGuid();

        Run(tracker,
            new CreateNode_Action(typeof(GrayscaleNode), filter, Guid.Empty),
            new ConnectProperties_Action(layer, filter, StructureNode.FiltersPropertyName, FilterNode.OutputPropertyName),
            new ChangeBoundary_Action());

        var member = tracker.Document.FindMemberOrThrow(layer);
        Assert.Contains(member.GetInputProperty(StructureNode.FiltersPropertyName)!.Connection!.Node.Id, new[] { filter });
    }

    [AvaloniaFact]
    public void EffectsInsertBeforeTheOutput()
    {
        var (tracker, layer, output) = CreateBlock();
        Guid effect = Guid.NewGuid();

        Run(tracker,
            new CreateNode_Action(typeof(DitherNode), effect, Guid.Empty),
            new ConnectProperties_Action(effect, layer, "Background", "Output"),
            new ConnectProperties_Action(output, effect, OutputNode.InputPropertyName, "Output"),
            new ChangeBoundary_Action());

        Assert.Equal(effect, tracker.Document.NodeGraph.OutputNode!.GetInputProperty(OutputNode.InputPropertyName)!.Connection!.Node.Id);
    }

    [AvaloniaFact]
    public void HistoryNamesEachStep()
    {
        var (tracker, _, _) = CreateBlock();

        Run(tracker, new ClearSelection_Action(), new ChangeBoundary_Action());
        (List<string> undo, List<string> redo) = tracker.GetHistoryLabels();

        Assert.Equal("Clear Selection", undo[^1]);
        Assert.Empty(redo);

        Run(tracker, new Undo_Action());
        (undo, redo) = tracker.GetHistoryLabels();

        Assert.Equal("Clear Selection", Assert.Single(redo));
    }
}
