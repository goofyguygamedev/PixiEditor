using System.ComponentModel;

namespace PixiEditor.Extensions.CommonApi.UserPreferences.Settings.PixiEditor;

/// <summary>
/// What goes behind the artwork in an exported replay.
/// </summary>
/// <remarks>
/// A replay has to have one. Video has no alpha channel - the mp4 pixel format is yuv420p - so
/// transparent pixels become black wherever they are not painted over first. Black artwork on a
/// transparent canvas then encodes to a video that is black from corner to corner, which is exactly
/// the bug this setting exists to prevent. White is the default because it suits most artwork and
/// compresses cleanly; anyone drawing in white can pick something else rather than hitting the same
/// problem from the other side.
/// </remarks>
public enum TimelapseBackground
{
    [Description("TIMELAPSE_BACKGROUND_WHITE")]
    White,

    [Description("TIMELAPSE_BACKGROUND_BLACK")]
    Black,

    /// <summary>The editor's own transparency checkerboard, so the replay matches what was on screen.</summary>
    [Description("TIMELAPSE_BACKGROUND_CHECKERBOARD")]
    Checkerboard
}
