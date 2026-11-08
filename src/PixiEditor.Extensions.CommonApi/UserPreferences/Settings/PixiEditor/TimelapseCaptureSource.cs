using System.ComponentModel;

namespace PixiEditor.Extensions.CommonApi.UserPreferences.Settings.PixiEditor;

/// <summary>
/// What the timelapse recorder puts in a frame.
/// </summary>
public enum TimelapseCaptureSource
{
    /// <summary>
    /// The rendered document on its own, at canvas resolution - no UI, no zoom, no scrolling. The
    /// replay shows the artwork appearing, which is what a timelapse is usually for.
    /// </summary>
    [Description("TIMELAPSE_SOURCE_ARTWORK")]
    ArtworkOnly,

    /// <summary>
    /// The whole editor window: panels, toolbars, dialogs, the canvas at whatever zoom and pan it
    /// happens to be at. Useful for showing how something was made rather than only what was made.
    /// </summary>
    [Description("TIMELAPSE_SOURCE_WHOLE_PROGRAM")]
    WholeProgram
}
