using PixiEditor.UI.Common.Localization;

namespace PixiEditor.ViewModels.Dock;

/// <summary>
/// The start screen as a tab beside the open images, which is what the + button opens.
/// </summary>
/// <remarks>
/// It holds nothing itself. The screen is <see cref="Views.Windows.HelloThereView"/>, found by the
/// view locator from this type's name, and the recent file list it shows is the editor's own.
/// </remarks>
internal class HomeDockViewModel : DockableViewModel
{
    public const string TabId = "HomeDock";

    public override string Id => TabId;

    public override string Title => new LocalizedString("HOME_TAB_TITLE");

    public override bool CanFloat => true;

    public override bool CanClose => true;
}
