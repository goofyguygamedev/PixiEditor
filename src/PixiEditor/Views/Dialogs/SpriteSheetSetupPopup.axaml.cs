using Avalonia;
using Avalonia.Interactivity;
using Drawie.Numerics;
using PixiEditor.Helpers;

namespace PixiEditor.Views.Dialogs;

/// <summary>
/// Asks for the shape of a sprite sheet: how big a frame is, how many there are and how many sit
/// side by side. One column is the vertical strip most sheets use.
/// </summary>
internal partial class SpriteSheetSetupPopup : PixiEditorPopup
{
    public static readonly StyledProperty<double> FrameWidthProperty =
        AvaloniaProperty.Register<SpriteSheetSetupPopup, double>(nameof(FrameWidth), 32);

    public static readonly StyledProperty<double> FrameHeightProperty =
        AvaloniaProperty.Register<SpriteSheetSetupPopup, double>(nameof(FrameHeight), 32);

    public static readonly StyledProperty<double> FrameCountProperty =
        AvaloniaProperty.Register<SpriteSheetSetupPopup, double>(nameof(FrameCount), 4);

    public static readonly StyledProperty<double> ColumnsProperty =
        AvaloniaProperty.Register<SpriteSheetSetupPopup, double>(nameof(Columns), 1);

    public static readonly StyledProperty<string> ResultDescriptionProperty =
        AvaloniaProperty.Register<SpriteSheetSetupPopup, string>(nameof(ResultDescription), string.Empty);

    public SpriteSheetSetupPopup()
    {
        InitializeComponent();
        UpdateDescription();
    }

    public SpriteSheetSetupPopup(VecI startingFrameSize)
        : this()
    {
        FrameWidth = startingFrameSize.X;
        FrameHeight = startingFrameSize.Y;
    }

    public double FrameWidth
    {
        get => GetValue(FrameWidthProperty);
        set => SetValue(FrameWidthProperty, value);
    }

    public double FrameHeight
    {
        get => GetValue(FrameHeightProperty);
        set => SetValue(FrameHeightProperty, value);
    }

    public double FrameCount
    {
        get => GetValue(FrameCountProperty);
        set => SetValue(FrameCountProperty, value);
    }

    public double Columns
    {
        get => GetValue(ColumnsProperty);
        set => SetValue(ColumnsProperty, value);
    }

    /// <summary>The canvas these numbers add up to, spelled out so it is clear before applying.</summary>
    public string ResultDescription
    {
        get => GetValue(ResultDescriptionProperty);
        private set => SetValue(ResultDescriptionProperty, value);
    }

    public VecI FrameSize => new((int)FrameWidth, (int)FrameHeight);

    public VecI CanvasSize => SpriteSheetLayout.CanvasFor(FrameSize, (int)FrameCount, (int)Columns);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == FrameWidthProperty || change.Property == FrameHeightProperty
            || change.Property == FrameCountProperty || change.Property == ColumnsProperty)
        {
            UpdateDescription();
        }
    }

    private void UpdateDescription()
    {
        VecI canvas = CanvasSize;
        ResultDescription = $"{(int)FrameCount} x {FrameSize.X}x{FrameSize.Y}  ->  {canvas.X} x {canvas.Y}";
    }

    private void OkButton_Click(object? sender, RoutedEventArgs e) => SetResultAndCloseCommand();

    private void CancelButton_Click(object? sender, RoutedEventArgs e) => Close(false);
}
