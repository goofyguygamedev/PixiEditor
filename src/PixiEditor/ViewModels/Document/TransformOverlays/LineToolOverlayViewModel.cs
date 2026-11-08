using System.Linq;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PixiEditor.Models.Handlers;
using Drawie.Numerics;

namespace PixiEditor.ViewModels.Document.TransformOverlays;

internal class LineToolOverlayViewModel : ObservableObject, ILineOverlayHandler
{
    public event EventHandler<(VecD, VecD)>? LineMoved;

    private VecD lineStart;

    public VecD LineStart
    {
        get => lineStart;
        set
        {
            if (SetProperty(ref lineStart, value) && isInitialized)
                LineMoved?.Invoke(this, (lineStart, lineEnd));
        }
    }

    private VecD lineEnd;

    public VecD LineEnd
    {
        get => lineEnd;
        set
        {
            if (SetProperty(ref lineEnd, value) && isInitialized)
                LineMoved?.Invoke(this, (lineStart, lineEnd));
        }
    }

    private IReadOnlyList<VecD> controlPoints = [];

    /// <summary>
    /// Bound two-way to the overlay, so dragging a curve handle lands here. Assigned as a whole new
    /// list rather than mutated, so the change actually notifies.
    /// </summary>
    public IReadOnlyList<VecD> ControlPoints
    {
        get => controlPoints;
        set
        {
            if (SetProperty(ref controlPoints, value ?? []) && isInitialized)
                LineMoved?.Invoke(this, (lineStart, lineEnd));
        }
    }

    private double smoothness = 1.0;

    public double Smoothness
    {
        get => smoothness;
        set
        {
            if (SetProperty(ref smoothness, value) && isInitialized)
                LineMoved?.Invoke(this, (lineStart, lineEnd));
        }
    }

    private bool isometricLock;

    public bool IsometricLock
    {
        get => isometricLock;
        set => SetProperty(ref isometricLock, value);
    }

    public void SetControlPoints(IReadOnlyList<VecD> points) => ControlPoints = points.ToList();

    private bool showHandles;
    public bool ShowHandles
    {
        get => showHandles;
        set => SetProperty(ref showHandles, value);
    }

    private bool isSizeBoxEnabled;
    public bool IsSizeBoxEnabled
    {
        get => isSizeBoxEnabled;
        set => SetProperty(ref isSizeBoxEnabled, value);
    }

    private bool isEnabled;

    public bool IsEnabled
    {
        get => isEnabled;
        set => SetProperty(ref isEnabled, value);
    }

    private ICommand? actionCompletedCommand = null;

    public ICommand? ActionCompletedCommand
    {
        get => actionCompletedCommand;
        set => SetProperty(ref actionCompletedCommand, value);
    }

    private ICommand addToUndoCommand = null;
    public ICommand AddToUndoCommand
    {
        get => addToUndoCommand;
        set => SetProperty(ref addToUndoCommand, value);
    }
    
    private bool showApplyButton;

    public bool ShowApplyButton
    {
        get => showApplyButton;
        set => SetProperty(ref showApplyButton, value);
    }

    private bool isInitialized;
    
    public LineToolOverlayViewModel()
    {
    }

    public void Show(VecD lineStart, VecD endPos, bool showApplyButton, Action<(VecD, VecD)> addToUndo,
        IReadOnlyList<VecD>? controlPoints = null)
    {
        isInitialized = false;
        LineStart = lineStart;
        LineEnd = endPos;
        ControlPoints = controlPoints?.ToList() ?? [];
        IsEnabled = true;
        ShowApplyButton = showApplyButton;
        ShowHandles = true;
        IsSizeBoxEnabled = false;
        AddToUndoCommand = new RelayCommand(() => addToUndo((LineStart, LineEnd)));
        
        isInitialized = true;
    }

    public void Hide()
    {
        IsEnabled = false;
        ShowApplyButton = false;
        IsSizeBoxEnabled = false;
        isInitialized = false;
    }

    public bool Nudge(VecD distance)
    {
        // Control points move with the line, otherwise nudging would reshape the curve.
        if (controlPoints.Count > 0)
            ControlPoints = controlPoints.Select(point => point + distance).ToList();

        LineStart = LineStart + distance;
        LineEnd = LineEnd + distance;
        return true;
    }
}
