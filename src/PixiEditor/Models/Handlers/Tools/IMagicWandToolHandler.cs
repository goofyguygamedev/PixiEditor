using PixiEditor.ChangeableDocument.Enums;
using PixiEditor.Models.Tools;

namespace PixiEditor.Models.Handlers.Tools;

internal interface IMagicWandToolHandler : IToolHandler
{
    public SelectionMode ResultingSelectionMode { get; }
    public DocumentScope DocumentScope { get; }
    public float Tolerance { get; }
    bool Contiguous { get; }

    /// <summary>
    /// <see cref="Contiguous"/> after modifier keys are applied; holding Alt selects every
    /// matching pixel in the layer rather than only the touching region.
    /// </summary>
    bool ResultingContiguous { get; }
}
