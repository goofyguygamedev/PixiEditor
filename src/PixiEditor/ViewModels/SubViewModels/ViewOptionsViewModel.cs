using Avalonia.Input;
using Drawie.Numerics;
using PixiEditor.Extensions.CommonApi.UserPreferences.Settings.PixiEditor;
using PixiEditor.Models.Commands.Attributes.Commands;

namespace PixiEditor.ViewModels.SubViewModels;
#nullable enable
internal class ViewOptionsViewModel : SubViewModel<ViewModelMain>
{
    private bool gridLinesEnabled;

    public bool GridLinesEnabled
    {
        get => gridLinesEnabled;
        set => SetProperty(ref gridLinesEnabled, value);
    }
    
    private bool snappingEnabled = true;
    public bool SnappingEnabled
    {
        get => snappingEnabled;
        set
        {
            SetProperty(ref snappingEnabled, value);
            Owner.DocumentManagerSubViewModel.ActiveDocument.SnappingViewModel.SnappingController.SnappingEnabled = value;
        }
    }
    
    private bool highResRender = true;
    public bool HighResRender
    {
        get => highResRender;
        set
        {
            SetProperty(ref highResRender, value);
        }
    }

    private int maxBilinearSampleSize = 4096;
    public int MaxBilinearSampleSize
    {
        get => maxBilinearSampleSize;
        set
        {
            SetProperty(ref maxBilinearSampleSize, value);
        }
    }

    public ViewOptionsViewModel(ViewModelMain owner)
        : base(owner)
    {
        MaxBilinearSampleSize = PixiEditorSettings.Performance.MaxBilinearSampleSize.Value;

        PixiEditorSettings.Performance.MaxBilinearSampleSize.ValueChanged += (s, e) =>
        {
            MaxBilinearSampleSize = PixiEditorSettings.Performance.MaxBilinearSampleSize.Value;
        };
    }

    /// <summary>
    /// Raised when something wants the grid set to a particular cell size - laying out a sprite
    /// sheet, say. Each open viewport listens, because the size is its own property rather than
    /// something kept here, and all of them should agree.
    /// </summary>
    public event Action<VecD>? GridSizeRequested;

    /// <summary>Turns the grid on and sets its cells, so frame boundaries are visible.</summary>
    public void ShowGridOfSize(VecD cellSize)
    {
        GridLinesEnabled = true;
        GridSizeRequested?.Invoke(cellSize);
    }

    [Command.Basic("PixiEditor.View.ToggleGrid", "TOGGLE_GRIDLINES", "TOGGLE_GRIDLINES", Key = Key.OemTilde,
        Modifiers = KeyModifiers.Control,
        Icon = PixiPerfectIcons.Grid)]
    public void ToggleGridLines()
    {
        GridLinesEnabled = !GridLinesEnabled;
    }

    private bool isometricGridEnabled;

    public bool IsometricGridEnabled
    {
        get => isometricGridEnabled;
        set => SetProperty(ref isometricGridEnabled, value);
    }

    [Command.Basic("PixiEditor.View.ToggleIsometricGrid", "TOGGLE_ISOMETRIC_GRID", "TOGGLE_ISOMETRIC_GRID",
        MenuItemPath = "VIEW/TOGGLE_ISOMETRIC_GRID", MenuItemOrder = 30, Icon = PixiPerfectIcons.Grid)]
    public void ToggleIsometricGrid()
    {
        IsometricGridEnabled = !IsometricGridEnabled;
    }

    [Command.Basic("PixiEditor.View.ZoomIn", 1, "ZOOM_IN", "ZOOM_IN", CanExecute = "PixiEditor.HasDocument",
        Key = Key.OemPlus,
        Icon = PixiPerfectIcons.ZoomIn, AnalyticsTrack = true)]
    [Command.Basic("PixiEditor.View.Zoomout", -1, "ZOOM_OUT", "ZOOM_OUT", CanExecute = "PixiEditor.HasDocument",
        Key = Key.OemMinus,
        Icon = PixiPerfectIcons.ZoomOut, AnalyticsTrack = true)]
    public void ZoomViewport(double zoom)
    {
        ViewportWindowViewModel? viewport = Owner.WindowSubViewModel.ActiveWindow as ViewportWindowViewModel;
        if (viewport is null)
            return;
        viewport.ZoomViewportTrigger.Execute(this, zoom);
    }

    [Command.Basic("PixiEditor.ToggleSnapping", "TOGGLE_SNAPPING", "TOGGLE_SNAPPING",
        Icon = PixiPerfectIcons.Snapping)]
    public void ToggleSnapping()
    {
        SnappingEnabled = !SnappingEnabled;
    }
}
