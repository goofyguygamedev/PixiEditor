using Drawie.Backend.Core.ColorsImpl;
using PixiEditor.ChangeableDocument.Enums;
using PixiEditor.ChangeableDocument.Helpers;

namespace PixiEditor.Models.Handlers.Tools;

internal interface IGradientFillToolHandler : IToolHandler
{
    public GradientShape Shape { get; }
    public ShadeFalloff Falloff { get; }
    public double Steps { get; }
    public OrderedDither Pattern { get; }
    public bool Reverse { get; }
    public bool FadeToTransparent { get; }
    public bool UseMainColors { get; }
    public Color CustomFrom { get; }
    public Color CustomTo { get; }
    public BlendMode GradientBlendMode { get; }
    public float Strength { get; }
    public GradientArea Area { get; }
    public float Tolerance { get; }
}
