using System.Reflection;
using System.Text.Json;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Platform;
using PixiEditor.Models.Commands.Attributes.Commands;
using PixiEditor.Models.Handlers;
using PixiEditor.ViewModels;
using PixiEditor.ViewModels.SubViewModels;
using PixiEditor.ViewModels.Tools;
using PixiEditor.ViewModels.Tools.ToolSettings.Settings;

namespace PixiEditor.Tests;

/// <summary>
/// Guards the help text in the bar at the bottom of the window, and the toolbar settings an artist
/// has to be able to reach. Both break silently: a missing localization key shows as raw text, and a
/// setting can be hidden or pinned to one value by the tool set config with nothing to say so.
/// </summary>
public class ToolHelpTextTests : FullPixiEditorTest
{
    [AvaloniaFact]
    public void EveryToolSaysHowToUseItInTheBottomBar()
    {
        HashSet<string> keys = EnglishKeys();

        string[] broken = Tools()
            .Where(tool => string.IsNullOrWhiteSpace(tool.ActionDisplay.Key) || !keys.Contains(tool.ActionDisplay.Key))
            .Select(tool => $"{tool.ToolName} -> '{tool.ActionDisplay.Key}'")
            .ToArray();

        Assert.Empty(broken);
    }

    [AvaloniaFact]
    public void TheCloneStampTellsTheArtistToPickASourceFirst()
    {
        IToolHandler stamp = Tools().Single(tool => tool.ToolName == "CloneStamp");

        // Painting before a source is picked does nothing at all, so the bar has to say that much.
        Assert.Equal("CLONE_STAMP_TOOL_ACTION_DISPLAY_NO_SOURCE", stamp.ActionDisplay.Key);
        Assert.Contains("CLONE_STAMP_TOOL_ACTION_DISPLAY_READY", EnglishKeys());
    }

    [AvaloniaFact]
    public void TheFillToolExposesToleranceAndFillModeInEveryToolSet()
    {
        List<string> hidden = new();

        foreach ((IToolSetHandler set, IToolHandler fill) in FillInEveryToolSet())
        {
            foreach (string name in new[] { "Tolerance", "FillMode" })
            {
                if (fill.Toolbar.GetSetting(name) is not { IsExposed: true })
                {
                    hidden.Add($"{name} in {set.Name}");
                }
            }
        }

        Assert.Empty(hidden);
    }

    [AvaloniaFact]
    public void TheFillToolsToleranceIsNotPinnedToOneValue()
    {
        foreach ((IToolSetHandler set, IToolHandler fill) in FillInEveryToolSet())
        {
            Setting tolerance = fill.Toolbar.GetSetting("Tolerance");
            tolerance.Value = 0.5f;

            float read = Assert.IsType<float>(tolerance.Value);
            Assert.True(Math.Abs(read - 0.5f) < 0.001f, $"the fill's tolerance reads {read} in {set.Name}, not what it was set to");
        }
    }

    [AvaloniaFact]
    public void NoTwoToolsInAToolSetDeclareTheSameKey()
    {
        var tools = (ToolsViewModel)ViewModelMain.Current!.ToolsSubViewModel;
        List<string> clashes = new();

        foreach (IToolSetHandler set in tools.AllToolSets)
        {
            var byKey = set.Tools
                .Select(tool => (Tool: tool, Key: DeclaredKey(tool)))
                .Where(pair => pair.Key != Key.None)
                .GroupBy(pair => pair.Key);

            foreach (var group in byKey.Where(group => group.Count() > 1))
            {
                clashes.Add($"{group.Key} in {set.Name}: {string.Join(" and ", group.Select(pair => pair.Tool.ToolName))}");
            }
        }

        Assert.True(clashes.Count == 0, $"two tools declare one key: {string.Join("; ", clashes)}");
    }

    [AvaloniaFact]
    public void TheMoveToolIsTheOneOnM()
    {
        IToolHandler move = Tools().Single(tool => tool.ToolName == "Move");
        IToolHandler select = Tools().Single(tool => tool.ToolName == "Select");

        Assert.Equal(Key.M, DeclaredKey(move));
        Assert.Equal(Key.V, DeclaredKey(select));
    }

    /// <summary>
    /// The key a tool ships with, from its own attribute. Not <c>Shortcut</c>, which is whatever the
    /// person running the tests has rebound it to and so says nothing about the code.
    /// </summary>
    private static Key DeclaredKey(IToolHandler tool) =>
        tool.GetType().GetCustomAttribute<Command.ToolAttribute>()?.Key ?? Key.None;

    /// <summary>The fill tool as each tool set that lists it sees it, with that set made active.</summary>
    private static IEnumerable<(IToolSetHandler Set, IToolHandler Fill)> FillInEveryToolSet()
    {
        var tools = (ToolsViewModel)ViewModelMain.Current!.ToolsSubViewModel;

        foreach (IToolSetHandler set in tools.AllToolSets)
        {
            IToolHandler? fill = set.Tools.FirstOrDefault(tool => tool.ToolName == "FloodFill");
            if (fill is null)
            {
                continue;
            }

            tools.SetActiveToolSet(set);
            yield return (set, fill);
        }
    }

    private static IEnumerable<IToolHandler> Tools() =>
        ViewModelMain.Current!.ToolsSubViewModel.AllToolSets.SelectMany(set => set.Tools).Distinct();

    private static HashSet<string> EnglishKeys()
    {
        using Stream stream = AssetLoader.Open(new Uri("avares://PixiEditor/Data/Localization/Languages/en.json"));
        using JsonDocument document = JsonDocument.Parse(stream);
        return document.RootElement.EnumerateObject().Select(property => property.Name).ToHashSet();
    }
}
