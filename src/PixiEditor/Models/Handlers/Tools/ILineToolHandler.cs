namespace PixiEditor.Models.Handlers.Tools;

internal interface ILineToolHandler : IToolHandler
{
    public double ToolSize { get; }
    public bool Snap { get; }
    public bool DrawFromCenter { get; }

    /// <summary>
    /// How many draggable points to put between the two ends. 0 is a plain straight line; 2 gives
    /// the pair of handles Paint.NET uses to bend a line into a curve.
    /// </summary>
    public int CurvePointCount { get; }

    /// <summary>
    /// How rounded the curve runs through each point: 1 is fully smooth, 0 gives straight segments
    /// meeting at sharp corners. Derived from the toolbar's sharpness setting.
    /// </summary>
    public double CurveSmoothness { get; }

    /// <summary>
    /// Constrains the line to the pixel-art isometric angles, at a whole number of steps along
    /// whichever one is nearest. See <see cref="Helpers.GeometryHelper.GetIsometricPosition"/>.
    /// </summary>
    public bool IsometricLock { get; }
}
