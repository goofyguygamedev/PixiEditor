using Drawie.Backend.Core.Numerics;
using Drawie.Numerics;

namespace PixiEditor.Helpers;

/// <summary>
/// Decides whether corners arriving from the transform box are the artist resizing the selection.
/// </summary>
public static class SelectionTransform
{
    /// <summary>
    /// True when <paramref name="corners"/> describe a selection different from the one that is
    /// already there, and so are worth writing.
    /// </summary>
    /// <remarks>
    /// Writing a no-op is not harmless. It opens an updateable change, and an open change owns the
    /// next undo: instead of stepping back through the artist's own history, the undo reverts that
    /// change and restores the path it captured, which can be from a quite different moment. The
    /// box raises this event when it is merely shown, not only when it is dragged, which is how
    /// that used to happen - an undo after a cut and paste would land on the wrong selection.
    /// </remarks>
    /// <param name="selectionBounds">The selection's current bounds, or null if nothing is selected.</param>
    /// <param name="corners">The corners the transform box is reporting.</param>
    /// <param name="showingBox">Whether these corners came from showing the box rather than a drag.</param>
    public static bool IsRealChange(RectD? selectionBounds, ShapeCorners corners, bool showingBox)
    {
        if (showingBox || selectionBounds is null)
        {
            return false;
        }

        RectD bounds = selectionBounds.Value;

        if (bounds is not { Width: > 0, Height: > 0 } || corners.IsPartiallyDegenerate)
        {
            return false;
        }

        return !new ShapeCorners(bounds).AlmostEquals(corners);
    }
}
