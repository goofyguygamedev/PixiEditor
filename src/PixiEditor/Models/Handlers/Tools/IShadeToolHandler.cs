using Drawie.Backend.Core.ColorsImpl;
using PixiEditor.ChangeableDocument.Enums;
using PixiEditor.ChangeableDocument.Helpers;

namespace PixiEditor.Models.Handlers.Tools;

internal interface IShadeToolHandler : IToolHandler
{
    public float Angle { get; }
    public float Spread { get; }
    public double Thickness { get; }
    public ShadeFalloff Falloff { get; }
    public double Bands { get; }
    public BlendMode ShadeBlendMode { get; }
    public float Strength { get; }
    public float Tolerance { get; }
    public bool UseMainColor { get; }

    /// <summary>The colour picked on the toolbar, for when the main colour is not being used.</summary>
    public Color CustomColor { get; }

    /// <summary>Records an angle set by dragging on the canvas, so the toolbar shows it.</summary>
    public void SetAngle(float angle);
}
