using System.Text.Json;
using Avalonia.Headless.XUnit;
using Avalonia.Platform;

namespace PixiEditor.Tests;

/// <summary>
/// Covers the strings behind alt-clicking a color into the palette. A missing key shows the raw
/// key name in the hint bar, which is exactly where someone looks to learn the gesture exists.
/// </summary>
public class ColorPickerPaletteTests : FullPixiEditorTest
{
    [AvaloniaFact]
    public void TheAltClickHintHasAString()
    {
        Assert.Contains("COLOR_PICKER_ACTION_DISPLAY_ADD_TO_PALETTE", EnglishKeys());
    }

    [AvaloniaFact]
    public void EveryStateTheColorPickerCanShowHasAString()
    {
        HashSet<string> keys = EnglishKeys();

        string[] missing = new[]
            {
                "COLOR_PICKER_TOOLTIP",
                "COLOR_PICKER_ACTION_DISPLAY_DEFAULT",
                "COLOR_PICKER_ACTION_DISPLAY_CANVAS_ONLY",
                "COLOR_PICKER_ACTION_DISPLAY_REFERENCE_ONLY",
                "COLOR_PICKER_ACTION_DISPLAY_ADD_TO_PALETTE"
            }
            .Where(key => !keys.Contains(key))
            .ToArray();

        Assert.Empty(missing);
    }

    [AvaloniaFact]
    public void TheTooltipStillTakesItsShortcutPlaceholder()
    {
        // The tool tip is formatted with the shortcut, so losing the placeholder would silently
        // stop telling the artist which key selects the tool.
        Assert.Contains("{0}", EnglishValue("COLOR_PICKER_TOOLTIP"));
    }

    private static HashSet<string> EnglishKeys()
    {
        using Stream stream = AssetLoader.Open(new Uri("avares://PixiEditor/Data/Localization/Languages/en.json"));
        using JsonDocument document = JsonDocument.Parse(stream);
        return document.RootElement.EnumerateObject().Select(property => property.Name).ToHashSet();
    }

    private static string EnglishValue(string key)
    {
        using Stream stream = AssetLoader.Open(new Uri("avares://PixiEditor/Data/Localization/Languages/en.json"));
        using JsonDocument document = JsonDocument.Parse(stream);
        return document.RootElement.GetProperty(key).GetString()!;
    }
}
