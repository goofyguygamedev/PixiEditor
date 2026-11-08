using Drawie.Numerics;

namespace PixiEditor.Models.Handlers;

internal interface ILineOverlayHandler
{
    public void Hide();
    public bool Nudge(VecD distance);
    public void Show(VecD startPos, VecD endPos, bool showApplyButton, Action<(VecD, VecD)> addToUndo,
        IReadOnlyList<VecD>? controlPoints = null);
    public VecD LineStart { get; set; }
    public VecD LineEnd { get; set; }
    public bool ShowHandles { get; set; }
    public bool IsSizeBoxEnabled { get; set; }

    /// <summary>
    /// The draggable points between <see cref="LineStart"/> and <see cref="LineEnd"/> that bend the
    /// line. Empty means a plain straight line.
    /// </summary>
    public IReadOnlyList<VecD> ControlPoints { get; }

    /// <summary>
    /// How rounded the curve is at each point: 0 gives straight segments meeting at sharp corners,
    /// 1 a fully smooth curve.
    /// </summary>
    public double Smoothness { get; set; }

    /// <summary>
    /// Whether dragging either end of the line is constrained to the pixel-art isometric angles.
    /// Mirrors the line tool's toolbar setting, which the overlay has no way to read for itself.
    /// </summary>
    public bool IsometricLock { get; set; }

    /// <summary>
    /// Replaces the control points. Pass an empty list to go back to a straight line.
    /// </summary>
    public void SetControlPoints(IReadOnlyList<VecD> points);
}
