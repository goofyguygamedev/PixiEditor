using PixiEditor.ChangeableDocument.Changeables.Graph.Nodes.Effects;
using PixiEditor.ViewModels.Nodes;

namespace PixiEditor.ViewModels.Document.Nodes.Effects;

[NodeViewModel("DITHER_NODE", "EFFECTS", PixiPerfectIcons.Grid)]
internal class DitherNodeViewModel : NodeViewModel<DitherNode>;

[NodeViewModel("LEVELS_NODE", "EFFECTS", PixiPerfectIcons.Sun)]
internal class LevelsNodeViewModel : NodeViewModel<LevelsNode>;

[NodeViewModel("CURVES_NODE", "EFFECTS", PixiPerfectIcons.Sun)]
internal class CurvesNodeViewModel : NodeViewModel<CurvesNode>;

[NodeViewModel("PIXELATE_NODE", "EFFECTS", PixiPerfectIcons.LowresCircle)]
internal class PixelateNodeViewModel : NodeViewModel<PixelateNode>;

[NodeViewModel("MOTION_BLUR_NODE", "EFFECTS", PixiPerfectIcons.Blur)]
internal class MotionBlurNodeViewModel : NodeViewModel<MotionBlurNode>;

[NodeViewModel("ZOOM_BLUR_NODE", "EFFECTS", PixiPerfectIcons.Blur)]
internal class ZoomBlurNodeViewModel : NodeViewModel<ZoomBlurNode>;

[NodeViewModel("SPIN_BLUR_NODE", "EFFECTS", PixiPerfectIcons.Blur)]
internal class SpinBlurNodeViewModel : NodeViewModel<SpinBlurNode>;

[NodeViewModel("PIXEL_GLOW_NODE", "EFFECTS", PixiPerfectIcons.Sun)]
internal class PixelGlowNodeViewModel : NodeViewModel<PixelGlowNode>;

[NodeViewModel("VIGNETTE_NODE", "EFFECTS", PixiPerfectIcons.Circle)]
internal class VignetteNodeViewModel : NodeViewModel<VignetteNode>;

[NodeViewModel("CHROMATIC_ABERRATION_NODE", "EFFECTS", PixiPerfectIcons.Droplet)]
internal class ChromaticAberrationNodeViewModel : NodeViewModel<ChromaticAberrationNode>;

[NodeViewModel("REDUCE_NOISE_NODE", "EFFECTS", PixiPerfectIcons.Blur)]
internal class ReduceNoiseNodeViewModel : NodeViewModel<ReduceNoiseNode>;
