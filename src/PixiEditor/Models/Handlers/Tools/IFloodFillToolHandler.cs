using PixiEditor.ChangeableDocument.Enums;

namespace PixiEditor.Models.Handlers.Tools;

internal interface IFloodFillToolHandler : IToolHandler
{
    public bool ConsiderAllLayers { get; }
    public float Tolerance { get; }
    FloodFillMode FillMode { get; }
    public bool Contiguous { get; }

    /// <summary>What <see cref="Contiguous"/> comes to once Alt has had its say.</summary>
    public bool ResultingContiguous { get; }
}
