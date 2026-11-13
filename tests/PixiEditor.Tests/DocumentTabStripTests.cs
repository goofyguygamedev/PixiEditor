using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using PixiDocks.Avalonia.Controls;
using PixiDocks.Core.Docking;

namespace PixiEditor.Tests;

/// <summary>
/// Covers the new-tab button living in the document tab strip. The theme it sits in overrides one
/// PixiDocks declares, so the thing most likely to break is not the button but the lookup: if
/// PixiDocks' own copy ever wins again, the button disappears without anything failing to build.
/// </summary>
public class DocumentTabStripTests : FullPixiEditorTest
{
    [AvaloniaFact]
    public void TheDocumentAreasTabStripHasANewTabButton()
    {
        Assert.NotNull(FindNewTabButton(ShowArea("DocumentArea")));
    }

    [AvaloniaFact]
    public void TheButtonOnlyShowsInTheDocumentArea()
    {
        Assert.True(FindNewTabButton(ShowArea("DocumentArea"))!.IsVisible);
        Assert.False(FindNewTabButton(ShowArea("ColorsArea"))!.IsVisible);
    }

    /// <summary>
    /// The button asks the area above it which one it is, so it can only answer with a real area in
    /// the tree. This uses the area's own theme rather than a stand-in, which is also what proves
    /// the overridden tab theme is the one the docking layout actually picks up.
    /// </summary>
    private static DockableArea ShowArea(string areaId)
    {
        DockableArea area = new() { Id = areaId, Dockables = new ObservableCollection<IDockable?>() };
        Window window = new() { Content = area, Width = 600, Height = 400 };

        window.Show();
        window.Measure(new Size(600, 400));
        window.Arrange(new Rect(0, 0, 600, 400));

        return area;
    }

    private static Button? FindNewTabButton(DockableArea area) =>
        area.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => b.Name == "PART_NewTabButton");
}
