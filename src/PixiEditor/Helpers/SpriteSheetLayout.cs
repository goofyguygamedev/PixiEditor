using Drawie.Numerics;

namespace PixiEditor.Helpers;

/// <summary>
/// Works out the canvas a sprite sheet of equal frames needs. Separate from the command so the
/// arithmetic can be checked on its own.
/// </summary>
public static class SpriteSheetLayout
{
    /// <summary>
    /// The canvas size for <paramref name="frameCount"/> frames of <paramref name="frameSize"/>,
    /// laid out <paramref name="columns"/> across. One column gives the vertical strip most sheets
    /// use: four 32 wide frames in one column is 32 by 128.
    /// </summary>
    /// <remarks>
    /// The last row is left full width even when it is not full of frames. A sheet is read back by
    /// multiplying out from the frame size, so a ragged edge would only make the maths lie.
    /// </remarks>
    public static VecI CanvasFor(VecI frameSize, int frameCount, int columns)
    {
        frameSize = new VecI(Math.Max(1, frameSize.X), Math.Max(1, frameSize.Y));
        frameCount = Math.Max(1, frameCount);
        columns = Math.Clamp(columns, 1, frameCount);

        int rows = (int)Math.Ceiling(frameCount / (double)columns);

        return new VecI(frameSize.X * columns, frameSize.Y * rows);
    }
}
