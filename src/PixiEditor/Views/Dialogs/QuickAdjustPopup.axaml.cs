using Avalonia;
using Avalonia.Interactivity;
using PixiEditor.ChangeableDocument.Helpers;
using PixiEditor.Models.Handlers;
using PixiEditor.ViewModels.Document;

namespace PixiEditor.Views.Dialogs;

/// <summary>
/// The layer panel's quick colour correction: four sliders that re-tune one live change, so the
/// canvas shows the result as they move and only one undo step is left behind.
/// </summary>
internal partial class QuickAdjustPopup : PixiEditorPopup
{
    public static readonly StyledProperty<double> HueProperty =
        AvaloniaProperty.Register<QuickAdjustPopup, double>(nameof(Hue));

    public static readonly StyledProperty<double> SaturationProperty =
        AvaloniaProperty.Register<QuickAdjustPopup, double>(nameof(Saturation));

    public static readonly StyledProperty<double> LightnessProperty =
        AvaloniaProperty.Register<QuickAdjustPopup, double>(nameof(Lightness));

    public static readonly StyledProperty<double> ContrastProperty =
        AvaloniaProperty.Register<QuickAdjustPopup, double>(nameof(Contrast));

    public static readonly StyledProperty<string> ScopeKeyProperty =
        AvaloniaProperty.Register<QuickAdjustPopup, string>(nameof(ScopeKey), "QUICK_ADJUST_WHOLE_LAYER");

    private readonly DocumentViewModel? document;
    private readonly IStructureMemberHandler? member;

    /// <summary>Whether anything was sent to the document, so cancelling knows if there is a change to throw away.</summary>
    private bool previewed;

    private bool kept;

    public QuickAdjustPopup()
    {
        InitializeComponent();
    }

    public QuickAdjustPopup(DocumentViewModel document, IStructureMemberHandler member)
        : this()
    {
        this.document = document;
        this.member = member;

        ScopeKey = document.SelectionPathBindable.IsEmpty
            ? "QUICK_ADJUST_WHOLE_LAYER"
            : "QUICK_ADJUST_SELECTION";
    }

    /// <summary>Degrees to turn the hue by.</summary>
    public double Hue
    {
        get => GetValue(HueProperty);
        set => SetValue(HueProperty, value);
    }

    /// <summary>Percent, -100 to 100.</summary>
    public double Saturation
    {
        get => GetValue(SaturationProperty);
        set => SetValue(SaturationProperty, value);
    }

    /// <summary>Percent, -100 to 100.</summary>
    public double Lightness
    {
        get => GetValue(LightnessProperty);
        set => SetValue(LightnessProperty, value);
    }

    /// <summary>Percent, -100 to 100.</summary>
    public double Contrast
    {
        get => GetValue(ContrastProperty);
        set => SetValue(ContrastProperty, value);
    }

    /// <summary>Localization key for the line saying what the sliders will affect.</summary>
    public string ScopeKey
    {
        get => GetValue(ScopeKeyProperty);
        set => SetValue(ScopeKeyProperty, value);
    }

    private ColorAdjustments Adjustments =>
        new(Hue, Saturation / 100.0, Lightness / 100.0, Contrast / 100.0);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == HueProperty || change.Property == SaturationProperty ||
            change.Property == LightnessProperty || change.Property == ContrastProperty)
        {
            Preview();
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        // Covers the window being closed by its own title bar rather than by a button.
        Discard();
        base.OnClosed(e);
    }

    private void Preview()
    {
        if (document is null || member is null || kept)
        {
            return;
        }

        // A neutral setting produces no patch at all, so there would be nothing for a later cancel
        // to undo. Only a correction that changed something counts as a live change.
        if (!PixelAdjust.IsNeutral(Adjustments))
        {
            previewed = true;
        }

        document.Operations.AdjustColors(member, Adjustments);
    }

    private void ResetButton_Click(object? sender, RoutedEventArgs e)
    {
        Hue = 0;
        Saturation = 0;
        Lightness = 0;
        Contrast = 0;
    }

    private void OkButton_Click(object? sender, RoutedEventArgs e)
    {
        if (document is not null && previewed)
        {
            document.Operations.EndAdjustColors(true);
        }

        kept = true;
        SetResultAndCloseCommand();
    }

    private void CancelButton_Click(object? sender, RoutedEventArgs e)
    {
        Discard();
        ClosePopup();
    }

    private void Discard()
    {
        if (document is null || kept || !previewed)
        {
            return;
        }

        // Marked first: closing after the button has already discarded must not undo a second time.
        kept = true;
        document.Operations.EndAdjustColors(false);
    }
}
