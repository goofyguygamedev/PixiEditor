using Drawie.Backend.Core.ColorsImpl;

namespace PixiEditor.ChangeableDocument.Rendering.ContextData;

public record struct EditorData
{
    public Color PrimaryColor { get; }
    public Color SecondaryColor { get; }

    /// <summary>
    /// How much alpha an erase takes away, from 0 (nothing) to 1 (everything). Null means a full
    /// erase. It is only read when <see cref="PrimaryColor"/> has an alpha of 0, which is what marks
    /// a stroke as an erase, so the alpha itself cannot carry this.
    /// </summary>
    public double? EraseStrength { get; }

    public EditorData(Color primaryColor, Color secondaryColor, double? eraseStrength = null)
    {
        PrimaryColor = primaryColor;
        SecondaryColor = secondaryColor;
        EraseStrength = eraseStrength;
    }
}
