using Drawie.Backend.Core.ColorsImpl;
using PixiEditor.ChangeableDocument.Enums;
using PixiEditor.ChangeableDocument.Helpers;

namespace PixiEditor.Models.Handlers.Tools;

internal interface IOutlineToolHandler : IToolHandler
{
    public double Thickness { get; }
    public OutlinePosition Position { get; }
    public OutlineCorners Corners { get; }
    public float Softness { get; }
    public ShadeFalloff Falloff { get; }
    public double Bands { get; }
    public OrderedDither Pattern { get; }
    public OutlineTarget Target { get; }
    public bool UseMainColor { get; }
    public Color CustomColor { get; }
    public BlendMode OutlineBlendMode { get; }
    public float Strength { get; }
    public float Tolerance { get; }
}
