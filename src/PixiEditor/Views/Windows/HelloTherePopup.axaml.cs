using PixiEditor.Views.Dialogs;

namespace PixiEditor.Views.Windows;

/// <summary>
/// The start screen in its own window, which is how it appears when PixiEditor opens.
/// </summary>
/// <remarks>
/// Only a host now: <see cref="HelloThereView"/> is the screen itself, so the same thing can also
/// be opened as a tab. What is left here is what only a window can do - deciding how big to be, and
/// closing once a file has been opened.
/// </remarks>
internal partial class HelloTherePopup : PixiEditorPopup
{
    public HelloTherePopup()
    {
        InitializeComponent();

        view.CloseRequested += Close;
        view.NewsPanelWidthChanged += width => Width += width;
        Activated += (_, _) => view.RefreshClipboard();

        ApplyStartupSize();
    }

    /// <summary>
    /// Sizes the window around how much there is to show, so a first run with no recent files does
    /// not open a window two thirds empty.
    /// </summary>
    private void ApplyStartupSize()
    {
        int newsWidth = view.NewsWidth;
        int recent = view.RecentCount;

        if (recent == 0)
        {
            Width = 550 + newsWidth;
            Height = 680;
        }
        else if (recent < 4)
        {
            Width = 545 + newsWidth;
            Height = 680;
        }
        else if (recent < 7)
        {
            Width = 575 + newsWidth;
            Height = 850;
        }
    }
}
