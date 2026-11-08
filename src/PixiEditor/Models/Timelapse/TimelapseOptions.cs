using PixiEditor.Extensions.CommonApi.UserPreferences.Settings.PixiEditor;

namespace PixiEditor.Models.Timelapse;

/// <summary>
/// Everything about a recording that the user can change, passed to the recorder as one value so a
/// settings change can be applied live without the recorder growing a setter per option.
/// </summary>
/// <param name="Trigger">What makes a frame get taken.</param>
/// <param name="Source">What goes in the frame.</param>
/// <param name="Background">What goes behind the artwork when the replay is encoded.</param>
/// <param name="SweepIntervalSeconds">
/// How often <see cref="TimelapseCaptureTrigger.OnActionComplete"/> checks for a change the
/// renderer was too busy to capture when the action finished. A safety net, not the capture rate.
/// </param>
/// <param name="DrawingIntervalSeconds">
/// How often <see cref="TimelapseCaptureTrigger.WhileDrawing"/> takes a frame for as long as a
/// stroke is in progress.
/// </param>
internal readonly record struct TimelapseOptions(
    TimelapseCaptureTrigger Trigger,
    TimelapseCaptureSource Source,
    TimelapseBackground Background,
    double SweepIntervalSeconds,
    double DrawingIntervalSeconds)
{
    /// <summary>Lower bound on the timer, so a mistyped interval cannot spin the capture loop.</summary>
    public const double MinDrawingInterval = 0.05;

    public const double MaxDrawingInterval = 10;

    public const double MinSweepInterval = 1;

    public const double MaxSweepInterval = 30;

    /// <summary>
    /// The timer period this configuration needs: the drawing interval when frames are taken
    /// during a stroke, otherwise the slower safety-net sweep.
    /// </summary>
    public double TimerIntervalSeconds =>
        Trigger == TimelapseCaptureTrigger.WhileDrawing
            ? Math.Clamp(DrawingIntervalSeconds, MinDrawingInterval, MaxDrawingInterval)
            : Math.Clamp(SweepIntervalSeconds, MinSweepInterval, MaxSweepInterval);
}
