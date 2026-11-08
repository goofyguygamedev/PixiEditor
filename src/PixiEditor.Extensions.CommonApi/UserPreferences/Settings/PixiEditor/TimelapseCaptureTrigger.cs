using System.ComponentModel;

namespace PixiEditor.Extensions.CommonApi.UserPreferences.Settings.PixiEditor;

/// <summary>
/// What makes the timelapse recorder take a frame.
/// </summary>
public enum TimelapseCaptureTrigger
{
    /// <summary>
    /// One frame per completed action, the way an undo step is one action. A stroke of any length
    /// becomes a single frame, so the replay jumps from finished thing to finished thing.
    /// </summary>
    [Description("TIMELAPSE_TRIGGER_ON_ACTION")]
    OnActionComplete,

    /// <summary>
    /// A frame every <see cref="PreferencesConstants.TimelapseDrawingIntervalSeconds"/> for as long
    /// as the mouse is down on a stroke, so long strokes are drawn out in the replay rather than
    /// appearing all at once. Completed actions are still captured, so nothing is lost relative to
    /// <see cref="OnActionComplete"/> - this only adds the frames in between.
    /// </summary>
    [Description("TIMELAPSE_TRIGGER_WHILE_DRAWING")]
    WhileDrawing
}
