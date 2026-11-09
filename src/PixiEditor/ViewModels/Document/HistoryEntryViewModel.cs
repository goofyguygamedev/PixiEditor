using System.Windows.Input;
using Avalonia.Media;
using CommunityToolkit.Mvvm.Input;

namespace PixiEditor.ViewModels.Document;

/// <summary>One step in a document's undo history, as listed in the History panel.</summary>
internal class HistoryEntryViewModel
{
    public string Label { get; }

    public FontWeight Weight { get; }

    /// <summary>Steps that have been undone are dimmed: they are still there, but not applied.</summary>
    public double Dim { get; }

    public ICommand JumpCommand { get; }

    public HistoryEntryViewModel(string label, bool isCurrent, bool isUndone, Action jump)
    {
        Label = label;
        Weight = isCurrent ? FontWeight.Bold : FontWeight.Normal;
        Dim = isUndone ? 0.45 : 1;
        JumpCommand = new RelayCommand(jump);
    }
}
