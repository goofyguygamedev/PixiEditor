using PixiEditor.UI.Common.Localization;
using PixiEditor.ViewModels.Document;

namespace PixiEditor.ViewModels.Dock;

internal class HistoryDockViewModel : DockableViewModel
{
    public override string Id => "History";
    public override string Title => new LocalizedString("HISTORY_TITLE");
    public override bool CanFloat => true;
    public override bool CanClose => true;

    public DocumentManagerViewModel DocumentManagerSubViewModel { get; }

    public HistoryDockViewModel(DocumentManagerViewModel documentManagerViewModel)
    {
        DocumentManagerSubViewModel = documentManagerViewModel;
        TabCustomizationSettings.Icon = UI.Common.Fonts.PixiPerfectIconExtensions.ToIcon(PixiPerfectIcons.Undo);
    }
}
