using Avalonia.Headless.XUnit;
using Avalonia.Platform;
using System.Text.Json;
using PixiEditor.ViewModels;
using PixiEditor.ViewModels.Dock;
using PixiEditor.Views.Dock;
using PixiEditor.Views.Windows;

namespace PixiEditor.Tests;

/// <summary>
/// Covers the start screen being openable as a tab: that the view locator finds its view, that the
/// screen itself still loads now that it is a control rather than a window, and that its title is a
/// real localization key.
/// </summary>
public class HomeTabTests : FullPixiEditorTest
{
    [AvaloniaFact]
    public void TheViewLocatorFindsTheHomeTabsView()
    {
        ViewLocator locator = new();

        Assert.IsType<HomeDockView>(locator.Build(new HomeDockViewModel()));
    }

    [AvaloniaFact]
    public void TheStartScreenStillLoadsAsAControl()
    {
        // It was a window until it had to be hostable in a tab. Building it here walks the whole
        // XAML, which is the only way to catch a binding that no longer resolves.
        HelloThereView view = new();

        Assert.NotNull(view.Content);
        Assert.Same(view, view.DataContext);
    }

    [AvaloniaFact]
    public void TheStartScreenKnowsHowWideItWantsToBe()
    {
        HelloThereView view = new();

        // What the window host sizes itself from. Zero when the news panel is collapsed or off.
        Assert.True(view.NewsWidth is 0 or HelloThereView.NewsPanelWidth, $"got {view.NewsWidth}");
        Assert.True(view.RecentCount >= 0);
    }

    [AvaloniaFact]
    public void TheWindowStillOpensTheSameScreen()
    {
        HelloTherePopup popup = new();

        Assert.NotNull(popup.Content);
    }

    [AvaloniaFact]
    public void TheHomeTabsTitleIsALocalizationKey()
    {
        using Stream stream = AssetLoader.Open(new Uri("avares://PixiEditor/Data/Localization/Languages/en.json"));
        using JsonDocument document = JsonDocument.Parse(stream);
        HashSet<string> keys = document.RootElement.EnumerateObject().Select(property => property.Name).ToHashSet();

        Assert.Contains("HOME_TAB_TITLE", keys);
        Assert.Contains("OPEN_HOME_TAB", keys);
        Assert.Contains("OPEN_HOME_TAB_DESCRIPTIVE", keys);
    }
}
